using System;

namespace DirtAndDiamonds.Simulation.Life;

public enum NpcActionId
{
    Idle = 0,
    Eat = 1,
    Sleep = 2,
    Shower = 3,
    SocializeEvening = 4,
    Workout = 5,
    DrinkAlone = 6,
    PickArgument = 7,
}

// PrimaryNeed is null for actions that don't restore a tracked need directly
// (Idle, and the stress-relief actions below).
public readonly struct NpcActionDefinition
{
    public readonly NpcActionId Id;
    public readonly NeedType? PrimaryNeed;
    public readonly float RestoreAmount;
    public readonly float TemporalCostHours;
    public readonly double FinancialCost;
    public readonly float Risk0To100;
    public readonly bool IsStressRelief;

    public NpcActionDefinition(
        NpcActionId id, NeedType? primaryNeed, float restoreAmount, float temporalCostHours,
        double financialCost, float risk0To100, bool isStressRelief)
    {
        Id = id;
        PrimaryNeed = primaryNeed;
        RestoreAmount = restoreAmount;
        TemporalCostHours = temporalCostHours;
        FinancialCost = financialCost;
        Risk0To100 = risk0To100;
        IsStressRelief = isStressRelief;
    }
}

public static class ActionCatalog
{
    // Restore amounts anchored to life_sim_needs_decay.md §6's illustrative anchors
    // (meal Hunger+45, night's sleep Sleep+80/8h, shower Hygiene+60, evening out
    // Social+35, workout Fitness+30). Hours/cost/risk have no design-doc anchor —
    // first-pass constants, data-edit tunable via simulate_utility_decay exactly
    // like NeedDecayProfile's own constants were.
    public static readonly NpcActionDefinition Idle = new(NpcActionId.Idle, null, 0f, 0f, 0.0, 0f, false);
    public static readonly NpcActionDefinition Eat = new(NpcActionId.Eat, NeedType.Hunger, 45f, 1f, 12.0, 0f, false);
    public static readonly NpcActionDefinition Sleep = new(NpcActionId.Sleep, NeedType.Sleep, 80f, 8f, 0.0, 0f, false);
    public static readonly NpcActionDefinition Shower = new(NpcActionId.Shower, NeedType.Hygiene, 60f, 1f, 0.0, 0f, false);
    public static readonly NpcActionDefinition SocializeEvening = new(NpcActionId.SocializeEvening, NeedType.Social, 35f, 3f, 40.0, 5f, false);
    public static readonly NpcActionDefinition Workout = new(NpcActionId.Workout, NeedType.Fitness, 30f, 2f, 10.0, 0f, false);

    // Stress-relief actions (life_sim_ai.md: "alcohol, arguments") restore no need
    // directly — their entire utility comes from UtilityCalculator's stress-relief
    // consideration, which only activates once a need is critical.
    public static readonly NpcActionDefinition DrinkAlone = new(NpcActionId.DrinkAlone, null, 0f, 2f, 25.0, 30f, true);
    public static readonly NpcActionDefinition PickArgument = new(NpcActionId.PickArgument, null, 0f, 1f, 0.0, 45f, true);

    // Idle listed first: SelectAction's strict-greater-than tie-break means Idle
    // — the guaranteed always-affordable fallback — wins any exact utility tie.
    public static readonly NpcActionDefinition[] All =
    {
        Idle, Eat, Sleep, Shower, SocializeEvening, Workout, DrinkAlone, PickArgument,
    };

    public static NpcActionDefinition Get(NpcActionId id) => id switch
    {
        NpcActionId.Idle => Idle,
        NpcActionId.Eat => Eat,
        NpcActionId.Sleep => Sleep,
        NpcActionId.Shower => Shower,
        NpcActionId.SocializeEvening => SocializeEvening,
        NpcActionId.Workout => Workout,
        NpcActionId.DrinkAlone => DrinkAlone,
        NpcActionId.PickArgument => PickArgument,
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };
}
