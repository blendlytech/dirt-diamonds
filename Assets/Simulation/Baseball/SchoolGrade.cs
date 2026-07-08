namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// A High-School amateur's academic year. The avatar enters as a
/// <see cref="Freshman"/> at <see cref="CareerManager.StartingAge"/> (16) and
/// advances one grade per aged season until <see cref="Senior"/>, after which
/// it competes to graduate up to College on merit through the
/// <see cref="PromotionManager"/> grade gate — earn the promotion and move on,
/// or be held back another year (a below-cut senior does not reach college
/// baseball). Display + gating flavor only — grade is DERIVED from age, never
/// stored, so there is no schema surface and nothing to migrate.
/// </summary>
public enum SchoolGrade : byte
{
    Freshman = 0,
    Sophomore = 1,
    Junior = 2,
    Senior = 3,
}

/// <summary>
/// Pure, engine-free age → <see cref="SchoolGrade"/> math, the
/// <see cref="PromotionScore"/>/<see cref="DevelopmentCurve"/> profile: a
/// deterministic core with no database and no RNG, so the harness can pin a
/// fixture against it. Grade is anchored on the two existing career constants
/// so it can never drift out of step with them: the freshman year is
/// <see cref="CareerManager.StartingAge"/> (the avatar's creation age) and the
/// senior year is <see cref="PromotionProfile.HighSchoolAgeCap"/> (the amateur
/// age cap the promotion pass already washes NPCs out at). Retuning either
/// constant reshapes the grade ladder automatically.
/// </summary>
public static class SchoolGrades
{
    /// <summary>The freshman (entry) age — the avatar's creation age.</summary>
    public const int FreshmanAge = CareerManager.StartingAge;

    /// <summary>The senior age — the last amateur year before graduation, reusing the HS age cap.</summary>
    public const int SeniorAge = PromotionProfile.HighSchoolAgeCap;

    /// <summary>Number of distinct grades (freshman … senior). Exactly four when the anchors are 16/19.</summary>
    public const int Count = SeniorAge - FreshmanAge + 1;

    /// <summary>
    /// The grade an HS amateur of <paramref name="age"/> is in. Clamped: a
    /// below-freshman age (a young NPC intake, defensively) reads Freshman, and
    /// a past-senior age (a graduating-but-not-yet-moved avatar the offseason
    /// it turns <see cref="SeniorAge"/>+1) reads Senior — the last grade it
    /// held before graduating.
    /// </summary>
    public static SchoolGrade ForAge(int age) =>
        (SchoolGrade)System.Math.Clamp(age - FreshmanAge, 0, Count - 1);
}
