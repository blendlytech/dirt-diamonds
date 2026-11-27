using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DirtAndDiamonds.Data.Family;

/// <summary>
/// One parent-phase job (13-6c brief §4.1, D1-A) as data. Ids are stable save
/// data (<c>Game_State.parent_job</c>); every player-facing word is a
/// strings-table key the phone derives from the id (13-6d).
/// </summary>
public sealed class ParentJobProfile
{
    /// <summary>The stable id ("coach", "steady", "nightlife").</summary>
    public required string Id { get; init; }

    /// <summary>The engine-written Entity_Flags name while the job is held (job_coach | job_steady | job_nightlife).</summary>
    public required string Flag { get; init; }

    /// <summary>Shift weekdays as a bitmask over the Core Weekday ordinals (bit 0 = Monday … bit 6 = Sunday).</summary>
    public required int ShiftDayMask { get; init; }

    /// <summary>Hours of a shift; a forced Work block on each shift day (D3-A).</summary>
    public required int ShiftHours { get; init; }

    /// <summary>The fixed weekly wage in whole dollars, settled on the 7-day tick.</summary>
    public required int WeeklyWage { get; init; }

    /// <summary>The top of the weekly seeded tip roll, whole dollars (0 = no tips).</summary>
    public required int TipMax { get; init; }

    /// <summary>Training sessions a week, shared by every trainable child (D6-A).</summary>
    public required int SessionsPerWeek { get; init; }

    /// <summary>The job's signed drill-odds bonus in percentage points.</summary>
    public required int DrillBonusPp { get; init; }

    /// <summary>The signed weekly stress impulse.</summary>
    public required int WeeklyStress { get; init; }

    /// <summary>The weekly recklessness change (person-stat write).</summary>
    public required int WeeklyRecklessness { get; init; }

    /// <summary>The signed weekly care change in basis points for every child under 16 who is unrostered.</summary>
    public required int WeeklyCareBp { get; init; }

    /// <summary>When positive, a week with at least this many family hours waives <see cref="WeeklyCareBp"/>.</summary>
    public required int CareWaiverFamilyHours { get; init; }

    /// <summary>An active Entity_Flags name that bars this job (D8-A: banned_for_life bars Coach), or null.</summary>
    public string? BarredByFlag { get; init; }

    /// <summary>True when <paramref name="weekdayOrdinal"/> (Core Weekday, Monday = 0) is a shift day.</summary>
    public bool IsShiftWeekday(int weekdayOrdinal) =>
        weekdayOrdinal >= 0 && weekdayOrdinal < 7 && (ShiftDayMask & (1 << weekdayOrdinal)) != 0;

    /// <summary>Shift days in a 7-day week.</summary>
    public int ShiftDaysPerWeek
    {
        get
        {
            int count = 0;
            for (int d = 0; d < 7; d++)
            {
                count += (ShiftDayMask >> d) & 1;
            }
            return count;
        }
    }
}

/// <summary>
/// The loaded <c>Assets/Data/Family/parent_jobs.json</c> (13-6c brief §2, §4.1).
/// The <c>SchoolCatalogJson</c> discipline: a hand-walked JsonDocument, no
/// reflection serializers, every error loud and labelled, so a content edit
/// that breaks a job fails the boot or the harness, never a payday.
/// </summary>
public sealed class ParentJobCatalog
{
    private static readonly string[] WeekdayTokens = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };

    private readonly List<ParentJobProfile> _jobs;
    private readonly Dictionary<string, ParentJobProfile> _byId;

    private ParentJobCatalog(List<ParentJobProfile> jobs, string defaultJobId)
    {
        _jobs = jobs;
        _byId = new Dictionary<string, ParentJobProfile>(StringComparer.Ordinal);
        foreach (ParentJobProfile job in jobs)
        {
            _byId.Add(job.Id, job);
        }
        if (!_byId.TryGetValue(defaultJobId, out ParentJobProfile? fallback))
        {
            throw new FormatException($"Parent job catalog: default_job '{defaultJobId}' is not one of the jobs.");
        }
        if (fallback.BarredByFlag is not null)
        {
            throw new FormatException($"Parent job catalog: the default job '{defaultJobId}' must never be barred.");
        }
        DefaultJob = fallback;
    }

    /// <summary>Every job in file order (the phone's card order).</summary>
    public IReadOnlyList<ParentJobProfile> Jobs => _jobs;

    /// <summary>The job an unanswered first card resolves to (D2-A: Steady).</summary>
    public ParentJobProfile DefaultJob { get; }

    public bool TryGet(string id, out ParentJobProfile job)
    {
        if (_byId.TryGetValue(id, out ParentJobProfile? found))
        {
            job = found;
            return true;
        }
        job = null!;
        return false;
    }

    public ParentJobProfile Get(string id) =>
        TryGet(id, out ParentJobProfile job) ? job : throw new ArgumentException($"'{id}' is not a parent-phase job.", nameof(id));

    public static ParentJobCatalog Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("jobs", out JsonElement jobs) || jobs.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Parent job catalog must be an object with a 'jobs' array.");
        }
        string defaultJob = root.TryGetProperty("default_job", out JsonElement d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()!
            : throw new FormatException("Parent job catalog: missing string 'default_job'.");

        var list = new List<ParentJobProfile>(jobs.GetArrayLength());
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement job in jobs.EnumerateArray())
        {
            string id = RequireString(job, "id", "?");
            if (!ids.Add(id))
            {
                throw new FormatException($"Parent job catalog: duplicate job id '{id}'.");
            }
            string flag = RequireString(job, "flag", id);
            if (!flags.Add(flag))
            {
                throw new FormatException($"Parent job catalog: job '{id}' reuses the flag '{flag}'.");
            }
            int mask = ParseShiftDays(job, id);
            var profile = new ParentJobProfile
            {
                Id = id,
                Flag = flag,
                ShiftDayMask = mask,
                ShiftHours = RequireInt(job, "shift_hours", id, 1, 12),
                WeeklyWage = RequireInt(job, "weekly_wage", id, 0, 100_000),
                TipMax = RequireInt(job, "tip_max", id, 0, 100_000),
                SessionsPerWeek = RequireInt(job, "sessions_per_week", id, 0, 7),
                DrillBonusPp = RequireInt(job, "drill_bonus_pp", id, -50, 50),
                WeeklyStress = RequireInt(job, "weekly_stress", id, -100, 100),
                WeeklyRecklessness = RequireInt(job, "weekly_recklessness", id, -100, 100),
                WeeklyCareBp = RequireInt(job, "weekly_care_bp", id, -99, 99),
                CareWaiverFamilyHours = RequireInt(job, "care_waiver_family_hours", id, 0, 168),
                BarredByFlag = OptionalString(job, "barred_by_flag", id),
            };
            list.Add(profile);
        }
        if (list.Count == 0)
        {
            throw new FormatException("Parent job catalog has no jobs.");
        }
        return new ParentJobCatalog(list, defaultJob);
    }

    private static int ParseShiftDays(JsonElement job, string id)
    {
        if (!job.TryGetProperty("shift_days", out JsonElement days) || days.ValueKind != JsonValueKind.Array
            || days.GetArrayLength() == 0)
        {
            throw new FormatException($"Parent job '{id}': 'shift_days' must be a non-empty array.");
        }
        int mask = 0;
        foreach (JsonElement day in days.EnumerateArray())
        {
            int ordinal = day.ValueKind == JsonValueKind.String ? Array.IndexOf(WeekdayTokens, day.GetString()) : -1;
            if (ordinal < 0)
            {
                throw new FormatException($"Parent job '{id}': unknown shift day '{day}' (use mon..sun).");
            }
            if ((mask & (1 << ordinal)) != 0)
            {
                throw new FormatException($"Parent job '{id}': shift day '{day}' is listed twice.");
            }
            mask |= 1 << ordinal;
        }
        return mask;
    }

    private static string RequireString(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"Parent job '{id}': missing non-empty string '{property}'.");
        }
        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"Parent job '{id}': '{property}' must be a non-empty string or null.");
        }
        return value.GetString();
    }

    private static int RequireInt(JsonElement element, string property, string id, int min, int max)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int number))
        {
            throw new FormatException($"Parent job '{id}': missing integer '{property}'.");
        }
        if (number < min || number > max)
        {
            throw new FormatException($"Parent job '{id}': '{property}' = {number} is outside [{min}, {max}].");
        }
        return number;
    }
}
