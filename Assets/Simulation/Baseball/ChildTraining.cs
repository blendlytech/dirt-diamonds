using System;
using System.Globalization;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>What a backyard session drills (13-6c brief §4.2, D4-A). Story and UI only: every focus moves the one coaching axis.</summary>
public enum TrainingFocus : byte
{
    Hitting = 0,
    Pitching = 1,
    Fielding = 2,
}

/// <summary>How hard a backyard session pushes (13-6c brief §4.2).</summary>
public enum TrainingIntensity : byte
{
    Easy = 0,
    Normal = 1,
    Hard = 2,
}

/// <summary>The resolution of one backyard session: the roll, the odds and the basis-point gains it earned before the season cap.</summary>
public readonly struct TrainingSessionOutcome
{
    public readonly bool Success;

    /// <summary>The clamped success odds in whole percentage points, [5, 95].</summary>
    public readonly int OddsPp;

    /// <summary>The one draw, [0, 100): the session succeeds when it is below <see cref="OddsPp"/>.</summary>
    public readonly int Roll;

    /// <summary>The coaching gain in basis points before the season cap (0 on a failure or at coaching 100).</summary>
    public readonly int CoachingBp;

    /// <summary>The signed care change in basis points (time together, or a failed Hard session's burnout).</summary>
    public readonly int CareBp;

    public TrainingSessionOutcome(bool success, int oddsPp, int roll, int coachingBp, int careBp)
    {
        Success = success;
        OddsPp = oddsPp;
        Roll = roll;
        CoachingBp = coachingBp;
        CareBp = careBp;
    }
}

/// <summary>
/// One child's standing training plan and signed sub-point carries, persisted
/// as the <c>training_plan:&lt;childId&gt;</c> Game_State text key (13-6c brief
/// §3 D9-A, A2): <c>focus:intensity:coachBp:careBp:seasonYear:seasonCoachBp:weekStart</c>.
/// The seventh field (the training week of the child's most recent session,
/// 0 = never) is the build's one addition to the brief's six (§11 B1): the
/// week-end fade and the autopilot's spread need to know who was trained this
/// week across a reload.
/// </summary>
public struct TrainingPlan
{
    public TrainingFocus Focus;
    public TrainingIntensity Intensity;

    /// <summary>Signed coaching carry in basis points, always within (−100, +100) after a flush.</summary>
    public int CoachBp;

    /// <summary>Signed care carry in basis points, always within (−100, +100) after a flush.</summary>
    public int CareBp;

    /// <summary>The season <see cref="SeasonCoachBp"/> counts toward (0 = none yet).</summary>
    public int SeasonYear;

    /// <summary>Coaching basis points gained this season, capped at <see cref="ChildTraining.TrainingProfile.SeasonCoachingCapBp"/>.</summary>
    public int SeasonCoachBp;

    /// <summary>The training week (its start day) of the child's most recent session; 0 = never trained.</summary>
    public long LastSessionWeekStart;

    /// <summary>The default plan: Normal, in the child's role (D6-A), with no carries.</summary>
    public static TrainingPlan Default(bool isPitcher) => new()
    {
        Focus = isPitcher ? TrainingFocus.Pitching : TrainingFocus.Hitting,
        Intensity = TrainingIntensity.Normal,
    };

    public readonly string Format() => string.Join(':',
        FocusToken(Focus),
        IntensityToken(Intensity),
        CoachBp.ToString(CultureInfo.InvariantCulture),
        CareBp.ToString(CultureInfo.InvariantCulture),
        SeasonYear.ToString(CultureInfo.InvariantCulture),
        SeasonCoachBp.ToString(CultureInfo.InvariantCulture),
        LastSessionWeekStart.ToString(CultureInfo.InvariantCulture));

    /// <summary>Parses <see cref="Format"/>'s text. A malformed value is save corruption and throws, never a silent reset.</summary>
    public static TrainingPlan Parse(string text)
    {
        string[] parts = text.Split(':');
        if (parts.Length != 7)
        {
            throw new FormatException($"training_plan value '{text}' has {parts.Length} fields, expected 7.");
        }
        return new TrainingPlan
        {
            Focus = ParseFocus(parts[0]),
            Intensity = ParseIntensity(parts[1]),
            CoachBp = ParseInt(parts[2], text),
            CareBp = ParseInt(parts[3], text),
            SeasonYear = ParseInt(parts[4], text),
            SeasonCoachBp = ParseInt(parts[5], text),
            LastSessionWeekStart = long.Parse(parts[6], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
        };
    }

    public static string FocusToken(TrainingFocus focus) => focus switch
    {
        TrainingFocus.Hitting => "hit",
        TrainingFocus.Pitching => "pitch",
        TrainingFocus.Fielding => "field",
        _ => throw new ArgumentOutOfRangeException(nameof(focus), focus, "Unknown training focus."),
    };

    public static string IntensityToken(TrainingIntensity intensity) => intensity switch
    {
        TrainingIntensity.Easy => "easy",
        TrainingIntensity.Normal => "normal",
        TrainingIntensity.Hard => "hard",
        _ => throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "Unknown training intensity."),
    };

    private static TrainingFocus ParseFocus(string token) => token switch
    {
        "hit" => TrainingFocus.Hitting,
        "pitch" => TrainingFocus.Pitching,
        "field" => TrainingFocus.Fielding,
        _ => throw new FormatException($"Unknown training focus '{token}'."),
    };

    private static TrainingIntensity ParseIntensity(string token) => token switch
    {
        "easy" => TrainingIntensity.Easy,
        "normal" => TrainingIntensity.Normal,
        "hard" => TrainingIntensity.Hard,
        _ => throw new FormatException($"Unknown training intensity '{token}'."),
    };

    private static int ParseInt(string token, string whole) =>
        int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new FormatException($"training_plan value '{whole}' has a non-integer field '{token}'.");
}

/// <summary>
/// Slice 13-6c (brief §4.2, D4–D6-A; Gemini 4 Carbon's signed §9 values, A2
/// and A5): the pure rules of the parent phase's backyard training. Integer
/// percentage points and basis points throughout (100 bp = 1 axis point),
/// rounding away from zero (the <see cref="NurtureBlend"/> discipline). No
/// database, no Godot, no sim RNG: <c>ChildTrainingService</c> (Economy)
/// reads the state, calls these and writes the result, and the shipped
/// <see cref="NurtureBlend"/> folds the axes at the takeover.
///
/// Every roll is one draw from a <see cref="SessionStream"/>, a
/// <see cref="PostseasonStreams.MixCombine"/> chain over the persisted
/// parent-phase seed, so a reload replays the same session.
/// </summary>
public static class ChildTraining
{
    /// <summary>Every signed constant as named data (§4.2); retuning one is a data edit behind the parent-phase gates.</summary>
    public static class TrainingProfile
    {
        /// <summary>A child is trainable from this age (D6-A)…</summary>
        public const int MinTrainableAge = 5;

        /// <summary>…through this one; at 16 the child takes over or walks.</summary>
        public const int MaxTrainableAge = 15;

        public const int EasyBasePp = 80;
        public const int NormalBasePp = 60;
        public const int HardBasePp = 40;

        public const int MinOddsPp = 5;
        public const int MaxOddsPp = 95;

        /// <summary>The drill tilt: round(30 × (score − 0.5)), so ±15 pp at 0 and 1 and exactly 0 at 0.5 (D4-A).</summary>
        public const double TiltScale = 30.0;

        /// <summary>The score autopilot and a skipped drill play at: no tilt.</summary>
        public const double NeutralScore = 0.5;

        /// <summary>A peak role sum at or below this gives no expertise.</summary>
        public const int ExpertiseFloorRoleSum = 135;

        /// <summary>Role-sum points per 10 pp of expertise.</summary>
        public const int ExpertiseSpanRoleSum = 90;

        public const int MaxExpertisePp = 10;

        /// <summary>Stress at or below this costs no fatigue.</summary>
        public const int FatigueStressFloor = 60;

        /// <summary>Stress points per 10 pp of fatigue.</summary>
        public const int FatigueStressSpan = 40;

        public const int MaxFatiguePp = 10;

        public const int EasyCoachingBp = 5;
        public const int NormalCoachingBp = 9;
        public const int HardCoachingBp = 15;

        public const int EasyCareBp = 18;
        public const int NormalCareBp = 12;
        public const int HardCareBp = 0;

        /// <summary>A failed Hard session: pushed too hard (burnout, never an injury).</summary>
        public const int HardFailureCareBp = -25;

        /// <summary>100 basis points = 1 whole axis point.</summary>
        public const int BasisPointsPerPoint = 100;

        /// <summary>+6 coaching a child a season (A2).</summary>
        public const int SeasonCoachingCapBp = 600;

        /// <summary>−0.06 coaching a week for a trainable child with no session that week, never below 50 (A2).</summary>
        public const int IdleCoachingDecayBp = 6;

        /// <summary>The fade's floor: the axis's neutral point.</summary>
        public const int FadeFloorCoaching = NurtureBlend.NurtureProfile.NeutralAxis;

        /// <summary>Diminishing returns: the gain scales by (100 − coaching) / this, capped at 1.</summary>
        public const double DiminishingSpan = 50.0;

        public const int MaxAxis = 100;
    }

    /// <summary>"CHLDTRNG": the session stream's domain tag (A5).</summary>
    public const ulong TrainingStreamTag = 0x4348_4C44_5452_4E47UL;

    /// <summary>"NGHTTIPS": the Nightlife tip stream's domain tag (A5).</summary>
    public const ulong NightTipsStreamTag = 0x4E47_4854_5449_5053UL;

    // ------------------------------------------------------------------
    // Streams (A5)
    // ------------------------------------------------------------------

    /// <summary>FNV-1a 64 over the id's UTF-16 code units: process-invariant, unlike .NET's randomized per-process string hash.</summary>
    public static ulong HashId64(string id)
    {
        ulong h = 0xCBF2_9CE4_8422_2325UL;
        for (int i = 0; i < id.Length; i++)
        {
            h = unchecked((h ^ id[i]) * 0x0000_0100_0000_01B3UL);
        }
        return h;
    }

    /// <summary>
    /// One session's stream: the persisted seed, the tag, the child, the day
    /// key and the session's ordinal in its week. The service keys every
    /// session of a week on the week's start day, so (day, ordinal) is unique.
    /// </summary>
    public static RngState SessionStream(ulong seed, string childId, long day, int sessionIndex)
    {
        ulong h = PostseasonStreams.MixCombine(seed, TrainingStreamTag);
        h = PostseasonStreams.MixCombine(h, HashId64(childId));
        h = PostseasonStreams.MixCombine(h, unchecked((ulong)day));
        h = PostseasonStreams.MixCombine(h, unchecked((ulong)sessionIndex));
        return new RngState(h | 1UL);
    }

    /// <summary>A Nightlife payday's tip stream: MixCombine(MixCombine(seed, NGHTTIPS), day) | 1.</summary>
    public static RngState TipStream(ulong seed, long day) =>
        new(PostseasonStreams.MixCombine(PostseasonStreams.MixCombine(seed, NightTipsStreamTag), unchecked((ulong)day)) | 1UL);

    /// <summary>The week's tips in whole dollars, [0, <paramref name="maxTip"/>]: one draw from <see cref="TipStream"/>.</summary>
    public static int TipDollars(ulong seed, long day, int maxTip)
    {
        if (maxTip <= 0)
        {
            return 0;
        }
        RngState rng = TipStream(seed, day);
        return rng.NextInt(maxTip + 1);
    }

    // ------------------------------------------------------------------
    // Odds (§4.2)
    // ------------------------------------------------------------------

    public static int BasePp(TrainingIntensity intensity) => intensity switch
    {
        TrainingIntensity.Easy => TrainingProfile.EasyBasePp,
        TrainingIntensity.Normal => TrainingProfile.NormalBasePp,
        TrainingIntensity.Hard => TrainingProfile.HardBasePp,
        _ => throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "Unknown training intensity."),
    };

    /// <summary>clamp(round(10 × (peakRoleSum − 135) / 90), 0, 10): 0 at 135, +2 at 150, +5 at 180, +10 at 225.</summary>
    public static int ExpertisePp(int peakRoleSum) => Math.Clamp(
        RoundAwayFromZero((double)TrainingProfile.MaxExpertisePp * (peakRoleSum - TrainingProfile.ExpertiseFloorRoleSum)
            / TrainingProfile.ExpertiseSpanRoleSum),
        0, TrainingProfile.MaxExpertisePp);

    /// <summary>
    /// The parent's peak role sum: the larger of the current and potential
    /// sums over the parent's role, through <see cref="PipelineRules.RoleRatingSum(PitcherRole, int, int, int)"/>
    /// (the reliever stamina credit), so a 42-year-old's decline doesn't erase what they know.
    /// </summary>
    public static int PeakRoleSum(PitcherRole role, in PlayerRatingsRow current, in PlayerPotentialRow potential)
    {
        bool batter = role == PitcherRole.None;
        int now = batter
            ? PipelineRules.RoleRatingSum(role, current.BatPower, current.BatContact, current.BatDiscipline)
            : PipelineRules.RoleRatingSum(role, current.PitStuff, current.PitControl, current.PitStamina);
        int peak = batter
            ? PipelineRules.RoleRatingSum(role, potential.BatPower, potential.BatContact, potential.BatDiscipline)
            : PipelineRules.RoleRatingSum(role, potential.PitStuff, potential.PitControl, potential.PitStamina);
        return Math.Max(now, peak);
    }

    /// <summary>round(30 × (clamp(score, 0, 1) − 0.5)): −15..+15 pp, exactly 0 at 0.5 (a NaN score plays neutral).</summary>
    public static int TiltPp(double score)
    {
        double clamped = double.IsNaN(score) ? TrainingProfile.NeutralScore : Math.Clamp(score, 0.0, 1.0);
        return RoundAwayFromZero(TrainingProfile.TiltScale * (clamped - TrainingProfile.NeutralScore));
    }

    /// <summary>clamp(round(10 × (stress − 60) / 40), 0, 10): 0 at stress ≤ 60, 10 at 100.</summary>
    public static int FatiguePp(double stress) => Math.Clamp(
        RoundAwayFromZero((double)TrainingProfile.MaxFatiguePp * (stress - TrainingProfile.FatigueStressFloor)
            / TrainingProfile.FatigueStressSpan),
        0, TrainingProfile.MaxFatiguePp);

    /// <summary>pPp = clamp(basePp + jobBonusPp + expertisePp + tiltPp − fatiguePp, 5, 95).</summary>
    public static int OddsPp(TrainingIntensity intensity, int jobBonusPp, int expertisePp, int tiltPp, int fatiguePp) =>
        Math.Clamp(BasePp(intensity) + jobBonusPp + expertisePp + tiltPp - fatiguePp,
            TrainingProfile.MinOddsPp, TrainingProfile.MaxOddsPp);

    // ------------------------------------------------------------------
    // Outcomes (§4.2)
    // ------------------------------------------------------------------

    /// <summary>
    /// A success's coaching gain in basis points: max(1, round(base × scale))
    /// with scale = clamp((100 − coaching) / 50, 0, 1) on the child's whole
    /// coaching; 0 once the axis is 100. The max(1, …) floor removes the
    /// integer stalls whole-point gains hit (§9 Q2).
    /// </summary>
    public static int CoachingGainBp(TrainingIntensity intensity, int coaching)
    {
        if (coaching >= TrainingProfile.MaxAxis)
        {
            return 0;
        }
        int baseBp = intensity switch
        {
            TrainingIntensity.Easy => TrainingProfile.EasyCoachingBp,
            TrainingIntensity.Normal => TrainingProfile.NormalCoachingBp,
            TrainingIntensity.Hard => TrainingProfile.HardCoachingBp,
            _ => throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "Unknown training intensity."),
        };
        double scale = Math.Clamp((TrainingProfile.MaxAxis - coaching) / TrainingProfile.DiminishingSpan, 0.0, 1.0);
        return Math.Max(1, RoundAwayFromZero(baseBp * scale));
    }

    /// <summary>A success's care gain in basis points (time together): Easy +18, Normal +12, Hard 0.</summary>
    public static int SuccessCareBp(TrainingIntensity intensity) => intensity switch
    {
        TrainingIntensity.Easy => TrainingProfile.EasyCareBp,
        TrainingIntensity.Normal => TrainingProfile.NormalCareBp,
        TrainingIntensity.Hard => TrainingProfile.HardCareBp,
        _ => throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "Unknown training intensity."),
    };

    /// <summary>A failure's care change: a failed Hard session costs 25 bp; Easy and Normal failures cost nothing.</summary>
    public static int FailureCareBp(TrainingIntensity intensity) =>
        intensity == TrainingIntensity.Hard ? TrainingProfile.HardFailureCareBp : 0;

    /// <summary>One session: a single draw, <c>NextInt(100) &lt; oddsPp</c> succeeds.</summary>
    public static TrainingSessionOutcome Resolve(ref RngState rng, TrainingIntensity intensity, int oddsPp, int coaching)
    {
        int roll = rng.NextInt(100);
        bool success = roll < oddsPp;
        return success
            ? new TrainingSessionOutcome(true, oddsPp, roll, CoachingGainBp(intensity, coaching), SuccessCareBp(intensity))
            : new TrainingSessionOutcome(false, oddsPp, roll, 0, FailureCareBp(intensity));
    }

    // ------------------------------------------------------------------
    // The carry, the season cap and the fade (A2)
    // ------------------------------------------------------------------

    /// <summary>
    /// Adds a coaching gain to the plan's carry under the season cap: the
    /// season total resets when <paramref name="seasonYear"/> advances, and the
    /// gain is trimmed to what the cap still allows. Returns the basis points
    /// actually added.
    /// </summary>
    public static int AddCoachingGain(ref TrainingPlan plan, int gainBp, int seasonYear)
    {
        if (plan.SeasonYear != seasonYear)
        {
            plan.SeasonYear = seasonYear;
            plan.SeasonCoachBp = 0;
        }
        int allowed = Math.Clamp(TrainingProfile.SeasonCoachingCapBp - plan.SeasonCoachBp, 0, TrainingProfile.SeasonCoachingCapBp);
        int applied = Math.Clamp(gainBp, 0, allowed);
        plan.CoachBp += applied;
        plan.SeasonCoachBp += applied;
        return applied;
    }

    /// <summary>
    /// Moves a carry's whole points out (truncating toward zero) and keeps the
    /// remainder, so the carry is always within (−100, +100) afterward
    /// (<c>LifeSimManager.PersonRuntime.Unflushed</c>'s discipline).
    /// </summary>
    public static int FlushWholePoints(ref int carryBp)
    {
        int whole = carryBp / TrainingProfile.BasisPointsPerPoint; // C# integer division truncates toward zero
        carryBp -= whole * TrainingProfile.BasisPointsPerPoint;
        return whole;
    }

    /// <summary>
    /// The week-end fade for a trainable child with no session that week:
    /// −6 bp of coaching, never taking the effective value (whole + carry)
    /// below 50. Returns the basis points removed (≤ 0); 0 at or below 50
    /// (no fade at coaching 50 with a zero carry, and a fade never raises a
    /// child some event pushed below 50).
    /// </summary>
    public static int ApplyIdleFade(ref TrainingPlan plan, int coaching)
    {
        int effective = coaching * TrainingProfile.BasisPointsPerPoint + plan.CoachBp;
        int floor = TrainingProfile.FadeFloorCoaching * TrainingProfile.BasisPointsPerPoint;
        if (effective <= floor)
        {
            return 0;
        }
        int faded = Math.Max(floor, effective - TrainingProfile.IdleCoachingDecayBp);
        int delta = faded - effective;
        plan.CoachBp += delta;
        return delta;
    }

    /// <summary>Trainable by age alone (ages 5–15); the service adds the Child edge and the unrostered test.</summary>
    public static bool IsTrainableAge(int age) =>
        age >= TrainingProfile.MinTrainableAge && age <= TrainingProfile.MaxTrainableAge;

    private static int RoundAwayFromZero(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
