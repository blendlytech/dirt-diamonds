using System;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Simulation.Hustles;

/// <summary>
/// Snapshot inputs for one Narcotics run (docs/design/hustles_narcotics_fencing.md
/// §3.0), read once at run start. Data-free/Baseball-free/engine-free — the
/// same profile <see cref="AtBatResolver"/> and <see cref="HeirGenetics"/>
/// keep, so <c>Tools/HustleHarness</c> drives it from hand-built fixtures with
/// no DB/graph setup. <see cref="OwnsTurfLocal"/> is read for shape-completeness
/// only — 8b writes <c>controls_turf_local</c> but does not yet build the
/// passive-turf-income consumer of it (§8, deliberately deferred).
/// </summary>
public readonly struct HustleContext
{
    public readonly double Funds;

    /// <summary>detection_risk / 100.</summary>
    public readonly double Heat;

    /// <summary>recklessness / 100.</summary>
    public readonly double Reck;

    public readonly int SupplierTrust;
    public readonly int CrewStandingLocal;
    public readonly bool OwnsTurfLocal;

    /// <summary>The uses_product flag — set only by a recklessness-gated content event elsewhere, never by this hustle (§3.2).</summary>
    public readonly bool UsesProduct;

    public HustleContext(
        double funds, double heat, double reck, int supplierTrust, int crewStandingLocal,
        bool ownsTurfLocal, bool usesProduct)
    {
        Funds = funds;
        Heat = heat;
        Reck = reck;
        SupplierTrust = supplierTrust;
        CrewStandingLocal = crewStandingLocal;
        OwnsTurfLocal = ownsTurfLocal;
        UsesProduct = usesProduct;
    }
}

public enum HustleStage : byte
{
    Idle,
    InventoryDrop,
    ProfitToxicityCut,
    TerritoryControl,
    Resolved,
    Busted,
}

public enum PushLevel : byte
{
    Hold,
    Encroach,
    TakeOver,
}

/// <summary>
/// Accumulated run state (§3): the current stage plus everything carried
/// forward to the next transition (inventory/cut/price) and every
/// resolution-bound delta accrued so far. A record struct so each transition
/// returns a non-destructively-modified copy (<c>state with { ... }</c>) — the
/// pattern <see cref="Life.RelationshipSeed"/> already established here.
/// <see cref="ToResolution"/> projects the terminal (Resolved/Busted) state
/// into the <see cref="HustleResolution"/> Layer 2 applies.
/// </summary>
public readonly record struct HustleState(
    HustleStage Stage,
    double BuyIn,
    double InventoryUnits,
    double SalableUnits,
    double EffPrice,
    double FundsDelta,
    int DetectionRiskDelta,
    int HealthCeilingDelta,
    int RecklessnessDelta,
    double StressDelta,
    int SupplierTrustDelta,
    int CrewStandingDelta,
    bool WatchlistFlag,
    bool BadProductFlag,
    bool SpoiledGoodsFlag,
    bool ControlsTurfFlag)
{
    public HustleResolution ToResolution() => new(
        FundsDelta, DetectionRiskDelta, HealthCeilingDelta, RecklessnessDelta, StressDelta,
        SupplierTrustDelta, CrewStandingDelta, WatchlistFlag, BadProductFlag, SpoiledGoodsFlag, ControlsTurfFlag);
}

/// <summary>
/// Pure resolver for the Narcotics 3-stage state machine
/// (docs/design/hustles_narcotics_fencing.md §3): Inventory Drop → Profit/
/// Toxicity Cut → Territory Control vs Factions. One method per stage
/// transition, each taking the current <see cref="HustleState"/>, the
/// player's decision for that stage, and <c>ref</c> <see cref="RngState"/>,
/// returning the next state — no DB, no bus, no graph reference (Layer 2's
/// <see cref="Economy.Hustles.HustleService"/> owns all of that). Every
/// constant lives in <see cref="NarcoticsProfile"/>: retuning is a data edit,
/// never a logic edit, same precedent as every calibration table in this
/// codebase.
/// </summary>
public static class NarcoticsHustle
{
    public static class NarcoticsProfile
    {
        public const double UnitCost = 10;
        public const double StreetPrice = 14;
        public const double BuyInMin = 100;
        public const double BuyInMaxBase = 1000;
        public const double CutMax = 2.5;
    }

    private readonly struct MarketTier
    {
        public readonly double MarketCap;
        public readonly double RevenueMult;
        public readonly double PConflict;

        public MarketTier(double marketCap, double revenueMult, double pConflict)
        {
            MarketCap = marketCap;
            RevenueMult = revenueMult;
            PConflict = pConflict;
        }
    }

    private static MarketTier Tier(PushLevel push) => push switch
    {
        PushLevel.Hold => new MarketTier(60, 1.00, 0.00),
        PushLevel.Encroach => new MarketTier(120, 1.30, 0.20),
        PushLevel.TakeOver => new MarketTier(200, 1.70, 0.45),
        _ => throw new ArgumentOutOfRangeException(nameof(push), push, null),
    };

    /// <summary>§3.1: the supplier-front ceiling before the player's own funds also bound the buy-in.</summary>
    public static double ComputeBuyInMax(in HustleContext ctx) =>
        NarcoticsProfile.BuyInMaxBase
        * (0.5 + 0.5 * (ctx.SupplierTrust + 100) / 200.0)
        * (1 - 0.4 * ctx.Heat);

    /// <summary>
    /// Stage 1: commits <paramref name="buyIn"/> (clamped to [BuyInMin, min(funds, BuyInMax)]
    /// by the caller — this throws on an out-of-range value, the same
    /// fail-loud contract <see cref="Life.DaySchedule"/>'s ctor keeps) and
    /// rolls the seizure check. Debits the buy-in immediately either way —
    /// capital is at risk from this point (§3.1).
    /// </summary>
    public static HustleState DropInventory(in HustleContext ctx, double buyIn, ref RngState rng)
    {
        double maxBuyIn = Math.Min(ctx.Funds, ComputeBuyInMax(in ctx));
        if (buyIn < NarcoticsProfile.BuyInMin || buyIn > maxBuyIn)
        {
            throw new ArgumentOutOfRangeException(
                nameof(buyIn), buyIn, $"Buy-in must be in [{NarcoticsProfile.BuyInMin}, {maxBuyIn}].");
        }

        double pSeize = Math.Clamp(
            0.05 + 0.12 * ctx.Heat - 0.05 * Math.Max(0, ctx.SupplierTrust) / 100.0, 0.02, 0.60);

        if (rng.NextDouble() < pSeize)
        {
            return new HustleState(
                HustleStage.Busted, buyIn, 0, 0, 0,
                FundsDelta: -buyIn, DetectionRiskDelta: 6, HealthCeilingDelta: 0, RecklessnessDelta: 2, StressDelta: 15,
                SupplierTrustDelta: 0, CrewStandingDelta: 0,
                WatchlistFlag: true, BadProductFlag: false, SpoiledGoodsFlag: false, ControlsTurfFlag: false);
        }

        return new HustleState(
            HustleStage.InventoryDrop, buyIn, buyIn / NarcoticsProfile.UnitCost, 0, 0,
            FundsDelta: -buyIn, DetectionRiskDelta: 0, HealthCeilingDelta: 0, RecklessnessDelta: 0, StressDelta: 0,
            SupplierTrustDelta: 0, CrewStandingDelta: 0,
            WatchlistFlag: false, BadProductFlag: false, SpoiledGoodsFlag: false, ControlsTurfFlag: false);
    }

    /// <summary>
    /// Stage 2: steps on the product by <paramref name="cutFactor"/> ∈ [1.0, CutMax]
    /// (§3.2) — concave revenue in c (volume up, price down) — and rolls the
    /// bad-batch check scaled by toxicity.
    /// </summary>
    public static HustleState CutProduct(in HustleState state, in HustleContext ctx, double cutFactor, ref RngState rng)
    {
        RequireStage(state, HustleStage.InventoryDrop, nameof(CutProduct));
        if (cutFactor < 1.0 || cutFactor > NarcoticsProfile.CutMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cutFactor), cutFactor, $"Cut factor must be in [1.0, {NarcoticsProfile.CutMax}].");
        }

        double toxicity = (cutFactor - 1.0) / (NarcoticsProfile.CutMax - 1.0);
        double sellUnits = state.InventoryUnits * cutFactor;
        double effPrice = NarcoticsProfile.StreetPrice * (1 - 0.25 * toxicity);
        double pBadBatch = 0.30 * toxicity;

        bool badBatch = rng.NextDouble() < pBadBatch;
        double salable = badBatch ? 0.40 * sellUnits : sellUnits;

        int detectionDelta = RoundAwayFromZero(2 + 4 * toxicity);
        double stressDelta = RoundAwayFromZero(5 + 10 * toxicity);
        int crewDelta = 0;
        int healthDelta = 0;

        if (badBatch)
        {
            detectionDelta += RoundAwayFromZero(10 * toxicity);
            crewDelta -= RoundAwayFromZero(15 * toxicity);
            if (ctx.UsesProduct)
            {
                healthDelta -= RoundAwayFromZero(6 * toxicity);
            }
        }

        return state with
        {
            Stage = HustleStage.ProfitToxicityCut,
            SalableUnits = salable,
            EffPrice = effPrice,
            DetectionRiskDelta = state.DetectionRiskDelta + detectionDelta,
            HealthCeilingDelta = state.HealthCeilingDelta + healthDelta,
            StressDelta = state.StressDelta + stressDelta,
            CrewStandingDelta = state.CrewStandingDelta + crewDelta,
            BadProductFlag = state.BadProductFlag || badBatch,
        };
    }

    /// <summary>Stage 3: picks the market/conflict tier (§3.3) and resolves demand saturation plus any turf-war roll.</summary>
    public static HustleState PushTerritory(in HustleState state, in HustleContext ctx, PushLevel push, ref RngState rng)
    {
        RequireStage(state, HustleStage.ProfitToxicityCut, nameof(PushTerritory));
        return ResolveMarket(in state, in ctx, push, ref rng);
    }

    /// <summary>
    /// Banks the profit accrued so far instead of continuing (§3): from
    /// InventoryDrop, the product goes unsold (a token spoiled_goods flag —
    /// design doc's own "rational player always at least sells" caveat, kept
    /// for the harness's per-stage bank-exit coverage); from
    /// ProfitToxicityCut, sells at Hold-level demand with zero conflict risk
    /// (Hold's own pConflict is 0, so no RngState draw is needed).
    /// </summary>
    public static HustleState BankExit(in HustleState state, in HustleContext ctx)
    {
        switch (state.Stage)
        {
            case HustleStage.InventoryDrop:
                return state with { Stage = HustleStage.Resolved, SpoiledGoodsFlag = true };
            case HustleStage.ProfitToxicityCut:
                RngState unused = default;
                return ResolveMarket(in state, in ctx, PushLevel.Hold, ref unused);
            default:
                throw new InvalidOperationException($"Cannot bank & exit from stage {state.Stage}.");
        }
    }

    private static HustleState ResolveMarket(in HustleState state, in HustleContext ctx, PushLevel push, ref RngState rng)
    {
        MarketTier tier = Tier(push);
        double grossRevenue = MarketRevenue(state.SalableUnits, state.EffPrice, in tier);

        if (tier.PConflict > 0 && rng.NextDouble() < tier.PConflict)
        {
            double hostility = Math.Max(0, -ctx.CrewStandingLocal) / 100.0;
            double pWin = Math.Clamp(0.45 + 0.25 * ctx.Reck - 0.20 * hostility, 0.10, 0.85);

            if (rng.NextDouble() < pWin)
            {
                return state with
                {
                    Stage = HustleStage.Resolved,
                    FundsDelta = state.FundsDelta + grossRevenue,
                    SupplierTrustDelta = state.SupplierTrustDelta + 10,
                    RecklessnessDelta = state.RecklessnessDelta + 2,
                    ControlsTurfFlag = true,
                };
            }

            // Retaliation: revenue collapses to Hold economics; the crew edge
            // is pushed to a deep Rival floor (§3.3) — a plain delta computed
            // from the context's own reading, per the wall's "faction standing
            // leaves as a plain delta" rule.
            double holdRevenue = MarketRevenue(state.SalableUnits, state.EffPrice, Tier(PushLevel.Hold));
            int crewDelta = Math.Min(-40, ctx.CrewStandingLocal - 40) - ctx.CrewStandingLocal;

            return state with
            {
                Stage = HustleStage.Resolved,
                FundsDelta = state.FundsDelta + holdRevenue - 0.5 * state.BuyIn,
                HealthCeilingDelta = state.HealthCeilingDelta - RoundAwayFromZero(8 + 6 * ctx.Reck),
                DetectionRiskDelta = state.DetectionRiskDelta + 8,
                StressDelta = state.StressDelta + 25,
                CrewStandingDelta = state.CrewStandingDelta + crewDelta,
            };
        }

        return state with { Stage = HustleStage.Resolved, FundsDelta = state.FundsDelta + grossRevenue };
    }

    /// <summary>§3.3 demand saturation: units up to MarketCap sell at effPrice, the excess fire-sales at half.</summary>
    private static double MarketRevenue(double salableUnits, double effPrice, in MarketTier tier)
    {
        double soldFull = Math.Min(salableUnits, tier.MarketCap);
        double soldFire = salableUnits - soldFull;
        return (soldFull * effPrice + soldFire * 0.5 * effPrice) * tier.RevenueMult;
    }

    private static void RequireStage(in HustleState state, HustleStage expected, string method)
    {
        if (state.Stage != expected)
        {
            throw new InvalidOperationException($"{method} requires stage {expected}, but the run is at {state.Stage}.");
        }
    }

    private static int RoundAwayFromZero(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
