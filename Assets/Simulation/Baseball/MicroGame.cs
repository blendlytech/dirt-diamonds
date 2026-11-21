using System.Text;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>Final line of one attended game (micro doc §9) — a value snapshot for UI/harness.</summary>
public readonly struct MicroGameResult
{
    public readonly int HomeTeamId;
    public readonly int AwayTeamId;
    public readonly int HomeScore;
    public readonly int AwayScore;
    public readonly int Innings;
    public readonly int HumanPa;
    public readonly int HumanPitchesSeen;
    public readonly double HomeStarterPitches;
    public readonly double AwayStarterPitches;

    public MicroGameResult(
        int homeTeamId, int awayTeamId, int homeScore, int awayScore, int innings,
        int humanPa, int humanPitchesSeen, double homeStarterPitches, double awayStarterPitches)
    {
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
        HomeScore = homeScore;
        AwayScore = awayScore;
        Innings = innings;
        HumanPa = humanPa;
        HumanPitchesSeen = humanPitchesSeen;
        HomeStarterPitches = homeStarterPitches;
        AwayStarterPitches = awayStarterPitches;
    }
}

/// <summary>Per-inning offensive aggregates across attended games (harness §11 test 4).</summary>
public struct InningLine
{
    public long Pa, Ab, H, Doubles, Triples, Hr, Bb, So;
}

/// <summary>
/// The attended-game driver (micro doc §9): plays the one game per day the
/// human is present for. The outer 25-state base-out chain advances through
/// the shared <see cref="BaseOutAdvancement"/>; the inner pitch chain (§5) is
/// spun up only for the PAs the human personally contests (batting, or every
/// PA while the human pitches); every other PA macro-resolves through the
/// shared <see cref="AtBatResolver"/> — always with the §8 fatigue-adjusted
/// effective pitcher ratings, so the whole game reflects the starter tiring.
///
/// Box scores accumulate in preallocated struct arrays and flush per game via
/// the ADDITIVE season upserts in the sim's OWN batch (never the calendar
/// tick's), alongside play-by-play rows for Game_Logs built at PA boundaries.
/// With logging disabled, a simulated game allocates zero bytes once warm.
/// Never references the Life sim.
///
/// When the human is on the mound, the pitcher policy supplies per-pitch
/// calls (type + zone target, v4) while the opposing batters answer with the
/// neutral policy; bullpen pulls (§8.5) happen between half-innings once the
/// fatigue multiplier crosses the threshold.
/// </summary>
public sealed class MicroGame
{
    /// <summary>Sentinel for "no human in this game" (fully NPC exhibition / harness runs).</summary>
    public const int NoHuman = -1;

    /// <summary>
    /// §8.5 bullpen trigger: the starter (or current reliever) is lifted
    /// between half-innings once his fatigue multiplier sinks below this —
    /// ~0.91 of capacity, mid-to-late game for an average starter.
    /// </summary>
    public const double BullpenPullThreshold = 0.85;

    private const int MaxTrackedInnings = 30;

    private readonly DatabaseManager _db;
    private readonly BaseballQueries _queries;

    // Roster-slot-indexed parallel arrays, filled once at Initialize — the
    // same load pattern (and query) as the macro LeagueSimulator.
    private RosterPlayerRow[] _roster = Array.Empty<RosterPlayerRow>();
    private string[] _displayNames = Array.Empty<string>();
    private BatterRatings[] _batterRatings = Array.Empty<BatterRatings>();
    private PitcherRatings[] _pitcherRatings = Array.Empty<PitcherRatings>();
    private PitcherArsenal[] _arsenals = Array.Empty<PitcherArsenal>();
    private bool[] _pedActive = Array.Empty<bool>();

    // Team-indexed, sized by RosterCapacity and looped to each team's own
    // counts (§5.2, slice 13-2): the lineup in batting order, starters and
    // relievers in StarterSelection quality order.
    private int[] _teamIds = Array.Empty<int>();
    private LeagueTier[] _teamTier = Array.Empty<LeagueTier>();
    private int[] _lineupSlots = Array.Empty<int>();
    private int[] _starterSlots = Array.Empty<int>();
    private int[] _starterCount = Array.Empty<int>();
    private int[] _relieverSlots = Array.Empty<int>();
    private int[] _relieverCount = Array.Empty<int>();
    private List<LineupCandidate>[] _teamCandidates = Array.Empty<List<LineupCandidate>>();
    private byte[] _teamDefense = Array.Empty<byte>();
    private int _teamCount;

    // 13-2: the game's calendar day for §6 rest. The career driver sets it per
    // attended game; headless callers that never do get one day per game.
    private long _gameDay;
    private bool _gameDaySet;
    private long _autoGameDay = 1;

    // 13-3 (brief D4): the Schedule_Games row the next game is, set by the
    // career driver per attended game; FlushGame marks it final in the
    // game's own batch. Headless callers that never set one write no result.
    private readonly ScheduleQueries _scheduleQueries;
    private long _scheduledGameId;
    private long _flushGameId;
    private int _flushHomeScore;
    private int _flushAwayScore;

    private string? _featuredPlayerId;

    /// <summary>
    /// Last-start days for §6 starter selection (slice 13-2): a private ledger
    /// by default; GameManager assigns the one shared with every tier sim.
    /// </summary>
    public PitcherRestLedger RestLedger { get; set; } = new();

    /// <summary>Starts made on short rest (the §6 fallback), for the 13-2 harness.</summary>
    internal int ShortRestStarts { get; private set; }

    /// <summary>Games where no starter could go and a reliever opened (§6), for the 13-2 harness.</summary>
    internal int BullpenGames { get; private set; }

    /// <summary>The last game's starting pitchers (home, away), for the 13-2 harness's macro/micro parity check.</summary>
    internal (string Home, string Away) LastStarters { get; private set; }

    // Game-scoped accumulators (cleared at the start of every PlayGame).
    private BattingLine[] _batting = Array.Empty<BattingLine>();
    private PitchingLine[] _pitching = Array.Empty<PitchingLine>();
    private bool[] _playedThisGame = Array.Empty<bool>();
    private readonly List<PendingLog> _pendingLogs = new(128);
    private readonly StringBuilder _payloadBuilder = new(160);
    private int _flushHomeTeamId;
    private int _flushAwayTeamId;
    private bool _gamePendingFlush;

    /// <summary>Cross-game per-inning offense, for fatigue validation and future UI splits.</summary>
    public readonly InningLine[] InningTotals = new InningLine[MaxTrackedInnings];

    /// <summary>§8: false freezes every fatigue multiplier at 1 (harness test 2).</summary>
    public bool FatigueEnabled = true;

    /// <summary>§8.5: false pins the starter for a complete game (harness tests
    /// that isolate starter fatigue from bullpen relief).</summary>
    public bool BullpenEnabled = true;

    /// <summary>§10: play-by-play capture; disable for headless bulk runs (zero-GC profile).</summary>
    public bool LoggingEnabled = true;

    /// <summary>
    /// Attended-game NPC play-by-play feed (the "render NPC PAs between the
    /// avatar's at-bats" gap): when a UI attaches its <see cref="PlayerIntentBridge"/>
    /// here, every NPC plate appearance is queued for it at the PA boundary.
    /// Null (the default) for headless/autopilot/harness games — the check is a
    /// single null comparison, so the zero-GC hot path is untouched when no
    /// viewer is watching.
    /// </summary>
    public PlayerIntentBridge? FeedSink;

    /// <summary>
    /// Rivalry intensity source (Phase 6), optional like <see cref="FeedSink"/>:
    /// null leaves every PA on the exact pre-rivalry path.
    /// </summary>
    public RivalryLedger? Rivalries;

    /// <summary>
    /// Person-layer lever source (HS-6, person-layer doc §6), optional like
    /// <see cref="Rivalries"/>: null (or an absent Player_Person row) reads
    /// as the neutral 50s and bakes bit-identical rating arrays. Set BEFORE
    /// <see cref="Initialize"/>; the same re-Initialize beats that refresh
    /// the macro sims refresh this one.
    /// </summary>
    public PersonQueries? Persons;

    /// <summary>
    /// Live person-lever mirror (the HS-6 disclosure's per-game person
    /// refresh), optional like <see cref="Equipment"/>: null keeps every
    /// attended game on the season-stable Initialize bake exactly. When set,
    /// slots whose committed happiness/confidence/teamwork moved since
    /// Initialize re-bake at game start (<see cref="RefreshPersonCache"/>) —
    /// so the avatar's in-season mood reaches TODAY'S attended game instead
    /// of waiting for the next re-Initialize beat. The ledger is in-memory
    /// (micro games run off the main thread — no mid-game DB reads); the
    /// macro sims deliberately stay Initialize-baked (§6.2 season-stable).
    /// </summary>
    public PersonLedger? PersonLevers;

    // Slot×slot intensity cache refreshed at game start when the ledger's
    // Version has moved — the per-PA cost is one byte read (zero-GC mandate).
    private byte[] _rivalrySlots = Array.Empty<byte>();
    private readonly List<RivalryPair> _rivalryScratch = new();
    private int _rivalryVersionSeen = -1;
    private bool _hasRivalries;

    /// <summary>
    /// Roster-availability source (Phase 8c), optional like <see cref="Rivalries"/>:
    /// null leaves every game on the exact pre-availability path. The career
    /// driver refreshes via <see cref="RefreshAvailability"/> before each
    /// attended game (the micro-sim doesn't know the calendar day itself).
    /// </summary>
    public AvailabilityLedger? Availability;

    /// <summary>Feed name for a shadowed slot's plate appearances (a nameless roster fill-in, not the absent player).</summary>
    private const string ReplacementDisplayName = "Call-Up";

    // The replacement call-up's fixed league-shaped arsenal (the v3→v4
    // backfill's 60/25/15 mix at replacement-level stuff) — only consulted
    // when the human bats against a shadowed pitcher.
    private static readonly PitcherArsenal ReplacementArsenal = new(
        LeagueSimulator.ReplacementRating, 40, 60,
        LeagueSimulator.ReplacementRating, 40, 25,
        LeagueSimulator.ReplacementRating, 40, 15);

    // Per-slot availability caches (Phase 8c), rebuilt when the ledger's
    // Version or the refreshed day moves; per-team replacement ratings baked
    // at Initialize with each team's own tier deltas (the micro-sim is
    // global — a shadowed HS slot is shadowed by an HS-environment call-up).
    private bool[] _slotUnavailable = Array.Empty<bool>();
    private byte[] _slotRustPenalty = Array.Empty<byte>();
    private readonly List<AbsenceEntry> _absenceScratch = new();
    private int _absenceVersionSeen = -1;
    private long _absenceDaySeen = -1;
    private bool _hasAbsences;
    private BatterRatings[] _replacementBatterByTeam = Array.Empty<BatterRatings>();
    private PitcherRatings[] _replacementPitcherByTeam = Array.Empty<PitcherRatings>();

    /// <summary>
    /// Purchased-gear source (Phase 8e), optional like <see cref="Rivalries"/>:
    /// null leaves every PA on the exact pre-equipment path. Version-gated
    /// cache refreshed at game start; the human's interactive pitch-chain
    /// at-bats flow through the same effective-ratings sites, so the avatar's
    /// gear shifts the anchor p* with zero extra plumbing.
    /// </summary>
    public EquipmentLedger? Equipment;

    // Per-slot gear-boost cache (Phase 8e), rebuilt when the ledger's Version
    // moves — gear never expires by calendar, so Version alone gates it.
    private byte[] _slotGearBoost = Array.Empty<byte>();
    private readonly List<EquipmentEntry> _equipmentScratch = new();
    private int _equipmentVersionSeen = -1;
    private bool _hasEquipment;

    // Per-game person refresh (the HS-6 disclosure): the currently-baked
    // §6.2 deltas per slot plus the Initialize-time inputs a re-bake needs
    // (team index, tier deltas, lineup membership, per-team fielding and
    // teamwork sums) — so RefreshPersonCache can land the exact bytes a full
    // re-Initialize would, without touching the database.
    private PersonRatingDeltas[] _slotPersonDeltas = Array.Empty<PersonRatingDeltas>();
    private int[] _slotTeam = Array.Empty<int>();
    private bool[] _slotInLineup = Array.Empty<bool>();
    private TierRatingDeltas[] _teamTierDeltas = Array.Empty<TierRatingDeltas>();
    private int[] _teamFieldingSum = Array.Empty<int>();
    private int[] _teamTeamworkSum = Array.Empty<int>();
    private readonly List<PersonLeverEntry> _personScratch = new();
    private int _personVersionSeen = -1;

    private bool _initialized;

    public MicroGame(DatabaseManager db, BaseballQueries queries)
    {
        _db = db;
        _queries = queries;
        _scheduleQueries = new ScheduleQueries(db);
    }

    private struct BattingLine
    {
        public int Pa, Ab, H, Doubles, Triples, Hr, Bb, So, Rbi;
    }

    private struct PitchingLine
    {
        public int G, Gs, W, L, Outs, HAllowed, Er, Bb, So;
    }

    private readonly struct PendingLog
    {
        public readonly string? PlayerId;
        public readonly string EventType;
        public readonly string? Payload;

        public PendingLog(string? playerId, string eventType, string? payload)
        {
            PlayerId = playerId;
            EventType = eventType;
            Payload = payload;
        }
    }

    // ------------------------------------------------------------------
    // Load & lookup
    // ------------------------------------------------------------------

    /// <summary>
    /// One-time bulk load (teams, roster join, active PED flags) — the same
    /// query surface the macro-sim uses; the per-PA hot path never touches
    /// the database after this returns.
    /// </summary>
    public void Initialize()
    {
        var teams = new List<TeamRow>(8);
        _queries.LoadAllTeams(teams);
        if (teams.Count == 0)
        {
            throw new InvalidOperationException("MicroGame requires a generated league (no Teams rows).");
        }
        _teamCount = teams.Count;
        _teamIds = new int[_teamCount];
        // Phase 9a: the micro-sim stays global (it hosts whichever tier the
        // avatar plays in), so tier environment deltas bake PER TEAM here —
        // each player shifted by their own team's tier at load, the same
        // load-time baking LeagueSimulator does per instance. An attended HS
        // game therefore plays in the HS environment the macro-sim calibrates,
        // and the §11 macro/micro consistency holds tier by tier by
        // construction. MLB (and every pre-v7 world) is all-zero deltas.
        var teamIndexById = new Dictionary<int, int>(_teamCount);
        _teamTierDeltas = new TierRatingDeltas[_teamCount];
        _teamTier = new LeagueTier[_teamCount];
        for (int t = 0; t < _teamCount; t++)
        {
            _teamIds[t] = teams[t].TeamId;
            _teamTier[t] = teams[t].Tier;
            _teamTierDeltas[t] = TierEffects.For(teams[t].Tier);
            teamIndexById.Add(teams[t].TeamId, t);
        }
        RestLedger.HydrateOnce(_queries);

        var rosterRows = new List<RosterPlayerRow>(_teamCount * RosterCapacity.MaxRoster);
        _queries.LoadRoster(rosterRows);
        _roster = rosterRows.ToArray();

        var pedIds = new List<string>();
        _queries.LoadActiveFlagPlayerIds(LeagueSimulator.PedActiveFlagName, pedIds);
        var pedSet = new HashSet<string>(pedIds, StringComparer.Ordinal);

        // 13-2 (brief D3): who plays is a legal defense from Player_Fielding.
        var fieldingRows = new List<PlayerFieldingRow>();
        _queries.LoadAllFielding(fieldingRows);
        var fieldingById = new Dictionary<string, PlayerFieldingRow>(fieldingRows.Count, StringComparer.Ordinal);
        foreach (PlayerFieldingRow f in fieldingRows)
        {
            fieldingById[f.PlayerId] = f;
        }

        // Arsenal rows keyed by player for slot assembly below (load-time only).
        var arsenalRows = new List<PitchArsenalRow>(_roster.Length * 3);
        _queries.LoadAllArsenals(arsenalRows);
        var arsenalParts = new Dictionary<string, (PitchArsenalRow Fb, PitchArsenalRow Brk, PitchArsenalRow Off, int Count)>(StringComparer.Ordinal);
        foreach (PitchArsenalRow row in arsenalRows)
        {
            arsenalParts.TryGetValue(row.PlayerId, out var parts);
            switch (row.Type)
            {
                case PitchType.Fastball: parts.Fb = row; break;
                case PitchType.Breaking: parts.Brk = row; break;
                default: parts.Off = row; break;
            }
            parts.Count++;
            arsenalParts[row.PlayerId] = parts;
        }

        int slots = _roster.Length;
        _displayNames = new string[slots];
        for (int i = 0; i < slots; i++)
        {
            RosterPlayerRow r = _roster[i];
            _displayNames[i] = r.FirstName.Length > 0 ? $"{r.FirstName[0]}. {r.LastName}" : r.LastName;
        }
        _batterRatings = new BatterRatings[slots];
        _pitcherRatings = new PitcherRatings[slots];
        _arsenals = new PitcherArsenal[slots];
        _pedActive = new bool[slots];
        _rivalrySlots = new byte[slots * slots];
        _rivalryVersionSeen = -1; // roster changed — force a cache rebuild
        _hasRivalries = false;
        _slotUnavailable = new bool[slots];
        _slotRustPenalty = new byte[slots];
        _absenceVersionSeen = -1; // roster changed — force a cache rebuild
        _absenceDaySeen = -1;
        _hasAbsences = false;
        _slotGearBoost = new byte[slots];
        _equipmentVersionSeen = -1; // roster changed — force a cache rebuild
        _hasEquipment = false;
        _slotPersonDeltas = new PersonRatingDeltas[slots];
        _slotTeam = new int[slots];
        _slotInLineup = new bool[slots];
        _personVersionSeen = -1; // roster changed — force a cache rebuild
        // One extra line past the roster: the discard row a shadowed slot's
        // replacement accumulates into. FlushGame loops the roster length
        // only, so a call-up's stats and PED bookkeeping never reach the
        // database or the absent player.
        _batting = new BattingLine[slots + 1];
        _pitching = new PitchingLine[slots + 1];
        _playedThisGame = new bool[slots + 1];

        // Phase 8c: each team's replacement call-up plays in that team's tier
        // environment, the same per-team baking the roster arrays get below.
        _replacementBatterByTeam = new BatterRatings[_teamCount];
        _replacementPitcherByTeam = new PitcherRatings[_teamCount];
        for (int t = 0; t < _teamCount; t++)
        {
            TierRatingDeltas deltas = _teamTierDeltas[t];
            _replacementBatterByTeam[t] = new BatterRatings(
                TierEffects.Shift(LeagueSimulator.ReplacementRating, deltas.BatPower),
                TierEffects.Shift(LeagueSimulator.ReplacementRating, deltas.BatContact),
                TierEffects.Shift(LeagueSimulator.ReplacementRating, deltas.BatDiscipline), pedActive: false);
            _replacementPitcherByTeam[t] = new PitcherRatings(
                TierEffects.Shift(LeagueSimulator.ReplacementRating, deltas.PitStuff),
                TierEffects.Shift(LeagueSimulator.ReplacementRating, deltas.PitControl),
                (byte)LeagueSimulator.ReplacementRating);
        }
        _lineupSlots = new int[_teamCount * RosterCapacity.Lineup];
        _starterSlots = new int[_teamCount * RosterCapacity.MaxStarters];
        _starterCount = new int[_teamCount];
        _relieverSlots = new int[_teamCount * RosterCapacity.MaxRelievers];
        _relieverCount = new int[_teamCount];
        _teamDefense = new byte[_teamCount];
        _teamCandidates = new List<LineupCandidate>[_teamCount];
        var starters = new List<int>[_teamCount];
        var relievers = new List<int>[_teamCount];
        for (int t = 0; t < _teamCount; t++)
        {
            _teamCandidates[t] = new List<LineupCandidate>(RosterCapacity.MaxPositionPlayers);
            starters[t] = new List<int>(RosterCapacity.MaxStarters);
            relievers[t] = new List<int>(RosterCapacity.MaxRelievers);
        }
        // Fielding/teamwork sums are retained past Initialize (fields, not
        // stack scratch): the per-game person refresh re-bakes a team's
        // defense byte from them when a lineup teamwork lever moves.
        _teamFieldingSum = new int[_teamCount];
        _teamTeamworkSum = new int[_teamCount];

        // HS-6 (person-layer doc §6): person levers load once here and bake
        // with the per-team tier deltas below — the exact macro-sim bake, so
        // the §11 macro/micro consistency holds through the person layer by
        // construction. No source / no row = neutral = bit-identical arrays.
        Dictionary<string, PersonRow>? personRows = null;
        if (Persons is not null)
        {
            personRows = new Dictionary<string, PersonRow>(slots, StringComparer.Ordinal);
            Persons.LoadAll(personRows);
        }

        for (int i = 0; i < slots; i++)
        {
            ref readonly RosterPlayerRow row = ref _roster[i];
            if (!teamIndexById.TryGetValue(row.TeamId, out int team))
            {
                throw new InvalidOperationException(
                    $"Player {row.PlayerId} references team_id {row.TeamId} with no Teams row (query-layer FK).");
            }

            bool ped = pedSet.Contains(row.PlayerId);
            _pedActive[i] = ped;
            _slotTeam[i] = team;
            PersonRatingDeltas person = default;
            if (personRows is not null && personRows.TryGetValue(row.PlayerId, out PersonRow personRow))
            {
                person = PersonEffects.For(in personRow);
            }
            _slotPersonDeltas[i] = person;
            BakeSlotRatings(i, in person);

            if (row.IsPitcher)
            {
                if (!arsenalParts.TryGetValue(row.PlayerId, out var parts) || parts.Count != 3)
                {
                    throw new InvalidOperationException(
                        $"Pitcher {row.PlayerId} has {parts.Count}/3 Pitch_Arsenals rows — " +
                        "the v4 backfill/generation invariant is broken.");
                }
                _arsenals[i] = new PitcherArsenal(
                    (byte)parts.Fb.Velocity, (byte)parts.Fb.Movement, (byte)parts.Fb.UsageWeight,
                    (byte)parts.Brk.Velocity, (byte)parts.Brk.Movement, (byte)parts.Brk.UsageWeight,
                    (byte)parts.Off.Velocity, (byte)parts.Off.Movement, (byte)parts.Off.UsageWeight);

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
                _teamCandidates[team].Add(new LineupCandidate(
                    i, row.PlayerId, row.BatPower + row.BatContact + row.BatDiscipline,
                    hasFielding ? fielding.DefensivePosition : null,
                    hasFielding ? fielding.SecondaryPosition : null));
            }
        }

        for (int t = 0; t < _teamCount; t++)
        {
            LeagueSimulator.CheckCapacity(teams[t], _teamCandidates[t].Count, starters[t].Count, relievers[t].Count);
            _starterCount[t] = RankPitchers(starters[t], _starterSlots, t * RosterCapacity.MaxStarters);
            _relieverCount[t] = RankPitchers(relievers[t], _relieverSlots, t * RosterCapacity.MaxRelievers);
        }
        _initialized = true;
        RebuildLineups();
    }

    /// <summary>
    /// Marks the one player who always starts (the avatar; 13-2 brief D3): if
    /// the legal-defense pick leaves him out, he replaces the DH. Stored, so
    /// every later re-Initialize honors it too; null clears it.
    /// </summary>
    public void SetFeaturedPlayer(string? playerId)
    {
        _featuredPlayerId = playerId;
        if (_initialized)
        {
            RebuildLineups();
        }
    }

    /// <summary>The calendar day the next <see cref="PlayGame{TBatter, TPitcher}"/> is played on (§6 rest).</summary>
    public void SetGameDay(long day)
    {
        _gameDay = day;
        _gameDaySet = true;
    }

    /// <summary>The Schedule_Games row the next <see cref="PlayGame{TBatter, TPitcher}"/> is (13-3); its result is written by <see cref="FlushGame"/>.</summary>
    public void SetScheduledGame(long gameId) => _scheduledGameId = gameId;

    // 13-4: the next game is a postseason game (brief §4.4). Selection then
    // applies the series no-repeat filter (The Red Dirt Series only:
    // _seriesFirstDay = its game-1 absolute day, else NoSeriesFilter) and
    // counts into the postseason diagnostics, never the regular-season ones
    // (Math record §10.1 Q7 channel 10). Consumed by the next PlayGame.
    private bool _postseasonNext;
    private bool _postseasonGame;
    private long _seriesFirstDay = PostseasonCalendar.NoSeriesFilter;

    /// <summary>Postseason short-rest starts (Q7 channel 10).</summary>
    internal int PostseasonShortRestStarts { get; private set; }

    /// <summary>Postseason bullpen games (Q7 channel 10).</summary>
    internal int PostseasonBullpenGames { get; private set; }

    /// <summary>
    /// Marks the next <see cref="PlayGame{TBatter, TPitcher}"/> as a
    /// postseason game (13-4): <paramref name="seriesFirstDay"/> is The Red
    /// Dirt Series' game-1 absolute day (its no-repeat window), else
    /// <see cref="PostseasonCalendar.NoSeriesFilter"/>.
    /// </summary>
    public void SetPostseasonGame(long seriesFirstDay)
    {
        _postseasonNext = true;
        _seriesFirstDay = seriesFirstDay;
    }

    /// <summary>
    /// True when the team has a legal pitcher for a postseason game on the
    /// day: a starter or reliever who is available and, inside a series
    /// window, hasn't started or opened in it (the narrow forfeit's test,
    /// brief D3). Pure: reads the rest ledger, records nothing.
    /// </summary>
    public bool HasLegalPostseasonPitcher(int teamId, long day, long seriesFirstDay)
    {
        int team = TeamIndexOf(teamId);
        int starterBase = team * RosterCapacity.MaxStarters;
        for (int k = 0; k < _starterCount[team]; k++)
        {
            if (IsLegalInSeries(_starterSlots[starterBase + k], seriesFirstDay, day))
            {
                return true;
            }
        }
        int relieverBase = team * RosterCapacity.MaxRelievers;
        for (int k = 0; k < _relieverCount[team]; k++)
        {
            if (IsLegalInSeries(_relieverSlots[relieverBase + k], seriesFirstDay, day))
            {
                return true;
            }
        }
        return false;
    }

    private bool IsLegalInSeries(int slot, long seriesFirstDay, long day)
    {
        long last = RestLedger.TryGetLastStart(_roster[slot].PlayerId, out long l) ? l : StarterSelection.NeverStarted;
        return !IsShadowed(slot) && !StarterSelection.StartedInSeries(last, seriesFirstDay, day);
    }

    private long ConsumeGameDay()
    {
        if (_gameDaySet)
        {
            _gameDaySet = false;
            return _gameDay;
        }
        return _autoGameDay++;
    }

    /// <summary>
    /// Builds every team's lineup (LineupBuilder, brief D3) and its §3
    /// defense byte. Mean-then-shift-then-teamwork, the exact formula
    /// LeagueSimulator bakes, so the same lineup carries the same defense
    /// byte in both sims.
    /// </summary>
    private void RebuildLineups()
    {
        int featuredSlot = _featuredPlayerId is null ? NoHuman : FindRosterSlot(_featuredPlayerId);
        for (int t = 0; t < _teamCount; t++)
        {
            List<LineupCandidate> candidates = _teamCandidates[t];
            foreach (LineupCandidate c in candidates)
            {
                _slotInLineup[c.Slot] = false;
            }
            int featuredHere = featuredSlot != NoHuman && _slotTeam[featuredSlot] == t ? featuredSlot : -1;
            LineupResult lineup = LineupBuilder.Build(candidates, featuredHere);
            _teamFieldingSum[t] = 0;
            _teamTeamworkSum[t] = 0;
            for (int k = 0; k < RosterCapacity.Lineup; k++)
            {
                int slot = lineup.Slots[k];
                _lineupSlots[t * RosterCapacity.Lineup + k] = slot;
                _slotInLineup[slot] = true;
                _teamFieldingSum[t] += _roster[slot].Fielding;
                _teamTeamworkSum[t] += _slotPersonDeltas[slot].Teamwork;
            }
            _teamDefense[t] = PersonEffects.BakeTeamDefense(
                _teamFieldingSum[t], RosterCapacity.Lineup, _teamTierDeltas[t].Defense, _teamTeamworkSum[t]);
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
    /// §6 starter selection (slice 13-2), the same rule and ledger the macro
    /// sim uses: the best rested starter, else the best-rested on short rest,
    /// else a reliever opens (<paramref name="bullpenUsed"/> then skips past
    /// him). When no pitcher is available at all, the top-ranked pitcher's
    /// slot is returned absent, so the shipped Phase 8c call-up shadows it.
    /// </summary>
    private int SelectPitcher(int team, long day, out int bullpenUsed)
    {
        int minRest = TierRules.MinRestDays(_teamTier[team]);
        int starterCount = _starterCount[team];
        int starterBase = team * RosterCapacity.MaxStarters;
        Span<long> lastStart = stackalloc long[RosterCapacity.MaxStarters];
        Span<bool> available = stackalloc bool[RosterCapacity.MaxStarters];
        // 13-4: The Red Dirt Series' no-repeat filter (all false outside it).
        Span<bool> usedInSeries = stackalloc bool[RosterCapacity.MaxStarters];
        usedInSeries.Clear();
        for (int k = 0; k < starterCount; k++)
        {
            int slot = _starterSlots[starterBase + k];
            lastStart[k] = RestLedger.TryGetLastStart(_roster[slot].PlayerId, out long last)
                ? last : StarterSelection.NeverStarted;
            available[k] = !IsShadowed(slot);
            if (_postseasonGame)
            {
                usedInSeries[k] = StarterSelection.StartedInSeries(lastStart[k], _seriesFirstDay, day);
            }
        }

        bullpenUsed = 0;
        int pick = StarterSelection.SelectStarter(
            lastStart[..starterCount], available[..starterCount], usedInSeries[..starterCount],
            day, minRest, out bool shortRest);
        if (pick >= 0)
        {
            // D5: TierRules.StartCapacityFactor(shortRest) is exactly 1 while
            // the workload knobs ship neutral; applying a non-zero penalty to
            // PitcherFatigue is R1-MATH's (PitcherFatigue.cs is outside 13-2).
            if (shortRest)
            {
                if (_postseasonGame)
                {
                    PostseasonShortRestStarts++;
                }
                else
                {
                    ShortRestStarts++;
                }
            }
            int starter = _starterSlots[starterBase + pick];
            RestLedger.RecordStart(_roster[starter].PlayerId, day);
            return starter;
        }

        int relieverCount = _relieverCount[team];
        int relieverBase = team * RosterCapacity.MaxRelievers;
        Span<bool> relieverAvailable = stackalloc bool[RosterCapacity.MaxRelievers];
        Span<bool> relieverUsed = stackalloc bool[RosterCapacity.MaxRelievers];
        relieverUsed.Clear();
        for (int k = 0; k < relieverCount; k++)
        {
            int slot = _relieverSlots[relieverBase + k];
            relieverAvailable[k] = !IsShadowed(slot);
            if (_postseasonGame)
            {
                long last = RestLedger.TryGetLastStart(_roster[slot].PlayerId, out long l) ? l : StarterSelection.NeverStarted;
                relieverUsed[k] = StarterSelection.StartedInSeries(last, _seriesFirstDay, day);
            }
        }
        int opener = _postseasonGame
            ? StarterSelection.SelectOpener(relieverAvailable[..relieverCount], relieverUsed[..relieverCount])
            : StarterSelection.SelectOpener(relieverAvailable[..relieverCount]);
        if (_postseasonGame)
        {
            PostseasonBullpenGames++;
        }
        else
        {
            BullpenGames++;
        }
        if (opener >= 0)
        {
            bullpenUsed = opener + 1;
            int slot = _relieverSlots[relieverBase + opener];
            RestLedger.RecordStart(_roster[slot].PlayerId, day);
            return slot;
        }
        return starterCount > 0 ? _starterSlots[starterBase] : _relieverSlots[relieverBase];
    }

    /// <summary>
    /// Bakes one slot's batter/pitcher rating structs from its roster base,
    /// its team's tier deltas, and the given §6.2 person deltas — the ONE
    /// bake site <see cref="Initialize"/> and <see cref="RefreshPersonCache"/>
    /// share, so the §6.3 order (base → tier → person, clamped per step in
    /// <see cref="PersonEffects.Bake"/>) can never fork between the season
    /// bake and the per-game refresh.
    /// </summary>
    private void BakeSlotRatings(int slot, in PersonRatingDeltas person)
    {
        ref readonly RosterPlayerRow row = ref _roster[slot];
        TierRatingDeltas tierDeltas = _teamTierDeltas[_slotTeam[slot]];
        _batterRatings[slot] = new BatterRatings(
            PersonEffects.Bake(row.BatPower, tierDeltas.BatPower, person.Confidence),
            PersonEffects.Bake(row.BatContact, tierDeltas.BatContact, person.Happiness),
            TierEffects.Shift(row.BatDiscipline, tierDeltas.BatDiscipline), _pedActive[slot]);
        _pitcherRatings[slot] = new PitcherRatings(
            PersonEffects.Bake(row.PitStuff, tierDeltas.PitStuff, person.Confidence),
            PersonEffects.Bake(row.PitControl, tierDeltas.PitControl, person.Happiness),
            (byte)row.PitStamina);
    }

    /// <summary>
    /// Re-bakes the §6 person deltas for any slot whose committed lever
    /// values moved since Initialize (the HS-6 disclosure's per-game person
    /// refresh) — Version-gated at game start like the rivalry/equipment
    /// caches, all in-memory (micro games run off the main thread; no
    /// mid-game DB reads). A null or empty ledger leaves every baked byte
    /// exactly as Initialize wrote it; entries equal to the baked row are
    /// per-slot no-ops. Because the ledger carries committed absolutes and
    /// this shares <see cref="BakeSlotRatings"/> (and the retained
    /// fielding/teamwork sums) with Initialize, refresh ≡ re-Initialize
    /// whenever ledger and Player_Person agree — which the read-back publish
    /// contract guarantees. That equivalence includes WHO pitches: a moved
    /// pitcher's team re-ranks its starter/reliever tables in place (13-2
    /// review R1). Ids outside the roster are skipped; the linear
    /// <see cref="FindRosterSlot"/> probe is fine here (moved levers are
    /// sparse — typically the avatar — and rebuilds are gated).
    /// </summary>
    private void RefreshPersonCache()
    {
        if (PersonLevers is null || PersonLevers.Version == _personVersionSeen)
        {
            return;
        }
        _personVersionSeen = PersonLevers.Version;

        Span<bool> defenseDirty = stackalloc bool[_teamCount];
        // 13-2 review R1: a pitcher's re-baked stuff/control moves his D2
        // quality, so his team's ranked starter/reliever tables re-sort below
        // — SelectPitcher scans them in rank order, and a re-Initialize ranks
        // after the same bake.
        Span<bool> pitcherOrderDirty = stackalloc bool[_teamCount];
        PersonLevers.CopyAll(_personScratch);
        for (int i = 0; i < _personScratch.Count; i++)
        {
            PersonLeverEntry entry = _personScratch[i];
            int slot = FindRosterSlot(entry.PlayerId);
            if (slot == NoHuman)
            {
                continue;
            }
            PersonRatingDeltas next = PersonEffects.ForLevers(entry.Happiness, entry.Confidence, entry.Teamwork);
            PersonRatingDeltas current = _slotPersonDeltas[slot];
            if (current.Confidence == next.Confidence && current.Happiness == next.Happiness
                && current.Teamwork == next.Teamwork)
            {
                continue;
            }
            // Only lineup slots feed the §6.2 team-defense share (pitchers
            // and bench never summed at Initialize either).
            if (_slotInLineup[slot] && current.Teamwork != next.Teamwork)
            {
                _teamTeamworkSum[_slotTeam[slot]] += next.Teamwork - current.Teamwork;
                defenseDirty[_slotTeam[slot]] = true;
            }
            _slotPersonDeltas[slot] = next;
            PitcherRatings before = _pitcherRatings[slot];
            BakeSlotRatings(slot, in next);
            if (_roster[slot].IsPitcher
                && StarterSelection.Quality(in before) != StarterSelection.Quality(in _pitcherRatings[slot]))
            {
                pitcherOrderDirty[_slotTeam[slot]] = true;
            }
        }
        for (int t = 0; t < _teamCount; t++)
        {
            if (defenseDirty[t])
            {
                _teamDefense[t] = PersonEffects.BakeTeamDefense(
                    _teamFieldingSum[t], RosterCapacity.Lineup,
                    _teamTierDeltas[t].Defense, _teamTeamworkSum[t]);
            }
            if (pitcherOrderDirty[t])
            {
                ReRankPitchers(_starterSlots, t * RosterCapacity.MaxStarters, _starterCount[t]);
                ReRankPitchers(_relieverSlots, t * RosterCapacity.MaxRelievers, _relieverCount[t]);
            }
        }
    }

    /// <summary>
    /// Restores one team's ranked pitcher table to the D2 order in place
    /// after a person refresh re-baked a pitcher (13-2 review R1). Insertion
    /// sort over ≤ <see cref="RosterCapacity.MaxStarters"/> entries:
    /// allocation-free, and because <see cref="StarterSelection.CompareQuality"/>
    /// is a total order (player ids are distinct), it lands exactly the order
    /// <see cref="RankPitchers"/> gives a re-Initialize over the same bake.
    /// </summary>
    private void ReRankPitchers(int[] table, int offset, int count)
    {
        int end = offset + count;
        for (int i = offset + 1; i < end; i++)
        {
            int slot = table[i];
            int j = i - 1;
            while (j >= offset && StarterSelection.CompareQuality(
                in _pitcherRatings[slot], _roster[slot].PlayerId,
                in _pitcherRatings[table[j]], _roster[table[j]].PlayerId) < 0)
            {
                table[j + 1] = table[j];
                j--;
            }
            table[j + 1] = slot;
        }
    }

    /// <summary>Baked team-defense byte (tier + HS-6 §6.2 teamwork lever) — internal so run_monte_carlo_batch pins the defense bake per sim.</summary>
    internal byte TeamDefenseFor(int teamId)
    {
        for (int t = 0; t < _teamCount; t++)
        {
            if (_teamIds[t] == teamId)
            {
                return _teamDefense[t];
            }
        }
        throw new ArgumentException($"Team {teamId} is not in this league.", nameof(teamId));
    }

    /// <summary>Roster slot of a player id, or <see cref="NoHuman"/> — resolve the avatar once, not per PA.</summary>
    public int FindRosterSlot(string playerId)
    {
        for (int i = 0; i < _roster.Length; i++)
        {
            if (string.Equals(_roster[i].PlayerId, playerId, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return NoHuman;
    }

    public void ResetInningTotals() => Array.Clear(InningTotals);

    /// <summary>
    /// Rebuilds the per-slot availability caches for the given absolute day
    /// (Phase 8c). The career driver calls this before playing a pending
    /// attended game — the micro-sim has no calendar of its own. Version- and
    /// day-gated like the macro-sim's refresh; the linear FindRosterSlot
    /// probe is fine here (absences are sparse, rebuilds are gated).
    /// </summary>
    public void RefreshAvailability(long day)
    {
        if (Availability is null)
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
            int slot = FindRosterSlot(entry.PlayerId);
            if (slot == NoHuman)
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

    /// <summary>True when the slot is shadowed by the replacement call-up today.</summary>
    private bool IsShadowed(int slot) => _hasAbsences && _slotUnavailable[slot];

    /// <summary>
    /// The base ratings a pitching slot carries into its fatigue model today:
    /// the team's replacement call-up when absent, rust-docked when
    /// recovering, the baked array value otherwise (exact pre-8c bytes).
    /// </summary>
    private PitcherRatings EffectivePitcherBase(int team, int slot, bool shadowed)
    {
        if (shadowed)
        {
            return _replacementPitcherByTeam[team];
        }
        // Phase 8e: gear boosts the tier-baked value BEFORE any rust dock
        // (equipment_quality.md §5 order) — and before fatigue seeding, so
        // fatigue erodes geared ratings the same way it erodes rusty ones.
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

    /// <summary>Batter-side rust dock (0 = the array value unchanged); PED flag rides along.</summary>
    private static BatterRatings ApplyRust(in BatterRatings b, byte rust) =>
        rust == 0 ? b : new BatterRatings(
            TierEffects.Shift(b.Power, -rust), TierEffects.Shift(b.Contact, -rust),
            TierEffects.Shift(b.Discipline, -rust), b.PedActive);

    /// <summary>
    /// Rebuilds the slot×slot intensity cache when the ledger has moved. Ids
    /// outside the roster are skipped; the linear <see cref="FindRosterSlot"/>
    /// probe is fine here — rebuilds are version-gated and rivalries sparse.
    /// </summary>
    /// <summary>
    /// Rebuilds the per-slot gear-boost cache when the ledger has moved
    /// (Phase 8e). Ids outside the roster are skipped; the linear
    /// <see cref="FindRosterSlot"/> probe is fine here — rebuilds are
    /// version-gated and equipped players sparse.
    /// </summary>
    private void RefreshEquipmentCache()
    {
        if (Equipment is null)
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
            int slot = FindRosterSlot(entry.PlayerId);
            if (slot != NoHuman)
            {
                _slotGearBoost[slot] = EquipmentEffects.BoostFor(entry.Quality);
                _hasEquipment |= _slotGearBoost[slot] > 0;
            }
        }
    }

    private void RefreshRivalryCache()
    {
        if (Rivalries is null)
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
            int a = FindRosterSlot(pair.PlayerAId);
            int b = FindRosterSlot(pair.PlayerBId);
            if (a != NoHuman && b != NoHuman)
            {
                _rivalrySlots[a * slots + b] = pair.Intensity;
                _rivalrySlots[b * slots + a] = pair.Intensity;
                _hasRivalries |= pair.Intensity > 0;
            }
        }
    }

    // ------------------------------------------------------------------
    // The attended game
    // ------------------------------------------------------------------

    /// <summary>
    /// Single-policy convenience: the human (if any) only bats; NPC pitchers
    /// run on the neutral mound autopilot. Existing Phase 5 call sites keep
    /// this shape.
    /// </summary>
    public MicroGameResult PlayGame<TPolicy>(
        int homeTeamId, int awayTeamId, int humanSlot, ref TPolicy policy, ref RngState rng)
        where TPolicy : IBatterPolicy
    {
        var neutralPitcher = new NeutralPitcherPolicy();
        return PlayGame(homeTeamId, awayTeamId, humanSlot, ref policy, ref neutralPitcher, ref rng);
    }

    /// <summary>
    /// Plays one full game. <paramref name="humanSlot"/> is the roster slot of
    /// the player's avatar (<see cref="NoHuman"/> for none); the batter policy
    /// supplies that avatar's per-pitch input when batting, the pitcher policy
    /// its pitch calls when on the mound — the neutral policies for headless
    /// runs (§6.1). Game-flow rules (the home team's tier's regulation
    /// innings, §4; home half skipped when ahead; extras while tied) mirror
    /// the macro-sim exactly; §8.5 bullpen pulls
    /// happen between half-innings off the fatigue multiplier.
    /// </summary>
    public MicroGameResult PlayGame<TBatter, TPitcher>(
        int homeTeamId, int awayTeamId, int humanSlot,
        ref TBatter batterPolicy, ref TPitcher pitcherPolicy, ref RngState rng)
        where TBatter : IBatterPolicy
        where TPitcher : IPitcherPolicy
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("MicroGame.PlayGame before Initialize().");
        }
        RefreshRivalryCache();
        RefreshEquipmentCache();
        RefreshPersonCache();
        int homeTeam = TeamIndexOf(homeTeamId);
        int awayTeam = TeamIndexOf(awayTeamId);
        if (homeTeam == awayTeam)
        {
            throw new ArgumentException("A team cannot host itself.");
        }

        // Reset game-scoped accumulators.
        Array.Clear(_batting);
        Array.Clear(_pitching);
        Array.Clear(_playedThisGame);
        _pendingLogs.Clear();
        _flushHomeTeamId = homeTeamId;
        _flushAwayTeamId = awayTeamId;
        _flushGameId = _scheduledGameId;
        _scheduledGameId = 0;
        // 13-4: postseason context is per game, like the scheduled row.
        _postseasonGame = _postseasonNext;
        _postseasonNext = false;
        if (!_postseasonGame)
        {
            _seriesFirstDay = PostseasonCalendar.NoSeriesFilter;
        }

        // 13-2 (brief D3): the human always starts. A position-player human
        // the legal-defense pick benched becomes the featured player (the DH
        // spot); the rebuild happens once, so warm games stay allocation-free.
        if (humanSlot != NoHuman && !_roster[humanSlot].IsPitcher && !_slotInLineup[humanSlot])
        {
            SetFeaturedPlayer(_roster[humanSlot].PlayerId);
        }

        // §6 starter selection (13-2): the shared rule and rest ledger.
        long gameDay = ConsumeGameDay();
        int homeStarter = SelectPitcher(homeTeam, gameDay, out int homeBullpenUsed);
        int awayStarter = SelectPitcher(awayTeam, gameDay, out int awayBullpenUsed);
        LastStarters = (_roster[homeStarter].PlayerId, _roster[awayStarter].PlayerId);
        int regulationInnings = TierRules.RegulationInnings(_teamTier[homeTeam]);

        // §8/§8.5: starters decay via the fatigue multiplier and hand off to
        // the bullpen when it sinks below the pull threshold. Phase 8c: a
        // shadowed slot's fatigue model runs on the replacement call-up's
        // base ratings (PED never applies to the call-up) and its stats land
        // on the discard line.
        int homePitcher = homeStarter;
        int awayPitcher = awayStarter;
        bool homePitcherShadowed = IsShadowed(homeStarter);
        bool awayPitcherShadowed = IsShadowed(awayStarter);
        int homePitcherStat = homePitcherShadowed ? _roster.Length : homeStarter;
        int awayPitcherStat = awayPitcherShadowed ? _roster.Length : awayStarter;
        PitcherRatings homeBase = EffectivePitcherBase(homeTeam, homeStarter, homePitcherShadowed);
        PitcherRatings awayBase = EffectivePitcherBase(awayTeam, awayStarter, awayPitcherShadowed);
        var homeFatigue = new PitcherFatigue(in homeBase, _pedActive[homeStarter] && !homePitcherShadowed, FatigueEnabled);
        var awayFatigue = new PitcherFatigue(in awayBase, _pedActive[awayStarter] && !awayPitcherShadowed, FatigueEnabled);
        double homeStarterPitches = -1.0;
        double awayStarterPitches = -1.0;
        int homeStarterStat = homePitcherStat;
        int awayStarterStat = awayPitcherStat;
        _pitching[homeStarterStat].G++;
        _pitching[homeStarterStat].Gs++;
        _pitching[awayStarterStat].G++;
        _pitching[awayStarterStat].Gs++;

        int homeScore = 0;
        int awayScore = 0;
        int homeOrder = 0;
        int awayOrder = 0;
        int humanPa = 0;
        int humanPitches = 0;
        int inning;

        for (inning = 1; ; inning++)
        {
            MaybePullPitcher(homeTeam, ref homePitcher, ref homePitcherStat, ref homePitcherShadowed,
                ref homeFatigue, ref homeBullpenUsed, ref homeStarterPitches);
            awayScore += PlayHalfInning(
                awayTeam, homePitcher, homePitcherStat, homePitcherShadowed,
                ref homeFatigue, _teamDefense[homeTeam], ref awayOrder,
                humanSlot, inning, isTopHalf: true, awayScore, homeScore,
                ref batterPolicy, ref pitcherPolicy, ref rng, ref humanPa, ref humanPitches);
            if (inning >= regulationInnings && homeScore > awayScore)
            {
                break;
            }
            MaybePullPitcher(awayTeam, ref awayPitcher, ref awayPitcherStat, ref awayPitcherShadowed,
                ref awayFatigue, ref awayBullpenUsed, ref awayStarterPitches);
            homeScore += PlayHalfInning(
                homeTeam, awayPitcher, awayPitcherStat, awayPitcherShadowed,
                ref awayFatigue, _teamDefense[awayTeam], ref homeOrder,
                humanSlot, inning, isTopHalf: false, awayScore, homeScore,
                ref batterPolicy, ref pitcherPolicy, ref rng, ref humanPa, ref humanPitches);
            if (inning >= regulationInnings && homeScore != awayScore)
            {
                break;
            }
        }

        // Decisions stay with the starters — pitcher-of-record bookkeeping is
        // a deliberate v4 simplification (documented artifact; W/L accounting
        // identities in the harness rely on exactly one W and L per game).
        if (homeScore > awayScore)
        {
            _pitching[homeStarterStat].W++;
            _pitching[awayStarterStat].L++;
        }
        else
        {
            _pitching[awayStarterStat].W++;
            _pitching[homeStarterStat].L++;
        }

        if (homeStarterPitches < 0.0)
        {
            homeStarterPitches = homeFatigue.PitchesThrown;
        }
        if (awayStarterPitches < 0.0)
        {
            awayStarterPitches = awayFatigue.PitchesThrown;
        }

        // §6/§8.4 post-game hook bookkeeping: participants are marked so flush
        // can apply the shared LeagueSimulator.Ped* costs per flagged player.
        // A shadowed slot marks the discard line instead — the absent player
        // did not take the field, so no PED game is ever charged to them.
        for (int i = 0; i < RosterCapacity.Lineup; i++)
        {
            int homeBatter = _lineupSlots[homeTeam * RosterCapacity.Lineup + i];
            int awayBatter = _lineupSlots[awayTeam * RosterCapacity.Lineup + i];
            _playedThisGame[IsShadowed(homeBatter) ? _roster.Length : homeBatter] = true;
            _playedThisGame[IsShadowed(awayBatter) ? _roster.Length : awayBatter] = true;
        }
        _playedThisGame[homeStarterStat] = true;
        _playedThisGame[awayStarterStat] = true;

        if (LoggingEnabled)
        {
            _payloadBuilder.Clear();
            _payloadBuilder.Append("{\"home\":").Append(homeScore)
                .Append(",\"away\":").Append(awayScore)
                .Append(",\"innings\":").Append(inning).Append('}');
            _pendingLogs.Add(new PendingLog(null, "final", _payloadBuilder.ToString()));
        }

        _flushHomeScore = homeScore;
        _flushAwayScore = awayScore;
        _gamePendingFlush = true;
        return new MicroGameResult(
            homeTeamId, awayTeamId, homeScore, awayScore, inning,
            humanPa, humanPitches, homeStarterPitches, awayStarterPitches);
    }

    /// <summary>
    /// §8.5: lifts the current pitcher between half-innings once his fatigue
    /// multiplier drops below <see cref="BullpenPullThreshold"/>, bringing the
    /// next fresh reliever (their own stamina-derived capacity). The starter's
    /// final pitch count is captured on his way out for the game line. An
    /// absent reliever still burns his bullpen turn but the call-up shadows
    /// him: replacement ratings, stats and PED bookkeeping to the discard line.
    /// </summary>
    private void MaybePullPitcher(
        int team, ref int pitcherSlot, ref int pitcherStatSlot, ref bool pitcherShadowed,
        ref PitcherFatigue fatigue, ref int bullpenUsed, ref double starterPitches)
    {
        if (!BullpenEnabled || bullpenUsed >= _relieverCount[team] ||
            fatigue.Multiplier >= BullpenPullThreshold)
        {
            return;
        }
        if (bullpenUsed == 0)
        {
            starterPitches = fatigue.PitchesThrown;
        }
        int reliever = _relieverSlots[team * RosterCapacity.MaxRelievers + bullpenUsed++];
        pitcherSlot = reliever;
        pitcherShadowed = IsShadowed(reliever);
        pitcherStatSlot = pitcherShadowed ? _roster.Length : reliever;
        PitcherRatings relieverBase = EffectivePitcherBase(team, reliever, pitcherShadowed);
        fatigue = new PitcherFatigue(in relieverBase, _pedActive[reliever] && !pitcherShadowed, FatigueEnabled);
        _pitching[pitcherStatSlot].G++;
        _playedThisGame[pitcherStatSlot] = true;
    }

    private int TeamIndexOf(int teamId)
    {
        for (int t = 0; t < _teamCount; t++)
        {
            if (_teamIds[t] == teamId)
            {
                return t;
            }
        }
        throw new ArgumentException($"Unknown team_id {teamId}.");
    }

    /// <summary>
    /// One half-inning of the outer chain. Every PA recomputes the anchor p*
    /// from the pitcher's CURRENT effective ratings (§5.2/§8.3); the pitch
    /// chain runs only when the human bats or pitches (§1).
    /// </summary>
    private int PlayHalfInning<TBatter, TPitcher>(
        int battingTeam, int pitcherSlot, int pitcherStatSlot, bool pitcherShadowed,
        ref PitcherFatigue fatigue, byte defense, ref int orderPos,
        int humanSlot, int inning, bool isTopHalf, int awayScore, int homeScore,
        ref TBatter batterPolicy, ref TPitcher pitcherPolicy, ref RngState rng,
        ref int humanPa, ref int humanPitches)
        where TBatter : IBatterPolicy
        where TPitcher : IPitcherPolicy
    {
        int outs = 0;
        int bases = 0;
        int runs = 0;
        var neutralBatter = new NeutralBatterPolicy();
        var neutralPitcher = new NeutralPitcherPolicy();
        // Hoisted: stackalloc inside the loop would only release at method exit.
        Span<double> anchor = stackalloc double[AtBatResolver.OutcomeCount];

        while (outs < 3)
        {
            int batterSlot = _lineupSlots[battingTeam * RosterCapacity.Lineup + orderPos];
            orderPos = (orderPos + 1) % RosterCapacity.Lineup;

            // Phase 8c: an absent batter's PA plays as the team's replacement
            // call-up with stats discarded; a recovering one bats rust-docked.
            bool batterShadowed = false;
            byte batterRust = 0;
            if (_hasAbsences)
            {
                batterShadowed = _slotUnavailable[batterSlot];
                batterRust = _slotRustPenalty[batterSlot];
            }
            int batterStatSlot = batterShadowed ? _roster.Length : batterSlot;

            PitcherRatings effectivePitcher = fatigue.EffectiveRatings();
            // Phase 6: an active rivalry bends this PA on BOTH paths below —
            // the boost lands on the effective ratings the same way fatigue
            // already does, upstream of the shared resolver, so the human's
            // pitch-chain anchor p* shifts consistently with an NPC macro PA.
            // No rivalry crosses a shadowed slot — the call-up is a stranger.
            byte rivalry = _hasRivalries && !batterShadowed && !pitcherShadowed
                ? _rivalrySlots[batterSlot * _roster.Length + pitcherSlot] : (byte)0;
            // Phase 8e: gear boosts the batter's tier-baked ratings BEFORE any
            // rust dock (equipment_quality.md §5 order); the call-up carries
            // no gear. Boost 0 is the identity — a no-gear PA keeps the exact
            // pre-8e bytes on both the macro and pitch-chain paths.
            byte batterGear = _hasEquipment && !batterShadowed ? _slotGearBoost[batterSlot] : (byte)0;
            BatterRatings batter = batterShadowed
                ? _replacementBatterByTeam[battingTeam]
                : ApplyRust(EquipmentEffects.Batter(in _batterRatings[batterSlot], batterGear), batterRust);
            if (rivalry != 0)
            {
                batter = RivalryEffects.Batter(in batter, rivalry);
                effectivePitcher = RivalryEffects.Pitcher(in effectivePitcher, rivalry);
            }
            PaOutcome outcome;
            int pitches = 0;

            // A shadowed human slot is an NPC PA — the player is in jail /
            // suspended / hurt; the call-up filling their spot macro-resolves,
            // so no policy handshake ever engages for a benched avatar.
            bool humanBatting = batterSlot == humanSlot && !batterShadowed;
            bool humanPitching = pitcherSlot == humanSlot && !pitcherShadowed;
            if (humanBatting || humanPitching)
            {
                // Inner chain (§5): anchor to the shared resolver, invert the
                // absorption equations, then play the count pitch by pitch.
                AtBatResolver.ComputeProbabilities(in batter, in effectivePitcher, defense, anchor);
                PitchClassRates rates = PitchChain.SolveNeutral(
                    anchor[(int)PaOutcome.Walk], anchor[(int)PaOutcome.Strikeout]);
                // A shadowed NPC pitcher throws the fixed replacement arsenal
                // (the human never faces the absent player's real mix).
                PitcherArsenal arsenal = pitcherShadowed ? ReplacementArsenal : _arsenals[pitcherSlot];
                var matchup = new PitchMatchup(
                    in arsenal, effectivePitcher.Control, batter.Discipline, batter.Contact);
                // Runs already scored this half count toward the batting side.
                var context = new HumanPaContext(
                    awayScore + (isTopHalf ? runs : 0), homeScore + (isTopHalf ? 0 : runs),
                    inning, isTopHalf, outs, bases, in effectivePitcher);
                if (humanBatting)
                {
                    batterPolicy.BeginPa(in context);
                    outcome = PitchChain.SimulatePa(
                        anchor, in rates, in matchup, ref batterPolicy, ref neutralPitcher,
                        ref fatigue, ref rng, out pitches);
                    batterPolicy.OnPaResolved(outcome);
                    humanPa++;
                    humanPitches += pitches;
                }
                else
                {
                    pitcherPolicy.BeginPa(in context);
                    outcome = PitchChain.SimulatePa(
                        anchor, in rates, in matchup, ref neutralBatter, ref pitcherPolicy,
                        ref fatigue, ref rng, out pitches);
                    pitcherPolicy.OnPaResolved(outcome);
                }
            }
            else
            {
                outcome = AtBatResolver.Resolve(in batter, in effectivePitcher, defense, ref rng);
                fatigue.AddNpcPa();
            }

            ref BattingLine bat = ref _batting[batterStatSlot];
            ref PitchingLine pit = ref _pitching[pitcherStatSlot];
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

            AccumulateInning(inning, outcome);

            if (LoggingEnabled)
            {
                AppendPaLog(inning, isTopHalf, batterSlot, batterShadowed, pitcherSlot, pitcherShadowed, outcome, paRuns, pitches);
            }
            // NPC feed: the human's own PA already renders via the bridge's
            // outcome queue ("Your PA: ..."), so only queue the others here.
            // A shadowed human slot IS an NPC PA (the call-up bats) — feed it.
            if (FeedSink != null && (batterSlot != humanSlot || batterShadowed))
            {
                FeedSink.PublishNpcPa(new NpcPaFeedEvent(
                    batterShadowed ? ReplacementDisplayName : _displayNames[batterSlot],
                    outcome, inning, isTopHalf, paRuns, rivalry != 0));
            }
        }
        return runs;
    }

    private void AccumulateInning(int inning, PaOutcome outcome)
    {
        ref InningLine line = ref InningTotals[Math.Min(inning, MaxTrackedInnings) - 1];
        line.Pa++;
        switch (outcome)
        {
            case PaOutcome.Walk:
                line.Bb++;
                break;
            case PaOutcome.Out:
                line.Ab++;
                break;
            case PaOutcome.Strikeout:
                line.Ab++;
                line.So++;
                break;
            case PaOutcome.Single:
                line.Ab++;
                line.H++;
                break;
            case PaOutcome.Double:
                line.Ab++;
                line.H++;
                line.Doubles++;
                break;
            case PaOutcome.Triple:
                line.Ab++;
                line.H++;
                line.Triples++;
                break;
            default:
                line.Ab++;
                line.H++;
                line.Hr++;
                break;
        }
    }

    /// <summary>
    /// §10: payloads are built at PA boundaries, never inside the pitch loop.
    /// A shadowed slot's PA is attributed to no one (null player_id, the
    /// league-level convention) and names "replacement" as the pitcher — the
    /// absent player must never appear to have played.
    /// </summary>
    private void AppendPaLog(
        int inning, bool isTopHalf, int batterSlot, bool batterShadowed,
        int pitcherSlot, bool pitcherShadowed, PaOutcome outcome, int runs, int pitches)
    {
        _payloadBuilder.Clear();
        _payloadBuilder.Append("{\"inning\":").Append(inning)
            .Append(",\"half\":\"").Append(isTopHalf ? "top" : "bottom")
            .Append("\",\"pitcher\":\"").Append(pitcherShadowed ? "replacement" : _roster[pitcherSlot].PlayerId)
            .Append("\",\"outcome\":\"").Append(OutcomeName(outcome))
            .Append("\",\"runs\":").Append(runs);
        if (pitches > 0)
        {
            _payloadBuilder.Append(",\"pitches\":").Append(pitches);
        }
        _payloadBuilder.Append('}');
        _pendingLogs.Add(new PendingLog(batterShadowed ? null : _roster[batterSlot].PlayerId, "pa", _payloadBuilder.ToString()));
    }

    private static string OutcomeName(PaOutcome outcome) => outcome switch
    {
        PaOutcome.Out => "Out",
        PaOutcome.Strikeout => "Strikeout",
        PaOutcome.Walk => "Walk",
        PaOutcome.Single => "Single",
        PaOutcome.Double => "Double",
        PaOutcome.Triple => "Triple",
        _ => "HomeRun",
    };

    // ------------------------------------------------------------------
    // Flush — the micro-sim's own batch, never the calendar tick's
    // ------------------------------------------------------------------

    /// <summary>
    /// Writes the last played game — additive box-score upserts, PED post-game
    /// costs (shared LeagueSimulator.Ped* constants), and the buffered
    /// play-by-play — in one transaction, then clears the pending state.
    /// </summary>
    public void FlushGame(int seasonYear, int gameDay) => FlushGame(seasonYear, gameDay, null);

    /// <summary>
    /// The flush with a 13-4 postseason hook. A postseason game (marked by
    /// <see cref="SetPostseasonGame"/>) writes NO Batting_Stats /
    /// Pitching_Stats rows (brief D4-A, Amendment 1) yet still commits its
    /// PED costs, Game_Logs, rest and schedule result; <paramref name="inBatch"/>
    /// then runs inside the same transaction (the series update), so the
    /// result and its series effects commit together or not at all.
    /// </summary>
    public void FlushGame(int seasonYear, int gameDay, Action<int, int>? inBatch)
    {
        if (!_gamePendingFlush)
        {
            throw new InvalidOperationException("No played game pending flush.");
        }
        bool writeStats = !_postseasonGame;

        _db.BeginBatch();
        try
        {
            for (int i = 0; i < _roster.Length; i++)
            {
                ref readonly BattingLine bat = ref _batting[i];
                if (writeStats && bat.Pa > 0)
                {
                    _queries.AddBattingGameCounts(
                        _roster[i].PlayerId, seasonYear,
                        bat.Pa, bat.Ab, bat.H, bat.Doubles, bat.Triples, bat.Hr, bat.Bb, bat.So, bat.Rbi, sb: 0);
                }

                ref readonly PitchingLine pit = ref _pitching[i];
                if (writeStats && pit.G > 0)
                {
                    _queries.AddPitchingGameCounts(
                        _roster[i].PlayerId, seasonYear,
                        pit.G, pit.Gs, pit.W, pit.L, sv: 0, pit.Outs, pit.HAllowed, pit.Er, pit.Bb, pit.So);
                }

                if (_playedThisGame[i] && _pedActive[i])
                {
                    _queries.ApplyPedGameCosts(
                        _roster[i].PlayerId,
                        LeagueSimulator.PedHealthCostPerGame,
                        LeagueSimulator.PedDetectionRiskPerGame);
                }
            }

            foreach (PendingLog log in _pendingLogs)
            {
                _queries.InsertGameLog(
                    seasonYear, gameDay, _flushHomeTeamId, _flushAwayTeamId,
                    log.PlayerId, log.EventType, log.Payload);
            }
            // 13-2 (brief D4): the attended game's starts ride its own batch.
            RestLedger.FlushDirty(_queries);
            // 13-3 (brief D4): and so does its schedule row's final score.
            if (_flushGameId != 0)
            {
                _scheduleQueries.RecordFinal(_flushGameId, _flushHomeScore, _flushAwayScore);
            }
            inBatch?.Invoke(_flushHomeScore, _flushAwayScore);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            RestLedger.AbandonFlush(); // review R2: rolled back, so the rows stay dirty
            throw;
        }
        RestLedger.AcknowledgeFlush(); // review R2: durable only once committed

        Array.Clear(_batting);
        Array.Clear(_pitching);
        Array.Clear(_playedThisGame);
        _pendingLogs.Clear();
        _gamePendingFlush = false;
    }
}
