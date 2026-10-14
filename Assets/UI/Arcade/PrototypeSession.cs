using DirtAndDiamonds.Simulation.Arcade;
// Only RngState is needed from Baseball. A plain `using` of the whole namespace
// would make PitchCall ambiguous: the micro sim declares its own PitchCall.
using RngState = DirtAndDiamonds.Simulation.Baseball.RngState;

namespace DirtAndDiamonds.UI.Arcade;

public enum PrototypePhase : byte
{
    /// <summary>The pitch is already thrown into the buffers; the pitcher is winding up. The batter may move the cursor.</summary>
    Windup,
    /// <summary>The ball is in the air toward the plate. One swing allowed, until the ball reaches the contact plane.</summary>
    Flight,
    /// <summary>A batted ball (fair or foul) is flying.</summary>
    Hit,
    /// <summary>The play is over; the readout holds, then the next pitch starts.</summary>
    Result,
}

/// <summary>One frame of collected input. The Godot layer fills it; the session never touches Godot.</summary>
public readonly struct PrototypeInput
{
    /// <summary>Contact-cursor stick, -1..1. +X is the first-base side (field +X).</summary>
    public readonly double StickX;
    /// <summary>Contact-cursor stick, -1..1. +Z is up.</summary>
    public readonly double StickZ;
    public readonly bool PowerHeld;
    /// <summary>Edge: true only on the frame the swing button went down.</summary>
    public readonly bool SwingPressed;

    public PrototypeInput(double stickX, double stickZ, bool powerHeld, bool swingPressed)
    {
        StickX = stickX;
        StickZ = stickZ;
        PowerHeld = powerHeld;
        SwingPressed = swingPressed;
    }
}

/// <summary>
/// The session tally the brief asks for. There are no fielders in G-0, so
/// "In play" counts fair batted balls, not hits; hits arrive with G-2.
/// </summary>
public struct PrototypeTally
{
    public int Pitches;
    public int Swings;
    public int Whiffs;
    public int Fouls;
    public int InPlay;
    public int HomeRuns;
    public int ExitVelocityCount;
    public double ExitVelocitySumMph;

    public readonly double AverageExitVelocityMph => ExitVelocityCount > 0 ? ExitVelocitySumMph / ExitVelocityCount : 0.0;
}

/// <summary>Everything about the last pitch the readout shows. Only valid once <see cref="HasPitch"/>.</summary>
public struct PrototypeReadout
{
    public bool HasPitch;
    public PitchKind Pitch;
    public double PitchMph;
    public bool InZone;
    /// <summary>Hit the dirt before the contact plane: unhittable.</summary>
    public bool Bounced;
    /// <summary>Hit the dirt before the plate (may still have been hittable).</summary>
    public bool InDirt;
    public PitchCall Call;
    public bool Swung;
    public bool PowerSwing;
    public ContactKind ContactKind;

    /// <summary>Contact time minus ball arrival, in ms. Negative = early. Valid when <see cref="Swung"/>.</summary>
    public double TimingMs;
    /// <summary>Timing error as a fraction of the half-window (|x| &gt; 1 = outside it).</summary>
    public double TimingFraction;
    /// <summary>0 = dead center of the cursor, 1 = its edge (&gt; 1 = missed).</summary>
    public double Offset;

    public bool HasBatted;
    public BattedBallKind Kind;
    public double ExitVelocityMph;
    public double LaunchAngleDeg;
    public double SprayDeg;
    public double DistanceFt;
    public double ApexFt;
    public double HangTimeSec;
}

/// <summary>
/// The G-0b prototype driver: owns one <see cref="ArcadeAtBat"/>, the
/// caller-owned <see cref="RngState"/> it needs, the pitch cycle (wind-up →
/// flight → optional batted ball → result), the contact cursor, the tally and
/// the readout. Pure C#, no Godot types: the scene collects input into a
/// <see cref="PrototypeInput"/>, calls <see cref="Tick"/> once per frame with
/// a monotonic clock, and renders the public state. That is the shape G-5's
/// ArcadeGame hand-off will keep ("Godot only renders snapshots and collects
/// input", GEMINI.md §11).
///
/// Boundaries, all deliberate:
/// <list type="bullet">
/// <item>It never edits Assets/Simulation/Arcade. The only sim seam is
/// <see cref="ArcadeAtBat"/> (ChoosePitch, ThrowPitch, Swing) plus
/// ArcadeTuning's public cursor/swing/read-cue helpers.</item>
/// <item>The RngState here is the prototype's own stream, seeded from the
/// scene. G-0 has no sim, so the caller IS the scene; in G-5 the game driver
/// owns the stream and the UI never sees it. Cosmetic randomness (shake) lives
/// in the renderer with its own RNG.</item>
/// <item>Outcomes come only from the solvers. A press resolves the swing at
/// once (the pitch is already determined), but its consequences (tally,
/// readout, the cut to the batted ball) are held until the ball reaches the
/// contact plane, so nothing is spoiled early.</item>
/// <item>Clock: <see cref="Tick"/> takes absolute seconds, so a swing's start
/// time is measured against the pitch's release on the same clock the ball is
/// drawn with, not summed from frame deltas.</item>
/// </list>
///
/// Not implemented, by the brief: check swings, fielders, runners, counts, AI
/// batters, the human pitching.
/// </summary>
public sealed class PrototypeSession
{
    // ---- Timeline (PROPOSAL: presentation pacing, not physics) ----

    public const double WindupSec = 0.75;
    public const double ResultHoldSec = 1.6;
    /// <summary>A swing press during the result hold skips ahead, but only after this long, so a late swing can't skip its own readout.</summary>
    public const double ResultSkipAfterSec = 0.35;
    /// <summary>Hit-stop on solid contact (spec §10.5: "a few frames"). Zero under reduce-motion.</summary>
    public const double HitStopSec = 0.05;
    public const double HitStopMinMph = 70.0;
    public const double ShakeSec = 0.45;
    public const double ContactFlashSec = 0.09;

    // ---- Cursor box at the contact plane (field metres; covers the chase area) ----

    public const double CursorMinX = -0.60;
    public const double CursorMaxX = 0.60;
    public const double CursorMinZ = 0.15;
    public const double CursorMaxZ = 1.45;

    private readonly PrototypeSettings _settings;
    private readonly ArcadeAtBat _atBat = new();
    private RngState _rng;
    private ulong _seed;

    private PitchFlight _flight;
    private PitchOutcome _outcome;
    private PrototypeTally _tally;
    private PrototypeReadout _readout;
    private int _readoutVersion;

    private PrototypePhase _phase = PrototypePhase.Windup;
    private double _lastNow;
    private bool _hasLastNow;
    private double _phaseStart;
    private double _releaseWall;
    private double _hitStartWall;
    private double _hitEndSec;
    private double _resultStart;
    private double _swingWall;
    private double _shakeUntil;
    private bool _swung;
    private bool _swingPower;
    private bool _outcomeApplied;
    private bool _resultWasHit;
    private int _pitchIdx;
    private int _hitIdx;

    public PrototypeSession(PrototypeSettings settings, ulong seed)
    {
        _settings = settings;
        Reseed(seed);
    }

    // ------------------------------------------------------------------
    // State the renderer reads (all plain fields/properties: no allocation)
    // ------------------------------------------------------------------

    public PrototypeSettings Settings => _settings;

    /// <summary>The prototype's one park: the neutral 330/375/400/375/330, 10 ft default (G-7 brings the venue catalog).</summary>
    public ParkDimensions Park { get; } = ParkDimensions.Default;

    public ulong Seed => _seed;
    public double Now { get; private set; }
    public PrototypePhase Phase => _phase;
    public PitchKind CurrentPitch { get; private set; }

    /// <summary>The ball's drawn position in field metres (valid when <see cref="BallVisible"/>).</summary>
    public Vec3 BallPosition { get; private set; }
    public bool BallVisible { get; private set; }

    /// <summary>0..1 through the wind-up (1 afterwards).</summary>
    public double WindupProgress { get; private set; }

    /// <summary>Seconds since release (Flight only).</summary>
    public double FlightTime { get; private set; }

    /// <summary>True once the pitch has "read" for this batter's discipline: the spin-colored trail cue (arcade §5.1).</summary>
    public bool ReadCueOn { get; private set; }

    public double CursorX { get; private set; }
    public double CursorZ { get; private set; } = (ArcadeTuning.ZoneBottom + ArcadeTuning.ZoneTop) * 0.5;

    /// <summary>The swing is on its way: the cursor is frozen where it was when the button went down.</summary>
    public bool CursorLocked => _swung;

    public bool PowerSwingActive => _swung ? _swingPower : PowerHeld;
    public bool PowerHeld { get; private set; }

    /// <summary>Cursor ellipse half-extents at the contact plane, in metres (rating, power swing and the cursor-size assist applied).</summary>
    public double CursorHalfWidthM { get; private set; }
    public double CursorHalfHeightM { get; private set; }

    /// <summary>Bat progress: 0 = swing started, 1 = the bat reaches the contact plane, &gt; 1 follow-through; -1 = no swing this pitch.</summary>
    public double SwingProgress =>
        _swung ? (Now - _swingWall) / ArcadeTuning.SwingDurationSec(_swingPower) : -1.0;

    /// <summary>Seconds from release to the ball reaching the contact plane (the moment a swing must meet).</summary>
    public double ArrivalTime => _flight.ArrivalTime;

    /// <summary>True once the batted ball should be shown from the field camera (after the hit-stop; held through the result).</summary>
    public bool InHitView =>
        (_phase == PrototypePhase.Hit && Now >= _hitStartWall) || (_phase == PrototypePhase.Result && _resultWasHit);

    public bool HitStopActive => _phase == PrototypePhase.Hit && Now < _hitStartWall;
    public bool ShakeActive => !_settings.ReduceMotion && Now < _shakeUntil;

    /// <summary>The contact point on the plane, for the flash (valid while <see cref="ContactFlashActive"/>).</summary>
    public Vec3 ContactPoint { get; private set; }
    public double ContactFlashUntil { get; private set; }
    public bool ContactFlashActive => !_settings.ReduceMotion && Now < ContactFlashUntil;

    /// <summary>The first-landing marker for the field view (set when a batted ball is resolved).</summary>
    public bool HasLanding { get; private set; }
    public Vec3 LandingPosition { get; private set; }

    /// <summary>Wall time of the last release, for the release puff.</summary>
    public double ReleaseWall => _releaseWall;

    public PrototypeTally Tally => _tally;
    public PrototypeReadout Readout => _readout;

    /// <summary>Bumped whenever the tally or readout changes, so the HUD can format text only then.</summary>
    public int ReadoutVersion => _readoutVersion;

    // ------------------------------------------------------------------
    // Control
    // ------------------------------------------------------------------

    public void Reseed(ulong seed)
    {
        _seed = seed;
        _rng = new RngState(seed);
    }

    public void ResetTally()
    {
        _tally = default;
        _readout = default;
        _readoutVersion++;
    }

    /// <summary>Discards any pitch in progress and starts a fresh wind-up (used at start-up and after the tuning panel closes).</summary>
    public void Restart(double now)
    {
        _lastNow = now;
        _hasLastNow = true;
        Now = now;
        StartWindup(now);
    }

    public void Tick(double now, in PrototypeInput input)
    {
        double dt = _hasLastNow ? Math.Clamp(now - _lastNow, 0.0, 0.1) : 0.0;
        _lastNow = now;
        _hasLastNow = true;
        Now = now;

        UpdateCursor(dt, in input);

        switch (_phase)
        {
            case PrototypePhase.Windup:
                TickWindup(now);
                break;
            case PrototypePhase.Flight:
                TickFlight(now, in input);
                break;
            case PrototypePhase.Hit:
                TickHit(now);
                break;
            default:
                TickResult(now, in input);
                break;
        }
    }

    /// <summary>Where the pitch was at <paramref name="seconds"/> after release, for the read-cue trail.</summary>
    public Vec3 PitchPositionAt(double seconds)
    {
        int idx = _pitchIdx;
        return SamplePath(_atBat.PitchPath, ref idx, Math.Max(0.0, seconds));
    }

    // ------------------------------------------------------------------
    // Cursor
    // ------------------------------------------------------------------

    private void UpdateCursor(double dt, in PrototypeInput input)
    {
        PowerHeld = input.PowerHeld;
        if (!_swung)
        {
            CursorX = Math.Clamp(CursorX + input.StickX * _settings.CursorSpeedMps * dt, CursorMinX, CursorMaxX);
            CursorZ = Math.Clamp(CursorZ + input.StickZ * _settings.CursorSpeedMps * dt, CursorMinZ, CursorMaxZ);
        }

        AssistScales assists = _settings.BuildAssists();
        byte contact = (byte)Math.Clamp(_settings.BatterContact, 0, 100);
        bool power = PowerSwingActive;
        CursorHalfWidthM = ArcadeTuning.CursorHalfWidth(contact, power) * assists.CursorSize;
        CursorHalfHeightM = ArcadeTuning.CursorHalfHeight(contact, power) * assists.CursorSize;
    }

    // ------------------------------------------------------------------
    // Pitch cycle
    // ------------------------------------------------------------------

    private void StartWindup(double now)
    {
        _phase = PrototypePhase.Windup;
        _phaseStart = now;
        _swung = false;
        _swingPower = false;
        _outcomeApplied = false;
        _resultWasHit = false;
        _pitchIdx = 0;
        _hitIdx = 0;
        WindupProgress = 0.0;
        FlightTime = 0.0;
        ReadCueOn = false;
        BallVisible = false;
        HasLanding = false;
        _shakeUntil = 0.0;
        ContactFlashUntil = 0.0;

        // The pitch is thrown into ArcadeAtBat's buffers now and revealed at release.
        PitcherProfile pitcher = _settings.BuildPitcher();
        ArcadeAtBat.ChoosePitch(ref _rng, pitcher, out PitchKind kind, out Vec3 target);
        if (_settings.ForcedPitch >= 0)
        {
            kind = (PitchKind)_settings.ForcedPitch;
        }
        _flight = _atBat.ThrowPitch(ref _rng, pitcher, _settings.Tier, kind, target, 0.0, 0.0);
        CurrentPitch = kind;
    }

    private void TickWindup(double now)
    {
        WindupProgress = Math.Clamp((now - _phaseStart) / WindupSec, 0.0, 1.0);
        if (now - _phaseStart < WindupSec)
        {
            return;
        }

        _phase = PrototypePhase.Flight;
        _releaseWall = now;
        _tally.Pitches++;
        _readoutVersion++;
        BallVisible = true;
        BallPosition = _atBat.PitchPath[0].Position;
        WindupProgress = 1.0;
    }

    private void TickFlight(double now, in PrototypeInput input)
    {
        double t = now - _releaseWall;
        FlightTime = t;
        double arrival = _flight.ArrivalTime;
        ReadCueOn = t >= ArcadeTuning.ReadCueDelaySec((byte)Math.Clamp(_settings.BatterDiscipline, 0, 100));

        // One swing per pitch, only while the ball is still short of the contact plane. The press
        // time is "seconds since release when the player pressed", less the latency compensation.
        if (!_swung && t < arrival && input.SwingPressed)
        {
            var swing = new SwingInput(true, t - _settings.LatencyCompMs / 1000.0, CursorX, CursorZ, input.PowerHeld);
            _outcome = _atBat.Swing(_flight, _settings.BuildBatter(), swing, _settings.BuildAssists(), Park);
            _swung = true;
            _swingPower = input.PowerHeld;
            _swingWall = now;
        }

        // The ball reaches the contact plane: whatever happened is now allowed to show.
        if (t >= arrival && !_outcomeApplied)
        {
            if (!_swung)
            {
                _outcome = _atBat.Swing(_flight, _settings.BuildBatter(), SwingInput.Take, _settings.BuildAssists(), Park);
            }
            ApplyOutcome(now);
        }

        if (_phase != PrototypePhase.Flight)
        {
            return;
        }

        ReadOnlySpan<BallState> path = _atBat.PitchPath;
        double pathEnd = path[path.Length - 1].Time;
        BallPosition = SamplePath(path, ref _pitchIdx, Math.Min(t, pathEnd));
        if (t >= pathEnd)
        {
            EnterResult(now);
        }
    }

    private void TickHit(double now)
    {
        double th = Math.Max(0.0, now - _hitStartWall);
        if (th > 0.0)
        {
            BallPosition = SamplePath(_atBat.HitPath, ref _hitIdx, Math.Min(th, _hitEndSec));
        }
        if (th >= _hitEndSec)
        {
            EnterResult(now);
        }
    }

    private void TickResult(double now, in PrototypeInput input)
    {
        double elapsed = now - _resultStart;
        if (elapsed >= ResultHoldSec || (input.SwingPressed && elapsed >= ResultSkipAfterSec))
        {
            StartWindup(now);
        }
    }

    private void EnterResult(double now)
    {
        _resultWasHit = _phase == PrototypePhase.Hit;
        _phase = PrototypePhase.Result;
        _resultStart = now;
        BallVisible = _resultWasHit;
    }

    private void ApplyOutcome(double now)
    {
        _outcomeApplied = true;
        PitchOutcome outcome = _outcome;
        ContactResult contact = outcome.Contact;
        BattedBallResult batted = outcome.BattedBall;
        bool swung = contact.Kind != ContactKind.Take;

        if (swung)
        {
            _tally.Swings++;
        }
        switch (outcome.Call)
        {
            case PitchCall.SwingingStrike:
                _tally.Whiffs++;
                break;
            case PitchCall.Foul:
            case PitchCall.FoulTip:
                _tally.Fouls++;
                break;
            case PitchCall.InPlay:
                _tally.InPlay++;
                if (batted.Kind == BattedBallKind.HomeRun)
                {
                    _tally.HomeRuns++;
                }
                break;
        }
        if (contact.Kind == ContactKind.BattedBall)
        {
            _tally.ExitVelocityCount++;
            _tally.ExitVelocitySumMph += contact.ExitVelocityMph;
        }

        _readout = default;
        _readout.HasPitch = true;
        _readout.Pitch = _flight.Release.Kind;
        _readout.PitchMph = _flight.Release.SpeedMph;
        _readout.InZone = _flight.IsStrike;
        _readout.Bounced = _flight.Bounced;
        _readout.InDirt = _flight.BouncedBeforePlate;
        _readout.Call = outcome.Call;
        _readout.Swung = swung;
        _readout.PowerSwing = _swingPower;
        _readout.ContactKind = contact.Kind;
        if (swung)
        {
            _readout.TimingMs = contact.TimingError * 1000.0;
            _readout.TimingFraction = contact.TimingFraction;
            _readout.Offset = contact.Offset;
        }
        if (contact.Kind == ContactKind.BattedBall)
        {
            _readout.HasBatted = true;
            _readout.Kind = batted.Kind;
            _readout.ExitVelocityMph = contact.ExitVelocityMph;
            _readout.LaunchAngleDeg = contact.LaunchAngleDeg;
            _readout.SprayDeg = contact.SprayDeg;
            _readout.DistanceFt = batted.DistanceFt;
            _readout.ApexFt = batted.ApexFt;
            _readout.HangTimeSec = batted.HangTimeSec;
        }
        _readoutVersion++;

        if (contact.Kind == ContactKind.BattedBall || contact.Kind == ContactKind.FoulTip)
        {
            ContactPoint = _flight.AtContactPlane.Position;
            ContactFlashUntil = now + ContactFlashSec;
        }

        if (contact.Kind != ContactKind.BattedBall)
        {
            return;
        }

        // Cut to the batted ball. A solid hit freezes the pitch view for a few frames first.
        bool hitStop = !_settings.ReduceMotion && contact.ExitVelocityMph >= HitStopMinMph;
        _phase = PrototypePhase.Hit;
        _hitStartWall = now + (hitStop ? HitStopSec : 0.0);
        _hitIdx = 0;
        ReadOnlySpan<BallState> hitPath = _atBat.HitPath;
        double pathEnd = hitPath[hitPath.Length - 1].Time;
        // The sampler knows no fence: a ball that hit the wall stops at the wall, as the solver judged it.
        _hitEndSec = batted.Kind == BattedBallKind.OffTheWall ? Math.Min(pathEnd, batted.HangTimeSec) : pathEnd;
        BallPosition = contact.Launch.Position;
        BallVisible = true;
        HasLanding = true;
        LandingPosition = batted.Landing;
        if (batted.Kind == BattedBallKind.HomeRun)
        {
            _shakeUntil = _hitStartWall + ShakeSec;
        }
    }

    /// <summary>
    /// Position along a sampled flight at time <paramref name="t"/>. The index
    /// is carried between calls so the walk is O(1) per frame; it moves either
    /// way, so the trail can look backwards too.
    /// </summary>
    private static Vec3 SamplePath(ReadOnlySpan<BallState> path, ref int idx, double t)
    {
        int last = path.Length - 1;
        if (last < 0)
        {
            return Vec3.Zero;
        }
        if (idx > last)
        {
            idx = last;
        }
        while (idx > 0 && path[idx].Time > t)
        {
            idx--;
        }
        while (idx < last && path[idx + 1].Time <= t)
        {
            idx++;
        }
        if (idx >= last)
        {
            return path[last].Position;
        }

        BallState a = path[idx];
        BallState b = path[idx + 1];
        double span = b.Time - a.Time;
        double f = span > 1e-9 ? Math.Clamp((t - a.Time) / span, 0.0, 1.0) : 0.0;
        return Vec3.Lerp(a.Position, b.Position, f);
    }
}
