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
/// 13-2 brief D3): a deterministic legal defense from Player_Fielding, built
/// once at Initialize (load-time code, so allocation is fine).
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
