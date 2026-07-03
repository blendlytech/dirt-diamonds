using System.Numerics;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Shared base-out advancement — the single source of truth for how the seven
/// <see cref="PaOutcome"/> events move runners (macro doc §7, micro doc §3).
/// Both sims call these functions per PA, and <see cref="BaseOutMatrices"/>
/// enumerates the same deterministic cores to materialize the analytic 25×25
/// A_e matrices, so the runtime and matrix representations can never drift.
///
/// Bases are 3 bits (1 = runner on 1B, 2 = 2B, 4 = 3B); outs belong to the
/// caller. Single/Double each carry one discretionary runner decision; the
/// decision is an explicit parameter on the deterministic core, with an
/// rng-drawing wrapper on top — runtime draws, the matrix builder enumerates
/// both branches with the knob weights. All functions are allocation-free.
/// </summary>
public static class BaseOutAdvancement
{
    // §7/§3 discretionary-advance calibration knobs, shared by both sims
    // (tuning order: macro §8 step 4 / micro §11).
    public const double SingleScoresFrom2nd = 0.60;
    public const double DoubleScoresFrom1st = 0.45;

    // §2 canonical base-out state index: state = bases*3 + outs for the 24
    // transient states (0..23); state 24 is the absorbing third out.
    public const int StateCount = 25;
    public const int TransientStateCount = 24;
    public const int AbsorbingState = 24;

    public static int ToState(int bases, int outs) =>
        outs >= 3 ? AbsorbingState : bases * 3 + outs;

    public static int BasesOfState(int state) => state / 3;

    public static int OutsOfState(int state) => state % 3;

    /// <summary>
    /// Applies one terminal PA event to (bases, outs); returns runs scored.
    /// Out/Strikeout advance no runners in this model (macro doc §1 folds
    /// productive outs into the calibrated hit rates).
    /// </summary>
    public static int Advance(PaOutcome outcome, ref int bases, ref int outs, ref RngState rng)
    {
        switch (outcome)
        {
            case PaOutcome.Out:
            case PaOutcome.Strikeout:
                outs++;
                return 0;
            case PaOutcome.Walk:
                return AdvanceWalk(ref bases);
            case PaOutcome.Single:
                return AdvanceSingle(ref bases, ref rng);
            case PaOutcome.Double:
                return AdvanceDouble(ref bases, ref rng);
            case PaOutcome.Triple:
                return AdvanceTriple(ref bases);
            default:
                return AdvanceHomeRun(ref bases);
        }
    }

    /// <summary>Walk: batter to 1B, force-only advancement (§7). Deterministic.</summary>
    public static int AdvanceWalk(ref int bases)
    {
        if ((bases & 0b001) == 0)
        {
            bases |= 0b001;
            return 0;
        }
        if ((bases & 0b010) == 0)
        {
            bases |= 0b010; // runner forced 1B→2B, batter takes 1B
            return 0;
        }
        if ((bases & 0b100) == 0)
        {
            bases = 0b111; // forced chain fills the bases
            return 0;
        }
        return 1; // bases loaded stay loaded; the runner from 3B scores
    }

    /// <summary>Single: draws the runner-from-2B decision only when it exists (draw-order stable).</summary>
    public static int AdvanceSingle(ref int bases, ref RngState rng)
    {
        bool runnerFrom2ndScores = (bases & 0b010) != 0 && rng.NextDouble() < SingleScoresFrom2nd;
        return AdvanceSingle(ref bases, runnerFrom2ndScores);
    }

    /// <summary>Single core: batter to 1B; R3 scores; R2 scores or holds at 3B per the decision; R1 to 2B.</summary>
    public static int AdvanceSingle(ref int bases, bool runnerFrom2ndScores)
    {
        int runs = 0;
        int next = 0b001;
        if ((bases & 0b100) != 0)
        {
            runs++;
        }
        if ((bases & 0b010) != 0)
        {
            if (runnerFrom2ndScores)
            {
                runs++;
            }
            else
            {
                next |= 0b100;
            }
        }
        if ((bases & 0b001) != 0)
        {
            next |= 0b010;
        }
        bases = next;
        return runs;
    }

    /// <summary>Double: draws the runner-from-1B decision only when it exists (draw-order stable).</summary>
    public static int AdvanceDouble(ref int bases, ref RngState rng)
    {
        bool runnerFrom1stScores = (bases & 0b001) != 0 && rng.NextDouble() < DoubleScoresFrom1st;
        return AdvanceDouble(ref bases, runnerFrom1stScores);
    }

    /// <summary>Double core: batter to 2B; R3 and R2 score; R1 scores or holds at 3B per the decision.</summary>
    public static int AdvanceDouble(ref int bases, bool runnerFrom1stScores)
    {
        int runs = 0;
        int next = 0b010;
        if ((bases & 0b100) != 0)
        {
            runs++;
        }
        if ((bases & 0b010) != 0)
        {
            runs++;
        }
        if ((bases & 0b001) != 0)
        {
            if (runnerFrom1stScores)
            {
                runs++;
            }
            else
            {
                next |= 0b100;
            }
        }
        bases = next;
        return runs;
    }

    /// <summary>Triple: batter to 3B, all runners score. Deterministic.</summary>
    public static int AdvanceTriple(ref int bases)
    {
        int runs = BitOperations.PopCount((uint)bases);
        bases = 0b100;
        return runs;
    }

    /// <summary>Home run: bases cleared, batter and all runners score. Deterministic.</summary>
    public static int AdvanceHomeRun(ref int bases)
    {
        int runs = BitOperations.PopCount((uint)bases) + 1;
        bases = 0;
        return runs;
    }
}
