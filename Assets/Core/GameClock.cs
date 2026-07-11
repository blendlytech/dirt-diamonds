namespace DirtAndDiamonds.Core;

/// <summary>
/// Speed ladder for the intraday wall clock (real_time_clock_slice_g.md §6).
/// Ordinals are persisted as the <c>time_speed</c> Game_State value, so the
/// numbering is save-format: append new steps, never renumber. The multiplier
/// behind each step lives in <see cref="GameClock.MinutesPerRealSecond"/> and
/// is a tuning knob, not save state.
/// </summary>
public enum TimeSpeed
{
    Paused = 0,
    Slow = 1,
    Normal = 2,
    Fast = 3,
    Faster = 4,
}

/// <summary>
/// Slice G's cosmetic intraday wall clock (real_time_clock_slice_g.md §3.1):
/// a pure metronome face that accumulates real seconds into game-minutes and
/// reports — never performs — the midnight crossing. The simulation stays
/// day-atomic: this class holds no reference to <see cref="TimeManager"/>,
/// the database, or the bus, and nothing in the sim ever reads it. The
/// driver (BaseballDashboard's buttons today, the G-2 TimeControlBar next)
/// owns the authority to advance the calendar; this is only its face.
///
/// Engine-independent like the rest of Assets/Core (no Godot types) — the
/// CoreLoop/MonteCarlo/GrittyEvents harnesses compile this file headless.
/// </summary>
public sealed class GameClock
{
    public const int MinutesPerDay = 1440;

    /// <summary>Canonical morning start, 08:00 (§3.1) — each rolled day resumes here, and a save without the KV keys boots here.</summary>
    public const int WakeMinute = 480;

    // Sub-minute accumulation between frames; whole minutes move to
    // MinuteOfDay in Advance. On a midnight crossing the surplus parks here
    // (§4.2 back-pressure — at most one roll per call) until the driver's
    // ResetToWake discards it with the skipped night.
    private float _pendingMinutes;

    /// <summary>Minutes since midnight, 0..1439. Displays derive from this; it changes at most once per whole game-minute (the dirty-flag seam).</summary>
    public int MinuteOfDay { get; private set; } = WakeMinute;

    public TimeSpeed Speed { get; set; } = TimeSpeed.Normal;

    public bool IsPaused => Speed == TimeSpeed.Paused;

    /// <summary>
    /// Game-minutes per real second for each ladder step (§6). Deliberately
    /// bounded: traversing seasons is Skip Day's job, not a speed. Tunable
    /// numbers, not design commitments — the enum is the seam.
    /// </summary>
    public static float MinutesPerRealSecond(TimeSpeed speed) => speed switch
    {
        TimeSpeed.Slow => 1f,
        TimeSpeed.Normal => 5f,
        TimeSpeed.Fast => 30f,
        TimeSpeed.Faster => 120f,
        _ => 0f, // Paused (and any out-of-range value holds still rather than racing)
    };

    /// <summary>
    /// Accumulates <paramref name="realSeconds"/> at the current speed and
    /// rolls <see cref="MinuteOfDay"/> forward. Returns the whole game-minutes
    /// applied this call. <paramref name="crossedMidnight"/> signals AT MOST
    /// one day boundary per call (§4.2: each day is a full sim tick, so the
    /// model itself back-pressures) — on a crossing, MinuteOfDay holds at
    /// 23:59 and the overshoot parks in the accumulator; the driver advances
    /// the calendar once, then calls <see cref="ResetToWake"/>. A paused
    /// clock (or a non-positive/NaN delta) is a no-op.
    /// </summary>
    public int Advance(float realSeconds, out bool crossedMidnight)
    {
        crossedMidnight = false;
        if (!(realSeconds > 0f))
        {
            return 0;
        }

        _pendingMinutes += realSeconds * MinutesPerRealSecond(Speed);
        int wholeMinutes = (int)_pendingMinutes;
        if (wholeMinutes <= 0)
        {
            return 0;
        }

        int minutesToMidnight = MinutesPerDay - 1 - MinuteOfDay;
        if (wholeMinutes > minutesToMidnight)
        {
            crossedMidnight = true;
            wholeMinutes = minutesToMidnight;
        }
        _pendingMinutes -= wholeMinutes;
        MinuteOfDay += wholeMinutes;
        return wholeMinutes;
    }

    /// <summary>
    /// Snaps to the canonical wake minute — the driver calls this right after
    /// each midnight-triggered day advance (§3.2), so every new day opens at
    /// 08:00. Drops any parked overshoot: the skipped night absorbs it, and
    /// discarding biases toward less time passing, never more.
    /// </summary>
    public void ResetToWake()
    {
        MinuteOfDay = WakeMinute;
        _pendingMinutes = 0f;
    }

    /// <summary>
    /// Boot-time restore from the persisted KV pair (§3.4). Out-of-range
    /// values clamp/default rather than throw — unlike the calendar keys, a
    /// mangled cosmetic-clock value is a reset to a sane face, never a
    /// refused save.
    /// </summary>
    public void Restore(long minuteOfDay, long speedOrdinal)
    {
        MinuteOfDay = (int)Math.Clamp(minuteOfDay, 0, MinutesPerDay - 1);
        Speed = speedOrdinal is >= (long)TimeSpeed.Paused and <= (long)TimeSpeed.Faster
            ? (TimeSpeed)speedOrdinal
            : TimeSpeed.Normal;
        _pendingMinutes = 0f;
    }
}
