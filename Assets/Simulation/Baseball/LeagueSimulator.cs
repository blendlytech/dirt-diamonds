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
    public const int TeamCount = 8;
    public const int LineupSize = 9;
    public const int RotationSize = 5;

    /// <summary>Relievers per team (schema v4). Micro-sim only — the macro-sim
    /// stays complete-game (its §8 calibration is pinned to that shape).</summary>
    public const int BullpenSize = 3;
    public const int RosterSizePerTeam = LineupSize + RotationSize + BullpenSize;

    /// <summary>7-day round-robin cycles × 22 = a 154-game season for every team.</summary>
    public const int RegularSeasonDays = RoundsPerCycle * CyclesPerSeason;
    internal const int RoundsPerCycle = TeamCount - 1;
    private const int CyclesPerSeason = 22;

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

    // Team-indexed (0..TeamCount-1, ordered by team_id): flat lineup/rotation
    // slot tables plus the §3 precomputed team-defense rating.
    private int[] _lineupSlots = Array.Empty<int>();
    private int[] _rotationSlots = Array.Empty<int>();
    private byte[] _teamDefense = Array.Empty<byte>();
    private int[] _teamGamesPlayed = Array.Empty<int>();

    private bool _initialized;

    /// <summary>
    /// Rivalry intensity source (Phase 6), same optional-attachment pattern as
    /// <see cref="MicroGame.FeedSink"/>: null — the default for every existing
    /// harness path — leaves the per-PA hot path bit-identical to the
    /// pre-rivalry code (a single false branch), so the M1 season lines cannot
    /// move unless a rivalry actually exists.
    /// </summary>
    public RivalryLedger? Rivalries;

    // Flat slot×slot intensity cache rebuilt only when the ledger's Version
    // moves (rivalries change at day granularity at most; PAs read one byte).
    private byte[] _rivalrySlots = Array.Empty<byte>();
    private Dictionary<string, int>? _slotByPlayerId;
    private readonly List<RivalryPair> _rivalryScratch = new();
    private int _rivalryVersionSeen = -1;
    private bool _hasRivalries;

    /// <summary>Season year with simulated-but-unflushed games; 0 = clean.</summary>
    private int _unflushedSeasonYear;

    /// <summary>
    /// Team index whose games the career driver owns (-1 = none). Every pairing
    /// containing this team is skipped by the macro sim — the attended-game
    /// micro-sim plays and flushes it instead — but the team's rotation counter
    /// still advances so the unsuppressed path stays bit-identical.
    /// </summary>
    private int _attendedTeamIndex = -1;

    private int[] _teamIds = Array.Empty<int>();

    /// <summary>
    /// The ladder tier this instance simulates (Phase 9a). One simulator per
    /// tier; each bulk-loads only its own 8-team league and bakes its tier's
    /// <see cref="TierEffects"/> deltas into the rating arrays at Initialize.
    /// Defaults to MLB so every pre-9a construction site (and harness fixture)
    /// keeps its exact behavior — the MLB delta vector is all-zero by contract.
    /// </summary>
    public LeagueTier Tier { get; }

    public LeagueSimulator(DatabaseManager db, BaseballQueries queries, StatsNormalizer normalizer, RngState rng,
        LeagueTier tier = LeagueTier.MLB)
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
        var teams = new List<TeamRow>(TeamCount);
        _queries.LoadTeamsByTier(Tier, teams);
        if (teams.Count != TeamCount)
        {
            throw new InvalidOperationException(
                $"League tier {Tier} requires exactly {TeamCount} teams; found {teams.Count}. " +
                "Run LeagueGenerator (and EnsureTierLeagues) first.");
        }

        var rosterRows = new List<RosterPlayerRow>(TeamCount * RosterSizePerTeam);
        _queries.LoadRosterByTier(Tier, rosterRows);
        _roster = rosterRows.ToArray();

        var pedIds = new List<string>();
        _queries.LoadActiveFlagPlayerIds(PedActiveFlagName, pedIds);
        var pedSet = new HashSet<string>(pedIds, StringComparer.Ordinal);

        var teamIndexById = new Dictionary<int, int>(TeamCount);
        _teamIds = new int[TeamCount];
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
        _batterRatings = new BatterRatings[slots];
        _pitcherRatings = new PitcherRatings[slots];
        _pedActive = new bool[slots];
        _pedGamesPlayed = new int[slots];
        _batting = new BattingLine[slots];
        _pitching = new PitchingLine[slots];
        _lineupSlots = new int[TeamCount * LineupSize];
        _rotationSlots = new int[TeamCount * RotationSize];
        _teamDefense = new byte[TeamCount];
        _teamGamesPlayed = new int[TeamCount];

        Span<int> lineupCount = stackalloc int[TeamCount];
        Span<int> rotationCount = stackalloc int[TeamCount];
        Span<int> fieldingSum = stackalloc int[TeamCount];

        // Phase 9a: the tier's league-environment deltas bake into the rating
        // arrays here, once — the per-PA hot path is untouched, and rivalry/PED
        // effects layer on top of the tier-shifted values exactly as they
        // layered on the raw ones. MLB's vector is all-zero, so an MLB world's
        // arrays are bit-identical to the pre-tier code.
        TierRatingDeltas tierDeltas = TierEffects.For(Tier);

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
            _batterRatings[i] = new BatterRatings(
                TierEffects.Shift(row.BatPower, tierDeltas.BatPower),
                TierEffects.Shift(row.BatContact, tierDeltas.BatContact),
                TierEffects.Shift(row.BatDiscipline, tierDeltas.BatDiscipline), ped);
            _pitcherRatings[i] = new PitcherRatings(
                TierEffects.Shift(row.PitStuff, tierDeltas.PitStuff),
                TierEffects.Shift(row.PitControl, tierDeltas.PitControl), (byte)row.PitStamina);

            if (row.IsPitcher)
            {
                if (row.Role == PitcherRole.None)
                {
                    throw new InvalidOperationException(
                        $"Pitcher {row.PlayerId} has no Pitcher_Roles row — the v4 backfill/generation invariant is broken.");
                }
                // Rotation = starters only; relievers are a micro-sim concern
                // (the macro-sim's complete-game shape is untouched by v4, so
                // its calibration — and the M1 lines — cannot move).
                if (row.Role == PitcherRole.Starter && rotationCount[team] < RotationSize)
                {
                    _rotationSlots[team * RotationSize + rotationCount[team]++] = i;
                }
            }
            else if (lineupCount[team] < LineupSize)
            {
                _lineupSlots[team * LineupSize + lineupCount[team]++] = i;
                fieldingSum[team] += row.Fielding;
            }
        }

        for (int t = 0; t < TeamCount; t++)
        {
            if (lineupCount[t] < LineupSize || rotationCount[t] < RotationSize)
            {
                throw new InvalidOperationException(
                    $"Team {teams[t].TeamId} ({teams[t].Abbreviation}) has {lineupCount[t]}/{LineupSize} " +
                    $"position players and {rotationCount[t]}/{RotationSize} starters — cannot field a season.");
            }
            // §3: team defense = mean fielding of the lineup, rounded, then
            // tier-shifted (9a — a zero delta is the exact pre-tier value).
            _teamDefense[t] = TierEffects.Shift(
                (fieldingSum[t] + LineupSize / 2) / LineupSize, tierDeltas.Defense);
        }

        _initialized = true;
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

        SimulateGameDay(e.DayOfSeason);
        _unflushedSeasonYear = e.SeasonYear;

        // Flush at the end of every 7-day round-robin cycle (RegularSeasonDays
        // is a multiple of RoundsPerCycle, so day 154 is covered). Keeping the
        // database at most a cycle stale matters once the player's own stats
        // are on the line; additive upserts make the cadence safe.
        if (e.DayOfSeason % RoundsPerCycle == 0)
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
    /// Plays one season day's games from the shared <see cref="LeagueSchedule"/>
    /// (same pairing order the pre-extraction code produced, so rng draw order
    /// — and every M1 season line — is bit-identical). The attended team's
    /// pairing is skipped but its rotation clock still ticks: that game is
    /// played, in the micro-sim. Internal so run_monte_carlo_batch can drive
    /// and profile it directly.
    /// </summary>
    internal void SimulateGameDay(int dayOfSeason)
    {
        RefreshRivalryCache();
        Span<SchedulePairing> pairings = stackalloc SchedulePairing[LeagueSchedule.PairingsPerDay];
        LeagueSchedule.GetDayPairings(dayOfSeason, pairings);

        foreach (SchedulePairing pairing in pairings)
        {
            if (pairing.HomeTeam == _attendedTeamIndex || pairing.AwayTeam == _attendedTeamIndex)
            {
                _teamGamesPlayed[pairing.HomeTeam]++;
                _teamGamesPlayed[pairing.AwayTeam]++;
                continue;
            }
            PlayGame(pairing.HomeTeam, pairing.AwayTeam);
        }
    }

    private void PlayGame(int homeTeam, int awayTeam)
    {
        int homeStarter = _rotationSlots[homeTeam * RotationSize + _teamGamesPlayed[homeTeam] % RotationSize];
        int awayStarter = _rotationSlots[awayTeam * RotationSize + _teamGamesPlayed[awayTeam] % RotationSize];
        _teamGamesPlayed[homeTeam]++;
        _teamGamesPlayed[awayTeam]++;

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
            awayScore += PlayHalfInning(awayTeam, homeStarter, _teamDefense[homeTeam], ref awayOrder);
            if (inning >= 9 && homeScore > awayScore)
            {
                break; // home leads after the top of the 9th (or later) — no bottom half
            }
            homeScore += PlayHalfInning(homeTeam, awayStarter, _teamDefense[awayTeam], ref homeOrder);
            if (inning >= 9 && homeScore != awayScore)
            {
                break; // decided in the 9th or extras; ties play on
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

        // §6 post-game hook bookkeeping: every flagged participant logs a game
        // under the influence; the DB costs apply at flush.
        for (int i = 0; i < LineupSize; i++)
        {
            CountPedGame(_lineupSlots[homeTeam * LineupSize + i]);
            CountPedGame(_lineupSlots[awayTeam * LineupSize + i]);
        }
        CountPedGame(homeStarter);
        CountPedGame(awayStarter);
    }

    private void CountPedGame(int slot)
    {
        if (_pedActive[slot])
        {
            _pedGamesPlayed[slot]++;
        }
    }

    /// <summary>
    /// Rebuilds the slot×slot intensity cache when the ledger has moved.
    /// Ids the ledger knows but the roster doesn't (retired/free agents) are
    /// simply skipped. Runs at day granularity, never per PA; the scratch
    /// list reuse keeps even the rebuild allocation-free once warm.
    /// </summary>
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
    /// </summary>
    private int PlayHalfInning(int battingTeam, int pitcherSlot, byte defense, ref int orderPos)
    {
        PitcherRatings pitcher = _pitcherRatings[pitcherSlot];
        int outs = 0;
        int bases = 0;
        int runs = 0;

        while (outs < 3)
        {
            int batterSlot = _lineupSlots[battingTeam * LineupSize + orderPos];
            orderPos = (orderPos + 1) % LineupSize;

            // Phase 6: an active batter-pitcher rivalry bends this PA through
            // effective ratings (RivalryEffects), same resolver. Intensity 0
            // takes the exact pre-rivalry call — bit-identical M1 lines.
            byte rivalry = _hasRivalries ? _rivalrySlots[batterSlot * _roster.Length + pitcherSlot] : (byte)0;
            PaOutcome outcome;
            if (rivalry == 0)
            {
                outcome = AtBatResolver.Resolve(
                    in _batterRatings[batterSlot], in pitcher, defense, ref _rng);
            }
            else
            {
                BatterRatings rivalBatter = RivalryEffects.Batter(in _batterRatings[batterSlot], rivalry);
                PitcherRatings rivalPitcher = RivalryEffects.Pitcher(in pitcher, rivalry);
                outcome = AtBatResolver.Resolve(in rivalBatter, in rivalPitcher, defense, ref _rng);
            }

            ref BattingLine bat = ref _batting[batterSlot];
            ref PitchingLine pit = ref _pitching[pitcherSlot];
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
