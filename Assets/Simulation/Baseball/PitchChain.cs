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
/// The human is an input stream; the neutral policy is its headless stand-in
/// (§6.1/§9). Implemented by structs and consumed through a generic constraint
/// so the per-pitch call devirtualizes — no boxing, no allocation.
/// </summary>
public interface IBatterPolicy
{
    BatterPitchInput NextPitch(in CountState count, ref RngState rng);
}

/// <summary>§6.1 neutral autopilot: plays exactly to the ratings (all-zero input).</summary>
public struct NeutralBatterPolicy : IBatterPolicy
{
    public readonly BatterPitchInput NextPitch(in CountState count, ref RngState rng) => default;
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
    /// Plays one interactive PA pitch-by-pitch: draws pitch classes from the
    /// neutral rates perturbed by the policy's per-pitch input, walks the count
    /// to an absorbing exit, and resolves the BIP exit from the anchor's
    /// renormalized in-play split perturbed by contact quality (§5.3). Charges
    /// every pitch to the pitcher's fatigue clock. Zero heap allocation.
    /// </summary>
    /// <param name="anchorProbabilities">p* — the 7-way macro distribution this PA is pinned to.</param>
    public static PaOutcome SimulatePa<TPolicy>(
        ReadOnlySpan<double> anchorProbabilities, in PitchClassRates neutralRates,
        ref TPolicy policy, ref PitcherFatigue fatigue, ref RngState rng, out int pitchCount)
        where TPolicy : IBatterPolicy
    {
        int balls = 0;
        int strikes = 0;
        pitchCount = 0;

        while (true)
        {
            var count = new CountState((byte)balls, (byte)strikes);
            BatterPitchInput input = policy.NextPitch(in count, ref rng);

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

            pitchCount++;
            fatigue.AddPitch();

            double draw = rng.NextDouble();
            if (draw < ball)
            {
                if (++balls == BallStates)
                {
                    return PaOutcome.Walk;
                }
            }
            else if (draw < ball + strike)
            {
                if (strikes < StrikeStates - 1)
                {
                    strikes++;
                }
                else if (rng.NextDouble() >= FoulShareOfStrikes)
                {
                    return PaOutcome.Strikeout;
                }
                // else: two-strike foul — count unchanged, PA continues.
            }
            else
            {
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
}
