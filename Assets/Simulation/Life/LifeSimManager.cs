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

        // HS-4: hydrated person-layer state, null for everyone the bridge
        // never hydrates (in practice: every NPC — only the avatar's person
        // stats have movers this arc). Null keeps the NPC hot path exactly
        // the pre-HS-4 code, allocation and branch-wise.
        public PersonRuntime? Person;
    }

    // HS-4 person-layer runtime (person-layer doc §2.2/§2.3): in-memory
    // current values (floats — fractional per-hour deltas), the unflushed
    // accumulator GameManager settles through the atomic DB adjusters (the
    // UnflushedFundsDelta pattern, per-stat), and the weekly GPA-drift
    // accumulators. Allocated once per hydration, never in a tick.
    private sealed class PersonRuntime
    {
        public PersonStats Stats;

        // Accrued by actions/drift since the last settle. GameManager
        // persists only the accumulated WHOLE part of each stat (the DB
        // columns are INTEGER) and settles exactly what it applied, so
        // sub-point fractions carry forward instead of rounding away.
        public PersonStats Unflushed;

        // §2.3: the per-person reversion target — the value observed at
        // hydration (the neutral row's 50 for a fresh avatar; a trait-shifted
        // base becomes that person's own setpoint). Saving mid-spike bakes
        // the remnant into the next session's setpoint — disclosed, the same
        // in-memory posture as stress cooldowns.
        public float HappinessSetpoint;

        // §2.2 weekly accumulators, reset at every weekly tick. Expected
        // accrues ExpectedSchoolHoursPerDay per observed day; School accrues
        // the planned block's actual hours (or the same per-day constant on
        // an unplanned day — autopilot attends school by default), so a
        // mid-week boot loses both sides together and the attendance
        // fraction stays fair.
        public float SchoolHoursThisWeek;
        public float ExpectedSchoolHoursThisWeek;
        public float StudyHoursThisWeek;

        // HS-5 (person-layer doc §7.1): the Family block's weekly total.
        // Unlike the School/Study accumulators above, this one does NOT
        // self-reset at TickPersonDay's weekly gate — ChildRearingService
        // (Economy.Family, outside this Data-free assembly) needs to read the
        // finalized total via PeekFamilyHoursThisWeek before it clears via
        // ClearFamilyHoursThisWeek, the exact peek-then-clear-after-commit
        // discipline UnflushedFundsDelta already uses.
        public float FamilyHoursThisWeek;
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
    // The avatar's runtime, cached by SetAvatar so TickHour's household-board
    // check is one ReferenceEquals — no string compare in the hot loop.
    private NpcRuntime? _avatarRuntime;
    private DaySchedule _todaySchedule;
    private bool _hasTodaySchedule;

    // HS-4 §5.3 (revised): hours saved per trip vs. walking, from the
    // avatar's best owned Transport item — consumed by TravelTime.ComputeHours
    // as a per-location discount, not a flat daily bonus. In-memory, not
    // state — resets with the avatar pointer, the stress-cooldown precedent.
    private float _avatarTransportHoursSaved;
    private readonly ActionWeights _weights;
    private readonly Action<DayAdvancedEvent> _onDayAdvanced;
    private readonly Action<StressImpulseEvent> _onStressImpulse;
    private readonly Action<FundsImpulseEvent> _onFundsImpulse;
    private readonly Action<PersonStatImpulseEvent> _onPersonStatImpulse;

    public LifeSimManager(ActionWeights? weights = null)
    {
        _weights = weights ?? UtilityCalculator.DefaultWeights;
        _onDayAdvanced = OnDayAdvanced;
        _onStressImpulse = OnStressImpulse;
        _onFundsImpulse = OnFundsImpulse;
        _onPersonStatImpulse = OnPersonStatImpulse;
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
    /// Household board (household_board.md): the avatar's §3 family wealth
    /// tier when parents cover a share of his board — Eat's FinancialCost and
    /// the weekly cost-of-living bill — or -1 for no coverage (the default:
    /// pre-HS-2 saves, College/pro tiers, harness worlds). Like
    /// <see cref="AvatarSchoolAvailable"/>, the bridge projects this in so the
    /// assembly never reads the DB; the setter caches the share once so the
    /// hot paths do no table lookup.
    /// </summary>
    public int AvatarBoardWealthTier
    {
        get => _avatarBoardWealthTier;
        set
        {
            _avatarBoardWealthTier = value;
            _avatarBoardShare = HouseholdBoard.ShareFor(value);
        }
    }

    /// <summary>The avatar's effective weekly bill after the household covers its share — the number the Bank tab shows.</summary>
    public double AvatarWeeklyCostOfLiving => WeeklyCostOfLiving * _avatarBoardShare;

    private int _avatarBoardWealthTier = -1;
    private double _avatarBoardShare = 1.0;

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
        _avatarRuntime = playerId is null ? null : _npcs[playerId];
        _hasTodaySchedule = false;
    }

    /// <summary>
    /// HS-4 §5.3 (revised): hours saved per trip vs. walking, by the
    /// avatar's best owned Transport item (car ~1.0, bike ~0.5, none 0) —
    /// projected in by the bridge from the item catalog (this assembly
    /// never sees Player_Items). Discounts each of <see cref="TravelTime"/>'s
    /// per-location round trips on PLANNED days only; an unplanned day never
    /// builds a <see cref="DaySchedule"/> at all, so it stays the pre-HS-4
    /// 24-hour autopilot day bit-identical regardless of this value.
    /// </summary>
    public float AvatarTransportHoursSaved
    {
        get => _avatarTransportHoursSaved;
        set => _avatarTransportHoursSaved = Math.Max(0f, value);
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
        // money through these impulses; HS-4 adds the person-stat mirror
        // (a DB-side person write, e.g. the §3.1 self-buy transport reward,
        // keeps the in-memory copy the GPA drift reads in step).
        bus.Subscribe(_onStressImpulse);
        bus.Subscribe(_onFundsImpulse);
        bus.Subscribe(_onPersonStatImpulse);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onDayAdvanced);
        bus.Unsubscribe(_onStressImpulse);
        bus.Unsubscribe(_onFundsImpulse);
        bus.Unsubscribe(_onPersonStatImpulse);
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

    // ------------------------------------------------------------------
    // HS-4 person-layer bridge surface (the needs/stress/funds pattern):
    // GameManager hydrates the avatar's Player_Person row after Seed(), the
    // drift/effect movers below accrue clamped deltas, and the daily flush
    // settles the applied whole parts back out through the atomic DB
    // adjusters. Untracked/unhydrated ids no-op exactly like SetNeeds.
    // ------------------------------------------------------------------

    /// <summary>
    /// Hydrates (or re-hydrates) a tracked person's stats. Sets the §2.3
    /// happiness setpoint to the hydrated value, zeroes the unflushed
    /// accumulator and the weekly GPA inputs — hydration is a checkpoint,
    /// never a pending delta. No-op for an untracked id.
    /// </summary>
    public void SetPersonStats(string playerId, in PersonStats stats)
    {
        if (_npcs.TryGetValue(playerId, out NpcRuntime? runtime))
        {
            runtime.Person = new PersonRuntime
            {
                Stats = stats,
                HappinessSetpoint = stats.Happiness,
            };
        }
    }

    public bool TryGetPersonStats(string playerId, out PersonStats stats)
    {
        if (_npcs.TryGetValue(playerId, out NpcRuntime? runtime) && runtime.Person is PersonRuntime person)
        {
            stats = person.Stats;
            return true;
        }
        stats = default;
        return false;
    }

    /// <summary>
    /// Non-destructive read of the person-stat deltas accrued since the last
    /// settle (the PeekFundsDelta twin). False when the id is untracked or
    /// never hydrated — the caller skips those entirely.
    /// </summary>
    public bool TryPeekPersonDeltas(string playerId, out PersonStats deltas)
    {
        if (_npcs.TryGetValue(playerId, out NpcRuntime? runtime) && runtime.Person is PersonRuntime person)
        {
            deltas = person.Unflushed;
            return true;
        }
        deltas = default;
        return false;
    }

    /// <summary>
    /// Subtracts exactly what the bridge durably persisted (call only after
    /// its batch commits — the ClearFundsDelta discipline). Named Settle, not
    /// Clear, deliberately: the DB columns are INTEGER, so the bridge applies
    /// whole parts only and the sub-point fractions stay banked here instead
    /// of rounding away. No-op when untracked/unhydrated.
    /// </summary>
    public void SettlePersonDeltas(string playerId, in PersonStats applied)
    {
        if (!_npcs.TryGetValue(playerId, out NpcRuntime? runtime) || runtime.Person is not PersonRuntime person)
        {
            return;
        }
        person.Unflushed.Gpa -= applied.Gpa;
        for (int s = 0; s < PersonStats.StatCount; s++)
        {
            var stat = (PersonStatId)s;
            person.Unflushed.Set(stat, person.Unflushed.Get(stat) - applied.Get(stat));
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

    /// <summary>
    /// Non-destructive read of the avatar's Family-block hours accumulated
    /// this week (HS-5, person-layer doc §7.1) — GameManager's
    /// OnChildRearingDayAdvanced peeks this before handing it to
    /// ChildRearingService, then calls <see cref="ClearFamilyHoursThisWeek"/>
    /// only once that tick's batch has committed, the same discipline
    /// <see cref="PeekFundsDelta"/> uses. 0 for an untracked id or one this
    /// bridge never hydrated a person for.
    /// </summary>
    public float PeekFamilyHoursThisWeek(string playerId) =>
        _npcs.TryGetValue(playerId, out NpcRuntime? runtime) && runtime.Person is PersonRuntime person
            ? person.FamilyHoursThisWeek
            : 0f;

    /// <summary>Resets a tracked, hydrated NPC's weekly Family-hours accumulator to 0 (call only after ChildRearingService's tick has durably committed). No-op otherwise.</summary>
    public void ClearFamilyHoursThisWeek(string playerId)
    {
        if (_npcs.TryGetValue(playerId, out NpcRuntime? runtime) && runtime.Person is PersonRuntime person)
        {
            person.FamilyHoursThisWeek = 0f;
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

    // HS-4: the person-stat twin of OnFundsImpulse — the DB column already
    // moved (an atomic clamped write inside the publisher's own batch, e.g.
    // ItemService's §3.1 reward), so only the in-memory CURRENT value moves;
    // Unflushed is deliberately untouched (flushing it again would
    // double-apply). The raw int payload is the PersonStatId ordinal —
    // CoreEvents carries primitives only.
    private void OnPersonStatImpulse(PersonStatImpulseEvent e)
    {
        if (e.Stat < 0 || e.Stat >= PersonStats.StatCount)
        {
            return;
        }
        if (_npcs.TryGetValue(e.PlayerId, out NpcRuntime? runtime) && runtime.Person is PersonRuntime person)
        {
            var stat = (PersonStatId)e.Stat;
            person.Stats.Set(stat, Math.Clamp(
                person.Stats.Get(stat) + e.Delta, PersonDrift.StatMin, PersonDrift.StatMax));
        }
    }

    /// <summary>
    /// The sole LifeSim-internal mutator of a person stat (the
    /// ApplyFundsDelta twin): clamps the current value into [0,100] and
    /// accrues the CLAMPED movement — never the nominal delta — into
    /// Unflushed, so a nudge that hits the ceiling never queues more change
    /// than the mirror actually took (matching the DB adjuster's own
    /// MAX/MIN clamp when the bridge later applies it).
    /// </summary>
    private static void ApplyPersonStatDelta(PersonRuntime person, PersonStatId stat, float delta)
    {
        float before = person.Stats.Get(stat);
        float after = Math.Clamp(before + delta, PersonDrift.StatMin, PersonDrift.StatMax);
        person.Stats.Set(stat, after);
        person.Unflushed.Set(stat, person.Unflushed.Get(stat) + (after - before));
    }

    /// <summary>GPA twin of <see cref="ApplyPersonStatDelta"/> — clamped into [0.0, 4.0], clamped movement accrued.</summary>
    private static void ApplyGpaDelta(PersonRuntime person, double delta)
    {
        double before = person.Stats.Gpa;
        double after = Math.Clamp(before + delta, PersonDrift.GpaMin, PersonDrift.GpaMax);
        person.Stats.Gpa = after;
        person.Unflushed.Gpa += after - before;
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
                // household_board.md: the family covers its share of the bill
                // while the kid is a covered high-schooler (share 1.0 = the
                // shipped full bill for Destitute, no-coverage, and every
                // pre-slice save).
                ApplyFundsDelta(npc, -WeeklyCostOfLiving * _avatarBoardShare);
            }

            // 9b: a planned avatar day runs its blocks instead of the
            // autopilot; everyone else (and an unplanned avatar) days through
            // the pre-9b loop unchanged.
            bool dayWasPlanned = _hasTodaySchedule && isAvatar;
            if (dayWasPlanned)
            {
                TickScheduledDay(npc, in _todaySchedule);
                _hasTodaySchedule = false;
            }
            else
            {
                for (int hour = 0; hour < HoursPerDay; hour++)
                {
                    TickHour(npc);
                }
            }

            // HS-4 §2.2/§2.3: the person layer's end-of-day drift — a no-op
            // for everyone the bridge never hydrated (all NPCs this arc).
            // Expected school hours are day-shaped (SchoolCalendar): 6 on an
            // in-session weekday, 0 on weekends/breaks — so a planned summer
            // or weekend day never reads as truancy.
            if (npc.Person is PersonRuntime person)
            {
                TickPersonDay(npc, person, isAvatar, dayWasPlanned, costOfLivingDue,
                    SchoolCalendar.HoursForDay(e.Day));
            }
        }
    }

    /// <summary>
    /// The two sanctioned exceptions to "person stats are sticky" (person-
    /// layer doc §2.2/§2.3), run after the day's hours have ticked:
    /// happiness's weak daily reversion toward the setpoint, and — on the
    /// weekly beat the cost-of-living/family ticks already share — the GPA
    /// closed form. An UNPLANNED day credits full default attendance (an
    /// autopiloted student goes to school), and both attendance accumulators
    /// add the exact same day-shaped value (<paramref name="expectedSchoolHoursToday"/>,
    /// SchoolCalendar's 6-on-a-school-day / 0-on-weekends-and-breaks) so an
    /// all-autopilot neutral week still divides to attendanceFrac == 1
    /// bit-exactly — the §2.2 neutral identity, now landing on real school
    /// days instead of smeared over 7. GPA
    /// drift is avatar-only (only the avatar has a schedule; NPC gpa stays
    /// static until HS-5 gives it an event mover) and gated on the 9b school
    /// gate (no drift in the pro tiers — GPA freezes at graduation). Stress
    /// drag samples the scalar AT the weekly tick (pinned, not averaged).
    /// </summary>
    private void TickPersonDay(NpcRuntime npc, PersonRuntime person, bool isAvatar, bool dayWasPlanned, bool weeklyTickDue,
        float expectedSchoolHoursToday)
    {
        ApplyPersonStatDelta(person, PersonStatId.Happiness,
            PersonDrift.HappinessDailyStep(person.Stats.Happiness, person.HappinessSetpoint));

        person.ExpectedSchoolHoursThisWeek += expectedSchoolHoursToday;
        if (!dayWasPlanned)
        {
            person.SchoolHoursThisWeek += expectedSchoolHoursToday;
        }

        if (!weeklyTickDue)
        {
            return;
        }
        if (isAvatar && AvatarSchoolAvailable)
        {
            float attendanceFrac = person.ExpectedSchoolHoursThisWeek > 0f
                ? Math.Min(1f, person.SchoolHoursThisWeek / person.ExpectedSchoolHoursThisWeek)
                : 1f;
            ApplyGpaDelta(person, PersonDrift.GpaWeeklyDelta(
                attendanceFrac, person.Stats.Intelligence, person.Stats.Discipline,
                person.StudyHoursThisWeek, npc.Stress));
        }
        // Reset regardless of the gate so a mid-career tier change never
        // inherits a stale week.
        person.SchoolHoursThisWeek = 0f;
        person.ExpectedSchoolHoursThisWeek = 0f;
        person.StudyHoursThisWeek = 0f;
    }

    /// <summary>
    /// One player-planned day in a fixed canonical day order: the daytime
    /// blocks (School → Practice → Game → Work), the HS-4 free-time activity
    /// (the chosen evening), the day's lumped Travel hours (§5.3, revised —
    /// one bucket, not interleaved per trip), then the unallocated hours on
    /// the standard autopilot, then Sleep as the night cap. Sleep MUST run last: run first, an avatar
    /// sleeping from a full meter wastes the whole restore against the
    /// 100-clamp and the meter drains all day anyway (harness-measured:
    /// 8h-sleep day ending at Sleep 33.5). Blocks are uninterruptible — the
    /// player's stated intent outranks the crisis override for the skeleton
    /// (the stress-forces-the-avatar's-hand rule can layer onto this seam
    /// later); free hours keep the full TickHour behavior, override included.
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

        // HS-4: the chosen free-time activity ticks like any other block —
        // per-hour needs restore/decay plus the person-stat effect channel.
        if (schedule.FreeTimeHours > 0)
        {
            NpcActionDefinition freeTimeDef = ActionCatalog.Get(schedule.FreeTimeActivity);
            TickBlockHours(npc, schedule.FreeTimeHours, in freeTimeDef);
        }

        // HS-5: Family rides Idle's neutral definition per-hour, same as
        // Practice/Game above — its real effect is weekly, accumulated below.
        TickBlockHours(npc, schedule.FamilyHours, in ActionCatalog.Idle);

        // §2.2 weekly-drift inputs: a planned day's attendance is exactly its
        // scheduled School hours (planning 0 = deliberate truancy), and Study
        // free-time hours feed the StudyHoursTerm — the one way GPA benefits
        // from an action, since the effect channel can't reach gpa.
        if (npc.Person is PersonRuntime person)
        {
            person.SchoolHoursThisWeek += schedule.SchoolHours;
            if (schedule.FreeTimeActivity == NpcActionId.Study)
            {
                person.StudyHoursThisWeek += schedule.FreeTimeHours;
            }
            // HS-5 §7.1: only a planned day contributes — unlike School there
            // is no autopilot default, a day with no plan commits no family
            // time (matching the ScheduleScreen row's own "no plan set"
            // framing, not the attendance-assumed truancy framing School uses).
            person.FamilyHoursThisWeek += schedule.FamilyHours;
        }

        // §5.3 (revised): the day's commute cost — TravelTime.ComputeHours
        // already forced this into the schedule (GameManager.SubmitDaySchedule,
        // like School), so it just ticks as one lumped inert bucket, same as
        // Practice/Game/Family above. It competes for the same 24 hours as
        // everything else, so a car's discount shows up as MORE free hours
        // below, not a bonus layered on top.
        TickBlockHours(npc, schedule.TravelHours, in ActionCatalog.Idle);

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
            // HS-4 effect channel: PersonStatEffect deltas are per-hour by
            // definition, so a block applies them tick by tick — no proration
            // needed. A never-hydrated person (every NPC) skips at the null.
            if (npc.Person is PersonRuntime person)
            {
                PersonStatEffect[] effects = def.PersonEffects;
                for (int i = 0; i < effects.Length; i++)
                {
                    ApplyPersonStatDelta(person, effects[i].Stat, effects[i].DeltaPerHour);
                }
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
        // household_board.md: the avatar's meal price after the family's
        // share; 1.0 for every NPC and for an uncovered avatar — both the
        // decision (SelectAction) and the charge (ApplyAction) must see the
        // same price or a broke covered kid would refuse meals he'd never pay for.
        double eatCostShare = ReferenceEquals(npc, _avatarRuntime) ? _avatarBoardShare : 1.0;

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
            NpcActionId picked = UtilityCalculator.SelectAction(npc.Needs, npc.Funds, _weights, out _, npc.Stress, eatCostShare);
            if (npc.BusyHoursRemaining <= 0 || picked != npc.CurrentAction)
            {
                ApplyAction(npc, picked, eatCostShare);
            }
        }
        else if (npc.BusyHoursRemaining <= 0)
        {
            NpcActionId picked = UtilityCalculator.SelectAction(npc.Needs, npc.Funds, _weights, out _, npc.Stress, eatCostShare);
            ApplyAction(npc, picked, eatCostShare);
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

    private static void ApplyAction(NpcRuntime npc, NpcActionId id, double eatCostShare)
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
        // HS-4 effect channel, one-shot form: the per-hour deltas scaled by
        // the action's nominal hours (the RestoreAmount mirror). No autopilot
        // catalog entry carries effects today — wired anyway so a future
        // catalog edit can't silently no-op on the autopilot path.
        if (npc.Person is PersonRuntime person && def.PersonEffects.Length > 0)
        {
            for (int i = 0; i < def.PersonEffects.Length; i++)
            {
                ApplyPersonStatDelta(
                    person, def.PersonEffects[i].Stat,
                    def.PersonEffects[i].DeltaPerHour * def.TemporalCostHours);
            }
        }
        // household_board.md: Eat is the one action a covered household
        // discounts — the id gate matters because LegalWork also carries
        // PrimaryNeed Hunger, and its income must never scale.
        ApplyFundsDelta(npc, id == NpcActionId.Eat ? -def.FinancialCost * eatCostShare : -def.FinancialCost);
        npc.CurrentAction = id;
        npc.BusyHoursRemaining = Math.Max(0, (int)MathF.Ceiling(def.TemporalCostHours) - 1);
    }
}
