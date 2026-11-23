using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>Which active-designation group a <see cref="DesignationCandidate"/> competes in.</summary>
public enum DesignationRole : byte
{
    PositionPlayer = 0,
    Starter = 1,
    Reliever = 2,
}

/// <summary>
/// One rostered player as the Sunday lock scores him (slice 13-5, brief D4).
/// Every input is a pure function of persisted state: ratings as the sims
/// bake them, absences from the <see cref="AvailabilityLedger"/>, and for the
/// avatar the ΔP + ΔA terms <see cref="RosterRules"/> computes from his
/// stats and person row.
/// </summary>
public struct DesignationCandidate
{
    /// <summary>The caller's index for this player (a roster slot); echoed back, never interpreted.</summary>
    public int Slot;
    public string PlayerId;

    /// <summary>Raw power + contact + discipline: <see cref="LineupBuilder"/>'s bat score (position players).</summary>
    public int BatScore;
    public FieldingPosition? Primary;
    public FieldingPosition? Secondary;

    /// <summary>Baked stuff + control (<see cref="StarterSelection.Quality"/>, brief §10.5 C3) and baked stamina (pitchers).</summary>
    public int Quality;
    public int Stamina;

    /// <summary>The team's game days in the locked week on which this player is absent (C1).</summary>
    public int MissedGames;

    /// <summary>The team's game days in the locked week (0 = no game that week: everyone counts as healthy).</summary>
    public int TeamGameDays;

    /// <summary>The tracked absence's end (exclusive); the unhealthy fill's first key.</summary>
    public long UntilDay;

    /// <summary>The injury-rust penalty on the player's first available game day of the week (0 = none).</summary>
    public byte Rust;

    public bool IsAvatar;

    /// <summary>The avatar's ΔP + ΔA (0 for everyone else); ΔH is derived from <see cref="MissedGames"/> and <see cref="Rust"/>.</summary>
    public double AvatarDelta;

    /// <summary>Available on every team game day of the locked week (C1).</summary>
    public readonly bool IsHealthy => MissedGames == 0;

    /// <summary>The avatar misses some but not all of the week's games: he competes at the margin with ΔH.</summary>
    public readonly bool IsMarginAvatar => IsAvatar && MissedGames > 0 && MissedGames < TeamGameDays;

    public readonly LineupCandidate AsLineupCandidate() => new(Slot, PlayerId, BatScore, Primary, Secondary);
}

/// <summary>
/// The pure 13-5 roster rules (brief D2e, D4, §10.1 Q2–Q4, §10.5 C1–C3):
/// <see cref="CanField"/>, the forfeit score, and the Sunday lock's
/// designation selector with its tie and fill keys. Allocation-free and
/// RNG-free: every tie breaks by player id ordinal ascending, so the same
/// save always yields the same designations. Every constant is a Math data
/// value (Gemini 4 Argon, brief §10.1); tuning one is a data edit gated by
/// run_monte_carlo_batch.
/// </summary>
public static class RosterRules
{
    // ---- active targets (spec §5.1 Pro: 12 position players, 3 SP, 3 RP) ----
    public const int ActivePositionPlayers = 12;
    public const int ActiveStarters = 3;
    public const int ActiveRelievers = 3;

    /// <summary>Pitchers auto-active by rank before the slot-3 margin (Q3 core protection).</summary>
    public const int ProtectedPitchers = 2;

    // ---- Q2 bench score ----
    public const int BackupCatcherCoverageWeight = 24;
    public const int MiddleInfieldCoverageWeight = 18;
    public const int OutfieldCoverageWeight = 15;
    public const int SecondaryPositionBonus = 6;
    public const double RustBatterMultiplier = 3.0;

    // ---- Q3 avatar margin (C1: per missed game, not per calendar day) ----
    public const double MissedGamePenalty = 12.0;
    public const double RustPitcherMultiplier = 2.0;
    public const int PerfMinPa = 20;
    public const int PerfPaShrink = 60;
    public const int PerfMinOuts = 18;
    public const int PerfOutsShrink = 54;
    public const double PerfRelativeClamp = 0.50;
    public const double PerfMaxDelta = 18.0;
    public const double AffinityWorkEthicWeight = 0.12;
    public const double AffinityDisciplineWeight = 0.10;
    public const double AffinityTeamworkWeight = 0.08;

    /// <summary>The neutral person value: every affinity input at 50 scores exactly 0.</summary>
    public const int AffinityNeutral = 50;

    // ---- the lock window (D6-A): Sunday before Pro's opener through the last postseason Sunday ----

    /// <summary>The first lock day of a season (91: the Sunday before Pro's opening day 93).</summary>
    public static readonly int FirstLockDayOfSeason = SundayOnOrBefore(TierSeasonCalendar.GameDay(LeagueTier.Pro, 1));

    /// <summary>The last lock day (273: the last Sunday on or before Pro's final slot, 275).</summary>
    public static readonly int LastLockDayOfSeason = SundayOnOrBefore(PostseasonCalendar.LastDay(LeagueTier.Pro));

    /// <summary>The last day a lock can still cover (279), so the last one a boot reconcile repairs.</summary>
    public static int LastCoveredDayOfSeason => LastLockDayOfSeason + GameCalendar.DaysPerWeek - 1;

    /// <summary>True on a Sunday inside the lock window.</summary>
    public static bool IsLockDay(int dayOfSeason) =>
        dayOfSeason % GameCalendar.DaysPerWeek == 0
        && dayOfSeason >= FirstLockDayOfSeason && dayOfSeason <= LastLockDayOfSeason;

    /// <summary>The Sunday on or before the day (the nominal lock day a boot reconcile uses).</summary>
    public static int SundayOnOrBefore(int dayOfSeason) => dayOfSeason - dayOfSeason % GameCalendar.DaysPerWeek;

    // ------------------------------------------------------------------
    // CanField and the forfeit score (D2)
    // ------------------------------------------------------------------

    /// <summary>
    /// The one fielding rule (D2e, spec §4): at least nine eligible position
    /// players and at least one legal pitcher. Eligible = available ∧ active;
    /// legal = an eligible SP or RP who, inside The Red Dirt Series' window,
    /// hasn't started or opened in it. Each sim counts from its own slot caches.
    /// </summary>
    public static bool CanField(int eligiblePositionPlayers, int legalPitchers) =>
        eligiblePositionPlayers >= RosterCapacity.Lineup && legalPitchers >= 1;

    /// <summary>
    /// The forfeit score (D2a, D2d-A): <c>RegulationInnings(tier)</c>–0 to the
    /// side that could field. When neither could, <paramref name="homeAwardedOnDouble"/>
    /// decides: the home team in the regular season, the high seed in the
    /// postseason. Returns the winner's side (true = home). Draws nothing.
    /// </summary>
    public static bool ForfeitScore(
        LeagueTier tier, bool homeCannotField, bool awayCannotField, bool homeAwardedOnDouble,
        out int homeScore, out int awayScore)
    {
        if (!homeCannotField && !awayCannotField)
        {
            throw new InvalidOperationException("ForfeitScore needs at least one side that can't field.");
        }
        int runs = TierRules.RegulationInnings(tier);
        bool homeWins = homeCannotField && awayCannotField ? homeAwardedOnDouble : awayCannotField;
        homeScore = homeWins ? runs : 0;
        awayScore = homeWins ? 0 : runs;
        return homeWins;
    }

    /// <summary>The <see cref="DirtAndDiamonds.GameForfeitedEvent"/> mask for the sides that couldn't field.</summary>
    public static byte ForfeitMask(bool homeCannotField, bool awayCannotField) =>
        (byte)((homeCannotField ? GameForfeitedEvent.HomeForfeited : 0)
            | (awayCannotField ? GameForfeitedEvent.AwayForfeited : 0));

    // ------------------------------------------------------------------
    // The avatar's margin inputs (Q3)
    // ------------------------------------------------------------------

    /// <summary>OPS from counting stats, the normalizer's formula: (H+BB)/(AB+BB) + TB/AB (0 when undefined).</summary>
    public static double Ops(long ab, long h, long doubles, long triples, long hr, long bb)
    {
        double obp = ab + bb > 0 ? (double)(h + bb) / (ab + bb) : 0.0;
        double slg = ab > 0 ? (double)(h + doubles + 2 * triples + 3 * hr) / ab : 0.0;
        return obp + slg;
    }

    /// <summary>ΔP for a batter: 18 · PA/(PA+60) · clamp((OPS − OPS_tier)/(0.5·OPS_tier), ±1); 0 under 20 PA.</summary>
    public static double BattingPerformanceDelta(int pa, double ops, double tierOps)
    {
        if (pa < PerfMinPa || tierOps <= 1e-6)
        {
            return 0.0;
        }
        double weight = (double)pa / (pa + PerfPaShrink);
        double relative = Math.Clamp((ops - tierOps) / (PerfRelativeClamp * tierOps), -1.0, 1.0);
        return PerfMaxDelta * weight * relative;
    }

    /// <summary>ΔP for a pitcher: 18 · outs/(outs+54) · clamp((ERA_tier − ERA)/(0.5·ERA_tier), ±1); 0 under 18 outs.</summary>
    public static double PitchingPerformanceDelta(long outs, long er, long tierOuts, long tierEr)
    {
        if (outs < PerfMinOuts || tierOuts <= 0)
        {
            return 0.0;
        }
        double tierEra = 27.0 * tierEr / tierOuts;
        if (tierEra <= 1e-6)
        {
            return 0.0;
        }
        double era = 27.0 * er / outs;
        double weight = (double)outs / (outs + PerfOutsShrink);
        double relative = Math.Clamp((tierEra - era) / (PerfRelativeClamp * tierEra), -1.0, 1.0);
        return PerfMaxDelta * weight * relative;
    }

    /// <summary>ΔA, the coach-affinity proxy (D4c-A): 0.12·(work ethic−50) + 0.10·(discipline−50) + 0.08·(teamwork−50).</summary>
    public static double AffinityDelta(int workEthic, int discipline, int teamwork) =>
        AffinityWorkEthicWeight * (workEthic - AffinityNeutral)
        + AffinityDisciplineWeight * (discipline - AffinityNeutral)
        + AffinityTeamworkWeight * (teamwork - AffinityNeutral);

    /// <summary>ΔH (C1): −12 per missed game, minus the role's rust multiplier × rust.</summary>
    public static double HealthDelta(in DesignationCandidate c, DesignationRole role) =>
        -MissedGamePenalty * c.MissedGames
        - (role == DesignationRole.PositionPlayer ? RustBatterMultiplier : RustPitcherMultiplier) * c.Rust;

    // ------------------------------------------------------------------
    // The designation selector (D4a-A / D4b-A)
    // ------------------------------------------------------------------

    /// <summary>
    /// Position players: <paramref name="ordered"/> is the team's position
    /// players in <see cref="LineupBuilder"/> order (bat score descending, id
    /// ascending). Marks <paramref name="active"/> (parallel to it) for
    /// <see cref="ActivePositionPlayers"/> of them:
    /// <list type="number">
    /// <item>over the healthy (C1), the nine unfeatured <see cref="LineupBuilder"/>
    /// starters (Amendment 2; <see cref="LineupBuilder.BuildInto"/>, which the
    /// harness proves equal to <c>Build(healthy, featuredSlot: -1)</c>);</item>
    /// <item>then three sequential Q2 bench picks over the other healthy
    /// players and a margin avatar (Q3: his unrusted bench score + ΔP + ΔH + ΔA);</item>
    /// <item>then the unhealthy fill (Q4): return day ascending, bat score
    /// descending, player id ascending.</item>
    /// </list>
    /// <paramref name="scratch"/> holds at least <c>ordered.Length</c> entries.
    /// </summary>
    public static void SelectPositionPlayers(
        ReadOnlySpan<DesignationCandidate> ordered, Span<bool> active, Span<LineupCandidate> scratch)
    {
        int n = ordered.Length;
        active[..n].Clear();
        int remaining = Math.Min(ActivePositionPlayers, n);

        // 1. The nine unfeatured starters over the healthy players.
        int healthy = 0;
        for (int i = 0; i < n; i++)
        {
            if (ordered[i].IsHealthy)
            {
                scratch[healthy++] = ordered[i].AsLineupCandidate();
            }
        }
        if (healthy >= RosterCapacity.Lineup)
        {
            Span<int> starterSlots = stackalloc int[RosterCapacity.Lineup];
            Span<sbyte> starterPositions = stackalloc sbyte[RosterCapacity.Lineup];
            LineupBuilder.BuildInto(scratch[..healthy], featuredSlot: -1, starterSlots, starterPositions);
            for (int k = 0; k < RosterCapacity.Lineup; k++)
            {
                active[IndexOfSlot(ordered, starterSlots[k])] = true;
            }
            remaining -= RosterCapacity.Lineup;
        }
        else
        {
            for (int i = 0; i < n; i++)
            {
                if (ordered[i].IsHealthy)
                {
                    active[i] = true;
                    remaining--;
                }
            }
        }

        // 2. The Q2 bench (and the Q3 margin avatar), three sequential greedy picks.
        bool coveredC = false;
        bool coveredMiddle = false;
        bool coveredOutfield = false;
        while (remaining > 0)
        {
            int best = -1;
            double bestScore = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                ref readonly DesignationCandidate c = ref ordered[i];
                if (active[i] || !(c.IsHealthy || c.IsMarginAvatar))
                {
                    continue;
                }
                double score = BenchScore(in c, coveredC, coveredMiddle, coveredOutfield);
                if (best < 0 || score > bestScore
                    || (score == bestScore && string.CompareOrdinal(c.PlayerId, ordered[best].PlayerId) < 0))
                {
                    best = i;
                    bestScore = score;
                }
            }
            if (best < 0)
            {
                break;
            }
            active[best] = true;
            remaining--;
            coveredC |= IsBackupCatcher(in ordered[best]);
            coveredMiddle |= IsMiddleInfield(in ordered[best]);
            coveredOutfield |= IsOutfield(in ordered[best]);
        }

        // 3. The unhealthy fill.
        FillUnhealthy(ordered, active, remaining, DesignationRole.PositionPlayer);
    }

    /// <summary>
    /// Starters or relievers: <paramref name="ranked"/> is the staff in the
    /// sims' <see cref="StarterSelection.CompareQuality"/> order (baked, C3).
    /// The top <see cref="ProtectedPitchers"/> healthy are active; the rest
    /// of <paramref name="activeTarget"/> goes to the best healthy (or margin
    /// avatar) by quality − 2·rust (the avatar: quality + ΔP + ΔH + ΔA), ties
    /// by stamina descending then id ascending (exactly CompareQuality when
    /// every delta is 0); then the unhealthy fill (Q4).
    /// </summary>
    public static void SelectPitchers(
        ReadOnlySpan<DesignationCandidate> ranked, int activeTarget, DesignationRole role, Span<bool> active)
    {
        int n = ranked.Length;
        active[..n].Clear();
        int remaining = Math.Min(activeTarget, n);

        int protectedCount = 0;
        for (int i = 0; i < n && protectedCount < ProtectedPitchers && remaining > 0; i++)
        {
            if (ranked[i].IsHealthy)
            {
                active[i] = true;
                protectedCount++;
                remaining--;
            }
        }

        while (remaining > 0)
        {
            int best = -1;
            double bestScore = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                ref readonly DesignationCandidate c = ref ranked[i];
                if (active[i] || !(c.IsHealthy || c.IsMarginAvatar))
                {
                    continue;
                }
                double score = PitcherMarginScore(in c, role);
                if (best < 0 || score > bestScore
                    || (score == bestScore && BeatsOnTies(in c, in ranked[best])))
                {
                    best = i;
                    bestScore = score;
                }
            }
            if (best < 0)
            {
                break;
            }
            active[best] = true;
            remaining--;
        }

        FillUnhealthy(ranked, active, remaining, role);
    }

    /// <summary>Q2's bench score (an NPC), or Q3's margin score (the avatar: unrusted, then ΔP + ΔH + ΔA).</summary>
    public static double BenchScore(in DesignationCandidate c, bool coveredC, bool coveredMiddle, bool coveredOutfield)
    {
        int coverage = 0;
        if (!coveredC && IsBackupCatcher(in c))
        {
            coverage = BackupCatcherCoverageWeight;
        }
        if (!coveredMiddle && IsMiddleInfield(in c))
        {
            coverage = Math.Max(coverage, MiddleInfieldCoverageWeight);
        }
        if (!coveredOutfield && IsOutfield(in c))
        {
            coverage = Math.Max(coverage, OutfieldCoverageWeight);
        }
        double score = c.BatScore + coverage + (c.Secondary is null ? 0 : SecondaryPositionBonus);
        // Rust counts once: inside ΔH for the avatar, as −3·rust for everyone else.
        return c.IsAvatar
            ? score + c.AvatarDelta + HealthDelta(in c, DesignationRole.PositionPlayer)
            : score - RustBatterMultiplier * c.Rust;
    }

    /// <summary>The slot-3 pitcher score: quality − 2·rust, or for the avatar quality + ΔP + ΔH + ΔA.</summary>
    public static double PitcherMarginScore(in DesignationCandidate c, DesignationRole role) =>
        c.IsAvatar
            ? c.Quality + c.AvatarDelta + HealthDelta(in c, role)
            : c.Quality - RustPitcherMultiplier * c.Rust;

    public static bool IsBackupCatcher(in DesignationCandidate c) =>
        c.Primary == FieldingPosition.C || c.Secondary == FieldingPosition.C;

    public static bool IsMiddleInfield(in DesignationCandidate c) =>
        c.Primary is FieldingPosition.SecondBase or FieldingPosition.Shortstop
        || c.Secondary is FieldingPosition.SecondBase or FieldingPosition.Shortstop;

    public static bool IsOutfield(in DesignationCandidate c) =>
        c.Primary is FieldingPosition.LeftField or FieldingPosition.CenterField or FieldingPosition.RightField
        || c.Secondary is FieldingPosition.LeftField or FieldingPosition.CenterField or FieldingPosition.RightField;

    /// <summary>Q4: when the healthy can't fill the group, absent players fill it: return day ↑, role quality ↓, id ↑.</summary>
    private static void FillUnhealthy(ReadOnlySpan<DesignationCandidate> group, Span<bool> active, int remaining, DesignationRole role)
    {
        while (remaining > 0)
        {
            int best = -1;
            for (int i = 0; i < group.Length; i++)
            {
                if (active[i] || group[i].IsHealthy || group[i].IsMarginAvatar)
                {
                    continue;
                }
                if (best < 0 || FillsBefore(in group[i], in group[best], role))
                {
                    best = i;
                }
            }
            if (best < 0)
            {
                return;
            }
            active[best] = true;
            remaining--;
        }
    }

    private static bool FillsBefore(in DesignationCandidate a, in DesignationCandidate b, DesignationRole role)
    {
        if (a.UntilDay != b.UntilDay)
        {
            return a.UntilDay < b.UntilDay;
        }
        if (role == DesignationRole.PositionPlayer)
        {
            if (a.BatScore != b.BatScore)
            {
                return a.BatScore > b.BatScore;
            }
        }
        else
        {
            if (a.Quality != b.Quality)
            {
                return a.Quality > b.Quality;
            }
            if (a.Stamina != b.Stamina)
            {
                return a.Stamina > b.Stamina;
            }
        }
        return string.CompareOrdinal(a.PlayerId, b.PlayerId) < 0;
    }

    /// <summary>The slot-3 tie key after an equal score: stamina descending, then player id ascending.</summary>
    private static bool BeatsOnTies(in DesignationCandidate a, in DesignationCandidate b) =>
        a.Stamina != b.Stamina ? a.Stamina > b.Stamina : string.CompareOrdinal(a.PlayerId, b.PlayerId) < 0;

    private static int IndexOfSlot(ReadOnlySpan<DesignationCandidate> group, int slot)
    {
        for (int i = 0; i < group.Length; i++)
        {
            if (group[i].Slot == slot)
            {
                return i;
            }
        }
        throw new InvalidOperationException($"Slot {slot} is not in the designation group.");
    }
}
