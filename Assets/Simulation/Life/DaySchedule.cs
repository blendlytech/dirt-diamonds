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
/// </summary>
public readonly struct DaySchedule
{
    public const int HoursPerDay = 24;

    public readonly int SleepHours;
    public readonly int SchoolHours;
    public readonly int PracticeHours;
    public readonly int GameHours;
    public readonly int WorkHours;

    public DaySchedule(int sleepHours, int schoolHours, int practiceHours, int gameHours, int workHours)
    {
        if (sleepHours < 0 || schoolHours < 0 || practiceHours < 0 || gameHours < 0 || workHours < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sleepHours), "Schedule block hours cannot be negative.");
        }
        int total = sleepHours + schoolHours + practiceHours + gameHours + workHours;
        if (total > HoursPerDay)
        {
            throw new ArgumentException(
                $"Schedule allocates {total}h — a day has only {HoursPerDay}.");
        }
        SleepHours = sleepHours;
        SchoolHours = schoolHours;
        PracticeHours = practiceHours;
        GameHours = gameHours;
        WorkHours = workHours;
    }

    public int AllocatedHours => SleepHours + SchoolHours + PracticeHours + GameHours + WorkHours;

    /// <summary>Unallocated hours, ticked by the standard autopilot after the blocks run.</summary>
    public int FreeHours => HoursPerDay - AllocatedHours;
}
