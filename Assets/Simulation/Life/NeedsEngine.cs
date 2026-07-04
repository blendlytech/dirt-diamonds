using System;

namespace DirtAndDiamonds.Simulation.Life;

public enum NeedType
{
    Hunger = 0,
    Sleep = 1,
    Hygiene = 2,
    Social = 3,
    Fitness = 4,
}

public struct NeedsState
{
    public float Hunger;
    public float Sleep;
    public float Hygiene;
    public float Social;
    public float Fitness;

    public static NeedsState FullySatisfied() => new()
    {
        Hunger = NeedsEngine.MaxNeed,
        Sleep = NeedsEngine.MaxNeed,
        Hygiene = NeedsEngine.MaxNeed,
        Social = NeedsEngine.MaxNeed,
        Fitness = NeedsEngine.MaxNeed,
    };

    public readonly float Get(NeedType need) => need switch
    {
        NeedType.Hunger => Hunger,
        NeedType.Sleep => Sleep,
        NeedType.Hygiene => Hygiene,
        NeedType.Social => Social,
        NeedType.Fitness => Fitness,
        _ => throw new ArgumentOutOfRangeException(nameof(need)),
    };

    public void Set(NeedType need, float value)
    {
        switch (need)
        {
            case NeedType.Hunger: Hunger = value; break;
            case NeedType.Sleep: Sleep = value; break;
            case NeedType.Hygiene: Hygiene = value; break;
            case NeedType.Social: Social = value; break;
            case NeedType.Fitness: Fitness = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(need));
        }
    }

    // Additive companion to Set (life_sim_needs_decay.md §6): actions restore
    // needs by a delta rather than assigning an absolute value.
    public void Restore(NeedType need, float amount) =>
        Set(need, Math.Clamp(Get(need) + amount, NeedsEngine.MinNeed, NeedsEngine.MaxNeed));

    public readonly bool AnyAtOrBelow(float threshold) =>
        Hunger <= threshold || Sleep <= threshold || Hygiene <= threshold || Social <= threshold || Fitness <= threshold;
}

// Per-need Environmental Multiplier vector (life_sim_needs_decay.md §4.1): encodes what
// the NPC is doing / where they are this hour as a multiplier on each need's decay rate
// (>1 accelerates, <1 slows). Neutral (all 1.0) is the calibrated baseline the §3 decay
// table is defined at; the scalar DecayHour overload is the uniform degenerate case the
// design doc calls E_all. Authored values belong in [0.25, 3.0] per §4.1.
public readonly struct EnvironmentalModifiers
{
    public readonly float Hunger;
    public readonly float Sleep;
    public readonly float Hygiene;
    public readonly float Social;
    public readonly float Fitness;

    public EnvironmentalModifiers(float hunger, float sleep, float hygiene, float social, float fitness)
    {
        Hunger = hunger;
        Sleep = sleep;
        Hygiene = hygiene;
        Social = social;
        Fitness = fitness;
    }

    public static EnvironmentalModifiers Uniform(float multiplier) =>
        new(multiplier, multiplier, multiplier, multiplier, multiplier);

    public static readonly EnvironmentalModifiers Neutral = Uniform(1f);

    public readonly float Get(NeedType need) => need switch
    {
        NeedType.Hunger => Hunger,
        NeedType.Sleep => Sleep,
        NeedType.Hygiene => Hygiene,
        NeedType.Social => Social,
        NeedType.Fitness => Fitness,
        _ => throw new ArgumentOutOfRangeException(nameof(need)),
    };
}

// BaseDecayPerHour: points lost per in-game hour at full satisfaction (deficitFraction == 0).
// AccelerationCoefficient/Power: how sharply decay speeds up as the need empties out, per
// life_sim_ai.md's "decay accelerates as the value approaches zero" mandate.
public readonly struct NeedDecayProfile
{
    public readonly float BaseDecayPerHour;
    public readonly float AccelerationCoefficient;
    public readonly float AccelerationPower;

    public NeedDecayProfile(float baseDecayPerHour, float accelerationCoefficient, float accelerationPower)
    {
        BaseDecayPerHour = baseDecayPerHour;
        AccelerationCoefficient = accelerationCoefficient;
        AccelerationPower = accelerationPower;
    }
}

public static class NeedsEngine
{
    public const float MinNeed = 0f;
    public const float MaxNeed = 100f;

    // Below this, an entity is in "desperation" territory (life_sim_ai.md stress/emotion overlay
    // fires action overrides at/under this line).
    public const float CriticalThreshold = 20f;

    // life_sim_needs_decay.md §4.2: E·S is capped in aggregate so a punishing activity
    // during a high-stress arc can't produce a physically absurd per-hour cliff.
    public const float MaxCombinedModifier = 3f;

    // Tuned via Tools/NeedsDecayHarness (simulate_utility_decay skill) against two anchors:
    // a 3-hour attended baseball game must not leave a standard NPC starving, and a full
    // 168-hour week of total neglect must drive every need into desperation.
    public static readonly NeedDecayProfile Hunger = new(baseDecayPerHour: 4.2f, accelerationCoefficient: 1.6f, accelerationPower: 2f);
    public static readonly NeedDecayProfile Sleep = new(baseDecayPerHour: 3.4f, accelerationCoefficient: 1.4f, accelerationPower: 2f);
    public static readonly NeedDecayProfile Hygiene = new(baseDecayPerHour: 1.4f, accelerationCoefficient: 1.3f, accelerationPower: 2f);
    public static readonly NeedDecayProfile Social = new(baseDecayPerHour: 1.0f, accelerationCoefficient: 1.2f, accelerationPower: 2f);
    public static readonly NeedDecayProfile Fitness = new(baseDecayPerHour: 0.55f, accelerationCoefficient: 1.1f, accelerationPower: 2f);

    public static NeedDecayProfile ProfileFor(NeedType need) => need switch
    {
        NeedType.Hunger => Hunger,
        NeedType.Sleep => Sleep,
        NeedType.Hygiene => Hygiene,
        NeedType.Social => Social,
        NeedType.Fitness => Fitness,
        _ => throw new ArgumentOutOfRangeException(nameof(need)),
    };

    // New_Need = Old_Need - (Base_Decay * Environmental_Multiplier * Stress_Modifier), with the
    // acceleration term folded into an effective Base_Decay evaluated at the current value.
    public static float DecayHour(float currentValue, in NeedDecayProfile profile, float environmentalMultiplier = 1f, float stressModifier = 1f)
    {
        // Floored at 0 (a modifier can slow decay to a standstill but never reverse it —
        // recovery is §6's additive Restore, not a negative decay) and ceilinged per §4.2.
        float combinedModifier = Math.Clamp(environmentalMultiplier * stressModifier, 0f, MaxCombinedModifier);
        float deficitFraction = 1f - (currentValue / MaxNeed);
        float acceleration = 1f + profile.AccelerationCoefficient * MathF.Pow(deficitFraction, profile.AccelerationPower);
        float effectiveDecay = profile.BaseDecayPerHour * combinedModifier * acceleration;
        return Math.Clamp(currentValue - effectiveDecay, MinNeed, MaxNeed);
    }

    // Uniform-scalar form (E_all): the degenerate case of the per-need vector below.
    public static NeedsState DecayHour(in NeedsState state, float environmentalMultiplier = 1f, float stressModifier = 1f) =>
        DecayHour(state, EnvironmentalModifiers.Uniform(environmentalMultiplier), stressModifier);

    // Per-need Environmental Multiplier form (life_sim_needs_decay.md §4.1's target shape):
    // a hot day hits Hygiene, not Social. Stress stays a uniform scalar (§4.2 — stress
    // frays everything at once).
    public static NeedsState DecayHour(in NeedsState state, in EnvironmentalModifiers environment, float stressModifier = 1f)
    {
        NeedsState next = state;
        next.Hunger = DecayHour(state.Hunger, Hunger, environment.Hunger, stressModifier);
        next.Sleep = DecayHour(state.Sleep, Sleep, environment.Sleep, stressModifier);
        next.Hygiene = DecayHour(state.Hygiene, Hygiene, environment.Hygiene, stressModifier);
        next.Social = DecayHour(state.Social, Social, environment.Social, stressModifier);
        next.Fitness = DecayHour(state.Fitness, Fitness, environment.Fitness, stressModifier);
        return next;
    }
}
