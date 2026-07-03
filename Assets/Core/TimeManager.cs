using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Core;

/// <summary>
/// Owns calendar advancement. Each tick persists the new day inside exactly
/// one batch transaction (the Phase 1 benchmarked path — one transaction per
/// calendar tick, never one per row), mirrors it into <see cref="GlobalState"/>,
/// then publishes to the <see cref="EventBus"/>. Because bus dispatch is
/// deferred, subscribers always run after the tick's transaction has
/// committed and are free to open their own batches — the tick's writes and
/// each sim's reaction commit separately by construction.
///
/// Engine-independent (no Godot types): Tools/CoreLoopHarness compiles this
/// source directly and drives the headless 365-day exit-criteria run.
/// </summary>
public sealed class TimeManager
{
    private readonly DatabaseManager _db;
    private readonly GameStateQueries _gameState;
    private readonly GlobalState _state;
    private readonly EventBus _bus;

    public TimeManager(DatabaseManager db, GameStateQueries gameState, GlobalState state, EventBus bus)
    {
        _db = db;
        _gameState = gameState;
        _state = state;
        _bus = bus;
    }

    /// <summary>
    /// Loads the persisted calendar into <see cref="GlobalState"/>, seeding a
    /// fresh save at day 1 of <paramref name="newGameStartYear"/> when no
    /// calendar exists yet. Idempotent across boots.
    /// </summary>
    public void Initialize(int newGameStartYear)
    {
        if (_gameState.TryGetInt64(GameStateKeys.CurrentDay, out long day))
        {
            if (!_gameState.TryGetInt64(GameStateKeys.StartSeasonYear, out long startYear))
            {
                throw new InvalidOperationException(
                    "Save has a current_day but no start_season_year — Game_State is corrupt.");
            }
            _state.SetCalendar(day, (int)startYear);
            return;
        }

        _db.RunInBatch(() =>
        {
            _gameState.SetInt64(GameStateKeys.CurrentDay, 1);
            _gameState.SetInt64(GameStateKeys.StartSeasonYear, newGameStartYear);
        });
        _state.SetCalendar(1, newGameStartYear);
    }

    /// <summary>
    /// Advances the calendar one day: one batch transaction, then
    /// <see cref="DayAdvancedEvent"/> (and <see cref="SeasonRolledOverEvent"/>
    /// on a season boundary) onto the bus. Handlers run at the driver's next
    /// <see cref="EventBus.DispatchPending"/>.
    /// </summary>
    public void AdvanceDay()
    {
        if (!_state.IsCalendarLoaded)
        {
            throw new InvalidOperationException("Calendar not loaded — call Initialize before advancing time.");
        }

        int previousSeasonYear = _state.SeasonYear;
        long nextDay = _state.CurrentDay + 1;

        _db.RunInBatch(() => _gameState.SetInt64(GameStateKeys.CurrentDay, nextDay));
        _state.SetCalendar(nextDay, _state.StartSeasonYear);

        _bus.Publish(new DayAdvancedEvent(nextDay, _state.SeasonYear, _state.DayOfSeason));
        if (_state.SeasonYear != previousSeasonYear)
        {
            _bus.Publish(new SeasonRolledOverEvent(previousSeasonYear, _state.SeasonYear));
        }
    }

    /// <summary>Multi-day skip; still one transaction and one event per tick so subscribers see every day.</summary>
    public void AdvanceDays(int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);
        for (int i = 0; i < days; i++)
        {
            AdvanceDay();
        }
    }
}
