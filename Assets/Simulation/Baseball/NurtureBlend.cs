using System;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Pure nurture blending for heir development
/// (docs/design/high_school_person_layer.md §7): folds the accumulated
/// Child_Development axes into the fixed HeirGenetics nature roll at
/// maturity/takeover. Same Data-clean, zero-DB profile as
/// <see cref="HeirGenetics"/> — deals only in <see cref="ChildDevelopmentRow"/>
/// and ints, never a connection; every calibration knob lives in
/// <see cref="NurtureProfile"/> so tuning is a data edit.
///
/// The §7.3 identity contract is load-bearing: at neutral axes
/// (care 50 / coaching 50 / funding 50 / neglect 0) both deltas are exactly 0,
/// so "no Child_Development row" and "a neutral row" produce the identical
/// pure-nature heir — existing heirs, NPC children, and every pre-HS-5
/// MonteCarloHarness succession fixture stay valid by construction.
/// </summary>
public static class NurtureBlend
{
    /// <summary>
    /// First-pass §7.2 constants (tunable data, the HeirGeneticsProfile
    /// precedent). Nature dominates by design: the potential cap is a real
    /// career lever but cannot manufacture a star; the interest cap is the
    /// failure engine — nurture can cross the willing threshold (40) in
    /// either direction.
    /// </summary>
    public static class NurtureProfile
    {
        /// <summary>Max ± nudge nurture applies to every Potential rating (§7.2).</summary>
        public const int PotentialNurtureCap = 8;

        /// <summary>Coaching leads the potential blend (§7.2).</summary>
        public const double CoachWeight = 0.5;

        /// <summary>Funding supports it (§7.2).</summary>
        public const double FundWeight = 0.3;

        /// <summary>Neglect bites — it drags BOTH potential and interest (§7.1, the failure axis).</summary>
        public const double NeglectWeight = 0.5;

        /// <summary>Max ± nudge nurture applies to baseball_interest (§7.2).</summary>
        public const int InterestNurtureCap = 20;

        /// <summary>The neutral resting point of care/coaching/funding — the schema default.</summary>
        public const int NeutralAxis = 50;
    }

    /// <summary>
    /// §7.2: the one zero-centred potential delta applied identically to every
    /// rating. Round away-from-zero then clamp to ±cap — the HeirGenetics
    /// rounding discipline (the harness fixtures depend on this mode).
    /// </summary>
    public static int PotentialDelta(int coaching, int funding, int neglect)
    {
        double raw = NurtureProfile.PotentialNurtureCap * (
            NurtureProfile.CoachWeight * (coaching - NurtureProfile.NeutralAxis) / 50.0
            + NurtureProfile.FundWeight * (funding - NurtureProfile.NeutralAxis) / 50.0
            - NurtureProfile.NeglectWeight * (neglect / 100.0));
        return Math.Clamp(RoundAwayFromZero(raw),
            -NurtureProfile.PotentialNurtureCap, NurtureProfile.PotentialNurtureCap);
    }

    /// <summary>§7.2: the zero-centred interest delta — care builds it, neglect burns it.</summary>
    public static int InterestDelta(int care, int neglect)
    {
        double raw = NurtureProfile.InterestNurtureCap * (
            (care - NurtureProfile.NeutralAxis) / 50.0 - neglect / 100.0);
        return Math.Clamp(RoundAwayFromZero(raw),
            -NurtureProfile.InterestNurtureCap, NurtureProfile.InterestNurtureCap);
    }

    /// <summary>Row-shaped conveniences for the succession hook.</summary>
    public static int PotentialDelta(in ChildDevelopmentRow dev) =>
        PotentialDelta(dev.Coaching, dev.Funding, dev.Neglect);

    public static int InterestDelta(in ChildDevelopmentRow dev) =>
        InterestDelta(dev.Care, dev.Neglect);

    /// <summary>§7.2: <c>final_interest = clamp(nature + Δ, 0, 100)</c>.</summary>
    public static int FinalInterest(int natureInterest, in ChildDevelopmentRow dev) =>
        Math.Clamp(natureInterest + InterestDelta(in dev), 0, 100);

    /// <summary>
    /// §7.2 applied to a whole Potential row: the SAME delta shifts every
    /// rating (one household raised one kid — coaching wasn't per-tool),
    /// each clamped back into the schema's [0,100].
    /// </summary>
    public static PlayerPotentialRow ApplyToPotential(in PlayerPotentialRow nature, int potentialDelta) => new()
    {
        PlayerId = nature.PlayerId,
        BatPower = ClampRating(nature.BatPower + potentialDelta),
        BatContact = ClampRating(nature.BatContact + potentialDelta),
        BatDiscipline = ClampRating(nature.BatDiscipline + potentialDelta),
        PitStuff = ClampRating(nature.PitStuff + potentialDelta),
        PitControl = ClampRating(nature.PitControl + potentialDelta),
        PitStamina = ClampRating(nature.PitStamina + potentialDelta),
        Fielding = ClampRating(nature.Fielding + potentialDelta),
    };

    /// <summary>
    /// The neutral row (§7.3): what <see cref="CareerManager.Succeed"/> resets
    /// a folded heir's axes to, so any hypothetical re-fold is a provable
    /// no-op by the identity contract — fold-once without a delete path.
    /// </summary>
    public static ChildDevelopmentRow NeutralRow(string childId, int day) => new()
    {
        ChildId = childId,
        Care = NurtureProfile.NeutralAxis,
        Coaching = NurtureProfile.NeutralAxis,
        Funding = NurtureProfile.NeutralAxis,
        Neglect = 0,
        LastTickDay = day,
    };

    private static int RoundAwayFromZero(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static int ClampRating(int value) => Math.Clamp(value, 0, 100);
}
