using DirtAndDiamonds.Simulation.Arcade;

namespace DirtAndDiamonds.UI.Arcade;

/// <summary>
/// The knobs the G-0b tuning panel turns (debug only). One mutable bag the
/// panel writes and <see cref="PrototypeSession"/> reads: profiles are built
/// at the start of each pitch, assists at the moment of the swing, so a knob
/// change takes effect on the next pitch.
///
/// Everything here is a G-0 playtest control, not a design decision. The
/// ratings feed the arcade core through the same <see cref="PitcherProfile"/>
/// / <see cref="BatterProfile"/> structs G-4 will fill from EffectiveRatings;
/// the assist multipliers are the two <see cref="AssistScales"/> fields
/// (arcade_baseball_overhaul.md §5.6: difficulty changes human assists and
/// nothing else). Cursor speed, latency compensation and reduce-motion are
/// presentation-side and never reach the physics core.
///
/// Defaults are the league-average silhouette at Pro tier with neutral
/// assists, i.e. the engine's raw first-pass tuning. PROPOSAL values only:
/// GPT 6 Astra Pro owns the real mapping (G-1) and difficulty targets (G-9).
/// </summary>
public sealed class PrototypeSettings
{
    // ---- Pitcher ----

    public ArcadeTier Tier = ArcadeTier.Pro;

    /// <summary>-1 = the prototype AI's usage mix; otherwise a <see cref="PitchKind"/> value forced for every pitch.</summary>
    public int ForcedPitch = -1;

    /// <summary>Velocity rating applied to all three pitch families (0-100): the "stuff" half of arcade §4.1.</summary>
    public int PitcherVelocity = 50;

    /// <summary>Movement rating applied to all three pitch families (0-100): break size and aftertouch budget.</summary>
    public int PitcherMovement = 50;

    public int PitcherControl = 50;

    /// <summary>PitcherFatigue multiplier: 1 = fresh, floor 0.5.</summary>
    public double PitcherFatigue = 1.0;

    public Hand PitcherThrows = Hand.Right;

    // ---- Batter ----

    public int BatterPower = 50;
    public int BatterContact = 50;

    /// <summary>Discipline only moves the pitch-read cue's timing (presentation); the core ignores it in G-0.</summary>
    public int BatterDiscipline = 50;

    public Hand BatterBats = Hand.Right;

    /// <summary>The PED power hook (x1.5, capped at 100) so the owner can feel it.</summary>
    public bool PedActive;

    // ---- Human assists (arcade §5.6) ----

    public double TimingWindowScale = 1.0;
    public double CursorSizeScale = 1.0;

    // ---- Presentation / controls (never reach the physics core) ----

    /// <summary>Contact-cursor speed at the contact plane (metres per second).</summary>
    public double CursorSpeedMps = 1.6;

    /// <summary>
    /// Display/input latency compensation (ms). A press is treated as having
    /// happened this much earlier, which is how rhythm games let a player
    /// calibrate away a laggy TV. Positive = earlier.
    /// </summary>
    public double LatencyCompMs;

    /// <summary>Gates hit-stop, screen shake, the bat waggle and contact flashes (spec §10.5: every effect respects reduce-motion).</summary>
    public bool ReduceMotion;

    // Usage mix of the shipped DDL backfill silhouette (PitcherProfile.LeagueAverage): 60 / 25 / 15.
    private const byte FastballUsage = 60;
    private const byte BreakingUsage = 25;
    private const byte OffspeedUsage = 15;

    private static byte Rating(int value) => (byte)Math.Clamp(value, 0, 100);

    public PitcherProfile BuildPitcher()
    {
        byte velocity = Rating(PitcherVelocity);
        byte movement = Rating(PitcherMovement);
        return new PitcherProfile(
            velocity, movement, FastballUsage,
            velocity, movement, BreakingUsage,
            velocity, movement, OffspeedUsage,
            Rating(PitcherControl), Math.Clamp(PitcherFatigue, 0.5, 1.0), PitcherThrows);
    }

    public BatterProfile BuildBatter() =>
        new(Rating(BatterPower), Rating(BatterContact), Rating(BatterDiscipline), PedActive, BatterBats);

    public AssistScales BuildAssists() => new(TimingWindowScale, CursorSizeScale);
}
