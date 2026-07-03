using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>One scheduled-but-unplayed attended game, awaiting the player (or autopilot).</summary>
public readonly struct PendingAttendedGame
{
    public readonly int SeasonYear;
    public readonly int DayOfSeason;

    /// <summary>Absolute game-day ordinal — the clock Game_Logs.game_day records against.</summary>
    public readonly long AbsoluteDay;

    public readonly int HomeTeamId;
    public readonly int AwayTeamId;

    public PendingAttendedGame(int seasonYear, int dayOfSeason, long absoluteDay, int homeTeamId, int awayTeamId)
    {
        SeasonYear = seasonYear;
        DayOfSeason = dayOfSeason;
        AbsoluteDay = absoluteDay;
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
    }
}

/// <summary>
/// The Phase 5 career driver: owns the player avatar and every game of the
/// avatar's team. On each <see cref="DayAdvancedEvent"/> it derives the team's
/// pairing from the shared <see cref="LeagueSchedule"/> (the macro sim skips
/// that pairing — see <see cref="LeagueSimulator.SetAttendedTeam"/>) and plays
/// it through <see cref="MicroGame"/>: immediately under the neutral autopilot
/// when <see cref="AutopilotAttendedGames"/> is set (skipped days, headless
/// runs), or parked as a <see cref="PendingAttendedGame"/> for the UI to play
/// interactively via <see cref="PlayPendingGame{TPolicy}"/> on a background
/// task. Either way the game flushes through the micro-sim's own additive
/// batch, which composes with the macro sim's cycle flushes on the same
/// season rows.
///
/// Engine-independent (no Godot types) — the Monte Carlo harness drives whole
/// careers headless. Never references the Life sim.
/// </summary>
public sealed class CareerManager
{
    // New-avatar Players-row defaults: a broke rookie. Ratings come from the
    // caller (the create-a-player UI); these are the life-sim starting facts.
    public const double StartingFunds = 500;
    public const int StartingAge = 19;

    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly BaseballQueries _baseball;
    private readonly GameStateQueries _gameState;
    private readonly GlobalState _state;
    private readonly LeagueSimulator _league;
    private readonly MicroGame _micro;
    private readonly Action<DayAdvancedEvent> _onDayAdvanced;
    private RngState _rng;

    private int[] _teamIds = Array.Empty<int>();
    private string? _avatarPlayerId;
    private int _avatarTeamId;
    private int _avatarTeamIndex = -1;
    private int _avatarSlot = MicroGame.NoHuman;

    private PendingAttendedGame _pending;
    private bool _hasPending;
    private volatile bool _gameInFlight;

    /// <summary>
    /// True (default): the day handler resolves attended games instantly with
    /// the neutral autopilot. False: games park as pending for the UI.
    /// </summary>
    public bool AutopilotAttendedGames = true;

    public bool HasAvatar => _avatarPlayerId is not null;

    public string AvatarPlayerId =>
        _avatarPlayerId ?? throw new InvalidOperationException("No career avatar exists.");

    public int AvatarTeamId => HasAvatar
        ? _avatarTeamId
        : throw new InvalidOperationException("No career avatar exists.");

    /// <summary>The avatar's micro-sim roster slot (resolve once, not per PA).</summary>
    public int AvatarSlot => _avatarSlot;

    public bool HasPendingGame => _hasPending;

    /// <summary>True while an interactive game is running on a background task.</summary>
    public bool IsGameInFlight => _gameInFlight;

    public CareerManager(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        GameStateQueries gameState, GlobalState state,
        LeagueSimulator league, MicroGame micro, RngState rng)
    {
        _db = db;
        _players = players;
        _baseball = baseball;
        _gameState = gameState;
        _state = state;
        _league = league;
        _micro = micro;
        _rng = rng;
        _onDayAdvanced = OnDayAdvanced;
    }

    // ------------------------------------------------------------------
    // Avatar lifecycle
    // ------------------------------------------------------------------

    /// <summary>
    /// Restores the avatar recorded in Game_State (existing save). Returns
    /// false on a save with no avatar yet. Call after the sims' Initialize().
    /// </summary>
    public bool LoadExistingAvatar()
    {
        if (!_gameState.TryGetText(GameStateKeys.AvatarPlayerId, out string avatarId))
        {
            return false;
        }
        if (!_players.TryGetById(avatarId, out PlayerRow row) || !row.TeamId.HasValue)
        {
            throw new InvalidOperationException(
                $"Game_State names avatar '{avatarId}' but the Players row is missing or unrostered — save is corrupt.");
        }
        ActivateAvatar(avatarId, row.TeamId.Value);
        return true;
    }

    /// <summary>
    /// Creates the player avatar on <paramref name="teamId"/>: inserts the
    /// Players + Player_Ratings rows, benches the team's weakest same-role
    /// player to free-agency (rosters stay exactly 9+5), records the avatar in
    /// Game_State — one batch — then reloads both sims' rosters and claims the
    /// team from the macro sim. The league's in-memory stats are flushed first
    /// so the reload loses nothing.
    /// </summary>
    public void CreateAvatar(string firstName, string lastName, int teamId, in PlayerRatingsRow ratings)
    {
        if (HasAvatar)
        {
            throw new InvalidOperationException($"Avatar {_avatarPlayerId} already exists (one career per save).");
        }

        string displacedId = FindDisplacedPlayer(teamId, ratings.IsPitcher);
        string avatarId = Guid.NewGuid().ToString();

        _league.FlushPending();

        _db.BeginBatch();
        try
        {
            _players.Insert(new PlayerRow
            {
                PlayerId = avatarId,
                FirstName = firstName,
                LastName = lastName,
                Age = StartingAge,
                TeamId = teamId,
                Funds = StartingFunds,
                HealthCeiling = 100,
                Recklessness = 0,
                BaseballInterest = 100,
                DetectionRisk = 0,
            });
            PlayerRatingsRow avatarRatings = ratings;
            avatarRatings.PlayerId = avatarId;
            _baseball.UpsertRatings(in avatarRatings);
            _players.SetTeam(displacedId, null);
            _gameState.SetText(GameStateKeys.AvatarPlayerId, avatarId);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        // Both sims re-bulk-load so their roster arrays include the avatar.
        _league.Initialize();
        _micro.Initialize();
        ActivateAvatar(avatarId, teamId);
    }

    private void ActivateAvatar(string avatarId, int teamId)
    {
        var teams = new List<TeamRow>(LeagueSimulator.TeamCount);
        _baseball.LoadAllTeams(teams);
        _teamIds = new int[teams.Count];
        for (int t = 0; t < teams.Count; t++)
        {
            _teamIds[t] = teams[t].TeamId;
        }

        _avatarTeamIndex = Array.IndexOf(_teamIds, teamId);
        if (_avatarTeamIndex < 0)
        {
            throw new InvalidOperationException($"Avatar team_id {teamId} has no Teams row.");
        }
        _avatarSlot = _micro.FindRosterSlot(avatarId);
        if (_avatarSlot == MicroGame.NoHuman)
        {
            throw new InvalidOperationException(
                $"Avatar '{avatarId}' is not in the loaded roster — did MicroGame.Initialize() run after creation?");
        }
        _avatarPlayerId = avatarId;
        _avatarTeamId = teamId;
        _league.SetAttendedTeam(teamId);
    }

    /// <summary>
    /// Weakest same-role player on the team by summed role ratings (ties break
    /// on player_id, matching the roster join's deterministic order).
    /// </summary>
    private string FindDisplacedPlayer(int teamId, bool isPitcher)
    {
        var roster = new List<RosterPlayerRow>(LeagueSimulator.TeamCount * LeagueSimulator.RosterSizePerTeam);
        _baseball.LoadRoster(roster);

        string? weakestId = null;
        int weakestSum = int.MaxValue;
        foreach (RosterPlayerRow row in roster)
        {
            if (row.TeamId != teamId || row.IsPitcher != isPitcher)
            {
                continue;
            }
            int sum = isPitcher
                ? row.PitStuff + row.PitControl + row.PitStamina
                : row.BatPower + row.BatContact + row.BatDiscipline;
            if (sum < weakestSum)
            {
                weakestSum = sum;
                weakestId = row.PlayerId;
            }
        }
        return weakestId ?? throw new InvalidOperationException(
            $"Team {teamId} has no {(isPitcher ? "pitcher" : "position player")} to displace — league not generated?");
    }

    // ------------------------------------------------------------------
    // Bus wiring & the attended-game day loop
    // ------------------------------------------------------------------

    public void AttachTo(EventBus bus) => bus.Subscribe(_onDayAdvanced);

    public void DetachFrom(EventBus bus) => bus.Unsubscribe(_onDayAdvanced);

    private void OnDayAdvanced(DayAdvancedEvent e)
    {
        if (!HasAvatar)
        {
            return;
        }
        if (_gameInFlight)
        {
            throw new InvalidOperationException(
                "Day advanced while an attended game is in flight — the UI must await the game before ticking the clock.");
        }

        // A pending game the player never sat down for is forfeited to the
        // autopilot before today's schedule is looked at.
        ResolvePendingWithAutopilot();

        if (e.DayOfSeason > LeagueSimulator.RegularSeasonDays)
        {
            return; // offseason
        }
        if (!LeagueSchedule.TryGetPairingFor(e.DayOfSeason, _avatarTeamIndex, out SchedulePairing pairing))
        {
            return;
        }

        _pending = new PendingAttendedGame(
            e.SeasonYear, e.DayOfSeason, e.Day,
            _teamIds[pairing.HomeTeam], _teamIds[pairing.AwayTeam]);
        _hasPending = true;

        if (AutopilotAttendedGames)
        {
            ResolvePendingWithAutopilot();
        }
    }

    /// <summary>The game waiting on the player, when <see cref="HasPendingGame"/>.</summary>
    public bool TryGetPendingGame(out PendingAttendedGame pending)
    {
        pending = _pending;
        return _hasPending;
    }

    /// <summary>
    /// Plays the pending attended game with the given policy and flushes its
    /// box score, play-by-play and PED costs in the micro-sim's own batch.
    /// The UI calls this on a background task with the interactive policy; a
    /// cancelled game (OperationCanceledException) stays pending and is
    /// forfeited to the autopilot on the next day tick.
    /// </summary>
    public MicroGameResult PlayPendingGame<TPolicy>(ref TPolicy policy)
        where TPolicy : IBatterPolicy
    {
        if (!_hasPending)
        {
            throw new InvalidOperationException("No attended game is pending.");
        }
        if (_gameInFlight)
        {
            throw new InvalidOperationException("The pending game is already being played.");
        }

        _gameInFlight = true;
        try
        {
            MicroGameResult result = _micro.PlayGame(
                _pending.HomeTeamId, _pending.AwayTeamId, _avatarSlot, ref policy, ref _rng);
            _micro.FlushGame(_pending.SeasonYear, checked((int)_pending.AbsoluteDay));
            _hasPending = false;
            return result;
        }
        finally
        {
            _gameInFlight = false;
        }
    }

    private void ResolvePendingWithAutopilot()
    {
        if (!_hasPending)
        {
            return;
        }
        var autopilot = new NeutralBatterPolicy();
        PlayPendingGame(ref autopilot);
    }
}
