using System;

namespace DirtAndDiamonds.Simulation.Hustles;

/// <summary>
/// Best-5-of-7 hand evaluation (docs/design/hustles_texas_holdem.md §4.2): every
/// hand collapses to one comparable <c>int</c> <c>HandScore</c> — higher wins,
/// equal is a tie (split pot):
/// <code>
/// HandScore = (Category &lt;&lt; 20) | (t1 &lt;&lt; 16) | (t2 &lt;&lt; 12) | (t3 &lt;&lt; 8) | (t4 &lt;&lt; 4) | t5
/// Category (high→low): 8 StraightFlush · 7 Quads · 6 FullHouse · 5 Flush
///                       4 Straight · 3 Trips · 2 TwoPair · 1 Pair · 0 HighCard
/// </code>
/// Ranks ≤14 fit in 4 bits, so the whole score fits in 24 bits and comparison
/// is a single <c>int</c> compare. The wheel (A-2-3-4-5) is the one special
/// case — the straight's top card is 5, not 14.
/// </summary>
public static class HoldemEvaluator
{
    public const int CategoryStraightFlush = 8;
    public const int CategoryQuads = 7;
    public const int CategoryFullHouse = 6;
    public const int CategoryFlush = 5;
    public const int CategoryStraight = 4;
    public const int CategoryTrips = 3;
    public const int CategoryTwoPair = 2;
    public const int CategoryPair = 1;
    public const int CategoryHighCard = 0;

    /// <summary>
    /// Production path — a histogram evaluator over rank/suit counts, no
    /// C(7,5) combination loop. <paramref name="sevenCards"/> must have
    /// Length == 7. Zero-alloc (stackalloc scratch only).
    /// </summary>
    public static int EvaluateBest7(ReadOnlySpan<Card> sevenCards)
    {
        if (sevenCards.Length != 7)
        {
            throw new ArgumentException("EvaluateBest7 requires exactly 7 cards.", nameof(sevenCards));
        }

        Span<int> rankCounts = stackalloc int[15]; // index by rank 2..14
        Span<int> suitCounts = stackalloc int[4];
        Span<int> suitRankMask = stackalloc int[4]; // bit r set => that suit holds rank r
        int rankMaskAll = 0;

        foreach (Card c in sevenCards)
        {
            int r = c.Rank, s = c.Suit;
            rankCounts[r]++;
            suitCounts[s]++;
            suitRankMask[s] |= 1 << r;
            rankMaskAll |= 1 << r;
        }

        // Flush / straight-flush. At most one suit can reach 5 of 7 cards, and a
        // flush suit mathematically precludes quads or a full house in a 7-card
        // hand (the quad/trip+pair cards alone can never stack 5 into one suit),
        // so it is safe — and correct, since Flush(5) < Quads(7)/FullHouse(6) can
        // never actually collide — to check this before those categories.
        for (int s = 0; s < 4; s++)
        {
            if (suitCounts[s] < 5)
            {
                continue;
            }

            int sfTop = TopStraightRank(suitRankMask[s]);
            if (sfTop > 0)
            {
                return Encode(CategoryStraightFlush, sfTop, 0, 0, 0, 0);
            }

            Span<int> flushRanks = stackalloc int[5];
            TopRanks(suitRankMask[s], 0, flushRanks);
            return Encode(CategoryFlush, flushRanks[0], flushRanks[1], flushRanks[2], flushRanks[3], flushRanks[4]);
        }

        int quadRank = HighestRankWithCount(rankCounts, 4, 4);
        if (quadRank > 0)
        {
            Span<int> kicker = stackalloc int[1];
            TopRanks(rankMaskAll, 1 << quadRank, kicker);
            return Encode(CategoryQuads, quadRank, kicker[0], 0, 0, 0);
        }

        int tripRank = HighestRankWithCount(rankCounts, 3, 4);
        if (tripRank > 0)
        {
            int secondTrip = HighestRankWithCount(rankCounts, 3, 4, exclude: 1 << tripRank);
            int pairRank = secondTrip > 0 ? secondTrip : HighestRankWithCount(rankCounts, 2, 4, exclude: 1 << tripRank);
            if (pairRank > 0)
            {
                return Encode(CategoryFullHouse, tripRank, pairRank, 0, 0, 0);
            }
        }

        int straightTop = TopStraightRank(rankMaskAll);
        if (straightTop > 0)
        {
            return Encode(CategoryStraight, straightTop, 0, 0, 0, 0);
        }

        if (tripRank > 0)
        {
            Span<int> kickers = stackalloc int[2];
            TopRanks(rankMaskAll, 1 << tripRank, kickers);
            return Encode(CategoryTrips, tripRank, kickers[0], kickers[1], 0, 0);
        }

        Span<int> pairRanks = stackalloc int[3];
        int pairCount = CollectRanksWithCount(rankCounts, 2, pairRanks);

        if (pairCount >= 2)
        {
            int excludeMask = (1 << pairRanks[0]) | (1 << pairRanks[1]);
            Span<int> kicker = stackalloc int[1];
            TopRanks(rankMaskAll, excludeMask, kicker);
            return Encode(CategoryTwoPair, pairRanks[0], pairRanks[1], kicker[0], 0, 0);
        }

        if (pairCount == 1)
        {
            Span<int> kickers = stackalloc int[3];
            TopRanks(rankMaskAll, 1 << pairRanks[0], kickers);
            return Encode(CategoryPair, pairRanks[0], kickers[0], kickers[1], kickers[2], 0);
        }

        Span<int> highCards = stackalloc int[5];
        TopRanks(rankMaskAll, 0, highCards);
        return Encode(CategoryHighCard, highCards[0], highCards[1], highCards[2], highCards[3], highCards[4]);
    }

    private static int Encode(int category, int t1, int t2, int t3, int t4, int t5) =>
        (category << 20) | (t1 << 16) | (t2 << 12) | (t3 << 8) | (t4 << 4) | t5;

    /// <summary>
    /// Highest rank (14→2) whose count in <paramref name="rankCounts"/> is
    /// ≥ <paramref name="minCount"/> (and, since a rank can never legally exceed
    /// 4 copies, effectively == when <paramref name="minCount"/>==4), skipping
    /// any rank set in <paramref name="exclude"/>. Returns 0 if none.
    /// </summary>
    private static int HighestRankWithCount(ReadOnlySpan<int> rankCounts, int minCount, int maxCount, int exclude = 0)
    {
        for (int r = 14; r >= 2; r--)
        {
            if ((exclude & (1 << r)) != 0)
            {
                continue;
            }
            if (rankCounts[r] >= minCount && rankCounts[r] <= maxCount)
            {
                return r;
            }
        }
        return 0;
    }

    private static int CollectRanksWithCount(ReadOnlySpan<int> rankCounts, int count, Span<int> outRanks)
    {
        int idx = 0;
        for (int r = 14; r >= 2 && idx < outRanks.Length; r--)
        {
            if (rankCounts[r] == count)
            {
                outRanks[idx++] = r;
            }
        }
        int matchCount = idx;
        for (; idx < outRanks.Length; idx++)
        {
            outRanks[idx] = 0;
        }
        return matchCount;
    }

    /// <summary>Fills <paramref name="outRanks"/> with the highest set bits of <paramref name="mask"/> (minus <paramref name="excludeMask"/>), descending, 0-padded if exhausted.</summary>
    private static void TopRanks(int mask, int excludeMask, Span<int> outRanks)
    {
        mask &= ~excludeMask;
        int idx = 0;
        for (int r = 14; r >= 2 && idx < outRanks.Length; r--)
        {
            if ((mask & (1 << r)) != 0)
            {
                outRanks[idx++] = r;
            }
        }
        for (; idx < outRanks.Length; idx++)
        {
            outRanks[idx] = 0;
        }
    }

    /// <summary>
    /// Highest straight top-rank present in <paramref name="rankPresenceMask"/>
    /// (a rank-presence bitmask, bit r set ⇔ that rank appears at least once),
    /// 0 if none. The wheel (A-2-3-4-5) is folded in via a virtual ace-low bit
    /// at position 1 — when the only qualifying window is {5,4,3,2,ace-low},
    /// this returns 5 exactly as the design calls for, with no separate branch.
    /// </summary>
    private static int TopStraightRank(int rankPresenceMask)
    {
        int mask = rankPresenceMask;
        if ((mask & (1 << 14)) != 0)
        {
            mask |= 1 << 1; // ace also plays low for the wheel
        }

        for (int hi = 14; hi >= 5; hi--)
        {
            int window = 0;
            for (int k = 0; k < 5; k++)
            {
                window |= 1 << (hi - k);
            }
            if ((mask & window) == window)
            {
                return hi;
            }
        }
        return 0;
    }
}

/// <summary>
/// Differential-test oracle only (§4.3/§14.1) — a naive C(7,5)=21-combo brute
/// force, deliberately implemented independently of <see cref="HoldemEvaluator"/>
/// (sort-based rather than histogram/bitmask-based) so a shared bug cannot hide
/// from the differential test. Never called by production code.
/// </summary>
public static class HoldemOracle
{
    // Every 5-of-7 index combination, generated once at class init — brute force,
    // not a hot path, so the small one-time jagged-array allocation is fine here.
    private static readonly int[][] Combos21 = GenerateCombinations();

    public static int EvaluateBest5Of7(ReadOnlySpan<Card> sevenCards)
    {
        if (sevenCards.Length != 7)
        {
            throw new ArgumentException("EvaluateBest5Of7 requires exactly 7 cards.", nameof(sevenCards));
        }

        int best = int.MinValue;
        Span<Card> subset = stackalloc Card[5];
        foreach (int[] combo in Combos21)
        {
            for (int i = 0; i < 5; i++)
            {
                subset[i] = sevenCards[combo[i]];
            }
            int score = Evaluate5(subset);
            if (score > best)
            {
                best = score;
            }
        }
        return best;
    }

    /// <summary>A self-contained, sort-based 5-card evaluator — no shared code with <see cref="HoldemEvaluator"/>.</summary>
    private static int Evaluate5(ReadOnlySpan<Card> five)
    {
        Span<int> ranks = stackalloc int[5];
        for (int i = 0; i < 5; i++)
        {
            ranks[i] = five[i].Rank;
        }
        // Insertion sort descending (5 elements).
        for (int i = 1; i < 5; i++)
        {
            int key = ranks[i];
            int j = i - 1;
            while (j >= 0 && ranks[j] < key)
            {
                ranks[j + 1] = ranks[j];
                j--;
            }
            ranks[j + 1] = key;
        }

        bool flush = five[0].Suit == five[1].Suit && five[0].Suit == five[2].Suit
            && five[0].Suit == five[3].Suit && five[0].Suit == five[4].Suit;

        bool allUnique = ranks[0] != ranks[1] && ranks[1] != ranks[2] && ranks[2] != ranks[3] && ranks[3] != ranks[4];
        bool straight = false;
        int straightTop = 0;
        if (allUnique)
        {
            if (ranks[0] - ranks[4] == 4)
            {
                straight = true;
                straightTop = ranks[0];
            }
            else if (ranks[0] == 14 && ranks[1] == 5 && ranks[2] == 4 && ranks[3] == 3 && ranks[4] == 2)
            {
                straight = true;
                straightTop = 5; // wheel — ace plays low
            }
        }

        if (straight && flush)
        {
            return Encode(HoldemEvaluator.CategoryStraightFlush, straightTop, 0, 0, 0, 0);
        }

        // Frequency table via simple linear scan (distinct approach from the
        // histogram evaluator's fixed-size rank array).
        Span<int> distinctRanks = stackalloc int[5];
        Span<int> counts = stackalloc int[5];
        int distinctCount = 0;
        for (int i = 0; i < 5; i++)
        {
            int r = ranks[i];
            int found = -1;
            for (int d = 0; d < distinctCount; d++)
            {
                if (distinctRanks[d] == r)
                {
                    found = d;
                    break;
                }
            }
            if (found >= 0)
            {
                counts[found]++;
            }
            else
            {
                distinctRanks[distinctCount] = r;
                counts[distinctCount] = 1;
                distinctCount++;
            }
        }

        // Bubble sort by (count desc, rank desc) — at most 5 elements.
        for (int i = 0; i < distinctCount; i++)
        {
            for (int j = 0; j < distinctCount - 1 - i; j++)
            {
                if (counts[j] < counts[j + 1] || (counts[j] == counts[j + 1] && distinctRanks[j] < distinctRanks[j + 1]))
                {
                    (counts[j], counts[j + 1]) = (counts[j + 1], counts[j]);
                    (distinctRanks[j], distinctRanks[j + 1]) = (distinctRanks[j + 1], distinctRanks[j]);
                }
            }
        }

        if (counts[0] == 4)
        {
            return Encode(HoldemEvaluator.CategoryQuads, distinctRanks[0], distinctRanks[1], 0, 0, 0);
        }
        if (counts[0] == 3 && distinctCount >= 2 && counts[1] >= 2)
        {
            return Encode(HoldemEvaluator.CategoryFullHouse, distinctRanks[0], distinctRanks[1], 0, 0, 0);
        }
        if (flush)
        {
            return Encode(HoldemEvaluator.CategoryFlush, ranks[0], ranks[1], ranks[2], ranks[3], ranks[4]);
        }
        if (straight)
        {
            return Encode(HoldemEvaluator.CategoryStraight, straightTop, 0, 0, 0, 0);
        }
        if (counts[0] == 3)
        {
            return Encode(HoldemEvaluator.CategoryTrips, distinctRanks[0], distinctRanks[1], distinctRanks[2], 0, 0);
        }
        if (counts[0] == 2 && counts[1] == 2)
        {
            return Encode(HoldemEvaluator.CategoryTwoPair, distinctRanks[0], distinctRanks[1], distinctRanks[2], 0, 0);
        }
        if (counts[0] == 2)
        {
            return Encode(HoldemEvaluator.CategoryPair, distinctRanks[0], distinctRanks[1], distinctRanks[2], distinctRanks[3], 0);
        }
        return Encode(HoldemEvaluator.CategoryHighCard, ranks[0], ranks[1], ranks[2], ranks[3], ranks[4]);
    }

    private static int Encode(int category, int t1, int t2, int t3, int t4, int t5) =>
        (category << 20) | (t1 << 16) | (t2 << 12) | (t3 << 8) | (t4 << 4) | t5;

    private static int[][] GenerateCombinations()
    {
        var combos = new System.Collections.Generic.List<int[]>(21);
        for (int a = 0; a < 7; a++)
        {
            for (int b = a + 1; b < 7; b++)
            {
                for (int c = b + 1; c < 7; c++)
                {
                    for (int d = c + 1; d < 7; d++)
                    {
                        for (int e = d + 1; e < 7; e++)
                        {
                            combos.Add(new[] { a, b, c, d, e });
                        }
                    }
                }
            }
        }
        return combos.ToArray();
    }
}
