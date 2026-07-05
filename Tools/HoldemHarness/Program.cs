using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Hustles;

namespace DirtAndDiamonds.Tools.HoldemHarness;

/// <summary>
/// Acceptance checks for the Phase 8d-1 Hold'em self-contained core
/// (docs/design/hustles_texas_holdem.md §14, checks 1–2): the evaluator
/// differential + fixtures, and the equity engine. Data-free/Godot-free, like
/// every Tools harness — compiles only Assets/Simulation/Hustles and
/// RngState.cs (see HoldemHarness.csproj).
///
/// Usage:
///   dotnet run --project Tools/HoldemHarness -- --gen-preflop [samplesPerPoint]
///     regenerates Assets/Simulation/Hustles/HoldemPreflopTable.cs (must run
///     BEFORE the checks below, since check 2's table-fidelity assertions
///     read whatever is currently baked there).
///   dotnet run --project Tools/HoldemHarness
///     runs the acceptance checks.
/// </summary>
internal static class Program
{
    private static readonly List<(string Name, bool Pass, string Detail)> Results = new();

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--gen-preflop")
        {
            int samplesPerPoint = args.Length > 1 && int.TryParse(args[1], out int s) ? s : 20_000;
            GeneratePreflopTable(samplesPerPoint);
            return 0;
        }

        RunEvaluatorChecks();
        RunEquityChecks();
        RunZeroAllocChecks();

        int failed = 0;
        Console.WriteLine();
        foreach ((string name, bool pass, string detail) in Results)
        {
            Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? $" — {detail}" : "")}");
            if (!pass)
            {
                failed++;
            }
        }
        Console.WriteLine($"\n{Results.Count - failed}/{Results.Count} checks passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Check(string name, bool pass, string detail = "") => Results.Add((name, pass, detail));

    private static Card C(int rank, int suit) => new(rank, suit);

    // ------------------------------------------------------------------
    // Check 1 — evaluator: differential test + category-ordering fixtures
    // ------------------------------------------------------------------

    private static void RunEvaluatorChecks()
    {
        RunDifferentialTest();
        RunCategoryOrderingFixtures();
        RunWheelFixture();
        RunSplitPotTieFixture();
    }

    private static void RunDifferentialTest()
    {
        const int trials = 5_000;
        var rng = new RngState(12345);
        Span<Card> deck = stackalloc Card[52];
        int mismatches = 0;
        string firstMismatch = "";

        for (int t = 0; t < trials; t++)
        {
            Deck.FillStandard(deck);
            Deck.Shuffle(deck, ref rng);
            ReadOnlySpan<Card> seven = deck[..7];

            int histogramScore = HoldemEvaluator.EvaluateBest7(seven);
            int oracleScore = HoldemOracle.EvaluateBest5Of7(seven);

            if (histogramScore != oracleScore)
            {
                mismatches++;
                if (firstMismatch.Length == 0)
                {
                    firstMismatch = $"trial {t}: histogram={histogramScore:X} oracle={oracleScore:X} cards=[{string.Join(",", seven.ToArray())}]";
                }
            }
        }

        Check($"evaluator: histogram ≡ 21-combo oracle bit-identical over {trials} random 7-card deals",
            mismatches == 0, mismatches == 0 ? "" : $"{mismatches} mismatches; first: {firstMismatch}");
    }

    private static void RunCategoryOrderingFixtures()
    {
        // Each fixture is a 7-card hand whose best-5 is exactly the named category, no
        // better — ranks/suits picked so no accidental higher category sneaks in.
        (string Name, int ExpectedCategory, Card[] Cards)[] fixtures =
        {
            ("high card", HoldemEvaluator.CategoryHighCard,
                new[] { C(14,0), C(12,1), C(10,2), C(8,3), C(6,0), C(4,1), C(2,2) }),
            ("pair", HoldemEvaluator.CategoryPair,
                new[] { C(14,0), C(14,1), C(10,2), C(8,3), C(6,0), C(4,1), C(2,2) }),
            ("two pair", HoldemEvaluator.CategoryTwoPair,
                new[] { C(14,0), C(14,1), C(10,2), C(10,3), C(6,0), C(4,1), C(2,2) }),
            ("trips", HoldemEvaluator.CategoryTrips,
                new[] { C(14,0), C(14,1), C(14,2), C(10,3), C(6,0), C(4,1), C(2,2) }),
            ("straight", HoldemEvaluator.CategoryStraight,
                new[] { C(6,0), C(7,1), C(8,2), C(9,3), C(10,0), C(2,1), C(4,2) }),
            ("flush", HoldemEvaluator.CategoryFlush,
                new[] { C(14,0), C(11,0), C(8,0), C(5,0), C(2,0), C(3,1), C(4,2) }),
            ("full house", HoldemEvaluator.CategoryFullHouse,
                new[] { C(14,0), C(14,1), C(14,2), C(10,3), C(10,0), C(4,1), C(6,2) }),
            ("quads", HoldemEvaluator.CategoryQuads,
                new[] { C(14,0), C(14,1), C(14,2), C(14,3), C(10,0), C(8,1), C(6,2) }),
            ("straight flush", HoldemEvaluator.CategoryStraightFlush,
                new[] { C(6,0), C(7,0), C(8,0), C(9,0), C(10,0), C(2,1), C(4,2) }),
            ("royal flush", HoldemEvaluator.CategoryStraightFlush,
                new[] { C(10,0), C(11,0), C(12,0), C(13,0), C(14,0), C(2,1), C(4,2) }),
        };

        int[] scores = new int[fixtures.Length];
        bool allCategoriesMatch = true;
        for (int i = 0; i < fixtures.Length; i++)
        {
            scores[i] = HoldemEvaluator.EvaluateBest7(fixtures[i].Cards);
            int actualCategory = scores[i] >> 20;
            if (actualCategory != fixtures[i].ExpectedCategory)
            {
                allCategoriesMatch = false;
            }
        }
        Check("evaluator: each fixture lands the expected category",
            allCategoriesMatch,
            string.Join(", ", Array.ConvertAll(fixtures, f => $"{f.Name}={f.ExpectedCategory}")));

        bool strictlyIncreasing = true;
        for (int i = 1; i < scores.Length - 1; i++) // exclude the royal-vs-SF pair at the end (handled separately)
        {
            if (scores[i] <= scores[i - 1])
            {
                strictlyIncreasing = false;
                break;
            }
        }
        Check("evaluator: category scores strictly increase high card → straight flush",
            strictlyIncreasing,
            string.Join(" < ", Array.ConvertAll(scores[..^1], s => s.ToString("X"))));

        Check("evaluator: royal flush outranks an ordinary straight flush (same category, higher top rank)",
            scores[^1] > scores[^2], $"royal={scores[^1]:X} sf(10-high)={scores[^2]:X}");
    }

    private static void RunWheelFixture()
    {
        // A-2-3-4-5 rainbow: the wheel plays 5-high, not ace-high.
        Card[] wheel = { C(14,0), C(2,1), C(3,2), C(4,3), C(5,0), C(9,1), C(7,2) };
        int wheelScore = HoldemEvaluator.EvaluateBest7(wheel);
        int wheelCategory = wheelScore >> 20;
        int wheelTop = (wheelScore >> 16) & 0xF;
        Check("wheel: A-2-3-4-5 rainbow scores as a straight, top rank 5 (ace plays low)",
            wheelCategory == HoldemEvaluator.CategoryStraight && wheelTop == 5,
            $"category={wheelCategory} top={wheelTop}");

        // 2-3-4-5-6 rainbow must outrank the wheel (higher top rank, same category).
        Card[] sixHigh = { C(2,0), C(3,1), C(4,2), C(5,3), C(6,0), C(9,1), C(11,2) };
        int sixHighScore = HoldemEvaluator.EvaluateBest7(sixHigh);
        Check("wheel: a 6-high straight outranks the wheel",
            sixHighScore > wheelScore, $"6-high={sixHighScore:X} wheel={wheelScore:X}");

        // Oracle must agree on the wheel too (independent implementation, same special case).
        int oracleWheelScore = HoldemOracle.EvaluateBest5Of7(wheel);
        Check("wheel: oracle agrees with the histogram evaluator", oracleWheelScore == wheelScore,
            $"histogram={wheelScore:X} oracle={oracleWheelScore:X}");
    }

    private static void RunSplitPotTieFixture()
    {
        // A broadway board (A-K-Q-J-T rainbow) plays for both heroes — neither hole
        // card pair improves it, so both best-5s are identical: a split pot.
        Card[] board = { C(14, 0), C(13, 1), C(12, 2), C(11, 3), C(10, 0) };
        Card[] hero1 = { board[0], board[1], board[2], board[3], board[4], C(2, 1), C(3, 2) };
        Card[] hero2 = { board[0], board[1], board[2], board[3], board[4], C(4, 1), C(5, 2) };

        int score1 = HoldemEvaluator.EvaluateBest7(hero1);
        int score2 = HoldemEvaluator.EvaluateBest7(hero2);
        Check("evaluator: identical best-5 across different holdings scores equal (split pot)",
            score1 == score2, $"hero1={score1:X} hero2={score2:X}");
    }

    // ------------------------------------------------------------------
    // Check 2 — equity engine
    // ------------------------------------------------------------------

    private static void RunEquityChecks()
    {
        Card[] aa = { C(14, 0), C(14, 1) };

        var rngAA = new RngState(1);
        double aaVs1 = HoldemEquity.Estimate(aa, ReadOnlySpan<Card>.Empty, 1, 60_000, ref rngAA);
        Check("equity: AA vs 1 random opponent ≈ 0.851", Math.Abs(aaVs1 - 0.851) < 0.02, $"got {aaVs1:F4}");

        Card[] aks = { C(14, 0), C(13, 0) };
        Card[] twos = { C(2, 1), C(2, 2) };
        var rngAkVs22 = new RngState(2);
        double akVs22 = EstimateVsKnownHand(aks, twos, 60_000, ref rngAkVs22);
        Check("equity: AKs vs 22 (known matchup) ≈ 0.50 coinflip", Math.Abs(akVs22 - 0.50) < 0.03, $"got {akVs22:F4}");

        var rngMono = new RngState(3);
        double e1 = HoldemEquity.Estimate(aa, ReadOnlySpan<Card>.Empty, 1, 20_000, ref rngMono);
        double e2 = HoldemEquity.Estimate(aa, ReadOnlySpan<Card>.Empty, 2, 20_000, ref rngMono);
        double e3 = HoldemEquity.Estimate(aa, ReadOnlySpan<Card>.Empty, 3, 20_000, ref rngMono);
        double e4 = HoldemEquity.Estimate(aa, ReadOnlySpan<Card>.Empty, 4, 20_000, ref rngMono);
        double e5 = HoldemEquity.Estimate(aa, ReadOnlySpan<Card>.Empty, 5, 20_000, ref rngMono);
        Check("equity: strictly decreases as liveOpponents rises (AA, same seed stream)",
            e1 > e2 && e2 > e3 && e3 > e4 && e4 > e5,
            $"{e1:F3} > {e2:F3} > {e3:F3} > {e4:F3} > {e5:F3}");

        // Postflop sanity: A9 suited with a flopped nut-flush draw + top-card equity vs 1
        // random opponent should sit comfortably ahead (not a design-doc anchor value — just
        // a sanity band, wide enough to only catch a badly broken postflop path).
        Card[] nutFlushDrawHero = { C(14, 0), C(9, 0) };
        Card[] flopTwoOfSuit = { C(2, 0), C(7, 0), C(11, 1) };
        var rngDraw = new RngState(4);
        double drawEquity = HoldemEquity.Estimate(nutFlushDrawHero, flopTwoOfSuit, 1, 40_000, ref rngDraw);
        Check("equity: a flopped nut flush draw (+ overcards) vs 1 opponent sits in a plausible band [0.55, 0.80]",
            drawEquity is >= 0.55 and <= 0.80, $"got {drawEquity:F4}");

        RunPreflopTableChecks();
    }

    /// <summary>
    /// Harness-only helper: hero vs a SPECIFIC known opponent hand (not the
    /// production Estimate(), which always treats opponents as unknown/random
    /// — this is purely a verification tool for the "AKs vs 22" published
    /// reference, which is a fixed matchup, not a vs-random-field number).
    /// </summary>
    private static double EstimateVsKnownHand(ReadOnlySpan<Card> heroCards, ReadOnlySpan<Card> oppCards, int samples, ref RngState rng)
    {
        Span<bool> used = stackalloc bool[64];
        foreach (Card c in heroCards) used[c.Code] = true;
        foreach (Card c in oppCards) used[c.Code] = true;

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

        Span<Card> scratch = stackalloc Card[remainingCount];
        Span<Card> heroFull = stackalloc Card[7];
        heroCards.CopyTo(heroFull);
        Span<Card> oppFull = stackalloc Card[7];
        oppCards.CopyTo(oppFull);

        double winAccum = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            remaining[..remainingCount].CopyTo(scratch);
            for (int j = 0; j < 5; j++)
            {
                int k = j + rng.NextInt(remainingCount - j);
                (scratch[j], scratch[k]) = (scratch[k], scratch[j]);
            }
            for (int r = 0; r < 5; r++)
            {
                heroFull[2 + r] = scratch[r];
                oppFull[2 + r] = scratch[r];
            }

            int heroScore = HoldemEvaluator.EvaluateBest7(heroFull);
            int oppScore = HoldemEvaluator.EvaluateBest7(oppFull);
            if (heroScore > oppScore) winAccum += 1.0;
            else if (heroScore == oppScore) winAccum += 0.5;
        }
        return winAccum / samples;
    }

    private static void RunPreflopTableChecks()
    {
        // The baked table's own AA-vs-1 entry matches the published reference.
        int aaIdx = HoldemEquity.StartingHandIndex(14, 14, suited: false);
        double bakedAaVs1 = HoldemPreflopTable.Equity[aaIdx, 0];
        Check("preflop table: baked AA vs 1 ≈ 0.851", Math.Abs(bakedAaVs1 - 0.851) < 0.02, $"got {bakedAaVs1:F4}");

        // Structural sanity: every hand's equity is non-increasing as opponent count rises.
        bool monotoneEverywhere = true;
        for (int idx = 0; idx < 169 && monotoneEverywhere; idx++)
        {
            for (int k = 1; k < 5; k++)
            {
                if (HoldemPreflopTable.Equity[idx, k] > HoldemPreflopTable.Equity[idx, k - 1] + 1e-9)
                {
                    monotoneEverywhere = false;
                    break;
                }
            }
        }
        Check("preflop table: every hand's equity is non-increasing as opponents rise", monotoneEverywhere);

        // Table fidelity: a handful of representative hands recomputed at a much higher K
        // should land close to the baked (lower-K) value — proves the bake isn't noise.
        (string Name, int Rank1, int Rank2, bool Suited)[] samples =
        {
            ("AA", 14, 14, false),
            ("72o", 7, 2, false),
            ("AKs", 14, 13, true),
            ("KK", 13, 13, false),
            ("76s", 7, 6, true),
        };

        var rng = new RngState(999);
        bool allClose = true;
        var detail = new StringBuilder();
        Span<Card> hero = stackalloc Card[2];
        foreach ((string name, int r1, int r2, bool suited) in samples)
        {
            hero[0] = C(r1, 0);
            hero[1] = (r1 != r2 && suited) ? C(r2, 0) : C(r2, 1);
            int idx = HoldemEquity.StartingHandIndex(r1, r2, suited);
            double baked = HoldemPreflopTable.Equity[idx, 0];
            double recompute = HoldemEquity.Estimate(hero, ReadOnlySpan<Card>.Empty, 1, 80_000, ref rng);
            bool close = Math.Abs(baked - recompute) < 0.03;
            allClose &= close;
            detail.Append($"{name}: baked={baked:F3} recompute={recompute:F3}; ");
        }
        Check("preflop table: high-K recompute matches the baked table within tolerance", allClose, detail.ToString());
    }

    // ------------------------------------------------------------------
    // Zero-allocation (§10) — the evaluator and equity engine are the
    // per-decision hot path once the betting layer (8d-2) lands.
    // ------------------------------------------------------------------

    private static void RunZeroAllocChecks()
    {
        Card[] deckSeed = new Card[52];
        Deck.FillStandard(deckSeed);
        Card[] aa = { C(14, 0), C(14, 1) };

        void OneEvaluatorRun(ulong seed)
        {
            var rng = new RngState(seed);
            Span<Card> deck = stackalloc Card[52];
            deckSeed.CopyTo(deck);
            Deck.Shuffle(deck, ref rng);
            _ = HoldemEvaluator.EvaluateBest7(deck[..7]);
        }

        void OneEquityRun(ulong seed)
        {
            var rng = new RngState(seed);
            _ = HoldemEquity.Estimate(aa, ReadOnlySpan<Card>.Empty, 3, 50, ref rng);
        }

        // Warm-up: JIT everything before measuring.
        for (ulong i = 1; i <= 50; i++)
        {
            OneEvaluatorRun(i);
            OneEquityRun(i);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong i = 1000; i <= 1200; i++)
        {
            OneEvaluatorRun(i);
        }
        long afterEvaluator = GC.GetAllocatedBytesForCurrentThread();

        for (ulong i = 1000; i <= 1200; i++)
        {
            OneEquityRun(i);
        }
        long afterEquity = GC.GetAllocatedBytesForCurrentThread();

        double evaluatorPerRun = (afterEvaluator - before) / 201.0;
        double equityPerRun = (afterEquity - afterEvaluator) / 201.0;

        Console.WriteLine($"--- zero-alloc: {evaluatorPerRun:F1} B/evaluator-run, {equityPerRun:F1} B/equity-run(3 opp, 50 samples) ---\n");

        Check("zero-alloc: a warm EvaluateBest7 call allocates ~0 B", evaluatorPerRun < 16, $"{evaluatorPerRun:F1} B/run");
        Check("zero-alloc: a warm Estimate call (3 opponents, 50 samples) allocates ~0 B", equityPerRun < 16, $"{equityPerRun:F1} B/run");
    }

    // ------------------------------------------------------------------
    // --gen-preflop: offline table generation (§5.2)
    // ------------------------------------------------------------------

    private static void GeneratePreflopTable(int samplesPerPoint)
    {
        var table = new double[169, 5];
        var labels = new string[169];
        var rng = new RngState(0xC0FFEEUL);
        var sw = Stopwatch.StartNew();

        for (int rankHigh = 14; rankHigh >= 2; rankHigh--)
        {
            for (int rankLow = rankHigh; rankLow >= 2; rankLow--)
            {
                if (rankLow == rankHigh)
                {
                    FillRow(table, labels, rankHigh, rankLow, suited: false, isPair: true, samplesPerPoint, ref rng);
                }
                else
                {
                    FillRow(table, labels, rankHigh, rankLow, suited: true, isPair: false, samplesPerPoint, ref rng);
                    FillRow(table, labels, rankHigh, rankLow, suited: false, isPair: false, samplesPerPoint, ref rng);
                }
            }
        }

        Console.WriteLine($"Preflop table generated in {sw.Elapsed.TotalSeconds:F1}s ({samplesPerPoint} samples/point).");
        Console.WriteLine($"  AA vs1={table[HoldemEquity.StartingHandIndex(14, 14, false), 0]:F4} " +
            $"AKs vs1={table[HoldemEquity.StartingHandIndex(14, 13, true), 0]:F4} " +
            $"72o vs1={table[HoldemEquity.StartingHandIndex(7, 2, false), 0]:F4}");

        WriteTableFile(table, labels);
    }

    private static void FillRow(double[,] table, string[] labels, int rankHigh, int rankLow, bool suited, bool isPair, int samples, ref RngState rng)
    {
        Card c0, c1;
        if (isPair)
        {
            c0 = new Card(rankHigh, 0);
            c1 = new Card(rankHigh, 1);
        }
        else if (suited)
        {
            c0 = new Card(rankHigh, 0);
            c1 = new Card(rankLow, 0);
        }
        else
        {
            c0 = new Card(rankHigh, 0);
            c1 = new Card(rankLow, 1);
        }

        Span<Card> hero = stackalloc Card[2] { c0, c1 };
        int idx = HoldemEquity.StartingHandIndex(rankHigh, rankLow, suited);
        labels[idx] = BuildLabel(rankHigh, rankLow, suited, isPair);
        for (int k = 1; k <= 5; k++)
        {
            table[idx, k - 1] = HoldemEquity.Estimate(hero, ReadOnlySpan<Card>.Empty, k, samples, ref rng);
        }
    }

    private static string BuildLabel(int rankHigh, int rankLow, bool suited, bool isPair)
    {
        char hi = RankChar(rankHigh), lo = RankChar(rankLow);
        if (isPair)
        {
            return $"{hi}{hi}";
        }
        return suited ? $"{hi}{lo}s" : $"{hi}{lo}o";
    }

    private static char RankChar(int rank) => rank switch
    {
        14 => 'A',
        13 => 'K',
        12 => 'Q',
        11 => 'J',
        10 => 'T',
        _ => (char)('0' + rank),
    };

    private static void WriteTableFile(double[,] table, string[] labels)
    {
        string outputPath = ResolveOutputPath();

        var sb = new StringBuilder();
        sb.AppendLine("namespace DirtAndDiamonds.Simulation.Hustles;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// GENERATED by `dotnet run --project Tools/HoldemHarness -- --gen-preflop` — do not hand-edit.");
        sb.AppendLine("/// Baked offline heads-up-to-5-way equity for all 169 canonical starting hands");
        sb.AppendLine("/// (docs/design/hustles_texas_holdem.md §5.2). Row index via");
        sb.AppendLine("/// <see cref=\"HoldemEquity.StartingHandIndex\"/>; column index = liveOpponents-1 (1..5).");
        sb.AppendLine("/// Verified against published references before commit: AA vs 1 ≈ 0.851, AKs vs 22 ≈ 0.50.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class HoldemPreflopTable");
        sb.AppendLine("{");
        sb.AppendLine("    public static readonly double[,] Equity = new double[169, 5]");
        sb.AppendLine("    {");
        for (int idx = 0; idx < 169; idx++)
        {
            string row = string.Join(", ", new[]
            {
                table[idx, 0].ToString("F4"), table[idx, 1].ToString("F4"), table[idx, 2].ToString("F4"),
                table[idx, 3].ToString("F4"), table[idx, 4].ToString("F4"),
            });
            string comma = idx < 168 ? "," : "";
            sb.AppendLine($"        {{ {row} }}{comma} // [{idx}] {labels[idx]}");
        }
        sb.AppendLine("    };");
        sb.AppendLine("}");

        File.WriteAllText(outputPath, sb.ToString());
        Console.WriteLine($"Wrote {outputPath}");
    }

    private static string ResolveOutputPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "DirtAndDiamonds.sln")))
        {
            current = current.Parent;
        }
        if (current == null)
        {
            throw new InvalidOperationException($"Could not locate repo root (DirtAndDiamonds.sln) from {AppContext.BaseDirectory}");
        }
        return Path.Combine(current.FullName, "Assets", "Simulation", "Hustles", "HoldemPreflopTable.cs");
    }
}
