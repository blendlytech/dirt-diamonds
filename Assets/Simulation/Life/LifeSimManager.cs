using System;
using System.Collections.Generic;
using DirtAndDiamonds.Core;

namespace DirtAndDiamonds.Simulation.Life;

// Plain seed data — deliberately decoupled from PlayerQueries/Data so this
// whole folder's only dependency beyond its own math is Core (EventBus). The
// caller (GameManager, in the real game) is responsible for projecting
// PlayerRow -> NpcSeed.
public readonly record struct NpcSeed(string PlayerId, double Funds);

// The Life sim's day-tick driver — the CareerManager-equivalent glue class.
// Subscribes to DayAdvancedEvent and, for every tracked NPC, expands one game
// day into 24 hourly NeedsEngine/UtilityCalculator ticks. Needs and stress
// live in-memory here and persist through GameManager's bridge (Life_Needs
// schema v5, Life_Stress schema v6) — this class itself stays Data-free.
public sealed class LifeSimManager
{
    private sealed class NpcRuntime
    {
        public NeedsState Needs;
        public double Funds;
        public int BusyHoursRemaining;
        public NpcActionId CurrentAction;

        // The §4.2 stress scalar, 0 (calm) to 100. Fed by StressImpulseEvent
        // (gritty events); persisted to Life_Stress since schema v6 —
        // GameManager hydrates via SetStress and flushes via TryGetStress,
        // the same bridge pattern needs use (gritty_event_framework.md §9).
        public float Stress;
    }

    private const int HoursPerDay = 24;

    public const float MinStress = 0f;
    public const float MaxStress = 100f;

    // First-pass constants (no design-doc anchor — disclosed, tunable via
    // simulate_utility_decay like every constant table here): a +25 stress
    // event frays an NPC for ~2.5 days of passive relaxation, and one
    // stress-relief action (DrinkAlone / PickArgument) buys ~1.5 days back.
    public const float StressRelaxationPerHour = 0.4f;
    public const float StressReliefPerAction = 15f;

    private readonly Dictionary<string, NpcRuntime> _npcs = new();
    // Dictionary enumeration order isn't a documented guarantee; tracked
    // separately so a harness/log trace is reproducible. Doesn't affect any
    // individual NPC's correctness — NPCs never interact with each other.
    private readonly List<string> _order = new();
    private readonly ActionWeights _weights;
    private readonly Action<DayAdvancedEvent> _onDayAdvanced;
    private readonly Action<StressImpulseEvent> _onStressImpulse;
    private readonly Action<FundsImpulseEvent> _onFundsImpulse;

    public LifeSimManager(ActionWeights? weights = null)
    {
        _weights = weights ?? UtilityCalculator.DefaultWeights;
        _onDayAdvanced = OnDayAdvanced;
        _onStressImpulse = OnStressImpulse;
        _onFundsImpulse = OnFundsImpulse;
    }

    public int NpcCount => _order.Count;

    // Persistence bridge surface (design doc §11): GameManager reads this list plus
    // TryGetNeeds to bulk-persist, and calls SetNeeds to hydrate from a save on
    // boot. Deliberately just IDs/state, not a DatabaseManager dependency, so this
    // class stays Data-free (Tools/NeedsDecayHarness compiles it standalone).
    public IReadOnlyList<string> TrackedPlayerIds => _order;

    // Overwrites a tracked NPC's needs (boot-time hydration from a save). A no-op
    // for an id not yet seeded — callers hydrate after Seed(), same ordering
    // GameManager already uses for funds via NpcSeed.
    public void SetNeeds(string playerId, in NeedsState needs)
    {
        if (_npcs.TryGetValue(playerId, out NpcRuntime? runtime))
        {
            runtime.Needs = needs;
        }
    }

    // Additive/idempotent: seeding an id already tracked is a no-op, so a
    // mid-game avatar creation can re-project the roster and re-seed safely.
    public void Seed(IReadOnlyList<NpcSeed> seeds)
    {
        for (int i = 0; i < seeds.Count; i++)
        {
            NpcSeed seed = seeds[i];
            if (_npcs.ContainsKey(seed.PlayerId))
            {
                continue;
            }
            _npcs.Add(seed.PlayerId, new NpcRuntime
            {
                Needs = NeedsState.FullySatisfied(),
                Funds = seed.Funds,
                BusyHoursRemaining = 0,
                CurrentAction = NpcActionId.Idle,
            });
            _order.Add(seed.PlayerId);
        }
    }

    public void AttachTo(EventBus bus)
    {
        bus.Subscribe(_onDayAdvanced);
        // Cross-system signals arrive via the bus, never a direct call
        // (life_sim_needs_decay.md §10) — gritty events raise stress and move
        // money through these two impulses.
        bus.Subscribe(_onStressImpulse);
        bus.Subscribe(_onFundsImpulse);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onDayAdvanced);
        bus.Unsubscribe(_onStressImpulse);
        bus.Unsubscribe(_onFundsImpulse);
    }

    public bool TryGetNeeds(string playerId, out NeedsState needs)
    {
        if (_npcs.TryGetValue(playerId, out NpcRuntime? runtime))
        {
            needs = runtime.Needs;
            return true;
        }
        needs = default;
        return false;
    }

    public bool TryGetFunds(string playerId, out double funds)
    {
        if (_npcs.TryGetValue(playerId, out NpcRuntime? runtime))
        {
            funds = runtime.Funds;
            return true;
        }
        funds = 0.0;
        return false;
    }

    public bool TryGetStress(string playerId, out float stress)
    {
        if (_npcs.TryGetValue(playerId, out NpcRuntime? runtime))
        {
            stress = runtime.Stress;
            return true;
        }
        stress = 0f;
        return false;
    }

    /// <summary>Overwrites a tracked NPC's stress (harness fixtures / future hydration). No-op when untracked, like SetNeeds.</summary>
    public void SetStress(string playerId, float stress)
    {
        if (_npcs.TryGetValue(playerId, out NpcRuntime? runtime))
        {
            runtime.Stress = Math.Clamp(stress, MinStress, MaxStress);
        }
    }

    private void OnStressImpulse(StressImpulseEvent e)
    {
        if (_npcs.TryGetValue(e.PlayerId, out NpcRuntime? runtime))
        {
            runtime.Stress = Math.Clamp(runtime.Stress + e.Delta, MinStress, MaxStress);
        }
    }

    // The DB row already moved (the applier's atomic floor-clamped UPDATE);
    // this keeps the in-memory mirror the utility layer reads in step.
    private void OnFundsImpulse(FundsImpulseEvent e)
    {
        if (_npcs.TryGetValue(e.PlayerId, out NpcRuntime? runtime))
        {
            runtime.Funds = Math.Max(0.0, runtime.Funds + e.Delta);
        }
    }

    private void OnDayAdvanced(DayAdvancedEvent e)
    {
        for (int i = 0; i < _order.Count; i++)
        {
            NpcRuntime npc = _npcs[_order[i]];
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                TickHour(npc);
            }
        }
    }

    private void TickHour(NpcRuntime npc)
    {
        // Crisis = a critical need OR high stress (life_sim_ai.md's two override
        // triggers — the stress one live since Phase 7 gave the scalar a source).
        bool critical = npc.Needs.AnyAtOrBelow(NeedsEngine.CriticalThreshold)
            || npc.Stress >= UtilityCalculator.StressOverrideThreshold;

        if (critical)
        {
            // life_sim_ai.md: the stress overlay can force a response "regardless
            // of temporal cost" — re-evaluate every hour even mid-action. If the
            // in-progress action is still the right call, let it run uninterrupted
            // (no double-restore); otherwise abandon it now, not when its
            // countdown happens to finish.
            NpcActionId picked = UtilityCalculator.SelectAction(npc.Needs, npc.Funds, _weights, out _, npc.Stress);
            if (npc.BusyHoursRemaining <= 0 || picked != npc.CurrentAction)
            {
                ApplyAction(npc, picked);
            }
        }
        else if (npc.BusyHoursRemaining <= 0)
        {
            NpcActionId picked = UtilityCalculator.SelectAction(npc.Needs, npc.Funds, _weights, out _, npc.Stress);
            ApplyAction(npc, picked);
        }

        if (npc.BusyHoursRemaining > 0)
        {
            npc.BusyHoursRemaining--;
        }

        // life_sim_needs_decay.md §4.1/§4.2: the hour decays under the current
        // activity's per-need Environmental Multiplier and the live stress
        // scalar's S (stress 0 → S=1, bit-identical to the pre-Phase-7 traces).
        npc.Needs = NeedsEngine.DecayHour(
            npc.Needs,
            ActionCatalog.Get(npc.CurrentAction).Environment,
            NeedsEngine.StressModifierFor(npc.Stress));

        // Passive relaxation: an arc fades unless events keep feeding it.
        npc.Stress = Math.Max(MinStress, npc.Stress - StressRelaxationPerHour);
    }

    private static void ApplyAction(NpcRuntime npc, NpcActionId id)
    {
        NpcActionDefinition def = ActionCatalog.Get(id);
        if (def.PrimaryNeed is NeedType need)
        {
            npc.Needs.Restore(need, def.RestoreAmount);
        }
        if (def.IsStressRelief)
        {
            npc.Stress = Math.Max(MinStress, npc.Stress - StressReliefPerAction);
        }
        npc.Funds = Math.Max(0.0, npc.Funds - def.FinancialCost);
        npc.CurrentAction = id;
        npc.BusyHoursRemaining = Math.Max(0, (int)MathF.Ceiling(def.TemporalCostHours) - 1);
    }
}
