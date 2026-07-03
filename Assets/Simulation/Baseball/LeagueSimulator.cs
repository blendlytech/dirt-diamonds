using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// The background-league macro-sim (design doc §7/§9, Milestone M1). Bulk-loads
/// rosters up front (Players ⋈ Player_Ratings), plays a round-robin schedule of
/// <see cref="AtBatResolver"/>-driven games in response to
/// <see cref="DayAdvancedEvent"/> off the bus, accumulates season counting
/// stats in preallocated struct arrays, and flushes them once per season in its
/// OWN batch transaction — never the calendar tick's. Rate columns are left 0
/// for <see cref="StatsNormalizer"/>.
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
    public const int RosterSizePerTeam = LineupSize + RotationSize;

    /// <summary>7-day round-robin cycles × 22 = a 154-game season for every team.</summary>
    public const int RegularSeasonDays = RoundsPerCycle * CyclesPerSeason;
    private const int RoundsPerCycle = TeamCount - 1;
    private const int CyclesPerSeason = 22;
    private const int GamesPerDay = TeamCount / 2;

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

    /// <summary>Season year with simulated-but-unflushed games; 0 = clean.</summary>
    private int _unflushedSeasonYear;

    public LeagueSimulator(DatabaseManager db, BaseballQueries queries, StatsNormalizer normalizer, RngState rng)
    {
        _db = db;
        _queries = queries;
        _normalizer = normalizer;
        _rng = rng;
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
        _queries.LoadAllTeams(teams);
        if (teams.Count != TeamCount)
        {
            throw new InvalidOperationException(
                $"League requires exactly {TeamCount} teams; found {teams.Count}. Run LeagueGenerator first.");
        }

        var rosterRows = new List<RosterPlayerRow>(TeamCount * RosterSizePerTeam);
        _queries.LoadRoster(rosterRows);
        _roster = rosterRows.ToArray();

        var pedIds = new List<string>();
        _queries.LoadActiveFlagPlayerIds(PedActiveFlagName, pedIds);
        var pedSet = new HashSet<string>(pedIds, StringComparer.Ordinal);

        var teamIndexById = new Dictionary<int, int>(TeamCount);
        for (int t = 0; t < teams.Count; t++)
        {
            teamIndexById.Add(teams[t].TeamId, t);
        }

        int slots = _roster.Length;
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
            _batterRatings[i] = new BatterRatings((byte)row.BatPower, (byte)row.BatContact, (byte)row.BatDiscipline, ped);
            _pitcherRatings[i] = new PitcherRatings((byte)row.PitStuff, (byte)row.PitControl, (byte)row.PitStamina);

            if (row.IsPitcher)
            {
                if (rotationCount[team] < RotationSize)
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
                    $"position players and {rotationCount[t]}/{RotationSize} pitchers — cannot field a season.");
            }
            // §3: team defense = mean fielding of the lineup, rounded.
            _teamDefense[t] = (byte)((fieldingSum[t] + LineupSize / 2) / LineupSize);
        }

        _initialized = true;
    }

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

        if (e.DayOfSeason == RegularSeasonDays)
        {
            FlushSeason(e.SeasonYear);
        }
    }

    private void OnSeasonRolledOver(SeasonRolledOverEvent e)
    {
        // Safety net: a simulator attached mid-season never saw the day-154
        // flush trigger. The guard on the year keeps this from re-flushing the
        // NEW season's day 1, which dispatches before this event on rollover day.
        if (_unflushedSeasonYear == e.PreviousSeasonYear)
        {
            FlushSeason(e.PreviousSeasonYear);
        }
    }

    // ------------------------------------------------------------------
    // Schedule & game loop (allocation-free)
    // ------------------------------------------------------------------

    /// <summary>
    /// Plays all four games for one season day. Circle-method round-robin with
    /// team index (TeamCount-1) fixed; home/away alternates by cycle+slot so
    /// pairings balance over the 22 cycles. Internal so run_monte_carlo_batch
    /// can drive and profile it directly.
    /// </summary>
    internal void SimulateGameDay(int dayOfSeason)
    {
        int cycle = (dayOfSeason - 1) / RoundsPerCycle;
        int round = (dayOfSeason - 1) % RoundsPerCycle;

        PlayPair(TeamCount - 1, round, cycle, 0);
        for (int i = 1; i < GamesPerDay; i++)
        {
            int a = (round + i) % RoundsPerCycle;
            int b = (round - i + RoundsPerCycle) % RoundsPerCycle;
            PlayPair(a, b, cycle, i);
        }
    }

    private void PlayPair(int teamA, int teamB, int cycle, int slot)
    {
        if (((cycle + slot) & 1) == 0)
        {
            PlayGame(teamA, teamB);
        }
        else
        {
            PlayGame(teamB, teamA);
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

            PaOutcome outcome = AtBatResolver.Resolve(
                in _batterRatings[batterSlot], in pitcher, defense, ref _rng);

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
    // Season flush — the sim's own batch, never the calendar tick's
    // ------------------------------------------------------------------

    private void FlushSeason(int seasonYear)
    {
        _db.BeginBatch();
        try
        {
            for (int i = 0; i < _roster.Length; i++)
            {
                ref readonly BattingLine bat = ref _batting[i];
                if (bat.Pa > 0)
                {
                    _queries.UpsertBattingSeasonCounts(
                        _roster[i].PlayerId, seasonYear,
                        bat.Pa, bat.Ab, bat.H, bat.Doubles, bat.Triples, bat.Hr, bat.Bb, bat.So, bat.Rbi, sb: 0);
                }

                ref readonly PitchingLine pit = ref _pitching[i];
                if (pit.G > 0)
                {
                    _queries.UpsertPitchingSeasonCounts(
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
        // normalizer's own transaction.
        _normalizer.NormalizeSeason(seasonYear);

        Array.Clear(_batting);
        Array.Clear(_pitching);
        Array.Clear(_pedGamesPlayed);
        _unflushedSeasonYear = 0;
    }
}
