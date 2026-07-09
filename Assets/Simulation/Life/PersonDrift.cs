using System;

namespace DirtAndDiamonds.Simulation.Life;

/// <summary>
/// The twelve integer person stats of the schema-v11 Player_Person row
/// (docs/design/high_school_person_layer.md §2), in the row's own column order
/// (gpa excluded — it is REAL and moves only through the §2.2 weekly drift,
/// never through the per-hour effect channel). CONTRACT: these ordinals are
/// mirrored by PersonQueries.AdjustStat's column table on the Data side (the
/// RelationshipKind/RelationshipType mirror precedent — this folder is
/// compiled Data-free by Tools/NeedsDecayHarness) and by
/// PersonStatImpulseEvent's raw int payload; Tools/SchemaValidator pins the
/// ordinal→column round-trip so the two sides can never silently diverge.
/// </summary>
public enum PersonStatId
{
    Intelligence = 0,
    Maturity = 1,
    Happiness = 2,
    Charisma = 3,
    Confidence = 4,
    Reputation = 5,
    SocialStatus = 6,
    Attractiveness = 7,
    Teamwork = 8,
    Morality = 9,
    Discipline = 10,
    WorkEthic = 11,
}

/// <summary>
/// One person-stat nudge carried by an action definition (HS-4's effect
/// channel). Deltas are PER HOUR of the activity: schedule blocks apply them
/// per ticked hour (the RestoreAmount proration mirror), and a one-shot
/// autopilot application scales by the action's nominal TemporalCostHours.
/// </summary>
public readonly struct PersonStatEffect
{
    public readonly PersonStatId Stat;
    public readonly float DeltaPerHour;

    public PersonStatEffect(PersonStatId stat, float deltaPerHour)
    {
        Stat = stat;
        DeltaPerHour = deltaPerHour;
    }
}

/// <summary>
/// In-memory person-stat vector (the NeedsState shape for the person layer):
/// the twelve <see cref="PersonStatId"/> scalars as floats plus GPA as double.
/// Floats deliberately — per-hour effect deltas are fractional and the
/// DB-side INTEGER columns only ever receive the accumulated whole part
/// (LifeSimManager's settle bookkeeping carries the fraction forward). Also
/// doubles as the unflushed-delta accumulator, where every field starts 0.
/// </summary>
public struct PersonStats
{
    public const int StatCount = 12;

    public double Gpa;
    public float Intelligence;
    public float Maturity;
    public float Happiness;
    public float Charisma;
    public float Confidence;
    public float Reputation;
    public float SocialStatus;
    public float Attractiveness;
    public float Teamwork;
    public float Morality;
    public float Discipline;
    public float WorkEthic;

    /// <summary>The schema's neutral row (all 50, gpa 2.5) — the v11 backfill default.</summary>
    public static PersonStats Neutral() => new()
    {
        Gpa = 2.5,
        Intelligence = 50f,
        Maturity = 50f,
        Happiness = 50f,
        Charisma = 50f,
        Confidence = 50f,
        Reputation = 50f,
        SocialStatus = 50f,
        Attractiveness = 50f,
        Teamwork = 50f,
        Morality = 50f,
        Discipline = 50f,
        WorkEthic = 50f,
    };

    public readonly float Get(PersonStatId stat) => stat switch
    {
        PersonStatId.Intelligence => Intelligence,
        PersonStatId.Maturity => Maturity,
        PersonStatId.Happiness => Happiness,
        PersonStatId.Charisma => Charisma,
        PersonStatId.Confidence => Confidence,
        PersonStatId.Reputation => Reputation,
        PersonStatId.SocialStatus => SocialStatus,
        PersonStatId.Attractiveness => Attractiveness,
        PersonStatId.Teamwork => Teamwork,
        PersonStatId.Morality => Morality,
        PersonStatId.Discipline => Discipline,
        PersonStatId.WorkEthic => WorkEthic,
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    public void Set(PersonStatId stat, float value)
    {
        switch (stat)
        {
            case PersonStatId.Intelligence: Intelligence = value; break;
            case PersonStatId.Maturity: Maturity = value; break;
            case PersonStatId.Happiness: Happiness = value; break;
            case PersonStatId.Charisma: Charisma = value; break;
            case PersonStatId.Confidence: Confidence = value; break;
            case PersonStatId.Reputation: Reputation = value; break;
            case PersonStatId.SocialStatus: SocialStatus = value; break;
            case PersonStatId.Attractiveness: Attractiveness = value; break;
            case PersonStatId.Teamwork: Teamwork = value; break;
            case PersonStatId.Morality: Morality = value; break;
            case PersonStatId.Discipline: Discipline = value; break;
            case PersonStatId.WorkEthic: WorkEthic = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(stat));
        }
    }
}

/// <summary>
/// The HS-4 drift math (high_school_person_layer.md §2.2/§2.3) — the ONLY two
/// exceptions to "person stats are sticky": GPA's weekly closed form and
/// happiness's weak daily mean-reversion. Pure statics, tunable-as-data
/// constants (the NeedDecayProfile/HeirGeneticsProfile table discipline).
/// GPA never moves through the per-hour effect channel; this closed form is
/// its single mover until HS-5 events gain a PersonStat consequence.
/// </summary>
public static class PersonDrift
{
    public const float StatMin = 0f;
    public const float StatMax = 100f;
    public const double GpaMin = 0.0;
    public const double GpaMax = 4.0;

    // ------------------------------------------------------------------
    // §2.2 GPA weekly drift. The doc pins the shape and the neutral identity
    // ("school fully attended, intelligence 50, discipline 50, no study, no
    // partner, no stress → drift 0") and leaves the closed form to HS-4:
    //
    //   Δgpa = GpaBasePerWeek · ( attendanceFrac · (1 + aptitude) − 1 )
    //          + StudyGpaPerHour · studyHours
    //          − StressGpaDragPerWeek · stress/100
    //          − partnerDrag
    //   aptitude = ( wI·(intelligence−50) + wD·(discipline−50) ) / 50
    //
    // The "−1" inside the base term is what makes truancy bite: full
    // attendance at neutral aptitude is exactly 0, zero attendance is exactly
    // −GpaBasePerWeek REGARDLESS of aptitude (you can't ace classes you skip),
    // and a gifted attendee (aptitude +1 at 100/100) drifts up at the same
    // rate a truant drifts down. All constants first-pass, tunable as data.
    // ------------------------------------------------------------------

    /// <summary>Weekly magnitude anchor: a full-truancy week costs this much GPA; a 100/100 aptitude full-attendance week earns it.</summary>
    public const double GpaBasePerWeek = 0.15;

    public const float GpaIntelligenceWeight = 0.5f;
    public const float GpaDisciplineWeight = 0.5f;

    /// <summary>Deliberate-study payoff: +0.01 GPA per free-time Study hour (14 h/week ≈ doubles the best natural drift). Uncapped — the schedule's 24h day and the needs it starves are the real cap (disclosed).</summary>
    public const double StudyGpaPerHour = 0.01;

    /// <summary>§2.2 StressDrag at stress 100 — sampled at the weekly tick, not averaged (pinned by the harness fixture).</summary>
    public const double StressGpaDragPerWeek = 0.10;

    /// <summary>"School fully attended" = this many School hours per 7-day week (≈ five 6-hour days; the calendar has no weekend concept).</summary>
    public const float ExpectedSchoolHoursPerWeek = 30f;

    /// <summary>
    /// Per observed day: what full attendance adds to the expectation
    /// accumulator, and what an UNPLANNED (autopilot) day credits as actual
    /// attendance — an autopiloted student attends school by default (the
    /// §2.2 neutral posture); skipping is a deliberate plan with 0 School
    /// hours. Both accumulators add this exact constant so a fully-autopilot
    /// week divides to attendanceFrac == 1 bit-exactly (the neutral identity
    /// can never drift on float summation).
    /// </summary>
    public const float ExpectedSchoolHoursPerDay = ExpectedSchoolHoursPerWeek / 7f;

    /// <summary>Zero-at-50 aptitude term: ±1 at the 0/100 extremes with the default half/half weights.</summary>
    public static float AptitudeTerm(float intelligence, float discipline) =>
        (GpaIntelligenceWeight * (intelligence - 50f) + GpaDisciplineWeight * (discipline - 50f)) / 50f;

    /// <summary>
    /// The §2.2 closed form — returns the signed weekly delta; the caller
    /// clamps the applied result into [<see cref="GpaMin"/>, <see cref="GpaMax"/>].
    /// <paramref name="partnerDrag"/> is the HS-5 dating hook (supportive
    /// partners pass a negative drag); no live feeder exists yet, callers pass 0.
    /// </summary>
    public static double GpaWeeklyDelta(
        float attendanceFrac, float intelligence, float discipline,
        float studyHours, float stress0To100, double partnerDrag = 0.0)
    {
        double attendance = Math.Clamp(attendanceFrac, 0f, 1f);
        double aptitude = AptitudeTerm(intelligence, discipline);
        return GpaBasePerWeek * (attendance * (1.0 + aptitude) - 1.0)
            + StudyGpaPerHour * studyHours
            - StressGpaDragPerWeek * (Math.Clamp(stress0To100, 0f, 100f) / 100.0)
            - partnerDrag;
    }

    // ------------------------------------------------------------------
    // §2.3 happiness mean-reversion: a spike bleeds back toward the
    // per-person setpoint over ~a week (0.8^7 ≈ 21% of a spike left after 7
    // days), weak enough that action/event deltas dominate day to day.
    // ------------------------------------------------------------------

    public const float HappinessReversionRatePerDay = 0.2f;

    /// <summary>The signed daily reversion step toward <paramref name="setpoint"/> (0 exactly at the setpoint).</summary>
    public static float HappinessDailyStep(float current, float setpoint) =>
        (setpoint - current) * HappinessReversionRatePerDay;
}
