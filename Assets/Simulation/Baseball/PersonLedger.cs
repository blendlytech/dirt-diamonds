using DirtAndDiamonds.Core;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>One player's committed §6.2 lever values as the ledger hands them to a sim's cache rebuild.</summary>
public readonly struct PersonLeverEntry
{
    public readonly string PlayerId;
    public readonly byte Happiness;
    public readonly byte Confidence;
    public readonly byte Teamwork;

    public PersonLeverEntry(string playerId, byte happiness, byte confidence, byte teamwork)
    {
        PlayerId = playerId;
        Happiness = happiness;
        Confidence = confidence;
        Teamwork = teamwork;
    }
}

/// <summary>
/// The attended game's live view of the §6.2 person levers (the HS-6
/// disclosure's per-game person refresh). Fed exclusively by
/// <see cref="PersonLeversChangedEvent"/> off the bus (the EquipmentLedger
/// transport pattern: the writer commits Player_Person in its own batch, then
/// publishes the row's read-back absolutes; this class never opens a
/// connection — the "no mid-game DB reads" constraint that motivated an
/// in-memory ledger in the first place).
///
/// NO boot-time seed, BY DESIGN (unlike Absences/Gear): the sims' Initialize
/// already bakes every persisted Player_Person row, so this ledger carries
/// only post-boot movement — an absent entry means "the Initialize-time bake
/// stands", and an empty ledger is the bit-identity by construction (no
/// harness guard world ever publishes). Because the payload is committed
/// absolutes, an entry that survives past a re-Initialize beat is merely
/// redundant, never stale: re-baking it reproduces the exact bytes Initialize
/// just wrote.
///
/// Consumers (MicroGame only — the macro sims deliberately stay on the §6.2
/// season-stable Initialize bake) never query per PA: they watch
/// <see cref="Version"/> and re-bake affected slots at game start when it
/// moves. All mutation happens on the dispatch thread; the attended game
/// reads at PlayGame start, mutually exclusive with the day tick that feeds
/// the dispatch (the EquipmentLedger memory model), so no lock is needed.
/// Same-value publications do not bump <see cref="Version"/> — no cache
/// churn from redundant announcements.
/// </summary>
public sealed class PersonLedger
{
    private readonly Dictionary<string, PersonLeverEntry> _levers = new(StringComparer.Ordinal);
    private readonly Action<PersonLeversChangedEvent> _onLeversChanged;

    /// <summary>Bumped on every effective change; consumers refresh caches when it moves.</summary>
    public int Version { get; private set; }

    public int Count => _levers.Count;

    public PersonLedger()
    {
        _onLeversChanged = OnLeversChanged;
    }

    public void AttachTo(EventBus bus) => bus.Subscribe(_onLeversChanged);

    public void DetachFrom(EventBus bus) => bus.Unsubscribe(_onLeversChanged);

    /// <summary>Fills <paramref name="destination"/> (cleared first) with every announced player.</summary>
    public int CopyAll(List<PersonLeverEntry> destination)
    {
        destination.Clear();
        foreach (KeyValuePair<string, PersonLeverEntry> pair in _levers)
        {
            destination.Add(pair.Value);
        }
        return destination.Count;
    }

    private void OnLeversChanged(PersonLeversChangedEvent e)
    {
        if (_levers.TryGetValue(e.PlayerId, out PersonLeverEntry existing)
            && existing.Happiness == e.Happiness
            && existing.Confidence == e.Confidence
            && existing.Teamwork == e.Teamwork)
        {
            return;
        }
        _levers[e.PlayerId] = new PersonLeverEntry(e.PlayerId, e.Happiness, e.Confidence, e.Teamwork);
        Version++;
    }
}
