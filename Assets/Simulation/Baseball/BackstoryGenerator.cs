using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// One rolled founding-avatar origin — the §3 wealth-tier reality of
/// docs/design/high_school_person_layer.md plus the two person-stat seeds the
/// backstory owns (§2.1: attractiveness is a backstory roll, social_status is
/// the family-tier seed). Everything an avatar-creation UI reveals (and may
/// re-roll) before <see cref="CareerManager.CreateAvatar"/> persists it.
/// Parent surnames are the avatar's own and are applied at creation, so the
/// roll carries first names only.
/// </summary>
public readonly struct Backstory
{
    /// <summary>§3 wealth tier, 0 (Destitute) … 4 (Wealthy).</summary>
    public readonly int WealthTier;

    /// <summary>Flavor + coarse event input (§3) — the tier anchor with mild jitter, never a gameplay meter.</summary>
    public readonly double HouseholdIncome;

    /// <summary>Replaces the flat CareerManager.StartingFunds; tier 2's value IS that flat $500 (the §3 continuity anchor).</summary>
    public readonly double StartingFunds;

    /// <summary>Paid on the weekly family tick (HS-3).</summary>
    public readonly double AllowanceWeekly;

    /// <summary>§4.2: tiers 0–1 have no home internet and must travel for free Wi-Fi.</summary>
    public readonly bool HomeWifi;

    /// <summary>Phone hardware tier 1–3 (§4.1 — features).</summary>
    public readonly int PhoneTier;

    /// <summary>Phone plan 0–2 (§4.2 — metering).</summary>
    public readonly int PhonePlan;

    /// <summary>Starting minute balance for the rolled plan (unlimited carries 0 — it never meters).</summary>
    public readonly int PhoneMinutes;

    /// <summary>Catalog item_id of the parental transport gift (§3.1), or null for tiers 0–1 (self-buy is their compensating arc).</summary>
    public readonly string? TransportGiftItemId;

    /// <summary>Parental strictness 0–100, rolled INDEPENDENT of wealth (§3 — orthogonal narrative axes).</summary>
    public readonly int Strictness;

    /// <summary>Person-stat seed: intrinsic attractiveness base (§2.1 "backstory roll").</summary>
    public readonly int Attractiveness;

    /// <summary>Person-stat seed: social_status base from the family tier (§2.1 "family tier seed").</summary>
    public readonly int SocialStatus;

    public readonly string Parent1FirstName;
    public readonly int Parent1Age;
    public readonly string Parent2FirstName;
    public readonly int Parent2Age;

    public Backstory(
        int wealthTier, double householdIncome, double startingFunds, double allowanceWeekly,
        bool homeWifi, int phoneTier, int phonePlan, int phoneMinutes, string? transportGiftItemId,
        int strictness, int attractiveness, int socialStatus,
        string parent1FirstName, int parent1Age, string parent2FirstName, int parent2Age)
    {
        WealthTier = wealthTier;
        HouseholdIncome = householdIncome;
        StartingFunds = startingFunds;
        AllowanceWeekly = allowanceWeekly;
        HomeWifi = homeWifi;
        PhoneTier = phoneTier;
        PhonePlan = phonePlan;
        PhoneMinutes = phoneMinutes;
        TransportGiftItemId = transportGiftItemId;
        Strictness = strictness;
        Attractiveness = attractiveness;
        SocialStatus = socialStatus;
        Parent1FirstName = parent1FirstName;
        Parent1Age = parent1Age;
        Parent2FirstName = parent2FirstName;
        Parent2Age = parent2Age;
    }
}

/// <summary>
/// Pure, engine-free backstory generation for the HS-2 creation revamp
/// (high_school_person_layer.md §3 verbatim). Deals only in <see cref="RngState"/>
/// and Data row DTOs — never the database, never a sim loop — the exact
/// <see cref="HeirGenetics"/> profile: every knob is data in
/// <see cref="BackstoryProfile"/>, the wealth-tier mapping exposes a
/// deterministic core (<see cref="RollWealthTier"/>) the harness drives with
/// exact percentiles, and <see cref="Roll"/> is seed-deterministic with a
/// pinned draw order (tier percentile, income bell, strictness bell,
/// attractiveness bell, then parent name/age pairs). Fork discipline is the
/// caller's: CareerManager rolls from a Split() of its own stream so the
/// pre-HS-2 draw sequence never moves.
/// </summary>
public static class BackstoryGenerator
{
    /// <summary>
    /// §3/§11 calibration knobs — constants-as-data, the HeirGeneticsProfile
    /// precedent. The per-tier tables are indexed by wealth tier 0–4 and are
    /// the design doc's table VERBATIM; the harness asserts them cell by cell.
    /// </summary>
    public static class BackstoryProfile
    {
        /// <summary>§3 tier frequencies in percent — Destitute 12, Working-class 28, Middle 35, Comfortable 18, Wealthy 7.</summary>
        public static readonly int[] TierFrequencyPercent = { 12, 28, 35, 18, 7 };

        /// <summary>§3 household income anchors (flavor + coarse event input).</summary>
        public static readonly double[] HouseholdIncomeByTier = { 14_000, 40_000, 72_000, 135_000, 320_000 };

        /// <summary>§3 starting funds. Tier 2 = $500 = the shipped flat CareerManager.StartingFunds — the continuity anchor the 8a economy was tuned against.</summary>
        public static readonly double[] StartingFundsByTier = { 50, 200, 500, 1_500, 5_000 };

        /// <summary>§3 weekly allowance (the HS-3 family-tick drip).</summary>
        public static readonly double[] AllowanceByTier = { 0, 10, 25, 60, 150 };

        /// <summary>§3 phone hardware by tier: burner / burner / mid / mid / flagship.</summary>
        public static readonly int[] PhoneTierByTier = { 1, 1, 2, 2, 3 };

        /// <summary>§3 phone plan by tier: prepaid / prepaid / basic / basic / unlimited.</summary>
        public static readonly int[] PhonePlanByTier = { 0, 0, 1, 1, 2 };

        /// <summary>
        /// Starting minute balance by PLAN (not tier): prepaid opens with a
        /// nearly-spent hand-me-down balance, basic opens on a full weekly
        /// allotment (§4.2's 50/wk refill), unlimited never meters so carries 0.
        /// First-pass numbers; HS-3 owns the live minute economy.
        /// </summary>
        public static readonly int[] StartingMinutesByPlan = { 30, 50, 0 };

        /// <summary>§4.2: home Wi-Fi exists at this tier and above.</summary>
        public const int HomeWifiMinTier = 2;

        /// <summary>
        /// §3.1 parental transport gifts by tier — catalog item_ids that are a
        /// CONTRACT with HS-3's Assets/Data/Items/items.json (which validates
        /// loudly at load, so a drifted id fails fast there). used_sedan is the
        /// design doc's own §5.1 example entry. Tiers 0–1 gift nothing.
        /// </summary>
        public static readonly string?[] TransportGiftByTier = { null, null, "commuter_bike", "used_sedan", "new_coupe" };

        /// <summary>§2.1 social_status family-tier seed: 30 + 10·tier, so the modal tier 2 sits exactly on the 50 neutral.</summary>
        public const int SocialStatusBase = 30;
        public const int SocialStatusPerTier = 10;

        /// <summary>Bell scale of the intrinsic attractiveness roll around 50 — modest, per §2.5's spread posture.</summary>
        public const int AttractivenessSpread = 15;

        /// <summary>Bell scale of the strictness roll around 50 (§3: independent of wealth).</summary>
        public const int StrictnessSpread = 25;

        /// <summary>Household income jitter as a fraction of the tier anchor (the "~" in the §3 table).</summary>
        public const double IncomeJitterFraction = 0.10;

        /// <summary>Parent age roll: min + NextInt(span). 36–48 against the avatar's 16 — always OLDER, the §1.2 direction invariant's load-bearing fact.</summary>
        public const int ParentAgeMin = 36;
        public const int ParentAgeSpan = 13;

        /// <summary>Affinity written on each parent→avatar Child edge — warmer than the newborn BirthAffinity (30): these parents raised the kid for 16 years.</summary>
        public const int ParentChildAffinity = 40;

        /// <summary>Affinity on the Partner edge between the two parents.</summary>
        public const int ParentPartnerAffinity = 50;

        /// <summary>Strictness written for an INHERITED household (§10: the player is the parent — a neutral dial, not a rolled one).</summary>
        public const int InheritedStrictness = 50;
    }

    /// <summary>
    /// Deterministic core of the wealth roll: maps a percentile 0–99 onto the
    /// §3 cumulative frequency bands (12 / 40 / 75 / 93 / 100).
    /// </summary>
    public static int RollWealthTier(int percentile)
    {
        if (percentile is < 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "Percentile must be 0–99.");
        }
        int cumulative = 0;
        for (int tier = 0; tier < BackstoryProfile.TierFrequencyPercent.Length; tier++)
        {
            cumulative += BackstoryProfile.TierFrequencyPercent[tier];
            if (percentile < cumulative)
            {
                return tier;
            }
        }
        return BackstoryProfile.TierFrequencyPercent.Length - 1; // unreachable while frequencies sum to 100
    }

    /// <summary>
    /// Inverts the §3 starting-funds ladder for the succession path (§10): a
    /// retiring avatar's funds place the household the heir grew up in. The
    /// thresholds ARE the per-tier starting funds — self-anchoring, so a
    /// funds-table retune moves both directions together.
    /// </summary>
    public static int WealthTierForFunds(double funds)
    {
        for (int tier = BackstoryProfile.StartingFundsByTier.Length - 1; tier >= 1; tier--)
        {
            if (funds >= BackstoryProfile.StartingFundsByTier[tier])
            {
                return tier;
            }
        }
        return 0;
    }

    public static double StartingFundsFor(int wealthTier) => BackstoryProfile.StartingFundsByTier[ValidTier(wealthTier)];
    public static double AllowanceFor(int wealthTier) => BackstoryProfile.AllowanceByTier[ValidTier(wealthTier)];
    public static double HouseholdIncomeAnchorFor(int wealthTier) => BackstoryProfile.HouseholdIncomeByTier[ValidTier(wealthTier)];
    public static int PhoneTierFor(int wealthTier) => BackstoryProfile.PhoneTierByTier[ValidTier(wealthTier)];
    public static int PhonePlanFor(int wealthTier) => BackstoryProfile.PhonePlanByTier[ValidTier(wealthTier)];
    public static int StartingMinutesFor(int phonePlan) => BackstoryProfile.StartingMinutesByPlan[phonePlan];
    public static bool HasHomeWifi(int wealthTier) => ValidTier(wealthTier) >= BackstoryProfile.HomeWifiMinTier;
    public static string? TransportGiftFor(int wealthTier) => BackstoryProfile.TransportGiftByTier[ValidTier(wealthTier)];
    public static int SocialStatusSeedFor(int wealthTier) =>
        BackstoryProfile.SocialStatusBase + BackstoryProfile.SocialStatusPerTier * ValidTier(wealthTier);

    /// <summary>
    /// Rolls one complete founding backstory. Draw order is part of the
    /// determinism contract: (1) wealth-tier percentile, (2) income-jitter
    /// bell, (3) strictness bell, (4) attractiveness bell, (5) parent-1 first
    /// name, (6) parent-1 age, (7) parent-2 first name, (8) parent-2 age.
    /// Everything else is a pure table lookup off the rolled tier.
    /// </summary>
    public static Backstory Roll(ref RngState rng)
    {
        int tier = RollWealthTier(rng.NextInt(100));
        double income = HouseholdIncomeAnchorFor(tier)
            * (1.0 + BackstoryProfile.IncomeJitterFraction * HeirGenetics.Bell(ref rng));
        int strictness = RollAround50(BackstoryProfile.StrictnessSpread, ref rng);
        int attractiveness = RollAround50(BackstoryProfile.AttractivenessSpread, ref rng);
        string parent1First = LeagueGenerator.GenerateFirstName(ref rng);
        int parent1Age = BackstoryProfile.ParentAgeMin + rng.NextInt(BackstoryProfile.ParentAgeSpan);
        string parent2First = LeagueGenerator.GenerateFirstName(ref rng);
        int parent2Age = BackstoryProfile.ParentAgeMin + rng.NextInt(BackstoryProfile.ParentAgeSpan);

        int plan = PhonePlanFor(tier);
        return new Backstory(
            tier, income, StartingFundsFor(tier), AllowanceFor(tier),
            HasHomeWifi(tier), PhoneTierFor(tier), plan, StartingMinutesFor(plan), TransportGiftFor(tier),
            strictness, attractiveness, SocialStatusSeedFor(tier),
            parent1First, parent1Age, parent2First, parent2Age);
    }

    /// <summary>
    /// The avatar's Player_Person seed: neutral everywhere except the two
    /// stats the backstory owns (§2.1) — attractiveness from the roll,
    /// social_status from the family tier. The creation UI's trait picker
    /// applies its offsets on top of this row and hands the result to
    /// <see cref="CareerManager.CreateAvatar"/> so creation stays one batch.
    /// </summary>
    public static PersonRow BuildPersonRow(string playerId, in Backstory backstory)
    {
        PersonRow row = PersonRow.Neutral(playerId);
        row.Attractiveness = backstory.Attractiveness;
        row.SocialStatus = backstory.SocialStatus;
        return row;
    }

    /// <summary>
    /// §10: the family context <see cref="CareerManager.Succeed"/> writes for
    /// an heir — the avatar's own household, never a fresh roll. Anchor
    /// income (no jitter — succession is deterministic), Wi-Fi by tier,
    /// allowance 0 (an adult taking over a career draws no allowance) and
    /// neutral strictness (the player IS the parent) are the disclosed
    /// first-pass simplifications.
    /// </summary>
    public static FamilyBackgroundRow InheritedFamily(string heirId, int wealthTier, string? parent1Id, string? parent2Id) => new()
    {
        PlayerId = heirId,
        WealthTier = ValidTier(wealthTier),
        HouseholdIncome = HouseholdIncomeAnchorFor(wealthTier),
        Parent1Id = parent1Id,
        Parent2Id = parent2Id,
        HomeWifi = HasHomeWifi(wealthTier),
        AllowanceWeekly = 0,
        Strictness = BackstoryProfile.InheritedStrictness,
    };

    /// <summary>The heir's phone at takeover: hardware/plan per the household tier, stamped with the succession day.</summary>
    public static PhoneStateRow InheritedPhone(string heirId, int wealthTier, int day)
    {
        int plan = PhonePlanFor(wealthTier);
        return new PhoneStateRow
        {
            PlayerId = heirId,
            Tier = PhoneTierFor(wealthTier),
            Plan = plan,
            MinutesRemaining = StartingMinutesFor(plan),
            PurchasedDay = day,
        };
    }

    /// <summary>Zero-centred triangular roll around 50 — HeirGenetics rounding (away from zero), clamped to the schema's [0,100].</summary>
    private static int RollAround50(int spread, ref RngState rng)
    {
        double value = 50 + spread * HeirGenetics.Bell(ref rng);
        return Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
    }

    private static int ValidTier(int wealthTier) =>
        wealthTier is >= 0 and <= 4
            ? wealthTier
            : throw new ArgumentOutOfRangeException(nameof(wealthTier), wealthTier, "Wealth tier must be 0–4.");
}
