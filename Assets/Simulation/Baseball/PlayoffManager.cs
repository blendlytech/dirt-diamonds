using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>How a postseason game resolved before (or instead of) being played.</summary>
public enum PostseasonPlay : byte
{
    Played = 0,

    /// <summary>The home team had no legal pitcher (the narrow postseason forfeit, brief D3).</summary>
    HomeForfeits = 1,

    /// <summary>The away team had no legal pitcher.</summary>
    AwayForfeits = 2,

    /// <summary>Neither team had a legal pitcher; the high seed is awarded the game (edge rule pending 13-5).</summary>
    BothForfeit = 3,
}

/// <summary>
/// The avatar's postseason game, claimed by the career driver (brief §4.4):
/// the row plus the series' natural key (the attended stream's key) and The
/// Red Dirt Series' no-repeat window.
/// </summary>
public readonly record struct PostseasonGameClaim(
    PostseasonGameRow Game, int Round, int BestOf, int HighSeedTeamId, int LowSeedTeamId, int SeriesFirstDayOfSeason)
{
    /// <summary>The no-repeat window's first absolute day, or <see cref="PostseasonCalendar.NoSeriesFilter"/>.</summary>
    public long SeriesFirstAbsoluteDay(long gameAbsoluteDay) =>
        Game.Tier == LeagueTier.College && Round == PostseasonCalendar.CollegeFinalRound
            ? gameAbsoluteDay - Game.DayOfSeason + SeriesFirstDayOfSeason
            : PostseasonCalendar.NoSeriesFilter;
}

/// <summary>
/// The postseason (slice 13-4; league_restructure.md §8, §13; brief D0–D7):
/// selection, seeding, the fixed brackets and series, the day handler that
/// plays every due NPC game through the tier sim's isolated macro adapter,
/// and the attended game's commit path. Lives in the Baseball sim and talks
/// to Life only through the bus and the DB.
///
/// <b>The DB rows are the coordinator</b> (D0-B): Playoff_Series, the
/// phase-1/2 Schedule_Games rows and the Team_Seasons seeds determine the
/// whole bracket, which is re-derived after every commit, so a reload
/// mid-series resumes exactly. A game's row exists only once it is certain
/// to be played (game 1 at series creation, game n after game n − 1 leaves
/// the series live), so an unplayed game never exists and never counts.
///
/// <b>Isolation</b> (D4-A): every game draws from its own
/// <see cref="PostseasonStreams.ForGame"/> stream keyed on the natural series
/// key; no postseason game writes a player stat line or touches a sim's
/// shared RNG, so the regular season and the sanctioned Pro guard stay
/// byte-exact with the postseason running.
///
/// <b>Order</b>: attach AFTER the career driver, whose handler claims the
/// avatar's game first; this manager then skips that game. Every game's
/// result, its series update and its bracket effects commit in one batch;
/// events publish after the commit.
/// </summary>
public sealed class PlayoffManager
{
    /// <summary>The strings-table key of College's final (never a C# literal; pending the owner's trademark screen).</summary>
    public const string CollegeFinalNameKey = "league.college.final.name";

    /// <summary>Game_State prefix of the champion publication marker (+ "{year}.{tier}").</summary>
    public const string ChampionMarkerPrefix = "playoffs.champion_published.";

    private const int Bye = -1;
    private const int Unknown = 0;

    // Fixed standard seeded trees, no reseeding (Amendment 4).
    private static readonly int[] Leaves16 = { 1, 16, 8, 9, 4, 13, 5, 12, 2, 15, 7, 10, 3, 14, 6, 11 };
    private static readonly int[] Leaves8 = { 1, 8, 4, 5, 2, 7, 3, 6 };

    private readonly DatabaseManager _db;
    private readonly BaseballQueries _baseball;
    private readonly PlayoffQueries _queries;
    private readonly GameStateQueries _gameState;
    private readonly LeagueDirectory _leagues;
    private readonly TeamSeasonsWriter _writer;
    private readonly Action<DayAdvancedEvent> _onDayAdvanced;
    private EventBus? _bus;

    private readonly List<PlayoffSeriesRow> _series = new();
    private readonly List<PostseasonGameRow> _games = new();
    private readonly List<TeamSeasonRow> _teamSeasons = new();
    private readonly List<CompletedGame> _completed = new();
    private readonly List<long> _dueIds = new();
    private readonly SeasonTable _table = new();
    private readonly List<TeamRow>?[] _teamsByTier = new List<TeamRow>?[LeagueDirectory.TierCount];

    // Events wait for their batch's commit; champions then wait for the next
    // day handler (their dispatch has drained) to write the marker.
    private readonly List<PlayoffSeriesDecidedEvent> _pendingDecided = new();
    private readonly List<ChampionshipWonEvent> _pendingChampions = new();
    private readonly List<ChampionshipWonEvent> _awaitingMarker = new();

    // The avatar's parked game (0 = none): the day handler leaves it to the career.
    private long _claimedGameId;

    public PlayoffManager(DatabaseManager db, BaseballQueries baseball, LeagueDirectory leagues)
    {
        _db = db;
        _baseball = baseball;
        _queries = new PlayoffQueries(db);
        _gameState = new GameStateQueries(db);
        _leagues = leagues;
        _writer = new TeamSeasonsWriter(_queries);
        _onDayAdvanced = OnDayAdvanced;
    }

    /// <summary>Series decided since construction (harness visibility).</summary>
    public int SeriesDecidedCount { get; private set; }

    /// <summary>Postseason games this manager played through the macro adapter (harness visibility).</summary>
    public int MacroGamesPlayed { get; private set; }

    /// <summary>Regular-season rows still unplayed at the last selection (0 in any single session; harness visibility).</summary>
    public int LastSelectionUnplayed { get; private set; }

    public void AttachTo(EventBus bus)
    {
        _bus = bus;
        bus.Subscribe(_onDayAdvanced);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onDayAdvanced);
        _bus = null;
    }

    // ------------------------------------------------------------------
    // Day handler
    // ------------------------------------------------------------------

    private void OnDayAdvanced(DayAdvancedEvent e)
    {
        // The previous pump has fully drained, so every champion published
        // before it has been dispatched: its marker can be written now.
        WritePublicationMarkers();
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            if (_leagues.TryGet((LeagueTier)t, out LeagueSimulator sim))
            {
                ProcessDay(sim, e);
            }
        }
    }

    /// <summary>
    /// One tier's postseason work for a day: selection on selection day
    /// (after a forced flush of pending regular-season results, D1), then
    /// every due unplayed NPC game (including any a crash left behind).
    /// Outside the tier's postseason window it returns without a query.
    /// </summary>
    internal void ProcessDay(LeagueSimulator sim, DayAdvancedEvent e)
    {
        LeagueTier tier = sim.Tier;
        if (e.DayOfSeason < PostseasonCalendar.SelectionDay(tier) || e.DayOfSeason > PostseasonCalendar.LastDay(tier))
        {
            return;
        }
        if (_queries.CountSeries(e.SeasonYear, tier) == 0)
        {
            RunSelection(sim, e.SeasonYear);
        }
        PlayDueGames(sim, e);
    }

    /// <summary>Writes the publication marker of every champion whose event has been dispatched.</summary>
    public void WritePublicationMarkers()
    {
        if (_awaitingMarker.Count == 0)
        {
            return;
        }
        _db.RunInBatch(() =>
        {
            foreach (ChampionshipWonEvent c in _awaitingMarker)
            {
                _gameState.SetInt64(MarkerKey(c.SeasonYear, (LeagueTier)c.Tier), c.TeamId);
            }
        });
        _awaitingMarker.Clear();
    }

    /// <summary>
    /// Boot recovery (brief §4.5): a decided final whose publication marker
    /// is missing (a crash between its commit and the marker) is published
    /// again. Checks the given season and the one before it.
    /// </summary>
    public int RepublishMissingChampions(int currentSeasonYear)
    {
        int republished = 0;
        for (int year = currentSeasonYear - 1; year <= currentSeasonYear; year++)
        {
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                var tier = (LeagueTier)t;
                if (_gameState.TryGetInt64(MarkerKey(year, tier), out _))
                {
                    continue;
                }
                _queries.LoadSeries(year, tier, _series);
                foreach (PlayoffSeriesRow s in _series)
                {
                    if (s.Round == PostseasonCalendar.FinalRound(tier) && s.WinnerTeamId.HasValue)
                    {
                        _pendingChampions.Add(new ChampionshipWonEvent(year, (byte)tier, s.WinnerTeamId.Value));
                        republished++;
                    }
                }
            }
        }
        PublishPendingEvents();
        return republished;
    }

    public static string MarkerKey(int seasonYear, LeagueTier tier) => $"{ChampionMarkerPrefix}{seasonYear}.{(int)tier}";

    // ------------------------------------------------------------------
    // Selection (D1, D2, D3)
    // ------------------------------------------------------------------

    private void RunSelection(LeagueSimulator sim, int seasonYear)
    {
        // D1: selection day forces the pending regular-season results out
        // first (Pro's Tue 240 finale would otherwise wait for Sunday 245).
        sim.FlushPending();
        LeagueTier tier = sim.Tier;
        ulong seed = sim.ScheduleSeed;
        _db.BeginBatch();
        try
        {
            // In one session the forced flush leaves nothing unplayed. A reload
            // can: a regular-season game parked at quit isn't re-parked, and a
            // crash loses the week's unflushed macro results (both pre-13-4
            // behavior). Selection then seeds on the completed games (D1 counts
            // final and forfeit rows only; win% handles unequal game counts)
            // instead of failing the player's save. The harness asserts 0.
            LastSelectionUnplayed = _queries.CountUnplayedRegularSeason(seasonYear, tier);
            _writer.Recompute(seasonYear, tier, sim.TeamIds, seed);
            List<TeamRow> teams = TeamsOf(tier);
            switch (tier)
            {
                case LeagueTier.HS:
                    SelectHs(seasonYear, teams, seed);
                    break;
                case LeagueTier.College:
                    SelectCollege(seasonYear);
                    break;
                default:
                    SelectPro(seasonYear, teams, seed);
                    break;
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
    }

    /// <summary>HS: top 3 per conference by D2; #1 byes; QF N2–N3, S2–S3.</summary>
    private void SelectHs(int seasonYear, List<TeamRow> teams, ulong seed)
    {
        foreach (int conference in Conferences(teams))
        {
            List<int> order = Standings.Order(_writer.Table, TeamsIn(teams, conference, null), seed, seasonYear, LeagueTier.HS);
            for (int s = 0; s < 3; s++)
            {
                _queries.SetSeed(order[s], seasonYear, s + 1);
            }
            CreateSeries(seasonYear, LeagueTier.HS, 1, order[1], order[2]);
        }
    }

    /// <summary>College: ranks 1–17 automatic (seeded 1…n per conference by rank); #18 hosts #19 in the play-in.</summary>
    private void SelectCollege(int seasonYear)
    {
        var ranked = new List<int>(_writer.LastRanking.Count);
        foreach (RankingCalculator.Entry entry in _writer.LastRanking)
        {
            ranked.Add(entry.TeamId);
        }
        SelectCollegeFromRanking(seasonYear, ranked);
    }

    /// <summary>
    /// College selection from a final ranking (team ids, #1 first), inside the
    /// caller's batch; Team_Seasons rows must exist. Internal so the harness
    /// can draw every conference split (6+12 … 12+6) from a fixed ranking.
    /// </summary>
    internal void SelectCollegeFromRanking(int seasonYear, IReadOnlyList<int> rankedTeamIds)
    {
        List<TeamRow> teams = TeamsOf(LeagueTier.College);
        var seedCount = new Dictionary<int, int>();
        for (int r = 0; r < 17; r++)
        {
            int conference = ConferenceOf(teams, rankedTeamIds[r]);
            seedCount[conference] = seedCount.GetValueOrDefault(conference) + 1;
            _queries.SetSeed(rankedTeamIds[r], seasonYear, seedCount[conference]);
        }
        CreateSeries(seasonYear, LeagueTier.College, PostseasonCalendar.CollegePlayInRound, rankedTeamIds[17], rankedTeamIds[18]);
    }

    /// <summary>Pro: division winners seeds 1–4 (D2 among winners), best two non-winners 5–6; WC 3v6, 4v5.</summary>
    private void SelectPro(int seasonYear, List<TeamRow> teams, ulong seed)
    {
        foreach (int conference in Conferences(teams))
        {
            var winners = new List<int>();
            foreach (int division in Divisions(teams, conference))
            {
                winners.Add(Standings.Order(_writer.Table, TeamsIn(teams, conference, division), seed, seasonYear, LeagueTier.Pro)[0]);
            }
            List<int> topFour = Standings.Order(_writer.Table, winners, seed, seasonYear, LeagueTier.Pro);
            var rest = new List<int>();
            foreach (int teamId in TeamsIn(teams, conference, null))
            {
                if (!winners.Contains(teamId))
                {
                    rest.Add(teamId);
                }
            }
            List<int> wildCards = Standings.Order(_writer.Table, rest, seed, seasonYear, LeagueTier.Pro);
            for (int s = 0; s < 4; s++)
            {
                _queries.SetSeed(topFour[s], seasonYear, s + 1);
            }
            _queries.SetSeed(wildCards[0], seasonYear, 5);
            _queries.SetSeed(wildCards[1], seasonYear, 6);
            CreateSeries(seasonYear, LeagueTier.Pro, 1, topFour[2], wildCards[1]);
            CreateSeries(seasonYear, LeagueTier.Pro, 1, topFour[3], wildCards[0]);
        }
    }

    /// <summary>Inserts a 0–0 series and its certain game 1 (inside the caller's batch).</summary>
    internal void CreateSeries(int seasonYear, LeagueTier tier, int round, int highSeedTeamId, int lowSeedTeamId)
    {
        int bestOf = PostseasonCalendar.BestOf(tier, round);
        long seriesId = _queries.InsertSeries(seasonYear, tier, round, bestOf, highSeedTeamId, lowSeedTeamId);
        InsertGame(seasonYear, tier, round, bestOf, seriesId, highSeedTeamId, lowSeedTeamId, 1);
    }

    private void InsertGame(int seasonYear, LeagueTier tier, int round, int bestOf, long seriesId, int high, int low, int gameInSeries)
    {
        bool highHosts = PostseasonCalendar.HighSeedHosts(bestOf, gameInSeries);
        _queries.InsertPostseasonGame(
            seasonYear, tier, PostseasonCalendar.GameDay(tier, round, gameInSeries),
            highHosts ? high : low, highHosts ? low : high, PostseasonCalendar.Phase(tier, round), seriesId, gameInSeries);
    }

    // ------------------------------------------------------------------
    // Macro games (D4-A)
    // ------------------------------------------------------------------

    private void PlayDueGames(LeagueSimulator sim, DayAdvancedEvent e)
    {
        LeagueTier tier = sim.Tier;
        _queries.LoadPostseasonGames(e.SeasonYear, tier, _games);
        // Snapshot the due rows first: a result can insert rows, always on later days.
        _dueIds.Clear();
        foreach (PostseasonGameRow row in _games)
        {
            if (!row.IsPlayed && row.DayOfSeason <= e.DayOfSeason && row.GameId != _claimedGameId)
            {
                _dueIds.Add(row.GameId);
            }
        }
        long seasonBase = e.Day - e.DayOfSeason;
        for (int i = 0; i < _dueIds.Count; i++)
        {
            PostseasonGameRow row = FindGame(_dueIds[i]);
            PlayoffSeriesRow series = LoadSeriesRow(e.SeasonYear, tier, row.SeriesId);
            PlayMacroGame(sim, row, series, seasonBase);
        }
    }

    private void PlayMacroGame(LeagueSimulator sim, PostseasonGameRow row, PlayoffSeriesRow series, long seasonBase)
    {
        LeagueTier tier = sim.Tier;
        long day = seasonBase + row.DayOfSeason;
        long seriesFirst = IsRedDirtSeries(tier, series.Round)
            ? seasonBase + PostseasonCalendar.GameDay(tier, series.Round, 1)
            : PostseasonCalendar.NoSeriesFilter;
        RngState rng = PostseasonStreams.ForGame(
            sim.ScheduleSeed, row.SeasonYear, tier, series.Round, series.HighSeedTeamId, series.LowSeedTeamId,
            row.GameInSeries, isAttended: false);
        PostseasonPlay play = sim.SimulatePostseasonGame(
            row.HomeTeamId, row.AwayTeamId, day, seriesFirst, ref rng, out int homeScore, out int awayScore);
        int status = ForfeitScore(tier, play, row, series, ref homeScore, ref awayScore);

        _db.BeginBatch();
        try
        {
            _queries.RecordPostseasonResult(row.GameId, status, homeScore, awayScore);
            if (play == PostseasonPlay.Played)
            {
                sim.WritePostseasonPedCosts();
            }
            sim.RestLedger.FlushDirty(_baseball);
            ApplySeriesResult(row.SeasonYear, tier, series, row, homeScore, awayScore);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            sim.RestLedger.AbandonFlush();
            sim.ClearPostseasonGameState(revertStarts: true);
            DiscardPendingEvents();
            throw;
        }
        sim.RestLedger.AcknowledgeFlush();
        sim.ClearPostseasonGameState(revertStarts: false);
        MacroGamesPlayed++;
        PublishPendingEvents();
    }

    /// <summary>The narrow forfeit's score (RegulationInnings–0, status 2); 1 for a played game.</summary>
    private static int ForfeitScore(
        LeagueTier tier, PostseasonPlay play, PostseasonGameRow row, PlayoffSeriesRow series,
        ref int homeScore, ref int awayScore)
    {
        int runs = TierRules.RegulationInnings(tier);
        switch (play)
        {
            case PostseasonPlay.Played:
                return 1;
            case PostseasonPlay.HomeForfeits:
                (homeScore, awayScore) = (0, runs);
                return 2;
            case PostseasonPlay.AwayForfeits:
                (homeScore, awayScore) = (runs, 0);
                return 2;
            default:
                bool highHome = row.HomeTeamId == series.HighSeedTeamId;
                (homeScore, awayScore) = highHome ? (runs, 0) : (0, runs);
                return 2;
        }
    }

    private static bool IsRedDirtSeries(LeagueTier tier, int round) =>
        tier == LeagueTier.College && round == PostseasonCalendar.CollegeFinalRound;

    // ------------------------------------------------------------------
    // Attended games (brief §4.4)
    // ------------------------------------------------------------------

    /// <summary>
    /// The career driver's claim on the avatar team's due postseason game
    /// (the earliest unplayed row on or before the day). The day handler
    /// leaves a claimed game alone until <see cref="ReleaseClaim"/>.
    /// </summary>
    public bool TryClaimAvatarGame(int seasonYear, LeagueTier tier, int dayOfSeason, int teamId, out PostseasonGameClaim claim)
    {
        claim = default;
        if (dayOfSeason < PostseasonCalendar.SelectionDay(tier) || dayOfSeason > PostseasonCalendar.LastDay(tier))
        {
            return false;
        }
        _queries.LoadPostseasonGames(seasonYear, tier, _games);
        foreach (PostseasonGameRow row in _games)
        {
            if (!row.IsPlayed && row.DayOfSeason <= dayOfSeason && row.Involves(teamId))
            {
                PlayoffSeriesRow series = LoadSeriesRow(seasonYear, tier, row.SeriesId);
                claim = new PostseasonGameClaim(row, series.Round, series.BestOf, series.HighSeedTeamId,
                    series.LowSeedTeamId, PostseasonCalendar.GameDay(tier, series.Round, 1));
                _claimedGameId = row.GameId;
                return true;
            }
        }
        return false;
    }

    /// <summary>Lets the day handler see the claimed game again (after it is recorded).</summary>
    public void ReleaseClaim() => _claimedGameId = 0;

    /// <summary>
    /// The attended game's series effects, run INSIDE the micro-sim's flush
    /// batch (after its RecordFinal), so the result and the series update
    /// commit together. Call <see cref="PublishPendingEvents"/> after the
    /// commit, or <see cref="DiscardPendingEvents"/> after a rollback.
    /// </summary>
    public void ApplyAttendedResultInBatch(in PostseasonGameClaim claim, int homeScore, int awayScore)
    {
        PlayoffSeriesRow series = LoadSeriesRow(claim.Game.SeasonYear, claim.Game.Tier, claim.Game.SeriesId);
        ApplySeriesResult(claim.Game.SeasonYear, claim.Game.Tier, series, claim.Game, homeScore, awayScore);
    }

    /// <summary>The attended game's narrow forfeit (no game is played): its own batch, then the events.</summary>
    public void RecordAttendedForfeit(in PostseasonGameClaim claim, PostseasonPlay play)
    {
        PostseasonGameRow row = claim.Game;
        PlayoffSeriesRow series = LoadSeriesRow(row.SeasonYear, row.Tier, row.SeriesId);
        int homeScore = 0;
        int awayScore = 0;
        int status = ForfeitScore(row.Tier, play, row, series, ref homeScore, ref awayScore);
        RecordResult(row, status, homeScore, awayScore);
    }

    /// <summary>
    /// Records one postseason row's result (status 1 final, 2 forfeit) and
    /// its series effects in one batch, then publishes. The forfeit path's
    /// commit; internal so the harness can drive every series path (AA … BAB).
    /// </summary>
    internal void RecordResult(PostseasonGameRow row, int status, int homeScore, int awayScore)
    {
        PlayoffSeriesRow series = LoadSeriesRow(row.SeasonYear, row.Tier, row.SeriesId);
        _db.BeginBatch();
        try
        {
            _queries.RecordPostseasonResult(row.GameId, status, homeScore, awayScore);
            ApplySeriesResult(row.SeasonYear, row.Tier, series, row, homeScore, awayScore);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            DiscardPendingEvents();
            throw;
        }
        PublishPendingEvents();
    }

    // ------------------------------------------------------------------
    // Series and brackets (D3, Amendment 4)
    // ------------------------------------------------------------------

    /// <summary>Scores one game into its series; inserts the next certain game or advances the bracket.</summary>
    private void ApplySeriesResult(
        int seasonYear, LeagueTier tier, PlayoffSeriesRow series, PostseasonGameRow row, int homeScore, int awayScore)
    {
        if (series.IsDecided)
        {
            throw new InvalidOperationException($"Series {series.SeriesId} is already decided; game {row.GameId} can't count.");
        }
        int winner = homeScore > awayScore ? row.HomeTeamId : row.AwayTeamId;
        int highWins = series.HighWins + (winner == series.HighSeedTeamId ? 1 : 0);
        int lowWins = series.LowWins + (winner == series.LowSeedTeamId ? 1 : 0);
        if (highWins < series.WinsNeeded && lowWins < series.WinsNeeded)
        {
            _queries.UpdateSeries(series.SeriesId, highWins, lowWins, null);
            InsertGame(seasonYear, tier, series.Round, series.BestOf, series.SeriesId,
                series.HighSeedTeamId, series.LowSeedTeamId, row.GameInSeries + 1);
            return;
        }

        bool highWon = highWins >= series.WinsNeeded;
        int seriesWinner = highWon ? series.HighSeedTeamId : series.LowSeedTeamId;
        int seriesLoser = highWon ? series.LowSeedTeamId : series.HighSeedTeamId;
        _queries.UpdateSeries(series.SeriesId, highWins, lowWins, seriesWinner);
        SeriesDecidedCount++;
        _pendingDecided.Add(new PlayoffSeriesDecidedEvent(
            seasonYear, (byte)tier, series.SeriesId, series.Round, seriesWinner, seriesLoser,
            highWon ? highWins : lowWins, highWon ? lowWins : highWins));
        if (series.Round == PostseasonCalendar.FinalRound(tier))
        {
            _pendingChampions.Add(new ChampionshipWonEvent(seasonYear, (byte)tier, seriesWinner));
            return;
        }
        AdvanceBracket(seasonYear, tier);
    }

    /// <summary>Creates every series whose two teams are now known (idempotent; reads this batch's rows).</summary>
    private void AdvanceBracket(int seasonYear, LeagueTier tier)
    {
        _queries.LoadSeries(seasonYear, tier, _series);
        _queries.LoadTeamSeasons(seasonYear, tier, _teamSeasons);
        List<TeamRow> teams = TeamsOf(tier);
        switch (tier)
        {
            case LeagueTier.HS:
                AdvanceHs(seasonYear, teams);
                break;
            case LeagueTier.College:
                AdvanceCollege(seasonYear, teams);
                break;
            default:
                AdvancePro(seasonYear, teams);
                break;
        }
    }

    private void AdvanceHs(int seasonYear, List<TeamRow> teams)
    {
        var champions = new List<int>(2);
        foreach (int conference in Conferences(teams))
        {
            int seed1 = TeamWithSeed(teams, conference, 1);
            int seed2 = TeamWithSeed(teams, conference, 2);
            PlayoffSeriesRow? qf = FindSeries(1, seed2);
            if (qf is null || !qf.Value.IsDecided)
            {
                continue;
            }
            PlayoffSeriesRow? final = FindSeries(2, seed1);
            if (final is null)
            {
                CreateSeries(seasonYear, LeagueTier.HS, 2, seed1, qf.Value.WinnerTeamId!.Value);
                continue;
            }
            if (final.Value.IsDecided)
            {
                champions.Add(final.Value.WinnerTeamId!.Value);
            }
        }
        if (champions.Count == 2 && FindSeriesInRound(3) is null)
        {
            (int high, int low) = HostByRecord(seasonYear, LeagueTier.HS, champions[0], champions[1]);
            CreateSeries(seasonYear, LeagueTier.HS, 3, high, low);
        }
    }

    private void AdvancePro(int seasonYear, List<TeamRow> teams)
    {
        var champions = new List<int>(2);
        foreach (int conference in Conferences(teams))
        {
            int s1 = TeamWithSeed(teams, conference, 1);
            int s2 = TeamWithSeed(teams, conference, 2);
            PlayoffSeriesRow? wcA = FindSeries(1, TeamWithSeed(teams, conference, 3));
            PlayoffSeriesRow? wcB = FindSeries(1, TeamWithSeed(teams, conference, 4));
            if (wcA is null || wcB is null || !wcA.Value.IsDecided || !wcB.Value.IsDecided)
            {
                continue;
            }
            PlayoffSeriesRow? div1 = FindSeries(2, s1);
            if (div1 is null)
            {
                // Division round reseeds: #1 v the lowest remaining seed, #2 v the other.
                int a = wcA.Value.WinnerTeamId!.Value;
                int b = wcB.Value.WinnerTeamId!.Value;
                (int lowest, int other) = SeedOf(a) > SeedOf(b) ? (a, b) : (b, a);
                CreateSeries(seasonYear, LeagueTier.Pro, 2, s1, lowest);
                CreateSeries(seasonYear, LeagueTier.Pro, 2, s2, other);
                continue;
            }
            PlayoffSeriesRow? div2 = FindSeries(2, s2);
            if (!div1.Value.IsDecided || div2 is null || !div2.Value.IsDecided)
            {
                continue;
            }
            int w1 = div1.Value.WinnerTeamId!.Value;
            int w2 = div2.Value.WinnerTeamId!.Value;
            PlayoffSeriesRow? cf = FindSeries(3, w1);
            if (cf is null)
            {
                (int high, int low) = SeedOf(w1) < SeedOf(w2) ? (w1, w2) : (w2, w1);
                CreateSeries(seasonYear, LeagueTier.Pro, 3, high, low);
                continue;
            }
            if (cf.Value.IsDecided)
            {
                champions.Add(cf.Value.WinnerTeamId!.Value);
            }
        }
        if (champions.Count == 2 && FindSeriesInRound(4) is null)
        {
            (int high, int low) = HostByRecord(seasonYear, LeagueTier.Pro, champions[0], champions[1]);
            CreateSeries(seasonYear, LeagueTier.Pro, 4, high, low);
        }
    }

    private void AdvanceCollege(int seasonYear, List<TeamRow> teams)
    {
        PlayoffSeriesRow? playIn = FindSeriesInRound(PostseasonCalendar.CollegePlayInRound);
        if (playIn is null || !playIn.Value.IsDecided)
        {
            return;
        }
        // The play-in winner joins its own conference as its last seed (it
        // ranks below all 17 automatic qualifiers); both brackets draw here.
        int playInWinner = playIn.Value.WinnerTeamId!.Value;
        if (SeedOf(playInWinner) == 0)
        {
            int conference = ConferenceOf(teams, playInWinner);
            int seeded = 0;
            foreach (TeamSeasonRow ts in _teamSeasons)
            {
                if (ts.Seed.HasValue && ConferenceOf(teams, ts.TeamId) == conference)
                {
                    seeded++;
                }
            }
            _queries.SetSeed(playInWinner, seasonYear, seeded + 1);
            _queries.LoadTeamSeasons(seasonYear, LeagueTier.College, _teamSeasons);
        }

        var champions = new List<int>(2);
        foreach (int conference in Conferences(teams))
        {
            int champion = AdvanceConferenceBracket(seasonYear, teams, conference);
            if (champion > 0)
            {
                champions.Add(champion);
            }
        }
        if (champions.Count == 2 && FindSeriesInRound(PostseasonCalendar.CollegeFinalRound) is null)
        {
            // The better selection (national) rank hosts games 1 and 3 (§8.2).
            (int high, int low) = RankOf(champions[0]) < RankOf(champions[1])
                ? (champions[0], champions[1]) : (champions[1], champions[0]);
            CreateSeries(seasonYear, LeagueTier.College, PostseasonCalendar.CollegeFinalRound, high, low);
        }
    }

    /// <summary>
    /// One conference's fixed seeded tree (Amendment 4): a 16-slot bracket over
    /// R1–R4 for 9–12 qualifiers, an 8-slot bracket over R2–R4 for 6–8; any
    /// slot above the qualifier count is a bye that advances its partner. Both
    /// end on R4 (day 167). Creates every series now determined; returns the
    /// conference champion, or 0 while undecided.
    /// </summary>
    private int AdvanceConferenceBracket(int seasonYear, List<TeamRow> teams, int conference)
    {
        var seedTeam = new int[13];
        int m = 0;
        foreach (TeamSeasonRow ts in _teamSeasons)
        {
            if (ts.Seed.HasValue && ConferenceOf(teams, ts.TeamId) == conference)
            {
                seedTeam[ts.Seed.Value] = ts.TeamId;
                m++;
            }
        }
        if (m < 6 || m > 12)
        {
            throw new InvalidOperationException($"College conference {conference} has {m} qualifiers; a legal split has 6–12.");
        }
        int[] leaves = m >= 9 ? Leaves16 : Leaves8;
        int firstRound = m >= 9 ? PostseasonCalendar.CollegeFirstBracketRound : PostseasonCalendar.CollegeFirstBracketRound + 1;
        int levels = m >= 9 ? 4 : 3;
        int root = NodeWinner(seasonYear, leaves, seedTeam, m, firstRound, levels, 0);
        return root > 0 ? root : 0;
    }

    private int NodeWinner(int seasonYear, int[] leaves, int[] seedTeam, int m, int firstRound, int level, int index)
    {
        if (level == 0)
        {
            int seed = leaves[index];
            return seed <= m ? seedTeam[seed] : Bye;
        }
        int a = NodeWinner(seasonYear, leaves, seedTeam, m, firstRound, level - 1, 2 * index);
        int b = NodeWinner(seasonYear, leaves, seedTeam, m, firstRound, level - 1, 2 * index + 1);
        if (a == Bye)
        {
            return b;
        }
        if (b == Bye)
        {
            return a;
        }
        if (a == Unknown || b == Unknown)
        {
            return Unknown;
        }
        int round = firstRound + level - 1;
        PlayoffSeriesRow? series = FindSeries(round, a);
        if (series is null)
        {
            // The better seed (= better national rank) is the high seed and hosts.
            (int high, int low) = SeedOf(a) < SeedOf(b) ? (a, b) : (b, a);
            CreateSeries(seasonYear, LeagueTier.College, round, high, low);
            return Unknown;
        }
        return series.Value.WinnerTeamId ?? Unknown;
    }

    /// <summary>D2 over the two teams' full regular-season records (no cross-conference H2H: RD → RA → coin).</summary>
    private (int High, int Low) HostByRecord(int seasonYear, LeagueTier tier, int a, int b)
    {
        _queries.LoadCompletedRegularSeason(seasonYear, tier, _completed);
        LeagueSimulator sim = _leagues.Get(tier);
        _table.Build(sim.TeamIds, _completed);
        List<int> order = Standings.Order(_table, new[] { a, b }, sim.ScheduleSeed, seasonYear, tier);
        return (order[0], order[1]);
    }

    // ------------------------------------------------------------------
    // Lookups over the loaded rows
    // ------------------------------------------------------------------

    private PlayoffSeriesRow? FindSeries(int round, int teamId)
    {
        foreach (PlayoffSeriesRow s in _series)
        {
            if (s.SeriesId != 0 && s.Round == round && s.Involves(teamId))
            {
                return s;
            }
        }
        return null;
    }

    private PlayoffSeriesRow? FindSeriesInRound(int round)
    {
        foreach (PlayoffSeriesRow s in _series)
        {
            if (s.SeriesId != 0 && s.Round == round)
            {
                return s;
            }
        }
        return null;
    }

    private PlayoffSeriesRow LoadSeriesRow(int seasonYear, LeagueTier tier, long seriesId)
    {
        _queries.LoadSeries(seasonYear, tier, _series);
        foreach (PlayoffSeriesRow s in _series)
        {
            if (s.SeriesId == seriesId)
            {
                return s;
            }
        }
        throw new InvalidOperationException($"Postseason game names series {seriesId}, which doesn't exist.");
    }

    private PostseasonGameRow FindGame(long gameId)
    {
        foreach (PostseasonGameRow row in _games)
        {
            if (row.GameId == gameId)
            {
                return row;
            }
        }
        throw new InvalidOperationException($"Postseason game {gameId} vanished.");
    }

    private int SeedOf(int teamId)
    {
        foreach (TeamSeasonRow ts in _teamSeasons)
        {
            if (ts.TeamId == teamId)
            {
                return ts.Seed ?? 0;
            }
        }
        return 0;
    }

    private int RankOf(int teamId)
    {
        foreach (TeamSeasonRow ts in _teamSeasons)
        {
            if (ts.TeamId == teamId)
            {
                return ts.NationalRank ?? int.MaxValue;
            }
        }
        return int.MaxValue;
    }

    private int TeamWithSeed(List<TeamRow> teams, int conference, int seed)
    {
        foreach (TeamSeasonRow ts in _teamSeasons)
        {
            if (ts.Seed == seed && ConferenceOf(teams, ts.TeamId) == conference)
            {
                return ts.TeamId;
            }
        }
        throw new InvalidOperationException($"Conference {conference} has no seed {seed}.");
    }

    private List<TeamRow> TeamsOf(LeagueTier tier)
    {
        List<TeamRow>? teams = _teamsByTier[(int)tier];
        if (teams is null)
        {
            teams = new List<TeamRow>(RosterCapacity.MaxTeamsPerTier);
            _baseball.LoadTeamsByTier(tier, teams);
            _teamsByTier[(int)tier] = teams;
        }
        return teams;
    }

    private static int ConferenceOf(List<TeamRow> teams, int teamId)
    {
        foreach (TeamRow t in teams)
        {
            if (t.TeamId == teamId)
            {
                return t.ConferenceId;
            }
        }
        throw new InvalidOperationException($"Team {teamId} is not in this tier.");
    }

    private static List<int> Conferences(List<TeamRow> teams)
    {
        var ids = new SortedSet<int>();
        foreach (TeamRow t in teams)
        {
            ids.Add(t.ConferenceId);
        }
        return new List<int>(ids);
    }

    private static List<int> Divisions(List<TeamRow> teams, int conference)
    {
        var ids = new SortedSet<int>();
        foreach (TeamRow t in teams)
        {
            if (t.ConferenceId == conference && t.DivisionId.HasValue)
            {
                ids.Add(t.DivisionId.Value);
            }
        }
        return new List<int>(ids);
    }

    private static List<int> TeamsIn(List<TeamRow> teams, int conference, int? division)
    {
        var ids = new List<int>();
        foreach (TeamRow t in teams)
        {
            if (t.ConferenceId == conference && (division is null || t.DivisionId == division))
            {
                ids.Add(t.TeamId);
            }
        }
        return ids;
    }

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------

    /// <summary>Publishes what the last committed batch decided (series, then champions).</summary>
    public void PublishPendingEvents()
    {
        if (_bus is not null)
        {
            foreach (PlayoffSeriesDecidedEvent e in _pendingDecided)
            {
                _bus.Publish(in e);
            }
            foreach (ChampionshipWonEvent e in _pendingChampions)
            {
                _bus.Publish(in e);
                _awaitingMarker.Add(e);
            }
        }
        _pendingDecided.Clear();
        _pendingChampions.Clear();
    }

    /// <summary>A batch rolled back: nothing it decided happened.</summary>
    public void DiscardPendingEvents()
    {
        _pendingDecided.Clear();
        _pendingChampions.Clear();
    }

    // ------------------------------------------------------------------
    // Read-only views (D6-B) and the planner's postseason blocks
    // ------------------------------------------------------------------

    // The views below run on the UI thread, possibly while an attended game
    // commits on its worker: they read into their own lists, never the
    // day handler's scratch (allocation is fine at UI time).

    /// <summary>A tier-season's bracket: every series with its score, next game and flags; the champion once crowned.</summary>
    public PlayoffBracketView GetBracket(int seasonYear, LeagueTier tier)
    {
        var series = new List<PlayoffSeriesRow>();
        var games = new List<PostseasonGameRow>();
        _queries.LoadSeries(seasonYear, tier, series);
        _queries.LoadPostseasonGames(seasonYear, tier, games);
        var views = new List<PlayoffSeriesView>(series.Count);
        int champion = 0;
        foreach (PlayoffSeriesRow s in series)
        {
            int nextNumber = 0;
            int nextDay = 0;
            foreach (PostseasonGameRow g in games)
            {
                if (g.SeriesId == s.SeriesId && !g.IsPlayed)
                {
                    nextNumber = g.GameInSeries;
                    nextDay = g.DayOfSeason;
                }
            }
            views.Add(new PlayoffSeriesView(
                s.SeriesId, s.Round, s.BestOf, s.HighSeedTeamId, s.LowSeedTeamId, s.HighWins, s.LowWins,
                s.WinnerTeamId ?? 0, nextNumber, nextDay,
                tier == LeagueTier.College && s.Round == PostseasonCalendar.CollegePlayInRound));
            if (s.Round == PostseasonCalendar.FinalRound(tier) && s.WinnerTeamId.HasValue)
            {
                champion = s.WinnerTeamId.Value;
            }
        }
        return new PlayoffBracketView(seasonYear, tier, champion, views);
    }

    /// <summary>The College National Ranking as last written (rank order; empty before week 1's flush).</summary>
    public IReadOnlyList<NationalRankView> GetNationalRanking(int seasonYear)
    {
        var rows = new List<TeamSeasonRow>();
        _queries.LoadTeamSeasons(seasonYear, LeagueTier.College, rows);
        var views = new List<NationalRankView>(rows.Count);
        foreach (TeamSeasonRow ts in rows)
        {
            if (ts.NationalRank.HasValue)
            {
                views.Add(new NationalRankView(ts.TeamId, ts.NationalRank.Value, ts.Wins, ts.Losses,
                    ts.RunsScored, ts.RunsAllowed, ts.Seed));
            }
        }
        views.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        return views;
    }

    /// <summary>
    /// The planner's postseason blocks for a team (spec §3.2, §16-I10): locked
    /// from selection day through its elimination or title day, free after;
    /// non-qualifiers are free from selection day. False before selection runs.
    /// </summary>
    public bool TryGetLockedPostseasonBlocks(
        int seasonYear, LeagueTier tier, int teamId, int dayOfSeason, out int practiceHours, out int gameHours)
    {
        practiceHours = 0;
        gameHours = 0;
        if (dayOfSeason < PostseasonCalendar.SelectionDay(tier) || dayOfSeason > PostseasonCalendar.LastDay(tier)
            || _queries.CountSeries(seasonYear, tier) == 0)
        {
            return false;
        }
        int lastAlive = LastAliveDay(seasonYear, tier, teamId, out bool hasGameToday, dayOfSeason);
        return PostseasonCalendar.TryGetLockedTeamBlocks(tier, dayOfSeason, lastAlive, hasGameToday, out practiceHours, out gameHours);
    }

    /// <summary>
    /// The team's last postseason day: its elimination or title game's day;
    /// the tier's last postseason day while alive; selection day − 1 for a
    /// non-qualifier.
    /// </summary>
    public int LastAliveDay(int seasonYear, LeagueTier tier, int teamId, out bool hasGameOn, int dayOfSeason)
    {
        var series = new List<PlayoffSeriesRow>();
        var games = new List<PostseasonGameRow>();
        var seasons = new List<TeamSeasonRow>();
        _queries.LoadSeries(seasonYear, tier, series);
        _queries.LoadPostseasonGames(seasonYear, tier, games);
        _queries.LoadTeamSeasons(seasonYear, tier, seasons);
        hasGameOn = false;
        foreach (PostseasonGameRow g in games)
        {
            if (g.DayOfSeason == dayOfSeason && g.Involves(teamId))
            {
                hasGameOn = true;
            }
        }
        bool qualified = seasons.Exists(ts => ts.TeamId == teamId && ts.Seed.HasValue);
        foreach (PlayoffSeriesRow s in series)
        {
            if (!s.Involves(teamId))
            {
                continue;
            }
            qualified = true;
            bool lost = s.WinnerTeamId.HasValue && s.WinnerTeamId.Value != teamId;
            bool title = s.WinnerTeamId == teamId && s.Round == PostseasonCalendar.FinalRound(tier);
            if (lost || title)
            {
                int last = 0;
                foreach (PostseasonGameRow g in games)
                {
                    if (g.SeriesId == s.SeriesId && g.DayOfSeason > last)
                    {
                        last = g.DayOfSeason;
                    }
                }
                return last;
            }
        }
        return qualified ? PostseasonCalendar.LastDay(tier) : PostseasonCalendar.SelectionDay(tier) - 1;
    }
}
