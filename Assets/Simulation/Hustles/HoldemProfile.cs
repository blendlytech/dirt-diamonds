using System;

namespace DirtAndDiamonds.Simulation.Hustles;

/// <summary>The five fixed opponent archetypes (docs/design/hustles_texas_holdem.md §7.1) — also the pool the hero autopilot (§7.3) and harness EV-band policies (§13) draw their tunables from.</summary>
public enum HoldemArchetype : byte { Nit, TAG, LAG, Station, Maniac }

/// <summary>The hero's session-level stakes choice (§3) — controls blinds/buy-in/field quality/raid hazard.</summary>
public enum StakesTier : byte { Low, Mid, High }

/// <summary>
/// Shared tunable constants for the Hold'em resolvers (docs/design/hustles_texas_holdem.md).
/// Retuning is a data edit here, never a logic edit — the same convention as
/// <see cref="NarcoticsHustle.NarcoticsProfile"/>/<see cref="FencingNegotiation.FencingProfile"/>.
/// 8d-1 populated only the equity-engine knob; 8d-2 adds the betting/rake/
/// archetype/session/raid constants (§3, §7.1, §9).
/// </summary>
public static class HoldemProfile
{
    /// <summary>§5.1: MC sample count for in-decision opponent/hero equity estimates — accuracy ±~3% is plenty for a fold/call threshold.</summary>
    public const int EquitySamplesDefault = 300;

    // ------------------------------------------------------------------
    // §3 stakes tiers — blinds, buy-in bounds (100 BB max / 20 BB min), and
    // the per-hand raid hazard base (Low is hardcoded to 0 at the call site,
    // never via this table — "a friendly home game never gets raided").
    // ------------------------------------------------------------------

    public readonly struct StakesTierProfile
    {
        public readonly long SmallBlind;
        public readonly long BigBlind;
        public readonly long BuyInMax;
        public readonly long BuyInMin;
        public readonly double RaidHazardBase;

        public StakesTierProfile(long smallBlind, long bigBlind, double raidHazardBase)
        {
            SmallBlind = smallBlind;
            BigBlind = bigBlind;
            BuyInMax = bigBlind * 100;
            BuyInMin = bigBlind * 20;
            RaidHazardBase = raidHazardBase;
        }
    }

    public static StakesTierProfile GetTier(StakesTier tier) => tier switch
    {
        StakesTier.Low => new StakesTierProfile(1, 2, 0.000),
        StakesTier.Mid => new StakesTierProfile(5, 10, 0.010),
        StakesTier.High => new StakesTierProfile(25, 50, 0.030),
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null),
    };

    /// <summary>§9: raid hazard heat coefficient (heat ∈ [0,1]) — Mid/High only; Low is always exactly 0 regardless of heat.</summary>
    public const double RaidHazardHeatCoefficient = 0.05;
    public const double RaidHazardCap = 0.25;

    /// <summary>§9: baseline detection risk from merely playing an underground Mid/High game — applied once per session; Low adds none.</summary>
    public const int BaselineDetectionMidHigh = 1;

    /// <summary>§9: the raid consequence — seizes the on-table stack (applied by the caller) plus these deltas.</summary>
    public const int RaidDetectionDelta = 10;
    public const double RaidStressDelta = 20;

    /// <summary>§9: a losing stand-up tilts stress in proportion to the loss fraction of the buy-in (max loss = the whole buy-in, no markers); a high-variance win (≥ doubling up) nudges recklessness — "the gambling high".</summary>
    public const double LossStressCoefficient = 10.0;

    // ------------------------------------------------------------------
    // §3: rake — 5%, capped at 3·BB, taken only on pots that saw a flop
    // ("no flop, no drop").
    // ------------------------------------------------------------------

    public const double RakeFraction = 0.05;
    public const double RakeCapInBigBlinds = 3.0;

    // ------------------------------------------------------------------
    // §7.1: archetype tunables — preflop entry-equity gate, value margin,
    // bluff-frequency multiplier (× the §6.3 GTO ratio), and bet size (× pot).
    // ------------------------------------------------------------------

    public readonly struct ArchetypeTunables
    {
        public readonly double PreflopEntryEquity;
        public readonly double ValueMargin;
        public readonly double BluffMult;
        public readonly double BetSizeFrac;

        public ArchetypeTunables(double preflopEntryEquity, double valueMargin, double bluffMult, double betSizeFrac)
        {
            PreflopEntryEquity = preflopEntryEquity;
            ValueMargin = valueMargin;
            BluffMult = bluffMult;
            BetSizeFrac = betSizeFrac;
        }
    }

    public static ArchetypeTunables GetArchetype(HoldemArchetype archetype) => archetype switch
    {
        HoldemArchetype.Nit => new ArchetypeTunables(0.62, 0.15, 0.4, 0.50),
        HoldemArchetype.TAG => new ArchetypeTunables(0.55, 0.10, 1.0, 0.66),
        HoldemArchetype.LAG => new ArchetypeTunables(0.48, 0.06, 1.4, 0.75),
        HoldemArchetype.Station => new ArchetypeTunables(0.44, 0.20, 0.2, 0.50),
        HoldemArchetype.Maniac => new ArchetypeTunables(0.40, 0.02, 2.2, 1.00),
        _ => throw new ArgumentOutOfRangeException(nameof(archetype), archetype, null),
    };

    /// <summary>§7.1 field skew by stakes — weights over [Nit, TAG, LAG, Station, Maniac], each summing to 1.0. Low is soft (Station/Maniac/LAG-heavy); High is sharp (TAG/Nit-heavy); Mid is balanced.</summary>
    public static readonly double[] FieldSkewLow = { 0.05, 0.10, 0.25, 0.35, 0.25 };
    public static readonly double[] FieldSkewMid = { 0.20, 0.20, 0.20, 0.20, 0.20 };
    public static readonly double[] FieldSkewHigh = { 0.30, 0.35, 0.20, 0.05, 0.10 };

    public static double[] GetFieldSkew(StakesTier tier) => tier switch
    {
        StakesTier.Low => FieldSkewLow,
        StakesTier.Mid => FieldSkewMid,
        StakesTier.High => FieldSkewHigh,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null),
    };
}
