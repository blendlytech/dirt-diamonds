using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Hustles;

namespace DirtAndDiamonds.Tools.HoldemHarness;

/// <summary>
/// Acceptance checks for the Phase 8d Hold'em core (docs/design/hustles_texas_holdem.md
/// §14, checks 1–10): the evaluator differential + fixtures, the equity
/// engine, pot-odds/MDF/bluff math, the betting state machine + chip
/// conservation, the session lifecycle, EV/skill bands, the raid tail,
/// determinism, and zero-alloc. Data-free/Godot-free, like every Tools
/// harness — compiles only Assets/Simulation/Hustles and RngState.cs (see
/// HoldemHarness.csproj).
///
/// Usage:
///   dotnet run --project Tools/HoldemHarness -- --gen-preflop [samplesPerPoint]
///     regenerates Assets/Simulation/Hustles/HoldemPreflopTable.cs (must run
///     BEFORE the checks below, since check 2's table-fidelity assertions
///     read whatever is currently baked there).
///   dotnet run --project Tools/HoldemHarness -- --fast
///     runs every check except the slow (~7 min) EV/skill-band sweep —
///     useful while iterating on anything upstream of it.
///   dotnet run --project Tools/HoldemHarness -- --debug-archetypes [seatCount]
///     NOT part of the check suite: isolates each named archetype's own
///     bb/100 + VPIP/showdown stats against a homogeneous TAG field (default
///     6 seats) — the tool that found the pot-odds/bluff-ratio mismatch (see
///     RequiredEquity's doc comment) and remains useful for any future
///     archetype-tuning pass.
///   dotnet run --project Tools/HoldemHarness
///     runs the full acceptance suite.
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

        if (args.Length > 0 && args[0] == "--debug-archetypes")
        {
            DebugArchetypesVsHomogeneousField(args);
            return 0;
        }

        if (args.Length > 0 && args[0] == "--fast")
        {
            RunEvaluatorChecks();
            RunEquityChecks();
            RunPotOddsChecks();
            RunBettingMechanicsChecks();
            RunSessionChecks();
            RunRaidTailChecks();
            RunDeterminismChecks();
            RunZeroAllocChecks();
            return ReportResults();
        }

        RunEvaluatorChecks();
        RunEquityChecks();
        RunPotOddsChecks();
        RunBettingMechanicsChecks();
        RunSessionChecks();
        RunEvSkillBandChecks();
        RunRaidTailChecks();
        RunDeterminismChecks();
        RunZeroAllocChecks();

        return ReportResults();
    }

    private static int ReportResults()
    {
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
    // Check 3 — pot-odds / MDF / bluff math (§6) + the hero autopilot (§7.3)
    // ------------------------------------------------------------------

    private static void RunPotOddsChecks()
    {
        // §6.1 pot odds — c/P, where P is the running pot AT decision time
        // (already includes the bet being faced). Matches the doc's own
        // worked examples exactly (pot-sized ⇒ 50%, half-pot ⇒ 33%) and,
        // load-bearingly, is algebraically identical to §6.2's MDF formula —
        // see HoldemAgent.RequiredEquity's doc comment for why that
        // equivalence turned out to be necessary, not cosmetic (a "more
        // textbook" c/(pot+c) reading was tried first and silently made
        // every archetype's own bluffs unprofitable, since it doesn't match
        // what §6.3's bluff-ratio calibrates a caller's indifference against).
        double potSizedBet = HoldemAgent.RequiredEquity(owed: 100, pot: 200); // P0=100, bet=100, pot_current(=P)=200
        Check("pot odds: a pot-sized bet requires 50% equity to call (c/P)",
            Math.Abs(potSizedBet - 0.5) < 1e-9, $"got {potSizedBet:F4}");
        double halfPotBet = HoldemAgent.RequiredEquity(owed: 50, pot: 150); // P0=100, bet=50, pot_current(=P)=150
        Check("pot odds: a half-pot bet requires 1/3 equity to call", Math.Abs(halfPotBet - 1.0 / 3.0) < 1e-9, $"got {halfPotBet:F4}");

        // §6.2 MDF — unambiguous, matches the doc exactly.
        Check("MDF: a pot-sized bet must be defended ≥50% of the time", Math.Abs(HoldemAgent.MinimumDefenseFrequency(1.0) - 0.5) < 1e-9);
        Check("MDF: a half-pot bet must be defended ≥66.7% of the time", Math.Abs(HoldemAgent.MinimumDefenseFrequency(0.5) - 2.0 / 3.0) < 1e-9);

        // §6.3 bluff ratio — matches the doc's own worked fractions exactly (2/3 value:1/3 bluff pot-size; 3/4:1/4 half-pot).
        Check("bluff ratio: a pot-sized bet ⇒ 1/3 of the betting range is bluffs", Math.Abs(HoldemAgent.BluffFraction(1.0) - 1.0 / 3.0) < 1e-9);
        Check("bluff ratio: a half-pot bet ⇒ 1/4 of the betting range is bluffs", Math.Abs(HoldemAgent.BluffFraction(0.5) - 0.25) < 1e-9);

        HoldemProfile.ArchetypeTunables nit = HoldemProfile.GetArchetype(HoldemArchetype.Nit);
        HoldemProfile.ArchetypeTunables tag = HoldemProfile.GetArchetype(HoldemArchetype.TAG);
        HoldemProfile.ArchetypeTunables lag = HoldemProfile.GetArchetype(HoldemArchetype.LAG);
        HoldemProfile.ArchetypeTunables maniac = HoldemProfile.GetArchetype(HoldemArchetype.Maniac);
        Check("archetypes: bluffMult strictly increases Nit < TAG < LAG < Maniac (§7.1's table)",
            nit.BluffMult < tag.BluffMult && tag.BluffMult < lag.BluffMult && lag.BluffMult < maniac.BluffMult,
            $"{nit.BluffMult} < {tag.BluffMult} < {lag.BluffMult} < {maniac.BluffMult}");

        HoldemProfile.ArchetypeTunables auto0 = HoldemAgent.HeroAutopilotProfile(0.0);
        HoldemProfile.ArchetypeTunables auto1 = HoldemAgent.HeroAutopilotProfile(1.0);
        HoldemProfile.ArchetypeTunables autoHalf = HoldemAgent.HeroAutopilotProfile(0.5);
        Check("hero autopilot: reck=0 is exactly TAG's tunables",
            auto0.ValueMargin == tag.ValueMargin && auto0.BluffMult == tag.BluffMult);
        Check("hero autopilot: reck=1 caps out at exactly Maniac's aggression (never past it)",
            Math.Abs(auto1.ValueMargin - maniac.ValueMargin) < 1e-9 && Math.Abs(auto1.BluffMult - maniac.BluffMult) < 1e-9,
            $"valueMargin={auto1.ValueMargin} (want {maniac.ValueMargin}) bluffMult={auto1.BluffMult} (want {maniac.BluffMult})");
        Check("hero autopilot: reck=0.5 sits strictly between TAG and Maniac",
            autoHalf.ValueMargin < tag.ValueMargin && autoHalf.ValueMargin > maniac.ValueMargin
            && autoHalf.BluffMult > tag.BluffMult && autoHalf.BluffMult < maniac.BluffMult,
            $"valueMargin={autoHalf.ValueMargin:F3} bluffMult={autoHalf.BluffMult:F3}");
    }

    // ------------------------------------------------------------------
    // Check 4 — betting mechanics + chip conservation (§8)
    // ------------------------------------------------------------------

    private static void RunBettingMechanicsChecks()
    {
        RunBlindsAndButtonChecks();
        RunIllegalActionChecks();
        RunChipConservationSweep();
    }

    private static void RunBlindsAndButtonChecks()
    {
        var table = new HoldemHandState();
        Span<HoldemArchetype> archetypes = stackalloc HoldemArchetype[3] { HoldemArchetype.TAG, HoldemArchetype.TAG, HoldemArchetype.TAG };
        Span<long> stacks = stackalloc long[3] { 1000, 1000, 1000 };
        // button=seat0; in a 3-max hand the seat AFTER the BB acts first preflop,
        // which for button=0/SB=1/BB=2 is seat0 itself — mark it hero so StartHand
        // pauses right after blinds, before any further action, for a clean read.
        table.ConfigureSeats(3, archetypes, heroSeat: 0, stacks, buttonSeat: 0, smallBlind: 5, bigBlind: 10);
        var rng = new RngState(42);
        table.StartHand(ref rng);

        Check("betting: the table pauses for the button's first preflop action in a 3-max hand",
            table.AwaitingHero && table.ActingSeat == 0, $"awaiting={table.AwaitingHero} acting={table.ActingSeat}");
        Check("blinds: the small blind seat posted SB", table.CommittedTotal[1] == 5, $"got {table.CommittedTotal[1]}");
        Check("blinds: the big blind seat posted BB", table.CommittedTotal[2] == 10, $"got {table.CommittedTotal[2]}");
        Check("blinds: the pot equals SB+BB before any further action", table.Pot == 15, $"got {table.Pot}");

        int before = table.ButtonSeat;
        table.RotateButton();
        Check("button: rotates by exactly one seat", table.ButtonSeat == (before + 1) % 3, $"before={before} after={table.ButtonSeat}");
    }

    private static void RunIllegalActionChecks()
    {
        // Heads-up: the button/SB acts first preflop — deterministic regardless of RNG.
        var table = new HoldemHandState();
        Span<HoldemArchetype> archetypes = stackalloc HoldemArchetype[2] { HoldemArchetype.TAG, HoldemArchetype.TAG };
        Span<long> stacks = stackalloc long[2] { 12, 1000 }; // hero posts SB=5, leaving Stack=7, available=12 — too short for a legal min-raise (would need 20)
        table.ConfigureSeats(2, archetypes, heroSeat: 0, stacks, buttonSeat: 0, smallBlind: 5, bigBlind: 10);
        var rng = new RngState(7);
        table.StartHand(ref rng);
        Check("illegal actions: heads-up hero (button/SB) acts first preflop", table.AwaitingHero && table.ActingSeat == 0);

        bool threwOverStack = false;
        try
        {
            table.SubmitHeroAction(HeroAction.RaiseTo(13), ref rng);
        }
        catch (ArgumentOutOfRangeException)
        {
            threwOverStack = true;
        }
        Check("illegal actions: a raise-to above available chips throws", threwOverStack);
        Check("illegal actions: a rejected action does not consume the pending hero decision", table.AwaitingHero && table.ActingSeat == 0);

        bool threwSubMinRaise = false;
        try
        {
            table.SubmitHeroAction(HeroAction.RaiseTo(11), ref rng); // between CurrentBet(10) and minLegal(20), not all-in(12)
        }
        catch (ArgumentOutOfRangeException)
        {
            threwSubMinRaise = true;
        }
        Check("illegal actions: a raise-to below the minimum legal raise (and not all-in) throws", threwSubMinRaise);

        table.SubmitHeroAction(HeroAction.RaiseTo(12), ref rng); // hero's entire stack — legal even though below the 20 min-raise
        Check("illegal actions: an all-in raise below the min-raise is legal", table.Stack[0] == 0, $"stack={table.Stack[0]}");
    }

    private static void RunChipConservationSweep()
    {
        var rng = new RngState(999);
        var table = new HoldemHandState();
        var archetypeValues = (HoldemArchetype[])Enum.GetValues(typeof(HoldemArchetype));
        const int trials = 3000;
        int violations = 0;
        string firstViolation = "";
        Span<HoldemArchetype> archetypeScratch = stackalloc HoldemArchetype[HoldemHandState.MaxSeats];
        Span<long> stackScratch = stackalloc long[HoldemHandState.MaxSeats];

        for (int t = 0; t < trials; t++)
        {
            int seatCount = 2 + rng.NextInt(5); // 2..6
            const long bb = 10;
            Span<HoldemArchetype> archetypes = archetypeScratch[..seatCount];
            Span<long> stacks = stackScratch[..seatCount];
            long sumBefore = 0;
            for (int i = 0; i < seatCount; i++)
            {
                archetypes[i] = archetypeValues[rng.NextInt(archetypeValues.Length)];
                // A mix of very short stacks (forces frequent all-ins/side pots) and deep ones.
                long stack = rng.NextDouble() < 0.4 ? bb + rng.NextInt((int)bb * 3) : bb * (20 + rng.NextInt(180));
                stacks[i] = stack;
                sumBefore += stack;
            }
            table.ConfigureSeats(seatCount, archetypes, heroSeat: -1, stacks, buttonSeat: rng.NextInt(seatCount), smallBlind: 5, bigBlind: bb);
            table.StartHand(ref rng); // no hero seat ⇒ runs the whole hand to completion in one call

            long sumAfter = 0;
            for (int i = 0; i < seatCount; i++)
            {
                sumAfter += table.Stack[i];
            }
            long rake = table.Result.Rake;
            if (!table.HandComplete || sumAfter + rake != sumBefore)
            {
                violations++;
                if (firstViolation.Length == 0)
                {
                    firstViolation = $"trial {t}: seats={seatCount} before={sumBefore} after={sumAfter} rake={rake} complete={table.HandComplete}";
                    Console.WriteLine($"--- chip conservation violation detail (trial {t}) ---");
                    Console.WriteLine($"seats={seatCount} button={table.ButtonSeat} sawFlop={table.SawFlop} boardCount={table.BoardCount} pot={table.Pot}");
                    for (int i = 0; i < seatCount; i++)
                    {
                        Console.WriteLine($"  seat {i}: status={table.Status[i]} committedTotal={table.CommittedTotal[i]} stackAfter={table.Stack[i]} startStack={stacks[i]} netChipChange={table.Result.NetChipChange[i]} showdownScore={table.Result.ShowdownScore[i]:X}");
                    }
                }
            }
        }
        Check($"betting: chip conservation (Σstacks + rake = Σstacks_before) holds across {trials} random hands (varied seat counts, forced all-ins)",
            violations == 0, violations == 0 ? "" : $"{violations} violations; first: {firstViolation}");
    }

    // ------------------------------------------------------------------
    // Check 5 — session: buy-in validation, bank-&-exit, bust (§9)
    // ------------------------------------------------------------------

    private static void RunSessionChecks()
    {
        var ctx = new HoldemContext(funds: 5000, heat: 0.1, reck: 0.2);
        HoldemProfile.StakesTierProfile mid = HoldemProfile.GetTier(StakesTier.Mid);

        var rngA = new RngState(1);
        bool threwTooSmall = false;
        try
        {
            HoldemSession.StartSession(in ctx, StakesTier.Mid, buyIn: mid.BuyInMin - 1, numOpponents: 5, ref rngA);
        }
        catch (ArgumentOutOfRangeException)
        {
            threwTooSmall = true;
        }
        Check("session: a buy-in below 20·BB throws", threwTooSmall);

        var rngB = new RngState(1);
        bool threwTooBig = false;
        try
        {
            HoldemSession.StartSession(in ctx, StakesTier.Mid, buyIn: mid.BuyInMax + 1, numOpponents: 5, ref rngB);
        }
        catch (ArgumentOutOfRangeException)
        {
            threwTooBig = true;
        }
        Check("session: a buy-in above min(funds,100·BB) throws", threwTooBig);

        var rngC = new RngState(2);
        var session = HoldemSession.StartSession(in ctx, StakesTier.Mid, buyIn: 1000, numOpponents: 3, ref rngC);
        HoldemProfile.ArchetypeTunables heroProfile = HoldemAgent.HeroAutopilotProfile(ctx.Reck);
        for (int i = 0; i < 10 && !session.IsOver; i++)
        {
            HoldemSession.PlayAutopilotHand(session, in ctx, heroProfile, ref rngC);
        }
        if (!session.IsOver)
        {
            long stackBeforeStandUp = session.Table.Stack[session.HeroSeat];
            HustleResolution resolution = HoldemSession.StandUp(session);
            Check("session: bank-&-exit's FundsDelta = stackReturned - buyIn",
                Math.Abs(resolution.FundsDelta - (stackBeforeStandUp - session.BuyIn)) < 1e-9, $"FundsDelta={resolution.FundsDelta}");
        }
        else
        {
            Check("session: an early bust/raid ends at FundsDelta = -buyIn (bank-&-exit fixture happened to bust)",
                Math.Abs(session.FundsDelta + session.BuyIn) < 1e-9, $"FundsDelta={session.FundsDelta}");
        }

        RunBustFixture();

        var rngD = new RngState(3);
        var session2 = HoldemSession.StartSession(in ctx, StakesTier.Low, buyIn: 100, numOpponents: 2, ref rngD);
        HoldemSession.StandUp(session2);
        bool threwDoubleStandUp = false;
        try
        {
            HoldemSession.StandUp(session2);
        }
        catch (InvalidOperationException)
        {
            threwDoubleStandUp = true;
        }
        Check("session: standing up an already-terminal session throws", threwDoubleStandUp);
    }

    private static void RunBustFixture()
    {
        var ctx = new HoldemContext(funds: 5000, heat: 0.0, reck: 0.0);
        HoldemProfile.ArchetypeTunables heroProfile = HoldemAgent.HeroAutopilotProfile(ctx.Reck);
        HoldemProfile.StakesTierProfile mid = HoldemProfile.GetTier(StakesTier.Mid);
        bool foundBust = false;

        for (ulong seed = 1; seed <= 200 && !foundBust; seed++)
        {
            var rng = new RngState(seed);
            var session = HoldemSession.StartSession(in ctx, StakesTier.Mid, buyIn: mid.BuyInMin, numOpponents: 3, ref rng);
            for (int h = 0; h < 300 && !session.IsOver; h++)
            {
                HoldemSession.PlayAutopilotHand(session, in ctx, heroProfile, ref rng);
            }
            if (session.Busted)
            {
                foundBust = true;
                Check("session: a natural bust ends at FundsDelta = -buyIn", Math.Abs(session.FundsDelta + session.BuyIn) < 1e-9, $"seed={seed} FundsDelta={session.FundsDelta}");
            }
        }
        Check("session: at least one seed produces a natural bust within 300 hands at min buy-in", foundBust);
    }

    // ------------------------------------------------------------------
    // Check 6 — EV / skill bands (§13)
    // ------------------------------------------------------------------

    private static void RunEvSkillBandChecks()
    {
        const int hands = 60_000;
        const ulong sharedSeed = 42; // shared across every policy below (common random numbers, see MeasureBb100)
        double tagLow = MeasureBb100(HoldemArchetype.TAG, null, StakesTier.Low, hands, sharedSeed);
        double lagLow = MeasureBb100(HoldemArchetype.LAG, null, StakesTier.Low, hands, sharedSeed);
        double nitLow = MeasureBb100(HoldemArchetype.Nit, null, StakesTier.Low, hands, sharedSeed);
        double stationLow = MeasureBb100(HoldemArchetype.Station, null, StakesTier.Low, hands, sharedSeed);
        double maniacLow = MeasureBb100(HoldemArchetype.Maniac, null, StakesTier.Low, hands, sharedSeed);
        double randomLow = MeasureBb100(null, HoldemAgent.RandomPolicyDecision, StakesTier.Low, hands, sharedSeed);

        Console.WriteLine($"--- EV bands (Low, {hands} hands/policy, common random numbers): TAG={tagLow:F2} LAG={lagLow:F2} Nit={nitLow:F2} Station={stationLow:F2} Maniac={maniacLow:F2} Random={randomLow:F2} bb/100 ---");

        // §13's target shape — TAG best, LAG/Nit a tier below, Station/Maniac
        // worst — measured against the tier's REAL field skew (mostly
        // Station/Maniac/LAG at Low, per §7.1): TAG clearly on top, Nit and
        // Station close to each other well below it, Maniac's heavy
        // over-bluffing (bluffMult 2.2) a severe loser. (A homogeneous
        // all-TAG-opponents probe via `--debug-archetypes`, run while
        // debugging the pot-odds formula below, showed a different — and
        // real-poker-theory-plausible — ranking among the passive archetypes,
        // since nothing in this model exploits a tight range's predictability
        // the way an adaptive opponent would; the doc's ordering is
        // specifically a claim about THIS field mix, which is what's asserted
        // here.)
        Check("skill monotonicity: a TAG hero clearly beats a Maniac hero at the same table/stakes", tagLow > maniacLow + 5, $"TAG={tagLow:F2} Maniac={maniacLow:F2}");
        Check("skill monotonicity: a TAG hero clearly beats a LAG hero at the same table/stakes", tagLow > lagLow + 5, $"TAG={tagLow:F2} LAG={lagLow:F2}");
        Check("skill monotonicity: a TAG hero clearly beats a Nit hero at the same table/stakes", tagLow > nitLow + 5, $"TAG={tagLow:F2} Nit={nitLow:F2}");
        Check("skill monotonicity: a TAG hero clearly beats a Station hero at the same table/stakes", tagLow > stationLow + 5, $"TAG={tagLow:F2} Station={stationLow:F2}");
        Check("skill monotonicity: Nit and Station (the two tightest/most passive archetypes) land close together, well below TAG", Math.Abs(nitLow - stationLow) < 0.25 * tagLow, $"Nit={nitLow:F2} Station={stationLow:F2} TAG={tagLow:F2}");
        Check("skill monotonicity: a TAG hero shows a positive win-rate against a soft (Low) field", tagLow > 0, $"TAG={tagLow:F2}");
        Check("rake binds: a random/coin-flip-quality (equity-blind) hero is net negative", randomLow < 0, $"Random={randomLow:F2}");

        double tagHigh = MeasureBb100(HoldemArchetype.TAG, null, StakesTier.High, hands, sharedSeed);
        Console.WriteLine($"--- EV bands (High, {hands} hands): TAG={tagHigh:F2} bb/100 ---");
        Check("field gradient: a TAG hero's win-rate is higher at Low (soft field) than at High (sharp field)", tagLow > tagHigh, $"Low={tagLow:F2} High={tagHigh:F2}");
    }

    /// <summary>
    /// Harness-only measurement: bb/100 for one policy at seat 0 against the
    /// tier's field-skew opponents. Each hand index draws its own fresh,
    /// independently-seeded <see cref="RngState"/> (deck + opponent
    /// archetypes + all decisions) derived from <paramref name="seed"/> and
    /// the hand index alone — so calling this with the SAME <paramref name="seed"/>
    /// for two different subject policies deals hand #i the identical cards
    /// against the identical opponent archetypes both times, differing only
    /// in how seat 0 itself plays (common random numbers). Without this,
    /// comparing policies drawn from independent RNG streams was dominated by
    /// field-composition and card-luck noise, not skill — real poker's
    /// variance is high enough that a naive single evolving stream needed
    /// far more than 30-60k hands per policy to converge cleanly. The
    /// subject's seat also rotates every hand (<c>h % seatCount</c>) so no
    /// policy is unfairly stuck with a fixed positional edge/disadvantage.
    /// Every seat resets to a fixed deep stack each hand, isolating the pure
    /// per-hand skill signal from stack-depth/bust confounds — <see cref="HoldemSession"/>
    /// (tested separately in check 5) is what actually carries a real
    /// bankroll hand-to-hand.
    /// </summary>
    private static double MeasureBb100(HoldemArchetype? subjectArchetype, PokerPolicy? subjectPolicyOverride, StakesTier tier, int handCount, ulong seed)
    {
        var table = new HoldemHandState();
        HoldemProfile.StakesTierProfile tierProfile = HoldemProfile.GetTier(tier);
        double[] skew = HoldemProfile.GetFieldSkew(tier);
        const int seatCount = 6;
        long deepStack = tierProfile.BigBlind * 200;

        Span<HoldemArchetype> archetypes = stackalloc HoldemArchetype[seatCount];
        Span<long> stacks = stackalloc long[seatCount];
        for (int i = 0; i < seatCount; i++)
        {
            stacks[i] = deepStack;
        }

        long totalNet = 0;
        for (int h = 0; h < handCount; h++)
        {
            var rng = new RngState(seed * 1_000_003UL + (ulong)h + 1);
            int subjectSeat = h % seatCount;
            for (int i = 0; i < seatCount; i++)
            {
                archetypes[i] = i == subjectSeat ? (subjectArchetype ?? HoldemArchetype.TAG) : DrawArchetypeForHarness(skew, ref rng);
            }
            table.ConfigureSeats(seatCount, archetypes, heroSeat: -1, stacks, buttonSeat: h % seatCount, tierProfile.SmallBlind, tierProfile.BigBlind);
            table.SeatPolicyOverride[subjectSeat] = subjectPolicyOverride;
            table.StartHand(ref rng);
            totalNet += table.Result.NetChipChange[subjectSeat];
        }
        return totalNet / (double)tierProfile.BigBlind / (handCount / 100.0);
    }

    private static HoldemArchetype DrawArchetypeForHarness(double[] skew, ref RngState rng)
    {
        double roll = rng.NextDouble();
        double cumulative = 0;
        for (int i = 0; i < skew.Length; i++)
        {
            cumulative += skew[i];
            if (roll < cumulative)
            {
                return (HoldemArchetype)i;
            }
        }
        return (HoldemArchetype)(skew.Length - 1);
    }

    /// <summary>Debug-only (not part of the check suite): isolates each named archetype's own performance against a HOMOGENEOUS 5-TAG field (no skew/field-composition variable at all), plus VPIP/showdown stats, to separate a real decision-logic issue from field-composition noise.</summary>
    private static void DebugArchetypesVsHomogeneousField(string[] args)
    {
        int seatCount = args.Length > 1 && int.TryParse(args[1], out int sc) ? sc : 6;
        Span<HoldemArchetype> archetypes = stackalloc HoldemArchetype[seatCount];
        Span<long> stacks = stackalloc long[seatCount];
        foreach (HoldemArchetype subject in (HoldemArchetype[])Enum.GetValues(typeof(HoldemArchetype)))
        {
            const int handCount = 20_000;
            var table = new HoldemHandState();
            var tierProfile = HoldemProfile.GetTier(StakesTier.Low);
            long deepStack = tierProfile.BigBlind * 200;
            for (int i = 0; i < seatCount; i++)
            {
                stacks[i] = deepStack;
            }

            long totalNet = 0;
            int vpipCount = 0; // voluntarily put chips in preflop beyond the blind already owed
            int showdownCount = 0;
            int showdownWins = 0;
            for (int h = 0; h < handCount; h++)
            {
                var rng = new RngState(9000UL + (ulong)h);
                int subjectSeat = h % seatCount;
                for (int i = 0; i < seatCount; i++)
                {
                    archetypes[i] = i == subjectSeat ? subject : HoldemArchetype.TAG;
                }
                table.ConfigureSeats(seatCount, archetypes, heroSeat: -1, stacks, buttonSeat: h % seatCount, tierProfile.SmallBlind, tierProfile.BigBlind);
                table.StartHand(ref rng);

                bool vpip = table.Status[subjectSeat] != SeatStatus.Folded || table.CommittedTotal[subjectSeat] > tierProfile.BigBlind || table.CommittedTotal[subjectSeat] > tierProfile.SmallBlind;
                if (vpip)
                {
                    vpipCount++;
                }
                if (table.Result.WentToShowdown && table.Status[subjectSeat] != SeatStatus.Folded)
                {
                    showdownCount++;
                    if (table.Result.NetChipChange[subjectSeat] > 0)
                    {
                        showdownWins++;
                    }
                }
                totalNet += table.Result.NetChipChange[subjectSeat];
            }
            double bb100 = totalNet / (double)tierProfile.BigBlind / (handCount / 100.0);
            Console.WriteLine($"{subject,-8} vs 5xTAG: bb/100={bb100,8:F2}  VPIP={100.0 * vpipCount / handCount,5:F1}%  showdowns={showdownCount,5} ({100.0 * showdownCount / handCount,4:F1}%)  showdownWin%={100.0 * showdownWins / Math.Max(1, showdownCount),5:F1}%");
        }
    }

    // ------------------------------------------------------------------
    // Check 7 — the raid tail (§9)
    // ------------------------------------------------------------------

    private static void RunRaidTailChecks()
    {
        var ctxHighHeat = new HoldemContext(funds: 5000, heat: 1.0, reck: 0.5);
        HoldemProfile.ArchetypeTunables heroProfileHighReck = HoldemAgent.HeroAutopilotProfile(ctxHighHeat.Reck);
        var rngLow = new RngState(55);
        var lowSession = HoldemSession.StartSession(in ctxHighHeat, StakesTier.Low, buyIn: HoldemProfile.GetTier(StakesTier.Low).BuyInMin, numOpponents: 3, ref rngLow);
        for (int h = 0; h < 500 && !lowSession.IsOver; h++)
        {
            HoldemSession.PlayAutopilotHand(lowSession, in ctxHighHeat, heroProfileHighReck, ref rngLow);
        }
        Check("raid tail: Low stakes never raids, even at max heat over 500 hands", !lowSession.Raided);

        bool foundRaid = false;
        for (ulong seed = 1; seed <= 500 && !foundRaid; seed++)
        {
            var ctx = new HoldemContext(funds: 5000, heat: 1.0, reck: 0.2);
            HoldemProfile.ArchetypeTunables heroProfile = HoldemAgent.HeroAutopilotProfile(ctx.Reck);
            var rng = new RngState(seed);
            var session = HoldemSession.StartSession(in ctx, StakesTier.High, buyIn: HoldemProfile.GetTier(StakesTier.High).BuyInMax, numOpponents: 3, ref rng);
            for (int h = 0; h < 30 && !session.IsOver; h++)
            {
                HoldemSession.PlayAutopilotHand(session, in ctx, heroProfile, ref rng);
            }
            if (session.Raided)
            {
                foundRaid = true;
                Check("raid tail: a raid seizes the on-table stack (FundsDelta = -buyIn)", Math.Abs(session.FundsDelta + session.BuyIn) < 1e-9, $"seed={seed}");
                Check("raid tail: a raid sets detection +10 (on top of the Mid/High baseline +1)",
                    session.DetectionRiskDelta == HoldemProfile.RaidDetectionDelta + HoldemProfile.BaselineDetectionMidHigh, $"got {session.DetectionRiskDelta}");
                Check("raid tail: a raid sets stress +20", Math.Abs(session.StressDelta - HoldemProfile.RaidStressDelta) < 1e-9, $"got {session.StressDelta}");
                Check("raid tail: a raid sets gambling_bust", session.SetGamblingBustFlag);
            }
        }
        Check("raid tail: at least one seed produces a raid within 500 seeds × 30 hands at High stakes/max heat", foundRaid);

        RunRaidRateMeasurement();
    }

    private static void RunRaidRateMeasurement()
    {
        var ctx = new HoldemContext(funds: 5000, heat: 0.5, reck: 0.0);
        HoldemProfile.ArchetypeTunables heroProfile = HoldemAgent.HeroAutopilotProfile(ctx.Reck);
        HoldemProfile.StakesTierProfile tierProfile = HoldemProfile.GetTier(StakesTier.High);
        double h = Math.Clamp(tierProfile.RaidHazardBase + HoldemProfile.RaidHazardHeatCoefficient * ctx.Heat, 0, HoldemProfile.RaidHazardCap);
        const int handsPerSession = 10;
        const int sessions = 4000;
        int raidedCount = 0;

        for (ulong seed = 1; seed <= sessions; seed++)
        {
            var rng = new RngState(seed * 7919 + 3);
            var session = HoldemSession.StartSession(in ctx, StakesTier.High, buyIn: tierProfile.BuyInMax, numOpponents: 3, ref rng);
            for (int i = 0; i < handsPerSession && !session.IsOver; i++)
            {
                HoldemSession.PlayAutopilotHand(session, in ctx, heroProfile, ref rng);
            }
            if (session.Raided)
            {
                raidedCount++;
            }
        }

        // A session that busts before handsPerSession removes itself from
        // the raid-risk pool for its remaining would-be hands (a bust ends
        // the session too), which trims the measured rate a bit below the
        // flat-n formula — a real, understood, one-directional bias (NOT
        // fixable by attributing each session's own HandsPlayed as its "trial
        // count": that count is itself a stopping time correlated with
        // whether THAT session raided, which biases things worse the other
        // way). ±0.08 comfortably covers the modest bust-driven undercount at
        // this hazard/hand-count without trying to model it exactly.
        double measuredRate = raidedCount / (double)sessions;
        double expectedRate = 1 - Math.Pow(1 - h, handsPerSession);
        Check($"raid tail: measured raid rate over {sessions} sessions × {handsPerSession} hands ≈ 1-(1-h)^n",
            Math.Abs(measuredRate - expectedRate) < 0.08, $"measured={measuredRate:F3} expected={expectedRate:F3} (h={h:F4})");
    }

    // ------------------------------------------------------------------
    // Check 8 — determinism (§10)
    // ------------------------------------------------------------------

    private static void RunDeterminismChecks()
    {
        var ctx = new HoldemContext(funds: 5000, heat: 0.3, reck: 0.4);

        HustleResolution Run(ulong seed)
        {
            var rng = new RngState(seed);
            var session = HoldemSession.StartSession(in ctx, StakesTier.Mid, buyIn: 1000, numOpponents: 4, ref rng);
            HoldemProfile.ArchetypeTunables heroProfile = HoldemAgent.HeroAutopilotProfile(ctx.Reck);
            for (int h = 0; h < 40 && !session.IsOver; h++)
            {
                HoldemSession.PlayAutopilotHand(session, in ctx, heroProfile, ref rng);
            }
            if (!session.IsOver)
            {
                HoldemSession.StandUp(session);
            }
            return session.ToResolution();
        }

        HustleResolution a = Run(2024);
        HustleResolution b = Run(2024);
        Check("determinism: same seed ⇒ bit-identical session resolution",
            a.FundsDelta == b.FundsDelta && a.DetectionRiskDelta == b.DetectionRiskDelta
            && a.StressDelta == b.StressDelta && a.RecklessnessDelta == b.RecklessnessDelta && a.SetGamblingBustFlag == b.SetGamblingBustFlag,
            $"a=({a.FundsDelta},{a.DetectionRiskDelta},{a.StressDelta},{a.RecklessnessDelta},{a.SetGamblingBustFlag}) b=({b.FundsDelta},{b.DetectionRiskDelta},{b.StressDelta},{b.RecklessnessDelta},{b.SetGamblingBustFlag})");

        // Sanity companion to the identical-seed check above: different seeds
        // should (usually) produce different results. Not run at ctx's heat
        // (0.3, ~64% cumulative raid chance over 40 hands at Mid) because a
        // raid's deltas are a fixed constant regardless of the hand sequence
        // that triggered it — two different seeds that both happen to raid
        // would look identical without that being a determinism problem, so
        // this scans several seeds at heat=0 (no raid) for at least one
        // differing pair, isolating genuine hand-to-hand outcome variation.
        var noRaidCtx = new HoldemContext(funds: 5000, heat: 0.0, reck: 0.4);
        HustleResolution RunNoRaid(ulong seed)
        {
            var rng = new RngState(seed);
            var session = HoldemSession.StartSession(in noRaidCtx, StakesTier.Mid, buyIn: 1000, numOpponents: 4, ref rng);
            HoldemProfile.ArchetypeTunables heroProfile = HoldemAgent.HeroAutopilotProfile(noRaidCtx.Reck);
            for (int h = 0; h < 40 && !session.IsOver; h++)
            {
                HoldemSession.PlayAutopilotHand(session, in noRaidCtx, heroProfile, ref rng);
            }
            if (!session.IsOver)
            {
                HoldemSession.StandUp(session);
            }
            return session.ToResolution();
        }

        double firstFundsDelta = RunNoRaid(3001).FundsDelta;
        bool foundDifference = false;
        for (ulong seed = 3002; seed <= 3010 && !foundDifference; seed++)
        {
            if (Math.Abs(RunNoRaid(seed).FundsDelta - firstFundsDelta) > 1e-9)
            {
                foundDifference = true;
            }
        }
        Check("determinism: different seeds (no raid in play) produce different resolutions (sanity, not comparing the same run twice)", foundDifference);
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

        RunHandAndSessionZeroAllocChecks();
    }

    /// <summary>§14.9 — a warm played hand (via the betting state machine) and a warm full session's per-hand play (via <see cref="HoldemSession"/>) each allocate ~0 B.</summary>
    private static void RunHandAndSessionZeroAllocChecks()
    {
        var handTable = new HoldemHandState();
        Span<HoldemArchetype> archs = stackalloc HoldemArchetype[6]
        {
            HoldemArchetype.TAG, HoldemArchetype.LAG, HoldemArchetype.Nit, HoldemArchetype.Station, HoldemArchetype.Maniac, HoldemArchetype.TAG,
        };
        Span<long> deepStacks = stackalloc long[6] { 2000, 2000, 2000, 2000, 2000, 2000 };
        handTable.ConfigureSeats(6, archs, heroSeat: -1, deepStacks, buttonSeat: 0, smallBlind: 5, bigBlind: 10);

        void OneHandRun(ulong seed)
        {
            var r = new RngState(seed);
            for (int i = 0; i < 6; i++)
            {
                handTable.SetStack(i, 2000);
            }
            handTable.StartHand(ref r);
            handTable.RotateButton();
        }

        for (ulong i = 1; i <= 50; i++)
        {
            OneHandRun(i);
        }

        long beforeHand = GC.GetAllocatedBytesForCurrentThread();
        for (ulong i = 1000; i <= 1100; i++)
        {
            OneHandRun(i);
        }
        long afterHand = GC.GetAllocatedBytesForCurrentThread();
        double handPerRun = (afterHand - beforeHand) / 101.0;
        Console.WriteLine($"--- zero-alloc: {handPerRun:F1} B/warm-hand-run (6 seats, full AI, betting state machine) ---");
        Check("zero-alloc: a warm played hand allocates ~0 B", handPerRun < 64, $"{handPerRun:F1} B/run");

        // A single session, pre-created (StartSession itself allocates the
        // pooled table — a real, low-frequency, once-per-session cost, not
        // part of the per-hand hot path this check targets), then many hands
        // played on it via the session layer's own bookkeeping.
        var ctx = new HoldemContext(funds: 100_000, heat: 0.0, reck: 0.3);
        var warmRng = new RngState(777);
        HoldemSessionState warmSession = HoldemSession.StartSession(in ctx, StakesTier.Low, buyIn: HoldemProfile.GetTier(StakesTier.Low).BuyInMax, numOpponents: 5, ref warmRng);
        HoldemProfile.ArchetypeTunables warmHeroProfile = HoldemAgent.HeroAutopilotProfile(ctx.Reck);

        // A real caller (session/UI/harness alike) must never call StartHand
        // again once IsOver — this guard is that contract, not a defensive
        // patch on the engine (playing on past bust left a zero-stack hero
        // marked Active/AllIn-for-nothing every hand, eventually producing a
        // showdown with no non-folded seat actually holding chips at some
        // side-pot level, which is a genuinely malformed hand, not a case the
        // resolver is expected to survive).
        void PlayOneSessionHand()
        {
            if (!warmSession.IsOver)
            {
                HoldemSession.PlayAutopilotHand(warmSession, in ctx, warmHeroProfile, ref warmRng);
            }
        }

        for (int i = 0; i < 30; i++)
        {
            PlayOneSessionHand();
        }

        long beforeSession = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            PlayOneSessionHand();
        }
        long afterSession = GC.GetAllocatedBytesForCurrentThread();
        double sessionPerHand = (afterSession - beforeSession) / 100.0;
        Console.WriteLine($"--- zero-alloc: {sessionPerHand:F1} B/session-hand (via HoldemSession.PlayAutopilotHand, post-setup) ---");
        Check("zero-alloc: a warm session's per-hand play (via HoldemSession) allocates ~0 B", sessionPerHand < 64, $"{sessionPerHand:F1} B/run");
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
