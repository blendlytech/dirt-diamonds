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

    private int[] _teamIds = Array.Empty<int>();
    private int[] _lineupSlots = Array.Empty<int>();
    private int[] _rotationSlots = Array.Empty<int>();
    private int[] _bullpenSlots = Array.Empty<int>();
    private byte[] _teamDefense = Array.Empty<byte>();
    private int[] _teamGamesPlayed = Array.Empty<int>();
    private int _teamCount;

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

    private bool _initialized;

    public MicroGame(DatabaseManager db, BaseballQueries queries)
    {
        _db = db;
        _queries = queries;
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
        var teamIndexById = new Dictionary<int, int>(_teamCount);
        for (int t = 0; t < _teamCount; t++)
        {
            _teamIds[t] = teams[t].TeamId;
            teamIndexById.Add(teams[t].TeamId, t);
        }

        var rosterRows = new List<RosterPlayerRow>(_teamCount * LeagueSimulator.RosterSizePerTeam);
        _queries.LoadRoster(rosterRows);
        _roster = rosterRows.ToArray();

        var pedIds = new List<string>();
        _queries.LoadActiveFlagPlayerIds(LeagueSimulator.PedActiveFlagName, pedIds);
        var pedSet = new HashSet<string>(pedIds, StringComparer.Ordinal);

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
        _batting = new BattingLine[slots];
        _pitching = new PitchingLine[slots];
        _playedThisGame = new bool[slots];
        _lineupSlots = new int[_teamCount * LeagueSimulator.LineupSize];
        _rotationSlots = new int[_teamCount * LeagueSimulator.RotationSize];
        _bullpenSlots = new int[_teamCount * LeagueSimulator.BullpenSize];
        _teamDefense = new byte[_teamCount];
        _teamGamesPlayed = new int[_teamCount];

        Span<int> lineupCount = stackalloc int[_teamCount];
        Span<int> rotationCount = stackalloc int[_teamCount];
        Span<int> bullpenCount = stackalloc int[_teamCount];
        Span<int> fieldingSum = stackalloc int[_teamCount];

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
                    case PitcherRole.Starter when rotationCount[team] < LeagueSimulator.RotationSize:
                        _rotationSlots[team * LeagueSimulator.RotationSize + rotationCount[team]++] = i;
                        break;
                    case PitcherRole.Reliever when bullpenCount[team] < LeagueSimulator.BullpenSize:
                        _bullpenSlots[team * LeagueSimulator.BullpenSize + bullpenCount[team]++] = i;
                        break;
                    case PitcherRole.None:
                        throw new InvalidOperationException(
                            $"Pitcher {row.PlayerId} has no Pitcher_Roles row — the v4 backfill/generation invariant is broken.");
                }
            }
            else if (lineupCount[team] < LeagueSimulator.LineupSize)
            {
                _lineupSlots[team * LeagueSimulator.LineupSize + lineupCount[team]++] = i;
                fieldingSum[team] += row.Fielding;
            }
        }

        for (int t = 0; t < _teamCount; t++)
        {
            if (lineupCount[t] < LeagueSimulator.LineupSize || rotationCount[t] < LeagueSimulator.RotationSize ||
                bullpenCount[t] < LeagueSimulator.BullpenSize)
            {
                throw new InvalidOperationException(
                    $"Team {_teamIds[t]} has {lineupCount[t]}/{LeagueSimulator.LineupSize} position players, " +
                    $"{rotationCount[t]}/{LeagueSimulator.RotationSize} starters and " +
                    $"{bullpenCount[t]}/{LeagueSimulator.BullpenSize} relievers — cannot host an attended game " +
                    "(did LeagueGenerator.EnsureV4 run?).");
            }
            _teamDefense[t] = (byte)((fieldingSum[t] + LeagueSimulator.LineupSize / 2) / LeagueSimulator.LineupSize);
        }

        _initialized = true;
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
    /// runs (§6.1). Game-flow rules (9 innings, home half skipped when ahead,
    /// extras while tied) mirror the macro-sim exactly; §8.5 bullpen pulls
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

        int homeStarter = _rotationSlots[homeTeam * LeagueSimulator.RotationSize + _teamGamesPlayed[homeTeam] % LeagueSimulator.RotationSize];
        int awayStarter = _rotationSlots[awayTeam * LeagueSimulator.RotationSize + _teamGamesPlayed[awayTeam] % LeagueSimulator.RotationSize];
        _teamGamesPlayed[homeTeam]++;
        _teamGamesPlayed[awayTeam]++;

        // §8/§8.5: starters decay via the fatigue multiplier and hand off to
        // the bullpen when it sinks below the pull threshold.
        int homePitcher = homeStarter;
        int awayPitcher = awayStarter;
        var homeFatigue = new PitcherFatigue(in _pitcherRatings[homeStarter], _pedActive[homeStarter], FatigueEnabled);
        var awayFatigue = new PitcherFatigue(in _pitcherRatings[awayStarter], _pedActive[awayStarter], FatigueEnabled);
        int homeBullpenUsed = 0;
        int awayBullpenUsed = 0;
        double homeStarterPitches = -1.0;
        double awayStarterPitches = -1.0;
        _pitching[homeStarter].G++;
        _pitching[homeStarter].Gs++;
        _pitching[awayStarter].G++;
        _pitching[awayStarter].Gs++;

        int homeScore = 0;
        int awayScore = 0;
        int homeOrder = 0;
        int awayOrder = 0;
        int humanPa = 0;
        int humanPitches = 0;
        int inning;

        for (inning = 1; ; inning++)
        {
            MaybePullPitcher(homeTeam, ref homePitcher, ref homeFatigue, ref homeBullpenUsed, ref homeStarterPitches);
            awayScore += PlayHalfInning(
                awayTeam, homePitcher, ref homeFatigue, _teamDefense[homeTeam], ref awayOrder,
                humanSlot, inning, isTopHalf: true, awayScore, homeScore,
                ref batterPolicy, ref pitcherPolicy, ref rng, ref humanPa, ref humanPitches);
            if (inning >= 9 && homeScore > awayScore)
            {
                break;
            }
            MaybePullPitcher(awayTeam, ref awayPitcher, ref awayFatigue, ref awayBullpenUsed, ref awayStarterPitches);
            homeScore += PlayHalfInning(
                homeTeam, awayPitcher, ref awayFatigue, _teamDefense[awayTeam], ref homeOrder,
                humanSlot, inning, isTopHalf: false, awayScore, homeScore,
                ref batterPolicy, ref pitcherPolicy, ref rng, ref humanPa, ref humanPitches);
            if (inning >= 9 && homeScore != awayScore)
            {
                break;
            }
        }

        // Decisions stay with the starters — pitcher-of-record bookkeeping is
        // a deliberate v4 simplification (documented artifact; W/L accounting
        // identities in the harness rely on exactly one W and L per game).
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
        for (int i = 0; i < LeagueSimulator.LineupSize; i++)
        {
            _playedThisGame[_lineupSlots[homeTeam * LeagueSimulator.LineupSize + i]] = true;
            _playedThisGame[_lineupSlots[awayTeam * LeagueSimulator.LineupSize + i]] = true;
        }
        _playedThisGame[homeStarter] = true;
        _playedThisGame[awayStarter] = true;

        if (LoggingEnabled)
        {
            _payloadBuilder.Clear();
            _payloadBuilder.Append("{\"home\":").Append(homeScore)
                .Append(",\"away\":").Append(awayScore)
                .Append(",\"innings\":").Append(inning).Append('}');
            _pendingLogs.Add(new PendingLog(null, "final", _payloadBuilder.ToString()));
        }

        _gamePendingFlush = true;
        return new MicroGameResult(
            homeTeamId, awayTeamId, homeScore, awayScore, inning,
            humanPa, humanPitches, homeStarterPitches, awayStarterPitches);
    }

    /// <summary>
    /// §8.5: lifts the current pitcher between half-innings once his fatigue
    /// multiplier drops below <see cref="BullpenPullThreshold"/>, bringing the
    /// next fresh reliever (their own stamina-derived capacity). The starter's
    /// final pitch count is captured on his way out for the game line.
    /// </summary>
    private void MaybePullPitcher(
        int team, ref int pitcherSlot, ref PitcherFatigue fatigue, ref int bullpenUsed,
        ref double starterPitches)
    {
        if (!BullpenEnabled || bullpenUsed >= LeagueSimulator.BullpenSize ||
            fatigue.Multiplier >= BullpenPullThreshold)
        {
            return;
        }
        if (bullpenUsed == 0)
        {
            starterPitches = fatigue.PitchesThrown;
        }
        int reliever = _bullpenSlots[team * LeagueSimulator.BullpenSize + bullpenUsed++];
        pitcherSlot = reliever;
        fatigue = new PitcherFatigue(in _pitcherRatings[reliever], _pedActive[reliever], FatigueEnabled);
        _pitching[reliever].G++;
        _playedThisGame[reliever] = true;
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
        int battingTeam, int pitcherSlot, ref PitcherFatigue fatigue, byte defense, ref int orderPos,
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
            int batterSlot = _lineupSlots[battingTeam * LeagueSimulator.LineupSize + orderPos];
            orderPos = (orderPos + 1) % LeagueSimulator.LineupSize;

            PitcherRatings effectivePitcher = fatigue.EffectiveRatings();
            PaOutcome outcome;
            int pitches = 0;

            bool humanBatting = batterSlot == humanSlot;
            bool humanPitching = pitcherSlot == humanSlot;
            if (humanBatting || humanPitching)
            {
                // Inner chain (§5): anchor to the shared resolver, invert the
                // absorption equations, then play the count pitch by pitch.
                AtBatResolver.ComputeProbabilities(in _batterRatings[batterSlot], in effectivePitcher, defense, anchor);
                PitchClassRates rates = PitchChain.SolveNeutral(
                    anchor[(int)PaOutcome.Walk], anchor[(int)PaOutcome.Strikeout]);
                var matchup = new PitchMatchup(
                    in _arsenals[pitcherSlot], effectivePitcher.Control, _batterRatings[batterSlot].Discipline);
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
                outcome = AtBatResolver.Resolve(in _batterRatings[batterSlot], in effectivePitcher, defense, ref rng);
                fatigue.AddNpcPa();
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
                AppendPaLog(inning, isTopHalf, batterSlot, pitcherSlot, outcome, paRuns, pitches);
            }
            // NPC feed: the human's own PA already renders via the bridge's
            // outcome queue ("Your PA: ..."), so only queue the others here.
            if (FeedSink != null && batterSlot != humanSlot)
            {
                FeedSink.PublishNpcPa(new NpcPaFeedEvent(_displayNames[batterSlot], outcome, inning, isTopHalf, paRuns));
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

    /// <summary>§10: payloads are built at PA boundaries, never inside the pitch loop.</summary>
    private void AppendPaLog(
        int inning, bool isTopHalf, int batterSlot, int pitcherSlot, PaOutcome outcome, int runs, int pitches)
    {
        _payloadBuilder.Clear();
        _payloadBuilder.Append("{\"inning\":").Append(inning)
            .Append(",\"half\":\"").Append(isTopHalf ? "top" : "bottom")
            .Append("\",\"pitcher\":\"").Append(_roster[pitcherSlot].PlayerId)
            .Append("\",\"outcome\":\"").Append(OutcomeName(outcome))
            .Append("\",\"runs\":").Append(runs);
        if (pitches > 0)
        {
            _payloadBuilder.Append(",\"pitches\":").Append(pitches);
        }
        _payloadBuilder.Append('}');
        _pendingLogs.Add(new PendingLog(_roster[batterSlot].PlayerId, "pa", _payloadBuilder.ToString()));
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
    public void FlushGame(int seasonYear, int gameDay)
    {
        if (!_gamePendingFlush)
        {
            throw new InvalidOperationException("No played game pending flush.");
        }

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
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        Array.Clear(_batting);
        Array.Clear(_pitching);
        Array.Clear(_playedThisGame);
        _pendingLogs.Clear();
        _gamePendingFlush = false;
    }
}
