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

/// <summary>Why a bloodline ended (heir mechanics §6). None = still in play.</summary>
public enum LineageFailure : byte
{
    None = 0,

    /// <summary>The avatar has no Child edges at all — nothing to succeed to.</summary>
    NoHeirs = 1,

    /// <summary>Children exist, but every one's baseball_interest is below the play threshold.</summary>
    NoWillingHeir = 2,

    /// <summary>Willing children exist but all are under MaturityAge at the forced retirement.</summary>
    NoPlayableHeir = 3,
}

public enum SuccessionOutcomeKind : byte
{
    /// <summary>Neither retirement trigger fired — the avatar plays on.</summary>
    NotTriggered = 0,

    /// <summary>A retirement trigger fired and an eligible heir was selected.</summary>
    Succeeded = 1,

    /// <summary>A retirement trigger fired with no eligible heir — lineage failure (§6).</summary>
    GameOver = 2,
}

/// <summary>The §5.2/§6 succession decision: carries the chosen heir or the failure reason.</summary>
public readonly struct SuccessionOutcome
{
    public readonly SuccessionOutcomeKind Kind;

    /// <summary>The selected heir's player_id when <see cref="Kind"/> is Succeeded, else null.</summary>
    public readonly string? HeirId;

    /// <summary>The lineage-failure reason when <see cref="Kind"/> is GameOver, else None.</summary>
    public readonly LineageFailure Reason;

    private SuccessionOutcome(SuccessionOutcomeKind kind, string? heirId, LineageFailure reason)
    {
        Kind = kind;
        HeirId = heirId;
        Reason = reason;
    }

    public static SuccessionOutcome NotTriggered => default;

    public static SuccessionOutcome Succeeded(string heirId) =>
        new(SuccessionOutcomeKind.Succeeded, heirId, LineageFailure.None);

    public static SuccessionOutcome GameOver(LineageFailure reason) =>
        new(SuccessionOutcomeKind.GameOver, null, reason);
}

/// <summary>
/// One eligible-heir summary for the succession-UI reveal — every willing,
/// of-age child gathered by <see cref="CareerManager.EvaluateSuccession(List{HeirCandidate})"/>,
/// not just the autopilot's best-by-rating pick.
/// </summary>
public readonly struct HeirCandidate
{
    public readonly string HeirId;
    public readonly string FirstName;
    public readonly string LastName;
    public readonly int Age;
    public readonly int BaseballInterest;
    public readonly PlayerRatingsRow Ratings;

    public HeirCandidate(
        string heirId, string firstName, string lastName, int age, int baseballInterest, in PlayerRatingsRow ratings)
    {
        HeirId = heirId;
        FirstName = firstName;
        LastName = lastName;
        Age = age;
        BaseballInterest = baseballInterest;
        Ratings = ratings;
    }
}

/// <summary>
/// A birth-notification UI's render source — the display-friendly companion
/// to the bare-ids <see cref="ChildBornEvent"/>, same relationship as
/// <see cref="HeirCandidate"/> to <see cref="SuccessionOutcome"/>. Names are
/// resolved once at announce time, so the toast stays correct even if the
/// avatar changes (succession) before the UI gets around to showing it.
/// </summary>
public readonly struct BirthAnnouncement
{
    public readonly string ChildId;
    public readonly string ChildFirstName;
    public readonly string ChildLastName;

    /// <summary>The co-parent's name, or null when the heir was conceived unpartnered.</summary>
    public readonly string? PartnerFirstName;
    public readonly string? PartnerLastName;

    public readonly long Day;

    public BirthAnnouncement(
        string childId, string childFirstName, string childLastName,
        string? partnerFirstName, string? partnerLastName, long day)
    {
        ChildId = childId;
        ChildFirstName = childFirstName;
        ChildLastName = childLastName;
        PartnerFirstName = partnerFirstName;
        PartnerLastName = partnerLastName;
        Day = day;
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
    private readonly LeagueDirectory _leagues;
    private readonly MicroGame _micro;
    private readonly Action<DayAdvancedEvent> _onDayAdvanced;
    private readonly Action<SeasonRolledOverEvent> _onSeasonRolledOver;
    private readonly Action<ChildConceptionRequestedEvent> _onChildConceptionRequested;
    private RngState _rng;

    /// <summary>The bus <see cref="AttachTo"/> wired, kept for the <see cref="ChildBornEvent"/> announce.</summary>
    private EventBus? _bus;

    private int[] _teamIds = Array.Empty<int>();
    private string? _avatarPlayerId;
    private int _avatarTeamId;
    private int _avatarTeamIndex = -1;
    private int _avatarSlot = MicroGame.NoHuman;

    private PendingAttendedGame _pending;
    private bool _hasPending;
    private volatile bool _gameInFlight;
    private LineageFailure _lineageOverReason;

    // Two SEPARATE lists, deliberately not shared: _heirScratch is a
    // throwaway buffer for the zero-arg EvaluateSuccession() wrapper (cleared
    // and repopulated on every call, including RunSuccessionCheck's own
    // internal ones); _pendingHeirSnapshot must survive across frames until
    // the UI resolves it, so a stray RunSuccessionCheck() can never Clear()
    // the very list a succession screen is mid-render against.
    private readonly List<HeirCandidate> _heirScratch = new();
    private readonly List<HeirCandidate> _pendingHeirSnapshot = new();
    private bool _hasPendingSuccession;

    // Unlocked like _pending/_pendingHeirSnapshot above: OnChildConceptionRequested
    // (the producer) and a UI's _Process (the consumer) both only ever run on
    // the main thread inside EventBus.DispatchPending/Godot's frame loop, so
    // there is no cross-thread race to guard against.
    private readonly Queue<BirthAnnouncement> _pendingBirths = new();

    /// <summary>
    /// True (default): the day handler resolves attended games instantly with
    /// the neutral autopilot. False: games park as pending for the UI.
    /// </summary>
    public bool AutopilotAttendedGames = true;

    /// <summary>
    /// Roster-availability source (Phase 8c), optional like the sims' own
    /// references: null — every pre-8c harness path — changes nothing. When
    /// the avatar is absent (arrest/injury/suspension) on a game day, the
    /// team's game still happens but resolves straight through the autopilot
    /// (the micro-sim shadows the avatar's slot with the call-up), so the UI
    /// never sees a pending interactive game the player isn't allowed to play.
    /// </summary>
    public AvailabilityLedger? Availability;

    /// <summary>
    /// Optional rate-denormalization hook (12c review): when set, every
    /// attended game's flush is followed by a tier-scoped rate recompute, so
    /// the post-game UI refresh never reads AVG/IP/ERA/WHIP one game stale
    /// (the macro sim normalizes only on its own cycle flush). Null — every
    /// harness path — leaves the flush behavior byte-identical.
    /// </summary>
    public StatsNormalizer? Normalizer;

    /// <summary>
    /// True (default, headless/harness-safe): a fired retirement trigger
    /// hands off to the single best-rated eligible heir immediately via
    /// <see cref="RunSuccessionCheck"/>. False: a Succeeded outcome instead
    /// parks every eligible heir as <see cref="HasPendingSuccessionChoice"/>
    /// for the succession UI to resolve via <see cref="ResolvePendingSuccession"/>.
    /// </summary>
    public bool AutopilotSuccession = true;

    public bool HasAvatar => _avatarPlayerId is not null;

    public string AvatarPlayerId =>
        _avatarPlayerId ?? throw new InvalidOperationException("No career avatar exists.");

    public int AvatarTeamId => HasAvatar
        ? _avatarTeamId
        : throw new InvalidOperationException("No career avatar exists.");

    /// <summary>The avatar's micro-sim roster slot (resolve once, not per PA).</summary>
    public int AvatarSlot => _avatarSlot;

    public bool HasPendingGame => _hasPending;

    /// <summary>
    /// The bloodline's failure reason once a retirement trigger has fired with
    /// no eligible heir (§6) — None while the dynasty is still in play. The
    /// persisted Game_State.lineage_over_reason key mirrors this; its presence
    /// IS the game-over flag.
    /// </summary>
    public LineageFailure LineageOverReason => _lineageOverReason;

    public bool IsLineageOver => _lineageOverReason != LineageFailure.None;

    /// <summary>
    /// The most recent succession decision the yearly rollover check (or a
    /// direct <see cref="RunSuccessionCheck"/> call) produced — how the UI
    /// notices a handoff or a game-over happened. NotTriggered until the first
    /// check fires.
    /// </summary>
    public SuccessionOutcome LastSuccession { get; private set; }

    /// <summary>
    /// Attach (or clear, with null) the sim-to-UI NPC play-by-play feed for the
    /// next <see cref="PlayPendingGame{TPolicy}"/> call. The UI sets this right
    /// before starting the interactive game's background task and clears it
    /// once the task completes — the sim thread only reads it, so there is no
    /// race as long as the clear happens after the task is observed done.
    /// </summary>
    public PlayerIntentBridge? FeedSink
    {
        set => _micro.FeedSink = value;
    }

    /// <summary>True while an interactive game is running on a background task.</summary>
    public bool IsGameInFlight => _gameInFlight;

    public CareerManager(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        GameStateQueries gameState, GlobalState state,
        LeagueDirectory leagues, MicroGame micro, RngState rng)
    {
        _db = db;
        _players = players;
        _baseball = baseball;
        _gameState = gameState;
        _state = state;
        _leagues = leagues;
        _micro = micro;
        _rng = rng;
        _onDayAdvanced = OnDayAdvanced;
        _onSeasonRolledOver = OnSeasonRolledOver;
        _onChildConceptionRequested = OnChildConceptionRequested;
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

        // One-time migration for saves whose avatar predates lineage state
        // (§1.3): the existing avatar is the gen-1 founder by definition.
        if (!_gameState.TryGetInt64(GameStateKeys.DynastyGeneration, out _))
        {
            _db.RunInBatch(() =>
            {
                _gameState.SetInt64(GameStateKeys.DynastyGeneration, 1);
                _gameState.SetText(GameStateKeys.DynastyFounderId, avatarId);
            });
        }
        _lineageOverReason = _gameState.TryGetText(GameStateKeys.LineageOverReason, out string reason)
            ? ParseLineageFailure(reason)
            : LineageFailure.None;
        return true;
    }

    /// <summary>
    /// Creates the player avatar on <paramref name="teamId"/>: inserts the
    /// Players + Player_Ratings rows (plus Pitcher_Roles and a stuff-derived
    /// Pitch_Arsenals set when the avatar pitches), benches the team's weakest
    /// same-role player to free-agency (rosters stay exactly 9+5+3), records
    /// the avatar in Game_State — one batch — then reloads both sims' rosters
    /// and claims the team from the macro sim. The league's in-memory stats
    /// are flushed first so the reload loses nothing.
    /// </summary>
    /// <param name="pitcherRole">Bullpen role when <paramref name="ratings"/>.IsPitcher; ignored for batters.</param>
    public void CreateAvatar(
        string firstName, string lastName, int teamId, in PlayerRatingsRow ratings,
        PitcherRole pitcherRole = PitcherRole.Starter)
    {
        if (HasAvatar)
        {
            throw new InvalidOperationException($"Avatar {_avatarPlayerId} already exists (one career per save).");
        }
        if (ratings.IsPitcher && pitcherRole == PitcherRole.None)
        {
            throw new ArgumentException("A pitcher avatar needs a bullpen role.", nameof(pitcherRole));
        }

        PitcherRole displacedRole = ratings.IsPitcher ? pitcherRole : PitcherRole.None;
        string displacedId = FindDisplacedPlayer(teamId, displacedRole);
        string avatarId = Guid.NewGuid().ToString();

        // 9a: the avatar's tier's sim is the one whose roster this mutates —
        // resolve it before the flush so nothing in flight is lost to the
        // reload. The other tiers' sims never see this team and stay warm.
        LeagueSimulator league = ResolveLeagueFor(teamId);
        league.FlushPending();

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
            // Schema v10 (development doc §3.3): the avatar's ceiling is the
            // chosen build plus a deterministic youth-headroom grant — a
            // max-built rating clamps at 100 (zero headroom, decline-only), a
            // modest build leaves room to develop. The disclosed creation
            // trade: front-load talent vs. leave room to grow.
            _baseball.UpsertPotential(new PlayerPotentialRow
            {
                PlayerId = avatarId,
                BatPower = DevelopmentCurve.HeadroomPotential(ratings.BatPower, StartingAge),
                BatContact = DevelopmentCurve.HeadroomPotential(ratings.BatContact, StartingAge),
                BatDiscipline = DevelopmentCurve.HeadroomPotential(ratings.BatDiscipline, StartingAge),
                PitStuff = DevelopmentCurve.HeadroomPotential(ratings.PitStuff, StartingAge),
                PitControl = DevelopmentCurve.HeadroomPotential(ratings.PitControl, StartingAge),
                PitStamina = DevelopmentCurve.HeadroomPotential(ratings.PitStamina, StartingAge),
                Fielding = DevelopmentCurve.HeadroomPotential(ratings.Fielding, StartingAge),
            });
            if (ratings.IsPitcher)
            {
                _baseball.UpsertPitcherRole(avatarId, pitcherRole);
                // Deterministic stuff-derived arsenal (spread 0 = no jitter);
                // a bespoke arsenal editor belongs to the creation UI later.
                LeagueGenerator.GenerateArsenal(_baseball, avatarId, ratings.PitStuff, ratingSpread: 0, ref _rng);
            }
            _players.SetTeam(displacedId, null);
            _gameState.SetText(GameStateKeys.AvatarPlayerId, avatarId);
            // Lineage bootstrap (§1.3): the first avatar is the dynasty founder.
            _gameState.SetInt64(GameStateKeys.DynastyGeneration, 1);
            _gameState.SetText(GameStateKeys.DynastyFounderId, avatarId);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        // Both sims re-bulk-load so their roster arrays include the avatar.
        league.Initialize();
        _micro.Initialize();
        ActivateAvatar(avatarId, teamId);
    }

    /// <summary>
    /// The macro-sim owning <paramref name="teamId"/>'s league (9a): tier from
    /// the team's Team_Tiers row, sim from the directory. Loud on a missing
    /// team — a career can only ever attach to a real, tiered franchise.
    /// </summary>
    private LeagueSimulator ResolveLeagueFor(int teamId)
    {
        if (!_baseball.TryGetTeamTier(teamId, out LeagueTier tier))
        {
            throw new InvalidOperationException($"Team {teamId} has no Teams row — cannot resolve its league tier.");
        }
        return _leagues.Get(tier);
    }

    /// <summary>
    /// Conceives an heir off the current avatar (design doc §9.2): parent A is
    /// always the avatar; parent B is the avatar's Partner counterpart's
    /// ratings when one exists (§2.3's average-parent vector when that
    /// Partner has no Player_Ratings row), else the average-parent outright.
    /// Inserts the heir unrostered (team_id null) with blended ratings, a
    /// rolled hidden baseball_interest, a role + stuff-derived arsenal when
    /// the heir is a pitcher, and a Child edge to each real parent — one
    /// batch, mirroring CreateAvatar minus the roster mutation. Neither sim is
    /// re-initialized: the heir is invisible to both until succession (§5)
    /// rosters it. Returns the heir's player_id.
    ///
    /// A non-null <paramref name="partnerId"/> names the co-parent explicitly
    /// and skips the DB Partner lookup — the bus-driven conception path
    /// resolves the partner from the live relationship graph, which a
    /// same-session marriage has reached even when the day-cadence flush has
    /// not yet persisted the edge (marriage_and_conception.md §4.2). Null
    /// keeps the original DB-reading path for direct/harness callers.
    /// </summary>
    public string ConceiveChild(string firstName, string lastName, int birthAge = 0, string? partnerId = null)
    {
        string avatarId = AvatarPlayerId; // throws if no avatar exists

        if (!_baseball.TryGetRatings(avatarId, out PlayerRatingsRow parentA))
        {
            throw new InvalidOperationException($"Avatar '{avatarId}' has no Player_Ratings row — save is corrupt.");
        }

        partnerId ??= FindPartnerId(avatarId);
        PlayerRatingsRow parentB = partnerId is not null && _baseball.TryGetRatings(partnerId, out PlayerRatingsRow partnerRatings)
            ? partnerRatings
            : HeirGenetics.AverageParent();

        // §2.2: is_pitcher and role inherit from parent A (the avatar); a
        // position-player avatar or an avatar mid-role-less state (shouldn't
        // happen, but is not this method's invariant to enforce) defaults Starter.
        bool isPitcher = parentA.IsPitcher;
        PitcherRole role = PitcherRole.None;
        if (isPitcher)
        {
            role = _baseball.TryGetPitcherRole(avatarId, out PitcherRole avatarRole) && avatarRole != PitcherRole.None
                ? avatarRole
                : PitcherRole.Starter;
        }

        PlayerRatingsRow heirRatings = HeirGenetics.BlendRatings(in parentA, in parentB, isPitcher, ref _rng);
        int interest = HeirGenetics.RollInterest(HeirGenetics.HeirGeneticsProfile.BirthAffinity, ref _rng);
        string heirId = Guid.NewGuid().ToString();

        // Schema v10 (development doc §3.3): the blended vector is the heir's
        // POTENTIAL — the genetic ceiling, reached only by developing. His
        // CURRENT ratings sit the full birth-age prospect discount below it
        // (deterministic, gap roll 1.0: same-age heirs shift identically, so
        // the succession best-by-rating ordering is never lottery-scrambled;
        // the §2.4 development jitter supplies the bust/breakout texture once
        // he is rostered). Development starts when succession rosters him —
        // unrostered people never develop (v1, disclosed).
        PlayerRatingsRow heirCurrent = heirRatings;
        heirCurrent.BatPower = DevelopmentCurve.RawCurrent(heirRatings.BatPower, birthAge, 1.0);
        heirCurrent.BatContact = DevelopmentCurve.RawCurrent(heirRatings.BatContact, birthAge, 1.0);
        heirCurrent.BatDiscipline = DevelopmentCurve.RawCurrent(heirRatings.BatDiscipline, birthAge, 1.0);
        heirCurrent.PitStuff = DevelopmentCurve.RawCurrent(heirRatings.PitStuff, birthAge, 1.0);
        heirCurrent.PitControl = DevelopmentCurve.RawCurrent(heirRatings.PitControl, birthAge, 1.0);
        heirCurrent.PitStamina = DevelopmentCurve.RawCurrent(heirRatings.PitStamina, birthAge, 1.0);
        heirCurrent.Fielding = DevelopmentCurve.RawCurrent(heirRatings.Fielding, birthAge, 1.0);

        _db.BeginBatch();
        try
        {
            _players.Insert(new PlayerRow
            {
                PlayerId = heirId,
                FirstName = firstName,
                LastName = lastName,
                Age = birthAge,
                TeamId = null,
                Funds = 0,
                HealthCeiling = 100,
                Recklessness = 0,
                BaseballInterest = interest,
                DetectionRisk = 0,
            });
            heirCurrent.PlayerId = heirId;
            _baseball.UpsertRatings(in heirCurrent);
            _baseball.UpsertPotential(new PlayerPotentialRow
            {
                PlayerId = heirId,
                BatPower = heirRatings.BatPower,
                BatContact = heirRatings.BatContact,
                BatDiscipline = heirRatings.BatDiscipline,
                PitStuff = heirRatings.PitStuff,
                PitControl = heirRatings.PitControl,
                PitStamina = heirRatings.PitStamina,
                Fielding = heirRatings.Fielding,
            });
            if (isPitcher)
            {
                _baseball.UpsertPitcherRole(heirId, role);
                // Deterministic stuff-derived arsenal (spread 0 = no jitter),
                // same call CreateAvatar makes for a pitcher avatar — shaped
                // by what the heir throws NOW (the current, discounted stuff).
                LeagueGenerator.GenerateArsenal(_baseball, heirId, heirCurrent.PitStuff, ratingSpread: 0, ref _rng);
            }
            _players.UpsertRelationship(avatarId, heirId, HeirGenetics.HeirGeneticsProfile.BirthAffinity, RelationshipType.Child);
            if (partnerId is not null)
            {
                _players.UpsertRelationship(partnerId, heirId, HeirGenetics.HeirGeneticsProfile.BirthAffinity, RelationshipType.Child);
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        return heirId;
    }

    /// <summary>The player's Partner relationship counterpart, if any (§1.1) — null when no Partner edge exists.</summary>
    private string? FindPartnerId(string playerId)
    {
        var relationships = new List<RelationshipRow>();
        _players.LoadRelationshipsFor(playerId, relationships);
        foreach (RelationshipRow rel in relationships)
        {
            if (rel.Type == RelationshipType.Partner)
            {
                return rel.Player1Id == playerId ? rel.Player2Id : rel.Player1Id;
            }
        }
        return null;
    }

    private void ActivateAvatar(string avatarId, int teamId)
    {
        // 9a: the schedule index must be TIER-relative — LeagueSchedule's
        // round-robin runs 0..7 WITHIN one league, so _teamIds holds only the
        // avatar tier's teams (ordered by team_id, matching the tier sim's own
        // team-index order) and _avatarTeamIndex indexes into that.
        LeagueSimulator league = ResolveLeagueFor(teamId);
        var teams = new List<TeamRow>(LeagueSimulator.TeamCount);
        _baseball.LoadTeamsByTier(league.Tier, teams);
        _teamIds = new int[teams.Count];
        for (int t = 0; t < teams.Count; t++)
        {
            _teamIds[t] = teams[t].TeamId;
        }

        _avatarTeamIndex = Array.IndexOf(_teamIds, teamId);
        if (_avatarTeamIndex < 0)
        {
            throw new InvalidOperationException($"Avatar team_id {teamId} has no Teams row in tier {league.Tier}.");
        }
        _avatarSlot = _micro.FindRosterSlot(avatarId);
        if (_avatarSlot == MicroGame.NoHuman)
        {
            throw new InvalidOperationException(
                $"Avatar '{avatarId}' is not in the loaded roster — did MicroGame.Initialize() run after creation?");
        }
        _avatarPlayerId = avatarId;
        _avatarTeamId = teamId;
        league.SetAttendedTeam(teamId);

        // 9b: announce the activation so the Life-sim bridge can re-point the
        // daily clock (creation and succession both pass through here). Null
        // _bus = not attached yet (boot-time LoadExistingAvatar, headless
        // fixtures) — the boot path syncs directly instead.
        _bus?.Publish(new AvatarChangedEvent(avatarId, teamId));
    }

    /// <summary>
    /// Weakest same-role player on the team by summed role ratings (ties break
    /// on player_id, matching the roster join's deterministic order). Role is
    /// exact since v4: a starter avatar displaces a starter, a reliever a
    /// reliever, a batter a batter — rosters stay 9 + 5 + 3.
    /// </summary>
    private string FindDisplacedPlayer(int teamId, PitcherRole role)
    {
        var roster = new List<RosterPlayerRow>(LeagueSimulator.TeamCount * LeagueSimulator.RosterSizePerTeam);
        _baseball.LoadRoster(roster);

        bool isPitcher = role != PitcherRole.None;
        string? weakestId = null;
        int weakestSum = int.MaxValue;
        foreach (RosterPlayerRow row in roster)
        {
            if (row.TeamId != teamId || row.Role != role)
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
            $"Team {teamId} has no {(isPitcher ? $"{role} pitcher" : "position player")} to displace — league not generated?");
    }

    // ------------------------------------------------------------------
    // Succession (heir mechanics §5–§6)
    // ------------------------------------------------------------------

    /// <summary>
    /// The §5.1/§5.2 succession decision, read-only: did a retirement trigger
    /// fire, and if so, which heir (if any) succeeds. Ages and interest are
    /// read live (the reveal, §4.2). Headless/autopilot selection: the
    /// eligible heir with the highest summed role ratings, ties broken on
    /// player_id — the same ordering rule <see cref="FindDisplacedPlayer"/>
    /// uses. Mutating the world is <see cref="Succeed"/>'s job.
    /// </summary>
    public SuccessionOutcome EvaluateSuccession() => EvaluateSuccession(_heirScratch);

    /// <summary>
    /// Same §5.1/§5.2 decision as the zero-arg overload, additionally filling
    /// <paramref name="eligibleHeirs"/> with every willing, of-age candidate
    /// (the succession-UI reveal list) — the autopilot's own best-by-rating
    /// pick is exactly the same computation, unchanged.
    /// </summary>
    public SuccessionOutcome EvaluateSuccession(List<HeirCandidate> eligibleHeirs)
    {
        eligibleHeirs.Clear();

        string avatarId = AvatarPlayerId; // throws if no avatar exists
        if (!_players.TryGetById(avatarId, out PlayerRow avatar))
        {
            throw new InvalidOperationException($"Avatar '{avatarId}' has no Players row — save is corrupt.");
        }
        if (avatar.Age < HeirGenetics.HeirGeneticsProfile.MandatoryRetirementAge
            && avatar.HealthCeiling > HeirGenetics.HeirGeneticsProfile.HealthRetirementFloor)
        {
            return SuccessionOutcome.NotTriggered;
        }

        // Gather the avatar's children: Child edges whose OTHER endpoint is
        // younger (§1.2 — the older endpoint is always the parent).
        var relationships = new List<RelationshipRow>();
        _players.LoadRelationshipsFor(avatarId, relationships);

        bool anyChild = false;
        bool anyWilling = false;
        string? bestHeirId = null;
        int bestSum = -1;
        foreach (RelationshipRow rel in relationships)
        {
            if (rel.Type != RelationshipType.Child)
            {
                continue;
            }
            string otherId = rel.Player1Id == avatarId ? rel.Player2Id : rel.Player1Id;
            if (!_players.TryGetById(otherId, out PlayerRow other) || other.Age >= avatar.Age)
            {
                continue; // the avatar's own parent, not a child
            }
            anyChild = true;
            if (other.BaseballInterest < HeirGenetics.HeirGeneticsProfile.InterestPlayThreshold)
            {
                continue; // unwilling — walks away from baseball (§4.2)
            }
            anyWilling = true;
            if (other.Age < HeirGenetics.HeirGeneticsProfile.MaturityAge
                || !_baseball.TryGetRatings(otherId, out PlayerRatingsRow ratings))
            {
                continue; // not of age (or unplayable: no ratings row)
            }
            eligibleHeirs.Add(new HeirCandidate(otherId, other.FirstName, other.LastName, other.Age, other.BaseballInterest, in ratings));
            int sum = ratings.IsPitcher
                ? ratings.PitStuff + ratings.PitControl + ratings.PitStamina
                : ratings.BatPower + ratings.BatContact + ratings.BatDiscipline;
            if (sum > bestSum || (sum == bestSum && string.CompareOrdinal(otherId, bestHeirId) < 0))
            {
                bestSum = sum;
                bestHeirId = otherId;
            }
        }

        if (bestHeirId is not null)
        {
            return SuccessionOutcome.Succeeded(bestHeirId);
        }
        return SuccessionOutcome.GameOver(
            !anyChild ? LineageFailure.NoHeirs
            : !anyWilling ? LineageFailure.NoWillingHeir
            : LineageFailure.NoPlayableHeir);
    }

    /// <summary>
    /// The §5.3 handoff — the one dangerous mutation that swaps the avatar.
    /// Promotes an existing, eligible child of the current avatar onto the
    /// retiring avatar's team, preserving the 9+5+3 roster invariant (§5.4):
    /// matching roles inherit the retiree's exact slot; a role mismatch
    /// displaces the team's weakest same-role player and backfills the
    /// retiree's vacated slot with the strongest signable free agent of that
    /// role (or a generated replacement-level filler when none exists). One
    /// batch re-points avatar_player_id and increments dynasty_generation,
    /// then both sims re-initialize against the new roster — identical to the
    /// tail of <see cref="CreateAvatar"/>. The UI's chosen-heir path and the
    /// autopilot rollover check both land here.
    /// </summary>
    public void Succeed(string heirId)
    {
        string retireeId = AvatarPlayerId; // throws if no avatar exists
        if (!_players.TryGetById(retireeId, out PlayerRow retiree) || !retiree.TeamId.HasValue)
        {
            throw new InvalidOperationException($"Avatar '{retireeId}' is missing or unrostered — save is corrupt.");
        }
        int teamId = retiree.TeamId.Value;

        if (!_players.TryGetById(heirId, out PlayerRow heir))
        {
            throw new ArgumentException($"Heir '{heirId}' has no Players row.", nameof(heirId));
        }
        if (heir.TeamId.HasValue)
        {
            throw new InvalidOperationException($"Heir '{heirId}' is already rostered on team {heir.TeamId}.");
        }
        if (!IsChildOf(retireeId, heirId, retiree.Age, heir.Age))
        {
            throw new ArgumentException($"'{heirId}' is not a child of the avatar — only heirs can succeed.", nameof(heirId));
        }
        // Fail loudly on an ineligible heir rather than silently rostering an
        // unwilling child — the reveal gate (§5.2) is a real rule, not UI polish.
        if (heir.BaseballInterest < HeirGenetics.HeirGeneticsProfile.InterestPlayThreshold)
        {
            throw new InvalidOperationException($"Heir '{heirId}' is unwilling (interest {heir.BaseballInterest}).");
        }
        if (heir.Age < HeirGenetics.HeirGeneticsProfile.MaturityAge)
        {
            throw new InvalidOperationException($"Heir '{heirId}' is not of age ({heir.Age}).");
        }
        if (!_baseball.TryGetRatings(heirId, out PlayerRatingsRow heirRatings)
            || !_baseball.TryGetRatings(retireeId, out PlayerRatingsRow retireeRatings))
        {
            throw new InvalidOperationException("Heir or retiree has no Player_Ratings row — save is corrupt.");
        }

        PitcherRole heirRole = ResolveRole(heirId, heirRatings.IsPitcher);
        PitcherRole retireeRole = ResolveRole(retireeId, retireeRatings.IsPitcher);

        // All pool reads happen BEFORE anyone is benched, so the retiree can
        // never be "promoted" back into their own vacated slot.
        bool sameRole = heirRole == retireeRole;
        string? displacedId = null;
        string? backfillId = null;
        if (!sameRole)
        {
            displacedId = FindDisplacedPlayer(teamId, heirRole);
            backfillId = FindStrongestFreeAgent(retireeRole, excludeId: heirId);
        }

        // 9a: succession stays within the family franchise's tier — the heir
        // inherits the retiree's team, so the retiree-team sim is the one
        // whose roster mutates (a cross-tier handoff is 9c's promotion seam).
        LeagueSimulator league = ResolveLeagueFor(teamId);
        league.FlushPending();

        _db.BeginBatch();
        try
        {
            _players.SetTeam(retireeId, null);      // §5.3 step 1: retiree → FA, stats persist
            _players.SetTeam(heirId, teamId);       // §5.3 step 2: heir onto the family franchise
            if (!sameRole)
            {
                _players.SetTeam(displacedId!, null);
                if (backfillId is not null)
                {
                    _players.SetTeam(backfillId, teamId);
                }
                else
                {
                    // No signable free agent of the vacated role: invent a
                    // replacement-level (spread 0 = league-average) filler,
                    // the same path EnsureV4 uses to invent relievers.
                    (string fillerId, int fillerStuff) = LeagueGenerator.GeneratePlayer(
                        _players, _baseball, teamId, retireeRole, ratingSpread: 0, ref _rng);
                    if (retireeRole != PitcherRole.None)
                    {
                        LeagueGenerator.GenerateArsenal(_baseball, fillerId, fillerStuff, ratingSpread: 0, ref _rng);
                    }
                }
            }
            _gameState.SetText(GameStateKeys.AvatarPlayerId, heirId);
            long generation = _gameState.TryGetInt64(GameStateKeys.DynastyGeneration, out long stored) ? stored : 1;
            _gameState.SetInt64(GameStateKeys.DynastyGeneration, generation + 1);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        // Identical to the tail of CreateAvatar: both sims re-bulk-load, then
        // the avatar slot/team/attended filter re-resolve against the new roster.
        league.Initialize();
        _micro.Initialize();
        ActivateAvatar(heirId, teamId);
    }

    /// <summary>
    /// Re-resolves the avatar against CURRENT database state after an external
    /// roster mutation has re-initialized the sims — the 9c promotion pass's
    /// re-activation tail (promotion doc §6), the <see cref="Succeed"/> tail
    /// generalized cross-tier. The caller (PromotionManager) has already
    /// committed its batch and re-run every tier sim's + the micro-sim's
    /// Initialize(); this re-reads the avatar's (possibly new) team, clears
    /// the attended-team claim on every registered tier sim — a cross-tier
    /// move must not leave the ORIGIN sim skipping its old team's games
    /// forever — and runs the standard activation: tier-relative
    /// _avatarTeamIndex against the new tier's teams, _avatarSlot re-found,
    /// SetAttendedTeam on the new tier's sim, AvatarChangedEvent republished
    /// (the 9b bridge re-points the Life-sim clock + school gate from it).
    /// The avatar keeps every prior stat row — they persist by player_id.
    /// </summary>
    public void ReactivateAvatar()
    {
        string avatarId = AvatarPlayerId; // throws if no avatar exists
        if (!_players.TryGetById(avatarId, out PlayerRow row) || !row.TeamId.HasValue)
        {
            throw new InvalidOperationException(
                $"Avatar '{avatarId}' is missing or unrostered after a roster mutation — the promotion pass must never bench the avatar.");
        }
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            if (_leagues.TryGet((LeagueTier)t, out LeagueSimulator sim))
            {
                sim.ClearAttendedTeam();
            }
        }
        ActivateAvatar(avatarId, row.TeamId.Value);
    }

    /// <summary>
    /// Evaluates succession and applies the outcome: a fired trigger either
    /// hands off to the selected heir (<see cref="Succeed"/>) or records the
    /// lineage failure in Game_State (§6 — the key's presence is the game-over
    /// flag). The yearly rollover check calls this; a voluntary-retirement UI
    /// would too. Returns (and stores in <see cref="LastSuccession"/>) the
    /// decision.
    /// </summary>
    public SuccessionOutcome RunSuccessionCheck()
    {
        SuccessionOutcome outcome = EvaluateSuccession();
        switch (outcome.Kind)
        {
            case SuccessionOutcomeKind.Succeeded:
                Succeed(outcome.HeirId!);
                break;
            case SuccessionOutcomeKind.GameOver:
                PersistGameOver(outcome.Reason);
                break;
        }
        LastSuccession = outcome;
        return outcome;
    }

    /// <summary>§6: writes the lineage-failure key — its presence in Game_State IS the game-over signal. There's no choice to make on a game-over, so both the autopilot and interactive paths apply it immediately (only a Succeeded outcome ever pauses for the UI).</summary>
    private void PersistGameOver(LineageFailure reason)
    {
        _gameState.SetText(GameStateKeys.LineageOverReason, LineageFailureName(reason));
        _lineageOverReason = reason;
    }

    /// <summary>True once a retirement trigger has fired with ≥1 eligible heir and <see cref="AutopilotSuccession"/> is off — the succession UI's cue to show the reveal/choice screen.</summary>
    public bool HasPendingSuccessionChoice => _hasPendingSuccession;

    /// <summary>Every eligible heir for the pending choice, when <see cref="HasPendingSuccessionChoice"/>.</summary>
    public bool TryGetPendingSuccessionChoice(out IReadOnlyList<HeirCandidate> candidates)
    {
        candidates = _pendingHeirSnapshot;
        return _hasPendingSuccession;
    }

    /// <summary>The succession UI's answer: hands off to the player's chosen heir. <paramref name="heirId"/> must be one of the pending candidates; <see cref="Succeed"/> independently re-validates against live state.</summary>
    public void ResolvePendingSuccession(string heirId)
    {
        if (!_hasPendingSuccession)
        {
            throw new InvalidOperationException("No succession choice is pending.");
        }
        bool isCandidate = false;
        for (int i = 0; i < _pendingHeirSnapshot.Count; i++)
        {
            if (_pendingHeirSnapshot[i].HeirId == heirId)
            {
                isCandidate = true;
                break;
            }
        }
        if (!isCandidate)
        {
            throw new ArgumentException($"'{heirId}' is not one of the pending succession candidates.", nameof(heirId));
        }
        _hasPendingSuccession = false;
        Succeed(heirId);
        LastSuccession = SuccessionOutcome.Succeeded(heirId);
    }

    /// <summary>True when a Child edge links the two ids and the age order marks <paramref name="parentId"/> as the parent (§1.2).</summary>
    private bool IsChildOf(string parentId, string childId, int parentAge, int childAge)
    {
        if (childAge >= parentAge)
        {
            return false;
        }
        var relationships = new List<RelationshipRow>();
        _players.LoadRelationshipsFor(childId, relationships);
        foreach (RelationshipRow rel in relationships)
        {
            if (rel.Type == RelationshipType.Child
                && (rel.Player1Id == parentId || rel.Player2Id == parentId))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A player's roster role: None for position players, their Pitcher_Roles row (default Starter) for pitchers.</summary>
    private PitcherRole ResolveRole(string playerId, bool isPitcher)
    {
        if (!isPitcher)
        {
            return PitcherRole.None;
        }
        return _baseball.TryGetPitcherRole(playerId, out PitcherRole role) && role != PitcherRole.None
            ? role
            : PitcherRole.Starter;
    }

    /// <summary>
    /// Strongest signable free agent of the given role by summed role ratings
    /// (ties on player_id — the SQL already orders by it, so first-wins is the
    /// tiebreak), or null when the pool is empty. The signability window
    /// (of age, not aged out, not health-retired) is applied in SQL; the
    /// incoming heir is excluded defensively.
    /// </summary>
    private string? FindStrongestFreeAgent(PitcherRole role, string excludeId)
    {
        var pool = new List<RosterPlayerRow>();
        _baseball.LoadFreeAgents(pool,
            minAge: HeirGenetics.HeirGeneticsProfile.MaturityAge,
            maxAge: HeirGenetics.HeirGeneticsProfile.MandatoryRetirementAge,
            minHealth: HeirGenetics.HeirGeneticsProfile.HealthRetirementFloor);

        bool isPitcher = role != PitcherRole.None;
        string? strongestId = null;
        int strongestSum = -1;
        foreach (RosterPlayerRow row in pool)
        {
            if (row.Role != role || row.PlayerId == excludeId)
            {
                continue;
            }
            int sum = isPitcher
                ? row.PitStuff + row.PitControl + row.PitStamina
                : row.BatPower + row.BatContact + row.BatDiscipline;
            if (sum > strongestSum)
            {
                strongestSum = sum;
                strongestId = row.PlayerId;
            }
        }
        return strongestId;
    }

    private static string LineageFailureName(LineageFailure reason) => reason switch
    {
        LineageFailure.NoHeirs => nameof(LineageFailure.NoHeirs),
        LineageFailure.NoWillingHeir => nameof(LineageFailure.NoWillingHeir),
        LineageFailure.NoPlayableHeir => nameof(LineageFailure.NoPlayableHeir),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private static LineageFailure ParseLineageFailure(string value) => value switch
    {
        nameof(LineageFailure.NoHeirs) => LineageFailure.NoHeirs,
        nameof(LineageFailure.NoWillingHeir) => LineageFailure.NoWillingHeir,
        nameof(LineageFailure.NoPlayableHeir) => LineageFailure.NoPlayableHeir,
        _ => throw new InvalidOperationException($"Unknown lineage_over_reason '{value}' in Game_State."),
    };

    // ------------------------------------------------------------------
    // Bus wiring & the attended-game day loop
    // ------------------------------------------------------------------

    public void AttachTo(EventBus bus)
    {
        _bus = bus;
        bus.Subscribe(_onDayAdvanced);
        bus.Subscribe(_onSeasonRolledOver);
        bus.Subscribe(_onChildConceptionRequested);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onDayAdvanced);
        bus.Unsubscribe(_onSeasonRolledOver);
        bus.Unsubscribe(_onChildConceptionRequested);
        if (ReferenceEquals(_bus, bus))
        {
            _bus = null;
        }
    }

    /// <summary>
    /// Services a gritty event's conceive_child request off the bus
    /// (marriage_and_conception.md §4.3) — the Narrative applier cannot call
    /// <see cref="ConceiveChild"/> directly (it never references Baseball), so
    /// the request rides a Core event. The co-parent id came from the LIVE
    /// relationship graph and is passed straight through, making conception
    /// independent of the day-cadence relationship flush.
    /// </summary>
    private void OnChildConceptionRequested(ChildConceptionRequestedEvent e)
    {
        // Deferred dispatch means the avatar can change (succession) between
        // publish and drain — a stale request for a now-retired avatar is
        // dropped, never applied to the wrong bloodline.
        if (_avatarPlayerId is null
            || !string.Equals(e.ParentAvatarId, _avatarPlayerId, StringComparison.Ordinal))
        {
            return;
        }
        if (!_players.TryGetById(_avatarPlayerId, out PlayerRow avatar))
        {
            throw new InvalidOperationException($"Avatar '{_avatarPlayerId}' has no Players row — save is corrupt.");
        }

        // Bloodline continuity: the avatar's surname; a generated first name
        // (a naming UI is a future ChildBornEvent consumer, §9).
        string firstName = LeagueGenerator.GenerateFirstName(ref _rng);
        string childId = ConceiveChild(firstName, avatar.LastName, birthAge: 0, e.PartnerId);

        string? partnerFirstName = null;
        string? partnerLastName = null;
        if (e.PartnerId is not null)
        {
            if (!_players.TryGetById(e.PartnerId, out PlayerRow partner))
            {
                throw new InvalidOperationException($"Partner '{e.PartnerId}' has no Players row — save is corrupt.");
            }
            partnerFirstName = partner.FirstName;
            partnerLastName = partner.LastName;
        }
        _pendingBirths.Enqueue(new BirthAnnouncement(childId, firstName, avatar.LastName, partnerFirstName, partnerLastName, e.Day));
        _bus?.Publish(new ChildBornEvent(childId, _avatarPlayerId, e.PartnerId, e.Day));
    }

    /// <summary>The birth-notification UI's feed: FIFO, one entry per successfully-serviced <see cref="ChildConceptionRequestedEvent"/> (a dropped stale request never enqueues one).</summary>
    public bool TryDequeuePendingBirth(out BirthAnnouncement announcement)
    {
        if (_pendingBirths.Count == 0)
        {
            announcement = default;
            return false;
        }
        announcement = _pendingBirths.Dequeue();
        return true;
    }

    private void OnSeasonRolledOver(SeasonRolledOverEvent e)
    {
        // §5.5: the world ages one year per season, avatar or not — one
        // set-based statement, its own implicit transaction (the rollover
        // event dispatches after the calendar tick's batch committed).
        _players.AgeAllPlayers();

        if (!HasAvatar || IsLineageOver)
        {
            return;
        }

        if (AutopilotSuccession)
        {
            RunSuccessionCheck();
            return;
        }

        if (_hasPendingSuccession)
        {
            // A previous succession choice was never answered before this
            // rollover came around again — resolve it fresh against LIVE
            // state rather than replay the stale candidate snapshot (a
            // candidate's eligibility could have changed meanwhile, and
            // Succeed() throws loudly on an ineligible heir). This IS this
            // rollover's succession handling for this tick — age/health only
            // ever move toward retirement, so the fresh check can't come back
            // NotTriggered, and a freshly-succeeded heir isn't re-checked
            // again in the same tick.
            _hasPendingSuccession = false;
            RunSuccessionCheck();
            return;
        }

        SuccessionOutcome outcome = EvaluateSuccession(_pendingHeirSnapshot);
        switch (outcome.Kind)
        {
            case SuccessionOutcomeKind.Succeeded:
                _hasPendingSuccession = true;
                break;
            case SuccessionOutcomeKind.GameOver:
                PersistGameOver(outcome.Reason);
                break;
        }
        LastSuccession = outcome;
    }

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

        if (AutopilotAttendedGames || IsAvatarAbsentOn(e.Day))
        {
            ResolvePendingWithAutopilot();
        }
    }

    /// <summary>True when the avatar is benched (absent, not merely rusty) on the given absolute day.</summary>
    public bool IsAvatarAbsentOn(long day) =>
        HasAvatar && Availability is not null
        && Availability.StateFor(_avatarPlayerId!, day) == SlotAvailability.Absent;

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
        var neutralPitcher = new NeutralPitcherPolicy();
        return PlayPendingGame(ref policy, ref neutralPitcher);
    }

    /// <summary>
    /// Two-policy form (v4): the pitcher policy supplies the avatar's pitch
    /// calls when the avatar is on the mound (pitcher careers); batter-only
    /// call sites use the single-policy overload.
    /// </summary>
    public MicroGameResult PlayPendingGame<TBatter, TPitcher>(ref TBatter batterPolicy, ref TPitcher pitcherPolicy)
        where TBatter : IBatterPolicy
        where TPitcher : IPitcherPolicy
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
            // Phase 8c: the micro-sim's availability caches are day-scoped and
            // it has no calendar of its own — refresh for the pending game's
            // day (covers the UI path, the autopilot path, and a stale
            // pending game forfeited a day late). Version+day-gated, so this
            // is a no-op on the common no-absence path.
            _micro.RefreshAvailability(_pending.AbsoluteDay);
            MicroGameResult result = _micro.PlayGame(
                _pending.HomeTeamId, _pending.AwayTeamId, _avatarSlot,
                ref batterPolicy, ref pitcherPolicy, ref _rng);
            _micro.FlushGame(_pending.SeasonYear, checked((int)_pending.AbsoluteDay));
            // The flush wrote fresh counting stats for this tier; recompute
            // its rate columns now (own batch, after — never inside — the
            // flush batch) so the dashboard's post-game read isn't stale.
            if (Normalizer is not null
                && _baseball.TryGetTeamTier(_pending.HomeTeamId, out LeagueTier tier))
            {
                Normalizer.NormalizeSeason(_pending.SeasonYear, tier);
            }
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
