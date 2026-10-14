using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Simulation.Arcade;

/// <summary>One thrown pitch, flown to the contact plane and the plate.</summary>
public readonly struct PitchFlight
{
    public readonly PitchRelease Release;
    /// <summary>The ball as it crosses the contact plane (where a swing meets it).</summary>
    public readonly BallState AtContactPlane;
    /// <summary>The ball as it crosses the front of the plate (where the umpire judges it).</summary>
    public readonly BallState AtPlate;
    /// <summary>True if the pitch hit the dirt before reaching the hitter: it can't be put in play.</summary>
    public readonly bool Bounced;
    public readonly bool IsStrike;

    public PitchFlight(in PitchRelease release, in BallState atContactPlane, in BallState atPlate, bool bounced, bool isStrike)
    {
        Release = release;
        AtContactPlane = atContactPlane;
        AtPlate = atPlate;
        Bounced = bounced;
        IsStrike = isStrike;
    }

    /// <summary>Seconds from release until the ball reaches the contact plane: the moment the swing must meet.</summary>
    public double ArrivalTime => AtContactPlane.Time;
}

public enum PitchCall : byte
{
    Ball,
    CalledStrike,
    SwingingStrike,
    FoulTip,
    Foul,
    InPlay,
}

/// <summary>The full result of one pitch with the batter's response.</summary>
public readonly struct PitchOutcome
{
    public readonly PitchCall Call;
    public readonly ContactResult Contact;
    /// <summary>Valid when <see cref="Contact"/> is a batted ball (fair or foul).</summary>
    public readonly BattedBallResult BattedBall;

    public PitchOutcome(PitchCall call, in ContactResult contact, in BattedBallResult battedBall)
    {
        Call = call;
        Contact = contact;
        BattedBall = battedBall;
    }
}

/// <summary>
/// The G-0 driver for one pitch (arcade_baseball_overhaul.md §12, G-0):
/// a simple AI picks and throws a pitch; the caller animates it from
/// <see cref="PitchPath"/>, collects the human's swing, and resolves it;
/// the batted ball's path lands in <see cref="HitPath"/> for the field
/// view. Every outcome comes from the solvers; the paths are only for
/// drawing. Buffers are allocated once, so a pitch allocates nothing.
///
/// Not the game driver — counts, outs, fielders and the rules engine are
/// G-2. This class exists so the feel prototype has one seam to call.
/// </summary>
public sealed class ArcadeAtBat
{
    /// <summary>Path samples are taken every N fixed steps (2 → 120 Hz, plenty for a 60 fps scene to interpolate).</summary>
    public const int SampleEvery = 2;

    private readonly BallState[] _pitchPath = new BallState[256];
    private readonly BallState[] _hitPath = new BallState[2048];
    private int _pitchCount;
    private int _hitCount;

    public ReadOnlySpan<BallState> PitchPath => _pitchPath.AsSpan(0, _pitchCount);
    public ReadOnlySpan<BallState> HitPath => _hitPath.AsSpan(0, _hitCount);

    /// <summary>
    /// PROPOSAL (G-0 only; real pitch-calling AI is G-3): picks a pitch
    /// family from the usage mix and a target — half the time somewhere in
    /// the zone, half the time on the edges or chasing out of it.
    /// </summary>
    public static void ChoosePitch(ref RngState rng, in PitcherProfile pitcher, out PitchKind kind, out Vec3 target)
    {
        double fb = pitcher.FastballUsage, br = pitcher.BreakingUsage, os = pitcher.OffspeedUsage;
        double total = Math.Max(fb + br + os, 1.0);
        double r = rng.NextDouble() * total;
        kind = r < fb ? PitchKind.Fastball : r < fb + br ? PitchKind.Breaking : PitchKind.Offspeed;

        bool inZone = rng.NextDouble() < ArcadeTuning.ProtoInZoneShare;
        double u = rng.NextDouble();
        double v = rng.NextDouble();
        double x, z;
        if (inZone)
        {
            double m = ArcadeTuning.ProtoZoneMargin;
            x = (u * 2.0 - 1.0) * (ArcadeTuning.PlateHalfWidth - m);
            z = ArcadeTuning.ZoneBottom + m + v * (ArcadeTuning.ZoneTop - ArcadeTuning.ZoneBottom - 2.0 * m);
        }
        else
        {
            x = (u * 2.0 - 1.0) * ArcadeTuning.ProtoChaseHalfWidth;
            z = ArcadeTuning.ProtoChaseBottom + v * (ArcadeTuning.ProtoChaseTop - ArcadeTuning.ProtoChaseBottom);
        }
        target = new Vec3(x, ArcadeTuning.PlateFrontY, z);
    }

    /// <summary>AI-chosen pitch, thrown and flown.</summary>
    public PitchFlight ThrowPitch(ref RngState rng, in PitcherProfile pitcher, ArcadeTier tier)
    {
        ChoosePitch(ref rng, pitcher, out PitchKind kind, out Vec3 target);
        return ThrowPitch(ref rng, pitcher, tier, kind, target, 0.0, 0.0);
    }

    /// <summary>A specific pitch (human pitching later, harness checks now), thrown and flown.</summary>
    public PitchFlight ThrowPitch(
        ref RngState rng, in PitcherProfile pitcher, ArcadeTier tier, PitchKind kind, in Vec3 target,
        double aftertouchX, double aftertouchZ)
    {
        PitchRelease release = PitchModel.Throw(ref rng, pitcher, tier, kind, target, aftertouchX, aftertouchZ);
        bool reached = BallPhysics.FlyToPlane(release.Launch, ArcadeTuning.ContactPlaneY, release.Aftertouch, out BallState atContact);
        BallState atPlate = atContact;
        if (reached)
        {
            Vec3 steer = atContact.Time < ArcadeTuning.AftertouchWindowSec ? release.Aftertouch : Vec3.Zero;
            reached = BallPhysics.FlyToPlane(atContact, ArcadeTuning.PlateFrontY, steer, out atPlate);
        }
        bool strike = reached && PitchModel.IsStrike(atPlate.Position);
        _pitchCount = BallPhysics.Sample(release.Launch, release.Aftertouch, ArcadeTuning.PlateFrontY - 1.0, SampleEvery, _pitchPath);
        return new PitchFlight(release, atContact, atPlate, !reached, strike);
    }

    /// <summary>Resolves the batter's response to a flown pitch and, on contact, flies the batted ball.</summary>
    public PitchOutcome Swing(in PitchFlight pitch, in BatterProfile batter, in SwingInput swing, in AssistScales assists, in ParkDimensions park)
    {
        _hitCount = 0;
        if (!swing.Swung)
        {
            var take = new ContactResult(ContactKind.Take, 0, 0, 0, 0, 0, 0, 0, default);
            return new PitchOutcome(pitch.IsStrike ? PitchCall.CalledStrike : PitchCall.Ball, take, default);
        }

        ContactResult contact = pitch.Bounced
            ? new ContactResult(ContactKind.Whiff, 0, 0, 0, 0, 0, 0, 0, default)
            : ContactModel.Resolve(pitch.AtContactPlane, batter, swing, assists);

        switch (contact.Kind)
        {
            case ContactKind.Whiff:
                return new PitchOutcome(PitchCall.SwingingStrike, contact, default);
            case ContactKind.FoulTip:
                return new PitchOutcome(PitchCall.FoulTip, contact, default);
        }

        BattedBallResult flight = BattedBallModel.Fly(contact.Launch, park);
        _hitCount = BallPhysics.Sample(contact.Launch, Vec3.Zero, double.NegativeInfinity, SampleEvery, _hitPath);
        return new PitchOutcome(flight.IsFair ? PitchCall.InPlay : PitchCall.Foul, contact, flight);
    }
}
