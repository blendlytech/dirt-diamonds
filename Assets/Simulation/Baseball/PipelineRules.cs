using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// The criminal-record flag names the pipeline reads for the avatar (DIRT-4
/// §5-teeth; 13-6 brief D6-A). Their home moved here from the retired
/// PromotionManager; content writes them, the pipeline only reads them, and
/// only for the avatar (NPCs never carry them and are never read for them).
/// </summary>
public static class CareerRecordFlags
{
    /// <summary>Active: the avatar's value is docked by <see cref="PipelineProfile.RecordArrestPenalty"/> wherever the pipeline ranks him.</summary>
    public const string RecordArrest = "record_arrest";

    /// <summary>The terminal mark (Pro-only by content). No 13-6 effect (D6-A); whether it forces the release path is the parent-phase brief's.</summary>
    public const string BannedForLife = "banned_for_life";
}

/// <summary>
/// Every slice 13-6 pipeline constant (league_restructure.md §9; brief §4
/// item 1, Gemini 4 Argon's §10 values) as named data: retuning one is a data
/// edit behind Tools/PipelineHarness and run_monte_carlo_batch, never a logic
/// edit. Retirement (42) and the health floor (40) are reused from
/// <see cref="HeirGenetics.HeirGeneticsProfile"/>, not duplicated.
/// </summary>
public static class PipelineProfile
{
    // ---- class years and declaration ----

    /// <summary>A College career is four class years; a class-4 senior's eligibility ends after it.</summary>
    public const int FinalClassYear = 4;

    /// <summary>The boot ensure's map: class_year = clamp(age − this, 1, 4) (Amendment 3).</summary>
    public const int BootClassYearAgeOffset = 17;

    /// <summary>Draft eligibility: after the sophomore season (class year ≥ 2).</summary>
    public const int DraftEligibleClassYear = 2;

    /// <summary>NPCs at this class year or later always declare (Amendment 3: keeps the transient pool ≥ 128).</summary>
    public const int AutoDeclareClassYear = 3;

    /// <summary>NPCs at this age or older always declare (the spec's rule; identical to class 3 in steady state).</summary>
    public const int AutoDeclareAge = 22;

    /// <summary>An eligible NPC projected inside this slot declares (the top two rounds).</summary>
    public const int DeclareProjectedSlotMax = 64;

    // ---- recruiting and intake (Amendment 2) ----

    /// <summary>HS intake is age 16 only, so every NPC gets exactly four HS seasons (16 → 19).</summary>
    public const int IntakeMinAge = 16;
    public const int IntakeAgeSpan = 1;

    public const int OutsideRecruitAge = 19;

    /// <summary>Outside recruits' potential bell is centred this far below the world's 50.</summary>
    public const int OutsideRecruitMeanOffset = -2;

    /// <summary>The generation age outside recruits' current ratings are discounted at (≈ an HS graduate's four development passes).</summary>
    public const int OutsideRecruitDiscountAge = 25;

    /// <summary>Outside recruits rolled beyond the league deficit, per role, so weak HS seniors can go unrecruited.</summary>
    public const int OutsideRecruitSurplusBatters = 15;
    public const int OutsideRecruitSurplusStarters = 5;
    public const int OutsideRecruitSurplusRelievers = 5;

    public static int OutsideRecruitSurplusFor(PitcherRole role) => role switch
    {
        PitcherRole.None => OutsideRecruitSurplusBatters,
        PitcherRole.Starter => OutsideRecruitSurplusStarters,
        _ => OutsideRecruitSurplusRelievers,
    };

    // ---- the avatar's offers (Amendment 4) ----

    /// <summary>F(r) = this − <see cref="OfferRankSlope"/>·(r − 1): the value a program at national rank r needs.</summary>
    public const double OfferTopProgramFloor = 111.5;
    public const double OfferRankSlope = 1.0;
    public const int MaxAvatarOffers = 5;

    /// <summary>A program missing a field position takes the best recruit at it within this many value points of the best overall.</summary>
    public const double RecruitPositionNeedMargin = 4.0;

    // ---- roster targets ----

    /// <summary>College programs recruit back to this role shape (world-gen's 15/4/5; caps 17/4/5, org 26, leave room).</summary>
    public const int CollegeTargetBatters = 15;
    public const int CollegeTargetStarters = 4;
    public const int CollegeTargetRelievers = 5;

    /// <summary>The College hard caps (spec §5.1), checked by the conservation gate.</summary>
    public const int CollegeCapBatters = 17;
    public const int CollegeCapStarters = 4;
    public const int CollegeCapRelievers = 5;
    public const int CollegeOrgCap = 26;

    /// <summary>The Pro shape: exactly 15/4/5 = 24 after releases; free agency signs only below it.</summary>
    public const int ProCapBatters = 15;
    public const int ProCapStarters = 4;
    public const int ProCapRelievers = 5;
    public const int ProOrgCap = ProCapBatters + ProCapStarters + ProCapRelievers;

    public static int CollegeTargetFor(PitcherRole role) => role switch
    {
        PitcherRole.None => CollegeTargetBatters,
        PitcherRole.Starter => CollegeTargetStarters,
        _ => CollegeTargetRelievers,
    };

    public static int ProCapFor(PitcherRole role) => role switch
    {
        PitcherRole.None => ProCapBatters,
        PitcherRole.Starter => ProCapStarters,
        _ => ProCapRelievers,
    };

    // ---- the Draft (Amendment 5) ----

    public const int DraftRounds = 4;
    public const double DraftRoleNeedMargin = 3.0;

    /// <summary>The 3/1/1 role quota across a team's four picks (it sums to five, so a team takes 3+1, 3+1 or 2+1+1).</summary>
    public const int DraftRoleQuotaBatters = 3;
    public const int DraftRoleQuotaStarters = 1;
    public const int DraftRoleQuotaRelievers = 1;

    public static int DraftRoleQuotaFor(PitcherRole role) => role switch
    {
        PitcherRole.None => DraftRoleQuotaBatters,
        PitcherRole.Starter => DraftRoleQuotaStarters,
        _ => DraftRoleQuotaRelievers,
    };

    /// <summary>The avatar's signing bonus: each round's top value and per-pick slope ($).</summary>
    public const int Round1TopBonus = 50_000;
    public const int Round1PickSlope = 900;
    public const int Round2TopBonus = 20_000;
    public const int Round2PickSlope = 250;
    public const int Round3TopBonus = 11_500;
    public const int Round3PickSlope = 175;
    public const int Round4TopBonus = 5_800;
    public const int Round4PickSlope = 120;

    /// <summary>D6-A, Amendment 6: Combine points docked from the avatar's value while record_arrest is active.</summary>
    public const double RecordArrestPenalty = 1.0;

    // ---- free agency (Amendment 6) ----

    public const int FreeAgentRetireAge = 30;
    public const int FreeAgentMaxUnsignedSeasons = 2;

    /// <summary>A free agent signed at a lock is valued at this neutral performance (no stat-table scan at a lock).</summary>
    public const double NeutralPerformance = 100.0;

    // ---- the takeover (D4b-C) ----

    /// <summary>A willing child takes over the career at this age, as an HS freshman.</summary>
    public const int TakeoverAge = 16;

    // ---- streams (Q8): each is MixCombine(MixCombine(seed, tag), year) | 1 ----

    public const ulong OutsideRecruitsTag = 0x5049_5045_4F55_5452UL;          // "PIPEOUTR"
    public const ulong OutsideRecruitsFieldingTag = 0x5049_5045_4F52_464CUL;  // "PIPEORFL"
    public const ulong HsIntakeTag = 0x5049_5045_4853_494EUL;                 // "PIPEHSIN"
    public const ulong HsIntakeFieldingTag = 0x5049_5045_4853_464CUL;         // "PIPEHSFL"
    public const ulong InauguralDraftOrderTag = 0x4452_4146_544F_5244UL;      // "DRAFTORD"

    /// <summary>Harness fixtures only: the live game writes pipeline_seed from its boot draw.</summary>
    public const ulong DefaultPipelineSeed = 0x136D_5EED_0000_0001UL;

    // ---- the PipelineHarness gradient gate (Q7, Amendment 7) ----

    public const int GradientBurnInSeasons = 5;
    public const int GradientWindowSeasons = 15;
    public const double GradientStationarityMaxDrift = 10.0;
    public const double MinBatterHsToCollegeMargin = 3.0;
    public const double MinBatterCollegeToProMargin = 10.0;
    public const double MinPitcherHsToCollegeMargin = 2.0;
    public const double MinPitcherCollegeToProMargin = 10.0;
}

/// <summary>
/// The pipeline's calendar days (D2-A), each a derived rule in
/// <see cref="TierSeasonCalendar"/> style, never a literal, and each a
/// non-Sunday. Day-of-season values in the fixed 365-day year (day 1 = Monday).
/// </summary>
public static class PipelineCalendar
{
    /// <summary>The Monday after The Red Dirt Series' last possible day (174): day 176.</summary>
    public static readonly int DeclarationOpenDay;

    /// <summary>The first Monday of July: day 183.</summary>
    public static readonly int DeclarationDeadlineDay;

    /// <summary>The second Saturday of July (<see cref="TierSeasonCalendar.DraftDay"/>): day 195.</summary>
    public static readonly int DraftDay;

    /// <summary>The first Monday of October: day 274.</summary>
    public static readonly int OffersDay;

    /// <summary>The second Wednesday of November: day 318.</summary>
    public static readonly int SigningDay;

    static PipelineCalendar()
    {
        DeclarationOpenDay = FirstOnOrAfter(PostseasonCalendar.LastDay(LeagueTier.College) + 1, Weekday.Monday);
        DeclarationDeadlineDay = FirstOnOrAfter(GameCalendar.DayOfSeasonFor(7, 1), Weekday.Monday);
        DraftDay = TierSeasonCalendar.DraftDay;
        OffersDay = FirstOnOrAfter(GameCalendar.DayOfSeasonFor(10, 1), Weekday.Monday);
        SigningDay = FirstOnOrAfter(GameCalendar.DayOfSeasonFor(11, 1), Weekday.Wednesday) + GameCalendar.DaysPerWeek;
    }

    private static int FirstOnOrAfter(int dayOfSeason, Weekday weekday)
    {
        int day = dayOfSeason;
        while (GameCalendar.WeekdayForDayOfSeason(day) != weekday)
        {
            day++;
        }
        return day;
    }
}

/// <summary>One recruit or draft-board entry the pure rules rank.</summary>
public struct PipelineChoice
{
    public string PlayerId;
    public PitcherRole Role;
    public FieldingPosition Position;
    public double Value;
    public bool Taken;
}

/// <summary>
/// The pipeline's pure rules (brief §4 item 1): value, offers, the walk-on,
/// projected slot and round, declaration, the draft pick, draft order,
/// releases, the free-agent retirement test and the signing bonus. No RNG, no
/// database: the harness pins fixtures against them. Every tie breaks on
/// player_id (or team_id) ascending.
/// </summary>
public static class PipelineRules
{
    public static readonly PitcherRole[] Roles = { PitcherRole.None, PitcherRole.Starter, PitcherRole.Reliever };

    /// <summary>
    /// The scouting sum (Amendment 1): relievers are credited
    /// <see cref="LeagueGenerator.RelieverStaminaPenalty"/> stamina back,
    /// clamped at 100, so all three roles share the 150 mean on a cross-role
    /// board. Within-role order is unchanged.
    /// </summary>
    public static int RoleRatingSum(in RosterPlayerRow row) => row.Role switch
    {
        PitcherRole.None => row.BatPower + row.BatContact + row.BatDiscipline,
        PitcherRole.Starter => row.PitStuff + row.PitControl + row.PitStamina,
        PitcherRole.Reliever => row.PitStuff + row.PitControl
            + Math.Min(100, row.PitStamina + LeagueGenerator.RelieverStaminaPenalty),
        _ => row.BatPower + row.BatContact + row.BatDiscipline,
    };

    /// <summary>The raw three-tool sum (the gradient metric, 9d §7): no reliever compensation.</summary>
    public static int RawRoleSum(in RosterPlayerRow row) => row.Role == PitcherRole.None
        ? row.BatPower + row.BatContact + row.BatDiscipline
        : row.PitStuff + row.PitControl + row.PitStamina;

    /// <summary>Summed role headroom, or 0 with no potential row (the v10 backfill semantics).</summary>
    public static int Headroom(in PipelineCandidateRow c) =>
        c.HasPotential ? PromotionScore.Headroom(in c.Ratings, in c.Potential, c.Ratings.Role != PitcherRole.None) : 0;

    /// <summary>
    /// The pipeline's value: <see cref="PromotionScore.Combine"/> of the
    /// tier-relative, tier-shrunk performance (or <paramref name="neutralPerformance"/>
    /// read as P = 100) and the headroom-aware scouting on
    /// <see cref="RoleRatingSum"/>, less the record dock when it applies.
    /// </summary>
    public static double Value(
        in PipelineCandidateRow c, LeagueTier shrinkTier, double leagueOps, double leagueEra,
        bool neutralPerformance, bool recordDock)
    {
        double p;
        if (neutralPerformance)
        {
            p = PipelineProfile.NeutralPerformance;
        }
        else if (c.Ratings.Role != PitcherRole.None)
        {
            p = PromotionScore.PitcherPerformance(
                c.Pitching.OutsRecorded, c.Pitching.Er, leagueEra, PromotionProfile.PitcherIpShrinkFor(shrinkTier));
        }
        else
        {
            p = PromotionScore.BatterPerformance(
                in c.Batting, leagueOps, PromotionProfile.BatterPaShrinkFor(shrinkTier));
        }
        double s = PromotionScore.Scouting(RoleRatingSum(in c.Ratings), c.Age, Headroom(in c));
        double v = PromotionScore.Combine(p, s);
        return recordDock ? v - PipelineProfile.RecordArrestPenalty : v;
    }

    // ------------------------------------------------------------------
    // Recruiting
    // ------------------------------------------------------------------

    /// <summary>The value a program at national rank <paramref name="rank"/> (1 = best) needs to offer the avatar.</summary>
    public static double OfferFloor(int rank) =>
        PipelineProfile.OfferTopProgramFloor - PipelineProfile.OfferRankSlope * (rank - 1);

    /// <summary>One College program as the offer rules see it.</summary>
    public readonly record struct Program(int TeamId, int Rank, int OpenSpots);

    /// <summary>
    /// The avatar's offers (Amendment 4): every program with an open spot in
    /// his role whose floor he clears, best-ranked first, up to five. With
    /// none, one walk-on from the lowest-ranked program with an open spot;
    /// only if no program has one, from the lowest-ranked program overall,
    /// where he displaces its weakest same-role returning player at the
    /// rollover (<paramref name="displaces"/>).
    /// </summary>
    public static void SelectAvatarOffers(
        double value, IReadOnlyList<Program> programs, List<int> offerTeamIds, out bool isWalkOn, out bool displaces)
    {
        offerTeamIds.Clear();
        isWalkOn = false;
        displaces = false;
        var ordered = new List<Program>(programs);
        ordered.Sort(static (a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.TeamId.CompareTo(b.TeamId));
        foreach (Program program in ordered)
        {
            if (program.OpenSpots > 0 && value >= OfferFloor(program.Rank))
            {
                offerTeamIds.Add(program.TeamId);
                if (offerTeamIds.Count == PipelineProfile.MaxAvatarOffers)
                {
                    return;
                }
            }
        }
        if (offerTeamIds.Count > 0 || ordered.Count == 0)
        {
            return;
        }
        isWalkOn = true;
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].OpenSpots > 0)
            {
                offerTeamIds.Add(ordered[i].TeamId);
                return;
            }
        }
        offerTeamIds.Add(ordered[^1].TeamId);
        displaces = true;
    }

    /// <summary>
    /// A program's most-needed role: the largest fractional deficit
    /// (open / target), then the larger open count, then None → Starter →
    /// Reliever. Roles whose pool is empty are skipped. Returns false when no
    /// fillable role is open.
    /// </summary>
    public static bool TryMostNeededRole(
        ReadOnlySpan<int> openByRole, ReadOnlySpan<int> targetByRole, ReadOnlySpan<bool> fillable, out PitcherRole role)
    {
        role = PitcherRole.None;
        int best = -1;
        for (int r = 0; r < Roles.Length; r++)
        {
            if (openByRole[r] <= 0 || !fillable[r])
            {
                continue;
            }
            if (best < 0)
            {
                best = r;
                continue;
            }
            // open[r]/target[r] > open[best]/target[best], cross-multiplied (exact in integers).
            long lhs = (long)openByRole[r] * targetByRole[best];
            long rhs = (long)openByRole[best] * targetByRole[r];
            if (lhs > rhs || (lhs == rhs && openByRole[r] > openByRole[best]))
            {
                best = r;
            }
        }
        if (best < 0)
        {
            return false;
        }
        role = Roles[best];
        return true;
    }

    /// <summary>
    /// The recruit a program signs for <paramref name="role"/> from a pool
    /// sorted best-first: the best available, except that a batter pick for
    /// a team missing a field position takes the best available at that
    /// position when he is within <see cref="PipelineProfile.RecruitPositionNeedMargin"/>
    /// (Amendment 4). Positions are tried C → RF. Returns −1 when the role's
    /// pool is empty.
    /// </summary>
    public static int PickRecruit(List<PipelineChoice> sortedPool, PitcherRole role, ReadOnlySpan<bool> coveredPositions)
    {
        int best = -1;
        for (int i = 0; i < sortedPool.Count; i++)
        {
            if (!sortedPool[i].Taken && sortedPool[i].Role == role)
            {
                best = i;
                break;
            }
        }
        if (best < 0 || role != PitcherRole.None)
        {
            return best;
        }
        foreach (FieldingPosition position in FieldingPositions.Field)
        {
            if (coveredPositions[(int)position])
            {
                continue;
            }
            for (int i = best; i < sortedPool.Count; i++)
            {
                PipelineChoice c = sortedPool[i];
                if (c.Taken || c.Role != PitcherRole.None || c.Position != position)
                {
                    continue;
                }
                if (c.Value >= sortedPool[best].Value - PipelineProfile.RecruitPositionNeedMargin)
                {
                    return i;
                }
                break;
            }
        }
        return best;
    }

    /// <summary>Best first: value descending, then player_id ascending (ordinal).</summary>
    public static int CompareChoice(PipelineChoice a, PipelineChoice b)
    {
        int byValue = b.Value.CompareTo(a.Value);
        return byValue != 0 ? byValue : string.CompareOrdinal(a.PlayerId, b.PlayerId);
    }

    // ------------------------------------------------------------------
    // The Draft
    // ------------------------------------------------------------------

    /// <summary>1–4 for a projected slot inside the 128 picks, 0 when projected undrafted (or ineligible, slot 0).</summary>
    public static int ProjectedRound(int projectedSlot, int picksPerRound) =>
        projectedSlot >= 1 && projectedSlot <= picksPerRound * PipelineProfile.DraftRounds
            ? (projectedSlot - 1) / picksPerRound + 1
            : 0;

    /// <summary>
    /// The NPC declaration rule (Amendment 3): eligible (class ≥ 2) and either
    /// a junior or senior, 22 or older, or projected in the top 64. The class
    /// term is identical to the age term in steady state and keeps the
    /// world-gen transient's pool above 128.
    /// </summary>
    public static bool ShouldNpcDeclare(int classYear, int age, int projectedSlot) =>
        classYear >= PipelineProfile.DraftEligibleClassYear
        && (classYear >= PipelineProfile.AutoDeclareClassYear
            || age >= PipelineProfile.AutoDeclareAge
            || (projectedSlot >= 1 && projectedSlot <= PipelineProfile.DeclareProjectedSlotMax));

    /// <summary>
    /// One pick (Amendment 5) from a board sorted best-first. A role short of
    /// its cap after known retirements may take its best within
    /// <see cref="PipelineProfile.DraftRoleNeedMargin"/> of the best overall
    /// (largest deficit first); otherwise, when the best overall's role has
    /// met its 3/1/1 quota, a role under quota may take its best within the
    /// margin; otherwise the best overall. Returns −1 on an empty board.
    /// </summary>
    public static int SelectDraftPick(List<PipelineChoice> sortedBoard, ReadOnlySpan<int> teamRoleCount, ReadOnlySpan<int> draftedInRole)
    {
        int best = -1;
        for (int i = 0; i < sortedBoard.Count; i++)
        {
            if (!sortedBoard[i].Taken)
            {
                best = i;
                break;
            }
        }
        if (best < 0)
        {
            return -1;
        }
        double floor = sortedBoard[best].Value - PipelineProfile.DraftRoleNeedMargin;

        // 1. An immediate cap deficit.
        int needPick = -1;
        int needDeficit = 0;
        for (int r = 0; r < Roles.Length; r++)
        {
            int deficit = PipelineProfile.ProCapFor(Roles[r]) - teamRoleCount[r];
            if (deficit <= 0)
            {
                continue;
            }
            int candidate = BestOfRole(sortedBoard, Roles[r], best, floor);
            if (candidate < 0)
            {
                continue;
            }
            if (needPick < 0 || deficit > needDeficit
                || (deficit == needDeficit && CompareChoice(sortedBoard[candidate], sortedBoard[needPick]) < 0))
            {
                needPick = candidate;
                needDeficit = deficit;
            }
        }
        if (needPick >= 0)
        {
            return needPick;
        }

        // 2. The 3/1/1 quota.
        int bestRole = Array.IndexOf(Roles, sortedBoard[best].Role);
        if (draftedInRole[bestRole] >= PipelineProfile.DraftRoleQuotaFor(sortedBoard[best].Role))
        {
            int quotaPick = -1;
            for (int r = 0; r < Roles.Length; r++)
            {
                if (draftedInRole[r] >= PipelineProfile.DraftRoleQuotaFor(Roles[r]))
                {
                    continue;
                }
                int candidate = BestOfRole(sortedBoard, Roles[r], best, floor);
                if (candidate >= 0 && (quotaPick < 0 || CompareChoice(sortedBoard[candidate], sortedBoard[quotaPick]) < 0))
                {
                    quotaPick = candidate;
                }
            }
            if (quotaPick >= 0)
            {
                return quotaPick;
            }
        }

        // 3. Best player available.
        return best;
    }

    private static int BestOfRole(List<PipelineChoice> sortedBoard, PitcherRole role, int from, double floor)
    {
        for (int i = from; i < sortedBoard.Count; i++)
        {
            PipelineChoice c = sortedBoard[i];
            if (c.Value < floor)
            {
                return -1;
            }
            if (!c.Taken && c.Role == role)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>The inaugural draft order's key (Argon Q5): ascending picks first.</summary>
    public static ulong InauguralDraftKey(ulong pipelineSeed, int seasonYear, int teamId) =>
        PostseasonStreams.MixCombine(
            PostseasonStreams.MixCombine(pipelineSeed, PipelineProfile.InauguralDraftOrderTag),
            PostseasonStreams.MixCombine(unchecked((ulong)seasonYear), unchecked((ulong)teamId)));

    /// <summary>The seeded inaugural order: (key ↑, team_id ↑).</summary>
    public static List<int> InauguralDraftOrder(IReadOnlyList<int> proTeamIds, ulong pipelineSeed, int seasonYear)
    {
        var order = new List<int>(proTeamIds);
        order.Sort((a, b) =>
        {
            int byKey = InauguralDraftKey(pipelineSeed, seasonYear, a).CompareTo(InauguralDraftKey(pipelineSeed, seasonYear, b));
            return byKey != 0 ? byKey : a.CompareTo(b);
        });
        return order;
    }

    /// <summary>
    /// The prior-season order (spec §9.3, R-1 Q09): reverse elimination round
    /// (non-playoff teams first, the champion last), then reverse standings
    /// within a round. <paramref name="standingsBestFirst"/> is the full
    /// <see cref="Standings.Order"/>; <paramref name="bucketByTeam"/> is 0 for
    /// a non-playoff team, the round it lost in for a playoff team, and the
    /// final round + 1 for the champion.
    /// </summary>
    public static List<int> PriorSeasonDraftOrder(IReadOnlyList<int> standingsBestFirst, IReadOnlyDictionary<int, int> bucketByTeam)
    {
        var standingIndex = new Dictionary<int, int>(standingsBestFirst.Count);
        for (int i = 0; i < standingsBestFirst.Count; i++)
        {
            standingIndex[standingsBestFirst[i]] = i;
        }
        var order = new List<int>(standingsBestFirst);
        order.Sort((a, b) =>
        {
            int byBucket = bucketByTeam[a].CompareTo(bucketByTeam[b]);
            return byBucket != 0 ? byBucket : standingIndex[b].CompareTo(standingIndex[a]);
        });
        return order;
    }

    /// <summary>
    /// The table-only standings fallback (unit fixtures with Team_Seasons rows
    /// but no game rows), best first: win pct, run differential, fewer runs
    /// allowed, then the standings coin (ascending wins), then team_id.
    /// </summary>
    public static List<int> TableOnlyStandings(IReadOnlyList<TeamSeasonRow> rows, ulong scheduleSeed, int seasonYear, LeagueTier tier)
    {
        var sorted = new List<TeamSeasonRow>(rows);
        sorted.Sort((a, b) =>
        {
            // Win pct (an empty record reads .500); equal fractions give equal doubles.
            int ga = a.Wins + a.Losses;
            int gb = b.Wins + b.Losses;
            double pa = ga > 0 ? (double)a.Wins / ga : 0.5;
            double pb = gb > 0 ? (double)b.Wins / gb : 0.5;
            int byPct = pb.CompareTo(pa);
            if (byPct != 0)
            {
                return byPct;
            }
            int byDiff = (b.RunsScored - b.RunsAllowed).CompareTo(a.RunsScored - a.RunsAllowed);
            if (byDiff != 0)
            {
                return byDiff;
            }
            int byAllowed = a.RunsAllowed.CompareTo(b.RunsAllowed);
            if (byAllowed != 0)
            {
                return byAllowed;
            }
            int byCoin = PostseasonStreams.StandingsCoinKey(scheduleSeed, seasonYear, tier, a.TeamId)
                .CompareTo(PostseasonStreams.StandingsCoinKey(scheduleSeed, seasonYear, tier, b.TeamId));
            return byCoin != 0 ? byCoin : a.TeamId.CompareTo(b.TeamId);
        });
        var order = new List<int>(sorted.Count);
        foreach (TeamSeasonRow row in sorted)
        {
            order.Add(row.TeamId);
        }
        return order;
    }

    /// <summary>
    /// The avatar's signing bonus by overall pick (Amendment 5): piecewise
    /// linear by round, $50,000 at pick 1 down to $2,080 at pick 128; 0
    /// outside the draft.
    /// </summary>
    public static int SigningBonusForPick(int overallPick) => overallPick switch
    {
        >= 1 and <= 32 => PipelineProfile.Round1TopBonus - PipelineProfile.Round1PickSlope * (overallPick - 1),
        >= 33 and <= 64 => PipelineProfile.Round2TopBonus - PipelineProfile.Round2PickSlope * (overallPick - 33),
        >= 65 and <= 96 => PipelineProfile.Round3TopBonus - PipelineProfile.Round3PickSlope * (overallPick - 65),
        >= 97 and <= 128 => PipelineProfile.Round4TopBonus - PipelineProfile.Round4PickSlope * (overallPick - 97),
        _ => 0,
    };

    // ------------------------------------------------------------------
    // Pro turnover
    // ------------------------------------------------------------------

    /// <summary>
    /// One role's release set for a Pro team: rank by value (↓, player_id ↑),
    /// keep <paramref name="keep"/>, release the rest (D4b-C: the avatar is
    /// ranked like anyone else). Appends the released ids, lowest value first.
    /// </summary>
    public static void SelectReleases(List<PipelineChoice> group, int keep, List<string> released)
    {
        if (group.Count <= keep)
        {
            return;
        }
        group.Sort(CompareChoice);
        for (int i = group.Count - 1; i >= keep; i--)
        {
            released.Add(group[i].PlayerId);
        }
    }

    /// <summary>
    /// The free-agent retirement test at the rollover into
    /// <paramref name="newSeasonYear"/> (Amendment 6): 30 or older, health
    /// at the floor, or two full seasons unsigned (released into Y: kept at
    /// Y+1, retired at Y+2).
    /// </summary>
    public static bool ShouldRetireFreeAgent(int age, int healthCeiling, int newSeasonYear, int seasonEnteredFreeAgency) =>
        age >= PipelineProfile.FreeAgentRetireAge
        || healthCeiling <= HeirGenetics.HeirGeneticsProfile.HealthRetirementFloor
        || newSeasonYear - seasonEnteredFreeAgency >= PipelineProfile.FreeAgentMaxUnsignedSeasons;

    /// <summary>The shipped rostered-retirement triggers (age 42, health 40), read after aging.</summary>
    public static bool ShouldRetireRostered(int age, int healthCeiling) =>
        age >= HeirGenetics.HeirGeneticsProfile.MandatoryRetirementAge
        || healthCeiling <= HeirGenetics.HeirGeneticsProfile.HealthRetirementFloor;

    /// <summary>The same test one aging ahead: a player who will retire at the next rollover (a known departure).</summary>
    public static bool RetiresAtNextRollover(int age, int healthCeiling) => ShouldRetireRostered(age + 1, healthCeiling);

    /// <summary>A derived stream (Q8): never held across days, so a reload reproduces it.</summary>
    public static RngState Stream(ulong pipelineSeed, ulong tag, int seasonYear) =>
        new(PostseasonStreams.MixCombine(PostseasonStreams.MixCombine(pipelineSeed, tag), unchecked((ulong)seasonYear)) | 1UL);
}
