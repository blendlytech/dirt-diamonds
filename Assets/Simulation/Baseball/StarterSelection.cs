namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>How a game's starting pitcher was chosen (league_restructure.md §6).</summary>
public enum StartKind : byte
{
    /// <summary>The highest-rated starter with full rest.</summary>
    Rested,
    /// <summary>No starter had full rest: the best-rested starter goes on short rest.</summary>
    ShortRest,
    /// <summary>No starter was available at all: a reliever opens (a bullpen game).</summary>
    Opener,
    /// <summary>No pitcher was available: the caller's legacy replacement call-up shadows a slot (retired in 13-5).</summary>
    None,
}

/// <summary>One pitcher the selection may pick, in roster-slot order.</summary>
public struct StarterCandidate
{
    public int Slot;

    /// <summary>Selection rating: raw stuff + control + stamina (the same in both sims by construction).</summary>
    public int Score;

    /// <summary>Absolute day of the last start, or <see cref="StarterSelection.NeverStarted"/>.</summary>
    public long LastStartDay;

    /// <summary>False while the player is absent (AvailabilityLedger).</summary>
    public bool Available;
}

/// <summary>
/// §6 "ride the ace", one rule for both sims: on each game day the
/// highest-rated available starter whose days since his last start are at
/// least <c>minRestDays</c> starts. If none qualifies, the best-rested
/// available starter starts on short rest; if no starter is available, the
/// best-rated available reliever opens. Ties go to the earlier roster slot,
/// so a spread-0 world picks deterministically. Pure and allocation-free.
/// </summary>
public static class StarterSelection
{
    /// <summary>A pitcher who has never started counts as fully rested.</summary>
    public const long NeverStarted = long.MinValue / 4;

    public static int Choose(
        ReadOnlySpan<StarterCandidate> starters, ReadOnlySpan<StarterCandidate> relievers,
        long today, int minRestDays, out StartKind kind)
    {
        int best = -1;
        int bestScore = int.MinValue;
        foreach (ref readonly StarterCandidate c in starters)
        {
            if (c.Available && today - c.LastStartDay >= minRestDays && c.Score > bestScore)
            {
                best = c.Slot;
                bestScore = c.Score;
            }
        }
        if (best >= 0)
        {
            kind = StartKind.Rested;
            return best;
        }

        long bestRest = long.MinValue;
        foreach (ref readonly StarterCandidate c in starters)
        {
            if (!c.Available)
            {
                continue;
            }
            long rest = today - c.LastStartDay;
            if (rest > bestRest || (rest == bestRest && c.Score > bestScore))
            {
                best = c.Slot;
                bestRest = rest;
                bestScore = c.Score;
            }
        }
        if (best >= 0)
        {
            kind = StartKind.ShortRest;
            return best;
        }

        foreach (ref readonly StarterCandidate c in relievers)
        {
            if (c.Available && c.Score > bestScore)
            {
                best = c.Slot;
                bestScore = c.Score;
            }
        }
        kind = best >= 0 ? StartKind.Opener : StartKind.None;
        return best;
    }

    /// <summary>The selection score both sims use: raw stuff + control + stamina.</summary>
    public static int ScoreOf(int pitStuff, int pitControl, int pitStamina) => pitStuff + pitControl + pitStamina;
}
