using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.UI.Scouting;

/// <summary>
/// Phase 10c (presentation_layer_narrative.md §5.2): the eight-band scout
/// letter grade. Centered so 50 (league average, per BaseballDtos'
/// PlayerRatingsRow convention) reads C+ and 40 (the 8c replacement-level
/// call-up floor) reads one grade below. Deliberately Godot-free/pure so
/// Tools/MonteCarloHarness can compile and fixture-pin it directly alongside
/// the PromotionScore math it reads from (§5.1 — "reuses these verbatim").
/// </summary>
public enum GradeLetter
{
    F,
    D,
    C,
    CPlus,
    B,
    BPlus,
    A,
    APlus,
}

public static class ScoutingGrade
{
    /// <summary>
    /// §5.2's band table. Open-ended at both tails (a >100 OFP from a
    /// saturated young prospect still reads A+; sub-zero never occurs given
    /// the schema's [0,100] CHECK but would read F) — no clamp needed.
    /// </summary>
    public static GradeLetter Grade(int rating) => rating switch
    {
        >= 90 => GradeLetter.APlus,
        >= 80 => GradeLetter.A,
        >= 70 => GradeLetter.BPlus,
        >= 60 => GradeLetter.B,
        >= 50 => GradeLetter.CPlus,
        >= 40 => GradeLetter.C,
        >= 30 => GradeLetter.D,
        _ => GradeLetter.F,
    };

    public static string Label(GradeLetter grade) => grade switch
    {
        GradeLetter.APlus => "A+",
        GradeLetter.A => "A",
        GradeLetter.BPlus => "B+",
        GradeLetter.B => "B",
        GradeLetter.CPlus => "C+",
        GradeLetter.C => "C",
        GradeLetter.D => "D",
        _ => "F",
    };

    /// <summary>Convenience: Grade(rating) rendered straight to its letter string.</summary>
    public static string Label(int rating) => Label(Grade(rating));

    /// <summary>
    /// §5.3's OFP headline. Disclosed finding: <see cref="PromotionScore.Scouting(int, int, int)"/>
    /// is deliberately 100-centred (Combine's own internal ranking
    /// convention — a peak-age, zero-headroom, exactly-league-average role
    /// sum of 150 scores exactly 100.0), NOT the 0-100/50-average scale
    /// Grade()'s bands are calibrated to. Read literally, the doc's
    /// "Grade(round(Scouting(...)))" would over-grade nearly every rostered
    /// player (any role-average rating past ~15 already clears the A+ floor
    /// at peak age) — a doc/implementation scale mismatch, not a resolver
    /// bug (Scouting is verbatim-correct for Combine's own ranking use).
    /// Halving recenters it exactly: at peak age with zero headroom,
    /// Scouting = 2·roleAvgRating, so Scouting/2 lands back on Grade's
    /// 50-average domain precisely, with ProjectionBonus still nudging
    /// young/high-headroom prospects up the same way it does for the real
    /// sweep. Flagged for Fable/Opus review — not silently patched into
    /// PromotionScore itself.
    /// </summary>
    public static int OfpRating(int roleRatingSum, int age, int headroom) =>
        (int)Math.Round(PromotionScore.Scouting(roleRatingSum, age, headroom) / 2.0, MidpointRounding.AwayFromZero);

    public static GradeLetter OfpGrade(int roleRatingSum, int age, int headroom) =>
        Grade(OfpRating(roleRatingSum, age, headroom));
}
