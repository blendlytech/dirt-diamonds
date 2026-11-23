using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// The background-league macro-sim (design doc §7/§9, Milestone M1). Bulk-loads
/// rosters up front (Players ⋈ Player_Ratings), plays a round-robin schedule of
/// <see cref="AtBatResolver"/>-driven games in response to
/// <see cref="DayAdvancedEvent"/> off the bus, accumulates counting stats in
/// preallocated struct arrays, and flushes them additively at the end of each
/// 7-day cycle in its OWN batch transaction — never the calendar tick's. Rate
/// columns are denormalized by <see cref="StatsNormalizer"/> after each flush.
/// When a career avatar exists, the attended team's games are skipped here and
/// owned by the micro-sim (see <see cref="SetAttendedTeam"/>).
///
/// Zero-GC contract: a simulated game day touches only preallocated arrays and
/// stack memory. Heap work happens at Initialize (load) and FlushSeason
/// (database writes), both outside the per-day hot path's steady state.
///
/// This class never references the Life sim; its only inputs are Core events
/// and the database.
/// </summary>
public sealed class LeagueSimulator
{
    /// <summary>Stats (and the rest ledger) flush every this many season days — the shipped 7-day cadence (every Sunday).</summary>
    internal const int FlushCadenceDays = 7;

    // §7 discretionary-advance calibration knobs (tuning order step 4). The
    // canonical values live in BaseOutAdvancement, shared with the micro-sim;
    // these aliases keep existing references stable.
    public const double SingleScoresFrom2nd = BaseOutAdvancement.SingleScoresFrom2nd;
    public const double DoubleScoresFrom1st = BaseOutAdvancement.DoubleScoresFrom1st;

    /// <summary>Flag name the Gritty Event system (Phase 7) writes; always inactive until then.</summary>
    public const string PedActiveFlagName = "ped_active";

    // §6 post-game PED costs — calibration constants owned by the drug-system
    // risk/reward tuning, applied per game played under the influence.
    public const int PedHealthCostPerGame = 1;
    public const int PedDetectionRiskPerGame = 2;

    private readonly DatabaseManager _db;
    private readonly BaseballQueries _queries;
    private readonly StatsNormalizer _normalizer;
    private readonly Action<DayAdvancedEvent> _onDayAdvanced;
    private readonly Action<SeasonRolledOverEvent> _onSeasonRolledOver;
    private RngState _rng;

    // Roster-slot-indexed parallel arrays, filled once at Initialize.
    private RosterPlayerRow[] _roster = Array.Empty<RosterPlayerRow>();
    private BatterRatings[] _batterRatings = Array.Empty<BatterRatings>();
    private PitcherRatings[] _pitcherRatings = Array.Empty<PitcherRatings>();
    private bool[] _pedActive = Array.Empty<bool>();
    private int[] _pedGamesPlayed = Array.Empty<int>();
    private BattingLine[] _batting = Array.Empty<BattingLine>();
    private PitchingLine[] _pitching = Array.Empty<PitchingLine>();

    // 13-4 (brief D4-A): the postseason's private, preallocated buffers. A
    // postseason game's lines land here and are discarded (no postseason
    // player lines), and its PED games count here, flushed in the postseason
    // day's own batch, so the regular-season accumulators, PED counters and
    // result queue never see a postseason game (Math record §10.1 Q7
    // channels 1, 5, 8).
    private BattingLine[] _postBatting = Array.Empty<BattingLine>();
    private PitchingLine[] _postPitching = Array.Empty<PitchingLine>();
    private int[] _postPedGamesPlayed = Array.Empty<int>();
    // The last postseason game's two starts and its diagnostic increments, so
    // a rolled-back postseason batch can put the rest ledger and the counters
    // back exactly (Q7 ch. 3 and 10; 13-4 review N1).
    private readonly PitcherRestLedger.StartUndo[] _postStartUndo = new PitcherRestLedger.StartUndo[2];
    private int _postStarterCount;
    private int _postShortRestBefore;
    private int _postBullpenBefore;

    // 13-4 (brief D1-A): Team_Seasons rides every results flush batch.
    private readonly TeamSeasonsWriter _teamSeasons;

    // Team-indexed (0..TeamCount-1, ordered by team_id), sized by
    // RosterCapacity, looped to the team's own counts (§5.2): the lineup in
    // batting order, starters and relievers in StarterSelection quality
    // order, plus the §3 precomputed team-defense rating.
    private int _teamCount;
    private int[] _lineupSlots = Array.Empty<int>();
    private int[] _starterSlots = Array.Empty<int>();
    private int[] _starterCount = Array.Empty<int>();
    private int[] _relieverSlots = Array.Empty<int>();
    private int[] _relieverCount = Array.Empty<int>();
    private byte[] _teamDefense = Array.Empty<byte>();
    private int _regulationInnings = 9;
    private int _minRestDays;

    // Slice 13-3 (league_restructure.md §3.1): the persisted schedule. The
    // current season plays; the next one is loaded too so the calendar can
    // look across New Year. Results of played games wait for the flush (D4).
    private readonly ScheduleQueries _scheduleQueries;
    private readonly GameStateQueries _gameState;
    private readonly List<ScheduledGameRow> _scheduleScratch = new();
    private SeasonSchedule _currentSeason;
    private SeasonSchedule _nextSeason;
    private readonly long[] _resultGameIds;
    private readonly int[] _resultHomeScores;
    private readonly int[] _resultAwayScores;
    // 13-5 (brief §4 item 5): the result queue's status lane (1 final, 2
    // forfeit) and the forfeits' events, both written and published by the
    // Sunday flush (Amendment 5c). Preallocated to a season's games, so a
    // forfeit day allocates nothing.
    private readonly int[] _resultStatuses;
    private readonly List<GameForfeitedEvent> _pendingForfeits;
    private int _resultCount;
    private EventBus? _bus;

    /// <summary>
    /// Last-start days for §6 starter selection (slice 13-2). Each sim owns a
    /// private ledger by default (every harness fixture); GameManager assigns
    /// ONE shared instance to every tier sim and the micro-sim before
    /// Initialize, so the attended game and the background league agree on rest.
    /// </summary>
    public PitcherRestLedger RestLedger { get; set; } = new();

    /// <summary>Teams in this tier's league (set at Initialize).</summary>
    public int TeamCount => _teamCount;

    /// <summary>Starts made on short rest (the §6 fallback), for the 13-2 harness.</summary>
    internal int ShortRestStarts { get; private set; }

    /// <summary>Games no starter could take (a reliever opened, §6), for the 13-2 harness.</summary>
    internal int BullpenGames { get; private set; }

    /// <summary>Lineup starters playing out of position across the league (13-2 brief D3), for the harness.</summary>
    internal int OutOfPositionStarters { get; private set; }

    /// <summary>Postseason short-rest starts, kept apart so the regular-season counter never moves (Q7 channel 10).</summary>
    internal int PostseasonShortRestStarts { get; private set; }

    /// <summary>Postseason bullpen games (a reliever opened), kept apart from <see cref="BullpenGames"/> (Q7 channel 10).</summary>
    internal int PostseasonBullpenGames { get; private set; }

    /// <summary>Game-day lineups rebuilt around an ineligible starter (13-5; 0 in the guard world, D1-A channel 2).</summary>
    internal int LineupRebuildCount { get; private set; }

    /// <summary>Regular-season macro forfeits queued since construction (13-5 harness visibility).</summary>
    internal int ForfeitCount { get; private set; }

    private bool _initialized;

    /// <summary>
    /// Rivalry intensity source (Phase 6), same optional-attachment pattern as
    /// <see cref="MicroGame.FeedSink"/>: null — the default for every existing
    /// harness path — leaves the per-PA hot path bit-identical to the
    /// pre-rivalry code (a single false branch), so the M1 season lines cannot
    /// move unless a rivalry actually exists.
    /// </summary>
    public RivalryLedger? Rivalries;

    /// <summary>
    /// Person-layer lever source (HS-6, person-layer doc §6), the same
    /// optional-attachment pattern as <see cref="Rivalries"/>: null — the
    /// default for every existing harness path — skips the load entirely, and
    /// an absent Player_Person row reads as the neutral 50s; both bake
    /// bit-identical rating arrays (<see cref="PersonEffects.Bake"/> with a 0
    /// delta is the exact tier-only shift). Set BEFORE <see cref="Initialize"/>;
    /// every re-Initialize beat (promotion sweep, development pass,
    /// succession) re-reads it, which is what makes the bake season-stable.
    /// </summary>
    public PersonQueries? Persons;

    // Flat slot×slot intensity cache rebuilt only when the ledger's Version
    // moves (rivalries change at day granularity at most; PAs read one byte).
    private byte[] _rivalrySlots = Array.Empty<byte>();
    private Dictionary<string, int>? _slotByPlayerId;
    private readonly List<RivalryPair> _rivalryScratch = new();
    private int _rivalryVersionSeen = -1;
    private bool _hasRivalries;

    /// <summary>
    /// Roster-availability source (Phase 8c), same optional-attachment pattern
    /// as <see cref="Rivalries"/>: null — the default for every existing
    /// harness path — leaves the per-PA hot path bit-identical to the
    /// pre-availability code (a single false branch), so the M1 season lines
    /// cannot move unless an absence actually exists.
    /// </summary>
    public AvailabilityLedger? Availability;

    // Per-slot availability caches rebuilt when the ledger's Version OR the
    // day moves (absences expire by the calendar, not by an event). 13-5:
    // there is no call-up any more (spec §7, no minor leagues) — an absent
    // player is a hole, so these caches gate who plays, never who shadows.
    private bool[] _slotUnavailable = Array.Empty<bool>();
    private byte[] _slotRustPenalty = Array.Empty<byte>();
    private readonly List<AbsenceEntry> _absenceScratch = new();
    private int _absenceVersionSeen = -1;
    private long _absenceDaySeen = -1;
    private bool _hasAbsences;

    /// <summary>
    /// The Pro Sunday lock's designations (slice 13-5, spec §7), the same
    /// optional-attachment pattern as <see cref="Availability"/>: null — and
    /// every HS/College player, and every day outside lock coverage — reads
    /// as active. An inactive player is ineligible exactly like an absent one.
    /// </summary>
    public RosterManager? Designations;

    // Per-slot inactive cache, rebuilt when the manager's Version or the day
    // moves (a lock's rows lapse by the calendar, Amendment 1).
    private bool[] _slotInactive = Array.Empty<bool>();
    private readonly List<string> _inactiveScratch = new();
    private int _designationVersionSeen = -1;
    private long _designationDaySeen = -1;
    private bool _hasInactive;

    // 13-5 (brief §4 item 4): the game-day lineup. Each team plays its
    // Initialize lineup unless one of those nine is ineligible today; then
    // the allocation-free LineupBuilder twin rebuilds it from the team's
    // eligible position players (kept in Build's order since Initialize) and
    // the game's defense byte re-bakes from the retained per-slot teamwork
    // (Amendment 5a). PlayHalfInning reads only the game lineup.
    private LineupCandidate[] _slotCandidate = Array.Empty<LineupCandidate>();
    private int[] _slotTeamwork = Array.Empty<int>();
    private int[] _positionOrder = Array.Empty<int>();
    private int[] _positionCount = Array.Empty<int>();
    private int[] _gameLineupSlots = Array.Empty<int>();
    private byte[] _gameDefense = Array.Empty<byte>();
    private readonly LineupCandidate[] _twinScratch = new LineupCandidate[RosterCapacity.MaxPositionPlayers];
    private readonly int[] _twinSlots = new int[RosterCapacity.Lineup];
    private readonly sbyte[] _twinPositions = new sbyte[RosterCapacity.Lineup];
    private int _tierDefenseDelta;

    /// <summary>
    /// Purchased-gear source (Phase 8e), same optional-attachment pattern as
    /// <see cref="Rivalries"/>: null — the default for every existing harness
    /// path — leaves the per-PA hot path bit-identical to the pre-equipment
    /// code (a single false branch), so the M1 season lines cannot move
    /// unless someone actually owns upgraded gear.
    /// </summary>
    public EquipmentLedger? Equipment;

    // Per-slot gear-boost cache rebuilt only when the ledger's Version moves
    // (gear never expires by calendar, unlike absences); PAs read one byte.
    private byte[] _slotGearBoost = Array.Empty<byte>();
    private readonly List<EquipmentEntry> _equipmentScratch = new();
    private int _equipmentVersionSeen = -1;
    private bool _hasEquipment;

    /// <summary>Season year with simulated-but-unflushed games; 0 = clean.</summary>
    private int _unflushedSeasonYear;

    /// <summary>
    /// Team index whose games the career driver owns (-1 = none). Every pairing
    /// containing this team is skipped by the macro sim — the attended-game
    /// micro-sim plays and flushes it instead, recording its starters in the
    /// shared rest ledger.
    /// </summary>
    private int _attendedTeamIndex = -1;

    private int[] _teamIds = Array.Empty<int>();

    /// <summary>
    /// The ladder tier this instance simulates (Phase 9a). One simulator per
    /// tier; each bulk-loads only its own league and bakes its tier's
    /// <see cref="TierEffects"/> deltas into the rating arrays at Initialize.
    /// Defaults to Pro (the former MLB rung, schema v14) so every pre-9a
    /// construction site and harness fixture keeps its exact behavior — the Pro
    /// delta vector is all-zero by contract.
    /// </summary>
    public LeagueTier Tier { get; }

    public LeagueSimulator(DatabaseManager db, BaseballQueries queries, StatsNormalizer normalizer, RngState rng,
        LeagueTier tier = LeagueTier.Pro)
    {
        _db = db;
        _queries = queries;
        _normalizer = normalizer;
        _rng = rng;
        Tier = tier;
        // Allocate the handler delegates once so Attach/Detach never churn.
        _onDayAdvanced = OnDayAdvanced;
        _onSeasonRolledOver = OnSeasonRolledOver;
        _scheduleQueries = new ScheduleQueries(db);
        _gameState = new GameStateQueries(db);
        _currentSeason = new SeasonSchedule(tier);
        _nextSeason = new SeasonSchedule(tier);
        int capacity = TierSeasonCalendar.GameDays(tier).Length * RosterCapacity.MaxTeamsPerTier / 2;
        _resultGameIds = new long[capacity];
        _resultHomeScores = new int[capacity];
        _resultAwayScores = new int[capacity];
        _resultStatuses = new int[capacity];
        _pendingForfeits = new List<GameForfeitedEvent>(capacity);
        _teamSeasons = new TeamSeasonsWriter(new PlayoffQueries(db));
    }

    /// <summary>This tier's team ids in team-index order (set at Initialize).</summary>
    internal ReadOnlySpan<int> TeamIds => _teamIds;

    /// <summary>A copy of the regular-season stream's state, for the 13-4 invariance checks (Q7 channel 2).</summary>
    internal RngState RngSnapshot => _rng;

    /// <summary>Regular-season results waiting for the flush, for the 13-4 invariance checks (Q7 channel 8).</summary>
    internal int PendingResultCount => _resultCount;

    /// <summary>The season with simulated-but-unflushed games (0 = clean), for the 13-4 invariance checks (Q7 channel 8).</summary>
    internal int UnflushedSeasonYear => _unflushedSeasonYear;

    /// <summary>The team's starter at a quality rank (0 = the ace), for the 13-4 no-repeat fixtures.</summary>
    internal string StarterId(int teamId, int qualityRank) =>
        _roster[_starterSlots[Array.IndexOf(_teamIds, teamId) * RosterCapacity.MaxStarters + qualityRank]].PlayerId;

    /// <summary>The team's reliever at a quality rank (0 = the first opener), for the 13-4 no-repeat fixtures.</summary>
    internal string RelieverId(int teamId, int qualityRank) =>
        _roster[_relieverSlots[Array.IndexOf(_teamIds, teamId) * RosterCapacity.MaxRelievers + qualityRank]].PlayerId;

    /// <summary>The team's starter and reliever counts, for the 13-4 no-repeat fixtures.</summary>
    internal (int Starters, int Relievers) StaffCounts(int teamId)
    {
        int team = Array.IndexOf(_teamIds, teamId);
        return (_starterCount[team], _relieverCount[team]);
    }

    /// <summary>The save's schedule seed (<see cref="SeasonScheduler.ResolveSeed"/>): every 13-4 stream and coin keys on it.</summary>
    public ulong ScheduleSeed => SeasonScheduler.ResolveSeed(_gameState);

    /// <summary>The loaded current season's schedule (empty until <see cref="PrepareSeason"/>).</summary>
    public SeasonSchedule CurrentSeason => _currentSeason;

    /// <summary>
    /// Makes <paramref name="seasonYear"/> the playing season (13-3 brief D1):
    /// ensures it and the next season are persisted (generated once, never
    /// regenerated) and loads both. The day handler calls it when the season
    /// year changes; GameManager calls it at boot. A no-op when already current.
    /// </summary>
    public void PrepareSeason(int seasonYear)
    {
        if (_currentSeason.SeasonYear == seasonYear)
        {
            return;
        }
        if (_teamIds.Length == 0)
        {
            throw new InvalidOperationException("LeagueSimulator.PrepareSeason needs Initialize() first (the team-index order).");
        }
        SeasonScheduler.EnsureSeasons(_db, _queries, _scheduleQueries, _gameState, Tier, seasonYear);
        if (_nextSeason.SeasonYear == seasonYear)
        {
            (_currentSeason, _nextSeason) = (_nextSeason, _currentSeason);
            _currentSeason.Reindex(_teamIds);
        }
        else
        {
            _currentSeason.Load(_scheduleQueries, seasonYear, _teamIds, _scheduleScratch);
        }
        if (_nextSeason.SeasonYear != seasonYear + 1)
        {
            _nextSeason.Load(_scheduleQueries, seasonYear + 1, _teamIds, _scheduleScratch);
        }
    }

    /// <summary>
    /// The scheduled game <paramref name="teamId"/> plays on the day, from the
    /// loaded current or next season; false on off days and for any season
    /// not loaded. Pure memory reads: the career driver claims the avatar's
    /// game and the calendar looks ahead through this, never the database.
    /// </summary>
    public bool TryGetScheduledGame(int seasonYear, int dayOfSeason, int teamId,
        out long gameId, out int homeTeamId, out int awayTeamId)
    {
        SeasonSchedule? season = _currentSeason.SeasonYear == seasonYear ? _currentSeason
            : _nextSeason.SeasonYear == seasonYear ? _nextSeason : null;
        if (season is not null && season.TryFindGame(dayOfSeason, teamId, out int game))
        {
            gameId = season.GameId(game);
            homeTeamId = season.HomeTeamId(game);
            awayTeamId = season.AwayTeamId(game);
            return true;
        }
        gameId = 0;
        homeTeamId = 0;
        awayTeamId = 0;
        return false;
    }

    /// <summary>Season counting stats for one batter — flushed to Batting_Stats.</summary>
    private struct BattingLine
    {
        public int Pa, Ab, H, Doubles, Triples, Hr, Bb, So, Rbi;
    }

    /// <summary>Season counting stats for one pitcher — flushed to Pitching_Stats.</summary>
    private struct PitchingLine
    {
        public int G, Gs, W, L, Outs, HAllowed, Er, Bb, So;
    }

    // ------------------------------------------------------------------
    // Load & wiring
    // ------------------------------------------------------------------

    /// <summary>
    /// One-time bulk load: teams, the roster join, and active PED flags. The
    /// per-PA hot path never touches the database after this returns.
    /// </summary>
    public void Initialize()
    {
        var teams = new List<TeamRow>(RosterCapacity.MaxTeamsPerTier);
        _queries.LoadTeamsByTier(Tier, teams);
        if (teams.Count < 2 || teams.Count % 2 != 0 || teams.Count > RosterCapacity.MaxTeamsPerTier)
        {
            throw new InvalidOperationException(
                $"League tier {Tier} needs an even team count from 2 to {RosterCapacity.MaxTeamsPerTier}; " +
                $"found {teams.Count}. Run LeagueGenerator first.");
        }
        _teamCount = teams.Count;
        _regulationInnings = TierRules.RegulationInnings(Tier);
        _minRestDays = TierRules.MinRestDays(Tier);
        RestLedger.HydrateOnce(_queries);

        var rosterRows = new List<RosterPlayerRow>(_teamCount * RosterCapacity.MaxRoster);
        _queries.LoadRosterByTier(Tier, rosterRows);
        _roster = rosterRows.ToArray();

        var pedIds = new List<string>();
        _queries.LoadActiveFlagPlayerIds(PedActiveFlagName, pedIds);
        var pedSet = new HashSet<string>(pedIds, StringComparer.Ordinal);

        // 13-2 (brief D3): who plays is a legal defense from Player_Fielding.
        var fieldingRows = new List<PlayerFieldingRow>();
        _queries.LoadAllFielding(fieldingRows);
        var fieldingById = new Dictionary<string, PlayerFieldingRow>(fieldingRows.Count, StringComparer.Ordinal);
        foreach (PlayerFieldingRow f in fieldingRows)
        {
            fieldingById[f.PlayerId] = f;
        }

        var teamIndexById = new Dictionary<int, int>(_teamCount);
        _teamIds = new int[_teamCount];
        for (int t = 0; t < teams.Count; t++)
        {
            _teamIds[t] = teams[t].TeamId;
            teamIndexById.Add(teams[t].TeamId, t);
        }

        int slots = _roster.Length;
        _rivalrySlots = new byte[slots * slots];
        _slotByPlayerId = new Dictionary<string, int>(slots, StringComparer.Ordinal);
        _rivalryVersionSeen = -1; // roster changed — force a cache rebuild
        _hasRivalries = false;
        _slotUnavailable = new bool[slots];
        _slotRustPenalty = new byte[slots];
        _absenceVersionSeen = -1; // roster changed — force a cache rebuild
        _absenceDaySeen = -1;
        _hasAbsences = false;
        _slotInactive = new bool[slots];
        _designationVersionSeen = -1; // roster changed — force a cache rebuild
        _designationDaySeen = -1;
        _hasInactive = false;
        _slotGearBoost = new byte[slots];
        _equipmentVersionSeen = -1; // roster changed — force a cache rebuild
        _hasEquipment = false;
        _batterRatings = new BatterRatings[slots];
        _pitcherRatings = new PitcherRatings[slots];
        _pedActive = new bool[slots];
        _pedGamesPlayed = new int[slots];
        // 13-5: one line per roster slot. The discard line the retired
        // call-up accumulated into is gone — only real players ever play.
        _batting = new BattingLine[slots];
        _pitching = new PitchingLine[slots];
        _postBatting = new BattingLine[slots];
        _postPitching = new PitchingLine[slots];
        _postPedGamesPlayed = new int[slots];
        _slotCandidate = new LineupCandidate[slots];
        _slotTeamwork = new int[slots];
        _positionOrder = new int[_teamCount * RosterCapacity.MaxPositionPlayers];
        _positionCount = new int[_teamCount];
        _gameLineupSlots = new int[_teamCount * RosterCapacity.Lineup];
        _gameDefense = new byte[_teamCount];
        _lineupSlots = new int[_teamCount * RosterCapacity.Lineup];
        _starterSlots = new int[_teamCount * RosterCapacity.MaxStarters];
        _starterCount = new int[_teamCount];
        _relieverSlots = new int[_teamCount * RosterCapacity.MaxRelievers];
        _relieverCount = new int[_teamCount];
        _teamDefense = new byte[_teamCount];

        var positionPlayers = new List<LineupCandidate>[_teamCount];
        var starters = new List<int>[_teamCount];
        var relievers = new List<int>[_teamCount];
        for (int t = 0; t < _teamCount; t++)
        {
            positionPlayers[t] = new List<LineupCandidate>(RosterCapacity.MaxPositionPlayers);
            starters[t] = new List<int>(RosterCapacity.MaxStarters);
            relievers[t] = new List<int>(RosterCapacity.MaxRelievers);
        }
        // 13-5 (Amendment 5a): per-slot teamwork is retained past Initialize,
        // so a reshuffled game-day lineup re-bakes its defense byte exactly.
        int[] teamworkBySlot = _slotTeamwork;

        // HS-6 (person-layer doc §6): person levers load once here and bake
        // with the tier deltas below. No Persons source, or no Player_Person
        // row, reads as the neutral 50s — a 0 person delta collapses to the
        // exact tier-only bake, so every pre-HS-6 world (and the all-neutral
        // guard world) keeps byte-identical rating arrays. Cold load, never
        // per PA.
        Dictionary<string, PersonRow>? personRows = null;
        if (Persons is not null)
        {
            personRows = new Dictionary<string, PersonRow>(slots, StringComparer.Ordinal);
            Persons.LoadAll(personRows);
        }

        // Phase 9a: the tier's league-environment deltas bake into the rating
        // arrays here, once — the per-PA hot path is untouched, and rivalry/PED
        // effects layer on top of the tier-shifted values exactly as they
        // layered on the raw ones. Pro's vector is all-zero, so a Pro world's
        // arrays are bit-identical to the pre-tier code.
        TierRatingDeltas tierDeltas = TierEffects.For(Tier);
        _tierDefenseDelta = tierDeltas.Defense;

        for (int i = 0; i < slots; i++)
        {
            ref readonly RosterPlayerRow row = ref _roster[i];
            if (!teamIndexById.TryGetValue(row.TeamId, out int team))
            {
                throw new InvalidOperationException(
                    $"Player {row.PlayerId} references team_id {row.TeamId} with no Teams row (query-layer FK).");
            }

            _slotByPlayerId.Add(row.PlayerId, i);
            bool ped = pedSet.Contains(row.PlayerId);
            _pedActive[i] = ped;
            PersonRatingDeltas person = default;
            if (personRows is not null && personRows.TryGetValue(row.PlayerId, out PersonRow personRow))
            {
                person = PersonEffects.For(in personRow);
            }
            teamworkBySlot[i] = person.Teamwork;
            _batterRatings[i] = new BatterRatings(
                PersonEffects.Bake(row.BatPower, tierDeltas.BatPower, person.Confidence),
                PersonEffects.Bake(row.BatContact, tierDeltas.BatContact, person.Happiness),
                TierEffects.Shift(row.BatDiscipline, tierDeltas.BatDiscipline), ped);
            _pitcherRatings[i] = new PitcherRatings(
                PersonEffects.Bake(row.PitStuff, tierDeltas.PitStuff, person.Confidence),
                PersonEffects.Bake(row.PitControl, tierDeltas.PitControl, person.Happiness),
                (byte)row.PitStamina);

            if (row.IsPitcher)
            {
                // Starters and relievers both: §6 lets a reliever open when no
                // starter can go. The macro-sim stays complete-game (its §8
                // calibration is pinned to that shape), so relievers pitch
                // here only as bullpen-game openers.
                switch (row.Role)
                {
                    case PitcherRole.Starter:
                        starters[team].Add(i);
                        break;
                    case PitcherRole.Reliever:
                        relievers[team].Add(i);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Pitcher {row.PlayerId} has no Pitcher_Roles row — the generation invariant is broken.");
                }
            }
            else
            {
                fieldingById.TryGetValue(row.PlayerId, out PlayerFieldingRow fielding);
                bool hasFielding = fielding.PlayerId is not null;
                var candidate = new LineupCandidate(
                    i, row.PlayerId, row.BatPower + row.BatContact + row.BatDiscipline,
                    hasFielding ? fielding.DefensivePosition : null,
                    hasFielding ? fielding.SecondaryPosition : null);
                _slotCandidate[i] = candidate;
                positionPlayers[team].Add(candidate);
            }
        }

        OutOfPositionStarters = 0;
        for (int t = 0; t < _teamCount; t++)
        {
            CheckCapacity(teams[t], positionPlayers[t].Count, starters[t].Count, relievers[t].Count);

            // 13-5: the team's position players in Build's order, kept for
            // the game-day twin (load time; the game day never sorts).
            var ordered = new List<LineupCandidate>(positionPlayers[t]);
            ordered.Sort(static (a, b) => LineupBuilder.CompareOrder(in a, in b));
            _positionCount[t] = ordered.Count;
            for (int k = 0; k < ordered.Count; k++)
            {
                _positionOrder[t * RosterCapacity.MaxPositionPlayers + k] = ordered[k].Slot;
            }

            LineupResult lineup = LineupBuilder.Build(positionPlayers[t]);
            OutOfPositionStarters += lineup.OutOfPosition;
            int fieldingSum = 0;
            int teamworkPointsSum = 0;
            for (int k = 0; k < RosterCapacity.Lineup; k++)
            {
                int slot = lineup.Slots[k];
                _lineupSlots[t * RosterCapacity.Lineup + k] = slot;
                fieldingSum += _roster[slot].Fielding;
                teamworkPointsSum += teamworkBySlot[slot];
            }
            // §3: team defense = mean fielding of the lineup, rounded, then
            // tier-shifted (9a), then the HS-6 §6.2 teamwork lever (a zero
            // points sum is the exact pre-HS-6 defense byte).
            _teamDefense[t] = PersonEffects.BakeTeamDefense(
                fieldingSum, RosterCapacity.Lineup, tierDeltas.Defense, teamworkPointsSum);

            _starterCount[t] = RankPitchers(starters[t], _starterSlots, t * RosterCapacity.MaxStarters);
            _relieverCount[t] = RankPitchers(relievers[t], _relieverSlots, t * RosterCapacity.MaxRelievers);
        }

        // A re-Initialize (development pass, succession) keeps the loaded
        // schedules; only the team-index order is re-mapped.
        if (_currentSeason.SeasonYear != 0)
        {
            _currentSeason.Reindex(_teamIds);
        }
        if (_nextSeason.SeasonYear != 0)
        {
            _nextSeason.Reindex(_teamIds);
        }
        _initialized = true;
    }

    /// <summary>
    /// §5.1/§5.2 capacity guard (slice 13-2): a team must field nine and carry
    /// at least one pitcher, and may not exceed any capacity. Loud, never a
    /// silent truncation.
    /// </summary>
    internal static void CheckCapacity(in TeamRow team, int positionPlayers, int starters, int relievers)
    {
        if (positionPlayers < RosterCapacity.Lineup || starters + relievers == 0
            || positionPlayers > RosterCapacity.MaxPositionPlayers
            || starters > RosterCapacity.MaxStarters || relievers > RosterCapacity.MaxRelievers)
        {
            throw new InvalidOperationException(
                $"Team {team.TeamId} ({team.Abbreviation}) carries {positionPlayers} position players, " +
                $"{starters} starters and {relievers} relievers; the sims need {RosterCapacity.Lineup}–" +
                $"{RosterCapacity.MaxPositionPlayers} position players, at least one pitcher, at most " +
                $"{RosterCapacity.MaxStarters} starters and {RosterCapacity.MaxRelievers} relievers (§5.2).");
        }
    }

    /// <summary>Writes <paramref name="pitchers"/> into a slot table in StarterSelection quality order; returns the count.</summary>
    private int RankPitchers(List<int> pitchers, int[] table, int offset)
    {
        pitchers.Sort((a, b) => StarterSelection.CompareQuality(
            in _pitcherRatings[a], _roster[a].PlayerId, in _pitcherRatings[b], _roster[b].PlayerId));
        for (int k = 0; k < pitchers.Count; k++)
        {
            table[offset + k] = pitchers[k];
        }
        return pitchers.Count;
    }

    /// <summary>
    /// Hands every game of <paramref name="teamId"/> to the career driver: the
    /// macro sim stops playing (and stat-flushing) that team's pairings. The
    /// micro-sim's per-game additive flush owns them from here on.
    /// </summary>
    public void SetAttendedTeam(int teamId)
    {
        int index = Array.IndexOf(_teamIds, teamId);
        if (index < 0)
        {
            throw new ArgumentException($"Unknown team_id {teamId} (or Initialize() has not run).");
        }
        _attendedTeamIndex = index;
    }

    public void ClearAttendedTeam() => _attendedTeamIndex = -1;

    public void AttachTo(EventBus bus)
    {
        _bus = bus;
        bus.Subscribe(_onDayAdvanced);
        bus.Subscribe(_onSeasonRolledOver);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onDayAdvanced);
        bus.Unsubscribe(_onSeasonRolledOver);
        if (ReferenceEquals(_bus, bus))
        {
            _bus = null;
        }
    }

    /// <summary>The bus a flush publishes its buffered forfeits on (13-5); harness drives that never attach can set one.</summary>
    internal EventBus? Bus
    {
        get => _bus;
        set => _bus = value;
    }

    // ------------------------------------------------------------------
    // 13-5: roster reads for the Sunday lock and the tools (RosterManager)
    // ------------------------------------------------------------------

    /// <summary>The team index of a team id in this league, or -1.</summary>
    internal int IndexOfTeam(int teamId) => Array.IndexOf(_teamIds, teamId);

    /// <summary>The team's position players' roster slots in <see cref="LineupBuilder"/> order (bat score ↓, id ↑).</summary>
    internal ReadOnlySpan<int> PositionOrder(int team) =>
        _positionOrder.AsSpan(team * RosterCapacity.MaxPositionPlayers, _positionCount[team]);

    /// <summary>The team's starters' roster slots in <see cref="StarterSelection.CompareQuality"/> order (baked, C3).</summary>
    internal ReadOnlySpan<int> StarterRank(int team) =>
        _starterSlots.AsSpan(team * RosterCapacity.MaxStarters, _starterCount[team]);

    /// <summary>The team's relievers' roster slots in quality order.</summary>
    internal ReadOnlySpan<int> RelieverRank(int team) =>
        _relieverSlots.AsSpan(team * RosterCapacity.MaxRelievers, _relieverCount[team]);

    /// <summary>The team's Initialize lineup (batting order), for the D1-A starters-always-active channel.</summary>
    internal ReadOnlySpan<int> InitialLineup(int team) =>
        _lineupSlots.AsSpan(team * RosterCapacity.Lineup, RosterCapacity.Lineup);

    /// <summary>The last game's lineup a team took the field with (its Initialize lineup unless reshuffled).</summary>
    internal ReadOnlySpan<int> GameLineup(int team) =>
        _gameLineupSlots.AsSpan(team * RosterCapacity.Lineup, RosterCapacity.Lineup);

    /// <summary>A position-player slot's lineup candidate (raw bat score and Player_Fielding positions).</summary>
    internal LineupCandidate CandidateAt(int slot) => _slotCandidate[slot];

    /// <summary>A slot's baked pitcher ratings (tier and person), exactly what <see cref="StarterSelection.CompareQuality"/> ranks.</summary>
    internal PitcherRatings BakedPitcherAt(int slot) => _pitcherRatings[slot];

    internal string PlayerIdAt(int slot) => _roster[slot].PlayerId;

    internal int TeamIdAt(int team) => _teamIds[team];

    // ------------------------------------------------------------------
    // Bus handlers
    // ------------------------------------------------------------------

    private void OnDayAdvanced(DayAdvancedEvent e)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("LeagueSimulator received a day event before Initialize().");
        }
        // 13-3: a new season year loads (and, the first time, generates) its
        // persisted schedule; it never waits on the rollover handlers (D1).
        PrepareSeason(e.SeasonYear);

        // Only the tier's TierSeasonCalendar game days have games (§3).
        if (TierSeasonCalendar.IsGameDay(Tier, e.DayOfSeason))
        {
            RefreshAvailability(e.Day);
            SimulateGameDay(e.DayOfSeason, e.Day);
            _unflushedSeasonYear = e.SeasonYear;
        }

        // Flush every 7 season days (every Sunday: day 1 is a Monday). Every
        // tier's last regular game falls before a Sunday flush. Keeping the
        // database at most a week stale matters once the player's own stats
        // are on the line; additive upserts make the cadence safe. Runs on
        // off days too — FlushAccumulated row-guards on Pa/G > 0, so an empty
        // flush writes nothing.
        if (e.DayOfSeason % FlushCadenceDays == 0)
        {
            FlushAccumulated(e.SeasonYear);
        }
    }

    /// <summary>
    /// The day handler without a bus or clock: run_monte_carlo_batch drives
    /// pooled replicate seasons through exactly the path a published
    /// <see cref="DayAdvancedEvent"/> takes (13-3 brief D5), minus the clock's
    /// own per-day Game_State write.
    /// </summary>
    internal void ProcessDay(DayAdvancedEvent e) => OnDayAdvanced(e);

    private void OnSeasonRolledOver(SeasonRolledOverEvent e)
    {
        // Safety net: a simulator attached mid-season never saw the day-154
        // flush trigger. The guard on the year keeps this from re-flushing the
        // NEW season's day 1, which dispatches before this event on rollover day.
        if (_unflushedSeasonYear == e.PreviousSeasonYear)
        {
            FlushAccumulated(e.PreviousSeasonYear);
        }
    }

    /// <summary>
    /// Flushes any simulated-but-unwritten games immediately (the career
    /// driver calls this before re-initializing rosters around avatar
    /// creation, so no in-memory stats are lost to the reload).
    /// </summary>
    public void FlushPending()
    {
        if (_unflushedSeasonYear != 0)
        {
            FlushAccumulated(_unflushedSeasonYear);
        }
    }

    // ------------------------------------------------------------------
    // Schedule & game loop (allocation-free)
    // ------------------------------------------------------------------

    /// <summary>
    /// Plays one day's scheduled games from the loaded season (13-3). The
    /// attended team's game is skipped: that game is played in the
    /// micro-sim, which records its starters in the shared rest ledger and
    /// its own result. Internal so run_monte_carlo_batch can drive and
    /// profile it directly (after <see cref="PrepareSeason"/>); this overload
    /// treats the season day as the absolute day. Off days play nothing.
    /// </summary>
    internal void SimulateGameDay(int dayOfSeason) => SimulateGameDay(dayOfSeason, dayOfSeason);

    /// <summary>
    /// <paramref name="absoluteDay"/> is the calendar day §6 rest is counted
    /// on (the event's <c>Day</c>), so rest carries across seasons.
    /// </summary>
    internal void SimulateGameDay(int dayOfSeason, long absoluteDay)
    {
        RefreshDesignations(absoluteDay);
        RefreshRivalryCache();
        RefreshEquipmentCache();
        int count = _currentSeason.GamesOn(dayOfSeason, out int first);
        for (int g = first; g < first + count; g++)
        {
            int home = _currentSeason.HomeIndex(g);
            int away = _currentSeason.AwayIndex(g);
            if (home == _attendedTeamIndex || away == _attendedTeamIndex)
            {
                continue;
            }
            PlayGame(home, away, absoluteDay, _currentSeason.GameId(g));
        }
    }

    /// <summary>
    /// §6 starter selection for one team on one day (slice 13-2): the shared
    /// <see cref="StarterSelection"/> rule over this team's eligible starters
    /// in quality order, else an eligible reliever opens (a bullpen game). The
    /// pick's start is recorded in the rest ledger. 13-5: the caller has
    /// already passed <see cref="RosterRules.CanField"/>, so a pitcher always
    /// exists; there is no call-up. Allocation-free: stack scratch sized by
    /// capacity.
    /// </summary>
    private int SelectPitcher(int team, long day)
    {
        int starterCount = _starterCount[team];
        int starterBase = team * RosterCapacity.MaxStarters;
        Span<long> lastStart = stackalloc long[RosterCapacity.MaxStarters];
        Span<bool> available = stackalloc bool[RosterCapacity.MaxStarters];
        // No series in the regular season; 13-4 supplies Red Dirt Series context.
        Span<bool> usedInSeries = stackalloc bool[RosterCapacity.MaxStarters];
        for (int k = 0; k < starterCount; k++)
        {
            int slot = _starterSlots[starterBase + k];
            lastStart[k] = RestLedger.TryGetLastStart(_roster[slot].PlayerId, out long last)
                ? last : StarterSelection.NeverStarted;
            available[k] = !IsIneligible(slot);
        }

        int pick = StarterSelection.SelectStarter(
            lastStart[..starterCount], available[..starterCount], usedInSeries[..starterCount],
            day, _minRestDays, out bool shortRest);
        if (pick >= 0)
        {
            // D5: the workload knobs ship neutral and the macro-sim is
            // complete-game (§6), so a short-rest start is only counted here.
            if (shortRest)
            {
                ShortRestStarts++;
            }
            int starter = _starterSlots[starterBase + pick];
            RestLedger.RecordStart(_roster[starter].PlayerId, day);
            return starter;
        }

        int relieverCount = _relieverCount[team];
        int relieverBase = team * RosterCapacity.MaxRelievers;
        Span<bool> relieverAvailable = stackalloc bool[RosterCapacity.MaxRelievers];
        for (int k = 0; k < relieverCount; k++)
        {
            int slot = _relieverSlots[relieverBase + k];
            relieverAvailable[k] = !IsIneligible(slot);
        }
        int opener = StarterSelection.SelectOpener(relieverAvailable[..relieverCount]);
        if (opener < 0)
        {
            throw new InvalidOperationException(
                $"Team {_teamIds[team]} passed CanField with no eligible pitcher on day {day}.");
        }
        BullpenGames++;
        int openerSlot = _relieverSlots[relieverBase + opener];
        RestLedger.RecordStart(_roster[openerSlot].PlayerId, day);
        return openerSlot;
    }

    private void PlayGame(int homeTeam, int awayTeam, long day, long gameId)
    {
        // 13-5 (D2): both sides must field before anything is recorded — a
        // forfeit records no start, no stat, no PED game (D2b) and draws
        // nothing from _rng (§1.2 #6).
        bool homeFields = RosterRules.CanField(PrepareGameLineup(homeTeam), CountEligiblePitchers(homeTeam));
        bool awayFields = RosterRules.CanField(PrepareGameLineup(awayTeam), CountEligiblePitchers(awayTeam));
        if (!homeFields || !awayFields)
        {
            QueueForfeit(homeTeam, awayTeam, gameId, !homeFields, !awayFields);
            return;
        }

        int homeStarter = SelectPitcher(homeTeam, day);
        int awayStarter = SelectPitcher(awayTeam, day);
        PitcherRatings homePitcher = EffectivePitcher(homeStarter);
        PitcherRatings awayPitcher = EffectivePitcher(awayStarter);

        // Complete games for both starters — bullpens arrive with the Phase 4
        // micro-sim's stamina model.
        _pitching[homeStarter].G++;
        _pitching[homeStarter].Gs++;
        _pitching[awayStarter].G++;
        _pitching[awayStarter].Gs++;

        int homeScore = 0;
        int awayScore = 0;
        int homeOrder = 0;
        int awayOrder = 0;

        for (int inning = 1; ; inning++)
        {
            awayScore += PlayHalfInning(
                awayTeam, homeStarter, in homePitcher,
                _gameDefense[homeTeam], ref awayOrder, ref _rng, _batting, _pitching);
            if (inning >= _regulationInnings && homeScore > awayScore)
            {
                break; // home leads after the top of the final regulation inning (or later) — no bottom half
            }
            homeScore += PlayHalfInning(
                homeTeam, awayStarter, in awayPitcher,
                _gameDefense[awayTeam], ref homeOrder, ref _rng, _batting, _pitching);
            if (inning >= _regulationInnings && homeScore != awayScore)
            {
                break; // decided in the final regulation inning or extras; ties play on
            }
        }

        if (homeScore > awayScore)
        {
            _pitching[homeStarter].W++;
            _pitching[awayStarter].L++;
        }
        else
        {
            _pitching[awayStarter].W++;
            _pitching[homeStarter].L++;
        }

        // 13-3 (D4): the final score rides the next flush batch.
        EnqueueResult(gameId, ScheduleStatusFinal, homeScore, awayScore);

        // §6 post-game hook bookkeeping: every flagged participant logs a game
        // under the influence; the DB costs apply at flush. 13-5
        // (Amendment 5a): the nine who actually played.
        for (int i = 0; i < RosterCapacity.Lineup; i++)
        {
            CountPedGame(_gameLineupSlots[homeTeam * RosterCapacity.Lineup + i]);
            CountPedGame(_gameLineupSlots[awayTeam * RosterCapacity.Lineup + i]);
        }
        CountPedGame(homeStarter);
        CountPedGame(awayStarter);
    }

    private const int ScheduleStatusFinal = 1;
    private const int ScheduleStatusForfeit = 2;

    private void EnqueueResult(long gameId, int status, int homeScore, int awayScore)
    {
        if (_resultCount == _resultGameIds.Length)
        {
            throw new InvalidOperationException(
                $"{Tier} sim played {_resultCount} games without a flush — more than a season holds.");
        }
        _resultGameIds[_resultCount] = gameId;
        _resultStatuses[_resultCount] = status;
        _resultHomeScores[_resultCount] = homeScore;
        _resultAwayScores[_resultCount] = awayScore;
        _resultCount++;
    }

    /// <summary>
    /// A regular-season macro forfeit (D2a, D2d-A): RegulationInnings–0 to
    /// the side that fields, the home team on a double forfeit; status 2 in
    /// the result lane and its event buffered for the flush (Amendment 5c).
    /// </summary>
    private void QueueForfeit(int homeTeam, int awayTeam, long gameId, bool homeCannot, bool awayCannot)
    {
        bool homeWins = RosterRules.ForfeitScore(
            Tier, homeCannot, awayCannot, homeAwardedOnDouble: true, out int homeScore, out int awayScore);
        EnqueueResult(gameId, ScheduleStatusForfeit, homeScore, awayScore);
        _pendingForfeits.Add(new GameForfeitedEvent(
            _currentSeason.SeasonYear, (byte)Tier, gameId, phase: 0, _teamIds[homeTeam], _teamIds[awayTeam],
            RosterRules.ForfeitMask(homeCannot, awayCannot), homeWins ? _teamIds[homeTeam] : _teamIds[awayTeam]));
        ForfeitCount++;
    }

    private void CountPedGame(int slot)
    {
        // 13-5: only players who took the field are passed in (no call-up
        // shadows anyone); rusty players play and pay as normal.
        if (_pedActive[slot])
        {
            _pedGamesPlayed[slot]++;
        }
    }

    // ------------------------------------------------------------------
    // 13-5: eligibility, the game-day lineup and CanField's inputs
    // ------------------------------------------------------------------

    /// <summary>Out of today's game: absent (Phase 8c) or inactive (the Pro lock). One or two false branches when neither is live.</summary>
    private bool IsIneligible(int slot) =>
        (_hasAbsences && _slotUnavailable[slot]) || (_hasInactive && _slotInactive[slot]);

    /// <summary>
    /// Sets the team's game lineup and defense byte for today and returns
    /// its eligible position players (at least nine when the Initialize
    /// lineup is intact — all CanField needs). The Initialize lineup plays
    /// when all nine of its starters are eligible (0 B, no rebuild); else the
    /// allocation-free twin rebuilds from the eligible players in Build's
    /// order, unfeatured (the macro sim has no avatar), and the defense byte
    /// re-bakes from the lineup's fielding and retained teamwork. Fewer than
    /// nine eligible returns the count and leaves the game lineup unset.
    /// </summary>
    private int PrepareGameLineup(int team)
    {
        int lineupBase = team * RosterCapacity.Lineup;
        bool intact = true;
        if (_hasAbsences || _hasInactive)
        {
            for (int k = 0; k < RosterCapacity.Lineup; k++)
            {
                if (IsIneligible(_lineupSlots[lineupBase + k]))
                {
                    intact = false;
                    break;
                }
            }
        }
        if (intact)
        {
            Array.Copy(_lineupSlots, lineupBase, _gameLineupSlots, lineupBase, RosterCapacity.Lineup);
            _gameDefense[team] = _teamDefense[team];
            return RosterCapacity.Lineup;
        }

        int eligible = 0;
        int orderBase = team * RosterCapacity.MaxPositionPlayers;
        for (int k = 0; k < _positionCount[team]; k++)
        {
            int slot = _positionOrder[orderBase + k];
            if (!IsIneligible(slot))
            {
                _twinScratch[eligible++] = _slotCandidate[slot];
            }
        }
        if (eligible < RosterCapacity.Lineup)
        {
            return eligible;
        }
        LineupBuilder.BuildInto(_twinScratch.AsSpan(0, eligible), featuredSlot: -1, _twinSlots, _twinPositions);
        int fieldingSum = 0;
        int teamworkSum = 0;
        for (int k = 0; k < RosterCapacity.Lineup; k++)
        {
            int slot = _twinSlots[k];
            _gameLineupSlots[lineupBase + k] = slot;
            fieldingSum += _roster[slot].Fielding;
            teamworkSum += _slotTeamwork[slot];
        }
        _gameDefense[team] = PersonEffects.BakeTeamDefense(
            fieldingSum, RosterCapacity.Lineup, _tierDefenseDelta, teamworkSum);
        LineupRebuildCount++;
        return eligible;
    }

    /// <summary>Eligible starters and relievers (CanField's regular-season pitcher clause: any eligible SP or RP).</summary>
    private int CountEligiblePitchers(int team)
    {
        int count = 0;
        int starterBase = team * RosterCapacity.MaxStarters;
        for (int k = 0; k < _starterCount[team]; k++)
        {
            count += IsIneligible(_starterSlots[starterBase + k]) ? 0 : 1;
        }
        int relieverBase = team * RosterCapacity.MaxRelievers;
        for (int k = 0; k < _relieverCount[team]; k++)
        {
            count += IsIneligible(_relieverSlots[relieverBase + k]) ? 0 : 1;
        }
        return count;
    }

    /// <summary>Legal postseason pitchers: eligible and, inside The Red Dirt Series' window, not yet used in it.</summary>
    private int CountLegalPostseasonPitchers(int team, long day, long seriesFirstDay)
    {
        int count = 0;
        int starterBase = team * RosterCapacity.MaxStarters;
        for (int k = 0; k < _starterCount[team]; k++)
        {
            count += IsLegalInSeries(_starterSlots[starterBase + k], seriesFirstDay, day) ? 1 : 0;
        }
        int relieverBase = team * RosterCapacity.MaxRelievers;
        for (int k = 0; k < _relieverCount[team]; k++)
        {
            count += IsLegalInSeries(_relieverSlots[relieverBase + k], seriesFirstDay, day) ? 1 : 0;
        }
        return count;
    }

    private bool IsLegalInSeries(int slot, long seriesFirstDay, long day)
    {
        if (IsIneligible(slot))
        {
            return false;
        }
        long last = RestLedger.TryGetLastStart(_roster[slot].PlayerId, out long l) ? l : StarterSelection.NeverStarted;
        return !StarterSelection.StartedInSeries(last, seriesFirstDay, day);
    }

    /// <summary>
    /// Rebuilds the per-slot inactive cache when the manager's Version or the
    /// day has moved (a lock's coverage lapses by the calendar). Ids outside
    /// this tier's roster are skipped. Day granularity, never per PA;
    /// allocation-free once the scratch list is warm.
    /// </summary>
    internal void RefreshDesignations(long day)
    {
        if (Designations is null || _slotByPlayerId is null)
        {
            _hasInactive = false;
            return;
        }
        if (Designations.Version == _designationVersionSeen && day == _designationDaySeen)
        {
            return;
        }
        _designationVersionSeen = Designations.Version;
        _designationDaySeen = day;
        Array.Clear(_slotInactive);
        _hasInactive = false;
        Designations.CopyInactive(day, _inactiveScratch);
        for (int i = 0; i < _inactiveScratch.Count; i++)
        {
            if (_slotByPlayerId.TryGetValue(_inactiveScratch[i], out int slot))
            {
                _slotInactive[slot] = true;
                _hasInactive = true;
            }
        }
    }

    // ------------------------------------------------------------------
    // 13-4: the isolated postseason macro adapter (brief D4-A, §4.2–§4.3)
    // ------------------------------------------------------------------

    /// <summary>
    /// Plays one postseason game between two of this tier's teams on
    /// <paramref name="rng"/> — a <see cref="PostseasonStreams.ForGame"/>
    /// stream, never <c>_rng</c> — and returns the score without touching
    /// the regular-season accumulators, PED counters, result queue,
    /// <c>_unflushedSeasonYear</c> or the regular-season diagnostic
    /// counters (Math record §10.1 Q7 channels 1, 2, 5, 8, 10). Player
    /// lines go to private buffers and are discarded (no postseason stats,
    /// D4-A); PED games wait in <see cref="WritePostseasonPedCosts"/>'s
    /// buffer for the caller's batch; starts are recorded in the shared rest
    /// ledger like any game's. In The Red Dirt Series
    /// (<paramref name="seriesFirstDay"/> = its game-1 absolute day; else
    /// <see cref="PostseasonCalendar.NoSeriesFilter"/>) no pitcher who
    /// started or opened earlier in the series may start (Amendment 5). A
    /// team that fails <see cref="RosterRules.CanField"/> (13-5: nine
    /// eligible position players and a legal pitcher) forfeits: no game is
    /// played and no start recorded. Allocation-free.
    /// </summary>
    internal PostseasonPlay SimulatePostseasonGame(
        int homeTeamId, int awayTeamId, long day, long seriesFirstDay, ref RngState rng,
        out int homeScore, out int awayScore)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("LeagueSimulator.SimulatePostseasonGame before Initialize().");
        }
        int homeTeam = Array.IndexOf(_teamIds, homeTeamId);
        int awayTeam = Array.IndexOf(_teamIds, awayTeamId);
        if (homeTeam < 0 || awayTeam < 0 || homeTeam == awayTeam)
        {
            throw new ArgumentException($"Postseason game {homeTeamId} v {awayTeamId} is not a {Tier} pairing.");
        }
        RefreshAvailability(day);
        RefreshDesignations(day);
        RefreshRivalryCache();
        RefreshEquipmentCache();
        _postStarterCount = 0;
        _postShortRestBefore = PostseasonShortRestStarts;
        _postBullpenBefore = PostseasonBullpenGames;

        // 13-5 (D2e, Amendment 5b): one CanField rule, checked for both sides
        // before any start is recorded. It subsumes 13-4's narrow forfeit
        // (no legal pitcher) and adds the nine-position-player clause.
        bool homeFields = RosterRules.CanField(
            PrepareGameLineup(homeTeam), CountLegalPostseasonPitchers(homeTeam, day, seriesFirstDay));
        bool awayFields = RosterRules.CanField(
            PrepareGameLineup(awayTeam), CountLegalPostseasonPitchers(awayTeam, day, seriesFirstDay));
        if (!homeFields || !awayFields)
        {
            homeScore = 0;
            awayScore = 0;
            return !homeFields && !awayFields ? PostseasonPlay.BothForfeit
                : !homeFields ? PostseasonPlay.HomeForfeits : PostseasonPlay.AwayForfeits;
        }
        int homeStarter = PickPostseasonPitcher(homeTeam, day, seriesFirstDay, out bool homeShortRest, out bool homeOpener);
        int awayStarter = PickPostseasonPitcher(awayTeam, day, seriesFirstDay, out bool awayShortRest, out bool awayOpener);
        if (homeStarter < 0 || awayStarter < 0)
        {
            throw new InvalidOperationException(
                $"Postseason game {homeTeamId} v {awayTeamId} passed CanField with no legal pitcher on day {day}.");
        }
        RecordPostseasonStart(homeStarter, day, homeShortRest, homeOpener);
        RecordPostseasonStart(awayStarter, day, awayShortRest, awayOpener);

        Array.Clear(_postBatting);
        Array.Clear(_postPitching);
        PitcherRatings homePitcher = EffectivePitcher(homeStarter);
        PitcherRatings awayPitcher = EffectivePitcher(awayStarter);
        homeScore = 0;
        awayScore = 0;
        int homeOrder = 0;
        int awayOrder = 0;
        // The regular game's flow exactly: complete games, home half skipped
        // when ahead after the top of the final regulation inning, extras while tied.
        for (int inning = 1; ; inning++)
        {
            awayScore += PlayHalfInning(
                awayTeam, homeStarter, in homePitcher,
                _gameDefense[homeTeam], ref awayOrder, ref rng, _postBatting, _postPitching);
            if (inning >= _regulationInnings && homeScore > awayScore)
            {
                break;
            }
            homeScore += PlayHalfInning(
                homeTeam, awayStarter, in awayPitcher,
                _gameDefense[awayTeam], ref homeOrder, ref rng, _postBatting, _postPitching);
            if (inning >= _regulationInnings && homeScore != awayScore)
            {
                break;
            }
        }

        for (int i = 0; i < RosterCapacity.Lineup; i++)
        {
            CountPostseasonPedGame(_gameLineupSlots[homeTeam * RosterCapacity.Lineup + i]);
            CountPostseasonPedGame(_gameLineupSlots[awayTeam * RosterCapacity.Lineup + i]);
        }
        CountPostseasonPedGame(homeStarter);
        CountPostseasonPedGame(awayStarter);
        return PostseasonPlay.Played;
    }

    /// <summary>
    /// §6 selection for a postseason game without recording anything: the
    /// shared rule with the series no-repeat filter over starters AND
    /// relievers (Amendment 5); -1 when no pitcher is legal (the caller has
    /// already checked <see cref="RosterRules.CanField"/>, 13-5).
    /// </summary>
    private int PickPostseasonPitcher(int team, long day, long seriesFirstDay, out bool shortRest, out bool opener)
    {
        int starterCount = _starterCount[team];
        int starterBase = team * RosterCapacity.MaxStarters;
        Span<long> lastStart = stackalloc long[RosterCapacity.MaxStarters];
        Span<bool> available = stackalloc bool[RosterCapacity.MaxStarters];
        Span<bool> usedInSeries = stackalloc bool[RosterCapacity.MaxStarters];
        for (int k = 0; k < starterCount; k++)
        {
            int slot = _starterSlots[starterBase + k];
            lastStart[k] = RestLedger.TryGetLastStart(_roster[slot].PlayerId, out long last)
                ? last : StarterSelection.NeverStarted;
            available[k] = !IsIneligible(slot);
            usedInSeries[k] = StarterSelection.StartedInSeries(lastStart[k], seriesFirstDay, day);
        }
        int pick = StarterSelection.SelectStarter(
            lastStart[..starterCount], available[..starterCount], usedInSeries[..starterCount],
            day, _minRestDays, out shortRest);
        opener = false;
        if (pick >= 0)
        {
            return _starterSlots[starterBase + pick];
        }

        int relieverCount = _relieverCount[team];
        int relieverBase = team * RosterCapacity.MaxRelievers;
        Span<bool> relieverAvailable = stackalloc bool[RosterCapacity.MaxRelievers];
        Span<bool> relieverUsed = stackalloc bool[RosterCapacity.MaxRelievers];
        for (int k = 0; k < relieverCount; k++)
        {
            int slot = _relieverSlots[relieverBase + k];
            relieverAvailable[k] = !IsIneligible(slot);
            long last = RestLedger.TryGetLastStart(_roster[slot].PlayerId, out long l) ? l : StarterSelection.NeverStarted;
            relieverUsed[k] = StarterSelection.StartedInSeries(last, seriesFirstDay, day);
        }
        int index = StarterSelection.SelectOpener(relieverAvailable[..relieverCount], relieverUsed[..relieverCount]);
        opener = true;
        return index >= 0 ? _relieverSlots[relieverBase + index] : -1;
    }

    private void RecordPostseasonStart(int slot, long day, bool shortRest, bool opener)
    {
        _postStartUndo[_postStarterCount++] = RestLedger.RecordStartUndoable(_roster[slot].PlayerId, day);
        if (shortRest)
        {
            PostseasonShortRestStarts++;
        }
        if (opener)
        {
            PostseasonBullpenGames++;
        }
    }

    private void CountPostseasonPedGame(int slot)
    {
        if (_pedActive[slot])
        {
            _postPedGamesPlayed[slot]++;
        }
    }

    /// <summary>
    /// Applies the postseason PED game costs (§6 post-game hook) through the
    /// caller's OPEN batch — the same per-game costs the regular season
    /// applies, from the separate postseason buffer (Q7 channel 5). Call
    /// <see cref="ClearPostseasonGameState"/> after the commit or rollback.
    /// </summary>
    internal void WritePostseasonPedCosts()
    {
        for (int i = 0; i < _postPedGamesPlayed.Length; i++)
        {
            if (_postPedGamesPlayed[i] > 0)
            {
                _queries.ApplyPedGameCosts(
                    _roster[i].PlayerId,
                    _postPedGamesPlayed[i] * PedHealthCostPerGame,
                    _postPedGamesPlayed[i] * PedDetectionRiskPerGame);
            }
        }
    }

    /// <summary>
    /// Ends a postseason game's in-memory state. After a rollback,
    /// <paramref name="revertStarts"/> puts the rest ledger back exactly as it
    /// was (a never-started pitcher loses his entry, no phantom day-0 row) and
    /// the postseason diagnostic counters with it, so a retried game replays
    /// exactly (13-4 review N1).
    /// </summary>
    internal void ClearPostseasonGameState(bool revertStarts)
    {
        if (revertStarts)
        {
            for (int k = _postStarterCount - 1; k >= 0; k--)
            {
                RestLedger.Revert(in _postStartUndo[k]);
            }
            PostseasonShortRestStarts = _postShortRestBefore;
            PostseasonBullpenGames = _postBullpenBefore;
        }
        _postStarterCount = 0;
        Array.Clear(_postPedGamesPlayed);
    }

    /// <summary>
    /// The ratings a pitching slot actually throws with today: rust-docked
    /// when recovering from an injury, the baked array value otherwise (the
    /// exact pre-8c bytes). 13-5: an absent pitcher never pitches.
    /// </summary>
    private PitcherRatings EffectivePitcher(int slot)
    {
        // Phase 8e: gear boosts the tier-baked value BEFORE any rust dock
        // (equipment_quality.md §5 order); boost 0 is the identity, so a
        // no-gear pitcher keeps the exact pre-8e bytes.
        byte gear = _hasEquipment ? _slotGearBoost[slot] : (byte)0;
        byte rust = _hasAbsences ? _slotRustPenalty[slot] : (byte)0;
        if (gear == 0 && rust == 0)
        {
            return _pitcherRatings[slot];
        }
        PitcherRatings p = EquipmentEffects.Pitcher(in _pitcherRatings[slot], gear);
        return rust == 0 ? p : new PitcherRatings(
            TierEffects.Shift(p.Stuff, -rust), TierEffects.Shift(p.Control, -rust), p.Stamina);
    }

    /// <summary>Baked team-defense byte (tier + HS-6 §6.2 teamwork lever) — internal so run_monte_carlo_batch pins the defense bake per sim.</summary>
    internal byte TeamDefenseFor(int teamId)
    {
        for (int t = 0; t < _teamIds.Length; t++)
        {
            if (_teamIds[t] == teamId)
            {
                return _teamDefense[t];
            }
        }
        throw new ArgumentException($"Team {teamId} is not in this league.", nameof(teamId));
    }

    /// <summary>Batter-side rust dock (0 = the array value unchanged). PED flag rides along — the resolver layers its multiplier as usual. Internal so run_monte_carlo_batch pins the arithmetic.</summary>
    internal static BatterRatings ApplyRust(in BatterRatings b, byte rust) =>
        rust == 0 ? b : new BatterRatings(
            TierEffects.Shift(b.Power, -rust), TierEffects.Shift(b.Contact, -rust),
            TierEffects.Shift(b.Discipline, -rust), b.PedActive);

    /// <summary>
    /// Rebuilds the per-slot availability caches when the ledger's Version or
    /// the day has moved (an absence expires by the calendar advancing, so
    /// Version alone is not enough). Ids the ledger knows but the roster
    /// doesn't are skipped. Runs at day granularity, never per PA; internal
    /// so the harness can pair it with a direct SimulateGameDay drive.
    /// </summary>
    internal void RefreshAvailability(long day)
    {
        if (Availability is null || _slotByPlayerId is null)
        {
            _hasAbsences = false;
            return;
        }
        if (Availability.Version == _absenceVersionSeen && day == _absenceDaySeen)
        {
            return;
        }
        _absenceVersionSeen = Availability.Version;
        _absenceDaySeen = day;
        Array.Clear(_slotUnavailable);
        Array.Clear(_slotRustPenalty);
        _hasAbsences = false;

        Availability.CopyActive(day, _absenceScratch);
        for (int i = 0; i < _absenceScratch.Count; i++)
        {
            AbsenceEntry entry = _absenceScratch[i];
            if (!_slotByPlayerId.TryGetValue(entry.PlayerId, out int slot))
            {
                continue;
            }
            switch (entry.StateOn(day))
            {
                case SlotAvailability.Absent:
                    _slotUnavailable[slot] = true;
                    _hasAbsences = true;
                    break;
                case SlotAvailability.Rusty:
                    _slotRustPenalty[slot] = entry.RatingPenalty;
                    _hasAbsences = true;
                    break;
            }
        }
    }

    /// <summary>
    /// Rebuilds the slot×slot intensity cache when the ledger has moved.
    /// Ids the ledger knows but the roster doesn't (retired/free agents) are
    /// simply skipped. Runs at day granularity, never per PA; the scratch
    /// list reuse keeps even the rebuild allocation-free once warm.
    /// </summary>
    /// <summary>
    /// Rebuilds the per-slot gear-boost cache when the ledger's Version has
    /// moved (gear never expires by calendar, so Version alone gates it —
    /// unlike absences). Ids the ledger knows but the roster doesn't are
    /// skipped. Runs at day granularity, never per PA.
    /// </summary>
    private void RefreshEquipmentCache()
    {
        if (Equipment is null || _slotByPlayerId is null)
        {
            _hasEquipment = false;
            return;
        }
        if (Equipment.Version == _equipmentVersionSeen)
        {
            return;
        }
        _equipmentVersionSeen = Equipment.Version;
        Array.Clear(_slotGearBoost);
        _hasEquipment = false;

        Equipment.CopyAll(_equipmentScratch);
        for (int i = 0; i < _equipmentScratch.Count; i++)
        {
            EquipmentEntry entry = _equipmentScratch[i];
            if (_slotByPlayerId.TryGetValue(entry.PlayerId, out int slot))
            {
                _slotGearBoost[slot] = EquipmentEffects.BoostFor(entry.Quality);
                _hasEquipment |= _slotGearBoost[slot] > 0;
            }
        }
    }

    private void RefreshRivalryCache()
    {
        if (Rivalries is null || _slotByPlayerId is null)
        {
            _hasRivalries = false;
            return;
        }
        if (Rivalries.Version == _rivalryVersionSeen)
        {
            return;
        }
        _rivalryVersionSeen = Rivalries.Version;
        Array.Clear(_rivalrySlots);
        _hasRivalries = false;

        int slots = _roster.Length;
        Rivalries.CopyPairs(_rivalryScratch);
        for (int i = 0; i < _rivalryScratch.Count; i++)
        {
            RivalryPair pair = _rivalryScratch[i];
            if (_slotByPlayerId.TryGetValue(pair.PlayerAId, out int a) &&
                _slotByPlayerId.TryGetValue(pair.PlayerBId, out int b))
            {
                _rivalrySlots[a * slots + b] = pair.Intensity;
                _rivalrySlots[b * slots + a] = pair.Intensity;
                _hasRivalries |= pair.Intensity > 0;
            }
        }
    }

    /// <summary>
    /// §7 base-out state machine. Bases are 3 bits (1 = 1B, 2 = 2B, 4 = 3B).
    /// Returns runs scored; charges the pitching line (all runs earned — no
    /// errors in the macro-sim) and credits batter counting stats + RBI.
    /// The pitcher's effective ratings are the caller's pick. 13-5: the
    /// batters are the batting team's game lineup (the Initialize lineup, or
    /// its reshuffle around an ineligible starter); nobody is shadowed.
    /// 13-4: the stream and the stat sinks are the caller's — the regular
    /// season passes <c>_rng</c> and the season accumulators (the exact
    /// pre-13-4 draws), a postseason game its own derived stream and the
    /// private discard buffers (D4-A).
    /// </summary>
    private int PlayHalfInning(
        int battingTeam, int pitcherSlot, in PitcherRatings pitcher,
        byte defense, ref int orderPos, ref RngState rng, BattingLine[] batting, PitchingLine[] pitching)
    {
        int outs = 0;
        int bases = 0;
        int runs = 0;

        while (outs < 3)
        {
            int batterSlot = _gameLineupSlots[battingTeam * RosterCapacity.Lineup + orderPos];
            orderPos = (orderPos + 1) % RosterCapacity.Lineup;

            byte batterRust = _hasAbsences ? _slotRustPenalty[batterSlot] : (byte)0;

            // Phase 6: an active batter-pitcher rivalry bends this PA through
            // effective ratings (RivalryEffects), same resolver. Intensity 0
            // takes the exact pre-rivalry call — bit-identical M1 lines.
            byte rivalry = _hasRivalries
                ? _rivalrySlots[batterSlot * _roster.Length + pitcherSlot] : (byte)0;
            // Phase 8e: gear boosts the batter's tier-baked ratings BEFORE any
            // rust dock (equipment_quality.md §5 order). Boost 0 keeps the
            // exact pre-8e fast path.
            byte batterGear = _hasEquipment ? _slotGearBoost[batterSlot] : (byte)0;
            PaOutcome outcome;
            if (rivalry == 0 && batterRust == 0 && batterGear == 0)
            {
                outcome = AtBatResolver.Resolve(
                    in _batterRatings[batterSlot], in pitcher, defense, ref rng);
            }
            else
            {
                BatterRatings paBatter =
                    ApplyRust(EquipmentEffects.Batter(in _batterRatings[batterSlot], batterGear), batterRust);
                PitcherRatings paPitcher = pitcher;
                if (rivalry != 0)
                {
                    paBatter = RivalryEffects.Batter(in paBatter, rivalry);
                    paPitcher = RivalryEffects.Pitcher(in pitcher, rivalry);
                }
                outcome = AtBatResolver.Resolve(in paBatter, in paPitcher, defense, ref rng);
            }

            ref BattingLine bat = ref batting[batterSlot];
            ref PitchingLine pit = ref pitching[pitcherSlot];
            bat.Pa++;
            int paRuns = 0;

            switch (outcome)
            {
                case PaOutcome.Out:
                    bat.Ab++;
                    outs++;
                    pit.Outs++;
                    break;
                case PaOutcome.Strikeout:
                    bat.Ab++;
                    bat.So++;
                    outs++;
                    pit.Outs++;
                    pit.So++;
                    break;
                case PaOutcome.Walk:
                    bat.Bb++;
                    pit.Bb++;
                    paRuns = BaseOutAdvancement.AdvanceWalk(ref bases);
                    break;
                case PaOutcome.Single:
                    bat.Ab++;
                    bat.H++;
                    pit.HAllowed++;
                    paRuns = BaseOutAdvancement.AdvanceSingle(ref bases, ref rng);
                    break;
                case PaOutcome.Double:
                    bat.Ab++;
                    bat.H++;
                    bat.Doubles++;
                    pit.HAllowed++;
                    paRuns = BaseOutAdvancement.AdvanceDouble(ref bases, ref rng);
                    break;
                case PaOutcome.Triple:
                    bat.Ab++;
                    bat.H++;
                    bat.Triples++;
                    pit.HAllowed++;
                    paRuns = BaseOutAdvancement.AdvanceTriple(ref bases);
                    break;
                default: // HomeRun
                    bat.Ab++;
                    bat.H++;
                    bat.Hr++;
                    pit.HAllowed++;
                    paRuns = BaseOutAdvancement.AdvanceHomeRun(ref bases);
                    break;
            }

            bat.Rbi += paRuns;
            pit.Er += paRuns;
            runs += paRuns;
        }
        return runs;
    }

    // ------------------------------------------------------------------
    // Cycle flush — the sim's own batch, never the calendar tick's
    // ------------------------------------------------------------------

    /// <summary>
    /// Writes everything accumulated since the last flush and clears the
    /// accumulators. ADDITIVE upserts (the micro-sim's per-game flushes land
    /// on the same (player, season) rows — an overwrite here would clobber
    /// the opponents' attended-game lines), so each chunk composes with both
    /// the previous cycles and the attended games.
    /// </summary>
    private void FlushAccumulated(int seasonYear)
    {
        _db.BeginBatch();
        try
        {
            for (int i = 0; i < _roster.Length; i++)
            {
                ref readonly BattingLine bat = ref _batting[i];
                if (bat.Pa > 0)
                {
                    _queries.AddBattingGameCounts(
                        _roster[i].PlayerId, seasonYear,
                        bat.Pa, bat.Ab, bat.H, bat.Doubles, bat.Triples, bat.Hr, bat.Bb, bat.So, bat.Rbi, sb: 0);
                }

                ref readonly PitchingLine pit = ref _pitching[i];
                if (pit.G > 0)
                {
                    _queries.AddPitchingGameCounts(
                        _roster[i].PlayerId, seasonYear,
                        pit.G, pit.Gs, pit.W, pit.L, sv: 0, pit.Outs, pit.HAllowed, pit.Er, pit.Bb, pit.So);
                }

                if (_pedGamesPlayed[i] > 0)
                {
                    _queries.ApplyPedGameCosts(
                        _roster[i].PlayerId,
                        _pedGamesPlayed[i] * PedHealthCostPerGame,
                        _pedGamesPlayed[i] * PedDetectionRiskPerGame);
                }
            }
            // 13-2 (brief D4): the starts made since the last flush ride the
            // same batch, so rest is never staler than the stats beside it.
            RestLedger.FlushDirty(_queries);
            // 13-3 (brief D4): so do the played games' results. On a rollback
            // they stay buffered with the stats and land on the retry. 13-5:
            // each with its status (1 final, 2 forfeit; brief §4 item 5).
            for (int r = 0; r < _resultCount; r++)
            {
                _scheduleQueries.RecordResult(
                    _resultGameIds[r], _resultStatuses[r], _resultHomeScores[r], _resultAwayScores[r]);
            }
            // 13-4 (brief D1-A): Team_Seasons (and College's national rank)
            // recomputed from the persisted results in the same batch, when
            // this flush carried results (an empty offseason flush skips it).
            if (_resultCount > 0)
            {
                _teamSeasons.Recompute(seasonYear, Tier, _teamIds, ScheduleSeed);
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            RestLedger.AbandonFlush(); // review R2: rolled back, so the rows stay dirty
            throw;
        }
        RestLedger.AcknowledgeFlush(); // review R2: durable only once committed

        // Rates are denormalized after the counting-stat batch, in the
        // normalizer's own transaction — tier-scoped (9a) so six sims flushing
        // the same day each rewrite only their own league's rows.
        _normalizer.NormalizeSeason(seasonYear, Tier);

        Array.Clear(_batting);
        Array.Clear(_pitching);
        Array.Clear(_pedGamesPlayed);
        _resultCount = 0;
        _unflushedSeasonYear = 0;

        // 13-5 (Amendment 5c): the week's forfeits publish only now, after
        // the batch that wrote them committed (a rollback keeps them
        // buffered with their results for the retry).
        if (_pendingForfeits.Count > 0)
        {
            for (int i = 0; i < _pendingForfeits.Count; i++)
            {
                _bus?.Publish(_pendingForfeits[i]);
            }
            _pendingForfeits.Clear();
        }
    }
}
