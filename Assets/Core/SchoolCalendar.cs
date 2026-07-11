namespace DirtAndDiamonds.Core;

/// <summary>
/// The school-year calendar: which days school is actually in session, and
/// how many hours it takes. Pure math over the day ordinal (no DB, no tier —
/// whether school APPLIES at all stays the Life sim's existing
/// AvatarSchoolAvailable gate; this class only says what a school day looks
/// like when it does). School runs Monday–Friday, 8:00 AM–2:00 PM (6 hours),
/// outside the summer and winter breaks. A fully attended in-session week is
/// still exactly 30 hours — the same total PersonDrift.ExpectedSchoolHoursPerWeek
/// always assumed — just landing on 5 real days instead of smeared over 7, so
/// the weekly GPA attendance ratio keeps its "autopilot week divides to 1
/// bit-exactly" identity as long as expectation and autopilot-attendance both
/// read <see cref="HoursForDay"/> for the same day.
/// </summary>
public static class SchoolCalendar
{
    /// <summary>Hours per in-session school day; 5 days × 6h = the historical 30h week.</summary>
    public const int HoursPerSchoolDay = 6;

    /// <summary>School starts 8:00 AM (display; the sim itself only tracks whole hours per day).</summary>
    public const int StartHour = 8;

    /// <summary>School lets out 2:00 PM = <see cref="StartHour"/> + <see cref="HoursPerSchoolDay"/>.</summary>
    public const int EndHour = StartHour + HoursPerSchoolDay;

    // Break windows as day-of-season bounds, derived from real dates so a
    // month-table retune moves them automatically. Winter break wraps the
    // season boundary (Dec 20 .. Jan 2).
    private static readonly int SummerBreakFirstDay = GameCalendar.DayOfSeasonFor(6, 10);   // Jun 10
    private static readonly int SummerBreakLastDay = GameCalendar.DayOfSeasonFor(8, 20);    // Aug 20
    private static readonly int WinterBreakFirstDay = GameCalendar.DayOfSeasonFor(12, 20);  // Dec 20
    private static readonly int WinterBreakLastDay = GameCalendar.DayOfSeasonFor(1, 2);     // Jan 2

    /// <summary>True when the school year is in session (not summer or winter break) on this day-of-season.</summary>
    public static bool IsInSession(int dayOfSeason)
    {
        if (dayOfSeason >= SummerBreakFirstDay && dayOfSeason <= SummerBreakLastDay)
        {
            return false;
        }
        // Winter break wraps: in it when past Dec 20 OR before Jan 2.
        return dayOfSeason < WinterBreakFirstDay && dayOfSeason > WinterBreakLastDay;
    }

    /// <summary>True when this day-of-season is an actual school day (in session AND a weekday).</summary>
    public static bool IsSchoolDay(int dayOfSeason) =>
        IsInSession(dayOfSeason)
        && !GameCalendar.IsWeekend(GameCalendar.WeekdayForDayOfSeason(dayOfSeason));

    /// <summary>School hours this day-of-season demands: 6 on a school day, 0 otherwise.</summary>
    public static int HoursForDayOfSeason(int dayOfSeason) =>
        IsSchoolDay(dayOfSeason) ? HoursPerSchoolDay : 0;

    /// <summary>Absolute-day form of <see cref="HoursForDayOfSeason"/>.</summary>
    public static int HoursForDay(long day) =>
        HoursForDayOfSeason(GlobalState.DayOfSeasonForDay(day));
}
