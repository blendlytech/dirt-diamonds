using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>One position player a team could start (13-2 brief D3).</summary>
public readonly record struct LineupCandidate(
    int Slot, string PlayerId, int BatScore, FieldingPosition? Primary, FieldingPosition? Secondary);

/// <summary>
/// A built lineup: <see cref="Slots"/> in batting order, with
/// <see cref="Positions"/> parallel to it (null = the designated hitter).
/// <see cref="OutOfPosition"/> counts starters playing a position that is
/// neither their primary nor their secondary — recorded for G-4; 13-2 gives
/// it no rating effect.
/// </summary>
public sealed record LineupResult(int[] Slots, FieldingPosition?[] Positions, int OutOfPosition);

/// <summary>
/// Who plays (league_restructure.md §5, arcade_baseball_overhaul.md §8.1;
/// 13-2 brief D3): a deterministic legal defense from Player_Fielding.
/// <see cref="Build"/> runs at load time (allocation is fine there);
/// <see cref="BuildInto"/> is its allocation-free twin for the game-day
/// reshuffle around absent or inactive players (13-5).
///
/// <list type="number">
/// <item>For C, 1B, 2B, 3B, SS, LF, CF, RF in that order: the best unchosen
/// player whose primary is that position; else whose secondary is.</item>
/// <item>Any position still open: the best unchosen player, out of position.</item>
/// <item>The DH: the best player left.</item>
/// <item>A featured player (the avatar) always starts: if the steps above
/// left him out, he replaces the DH.</item>
/// </list>
///
/// "Best" = the batter role-rating sum (power + contact + discipline) the 9c
/// scouting score uses, ties by player id ascending. The batting order is the
/// nine chosen in roster-slot order (the shipped order), so lineup
/// construction changes who plays, never how the order is drawn up.
/// </summary>
public static class LineupBuilder
{
    public static LineupResult Build(IReadOnlyList<LineupCandidate> candidates, int featuredSlot = -1)
    {
        if (candidates.Count < RosterCapacity.Lineup)
        {
            throw new InvalidOperationException(
                $"A lineup needs {RosterCapacity.Lineup} position players; the team has {candidates.Count}.");
        }

        var order = new List<LineupCandidate>(candidates);
        order.Sort(static (a, b) =>
        {
            int byBat = b.BatScore.CompareTo(a.BatScore);
            return byBat != 0 ? byBat : string.CompareOrdinal(a.PlayerId, b.PlayerId);
        });

        var chosen = new Dictionary<int, FieldingPosition?>(RosterCapacity.Lineup);
        var open = new List<FieldingPosition>(FieldingPositions.Field.Length);
        foreach (FieldingPosition position in FieldingPositions.Field)
        {
            int pick = FirstUnchosen(order, chosen, c => c.Primary == position);
            if (pick < 0)
            {
                pick = FirstUnchosen(order, chosen, c => c.Secondary == position);
            }
            if (pick < 0)
            {
                open.Add(position);
                continue;
            }
            chosen.Add(order[pick].Slot, position);
        }

        int outOfPosition = 0;
        foreach (FieldingPosition position in open)
        {
            int pick = FirstUnchosen(order, chosen, static _ => true);
            chosen.Add(order[pick].Slot, position);
            outOfPosition++;
        }

        bool featuredIsCandidate = featuredSlot >= 0 && order.Exists(c => c.Slot == featuredSlot);
        if (featuredIsCandidate && !chosen.ContainsKey(featuredSlot))
        {
            chosen.Add(featuredSlot, null);
        }
        else
        {
            chosen.Add(order[FirstUnchosen(order, chosen, static _ => true)].Slot, null);
        }

        int[] slots = chosen.Keys.Order().ToArray();
        var positions = new FieldingPosition?[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            positions[i] = chosen[slots[i]];
        }
        return new LineupResult(slots, positions, outOfPosition);
    }

    /// <summary>The <see cref="BuildInto"/> position code for the designated hitter.</summary>
    public const sbyte DesignatedHitter = -1;

    /// <summary>
    /// The allocation-free twin of <see cref="Build"/> for the game-day path
    /// (slice 13-5, brief §4 item 4): the same four steps, the same picks,
    /// written into caller buffers. <paramref name="ordered"/> must already be
    /// in <see cref="Build"/>'s order (bat score descending, then player id
    /// ordinal ascending); callers keep a per-team sorted roster from load
    /// time and filter it to today's eligible players, so no sort runs here.
    /// <paramref name="slots"/> receives the nine roster slots in batting
    /// (slot) order and <paramref name="positions"/> their positions as
    /// <c>(sbyte)FieldingPosition</c> or <see cref="DesignatedHitter"/>.
    /// Returns the out-of-position count. The MC harness proves it equal to
    /// <see cref="Build"/> slot for slot over random eligible subsets.
    /// </summary>
    public static int BuildInto(
        ReadOnlySpan<LineupCandidate> ordered, int featuredSlot, Span<int> slots, Span<sbyte> positions)
    {
        int n = ordered.Length;
        if (n < RosterCapacity.Lineup)
        {
            throw new InvalidOperationException(
                $"A lineup needs {RosterCapacity.Lineup} position players; the team has {n}.");
        }
        if (n > RosterCapacity.MaxPositionPlayers)
        {
            throw new InvalidOperationException(
                $"BuildInto takes at most {RosterCapacity.MaxPositionPlayers} candidates; got {n}.");
        }

        Span<bool> taken = stackalloc bool[RosterCapacity.MaxPositionPlayers];
        taken.Clear();
        Span<int> pickIndex = stackalloc int[RosterCapacity.Lineup];
        Span<sbyte> pickPosition = stackalloc sbyte[RosterCapacity.Lineup];
        Span<sbyte> open = stackalloc sbyte[8];
        int picks = 0;
        int openCount = 0;

        // Step 1: primary, then secondary, position by position.
        FieldingPosition[] field = FieldingPositions.Field;
        for (int f = 0; f < field.Length; f++)
        {
            FieldingPosition position = field[f];
            int pick = -1;
            for (int i = 0; i < n; i++)
            {
                if (!taken[i] && ordered[i].Primary == position)
                {
                    pick = i;
                    break;
                }
            }
            if (pick < 0)
            {
                for (int i = 0; i < n; i++)
                {
                    if (!taken[i] && ordered[i].Secondary == position)
                    {
                        pick = i;
                        break;
                    }
                }
            }
            if (pick < 0)
            {
                open[openCount++] = (sbyte)position;
                continue;
            }
            taken[pick] = true;
            pickIndex[picks] = pick;
            pickPosition[picks++] = (sbyte)position;
        }

        // Step 2: every open position takes the best unchosen player, out of position.
        int outOfPosition = 0;
        for (int o = 0; o < openCount; o++)
        {
            int pick = FirstUntaken(taken, n);
            taken[pick] = true;
            pickIndex[picks] = pick;
            pickPosition[picks++] = open[o];
            outOfPosition++;
        }

        // Step 3: the DH, or the featured player if the steps above left him out.
        int featuredIndex = -1;
        if (featuredSlot >= 0)
        {
            for (int i = 0; i < n; i++)
            {
                if (ordered[i].Slot == featuredSlot)
                {
                    featuredIndex = i;
                    break;
                }
            }
        }
        int dh = featuredIndex >= 0 && !taken[featuredIndex] ? featuredIndex : FirstUntaken(taken, n);
        taken[dh] = true;
        pickIndex[picks] = dh;
        pickPosition[picks++] = DesignatedHitter;

        // Step 4: batting order is roster-slot order (insertion sort over nine).
        for (int i = 1; i < picks; i++)
        {
            int index = pickIndex[i];
            sbyte position = pickPosition[i];
            int slot = ordered[index].Slot;
            int j = i - 1;
            while (j >= 0 && ordered[pickIndex[j]].Slot > slot)
            {
                pickIndex[j + 1] = pickIndex[j];
                pickPosition[j + 1] = pickPosition[j];
                j--;
            }
            pickIndex[j + 1] = index;
            pickPosition[j + 1] = position;
        }
        for (int k = 0; k < RosterCapacity.Lineup; k++)
        {
            slots[k] = ordered[pickIndex[k]].Slot;
            positions[k] = pickPosition[k];
        }
        return outOfPosition;
    }

    /// <summary><see cref="Build"/>'s candidate order: bat score descending, then player id ordinal ascending.</summary>
    public static int CompareOrder(in LineupCandidate a, in LineupCandidate b)
    {
        int byBat = b.BatScore.CompareTo(a.BatScore);
        return byBat != 0 ? byBat : string.CompareOrdinal(a.PlayerId, b.PlayerId);
    }

    private static int FirstUntaken(ReadOnlySpan<bool> taken, int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (!taken[i])
            {
                return i;
            }
        }
        throw new InvalidOperationException("LineupBuilder ran out of candidates (fewer than nine).");
    }

    private static int FirstUnchosen(
        List<LineupCandidate> order, Dictionary<int, FieldingPosition?> chosen, Func<LineupCandidate, bool> match)
    {
        for (int i = 0; i < order.Count; i++)
        {
            if (!chosen.ContainsKey(order[i].Slot) && match(order[i]))
            {
                return i;
            }
        }
        return -1;
    }
}
