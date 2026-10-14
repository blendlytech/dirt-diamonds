using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Simulation.Arcade;

/// <summary>
/// One pitcher's arcade-relevant ratings (arcade_baseball_overhaul.md §4.1):
/// the Pitch_Arsenals velocity / movement / usage per family, control, the
/// live fatigue multiplier (PitcherFatigue, 1 = fresh, floor 0.5) and the
/// throwing hand. In G-4 this is filled from EffectiveRatings; in G-0 the
/// prototype scene sets it directly.
/// </summary>
public readonly struct PitcherProfile
{
    public readonly byte FastballVelocity, FastballMovement, FastballUsage;
    public readonly byte BreakingVelocity, BreakingMovement, BreakingUsage;
    public readonly byte OffspeedVelocity, OffspeedMovement, OffspeedUsage;
    public readonly byte Control;
    public readonly double FatigueMultiplier;
    public readonly Hand Throws;

    public PitcherProfile(
        byte fastballVelocity, byte fastballMovement, byte fastballUsage,
        byte breakingVelocity, byte breakingMovement, byte breakingUsage,
        byte offspeedVelocity, byte offspeedMovement, byte offspeedUsage,
        byte control, double fatigueMultiplier, Hand throws)
    {
        FastballVelocity = fastballVelocity;
        FastballMovement = fastballMovement;
        FastballUsage = fastballUsage;
        BreakingVelocity = breakingVelocity;
        BreakingMovement = breakingMovement;
        BreakingUsage = breakingUsage;
        OffspeedVelocity = offspeedVelocity;
        OffspeedMovement = offspeedMovement;
        OffspeedUsage = offspeedUsage;
        Control = control;
        FatigueMultiplier = fatigueMultiplier;
        Throws = throws;
    }

    /// <summary>League-average silhouette (the shipped DDL backfill shape): 60/25/15 usage, all ratings 50.</summary>
    public static PitcherProfile LeagueAverage(Hand throws = Hand.Right) =>
        new(50, 50, 60, 50, 50, 25, 50, 50, 15, 50, 1.0, throws);

    public byte Velocity(PitchKind kind) => kind switch
    {
        PitchKind.Fastball => FastballVelocity,
        PitchKind.Breaking => BreakingVelocity,
        _ => OffspeedVelocity,
    };

    public byte Movement(PitchKind kind) => kind switch
    {
        PitchKind.Fastball => FastballMovement,
        PitchKind.Breaking => BreakingMovement,
        _ => OffspeedMovement,
    };

    public byte Usage(PitchKind kind) => kind switch
    {
        PitchKind.Fastball => FastballUsage,
        PitchKind.Breaking => BreakingUsage,
        _ => OffspeedUsage,
    };
}

/// <summary>A fully specified pitch at release: what was intended, where it was actually aimed after scatter, and the launch state.</summary>
public readonly struct PitchRelease
{
    public readonly PitchKind Kind;
    public readonly double SpeedMph;
    /// <summary>The point at the plate the pitcher meant to hit (X, Z at <see cref="ArcadeTuning.PlateFrontY"/>).</summary>
    public readonly Vec3 IntendedTarget;
    /// <summary>The intended target after control scatter: what the release velocity was solved for.</summary>
    public readonly Vec3 ActualTarget;
    public readonly BallState Launch;
    /// <summary>Aftertouch steer (m/s²) applied during the first <see cref="ArcadeTuning.AftertouchWindowSec"/> of flight.</summary>
    public readonly Vec3 Aftertouch;

    public PitchRelease(PitchKind kind, double speedMph, in Vec3 intendedTarget, in Vec3 actualTarget, in BallState launch, in Vec3 aftertouch)
    {
        Kind = kind;
        SpeedMph = speedMph;
        IntendedTarget = intendedTarget;
        ActualTarget = actualTarget;
        Launch = launch;
        Aftertouch = aftertouch;
    }
}

/// <summary>
/// Pitch construction (arcade_baseball_overhaul.md §3.1): speed and spin
/// from the arsenal ratings and tier, a shooting solver that finds the
/// release velocity whose real (drag + Magnus) flight passes through a
/// target at the plate, Gaussian control scatter, and bounded aftertouch.
/// All draws come from the caller's <see cref="RngState"/>.
/// </summary>
public static class PitchModel
{
    /// <summary>
    /// The pitch's spin vector (rad/s) for a pitcher of the given hand.
    /// Derivation (field frame, ball moving −Y): ω = (−1,0,0) is pure
    /// backspin (Magnus +Z); a −Z component pushes the ball toward −X, the
    /// right-hander's arm side. Breaking balls flip both: topspin plus a
    /// glove-side (+X) push. A left-hander mirrors X.
    /// </summary>
    public static Vec3 SpinVector(PitchKind kind, byte movementRating, Hand throws)
    {
        double omega = ArcadeTuning.BaseSpinRpm(kind) * ArcadeTuning.SpinEfficiency(kind)
            * ArcadeTuning.SpinScale(movementRating) * ArcadeTuning.RpmToRadPerSec;
        double tilt = ArcadeTuning.SpinTiltDeg(kind) * ArcadeTuning.DegToRad;
        double c = Math.Cos(tilt);
        double s = Math.Sin(tilt);
        Vec3 axis = kind == PitchKind.Breaking
            ? new Vec3(c, 0.0, s)    // topspin + glove-side sweep
            : new Vec3(-c, 0.0, -s); // backspin + arm-side run
        Vec3 spin = axis * omega;
        // Mirroring X for a left-hander flips the horizontal break while keeping the vertical.
        return throws == Hand.Right ? spin : new Vec3(spin.X, spin.Y, -spin.Z);
    }

    /// <summary>
    /// Solves for the release velocity (magnitude fixed at
    /// <paramref name="speedMps"/>) whose flight passes through
    /// <paramref name="target"/> at the plane Y = target.Y, using Newton's
    /// method on yaw and pitch angles with a finite-difference Jacobian.
    /// Deterministic: fixed iteration count bound and fixed perturbations.
    /// </summary>
    public static Vec3 SolveReleaseVelocity(in Vec3 release, double speedMps, in Vec3 spin, in Vec3 target)
    {
        // Initial guess: point at the target, raised to cancel gravity drop over the flight time.
        Vec3 delta = target - release;
        double flightTime = delta.Length / speedMps;
        double yaw = Math.Atan2(delta.X, -delta.Y);
        double horiz = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        double pitch = Math.Atan2(delta.Z + 0.5 * ArcadeTuning.Gravity * flightTime * flightTime, horiz);

        const double h = 1e-4;
        for (int iter = 0; iter < 12; iter++)
        {
            Miss(release, speedMps, spin, target, yaw, pitch, out double ex, out double ez);
            if (Math.Abs(ex) < 1e-5 && Math.Abs(ez) < 1e-5)
            {
                break;
            }
            Miss(release, speedMps, spin, target, yaw + h, pitch, out double exY, out double ezY);
            Miss(release, speedMps, spin, target, yaw, pitch + h, out double exP, out double ezP);
            double j11 = (exY - ex) / h, j12 = (exP - ex) / h;
            double j21 = (ezY - ez) / h, j22 = (ezP - ez) / h;
            double det = j11 * j22 - j12 * j21;
            if (Math.Abs(det) < 1e-12)
            {
                break;
            }
            yaw -= (j22 * ex - j12 * ez) / det;
            pitch -= (-j21 * ex + j11 * ez) / det;
        }
        return Direction(yaw, pitch) * speedMps;
    }

    private static Vec3 Direction(double yaw, double pitch)
    {
        double cp = Math.Cos(pitch);
        return new Vec3(Math.Sin(yaw) * cp, -Math.Cos(yaw) * cp, Math.Sin(pitch));
    }

    private static void Miss(in Vec3 release, double speed, in Vec3 spin, in Vec3 target, double yaw, double pitch, out double ex, out double ez)
    {
        var start = new BallState(release, Direction(yaw, pitch) * speed, spin, 0.0);
        BallPhysics.FlyToPlane(start, target.Y, Vec3.Zero, groundStops: false, out BallState atPlane);
        ex = atPlane.Position.X - target.X;
        ez = atPlane.Position.Z - target.Z;
    }

    /// <summary>One standard-normal draw (Box–Muller, two uniforms) — deterministic from the stream.</summary>
    public static double NextGaussian(ref RngState rng)
    {
        double u1 = 1.0 - rng.NextDouble(); // (0, 1]
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>
    /// Builds a pitch: speed and spin from the arsenal + tier, control
    /// scatter applied to the intended target (two Gaussian draws), then the
    /// release velocity solved against the scattered target.
    /// <paramref name="aftertouchInput"/> is a stick vector in [−1, 1]² (X =
    /// horizontal, Y = vertical), scaled by the movement budget; pass zero
    /// for none. Aftertouch is applied after solving, so a steered pitch
    /// misses its solved target by exactly the steer — that's the skill.
    /// </summary>
    public static PitchRelease Throw(
        ref RngState rng, in PitcherProfile pitcher, ArcadeTier tier, PitchKind kind,
        in Vec3 intendedTarget, double aftertouchX, double aftertouchZ)
    {
        double mph = ArcadeTuning.PitchSpeedMph(tier, kind, pitcher.Velocity(kind)) * ArcadeTuning.FatigueSpeedScale(pitcher.FatigueMultiplier);
        double speed = mph * ArcadeTuning.MphToMps;
        byte movement = pitcher.Movement(kind);
        Vec3 spin = SpinVector(kind, movement, pitcher.Throws);
        Vec3 release = ArcadeTuning.ReleasePoint(pitcher.Throws);

        double sigma = ArcadeTuning.ScatterSigma(pitcher.Control, pitcher.FatigueMultiplier);
        double dx = NextGaussian(ref rng) * sigma;
        double dz = NextGaussian(ref rng) * sigma;
        var actual = new Vec3(intendedTarget.X + dx, ArcadeTuning.PlateFrontY, intendedTarget.Z + dz);

        Vec3 velocity = SolveReleaseVelocity(release, speed, spin, actual);
        double budget = ArcadeTuning.AftertouchMaxAccel(movement);
        var aftertouch = new Vec3(
            Math.Clamp(aftertouchX, -1.0, 1.0) * budget,
            0.0,
            Math.Clamp(aftertouchZ, -1.0, 1.0) * budget);

        return new PitchRelease(kind, mph, intendedTarget, actual, new BallState(release, velocity, spin, 0.0), aftertouch);
    }

    /// <summary>True when the ball's center crosses the plate front inside the zone (the ball's radius counts, as in the rulebook).</summary>
    public static bool IsStrike(in Vec3 atPlate) =>
        Math.Abs(atPlate.X) <= ArcadeTuning.PlateHalfWidth + ArcadeTuning.BallRadius
        && atPlate.Z >= ArcadeTuning.ZoneBottom - ArcadeTuning.BallRadius
        && atPlate.Z <= ArcadeTuning.ZoneTop + ArcadeTuning.BallRadius;
}
