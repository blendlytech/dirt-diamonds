namespace DirtAndDiamonds.Data;

// ----------------------------------------------------------------------
// Slice 13-4 (docs/tasks/13-4_playoffs_ranking_brief.md): row DTOs for
// Team_Seasons, Playoff_Series and postseason Schedule_Games, plus the
// read-only views presentation consumes (D6-B: the bracket screen is 15c's;
// these are its inputs). Views carry ids and numbers only; names resolve
// through the strings table and Teams at render time.
// ----------------------------------------------------------------------

/// <summary>One completed regular-season game (phase 0, status final or forfeit): the input of standings and the National Ranking.</summary>
public readonly struct CompletedGame
{
    public readonly int HomeTeamId;
    public readonly int AwayTeamId;
    public readonly int HomeScore;
    public readonly int AwayScore;

    public CompletedGame(int homeTeamId, int awayTeamId, int homeScore, int awayScore)
    {
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
        HomeScore = homeScore;
        AwayScore = awayScore;
    }

    public int WinnerTeamId => HomeScore > AwayScore ? HomeTeamId : AwayTeamId;
}

/// <summary>One Team_Seasons row (D1): regular-season record, College national rank, postseason seed.</summary>
public readonly record struct TeamSeasonRow(
    int TeamId, int SeasonYear, int Wins, int Losses, int RunsScored, int RunsAllowed, int? NationalRank, int? Seed);

/// <summary>One Playoff_Series row.</summary>
public readonly record struct PlayoffSeriesRow(
    long SeriesId, int SeasonYear, LeagueTier Tier, int Round, int BestOf,
    int HighSeedTeamId, int LowSeedTeamId, int HighWins, int LowWins, int? WinnerTeamId)
{
    /// <summary>Wins that decide the series (best_of / 2 + 1).</summary>
    public int WinsNeeded => BestOf / 2 + 1;

    public bool IsDecided => WinnerTeamId.HasValue;

    public bool Involves(int teamId) => HighSeedTeamId == teamId || LowSeedTeamId == teamId;
}

/// <summary>One postseason Schedule_Games row (phase 1 play-in or 2 playoff).</summary>
public readonly record struct PostseasonGameRow(
    long GameId, int SeasonYear, LeagueTier Tier, int DayOfSeason, int HomeTeamId, int AwayTeamId,
    int Phase, long SeriesId, int GameInSeries, int Status, int? HomeScore, int? AwayScore)
{
    public bool IsPlayed => Status != 0;

    public bool Involves(int teamId) => HomeTeamId == teamId || AwayTeamId == teamId;
}

/// <summary>
/// A series as presentation sees it (D6-B): score, the next game's number and
/// day, can-clinch and elimination flags, and the winner once decided.
/// </summary>
public readonly record struct PlayoffSeriesView(
    long SeriesId, int Round, int BestOf, int HighSeedTeamId, int LowSeedTeamId,
    int HighWins, int LowWins, int WinnerTeamId, int NextGameNumber, int NextGameDayOfSeason, bool IsPlayIn)
{
    public bool IsDecided => WinnerTeamId != 0;

    /// <summary>The high seed wins the series with its next win.</summary>
    public bool HighCanClinch => !IsDecided && HighWins == BestOf / 2;

    /// <summary>The low seed wins the series with its next win.</summary>
    public bool LowCanClinch => !IsDecided && LowWins == BestOf / 2;

    /// <summary>The next game can end the series (an elimination game for at least one side).</summary>
    public bool IsEliminationGame => HighCanClinch || LowCanClinch;
}

/// <summary>A tier-season's postseason as presentation sees it (D6-B).</summary>
public readonly record struct PlayoffBracketView(
    int SeasonYear, LeagueTier Tier, int ChampionTeamId, IReadOnlyList<PlayoffSeriesView> Series);

/// <summary>One line of the College National Ranking ("#7 nationally").</summary>
public readonly record struct NationalRankView(
    int TeamId, int Rank, int Wins, int Losses, int RunsScored, int RunsAllowed, int? Seed);

// ----------------------------------------------------------------------
// Slice 13-7 (docs/tasks/13-7_ripple_brief.md §4.5, D4-A): the standings view.
// Ids and numbers only, like the views above; names resolve at render time.
// ----------------------------------------------------------------------

/// <summary>
/// One team's line in a standings group: the regular-season record rebuilt from
/// completed games (finals and forfeits, the same rows <c>Team_Seasons</c> is
/// written from), plus the College national rank and the postseason seed once
/// those have been written (null before).
/// </summary>
public readonly record struct StandingsRowView(
    int TeamId, int Wins, int Losses, int RunsScored, int RunsAllowed, int? NationalRank, int? Seed);

/// <summary>
/// A conference (HS, which has no divisions: <see cref="DivisionId"/> is null)
/// or a division, with its rows ordered best first by <c>Standings.Order</c>.
/// </summary>
public readonly record struct StandingsGroupView(
    int ConferenceId, int? DivisionId, IReadOnlyList<StandingsRowView> Rows);

/// <summary>A tier-season's standings as presentation sees it: every group, conference then division order.</summary>
public readonly record struct StandingsView(int SeasonYear, LeagueTier Tier, IReadOnlyList<StandingsGroupView> Groups);
