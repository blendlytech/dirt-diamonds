using DirtAndDiamonds.Simulation.Arcade;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.ArcadeHarness;

/// <summary>
/// Headless checks for the arcade physics core (arcade_baseball_overhaul.md §9).
/// G-0 ships the prototype suites: physics sanity against MLB Statcast
/// shapes, the aim solver, the contact model's behavior, determinism and
/// zero allocation. G-2 adds rules suites; G-3 adds per-tier calibration.
/// Exit code 0 = every check passed.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;
    private static readonly List<string> Failed = new();

    private static int Main()
    {
        Console.WriteLine("ArcadeHarness — G-0 prototype suites");
        PitchSuite();
        AimSuite();
        CarrySuite();
        ContactSuite();
        DeterminismSuite();
        AllocationSuite();

        Console.WriteLine();
        Console.WriteLine($"TOTAL {_pass}/{_pass + _fail} passed");
        if (_fail > 0)
        {
            Console.WriteLine("FAILED:");
            foreach (string name in Failed)
            {
                Console.WriteLine($"  - {name}");
            }
            return 1;
        }
        return 0;
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (ok)
        {
            _pass++;
        }
        else
        {
            _fail++;
            Failed.Add(name);
        }
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
    }

    private static void Section(string title) => Console.WriteLine($"\n== {title}");

    // ------------------------------------------------------------------
    // 1. Pitch flight against Statcast shapes
    // ------------------------------------------------------------------

    private static void PitchSuite()
    {
        Section("1. Pitch flight");
        Vec3 release = ArcadeTuning.ReleasePointRight;
        Vec3 middle = new(0.0, ArcadeTuning.PlateFrontY, 0.76);

        // 95 mph fastball: flight time and speed loss.
        double speed = 95.0 * ArcadeTuning.MphToMps;
        Vec3 fbSpin = PitchModel.SpinVector(PitchKind.Fastball, 50, Hand.Right);
        Vec3 v = PitchModel.SolveReleaseVelocity(release, speed, fbSpin, middle);
        BallPhysics.FlyToPlane(new BallState(release, v, fbSpin, 0), ArcadeTuning.PlateFrontY, Vec3.Zero, out BallState atPlate);
        Check("95 mph FB reaches the plate in 0.38–0.46 s", atPlate.Time is >= 0.38 and <= 0.46, $"{atPlate.Time:F3} s");
        double ratio = atPlate.Speed / speed;
        Check("FB keeps 88–95% of its speed to the plate", ratio is >= 0.88 and <= 0.95, $"{ratio:P1}");

        // Induced vertical break: same release velocity, with vs. without spin.
        BallPhysics.FlyToPlane(new BallState(release, v, Vec3.Zero, 0), ArcadeTuning.PlateFrontY, Vec3.Zero, out BallState noSpin);
        double ivbIn = (atPlate.Position.Z - noSpin.Position.Z) * 39.37;
        Check("FB induced vertical break 10–22 in", ivbIn is >= 10 and <= 22, $"{ivbIn:F1} in");
        double runIn = (atPlate.Position.X - noSpin.Position.X) * 39.37;
        Check("RHP FB runs to the arm side (−X) 3–14 in", runIn is <= -3 and >= -14, $"{runIn:F1} in");

        // Breaking ball: drops below no-spin and sweeps to the glove side (+X) for a RHP.
        double brSpeed = ArcadeTuning.PitchSpeedMph(ArcadeTier.Pro, PitchKind.Breaking, 50) * ArcadeTuning.MphToMps;
        Vec3 brSpin = PitchModel.SpinVector(PitchKind.Breaking, 50, Hand.Right);
        Vec3 bv = PitchModel.SolveReleaseVelocity(release, brSpeed, brSpin, middle);
        BallPhysics.FlyToPlane(new BallState(release, bv, brSpin, 0), ArcadeTuning.PlateFrontY, Vec3.Zero, out BallState brPlate);
        BallPhysics.FlyToPlane(new BallState(release, bv, Vec3.Zero, 0), ArcadeTuning.PlateFrontY, Vec3.Zero, out BallState brNoSpin);
        double dropIn = (brNoSpin.Position.Z - brPlate.Position.Z) * 39.37;
        double sweepIn = (brPlate.Position.X - brNoSpin.Position.X) * 39.37;
        Check("Breaking ball drops 3–16 in more than a spinless ball", dropIn is >= 3 and <= 16, $"{dropIn:F1} in");
        Check("RHP breaking ball sweeps to the glove side (+X) 3–16 in", sweepIn is >= 3 and <= 16, $"{sweepIn:F1} in");

        // Left-hander mirrors the horizontal break.
        Vec3 lhSpin = PitchModel.SpinVector(PitchKind.Fastball, 50, Hand.Left);
        Vec3 lhRelease = ArcadeTuning.ReleasePoint(Hand.Left);
        Vec3 lv = PitchModel.SolveReleaseVelocity(lhRelease, speed, lhSpin, middle);
        BallPhysics.FlyToPlane(new BallState(lhRelease, lv, lhSpin, 0), ArcadeTuning.PlateFrontY, Vec3.Zero, out BallState lhPlate);
        BallPhysics.FlyToPlane(new BallState(lhRelease, lv, Vec3.Zero, 0), ArcadeTuning.PlateFrontY, Vec3.Zero, out BallState lhNo);
        double lhRun = (lhPlate.Position.X - lhNo.Position.X) * 39.37;
        Check("LHP FB runs the other way (+X)", lhRun > 3, $"{lhRun:F1} in");

        // Tier ranges and the velocity rating.
        double hs = ArcadeTuning.PitchSpeedMph(ArcadeTier.HighSchool, PitchKind.Fastball, 50);
        double col = ArcadeTuning.PitchSpeedMph(ArcadeTier.College, PitchKind.Fastball, 50);
        double pro = ArcadeTuning.PitchSpeedMph(ArcadeTier.Pro, PitchKind.Fastball, 50);
        Check("Average fastball climbs HS < College < Pro", hs < col && col < pro, $"{hs:F1} / {col:F1} / {pro:F1} mph");
        Check("Pro velocity rating 100 > 50 > 0",
            ArcadeTuning.PitchSpeedMph(ArcadeTier.Pro, PitchKind.Fastball, 100) > pro && pro > ArcadeTuning.PitchSpeedMph(ArcadeTier.Pro, PitchKind.Fastball, 0), "monotonic");

        // Control scatter shrinks with control and grows with fatigue.
        Check("Scatter: control 90 < control 30", ArcadeTuning.ScatterSigma(90, 1.0) < ArcadeTuning.ScatterSigma(30, 1.0), "monotonic");
        Check("Scatter: tired > fresh", ArcadeTuning.ScatterSigma(50, 0.6) > ArcadeTuning.ScatterSigma(50, 1.0), "monotonic");
    }

    // ------------------------------------------------------------------
    // 2. Aim solver
    // ------------------------------------------------------------------

    private static void AimSuite()
    {
        Section("2. Aim solver");
        var rng = new RngState(0xA1A1UL);
        double worst = 0.0;
        int n = 0;
        foreach (PitchKind kind in new[] { PitchKind.Fastball, PitchKind.Breaking, PitchKind.Offspeed })
        {
            foreach (Hand hand in new[] { Hand.Right, Hand.Left })
            {
                Vec3 release = ArcadeTuning.ReleasePoint(hand);
                Vec3 spin = PitchModel.SpinVector(kind, 80, hand);
                double speed = ArcadeTuning.PitchSpeedMph(ArcadeTier.Pro, kind, 70) * ArcadeTuning.MphToMps;
                for (int i = 0; i < 25; i++)
                {
                    var target = new Vec3((rng.NextDouble() * 2 - 1) * 0.35, ArcadeTuning.PlateFrontY, 0.35 + rng.NextDouble() * 0.9);
                    Vec3 v = PitchModel.SolveReleaseVelocity(release, speed, spin, target);
                    BallPhysics.FlyToPlane(new BallState(release, v, spin, 0), target.Y, Vec3.Zero, out BallState at);
                    double err = Math.Sqrt(Math.Pow(at.Position.X - target.X, 2) + Math.Pow(at.Position.Z - target.Z, 2));
                    worst = Math.Max(worst, err);
                    n++;
                }
            }
        }
        Check($"Zero-scatter pitches land within 1 mm of target ({n} pitches)", worst < 0.001, $"worst {worst * 1000:F3} mm");

        // Aftertouch moves the pitch, within budget, and only in its direction.
        var p = PitcherProfile.LeagueAverage();
        var rngA = new RngState(7);
        var rngB = new RngState(7);
        var bat = new ArcadeAtBat();
        Vec3 mid = new(0, ArcadeTuning.PlateFrontY, 0.76);
        PitchFlight straight = bat.ThrowPitch(ref rngA, p, ArcadeTier.Pro, PitchKind.Fastball, mid, 0, 0);
        PitchFlight steered = bat.ThrowPitch(ref rngB, p, ArcadeTier.Pro, PitchKind.Fastball, mid, 1.0, 0);
        double moved = (steered.AtPlate.Position.X - straight.AtPlate.Position.X) * 39.37;
        Check("Full right aftertouch moves the pitch +X by 0.5–6 in", moved is >= 0.5 and <= 6, $"{moved:F2} in");
    }

    // ------------------------------------------------------------------
    // 3. Batted-ball carry against Statcast shapes
    // ------------------------------------------------------------------

    private static BattedBallResult Hit(double evMph, double laDeg, double sprayDeg = 0.0)
    {
        BallState launch = ContactModel.LaunchState(new Vec3(0, ArcadeTuning.ContactPlaneY, 0.9), evMph * ArcadeTuning.MphToMps, laDeg, sprayDeg);
        return BattedBallModel.Fly(launch, ParkDimensions.Default);
    }

    private static void CarrySuite()
    {
        Section("3. Batted-ball carry");
        var barrel = Hit(100, 28);
        Check("100 mph @ 28° carries 370–440 ft", barrel.DistanceFt is >= 370 and <= 440, $"{barrel.DistanceFt:F0} ft, {barrel.HangTimeSec:F1} s, {barrel.Kind}");
        var bomb = Hit(110, 25);
        Check("110 mph @ 25° carries 420–490 ft and is a HR", bomb.DistanceFt is >= 420 and <= 490 && bomb.Kind == BattedBallKind.HomeRun, $"{bomb.DistanceFt:F0} ft, {bomb.Kind}");
        var liner = Hit(92, 12);
        Check("92 mph @ 12° liner lands 170–280 ft", liner.DistanceFt is >= 170 and <= 280 && liner.Kind == BattedBallKind.LineDrive, $"{liner.DistanceFt:F0} ft, {liner.Kind}");
        var grounder = Hit(95, -6);
        Check("95 mph @ −6° grounder bounces within 40 ft", grounder.DistanceFt < 40 && grounder.Kind == BattedBallKind.GroundBall, $"{grounder.DistanceFt:F0} ft, {grounder.Kind}");
        var popup = Hit(80, 65);
        Check("80 mph @ 65° pop-up: < 200 ft, hangs > 4 s", popup.DistanceFt < 200 && popup.HangTimeSec > 4 && popup.Kind == BattedBallKind.PopUp, $"{popup.DistanceFt:F0} ft, {popup.HangTimeSec:F1} s");
        var cansOfCorn = Hit(88, 30);
        Check("88 mph @ 30° stays in the park", cansOfCorn.Kind == BattedBallKind.FlyBall, $"{cansOfCorn.DistanceFt:F0} ft, {cansOfCorn.Kind}");
        var foul = Hit(95, 20, 55);
        Check("A ball sprayed at 55° is foul", !foul.IsFair && foul.Kind == BattedBallKind.Foul, $"{foul.Kind}");
        var shortPark = new ParkDimensions(310, 360, 395, 360, 310, 8);
        var deepPark = new ParkDimensions(345, 390, 420, 390, 345, 12);
        BallState down = ContactModel.LaunchState(new Vec3(0, ArcadeTuning.ContactPlaneY, 0.9), 89 * ArcadeTuning.MphToMps, 27, -40);
        var inShort = BattedBallModel.Fly(down, shortPark);
        var inDeep = BattedBallModel.Fly(down, deepPark);
        Check("The same drive down the line: HR in a short park, not in a deep one",
            inShort.Kind == BattedBallKind.HomeRun && inDeep.Kind != BattedBallKind.HomeRun, $"{inShort.Kind} vs {inDeep.Kind} ({inShort.DistanceFt:F0} ft)");
        // Foul-pole rule (G-0a review): a drive that clears the fence fair near the pole and
        // then slices past the line is a home run, judged at the fence, not at the landing point.
        var pole = Hit(100, 25, 41.25);
        Check("Foul pole: clears fair at the fence, lands past the line → still a HR",
            pole.Kind == BattedBallKind.HomeRun && pole.IsFair && Math.Abs(Math.Atan2(pole.Landing.X, pole.Landing.Y) / ArcadeTuning.DegToRad) > ArcadeTuning.FoulLineDeg,
            $"{pole.Kind}, lands at {Math.Atan2(pole.Landing.X, pole.Landing.Y) / ArcadeTuning.DegToRad:F1}°");
        // Independent sweep: walk each ball's sampled path, find where it first reaches the
        // fence radius, and judge fair/foul there by angle. The model's call must agree.
        var path = new BallState[4096];
        var park = ParkDimensions.Default;
        int poleMisjudged = 0, poleCases = 0;
        for (double sp = 38.0; sp <= 45.0; sp += 0.25)
        {
            for (double la = 20.0; la <= 35.0; la += 1.0)
            {
                BallState launch = ContactModel.LaunchState(new Vec3(0, ArcadeTuning.ContactPlaneY, 0.9), 104 * ArcadeTuning.MphToMps, la, sp);
                var b = BattedBallModel.Fly(launch, park);
                int n = BallPhysics.Sample(launch, Vec3.Zero, double.NegativeInfinity, 1, path);
                for (int k = 1; k < n; k++)
                {
                    Vec3 q = path[k].Position;
                    double deg = Math.Atan2(q.X, q.Y) / ArcadeTuning.DegToRad;
                    if (q.GroundDistance >= park.FenceDistanceM(deg))
                    {
                        if (Math.Abs(deg) <= ArcadeTuning.FoulLineDeg && q.Z > park.WallHeightM)
                        {
                            poleCases++;
                            if (b.Kind != BattedBallKind.HomeRun) poleMisjudged++;
                        }
                        break;
                    }
                }
            }
        }
        Check("Foul pole sweep (104 mph, spray 38–45°, LA 20–35°): every ball over the fence fair is a HR",
            poleMisjudged == 0 && poleCases > 0, $"{poleCases} cleared fair, {poleMisjudged} misjudged");
        Check("More exit velocity → more carry at 28°", Hit(95, 28).DistanceFt < Hit(100, 28).DistanceFt && Hit(100, 28).DistanceFt < Hit(105, 28).DistanceFt, "monotonic");
    }

    // ------------------------------------------------------------------
    // 4. Contact model behavior
    // ------------------------------------------------------------------

    private static BallState MiddlePitchAtContact(double mph)
    {
        Vec3 release = ArcadeTuning.ReleasePointRight;
        Vec3 spin = PitchModel.SpinVector(PitchKind.Fastball, 50, Hand.Right);
        Vec3 target = new(0, ArcadeTuning.ContactPlaneY, 0.80);
        Vec3 v = PitchModel.SolveReleaseVelocity(release, mph * ArcadeTuning.MphToMps, spin, target);
        BallPhysics.FlyToPlane(new BallState(release, v, spin, 0), ArcadeTuning.ContactPlaneY, Vec3.Zero, out BallState at);
        return at;
    }

    private static SwingInput Perfect(in BallState at, double timingOffset = 0.0, double dx = 0.0, double dz = 0.0, bool power = false) =>
        new(true, at.Time - ArcadeTuning.SwingDurationSec(power) + timingOffset, at.Position.X + dx, at.Position.Z + dz, power);

    private static void ContactSuite()
    {
        Section("4. Contact model");
        BallState at = MiddlePitchAtContact(92);
        var avg = BatterProfile.LeagueAverage();
        var none = AssistScales.None;

        ContactResult center = ContactModel.Resolve(at, avg, Perfect(at), none);
        Check("Centered, on-time swing (power 50 vs 92 mph) → 95–108 mph", center.ExitVelocityMph is >= 95 and <= 108, $"{center.ExitVelocityMph:F1} mph @ {center.LaunchAngleDeg:F0}°");
        Check("Centered contact launches near the center angle (8–16°)", center.LaunchAngleDeg is >= 8 and <= 16, $"{center.LaunchAngleDeg:F1}°");

        double halfH = ArcadeTuning.CursorHalfHeight(50, false);
        ContactResult offCenter = ContactModel.Resolve(at, avg, Perfect(at, 0, 0, 0.7 * halfH), none);
        Check("Off-center (70% of cursor) loses ≥ 8% exit velocity", offCenter.ExitVelocityMps <= center.ExitVelocityMps * 0.92, $"{offCenter.ExitVelocityMph:F1} mph");
        Check("Bat over the ball → grounder angle (< 0°)", offCenter.LaunchAngleDeg < 0, $"{offCenter.LaunchAngleDeg:F1}°");
        ContactResult under = ContactModel.Resolve(at, avg, Perfect(at, 0, 0, -0.4 * halfH), none);
        Check("Bat under the ball → lift (> 25°)", under.LaunchAngleDeg > 25, $"{under.LaunchAngleDeg:F1}°");

        double ev20 = ContactModel.Resolve(at, new BatterProfile(20, 50, 50, false, Hand.Right), Perfect(at), none).ExitVelocityMph;
        double ev80 = ContactModel.Resolve(at, new BatterProfile(80, 50, 50, false, Hand.Right), Perfect(at), none).ExitVelocityMph;
        Check("Power is monotonic: 20 < 50 < 80", ev20 < center.ExitVelocityMph && center.ExitVelocityMph < ev80, $"{ev20:F1} / {center.ExitVelocityMph:F1} / {ev80:F1} mph");
        double evPed = ContactModel.Resolve(at, new BatterProfile(50, 50, 50, true, Hand.Right), Perfect(at), none).ExitVelocityMph;
        Check("PED power hook raises exit velocity", evPed > center.ExitVelocityMph, $"{evPed:F1} mph");

        ContactResult power = ContactModel.Resolve(at, avg, Perfect(at, 0, 0, 0, true), none);
        Check("Power swing: more exit velocity", power.ExitVelocityMps > center.ExitVelocityMps, $"{power.ExitVelocityMph:F1} mph");
        Check("Power swing: smaller cursor", ArcadeTuning.CursorHalfWidth(50, true) < ArcadeTuning.CursorHalfWidth(50, false), "monotonic");

        double window = ArcadeTuning.TimingWindowSec(50, false);
        ContactResult early = ContactModel.Resolve(at, avg, Perfect(at, -0.5 * window), none);
        ContactResult late = ContactModel.Resolve(at, avg, Perfect(at, +0.5 * window), none);
        Check("RHB early → pulled to left field (spray < −10°)", early.SprayDeg < -10, $"{early.SprayDeg:F1}°");
        Check("RHB late → opposite field (spray > +10°)", late.SprayDeg > 10, $"{late.SprayDeg:F1}°");
        ContactResult lhbEarly = ContactModel.Resolve(at, BatterProfile.LeagueAverage(Hand.Left), Perfect(at, -0.5 * window), none);
        Check("LHB early → pulled to right field (spray > +10°)", lhbEarly.SprayDeg > 10, $"{lhbEarly.SprayDeg:F1}°");

        Check("Outside the timing window → whiff", ContactModel.Resolve(at, avg, Perfect(at, 1.05 * window), none).Kind == ContactKind.Whiff, "late");
        Check("Too early (outside the window) → whiff", ContactModel.Resolve(at, avg, Perfect(at, -1.05 * window), none).Kind == ContactKind.Whiff, "early");
        Check("Outside the cursor → whiff", ContactModel.Resolve(at, avg, Perfect(at, 0, 1.05 * ArcadeTuning.CursorHalfWidth(50, false)), none).Kind == ContactKind.Whiff, "wide");
        Check("Cursor edge → foul tip", ContactModel.Resolve(at, avg, Perfect(at, 0, 0.96 * ArcadeTuning.CursorHalfWidth(50, false)), none).Kind == ContactKind.FoulTip, "nick");
        Check("No swing → take", ContactModel.Resolve(at, avg, SwingInput.Take, none).Kind == ContactKind.Take, "take");

        Check("Contact widens the timing window", ArcadeTuning.TimingWindowSec(80, false) > ArcadeTuning.TimingWindowSec(20, false), "monotonic");
        Check("Contact widens the cursor", ArcadeTuning.CursorHalfHeight(80, false) > ArcadeTuning.CursorHalfHeight(20, false), "monotonic");
        Check("Discipline reads the pitch sooner", ArcadeTuning.ReadCueDelaySec(80) < ArcadeTuning.ReadCueDelaySec(20), "monotonic");

        var easy = new AssistScales(1.5, 1.3);
        ContactResult assisted = ContactModel.Resolve(at, avg, Perfect(at, 1.2 * window), easy);
        Check("Assist scales widen only the human window (a 1.2× late swing connects)", assisted.Kind != ContactKind.Whiff, $"{assisted.Kind}");

        // A full pitch through the driver.
        var rng = new RngState(99);
        var atBat = new ArcadeAtBat();
        PitchFlight pf = atBat.ThrowPitch(ref rng, PitcherProfile.LeagueAverage(), ArcadeTier.Pro, PitchKind.Fastball, new Vec3(0, ArcadeTuning.PlateFrontY, 0.8), 0, 0);
        PitchOutcome o = atBat.Swing(pf, avg, Perfect(pf.AtContactPlane), none, ParkDimensions.Default);
        Check("Driver: a perfect swing puts the ball in play with a path to draw",
            o.Contact.Kind == ContactKind.BattedBall && atBat.HitPath.Length > 10 && atBat.PitchPath.Length > 10,
            $"{o.Call}, {o.BattedBall.Kind}, {o.BattedBall.DistanceFt:F0} ft, path {atBat.HitPath.Length} pts");
        PitchOutcome takeO = atBat.Swing(pf, avg, SwingInput.Take, none, ParkDimensions.Default);
        Check("Driver: a take is called by the zone", takeO.Call == (pf.IsStrike ? PitchCall.CalledStrike : PitchCall.Ball), $"{takeO.Call}");
    }

    // ------------------------------------------------------------------
    // 5. Determinism
    // ------------------------------------------------------------------

    private static ulong RunSession(ulong seed, int pitches)
    {
        var rng = new RngState(seed);
        var swingRng = new RngState(seed ^ 0x5EEDUL); // stands in for recorded human inputs
        var atBat = new ArcadeAtBat();
        var batter = new BatterProfile(60, 55, 50, false, Hand.Right);
        var pitcher = new PitcherProfile(70, 60, 55, 60, 70, 30, 55, 60, 15, 55, 0.9, Hand.Right);
        ulong h = 1469598103934665603UL;
        for (int i = 0; i < pitches; i++)
        {
            PitchFlight pf = atBat.ThrowPitch(ref rng, pitcher, ArcadeTier.Pro);
            double jitter = (swingRng.NextDouble() - 0.5) * 0.08;
            double aimX = pf.AtContactPlane.Position.X + (swingRng.NextDouble() - 0.5) * 0.15;
            double aimZ = pf.AtContactPlane.Position.Z + (swingRng.NextDouble() - 0.5) * 0.12;
            bool swing = swingRng.NextDouble() < 0.6;
            var input = new SwingInput(swing, pf.ArrivalTime - ArcadeTuning.SwingDurationSec(false) + jitter, aimX, aimZ, false);
            PitchOutcome o = atBat.Swing(pf, batter, input, AssistScales.None, ParkDimensions.Default);
            h = Mix(h, (ulong)o.Call);
            h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(pf.AtPlate.Position.X));
            h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(pf.AtPlate.Position.Z));
            h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(o.BattedBall.DistanceFt));
            h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(o.Contact.ExitVelocityMps));
        }
        return h;
    }

    private static ulong Mix(ulong h, ulong v) => (h ^ v) * 1099511628211UL;

    private static void DeterminismSuite()
    {
        Section("5. Determinism");
        ulong a = RunSession(42, 2000);
        ulong b = RunSession(42, 2000);
        ulong c = RunSession(43, 2000);
        Check("Same seed + same inputs → bit-identical 2,000-pitch session", a == b, $"{a:X16}");
        Check("A different seed → a different session", a != c, $"{c:X16}");

        // A sanity glimpse of what the prototype feels like (informational, loose bounds).
        var rng = new RngState(2026);
        var swingRng = new RngState(2027);
        var atBat = new ArcadeAtBat();
        var batter = BatterProfile.LeagueAverage();
        var pitcher = PitcherProfile.LeagueAverage();
        int swings = 0, whiffs = 0, inPlay = 0, hr = 0;
        double evSum = 0;
        for (int i = 0; i < 5000; i++)
        {
            PitchFlight pf = atBat.ThrowPitch(ref rng, pitcher, ArcadeTier.Pro);
            if (!pf.IsStrike)
            {
                continue;
            }
            // A "decent human": timing σ ≈ 25 ms, aim σ ≈ 4 cm.
            double jitter = PitchModel.NextGaussian(ref swingRng) * 0.025;
            double aimX = pf.AtContactPlane.Position.X + PitchModel.NextGaussian(ref swingRng) * 0.04;
            double aimZ = pf.AtContactPlane.Position.Z + PitchModel.NextGaussian(ref swingRng) * 0.04;
            var input = new SwingInput(true, pf.ArrivalTime - ArcadeTuning.SwingDurationSec(false) + jitter, aimX, aimZ, false);
            PitchOutcome o = atBat.Swing(pf, batter, input, AssistScales.None, ParkDimensions.Default);
            swings++;
            if (o.Call == PitchCall.SwingingStrike) whiffs++;
            if (o.Call == PitchCall.InPlay) { inPlay++; evSum += o.Contact.ExitVelocityMph; }
            if (o.Call == PitchCall.InPlay && o.BattedBall.Kind == BattedBallKind.HomeRun) hr++;
        }
        double whiffRate = (double)whiffs / swings;
        double avgEv = evSum / Math.Max(inPlay, 1);
        Console.WriteLine($"  [INFO] 'decent human' vs avg Pro pitcher on strikes: whiff {whiffRate:P0}, in play {(double)inPlay / swings:P0}, avg EV {avgEv:F1} mph, HR/in-play {(double)hr / Math.Max(inPlay, 1):P1}");
        Check("Feel glimpse is plausible (whiff 5–60%, avg EV 75–100 mph)", whiffRate is >= 0.05 and <= 0.60 && avgEv is >= 75 and <= 100, "loose G-0 bounds; G-3 calibrates for real");
    }

    // ------------------------------------------------------------------
    // 6. Zero allocation
    // ------------------------------------------------------------------

    private static void AllocationSuite()
    {
        Section("6. Zero allocation");
        RunSession(1, 50); // warm up / JIT
        var rng = new RngState(5);
        var atBat = new ArcadeAtBat();
        var batter = BatterProfile.LeagueAverage();
        var pitcher = PitcherProfile.LeagueAverage();
        // Warm the exact path.
        PitchFlight warm = atBat.ThrowPitch(ref rng, pitcher, ArcadeTier.Pro);
        atBat.Swing(warm, batter, new SwingInput(true, warm.ArrivalTime - 0.15, warm.AtContactPlane.Position.X, warm.AtContactPlane.Position.Z, false), AssistScales.None, ParkDimensions.Default);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++)
        {
            PitchFlight pf = atBat.ThrowPitch(ref rng, pitcher, ArcadeTier.Pro);
            var input = new SwingInput(true, pf.ArrivalTime - 0.15, pf.AtContactPlane.Position.X, pf.AtContactPlane.Position.Z, false);
            atBat.Swing(pf, batter, input, AssistScales.None, ParkDimensions.Default);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("500 pitches + swings + flights allocate 0 bytes", allocated == 0, $"{allocated} bytes");
    }
}
