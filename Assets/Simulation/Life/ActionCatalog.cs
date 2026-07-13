using System;

namespace DirtAndDiamonds.Simulation.Life;

public enum NpcActionId
{
    Idle = 0,
    Eat = 1,
    Sleep = 2,
    Shower = 3,
    SocializeEvening = 4,
    Workout = 5,
    DrinkAlone = 6,
    PickArgument = 7,

    // Schedule-block-only (Phase 9b/8a) — never autopilot-selected, so these
    // are deliberately NOT added to ActionCatalog.All/UtilityCalculator's scan.
    School = 8,
    LegalWork = 9,

    // Schedule-block-only (Phase 8b) — the Work block's needs-drain definition
    // when the avatar selected an interactive hustle (Narcotics/Fencing)
    // instead of Legal Work. Also excluded from All/UtilityCalculator's scan.
    HustleWork = 10,

    // HS-4 free-time activities (high_school_person_layer.md §2.2, the plan's
    // Epic 4): schedulable only through DaySchedule's free-time block, never
    // autopilot-selected — deliberately absent from All so every pre-HS-4
    // autopilot/NPC trace stays bit-identical (§9.1's neutral-identity hook).
    // These are the person-stat effect channel's first consumers.
    Church = 11,
    VideoGames = 12,
    Study = 13,
    Hangout = 14,

    // Schedule-block-only (sleep bands, SleepProfile): the hours of a planned
    // night PAST SleepProfile.OptimalHours tick under this — Sleep's exact
    // restful environment but zero restore, so oversleeping wastes the hours
    // instead of banking extra Sleep. Excluded from All/UtilityCalculator's
    // scan like every other block-only id.
    Oversleep = 15,
}

// PrimaryNeed is null for actions that don't restore a tracked need directly
// (Idle, and the stress-relief actions below).
public readonly struct NpcActionDefinition
{
    public readonly NpcActionId Id;
    public readonly NeedType? PrimaryNeed;
    public readonly float RestoreAmount;
    public readonly float TemporalCostHours;

    // Positive = costs money per occurrence (every autopilot action so far);
    // negative = EARNS money (LegalWork, Phase 8a) — both ApplyAction's and
    // TickBlockHours' funds math compute Funds - FinancialCost, so a negative
    // value already falls out correctly as income, no separate field needed.
    public readonly double FinancialCost;
    public readonly float Risk0To100;
    public readonly bool IsStressRelief;

    // life_sim_needs_decay.md §4.1: the per-need Environmental Multiplier that applies
    // to every decay tick spent performing this action. The activity IS the location
    // context for now — a dedicated Location/place entity is deferred until NPCs can
    // actually be somewhere (Phase 7+ gritty-event venues); this field is where such a
    // context would compose in.
    public readonly EnvironmentalModifiers Environment;

    // HS-4 person-stat effect channel (person-layer doc §2): per-HOUR nudges
    // applied while performing this action — schedule blocks apply them per
    // ticked hour, a one-shot autopilot application scales by
    // TemporalCostHours. The array reference is shared static catalog data
    // (allocated once at class init, empty for the classic actions), so the
    // struct stays copy-cheap and the tick paths stay allocation-free. GPA is
    // deliberately NOT reachable here — it moves only through PersonDrift's
    // §2.2 weekly closed form (Study feeds its StudyHoursTerm via the
    // schedule accumulator, not a stat delta).
    public readonly PersonStatEffect[] PersonEffects;

    public NpcActionDefinition(
        NpcActionId id, NeedType? primaryNeed, float restoreAmount, float temporalCostHours,
        double financialCost, float risk0To100, bool isStressRelief,
        EnvironmentalModifiers? environment = null,
        PersonStatEffect[]? personEffects = null)
    {
        Id = id;
        PrimaryNeed = primaryNeed;
        RestoreAmount = restoreAmount;
        TemporalCostHours = temporalCostHours;
        FinancialCost = financialCost;
        Risk0To100 = risk0To100;
        IsStressRelief = isStressRelief;
        Environment = environment ?? EnvironmentalModifiers.Neutral;
        PersonEffects = personEffects ?? Array.Empty<PersonStatEffect>();
    }
}

public static class ActionCatalog
{
    // Restore amounts anchored to life_sim_needs_decay.md §6's illustrative anchors
    // (meal Hunger+45, night's sleep Sleep+80/8h, shower Hygiene+60, evening out
    // Social+35, workout Fitness+30). Hours/cost/risk have no design-doc anchor —
    // first-pass constants, data-edit tunable via simulate_utility_decay exactly
    // like NeedDecayProfile's own constants were.
    // Environment vectors (life_sim_needs_decay.md §4.1 anchors where one exists):
    // Idle/Eat run at the calibrated neutral baseline; Sleep/Shower happen at home
    // ("resting at home" ×0.8, all needs); SocializeEvening is the bar/party anchor
    // (being social slows Social decay ×0.4 — the +35 restore is separate, per §6's
    // decay-vs-recovery split); Workout is §6's "actions trade needs against each
    // other" — exertion accelerates Hunger ×1.5 (the labor-hustle Hunger anchor) and
    // Sleep ×1.3 (no doc anchor — first-pass invented, tunable like every constant
    // here via simulate_utility_decay).
    public static readonly NpcActionDefinition Idle = new(NpcActionId.Idle, null, 0f, 0f, 0.0, 0f, false);
    public static readonly NpcActionDefinition Eat = new(NpcActionId.Eat, NeedType.Hunger, 45f, 1f, 12.0, 0f, false);
    public static readonly NpcActionDefinition Sleep = new(NpcActionId.Sleep, NeedType.Sleep, 80f, 8f, 0.0, 0f, false,
        EnvironmentalModifiers.Uniform(0.8f));
    public static readonly NpcActionDefinition Shower = new(NpcActionId.Shower, NeedType.Hygiene, 60f, 1f, 0.0, 0f, false,
        EnvironmentalModifiers.Uniform(0.8f));
    public static readonly NpcActionDefinition SocializeEvening = new(NpcActionId.SocializeEvening, NeedType.Social, 35f, 3f, 40.0, 5f, false,
        new EnvironmentalModifiers(hunger: 1f, sleep: 1f, hygiene: 1f, social: 0.4f, fitness: 1f));
    public static readonly NpcActionDefinition Workout = new(NpcActionId.Workout, NeedType.Fitness, 30f, 2f, 10.0, 0f, false,
        new EnvironmentalModifiers(hunger: 1.5f, sleep: 1.3f, hygiene: 1f, social: 1f, fitness: 1f));

    // Stress-relief actions (life_sim_ai.md: "alcohol, arguments") restore no need
    // directly — their entire utility comes from UtilityCalculator's stress-relief
    // consideration, which only activates once a need is critical.
    public static readonly NpcActionDefinition DrinkAlone = new(NpcActionId.DrinkAlone, null, 0f, 2f, 25.0, 30f, true);
    public static readonly NpcActionDefinition PickArgument = new(NpcActionId.PickArgument, null, 0f, 1f, 0.0, 45f, true);

    // Phase 9b/8a schedule blocks — plan-driven only (LifeSimManager.TickBlockHours),
    // never autopilot-selected, so deliberately excluded from All below.
    //
    // School: a modest built-in lunch (+3/hr) closing the 9b-disclosed
    // Hunger-starvation gap for a school-heavy day. Environment neutral (a
    // classroom isn't a punishing environment).
    public static readonly NpcActionDefinition School =
        new(NpcActionId.School, NeedType.Hunger, 18f, 6f, 0.0, 0f, false);

    // LegalWork ("Legal Work" hustle, gritty_events.md: "time-skip abstraction
    // that heavily drains Energy and Fitness for minimal payout" — this
    // codebase's only Energy analog is Sleep): a work lunch break (+2/hr,
    // closing the same meal-access gap for Work) at $25/hr (FinancialCost
    // negative = income), zero risk, heavy Sleep/Fitness drain via Environment.
    // $25/hr (not a smaller "modest" number) is a tuned first-pass constant,
    // not a design-doc anchor: a broke NPC's OWN free-hour autopilot spend
    // (Eat/Shower/SocializeEvening/Workout, funds-blind — SelectAction only
    // discourages a cost via FinancialCostScore, never refuses it) already
    // runs well past the $70/week cost-of-living drain on its own (harness-
    // measured), so Legal Work has to outpace BOTH to make working genuinely
    // pay off — tunable via simulate_utility_decay like every other table here.
    public static readonly NpcActionDefinition LegalWork =
        new(NpcActionId.LegalWork, NeedType.Hunger, 16f, 8f, -200.0, 0f, false,
            new EnvironmentalModifiers(hunger: 1f, sleep: 2f, hygiene: 1f, social: 1f, fitness: 1.8f));

    // Oversleep (sleep bands, SleepProfile): the hours of a planned night
    // past OptimalHours. Sleep's exact 0.8 restful environment, zero restore
    // (PrimaryNeed null) — lying in bed still shelters the other needs, it
    // just banks nothing. Block-only; never autopilot-selected.
    public static readonly NpcActionDefinition Oversleep =
        new(NpcActionId.Oversleep, null, 0f, 1f, 0.0, 0f, false,
            EnvironmentalModifiers.Uniform(0.8f));

    // HustleWork (Phase 8b, hustles_narcotics_fencing.md §2): the SAME
    // meal-access + needs drain as LegalWork — the Work block still costs the
    // avatar the same hours/exertion regardless of which activity they picked
    // — but FinancialCost = 0. The interactive hustle session (Narcotics/
    // Fencing), not the tick, is where the money and risk move.
    public static readonly NpcActionDefinition HustleWork =
        new(NpcActionId.HustleWork, NeedType.Hunger, 16f, 8f, 0.0, 0f, false,
            new EnvironmentalModifiers(hunger: 1f, sleep: 2f, hygiene: 1f, social: 1f, fitness: 1.8f));

    // ------------------------------------------------------------------
    // HS-4 free-time activities (person-layer doc §2.1/§2.2): the DaySchedule
    // free-time block's catalog — never autopilot-selected (absent from All,
    // like School/LegalWork), so the plan's "autopilot-eligible where
    // sensible" resolves to NONE at first pass (disclosed): each either
    // restores nothing (utility ≈ Idle, unreachable) or would perturb the
    // calibrated SocializeEvening traces. Every magnitude below is a
    // first-pass invention (the doc pins consumers, not sizes), tunable via
    // simulate_utility_decay like every other table here. All free — a
    // teenager's evening habits cost time, not money (disclosed; Hangout's
    // Social restore is deliberately weaker per hour than the paid
    // SocializeEvening night out, 8/h vs ~11.7/h).
    // ------------------------------------------------------------------

    public static readonly NpcActionDefinition Church =
        new(NpcActionId.Church, null, 0f, 2f, 0.0, 0f, false,
            personEffects: new[] { new PersonStatEffect(PersonStatId.Morality, 0.4f) });

    public static readonly NpcActionDefinition VideoGames =
        new(NpcActionId.VideoGames, null, 0f, 2f, 0.0, 0f, false,
            personEffects: new[]
            {
                new PersonStatEffect(PersonStatId.Happiness, 1.0f),
                new PersonStatEffect(PersonStatId.Discipline, -0.2f),
            });

    // Study's GPA payoff rides PersonDrift.StudyGpaPerHour through the weekly
    // closed form (LifeSimManager accumulates the block's hours), NOT a stat
    // delta here — the happiness cost is the §2.1 "+GPA drift, −happiness"
    // trade's other half.
    public static readonly NpcActionDefinition Study =
        new(NpcActionId.Study, null, 0f, 2f, 0.0, 0f, false,
            personEffects: new[] { new PersonStatEffect(PersonStatId.Happiness, -0.3f) });

    public static readonly NpcActionDefinition Hangout =
        new(NpcActionId.Hangout, NeedType.Social, 24f, 3f, 0.0, 0f, false,
            new EnvironmentalModifiers(hunger: 1f, sleep: 1f, hygiene: 1f, social: 0.4f, fitness: 1f),
            personEffects: new[] { new PersonStatEffect(PersonStatId.Charisma, 0.3f) });

    /// <summary>True for the actions DaySchedule accepts in its free-time block (HS-4).</summary>
    public static bool IsFreeTimeActivity(NpcActionId id) =>
        id is NpcActionId.Church or NpcActionId.VideoGames or NpcActionId.Study or NpcActionId.Hangout;

    // Idle listed first: SelectAction's strict-greater-than tie-break means Idle
    // — the guaranteed always-affordable fallback — wins any exact utility tie.
    // School/LegalWork/HustleWork deliberately absent — schedule-block-only (see
    // above); Church/VideoGames/Study/Hangout deliberately absent — free-time-
    // block-only (see their banner comment).
    public static readonly NpcActionDefinition[] All =
    {
        Idle, Eat, Sleep, Shower, SocializeEvening, Workout, DrinkAlone, PickArgument,
    };

    public static NpcActionDefinition Get(NpcActionId id) => id switch
    {
        NpcActionId.Idle => Idle,
        NpcActionId.Eat => Eat,
        NpcActionId.Sleep => Sleep,
        NpcActionId.Shower => Shower,
        NpcActionId.SocializeEvening => SocializeEvening,
        NpcActionId.Workout => Workout,
        NpcActionId.DrinkAlone => DrinkAlone,
        NpcActionId.PickArgument => PickArgument,
        NpcActionId.School => School,
        NpcActionId.LegalWork => LegalWork,
        NpcActionId.HustleWork => HustleWork,
        NpcActionId.Church => Church,
        NpcActionId.VideoGames => VideoGames,
        NpcActionId.Study => Study,
        NpcActionId.Hangout => Hangout,
        NpcActionId.Oversleep => Oversleep,
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };
}
