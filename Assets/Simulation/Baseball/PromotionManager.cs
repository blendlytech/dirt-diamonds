using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Calibration knobs for the 9c promotion/advancement gates
/// (docs/design/promotion_advancement_gates.md §2/§3) — constants, not
/// literals, the <see cref="HeirGenetics.HeirGeneticsProfile"/>/<see cref="TierEffects"/>
/// precedent: retuning any of these is a data edit behind run_monte_carlo_batch,
/// never a logic edit. Retirement (42) / health-floor (40) are REUSED from
/// <see cref="HeirGenetics.HeirGeneticsProfile"/>, deliberately not duplicated,
/// so NPC and avatar aging-out share one rule.
/// </summary>
public static class PromotionProfile
{
    /// <summary>§2.3 combine weights: A = wP·P + wS·S, wP + wS = 1.</summary>
    public const double PerformanceWeight = 0.5;
    public const double ScoutingWeight = 0.5;

    /// <summary>§2.1 batter reliability shrinkage: w = PA/(PA + this). ~600-PA regulars keep most of their signal; a 3-PA fluke shrinks to league-average.</summary>
    public const int BatterPaShrink = 200;

    /// <summary>§2.1 pitcher reliability shrinkage: w = IP/(IP + this), IP = outs/3.</summary>
    public const double PitcherIpShrink = 40.0;

    /// <summary>
    /// §2.1 guard on the raw performance ratio: one season can argue you are
    /// at most this many times league-average. Without it a 1-out 0.00-ERA
    /// scrap of relief work produces an unbounded ratio that no shrinkage
    /// weight can tame.
    /// </summary>
    public const double PerformanceRatioCap = 3.0;

    /// <summary>§2.2 age bonus: +this at <see cref="YoungestProspectAge"/>, 0 at <see cref="PeakAge"/>, mildly negative past it (clamped symmetric).</summary>
    public const double AgeBonusMax = 10.0;
    public const int PeakAge = 27;
    public const int YoungestProspectAge = 15;

    /// <summary>
    /// 9d-2 (development doc §6): the summed role headroom (Σ potential −
    /// current over the role's three ratings) at which a young player's
    /// projection reads the FULL age-scaled bonus — an average raw HS intake
    /// under the first-pass ProspectDiscount lands near it, a half-developed
    /// prospect reads a partial bonus, an at-ceiling player reads zero.
    /// </summary>
    public const int HeadroomForFullProjection = 30;

    /// <summary>§3.2 merit-swap hysteresis: the riser must out-rank the incumbent by more than this many A-points, so a marginal "AAAA" player does not yo-yo every year.</summary>
    public const double SwapMargin = 5.0;

    /// <summary>§3.2 per-boundary merit-swap caps, per role — bounds annual churn independently of removals.</summary>
    public const int SwapCapBatters = 2;
    public const int SwapCapStarters = 1;
    public const int SwapCapRelievers = 1;

    public static int SwapCapFor(PitcherRole role) => role switch
    {
        PitcherRole.None => SwapCapBatters,
        PitcherRole.Starter => SwapCapStarters,
        PitcherRole.Reliever => SwapCapRelievers,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    /// <summary>§3.1 amateur age caps: over the cap without earning promotion = washed out to free agency.</summary>
    public const int HighSchoolAgeCap = 19;
    public const int CollegeAgeCap = 23;

    /// <summary>The tier's age-out cap, or int.MaxValue where no cap applies (pro tiers age out via retirement only).</summary>
    public static int AgeCapFor(LeagueTier tier) => tier switch
    {
        LeagueTier.HS => HighSchoolAgeCap,
        LeagueTier.College => CollegeAgeCap,
        _ => int.MaxValue,
    };

    /// <summary>§3.2 HS intake ages: the young end of the HS generation window (15–16), so fresh prospects have a full amateur runway.</summary>
    public const int IntakeMinAge = 15;
    public const int IntakeAgeSpan = 2;
}

/// <summary>
/// Pure, engine-free advancement-score math for the 9c promotion gates
/// (promotion doc §2) — the <see cref="HeirGenetics"/> profile: deterministic
/// cores the harness pins fixtures against with no database and no RNG.
/// P (performance) is tier-relative because each player's line was produced
/// against tier-appropriate competition (the §9 environment argument); S
/// (scouting) is the role-summed raw ratings plus a small age projection.
/// Both are reported on a 100-centred scale so the §2.3 combine is a plain
/// weighted sum.
/// </summary>
public static class PromotionScore
{
    private const double Epsilon = 1e-6;

    /// <summary>
    /// OPS from a counting line with the exact SqlNormalizeBattingRates
    /// formula (OBP = (H+BB)/(AB+BB), SLG = (H + 2B + 2·3B + 3·HR)/AB), so the
    /// score never depends on whether the rate denormalization has run.
    /// </summary>
    public static double Ops(in SeasonBattingLine line)
    {
        double obp = line.Ab + line.Bb > 0 ? (double)(line.H + line.Bb) / (line.Ab + line.Bb) : 0.0;
        double slg = line.Ab > 0 ? (double)(line.H + line.Doubles + 2 * line.Triples + 3 * line.Hr) / line.Ab : 0.0;
        return obp + slg;
    }

    /// <summary>League OPS from tier season totals — the same formula as <see cref="Ops"/>, over the sums.</summary>
    public static double LeagueOps(in LeagueBattingTotals totals)
    {
        double obp = totals.Ab + totals.Bb > 0 ? (double)(totals.H + totals.Bb) / (totals.Ab + totals.Bb) : 0.0;
        double slg = totals.Ab > 0 ? (double)(totals.H + totals.Doubles + 2 * totals.Triples + 3 * totals.Hr) / totals.Ab : 0.0;
        return obp + slg;
    }

    /// <summary>League ERA from tier season totals (ERA = 27·ER/outs, the SqlNormalizePitchingRates formula).</summary>
    public static double LeagueEra(in LeaguePitchingTotals totals) =>
        totals.OutsRecorded > 0 ? 27.0 * totals.Er / totals.OutsRecorded : 0.0;

    /// <summary>
    /// §2.1 batter performance on the 100-centred scale: an OPS+-style ratio
    /// against the tier's league average, reliability-shrunk toward 100 by
    /// PA/(PA+k). No line (or no league season) reads exactly 100 — a fresh
    /// intake or a shadow call-up's discard fragment can never top a cohort.
    /// </summary>
    public static double BatterPerformance(in SeasonBattingLine line, double leagueOps)
    {
        double rawP = line.Pa > 0 && leagueOps > Epsilon
            ? Math.Clamp(Ops(in line) / leagueOps, 0.0, PromotionProfile.PerformanceRatioCap)
            : 1.0;
        double w = line.Pa / (double)(line.Pa + PromotionProfile.BatterPaShrink);
        return 100.0 * (1.0 + w * (rawP - 1.0));
    }

    /// <summary>
    /// §2.1 pitcher performance: ERA−-style (league ERA over player ERA, so a
    /// lower ERA scores higher), capped and reliability-shrunk by IP/(IP+k).
    /// WHIP is a disclosed first-pass-unused secondary.
    /// </summary>
    public static double PitcherPerformance(int outsRecorded, int er, double leagueEra)
    {
        double rawP = 1.0;
        if (outsRecorded > 0 && leagueEra > Epsilon)
        {
            double era = 27.0 * er / outsRecorded;
            rawP = Math.Clamp(leagueEra / Math.Max(era, Epsilon), 0.0, PromotionProfile.PerformanceRatioCap);
        }
        double ip = outsRecorded / 3.0;
        double w = ip / (ip + PromotionProfile.PitcherIpShrink);
        return 100.0 * (1.0 + w * (rawP - 1.0));
    }

    /// <summary>
    /// §2.2 age projection: a modest, monotonically non-increasing linear
    /// taper — up to +max at 15, 0 at peak (27), mildly negative past it.
    /// Since 9d-2 this is the LEGACY fallback for callers with no potential
    /// in hand: it equals <see cref="ProjectionBonus"/> under an implicit
    /// full-headroom assumption (the pre-9d fudge — young players were
    /// presumed projectable because nothing measured their ceiling). The
    /// sweep itself scores through ProjectionBonus with real headroom.
    /// </summary>
    public static double AgeBonus(int age)
    {
        double slope = PromotionProfile.AgeBonusMax
            / (PromotionProfile.PeakAge - PromotionProfile.YoungestProspectAge);
        return Math.Clamp((PromotionProfile.PeakAge - age) * slope,
            -PromotionProfile.AgeBonusMax, PromotionProfile.AgeBonusMax);
    }

    /// <summary>
    /// 9d-2 (development doc §6): <see cref="AgeBonus"/> evolved — the
    /// scouting projection now reflects REAL remaining headroom instead of
    /// the age-only fudge. The upside is the same age-tapered bonus, scaled
    /// by how much ceiling actually remains (saturating at
    /// <see cref="PromotionProfile.HeadroomForFullProjection"/>): a young
    /// player far below his ceiling projects up in full, one already at his
    /// ceiling projects flat. Past the peak the age-driven decline projection
    /// stands untouched — decline is coming regardless of paper headroom.
    /// ProjectionBonus(age, headroom ≥ saturation) ≡ AgeBonus(age); same
    /// scale, same clamp, same profile constants.
    /// </summary>
    public static double ProjectionBonus(int age, int headroom)
    {
        double ageBonus = AgeBonus(age);
        if (ageBonus <= 0.0)
        {
            return ageBonus;
        }
        double saturation = Math.Clamp(
            headroom / (double)PromotionProfile.HeadroomForFullProjection, 0.0, 1.0);
        return ageBonus * saturation;
    }

    /// <summary>
    /// §6 the quantity <see cref="ProjectionBonus"/> consumes: Σ max(0,
    /// potential − current) over the role's three ratings. current ≤
    /// potential is the 9d world invariant, so the max() is defensive; a
    /// missing potential row reads zero headroom at the call sites (the v10
    /// backfill semantics — potential = current, decline-only).
    /// </summary>
    public static int Headroom(in RosterPlayerRow current, in PlayerPotentialRow potential, bool isPitcher) =>
        isPitcher
            ? Math.Max(0, potential.PitStuff - current.PitStuff)
                + Math.Max(0, potential.PitControl - current.PitControl)
                + Math.Max(0, potential.PitStamina - current.PitStamina)
            : Math.Max(0, potential.BatPower - current.BatPower)
                + Math.Max(0, potential.BatContact - current.BatContact)
                + Math.Max(0, potential.BatDiscipline - current.BatDiscipline);

    /// <summary>
    /// §2.2 scouting on the 100-centred scale: the role-summed raw ratings
    /// (batter Power+Contact+Discipline, pitcher Stuff+Control+Stamina — the
    /// exact metric FindDisplacedPlayer/EvaluateSuccession already rank by,
    /// 150 = all-average) plus the age projection, over 150. This age-only
    /// form stays for callers with no potential in hand (≡ full headroom).
    /// </summary>
    public static double Scouting(int roleRatingSum, int age) =>
        100.0 * (roleRatingSum + AgeBonus(age)) / 150.0;

    /// <summary>The 9d-2 headroom-aware form (development doc §6) the sweep scores through: scouting projects the player's actual remaining ceiling.</summary>
    public static double Scouting(int roleRatingSum, int age, int headroom) =>
        100.0 * (roleRatingSum + ProjectionBonus(age, headroom)) / 150.0;

    /// <summary>§2.3: A = wP·P + wS·S. Ties break on player_id at every call site.</summary>
    public static double Combine(double performance, double scouting) =>
        PromotionProfile.PerformanceWeight * performance + PromotionProfile.ScoutingWeight * scouting;
}

/// <summary>
/// What the last offseason pass did — the promotion counterpart of
/// <see cref="CareerManager.LastSuccession"/>, how a UI (or the harness)
/// notices movement. Intake equals Removals every year by the §3 conservation
/// law; the harness asserts it.
/// </summary>
public readonly struct PromotionSummary
{
    /// <summary>The completed season the pass evaluated (0 until the first pass runs).</summary>
    public readonly int SeasonYear;

    /// <summary>Players removed to free agency (retirement, health, amateur age-out).</summary>
    public readonly int Removals;

    /// <summary>Upward moves: vacancy promotions + merit-swap risers (the avatar's included).</summary>
    public readonly int Promotions;

    /// <summary>Downward moves: merit-swap incumbents relegated one tier.</summary>
    public readonly int Relegations;

    /// <summary>Fresh 15–16-year-olds generated into HS vacancies.</summary>
    public readonly int Intake;

    public readonly bool AvatarPromoted;

    /// <summary>The avatar's destination team when <see cref="AvatarPromoted"/>, else 0.</summary>
    public readonly int AvatarTeamId;

    public PromotionSummary(
        int seasonYear, int removals, int promotions, int relegations, int intake,
        bool avatarPromoted, int avatarTeamId)
    {
        SeasonYear = seasonYear;
        Removals = removals;
        Promotions = promotions;
        Relegations = relegations;
        Intake = intake;
        AvatarPromoted = avatarPromoted;
        AvatarTeamId = avatarTeamId;
    }
}

/// <summary>
/// The Phase 9c offseason sorting mechanism (promotion doc §3): on each
/// <see cref="SeasonRolledOverEvent"/> — subscribed AFTER the six tier sims
/// and <see cref="CareerManager"/>, so the completed season's stats are final
/// and every age is post-aging — it re-sorts players across the 9a tier
/// ladder. Retired/broken/aged-out players leave to free agency
/// (SetTeam(null), stats preserved), each removal's vacancy cascades down as
/// role-matched vacancy promotions terminating in exactly one generated HS
/// intake, and capped, hysteresis-margined merit swaps exchange the best of
/// each tier with the worst of the tier above. Every operation is a matched
/// exchange per (tier, role), which is what makes the roster invariant —
/// every tier exactly 8 × (9 batters + 5 starters + 3 relievers), rostered
/// population constant at 816 — a provable conservation law rather than a
/// hope.
///
/// No schema change (§7): promotion is SetTeam, removal is SetTeam(null),
/// intake is LeagueGenerator.GeneratePlayer, and tier is derived from the
/// team's Team_Tiers row. All mutations commit in ONE batch; the only RNG
/// draws are intake generation from a dedicated forked stream, so the six
/// sims' and the career's streams are never perturbed. A pass that moves
/// nobody is a complete no-op — no flush, no re-init, bit-identical world
/// (the empty-ledger-neutrality precedent).
///
/// Runs once per offseason — load-time-class code (ordinary allocation is
/// fine), never the per-PA hot path. Baseball-only: never references the
/// Life sim. Disclosed: background relievers rank almost purely on scouting,
/// because the macro-sim's complete-game shape gives them no season line.
/// </summary>
public sealed class PromotionManager
{
    private static readonly PitcherRole[] SweepRoles =
    {
        PitcherRole.None, PitcherRole.Starter, PitcherRole.Reliever,
    };

    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly BaseballQueries _baseball;
    private readonly GameStateQueries _gameState;
    private readonly LeagueDirectory _leagues;
    private readonly MicroGame _micro;
    private readonly Action<SeasonRolledOverEvent> _onSeasonRolledOver;
    private RngState _rng;

    /// <summary>
    /// The avatar handoff owner (promotion doc §6), optional like the sims'
    /// ledger references: null — every NPC-only harness world — excludes the
    /// avatar from the sweep entirely and never moves it. When attached, the
    /// avatar rides the same A-ranking as its cohort and, on clearing the
    /// same bar an NPC would, moves through the careful single-avatar path
    /// (<see cref="CareerManager.ReactivateAvatar"/>). Succession precedence:
    /// a pending succession choice or a finished lineage skips the avatar's
    /// promotion that offseason — the NPC churn still runs. v1: the avatar
    /// promotes, never auto-relegates (a slump costs the promotion, not the
    /// job) — a disclosed player-experience call, one flag from symmetric.
    ///
    /// High-school grade gate (<see cref="SchoolGrade"/>): while the avatar is
    /// a current HS student (age within <see cref="PromotionProfile.HighSchoolAgeCap"/>,
    /// grades freshman → senior) it is held out of the sweep and cannot promote
    /// — the four amateur seasons are guaranteed. Once its senior year is done
    /// (age past the cap) it competes for the HS→College move on MERIT like any
    /// other riser: earn it and graduate to college baseball, or be HELD BACK
    /// (stays in HS, protected from the amateur washout NPCs get) and try again
    /// next offseason as it develops. Not graduating means no college baseball —
    /// graduation is never forced.
    /// </summary>
    public CareerManager? Career;

    /// <summary>What the most recent pass did — default (SeasonYear 0) until the first rollover.</summary>
    public PromotionSummary LastRun { get; private set; }

    public PromotionManager(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        GameStateQueries gameState, LeagueDirectory leagues, MicroGame micro, RngState rng)
    {
        _db = db;
        _players = players;
        _baseball = baseball;
        _gameState = gameState;
        _leagues = leagues;
        _micro = micro;
        _rng = rng;
        _onSeasonRolledOver = OnSeasonRolledOver;
    }

    public void AttachTo(EventBus bus) => bus.Subscribe(_onSeasonRolledOver);

    public void DetachFrom(EventBus bus) => bus.Unsubscribe(_onSeasonRolledOver);

    private void OnSeasonRolledOver(SeasonRolledOverEvent e) => RunOffseason(e.PreviousSeasonYear);

    // ------------------------------------------------------------------
    // The offseason pass
    // ------------------------------------------------------------------

    /// <summary>One rostered player's rank entry within a (tier, role) cohort.</summary>
    private struct Candidate
    {
        public string PlayerId;
        public int TeamId;
        public double Score;

        /// <summary>Amateur over the tier's age cap — removed at their tier's turn unless the sweep already promoted them (§3.1 "did not earn promotion this cycle").</summary>
        public bool AgeOutPending;

        public bool IsAvatar;
    }

    /// <summary>
    /// The full §3 pass for one completed season: removal set, top-down
    /// per-role sweep, HS intake, one batch commit, then flush → re-init →
    /// avatar re-activation. Public so the harness can drive it directly;
    /// the bus handler is a one-liner onto this.
    /// </summary>
    public void RunOffseason(int completedSeasonYear)
    {
        // ---- bulk loads (the whole pass reads up front, never mid-sweep) ----
        var teams = new List<TeamRow>(LeagueDirectory.TierCount * LeagueSimulator.TeamCount);
        _baseball.LoadAllTeams(teams);
        if (teams.Count == 0)
        {
            return; // no world yet — nothing to sort
        }
        var tierByTeam = new Dictionary<int, LeagueTier>(teams.Count);
        Span<int> teamsPerTier = stackalloc int[LeagueDirectory.TierCount];
        foreach (TeamRow team in teams)
        {
            tierByTeam.Add(team.TeamId, team.Tier);
            teamsPerTier[(int)team.Tier]++;
        }
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            if (teamsPerTier[t] != LeagueSimulator.TeamCount)
            {
                throw new InvalidOperationException(
                    $"Promotion pass requires the full 6-tier ladder; tier {(LeagueTier)t} has {teamsPerTier[t]}/{LeagueSimulator.TeamCount} teams. " +
                    "Run LeagueGenerator.EnsureTierLeagues first.");
            }
        }

        var playerRows = new List<PlayerRow>();
        _players.LoadAll(playerRows);
        var ageHealthById = new Dictionary<string, (int Age, int Health)>(playerRows.Count, StringComparer.Ordinal);
        foreach (PlayerRow row in playerRows)
        {
            ageHealthById.Add(row.PlayerId, (row.Age, row.HealthCeiling));
        }

        var roster = new List<RosterPlayerRow>(
            LeagueDirectory.TierCount * LeagueSimulator.TeamCount * LeagueSimulator.RosterSizePerTeam);
        _baseball.LoadRoster(roster);

        // 9d-2 (development doc §6): the sweep's scouting projects real
        // remaining headroom, so the pass also bulk-loads the stored ceilings
        // the development pass just moved everyone toward.
        var potentialById = new Dictionary<string, PlayerPotentialRow>(roster.Count, StringComparer.Ordinal);
        _baseball.LoadAllPotential(potentialById);

        var batLines = new List<SeasonBattingLine>();
        _baseball.LoadSeasonBattingLines(completedSeasonYear, batLines);
        var batById = new Dictionary<string, SeasonBattingLine>(batLines.Count, StringComparer.Ordinal);
        foreach (SeasonBattingLine line in batLines)
        {
            batById.Add(line.PlayerId, line);
        }
        var pitLines = new List<SeasonPitchingLine>();
        _baseball.LoadSeasonPitchingLines(completedSeasonYear, pitLines);
        var pitById = new Dictionary<string, SeasonPitchingLine>(pitLines.Count, StringComparer.Ordinal);
        foreach (SeasonPitchingLine line in pitLines)
        {
            pitById.Add(line.PlayerId, line);
        }

        Span<double> leagueOps = stackalloc double[LeagueDirectory.TierCount];
        Span<double> leagueEra = stackalloc double[LeagueDirectory.TierCount];
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            LeagueBattingTotals bat = _baseball.LoadLeagueBattingTotals(completedSeasonYear, (LeagueTier)t);
            LeaguePitchingTotals pit = _baseball.LoadLeaguePitchingTotals(completedSeasonYear, (LeagueTier)t);
            leagueOps[t] = PromotionScore.LeagueOps(in bat);
            leagueEra[t] = PromotionScore.LeagueEra(in pit);
        }

        // ---- avatar gate (§5/§6): excluded from the set-based sweep always;
        // rides the ranking through the careful path only when a career is
        // attached and no succession is pending / the lineage is alive. ----
        string? avatarId = _gameState.TryGetText(GameStateKeys.AvatarPlayerId, out string storedAvatarId)
            ? storedAvatarId
            : null;
        bool avatarEligible = avatarId is not null
            && Career is { HasAvatar: true } career
            && !career.HasPendingSuccessionChoice
            && !career.IsLineageOver;

        // ---- the three role sweeps (roles never change via promotion, §11) ----
        var teamWrites = new List<(string PlayerId, int? TeamId)>();
        var intake = new List<(PitcherRole Role, int TeamId)>();
        int removals = 0;
        int promotions = 0;
        int relegations = 0;
        bool avatarPromoted = false;
        int avatarTeamId = 0;

        foreach (PitcherRole role in SweepRoles)
        {
            SweepRole(role, roster, tierByTeam, ageHealthById, potentialById, batById, pitById,
                leagueOps, leagueEra, avatarId, avatarEligible,
                teamWrites, intake, ref removals, ref promotions, ref relegations,
                ref avatarPromoted, ref avatarTeamId);
        }

        if (teamWrites.Count == 0 && intake.Count == 0)
        {
            // Nothing moved: a complete no-op — no flush, no re-init, the
            // world stays bit-identical (the empty-ledger-neutrality bar).
            LastRun = new PromotionSummary(completedSeasonYear, 0, 0, 0, 0, false, 0);
            return;
        }

        // ---- §4: flush before re-init — the new season's day-1 games are
        // already in the sims' in-memory arrays (DayAdvanced dispatches before
        // SeasonRolledOver), and the reload must lose nothing. ----
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            if (_leagues.TryGet((LeagueTier)t, out LeagueSimulator sim))
            {
                sim.FlushPending();
            }
        }

        // ---- one batch for every mutation (the one-transaction-per-tick rule);
        // the only RNG draws are intake generation, from the dedicated fork. ----
        _db.BeginBatch();
        try
        {
            foreach ((string playerId, int? teamId) in teamWrites)
            {
                _players.SetTeam(playerId, teamId);
            }
            foreach ((PitcherRole role, int teamId) in intake)
            {
                (string id, int stuff) = LeagueGenerator.GeneratePlayer(
                    _players, _baseball, teamId, role, LeagueGenerator.DefaultRatingSpread, ref _rng,
                    PromotionProfile.IntakeMinAge, PromotionProfile.IntakeAgeSpan);
                if (role != PitcherRole.None)
                {
                    LeagueGenerator.GenerateArsenal(_baseball, id, stuff, LeagueGenerator.DefaultRatingSpread, ref _rng);
                }
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        // ---- the Succeed tail, generalized (§4/§6): every registered tier
        // sim + the micro-sim re-bulk-load once, then the avatar re-resolves
        // against its (possibly new) tier. ----
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            if (_leagues.TryGet((LeagueTier)t, out LeagueSimulator sim))
            {
                sim.Initialize();
            }
        }
        _micro.Initialize();
        if (Career is { HasAvatar: true } activeCareer)
        {
            activeCareer.ReactivateAvatar();
        }

        LastRun = new PromotionSummary(
            completedSeasonYear, removals, promotions, relegations, intake.Count,
            avatarPromoted, avatarTeamId);
    }

    /// <summary>
    /// The §3.2 single top-down sweep for one role: forced removals seed each
    /// tier's need, tiers hand freshly-opened holes downward as vacancy
    /// promotions, capped merit swaps exchange across each boundary, amateur
    /// age-outs are finalized at their tier's turn (after their chance to
    /// promote has passed), and HS terminates every cascade with generated
    /// intake. Each boundary×role is an independent matched reconciliation —
    /// the conservation law's proof shape.
    /// </summary>
    private void SweepRole(
        PitcherRole role, List<RosterPlayerRow> roster,
        Dictionary<int, LeagueTier> tierByTeam,
        Dictionary<string, (int Age, int Health)> ageHealthById,
        Dictionary<string, PlayerPotentialRow> potentialById,
        Dictionary<string, SeasonBattingLine> batById,
        Dictionary<string, SeasonPitchingLine> pitById,
        ReadOnlySpan<double> leagueOps, ReadOnlySpan<double> leagueEra,
        string? avatarId, bool avatarEligible,
        List<(string PlayerId, int? TeamId)> teamWrites, List<(PitcherRole Role, int TeamId)> intake,
        ref int removals, ref int promotions, ref int relegations,
        ref bool avatarPromoted, ref int avatarTeamId)
    {
        var pools = new List<Candidate>[LeagueDirectory.TierCount];
        var vacated = new List<int>[LeagueDirectory.TierCount];
        var arrivals = new List<Candidate>[LeagueDirectory.TierCount];
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            pools[t] = new List<Candidate>();
            vacated[t] = new List<int>();
            arrivals[t] = new List<Candidate>();
        }

        // ---- build the cohorts; forced removals (retirement/health — the
        // exact succession triggers) leave immediately and can never promote
        // or relegate ("removed rather than relegated", §5). ----
        foreach (RosterPlayerRow row in roster)
        {
            if (row.Role != role)
            {
                continue;
            }
            int tier = (int)tierByTeam[row.TeamId];
            bool isAvatar = avatarId is not null && string.Equals(row.PlayerId, avatarId, StringComparison.Ordinal);
            if (isAvatar && !avatarEligible)
            {
                continue; // §5: the avatar's lifecycle is succession's; skip entirely this offseason
            }
            (int age, int health) = ageHealthById[row.PlayerId];
            // Grade gate (SchoolGrade): a current HS student — freshman through
            // senior, age within the amateur cap — is held in high school. The
            // avatar cannot promote out of HS before its senior year is done;
            // excluding it from the cohort entirely (the same skip the pending-
            // succession case above uses, so the §10.2 conservation law still
            // holds) leaves it on its team this offseason. A senior whose year
            // is done — age PAST the cap — falls through to be pooled and
            // competes for the HS→College move on MERIT like everyone else: earn
            // it and graduate, or be held back another year (see the swap block).
            if (isAvatar && tier == (int)LeagueTier.HS && age <= PromotionProfile.HighSchoolAgeCap)
            {
                continue;
            }
            if (!isAvatar
                && (age >= HeirGenetics.HeirGeneticsProfile.MandatoryRetirementAge
                    || health <= HeirGenetics.HeirGeneticsProfile.HealthRetirementFloor))
            {
                teamWrites.Add((row.PlayerId, null)); // → FA, stats preserved (the succession-retiree precedent)
                vacated[tier].Add(row.TeamId);
                removals++;
                continue;
            }

            bool isPitcher = role != PitcherRole.None;
            int talent = isPitcher
                ? row.PitStuff + row.PitControl + row.PitStamina
                : row.BatPower + row.BatContact + row.BatDiscipline;
            // 9d-2 §6: a missing ceiling row reads zero headroom — the v10
            // backfill semantics (potential = current) for any straggler.
            int headroom = potentialById.TryGetValue(row.PlayerId, out PlayerPotentialRow pot)
                ? PromotionScore.Headroom(in row, in pot, isPitcher)
                : 0;
            double p;
            if (isPitcher)
            {
                pitById.TryGetValue(row.PlayerId, out SeasonPitchingLine pit);
                p = PromotionScore.PitcherPerformance(pit.OutsRecorded, pit.Er, leagueEra[tier]);
            }
            else
            {
                batById.TryGetValue(row.PlayerId, out SeasonBattingLine bat);
                p = PromotionScore.BatterPerformance(in bat, leagueOps[tier]);
            }
            pools[tier].Add(new Candidate
            {
                PlayerId = row.PlayerId,
                TeamId = row.TeamId,
                Score = PromotionScore.Combine(p, PromotionScore.Scouting(talent, age, headroom)),
                AgeOutPending = !isAvatar && age > PromotionProfile.AgeCapFor((LeagueTier)tier),
                IsAvatar = isAvatar,
            });
        }
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            pools[t].Sort(static (a, b) =>
            {
                int byScore = b.Score.CompareTo(a.Score);
                return byScore != 0 ? byScore : string.CompareOrdinal(a.PlayerId, b.PlayerId);
            });
        }

        // ---- top-down: MLB(5) → HS(0) ----
        for (int t = LeagueDirectory.TierCount - 1; t >= 0; t--)
        {
            // (a) finalize this tier's amateur age-outs — their chance to
            // earn promotion (the boundary above) has passed.
            for (int i = pools[t].Count - 1; i >= 0; i--)
            {
                if (pools[t][i].AgeOutPending)
                {
                    teamWrites.Add((pools[t][i].PlayerId, null));
                    vacated[t].Add(pools[t][i].TeamId);
                    removals++;
                    pools[t].RemoveAt(i);
                }
            }

            int need = vacated[t].Count - arrivals[t].Count;
            if (t == 0)
            {
                // (d) HS terminates every cascade: one generated 15–16-year-old
                // per remaining vacancy (need ≡ this year's removals, §3).
                vacated[0].Sort();
                for (int i = arrivals[0].Count; i < vacated[0].Count; i++)
                {
                    intake.Add((role, vacated[0][i]));
                }
                continue;
            }

            // (b) vacancy promotions: T−1's best fill this tier's holes,
            // deferring the need downward as fresh vacancies in T−1.
            for (int i = 0; i < need; i++)
            {
                if (pools[t - 1].Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Tier {(LeagueTier)(t - 1)} {role} pool exhausted filling tier {(LeagueTier)t} — the conservation law cannot hold.");
                }
                Candidate riser = pools[t - 1][0];
                pools[t - 1].RemoveAt(0);
                vacated[t - 1].Add(riser.TeamId);
                arrivals[t].Add(riser);
                promotions++;
            }

            // (c) merit swaps, capped + hysteresis-margined: the boundary's
            // best remaining riser against the worst incumbent — the avatar
            // is never a relegation target (v1 promotes, never auto-relegates).
            int cap = PromotionProfile.SwapCapFor(role);
            for (int swaps = 0; swaps < cap && pools[t - 1].Count > 0; swaps++)
            {
                Candidate riser = pools[t - 1][0];
                int worstIndex = pools[t].Count - 1;
                while (worstIndex >= 0 && pools[t][worstIndex].IsAvatar)
                {
                    worstIndex--;
                }
                if (worstIndex < 0)
                {
                    break;
                }
                Candidate incumbent = pools[t][worstIndex];
                if (riser.Score <= incumbent.Score + PromotionProfile.SwapMargin)
                {
                    break;
                }
                // Direct matched exchange: the riser takes the incumbent's
                // exact team slot, the incumbent relegates into the riser's —
                // net-zero on both tiers (and §6's destination when the riser
                // is the avatar).
                teamWrites.Add((riser.PlayerId, incumbent.TeamId));
                teamWrites.Add((incumbent.PlayerId, riser.TeamId));
                pools[t - 1].RemoveAt(0);
                pools[t].RemoveAt(worstIndex);
                promotions++;
                relegations++;
                if (riser.IsAvatar)
                {
                    avatarPromoted = true;
                    avatarTeamId = incumbent.TeamId;
                }
            }

            // Graduation is EARNED, never forced: a graduated HS senior (age
            // past the amateur cap) competes for the HS→College move on merit
            // through (b)/(c) above exactly like any other riser. If it out-ranks
            // a College incumbent it graduates; if it does not, it is HELD BACK —
            // it stays in high school (the avatar's age-out protection in the
            // cohort build keeps it from washing out like an over-cap NPC) and
            // tries again next offseason as it develops. Not graduating means no
            // college baseball, by design. There is no forced-graduation step.
        }

        // ---- team assignment: each tier's arrivals take its vacated slots,
        // best score to lowest team_id — deterministic, and exactly matched
        // by construction (the conservation law; assert loudly regardless). ----
        for (int t = 1; t < LeagueDirectory.TierCount; t++)
        {
            if (arrivals[t].Count != vacated[t].Count)
            {
                throw new InvalidOperationException(
                    $"Tier {(LeagueTier)t} {role} reconciliation broke: {arrivals[t].Count} arrivals for {vacated[t].Count} vacancies.");
            }
            vacated[t].Sort();
            for (int i = 0; i < arrivals[t].Count; i++)
            {
                teamWrites.Add((arrivals[t][i].PlayerId, vacated[t][i]));
                if (arrivals[t][i].IsAvatar)
                {
                    avatarPromoted = true;
                    avatarTeamId = vacated[t][i];
                }
            }
        }
    }
}
