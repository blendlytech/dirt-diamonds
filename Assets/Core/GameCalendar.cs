namespace DirtAndDiamonds.Core;

/// <summary>Day of the week. Day-of-season 1 is a Monday by definition (see <see cref="GameCalendar"/>).</summary>
public enum Weekday : byte
{
    Monday = 0,
    Tuesday = 1,
    Wednesday = 2,
    Thursday = 3,
    Friday = 4,
    Saturday = 5,
    Sunday = 6,
}

/// <summary>A month/day pair within the fixed 365-day sim year (no leap years, matching <see cref="GlobalState.DaysPerSeason"/>).</summary>
public readonly struct CalendarDate
{
    /// <summary>1..12.</summary>
    public readonly int Month;

    /// <summary>1..31, within <see cref="Month"/>.</summary>
    public readonly int Day;

    public CalendarDate(int month, int day)
    {
        Month = month;
        Day = day;
    }
}

/// <summary>
/// Pure day-ordinal → weekday/date math, following <c>SchoolGrade.cs</c>'s
/// derived-never-stored posture: everything here is a function of the absolute
/// day (or day-of-season) the calendar already persists, so there is no schema
/// surface and nothing to migrate. Weekday is anchored to the DAY-OF-SEASON
/// (day-of-season 1 = Monday, every season): <see cref="GlobalState.DaysPerSeason"/>
/// (365) is not a multiple of 7, so anchoring on the absolute day would shift
/// every season's weekday pattern by one day per year — making school/practice/
/// game day sets differ season to season and unprecomputable. The sim year
/// already "deliberately ignores real-world leap years" (GlobalState); every
/// season starting on a Monday is the same flavor of simplification, and it
/// keeps the whole year's schedule a single static table.
/// </summary>
public static class GameCalendar
{
    public const int DaysPerWeek = 7;
    public const int MonthsPerYear = 12;

    /// <summary>Non-leap month lengths; sums to 365 = <see cref="GlobalState.DaysPerSeason"/>.</summary>
    private static readonly int[] MonthLengths = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

    /// <summary>First day-of-season of each month (1, 32, 60, ...), derived once from <see cref="MonthLengths"/>.</summary>
    private static readonly int[] MonthStartDays = BuildMonthStarts();

    private static readonly string[] WeekdayNames =
    {
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
    };

    private static readonly string[] MonthNames =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    private static int[] BuildMonthStarts()
    {
        var starts = new int[MonthsPerYear];
        int day = 1;
        for (int m = 0; m < MonthsPerYear; m++)
        {
            starts[m] = day;
            day += MonthLengths[m];
        }
        return starts;
    }

    /// <summary>The weekday of a 1..365 day-of-season. Day-of-season 1 = Monday.</summary>
    public static Weekday WeekdayForDayOfSeason(int dayOfSeason) =>
        (Weekday)((dayOfSeason - 1) % DaysPerWeek);

    /// <summary>The weekday of an absolute (1-based) game day, via its day-of-season.</summary>
    public static Weekday WeekdayForDay(long day) =>
        WeekdayForDayOfSeason(GlobalState.DayOfSeasonForDay(day));

    /// <summary>The month/day a 1..365 day-of-season falls on.</summary>
    public static CalendarDate DateForDayOfSeason(int dayOfSeason)
    {
        if (dayOfSeason < 1 || dayOfSeason > GlobalState.DaysPerSeason)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayOfSeason), dayOfSeason, "Day-of-season is 1..365.");
        }
        int month = MonthsPerYear - 1;
        while (MonthStartDays[month] > dayOfSeason)
        {
            month--;
        }
        return new CalendarDate(month + 1, dayOfSeason - MonthStartDays[month] + 1);
    }

    /// <summary>The 1..365 day-of-season of a month/day pair — the inverse of <see cref="DateForDayOfSeason"/>.</summary>
    public static int DayOfSeasonFor(int month, int day)
    {
        if (month < 1 || month > MonthsPerYear)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month is 1..12.");
        }
        if (day < 1 || day > MonthLengths[month - 1])
        {
            throw new ArgumentOutOfRangeException(
                nameof(day), day, $"Month {month} has {MonthLengths[month - 1]} days.");
        }
        return MonthStartDays[month - 1] + day - 1;
    }

    /// <summary>True for Saturday/Sunday.</summary>
    public static bool IsWeekend(Weekday weekday) => weekday >= Weekday.Saturday;

    public static string NameOf(Weekday weekday) => WeekdayNames[(int)weekday];

    /// <summary>Month name for a 1..12 month.</summary>
    public static string MonthName(int month) => MonthNames[month - 1];
}
