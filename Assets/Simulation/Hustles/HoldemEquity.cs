using System;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Simulation.Hustles;

/// <summary>
/// The mandated "direct mathematical card simulation evaluating pot odds"
/// (docs/design/hustles_texas_holdem.md §5): a bounded Monte-Carlo runout
/// estimates an agent's win probability (ties counted as fractional wins)
/// given its hole cards, the live board, and a count of live opponents with
/// unknown holes. Every draw comes from the caller's <c>ref RngState</c>, so
/// equity is deterministic per seed. Zero-alloc (stackalloc scratch only).
/// </summary>
public static class HoldemEquity
{
    /// <summary>
    /// §5.1 postflop equity: <paramref name="heroCards"/> (length 2) vs.
    /// <paramref name="liveOpponents"/> (1..5) unknown hands, given
    /// <paramref name="board"/> (length 0..5 — 0 also works, used to bake the
    /// §5.2 preflop table offline). Draws <c>liveOpponents*2 + (5-board.Length)</c>
    /// cards per sample via one partial Fisher–Yates from the undealt deck.
    /// </summary>
    public static double Estimate(
        ReadOnlySpan<Card> heroCards, ReadOnlySpan<Card> board, int liveOpponents, int samples, ref RngState rng)
    {
        if (heroCards.Length != 2)
        {
            throw new ArgumentException("heroCards must have Length 2.", nameof(heroCards));
        }
        if (board.Length > 5)
        {
            throw new ArgumentException("board must have Length <= 5.", nameof(board));
        }
        if (liveOpponents < 1 || liveOpponents > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(liveOpponents), liveOpponents, "liveOpponents must be in [1,5].");
        }
        if (samples < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(samples), samples, "samples must be >= 1.");
        }

        Span<bool> used = stackalloc bool[64]; // indexed directly by Card.Code (max 59)
        foreach (Card c in heroCards)
        {
            used[c.Code] = true;
        }
        foreach (Card c in board)
        {
            used[c.Code] = true;
        }

        Span<Card> remaining = stackalloc Card[52];
        int remainingCount = 0;
        for (int rank = 2; rank <= 14; rank++)
        {
            for (int suit = 0; suit < 4; suit++)
            {
                var c = new Card(rank, suit);
                if (!used[c.Code])
                {
                    remaining[remainingCount++] = c;
                }
            }
        }

        int neededRunout = 5 - board.Length;
        int drawCount = liveOpponents * 2 + neededRunout;
        if (drawCount > remainingCount)
        {
            throw new InvalidOperationException("Not enough undealt cards for the requested opponent count.");
        }

        Span<Card> scratch = stackalloc Card[remainingCount];
        Span<Card> fullBoard = stackalloc Card[5];
        board.CopyTo(fullBoard);
        Span<Card> heroFull = stackalloc Card[7];
        heroCards.CopyTo(heroFull);
        Span<Card> oppFull = stackalloc Card[7];

        double winAccum = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            remaining[..remainingCount].CopyTo(scratch);
            for (int j = 0; j < drawCount; j++)
            {
                int k = j + rng.NextInt(remainingCount - j);
                (scratch[j], scratch[k]) = (scratch[k], scratch[j]);
            }

            for (int r = 0; r < neededRunout; r++)
            {
                fullBoard[board.Length + r] = scratch[liveOpponents * 2 + r];
            }
            fullBoard.CopyTo(heroFull[2..]);
            fullBoard.CopyTo(oppFull[2..]);

            int heroScore = HoldemEvaluator.EvaluateBest7(heroFull);

            int beatsCount = 0, tiesCount = 0;
            for (int o = 0; o < liveOpponents; o++)
            {
                oppFull[0] = scratch[o * 2];
                oppFull[1] = scratch[o * 2 + 1];
                int oppScore = HoldemEvaluator.EvaluateBest7(oppFull);
                if (oppScore < heroScore)
                {
                    beatsCount++;
                }
                else if (oppScore == heroScore)
                {
                    tiesCount++;
                }
            }

            if (beatsCount == liveOpponents)
            {
                winAccum += 1.0;
            }
            else if (beatsCount + tiesCount == liveOpponents)
            {
                winAccum += 1.0 / (tiesCount + 1);
            }
        }

        return winAccum / samples;
    }

    /// <summary>
    /// §5.2: canonical index of one of the 169 starting hands into the
    /// 13x13 grid — diagonal = pairs, upper triangle = suited, lower triangle
    /// = offsuit (rank-index 0=Ace..12=Two on both axes). <paramref name="rank1"/>/
    /// <paramref name="rank2"/> need not be pre-ordered; <paramref name="suited"/>
    /// is ignored for pairs.
    /// </summary>
    public static int StartingHandIndex(int rank1, int rank2, bool suited)
    {
        int a = 14 - rank1;
        int b = 14 - rank2;
        if (a == b)
        {
            return a * 13 + a;
        }
        int hi = Math.Min(a, b);
        int lo = Math.Max(a, b);
        return suited ? hi * 13 + lo : lo * 13 + hi;
    }

    /// <summary>§5.2: the baked offline table — no per-decision MC preflop.</summary>
    public static double LookupPreflopEquity(int handIndex, int liveOpponents)
    {
        if (liveOpponents < 1 || liveOpponents > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(liveOpponents), liveOpponents, "liveOpponents must be in [1,5].");
        }
        return HoldemPreflopTable.Equity[handIndex, liveOpponents - 1];
    }
}
