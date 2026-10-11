using DirtAndDiamonds.Core;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Who started when (league_restructure.md §6), shared by every macro sim and
/// the micro-sim so a starter's rest is the same fact wherever his next game is
/// played: the macro sims play every game except the attended team's, which the
/// micro-sim plays, and an opponent's ace must not start two days running just
/// because the two sims each kept their own rotation.
///
/// Keyed by player id on the absolute calendar day (season rollover never
/// resets rest). In memory only for now: a reload treats every pitcher as
/// rested; persisting it belongs with the save-continuity work (R1-SAVE-2).
/// Pre-sized at Initialize so recording a start never allocates.
/// </summary>
public sealed class PitcherRestLedger
{
    private readonly Dictionary<string, long> _lastStart = new(StringComparer.Ordinal);
    private readonly Action<DayAdvancedEvent> _onDayAdvanced;

    public PitcherRestLedger()
    {
        _onDayAdvanced = e => Today = e.Day;
    }

    /// <summary>The current absolute day. Set from the bus, or by a macro sim's own day handler.</summary>
    public long Today { get; set; }

    public void AttachTo(EventBus bus) => bus.Subscribe(_onDayAdvanced);

    public void DetachFrom(EventBus bus) => bus.Unsubscribe(_onDayAdvanced);

    /// <summary>Reserves room for a roster so <see cref="RecordStart"/> stays allocation-free.</summary>
    public void EnsureCapacity(int players) => _lastStart.EnsureCapacity(players);

    public long LastStartDay(string playerId) =>
        _lastStart.TryGetValue(playerId, out long day) ? day : StarterSelection.NeverStarted;

    public void RecordStart(string playerId, long day) => _lastStart[playerId] = day;

    public void Clear() => _lastStart.Clear();
}
