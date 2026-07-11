using System;

namespace DirtAndDiamonds.Simulation.Life;

/// <summary>
/// Per-trip commute cost for a planned day (design doc §5.3, revised): every
/// distinct place the avatar leaves home for that day — school, the team
/// facility (Practice/Game share one trip), work, or an out-of-home
/// free-time activity (Hangout/Church only; Study/VideoGames stay home) —
/// costs a mode-dependent round trip. Walking is the baseline; the avatar's
/// best owned Transport item (<see cref="LifeSimManager.AvatarTransportHoursSaved"/>,
/// still sourced from <c>ItemEffects.BestTransportHoursSaved</c>) discounts
/// each trip, floored so even the best car never makes a trip free. Pure/
/// static/zero-alloc, the same style as <see cref="PersonDrift"/>.
/// </summary>
public static class TravelTime
{
    public const float WalkRoundTripHoursPerTrip = 1.25f;
    public const float MinRoundTripHoursPerTrip = 0.25f;

    /// <summary>
    /// The day's total travel hours (whole, rounded away from zero) and how
    /// many distinct trips produced it. Zero trips is always zero hours,
    /// regardless of transport.
    /// </summary>
    public static int ComputeHours(
        int schoolHours, int practiceHours, int gameHours, int workHours,
        int freeTimeHours, NpcActionId freeTimeActivity, float transportHoursSaved,
        out int trips)
    {
        trips = CountTrips(schoolHours, practiceHours, gameHours, workHours, freeTimeHours, freeTimeActivity);
        if (trips == 0)
        {
            return 0;
        }
        float perTrip = Math.Max(WalkRoundTripHoursPerTrip - transportHoursSaved, MinRoundTripHoursPerTrip);
        return (int)MathF.Round(trips * perTrip, MidpointRounding.AwayFromZero);
    }

    private static int CountTrips(
        int schoolHours, int practiceHours, int gameHours, int workHours,
        int freeTimeHours, NpcActionId freeTimeActivity)
    {
        int trips = 0;
        if (schoolHours > 0)
        {
            trips++;
        }
        if (practiceHours > 0 || gameHours > 0)
        {
            trips++;
        }
        if (workHours > 0)
        {
            trips++;
        }
        if (freeTimeHours > 0 && freeTimeActivity is NpcActionId.Hangout or NpcActionId.Church)
        {
            trips++;
        }
        return trips;
    }
}
