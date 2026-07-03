namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Terminal plate-appearance outcomes of the macro-sim, in the §1 table order —
/// the index into every constant table below. HBP is folded into Walk; ROE and
/// sacrifices are folded into Out (design doc §1).
/// </summary>
public enum PaOutcome : byte
{
    Out,
    Strikeout,
    Walk,
    Single,
    Double,
    Triple,
    HomeRun,
}

/// <summary>
/// Batter inputs to one PA. Built from a Player_Ratings row plus the player's
/// live "ped_active" flag — carrying the flag here means the resolver applies
/// the clamped 1.5× power multiplier itself and no caller can forget it (§6/§9).
/// </summary>
public readonly struct BatterRatings
{
    public readonly byte Power;
    public readonly byte Contact;
    public readonly byte Discipline;
    public readonly bool PedActive;

    public BatterRatings(byte power, byte contact, byte discipline, bool pedActive)
    {
        Power = power;
        Contact = contact;
        Discipline = discipline;
        PedActive = pedActive;
    }
}

/// <summary>
/// Pitcher inputs to one PA. Stamina is carried for the Phase 4 micro-sim's
/// fatigue curve; it is not a §4 matchup input.
/// </summary>
public readonly struct PitcherRatings
{
    public readonly byte Stuff;
    public readonly byte Control;
    public readonly byte Stamina;

    public PitcherRatings(byte stuff, byte control, byte stamina)
    {
        Stuff = stuff;
        Control = control;
        Stamina = stamina;
    }
}

/// <summary>
/// Pure, allocation-free plate-appearance resolver implementing
/// docs/design/baseball_pa_outcome_model.md: log-linear matchup modulation of
/// MLB-calibrated baselines, renormalized, sampled with a single uniform draw.
/// Every number is a named calibration table — tuning for run_monte_carlo_batch
/// is a data edit here, never a logic edit (§9).
/// </summary>
public static class AtBatResolver
{
    public const int OutcomeCount = 7;

    /// <summary>§6 PED hook: min(100, round(power × 1.5)) before normalization.</summary>
    public const double PedPowerMultiplier = 1.5;

    // §2 league-average baselines — must sum to 1; all ratings 50 reproduces
    // these exactly (.247/.315/.412, .727 OPS). Retune only this block if the
    // target MLB era shifts.
    private static readonly double[] BaseProbabilities =
    {
        0.460, // Out
        0.225, // Strikeout
        0.090, // Walk (incl. HBP)
        0.143, // Single
        0.046, // Double
        0.004, // Triple
        0.032, // HomeRun
    };

    // §4.2 sensitivities k_O — how far a matchup swing moves each outcome.
    private static readonly double[] Sensitivities =
    {
        0.35, // Out
        0.55, // Strikeout
        0.55, // Walk
        0.40, // Single
        0.45, // Double
        0.35, // Triple
        0.70, // HomeRun
    };

    // §4.1 matchup index weights, row per outcome, columns in DeviationSlot
    // order. Strikeout/Walk/HomeRun rows carry NO fielding term by construction
    // (§4.3) — do not add one.
    private const int DeviationCount = 6;

    private static class DeviationSlot
    {
        public const int BatPower = 0;
        public const int BatContact = 1;
        public const int BatDiscipline = 2;
        public const int PitStuff = 3;
        public const int PitControl = 4;
        public const int TeamDefense = 5;
    }

    private static readonly double[] MatchupWeights =
    {
        //  bPow   bCon   bDis  pStuff  pCtl   dFld
        -0.20, -0.70,  0.00,  0.50,  0.00,  0.60, // Out
         0.00, -0.80, -0.20,  1.00,  0.00,  0.00, // Strikeout
         0.00,  0.00,  1.00,  0.00, -1.00,  0.00, // Walk
         0.00,  0.90,  0.00, -0.30,  0.00, -0.50, // Single
         0.60,  0.30,  0.00, -0.20,  0.00, -0.40, // Double
         0.20,  0.20,  0.00,  0.00,  0.00, -0.30, // Triple
         1.00,  0.00,  0.00, -0.40,  0.00,  0.00, // HomeRun
    };

    /// <summary>
    /// Fills <paramref name="probabilities"/> (length ≥ 7) with the normalized
    /// outcome distribution for this matchup. Exposed separately from
    /// <see cref="Resolve"/> so the harness can assert the §5 numeric fixtures
    /// without consuming RNG draws.
    /// </summary>
    public static void ComputeProbabilities(
        in BatterRatings batter, in PitcherRatings pitcher, byte fielding, Span<double> probabilities)
    {
        // §6: clamped 1.5× effective power under an active PED flag, applied to
        // the raw 0–100 rating before deviation normalization. Integer
        // round-half-up form of round(power * 1.5).
        int power = batter.PedActive
            ? Math.Min(100, (batter.Power * 3 + 1) / 2)
            : batter.Power;

        // §3: every rating maps to a signed deviation in [-1, +1]; 50 = 0.
        Span<double> deviations = stackalloc double[DeviationCount];
        deviations[DeviationSlot.BatPower] = (power - 50) / 50.0;
        deviations[DeviationSlot.BatContact] = (batter.Contact - 50) / 50.0;
        deviations[DeviationSlot.BatDiscipline] = (batter.Discipline - 50) / 50.0;
        deviations[DeviationSlot.PitStuff] = (pitcher.Stuff - 50) / 50.0;
        deviations[DeviationSlot.PitControl] = (pitcher.Control - 50) / 50.0;
        deviations[DeviationSlot.TeamDefense] = (fielding - 50) / 50.0;

        // §4: w_O = p_base[O] * exp(k_O * m_O), then renormalize. exp keeps
        // every weight positive, so any rating combination yields a valid
        // distribution.
        double total = 0.0;
        for (int outcome = 0; outcome < OutcomeCount; outcome++)
        {
            double matchupIndex = 0.0;
            int row = outcome * DeviationCount;
            for (int i = 0; i < DeviationCount; i++)
            {
                matchupIndex += MatchupWeights[row + i] * deviations[i];
            }
            double weight = BaseProbabilities[outcome] * Math.Exp(Sensitivities[outcome] * matchupIndex);
            probabilities[outcome] = weight;
            total += weight;
        }
        for (int outcome = 0; outcome < OutcomeCount; outcome++)
        {
            probabilities[outcome] /= total;
        }
    }

    /// <summary>
    /// Resolves one plate appearance: computes the matchup distribution and
    /// samples it with a single uniform draw against the cumulative (§9).
    /// Zero heap allocation.
    /// </summary>
    public static PaOutcome Resolve(
        in BatterRatings batter, in PitcherRatings pitcher, byte fielding, ref RngState rng)
    {
        Span<double> probabilities = stackalloc double[OutcomeCount];
        ComputeProbabilities(in batter, in pitcher, fielding, probabilities);

        double draw = rng.NextDouble();
        double cumulative = 0.0;
        for (int outcome = 0; outcome < OutcomeCount - 1; outcome++)
        {
            cumulative += probabilities[outcome];
            if (draw < cumulative)
            {
                return (PaOutcome)outcome;
            }
        }
        // Floating-point remainder lands on the last bucket.
        return PaOutcome.HomeRun;
    }
}
