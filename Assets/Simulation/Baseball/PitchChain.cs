using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>Per-pitch event classes of the inner chain (micro doc §5.1).</summary>
public enum PitchClass : byte
{
    Ball,
    Strike,
    Foul,
    InPlay,
}

/// <summary>Ball-strike count — the inner chain's transient state (§5).</summary>
public readonly struct CountState
{
    public readonly byte Balls;
    public readonly byte Strikes;

    public CountState(byte balls, byte strikes)
    {
        Balls = balls;
        Strikes = strikes;
    }
}

/// <summary>
/// One pitch worth of batter input, already reduced to the two scalars the
/// chain consumes (§6): neutral (league-average) play is the zero point, so
/// <c>default</c> IS the neutral input and the §7 consistency identity holds.
/// </summary>
public readonly struct BatterPitchInput
{
    /// <summary>[-1, +1]: plate-discipline edge; + shifts per-pitch mass from strikes toward balls.</summary>
    public readonly double DisciplineEdge;

    /// <summary>[-1, +1]: contact quality q; perturbs the §5.3 ball-in-play split (+ toward XBH/HR).</summary>
    public readonly double ContactQuality;

    public BatterPitchInput(double disciplineEdge, double contactQuality)
    {
        DisciplineEdge = disciplineEdge;
        ContactQuality = contactQuality;
    }
}

/// <summary>
/// One pitcher's three-pitch repertoire (schema v4 Pitch_Arsenals), packed as
/// bytes so the per-pitch hot path stays allocation-free. Indexed by
/// <see cref="PitchType"/>; usage weights are the selection mix (sum 100 at
/// the query layer, but <see cref="DrawType"/> only needs them positive).
/// </summary>
public readonly struct PitcherArsenal
{
    private readonly byte _fastballVelocity, _fastballMovement, _fastballUsage;
    private readonly byte _breakingVelocity, _breakingMovement, _breakingUsage;
    private readonly byte _offspeedVelocity, _offspeedMovement, _offspeedUsage;

    public PitcherArsenal(
        byte fastballVelocity, byte fastballMovement, byte fastballUsage,
        byte breakingVelocity, byte breakingMovement, byte breakingUsage,
        byte offspeedVelocity, byte offspeedMovement, byte offspeedUsage)
    {
        _fastballVelocity = fastballVelocity;
        _fastballMovement = fastballMovement;
        _fastballUsage = fastballUsage;
        _breakingVelocity = breakingVelocity;
        _breakingMovement = breakingMovement;
        _breakingUsage = breakingUsage;
        _offspeedVelocity = offspeedVelocity;
        _offspeedMovement = offspeedMovement;
        _offspeedUsage = offspeedUsage;
    }

    /// <summary>The DDL-backfill silhouette at league-average stuff (50): 50/40/60, 30/50/25, 25/55/15.</summary>
    public static PitcherArsenal LeagueAverage => new(50, 40, 60, 30, 50, 25, 25, 55, 15);

    public byte Velocity(PitchType type) => type switch
    {
        PitchType.Fastball => _fastballVelocity,
        PitchType.Breaking => _breakingVelocity,
        _ => _offspeedVelocity,
    };

    public byte Movement(PitchType type) => type switch
    {
        PitchType.Fastball => _fastballMovement,
        PitchType.Breaking => _breakingMovement,
        _ => _offspeedMovement,
    };

    public byte Usage(PitchType type) => type switch
    {
        PitchType.Fastball => _fastballUsage,
        PitchType.Breaking => _breakingUsage,
        _ => _offspeedUsage,
    };

    /// <summary>Draws a pitch type from the usage mix (one uniform draw).</summary>
    public PitchType DrawType(ref RngState rng)
    {
        int total = _fastballUsage + _breakingUsage + _offspeedUsage;
        if (total <= 0)
        {
            return PitchType.Fastball;
        }
        int draw = rng.NextInt(total);
        if (draw < _fastballUsage)
        {
            return PitchType.Fastball;
        }
        return draw < _fastballUsage + _breakingUsage ? PitchType.Breaking : PitchType.Offspeed;
    }
}

/// <summary>
/// The per-PA slice of the v4 pitch model that is not already inside the §5
/// anchor: the pitcher's arsenal, his CURRENT (fatigue-adjusted) control for
/// the zone-tendency model, and the batter's discipline for the cue blur.
/// Built once per PA by the game driver.
/// </summary>
public readonly struct PitchMatchup
{
    public readonly PitcherArsenal Arsenal;
    public readonly byte PitcherControl;
    public readonly byte BatterDiscipline;

    public PitchMatchup(in PitcherArsenal arsenal, byte pitcherControl, byte batterDiscipline)
    {
        Arsenal = arsenal;
        PitcherControl = pitcherControl;
        BatterDiscipline = batterDiscipline;
    }
}

/// <summary>
/// What the batter sees before committing to a pitch (the §6 zone-read
/// minigame, real as of v4): a pitch-type cue — usually the true type, blurred
/// toward a wrong one by movement vs. the batter's discipline — and the zone
/// probability IMPLIED BY THAT CUE (scouting knowledge). The actual location
/// is drawn separately and never shown; reading well means turning this look
/// into a correct in/out-of-zone guess.
/// </summary>
public readonly struct PitchLook
{
    public readonly PitchType Cue;

    /// <summary>P(in zone) as the batter's scouting suggests for the cued type at this count.</summary>
    public readonly double ZoneProbability;

    public PitchLook(PitchType cue, double zoneProbability)
    {
        Cue = cue;
        ZoneProbability = zoneProbability;
    }
}

/// <summary>How a batter answers a pitch (v4). Neutral = play the ratings (§6.1 zero input).</summary>
public enum BatterIntentKind : byte
{
    Neutral,
    Take,
    Swing,
}

/// <summary>
/// One pitch worth of raw batter intent. The chain resolves the zone guess
/// against the actual drawn location and maps the result through
/// <see cref="PlayerInputModel"/>; a Neutral intent bypasses the mapping
/// entirely so the §7 consistency identity holds bit-exactly.
/// </summary>
public readonly struct BatterIntent
{
    public readonly BatterIntentKind Kind;

    /// <summary>Signed swing timing error τ ∈ [-1, +1]; meaningful only when Kind == Swing.</summary>
    public readonly double Timing;

    /// <summary>The zone read: true = "this one's in the zone".</summary>
    public readonly bool GuessInZone;

    private BatterIntent(BatterIntentKind kind, double timing, bool guessInZone)
    {
        Kind = kind;
        Timing = timing;
        GuessInZone = guessInZone;
    }

    public static BatterIntent Neutral => default;

    public static BatterIntent Take(bool guessInZone) =>
        new(BatterIntentKind.Take, 0.0, guessInZone);

    public static BatterIntent Swing(double timing, bool guessInZone) =>
        new(BatterIntentKind.Swing, timing, guessInZone);
}

/// <summary>
/// One pitch worth of pitcher intent (the v4 pitcher-side input model).
/// Neutral = the tendency model picks type and location odds (NPC pitchers,
/// autopilot); a called pitch pins the type and aims in or out of the zone,
/// with execution odds set by the pitcher's effective control.
/// </summary>
public readonly struct PitchCall
{
    public readonly bool IsCalled;
    public readonly PitchType Type;
    public readonly bool TargetInZone;

    private PitchCall(bool isCalled, PitchType type, bool targetInZone)
    {
        IsCalled = isCalled;
        Type = type;
        TargetInZone = targetInZone;
    }

    public static PitchCall Neutral => default;

    public static PitchCall Throw(PitchType type, bool targetInZone) =>
        new(true, type, targetInZone);
}

/// <summary>
/// Everything about the game situation that is fixed for the duration of one
/// human PA — handed to the policy at PA start (Phase 5: the interactive
/// policy forwards it to the UI, and needs the effective pitcher ratings for
/// the §6 timing-tolerance mapping). Count evolves per pitch via
/// <see cref="IBatterPolicy.NextPitch"/>; nothing else changes mid-PA.
/// </summary>
public readonly struct HumanPaContext
{
    public readonly int AwayScore;
    public readonly int HomeScore;
    public readonly int Inning;
    public readonly bool IsTopHalf;
    public readonly int Outs;

    /// <summary>3-bit base map, matching the sim (1 = 1B, 2 = 2B, 4 = 3B).</summary>
    public readonly int Bases;

    /// <summary>Fatigue-adjusted ratings the PA is being resolved against (§8).</summary>
    public readonly PitcherRatings EffectivePitcher;

    public HumanPaContext(
        int awayScore, int homeScore, int inning, bool isTopHalf, int outs, int bases,
        in PitcherRatings effectivePitcher)
    {
        AwayScore = awayScore;
        HomeScore = homeScore;
        Inning = inning;
        IsTopHalf = isTopHalf;
        Outs = outs;
        Bases = bases;
        EffectivePitcher = effectivePitcher;
    }
}

/// <summary>
/// The human is an input stream; the neutral policy is its headless stand-in
/// (§6.1/§9). Implemented by structs and consumed through a generic constraint
/// so the per-pitch call devirtualizes — no boxing, no allocation. Every
/// implementor supplies all three members explicitly (a default interface
/// method would box the struct receiver on the constrained call).
/// </summary>
public interface IBatterPolicy
{
    /// <summary>Called by the driver once at the start of each human PA, before any pitch.</summary>
    void BeginPa(in HumanPaContext context);

    BatterIntent NextPitch(in PitchLook look, in CountState count, ref RngState rng);

    /// <summary>Per-pitch observation hook (UI feedback). Consumes no RNG; a no-op off the interactive path.</summary>
    void OnPitchResolved(in PitchResult result);

    /// <summary>Called by the driver after the human PA resolves (play-log / UI feedback hook).</summary>
    void OnPaResolved(PaOutcome outcome);
}

/// <summary>§6.1 neutral autopilot: plays exactly to the ratings (all-zero input).</summary>
public struct NeutralBatterPolicy : IBatterPolicy
{
    public readonly void BeginPa(in HumanPaContext context)
    {
    }

    public readonly BatterIntent NextPitch(in PitchLook look, in CountState count, ref RngState rng) =>
        BatterIntent.Neutral;

    public readonly void OnPitchResolved(in PitchResult result)
    {
    }

    public readonly void OnPaResolved(PaOutcome outcome)
    {
    }
}

/// <summary>
/// Pitcher-side twin of <see cref="IBatterPolicy"/> (v4): the human on the
/// mound is an input stream of pitch calls; the neutral policy lets the
/// tendency model pitch to the ratings. Same struct/generic-constraint
/// contract — all members explicit, no boxing.
/// </summary>
public interface IPitcherPolicy
{
    /// <summary>Called once per PA the human pitches, before any pitch of it.</summary>
    void BeginPa(in HumanPaContext context);

    PitchCall NextPitch(in CountState count, ref RngState rng);

    /// <summary>Called after each PA the human pitched (play-log / UI feedback hook).</summary>
    void OnPaResolved(PaOutcome outcome);
}

/// <summary>Neutral mound autopilot: type from the usage mix, location from the tendency model.</summary>
public struct NeutralPitcherPolicy : IPitcherPolicy
{
    public readonly void BeginPa(in HumanPaContext context)
    {
    }

    public readonly PitchCall NextPitch(in CountState count, ref RngState rng) => PitchCall.Neutral;

    public readonly void OnPaResolved(PaOutcome outcome)
    {
    }
}

/// <summary>
/// Count-independent per-pitch class probabilities (ball / strike-class /
/// in-play), produced by <see cref="PitchChain.SolveNeutral"/> so the chain's
/// absorption matches the macro anchor p* exactly (§5.2).
/// </summary>
public readonly struct PitchClassRates
{
    public readonly double Ball;
    public readonly double StrikeClass;
    public readonly double InPlay;

    public PitchClassRates(double ball, double strikeClass, double inPlay)
    {
        Ball = ball;
        StrikeClass = strikeClass;
        InPlay = inPlay;
    }
}

/// <summary>
/// The inner absorbing Markov chain of micro doc §5: 12 count states, three
/// absorbing exits (BB / K / ball-in-play), one transition per pitch. The
/// neutral per-pitch class rates are recovered from the macro anchor
/// p* = AtBatResolver.ComputeProbabilities by inverting the chain's absorption
/// equations (§5.2), and the BIP exit re-draws from p*'s renormalized in-play
/// split (§5.3) — so under neutral input the full 7-way terminal distribution
/// is the macro distribution BY CONSTRUCTION (§7). Human input perturbs the
/// class rates and the BIP split, clamped, and is the game.
///
/// Model shape: the strike class folds called/swinging strikes and fouls
/// together — below two strikes they advance the count identically, and at
/// two strikes a fraction FoulShareOfStrikes of strike-class pitches are
/// fouls that leave the count unchanged (a length-only refresh, §5.1). The
/// foul share therefore tunes pitches/PA without moving the outcome split.
///
/// Everything here is stack/struct math — zero heap allocation per solve,
/// per pitch, and per PA.
/// </summary>
public static class PitchChain
{
    /// <summary>
    /// Fraction of strike-class pitches that are fouls (§5.1) — the pitches/PA
    /// realism knob (§11 test 3, tuning step 2). Only consulted at two strikes,
    /// where a foul self-loops instead of striking out; 0.68 lands the analytic
    /// expected length at ~3.84 pitches/PA for the league-average matchup.
    /// </summary>
    public const double FoulShareOfStrikes = 0.68;

    // §6 input gains: how hard a full discipline edge (±1) shifts the per-pitch
    // ball/strike mass (§11 tuning step 4 — feel knobs, gated on tests 1–2
    // still passing under the neutral policy, whose edge is 0).
    public const double DisciplineBallGain = 0.30;
    public const double DisciplineStrikeGain = 0.20;

    // §5.3 contact-quality gains on the BIP split, in in-play bucket order
    // {Out, Single, Double, Triple, HomeRun}: weight_e = p*_e · exp(q · gain).
    // q = 0 (neutral) reproduces the macro BIP-conditional split exactly.
    private static readonly double[] BipQualityGains = { -0.90, 0.25, 0.60, 0.40, 1.20 };

    // ------------------------------------------------------------------
    // v4 zone/pitch-type model knobs. The location layer is an EXACT mixture:
    // whatever z says, the conditional class rates below average back to the
    // unconditional rates, so the neutral 7-way distribution (and therefore
    // the §7 macro identity) cannot move — these knobs shape information and
    // feel, not calibration.
    // ------------------------------------------------------------------

    /// <summary>League-average in-zone probability at a 0-0 count, control 50, fastball.</summary>
    public const double ZoneBase = 0.45;

    /// <summary>Zone-tendency shift per point of effective control above 50.</summary>
    public const double ZoneControlGain = 0.003;

    /// <summary>Per ball in the count: behind, the pitcher has to come in.</summary>
    public const double ZoneBallGain = 0.05;

    /// <summary>Per strike in the count: ahead, he can waste one.</summary>
    public const double ZoneStrikeGain = -0.04;

    /// <summary>Per-type zone shifts: fastballs pound the zone, breakers hunt chases.</summary>
    public const double ZoneShiftFastball = 0.06;
    public const double ZoneShiftBreaking = -0.06;
    public const double ZoneShiftOffspeed = -0.02;

    public const double ZoneMin = 0.20;
    public const double ZoneMax = 0.80;

    /// <summary>
    /// How strongly location splits the ball mass: in-zone pitches carry
    /// ball·(1−s), out-of-zone pitches the balancing surplus. 0 would make the
    /// read worthless; 1 would make locations fully deterministic.
    /// </summary>
    public const double ZoneSeparation = 0.55;

    /// <summary>Cap on the out-of-zone conditional ball rate (keeps the mixture solvable at high z).</summary>
    public const double MaxConditionalBall = 0.95;

    /// <summary>Base probability the pre-pitch type cue is wrong at movement 50 vs discipline 50.</summary>
    public const double CueBlurBase = 0.10;

    /// <summary>Cue blur added per point of pitch movement above 50.</summary>
    public const double CueBlurMovementGain = 0.004;

    /// <summary>Cue blur removed per point of batter discipline above 50.</summary>
    public const double CueBlurDisciplineGain = 0.002;

    public const double CueBlurMin = 0.02;
    public const double CueBlurMax = 0.50;

    /// <summary>P(a called pitch lands on the targeted side of the zone edge) at control 50.</summary>
    public const double ExecutionBase = 0.75;

    /// <summary>Execution accuracy per point of effective control above 50.</summary>
    public const double ExecutionControlGain = 0.004;

    public const double ExecutionMin = 0.50;
    public const double ExecutionMax = 0.98;

    /// <summary>Tendency-model P(in zone) for one pitch — also the batter's scouting readout for a cued type.</summary>
    public static double ZoneProbability(in CountState count, byte effectiveControl, PitchType type)
    {
        double shift = type switch
        {
            PitchType.Fastball => ZoneShiftFastball,
            PitchType.Breaking => ZoneShiftBreaking,
            _ => ZoneShiftOffspeed,
        };
        return Math.Clamp(
            ZoneBase + ZoneControlGain * (effectiveControl - 50)
                     + ZoneBallGain * count.Balls + ZoneStrikeGain * count.Strikes + shift,
            ZoneMin, ZoneMax);
    }

    /// <summary>P(in zone) for a CALLED pitch: the target side, hit with control-driven accuracy.</summary>
    public static double ExecutedZoneProbability(bool targetInZone, byte effectiveControl)
    {
        double accuracy = Math.Clamp(
            ExecutionBase + ExecutionControlGain * (effectiveControl - 50), ExecutionMin, ExecutionMax);
        return targetInZone ? accuracy : 1.0 - accuracy;
    }

    private const int BallStates = 4;   // b ∈ 0..3
    private const int StrikeStates = 3; // s ∈ 0..2
    private const int CountStates = BallStates * StrikeStates;

    private const int SolverMaxIterations = 60;
    private const double SolverTolerance = 1e-12;
    private const double SolverStepEpsilon = 1e-7;
    private const double RateFloor = 1e-4;

    // ------------------------------------------------------------------
    // §5.2 — absorption analysis and the neutral inversion
    // ------------------------------------------------------------------

    /// <summary>
    /// Analytic absorption probabilities and expected pitch count of the count
    /// chain from (0,0) under the given per-pitch class rates — the §4
    /// fundamental-matrix quantities, computed by direct back-substitution
    /// (the chain is acyclic apart from the two-strike foul self-loop).
    /// Exposed so the harness can prove the §5.2 pin analytically.
    /// </summary>
    public static void ComputeAbsorption(
        in PitchClassRates rates, out double walkProbability, out double strikeoutProbability,
        out double expectedPitches)
    {
        double ball = rates.Ball;
        double strike = rates.StrikeClass;

        Span<double> toWalk = stackalloc double[CountStates];
        Span<double> toStrikeout = stackalloc double[CountStates];
        Span<double> pitches = stackalloc double[CountStates];

        for (int b = BallStates - 1; b >= 0; b--)
        {
            for (int s = StrikeStates - 1; s >= 0; s--)
            {
                int state = b * StrikeStates + s;
                int ballSuccessor = (b + 1) * StrikeStates + s;
                bool ballWalks = b == BallStates - 1;

                if (s == StrikeStates - 1)
                {
                    // Two strikes: a foul strike-class pitch self-loops.
                    double renorm = 1.0 - strike * FoulShareOfStrikes;
                    double strikeoutMass = strike * (1.0 - FoulShareOfStrikes);
                    toWalk[state] = ball * (ballWalks ? 1.0 : toWalk[ballSuccessor]) / renorm;
                    toStrikeout[state] =
                        (strikeoutMass + ball * (ballWalks ? 0.0 : toStrikeout[ballSuccessor])) / renorm;
                    pitches[state] = (1.0 + ball * (ballWalks ? 0.0 : pitches[ballSuccessor])) / renorm;
                }
                else
                {
                    int strikeSuccessor = state + 1;
                    toWalk[state] = ball * (ballWalks ? 1.0 : toWalk[ballSuccessor]) + strike * toWalk[strikeSuccessor];
                    toStrikeout[state] =
                        ball * (ballWalks ? 0.0 : toStrikeout[ballSuccessor]) + strike * toStrikeout[strikeSuccessor];
                    pitches[state] = 1.0 + ball * (ballWalks ? 0.0 : pitches[ballSuccessor]) + strike * pitches[strikeSuccessor];
                }
            }
        }

        walkProbability = toWalk[0];
        strikeoutProbability = toStrikeout[0];
        expectedPitches = pitches[0];
    }

    /// <summary>
    /// §5.2 inversion: recovers the count-independent per-pitch class rates
    /// whose absorption from (0,0) hits the macro anchor's Walk and Strikeout
    /// probabilities exactly (in-play mass is the complement). Newton on the
    /// two free rates with a finite-difference Jacobian; the forward solve is
    /// 12 states, so this costs microseconds per PA. Throws if the target is
    /// outside the chain's reachable set — impossible for any distribution the
    /// shared resolver can emit with calibrated tables.
    /// </summary>
    public static PitchClassRates SolveNeutral(double targetWalk, double targetStrikeout)
    {
        double ball = 0.36;
        double strike = 0.46;

        for (int iteration = 0; iteration < SolverMaxIterations; iteration++)
        {
            ComputeAbsorption(new PitchClassRates(ball, strike, 1.0 - ball - strike),
                out double walk, out double strikeout, out _);
            double residualWalk = walk - targetWalk;
            double residualStrikeout = strikeout - targetStrikeout;
            if (Math.Abs(residualWalk) < SolverTolerance && Math.Abs(residualStrikeout) < SolverTolerance)
            {
                return new PitchClassRates(ball, strike, 1.0 - ball - strike);
            }

            // Finite-difference Jacobian, 2×2.
            ComputeAbsorption(new PitchClassRates(ball + SolverStepEpsilon, strike, 1.0 - ball - SolverStepEpsilon - strike),
                out double walkDb, out double strikeoutDb, out _);
            ComputeAbsorption(new PitchClassRates(ball, strike + SolverStepEpsilon, 1.0 - ball - strike - SolverStepEpsilon),
                out double walkDs, out double strikeoutDs, out _);
            double j00 = (walkDb - walk) / SolverStepEpsilon;
            double j01 = (walkDs - walk) / SolverStepEpsilon;
            double j10 = (strikeoutDb - strikeout) / SolverStepEpsilon;
            double j11 = (strikeoutDs - strikeout) / SolverStepEpsilon;

            double determinant = j00 * j11 - j01 * j10;
            if (Math.Abs(determinant) < 1e-14)
            {
                break;
            }
            double stepBall = (residualWalk * j11 - residualStrikeout * j01) / determinant;
            double stepStrike = (residualStrikeout * j00 - residualWalk * j10) / determinant;

            ball = Math.Clamp(ball - stepBall, RateFloor, 1.0 - 2.0 * RateFloor);
            strike = Math.Clamp(strike - stepStrike, RateFloor, 1.0 - ball - RateFloor);
        }

        throw new InvalidOperationException(
            $"Pitch-chain inversion did not converge for targets BB={targetWalk:F6}, K={targetStrikeout:F6}.");
    }

    // ------------------------------------------------------------------
    // §5/§6 — the per-pitch simulation of one human PA
    // ------------------------------------------------------------------

    /// <summary>
    /// Plays one contested PA pitch-by-pitch (v4). Per pitch: the pitcher
    /// policy calls (or the tendency model draws) a type and a zone target;
    /// an actual in/out-of-zone location is drawn; the batter policy answers a
    /// blurred type cue with a swing/take + zone guess; the read resolves
    /// against the actual location and maps through <see cref="PlayerInputModel"/>;
    /// finally the pitch class is drawn from the LOCATION-CONDITIONED rates —
    /// an exact mixture of the (input-perturbed) §5 rates, so neutral play
    /// still reproduces the macro anchor by construction (§7). Charges every
    /// pitch to the pitcher's fatigue clock. Zero heap allocation.
    /// </summary>
    /// <param name="anchorProbabilities">p* — the 7-way macro distribution this PA is pinned to.</param>
    public static PaOutcome SimulatePa<TBatter, TPitcher>(
        ReadOnlySpan<double> anchorProbabilities, in PitchClassRates neutralRates,
        in PitchMatchup matchup, ref TBatter batter, ref TPitcher pitcher,
        ref PitcherFatigue fatigue, ref RngState rng, out int pitchCount)
        where TBatter : IBatterPolicy
        where TPitcher : IPitcherPolicy
    {
        int balls = 0;
        int strikes = 0;
        pitchCount = 0;

        while (true)
        {
            var count = new CountState((byte)balls, (byte)strikes);

            // --- the pitch: type + location -------------------------------
            // zoneReference is what the calibration (and the batter's
            // scouting) assumes for this type/count — the conditional class
            // rates below are built from IT, so a neutral pitcher (location
            // drawn from the same reference) mixes back to the §5 rates
            // exactly, while a called pitch that aims off the reference
            // genuinely shifts outcomes (more balls when painting away, more
            // contact when challenging the zone).
            PitchCall call = pitcher.NextPitch(in count, ref rng);
            PitchType type = call.IsCalled ? call.Type : matchup.Arsenal.DrawType(ref rng);
            double zoneReference = ZoneProbability(in count, matchup.PitcherControl, type);
            double zoneDraw = call.IsCalled
                ? ExecutedZoneProbability(call.TargetInZone, matchup.PitcherControl)
                : zoneReference;
            bool inZone = rng.NextDouble() < zoneDraw;

            // --- the look: cue blurred by movement vs discipline ----------
            double blur = Math.Clamp(
                CueBlurBase + CueBlurMovementGain * (matchup.Arsenal.Movement(type) - 50)
                            - CueBlurDisciplineGain * (matchup.BatterDiscipline - 50),
                CueBlurMin, CueBlurMax);
            PitchType cue = type;
            if (rng.NextDouble() < blur)
            {
                // One of the two wrong types, uniformly.
                int wrong = rng.NextInt(2);
                cue = (PitchType)(((int)type + 1 + wrong) % 3);
            }
            var look = new PitchLook(cue, ZoneProbability(in count, matchup.PitcherControl, cue));

            // --- the batter's answer, resolved against the actual location -
            BatterIntent intent = batter.NextPitch(in look, in count, ref rng);
            BatterPitchInput input;
            switch (intent.Kind)
            {
                case BatterIntentKind.Take:
                    input = PlayerInputModel.FromTake(intent.GuessInZone == inZone);
                    break;
                case BatterIntentKind.Swing:
                    input = PlayerInputModel.FromSwing(
                        intent.Timing, intent.GuessInZone == inZone,
                        PlayerInputModel.PerceivedStuff(
                            fatigue.EffectiveRatings().Stuff,
                            matchup.Arsenal.Velocity(type), matchup.Arsenal.Movement(type)));
                    break;
                default: // Neutral — the §7 anchor: identically zero input.
                    input = default;
                    break;
            }

            // §6 discipline edge shifts per-pitch mass between balls and
            // strikes, renormalized — a zero edge leaves the neutral rates
            // bit-identical (the consistency anchor).
            double ball = neutralRates.Ball;
            double strike = neutralRates.StrikeClass;
            double inPlay = neutralRates.InPlay;
            double edge = Math.Clamp(input.DisciplineEdge, -1.0, 1.0);
            if (edge != 0.0)
            {
                ball *= 1.0 + DisciplineBallGain * edge;
                strike *= 1.0 - DisciplineStrikeGain * edge;
                double total = ball + strike + inPlay;
                ball /= total;
                strike /= total;
            }

            // --- location-conditioned class rates (exact mixture) ----------
            // Built from the REFERENCE zone probability zᵣ: ballIn = ball(1−s)
            // and ballOut carries the balancing surplus, so zᵣ·ballIn +
            // (1−zᵣ)·ballOut = ball for any zᵣ and s. When the location is
            // drawn from zᵣ (neutral pitching) the marginal is therefore
            // preserved by construction; a called pitch draws its location
            // elsewhere and genuinely re-weights the mixture. Strike/in-play
            // scale by the shared factor k = (1 − ballCond)/(1 − ball), which
            // keeps their ratio and mixes back the same way.
            double conditionedBall = ball;
            if (ball > 0.0 && ball < 1.0 && zoneReference > 0.0 && zoneReference < 1.0)
            {
                double separation = ZoneSeparation;
                double surplusFactor = separation * zoneReference / (1.0 - zoneReference);
                if (ball * (1.0 + surplusFactor) > MaxConditionalBall)
                {
                    separation = (MaxConditionalBall / ball - 1.0) * (1.0 - zoneReference) / zoneReference;
                }
                conditionedBall = inZone
                    ? ball * (1.0 - separation)
                    : ball * (1.0 + separation * zoneReference / (1.0 - zoneReference));
            }
            double conditionScale = ball < 1.0 ? (1.0 - conditionedBall) / (1.0 - ball) : 0.0;
            double conditionedStrike = strike * conditionScale;

            pitchCount++;
            fatigue.AddPitch();

            bool swung = intent.Kind == BatterIntentKind.Swing;
            double draw = rng.NextDouble();
            if (draw < conditionedBall)
            {
                bool walked = ++balls == BallStates;
                batter.OnPitchResolved(new PitchResult(
                    PitchClass.Ball, type, inZone, swung, (byte)balls, (byte)strikes, walked));
                if (walked)
                {
                    return PaOutcome.Walk;
                }
            }
            else if (draw < conditionedBall + conditionedStrike)
            {
                if (strikes < StrikeStates - 1)
                {
                    strikes++;
                    batter.OnPitchResolved(new PitchResult(
                        PitchClass.Strike, type, inZone, swung, (byte)balls, (byte)strikes, paEnded: false));
                }
                else if (rng.NextDouble() >= FoulShareOfStrikes)
                {
                    batter.OnPitchResolved(new PitchResult(
                        PitchClass.Strike, type, inZone, swung, (byte)balls, (byte)strikes, paEnded: true));
                    return PaOutcome.Strikeout;
                }
                else
                {
                    // two-strike foul — count unchanged, PA continues.
                    batter.OnPitchResolved(new PitchResult(
                        PitchClass.Foul, type, inZone, swung, (byte)balls, (byte)strikes, paEnded: false));
                }
            }
            else
            {
                batter.OnPitchResolved(new PitchResult(
                    PitchClass.InPlay, type, inZone, swung, (byte)balls, (byte)strikes, paEnded: true));
                return DrawBallInPlay(anchorProbabilities, input.ContactQuality, ref rng);
            }
        }
    }

    /// <summary>
    /// §5.3 BIP resolution: the anchor renormalized over its five in-play
    /// buckets, log-linearly perturbed by contact quality q (clamped). q = 0
    /// is exactly the macro BIP-conditional split.
    /// </summary>
    public static PaOutcome DrawBallInPlay(
        ReadOnlySpan<double> anchorProbabilities, double contactQuality, ref RngState rng)
    {
        double quality = Math.Clamp(contactQuality, -1.0, 1.0);

        // In-play buckets are Out plus the four hits: outcome indices 0, 3..6.
        Span<double> weights = stackalloc double[5];
        weights[0] = anchorProbabilities[(int)PaOutcome.Out];
        weights[1] = anchorProbabilities[(int)PaOutcome.Single];
        weights[2] = anchorProbabilities[(int)PaOutcome.Double];
        weights[3] = anchorProbabilities[(int)PaOutcome.Triple];
        weights[4] = anchorProbabilities[(int)PaOutcome.HomeRun];

        double total = 0.0;
        for (int i = 0; i < weights.Length; i++)
        {
            if (quality != 0.0)
            {
                weights[i] *= Math.Exp(quality * BipQualityGains[i]);
            }
            total += weights[i];
        }

        double draw = rng.NextDouble() * total;
        double cumulative = 0.0;
        for (int i = 0; i < weights.Length - 1; i++)
        {
            cumulative += weights[i];
            if (draw < cumulative)
            {
                return i == 0 ? PaOutcome.Out : (PaOutcome)((int)PaOutcome.Single + i - 1);
            }
        }
        return PaOutcome.HomeRun;
    }
}

/// <summary>
/// §6 mapping from the raw player intent the UI captures (swing timing error
/// and a zone read) to the two scalars the pitch chain consumes. Lives sim-side
/// so the UI stays a dumb signal emitter; the neutral policy bypasses this
/// entirely (its input is identically zero). All constants are feel knobs
/// (§11 tuning step 4).
/// </summary>
public static class PlayerInputModel
{
    /// <summary>Base half-width of the barrel window in normalized timing units.</summary>
    public const double TimingToleranceBase = 0.35;

    /// <summary>How much a 100-stuff pitcher narrows the barrel window (§6: nastier stuff, smaller window).</summary>
    public const double TimingToleranceStuffNarrowing = 0.40;

    /// <summary>A correct zone read widens the timing tolerance (§6).</summary>
    public const double CorrectReadToleranceBonus = 1.5;

    /// <summary>Discipline edge granted by a correct zone read.</summary>
    public const double CorrectReadDisciplineEdge = 0.25;

    /// <summary>Discipline penalty for chasing on a wrong read.</summary>
    public const double WrongReadDisciplineEdge = -0.25;

    /// <summary>
    /// Swing: signed timing error τ ∈ [-1, +1] maps to contact quality
    /// q = 1 − (τ/τ_tol)², clamped — peaked at on-time, negative when badly
    /// mistimed. τ_tol narrows as effective pitcher stuff rises.
    /// </summary>
    public static BatterPitchInput FromSwing(double timingError, bool zoneReadCorrect, byte effectivePitcherStuff)
    {
        double tolerance = TimingToleranceBase
            * (1.0 - TimingToleranceStuffNarrowing * Math.Max(0, effectivePitcherStuff - 50) / 50.0);
        if (zoneReadCorrect)
        {
            tolerance *= CorrectReadToleranceBonus;
        }
        double normalized = timingError / tolerance;
        double quality = Math.Clamp(1.0 - normalized * normalized, -1.0, 1.0);
        double edge = zoneReadCorrect ? CorrectReadDisciplineEdge : WrongReadDisciplineEdge;
        return new BatterPitchInput(edge, quality);
    }

    /// <summary>Take: no contact quality in play; discipline edge from the read.</summary>
    public static BatterPitchInput FromTake(bool zoneReadCorrect) =>
        new(zoneReadCorrect ? CorrectReadDisciplineEdge : WrongReadDisciplineEdge, 0.0);

    /// <summary>Weight of pitch-type velocity above 50 in the perceived-stuff blend (v4).</summary>
    public const double PerceivedStuffVelocityWeight = 0.30;

    /// <summary>Weight of pitch-type movement above 50 in the perceived-stuff blend (v4).</summary>
    public const double PerceivedStuffMovementWeight = 0.20;

    /// <summary>
    /// v4: the stuff rating the timing window is judged against, per pitch —
    /// the pitcher's (fatigue-adjusted) stuff sharpened or dulled by the
    /// thrown type's velocity and movement. At a 50/50 pitch this is exactly
    /// the effective stuff, so pre-v4 feel is the league-average case.
    /// </summary>
    public static byte PerceivedStuff(byte effectiveStuff, byte velocity, byte movement) =>
        (byte)Math.Clamp(
            (int)Math.Round(
                effectiveStuff
                + PerceivedStuffVelocityWeight * (velocity - 50)
                + PerceivedStuffMovementWeight * (movement - 50),
                MidpointRounding.AwayFromZero),
            0, 100);
}
