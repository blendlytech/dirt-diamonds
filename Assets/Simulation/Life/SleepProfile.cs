namespace DirtAndDiamonds.Simulation.Life;

/// <summary>
/// Sleep-band rules for the avatar's PLANNED day (user directive 2026-07-12:
/// "sleep is an absolute"). The body always takes a guaranteed minimum; the
/// planning decision is how much MORE to give it: under 6 hours is a short
/// night (stress up, mood down, and the smaller restore it always was), 6–7
/// is merely fine, exactly 8 maximizes the restore AND grants the boost
/// (extra stress relief, mood up), and hours past 8 restore nothing while
/// souring the mood. Avatar-only by construction — the bands apply in
/// TickScheduledDay, and only the avatar has a schedule; NPC autopilot sleep
/// (the 8h Sleep action) is untouched, so every background trace is
/// byte-identical. Constants-as-data in the ActionCatalog idiom — first-pass
/// values, tunable via simulate_utility_decay; the harness pins every one.
/// </summary>
public static class SleepProfile
{
    /// <summary>The planner cannot book a night below this — GameManager.SubmitDaySchedule forces it and the slider's floor mirrors it. The body is an absolute.</summary>
    public const int MinPlannedSleepHours = 2;

    /// <summary>Below this is a short night: <see cref="ShortNightStress"/> + <see cref="ShortNightHappinessDelta"/> land when the block ends.</summary>
    public const int HealthyMinHours = 6;

    /// <summary>Exactly this maximizes restore and grants the boost; every hour past it restores nothing (ActionCatalog.Oversleep) and sours the mood.</summary>
    public const int OptimalHours = 8;

    public const float ShortNightStress = 8f;
    public const float GoodNightStressRelief = 4f;
    public const float ShortNightHappinessDelta = -1f;
    public const float GoodNightHappinessDelta = 1f;
    public const float OversleepHappinessDelta = -1f;
}
