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
    // Tuning-pass note (2026-07-03, simulate_utility_decay): the first-pass
    // power=2 made every need-restoring action overtake Idle at a trivial
    // ~11-16% deficit (need value ~84-89) because a cheap action's non-deficit
    // terms (temporal/financial/risk, all near-max at low cost) already sit
    // within ~0.01-0.06 of Idle's 0.8 baseline — the "frequent Eat" artifact
    // was really "every cheap action fires immediately," Eat just showed it
    // first because Hunger decays fastest. power=5 (steeper convexity) pushes
    // the crossover down to a real ~40-60%-deficit band (see Program.cs's
    // crossover-sweep check); weight raised 1.0->1.4 alongside it to hold the
    // Hunger-critical-vs-DrinkAlone margin the power increase alone would
    // erode (verified: Eat still wins with a *larger* margin than before).
    public const float DefaultDeficitPower = 5f;

    // First-pass weights, tuned via simulate_utility_decay like every other
    // constant table in this codebase.
    public static readonly ActionWeights DefaultWeights =
        new(needDeficitWeight: 1.4f, temporalCostWeight: 0.3f, financialCostWeight: 0.3f, riskWeight: 0.2f, stressReliefWeight: 0.8f);

    // life_sim_ai.md's second override trigger, live since Phase 7 gave the
    // stress scalar a source: at/above this, "high stress can override queued
    // actions, forcing the entity to autonomously seek stress-relief actions
    // (alcohol, arguments) regardless of temporal cost" — same crisis pass the
    // needs threshold engages, new trigger.
    public const float StressOverrideThreshold = 70f;

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
    // eatCostShare (household_board.md): the fraction of Eat's FinancialCost
    // this person actually pays — the decision must see the same discounted
    // price ApplyAction will charge, or a broke-but-covered avatar would
    // flinch at a $12 he'd never pay. Scales the Eat row only; 1 for NPCs.
    public static NpcActionId SelectAction(in NeedsState needs, double fundsAvailable, in ActionWeights weights, out float bestUtility, float stress0To100 = 0f, double eatCostShare = 1.0)
    {
        // life_sim_ai.md: at/under CriticalThreshold — or at/above the stress
        // override line, now that gritty events feed the stress scalar — the
        // stress overlay can force an autonomous response regardless of temporal
        // cost. Implemented as a whole-pass override rather than a per-action
        // filter: temporal cost stops counting at all, and stress-relief actions
        // (otherwise never worth it) become fully competitive.
        bool anyCritical = needs.AnyAtOrBelow(NeedsEngine.CriticalThreshold)
            || stress0To100 >= StressOverrideThreshold;

        NpcActionDefinition[] all = ActionCatalog.All;
        NpcActionId bestId = all[0].Id;
        bestUtility = float.NegativeInfinity;

        for (int i = 0; i < all.Length; i++)
        {
            NpcActionDefinition def = all[i];

            float needScore = def.PrimaryNeed is NeedType need ? NeedDeficitScore(needs.Get(need)) : 0f;
            float temporalWeight = anyCritical ? 0f : weights.TemporalCostWeight;
            float stressReliefScore = def.IsStressRelief && anyCritical ? 1f : 0f;

            double financialCost = def.Id == NpcActionId.Eat ? def.FinancialCost * eatCostShare : def.FinancialCost;
            float utility =
                needScore * weights.NeedDeficitWeight +
                TemporalCostScore(def.TemporalCostHours) * temporalWeight +
                FinancialCostScore(financialCost, fundsAvailable) * weights.FinancialCostWeight +
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
