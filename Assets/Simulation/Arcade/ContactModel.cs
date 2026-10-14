namespace DirtAndDiamonds.Simulation.Arcade;

/// <summary>A batter's arcade-relevant ratings (arcade §4.1). Filled from EffectiveRatings in G-4; set directly in G-0.</summary>
public readonly struct BatterProfile
{
    public readonly byte Power;
    public readonly byte Contact;
    public readonly byte Discipline;
    public readonly bool PedActive;
    public readonly Hand Bats;

    public BatterProfile(byte power, byte contact, byte discipline, bool pedActive, Hand bats)
    {
        Power = power;
        Contact = contact;
        Discipline = discipline;
        PedActive = pedActive;
        Bats = bats;
    }

    public static BatterProfile LeagueAverage(Hand bats = Hand.Right) => new(50, 50, 50, false, bats);
}

/// <summary>
/// Human-only assists (arcade §5.6): difficulty presets scale these and
/// nothing else, so difficulty can never move calibration. AI batters use
/// <see cref="None"/>.
/// </summary>
public readonly struct AssistScales
{
    public readonly double TimingWindow;
    public readonly double CursorSize;

    public AssistScales(double timingWindow, double cursorSize)
    {
        TimingWindow = timingWindow;
        CursorSize = cursorSize;
    }

    public static AssistScales None => new(1.0, 1.0);
}

/// <summary>
/// One swing as the player (or AI) made it. Times are seconds since the
/// pitch's release. The cursor is the point on the contact plane (X, Z in
/// field metres) where the batter aims the bat's sweet spot.
/// </summary>
public readonly struct SwingInput
{
    public readonly bool Swung;
    public readonly double SwingStartTime;
    public readonly double CursorX;
    public readonly double CursorZ;
    public readonly bool PowerSwing;

    public SwingInput(bool swung, double swingStartTime, double cursorX, double cursorZ, bool powerSwing)
    {
        Swung = swung;
        SwingStartTime = swingStartTime;
        CursorX = cursorX;
        CursorZ = cursorZ;
        PowerSwing = powerSwing;
    }

    public static SwingInput Take => new(false, 0.0, 0.0, 0.0, false);
}

public enum ContactKind : byte
{
    /// <summary>No swing.</summary>
    Take,
    /// <summary>Swung and missed: outside the timing window or the cursor.</summary>
    Whiff,
    /// <summary>Barely nicked: a foul tip straight back.</summary>
    FoulTip,
    /// <summary>The ball came off the bat (fair or foul is decided by its flight).</summary>
    BattedBall,
}

/// <summary>What happened at the contact plane, plus the ball's launch state if it was hit.</summary>
public readonly struct ContactResult
{
    public readonly ContactKind Kind;
    /// <summary>Contact time minus ball arrival (s); negative = early.</summary>
    public readonly double TimingError;
    /// <summary>Timing error as a fraction of the timing half-window (−1..1 inside it).</summary>
    public readonly double TimingFraction;
    /// <summary>Normalized distance from the sweet spot (0 = dead center, 1 = cursor edge).</summary>
    public readonly double Offset;
    public readonly double CollisionEfficiency;
    public readonly double ExitVelocityMps;
    public readonly double LaunchAngleDeg;
    /// <summary>Horizontal spray from the center-field line (deg); + toward right field.</summary>
    public readonly double SprayDeg;
    public readonly BallState Launch;

    public ContactResult(
        ContactKind kind, double timingError, double timingFraction, double offset, double collisionEfficiency,
        double exitVelocityMps, double launchAngleDeg, double sprayDeg, in BallState launch)
    {
        Kind = kind;
        TimingError = timingError;
        TimingFraction = timingFraction;
        Offset = offset;
        CollisionEfficiency = collisionEfficiency;
        ExitVelocityMps = exitVelocityMps;
        LaunchAngleDeg = launchAngleDeg;
        SprayDeg = sprayDeg;
        Launch = launch;
    }

    public double ExitVelocityMph => ExitVelocityMps / ArcadeTuning.MphToMps;
}

/// <summary>
/// Bat-ball contact (arcade_baseball_overhaul.md §3.2), solved at the exact
/// moment the pitch crosses the contact plane:
/// <list type="bullet">
/// <item>timing error → spray (early pulls, late goes the other way; the outer quarter of the window fouls off);</item>
/// <item>vertical offset of ball vs. sweet spot → launch angle (under the ball lifts it, over it beats it into the ground);</item>
/// <item>distance from the sweet spot (and timing) → collision efficiency q, and
/// exit velocity EV = q·v_pitch + (1 + q)·v_bat, with bat speed from Power.</item>
/// </list>
/// Pure and deterministic — no random draw at all: the batter's hands are the noise.
/// </summary>
public static class ContactModel
{
    public static ContactResult Resolve(in BallState atContactPlane, in BatterProfile batter, in SwingInput swing, in AssistScales assists)
    {
        if (!swing.Swung)
        {
            return new ContactResult(ContactKind.Take, 0, 0, 0, 0, 0, 0, 0, default);
        }

        double window = ArcadeTuning.TimingWindowSec(batter.Contact, swing.PowerSwing) * assists.TimingWindow;
        double contactTime = swing.SwingStartTime + ArcadeTuning.SwingDurationSec(swing.PowerSwing);
        double timingError = contactTime - atContactPlane.Time;
        double tf = timingError / window;

        double halfW = ArcadeTuning.CursorHalfWidth(batter.Contact, swing.PowerSwing) * assists.CursorSize;
        double halfH = ArcadeTuning.CursorHalfHeight(batter.Contact, swing.PowerSwing) * assists.CursorSize;
        Vec3 ball = atContactPlane.Position;
        double nx = (ball.X - swing.CursorX) / halfW;
        // Positive nz: the bat's center is above the ball's — it strikes the top half (grounder).
        double nz = (swing.CursorZ - ball.Z) / halfH;
        double offset = Math.Sqrt(nx * nx + nz * nz);

        if (Math.Abs(tf) > 1.0 || offset > 1.0)
        {
            return new ContactResult(ContactKind.Whiff, timingError, tf, offset, 0, 0, 0, 0, default);
        }
        if (offset > ArcadeTuning.FoulTipOffset)
        {
            return new ContactResult(ContactKind.FoulTip, timingError, tf, offset, 0, 0, 0, 0, default);
        }

        double tf2 = tf * tf;
        double q = ArcadeTuning.CollisionEfficiencyMax
            * (1.0 - ArcadeTuning.CollisionOffsetFalloff * offset * offset)
            * (1.0 - ArcadeTuning.CollisionTimingFalloff * tf2);
        double batSpeed = ArcadeTuning.BatSpeedMps(batter.Power, batter.PedActive, swing.PowerSwing)
            * (1.0 - ArcadeTuning.BatSpeedTimingFalloff * tf2);
        double ev = q * atContactPlane.Speed + (1.0 + q) * batSpeed;

        double launch = ArcadeTuning.LaunchAngleCenterDeg - ArcadeTuning.LaunchAngleSpanDeg * nz;

        // Early (tf < 0) pulls: a right-handed hitter pulls to left field (−X, negative spray).
        double side = batter.Bats == Hand.Right ? 1.0 : -1.0;
        double spray = side * tf * ArcadeTuning.SprayAtWindowEdgeDeg
            // An outside pitch is +X to a right-hander and −X to a left-hander, so this term needs no hand sign.
            + ArcadeTuning.SprayPerPlateWidthDeg * (ball.X / ArcadeTuning.PlateHalfWidth);

        BallState launchState = LaunchState(ball, ev, launch, spray);
        return new ContactResult(ContactKind.BattedBall, timingError, tf, offset, q, ev, launch, spray, launchState);
    }

    /// <summary>
    /// The batted ball's initial state from exit velocity, launch angle and
    /// spray, with backspin (lift) or topspin from the launch angle and
    /// sidespin that slices balls toward the nearer foul line.
    /// </summary>
    public static BallState LaunchState(in Vec3 contactPoint, double exitVelocityMps, double launchAngleDeg, double sprayDeg)
    {
        double la = launchAngleDeg * ArcadeTuning.DegToRad;
        double sp = sprayDeg * ArcadeTuning.DegToRad;
        double cl = Math.Cos(la);
        var velocity = new Vec3(exitVelocityMps * cl * Math.Sin(sp), exitVelocityMps * cl * Math.Cos(sp), exitVelocityMps * Math.Sin(la));

        // h = horizontal travel direction. Backspin axis h × Z gives Magnus +Z.
        var h = new Vec3(Math.Sin(sp), Math.Cos(sp), 0.0);
        Vec3 backAxis = Vec3.Cross(h, Vec3.UnitZ);
        double back = ArcadeTuning.BattedBackspinRpm(launchAngleDeg) * ArcadeTuning.RpmToRadPerSec;
        // Spin about −Z pushes the ball to the right of travel; a ball sprayed right (+) slices further right.
        double side = ArcadeTuning.BattedSidespinRpmAtLine * Math.Clamp(sprayDeg / ArcadeTuning.FoulLineDeg, -1.5, 1.5) * ArcadeTuning.RpmToRadPerSec;
        Vec3 spin = backAxis * back + new Vec3(0.0, 0.0, -side);

        return new BallState(contactPoint, velocity, spin, 0.0);
    }
}
