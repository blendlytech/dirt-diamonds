namespace DirtAndDiamonds.Core;

/// <summary>
/// In-memory snapshot of session-wide simulation state — currently the
/// calendar. The database rows (Game_State table) are the source of truth;
/// this mirror exists so simulation loops read the current day without a
/// query. Written only by <see cref="TimeManager"/>; everything else treats
/// it as read-only.
/// </summary>
public sealed class GlobalState
{
    /// <summary>Fixed sim-year length; season math deliberately ignores real-world leap years.</summary>
    public const int DaysPerSeason = 365;

    /// <summary>
    /// Absolute 1-based game-day ordinal — the same clock Entity_Flags.set_on_day
    /// and Game_Logs.game_day record against. 0 means the calendar has not been
    /// loaded from the database yet.
    /// </summary>
    public long CurrentDay { get; private set; }

    /// <summary>Season year that day 1 belongs to (set once at new-game seed).</summary>
    public int StartSeasonYear { get; private set; }

    public bool IsCalendarLoaded => CurrentDay > 0;

    public int SeasonYear => StartSeasonYear + (int)((CurrentDay - 1) / DaysPerSeason);

    /// <summary>1..<see cref="DaysPerSeason"/> within <see cref="SeasonYear"/>.</summary>
    public int DayOfSeason => (int)((CurrentDay - 1) % DaysPerSeason) + 1;

    /// <summary>TimeManager-only. Mirrors what was just committed to Game_State.</summary>
    public void SetCalendar(long currentDay, int startSeasonYear)
    {
        if (currentDay < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(currentDay), currentDay, "Game days are 1-based.");
        }
        CurrentDay = currentDay;
        StartSeasonYear = startSeasonYear;
    }
}
