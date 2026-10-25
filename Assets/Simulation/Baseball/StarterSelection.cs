using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// "Ride the ace" (league_restructure.md §6, CS-M §2; slice 13-2): the ONE
/// starter rule both sims use, so the source spec's 2-man rotations emerge
/// with no special-case AI.
///
/// <list type="number">
/// <item>Drop absent starters, and in a series every starter who already
/// started in it (the Red Dirt Series rule, §16-I7; hard, never bypassed).</item>
/// <item>The highest-quality remaining starter whose calendar-date gap since
/// his last start is ≥ <see cref="TierRules.MinRestDays"/> starts. A
/// never-started starter is rested.</item>
/// <item>Else the best-rested remaining starter starts on short rest (largest
/// gap; ties keep the quality order).</item>
/// <item>Else no starter can go: a reliever opens (a bullpen game).</item>
/// </list>
///
/// Quality (brief D2, CS-M §2): the starter's tier-and-person-baked stuff +
/// control (the mean, scaled), before in-game fatigue; ties by stamina
/// descending, then player id ascending. Callers rank each team's starters
/// at Initialize with <see cref="CompareQuality"/>, keep that order current
/// whenever a baked rating moves (MicroGame's per-game person refresh
/// re-ranks in place; the macro sim bakes only at Initialize), and pass
/// candidates in that order, so selection itself is a scan: pure,
/// allocation-free, and it draws no random number.
/// </summary>
public static class StarterSelection
{
    /// <summary>The <c>lastStartDay</c> value for a pitcher who has never started.</summary>
    public const long NeverStarted = long.MinValue;

    /// <summary>
    /// Index of today's starter among <paramref name="lastStartDay"/>'s
    /// candidates (in quality order), or -1 when none can start (a bullpen
    /// game). <paramref name="shortRest"/> is true only for the step-3 fallback.
    /// </summary>
    public static int SelectStarter(
        ReadOnlySpan<long> lastStartDay, ReadOnlySpan<bool> available, ReadOnlySpan<bool> usedInSeries,
        long day, int minRestDays, out bool shortRest)
    {
        for (int i = 0; i < lastStartDay.Length; i++)
        {
            if (available[i] && !usedInSeries[i] && Gap(lastStartDay[i], day) >= minRestDays)
            {
                shortRest = false;
                return i;
            }
        }

        int best = -1;
        long bestGap = long.MinValue;
        for (int i = 0; i < lastStartDay.Length; i++)
        {
            if (!available[i] || usedInSeries[i])
            {
                continue;
            }
            long gap = Gap(lastStartDay[i], day);
            if (gap > bestGap)
            {
                bestGap = gap;
                best = i;
            }
        }
        shortRest = best >= 0;
        return best;
    }

    /// <summary>The bullpen-game opener: the first available reliever in quality order, or -1.</summary>
    public static int SelectOpener(ReadOnlySpan<bool> relieverAvailable)
    {
        for (int i = 0; i < relieverAvailable.Length; i++)
        {
            if (relieverAvailable[i])
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Calendar-date gap; a never-started pitcher is infinitely rested.</summary>
    public static long Gap(long lastStartDay, long day) =>
        lastStartDay == NeverStarted ? long.MaxValue : day - lastStartDay;

    /// <summary>The D2 quality score: baked stuff + control (twice the mean, so integers compare exactly).</summary>
    public static int Quality(in PitcherRatings baked) => baked.Stuff + baked.Control;

    /// <summary>
    /// The D2 ordering, best first: quality descending, then stamina
    /// descending, then player id ascending (ordinal), so ties are stable
    /// across both sims and every reload.
    /// </summary>
    public static int CompareQuality(
        in PitcherRatings a, string aPlayerId, in PitcherRatings b, string bPlayerId)
    {
        int byQuality = Quality(in b).CompareTo(Quality(in a));
        if (byQuality != 0)
        {
            return byQuality;
        }
        int byStamina = b.Stamina.CompareTo(a.Stamina);
        return byStamina != 0 ? byStamina : string.CompareOrdinal(aPlayerId, bPlayerId);
    }
}
