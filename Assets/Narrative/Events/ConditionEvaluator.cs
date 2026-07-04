using System;
using System.Collections.Generic;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Narrative.Events;

/// <summary>
/// Evaluates an event's prerequisite conjunction against one subject's polled
/// snapshot (gritty_event_framework.md §3). Pure and zero-allocation: field
/// reads off the <see cref="PollPlayerRow"/> struct, flag probes into the
/// per-day (player, flag) → set_on_day map the dispatcher rebuilds on day
/// change. Runs on the polling thread; touches no database and no bus.
/// </summary>
public static class ConditionEvaluator
{
    public static bool AllHold(
        EventPrerequisite[] prerequisites,
        in PollPlayerRow subject,
        Dictionary<(string PlayerId, string FlagName), long> activeFlags,
        long currentDay)
    {
        for (int i = 0; i < prerequisites.Length; i++)
        {
            if (!Holds(in prerequisites[i], in subject, activeFlags, currentDay))
            {
                return false;
            }
        }
        return true;
    }

    public static bool Holds(
        in EventPrerequisite prerequisite,
        in PollPlayerRow subject,
        Dictionary<(string PlayerId, string FlagName), long> activeFlags,
        long currentDay)
    {
        switch (prerequisite.Kind)
        {
            case PrerequisiteKind.Field:
                return Compare(FieldValue(in subject, prerequisite.Field), prerequisite.Comparison, prerequisite.Value);

            case PrerequisiteKind.FlagActive:
                return activeFlags.TryGetValue((subject.PlayerId, prerequisite.FlagName!), out long setOnDay)
                    && currentDay - setOnDay >= prerequisite.MinDaysSince;

            case PrerequisiteKind.FlagInactive:
                return !activeFlags.ContainsKey((subject.PlayerId, prerequisite.FlagName!));

            default:
                throw new ArgumentOutOfRangeException(nameof(prerequisite), prerequisite.Kind, null);
        }
    }

    private static double FieldValue(in PollPlayerRow subject, SubjectField field) => field switch
    {
        SubjectField.Funds => subject.Funds,
        SubjectField.Age => subject.Age,
        SubjectField.Recklessness => subject.Recklessness,
        SubjectField.HealthCeiling => subject.HealthCeiling,
        SubjectField.DetectionRisk => subject.DetectionRisk,
        SubjectField.BaseballInterest => subject.BaseballInterest,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    private static bool Compare(double actual, FieldComparison comparison, double expected) => comparison switch
    {
        FieldComparison.Less => actual < expected,
        FieldComparison.LessOrEqual => actual <= expected,
        FieldComparison.Greater => actual > expected,
        FieldComparison.GreaterOrEqual => actual >= expected,
        FieldComparison.Equal => actual == expected,
        FieldComparison.NotEqual => actual != expected,
        _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, null),
    };
}
