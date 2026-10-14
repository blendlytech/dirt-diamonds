namespace DirtAndDiamonds.Simulation.Arcade;

/// <summary>
/// A ballpark's outfield geometry (arcade §3.3, §10.4): fence distances at
/// the five standard points (LF line, LCF, CF, RCF, RF line; feet) and a
/// single wall height. In G-7 these come from the team's entry in
/// Assets/Data/Venues/venues.json; G-0 uses <see cref="Default"/>.
/// </summary>
public readonly struct ParkDimensions
{
    public readonly double LeftLineFt, LeftCenterFt, CenterFt, RightCenterFt, RightLineFt;
    public readonly double WallHeightFt;

    public ParkDimensions(double leftLineFt, double leftCenterFt, double centerFt, double rightCenterFt, double rightLineFt, double wallHeightFt)
    {
        LeftLineFt = leftLineFt;
        LeftCenterFt = leftCenterFt;
        CenterFt = centerFt;
        RightCenterFt = rightCenterFt;
        RightLineFt = rightLineFt;
        WallHeightFt = wallHeightFt;
    }

    /// <summary>A symmetric, neutral park: 330 / 375 / 400 / 375 / 330 ft, 10 ft wall.</summary>
    public static ParkDimensions Default => new(330, 375, 400, 375, 330, 10);

    public double WallHeightM => WallHeightFt / ArcadeTuning.MetresToFeet;

    /// <summary>Fence distance (m) at a spray angle (deg, + toward right field), linearly interpolated between the five points.</summary>
    public double FenceDistanceM(double sprayDeg)
    {
        double a = Math.Clamp(sprayDeg, -ArcadeTuning.FoulLineDeg, ArcadeTuning.FoulLineDeg);
        double ft;
        if (a < -22.5)
        {
            ft = LeftLineFt + (LeftCenterFt - LeftLineFt) * ((a + 45.0) / 22.5);
        }
        else if (a < 0.0)
        {
            ft = LeftCenterFt + (CenterFt - LeftCenterFt) * ((a + 22.5) / 22.5);
        }
        else if (a < 22.5)
        {
            ft = CenterFt + (RightCenterFt - CenterFt) * (a / 22.5);
        }
        else
        {
            ft = RightCenterFt + (RightLineFt - RightCenterFt) * ((a - 22.5) / 22.5);
        }
        return ft / ArcadeTuning.MetresToFeet;
    }
}

/// <summary>How a batted ball is described in the G-0 readout (no fielders yet).</summary>
public enum BattedBallKind : byte
{
    Foul,
    PopUp,
    GroundBall,
    LineDrive,
    FlyBall,
    /// <summary>Reached the fence below the top of the wall.</summary>
    OffTheWall,
    HomeRun,
}

/// <summary>The flight of one batted ball to its first landing (or the fence).</summary>
public readonly struct BattedBallResult
{
    public readonly BattedBallKind Kind;
    public readonly bool IsFair;
    public readonly double ExitVelocityMph;
    public readonly double LaunchAngleDeg;
    public readonly double SprayDeg;
    /// <summary>Ground distance from home to the first landing (ft). For a home run: where it would have landed with no stands.</summary>
    public readonly double DistanceFt;
    /// <summary>Peak height (ft).</summary>
    public readonly double ApexFt;
    /// <summary>Time from contact to first landing, or to the fence (s).</summary>
    public readonly double HangTimeSec;
    public readonly Vec3 Landing;

    public BattedBallResult(
        BattedBallKind kind, bool isFair, double exitVelocityMph, double launchAngleDeg, double sprayDeg,
        double distanceFt, double apexFt, double hangTimeSec, in Vec3 landing)
    {
        Kind = kind;
        IsFair = isFair;
        ExitVelocityMph = exitVelocityMph;
        LaunchAngleDeg = launchAngleDeg;
        SprayDeg = sprayDeg;
        DistanceFt = distanceFt;
        ApexFt = apexFt;
        HangTimeSec = hangTimeSec;
        Landing = landing;
    }
}

/// <summary>
/// Batted-ball flight (arcade §3.3): integrates from the bat to the first
/// landing, checking the fence on the way. Fair/foul uses the angle where
/// the ball reaches the fence (the foul-pole rule) or, short of the fence,
/// where it first lands (a G-0 simplification; the G-2 rules engine adds the
/// infield "where it stops / passes the bag" rule).
/// </summary>
public static class BattedBallModel
{
    public static BattedBallResult Fly(in BallState launch, in ParkDimensions park)
    {
        BallState s = launch;
        double apex = s.Position.Z;
        double wall = park.WallHeightM;
        bool crossedFence = false;
        bool homeRun = false;
        bool offWall = false;
        double fenceTime = 0.0;
        Vec3 fencePoint = Vec3.Zero;
        const double dt = ArcadeTuning.FixedStep;

        for (int i = 0; i < 8192; i++)
        {
            BallState next = BallPhysics.Step(s, dt);
            if (next.Position.Z > apex)
            {
                apex = next.Position.Z;
            }

            if (!crossedFence && next.Position.Y > 0.0)
            {
                double sprayNow = Math.Atan2(next.Position.X, next.Position.Y) / ArcadeTuning.DegToRad;
                if (Math.Abs(sprayNow) <= ArcadeTuning.FoulLineDeg
                    && next.Position.GroundDistance >= park.FenceDistanceM(sprayNow))
                {
                    crossedFence = true;
                    fenceTime = next.Time;
                    fencePoint = next.Position;
                    if (next.Position.Z > wall)
                    {
                        homeRun = true; // keep flying to report the "would have landed" distance
                    }
                    else
                    {
                        offWall = true;
                        s = next;
                        break;
                    }
                }
            }

            if (next.Position.Z <= ArcadeTuning.BallRadius)
            {
                double denom = s.Position.Z - next.Position.Z;
                double t = denom > 0.0 ? (s.Position.Z - ArcadeTuning.BallRadius) / denom : 1.0;
                s = BallState.Lerp(s, next, Math.Clamp(t, 0.0, 1.0));
                break;
            }
            s = next;
        }

        Vec3 land = offWall ? fencePoint : s.Position;
        // A ball that reaches the fence is judged where it crosses it (the foul
        // pole rule): a fair drive that slices past the line after clearing the
        // wall is still a home run. Anything else is judged where it lands.
        Vec3 judge = homeRun || offWall ? fencePoint : land;
        double judgeSpray = Math.Atan2(judge.X, Math.Max(judge.Y, 1e-9)) / ArcadeTuning.DegToRad;
        bool fair = judge.Y > 0.0 && Math.Abs(judgeSpray) <= ArcadeTuning.FoulLineDeg;

        double evMph = launch.Speed / ArcadeTuning.MphToMps;
        double la = Math.Asin(Math.Clamp(launch.Velocity.Z / Math.Max(launch.Speed, 1e-9), -1.0, 1.0)) / ArcadeTuning.DegToRad;
        double spray = Math.Atan2(launch.Velocity.X, launch.Velocity.Y) / ArcadeTuning.DegToRad;

        BattedBallKind kind;
        if (!fair)
        {
            kind = BattedBallKind.Foul;
        }
        else if (homeRun)
        {
            kind = BattedBallKind.HomeRun;
        }
        else if (offWall)
        {
            kind = BattedBallKind.OffTheWall;
        }
        else if (la < 10.0)
        {
            kind = BattedBallKind.GroundBall;
        }
        else if (la < 25.0)
        {
            kind = BattedBallKind.LineDrive;
        }
        else if (la < 50.0)
        {
            kind = BattedBallKind.FlyBall;
        }
        else
        {
            kind = BattedBallKind.PopUp;
        }

        double hang = (offWall ? fenceTime : s.Time) - launch.Time;
        return new BattedBallResult(
            kind, fair, evMph, la, spray,
            land.GroundDistance * ArcadeTuning.MetresToFeet,
            apex * ArcadeTuning.MetresToFeet,
            hang, land);
    }
}
