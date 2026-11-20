using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Every tier's season calendar (league_restructure.md §3, slice 13-3),
/// replacing the HS-only <c>HsSeasonCalendar</c>. Derived, never stored:
/// <see cref="GameCalendar"/> anchors day-of-season 1 to a Monday on a fixed
/// 365-day year, so each rule below ("the first Friday of March") lands on the
/// same day-of-season every season, and the whole year precomputes once into
/// flat lookups (zero allocation on the sim's hot path).
///
/// | Tier    | Game days                                              | Practice (in-season) |
/// | HS      | 10 Fridays from the first Friday of March              | Mon–Thu              |
/// | College | Wed + Sat × 14 weeks, from the Wednesday before the    | Mon, Tue, Thu, Fri   |
/// |         | second Saturday of February to the 14th Saturday       |                      |
/// | Pro     | Tue/Thu/Sat × 21 weeks from the first Tuesday of April,| Mon, Wed, Fri        |
/// |         | then week 22's Tuesday (§16-I9) = 64                   |                      |
///
/// The season window runs from the Monday of week 1 to the last
/// regular-season game day: team Practice and Game blocks are locked inside
/// it and free outside it (§3.2, §16-I10). No tier ever plays on a Sunday.
/// Postseason days are 13-4's.
/// </summary>
public static class TierSeasonCalendar
{
    /// <summary>A team game blocks 3 schedule hours (warmup + game + travel), every tier (§3.2 default).</summary>
    public const int GameHours = 3;

    /// <summary>Team practice blocks 2 schedule hours on practice days, every tier (§3.2 default).</summary>
    public const int PracticeHours = 2;

    /// <summary>Most regular-season game days any tier plays (Pro: 64).</summary>
    public const int MaxGameDays = 64;

    /// <summary>The Draft: the second Saturday of July (§9.3).</summary>
    public static readonly int DraftDay;

    private static readonly int[][] GameDaysByTier;
    // [tier][dayOfSeason] → 1-based game ordinal; 0 = no game. Slot 0 unused.
    private static readonly byte[][] GameOrdinalByDay;
    private static readonly int[] WindowFirstDay;
    private static readonly int[] WindowLastDay;

    static TierSeasonCalendar()
    {
        int tiers = LeagueDirectory.TierCount;
        GameDaysByTier = new int[tiers][];

        // HS: 10 Fridays from the first Friday of March.
        int hsOpen = FirstWeekdayOnOrAfter(GameCalendar.DayOfSeasonFor(3, 1), Weekday.Friday);
        var hs = new int[10];
        for (int w = 0; w < hs.Length; w++)
        {
            hs[w] = hsOpen + w * GameCalendar.DaysPerWeek;
        }
        GameDaysByTier[(int)LeagueTier.HS] = hs;

        // College: Wednesday + Saturday for 14 weeks; week 1's Saturday is the
        // second Saturday of February, its Wednesday the one before it.
        int secondSaturday = FirstWeekdayOnOrAfter(GameCalendar.DayOfSeasonFor(2, 1), Weekday.Saturday)
            + GameCalendar.DaysPerWeek;
        var college = new int[28];
        for (int w = 0; w < 14; w++)
        {
            college[2 * w] = secondSaturday - 3 + w * GameCalendar.DaysPerWeek;
            college[2 * w + 1] = secondSaturday + w * GameCalendar.DaysPerWeek;
        }
        GameDaysByTier[(int)LeagueTier.College] = college;

        // Pro: Tue/Thu/Sat for weeks 1–21 from the first Tuesday of April,
        // then week 22's Tuesday alone (§3 table, §16-I9) = 64.
        int proOpen = FirstWeekdayOnOrAfter(GameCalendar.DayOfSeasonFor(4, 1), Weekday.Tuesday);
        var pro = new int[MaxGameDays];
        for (int w = 0; w < 21; w++)
        {
            int tuesday = proOpen + w * GameCalendar.DaysPerWeek;
            pro[3 * w] = tuesday;
            pro[3 * w + 1] = tuesday + 2;
            pro[3 * w + 2] = tuesday + 4;
        }
        pro[63] = proOpen + 21 * GameCalendar.DaysPerWeek;
        GameDaysByTier[(int)LeagueTier.Pro] = pro;

        GameOrdinalByDay = new byte[tiers][];
        WindowFirstDay = new int[tiers];
        WindowLastDay = new int[tiers];
        for (int t = 0; t < tiers; t++)
        {
            int[] days = GameDaysByTier[t];
            var ordinals = new byte[GlobalState.DaysPerSeason + 1];
            for (int k = 0; k < days.Length; k++)
            {
                ordinals[days[k]] = (byte)(k + 1);
            }
            GameOrdinalByDay[t] = ordinals;
            // The window opens on the Monday of the first game's week.
            WindowFirstDay[t] = days[0] - (int)GameCalendar.WeekdayForDayOfSeason(days[0]);
            WindowLastDay[t] = days[^1];
        }

        DraftDay = FirstWeekdayOnOrAfter(GameCalendar.DayOfSeasonFor(7, 1), Weekday.Saturday)
            + GameCalendar.DaysPerWeek;
    }

    private static int FirstWeekdayOnOrAfter(int dayOfSeason, Weekday weekday)
    {
        int day = dayOfSeason;
        while (GameCalendar.WeekdayForDayOfSeason(day) != weekday)
        {
            day++;
        }
        return day;
    }

    /// <summary>The tier's regular-season game days, ascending (HS 10, College 28, Pro 64).</summary>
    public static ReadOnlySpan<int> GameDays(LeagueTier tier) => GameDaysByTier[(int)tier];

    /// <summary>Regular-season games each team plays (= game days: every team plays every game day, §3.1).</summary>
    public static int GamesPerTeam(LeagueTier tier) => GameDaysByTier[(int)tier].Length;

    /// <summary>True when the tier plays a regular-season game on this day-of-season.</summary>
    public static bool IsGameDay(LeagueTier tier, int dayOfSeason) =>
        dayOfSeason >= 1 && dayOfSeason <= GlobalState.DaysPerSeason
        && GameOrdinalByDay[(int)tier][dayOfSeason] != 0;

    /// <summary>The 1-based ordinal of this day's game within the tier's regular season; false on non-game days.</summary>
    public static bool TryGetGameOrdinal(LeagueTier tier, int dayOfSeason, out int ordinal)
    {
        ordinal = IsGameDay(tier, dayOfSeason) ? GameOrdinalByDay[(int)tier][dayOfSeason] : 0;
        return ordinal != 0;
    }

    /// <summary>The day-of-season of the tier's <paramref name="ordinal"/>-th game day (1-based).</summary>
    public static int GameDay(LeagueTier tier, int ordinal) => GameDaysByTier[(int)tier][ordinal - 1];

    /// <summary>First day of the in-season window: the Monday of week 1.</summary>
    public static int SeasonWindowFirstDay(LeagueTier tier) => WindowFirstDay[(int)tier];

    /// <summary>Last regular-season game day — the window's last day.</summary>
    public static int RegularSeasonLastDay(LeagueTier tier) => WindowLastDay[(int)tier];

    /// <summary>The first day after the regular season (13-4 schedules the postseason from here).</summary>
    public static int PostseasonFirstDay(LeagueTier tier) => WindowLastDay[(int)tier] + 1;

    /// <summary>True inside the tier's season window (Monday of week 1 .. last regular game day).</summary>
    public static bool IsInSeasonWindow(LeagueTier tier, int dayOfSeason) =>
        dayOfSeason >= WindowFirstDay[(int)tier] && dayOfSeason <= WindowLastDay[(int)tier];

    /// <summary>
    /// The planner's calendar-locked team blocks for a day (§3.2, 13-3 brief
    /// D3): inside the season window, Practice and Game are forced (0 on days
    /// without one) and the method returns true; outside it they're free
    /// sliders (false, both 0). GameManager's MandatoryBlocks projection is
    /// this plus school, so the harness proves the exact rule.
    /// </summary>
    public static bool TryGetLockedTeamBlocks(LeagueTier tier, int dayOfSeason, out int practiceHours, out int gameHours)
    {
        if (!IsInSeasonWindow(tier, dayOfSeason))
        {
            practiceHours = 0;
            gameHours = 0;
            return false;
        }
        practiceHours = IsPracticeDay(tier, dayOfSeason) ? PracticeHours : 0;
        gameHours = IsGameDay(tier, dayOfSeason) ? GameHours : 0;
        return true;
    }

    /// <summary>True on the tier's team-practice days inside the season window (§3 table).</summary>
    public static bool IsPracticeDay(LeagueTier tier, int dayOfSeason)
    {
        if (!IsInSeasonWindow(tier, dayOfSeason))
        {
            return false;
        }
        Weekday weekday = GameCalendar.WeekdayForDayOfSeason(dayOfSeason);
        return tier switch
        {
            LeagueTier.HS => weekday <= Weekday.Thursday,
            LeagueTier.College => weekday is Weekday.Monday or Weekday.Tuesday or Weekday.Thursday or Weekday.Friday,
            _ => weekday is Weekday.Monday or Weekday.Wednesday or Weekday.Friday,
        };
    }
}
