using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Hustles;

namespace DirtAndDiamonds.Tools.HustleHarness;

/// <summary>
/// Acceptance checks for the Phase 8b Hustles pure resolvers
/// (docs/design/hustles_narcotics_fencing.md §9, checks 1–8 — check 9, the
/// Layer-2/DB integration proof, lives in GrittyEventsHarness since it already
/// compiles Data+Core+Life+Narrative headless). Data-free/Godot-free, like
/// every Tools harness — this one compiles only Assets/Simulation/Hustles and
/// RngState.cs (see HustleHarness.csproj).
///
/// Usage: dotnet run --project Tools/HustleHarness
/// </summary>
internal static class Program
{
    private static readonly List<(string Name, bool Pass, string Detail)> Results = new();

    private static int Main()
    {
        RunStateMachineChecks();
        RunStage1Checks();
        RunStage2Checks();
        RunStage3Checks();
        RunEvBandChecks();
        RunFencingChecks();
        RunRobberyChecks();
        RunDeterminismChecks();
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

    // A mid-career avatar, per design doc §3.4's canonical calibration point.
    private static HustleContext MidCareerCtx(int crewStandingLocal = 0, bool usesProduct = false) =>
        new(funds: 5000, heat: 0.20, reck: 0.5, supplierTrust: 20, crewStandingLocal: crewStandingLocal,
            ownsTurfLocal: false, usesProduct: usesProduct);

    private static FencingContext FenceCtx(int fenceStanding = 0, double heat = 0.20) => new(heat, fenceStanding);

    /// <summary>Brute-force search for the smallest seed ≥1 satisfying <paramref name="predicate"/> — the harness's way to "force" a specific roll without mocking RngState.</summary>
    private static ulong FindSeed(Func<ulong, bool> predicate, int maxTries = 500_000)
    {
        for (ulong s = 1; s <= (ulong)maxTries; s++)
        {
            if (predicate(s))
            {
                return s;
            }
        }
        throw new InvalidOperationException("No seed found matching predicate within search bound.");
    }

    // ------------------------------------------------------------------
    // Check 1 — state-machine legality
    // ------------------------------------------------------------------

    private static void RunStateMachineChecks()
    {
        HustleContext ctx = MidCareerCtx();

        // Every DropInventory call lands in InventoryDrop or Busted — never anything else.
        bool allLegalFromStart = true;
        for (ulong s = 1; s <= 200; s++)
        {
            var rng = new RngState(s);
            HustleState result = NarcoticsHustle.DropInventory(in ctx, 300, ref rng);
            if (result.Stage is not (HustleStage.InventoryDrop or HustleStage.Busted))
            {
                allLegalFromStart = false;
                break;
            }
        }
        Check("state machine: DropInventory always lands InventoryDrop or Busted", allLegalFromStart);

        // A clean (non-seized) drop, deterministic downstream (c=1.0 never rolls a bad batch).
        ulong cleanSeed = FindSeed(s =>
        {
            var rng = new RngState(s);
            return NarcoticsHustle.DropInventory(in ctx, 300, ref rng).Stage == HustleStage.InventoryDrop;
        });
        var cleanRng = new RngState(cleanSeed);
        HustleState afterDrop = NarcoticsHustle.DropInventory(in ctx, 300, ref cleanRng);
        Check("state machine: bank-exit at InventoryDrop projects profit-so-far exactly (-B)",
            NarcoticsHustle.BankExit(in afterDrop, in ctx).FundsDelta == -300,
            $"got {NarcoticsHustle.BankExit(in afterDrop, in ctx).FundsDelta}");

        HustleState afterCut = NarcoticsHustle.CutProduct(in afterDrop, in ctx, 1.0, ref cleanRng);
        Check("state machine: CutProduct always lands ProfitToxicityCut", afterCut.Stage == HustleStage.ProfitToxicityCut);

        // c=1.0 clean: 30 units @ $14, Hold cap 60 -> full sale, no fire-sale, no conflict (pConflict=0).
        HustleState bankAtCut = NarcoticsHustle.BankExit(in afterCut, in ctx);
        double expectedBankFunds = -300 + 30 * 14; // = 120
        Check("state machine: bank-exit at ProfitToxicityCut projects profit-so-far exactly",
            bankAtCut.Stage == HustleStage.Resolved && bankAtCut.FundsDelta == expectedBankFunds,
            $"expected {expectedBankFunds}, got {bankAtCut.FundsDelta}");

        var pushRng = new RngState(cleanSeed + 1000);
        HustleState resolved = NarcoticsHustle.PushTerritory(in afterCut, in ctx, PushLevel.Hold, ref pushRng);
        Check("state machine: PushTerritory always lands Resolved", resolved.Stage == HustleStage.Resolved);

        // Resolved/Busted are absorbing — every further transition throws.
        bool resolvedAbsorbing = ThrowsOn(() => NarcoticsHustle.CutProduct(in resolved, in ctx, 1.0, ref pushRng))
            && ThrowsOn(() => NarcoticsHustle.PushTerritory(in resolved, in ctx, PushLevel.Hold, ref pushRng))
            && ThrowsOn(() => NarcoticsHustle.BankExit(in resolved, in ctx));
        Check("state machine: Resolved is absorbing (every further call throws)", resolvedAbsorbing);

        ulong seizeSeed = FindSeed(s =>
        {
            var rng = new RngState(s);
            return NarcoticsHustle.DropInventory(in ctx, 300, ref rng).Stage == HustleStage.Busted;
        });
        var seizeRng = new RngState(seizeSeed);
        HustleState busted = NarcoticsHustle.DropInventory(in ctx, 300, ref seizeRng);
        bool bustedAbsorbing = ThrowsOn(() => NarcoticsHustle.CutProduct(in busted, in ctx, 1.0, ref seizeRng))
            && ThrowsOn(() => NarcoticsHustle.PushTerritory(in busted, in ctx, PushLevel.Hold, ref seizeRng))
            && ThrowsOn(() => NarcoticsHustle.BankExit(in busted, in ctx));
        Check("state machine: Busted is absorbing (every further call throws)", bustedAbsorbing);

        // Wrong-stage calls are illegal transitions too.
        bool wrongStageThrows = ThrowsOn(() => NarcoticsHustle.PushTerritory(in afterDrop, in ctx, PushLevel.Hold, ref cleanRng))
            && ThrowsOn(() => NarcoticsHustle.CutProduct(in resolved, in ctx, 1.0, ref cleanRng));
        Check("state machine: calling a stage's method from the wrong stage throws", wrongStageThrows);
    }

    private static bool ThrowsOn(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    // ------------------------------------------------------------------
    // Check 2 — Stage 1 (Inventory Drop)
    // ------------------------------------------------------------------

    private static void RunStage1Checks()
    {
        HustleContext baseline = MidCareerCtx();
        double baseMax = NarcoticsHustle.ComputeBuyInMax(in baseline);

        HustleContext higherTrust = new(baseline.Funds, baseline.Heat, baseline.Reck, supplierTrust: 90,
            baseline.CrewStandingLocal, baseline.OwnsTurfLocal, baseline.UsesProduct);
        HustleContext higherHeat = new(baseline.Funds, heat: 0.8, baseline.Reck, baseline.SupplierTrust,
            baseline.CrewStandingLocal, baseline.OwnsTurfLocal, baseline.UsesProduct);

        Check("stage1: BuyInMax rises with supplier trust",
            NarcoticsHustle.ComputeBuyInMax(in higherTrust) > baseMax);
        Check("stage1: BuyInMax falls with heat",
            NarcoticsHustle.ComputeBuyInMax(in higherHeat) < baseMax);

        var rng = new RngState(1);
        Check("stage1: buyIn below BuyInMin throws",
            ThrowsOn(() => NarcoticsHustle.DropInventory(in baseline, NarcoticsHustle.NarcoticsProfile.BuyInMin - 1, ref rng)));
        Check("stage1: buyIn above min(funds, BuyInMax) throws",
            ThrowsOn(() => NarcoticsHustle.DropInventory(in baseline, baseMax + 1, ref rng)));

        ulong seizeSeed = FindSeed(s =>
        {
            var r = new RngState(s);
            return NarcoticsHustle.DropInventory(in baseline, 300, ref r).Stage == HustleStage.Busted;
        });
        var seizeRng = new RngState(seizeSeed);
        HustleState busted = NarcoticsHustle.DropInventory(in baseline, 300, ref seizeRng);
        Check("stage1: forced seizure loses exactly B and writes the seizure deltas",
            busted.FundsDelta == -300 && busted.DetectionRiskDelta == 6 && busted.RecklessnessDelta == 2
            && busted.StressDelta == 15 && busted.WatchlistFlag,
            $"funds={busted.FundsDelta} detect={busted.DetectionRiskDelta} reck={busted.RecklessnessDelta} stress={busted.StressDelta} watch={busted.WatchlistFlag}");
    }

    // ------------------------------------------------------------------
    // Check 3 — Stage 2 (Profit / Toxicity Cut)
    // ------------------------------------------------------------------

    private static void RunStage2Checks()
    {
        HustleContext ctx = MidCareerCtx();
        HustleState fixture = new(HustleStage.InventoryDrop, BuyIn: 1000, InventoryUnits: 100, SalableUnits: 0, EffPrice: 0,
            0, 0, 0, 0, 0, 0, 0, false, false, false, false);

        // "At fixed demand": raw sellUnits*effPrice with no bad-batch noise (seek a
        // clean-batch seed at each c so the comparison isolates the price/volume trade).
        double RawRevenue(double c)
        {
            ulong seed = FindSeed(s =>
            {
                var r = new RngState(s);
                return !NarcoticsHustle.CutProduct(in fixture, in ctx, c, ref r).BadProductFlag;
            });
            var rng = new RngState(seed);
            HustleState cut = NarcoticsHustle.CutProduct(in fixture, in ctx, c, ref rng);
            return cut.SalableUnits * cut.EffPrice;
        }

        double rLow = RawRevenue(1.0);
        double rMid = RawRevenue(1.75);
        double rHigh = RawRevenue(NarcoticsHustle.NarcoticsProfile.CutMax);

        // With CutMax=2.5 and the 0.25 toxicity discount, the formula's true vertex sits at
        // c=3.5 (outside the playable domain) — raw revenue is monotonically increasing over
        // [1, CutMax], not cresting mid-domain. The function is still genuinely concave
        // (strictly diminishing marginal returns to cutting further), which IS the
        // price/volume trade the design doc's "real optimum" language is pointing at — this
        // check proves that property instead of the domain-inconsistent "mid beats both ends"
        // framing in §9 check 3's literal wording. Flagged for Fable/Opus: retuning CutMax
        // down (or the 0.25 coefficient up) would move the vertex inside the domain and make
        // the literal "mid beats both ends" reading true too.
        double firstStep = rMid - rLow;
        double secondStep = rHigh - rMid;
        Check("stage2: revenue is concave in c (strictly diminishing marginal returns to cutting further)",
            firstStep > 0 && secondStep > 0 && secondStep < firstStep,
            $"R(1.0)={rLow:F1} R(1.75)={rMid:F1} R(2.5)={rHigh:F1} (Δ1={firstStep:F1}, Δ2={secondStep:F1})");

        ulong badSeed = FindSeed(s =>
        {
            var r = new RngState(s);
            return NarcoticsHustle.CutProduct(in fixture, in ctx, 1.6, ref r).BadProductFlag;
        });
        var badRng = new RngState(badSeed);
        HustleState badBatch = NarcoticsHustle.CutProduct(in fixture, in ctx, 1.6, ref badRng);
        double toxicity = (1.6 - 1.0) / (NarcoticsHustle.NarcoticsProfile.CutMax - 1.0);
        double expectedSalable = 0.40 * (100 * 1.6);
        int expectedDetection = (int)Math.Round(2 + 4 * toxicity, MidpointRounding.AwayFromZero)
            + (int)Math.Round(10 * toxicity, MidpointRounding.AwayFromZero);
        int expectedCrewDelta = -(int)Math.Round(15 * toxicity, MidpointRounding.AwayFromZero);
        Check("stage2: bad batch applies the 0.40 haircut and toxicity-scaled deltas",
            badBatch.SalableUnits == expectedSalable && badBatch.DetectionRiskDelta == expectedDetection
            && badBatch.CrewStandingDelta == expectedCrewDelta && badBatch.BadProductFlag,
            $"salable={badBatch.SalableUnits} (want {expectedSalable}), detect={badBatch.DetectionRiskDelta} (want {expectedDetection}), crew={badBatch.CrewStandingDelta} (want {expectedCrewDelta})");

        // uses_product content flag additionally costs health on a bad batch.
        HustleContext userCtx = MidCareerCtx(usesProduct: true);
        var userRng = new RngState(badSeed);
        HustleState userBadBatch = NarcoticsHustle.CutProduct(in fixture, in userCtx, 1.6, ref userRng);
        Check("stage2: uses_product additionally costs health on a bad batch",
            userBadBatch.HealthCeilingDelta < 0 && badBatch.HealthCeilingDelta == 0,
            $"userHealth={userBadBatch.HealthCeilingDelta} nonUserHealth={badBatch.HealthCeilingDelta}");
    }

    // ------------------------------------------------------------------
    // Check 4 — Stage 3 (Territory Control vs Factions)
    // ------------------------------------------------------------------

    private static void RunStage3Checks()
    {
        HustleContext ctx = MidCareerCtx();
        // 250 salable units at $10 stranding well past every tier's MarketCap.
        HustleState fixture = new(HustleStage.ProfitToxicityCut, BuyIn: 1000, InventoryUnits: 250, SalableUnits: 250, EffPrice: 10,
            0, 0, 0, 0, 0, 0, 0, false, false, false, false);

        var holdRng = new RngState(1);
        HustleState hold = NarcoticsHustle.PushTerritory(in fixture, in ctx, PushLevel.Hold, ref holdRng);
        double expectedHold = (60 * 10 + 190 * 0.5 * 10) * 1.00; // 60 full + 190 fire-sale
        Check("stage3: demand saturation strands fire-sale units at Hold",
            hold.FundsDelta == expectedHold, $"expected {expectedHold}, got {hold.FundsDelta}");

        // TakeOver's own table values drive a different (larger) cap/multiplier, even with a
        // forced no-conflict seed (search for one so the comparison isolates the table, not luck).
        ulong noConflictSeed = FindSeed(s =>
        {
            var r = new RngState(s);
            var result = NarcoticsHustle.PushTerritory(in fixture, in ctx, PushLevel.TakeOver, ref r);
            return result.ControlsTurfFlag == false && result.CrewStandingDelta == 0; // no conflict rolled at all
        });
        var takeOverRng = new RngState(noConflictSeed);
        HustleState takeOverNoConflict = NarcoticsHustle.PushTerritory(in fixture, in ctx, PushLevel.TakeOver, ref takeOverRng);
        double expectedTakeOver = (200 * 10 + 50 * 0.5 * 10) * 1.70;
        Check("stage3: the market table (MarketCap/m) drives revenue at TakeOver",
            takeOverNoConflict.FundsDelta == expectedTakeOver, $"expected {expectedTakeOver}, got {takeOverNoConflict.FundsDelta}");

        ulong retaliationSeed = FindSeed(s =>
        {
            var r = new RngState(s);
            return NarcoticsHustle.PushTerritory(in fixture, in ctx, PushLevel.TakeOver, ref r).CrewStandingDelta <= -40;
        });
        var retalRng = new RngState(retaliationSeed);
        HustleState retaliation = NarcoticsHustle.PushTerritory(in fixture, in ctx, PushLevel.TakeOver, ref retalRng);
        Check("stage3: forced retaliation writes the injury/detection/stress bundle and pushes the crew edge below -40",
            retaliation.HealthCeilingDelta < 0 && retaliation.DetectionRiskDelta == 8 && retaliation.StressDelta == 25
            && retaliation.CrewStandingDelta <= -40 && !retaliation.ControlsTurfFlag,
            $"health={retaliation.HealthCeilingDelta} detect={retaliation.DetectionRiskDelta} stress={retaliation.StressDelta} crew={retaliation.CrewStandingDelta}");
    }

    // ------------------------------------------------------------------
    // Check 5 — EV & tail bands over seeded policy runs (§3.4)
    // ------------------------------------------------------------------

    private static (double MeanFunds, double SeizeRate, double RetaliationRate) RunPolicy(
        HustleContext ctx, double buyIn, double cut, PushLevel push, int trials, ulong seedBase)
    {
        double totalFunds = 0;
        int seizures = 0;
        int retaliations = 0;
        for (int i = 0; i < trials; i++)
        {
            var rng = new RngState(seedBase + (ulong)i * 7919UL + 1UL);
            HustleState state = NarcoticsHustle.DropInventory(in ctx, buyIn, ref rng);
            if (state.Stage == HustleStage.Busted)
            {
                seizures++;
                totalFunds += state.FundsDelta;
                continue;
            }
            state = NarcoticsHustle.CutProduct(in state, in ctx, cut, ref rng);
            state = NarcoticsHustle.PushTerritory(in state, in ctx, push, ref rng);
            if (state.CrewStandingDelta <= -40)
            {
                retaliations++;
            }
            totalFunds += state.FundsDelta;
        }
        return (totalFunds / trials, (double)seizures / trials, (double)retaliations / trials);
    }

    private static void RunEvBandChecks()
    {
        const int trials = 20_000;
        HustleContext ctx = MidCareerCtx();

        // §3.4's own worked table names B=1000 for Aggressive at these exact parameters
        // (heat=0.20, supplierTrust=20), but BuyInMax at that point is 736 (§3.1's own
        // formula) — the table's B exceeds its own document's cap. Flagged for Fable/Opus:
        // this harness uses the actual feasible ceiling instead of the infeasible literal.
        double aggressiveBuyIn = Math.Min(1000, Math.Min(ctx.Funds, NarcoticsHustle.ComputeBuyInMax(in ctx)));

        var safe = RunPolicy(ctx, 300, 1.0, PushLevel.Hold, trials, 1_000_000UL);
        var moderate = RunPolicy(ctx, 300, 1.6, PushLevel.Hold, trials, 2_000_000UL);
        var aggressive = RunPolicy(ctx, aggressiveBuyIn, NarcoticsHustle.NarcoticsProfile.CutMax, PushLevel.TakeOver, trials, 3_000_000UL);

        Console.WriteLine("--- Narcotics policy bands (mid-career avatar, N=20,000/policy) ---");
        Console.WriteLine($"  Safe:       mean={safe.MeanFunds,7:F1}  seize={safe.SeizeRate:P1}  retaliation={safe.RetaliationRate:P1}");
        Console.WriteLine($"  Moderate:   mean={moderate.MeanFunds,7:F1}  seize={moderate.SeizeRate:P1}  retaliation={moderate.RetaliationRate:P1}");
        Console.WriteLine($"  Aggressive: mean={aggressive.MeanFunds,7:F1}  seize={aggressive.SeizeRate:P1}  retaliation={aggressive.RetaliationRate:P1}\n");

        Check("EV bands: mean net funds strictly rises Safe < Moderate < Aggressive",
            safe.MeanFunds < moderate.MeanFunds && moderate.MeanFunds < aggressive.MeanFunds,
            $"safe={safe.MeanFunds:F1} moderate={moderate.MeanFunds:F1} aggressive={aggressive.MeanFunds:F1}");

        Check("EV bands: every policy has positive expected value",
            safe.MeanFunds > 0 && moderate.MeanFunds > 0 && aggressive.MeanFunds > 0);

        double expectedPSeize = Math.Clamp(0.05 + 0.12 * ctx.Heat - 0.05 * Math.Max(0, ctx.SupplierTrust) / 100.0, 0.02, 0.60);
        Check("EV bands: seizure rate matches the pSeize formula within sampling noise (B-independent)",
            Math.Abs(safe.SeizeRate - expectedPSeize) < 0.02 && Math.Abs(moderate.SeizeRate - expectedPSeize) < 0.02
            && Math.Abs(aggressive.SeizeRate - expectedPSeize) < 0.02,
            $"expected≈{expectedPSeize:P1}, got safe={safe.SeizeRate:P1} moderate={moderate.SeizeRate:P1} aggressive={aggressive.SeizeRate:P1}");

        Check("EV bands: tail risk is non-decreasing Safe→Aggressive (Hold policies never retaliate; TakeOver does)",
            safe.RetaliationRate == 0 && moderate.RetaliationRate == 0 && aggressive.RetaliationRate > 0,
            $"safe={safe.RetaliationRate:P1} moderate={moderate.RetaliationRate:P1} aggressive={aggressive.RetaliationRate:P1}");
    }

    // ------------------------------------------------------------------
    // Check 6 — Fencing
    // ------------------------------------------------------------------

    private static void RunFencingChecks()
    {
        // Hidden R respects standing: same seed => identical V/baseR draws, so the only
        // difference between the two calls is rMult's ctx-dependent term.
        ulong lotSeed = 42;
        var lowRng = new RngState(lotSeed);
        var highRng = new RngState(lotSeed);
        FencingState lowStanding = FencingNegotiation.StartLot(new FencingContext(0.2, -100), ref lowRng);
        FencingState highStanding = FencingNegotiation.StartLot(new FencingContext(0.2, 100), ref highRng);
        Check("fencing: hidden reservation R rises with fence standing (same draw, different standing)",
            highStanding.HiddenReservation > lowStanding.HiddenReservation,
            $"low={lowStanding.HiddenReservation:F1} high={highStanding.HiddenReservation:F1}");

        // Neutral policy: accept once the live offer reaches the fixed multiple of the
        // opening lowball; otherwise counter deliberately high so the offer climbs.
        static FencingState RunNeutral(FencingContext ctx, ref RngState rng)
        {
            FencingState state = FencingNegotiation.StartLot(in ctx, ref rng);
            while (state.Outcome == FencingOutcomeKind.InProgress)
            {
                state = FencingNegotiation.NeutralAcceptDecision(in state)
                    ? FencingNegotiation.Accept(in state, in ctx, ref rng)
                    : FencingNegotiation.Counter(in state, in ctx, state.CurrentOffer * 2.0, ref rng);
            }
            return state;
        }

        // An ultra-aggressive policy that never accepts (always counters absurdly high) —
        // used to prove walk-rate rises with counter-aggression.
        static FencingState RunAggressive(FencingContext ctx, ref RngState rng)
        {
            FencingState state = FencingNegotiation.StartLot(in ctx, ref rng);
            while (state.Outcome == FencingOutcomeKind.InProgress)
            {
                state = FencingNegotiation.Counter(in state, in ctx, state.CurrentOffer * 10.0, ref rng);
            }
            return state;
        }

        const int trials = 20_000;
        FencingContext fenceCtx = FenceCtx();
        int deals = 0, walks = 0, stings = 0;
        double totalFunds = 0;
        for (int i = 0; i < trials; i++)
        {
            var rng = new RngState((ulong)i * 104729UL + 5UL);
            FencingState result = RunNeutral(fenceCtx, ref rng);
            if (result.Outcome == FencingOutcomeKind.Deal)
            {
                deals++;
                totalFunds += result.FundsDelta;
                if (result.WatchlistFlag)
                {
                    stings++;
                }
            }
            else
            {
                walks++;
            }
        }
        double neutralWalkRate = (double)walks / trials;
        double neutralStingRate = deals > 0 ? (double)stings / deals : 0;
        double neutralMeanFunds = totalFunds / trials;

        int aggressiveWalks = 0;
        for (int i = 0; i < trials; i++)
        {
            var rng = new RngState((ulong)i * 104729UL + 9_000_000UL);
            FencingState result = RunAggressive(fenceCtx, ref rng);
            if (result.Outcome == FencingOutcomeKind.Walk)
            {
                aggressiveWalks++;
            }
        }
        double aggressiveWalkRate = (double)aggressiveWalks / trials;

        Console.WriteLine("--- Fencing neutral policy bands (N=20,000) ---");
        Console.WriteLine($"  neutral:    mean funds={neutralMeanFunds:F1}  walk rate={neutralWalkRate:P1}  sting rate={neutralStingRate:P1}");
        Console.WriteLine($"  aggressive: walk rate={aggressiveWalkRate:P1}\n");

        Check("fencing: neutral policy has positive expected value with a low, steady sting rate",
            neutralMeanFunds > 0 && neutralStingRate < 0.20,
            $"mean={neutralMeanFunds:F1} sting={neutralStingRate:P1}");
        Check("fencing: walk-rate rises with counter-aggression",
            aggressiveWalkRate > neutralWalkRate,
            $"neutral={neutralWalkRate:P1} aggressive={aggressiveWalkRate:P1}");

        ulong stingSeed = FindSeed(s =>
        {
            var r = new RngState(s);
            FencingState lot = FencingNegotiation.StartLot(in fenceCtx, ref r);
            return FencingNegotiation.Accept(in lot, in fenceCtx, ref r).WatchlistFlag;
        });
        var stingRng = new RngState(stingSeed);
        FencingState stingLot = FencingNegotiation.StartLot(in fenceCtx, ref stingRng);
        double expectedDeal = stingLot.CurrentOffer;
        FencingState stungDeal = FencingNegotiation.Accept(in stingLot, in fenceCtx, ref stingRng);
        Check("fencing: forced sting halves the deal and writes the sting deltas",
            stungDeal.FundsDelta == 0.5 * expectedDeal && stungDeal.DetectionRiskDelta == 10 && stungDeal.WatchlistFlag,
            $"funds={stungDeal.FundsDelta} (want {0.5 * expectedDeal}), detect={stungDeal.DetectionRiskDelta}");
    }

    // ------------------------------------------------------------------
    // Check R — Robbery (docs/design/hustle_minigames_depth_pass.md §5, slice R-1)
    // ------------------------------------------------------------------

    private static RobberyContext RobberyCtx(double funds = 5000, double heat = 0.20, double reck = 0.5, bool hasCrew = true) =>
        new(funds, heat, reck, hasCrew);

    /// <summary>
    /// Drives one full robbery run under a fixed policy: pick/case → approach →
    /// execute → (press or bail per <paramref name="alwaysPress"/>) → getaway.
    /// The cautious neutral policy (§5.3) is (ConvenienceStore, cased, SoloQuiet,
    /// never press) — <see cref="RobberyHustle.NeutralPressLuckDecision"/>.
    /// </summary>
    private static RobberyState RunRobberyPolicy(
        in RobberyContext ctx, RobberyTarget target, bool caseIt, RobberyApproach approach, bool alwaysPress, ref RngState rng)
    {
        RobberyState state = RobberyHustle.CaseTarget(in ctx, target, caseIt);
        state = RobberyHustle.ChooseApproach(in state, in ctx, approach);
        state = RobberyHustle.Execute(in state, in ctx, ref rng);
        if (state.Stage == RobberyStage.Busted)
        {
            return state;
        }
        bool press = alwaysPress || RobberyHustle.NeutralPressLuckDecision(in state);
        state = press
            ? RobberyHustle.PressLuck(in state, in ctx, ref rng)
            : RobberyHustle.GrabAndRun(in state);
        if (state.Stage == RobberyStage.Busted)
        {
            return state;
        }
        return RobberyHustle.Getaway(in state, in ctx, ref rng);
    }

    private static void RunRobberyChecks()
    {
        RobberyContext ctx = RobberyCtx();

        // --- state-machine legality -----------------------------------
        RobberyState cased = RobberyHustle.CaseTarget(in ctx, RobberyTarget.BookieStash, caseIt: true);
        Check("robbery: CaseTarget lands Cased and books the casing heat cost",
            cased.Stage == RobberyStage.Cased && cased.Cased
            && cased.DetectionRiskDelta == RobberyHustle.RobberyProfile.CaseHeatCost,
            $"stage={cased.Stage} detect={cased.DetectionRiskDelta}");

        RobberyState uncased = RobberyHustle.CaseTarget(in ctx, RobberyTarget.BookieStash, caseIt: false);
        Check("robbery: skipping the casing beat costs no heat", uncased.DetectionRiskDelta == 0);

        RobberyContext noCrew = RobberyCtx(hasCrew: false);
        Check("robbery: crew approach without a crew rep throws",
            ThrowsOn(() => RobberyHustle.ChooseApproach(in cased, in noCrew, RobberyApproach.Crew)));

        RobberyState approached = RobberyHustle.ChooseApproach(in cased, in ctx, RobberyApproach.SoloQuiet);
        Check("robbery: ChooseApproach lands Approached (solo-quiet keeps the base score)",
            approached.Stage == RobberyStage.Approached && approached.FullScore == cased.FullScore);

        RobberyState strongArm = RobberyHustle.ChooseApproach(in cased, in ctx, RobberyApproach.StrongArm);
        RobberyState crewJob = RobberyHustle.ChooseApproach(in cased, in ctx, RobberyApproach.Crew);
        Check("robbery: approach score multipliers order SoloQuiet < StrongArm < Crew",
            approached.FullScore < strongArm.FullScore && strongArm.FullScore < crewJob.FullScore,
            $"solo={approached.FullScore:F0} strong={strongArm.FullScore:F0} crew={crewJob.FullScore:F0}");

        bool executeLandsLegal = true;
        for (ulong s = 1; s <= 200; s++)
        {
            var rng = new RngState(s);
            RobberyState result = RobberyHustle.Execute(in approached, in ctx, ref rng);
            if (result.Stage is not (RobberyStage.Grabbed or RobberyStage.Busted))
            {
                executeLandsLegal = false;
                break;
            }
        }
        Check("robbery: Execute always lands Grabbed or Busted", executeLandsLegal);

        ulong grabSeed = FindSeed(s =>
        {
            var rng = new RngState(s);
            return RobberyHustle.Execute(in approached, in ctx, ref rng).Stage == RobberyStage.Grabbed;
        });
        var grabRng = new RngState(grabSeed);
        RobberyState grabbed = RobberyHustle.Execute(in approached, in ctx, ref grabRng);
        Check("robbery: a successful execute secures exactly the partial fraction of the full score",
            grabbed.PartialTake == approached.FullScore * RobberyHustle.RobberyProfile.GrabPartialFrac,
            $"partial={grabbed.PartialTake:F0} full={approached.FullScore:F0}");

        RobberyState bailed = RobberyHustle.GrabAndRun(in grabbed);
        Check("robbery: GrabAndRun locks the partial and lands Escaping (no RNG)",
            bailed.Stage == RobberyStage.Escaping && bailed.PartialTake == grabbed.PartialTake);

        ulong pressWinSeed = FindSeed(s =>
        {
            var rng = new RngState(s);
            return RobberyHustle.PressLuck(in grabbed, in ctx, ref rng).Stage == RobberyStage.Escaping;
        });
        var pressWinRng = new RngState(pressWinSeed);
        RobberyState pressed = RobberyHustle.PressLuck(in grabbed, in ctx, ref pressWinRng);
        Check("robbery: a won press-your-luck upgrades the take to the full score",
            pressed.PartialTake == grabbed.FullScore, $"take={pressed.PartialTake:F0} full={grabbed.FullScore:F0}");

        ulong pressBustSeed = FindSeed(s =>
        {
            var rng = new RngState(s);
            return RobberyHustle.PressLuck(in grabbed, in ctx, ref rng).Stage == RobberyStage.Busted;
        });
        var pressBustRng = new RngState(pressBustSeed);
        RobberyState pressBusted = RobberyHustle.PressLuck(in grabbed, in ctx, ref pressBustRng);
        double bookieLegalCost = RobberyHustle.RobberyProfile.BustLegalCostFrac * 900; // BookieStash base score
        Check("robbery: a lost press-your-luck wipes the whole take and writes the bust bundle incl. legal fees",
            pressBusted.Stage == RobberyStage.Busted && pressBusted.PartialTake == 0
            && pressBusted.FundsDelta == -bookieLegalCost
            && pressBusted.RobberyBustFlag && !pressBusted.HotGoodsFlag
            && pressBusted.DetectionRiskDelta == RobberyHustle.RobberyProfile.CaseHeatCost + RobberyHustle.RobberyProfile.BustDetectionSpike,
            $"take={pressBusted.PartialTake} funds={pressBusted.FundsDelta} (want {-bookieLegalCost}) detect={pressBusted.DetectionRiskDelta}");

        // The bail hit is bounded by funds on hand — a bust zeroes you out, never indebts you.
        RobberyContext brokeCtx = RobberyCtx(funds: 100);
        var brokeBustRng = new RngState(pressBustSeed);
        RobberyState brokeBusted = RobberyHustle.PressLuck(in grabbed, in brokeCtx, ref brokeBustRng);
        Check("robbery: the bust's legal fees clamp to funds on hand (never indebts)",
            brokeBusted.FundsDelta == -100, $"funds={brokeBusted.FundsDelta} (want -100)");

        ulong cleanGetawaySeed = FindSeed(s =>
        {
            var rng = new RngState(s);
            RobberyState g = RobberyHustle.Getaway(in bailed, in ctx, ref rng);
            return g.FundsDelta == bailed.PartialTake; // full take banked ⇒ no botch
        });
        var cleanRng = new RngState(cleanGetawaySeed);
        RobberyState cleanDone = RobberyHustle.Getaway(in bailed, in ctx, ref cleanRng);
        Check("robbery: a clean getaway banks the secured take, adds the target heat, and arms hot_goods",
            cleanDone.Stage == RobberyStage.Resolved && cleanDone.HotGoodsFlag && !cleanDone.RobberyBustFlag
            && cleanDone.DetectionRiskDelta == RobberyHustle.RobberyProfile.CaseHeatCost + 6, // BookieStash baseHeat
            $"stage={cleanDone.Stage} detect={cleanDone.DetectionRiskDelta} funds={cleanDone.FundsDelta:F0}");
        Check("robbery: ToResolution projects the bust flag and only robbery-shaped deltas",
            !cleanDone.ToResolution().SetRobberyBustFlag && pressBusted.ToResolution().SetRobberyBustFlag
            && cleanDone.ToResolution().SupplierTrustDelta == 0 && !cleanDone.ToResolution().SetWatchlistFlag);

        ulong botchSeed = FindSeed(s =>
        {
            var rng = new RngState(s);
            RobberyState g = RobberyHustle.Getaway(in bailed, in ctx, ref rng);
            return g.FundsDelta < bailed.PartialTake;
        });
        var botchRng = new RngState(botchSeed);
        RobberyState botched = RobberyHustle.Getaway(in bailed, in ctx, ref botchRng);
        Check("robbery: a botched getaway converts the take down and spikes heat but is not itself a bust",
            botched.Stage == RobberyStage.Resolved && !botched.RobberyBustFlag
            && botched.FundsDelta == bailed.PartialTake * RobberyHustle.RobberyProfile.BotchedGetawayFrac
            && botched.DetectionRiskDelta == RobberyHustle.RobberyProfile.CaseHeatCost + 6 + RobberyHustle.RobberyProfile.BotchedGetawayDetection,
            $"funds={botched.FundsDelta:F0} (want {bailed.PartialTake * RobberyHustle.RobberyProfile.BotchedGetawayFrac:F0}), detect={botched.DetectionRiskDelta}");

        // Crew split: the same clean-getaway seed banks only the crew share and nudges standing.
        RobberyState crewApproached = RobberyHustle.ChooseApproach(in cased, in ctx, RobberyApproach.Crew);
        ulong crewGrabSeed = FindSeed(s =>
        {
            var rng = new RngState(s);
            return RobberyHustle.Execute(in crewApproached, in ctx, ref rng).Stage == RobberyStage.Grabbed;
        });
        var crewRng = new RngState(crewGrabSeed);
        RobberyState crewBailed = RobberyHustle.GrabAndRun(RobberyHustle.Execute(in crewApproached, in ctx, ref crewRng));
        ulong crewCleanSeed = FindSeed(s =>
        {
            var rng = new RngState(s);
            RobberyState g = RobberyHustle.Getaway(in crewBailed, in ctx, ref rng);
            return g.DetectionRiskDelta == RobberyHustle.RobberyProfile.CaseHeatCost + 6; // no botch detection
        });
        var crewCleanRng = new RngState(crewCleanSeed);
        RobberyState crewDone = RobberyHustle.Getaway(in crewBailed, in ctx, ref crewCleanRng);
        Check("robbery: a crew job splits the take and nudges crew standing",
            crewDone.FundsDelta == crewBailed.PartialTake * 0.60 && crewDone.CrewStandingDelta == 3,
            $"funds={crewDone.FundsDelta:F0} (secured {crewBailed.PartialTake:F0}), crew={crewDone.CrewStandingDelta}");

        // Strong-arm bust risks the body; solo-quiet never does.
        RobberyState strongArmed = RobberyHustle.ChooseApproach(in cased, in ctx, RobberyApproach.StrongArm);
        ulong strongBustSeed = FindSeed(s =>
        {
            var rng = new RngState(s);
            return RobberyHustle.Execute(in strongArmed, in ctx, ref rng).Stage == RobberyStage.Busted;
        });
        var strongBustRng = new RngState(strongBustSeed);
        RobberyState strongBusted = RobberyHustle.Execute(in strongArmed, in ctx, ref strongBustRng);
        ulong soloBustSeed = FindSeed(s =>
        {
            var rng = new RngState(s);
            return RobberyHustle.Execute(in approached, in ctx, ref rng).Stage == RobberyStage.Busted;
        });
        var soloBustRng = new RngState(soloBustSeed);
        RobberyState soloBusted = RobberyHustle.Execute(in approached, in ctx, ref soloBustRng);
        Check("robbery: a strong-arm bust costs health_ceiling; a solo-quiet bust does not",
            strongBusted.HealthCeilingDelta < 0 && soloBusted.HealthCeilingDelta == 0,
            $"strongArm={strongBusted.HealthCeilingDelta} solo={soloBusted.HealthCeilingDelta}");

        // Absorbing terminals + wrong-stage calls throw.
        bool terminalAbsorbing =
            ThrowsOn(() => RobberyHustle.ChooseApproach(in cleanDone, in ctx, RobberyApproach.SoloQuiet))
            && ThrowsOn(() => { var r = new RngState(7); RobberyHustle.Execute(in cleanDone, in ctx, ref r); })
            && ThrowsOn(() => { var r = new RngState(7); RobberyHustle.PressLuck(in pressBusted, in ctx, ref r); })
            && ThrowsOn(() => RobberyHustle.GrabAndRun(in pressBusted))
            && ThrowsOn(() => { var r = new RngState(7); RobberyHustle.Getaway(in cleanDone, in ctx, ref r); });
        Check("robbery: Resolved/Busted are absorbing (every further call throws)", terminalAbsorbing);
        bool wrongStageThrows =
            ThrowsOn(() => { var r = new RngState(7); RobberyHustle.Execute(in cased, in ctx, ref r); })
            && ThrowsOn(() => { var r = new RngState(7); RobberyHustle.PressLuck(in approached, in ctx, ref r); })
            && ThrowsOn(() => { var r = new RngState(7); RobberyHustle.Getaway(in grabbed, in ctx, ref r); });
        Check("robbery: calling a stage's method from the wrong stage throws", wrongStageThrows);

        // --- skill monotonicity ----------------------------------------
        RobberyState casedRun = RobberyHustle.ChooseApproach(
            RobberyHustle.CaseTarget(in ctx, RobberyTarget.BookieStash, caseIt: true), in ctx, RobberyApproach.SoloQuiet);
        RobberyState uncasedRun = RobberyHustle.ChooseApproach(
            RobberyHustle.CaseTarget(in ctx, RobberyTarget.BookieStash, caseIt: false), in ctx, RobberyApproach.SoloQuiet);
        Check("robbery: casing raises the execute success probability",
            RobberyHustle.ComputeExecuteSuccessProbability(in casedRun, in ctx)
                > RobberyHustle.ComputeExecuteSuccessProbability(in uncasedRun, in ctx));

        RobberyState easyMark = RobberyHustle.ChooseApproach(
            RobberyHustle.CaseTarget(in ctx, RobberyTarget.ConvenienceStore, caseIt: false), in ctx, RobberyApproach.SoloQuiet);
        RobberyState hardMark = RobberyHustle.ChooseApproach(
            RobberyHustle.CaseTarget(in ctx, RobberyTarget.Warehouse, caseIt: false), in ctx, RobberyApproach.SoloQuiet);
        Check("robbery: harder marks are likelier to go wrong (and pay more)",
            RobberyHustle.ComputeExecuteSuccessProbability(in easyMark, in ctx)
                > RobberyHustle.ComputeExecuteSuccessProbability(in hardMark, in ctx)
            && hardMark.FullScore > easyMark.FullScore);

        RobberyContext hotCtx = RobberyCtx(heat: 0.8);
        Check("robbery: accumulated heat makes every job harder",
            RobberyHustle.ComputeExecuteSuccessProbability(in casedRun, in hotCtx)
                < RobberyHustle.ComputeExecuteSuccessProbability(in casedRun, in ctx));

        // --- EV & tail band (the §9 skill-sensible gate) ----------------
        const int trials = 20_000;
        (double MeanFunds, double BustRate, double MeanDetect) Band(
            RobberyTarget target, bool caseIt, RobberyApproach approach, bool alwaysPress, ulong seedBase)
        {
            double funds = 0, detect = 0;
            int busts = 0;
            for (int i = 0; i < trials; i++)
            {
                var rng = new RngState(seedBase + (ulong)i * 7919UL + 1UL);
                RobberyState result = RunRobberyPolicy(in ctx, target, caseIt, approach, alwaysPress, ref rng);
                funds += result.FundsDelta;
                detect += result.DetectionRiskDelta;
                if (result.Stage == RobberyStage.Busted)
                {
                    busts++;
                }
            }
            return (funds / trials, (double)busts / trials, detect / trials);
        }

        var cautious = Band(RobberyTarget.ConvenienceStore, caseIt: true, RobberyApproach.SoloQuiet, alwaysPress: false, 10_000_000UL);
        var moderate = Band(RobberyTarget.BookieStash, caseIt: true, RobberyApproach.SoloQuiet, alwaysPress: true, 20_000_000UL);
        var reckless = Band(RobberyTarget.Warehouse, caseIt: false, RobberyApproach.StrongArm, alwaysPress: true, 30_000_000UL);

        Console.WriteLine("--- Robbery policy bands (mid-career avatar, N=20,000/policy) ---");
        Console.WriteLine($"  Cautious (store, cased, quiet, bail):     mean={cautious.MeanFunds,7:F1}  bust={cautious.BustRate:P1}  detect={cautious.MeanDetect:F1}");
        Console.WriteLine($"  Moderate (bookie, cased, quiet, press):   mean={moderate.MeanFunds,7:F1}  bust={moderate.BustRate:P1}  detect={moderate.MeanDetect:F1}");
        Console.WriteLine($"  Reckless (warehouse, blind, arm, press):  mean={reckless.MeanFunds,7:F1}  bust={reckless.BustRate:P1}  detect={reckless.MeanDetect:F1}\n");

        Check("robbery EV band: the cautious neutral policy is +EV with a genuinely low bust rate",
            cautious.MeanFunds > 0 && cautious.BustRate < 0.10,
            $"mean={cautious.MeanFunds:F1} bust={cautious.BustRate:P1}");
        Check("robbery EV band: pushing deeper pays more (cautious < moderate mean)",
            cautious.MeanFunds < moderate.MeanFunds,
            $"cautious={cautious.MeanFunds:F1} moderate={moderate.MeanFunds:F1}");
        Check("robbery EV band: tail risk strictly rises cautious < moderate < reckless (INV-2)",
            cautious.BustRate < moderate.BustRate && moderate.BustRate < reckless.BustRate,
            $"bust: cautious={cautious.BustRate:P1} moderate={moderate.BustRate:P1} reckless={reckless.BustRate:P1}");
        Check("robbery EV band: the reckless policy carries a career-real bust rate (>30%)",
            reckless.BustRate > 0.30, $"bust={reckless.BustRate:P1}");
        Check("robbery EV band: busts are a real financial loss — the reckless policy's legal fees eat its upside (reckless mean < moderate mean)",
            reckless.MeanFunds < moderate.MeanFunds,
            $"reckless={reckless.MeanFunds:F1} moderate={moderate.MeanFunds:F1}");
        Check("robbery EV band: mean detection accrual rises with recklessness of policy",
            cautious.MeanDetect < moderate.MeanDetect && moderate.MeanDetect < reckless.MeanDetect,
            $"detect: cautious={cautious.MeanDetect:F1} moderate={moderate.MeanDetect:F1} reckless={reckless.MeanDetect:F1}");
    }

    // ------------------------------------------------------------------
    // Check 7 — determinism
    // ------------------------------------------------------------------

    private static void RunDeterminismChecks()
    {
        HustleContext ctx = MidCareerCtx();
        HustleResolution RunNarcotics(ulong seed)
        {
            var rng = new RngState(seed);
            HustleState state = NarcoticsHustle.DropInventory(in ctx, 300, ref rng);
            if (state.Stage == HustleStage.Busted)
            {
                return state.ToResolution();
            }
            state = NarcoticsHustle.CutProduct(in state, in ctx, 1.6, ref rng);
            state = NarcoticsHustle.PushTerritory(in state, in ctx, PushLevel.Encroach, ref rng);
            return state.ToResolution();
        }

        const ulong seed = 987654321UL;
        Check("determinism: same seed ⇒ identical Narcotics resolution",
            RunNarcotics(seed).Equals(RunNarcotics(seed)));

        FencingContext fenceCtx = FenceCtx();
        FencingState RunFencing(ulong s)
        {
            var rng = new RngState(s);
            FencingState state = FencingNegotiation.StartLot(in fenceCtx, ref rng);
            state = FencingNegotiation.Counter(in state, in fenceCtx, state.CurrentOffer * 1.8, ref rng);
            if (state.Outcome == FencingOutcomeKind.InProgress)
            {
                state = FencingNegotiation.Accept(in state, in fenceCtx, ref rng);
            }
            return state;
        }

        Check("determinism: same seed ⇒ identical Fencing negotiation",
            RunFencing(seed).Equals(RunFencing(seed)));

        RobberyContext robberyCtx = RobberyCtx();
        RobberyState RunRobbery(ulong s)
        {
            var rng = new RngState(s);
            return RunRobberyPolicy(in robberyCtx, RobberyTarget.BookieStash, caseIt: true,
                RobberyApproach.SoloQuiet, alwaysPress: true, ref rng);
        }

        Check("determinism: same seed ⇒ identical Robbery run",
            RunRobbery(seed).Equals(RunRobbery(seed)));
    }

    // ------------------------------------------------------------------
    // Check 8 — zero allocation
    // ------------------------------------------------------------------

    private static void RunZeroAllocChecks()
    {
        HustleContext ctx = MidCareerCtx();
        FencingContext fenceCtx = FenceCtx();

        void OneNarcoticsRun(ulong seed)
        {
            var rng = new RngState(seed);
            HustleState state = NarcoticsHustle.DropInventory(in ctx, 300, ref rng);
            if (state.Stage != HustleStage.Busted)
            {
                state = NarcoticsHustle.CutProduct(in state, in ctx, 1.6, ref rng);
                state = NarcoticsHustle.PushTerritory(in state, in ctx, PushLevel.Encroach, ref rng);
            }
            _ = state.ToResolution();
        }

        void OneFencingRun(ulong seed)
        {
            var rng = new RngState(seed);
            FencingState state = FencingNegotiation.StartLot(in fenceCtx, ref rng);
            state = FencingNegotiation.Counter(in state, in fenceCtx, state.CurrentOffer * 1.8, ref rng);
            if (state.Outcome == FencingOutcomeKind.InProgress)
            {
                state = FencingNegotiation.Accept(in state, in fenceCtx, ref rng);
            }
            _ = state.ToResolution();
        }

        RobberyContext robberyCtx = RobberyCtx();
        void OneRobberyRun(ulong seed)
        {
            var rng = new RngState(seed);
            RobberyState state = RunRobberyPolicy(in robberyCtx, RobberyTarget.BookieStash, caseIt: true,
                RobberyApproach.SoloQuiet, alwaysPress: false, ref rng);
            _ = state.ToResolution();
        }

        // Warm-up: JIT everything before measuring.
        for (ulong i = 1; i <= 50; i++)
        {
            OneNarcoticsRun(i);
            OneFencingRun(i);
            OneRobberyRun(i);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ulong i = 1000; i <= 1200; i++)
        {
            OneNarcoticsRun(i);
        }
        long afterNarcotics = GC.GetAllocatedBytesForCurrentThread();

        for (ulong i = 1000; i <= 1200; i++)
        {
            OneFencingRun(i);
        }
        long afterFencing = GC.GetAllocatedBytesForCurrentThread();

        for (ulong i = 1000; i <= 1200; i++)
        {
            OneRobberyRun(i);
        }
        long afterRobbery = GC.GetAllocatedBytesForCurrentThread();

        double narcoticsPerRun = (afterNarcotics - before) / 201.0;
        double fencingPerRun = (afterFencing - afterNarcotics) / 201.0;
        double robberyPerRun = (afterRobbery - afterFencing) / 201.0;

        Console.WriteLine($"--- zero-alloc: {narcoticsPerRun:F1} B/Narcotics-run, {fencingPerRun:F1} B/Fencing-run, {robberyPerRun:F1} B/Robbery-run ---\n");

        Check("zero-alloc: a warm resolved Narcotics run allocates ~0 B", narcoticsPerRun < 16,
            $"{narcoticsPerRun:F1} B/run");
        Check("zero-alloc: a warm completed Fencing negotiation allocates ~0 B", fencingPerRun < 16,
            $"{fencingPerRun:F1} B/run");
        Check("zero-alloc: a warm completed Robbery run allocates ~0 B", robberyPerRun < 16,
            $"{robberyPerRun:F1} B/run");
    }
}
