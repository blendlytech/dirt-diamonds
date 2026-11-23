using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Narrative.Roster;

/// <summary>
/// The coach beats of slice 13-5 (spec §7; brief D5-A, §10.1 Q6, Amendment 5):
/// a Narrative-layer subscriber that turns two Core events into phone texts
/// and stress. It never references Baseball — it learns about designations
/// and forfeits only from <see cref="RosterDesignationChangedEvent"/> and
/// <see cref="GameForfeitedEvent"/> (primitives) and the database, and its
/// writes commit in their own batch, never a Baseball one.
///
/// <list type="bullet">
/// <item>The avatar made inactive (active → inactive): a text from
/// <c>coach_reyes</c> and a <see cref="StressImpulseEvent"/> of
/// <see cref="InactiveStressDelta"/>.</item>
/// <item>Re-activated (inactive → active): a text and <see cref="ReactivatedStressDelta"/>.</item>
/// <item>A second consecutive inactive week sends nothing (transitions only).</item>
/// <item>A forfeit involving the avatar's team: a text from the tier's coach
/// (HS <c>coach_hs</c>, College <c>coach_college</c>, Pro <c>coach_reyes</c>)
/// and no stress (Amendment 5).</item>
/// </list>
///
/// Copy lives in the strings table (<c>roster.*</c> keys, placeholder lines
/// Claude Sonnet 5.5 polishes later; no <c>{tokens}</c> until L-3's
/// NarrativeTokenResolver). Text resolves at write time through
/// <see cref="ResolveText"/> (the live game wires Godot's translation
/// server); without one, the key itself is written.
/// </summary>
public sealed class RosterBeatWriter
{
    /// <summary>Stress on going inactive (Math data value, brief §10.1 Q6).</summary>
    public const float InactiveStressDelta = 15.0f;

    /// <summary>Stress relief on re-activation (Math data value, brief §10.1 Q6).</summary>
    public const float ReactivatedStressDelta = -5.0f;

    public const string InactiveKey = "roster.lock.inactive";
    public const string ReactivatedKey = "roster.lock.reactivated";
    public const string ForfeitLostKey = "roster.forfeit.lost";
    public const string ForfeitWonKey = "roster.forfeit.won";

    public const string ProCoachContact = "coach_reyes";
    public const string CollegeCoachContact = "coach_college";
    public const string HsCoachContact = "coach_hs";

    private readonly DatabaseManager _db;
    private readonly NarrativeLogQueries _log;
    private readonly GameStateQueries _gameState;
    private readonly PlayerQueries _players;
    private readonly GlobalState _state;
    private readonly Action<RosterDesignationChangedEvent> _onDesignationChanged;
    private readonly Action<GameForfeitedEvent> _onForfeited;
    private EventBus? _bus;

    /// <summary>Resolves a strings-table key to its text at write time; null writes the key.</summary>
    public Func<string, string>? ResolveText;

    /// <summary>Coach texts written since construction (harness visibility).</summary>
    public int TextsWritten { get; private set; }

    /// <summary>Stress impulses published since construction (harness visibility).</summary>
    public int StressImpulses { get; private set; }

    public RosterBeatWriter(
        DatabaseManager db, NarrativeLogQueries log, GameStateQueries gameState, PlayerQueries players, GlobalState state)
    {
        _db = db;
        _log = log;
        _gameState = gameState;
        _players = players;
        _state = state;
        _onDesignationChanged = OnDesignationChanged;
        _onForfeited = OnForfeited;
    }

    public void AttachTo(EventBus bus)
    {
        _bus = bus;
        bus.Subscribe(_onDesignationChanged);
        bus.Subscribe(_onForfeited);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onDesignationChanged);
        bus.Unsubscribe(_onForfeited);
        if (ReferenceEquals(_bus, bus))
        {
            _bus = null;
        }
    }

    /// <summary>The coach contact of a tier (the LeagueTier value: 0 HS, 1 College, 2 Pro).</summary>
    public static string CoachContactFor(byte tier) => tier switch
    {
        0 => HsCoachContact,
        1 => CollegeCoachContact,
        _ => ProCoachContact,
    };

    private void OnDesignationChanged(RosterDesignationChangedEvent e)
    {
        if (e.IsActive == e.WasActive)
        {
            return; // transitions only (Q6)
        }
        WriteText(e.PlayerId, ProCoachContact, e.IsActive ? ReactivatedKey : InactiveKey);
        StressImpulses++;
        _bus?.Publish(new StressImpulseEvent(e.PlayerId, e.IsActive ? ReactivatedStressDelta : InactiveStressDelta));
    }

    private void OnForfeited(GameForfeitedEvent e)
    {
        if (!_gameState.TryGetText(GameStateKeys.AvatarPlayerId, out string avatarId) || avatarId.Length == 0
            || !_players.TryGetById(avatarId, out PlayerRow avatar) || avatar.TeamId is not int teamId
            || !e.Involves(teamId))
        {
            return;
        }
        // A forfeit is a coach text and nothing else: no stress (Amendment 5).
        WriteText(avatarId, CoachContactFor(e.Tier), e.WinnerTeamId == teamId ? ForfeitWonKey : ForfeitLostKey);
    }

    private void WriteText(string playerId, string contactId, string key)
    {
        string body = ResolveText?.Invoke(key) ?? key;
        _db.BeginBatch();
        try
        {
            _log.InsertText(_state.SeasonYear, _state.CurrentDay, playerId, contactId, body);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        TextsWritten++;
    }
}
