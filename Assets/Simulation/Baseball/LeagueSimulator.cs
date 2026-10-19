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
    /// <summary>
    /// The interim season window (slice 13-2, brief D1-A): 154 game days, one
    /// round-robin round per day for any even team count (12/24/32 since
    /// schema v14). Slice 13-3 replaces it with persisted per-tier schedules
    /// (league_restructure.md §3).
    /// </summary>
    public const int RegularSeasonDays = 154;

    /// <summary>Stats (and the rest ledger) flush every this many season days — the shipped 7-day cadence.</summary>
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

    /// <summary>
    /// The nameless replacement-level call-up who shadows an absent player's
    /// slot: every rating this many points below league average (50), before
    /// tier shifting. A calibration constant like the tier/rivalry deltas —
    /// tuning is a data edit behind run_monte_carlo_batch.
    /// </summary>
    public const int ReplacementRating = 40;

    // Per-slot availability caches rebuilt when the ledger's Version OR the
    // day moves (absences expire by the calendar, not by an event); the
    // per-PA cost is one bool read when any absence is live, one false
    // branch when none is.
    private bool[] _slotUnavailable = Array.Empty<bool>();
    private byte[] _slotRustPenalty = Array.Empty<byte>();
    private readonly List<AbsenceEntry> _absenceScratch = new();
    private int _absenceVersionSeen = -1;
    private long _absenceDaySeen = -1;
    private bool _hasAbsences;
    private BatterRatings _replacementBatter;
    private PitcherRatings _replacementPitcher;

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
                $"League tier {Tier} needs an even team count from 2 to {RosterCapacity.MaxTeamsPerTier} " +
                $"for the interim round-robin; found {teams.Count}. Run LeagueGenerator first.");
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
        _slotGearBoost = new byte[slots];
        _equipmentVersionSeen = -1; // roster changed — force a cache rebuild
        _hasEquipment = false;
        _batterRatings = new BatterRatings[slots];
        _pitcherRatings = new PitcherRatings[slots];
        _pedActive = new bool[slots];
        _pedGamesPlayed = new int[slots];
        // One extra line past the roster: the discard row replacement-level
        // call-ups accumulate into. FlushAccumulated loops the roster length
        // only, so nothing a nameless call-up does ever reaches the database.
        _batting = new BattingLine[slots + 1];
        _pitching = new PitchingLine[slots + 1];
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
        var teamworkBySlot = new int[slots];

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

        // Phase 8c: the replacement-level call-up plays in this league's
        // environment too, so its ratings tier-shift exactly like a rostered
        // player's.
        _replacementBatter = new BatterRatings(
            TierEffects.Shift(ReplacementRating, tierDeltas.BatPower),
            TierEffects.Shift(ReplacementRating, tierDeltas.BatContact),
            TierEffects.Shift(ReplacementRating, tierDeltas.BatDiscipline), pedActive: false);
        _replacementPitcher = new PitcherRatings(
            TierEffects.Shift(ReplacementRating, tierDeltas.PitStuff),
            TierEffects.Shift(ReplacementRating, tierDeltas.PitControl), (byte)ReplacementRating);

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
                positionPlayers[team].Add(new LineupCandidate(
                    i, row.PlayerId, row.BatPower + row.BatContact + row.BatDiscipline,
                    hasFielding ? fielding.DefensivePosition : null,
                    hasFielding ? fielding.SecondaryPosition : null));
            }
        }

        OutOfPositionStarters = 0;
        for (int t = 0; t < _teamCount; t++)
        {
            CheckCapacity(teams[t], positionPlayers[t].Count, starters[t].Count, relievers[t].Count);

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
        bus.Subscribe(_onDayAdvanced);
        bus.Subscribe(_onSeasonRolledOver);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onDayAdvanced);
        bus.Unsubscribe(_onSeasonRolledOver);
    }

    // ------------------------------------------------------------------
    // Bus handlers
    // ------------------------------------------------------------------

    private void OnDayAdvanced(DayAdvancedEvent e)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("LeagueSimulator received a day event before Initialize().");
        }
        if (e.DayOfSeason > RegularSeasonDays)
        {
            return; // offseason
        }

        // HS plays a realistic sparse spring schedule (HsSeasonCalendar);
        // every other tier maps identity (scheduleDay == dayOfSeason), so
        // their rng draw order stays bit-for-bit what it always was.
        if (HsSeasonCalendar.TryGetLeagueScheduleDay(Tier, e.DayOfSeason, out int scheduleDay))
        {
            RefreshAvailability(e.Day);
            SimulateGameDay(scheduleDay, e.Day);
            _unflushedSeasonYear = e.SeasonYear;
        }

        // Flush every 7 season days (154 is a multiple of 7, so the last day
        // is covered). Keeping the database at most a week stale matters once
        // the player's own stats are on the line; additive upserts make the
        // cadence safe. Runs on HS off days too — FlushAccumulated row-guards
        // on Pa/G > 0, so an empty flush writes nothing.
        if (e.DayOfSeason % FlushCadenceDays == 0)
        {
            FlushAccumulated(e.SeasonYear);
        }
    }

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
    /// Plays one season day's games from the shared <see cref="LeagueSchedule"/>.
    /// The attended team's pairing is skipped: that game is played in the
    /// micro-sim, which records its starters in the shared rest ledger.
    /// Internal so run_monte_carlo_batch can drive and profile it directly;
    /// this overload treats the season day as the absolute day.
    /// </summary>
    internal void SimulateGameDay(int dayOfSeason) => SimulateGameDay(dayOfSeason, dayOfSeason);

    /// <summary>
    /// <paramref name="absoluteDay"/> is the calendar day §6 rest is counted
    /// on (the event's <c>Day</c>), so rest carries across seasons.
    /// </summary>
    internal void SimulateGameDay(int dayOfSeason, long absoluteDay)
    {
        RefreshRivalryCache();
        RefreshEquipmentCache();
        Span<SchedulePairing> pairings = stackalloc SchedulePairing[RosterCapacity.MaxTeamsPerTier / 2];
        int pairingCount = _teamCount / 2;
        LeagueSchedule.GetDayPairings(_teamCount, dayOfSeason, pairings[..pairingCount]);

        for (int p = 0; p < pairingCount; p++)
        {
            SchedulePairing pairing = pairings[p];
            if (pairing.HomeTeam == _attendedTeamIndex || pairing.AwayTeam == _attendedTeamIndex)
            {
                continue;
            }
            PlayGame(pairing.HomeTeam, pairing.AwayTeam, absoluteDay);
        }
    }

    /// <summary>
    /// §6 starter selection for one team on one day (slice 13-2): the shared
    /// <see cref="StarterSelection"/> rule over this team's starters in
    /// quality order, else a reliever opens (a bullpen game). The pick's start
    /// is recorded in the rest ledger. When no pitcher at all is available,
    /// the shipped Phase 8c replacement call-up shadows the top-ranked
    /// pitcher's slot (<paramref name="shadowed"/>; forfeits are 13-5's).
    /// Allocation-free: stack scratch sized by capacity.
    /// </summary>
    private int SelectPitcher(int team, long day, out bool shadowed)
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
            available[k] = !(_hasAbsences && _slotUnavailable[slot]);
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
            shadowed = false;
            return starter;
        }

        int relieverCount = _relieverCount[team];
        int relieverBase = team * RosterCapacity.MaxRelievers;
        Span<bool> relieverAvailable = stackalloc bool[RosterCapacity.MaxRelievers];
        for (int k = 0; k < relieverCount; k++)
        {
            int slot = _relieverSlots[relieverBase + k];
            relieverAvailable[k] = !(_hasAbsences && _slotUnavailable[slot]);
        }
        int opener = StarterSelection.SelectOpener(relieverAvailable[..relieverCount]);
        BullpenGames++;
        if (opener >= 0)
        {
            int slot = _relieverSlots[relieverBase + opener];
            RestLedger.RecordStart(_roster[slot].PlayerId, day);
            shadowed = false;
            return slot;
        }

        shadowed = true;
        return starterCount > 0 ? _starterSlots[starterBase] : _relieverSlots[relieverBase];
    }

    private void PlayGame(int homeTeam, int awayTeam, long day)
    {
        int homeStarter = SelectPitcher(homeTeam, day, out bool homeStarterOut);
        int awayStarter = SelectPitcher(awayTeam, day, out bool awayStarterOut);
        int homeStarterStat = homeStarterOut ? _roster.Length : homeStarter;
        int awayStarterStat = awayStarterOut ? _roster.Length : awayStarter;
        PitcherRatings homePitcher = EffectivePitcher(homeStarter, homeStarterOut);
        PitcherRatings awayPitcher = EffectivePitcher(awayStarter, awayStarterOut);

        // Complete games for both starters — bullpens arrive with the Phase 4
        // micro-sim's stamina model.
        _pitching[homeStarterStat].G++;
        _pitching[homeStarterStat].Gs++;
        _pitching[awayStarterStat].G++;
        _pitching[awayStarterStat].Gs++;

        int homeScore = 0;
        int awayScore = 0;
        int homeOrder = 0;
        int awayOrder = 0;

        for (int inning = 1; ; inning++)
        {
            awayScore += PlayHalfInning(
                awayTeam, homeStarter, homeStarterStat, in homePitcher, homeStarterOut,
                _teamDefense[homeTeam], ref awayOrder);
            if (inning >= _regulationInnings && homeScore > awayScore)
            {
                break; // home leads after the top of the final regulation inning (or later) — no bottom half
            }
            homeScore += PlayHalfInning(
                homeTeam, awayStarter, awayStarterStat, in awayPitcher, awayStarterOut,
                _teamDefense[awayTeam], ref homeOrder);
            if (inning >= _regulationInnings && homeScore != awayScore)
            {
                break; // decided in the final regulation inning or extras; ties play on
            }
        }

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

        // §6 post-game hook bookkeeping: every flagged participant logs a game
        // under the influence; the DB costs apply at flush.
        for (int i = 0; i < RosterCapacity.Lineup; i++)
        {
            CountPedGame(_lineupSlots[homeTeam * RosterCapacity.Lineup + i]);
            CountPedGame(_lineupSlots[awayTeam * RosterCapacity.Lineup + i]);
        }
        CountPedGame(homeStarter);
        CountPedGame(awayStarter);
    }

    private void CountPedGame(int slot)
    {
        // An absent (shadowed) player did not take the field — no game under
        // the influence, no PED cost. Rusty players play and pay as normal.
        if (_pedActive[slot] && !(_hasAbsences && _slotUnavailable[slot]))
        {
            _pedGamesPlayed[slot]++;
        }
    }

    /// <summary>
    /// The ratings a pitching slot actually throws with today: the
    /// replacement call-up when absent, rust-docked when recovering from an
    /// injury, the baked array value otherwise (the exact pre-8c bytes).
    /// </summary>
    private PitcherRatings EffectivePitcher(int slot, bool shadowed)
    {
        if (shadowed)
        {
            return _replacementPitcher;
        }
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
    /// The pitcher's effective ratings and stat line are the caller's picks
    /// (Phase 8c: a shadowed starter throws replacement stuff into the
    /// discard line); an absent batter's PA plays as the replacement call-up
    /// with stats discarded, and neither side of a shadowed matchup carries a
    /// rivalry (the call-up is a different person).
    /// </summary>
    private int PlayHalfInning(
        int battingTeam, int pitcherSlot, int pitcherStatSlot, in PitcherRatings pitcher, bool pitcherShadowed,
        byte defense, ref int orderPos)
    {
        int outs = 0;
        int bases = 0;
        int runs = 0;

        while (outs < 3)
        {
            int batterSlot = _lineupSlots[battingTeam * RosterCapacity.Lineup + orderPos];
            orderPos = (orderPos + 1) % RosterCapacity.Lineup;

            bool batterShadowed = false;
            byte batterRust = 0;
            if (_hasAbsences)
            {
                batterShadowed = _slotUnavailable[batterSlot];
                batterRust = _slotRustPenalty[batterSlot];
            }
            int batterStatSlot = batterShadowed ? _roster.Length : batterSlot;

            // Phase 6: an active batter-pitcher rivalry bends this PA through
            // effective ratings (RivalryEffects), same resolver. Intensity 0
            // takes the exact pre-rivalry call — bit-identical M1 lines.
            byte rivalry = _hasRivalries && !batterShadowed && !pitcherShadowed
                ? _rivalrySlots[batterSlot * _roster.Length + pitcherSlot] : (byte)0;
            // Phase 8e: gear boosts the batter's tier-baked ratings BEFORE any
            // rust dock (equipment_quality.md §5 order). A shadowed slot's
            // call-up carries no gear — a stranger, the same rule that stops
            // rivalries crossing a shadowed slot. Boost 0 keeps the exact
            // pre-8e fast path.
            byte batterGear = _hasEquipment && !batterShadowed ? _slotGearBoost[batterSlot] : (byte)0;
            PaOutcome outcome;
            if (rivalry == 0 && !batterShadowed && batterRust == 0 && batterGear == 0)
            {
                outcome = AtBatResolver.Resolve(
                    in _batterRatings[batterSlot], in pitcher, defense, ref _rng);
            }
            else
            {
                BatterRatings paBatter = batterShadowed
                    ? _replacementBatter
                    : ApplyRust(EquipmentEffects.Batter(in _batterRatings[batterSlot], batterGear), batterRust);
                PitcherRatings paPitcher = pitcher;
                if (rivalry != 0)
                {
                    paBatter = RivalryEffects.Batter(in paBatter, rivalry);
                    paPitcher = RivalryEffects.Pitcher(in pitcher, rivalry);
                }
                outcome = AtBatResolver.Resolve(in paBatter, in paPitcher, defense, ref _rng);
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
                    paRuns = BaseOutAdvancement.AdvanceSingle(ref bases, ref _rng);
                    break;
                case PaOutcome.Double:
                    bat.Ab++;
                    bat.H++;
                    bat.Doubles++;
                    pit.HAllowed++;
                    paRuns = BaseOutAdvancement.AdvanceDouble(ref bases, ref _rng);
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
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        // Rates are denormalized after the counting-stat batch, in the
        // normalizer's own transaction — tier-scoped (9a) so six sims flushing
        // the same day each rewrite only their own league's rows.
        _normalizer.NormalizeSeason(seasonYear, Tier);

        Array.Clear(_batting);
        Array.Clear(_pitching);
        Array.Clear(_pedGamesPlayed);
        _unflushedSeasonYear = 0;
    }
}
