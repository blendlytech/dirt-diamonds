using System;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Simulation.Hustles;

/// <summary>
/// Snapshot inputs for one Robbery run (docs/design/hustle_minigames_depth_pass.md
/// §5), read once at run start. Data-free/Baseball-free/engine-free — the same
/// profile every hustle resolver keeps, so <c>Tools/HustleHarness</c> drives it
/// from hand-built fixtures with no DB/graph setup. <see cref="HasCrew"/> is the
/// only new context input over Narcotics' shape: the crew-job approach (§5.1.2)
/// needs a resolved crew rep (<c>HustleCrewPlayerId</c>, already resolved by
/// <see cref="Economy.Hustles.HustleService"/>) and degrades gracefully to
/// unavailable when there is none.
/// </summary>
public readonly struct RobberyContext
{
    /// <summary>Funds on hand — bounds the bail/legal-fees hit a bust can take (a bust can zero you out, never indebt you).</summary>
    public readonly double Funds;

    /// <summary>detection_risk / 100.</summary>
    public readonly double Heat;

    /// <summary>recklessness / 100.</summary>
    public readonly double Reck;

    /// <summary>Whether a crew rep is resolved — gates the crew-job approach (§5.1.2).</summary>
    public readonly bool HasCrew;

    public RobberyContext(double funds, double heat, double reck, bool hasCrew)
    {
        Funds = funds;
        Heat = heat;
        Reck = reck;
        HasCrew = hasCrew;
    }
}

/// <summary>The mark chosen in the Case stage (§5.1.1). Ordered easy→hard; the score climbs with the difficulty.</summary>
public enum RobberyTarget : byte
{
    ConvenienceStore,
    BookieStash,
    Warehouse,
}

/// <summary>How the job is run (§5.1.2) — sets the score/bust/health/heat curve for the execute + getaway rolls.</summary>
public enum RobberyApproach : byte
{
    SoloQuiet,
    StrongArm,
    Crew,
}

/// <summary>
/// The four-stage sequence (§5.1). Idle is the pre-case start; Grabbed is the
/// press-your-luck live beat (partial take in hand, decision pending); Escaping
/// is the take-secured state awaiting the getaway roll; Busted/Resolved are
/// absorbing.
/// </summary>
public enum RobberyStage : byte
{
    Idle,
    Cased,
    Approached,
    Grabbed,
    Escaping,
    Resolved,
    Busted,
}

/// <summary>
/// Accumulated run state (§5.1): the current stage plus everything carried
/// forward (chosen target/approach, the score reachable this run, the take
/// grabbed so far) and every resolution-bound delta accrued so far. A record
/// struct so each transition returns a non-destructively-modified copy
/// (<c>state with { ... }</c>) — the same pattern <see cref="HustleState"/> and
/// <see cref="FencingState"/> keep, so the whole four-stage sequence accrues in
/// memory and projects into one <see cref="HustleResolution"/> on the terminal
/// (Resolved/Busted) state (INV-1). <see cref="Cased"/> records the optional
/// casing beat so the execute/getaway rolls can read the variance reduction it
/// bought; UI never has to.
/// </summary>
public readonly record struct RobberyState(
    RobberyStage Stage,
    RobberyTarget Target,
    RobberyApproach Approach,
    bool Cased,
    double FullScore,
    double PartialTake,
    double FundsDelta,
    int DetectionRiskDelta,
    int HealthCeilingDelta,
    int RecklessnessDelta,
    double StressDelta,
    int CrewStandingDelta,
    bool RobberyBustFlag,
    bool HotGoodsFlag)
{
    public HustleResolution ToResolution() => new(
        FundsDelta, DetectionRiskDelta, HealthCeilingDelta, RecklessnessDelta, StressDelta,
        supplierTrustDelta: 0, crewStandingDelta: CrewStandingDelta,
        setWatchlistFlag: false, setBadProductFlag: false, setSpoiledGoodsFlag: false, setControlsTurfFlag: false,
        setGamblingBustFlag: false, setRobberyBustFlag: RobberyBustFlag, setArmsHotGoodsFlag: HotGoodsFlag);
}

/// <summary>
/// Pure resolver for the Robbery four-stage state machine
/// (docs/design/hustle_minigames_depth_pass.md §5): Case → Approach → Execute
/// (with a mid-execute press-your-luck beat) → Getaway. One method per stage
/// transition, each taking the current <see cref="RobberyState"/>, the player's
/// decision for that stage, and <c>ref</c> <see cref="RngState"/>, returning the
/// next state — no DB, no bus, no graph reference (Layer 2's
/// <see cref="Economy.Hustles.HustleService"/> owns all of that, R-2). Every
/// constant lives in <see cref="RobberyProfile"/> / the per-enum tables:
/// retuning is a data edit, never a logic edit, the same precedent as
/// <see cref="NarcoticsHustle"/> and every calibration table in this codebase.
///
/// INV-2 (every dollar of upside buys career tail risk) is structural here:
/// harder targets and louder approaches raise <see cref="RobberyState.FullScore"/>
/// AND the execute-bust / getaway-bust probabilities, and the press-your-luck
/// beat trades the remaining score for a rising bust roll — a cautious policy is
/// +EV-but-lumpy, a greedy one carries a real <c>robbery_bust</c> rate. The
/// bust is a loss on every axis: a funds-bounded bail/legal-fees hit
/// (<see cref="RobberyProfile.BustLegalCostFrac"/> of the mark's base score),
/// the arrest-triad detection spike, and (on strong-arm) a health_ceiling hit;
/// the arrest-absence *teeth* are a DIRT decision, deferred (§11) — this
/// resolver only sets the flag.
/// </summary>
public static class RobberyHustle
{
    public static class RobberyProfile
    {
        /// <summary>Casing spends a beat to shave execute variance (§5.1.1) at a small heat cost.</summary>
        public const int CaseHeatCost = 2;

        /// <summary>The additive success-probability bump a cased target grants on the execute roll — the "reduces variance" payoff cashed as a better mean.</summary>
        public const double CaseSuccessBonus = 0.12;

        /// <summary>Fraction of the full score already secured when the execute roll succeeds, before the press-your-luck beat (§5.1.3).</summary>
        public const double GrabPartialFrac = 0.5;

        /// <summary>Base probability the getaway is botched, before approach/casing modifiers (§5.1.4).</summary>
        public const double GetawayBotchBase = 0.25;

        /// <summary>A botched getaway after a full grab converts the take down to this fraction and spikes heat.</summary>
        public const double BotchedGetawayFrac = GrabPartialFrac;

        /// <summary>Detection spike written on any robbery_bust — feeds 8c's arrest triad (§5.1.3).</summary>
        public const int BustDetectionSpike = 12;

        /// <summary>
        /// A bust's bail/legal-fees hit as a fraction of the chosen mark's base
        /// score — the monetary stake that makes a robbery gone wrong a real
        /// financial loss, not just a career one. Bigger job, bigger bail.
        /// Clamped to <see cref="RobberyContext.Funds"/> so a bust can zero the
        /// player out but never indebt them (the same funds-bounded discipline
        /// as Narcotics' buy-in).
        /// </summary>
        public const double BustLegalCostFrac = 0.25;

        /// <summary>Detection added by a merely-botched (non-bust) getaway.</summary>
        public const int BotchedGetawayDetection = 6;

        /// <summary>Recklessness the act itself accrues, win or lose.</summary>
        public const int BaseRecklessnessDelta = 2;
    }

    private readonly struct TargetProfile
    {
        public readonly double BaseScore;

        /// <summary>0..1 — folded into the execute roll; a harder mark is likelier to go wrong.</summary>
        public readonly double Difficulty;

        public readonly int BaseHeat;

        public TargetProfile(double baseScore, double difficulty, int baseHeat)
        {
            BaseScore = baseScore;
            Difficulty = difficulty;
            BaseHeat = baseHeat;
        }
    }

    private static TargetProfile Target(RobberyTarget target) => target switch
    {
        RobberyTarget.ConvenienceStore => new TargetProfile(baseScore: 350, difficulty: 0.20, baseHeat: 3),
        RobberyTarget.BookieStash => new TargetProfile(baseScore: 900, difficulty: 0.40, baseHeat: 6),
        RobberyTarget.Warehouse => new TargetProfile(baseScore: 1800, difficulty: 0.60, baseHeat: 10),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private readonly struct ApproachProfile
    {
        public readonly double ScoreMult;

        /// <summary>Additive term on the execute failure probability — louder ⇒ likelier to go wrong.</summary>
        public readonly double BustAdd;

        /// <summary>Additive term on the getaway botch probability — quiet approaches lower it.</summary>
        public readonly double GetawayAdd;

        /// <summary>Whether a botched job risks a health_ceiling hit (strong-arm only takes a beating).</summary>
        public readonly bool HealthAtRisk;

        /// <summary>The take is split with the crew before it reaches the player (crew job).</summary>
        public readonly double TakeShare;

        public ApproachProfile(double scoreMult, double bustAdd, double getawayAdd, bool healthAtRisk, double takeShare)
        {
            ScoreMult = scoreMult;
            BustAdd = bustAdd;
            GetawayAdd = getawayAdd;
            HealthAtRisk = healthAtRisk;
            TakeShare = takeShare;
        }
    }

    private static ApproachProfile Approach(RobberyApproach approach) => approach switch
    {
        RobberyApproach.SoloQuiet => new ApproachProfile(scoreMult: 1.00, bustAdd: 0.00, getawayAdd: -0.10, healthAtRisk: false, takeShare: 1.00),
        RobberyApproach.StrongArm => new ApproachProfile(scoreMult: 1.35, bustAdd: 0.12, getawayAdd: 0.05, healthAtRisk: true, takeShare: 1.00),
        RobberyApproach.Crew => new ApproachProfile(scoreMult: 1.70, bustAdd: 0.06, getawayAdd: 0.00, healthAtRisk: false, takeShare: 0.60),
        _ => throw new ArgumentOutOfRangeException(nameof(approach), approach, null),
    };

    /// <summary>
    /// Stage 1: picks the mark. <paramref name="caseIt"/> spends the casing beat
    /// (§5.1.1) — a small heat cost now for a better execute roll later. No RNG:
    /// casing is a deterministic variance-reducer, the roll it improves comes at
    /// Execute. Sets the run's reachable full score before the approach multiplier.
    /// </summary>
    public static RobberyState CaseTarget(in RobberyContext ctx, RobberyTarget target, bool caseIt)
    {
        TargetProfile profile = Target(target);
        return new RobberyState(
            RobberyStage.Cased, target, RobberyApproach.SoloQuiet, caseIt,
            FullScore: profile.BaseScore, PartialTake: 0,
            FundsDelta: 0, DetectionRiskDelta: caseIt ? RobberyProfile.CaseHeatCost : 0,
            HealthCeilingDelta: 0, RecklessnessDelta: 0, StressDelta: 0, CrewStandingDelta: 0,
            RobberyBustFlag: false, HotGoodsFlag: false);
    }

    /// <summary>
    /// Stage 2: locks the approach and applies its score multiplier to the run's
    /// reachable full score (§5.1.2). The crew job requires a resolved crew rep;
    /// its split take is folded in at grab/bank time, not here. No RNG.
    /// </summary>
    public static RobberyState ChooseApproach(in RobberyState state, in RobberyContext ctx, RobberyApproach approach)
    {
        RequireStage(state, RobberyStage.Cased, nameof(ChooseApproach));
        if (approach == RobberyApproach.Crew && !ctx.HasCrew)
        {
            throw new InvalidOperationException("Crew approach requires a resolved crew rep (HasCrew).");
        }

        double scoredFull = state.FullScore * Approach(approach).ScoreMult;
        return state with { Stage = RobberyStage.Approached, Approach = approach, FullScore = scoredFull };
    }

    /// <summary>§5.1.3: the execute success probability from (target difficulty, approach, Reck, Heat, casing).</summary>
    public static double ComputeExecuteSuccessProbability(in RobberyState state, in RobberyContext ctx)
    {
        TargetProfile target = Target(state.Target);
        ApproachProfile approach = Approach(state.Approach);
        double pFail = Math.Clamp(
            target.Difficulty + approach.BustAdd + 0.15 * ctx.Heat - 0.10 * ctx.Reck
            - (state.Cased ? RobberyProfile.CaseSuccessBonus : 0.0),
            0.03, 0.90);
        return 1.0 - pFail;
    }

    /// <summary>
    /// Stage 3, the roll (§5.1.3): a success grabs the partial take and moves to
    /// <see cref="RobberyStage.Grabbed"/> awaiting the press-your-luck beat; a
    /// failure is a bust — the take is zero, the arrest-triad detection spike and
    /// the base recklessness/stress land, plus a health hit on a strong-arm job.
    /// </summary>
    public static RobberyState Execute(in RobberyState state, in RobberyContext ctx, ref RngState rng)
    {
        RequireStage(state, RobberyStage.Approached, nameof(Execute));

        if (rng.NextDouble() >= ComputeExecuteSuccessProbability(in state, in ctx))
        {
            return Bust(in state, in ctx);
        }

        double partial = state.FullScore * RobberyProfile.GrabPartialFrac;
        return state with
        {
            Stage = RobberyStage.Grabbed,
            PartialTake = partial,
            RecklessnessDelta = state.RecklessnessDelta + RobberyProfile.BaseRecklessnessDelta,
        };
    }

    /// <summary>§5.1.3: the added bust probability of pushing for the full score instead of grabbing and running.</summary>
    public static double ComputePressLuckBustProbability(in RobberyState state, in RobberyContext ctx)
    {
        ApproachProfile approach = Approach(state.Approach);
        return Math.Clamp(
            0.25 + approach.BustAdd + 0.15 * ctx.Heat - 0.10 * ctx.Reck
            - (state.Cased ? 0.5 * RobberyProfile.CaseSuccessBonus : 0.0),
            0.05, 0.85);
    }

    /// <summary>
    /// Stage 3, the live decision (§5.1.3) — press your luck for the full score
    /// at a rising bust roll. Success upgrades the grabbed partial to the full
    /// score and moves to Escaping (getaway pending); a bust wipes the whole
    /// take (you had it and lost it) and writes the bust bundle.
    /// </summary>
    public static RobberyState PressLuck(in RobberyState state, in RobberyContext ctx, ref RngState rng)
    {
        RequireStage(state, RobberyStage.Grabbed, nameof(PressLuck));

        if (rng.NextDouble() < ComputePressLuckBustProbability(in state, in ctx))
        {
            return Bust(in state, in ctx);
        }

        return state with { Stage = RobberyStage.Escaping, PartialTake = state.FullScore };
    }

    /// <summary>
    /// Grab and run (§5.1.3): bail with the partial take already secured instead
    /// of pressing for the full score. Moves straight to Escaping with the
    /// partial locked in. No RNG — bailing is the sure thing.
    /// </summary>
    public static RobberyState GrabAndRun(in RobberyState state)
    {
        RequireStage(state, RobberyStage.Grabbed, nameof(GrabAndRun));
        return state with { Stage = RobberyStage.Escaping };
    }

    /// <summary>
    /// Stage 4 (§5.1.4): the final heat/detection roll, its botch probability
    /// lowered by casing and a quiet approach — so the early picks visibly pay
    /// off here. A clean getaway banks the secured take (crew split applied) and
    /// arms <c>hot_goods</c> (§5.2). A botch on a full-score grab converts the
    /// take down and spikes heat; a botch on an already-bailed partial trims it
    /// and adds heat but is never itself a bust (you already ran). The take
    /// carried in is the amount secured at grab/press time.
    /// </summary>
    public static RobberyState Getaway(in RobberyState state, in RobberyContext ctx, ref RngState rng)
    {
        RequireStage(state, RobberyStage.Escaping, nameof(Getaway));

        ApproachProfile approach = Approach(state.Approach);
        TargetProfile target = Target(state.Target);
        double securedTake = state.PartialTake; // locked in by GrabAndRun / PressLuck
        double pBotch = Math.Clamp(
            RobberyProfile.GetawayBotchBase + approach.GetawayAdd + 0.5 * target.Difficulty
            + 0.10 * ctx.Heat - (state.Cased ? RobberyProfile.CaseSuccessBonus : 0.0),
            0.03, 0.85);

        bool botched = rng.NextDouble() < pBotch;
        double bankedTake = botched ? securedTake * RobberyProfile.BotchedGetawayFrac : securedTake;
        double playerTake = bankedTake * approach.TakeShare;
        int crewDelta = state.Approach == RobberyApproach.Crew ? 3 : 0;

        return state with
        {
            Stage = RobberyStage.Resolved,
            FundsDelta = state.FundsDelta + playerTake,
            DetectionRiskDelta = state.DetectionRiskDelta + target.BaseHeat + (botched ? RobberyProfile.BotchedGetawayDetection : 0),
            StressDelta = state.StressDelta + (botched ? 20 : 8),
            CrewStandingDelta = state.CrewStandingDelta + crewDelta,
            HotGoodsFlag = true,
        };
    }

    /// <summary>
    /// The headless autopilot stand-in (§5.3): the cautious neutral policy —
    /// quiet approach, always grab-and-run on the press-your-luck beat (never
    /// press). Used by <c>Tools/HustleHarness</c> to assert a fixed policy's EV/
    /// tail band, the <see cref="FencingNegotiation.NeutralAcceptDecision"/> /
    /// Hold'em-TAG-autopilot precedent. A cautious policy should be +EV-but-lumpy
    /// with a low bust rate; a reckless press-every-beat policy carries a real one.
    /// </summary>
    public static bool NeutralPressLuckDecision(in RobberyState state) => false;

    private static RobberyState Bust(in RobberyState state, in RobberyContext ctx)
    {
        ApproachProfile approach = Approach(state.Approach);
        int healthHit = approach.HealthAtRisk
            ? -(int)Math.Round(6 + 8 * ctx.Reck, MidpointRounding.AwayFromZero)
            : 0;

        // Bail/legal fees: score-proportional, bounded by funds on hand — the
        // take is wiped AND the bust costs real money (§5.2, user ruling
        // 2026-07-12: a robbery gone wrong must be a financial loss too).
        double legalCost = Math.Min(
            Math.Max(0, ctx.Funds), RobberyProfile.BustLegalCostFrac * Target(state.Target).BaseScore);

        return state with
        {
            Stage = RobberyStage.Busted,
            PartialTake = 0,
            FundsDelta = -legalCost,
            DetectionRiskDelta = state.DetectionRiskDelta + RobberyProfile.BustDetectionSpike,
            HealthCeilingDelta = state.HealthCeilingDelta + healthHit,
            RecklessnessDelta = state.RecklessnessDelta + RobberyProfile.BaseRecklessnessDelta,
            StressDelta = state.StressDelta + 25,
            RobberyBustFlag = true,
        };
    }

    private static void RequireStage(in RobberyState state, RobberyStage expected, string method)
    {
        if (state.Stage != expected)
        {
            throw new InvalidOperationException($"{method} requires stage {expected}, but the run is at {state.Stage}.");
        }
    }
}
