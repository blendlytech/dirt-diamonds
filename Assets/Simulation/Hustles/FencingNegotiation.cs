using System;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Simulation.Hustles;

/// <summary>Snapshot inputs for one Fencing lot negotiation (§4.1), read once per lot.</summary>
public readonly struct FencingContext
{
    /// <summary>detection_risk / 100.</summary>
    public readonly double Heat;

    public readonly int FenceStanding;

    public FencingContext(double heat, int fenceStanding)
    {
        Heat = heat;
        FenceStanding = fenceStanding;
    }
}

public enum FencingOutcomeKind : byte
{
    InProgress,
    Deal,
    Walk,
}

/// <summary>
/// One negotiation's live state (§4): the fence's current offer plus the
/// hidden true value/reservation drawn once at <see cref="FencingNegotiation.StartLot"/>
/// — kept as plain public fields (not hidden from the pure-math layer) so
/// <c>Tools/HustleHarness</c> can assert against them directly; Layer 3's UI
/// simply never renders <see cref="HiddenValue"/>/<see cref="HiddenReservation"/>.
/// A record struct like <see cref="HustleState"/>, for the same non-destructive
/// per-round <c>with</c> updates.
/// </summary>
public readonly record struct FencingState(
    FencingOutcomeKind Outcome,
    double HiddenValue,
    double HiddenReservation,
    double InitialOffer,
    double CurrentOffer,
    int InitialPatience,
    int PatienceRemaining,
    double DealPrice,
    double FundsDelta,
    int DetectionRiskDelta,
    double StressDelta,
    bool WatchlistFlag,
    bool SpoiledGoodsFlag)
{
    public HustleResolution ToResolution() => new(
        FundsDelta, DetectionRiskDelta, healthCeilingDelta: 0, recklessnessDelta: 0, StressDelta,
        supplierTrustDelta: 0, crewStandingDelta: 0,
        WatchlistFlag, setBadProductFlag: false, SpoiledGoodsFlag, setControlsTurfFlag: false);
}

/// <summary>
/// Pure resolver for the Fencing alternating-offer negotiation (§4): an
/// information-and-nerve game over a hidden reservation price, no twitch
/// skill — player "skill" is standing (better R, lower sting, more patience).
/// Every constant lives in <see cref="FencingProfile"/>.
/// </summary>
public static class FencingNegotiation
{
    public static class FencingProfile
    {
        public const double VMin = 200;
        public const double VMax = 800;
        public const double BaseRMin = 0.45;
        public const double BaseRMax = 0.75;
        public const double RMultMin = 0.40;
        public const double RMultMax = 0.85;
        public const double OpenFrac = 0.55;
        public const double ClimbFrac = 0.5;
        public const int PatienceBase = 4;

        /// <summary>No design-doc anchor for the exact "+1 patience" gate — a fence this well-standing grants an extra round, first-pass/HustleHarness-tunable like every other table here.</summary>
        public const int PatienceBonusStanding = 50;

        /// <summary>
        /// The §4.3 neutral autopilot's "fixed factor": accepts once the live
        /// offer has climbed to this multiple of the fence's opening lowball.
        /// No design-doc anchor for the exact multiplier — first-pass/tunable.
        /// </summary>
        public const double NeutralAcceptMultiplier = 1.3;
    }

    /// <summary>Draws the lot's hidden V/R and the fence's opening offer (§4.1) — the only RngState draw in the whole negotiation besides the per-deal sting roll.</summary>
    public static FencingState StartLot(in FencingContext ctx, ref RngState rng)
    {
        double v = FencingProfile.VMin + rng.NextDouble() * (FencingProfile.VMax - FencingProfile.VMin);
        double baseR = FencingProfile.BaseRMin + rng.NextDouble() * (FencingProfile.BaseRMax - FencingProfile.BaseRMin);
        double rMult = Math.Clamp(
            baseR + 0.10 * (ctx.FenceStanding / 100.0), FencingProfile.RMultMin, FencingProfile.RMultMax);
        double r = v * rMult;
        int patience = FencingProfile.PatienceBase + (ctx.FenceStanding >= FencingProfile.PatienceBonusStanding ? 1 : 0);
        double o1 = r * FencingProfile.OpenFrac;

        return new FencingState(
            FencingOutcomeKind.InProgress, v, r, o1, o1, patience, patience,
            DealPrice: 0, FundsDelta: 0, DetectionRiskDelta: 0, StressDelta: 0,
            WatchlistFlag: false, SpoiledGoodsFlag: false);
    }

    /// <summary>§4.1: is this fence wired? Rolled on every closed deal, never on a walk.</summary>
    public static double ComputeStingProbability(in FencingContext ctx) =>
        Math.Clamp(0.04 + 0.10 * ctx.Heat - 0.06 * Math.Max(0, ctx.FenceStanding) / 100.0, 0.01, 0.40);

    /// <summary>Takes the sure offer on the table now.</summary>
    public static FencingState Accept(in FencingState state, in FencingContext ctx, ref RngState rng)
    {
        RequireInProgress(state);
        return CloseDeal(state, in ctx, state.CurrentOffer, ref rng);
    }

    /// <summary>
    /// Names a price. A reasonable/lowball ask (≤ the fence's live willingness)
    /// closes immediately at that price; an aggressive ask makes the fence
    /// raise its offer toward R and burns a round — patience hitting 0 with no
    /// deal is a walk (§4.2/§4.3).
    /// </summary>
    public static FencingState Counter(in FencingState state, in FencingContext ctx, double askPrice, ref RngState rng)
    {
        RequireInProgress(state);

        double fenceCeiling = ComputeFenceCeiling(in state);
        if (askPrice <= fenceCeiling)
        {
            return CloseDeal(state, in ctx, askPrice, ref rng);
        }

        int patience = state.PatienceRemaining - 1;
        if (patience <= 0)
        {
            return state with
            {
                Outcome = FencingOutcomeKind.Walk,
                PatienceRemaining = 0,
                StressDelta = state.StressDelta + 8,
                SpoiledGoodsFlag = true,
            };
        }

        double nextOffer = state.CurrentOffer + FencingProfile.ClimbFrac * (state.HiddenReservation - state.CurrentOffer);
        return state with { CurrentOffer = nextOffer, PatienceRemaining = patience };
    }

    /// <summary>The §4.3 headless autopilot stand-in: accepts once the live offer reaches a fixed multiple of the fence's opening lowball.</summary>
    public static bool NeutralAcceptDecision(in FencingState state) =>
        state.CurrentOffer >= state.InitialOffer * FencingProfile.NeutralAcceptMultiplier;

    /// <summary>§4.2: starts at the current offer (round 1, full patience) and climbs to R as patience burns toward 0.</summary>
    private static double ComputeFenceCeiling(in FencingState state) =>
        state.HiddenReservation - (state.HiddenReservation - state.CurrentOffer)
            * ((double)state.PatienceRemaining / state.InitialPatience);

    private static FencingState CloseDeal(in FencingState state, in FencingContext ctx, double dealPrice, ref RngState rng)
    {
        bool sting = rng.NextDouble() < ComputeStingProbability(in ctx);
        double funds = sting ? 0.5 * dealPrice : dealPrice;
        return state with
        {
            Outcome = FencingOutcomeKind.Deal,
            DealPrice = dealPrice,
            FundsDelta = state.FundsDelta + funds,
            DetectionRiskDelta = state.DetectionRiskDelta + (sting ? 10 : 0),
            WatchlistFlag = state.WatchlistFlag || sting,
        };
    }

    private static void RequireInProgress(in FencingState state)
    {
        if (state.Outcome != FencingOutcomeKind.InProgress)
        {
            throw new InvalidOperationException($"Cannot act on a negotiation already resolved ({state.Outcome}).");
        }
    }
}
