using System.Collections.Generic;
using System.Text;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// The browsable week calendar card (BaseballDashboard's card row, next to
/// ScheduleScreen): one row per day showing the real date, weekday, and the
/// calendar-mandated School / Practice / Game blocks with their times —
/// School from <see cref="SchoolCalendar"/>, HS Practice/Game from
/// <see cref="HsSeasonCalendar"/>, both through the same
/// <see cref="GameManager.GetMandatoryBlocksFor"/> projection the
/// ScheduleScreen's locked rows and SubmitDaySchedule's server-side forcing
/// read, so what this predicts is exactly what the planner will mandate.
/// Game rows resolve the opponent through
/// <see cref="CareerManager.TryGetScheduledGameFor"/> (pure schedule math —
/// no pending-game state is touched looking ahead). Pure reads; renders are
/// dirty-flagged on (shown week, current day), so nothing formats per frame.
/// Node paths verified against CalendarScreen.tscn before this script was
/// written.
/// </summary>
public sealed partial class CalendarScreen : PanelContainer
{
    [Export]
    public string WeekLabelFormat { get; set; } = "{0} {1} – {2} {3}, {4}";

    [Export]
    public string SchoolEntry { get; set; } = "School 8AM–2PM";

    [Export]
    public string PracticeEntry { get; set; } = "Practice 3–5PM";

    [Export]
    public string GameVsFormat { get; set; } = "GAME 4PM vs {0}";

    [Export]
    public string GameAtFormat { get; set; } = "GAME 4PM @ {0}";

    [Export]
    public string FreeDayText { get; set; } = "Free day";

    [Export]
    public string TodaySuffix { get; set; } = "  (today)";

    /// <summary>How many weeks the player can page away from the current week in either direction.</summary>
    [Export]
    public int MaxWeekOffset { get; set; } = 52;

    private const int DaysShown = GameCalendar.DaysPerWeek;

    private Button _prevWeekButton = null!;
    private Button _nextWeekButton = null!;
    private Label _weekLabel = null!;
    private readonly Label[] _dayLabels = new Label[DaysShown];

    private int _weekOffset;

    // Dirty-flag identity: re-render only when the viewed week or the current
    // day actually changed (ui_conventions.md: no per-frame formatting).
    private long _shownWeekStart = -1;
    private long _shownCurrentDay = -1;

    // Opponent-name cache, reloaded only when the avatar's tier changes —
    // renders are rare (day advance / week paging), reads go through the same
    // BaseballQueries surface BaseballDashboard's standings already use.
    private readonly List<TeamRow> _teamsBuffer = new();
    private readonly Dictionary<int, string> _teamNameById = new();
    private bool _teamNamesLoaded;
    private LeagueTier _teamNamesTier;

    private readonly StringBuilder _lineBuilder = new(96);

    public override void _Ready()
    {
        _prevWeekButton = GetNode<Button>("Layout/HeaderRow/PrevWeekButton");
        _nextWeekButton = GetNode<Button>("Layout/HeaderRow/NextWeekButton");
        _weekLabel = GetNode<Label>("Layout/HeaderRow/WeekLabel");
        for (int i = 0; i < DaysShown; i++)
        {
            _dayLabels[i] = GetNode<Label>($"Layout/Day{i}");
        }

        _prevWeekButton.Pressed += OnPrevWeekPressed;
        _nextWeekButton.Pressed += OnNextWeekPressed;
    }

    public override void _ExitTree()
    {
        _prevWeekButton.Pressed -= OnPrevWeekPressed;
        _nextWeekButton.Pressed -= OnNextWeekPressed;
    }

    private void OnPrevWeekPressed()
    {
        if (_weekOffset > -MaxWeekOffset)
        {
            _weekOffset--;
        }
    }

    private void OnNextWeekPressed()
    {
        if (_weekOffset < MaxWeekOffset)
        {
            _weekOffset++;
        }
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        CareerManager career = gm.Career;
        if (!career.HasAvatar)
        {
            Visible = false;
            return;
        }
        Visible = true;

        long currentDay = gm.State.CurrentDay;

        // The week (Mon-anchored on day-of-season, GameCalendar's anchor)
        // containing the current day, paged by the Prev/Next offset. Weeks
        // can't start before day 1.
        int dos = GlobalState.DayOfSeasonForDay(currentDay);
        long weekStart = currentDay - (int)GameCalendar.WeekdayForDayOfSeason(dos)
            + (long)_weekOffset * GameCalendar.DaysPerWeek;
        if (weekStart < 1)
        {
            weekStart = 1;
        }

        if (weekStart == _shownWeekStart && currentDay == _shownCurrentDay)
        {
            return;
        }
        _shownWeekStart = weekStart;
        _shownCurrentDay = currentDay;
        Render(gm, career, weekStart, currentDay);
    }

    private void Render(GameManager gm, CareerManager career, long weekStart, long currentDay)
    {
        long weekEnd = weekStart + DaysShown - 1;
        CalendarDate startDate = GameCalendar.DateForDayOfSeason(GlobalState.DayOfSeasonForDay(weekStart));
        CalendarDate endDate = GameCalendar.DateForDayOfSeason(GlobalState.DayOfSeasonForDay(weekEnd));
        _weekLabel.Text = string.Format(
            WeekLabelFormat,
            GameCalendar.MonthName(startDate.Month), startDate.Day,
            GameCalendar.MonthName(endDate.Month), endDate.Day,
            gm.State.SeasonYearForDay(weekStart));

        EnsureTeamNames(gm, career);

        for (int i = 0; i < DaysShown; i++)
        {
            long day = weekStart + i;
            _dayLabels[i].Text = BuildDayLine(gm, career, day, currentDay);
        }
    }

    private string BuildDayLine(GameManager gm, CareerManager career, long day, long currentDay)
    {
        int dos = GlobalState.DayOfSeasonForDay(day);
        CalendarDate date = GameCalendar.DateForDayOfSeason(dos);
        Weekday weekday = GameCalendar.WeekdayForDayOfSeason(dos);
        MandatoryBlocks blocks = gm.GetMandatoryBlocksFor(day);

        _lineBuilder.Clear();
        _lineBuilder.Append(GameCalendar.NameOf(weekday), 0, 3);
        _lineBuilder.Append(' ');
        _lineBuilder.Append(GameCalendar.MonthName(date.Month), 0, 3);
        _lineBuilder.Append(' ');
        _lineBuilder.Append(date.Day);
        _lineBuilder.Append(" — ");

        bool any = false;
        if (blocks.SchoolHours > 0)
        {
            _lineBuilder.Append(SchoolEntry);
            any = true;
        }
        if (blocks.PracticeHours > 0)
        {
            if (any)
            {
                _lineBuilder.Append(" · ");
            }
            _lineBuilder.Append(PracticeEntry);
            any = true;
        }
        // Game entries come from the schedule itself, not just the mandated
        // hours — non-HS tiers (blocks.GameHours == 0, unlocked) still play
        // daily in-season, and the calendar should say so.
        if (career.TryGetScheduledGameFor(day, out int homeTeamId, out int awayTeamId))
        {
            if (any)
            {
                _lineBuilder.Append(" · ");
            }
            bool home = homeTeamId == career.AvatarTeamId;
            int opponentId = home ? awayTeamId : homeTeamId;
            string opponent = _teamNameById.TryGetValue(opponentId, out string? name) ? name : opponentId.ToString();
            _lineBuilder.AppendFormat(home ? GameVsFormat : GameAtFormat, opponent);
            any = true;
        }
        if (!any)
        {
            _lineBuilder.Append(FreeDayText);
        }
        if (day == currentDay)
        {
            _lineBuilder.Append(TodaySuffix);
        }
        return _lineBuilder.ToString();
    }

    private void EnsureTeamNames(GameManager gm, CareerManager career)
    {
        if (!gm.Baseball.TryGetTeamTier(career.AvatarTeamId, out LeagueTier tier)
            || (_teamNamesLoaded && tier == _teamNamesTier))
        {
            return;
        }
        gm.Baseball.LoadTeamsByTier(tier, _teamsBuffer);
        _teamNameById.Clear();
        foreach (TeamRow team in _teamsBuffer)
        {
            _teamNameById[team.TeamId] = team.Name;
        }
        _teamNamesLoaded = true;
        _teamNamesTier = tier;
    }
}
