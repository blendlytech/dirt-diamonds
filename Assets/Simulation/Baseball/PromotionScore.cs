using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// The value function's calibration knobs (promotion doc §2, kept as the
/// pipeline's value math by slice 13-6, league_restructure.md §9.1/§9.5) —
/// constants, not literals: retuning any of these is a data edit behind
/// run_monte_carlo_batch and Tools/PipelineHarness, never a logic edit. The
/// retired 9c pass's own knobs (swap margins and caps, the six-tier shape,
/// intake ages, the amateur age-out caps, the record dock) were removed in
/// 13-6; the pipeline's knobs live in <see cref="PipelineProfile"/>.
/// </summary>
public static class PromotionProfile
{
    /// <summary>§2.3 combine weights: A = wP·P + wS·S, wP + wS = 1.</summary>
    public const double PerformanceWeight = 0.5;
    public const double ScoutingWeight = 0.5;

    /// <summary>§2.1 batter reliability shrinkage: w = PA/(PA + this). ~600-PA regulars keep most of their signal; a 3-PA fluke shrinks to league-average.</summary>
    public const int BatterPaShrink = 200;

    /// <summary>§2.1 pitcher reliability shrinkage: w = IP/(IP + this), IP = outs/3.</summary>
    public const double PitcherIpShrink = 40.0;

    /// <summary>
    /// HS-tier batter shrinkage. HS plays a short season (13-3's
    /// TierSeasonCalendar: 10 games), so the shared
    /// <see cref="BatterPaShrink"/> would crush every HS line toward
    /// league-average regardless of merit. Chosen so a full-season HS regular
    /// keeps the same signal fraction a ~600-PA regular keeps under the
    /// shared constant: 110/(110+35) ≈ 600/(600+200) ≈ 0.75.
    /// </summary>
    public const int HsBatterPaShrink = 35;

    /// <summary>
    /// HS-tier pitcher shrinkage, same reasoning: an HS starter throws ~5
    /// complete-game starts ≈ 47 IP (vs ~280 under daily play);
    /// 47/(47+7) ≈ 280/(280+40) ≈ 0.87.
    /// </summary>
    public const double HsPitcherIpShrink = 7.0;

    /// <summary>The tier's batter PA-shrink constant — HS's short season gets its own calibration.</summary>
    public static int BatterPaShrinkFor(LeagueTier tier) =>
        tier == LeagueTier.HS ? HsBatterPaShrink : BatterPaShrink;

    /// <summary>The tier's pitcher IP-shrink constant — HS's short season gets its own calibration.</summary>
    public static double PitcherIpShrinkFor(LeagueTier tier) =>
        tier == LeagueTier.HS ? HsPitcherIpShrink : PitcherIpShrink;

    /// <summary>
    /// §2.1 guard on the raw performance ratio: one season can argue you are
    /// at most this many times league-average. Without it a 1-out 0.00-ERA
    /// scrap of relief work produces an unbounded ratio that no shrinkage
    /// weight can tame.
    /// </summary>
    public const double PerformanceRatioCap = 3.0;

    /// <summary>§2.2 age bonus: +this at <see cref="YoungestProspectAge"/>, 0 at <see cref="PeakAge"/>, mildly negative past it (clamped symmetric).</summary>
    public const double AgeBonusMax = 10.0;
    public const int PeakAge = 27;
    public const int YoungestProspectAge = 15;

    /// <summary>
    /// 9d-2 (development doc §6): the summed role headroom (Σ potential −
    /// current over the role's three ratings) at which a young player's
    /// projection reads the FULL age-scaled bonus — an average raw HS intake
    /// under the first-pass ProspectDiscount lands near it, a half-developed
    /// prospect reads a partial bonus, an at-ceiling player reads zero.
    /// </summary>
    public const int HeadroomForFullProjection = 30;

    /// <summary>
    /// The HS senior age: four HS seasons, freshman 16 → senior 19
    /// (<see cref="SchoolGrades.SeniorAge"/> reads it). A senior graduates at
    /// the rollover after his senior season (13-6: recruited, or retired from
    /// baseball when no program signs him; the avatar always moves on, D4a-A).
    /// </summary>
    public const int HighSchoolAgeCap = 19;
}

/// <summary>
/// Pure, engine-free value math (promotion doc §2; the pipeline's value
/// function since 13-6, <see cref="PipelineRules"/>) — the <see cref="HeirGenetics"/> profile: deterministic
/// cores the harness pins fixtures against with no database and no RNG.
/// P (performance) is tier-relative because each player's line was produced
/// against tier-appropriate competition (the §9 environment argument); S
/// (scouting) is the role-summed raw ratings plus a small age projection.
/// Both are reported on a 100-centred scale so the §2.3 combine is a plain
/// weighted sum.
/// </summary>
public static class PromotionScore
{
    private const double Epsilon = 1e-6;

    /// <summary>
    /// OPS from a counting line with the exact SqlNormalizeBattingRates
    /// formula (OBP = (H+BB)/(AB+BB), SLG = (H + 2B + 2·3B + 3·HR)/AB), so the
    /// score never depends on whether the rate denormalization has run.
    /// </summary>
    public static double Ops(in SeasonBattingLine line)
    {
        double obp = line.Ab + line.Bb > 0 ? (double)(line.H + line.Bb) / (line.Ab + line.Bb) : 0.0;
        double slg = line.Ab > 0 ? (double)(line.H + line.Doubles + 2 * line.Triples + 3 * line.Hr) / line.Ab : 0.0;
        return obp + slg;
    }

    /// <summary>League OPS from tier season totals — the same formula as <see cref="Ops"/>, over the sums.</summary>
    public static double LeagueOps(in LeagueBattingTotals totals)
    {
        double obp = totals.Ab + totals.Bb > 0 ? (double)(totals.H + totals.Bb) / (totals.Ab + totals.Bb) : 0.0;
        double slg = totals.Ab > 0 ? (double)(totals.H + totals.Doubles + 2 * totals.Triples + 3 * totals.Hr) / totals.Ab : 0.0;
        return obp + slg;
    }

    /// <summary>League ERA from tier season totals (ERA = 27·ER/outs, the SqlNormalizePitchingRates formula).</summary>
    public static double LeagueEra(in LeaguePitchingTotals totals) =>
        totals.OutsRecorded > 0 ? 27.0 * totals.Er / totals.OutsRecorded : 0.0;

    /// <summary>
    /// §2.1 batter performance on the 100-centred scale: an OPS+-style ratio
    /// against the tier's league average, reliability-shrunk toward 100 by
    /// PA/(PA+k). No line (or no league season) reads exactly 100 — a fresh
    /// intake or an outside recruit (no games yet) reads neutral.
    /// </summary>
    public static double BatterPerformance(in SeasonBattingLine line, double leagueOps) =>
        BatterPerformance(in line, leagueOps, PromotionProfile.BatterPaShrink);

    /// <summary>Shrink-parameterized form — the pipeline passes the tier's own constant (<see cref="PromotionProfile.BatterPaShrinkFor"/>).</summary>
    public static double BatterPerformance(in SeasonBattingLine line, double leagueOps, int paShrink)
    {
        double rawP = line.Pa > 0 && leagueOps > Epsilon
            ? Math.Clamp(Ops(in line) / leagueOps, 0.0, PromotionProfile.PerformanceRatioCap)
            : 1.0;
        double w = line.Pa / (double)(line.Pa + paShrink);
        return 100.0 * (1.0 + w * (rawP - 1.0));
    }

    /// <summary>
    /// §2.1 pitcher performance: ERA−-style (league ERA over player ERA, so a
    /// lower ERA scores higher), capped and reliability-shrunk by IP/(IP+k).
    /// WHIP is a disclosed first-pass-unused secondary.
    /// </summary>
    public static double PitcherPerformance(int outsRecorded, int er, double leagueEra) =>
        PitcherPerformance(outsRecorded, er, leagueEra, PromotionProfile.PitcherIpShrink);

    /// <summary>Shrink-parameterized form — the pipeline passes the tier's own constant (<see cref="PromotionProfile.PitcherIpShrinkFor"/>).</summary>
    public static double PitcherPerformance(int outsRecorded, int er, double leagueEra, double ipShrink)
    {
        double rawP = 1.0;
        if (outsRecorded > 0 && leagueEra > Epsilon)
        {
            double era = 27.0 * er / outsRecorded;
            rawP = Math.Clamp(leagueEra / Math.Max(era, Epsilon), 0.0, PromotionProfile.PerformanceRatioCap);
        }
        double ip = outsRecorded / 3.0;
        double w = ip / (ip + ipShrink);
        return 100.0 * (1.0 + w * (rawP - 1.0));
    }

    /// <summary>
    /// §2.2 age projection: a modest, monotonically non-increasing linear
    /// taper — up to +max at 15, 0 at peak (27), mildly negative past it.
    /// Since 9d-2 this is the LEGACY fallback for callers with no potential
    /// in hand: it equals <see cref="ProjectionBonus"/> under an implicit
    /// full-headroom assumption (the pre-9d fudge — young players were
    /// presumed projectable because nothing measured their ceiling). The
    /// pipeline scores through ProjectionBonus with real headroom.
    /// </summary>
    public static double AgeBonus(int age)
    {
        double slope = PromotionProfile.AgeBonusMax
            / (PromotionProfile.PeakAge - PromotionProfile.YoungestProspectAge);
        return Math.Clamp((PromotionProfile.PeakAge - age) * slope,
            -PromotionProfile.AgeBonusMax, PromotionProfile.AgeBonusMax);
    }

    /// <summary>
    /// 9d-2 (development doc §6): <see cref="AgeBonus"/> evolved — the
    /// scouting projection now reflects REAL remaining headroom instead of
    /// the age-only fudge. The upside is the same age-tapered bonus, scaled
    /// by how much ceiling actually remains (saturating at
    /// <see cref="PromotionProfile.HeadroomForFullProjection"/>): a young
    /// player far below his ceiling projects up in full, one already at his
    /// ceiling projects flat. Past the peak the age-driven decline projection
    /// stands untouched — decline is coming regardless of paper headroom.
    /// ProjectionBonus(age, headroom ≥ saturation) ≡ AgeBonus(age); same
    /// scale, same clamp, same profile constants.
    /// </summary>
    public static double ProjectionBonus(int age, int headroom)
    {
        double ageBonus = AgeBonus(age);
        if (ageBonus <= 0.0)
        {
            return ageBonus;
        }
        double saturation = Math.Clamp(
            headroom / (double)PromotionProfile.HeadroomForFullProjection, 0.0, 1.0);
        return ageBonus * saturation;
    }

    /// <summary>
    /// §6 the quantity <see cref="ProjectionBonus"/> consumes: Σ max(0,
    /// potential − current) over the role's three ratings. current ≤
    /// potential is the 9d world invariant, so the max() is defensive; a
    /// missing potential row reads zero headroom at the call sites (the v10
    /// backfill semantics — potential = current, decline-only).
    /// </summary>
    public static int Headroom(in RosterPlayerRow current, in PlayerPotentialRow potential, bool isPitcher) =>
        isPitcher
            ? Math.Max(0, potential.PitStuff - current.PitStuff)
                + Math.Max(0, potential.PitControl - current.PitControl)
                + Math.Max(0, potential.PitStamina - current.PitStamina)
            : Math.Max(0, potential.BatPower - current.BatPower)
                + Math.Max(0, potential.BatContact - current.BatContact)
                + Math.Max(0, potential.BatDiscipline - current.BatDiscipline);

    /// <summary>
    /// §2.2 scouting on the 100-centred scale: the role-summed raw ratings
    /// (batter Power+Contact+Discipline, pitcher Stuff+Control+Stamina — the
    /// exact metric FindDisplacedPlayer/EvaluateSuccession already rank by,
    /// 150 = all-average) plus the age projection, over 150. This age-only
    /// form stays for callers with no potential in hand (≡ full headroom).
    /// </summary>
    public static double Scouting(int roleRatingSum, int age) =>
        100.0 * (roleRatingSum + AgeBonus(age)) / 150.0;

    /// <summary>The 9d-2 headroom-aware form (development doc §6) the pipeline scores through: scouting projects the player's actual remaining ceiling.</summary>
    public static double Scouting(int roleRatingSum, int age, int headroom) =>
        100.0 * (roleRatingSum + ProjectionBonus(age, headroom)) / 150.0;

    /// <summary>§2.3: A = wP·P + wS·S. Ties break on player_id at every call site.</summary>
    public static double Combine(double performance, double scouting) =>
        PromotionProfile.PerformanceWeight * performance + PromotionProfile.ScoutingWeight * scouting;
}
