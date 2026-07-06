using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Which of the seven Player_Ratings scales a development delta applies to —
/// the 9d curve's physical/skill split (development doc §2.3): plate
/// discipline and pitcher command age gracefully; the physical tools do not.
/// </summary>
public enum RatingKind : byte
{
    BatPower = 0,
    BatContact = 1,
    BatDiscipline = 2,
    PitStuff = 3,
    PitControl = 4,
    PitStamina = 5,
    Fielding = 6,
}

/// <summary>
/// Calibration knobs for the 9d development/decline curves
/// (docs/design/development_decline_curves.md §2–§4) — constants, not
/// literals, the <see cref="PromotionProfile"/>/<see cref="HeirGenetics.HeirGeneticsProfile"/>
/// precedent: retuning any of these is a data edit behind the tuning harness,
/// never a logic edit. The career pivots — peak (27), youngest prospect (15),
/// retirement (42) — are REUSED from <see cref="PromotionProfile"/> and
/// <see cref="HeirGenetics.HeirGeneticsProfile"/>, deliberately never
/// re-declared, so the promotion age projection, the aging-out rules and the
/// development curve can never disagree about the shape of a career.
/// </summary>
public static class DevelopmentProfile
{
    /// <summary>§2.1 growth: fraction of the (potential − current) gap closed per season at full youth weight.</summary>
    public const double GrowthRate = 0.5;

    /// <summary>
    /// §2.3 decline: rating points lost per season per full-weight rating at
    /// full age weight (age 42), before health scaling. §7 tuning-pass value
    /// — halved from the 9d-1 first-pass 6.0. At 6.0 the pro tiers' long-
    /// tenured, never-swapped decliners (no age cap past College, unlike HS/
    /// College's pure-growth population) dragged MinorA/MinorAA/MinorAAA's
    /// mean talent down enough to invert College above MinorA in the
    /// long-run equilibrium (Tools/MonteCarloHarness's §7 suite proved this
    /// empirically over 80-generation runs, robust across seeds). 3.0 clears
    /// the inversion with a comfortable margin while still producing a real,
    /// accelerating decline (erosion only rounds to zero below age ~34 —
    /// i.e. decline is imperceptible right after the peak and picks up
    /// through the mid-30s to retirement, not gone, just gentler than the
    /// first-pass value).
    /// </summary>
    public const double DeclineRate = 3.0;

    /// <summary>§2.3 kindWeight for the physical tools (power/contact/stuff/stamina/fielding) — full decline weight.</summary>
    public const double PhysicalKindWeight = 1.0;

    /// <summary>§2.3 kindWeight for the command/discipline skills (BatDiscipline, PitControl) — they hold up with age, the crafty-veteran texture.</summary>
    public const double SkillKindWeight = 0.5;

    /// <summary>
    /// §2.3 health→decline coupling: healthScale = 1 + this · (100 − health)/100,
    /// so an eroded health_ceiling (PED costs, injuries) accelerates decline —
    /// 1.6× at the health-40 retirement floor. 0 is the clean off-switch the
    /// harness's isolation fixtures use.
    /// </summary>
    public const double HealthDeclineCoefficient = 1.0;

    /// <summary>§2.4 bust/breakout jitter scale in rating points, further scaled by the phase weight and kindWeight — zero at the peak by construction.</summary>
    public const double JitterSpread = 2.0;

    /// <summary>
    /// §3.3 raw-intake prospect discount: max rating points a generated
    /// player's current sits below his rolled potential, scaled by
    /// youthWeight(generation age) and a per-rating uniform gap roll — THE
    /// band-moving lever (doc §7), first-pass mild by design.
    /// </summary>
    public const int ProspectDiscount = 20;

    /// <summary>
    /// §3.3 avatar creation headroom: rating points of ceiling granted above
    /// the player-chosen current ratings, scaled by youthWeight(19) and
    /// clamped at 100 — a max-built avatar gets zero headroom (decline-only),
    /// a modest build leaves room to grow. Deterministic in v1 (no roll): the
    /// creation UI's risk/reward must be readable, not a hidden lottery.
    /// </summary>
    public const int AvatarCreationHeadroom = 15;

    /// <summary>§4 practice: hard cap on the extra per-season growth fraction the avatar's practice credit can add — you cannot grind a 40 to a 100 in one winter.</summary>
    public const double PracticeFracCap = 0.25;

    /// <summary>
    /// §4 practice conversion: extra growth fraction per practiced hour at
    /// the margin (the "points-per-hour" knob — on a 40-point gap the first
    /// hour is worth 0.02 rating points). Diminishing returns bend the line
    /// toward <see cref="PracticeFracCap"/> (see
    /// <see cref="DevelopmentCurve.PracticeFraction"/>): a casual hour a day
    /// earns a real boost, only a grinder approaches the cap.
    /// </summary>
    public const double PracticeFracPerHour = 0.0005;

    /// <summary>The §2.3 physical/skill split, per rating.</summary>
    public static double KindWeightFor(RatingKind kind) => kind switch
    {
        RatingKind.BatDiscipline or RatingKind.PitControl => SkillKindWeight,
        _ => PhysicalKindWeight,
    };
}

/// <summary>
/// Pure, engine-free, DB-free development-curve math (development doc §2/§3)
/// — the <see cref="HeirGenetics"/>/<see cref="PromotionScore"/> profile:
/// deterministic <c>bell</c>-injecting cores the harness pins fixtures
/// against, plus RNG-driven overloads for production. The load-bearing curve
/// property (§1/§7): growth is a fraction of the REMAINING gap to a stored
/// per-player potential, so a rating asymptotes at its ceiling and never
/// overshoots — the anchor that makes the league's rating distribution
/// stationary. Decline is an accelerating absolute erosion off the CURRENT
/// rating, kindWeight-split and health-scaled. Both phase weights are zero at
/// <see cref="PromotionProfile.PeakAge"/>, so the peak plateau falls out of
/// the math with no special case, and a whole-roster pass over players pinned
/// at the peak is a provable no-op.
/// </summary>
public static class DevelopmentCurve
{
    /// <summary>
    /// §2.1 growth taper: 1.0 at the youngest prospect age (15), linearly down
    /// to 0.0 at the peak (27), clamped — ages below 15 (unrostered child
    /// heirs, defensively) read full weight, ages past the peak read zero.
    /// </summary>
    public static double YouthWeight(int age) =>
        Math.Clamp((PromotionProfile.PeakAge - age)
            / (double)(PromotionProfile.PeakAge - PromotionProfile.YoungestProspectAge), 0.0, 1.0);

    /// <summary>
    /// §2.3 decline accelerator: 0 at the peak (27), rising quadratically to
    /// 1.0 at mandatory retirement (42) — gentle in the early 30s, steep at
    /// the end, the real shape of aging. Deliberately unclamped above 1 so a
    /// hypothetical past-42 player (an avatar mid-succession-choice) keeps
    /// collapsing rather than plateauing.
    /// </summary>
    public static double AgeWeight(int age)
    {
        double t = Math.Max(0.0,
            (age - PromotionProfile.PeakAge)
            / (double)(HeirGenetics.HeirGeneticsProfile.MandatoryRetirementAge - PromotionProfile.PeakAge));
        return t * t;
    }

    /// <summary>§2.3 health→decline coupling — 1.0 at full health, rising as health_ceiling erodes.</summary>
    public static double HealthScale(int health) =>
        1.0 + DevelopmentProfile.HealthDeclineCoefficient * (100 - Math.Clamp(health, 0, 100)) / 100.0;

    /// <summary>
    /// §2 deterministic core: one rating's post-offseason value. Growth
    /// (age ≤ peak) closes growthFrac of the gap to potential plus a
    /// youth-scaled jitter, clamped so it NEVER overshoots the ceiling;
    /// decline (age > peak) erodes kindWeight · DeclineRate · ageWeight ·
    /// healthScale points plus an age-scaled jitter. <paramref name="practiceFrac"/>
    /// (the avatar's §4 lever, 0 for every NPC) adds a capped extra gap
    /// fraction young and relieves decline old ("staying in shape"). The
    /// caller guarantees current ≤ potential (true for every creation path);
    /// the [0, potential] clamp preserves it for life.
    /// </summary>
    public static int DevelopRating(
        int current, int potential, int age, RatingKind kind, int health, double practiceFrac, double bell)
    {
        double kindWeight = DevelopmentProfile.KindWeightFor(kind);
        double practice = Math.Clamp(practiceFrac, 0.0, DevelopmentProfile.PracticeFracCap);
        int next;
        if (age <= PromotionProfile.PeakAge)
        {
            int gap = Math.Max(0, potential - current);
            double youth = YouthWeight(age);
            double growthFrac = Math.Min(1.0, DevelopmentProfile.GrowthRate * youth + practice);
            double jitter = DevelopmentProfile.JitterSpread * youth * kindWeight * bell;
            next = current + RoundAwayFromZero(growthFrac * gap + jitter);
        }
        else
        {
            double aged = AgeWeight(age);
            double erosion = kindWeight * DevelopmentProfile.DeclineRate * aged * HealthScale(health)
                * (1.0 - practice);
            double jitter = DevelopmentProfile.JitterSpread * aged * kindWeight * bell;
            next = current - RoundAwayFromZero(erosion + jitter);
        }
        return Math.Clamp(next, 0, Math.Max(potential, current));
    }

    /// <summary>RNG-driven form: draws its own <see cref="HeirGenetics.Bell"/> (the shared sum-of-three-uniforms shape).</summary>
    public static int DevelopRating(
        int current, int potential, int age, RatingKind kind, int health, double practiceFrac, ref RngState rng) =>
        DevelopRating(current, potential, age, kind, health, practiceFrac, HeirGenetics.Bell(ref rng));

    /// <summary>
    /// §4 the season's practice-credit conversion: accumulated Practice hours
    /// → the avatar's extra growth fraction, exponential-saturation shape —
    /// initial slope exactly <see cref="DevelopmentProfile.PracticeFracPerHour"/>,
    /// strictly diminishing marginal returns, asymptote at
    /// <see cref="DevelopmentProfile.PracticeFracCap"/> ("you cannot grind a
    /// 40 to a 100 in one winter"). Deterministic and DB-free — the harness
    /// pins fixtures against it; zero and negative hours read 0 (an heir's
    /// cleared credit is simply no bonus).
    /// </summary>
    public static double PracticeFraction(long practicedHours) =>
        practicedHours <= 0
            ? 0.0
            : DevelopmentProfile.PracticeFracCap * (1.0 - Math.Exp(
                -DevelopmentProfile.PracticeFracPerHour * practicedHours / DevelopmentProfile.PracticeFracCap));

    /// <summary>
    /// §3.3 deterministic core of the raw-intake discount: a generated
    /// player's CURRENT rating, sitting <c>ProspectDiscount · youthWeight(age)
    /// · gapRoll</c> points below his rolled potential — big for a 15-year-old
    /// HS intake, zero at/past the peak, so a generated veteran enters at his
    /// ceiling. Clamped to [0, potential], establishing the current ≤ potential
    /// invariant at birth.
    /// </summary>
    public static int RawCurrent(int potential, int generationAge, double gapRoll) =>
        Math.Clamp(
            potential - RoundAwayFromZero(DevelopmentProfile.ProspectDiscount * YouthWeight(generationAge) * gapRoll),
            0, potential);

    /// <summary>RNG-driven form for world-gen intake: one uniform gap roll per rating.</summary>
    public static int RawCurrent(int potential, int generationAge, ref RngState rng) =>
        RawCurrent(potential, generationAge, rng.NextDouble());

    /// <summary>
    /// §3.3 avatar-creation ceiling: the player-chosen current rating plus a
    /// deterministic youth-headroom grant, clamped to [current, 100] — a
    /// max-built rating gets zero headroom. Also the heir path's inverse
    /// companion: heirs discount current below the blended ceiling via
    /// <see cref="RawCurrent(int,int,double)"/> at full gap roll instead.
    /// </summary>
    public static int HeadroomPotential(int current, int age) =>
        Math.Clamp(
            current + RoundAwayFromZero(DevelopmentProfile.AvatarCreationHeadroom * YouthWeight(age)),
            current, 100);

    private static int RoundAwayFromZero(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);
}

/// <summary>
/// What the last development pass did — the 9d counterpart of
/// <see cref="PromotionSummary"/>/<see cref="CareerManager.LastSuccession"/>,
/// how a UI (or the harness) notices ratings moved.
/// </summary>
public readonly struct DevelopmentSummary
{
    /// <summary>The completed season the pass developed after (0 until the first pass runs).</summary>
    public readonly int SeasonYear;

    /// <summary>Rostered players whose ratings moved this offseason.</summary>
    public readonly int PlayersChanged;

    /// <summary>Total rating points gained across the roster (growth side).</summary>
    public readonly int PointsUp;

    /// <summary>Total rating points lost across the roster (decline side).</summary>
    public readonly int PointsDown;

    public DevelopmentSummary(int seasonYear, int playersChanged, int pointsUp, int pointsDown)
    {
        SeasonYear = seasonYear;
        PlayersChanged = playersChanged;
        PointsUp = pointsUp;
        PointsDown = pointsDown;
    }
}

/// <summary>
/// The Phase 9d offseason development pass (development doc §5): on each
/// <see cref="SeasonRolledOverEvent"/> — subscribed AFTER
/// <see cref="CareerManager"/> (ages are post-aging) and BEFORE
/// <see cref="PromotionManager"/> (the sweep's scouting reads the
/// just-developed ratings: develop-before-sort, the §1 "climbs because he
/// developed" property) — it moves every ROSTERED player's ratings along the
/// <see cref="DevelopmentCurve"/>: young players close a fraction of the gap
/// to their stored <c>Player_Potential</c> ceiling, veterans erode off their
/// current ratings. Unrostered people (free agents, child heirs) do not
/// develop in v1 — an heir starts developing when succession rosters him.
///
/// All rating writes commit in ONE batch (one-transaction-per-tick); the only
/// RNG is the §2.4 jitter, from a dedicated forked stream, so the six sims'
/// and the career's streams are never perturbed. A pass that changes no
/// rating (everyone pinned at the peak, or a frozen population) is a complete
/// no-op — no flush, no re-init, bit-identical world (the
/// empty-ledger-neutrality bar). Otherwise: flush every registered tier sim
/// BEFORE the batch (the new season's day-1 games are already in their
/// arrays), re-<c>Initialize()</c> them all after so <see cref="TierEffects"/>
/// re-bakes the developed ratings, and re-resolve the avatar's micro slot.
///
/// Runs once per offseason — load-time-class code, never the per-PA hot
/// path. Baseball-only: never references the Life sim (the avatar's Practice
/// credit arrives via the Game_State key the GameManager bridge accumulates —
/// §4 — consumed and cleared here, never a Life reference).
/// </summary>
public sealed class DevelopmentManager
{
    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly BaseballQueries _baseball;
    private readonly GameStateQueries _gameState;
    private readonly LeagueDirectory _leagues;
    private readonly MicroGame _micro;
    private readonly Action<SeasonRolledOverEvent> _onSeasonRolledOver;
    private RngState _rng;

    /// <summary>
    /// Optional avatar owner (the <see cref="PromotionManager.Career"/>
    /// precedent): when attached, the pass re-resolves the avatar's micro
    /// slot after its re-init (the team never changes — development moves
    /// ratings, not people — so this is a defensive re-find, not a transfer)
    /// and gates the §4 practice credit: the accumulated hours apply to the
    /// avatar's own growth only when no succession occurred this rollover.
    /// Null (every NPC-only harness world) skips the practice term entirely.
    /// </summary>
    public CareerManager? Career;

    /// <summary>What the most recent pass did — default (SeasonYear 0) until the first rollover.</summary>
    public DevelopmentSummary LastRun { get; private set; }

    public DevelopmentManager(
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

    private void OnSeasonRolledOver(SeasonRolledOverEvent e) => RunDevelopment(e.PreviousSeasonYear);

    /// <summary>
    /// The full §5 pass for one offseason: bulk loads up front (roster,
    /// ages/health, potentials — never row-at-a-time mid-pass), the curve per
    /// rostered player per rating, one batch of changed-row upserts, then
    /// flush → commit → re-init → avatar slot re-find. Public so the harness
    /// drives it directly; the bus handler is a one-liner onto this.
    /// </summary>
    public void RunDevelopment(int completedSeasonYear)
    {
        var roster = new List<RosterPlayerRow>(
            LeagueDirectory.TierCount * LeagueSimulator.TeamCount * LeagueSimulator.RosterSizePerTeam);
        _baseball.LoadRoster(roster);
        if (roster.Count == 0)
        {
            return; // no world yet — nothing to develop
        }

        var playerRows = new List<PlayerRow>();
        _players.LoadAll(playerRows);
        var ageHealthById = new Dictionary<string, (int Age, int Health)>(playerRows.Count, StringComparer.Ordinal);
        foreach (PlayerRow row in playerRows)
        {
            ageHealthById.Add(row.PlayerId, (row.Age, row.HealthCeiling));
        }

        var potentialById = new Dictionary<string, PlayerPotentialRow>(roster.Count, StringComparer.Ordinal);
        _baseball.LoadAllPotential(potentialById);

        // ---- §4 the Practice lever: the season's accumulated credit (hours,
        // written day by day through the GameManager bridge) converts to the
        // avatar's extra growth fraction. The succession guard is a contract,
        // not dispatch-timing luck: when THIS rollover handed the career to an
        // heir (or parked the handoff choice), the credit was the outgoing
        // avatar's training and leaves with him — nobody collects it. The key
        // clears regardless, so credit never banks across seasons and the
        // seasonal cap cannot be stockpiled around.
        double avatarPracticeFrac = 0.0;
        string? practiceAvatarId = null;
        bool hadPracticeCredit =
            _gameState.TryGetInt64(GameStateKeys.AvatarPracticeCredit, out long practiceHours)
            && practiceHours != 0;
        if (hadPracticeCredit
            && Career is { HasAvatar: true } practiceCareer
            && !practiceCareer.IsLineageOver
            && practiceCareer.LastSuccession.Kind != SuccessionOutcomeKind.Succeeded)
        {
            practiceAvatarId = practiceCareer.AvatarPlayerId;
            avatarPracticeFrac = DevelopmentCurve.PracticeFraction(practiceHours);
        }

        // ---- the curve, per rostered player (roster order is deterministic:
        // (team_id, player_id) — the jitter draw sequence is reproducible;
        // the practice fraction consumes no draw, so a practiced and an
        // unpracticed world share one bell sequence). ----
        var writes = new List<PlayerRatingsRow>();
        int pointsUp = 0;
        int pointsDown = 0;
        foreach (RosterPlayerRow row in roster)
        {
            (int age, int health) = ageHealthById[row.PlayerId];
            if (!potentialById.TryGetValue(row.PlayerId, out PlayerPotentialRow pot))
            {
                // No ceiling row (defensive — the v10 backfill covers every
                // pre-existing player, creation paths write their own): the
                // backfill semantics, potential = current, decline-only.
                pot = new PlayerPotentialRow
                {
                    PlayerId = row.PlayerId,
                    BatPower = row.BatPower,
                    BatContact = row.BatContact,
                    BatDiscipline = row.BatDiscipline,
                    PitStuff = row.PitStuff,
                    PitControl = row.PitControl,
                    PitStamina = row.PitStamina,
                    Fielding = row.Fielding,
                };
            }

            double practiceFrac =
                practiceAvatarId is not null
                && string.Equals(row.PlayerId, practiceAvatarId, StringComparison.Ordinal)
                    ? avatarPracticeFrac
                    : 0.0;
            var developed = new PlayerRatingsRow
            {
                PlayerId = row.PlayerId,
                IsPitcher = row.IsPitcher,
                BatPower = DevelopmentCurve.DevelopRating(row.BatPower, pot.BatPower, age, RatingKind.BatPower, health, practiceFrac, ref _rng),
                BatContact = DevelopmentCurve.DevelopRating(row.BatContact, pot.BatContact, age, RatingKind.BatContact, health, practiceFrac, ref _rng),
                BatDiscipline = DevelopmentCurve.DevelopRating(row.BatDiscipline, pot.BatDiscipline, age, RatingKind.BatDiscipline, health, practiceFrac, ref _rng),
                PitStuff = DevelopmentCurve.DevelopRating(row.PitStuff, pot.PitStuff, age, RatingKind.PitStuff, health, practiceFrac, ref _rng),
                PitControl = DevelopmentCurve.DevelopRating(row.PitControl, pot.PitControl, age, RatingKind.PitControl, health, practiceFrac, ref _rng),
                PitStamina = DevelopmentCurve.DevelopRating(row.PitStamina, pot.PitStamina, age, RatingKind.PitStamina, health, practiceFrac, ref _rng),
                Fielding = DevelopmentCurve.DevelopRating(row.Fielding, pot.Fielding, age, RatingKind.Fielding, health, practiceFrac, ref _rng),
            };

            int delta =
                (developed.BatPower - row.BatPower) + (developed.BatContact - row.BatContact)
                + (developed.BatDiscipline - row.BatDiscipline) + (developed.PitStuff - row.PitStuff)
                + (developed.PitControl - row.PitControl) + (developed.PitStamina - row.PitStamina)
                + (developed.Fielding - row.Fielding);
            bool changed =
                developed.BatPower != row.BatPower || developed.BatContact != row.BatContact
                || developed.BatDiscipline != row.BatDiscipline || developed.PitStuff != row.PitStuff
                || developed.PitControl != row.PitControl || developed.PitStamina != row.PitStamina
                || developed.Fielding != row.Fielding;
            if (!changed)
            {
                continue;
            }
            writes.Add(developed);
            if (delta > 0)
            {
                pointsUp += delta;
            }
            else
            {
                pointsDown -= delta;
            }
        }

        if (writes.Count == 0)
        {
            // Nothing moved: a complete no-op — no flush, no re-init, the
            // world stays bit-identical (the empty-ledger-neutrality bar).
            // A present practice credit is still spent (the winter's training
            // happened; §4 forbids banking it) — one KV write, sim untouched.
            if (hadPracticeCredit)
            {
                _gameState.SetInt64(GameStateKeys.AvatarPracticeCredit, 0);
            }
            LastRun = new DevelopmentSummary(completedSeasonYear, 0, 0, 0);
            return;
        }

        // ---- §5: flush before re-init — the new season's day-1 games are
        // already in the sims' in-memory arrays (DayAdvanced dispatches before
        // SeasonRolledOver), and the reload must lose nothing. ----
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            if (_leagues.TryGet((LeagueTier)t, out LeagueSimulator sim))
            {
                sim.FlushPending();
            }
        }

        // ---- one batch for every rating write (the one-transaction-per-tick
        // rule); the consumed practice credit clears in the same transaction,
        // so a rollback loses neither the development nor the credit. ----
        _db.BeginBatch();
        try
        {
            foreach (PlayerRatingsRow write in writes)
            {
                _baseball.UpsertRatings(in write);
            }
            if (hadPracticeCredit)
            {
                _gameState.SetInt64(GameStateKeys.AvatarPracticeCredit, 0);
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        // ---- every registered tier sim + the micro-sim re-bulk-load so
        // TierEffects re-bakes the developed ratings; the avatar re-resolves
        // to the same team/slot against the fresh arrays (defensive parity
        // with the 9c tail — development never moves anyone). ----
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

        LastRun = new DevelopmentSummary(completedSeasonYear, writes.Count, pointsUp, pointsDown);
    }
}
