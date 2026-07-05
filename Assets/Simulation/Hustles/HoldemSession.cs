using System;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Simulation.Hustles;

/// <summary>Snapshot inputs for one Hold'em session (docs/design/hustles_texas_holdem.md §9) — simpler than Narcotics'/Fencing's contexts: no faction reps at all (§1).</summary>
public readonly struct HoldemContext
{
    public readonly double Funds;

    /// <summary>detection_risk / 100.</summary>
    public readonly double Heat;

    /// <summary>recklessness / 100 — feeds the hero autopilot's TAG→Maniac interpolation (§7.3).</summary>
    public readonly double Reck;

    public HoldemContext(double funds, double heat, double reck)
    {
        Funds = funds;
        Heat = heat;
        Reck = reck;
    }
}

/// <summary>
/// Accumulated bookkeeping for one session (§9): buy-in → hands → stand-up.
/// Owns the one pooled <see cref="HoldemHandState"/> reused for every hand
/// this session plays (§10's "one instance reused across the whole session").
/// A class (not the record-struct pattern the other hustles use) because it
/// must hold a live reference to that pooled table, not copy it.
/// </summary>
public sealed class HoldemSessionState
{
    public readonly HoldemHandState Table = new();
    public StakesTier Tier;
    public long BuyIn;
    public int SeatCount;
    public int HeroSeat;
    public int HandsPlayed;
    public bool StoodUp;
    public bool Busted;
    public bool Raided;

    public double FundsDelta;
    public int DetectionRiskDelta;
    public int RecklessnessDelta;
    public double StressDelta;
    public bool SetGamblingBustFlag;

    public bool IsOver => StoodUp || Busted || Raided;

    public HustleResolution ToResolution() => new(
        FundsDelta, DetectionRiskDelta, healthCeilingDelta: 0, RecklessnessDelta, StressDelta,
        supplierTrustDelta: 0, crewStandingDelta: 0,
        setWatchlistFlag: false, setBadProductFlag: false, setSpoiledGoodsFlag: false, setControlsTurfFlag: false,
        setGamblingBustFlag: SetGamblingBustFlag);
}

/// <summary>
/// Pure resolver for the Hold'em session lifecycle (§9): validates and seats
/// a buy-in, folds each completed hand's stack change into session
/// bookkeeping, rolls the per-hand raid hazard, and projects the terminal
/// state into the shared <see cref="HustleResolution"/>. A hand itself is
/// driven by <see cref="HoldemHandState.StartHand"/>/<see cref="HoldemHandState.SubmitHeroAction"/>
/// directly (the UI, or <see cref="PlayAutopilotHand"/> for the harness) —
/// this class only owns what happens between hands.
/// </summary>
public static class HoldemSession
{
    /// <summary>§9: validates <paramref name="buyIn"/> ∈ [20·BB, min(funds,100·BB)], seats the field from the tier's §7.1 skew, and sets the button.</summary>
    public static HoldemSessionState StartSession(in HoldemContext ctx, StakesTier tier, long buyIn, int numOpponents, ref RngState rng)
    {
        if (numOpponents < 1 || numOpponents > HoldemHandState.MaxSeats - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(numOpponents), numOpponents, $"numOpponents must be in [1,{HoldemHandState.MaxSeats - 1}].");
        }

        HoldemProfile.StakesTierProfile tierProfile = HoldemProfile.GetTier(tier);
        long maxBuyIn = Math.Min((long)ctx.Funds, tierProfile.BuyInMax);
        if (buyIn < tierProfile.BuyInMin || buyIn > maxBuyIn)
        {
            throw new ArgumentOutOfRangeException(nameof(buyIn), buyIn, $"Buy-in must be in [{tierProfile.BuyInMin}, {maxBuyIn}].");
        }

        int seatCount = numOpponents + 1;
        const int heroSeat = 0;
        var session = new HoldemSessionState { Tier = tier, BuyIn = buyIn, SeatCount = seatCount, HeroSeat = heroSeat };

        Span<HoldemArchetype> archetypes = stackalloc HoldemArchetype[seatCount];
        Span<long> stacks = stackalloc long[seatCount];
        archetypes[heroSeat] = HoldemArchetype.TAG; // unused by the interactive contract; a harmless placeholder
        stacks[heroSeat] = buyIn;
        double[] skew = HoldemProfile.GetFieldSkew(tier);
        for (int i = 0; i < seatCount; i++)
        {
            if (i == heroSeat)
            {
                continue;
            }
            archetypes[i] = DrawArchetype(skew, ref rng);
            stacks[i] = tierProfile.BuyInMax;
        }

        session.Table.ConfigureSeats(seatCount, archetypes, heroSeat, stacks, buttonSeat: 0, tierProfile.SmallBlind, tierProfile.BigBlind);

        if (tier != StakesTier.Low)
        {
            session.DetectionRiskDelta += HoldemProfile.BaselineDetectionMidHigh;
        }
        return session;
    }

    /// <summary>Call once a hand started via <see cref="HoldemHandState.StartHand"/>/<see cref="HoldemHandState.SubmitHeroAction"/> reaches <see cref="HoldemHandState.HandComplete"/> — folds bust/raid/button-rotation/rebuy bookkeeping (§9).</summary>
    public static void CompleteHand(HoldemSessionState session, in HoldemContext ctx, ref RngState rng)
    {
        if (!session.Table.HandComplete)
        {
            throw new InvalidOperationException("CompleteHand called before the hand resolved.");
        }
        session.HandsPlayed++;

        if (session.Table.Stack[session.HeroSeat] <= 0)
        {
            session.Busted = true;
            session.FundsDelta = -session.BuyIn;
            ApplyFinalVarianceDeltas(session);
            return;
        }

        HoldemProfile.StakesTierProfile tierProfile = HoldemProfile.GetTier(session.Tier);
        double h = session.Tier == StakesTier.Low
            ? 0.0
            : Math.Clamp(tierProfile.RaidHazardBase + HoldemProfile.RaidHazardHeatCoefficient * ctx.Heat, 0, HoldemProfile.RaidHazardCap);

        if (h > 0 && rng.NextDouble() < h)
        {
            session.Raided = true;
            session.FundsDelta = -session.BuyIn;
            session.DetectionRiskDelta += HoldemProfile.RaidDetectionDelta;
            session.StressDelta += HoldemProfile.RaidStressDelta;
            session.SetGamblingBustFlag = true;
            return;
        }

        session.Table.RotateButton();
        RebuyOpponents(session, in tierProfile);
    }

    /// <summary>§9: bank &amp; exit — takes the current stack and goes. <c>FundsDelta = stackReturned − buyIn</c>, a single net delta.</summary>
    public static HustleResolution StandUp(HoldemSessionState session)
    {
        if (session.IsOver)
        {
            throw new InvalidOperationException("Cannot stand up — the session is already over.");
        }
        session.FundsDelta = session.Table.Stack[session.HeroSeat] - session.BuyIn;
        ApplyFinalVarianceDeltas(session);
        session.StoodUp = true;
        return session.ToResolution();
    }

    /// <summary>
    /// Harness/autopilot convenience: drives one full hand end to end
    /// (StartHand → opponents + the hero via <paramref name="heroProfile"/> →
    /// CompleteHand). A UI drives the identical StartHand/SubmitHeroAction/
    /// CompleteHand sequence itself instead, at the player's own pace.
    /// </summary>
    public static void PlayAutopilotHand(HoldemSessionState session, in HoldemContext ctx, in HoldemProfile.ArchetypeTunables? heroProfile, ref RngState rng)
    {
        session.Table.StartHand(ref rng);
        while (session.Table.AwaitingHero)
        {
            if (heroProfile is null)
            {
                throw new InvalidOperationException("The hero seat is awaiting a decision but no heroProfile was supplied for autopilot.");
            }
            HeroAction action = HoldemAgent.DecideAction(session.Table, session.HeroSeat, heroProfile.Value, ref rng);
            session.Table.SubmitHeroAction(action, ref rng);
        }
        CompleteHand(session, in ctx, ref rng);
    }

    private static void ApplyFinalVarianceDeltas(HoldemSessionState session)
    {
        if (session.FundsDelta < 0)
        {
            double lossFraction = Math.Clamp(-session.FundsDelta / session.BuyIn, 0, 1);
            session.StressDelta += HoldemProfile.LossStressCoefficient * lossFraction;
        }
        else if (session.FundsDelta >= session.BuyIn)
        {
            session.RecklessnessDelta += 1;
        }
    }

    private static void RebuyOpponents(HoldemSessionState session, in HoldemProfile.StakesTierProfile tierProfile)
    {
        for (int i = 0; i < session.SeatCount; i++)
        {
            if (i == session.HeroSeat)
            {
                continue;
            }
            if (session.Table.Stack[i] < tierProfile.BigBlind)
            {
                session.Table.SetStack(i, tierProfile.BuyInMax);
            }
        }
    }

    private static HoldemArchetype DrawArchetype(double[] skew, ref RngState rng)
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
}
