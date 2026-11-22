using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Every 13-4 random stream and coin (13-4 brief §2 Amendment 3, Math record
/// §10.1 Q7/Q8): chained SplitMix64 avalanches over persisted inputs only, so
/// the same save and seed give the same bracket, games and champions across
/// any reload. None of these ever reads or advances a sim's <see cref="RngState"/>
/// (<c>LeagueSimulator._rng</c>, <c>CareerManager._rng</c>), which is what keeps
/// the regular season, and the sanctioned Pro guard, byte-exact with the
/// postseason running (D4-A).
///
/// The 64-bit domain tags are ASCII labels, disjoint from
/// <see cref="SeasonScheduler.StreamFor"/>'s inputs.
/// </summary>
public static class PostseasonStreams
{
    public const ulong PostMacroGameTag = 0x504F_5354_4D41_4352UL; // "POSTMACR"
    public const ulong PostMicroGameTag = 0x504F_5354_4D49_4352UL; // "POSTMICR"
    public const ulong StandingsCoinTag = 0x5354_414E_4436_345FUL; // "STAND64_"
    public const ulong RankingCoinTag = 0x5241_4E4B_4736_345FUL;   // "RANKG64_"
    public const ulong Subsample14Tag = 0x5355_4231_3443_4D50UL;   // "SUB14CMP"

    /// <summary>SplitMix64's finalizer: a bijective 64-bit avalanche.</summary>
    public static ulong Mix64(ulong z)
    {
        z = unchecked(z + 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    /// <summary>Chains one more key into a running hash.</summary>
    public static ulong MixCombine(ulong h, ulong k) => Mix64(h ^ Mix64(k));

    /// <summary>
    /// The natural series key (round, high seed, low seed), never the
    /// auto-rowid <c>series_id</c>: another tier's playoffs running first
    /// would shift a rowid, not this. Unique by construction (a team plays
    /// once per round); the harness asserts it.
    /// </summary>
    public static ulong SeriesKey(int round, int highSeedTeamId, int lowSeedTeamId) =>
        ((ulong)(uint)round << 32) | ((ulong)(uint)highSeedTeamId << 16) | (uint)lowSeedTeamId;

    /// <summary>One postseason game's stream (macro: POSTMACR; attended: POSTMICR).</summary>
    public static RngState ForGame(
        ulong scheduleSeed, int seasonYear, LeagueTier tier,
        int round, int highSeedTeamId, int lowSeedTeamId, int gameInSeries, bool isAttended = false)
    {
        ulong tag = isAttended ? PostMicroGameTag : PostMacroGameTag;
        ulong h = MixCombine(scheduleSeed, tag);
        h = MixCombine(h, unchecked((ulong)seasonYear));
        h = MixCombine(h, (ulong)tier);
        h = MixCombine(h, SeriesKey(round, highSeedTeamId, lowSeedTeamId));
        h = MixCombine(h, unchecked((ulong)gameInSeries));
        return new RngState(h);
    }

    /// <summary>D2's seeded coin key for a team (ascending wins the coin).</summary>
    public static ulong StandingsCoinKey(ulong scheduleSeed, int seasonYear, LeagueTier tier, int teamId) =>
        CoinKey(StandingsCoinTag, scheduleSeed, seasonYear, tier, teamId);

    /// <summary>Amendment 2's National Ranking exact-tie coin key (ascending wins the coin).</summary>
    public static ulong RankingCoinKey(ulong scheduleSeed, int seasonYear, LeagueTier tier, int teamId) =>
        CoinKey(RankingCoinTag, scheduleSeed, seasonYear, tier, teamId);

    private static ulong CoinKey(ulong tag, ulong scheduleSeed, int seasonYear, LeagueTier tier, int teamId)
    {
        ulong h = MixCombine(scheduleSeed, tag);
        h = MixCombine(h, unchecked((ulong)seasonYear));
        h = MixCombine(h, (ulong)tier);
        return MixCombine(h, unchecked((ulong)teamId));
    }

    /// <summary>D5.1's 14-game sub-sampler stream for one (world, scheduler, outcome) analysis tuple.</summary>
    public static RngState Subsample14(ulong scheduleSeed, int world, int scheduler, int outcome)
    {
        ulong h = MixCombine(scheduleSeed, Subsample14Tag);
        h = MixCombine(h, unchecked((ulong)world));
        h = MixCombine(h, unchecked((ulong)scheduler));
        return new RngState(MixCombine(h, unchecked((ulong)outcome)));
    }
}
