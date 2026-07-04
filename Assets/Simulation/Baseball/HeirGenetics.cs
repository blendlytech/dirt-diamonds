using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Pure, engine-free genetic blending and hidden-interest rolling for heir
/// generation (docs/design/heir_mechanics.md §2, §4). Deals only in
/// <see cref="PlayerRatingsRow"/> (Data) and <see cref="RngState"/> — never
/// the Life sim, never the database — the same Data-clean, zero-DB profile
/// as <see cref="AtBatResolver"/>. Every calibration knob lives in
/// <see cref="HeirGeneticsProfile"/>: tuning is a data edit, never a logic
/// edit.
///
/// Each rating/interest exposes a deterministic core taking the "genetic
/// lottery" <c>bell</c> draw directly (never consuming RNG), mirroring how
/// <see cref="AtBatResolver.ComputeProbabilities"/> is exposed apart from
/// <see cref="AtBatResolver.Resolve"/> — the harness asserts the §7 fixtures
/// and the §3 regression identity (Spread's contribution vanishes at
/// bell=0, regardless of the constant's actual value) without needing to
/// reverse-engineer an RngState seed that lands on an exact draw.
/// </summary>
public static class HeirGenetics
{
    /// <summary>
    /// Calibration knobs for genetic blending, interest rolling, and lineage
    /// timing (design doc §2/§4/§5). Constants, not literals — retuning any
    /// of these is a data edit here, same precedent as
    /// <c>NeedsEngine.NeedDecayProfile</c> and <c>AtBatResolver</c>'s tables.
    /// </summary>
    public static class HeirGeneticsProfile
    {
        /// <summary>League-average rating anchor every 0–100 scale regresses toward (§2.1).</summary>
        public const int Mean = 50;

        /// <summary>Narrow-sense heritability: the fraction of midparent deviation an offspring is expected to inherit (§2.1).</summary>
        public const double Heritability = 0.5;

        /// <summary>Scale of the zero-centred triangular "genetic lottery" draw added on top of the blended value (§2.1).</summary>
        public const int Spread = 12;

        /// <summary>"Raised in a baseball household" baseline interest before affinity/luck (§4.1).</summary>
        public const int InterestBaseline = 55;

        /// <summary>Max nudge to interest from a fully warm (100) parent-child affinity (§4.1).</summary>
        public const int InterestAffinityMax = 15;

        /// <summary>Spread of the interest lottery — the failure engine (§4.1).</summary>
        public const int InterestSpread = 30;

        /// <summary>Affinity written on a freshly-created Child edge (§4.1, §9.2).</summary>
        public const int BirthAffinity = 30;

        /// <summary>Minimum revealed interest to count as a willing successor (§4.2).</summary>
        public const int InterestPlayThreshold = 40;

        /// <summary>Minimum age to be an eligible successor (§5.2) — mirrors CareerManager.StartingAge.</summary>
        public const int MaturityAge = 19;

        /// <summary>Forced-retirement age trigger (§5.1).</summary>
        public const int MandatoryRetirementAge = 42;

        /// <summary>Forced-retirement health_ceiling floor trigger (§5.1) — the PED-erosion coupling.</summary>
        public const int HealthRetirementFloor = 40;
    }

    /// <summary>
    /// The zero-centred triangular draw shared with LeagueGenerator.RollRating
    /// (sum of three independent uniforms, rescaled): concentrated near 0,
    /// range (-1, 1). Exposed so callers needing a real RNG-driven draw don't
    /// duplicate the formula; the deterministic <c>bell</c>-taking overloads
    /// below are what the harness injects fixed values into.
    /// </summary>
    public static double Bell(ref RngState rng) =>
        (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) / 1.5;

    /// <summary>
    /// §2.1 for a single rating: midparent → regression-to-mean → lottery.
    /// Rounded away-from-zero then clamped to the schema's [0,100] — pinned
    /// exactly per §2.1 (the §7 fixtures depend on this rounding mode).
    /// </summary>
    public static int BlendRating(int parentARating, int parentBRating, double bell)
    {
        double midparent = (parentARating + parentBRating) / 2.0;
        double blended = HeirGeneticsProfile.Mean + HeirGeneticsProfile.Heritability * (midparent - HeirGeneticsProfile.Mean);
        return ClampRating(RoundAwayFromZero(blended + HeirGeneticsProfile.Spread * bell));
    }

    /// <summary>RNG-driven form of <see cref="BlendRating(int,int,double)"/>: draws its own <see cref="Bell"/>.</summary>
    public static int BlendRating(int parentARating, int parentBRating, ref RngState rng) =>
        BlendRating(parentARating, parentBRating, Bell(ref rng));

    /// <summary>
    /// Deterministic core: applies <see cref="BlendRating(int,int,double)"/> to
    /// all seven ratings with the SAME injected <paramref name="bell"/> — a
    /// harness convenience for asserting the law is applied identically and
    /// independently to every field (§8 check 1's "full-vector" fixture), not
    /// a production path (production draws an independent bell per rating).
    /// is_pitcher is the caller's decision (§2.2); PlayerId is left unset.
    /// </summary>
    public static PlayerRatingsRow BlendRatings(in PlayerRatingsRow parentA, in PlayerRatingsRow parentB, bool isPitcher, double bell) => new()
    {
        IsPitcher = isPitcher,
        BatPower = BlendRating(parentA.BatPower, parentB.BatPower, bell),
        BatContact = BlendRating(parentA.BatContact, parentB.BatContact, bell),
        BatDiscipline = BlendRating(parentA.BatDiscipline, parentB.BatDiscipline, bell),
        PitStuff = BlendRating(parentA.PitStuff, parentB.PitStuff, bell),
        PitControl = BlendRating(parentA.PitControl, parentB.PitControl, bell),
        PitStamina = BlendRating(parentA.PitStamina, parentB.PitStamina, bell),
        Fielding = BlendRating(parentA.Fielding, parentB.Fielding, bell),
    };

    /// <summary>
    /// Production path (design doc §9.1 signature): blends all seven ratings
    /// independently, each drawing its own <see cref="Bell"/> from
    /// <paramref name="rng"/>. is_pitcher is the caller's decision (§2.2:
    /// inherited from parent A, the avatar); PlayerId is left for the caller.
    /// </summary>
    public static PlayerRatingsRow BlendRatings(in PlayerRatingsRow parentA, in PlayerRatingsRow parentB, bool isPitcher, ref RngState rng) => new()
    {
        IsPitcher = isPitcher,
        BatPower = BlendRating(parentA.BatPower, parentB.BatPower, ref rng),
        BatContact = BlendRating(parentA.BatContact, parentB.BatContact, ref rng),
        BatDiscipline = BlendRating(parentA.BatDiscipline, parentB.BatDiscipline, ref rng),
        PitStuff = BlendRating(parentA.PitStuff, parentB.PitStuff, ref rng),
        PitControl = BlendRating(parentA.PitControl, parentB.PitControl, ref rng),
        PitStamina = BlendRating(parentA.PitStamina, parentB.PitStamina, ref rng),
        Fielding = BlendRating(parentA.Fielding, parentB.Fielding, ref rng),
    };

    /// <summary>
    /// §2.3: the degenerate league-average parent substituted when the avatar
    /// has no Partner (or the Partner has no Player_Ratings row of their own)
    /// — every rating at the mean, IsPitcher false. Never inserted as a row.
    /// </summary>
    public static PlayerRatingsRow AverageParent() => new()
    {
        IsPitcher = false,
        BatPower = HeirGeneticsProfile.Mean,
        BatContact = HeirGeneticsProfile.Mean,
        BatDiscipline = HeirGeneticsProfile.Mean,
        PitStuff = HeirGeneticsProfile.Mean,
        PitControl = HeirGeneticsProfile.Mean,
        PitStamina = HeirGeneticsProfile.Mean,
        Fielding = HeirGeneticsProfile.Mean,
    };

    /// <summary>
    /// §4.1 deterministic core: hidden baseball_interest at conception —
    /// nurture (the parent-child Child-edge affinity) + luck, never parent
    /// interest (NPCs carry 0, and blending it would drag the roll wrong).
    /// Rounded away-from-zero, clamped to [0,100].
    /// </summary>
    public static int RollInterest(int childEdgeAffinity, double bell)
    {
        double affinityAdjust = HeirGeneticsProfile.InterestAffinityMax * (childEdgeAffinity / 100.0);
        double raw = HeirGeneticsProfile.InterestBaseline + affinityAdjust + HeirGeneticsProfile.InterestSpread * bell;
        return ClampRating(RoundAwayFromZero(raw));
    }

    /// <summary>RNG-driven form (design doc §9.1 signature): draws its own <see cref="Bell"/>.</summary>
    public static int RollInterest(int childEdgeAffinity, ref RngState rng) =>
        RollInterest(childEdgeAffinity, Bell(ref rng));

    private static int RoundAwayFromZero(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static int ClampRating(int value) => Math.Clamp(value, 0, 100);
}
