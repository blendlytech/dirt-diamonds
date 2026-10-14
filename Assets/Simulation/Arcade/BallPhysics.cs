namespace DirtAndDiamonds.Simulation.Arcade;

/// <summary>One ball's kinematic state: position (m), velocity (m/s), spin vector (rad/s), and time since the flight began (s).</summary>
public readonly struct BallState
{
    public readonly Vec3 Position;
    public readonly Vec3 Velocity;
    public readonly Vec3 Spin;
    public readonly double Time;

    public BallState(in Vec3 position, in Vec3 velocity, in Vec3 spin, double time)
    {
        Position = position;
        Velocity = velocity;
        Spin = spin;
        Time = time;
    }

    public double Speed => Velocity.Length;

    /// <summary>Linear interpolation between two states one step apart (spin carried from <paramref name="a"/>; it is constant over a flight).</summary>
    public static BallState Lerp(in BallState a, in BallState b, double t) => new(
        Vec3.Lerp(a.Position, b.Position, t),
        Vec3.Lerp(a.Velocity, b.Velocity, t),
        a.Spin,
        a.Time + (b.Time - a.Time) * t);
}

/// <summary>
/// The arcade core's flight integrator (arcade_baseball_overhaul.md §3.1,
/// §3.3): gravity, quadratic drag and Magnus lift, advanced by classic RK4
/// at a fixed step. Pure and allocation-free — every method works on value
/// types and caller-owned spans, so a skipped game re-runs bit-identically.
///
/// Spin is held constant over a flight (first pass; real spin decays a few
/// percent per second — a G-3 refinement if the bands need it).
/// </summary>
public static class BallPhysics
{
    private const double DragK = 0.5 * ArcadeTuning.AirDensity * ArcadeTuning.DragCoefficient * ArcadeTuning.BallArea / ArcadeTuning.BallMass;
    private const double LiftK = 0.5 * ArcadeTuning.AirDensity * ArcadeTuning.BallArea / ArcadeTuning.BallMass;

    /// <summary>
    /// Sawicki lift curve on the spin parameter S = rω/v, clamped
    /// (coefficients in <see cref="ArcadeTuning"/>): C_L = 1.5·S below
    /// S = 0.1, else 0.09 + 0.6·S, at most 0.40.
    /// </summary>
    public static double LiftCoefficient(double spinParameter)
    {
        double cl = spinParameter < ArcadeTuning.LiftKneeS
            ? ArcadeTuning.LiftLowSlope * spinParameter
            : ArcadeTuning.LiftHighIntercept + ArcadeTuning.LiftHighSlope * spinParameter;
        return Math.Min(cl, ArcadeTuning.MaxLiftCoefficient);
    }

    /// <summary>Total acceleration on the ball: gravity + drag + Magnus + an optional external steer (pitch aftertouch).</summary>
    public static Vec3 Acceleration(in Vec3 velocity, in Vec3 spin, in Vec3 extraAccel)
    {
        double speed = velocity.Length;
        Vec3 accel = new(extraAccel.X, extraAccel.Y, extraAccel.Z - ArcadeTuning.Gravity);
        if (speed <= 1e-9)
        {
            return accel;
        }

        // Drag opposes velocity: a = -k·|v|·v.
        accel -= velocity * (DragK * speed);

        // Magnus lift along (ω × v), magnitude ½ρC_L·A·v²/m.
        double omega = spin.Length;
        if (omega > 1e-9)
        {
            Vec3 cross = Vec3.Cross(spin, velocity);
            double crossLen = cross.Length;
            if (crossLen > 1e-12)
            {
                double s = ArcadeTuning.BallRadius * omega / speed;
                double lift = LiftK * LiftCoefficient(s) * speed * speed;
                accel += cross * (lift / crossLen);
            }
        }
        return accel;
    }

    /// <summary>One RK4 step of <paramref name="dt"/> seconds.</summary>
    public static BallState Step(in BallState s, double dt, in Vec3 extraAccel)
    {
        Vec3 p = s.Position;
        Vec3 v = s.Velocity;
        Vec3 w = s.Spin;

        Vec3 a1 = Acceleration(v, w, extraAccel);
        Vec3 v1 = v;

        Vec3 v2 = v + a1 * (0.5 * dt);
        Vec3 a2 = Acceleration(v2, w, extraAccel);

        Vec3 v3 = v + a2 * (0.5 * dt);
        Vec3 a3 = Acceleration(v3, w, extraAccel);

        Vec3 v4 = v + a3 * dt;
        Vec3 a4 = Acceleration(v4, w, extraAccel);

        Vec3 newP = p + (v1 + 2.0 * v2 + 2.0 * v3 + v4) * (dt / 6.0);
        Vec3 newV = v + (a1 + 2.0 * a2 + 2.0 * a3 + a4) * (dt / 6.0);
        return new BallState(newP, newV, w, s.Time + dt);
    }

    /// <summary>One step with no external steer.</summary>
    public static BallState Step(in BallState s, double dt) => Step(s, dt, Vec3.Zero);

    /// <summary>
    /// Flies a pitch toward the plate until it crosses the plane
    /// Y = <paramref name="planeY"/> (the ball travels toward −Y), solving the
    /// crossing inside the step by interpolation. Returns false if the ball
    /// hits the ground first (a bounced pitch), comparing the two events'
    /// times when both fall inside the same step; <paramref name="atPlane"/>
    /// is then the state where it touched down.
    /// </summary>
    /// <param name="aftertouch">Steer applied while the flight time is under <see cref="ArcadeTuning.AftertouchWindowSec"/>.</param>
    public static bool FlyToPlane(in BallState start, double planeY, in Vec3 aftertouch, out BallState atPlane) =>
        FlyToPlane(start, planeY, aftertouch, groundStops: true, out atPlane);

    /// <summary>
    /// As above; with <paramref name="groundStops"/> false the ball passes
    /// through the ground. The aim solver uses that, so a trial throw that
    /// would bounce still yields a smooth miss distance for Newton's method.
    /// </summary>
    public static bool FlyToPlane(in BallState start, double planeY, in Vec3 aftertouch, bool groundStops, out BallState atPlane)
    {
        BallState s = start;
        const double dt = ArcadeTuning.FixedStep;
        for (int i = 0; i < 4096; i++)
        {
            Vec3 steer = s.Time < ArcadeTuning.AftertouchWindowSec ? aftertouch : Vec3.Zero;
            BallState next = Step(s, dt, steer);
            bool groundHit = groundStops && next.Position.Z <= ArcadeTuning.BallRadius;
            bool planeHit = next.Position.Y <= planeY;
            if (groundHit || planeHit)
            {
                // Both events can fall inside one step: whichever happens first
                // within the step decides (G-0 review finding 3). A tie goes to
                // the plane: the ball reached it before touching down.
                double tGround = groundHit
                    ? CrossingFraction(s.Position.Z - ArcadeTuning.BallRadius, s.Position.Z - next.Position.Z)
                    : double.PositiveInfinity;
                double tPlane = planeHit
                    ? CrossingFraction(s.Position.Y - planeY, s.Position.Y - next.Position.Y)
                    : double.PositiveInfinity;
                bool reached = tPlane <= tGround;
                atPlane = BallState.Lerp(s, next, reached ? tPlane : tGround);
                return reached;
            }
            s = next;
        }
        atPlane = s;
        return false;
    }

    /// <summary>Where inside a step a linear crossing happens: <paramref name="before"/> is the distance still to go at the step's start, <paramref name="span"/> the distance covered in the step. Clamped to [0, 1].</summary>
    private static double CrossingFraction(double before, double span) =>
        span > 0.0 ? Math.Clamp(before / span, 0.0, 1.0) : 1.0;

    /// <summary>
    /// Samples a flight into <paramref name="buffer"/> every
    /// <paramref name="sampleEvery"/> fixed steps (1 = every 1/240 s), until
    /// the ball reaches the ground, crosses <paramref name="stopAtPlaneY"/>
    /// moving toward −Y (use <see cref="double.NegativeInfinity"/> to ignore),
    /// or the buffer is full. The first entry is the start state. Returns the
    /// number of samples written. For presentation: the scene animates these
    /// points; outcomes always come from the solvers, never from the samples.
    /// </summary>
    public static int Sample(in BallState start, in Vec3 aftertouch, double stopAtPlaneY, int sampleEvery, Span<BallState> buffer)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }
        int every = Math.Max(1, sampleEvery);
        BallState s = start;
        buffer[0] = s;
        int count = 1;
        int step = 0;
        const double dt = ArcadeTuning.FixedStep;
        while (count < buffer.Length && step < 8192)
        {
            Vec3 steer = s.Time < ArcadeTuning.AftertouchWindowSec ? aftertouch : Vec3.Zero;
            s = Step(s, dt, steer);
            step++;
            bool done = s.Position.Z <= ArcadeTuning.BallRadius || s.Position.Y <= stopAtPlaneY;
            if (done || step % every == 0)
            {
                buffer[count++] = s;
            }
            if (done)
            {
                break;
            }
        }
        return count;
    }
}
