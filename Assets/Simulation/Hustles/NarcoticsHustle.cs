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

public enum HaggleOutcomeKind : byte
{
    InProgress,

    /// <summary>Price agreed — the run proceeds at <see cref="HaggleState.DealUnitCost"/>.</summary>
    Deal,

    /// <summary>Patience ran out — the supplier is unwilling today (forfeit-to-no-deal, §3.1) and trust takes the push-too-hard penalty.</summary>
    Refused,

    /// <summary>The player walked — no run today, no trust consequence.</summary>
    Walked,
}

/// <summary>
/// One supplier haggle's live state (docs/design/hustle_minigames_depth_pass.md
/// §3.1, slice N-2): the buyer-side inversion of <see cref="FencingState"/> —
/// the supplier's ask descends toward a hidden floor as patience burns, and the
/// player wants a LOW close. Hidden fields stay public for the harness, same as
/// Fencing's; Layer 3 never renders <see cref="HiddenFloor"/>. A closed deal
/// carries the run's effective unit cost plus the allotment multiplier
/// (<see cref="BuyInMaxMult"/> — pay nearer the ask and the supplier fronts
/// more; squeeze the floor and the allotment shrinks: the margin/volume trade).
/// <see cref="ToResolution"/> projects only the trust delta — no money moves at
/// the haggle itself; the buy-in debit stays in <see cref="NarcoticsHustle.DropInventory"/>.
/// </summary>
public readonly record struct HaggleState(
    HaggleOutcomeKind Outcome,
    double HiddenFloor,
    double InitialAsk,
    double CurrentAsk,
    int InitialPatience,
    int PatienceRemaining,
    double DealUnitCost,
    double BuyInMaxMult,
    int SupplierTrustDelta)
{
    public HustleResolution ToResolution() => new(
        fundsDelta: 0, detectionRiskDelta: 0, healthCeilingDelta: 0, recklessnessDelta: 0, stressDelta: 0,
        SupplierTrustDelta, crewStandingDelta: 0,
        setWatchlistFlag: false, setBadProductFlag: false, setSpoiledGoodsFlag: false, setControlsTurfFlag: false);
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

        // --- Supplier haggle (§3.1, N-2). EV-neutrality is LOCKED: the neutral
        // autopilot closes deterministically at HaggleOpenAskMult · (1 − DropFrac)²
        // = 1.1 × the hidden floor, so with the floor-draw mean at trust 20 being
        // 10 × (E[u] − 0.02) = 9.1, the neutral mean effective cost is 10.01 ≈
        // UnitCost — the haggle adds variance and agency, not free money. Any
        // retune of these six coupled constants must re-prove that identity in
        // HustleHarness (the ±0.15 band check).
        public const double HaggleFloorUMin = 0.86;
        public const double HaggleFloorUMax = 1.00;
        public const double HaggleTrustDiscount = 0.10;
        public const double HaggleFloorMultMin = 0.60;
        public const double HaggleFloorMultMax = 1.05;
        public const double HaggleOpenAskMult = 1.4;
        public const double HaggleDropFrac = 0.5;
        public const int HagglePatienceBase = 4;

        /// <summary>Mirror of Fencing's PatienceBonusStanding: a supplier this trusted grants an extra round.</summary>
        public const int HagglePatienceBonusTrust = 50;

        /// <summary>The §3.1 neutral autopilot's fixed fraction: accept once the ask has fallen to this multiple of the opening ask.</summary>
        public const double NeutralHaggleAcceptFrac = 0.85;

        // Allotment multiplier over ComputeBuyInMax, lerped by where the close
        // landed between floor and opening ask — the neutral close (t = 0.25)
        // lands exactly 1.0, preserving the shipped ceiling.
        public const double HaggleBuyInMultMin = 0.90;
        public const double HaggleBuyInMultSpan = 0.40;

        public const int HaggleDealTrustBonus = 1;
        public const int HaggleRefusalTrustPenalty = 4;
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
    /// Stage 0 (§3.1, N-2): opens the supplier haggle — draws the hidden floor
    /// (trusted suppliers quote lower) and the opening ask above it. The one
    /// RngState draw in the whole haggle; every later transition is
    /// deterministic, mirroring Fencing's draw-once-then-negotiate shape.
    /// </summary>
    public static HaggleState HaggleStart(in HustleContext ctx, ref RngState rng)
    {
        double u = NarcoticsProfile.HaggleFloorUMin
            + rng.NextDouble() * (NarcoticsProfile.HaggleFloorUMax - NarcoticsProfile.HaggleFloorUMin);
        double floorMult = Math.Clamp(
            u - NarcoticsProfile.HaggleTrustDiscount * (ctx.SupplierTrust / 100.0),
            NarcoticsProfile.HaggleFloorMultMin, NarcoticsProfile.HaggleFloorMultMax);
        double floor = NarcoticsProfile.UnitCost * floorMult;
        double ask = floor * NarcoticsProfile.HaggleOpenAskMult;
        int patience = NarcoticsProfile.HagglePatienceBase
            + (ctx.SupplierTrust >= NarcoticsProfile.HagglePatienceBonusTrust ? 1 : 0);

        return new HaggleState(
            HaggleOutcomeKind.InProgress, floor, ask, ask, patience, patience,
            DealUnitCost: 0, BuyInMaxMult: 0, SupplierTrustDelta: 0);
    }

    /// <summary>Takes the supplier's ask on the table now.</summary>
    public static HaggleState HaggleAccept(in HaggleState state)
    {
        RequireHaggleInProgress(state);
        return CloseHaggle(state, state.CurrentAsk);
    }

    /// <summary>
    /// Bids a price. A bid at/above the supplier's live willingness closes at
    /// min(bid, ask) — he never charges more than he's asking; a lowball makes
    /// him concede toward the floor and burns a round. Patience hitting 0 is a
    /// refusal: no run today and the push-too-hard trust penalty (§3.1).
    /// </summary>
    public static HaggleState HaggleCounter(in HaggleState state, double bidPrice)
    {
        RequireHaggleInProgress(state);

        double willingness = ComputeSupplierWillingness(in state);
        if (bidPrice >= willingness)
        {
            return CloseHaggle(state, Math.Min(bidPrice, state.CurrentAsk));
        }

        int patience = state.PatienceRemaining - 1;
        if (patience <= 0)
        {
            return state with
            {
                Outcome = HaggleOutcomeKind.Refused,
                PatienceRemaining = 0,
                SupplierTrustDelta = -NarcoticsProfile.HaggleRefusalTrustPenalty,
            };
        }

        double nextAsk = state.CurrentAsk - NarcoticsProfile.HaggleDropFrac * (state.CurrentAsk - state.HiddenFloor);
        return state with { CurrentAsk = nextAsk, PatienceRemaining = patience };
    }

    /// <summary>Walks away from today's price: no run, no trust consequence.</summary>
    public static HaggleState HaggleWalk(in HaggleState state)
    {
        RequireHaggleInProgress(state);
        return state with { Outcome = HaggleOutcomeKind.Walked };
    }

    /// <summary>The §3.1 headless autopilot stand-in: accepts once the ask has fallen to a fixed fraction of the opening ask — the buyer-side mirror of Fencing's <see cref="FencingNegotiation.NeutralAcceptDecision"/>.</summary>
    public static bool NeutralHaggleDecision(in HaggleState state) =>
        state.CurrentAsk <= state.InitialAsk * NarcoticsProfile.NeutralHaggleAcceptFrac;

    /// <summary>Buyer-side mirror of Fencing's ceiling: starts at the current ask (full patience) and descends to the hidden floor as patience burns.</summary>
    private static double ComputeSupplierWillingness(in HaggleState state) =>
        state.HiddenFloor + (state.CurrentAsk - state.HiddenFloor)
            * ((double)state.PatienceRemaining / state.InitialPatience);

    private static HaggleState CloseHaggle(in HaggleState state, double dealPrice)
    {
        double t = state.InitialAsk > state.HiddenFloor
            ? Math.Clamp((dealPrice - state.HiddenFloor) / (state.InitialAsk - state.HiddenFloor), 0, 1)
            : 1;
        return state with
        {
            Outcome = HaggleOutcomeKind.Deal,
            DealUnitCost = dealPrice,
            BuyInMaxMult = NarcoticsProfile.HaggleBuyInMultMin + NarcoticsProfile.HaggleBuyInMultSpan * t,
            SupplierTrustDelta = NarcoticsProfile.HaggleDealTrustBonus,
        };
    }

    private static void RequireHaggleInProgress(in HaggleState state)
    {
        if (state.Outcome != HaggleOutcomeKind.InProgress)
        {
            throw new InvalidOperationException($"Cannot act on a haggle already resolved ({state.Outcome}).");
        }
    }

    /// <summary>
    /// Stage 1: commits <paramref name="buyIn"/> (clamped to [BuyInMin, min(funds, BuyInMax)]
    /// by the caller — this throws on an out-of-range value, the same
    /// fail-loud contract <see cref="Life.DaySchedule"/>'s ctor keeps) and
    /// rolls the seizure check. Debits the buy-in immediately either way —
    /// capital is at risk from this point (§3.1). This flat-price signature is
    /// the shipped pre-N-2 path, byte-identical; a haggled run goes through the
    /// overload below.
    /// </summary>
    public static HustleState DropInventory(in HustleContext ctx, double buyIn, ref RngState rng) =>
        DropInventory(in ctx, buyIn, NarcoticsProfile.UnitCost, buyInMaxMult: 1.0, ref rng);

    /// <summary>
    /// N-2 overload: Stage 1 at the haggled <paramref name="unitCost"/> and
    /// allotment multiplier from a closed <see cref="HaggleState"/> — units per
    /// dollar and the supplier-front ceiling both move with the deal struck.
    /// </summary>
    public static HustleState DropInventory(
        in HustleContext ctx, double buyIn, double unitCost, double buyInMaxMult, ref RngState rng)
    {
        double maxBuyIn = Math.Min(ctx.Funds, ComputeBuyInMax(in ctx) * buyInMaxMult);
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
            HustleStage.InventoryDrop, buyIn, buyIn / unitCost, 0, 0,
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
