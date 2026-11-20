using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// One tier-season's persisted regular-season schedule in memory (slice
/// 13-3): the single table the tier's macro sim plays from and the career
/// driver claims the avatar's game from, so the attended-team skip and the
/// pending game always come from the same Schedule_Games row. Flat arrays
/// sized once by capacity; a load reuses them and a game-day lookup never
/// allocates.
/// </summary>
public sealed class SeasonSchedule
{
    private readonly long[] _gameIds;
    private readonly int[] _homeTeamIds;
    private readonly int[] _awayTeamIds;
    private readonly int[] _homeIndex;
    private readonly int[] _awayIndex;
    // Games of day d are [_dayStart[d], _dayStart[d + 1]); days 1..365.
    private readonly int[] _dayStart = new int[GlobalState.DaysPerSeason + 2];
    private int _count;

    public SeasonSchedule(LeagueTier tier)
    {
        Tier = tier;
        int capacity = TierSeasonCalendar.GameDays(tier).Length * RosterCapacity.MaxTeamsPerTier / 2;
        _gameIds = new long[capacity];
        _homeTeamIds = new int[capacity];
        _awayTeamIds = new int[capacity];
        _homeIndex = new int[capacity];
        _awayIndex = new int[capacity];
    }

    public LeagueTier Tier { get; }

    /// <summary>The loaded season; 0 = nothing loaded.</summary>
    public int SeasonYear { get; private set; }

    /// <summary>Games in the loaded season.</summary>
    public int Count => _count;

    /// <summary>
    /// Loads <paramref name="seasonYear"/>'s rows (play order) and maps team
    /// ids to <paramref name="teamIds"/>' indexes. A row naming a team outside
    /// the tier fails loudly.
    /// </summary>
    internal void Load(ScheduleQueries queries, int seasonYear, int[] teamIds, List<ScheduledGameRow> scratch)
    {
        queries.LoadRegularSeason(seasonYear, Tier, scratch);
        if (scratch.Count > _gameIds.Length)
        {
            throw new InvalidOperationException(
                $"{Tier} {seasonYear} has {scratch.Count} scheduled games; capacity is {_gameIds.Length}.");
        }
        Array.Clear(_dayStart);
        for (int i = 0; i < scratch.Count; i++)
        {
            ScheduledGameRow row = scratch[i];
            _gameIds[i] = row.GameId;
            _homeTeamIds[i] = row.HomeTeamId;
            _awayTeamIds[i] = row.AwayTeamId;
            _dayStart[row.DayOfSeason + 1]++;
        }
        for (int d = 1; d < _dayStart.Length; d++)
        {
            _dayStart[d] += _dayStart[d - 1];
        }
        _count = scratch.Count;
        SeasonYear = seasonYear;
        Reindex(teamIds);
    }

    /// <summary>Re-maps team ids to a (re-Initialized) sim's team-index order.</summary>
    internal void Reindex(int[] teamIds)
    {
        for (int i = 0; i < _count; i++)
        {
            _homeIndex[i] = IndexOf(teamIds, _homeTeamIds[i]);
            _awayIndex[i] = IndexOf(teamIds, _awayTeamIds[i]);
        }
    }

    private int IndexOf(int[] teamIds, int teamId)
    {
        int index = Array.IndexOf(teamIds, teamId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Scheduled team {teamId} is not in the {Tier} league.");
        }
        return index;
    }

    /// <summary>The day's games as a range [first, first + count) of game indexes.</summary>
    public int GamesOn(int dayOfSeason, out int first)
    {
        if (SeasonYear == 0 || dayOfSeason < 1 || dayOfSeason > GlobalState.DaysPerSeason)
        {
            first = 0;
            return 0;
        }
        first = _dayStart[dayOfSeason];
        return _dayStart[dayOfSeason + 1] - first;
    }

    public long GameId(int game) => _gameIds[game];

    public int HomeTeamId(int game) => _homeTeamIds[game];

    public int AwayTeamId(int game) => _awayTeamIds[game];

    internal int HomeIndex(int game) => _homeIndex[game];

    internal int AwayIndex(int game) => _awayIndex[game];

    /// <summary>The game <paramref name="teamId"/> plays on the day; false on an off day or when no season is loaded.</summary>
    public bool TryFindGame(int dayOfSeason, int teamId, out int game)
    {
        int count = GamesOn(dayOfSeason, out int first);
        for (int g = first; g < first + count; g++)
        {
            if (_homeTeamIds[g] == teamId || _awayTeamIds[g] == teamId)
            {
                game = g;
                return true;
            }
        }
        game = -1;
        return false;
    }
}
