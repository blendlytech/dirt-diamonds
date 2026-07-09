using System;

namespace DirtAndDiamonds.Simulation.Life;

/// <summary>
/// The avatar's player-chosen allocation of one calendar day's 24 hours across
/// the five block types (Phase 9b's bare daily clock). Hours left unallocated
/// are "free" and run the standard autopilot utility tick, so a partial plan
/// degrades gracefully instead of leaving dead hours.
///
/// Block semantics in this skeleton:
///   Sleep    — restores per-hour at ActionCatalog.Sleep's rate/environment.
///   School   — inert placeholder (consumes hours only) until 9d's development
///              curves; only schedulable in the HS/College tiers
///              (LifeSimManager.AvatarSchoolAvailable, projected in by the
///              caller — this assembly never sees LeagueTier).
///   Practice — inert placeholder until 9d, same as School.
///   Game     — life-side inert; the attended game itself is CareerManager's
///              pending-game flow, coordinated by the UI, never by this class.
///   Work     — inert hook until Phase 8a's hustles plug a real payout in.
///   FreeTime — HS-4: a chosen evening activity (Church / VideoGames / Study /
///              Hangout) so free time is a choice, not just autopilot; the
///              hours tick under the chosen catalog definition (person-stat
///              effects included). 0 hours = the pre-HS-4 all-autopilot
///              evening, bit-identical.
///   Family   — HS-5 (person-layer doc §7.1): time deliberately spent with
///              the avatar's own children, competing for the same 24 hours
///              as every other block. Inert like Practice/Game per hour —
///              its effect is weekly, not daily: TickPersonDay accumulates
///              it into PersonRuntime.FamilyHoursThisWeek, and
///              ChildRearingService reads that total at the weekly tick to
///              move the Child_Development axes. Appended last, defaulted
///              to 0, so every pre-HS-5 call site (including scripted
///              harness schedules) stays source- and behavior-compatible.
/// </summary>
public readonly struct DaySchedule
{
    public const int HoursPerDay = 24;

    public readonly int SleepHours;
    public readonly int SchoolHours;
    public readonly int PracticeHours;
    public readonly int GameHours;
    public readonly int WorkHours;
    public readonly int FreeTimeHours;
    public readonly int FamilyHours;

    /// <summary>The activity <see cref="FreeTimeHours"/> ticks under; Idle iff the block is empty.</summary>
    public readonly NpcActionId FreeTimeActivity;

    public DaySchedule(
        int sleepHours, int schoolHours, int practiceHours, int gameHours, int workHours,
        int freeTimeHours = 0, NpcActionId freeTimeActivity = NpcActionId.Idle, int familyHours = 0)
    {
        if (sleepHours < 0 || schoolHours < 0 || practiceHours < 0 || gameHours < 0 || workHours < 0
            || freeTimeHours < 0 || familyHours < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sleepHours), "Schedule block hours cannot be negative.");
        }
        int total = sleepHours + schoolHours + practiceHours + gameHours + workHours + freeTimeHours + familyHours;
        if (total > HoursPerDay)
        {
            throw new ArgumentException(
                $"Schedule allocates {total}h — a day has only {HoursPerDay}.");
        }
        if (freeTimeHours > 0 && !ActionCatalog.IsFreeTimeActivity(freeTimeActivity))
        {
            throw new ArgumentException(
                $"'{freeTimeActivity}' is not a free-time activity — the free-time block takes Church/VideoGames/Study/Hangout only.");
        }
        SleepHours = sleepHours;
        SchoolHours = schoolHours;
        PracticeHours = practiceHours;
        GameHours = gameHours;
        WorkHours = workHours;
        FreeTimeHours = freeTimeHours;
        FamilyHours = familyHours;
        // Idle when empty, whatever the caller passed — an unused selection
        // must never make two otherwise-identical plans compare different.
        FreeTimeActivity = freeTimeHours > 0 ? freeTimeActivity : NpcActionId.Idle;
    }

    public int AllocatedHours =>
        SleepHours + SchoolHours + PracticeHours + GameHours + WorkHours + FreeTimeHours + FamilyHours;

    /// <summary>Unallocated hours, ticked by the standard autopilot after the blocks run.</summary>
    public int FreeHours => HoursPerDay - AllocatedHours;
}
