using System;

namespace DirtAndDiamonds.Simulation.Life;

// Weights are tunable static data (mirrors NeedDecayProfile's "tuning is a data
// edit" precedent) — never hard-coded inline in SelectAction.
public readonly struct ActionWeights
{
    public readonly float NeedDeficitWeight;
    public readonly float TemporalCostWeight;
    public readonly float FinancialCostWeight;
    public readonly float RiskWeight;
    public readonly float StressReliefWeight;

    public ActionWeights(
        float needDeficitWeight, float temporalCostWeight, float financialCostWeight,
        float riskWeight, float stressReliefWeight)
    {
        NeedDeficitWeight = needDeficitWeight;
        TemporalCostWeight = temporalCostWeight;
        FinancialCostWeight = financialCostWeight;
        RiskWeight = riskWeight;
        StressReliefWeight = stressReliefWeight;
    }
}

// Utility = Sum(Consideration * Weight) per life_sim_ai.md, scored against the
// static ActionCatalog. Every *Score helper is "higher is better" by convention
// so ActionWeights never needs a negative entry to behave sensibly.
public static class UtilityCalculator
{
    public const float DefaultDeficitPower = 2f;

    // First-pass weights, tuned via simulate_utility_decay like every other
    // constant table in this codebase.
    public static readonly ActionWeights DefaultWeights =
        new(needDeficitWeight: 1.0f, temporalCostWeight: 0.3f, financialCostWeight: 0.3f, riskWeight: 0.2f, stressReliefWeight: 0.8f);

    // life_sim_needs_decay.md §7: convex, low-need-dominant — a starving NPC
    // values food disproportionately. 0 when full, 1 when empty.
    public static float NeedDeficitScore(float value, float power = DefaultDeficitPower) =>
        MathF.Pow((NeedsEngine.MaxNeed - value) / NeedsEngine.MaxNeed, power);

    // Linear falloff against a 24h horizon (the driver's own tick granularity).
    public static float TemporalCostScore(float hours) =>
        1f - Math.Clamp(hours / 24f, 0f, 1f);

    // Ratio of cost to available funds, uncapped below zero on purpose — an
    // unaffordable action must be able to lose outright to Idle, not just be
    // discouraged. fundsAvailable is floored (not branched) at a one-cent
    // epsilon so the score stays continuous as funds approach zero instead of
    // jumping to a special-cased constant.
    public static float FinancialCostScore(double cost, double fundsAvailable)
    {
        if (cost <= 0.0)
        {
            return 1f;
        }
        double denominator = Math.Max(fundsAvailable, 0.01);
        return 1f - (float)(cost / denominator);
    }

    public static float RiskScore(float risk0To100) => 1f - risk0To100 / 100f;

    // Never filters/skips a candidate for unaffordability or high risk — only
    // penalizes via score — so a desperate or broke NPC can never come back
    // empty-handed (ActionCatalog.Idle is always a zero-cost candidate).
    public static NpcActionId SelectAction(in NeedsState needs, double fundsAvailable, in ActionWeights weights, out float bestUtility)
    {
        // life_sim_ai.md: at/under CriticalThreshold, the stress overlay can force
        // an autonomous response regardless of temporal cost. Implemented as a
        // whole-pass override rather than a per-action filter: temporal cost stops
        // counting at all, and stress-relief actions (otherwise never worth it)
        // become fully competitive.
        bool anyCritical = needs.AnyAtOrBelow(NeedsEngine.CriticalThreshold);

        NpcActionDefinition[] all = ActionCatalog.All;
        NpcActionId bestId = all[0].Id;
        bestUtility = float.NegativeInfinity;

        for (int i = 0; i < all.Length; i++)
        {
            NpcActionDefinition def = all[i];

            float needScore = def.PrimaryNeed is NeedType need ? NeedDeficitScore(needs.Get(need)) : 0f;
            float temporalWeight = anyCritical ? 0f : weights.TemporalCostWeight;
            float stressReliefScore = def.IsStressRelief && anyCritical ? 1f : 0f;

            float utility =
                needScore * weights.NeedDeficitWeight +
                TemporalCostScore(def.TemporalCostHours) * temporalWeight +
                FinancialCostScore(def.FinancialCost, fundsAvailable) * weights.FinancialCostWeight +
                RiskScore(def.Risk0To100) * weights.RiskWeight +
                stressReliefScore * weights.StressReliefWeight;

            if (utility > bestUtility)
            {
                bestUtility = utility;
                bestId = def.Id;
            }
        }

        return bestId;
    }
}
