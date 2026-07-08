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

    /// <summary>
    /// Batter contact rating — consumed only by the Kind == Read (human) branch,
    /// where it sets the contact/whiff floor and ceiling on emergent swings
    /// (at_bat_read_input_model.md §3.4). Neutral/Take/Swing ignore it. The
    /// three-arg ctor defaults it to the league-average 50, so existing call
    /// sites are unaffected.
    /// </summary>
    public readonly byte BatterContact;

    public PitchMatchup(in PitcherArsenal arsenal, byte pitcherControl, byte batterDiscipline)
        : this(in arsenal, pitcherControl, batterDiscipline, batterContact: 50)
    {
    }

    public PitchMatchup(in PitcherArsenal arsenal, byte pitcherControl, byte batterDiscipline, byte batterContact)
    {
        Arsenal = arsenal;
        PitcherControl = pitcherControl;
        BatterDiscipline = batterDiscipline;
        BatterContact = batterContact;
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

/// <summary>How a batter answers a pitch. Neutral = play the ratings (§6.1 zero input).</summary>
public enum BatterIntentKind : byte
{
    Neutral,

    /// <summary>
    /// The "Read the Pitch" input (at_bat_read_input_model.md): the player submits
    /// a guessed type + a 3×3 zone cell + an approach dial, and the swing itself is
    /// emergent. Only this branch of <see cref="PitchChain.SimulatePa"/> runs the
    /// belief/swing model — Neutral stays byte-identical (Invariant N).
    /// </summary>
    Read,
}

/// <summary>
/// One pitch worth of raw batter intent. The chain resolves the read against the
/// actual drawn location on the <see cref="BatterIntentKind.Read"/> branch; a
/// Neutral intent (default) bypasses it entirely so the §7 consistency identity
/// holds bit-exactly.
/// </summary>
public readonly struct BatterIntent
{
    public readonly BatterIntentKind Kind;

    // --- Kind == Read fields (at_bat_read_input_model.md §2) --------------
    // All default to zero so `default` stays a Neutral intent (Invariant N).

    /// <summary>Read only: the player's guessed pitch type.</summary>
    public readonly PitchType GuessType;

    /// <summary>Read only: the guessed 3×3 zone cell (0..8), or <see cref="ReadInputModel.OutOfZoneCell"/> for "expect a ball".</summary>
    public readonly byte GuessCell;

    /// <summary>Read only: approach dial τ_appr ∈ [-1, +1] (patient ↔ aggressive), default 0.</summary>
    public readonly double Approach;

    /// <summary>
    /// Read only: when true, the read is graded from the <c>Scripted*</c> fields
    /// directly instead of <see cref="GuessType"/>/<see cref="GuessCell"/> vs. the
    /// true draw. The oracle/harness seam — a "perfect reader" cannot be expressed
    /// as a blind guess because the batter never sees the true location.
    /// </summary>
    public readonly bool ReadIsScripted;
    public readonly bool ScriptedTypeOk;
    public readonly bool ScriptedInOutOk;
    public readonly double ScriptedLocAcc;

    private BatterIntent(
        PitchType guessType, byte guessCell, double approach,
        bool readIsScripted, bool scriptedTypeOk, bool scriptedInOutOk, double scriptedLocAcc)
    {
        Kind = BatterIntentKind.Read;
        GuessType = guessType;
        GuessCell = guessCell;
        Approach = approach;
        ReadIsScripted = readIsScripted;
        ScriptedTypeOk = scriptedTypeOk;
        ScriptedInOutOk = scriptedInOutOk;
        ScriptedLocAcc = scriptedLocAcc;
    }

    public static BatterIntent Neutral => default;

    /// <summary>A "Read the Pitch" intent: guessed type + zone cell (or "expect a ball") + approach dial.</summary>
    public static BatterIntent Read(PitchType guessType, byte guessCell, double approach) =>
        new(guessType, guessCell, approach, readIsScripted: false, false, false, 0.0);

    /// <summary>
    /// Oracle/harness seam: a Read intent whose grade is supplied directly (a scripted
    /// perfect/fooled reader), bypassing the guess-vs-truth grading in <see cref="PitchChain.SimulatePa"/>.
    /// </summary>
    public static BatterIntent ScriptedRead(bool typeOk, bool inOutOk, double locAcc, double approach) =>
        new(default, ReadInputModel.OutOfZoneCell, approach,
            readIsScripted: true, typeOk, inOutOk, Math.Clamp(locAcc, 0.0, 1.0));
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
    /// Plays one contested PA pitch-by-pitch. Per pitch: the pitcher policy
    /// calls (or the tendency model draws) a type and a zone target; an actual
    /// in/out-of-zone location is drawn; the batter policy answers a blurred
    /// type cue with either a Neutral (§7 anchor) or a Read intent — the
    /// latter resolves via <see cref="ReadInputModel"/> into an emergent
    /// swing/take, gating the reachable pitch class; a Neutral intent instead
    /// draws the pitch class from the LOCATION-CONDITIONED rates — an exact
    /// mixture of the §5 rates, so neutral play still reproduces the macro
    /// anchor by construction (§7). Charges every pitch to the pitcher's
    /// fatigue clock. Zero heap allocation.
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

            // ============================================================
            // Kind == Read (human) branch — at_bat_read_input_model.md §3.
            // Fully self-contained: it draws a location sub-cell, forms a
            // strike-belief, decides the swing emergently, and GATES the pitch
            // class on that swing (a take is only Ball/called-Strike; a swing
            // is only whiff/Foul/InPlay). It never touches the Neutral/Take/
            // Swing path below, so Invariant N holds by construction — the
            // calibrated branch keeps today's exact code and RNG sequence.
            // ============================================================
            if (intent.Kind == BatterIntentKind.Read)
            {
                // §3.1 sub-cell for the true location — human branch only, so
                // the neutral branch's RNG sequence is untouched.
                byte trueCell = inZone ? (byte)rng.NextInt(9) : ReadInputModel.OutOfZoneCell;

                bool typeOk, inOutOk;
                double locAcc;
                if (intent.ReadIsScripted)
                {
                    typeOk = intent.ScriptedTypeOk;
                    inOutOk = intent.ScriptedInOutOk;
                    locAcc = intent.ScriptedLocAcc;
                }
                else
                {
                    ReadInputModel.GradeRead(
                        intent.GuessType, intent.GuessCell, type, inZone, trueCell,
                        out typeOk, out inOutOk, out locAcc);
                }

                double believedStrike = ReadInputModel.BelievedStrike(
                    inZone, look.ZoneProbability, inOutOk, locAcc, matchup.BatterDiscipline);
                double pSwing = ReadInputModel.SwingProbability(
                    in count, believedStrike, intent.Approach, matchup.BatterDiscipline);
                bool swings = rng.NextDouble() < pSwing;

                pitchCount++;
                fatigue.AddPitch();

                if (!swings)
                {
                    // Take: called strike in the zone, ball out of it. Never Foul/InPlay.
                    if (inZone)
                    {
                        if (strikes < StrikeStates - 1)
                        {
                            strikes++;
                            batter.OnPitchResolved(new PitchResult(
                                PitchClass.Strike, type, inZone, false, (byte)balls, (byte)strikes, paEnded: false, typeOk, locAcc));
                            continue;
                        }
                        batter.OnPitchResolved(new PitchResult(
                            PitchClass.Strike, type, inZone, false, (byte)balls, (byte)strikes, paEnded: true, typeOk, locAcc));
                        return PaOutcome.Strikeout; // called third strike
                    }
                    bool walkedOnTake = ++balls == BallStates;
                    batter.OnPitchResolved(new PitchResult(
                        PitchClass.Ball, type, inZone, false, (byte)balls, (byte)strikes, walkedOnTake, typeOk, locAcc));
                    if (walkedOnTake)
                    {
                        return PaOutcome.Walk;
                    }
                    continue;
                }

                // Swing: whiff, Foul, or InPlay — never Ball.
                double readQuality = ReadInputModel.ReadQuality(typeOk, locAcc);
                bool madeContact = rng.NextDouble()
                    < ReadInputModel.ContactProbability(readQuality, matchup.BatterContact);
                if (!madeContact)
                {
                    if (strikes < StrikeStates - 1)
                    {
                        strikes++;
                        batter.OnPitchResolved(new PitchResult(
                            PitchClass.Strike, type, inZone, true, (byte)balls, (byte)strikes, paEnded: false, typeOk, locAcc));
                        continue;
                    }
                    batter.OnPitchResolved(new PitchResult(
                        PitchClass.Strike, type, inZone, true, (byte)balls, (byte)strikes, paEnded: true, typeOk, locAcc));
                    return PaOutcome.Strikeout; // swinging third strike
                }

                bool atTwoStrikes = strikes == StrikeStates - 1;
                double foulShare = atTwoStrikes ? FoulShareOfStrikes : ReadInputModel.EarlyFoulShare;
                if (rng.NextDouble() < foulShare)
                {
                    if (atTwoStrikes)
                    {
                        // Two-strike foul: count unchanged, PA continues (§5.1 self-loop).
                        batter.OnPitchResolved(new PitchResult(
                            PitchClass.Foul, type, inZone, true, (byte)balls, (byte)strikes, paEnded: false, typeOk, locAcc));
                        continue;
                    }
                    // Sub-two-strike foul advances the count — surfaces as a Strike (12d-1 frozen contract).
                    strikes++;
                    batter.OnPitchResolved(new PitchResult(
                        PitchClass.Strike, type, inZone, true, (byte)balls, (byte)strikes, paEnded: false, typeOk, locAcc));
                    continue;
                }

                // In play — the read-derived contact quality feeds the UNCHANGED §5.3 BIP split.
                double q = ReadInputModel.ContactQuality(readQuality, matchup.BatterContact);
                batter.OnPitchResolved(new PitchResult(
                    PitchClass.InPlay, type, inZone, true, (byte)balls, (byte)strikes, paEnded: true, typeOk, locAcc));
                return DrawBallInPlay(anchorProbabilities, q, ref rng);
            }

            // Neutral — the only Kind left at this point (Read already returned
            // above) — the §7 anchor: identically zero input.
            BatterPitchInput input = default;

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

            // Neutral never swings — the §7 anchor: a called pitch is never batter-initiated.
            const bool swung = false;
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
/// The "Read the Pitch" model (at_bat_read_input_model.md §3), consumed ONLY by
/// the Kind == Read branch of <see cref="PitchChain.SimulatePa"/>. Pure struct
/// math, no allocation. The pipeline is:
///
///   grade      — the guessed type + 3×3 cell vs. the true draw → (typeOk, inOutOk, locAcc)
///   belief     — the location call sets the SIGN of the strike-belief move, its
///                magnitude (conviction) grows with cell precision and discipline;
///                a wrong in/out call INVERTS belief (reads are decisive: you're fooled)
///   swing      — pSwing = base(count) + approach·gain + read·gain·(belief − threshold)
///   contact    — a blended read-quality R sets the whiff floor and the in-play
///                contact quality q (fed to the unchanged §5.3 BIP split)
///
/// Every constant is a first-pass feel knob; the A0 sweep (perfect / random /
/// fooled reader) is what proves the spread is dramatic but not degenerate before
/// the UI is built on top. None of this touches the calibrated neutral path.
/// </summary>
public static class ReadInputModel
{
    /// <summary>Sentinel <see cref="BatterIntent.GuessCell"/> value: "expect a ball" (out of the zone). Cells 0..8 are the 3×3 grid.</summary>
    public const byte OutOfZoneCell = 9;

    // --- §3.1 read grading -------------------------------------------------

    /// <summary>Location accuracy for a correct in-zone call one cell (Chebyshev) off the true cell.</summary>
    public const double LocAccAdjacent = 0.6;

    /// <summary>Location accuracy for a correct in-zone call two cells off the true cell.</summary>
    public const double LocAccFar = 0.3;

    // --- §3.2.1 read quality (drives contact, §3.4) ------------------------

    public const double ReadTypeWeight = 0.40;
    public const double ReadLocWeight = 0.60;

    // --- §3.2.2 belief conviction ------------------------------------------

    /// <summary>Base fraction of the way belief moves from the scouting prior toward its (correct or inverted) target.</summary>
    public const double ConvictionBase = 0.70;

    /// <summary>Extra conviction per unit of location accuracy (a precise cell read is more decisive).</summary>
    public const double ConvictionLocGain = 0.30;

    /// <summary>
    /// Discipline's effect on conviction, SIGNED by whether the in/out call was right:
    /// a disciplined hitter commits harder to a correct read and is fooled less by a
    /// wrong one (§3.2.1 "punished less by a marginal one").
    /// </summary>
    public const double ConvictionDisciplineGain = 0.30;

    // --- §3.2.3 swing decision ---------------------------------------------

    public const double SwingBaseInit = 0.45;
    public const double SwingBasePerStrike = 0.10;   // protect with two strikes
    public const double SwingBasePerBall = -0.05;    // take when ahead in the count
    public const double SwingBaseMin = 0.15;
    public const double SwingBaseMax = 0.85;

    /// <summary>How far the approach dial (±1) shifts the swing probability.</summary>
    public const double SwingApproachGain = 0.25;

    /// <summary>How hard believed-strike drives the swing — the "reads decisive" knob.</summary>
    public const double SwingReadGain = 0.90;

    /// <summary>Believed-strike level at which a league-average hitter is 50/50 to offer.</summary>
    public const double SwingThresholdBase = 0.50;

    /// <summary>Discipline raises the threshold (only swing when quite sure it's a strike).</summary>
    public const double SwingThresholdDisciplineGain = 0.10;

    // --- §3.3 / §3.4 contact -----------------------------------------------

    public const double ContactProbBase = 0.60;
    public const double ContactProbReadGain = 0.25;
    public const double ContactProbRatingGain = 0.15;
    public const double ContactProbMin = 0.15;
    public const double ContactProbMax = 0.92;

    public const double ContactQualityReadGain = 0.90;
    public const double ContactQualityRatingGain = 0.30;

    /// <summary>Share of sub-two-strike swing contact that fouls off (surfaces as a count-advancing Strike, per the 12d-1 contract).</summary>
    public const double EarlyFoulShare = 0.30;

    /// <summary>3×3 Chebyshev distance between two cells 0..8 (row = c/3, col = c%3).</summary>
    public static int CellDistance(byte a, byte b) =>
        Math.Max(Math.Abs(a / 3 - b / 3), Math.Abs(a % 3 - b % 3));

    /// <summary>§3.1: grade a live guess against the true draw.</summary>
    public static void GradeRead(
        PitchType guessType, byte guessCell, PitchType trueType, bool inZone, byte trueCell,
        out bool typeOk, out bool inOutOk, out double locAcc)
    {
        typeOk = guessType == trueType;
        bool guessInZone = guessCell < OutOfZoneCell;
        inOutOk = guessInZone == inZone;
        if (!inOutOk)
        {
            locAcc = 0.0; // wrong in/out call — the worst read
        }
        else if (!inZone)
        {
            locAcc = 1.0; // correctly called a ball (coarse OUT, v1: no sub-direction)
        }
        else
        {
            locAcc = CellDistance(guessCell, trueCell) switch { 0 => 1.0, 1 => LocAccAdjacent, _ => LocAccFar };
        }
    }

    /// <summary>§3.2.1 blended read quality R ∈ [0,1] — the contact driver.</summary>
    public static double ReadQuality(bool typeOk, double locAcc) =>
        ReadTypeWeight * (typeOk ? 1.0 : 0.0) + ReadLocWeight * locAcc;

    /// <summary>§3.2.2 believed-strike ∈ [0,1]: the location call signs the move, conviction sizes it, a wrong call inverts.</summary>
    public static double BelievedStrike(bool inZone, double zonePrior, bool inOutOk, double locAcc, byte discipline)
    {
        double truth = inZone ? 1.0 : 0.0;
        double d = Math.Clamp((discipline - 50) / 50.0, -1.0, 1.0);
        double commit = Math.Clamp(
            ConvictionBase + ConvictionLocGain * locAcc + ConvictionDisciplineGain * d * (inOutOk ? 1.0 : -1.0),
            0.0, 1.0);
        double target = inOutOk ? truth : 1.0 - truth;
        return Math.Clamp(zonePrior + commit * (target - zonePrior), 0.0, 1.0);
    }

    /// <summary>§3.2.3 swing probability ∈ [0,1].</summary>
    public static double SwingProbability(in CountState count, double believedStrike, double approach, byte discipline)
    {
        double d = Math.Clamp((discipline - 50) / 50.0, -1.0, 1.0);
        double baseSwing = Math.Clamp(
            SwingBaseInit + SwingBasePerStrike * count.Strikes + SwingBasePerBall * count.Balls,
            SwingBaseMin, SwingBaseMax);
        double threshold = SwingThresholdBase + SwingThresholdDisciplineGain * d;
        double p = baseSwing
            + SwingApproachGain * Math.Clamp(approach, -1.0, 1.0)
            + SwingReadGain * (believedStrike - threshold);
        return Math.Clamp(p, 0.0, 1.0);
    }

    /// <summary>§3.4 P(contact | swing): read quality sets the floor, contact rating the ceiling.</summary>
    public static double ContactProbability(double readQuality, byte contactRating)
    {
        double c = Math.Clamp((contactRating - 50) / 50.0, -1.0, 1.0);
        return Math.Clamp(
            ContactProbBase + ContactProbReadGain * (2.0 * readQuality - 1.0) + ContactProbRatingGain * c,
            ContactProbMin, ContactProbMax);
    }

    /// <summary>§3.4 contact quality q ∈ [-1,+1] fed to the unchanged §5.3 BIP split.</summary>
    public static double ContactQuality(double readQuality, byte contactRating)
    {
        double c = Math.Clamp((contactRating - 50) / 50.0, -1.0, 1.0);
        return Math.Clamp(
            ContactQualityReadGain * (2.0 * readQuality - 1.0) + ContactQualityRatingGain * c,
            -1.0, 1.0);
    }
}
