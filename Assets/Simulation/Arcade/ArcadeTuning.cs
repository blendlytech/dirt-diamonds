namespace DirtAndDiamonds.Simulation.Arcade;

/// <summary>League tier as the arcade core sees it (mirrors Phase 13's LeagueTier HS/College/Pro without referencing it).</summary>
public enum ArcadeTier : byte
{
    HighSchool = 0,
    College = 1,
    Pro = 2,
}

/// <summary>Batter / pitcher handedness.</summary>
public enum Hand : byte
{
    Right = 0,
    Left = 1,
}

/// <summary>Pitch families, mirrored by name from the micro sim's PitchType so Arcade stays standalone.</summary>
public enum PitchKind : byte
{
    Fastball = 0,
    Breaking = 1,
    Offspeed = 2,
}

/// <summary>
/// Every physical constant and every ratings → physics mapping the arcade
/// core uses, in one place (arcade_baseball_overhaul.md §4.1, §9).
///
/// PHYSICS constants (air, ball, field geometry) are textbook values.
/// Everything marked PROPOSAL is a G-0 first pass for the feel prototype:
/// Gemini Pro Math owns the final values in G-1 (mapping) and G-3
/// (calibration to the Phase 13 per-tier bands). Retuning is a data edit
/// here, never a logic edit elsewhere — the AtBatResolver discipline.
/// </summary>
public static class ArcadeTuning
{
    // ------------------------------------------------------------------
    // Physics (textbook)
    // ------------------------------------------------------------------

    public const double Gravity = 9.80665;          // m/s²
    public const double AirDensity = 1.20;          // kg/m³, ~sea level, 20 °C
    public const double BallMass = 0.145;           // kg (5.125 oz)
    public const double BallRadius = 0.0366;        // m (9.125 in circumference)
    public const double BallArea = Math.PI * BallRadius * BallRadius;
    public const double DragCoefficient = 0.37;     // typical for a pitched/batted ball (Nathan ~0.35–0.40), constant first pass
    public const double MaxLiftCoefficient = 0.40;  // clamp for the Sawicki lift curve

    public const double MphToMps = 0.44704;
    public const double MetresToFeet = 3.280839895;
    public const double RpmToRadPerSec = 2.0 * Math.PI / 60.0;
    public const double DegToRad = Math.PI / 180.0;

    /// <summary>Fixed simulation step (spec §3: 240 Hz; crossings are solved inside the step).</summary>
    public const double FixedStep = 1.0 / 240.0;

    // ------------------------------------------------------------------
    // Field geometry (rulebook)
    // ------------------------------------------------------------------

    /// <summary>Front edge of home plate (17 in from its back point), the strike-zone plane.</summary>
    public const double PlateFrontY = 0.4318;

    /// <summary>Half the plate's width (8.5 in).</summary>
    public const double PlateHalfWidth = 0.2159;

    /// <summary>PROPOSAL: the contact plane, slightly out in front of the plate where a well-timed swing meets the ball.</summary>
    public const double ContactPlaneY = 0.60;

    /// <summary>Strike-zone bottom/top for an average batter (≈18 in / 42 in).</summary>
    public const double ZoneBottom = 0.46;
    public const double ZoneTop = 1.07;

    /// <summary>Foul lines at ±45° from the center-field line.</summary>
    public const double FoulLineDeg = 45.0;

    // ------------------------------------------------------------------
    // Pitching — PROPOSAL
    // ------------------------------------------------------------------

    /// <summary>Release point for a right-hander (extension included): ~54.5 ft from the plate, 6 ft high, arm side.</summary>
    public static readonly Vec3 ReleasePointRight = new(-0.50, 16.60, 1.83);

    public static Vec3 ReleasePoint(Hand throws) =>
        throws == Hand.Right ? ReleasePointRight : new Vec3(-ReleasePointRight.X, ReleasePointRight.Y, ReleasePointRight.Z);

    /// <summary>PROPOSAL: fastball velocity range (mph) per tier, mapped linearly from the arsenal velocity rating 0→100.</summary>
    public static void FastballRangeMph(ArcadeTier tier, out double min, out double max)
    {
        switch (tier)
        {
            case ArcadeTier.HighSchool: min = 70.0; max = 85.0; break;
            case ArcadeTier.College: min = 80.0; max = 94.0; break;
            default: min = 88.0; max = 101.0; break;
        }
    }

    /// <summary>
    /// PROPOSAL: release speed (mph) of a pitch from its arsenal velocity
    /// rating. Fastballs map straight onto the tier range; breaking and
    /// offspeed pitches run at a fraction of it, so their rating moves them
    /// inside a slower band.
    /// </summary>
    public static double PitchSpeedMph(ArcadeTier tier, PitchKind kind, byte velocityRating)
    {
        FastballRangeMph(tier, out double min, out double max);
        double fastball = min + (max - min) * (velocityRating / 100.0);
        return kind switch
        {
            PitchKind.Fastball => fastball,
            PitchKind.Breaking => fastball * 0.84,
            _ => fastball * 0.87,
        };
    }

    /// <summary>PROPOSAL: base spin (rpm) per pitch family at movement 50.</summary>
    public static double BaseSpinRpm(PitchKind kind) => kind switch
    {
        PitchKind.Fastball => 2300.0,
        PitchKind.Breaking => 2500.0,
        _ => 1750.0,
    };

    /// <summary>
    /// PROPOSAL: spin efficiency — the share of spin that is transverse
    /// (makes Magnus force); the rest is gyro spin. Real fastballs run
    /// ~90%, curveballs and sliders ~50–75%, changeups ~85%.
    /// </summary>
    public static double SpinEfficiency(PitchKind kind) => kind switch
    {
        PitchKind.Fastball => 0.90,
        PitchKind.Breaking => 0.65,
        _ => 0.85,
    };

    /// <summary>PROPOSAL: movement rating scales spin rate 0.75× (0) → 1.25× (100).</summary>
    public static double SpinScale(byte movementRating) => 0.75 + 0.5 * (movementRating / 100.0);

    /// <summary>
    /// PROPOSAL: spin-axis tilt (degrees) per family for a right-hander.
    /// Fastball: backspin tilted to the arm side (ride + run). Breaking:
    /// topspin tilted to the glove side (drop + sweep). Offspeed: backspin
    /// tilted further to the arm side (fade + less ride).
    /// </summary>
    public static double SpinTiltDeg(PitchKind kind) => kind switch
    {
        PitchKind.Fastball => 30.0,
        PitchKind.Breaking => 40.0,
        _ => 50.0,
    };

    /// <summary>PROPOSAL: control → per-axis location scatter σ (m) at the plate: 0.20 m at control 0, 0.04 m at 100.</summary>
    public static double ScatterSigma(byte controlRating, double fatigueMultiplier)
    {
        double sigma = 0.20 - 0.16 * (controlRating / 100.0);
        double f = Math.Clamp(fatigueMultiplier, 0.5, 1.0);
        return sigma / f;
    }

    /// <summary>PROPOSAL: a tired pitcher loses velocity: 100% fresh, 97% at fatigue 0.5 (the multiplier's floor), clamped to [0.9, 1].</summary>
    public static double FatigueSpeedScale(double fatigueMultiplier) => Math.Clamp(0.94 + 0.06 * fatigueMultiplier, 0.9, 1.0);

    /// <summary>PROPOSAL (G-0 prototype AI only; G-3 replaces it): share of pitches aimed in the zone.</summary>
    public const double ProtoInZoneShare = 0.5;

    /// <summary>PROPOSAL (G-0 prototype AI): in-zone targets stay this far inside the zone edges (m).</summary>
    public const double ProtoZoneMargin = 0.03;

    /// <summary>PROPOSAL (G-0 prototype AI): the edge/chase target box (m): ±X half-width and Z bottom/top.</summary>
    public const double ProtoChaseHalfWidth = 0.38;
    public const double ProtoChaseBottom = 0.30;
    public const double ProtoChaseTop = 1.30;

    /// <summary>PROPOSAL: aftertouch — a lateral/vertical steer the pitcher can apply during the first part of the flight.</summary>
    public const double AftertouchWindowSec = 0.15;

    /// <summary>PROPOSAL: aftertouch acceleration budget (m/s²) from movement: 0.5 at 0, 3.0 at 100.</summary>
    public static double AftertouchMaxAccel(byte movementRating) => 0.5 + 2.5 * (movementRating / 100.0);

    // ------------------------------------------------------------------
    // Batting — PROPOSAL
    // ------------------------------------------------------------------

    /// <summary>PROPOSAL: bat speed at contact (mph) from power: 58 at 0, 70 at 50, 82 at 100; a power swing adds 6%.</summary>
    public static double BatSpeedMps(byte powerRating, bool pedActive, bool powerSwing)
    {
        double power = pedActive ? Math.Min(100.0, Math.Round(powerRating * 1.5)) : powerRating;
        double mph = 58.0 + 24.0 * (power / 100.0);
        if (powerSwing)
        {
            mph *= 1.06;
        }
        return mph * MphToMps;
    }

    /// <summary>PROPOSAL: time from swing trigger to the bat reaching the contact plane (s). A power swing is slightly slower to start.</summary>
    public static double SwingDurationSec(bool powerSwing) => powerSwing ? 0.165 : 0.150;

    /// <summary>
    /// PROPOSAL: timing half-window (s) inside which the bat can meet the
    /// ball at all: 30 ms at contact 0, 65 ms at 100. The outer quarter of
    /// the window produces fouls (spray past the lines).
    /// </summary>
    public static double TimingWindowSec(byte contactRating, bool powerSwing)
    {
        double w = 0.030 + 0.035 * (contactRating / 100.0);
        return powerSwing ? w * 0.85 : w;
    }

    /// <summary>PROPOSAL: contact cursor half-width (m): 0.09 at contact 0, 0.16 at 100; a power swing shrinks it 25%.</summary>
    public static double CursorHalfWidth(byte contactRating, bool powerSwing)
    {
        double w = 0.09 + 0.07 * (contactRating / 100.0);
        return powerSwing ? w * 0.75 : w;
    }

    /// <summary>PROPOSAL: contact cursor half-height (m): 0.06 at contact 0, 0.11 at 100; a power swing shrinks it 25%.</summary>
    public static double CursorHalfHeight(byte contactRating, bool powerSwing)
    {
        double h = 0.06 + 0.05 * (contactRating / 100.0);
        return powerSwing ? h * 0.75 : h;
    }

    /// <summary>
    /// PROPOSAL: discipline → how long after release the pitch "reads" for a
    /// human (when the spin-colored trail cue appears): 0.22 s at 0, 0.08 s
    /// at 100. Presentation timing only; it never changes the physics.
    /// </summary>
    public static double ReadCueDelaySec(byte disciplineRating) => 0.22 - 0.14 * (disciplineRating / 100.0);

    /// <summary>PROPOSAL: peak collision efficiency q on the sweet spot (MLB-like ≈ 0.2).</summary>
    public const double CollisionEfficiencyMax = 0.21;

    /// <summary>PROPOSAL: how fast q falls with distance from the sweet spot (normalized 0..1) and with timing error.</summary>
    public const double CollisionOffsetFalloff = 0.55;
    public const double CollisionTimingFalloff = 0.35;

    /// <summary>PROPOSAL: a mistimed swing meets the ball before/after peak bat speed.</summary>
    public const double BatSpeedTimingFalloff = 0.25;

    /// <summary>PROPOSAL: a contact this close to the cursor's edge only nicks the ball (foul tip).</summary>
    public const double FoulTipOffset = 0.92;

    /// <summary>PROPOSAL: launch angle at a centered hit, and the span the vertical offset sweeps (± across the cursor).</summary>
    public const double LaunchAngleCenterDeg = 12.0;
    public const double LaunchAngleSpanDeg = 45.0;

    /// <summary>PROPOSAL: spray (deg) at the edge of the timing window; |spray| > 45° is foul, so the outer 25% of the window fouls off.</summary>
    public const double SprayAtWindowEdgeDeg = 60.0;

    /// <summary>PROPOSAL: extra spray from pitch location: an outside pitch goes the other way.</summary>
    public const double SprayPerPlateWidthDeg = 12.0;

    /// <summary>PROPOSAL: backspin (rpm) off the bat by launch angle; grounders get topspin.</summary>
    public static double BattedBackspinRpm(double launchAngleDeg) =>
        launchAngleDeg >= 0.0
            ? Math.Clamp(1200.0 + 42.0 * launchAngleDeg, 200.0, 4500.0)
            : -Math.Clamp(800.0 + 20.0 * -launchAngleDeg, 0.0, 2000.0);

    /// <summary>PROPOSAL: sidespin (rpm) off the bat — balls hit toward a line slice toward it.</summary>
    public const double BattedSidespinRpmAtLine = 600.0;
}
