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
        float deficitFraction = 1f - (currentValue / MaxNeed);
        float acceleration = 1f + profile.AccelerationCoefficient * MathF.Pow(deficitFraction, profile.AccelerationPower);
        float effectiveDecay = profile.BaseDecayPerHour * environmentalMultiplier * stressModifier * acceleration;
        return Math.Clamp(currentValue - effectiveDecay, MinNeed, MaxNeed);
    }

    public static NeedsState DecayHour(in NeedsState state, float environmentalMultiplier = 1f, float stressModifier = 1f)
    {
        NeedsState next = state;
        next.Hunger = DecayHour(state.Hunger, Hunger, environmentalMultiplier, stressModifier);
        next.Sleep = DecayHour(state.Sleep, Sleep, environmentalMultiplier, stressModifier);
        next.Hygiene = DecayHour(state.Hygiene, Hygiene, environmentalMultiplier, stressModifier);
        next.Social = DecayHour(state.Social, Social, environmentalMultiplier, stressModifier);
        next.Fitness = DecayHour(state.Fitness, Fitness, environmentalMultiplier, stressModifier);
        return next;
    }
}
