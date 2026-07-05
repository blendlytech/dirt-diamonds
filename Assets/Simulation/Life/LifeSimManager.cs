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

        // Phase 8a: Funds changed by THIS NPC's own actions/blocks/drain since
        // GameManager's last flush — NOT touched by OnFundsImpulse (that
        // portion is already committed to the DB by whoever published the
        // impulse, e.g. a gritty event's own AdjustFunds call). GameManager
        // peeks/clears this once a day and applies it via the same atomic
        // AdjustFunds writer gritty events use, keeping Players.funds
        // single-writer-clean end to end (see ApplyFundsDelta below).
        public double UnflushedFundsDelta;
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

    // Phase 8a survival economy: a bundled weekly rent+food+gear cost-of-living
    // drain — deliberately ONE line item, not three (disclosed simplification),
    // applied to the AVATAR ONLY (see OnDayAdvanced). NPCs have no schedule and
    // can never reach School/LegalWork via autopilot, so they have no possible
    // income mechanic under this design — draining their funds too would just
    // monotonically zero out the whole background population with no validated
    // purpose. First-pass constants, tunable via simulate_utility_decay like
    // every other table here.
    public const int CostOfLivingCadenceDays = 7;
    public const double WeeklyCostOfLiving = 70.0;

    private readonly Dictionary<string, NpcRuntime> _npcs = new();
    // Dictionary enumeration order isn't a documented guarantee; tracked
    // separately so a harness/log trace is reproducible. Doesn't affect any
    // individual NPC's correctness — NPCs never interact with each other.
    private readonly List<string> _order = new();

    // Phase 9b daily clock: which tracked person is the player avatar, and the
    // one-shot plan for today. With no plan set, the avatar days through the
    // same TickHour autopilot as every NPC, bit-for-bit — the same
    // default-autopilot contract CareerManager.AutopilotAttendedGames keeps
    // for headless callers. A set plan is consumed by the day it runs;
    // an unplanned tomorrow autopilots again (the pending-game-forfeit mirror).
    private string? _avatarPlayerId;
    private DaySchedule _todaySchedule;
    private bool _hasTodaySchedule;
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

    // ------------------------------------------------------------------
    // Avatar daily clock (Phase 9b)
    // ------------------------------------------------------------------

    /// <summary>The tracked person whose day the player can plan, or null (pure-NPC world).</summary>
    public string? AvatarPlayerId => _avatarPlayerId;

    /// <summary>
    /// Whether School hours are schedulable — true only in the HS/College
    /// tiers. The tier itself lives on the Baseball side of the wall; the
    /// bridge (GameManager) projects it into this bool so this assembly stays
    /// Baseball-free. Defaults false: no school unless somebody says so.
    /// </summary>
    public bool AvatarSchoolAvailable { get; set; }

    /// <summary>
    /// Phase 8b: whether today's Work block should tick under
    /// <see cref="ActionCatalog.HustleWork"/> (an interactive Narcotics/
    /// Fencing session is armed) instead of <see cref="ActionCatalog.LegalWork"/>
    /// (the passive 8a payout, default false). GameManager owns the selection
    /// — this assembly never sees the concept of "which hustle," only which
    /// needs-drain definition to tick, keeping the wall intact.
    /// </summary>
    public bool AvatarWorkIsHustle { get; set; }

    /// <summary>
    /// Points the daily clock at a tracked person (null clears it). Loud on an
    /// untracked id — the bridge seeds the avatar before pointing at it. Any
    /// pending plan is dropped: a succession heir starts on autopilot, never
    /// on the retiree's leftover schedule.
    /// </summary>
    public void SetAvatar(string? playerId)
    {
        if (playerId is not null && !_npcs.ContainsKey(playerId))
        {
            throw new InvalidOperationException(
                $"'{playerId}' is not a tracked life-sim person — seed it before making it the avatar.");
        }
        _avatarPlayerId = playerId;
        _hasTodaySchedule = false;
    }

    public bool HasTodaySchedule => _hasTodaySchedule;

    /// <summary>The plan awaiting the next day tick, when <see cref="HasTodaySchedule"/>.</summary>
    public bool TryGetTodaySchedule(out DaySchedule schedule)
    {
        schedule = _todaySchedule;
        return _hasTodaySchedule;
    }

    /// <summary>
    /// Queues the avatar's plan for the next day tick, replacing any earlier
    /// plan for that day. Throws when no avatar is set, and when the plan
    /// includes School hours outside the HS/College tiers (the 9b gate).
    /// </summary>
    public void SetTodaySchedule(in DaySchedule schedule)
    {
        if (_avatarPlayerId is null)
        {
            throw new InvalidOperationException("No avatar is set — only the avatar's day can be planned.");
        }
        if (schedule.SchoolHours > 0 && !AvatarSchoolAvailable)
        {
            throw new InvalidOperationException(
                "School hours are only schedulable in the HS/College tiers.");
        }
        _todaySchedule = schedule;
        _hasTodaySchedule = true;
    }

    /// <summary>Drops the pending plan — the avatar's next day runs on autopilot.</summary>
    public void ClearTodaySchedule() => _hasTodaySchedule = false;

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

    /// <summary>
    /// Non-destructive read of the funds delta accrued since the last
    /// <see cref="ClearFundsDelta"/> (GameManager's persistence bridge, Phase
    /// 8a). Deliberately a separate call from clearing — GameManager peeks
    /// every tracked id, applies each nonzero delta via PlayerQueries.AdjustFunds
    /// inside its own batch, and only clears (below) once that batch commits,
    /// so a rolled-back transaction leaves nothing lost. 0 for an untracked id.
    /// </summary>
    public double PeekFundsDelta(string playerId) =>
        _npcs.TryGetValue(playerId, out NpcRuntime? runtime) ? runtime.UnflushedFundsDelta : 0.0;

    /// <summary>Resets a tracked NPC's unflushed funds delta to 0 (call only after its value has been durably persisted). No-op when untracked.</summary>
    public void ClearFundsDelta(string playerId)
    {
        if (_npcs.TryGetValue(playerId, out NpcRuntime? runtime))
        {
            runtime.UnflushedFundsDelta = 0.0;
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
        bool costOfLivingDue = e.Day % CostOfLivingCadenceDays == 0;
        for (int i = 0; i < _order.Count; i++)
        {
            NpcRuntime npc = _npcs[_order[i]];
            bool isAvatar = string.Equals(_order[i], _avatarPlayerId, StringComparison.Ordinal);

            // 8a: the recurring cost-of-living bill — avatar-only (see the
            // constants' doc comment), fires regardless of whether today is
            // planned or autopiloted.
            if (costOfLivingDue && isAvatar)
            {
                ApplyFundsDelta(npc, -WeeklyCostOfLiving);
            }

            // 9b: a planned avatar day runs its blocks instead of the
            // autopilot; everyone else (and an unplanned avatar) days through
            // the pre-9b loop unchanged.
            if (_hasTodaySchedule && isAvatar)
            {
                TickScheduledDay(npc, in _todaySchedule);
                _hasTodaySchedule = false;
                continue;
            }
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                TickHour(npc);
            }
        }
    }

    /// <summary>
    /// One player-planned day in a fixed canonical day order: the daytime
    /// blocks (School → Practice → Game → Work), then the unallocated hours
    /// on the standard autopilot (the evening), then Sleep as the night cap.
    /// Sleep MUST run last: run first, an avatar sleeping from a full meter
    /// wastes the whole restore against the 100-clamp and the meter drains
    /// all day anyway (harness-measured: 8h-sleep day ending at Sleep 33.5).
    /// Blocks are uninterruptible — the player's stated intent outranks the
    /// crisis override for the skeleton (the stress-forces-the-avatar's-hand
    /// rule can layer onto this seam later); free hours keep the full
    /// TickHour behavior, override included.
    /// </summary>
    private void TickScheduledDay(NpcRuntime npc, in DaySchedule schedule)
    {
        // The plan preempts whatever autopilot action was mid-flight at
        // midnight — the same abandon-now rule the crisis override applies.
        npc.BusyHoursRemaining = 0;
        npc.CurrentAction = NpcActionId.Idle;

        // Practice is inert until 9d's development curves, Game is life-side
        // inert (CareerManager owns the attended game) — both still ride
        // Idle's neutral definition. School and Work got their real 8a
        // definitions (meal access + neutral env for School; meal access +
        // income + heavy Sleep/Fitness drain for LegalWork).
        TickBlockHours(npc, schedule.SchoolHours, in ActionCatalog.School);
        TickBlockHours(npc, schedule.PracticeHours, in ActionCatalog.Idle);
        TickBlockHours(npc, schedule.GameHours, in ActionCatalog.Idle);
        ref readonly NpcActionDefinition workDef = ref (AvatarWorkIsHustle ? ref ActionCatalog.HustleWork : ref ActionCatalog.LegalWork);
        TickBlockHours(npc, schedule.WorkHours, in workDef);

        for (int hour = 0; hour < schedule.FreeHours; hour++)
        {
            TickHour(npc);
        }

        // Sleep is the one live block: per-hour restore at the catalog entry's
        // own rate/environment, so tuning Sleep tunes both paths at once.
        TickBlockHours(npc, schedule.SleepHours, in ActionCatalog.Sleep);
    }

    /// <summary>
    /// Ticks one block's hours under its definition: the restore spreads
    /// evenly per hour (an 8h Sleep block lands the same +80 the autopilot's
    /// one-shot Sleep action grants), then the hour decays under the block's
    /// environment and the live stress scalar, exactly like TickHour's tail.
    /// </summary>
    private static void TickBlockHours(NpcRuntime npc, int hours, in NpcActionDefinition def)
    {
        float restorePerHour = def.PrimaryNeed is not null && def.TemporalCostHours > 0f
            ? def.RestoreAmount / def.TemporalCostHours
            : 0f;
        // Same per-hour proration as restorePerHour above, applied to funds —
        // guarded on the SAME denominator (TemporalCostHours > 0), never on
        // FinancialCost == 0, so a future zero-hours block definition can
        // never divide into a NaN that permanently corrupts Funds (Math.Max
        // propagates NaN forever once it lands in the mirror). A true no-op
        // for every block whose FinancialCost is 0 (Sleep, Idle-backed
        // Practice/Game) — only LegalWork's negative cost (income) is nonzero.
        double fundsPerHour = def.TemporalCostHours > 0f
            ? -def.FinancialCost / def.TemporalCostHours
            : 0.0;
        for (int hour = 0; hour < hours; hour++)
        {
            if (def.PrimaryNeed is NeedType need)
            {
                npc.Needs.Restore(need, restorePerHour);
            }
            if (fundsPerHour != 0.0)
            {
                ApplyFundsDelta(npc, fundsPerHour);
            }
            npc.Needs = NeedsEngine.DecayHour(
                npc.Needs, def.Environment, NeedsEngine.StressModifierFor(npc.Stress));
            npc.Stress = Math.Max(MinStress, npc.Stress - StressRelaxationPerHour);
        }
    }

    /// <summary>
    /// The sole mutator of NpcRuntime.Funds for LifeSim-internal reasons
    /// (autopilot ApplyAction, schedule blocks, the recurring drain) — diffs
    /// the CLAMPED value (not the nominal delta) into UnflushedFundsDelta, so
    /// a debit that bottoms out at the zero floor never queues more "debt"
    /// than the mirror actually gave up (matching AdjustFunds' own SQL-side
    /// MAX(0, ...) clamp when GameManager later applies this delta to the DB).
    /// OnFundsImpulse (gritty events) deliberately does NOT go through this —
    /// that portion is already committed to the DB by whoever published it.
    /// </summary>
    private static void ApplyFundsDelta(NpcRuntime npc, double delta)
    {
        double before = npc.Funds;
        npc.Funds = Math.Max(0.0, npc.Funds + delta);
        npc.UnflushedFundsDelta += npc.Funds - before;
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
        ApplyFundsDelta(npc, -def.FinancialCost);
        npc.CurrentAction = id;
        npc.BusyHoursRemaining = Math.Max(0, (int)MathF.Ceiling(def.TemporalCostHours) - 1);
    }
}
