using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// The High School tier's realistic season calendar — the one tier whose
/// games do NOT land on every day of the 154-day league window. HS ball runs
/// a spring season (March 1 – May 31) with games on Tuesdays and Fridays
/// (26 per season) and team practice on Mondays, Wednesdays and Thursdays;
/// weekends are off. Derived-never-stored like <see cref="SchoolGrades"/>:
/// everything is pure math over the day-of-season, precomputed once at static
/// init into a flat lookup (zero allocation on the sim's hot path).
///
/// The round-robin math itself is untouched: <see cref="LeagueSchedule"/>
/// still deals in a dense 1-based sequence of game days — HS call sites feed
/// it the GAME ORDINAL (1..<see cref="GamesPerSeason"/>) from
/// <see cref="TryGetLeagueScheduleDay"/> in place of the raw day-of-season,
/// so pairings cycle through the same balanced rotation, just on fewer days.
/// Because weekday is anchored to day-of-season (see <see cref="GameCalendar"/>),
/// every season has the identical game-day set and the same
/// <see cref="GamesPerSeason"/> count.
/// </summary>
public static class HsSeasonCalendar
{
    /// <summary>Season opens March 1 (day-of-season 60).</summary>
    public static readonly int SeasonFirstDay = GameCalendar.DayOfSeasonFor(3, 1);

    /// <summary>Season closes May 31 (day-of-season 151).</summary>
    public static readonly int SeasonLastDay = GameCalendar.DayOfSeasonFor(5, 31);

    /// <summary>An HS game blocks 3 schedule hours (warmup + game + travel).</summary>
    public const int GameHours = 3;

    /// <summary>Team practice blocks 2 schedule hours on practice days.</summary>
    public const int PracticeHours = 2;

    /// <summary>Practice starts 3:00 PM — an hour after school lets out (<see cref="SchoolCalendar.EndHour"/> + 1).</summary>
    public const int PracticeStartHour = 15;

    /// <summary>First pitch 4:00 PM on game days.</summary>
    public const int GameStartHour = 16;

    /// <summary>HS games per season (Tuesdays + Fridays inside the window — 26 with the March–May anchors).</summary>
    public static readonly int GamesPerSeason;

    // dayOfSeason (1-based index; slot 0 unused) → 1-based game ordinal, 0 = no game.
    private static readonly byte[] GameOrdinalByDay;

    static HsSeasonCalendar()
    {
        GameOrdinalByDay = new byte[GlobalState.DaysPerSeason + 1];
        byte ordinal = 0;
        for (int day = SeasonFirstDay; day <= SeasonLastDay; day++)
        {
            Weekday weekday = GameCalendar.WeekdayForDayOfSeason(day);
            if (weekday is Weekday.Tuesday or Weekday.Friday)
            {
                GameOrdinalByDay[day] = ++ordinal;
            }
        }
        GamesPerSeason = ordinal;
    }

    /// <summary>True when the HS league plays on this day-of-season.</summary>
    public static bool IsGameDay(int dayOfSeason) =>
        dayOfSeason >= 1 && dayOfSeason <= GlobalState.DaysPerSeason
        && GameOrdinalByDay[dayOfSeason] != 0;

    /// <summary>
    /// The 1-based ordinal of this day's HS game within the season (the value
    /// HS call sites feed <see cref="LeagueSchedule"/> as its schedule day).
    /// False on non-game days.
    /// </summary>
    public static bool TryGetGameOrdinal(int dayOfSeason, out int ordinal)
    {
        if (!IsGameDay(dayOfSeason))
        {
            ordinal = 0;
            return false;
        }
        ordinal = GameOrdinalByDay[dayOfSeason];
        return true;
    }

    /// <summary>True on HS team-practice days: Monday/Wednesday/Thursday inside the season window.</summary>
    public static bool IsPracticeDay(int dayOfSeason)
    {
        if (dayOfSeason < SeasonFirstDay || dayOfSeason > SeasonLastDay)
        {
            return false;
        }
        Weekday weekday = GameCalendar.WeekdayForDayOfSeason(dayOfSeason);
        return weekday is Weekday.Monday or Weekday.Wednesday or Weekday.Thursday;
    }

    /// <summary>
    /// The tier-aware schedule seam shared by the macro sim and the career
    /// driver: maps a calendar day-of-season to the dense schedule day
    /// <see cref="LeagueSchedule"/> consumes. Every tier except HS plays daily
    /// (identity mapping — bit-identical to the pre-calendar behavior); HS
    /// maps its sparse game days to their ordinal and reports false on days
    /// with no HS game. Callers guard the regular season
    /// (1..<see cref="LeagueSimulator.RegularSeasonDays"/>) themselves,
    /// exactly as they always did.
    /// </summary>
    public static bool TryGetLeagueScheduleDay(LeagueTier tier, int dayOfSeason, out int scheduleDay)
    {
        if (tier != LeagueTier.HS)
        {
            scheduleDay = dayOfSeason;
            return true;
        }
        return TryGetGameOrdinal(dayOfSeason, out scheduleDay);
    }
}
