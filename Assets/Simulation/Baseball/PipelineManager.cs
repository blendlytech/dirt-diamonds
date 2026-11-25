using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// The Sunday lock's pre-designation hook (13-6 brief §4 item 5):
/// <see cref="RosterManager"/> calls it first inside every Pro lock. A league
/// with every team at the 24-player org cap is a no-op (0 B, no re-init).
/// </summary>
public interface IFreeAgencyMarket
{
    /// <summary>Signs free agents for teams below the cap; returns the number signed.</summary>
    int FillBeforeLock(int seasonYear, long lockDay);
}

/// <summary>What the most recent rollover pass did (UI and harness visibility).</summary>
public readonly record struct PipelineSummary(
    int SeasonYear,
    int RosteredRetirements,
    int FreeAgentRetirements,
    int HsGraduates,
    int UnrecruitedSeniors,
    int OutsideRecruitsJoined,
    int Draftees,
    int UndraftedSeniors,
    int ProReleases,
    int Intake,
    int Displaced,
    bool AvatarMoved,
    bool AvatarReleased,
    bool AvatarTookOver,
    bool AvatarGameOver,
    bool ReInitialized);

/// <summary>The avatar's pending recruiting decision (days 274–317), when <see cref="PipelineManager.AutopilotPipeline"/> is off.</summary>
public readonly record struct PendingRecruitingChoice(int SeasonYear, IReadOnlyList<RecruitingOfferRow> Offers, bool IsWalkOn);

/// <summary>The avatar's pending declaration decision (days 176–182), when <see cref="PipelineManager.AutopilotPipeline"/> is off.</summary>
public readonly record struct PendingDeclaration(int SeasonYear, int ClassYear, int ProjectedSlot, int ProjectedRound);

/// <summary>
/// The slice 13-6 pipeline (league_restructure.md §9; brief D1–D8): HS
/// seniors are recruited into College in the fall, College players declare
/// and are drafted in July (4 rounds × 32 picks, reverse Pro standings),
/// drafted players report to Pro the next spring, and Pro teams over the org
/// cap release their lowest-value players into a free-agent pool that signs
/// only at the Sunday lock. It replaces the 9c PromotionManager in its
/// subscription slot (the last <see cref="SeasonRolledOverEvent"/> handler,
/// after aging and development) and its boot draw.
///
/// <b>Calendar (D2-A)</b>: <see cref="PipelineCalendar"/>'s days 176, 183,
/// 195, 274 and 318, the rollover, and the Pro Sunday locks.
///
/// <b>Determinism</b>: no draw from any sim's stream, the career's or the
/// postseason's. Every draw comes from a stream derived from the persisted
/// <c>pipeline_seed</c> by (season, purpose) and is never held across days;
/// everything else is a pure function of persisted state, ties by player_id
/// (or team_id). Same save ⇒ same pipeline, reload included.
///
/// <b>Collaborators are optional (C3)</b>: any subset of tier sims, no
/// <see cref="Career"/> (then the save's avatar, if any, is frozen: never
/// moved), no <see cref="Micro"/>, no RosterManager. A missing sim is simply
/// not re-initialized.
///
/// Load-time-class code: it runs a few times a season and may allocate; the
/// game-day path is one switch on the day of season. Baseball-only: never
/// references the Life sim; Life, Narrative and UI learn about it through
/// Core events (primitives) and the database.
/// </summary>
public sealed class PipelineManager : IFreeAgencyMarket
{
    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly BaseballQueries _baseball;
    private readonly GameStateQueries _gameState;
    private readonly GlobalState _state;
    private readonly LeagueDirectory _leagues;
    private readonly PipelineQueries _queries;
    private readonly PlayoffQueries _playoffs;
    private readonly Action<DayAdvancedEvent> _onDayAdvanced;
    private readonly Action<SeasonRolledOverEvent> _onSeasonRolledOver;
    private EventBus? _bus;
    private ulong _seed;
    private bool _seedKnown;

    /// <summary>The attended-game micro-sim, re-initialized after any roster move. Null: no micro-sim to refresh.</summary>
    public MicroGame? Micro;

    /// <summary>The avatar's owner (D4, D4b-C). Null: an NPC-only world; the save's avatar (if any) is never moved.</summary>
    public CareerManager? Career;

    /// <summary>
    /// True (default, headless): the avatar's recruiting and declaration
    /// decisions resolve at their deadlines by the autopilot rule and no
    /// pending choice is exposed. False (the live game): the choices are
    /// pending for the phone; an unanswered one still resolves at its
    /// deadline. The clock never stops for either.
    /// </summary>
    public bool AutopilotPipeline = true;

    /// <summary>The rating spread of pipeline-generated players (intake, outside recruits); calibration worlds pass 0.</summary>
    public int RatingSpread = LeagueGenerator.DefaultRatingSpread;

    public PipelineSummary LastRun { get; private set; }

    /// <summary>Free agents signed at locks since construction.</summary>
    public int FreeAgentSignings { get; private set; }

    /// <summary>Lock-time re-initializations since construction (one per lock that signed anyone).</summary>
    public int LockReinitializations { get; private set; }

    /// <summary>The last lock's signings.</summary>
    public int LastLockSignings { get; private set; }

    /// <summary>NPC and avatar declarers at the last declaration deadline.</summary>
    public int LastDeclarers { get; private set; }

    /// <summary>Recruits signed at the last Signing Day (HS seniors + outside recruits), and the outside share.</summary>
    public int LastRecruitsSigned { get; private set; }
    public int LastOutsideRecruitsSigned { get; private set; }
    public int LastOutsideRecruitsRolled { get; private set; }

    /// <summary>Calendar steps run since construction (harness visibility: a reload must not re-run a done step).</summary>
    public int DraftsRun { get; private set; }
    public int SigningDaysRun { get; private set; }
    public int OffersRun { get; private set; }

    public PipelineManager(
        DatabaseManager db, PlayerQueries players, BaseballQueries baseball,
        GameStateQueries gameState, GlobalState state, LeagueDirectory leagues)
    {
        _db = db;
        _players = players;
        _baseball = baseball;
        _gameState = gameState;
        _state = state;
        _leagues = leagues;
        _queries = new PipelineQueries(db);
        _playoffs = new PlayoffQueries(db);
        _onDayAdvanced = OnDayAdvanced;
        _onSeasonRolledOver = OnSeasonRolledOver;
    }

    public void AttachTo(EventBus bus)
    {
        _bus = bus;
        bus.Subscribe(_onDayAdvanced);
        bus.Subscribe(_onSeasonRolledOver);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onDayAdvanced);
        bus.Unsubscribe(_onSeasonRolledOver);
        if (ReferenceEquals(_bus, bus))
        {
            _bus = null;
        }
    }

    // ------------------------------------------------------------------
    // Seed and boot
    // ------------------------------------------------------------------

    /// <summary>
    /// The persisted pipeline seed (Q8): Game_State.pipeline_seed, or
    /// <see cref="PipelineProfile.DefaultPipelineSeed"/> in a fixture that
    /// never wrote it.
    /// </summary>
    public ulong Seed
    {
        get
        {
            if (!_seedKnown)
            {
                _seed = _gameState.TryGetInt64(GameStateKeys.PipelineSeed, out long stored)
                    ? unchecked((ulong)stored)
                    : PipelineProfile.DefaultPipelineSeed;
                _seedKnown = true;
            }
            return _seed;
        }
    }

    /// <summary>
    /// Writes <c>pipeline_seed</c> once from the boot draw PromotionManager
    /// used to take (§1.2 #7). The caller draws whether or not the key exists,
    /// so every later boot consumer's stream is unchanged.
    /// </summary>
    public void EnsureSeed(ulong bootDraw)
    {
        if (_gameState.TryGetInt64(GameStateKeys.PipelineSeed, out long stored))
        {
            _seed = unchecked((ulong)stored);
        }
        else
        {
            _db.RunInBatch(() => _gameState.SetInt64(GameStateKeys.PipelineSeed, unchecked((long)bootDraw)));
            _seed = bootDraw;
        }
        _seedKnown = true;
    }

    /// <summary>
    /// The boot ensure (D2-A, Amendment 3): every College-rostered player gets
    /// a class-year row (world-gen and any straggler), then a crash between a
    /// calendar day's commit and the pipeline's re-runs that day's step,
    /// keyed on its persisted output, so each step is done exactly once.
    /// Call after the career has loaded its avatar.
    /// </summary>
    public void EnsureAtBoot(long today)
    {
        if (today < 1)
        {
            return;
        }
        int seasonYear = _state.SeasonYearForDay(today);
        int dayOfSeason = GlobalState.DayOfSeasonForDay(today);
        EnsureCollegeEnrollment(seasonYear);
        if (dayOfSeason >= PipelineCalendar.DraftDay)
        {
            RunDraft(seasonYear, today - (dayOfSeason - PipelineCalendar.DraftDay));
        }
        else if (dayOfSeason >= PipelineCalendar.DeclarationDeadlineDay)
        {
            RunDeclaration(seasonYear, today - (dayOfSeason - PipelineCalendar.DeclarationDeadlineDay));
        }
        if (dayOfSeason >= PipelineCalendar.SigningDay)
        {
            RunSigningDay(seasonYear, today - (dayOfSeason - PipelineCalendar.SigningDay));
        }
        else if (dayOfSeason >= PipelineCalendar.OffersDay)
        {
            RunOffers(seasonYear, today - (dayOfSeason - PipelineCalendar.OffersDay));
        }
    }

    /// <summary>Backfills a class-year row for every College-rostered player without one: class = clamp(age − 17, 1, 4).</summary>
    public int EnsureCollegeEnrollment(int seasonYear)
    {
        var roster = new List<RosterPlayerRow>(RosterCapacity.MaxTeamsPerTier * RosterCapacity.MaxRoster);
        _baseball.LoadRosterByTier(LeagueTier.College, roster);
        if (roster.Count == 0)
        {
            return 0;
        }
        var rows = new List<CollegeEnrollmentRow>();
        _queries.LoadEnrollments(rows);
        var enrolled = new HashSet<string>(StringComparer.Ordinal);
        foreach (CollegeEnrollmentRow row in rows)
        {
            enrolled.Add(row.PlayerId);
        }
        var missing = new List<(string Id, int TeamId, int Age)>();
        foreach (RosterPlayerRow row in roster)
        {
            if (!enrolled.Contains(row.PlayerId) && _players.TryGetById(row.PlayerId, out PlayerRow player))
            {
                missing.Add((row.PlayerId, row.TeamId, player.Age));
            }
        }
        if (missing.Count == 0)
        {
            return 0;
        }
        _db.RunInBatch(() =>
        {
            foreach ((string id, int teamId, int age) in missing)
            {
                WriteBootEnrollment(id, teamId, age, seasonYear);
            }
        });
        return missing.Count;
    }

    private void WriteBootEnrollment(string playerId, int teamId, int age, int seasonYear)
    {
        int classYear = Math.Clamp(age - PipelineProfile.BootClassYearAgeOffset, 1, PipelineProfile.FinalClassYear);
        _queries.UpsertEnrollment(new CollegeEnrollmentRow
        {
            PlayerId = playerId,
            TeamId = teamId,
            EntrySeason = seasonYear - (classYear - 1),
            ClassYear = classYear,
            DeclaredSeason = null,
        });
    }

    // ------------------------------------------------------------------
    // The calendar
    // ------------------------------------------------------------------

    private void OnDayAdvanced(DayAdvancedEvent e)
    {
        int d = e.DayOfSeason;
        if (d == PipelineCalendar.DeclarationOpenDay)
        {
            OpenDeclaration(e.SeasonYear, e.Day);
        }
        else if (d == PipelineCalendar.DeclarationDeadlineDay)
        {
            RunDeclaration(e.SeasonYear, e.Day);
        }
        else if (d == PipelineCalendar.DraftDay)
        {
            RunDraft(e.SeasonYear, e.Day);
        }
        else if (d == PipelineCalendar.OffersDay)
        {
            RunOffers(e.SeasonYear, e.Day);
        }
        else if (d == PipelineCalendar.SigningDay)
        {
            RunSigningDay(e.SeasonYear, e.Day);
        }
    }

    private void OnSeasonRolledOver(SeasonRolledOverEvent e) => RunRollover(e.PreviousSeasonYear, e.NewSeasonYear);

    // ------------------------------------------------------------------
    // The avatar's eligibility
    // ------------------------------------------------------------------

    /// <summary>The avatar the pipeline moves this pass, or null (no career, released, game over, a pending succession).</summary>
    private string? ActiveAvatarId() =>
        Career is { HasAvatar: true, IsAvatarRostered: true, IsLineageOver: false, HasPendingSuccessionChoice: false } career
            ? career.AvatarPlayerId
            : null;

    /// <summary>A rostered avatar the pipeline must leave exactly where he is (no career attached, game over, a pending succession).</summary>
    private string? FrozenAvatarId()
    {
        if (Career is null)
        {
            return _gameState.TryGetText(GameStateKeys.AvatarPlayerId, out string id) && id.Length > 0 ? id : null;
        }
        return Career.HasAvatar && ActiveAvatarId() is null ? Career.AvatarPlayerId : null;
    }

    private bool HasActiveFlag(string playerId, string flagName)
    {
        var ids = new List<string>();
        _baseball.LoadActiveFlagPlayerIds(flagName, ids);
        return ids.Contains(playerId);
    }

    /// <summary>D6-A: the record dock applies to the avatar only; banned_for_life has no 13-6 effect.</summary>
    private bool AvatarRecordDock(string? avatarId) =>
        avatarId is not null && HasActiveFlag(avatarId, CareerRecordFlags.RecordArrest);

    // ------------------------------------------------------------------
    // Snapshots (bulk reads up front, never row-at-a-time mid-pass)
    // ------------------------------------------------------------------

    private sealed class Snapshot
    {
        public readonly Dictionary<string, PlayerRow> Players = new(StringComparer.Ordinal);
        public readonly Dictionary<int, LeagueTier> TierByTeam = new();
        public readonly List<TeamRow> Teams = new();
        public readonly List<RosterPlayerRow> Roster = new();
        public readonly Dictionary<string, PlayerPotentialRow> Potential = new(StringComparer.Ordinal);
        public readonly Dictionary<string, SeasonBattingLine> Batting = new(StringComparer.Ordinal);
        public readonly Dictionary<string, SeasonPitchingLine> Pitching = new(StringComparer.Ordinal);
        public readonly Dictionary<string, CollegeEnrollmentRow> Enrollment = new(StringComparer.Ordinal);
        public readonly Dictionary<string, PlayerFieldingRow> Fielding = new(StringComparer.Ordinal);
        public readonly double[] LeagueOps = new double[LeagueDirectory.TierCount];
        public readonly double[] LeagueEra = new double[LeagueDirectory.TierCount];
        public int StatSeasonYear;

        public PipelineCandidateRow Candidate(in RosterPlayerRow row)
        {
            Players.TryGetValue(row.PlayerId, out PlayerRow player);
            var c = new PipelineCandidateRow
            {
                Ratings = row,
                Age = player.Age,
                HealthCeiling = player.HealthCeiling,
                HasPotential = Potential.TryGetValue(row.PlayerId, out PlayerPotentialRow pot),
                Potential = pot,
                Tier = TierByTeam.TryGetValue(row.TeamId, out LeagueTier tier) ? tier : LeagueTier.HS,
            };
            Batting.TryGetValue(row.PlayerId, out c.Batting);
            Pitching.TryGetValue(row.PlayerId, out c.Pitching);
            return c;
        }

        public FieldingPosition PositionOf(string playerId, PitcherRole role) =>
            role != PitcherRole.None ? FieldingPosition.P
            : Fielding.TryGetValue(playerId, out PlayerFieldingRow f) ? f.DefensivePosition
            : FieldingPosition.C;
    }

    private static readonly LeagueTier[] AllTiers = { LeagueTier.HS, LeagueTier.College, LeagueTier.Pro };
    private static readonly LeagueTier[] CollegeOnly = { LeagueTier.College };
    private static readonly LeagueTier[] CollegeAndPro = { LeagueTier.College, LeagueTier.Pro };
    private static readonly LeagueTier[] HsAndCollege = { LeagueTier.HS, LeagueTier.College };

    /// <summary>
    /// One step's bulk reads: the rostered players of <paramref name="tiers"/>
    /// (ratings, role, age, health, potential through the C5 candidate join,
    /// tier order HS → College → Pro, so the roster join's (team_id,
    /// player_id) order holds), the completed season's lines and league
    /// totals, enrollment, and (when asked) fielding. <see cref="Snapshot.Players"/>
    /// holds only those rostered players.
    /// </summary>
    private Snapshot LoadSnapshot(int statSeasonYear, bool fielding, LeagueTier[]? tiers = null)
    {
        var s = new Snapshot { StatSeasonYear = statSeasonYear };
        _baseball.LoadAllTeams(s.Teams);
        foreach (TeamRow team in s.Teams)
        {
            s.TierByTeam[team.TeamId] = team.Tier;
        }
        var candidates = new List<PipelineCandidateRow>(RosterCapacity.MaxTeamsPerTier * RosterCapacity.MaxRoster);
        foreach (LeagueTier tier in tiers ?? AllTiers)
        {
            _queries.AppendCandidatesByTier(tier, candidates);
        }
        foreach (PipelineCandidateRow c in candidates)
        {
            s.Roster.Add(c.Ratings);
            s.Players[c.Ratings.PlayerId] = new PlayerRow
            {
                PlayerId = c.Ratings.PlayerId, Age = c.Age, HealthCeiling = c.HealthCeiling, TeamId = c.Ratings.TeamId,
            };
            if (c.HasPotential)
            {
                s.Potential[c.Ratings.PlayerId] = c.Potential;
            }
        }
        var bat = new List<SeasonBattingLine>();
        _baseball.LoadSeasonBattingLines(statSeasonYear, bat);
        foreach (SeasonBattingLine line in bat)
        {
            s.Batting[line.PlayerId] = line;
        }
        var pit = new List<SeasonPitchingLine>();
        _baseball.LoadSeasonPitchingLines(statSeasonYear, pit);
        foreach (SeasonPitchingLine line in pit)
        {
            s.Pitching[line.PlayerId] = line;
        }
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            LeagueBattingTotals bt = _baseball.LoadLeagueBattingTotals(statSeasonYear, (LeagueTier)t);
            LeaguePitchingTotals pt = _baseball.LoadLeaguePitchingTotals(statSeasonYear, (LeagueTier)t);
            s.LeagueOps[t] = PromotionScore.LeagueOps(in bt);
            s.LeagueEra[t] = PromotionScore.LeagueEra(in pt);
        }
        var enrollment = new List<CollegeEnrollmentRow>();
        _queries.LoadEnrollments(enrollment);
        foreach (CollegeEnrollmentRow row in enrollment)
        {
            s.Enrollment[row.PlayerId] = row;
        }
        if (fielding)
        {
            var rows = new List<PlayerFieldingRow>();
            _baseball.LoadAllFielding(rows);
            foreach (PlayerFieldingRow row in rows)
            {
                s.Fielding[row.PlayerId] = row;
            }
        }
        return s;
    }

    private double ValueOf(Snapshot s, in RosterPlayerRow row, LeagueTier tier, bool neutral, bool dock)
    {
        PipelineCandidateRow c = s.Candidate(in row);
        return PipelineRules.Value(in c, tier, s.LeagueOps[(int)tier], s.LeagueEra[(int)tier], neutral, dock);
    }

    private static int RoleIndex(PitcherRole role) => role switch
    {
        PitcherRole.None => 0,
        PitcherRole.Starter => 1,
        _ => 2,
    };

    // ------------------------------------------------------------------
    // The draft board (days 176, 183, 195, the status view)
    // ------------------------------------------------------------------

    private sealed class BoardEntry
    {
        public PipelineChoice Choice;
        public int TeamId;
        public int ClassYear;
        public int Age;
        public int? DeclaredSeason;
    }

    /// <summary>The eligible board (class ≥ 2, College-rostered), best first; the frozen avatar is left off.</summary>
    private List<BoardEntry> BuildBoard(Snapshot s, string? avatarId, bool avatarDock)
    {
        string? frozen = FrozenAvatarId();
        var board = new List<BoardEntry>();
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (!s.TierByTeam.TryGetValue(row.TeamId, out LeagueTier tier) || tier != LeagueTier.College
                || !s.Enrollment.TryGetValue(row.PlayerId, out CollegeEnrollmentRow en)
                || en.ClassYear < PipelineProfile.DraftEligibleClassYear
                || string.Equals(row.PlayerId, frozen, StringComparison.Ordinal))
            {
                continue;
            }
            bool isAvatar = string.Equals(row.PlayerId, avatarId, StringComparison.Ordinal);
            board.Add(new BoardEntry
            {
                Choice = new PipelineChoice
                {
                    PlayerId = row.PlayerId,
                    Role = row.Role,
                    Value = ValueOf(s, in row, LeagueTier.College, neutral: false, dock: isAvatar && avatarDock),
                },
                TeamId = row.TeamId,
                ClassYear = en.ClassYear,
                Age = s.Players.TryGetValue(row.PlayerId, out PlayerRow p) ? p.Age : 0,
                DeclaredSeason = en.DeclaredSeason,
            });
        }
        board.Sort(static (a, b) => PipelineRules.CompareChoice(a.Choice, b.Choice));
        return board;
    }

    private int ProTeamCount()
    {
        int count = _baseball.CountTeamsInTier(LeagueTier.Pro);
        return count > 0 ? count : RosterCapacity.MaxTeamsPerTier;
    }

    /// <summary>Day 176: the avatar's declaration window (the phone's cue). Writes nothing; the pending choice is derived from the database.</summary>
    private void OpenDeclaration(int seasonYear, long day)
    {
        string? avatarId = ActiveAvatarId();
        if (avatarId is null || !TryAvatarBoardSlot(seasonYear, avatarId, out int classYear, out int slot))
        {
            return;
        }
        _bus?.Publish(new DraftDeclarationOpenedEvent(
            avatarId, seasonYear, day, slot, PipelineRules.ProjectedRound(slot, ProTeamCount()),
            classYear >= PipelineProfile.FinalClassYear));
    }

    private bool TryAvatarBoardSlot(int seasonYear, string avatarId, out int classYear, out int slot)
    {
        classYear = 0;
        slot = 0;
        Snapshot s = LoadSnapshot(seasonYear, fielding: false, CollegeOnly);
        if (!s.Enrollment.TryGetValue(avatarId, out CollegeEnrollmentRow en) || en.ClassYear < PipelineProfile.DraftEligibleClassYear)
        {
            return false;
        }
        classYear = en.ClassYear;
        List<BoardEntry> board = BuildBoard(s, avatarId, AvatarRecordDock(avatarId));
        for (int i = 0; i < board.Count; i++)
        {
            if (string.Equals(board[i].Choice.PlayerId, avatarId, StringComparison.Ordinal))
            {
                slot = i + 1;
                return true;
            }
        }
        return false;
    }

    private bool AvatarDeclinedIn(string avatarId, int seasonYear)
    {
        var flags = new List<EntityFlagRow>();
        _players.LoadActiveFlags(avatarId, flags);
        foreach (EntityFlagRow flag in flags)
        {
            if (flag.FlagName == PipelineFlags.DeclarationDeclined && flag.SetOnDay is long day
                && _state.SeasonYearForDay(day) == seasonYear)
            {
                return true;
            }
        }
        return false;
    }

    // The pending getters are read by the phone every frame: their answer is
    // cached for the day and dropped when an answer (or a calendar step) writes.
    private long _pendingCacheDay = -1;
    private bool _pendingDeclarationKnown;
    private bool _hasPendingDeclaration;
    private PendingDeclaration _pendingDeclaration;
    private bool _pendingRecruitingKnown;
    private bool _hasPendingRecruiting;
    private PendingRecruitingChoice _pendingRecruiting;

    private void RefreshPendingCache()
    {
        if (_pendingCacheDay != _state.CurrentDay)
        {
            _pendingCacheDay = _state.CurrentDay;
            _pendingDeclarationKnown = false;
            _pendingRecruitingKnown = false;
        }
    }

    /// <summary>The avatar's declaration decision, days 176–182 (D5-A), when the pipeline isn't on autopilot.</summary>
    public bool TryGetPendingDeclaration(out PendingDeclaration pending)
    {
        RefreshPendingCache();
        if (!_pendingDeclarationKnown)
        {
            _hasPendingDeclaration = ComputePendingDeclaration(out _pendingDeclaration);
            _pendingDeclarationKnown = true;
        }
        pending = _pendingDeclaration;
        return _hasPendingDeclaration;
    }

    private bool ComputePendingDeclaration(out PendingDeclaration pending)
    {
        pending = default;
        string? avatarId = ActiveAvatarId();
        int dayOfSeason = _state.DayOfSeason;
        if (AutopilotPipeline || avatarId is null
            || dayOfSeason < PipelineCalendar.DeclarationOpenDay || dayOfSeason >= PipelineCalendar.DeclarationDeadlineDay)
        {
            return false;
        }
        int seasonYear = _state.SeasonYear;
        if (!TryAvatarBoardSlot(seasonYear, avatarId, out int classYear, out int slot)
            || classYear >= PipelineProfile.FinalClassYear || AvatarDeclinedIn(avatarId, seasonYear))
        {
            return false;
        }
        var rows = new List<CollegeEnrollmentRow>();
        _queries.LoadEnrollments(rows);
        foreach (CollegeEnrollmentRow row in rows)
        {
            if (row.PlayerId == avatarId && row.DeclaredSeason == seasonYear)
            {
                return false; // already declared
            }
        }
        pending = new PendingDeclaration(seasonYear, classYear, slot, PipelineRules.ProjectedRound(slot, ProTeamCount()));
        return true;
    }

    /// <summary>The phone's answer (persisted at once, so a reload keeps it and it resolves once).</summary>
    public void ResolveDeclaration(bool declare)
    {
        if (!TryGetPendingDeclaration(out PendingDeclaration pending))
        {
            throw new InvalidOperationException("No declaration decision is pending.");
        }
        string avatarId = ActiveAvatarId()!;
        long day = _state.CurrentDay;
        _db.RunInBatch(() =>
        {
            if (declare)
            {
                _queries.SetDeclared(avatarId, pending.SeasonYear);
            }
            else
            {
                _players.SetFlag(avatarId, PipelineFlags.DeclarationDeclined, true, day);
            }
        });
        _pendingCacheDay = -1;
    }

    /// <summary>
    /// Day 183, the declaration deadline (D2-A, Amendment 3): every eligible
    /// NPC declares by <see cref="PipelineRules.ShouldNpcDeclare"/>; the
    /// avatar's explicit answer stands, a class-4 senior is always in the
    /// pool, and an unanswered avatar resolves by the NPC rule. Idempotent
    /// until the Draft runs.
    /// </summary>
    public void RunDeclaration(int seasonYear, long day)
    {
        if (_queries.CountDraftPicks(seasonYear) > 0)
        {
            return;
        }
        string? avatarId = ActiveAvatarId();
        Snapshot s = LoadSnapshot(seasonYear, fielding: false, CollegeOnly);
        List<BoardEntry> board = BuildBoard(s, avatarId, AvatarRecordDock(avatarId));
        bool avatarDeclined = avatarId is not null && AvatarDeclinedIn(avatarId, seasonYear);
        var declare = new List<string>();
        int declarers = 0;
        for (int i = 0; i < board.Count; i++)
        {
            BoardEntry e = board[i];
            bool isAvatar = string.Equals(e.Choice.PlayerId, avatarId, StringComparison.Ordinal);
            bool declares;
            if (isAvatar && e.ClassYear >= PipelineProfile.FinalClassYear)
            {
                declares = true;
            }
            else if (isAvatar && e.DeclaredSeason == seasonYear)
            {
                declares = true;
            }
            else if (isAvatar && avatarDeclined)
            {
                declares = false;
            }
            else
            {
                declares = PipelineRules.ShouldNpcDeclare(e.ClassYear, e.Age, i + 1);
            }
            if (declares)
            {
                declarers++;
                if (e.DeclaredSeason != seasonYear)
                {
                    declare.Add(e.Choice.PlayerId);
                }
            }
        }
        LastDeclarers = declarers;
        if (declare.Count == 0)
        {
            return;
        }
        _db.RunInBatch(() =>
        {
            foreach (string id in declare)
            {
                _queries.SetDeclared(id, seasonYear);
            }
        });
    }

    // ------------------------------------------------------------------
    // The Draft (day 195)
    // ------------------------------------------------------------------

    /// <summary>The declaration deadline's output exists: an NPC row declared this season (the avatar's own answer can predate the day).</summary>
    private bool AnyDeclared(int seasonYear)
    {
        string? avatarId = Career is { HasAvatar: true } c ? c.AvatarPlayerId : null;
        var rows = new List<CollegeEnrollmentRow>();
        _queries.LoadEnrollments(rows);
        foreach (CollegeEnrollmentRow row in rows)
        {
            if (row.DeclaredSeason == seasonYear && !string.Equals(row.PlayerId, avatarId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The Draft order for <paramref name="seasonYear"/>: reverse prior-season
    /// Pro order (elimination round, then standings) when the prior season's
    /// Team_Seasons rows exist, else the seeded inaugural order (Q5).
    /// </summary>
    public List<int> DraftOrder(int seasonYear)
    {
        var proTeams = new List<TeamRow>(RosterCapacity.MaxTeamsPerTier);
        _baseball.LoadTeamsByTier(LeagueTier.Pro, proTeams);
        var teamIds = new List<int>(proTeams.Count);
        foreach (TeamRow team in proTeams)
        {
            teamIds.Add(team.TeamId);
        }
        teamIds.Sort();
        int prior = seasonYear - 1;
        var seasons = new List<TeamSeasonRow>();
        _playoffs.LoadTeamSeasons(prior, LeagueTier.Pro, seasons);
        if (seasons.Count < teamIds.Count)
        {
            return PipelineRules.InauguralDraftOrder(teamIds, Seed, seasonYear);
        }
        ulong scheduleSeed = SeasonScheduler.ResolveSeed(_gameState);
        var games = new List<CompletedGame>();
        _playoffs.LoadCompletedRegularSeason(prior, LeagueTier.Pro, games);
        List<int> standings;
        if (games.Count > 0)
        {
            var table = new SeasonTable();
            table.Build(teamIds.ToArray(), games);
            standings = Standings.Order(table, teamIds, scheduleSeed, prior, LeagueTier.Pro);
        }
        else
        {
            standings = PipelineRules.TableOnlyStandings(seasons, scheduleSeed, prior, LeagueTier.Pro);
        }
        var buckets = new Dictionary<int, int>(teamIds.Count);
        foreach (int teamId in teamIds)
        {
            buckets[teamId] = 0;
        }
        var series = new List<PlayoffSeriesRow>();
        _playoffs.LoadSeries(prior, LeagueTier.Pro, series);
        int finalRound = PostseasonCalendar.FinalRound(LeagueTier.Pro);
        foreach (PlayoffSeriesRow row in series)
        {
            if (row.WinnerTeamId is not int winner)
            {
                continue;
            }
            int loser = winner == row.HighSeedTeamId ? row.LowSeedTeamId : row.HighSeedTeamId;
            buckets[loser] = Math.Max(buckets[loser], row.Round);
            if (row.Round == finalRound)
            {
                buckets[winner] = finalRound + 1;
            }
        }
        return PipelineRules.PriorSeasonDraftOrder(standings, buckets);
    }

    /// <summary>
    /// Day 195 (D2-A, Amendment 5): 128 picks in one batch on the board of
    /// declared players. Writes Draft_Picks, a pipeline_pro_signed flag per
    /// pick, declared_season = NULL for undrafted underclassmen (they
    /// return), and the avatar's signing bonus; then publishes the funds
    /// impulse, ProContractSignedEvent per pick and DraftCompletedEvent.
    /// Drafted players stay on their College team until the rollover.
    /// </summary>
    public void RunDraft(int seasonYear, long day)
    {
        if (_queries.CountDraftPicks(seasonYear) > 0)
        {
            return;
        }
        if (!AnyDeclared(seasonYear))
        {
            RunDeclaration(seasonYear, day); // a crash catch-up may arrive without the day-183 step
        }
        string? avatarId = ActiveAvatarId();
        Snapshot s = LoadSnapshot(seasonYear, fielding: false, CollegeAndPro);
        List<BoardEntry> eligible = BuildBoard(s, avatarId, AvatarRecordDock(avatarId));
        var board = new List<PipelineChoice>();
        var byId = new Dictionary<string, BoardEntry>(StringComparer.Ordinal);
        foreach (BoardEntry e in eligible)
        {
            if (e.DeclaredSeason == seasonYear)
            {
                board.Add(e.Choice);
                byId[e.Choice.PlayerId] = e;
            }
        }
        List<int> order = DraftOrder(seasonYear);
        if (order.Count == 0)
        {
            return;
        }

        // Known departures: a Pro player who retires at the next rollover leaves a role short.
        var teamIndex = new Dictionary<int, int>(order.Count);
        for (int i = 0; i < order.Count; i++)
        {
            teamIndex[order[i]] = i;
        }
        var roleCount = new int[order.Count * 3];
        var drafted = new int[order.Count * 3];
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (teamIndex.TryGetValue(row.TeamId, out int t) && s.Players.TryGetValue(row.PlayerId, out PlayerRow p)
                && !PipelineRules.RetiresAtNextRollover(p.Age, p.HealthCeiling))
            {
                roleCount[t * 3 + RoleIndex(row.Role)]++;
            }
        }

        var picks = new List<DraftPickRow>(order.Count * PipelineProfile.DraftRounds);
        int overall = 0;
        for (int round = 1; round <= PipelineProfile.DraftRounds; round++)
        {
            for (int k = 0; k < order.Count; k++)
            {
                int pick = PipelineRules.SelectDraftPick(board,
                    roleCount.AsSpan(k * 3, 3), drafted.AsSpan(k * 3, 3));
                if (pick < 0)
                {
                    break;
                }
                PipelineChoice c = board[pick];
                c.Taken = true;
                board[pick] = c;
                roleCount[k * 3 + RoleIndex(c.Role)]++;
                drafted[k * 3 + RoleIndex(c.Role)]++;
                overall++;
                picks.Add(new DraftPickRow
                {
                    SeasonYear = seasonYear, OverallPick = overall, Round = round, TeamId = order[k], PlayerId = c.PlayerId,
                });
            }
        }

        int avatarPick = 0;
        int avatarTeam = 0;
        foreach (DraftPickRow p in picks)
        {
            if (string.Equals(p.PlayerId, avatarId, StringComparison.Ordinal))
            {
                avatarPick = p.OverallPick;
                avatarTeam = p.TeamId;
            }
        }
        int bonus = PipelineRules.SigningBonusForPick(avatarPick);
        var pickedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (DraftPickRow p in picks)
        {
            pickedIds.Add(p.PlayerId);
        }
        _db.BeginBatch();
        try
        {
            foreach (DraftPickRow p in picks)
            {
                _queries.InsertDraftPick(in p);
                _players.SetFlag(p.PlayerId, PipelineFlags.ProSigned, true, day);
            }
            foreach (PipelineChoice c in board)
            {
                if (!pickedIds.Contains(c.PlayerId) && byId[c.PlayerId].ClassYear < PipelineProfile.FinalClassYear)
                {
                    _queries.SetDeclared(c.PlayerId, null); // Amendment 3: undrafted underclassmen return
                }
            }
            if (bonus > 0)
            {
                _players.AdjustFunds(avatarId!, bonus);
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        DraftsRun++;
        _pendingCacheDay = -1;
        if (bonus > 0)
        {
            _bus?.Publish(new FundsImpulseEvent(avatarId!, bonus));
        }
        foreach (DraftPickRow p in picks)
        {
            _bus?.Publish(new ProContractSignedEvent(p.PlayerId, p.TeamId, day, wasDrafted: true));
        }
        _bus?.Publish(new DraftCompletedEvent(seasonYear, day, avatarPick, avatarTeam));
    }

    // ------------------------------------------------------------------
    // Recruiting (days 274 and 318)
    // ------------------------------------------------------------------

    /// <summary>College programs in last season's national-rank order (team_id order without College standings), rank 1 first.</summary>
    private List<(int TeamId, int Rank)> RankedPrograms(Snapshot s, int seasonYear)
    {
        var ids = new List<int>();
        foreach (TeamRow team in s.Teams)
        {
            if (team.Tier == LeagueTier.College)
            {
                ids.Add(team.TeamId);
            }
        }
        ids.Sort();
        var seasons = new List<TeamSeasonRow>();
        _playoffs.LoadTeamSeasons(seasonYear, LeagueTier.College, seasons);
        var rankById = new Dictionary<int, int>();
        foreach (TeamSeasonRow row in seasons)
        {
            if (row.NationalRank is int rank)
            {
                rankById[row.TeamId] = rank;
            }
        }
        if (rankById.Count == ids.Count)
        {
            ids.Sort((a, b) =>
            {
                int byRank = rankById[a].CompareTo(rankById[b]);
                return byRank != 0 ? byRank : a.CompareTo(b);
            });
        }
        var result = new List<(int, int)>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            result.Add((ids[i], i + 1));
        }
        return result;
    }

    /// <summary>
    /// Each program's open spots by role: the target shape minus returning
    /// players (class < 4, not drafted this season, not due to retire).
    /// </summary>
    private Dictionary<int, int[]> OpenSpots(Snapshot s, IEnumerable<(int TeamId, int Rank)> programs, HashSet<string> draftedThisSeason, string? excludeId)
    {
        var open = new Dictionary<int, int[]>();
        foreach ((int teamId, _) in programs)
        {
            open[teamId] = new[]
            {
                PipelineProfile.CollegeTargetBatters, PipelineProfile.CollegeTargetStarters, PipelineProfile.CollegeTargetRelievers,
            };
        }
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (!open.TryGetValue(row.TeamId, out int[]? spots)
                || string.Equals(row.PlayerId, excludeId, StringComparison.Ordinal))
            {
                continue;
            }
            bool returning = s.Enrollment.TryGetValue(row.PlayerId, out CollegeEnrollmentRow en)
                && en.ClassYear < PipelineProfile.FinalClassYear
                && !draftedThisSeason.Contains(row.PlayerId)
                && s.Players.TryGetValue(row.PlayerId, out PlayerRow p)
                && !PipelineRules.RetiresAtNextRollover(p.Age, p.HealthCeiling);
            if (returning)
            {
                spots[RoleIndex(row.Role)]--;
            }
        }
        foreach (int[] spots in open.Values)
        {
            for (int r = 0; r < 3; r++)
            {
                spots[r] = Math.Max(0, spots[r]);
            }
        }
        return open;
    }

    private HashSet<string> DraftedIn(int seasonYear)
    {
        var picks = new List<DraftPickRow>();
        _queries.LoadDraftPicks(seasonYear, picks);
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (DraftPickRow p in picks)
        {
            set.Add(p.PlayerId);
        }
        return set;
    }

    /// <summary>True when the avatar is an HS senior this fall (on an HS team, age ≥ 19).</summary>
    private bool IsAvatarHsSenior(Snapshot s, string avatarId, out RosterPlayerRow row)
    {
        foreach (RosterPlayerRow r in s.Roster)
        {
            if (string.Equals(r.PlayerId, avatarId, StringComparison.Ordinal))
            {
                row = r;
                return s.TierByTeam.TryGetValue(r.TeamId, out LeagueTier tier) && tier == LeagueTier.HS
                    && s.Players.TryGetValue(r.PlayerId, out PlayerRow p) && p.Age >= SchoolGrades.SeniorAge;
            }
        }
        row = default;
        return false;
    }

    /// <summary>The avatar's offer computation (Amendment 4), shared by day 274 and the rollover's fallback walk-on.</summary>
    private (List<int> Offers, bool IsWalkOn, bool Displaces) ComputeAvatarOffers(Snapshot s, int seasonYear, in RosterPlayerRow avatarRow)
    {
        List<(int TeamId, int Rank)> ranked = RankedPrograms(s, seasonYear);
        Dictionary<int, int[]> open = OpenSpots(s, ranked, DraftedIn(seasonYear), avatarRow.PlayerId);
        var programs = new List<PipelineRules.Program>(ranked.Count);
        foreach ((int teamId, int rank) in ranked)
        {
            programs.Add(new PipelineRules.Program(teamId, rank, open[teamId][RoleIndex(avatarRow.Role)]));
        }
        double value = ValueOf(s, in avatarRow, LeagueTier.HS, neutral: false, dock: AvatarRecordDock(avatarRow.PlayerId));
        var offers = new List<int>();
        PipelineRules.SelectAvatarOffers(value, programs, offers, out bool walkOn, out bool displaces);
        return (offers, walkOn, displaces);
    }

    /// <summary>
    /// Day 274 (D2-A): the avatar, a senior, gets up to five offers from the
    /// programs whose floor he clears and that have an open spot in his role,
    /// or one walk-on (D4a-A). Recruiting_Offers rows (status 0), then
    /// <see cref="RecruitingOffersEvent"/>. NPCs get no offers (they're
    /// assigned on Signing Day).
    /// </summary>
    public void RunOffers(int seasonYear, long day)
    {
        string? avatarId = ActiveAvatarId();
        if (avatarId is null)
        {
            return;
        }
        var existing = new List<RecruitingOfferRow>();
        if (_queries.LoadOffersFor(avatarId, seasonYear, existing) > 0)
        {
            return;
        }
        Snapshot s = LoadSnapshot(seasonYear, fielding: false, HsAndCollege);
        if (!IsAvatarHsSenior(s, avatarId, out RosterPlayerRow avatarRow))
        {
            return;
        }
        (List<int> offers, bool walkOn, _) = ComputeAvatarOffers(s, seasonYear, in avatarRow);
        if (offers.Count == 0)
        {
            return;
        }
        _db.RunInBatch(() =>
        {
            foreach (int teamId in offers)
            {
                _queries.UpsertOffer(new RecruitingOfferRow
                {
                    PlayerId = avatarId, TeamId = teamId, SeasonYear = seasonYear, Status = RecruitingOfferStatus.Open,
                });
            }
        });
        OffersRun++;
        _pendingCacheDay = -1;
        _bus?.Publish(new RecruitingOffersEvent(avatarId, seasonYear, offers.Count, walkOn));
    }

    /// <summary>The avatar's recruiting decision (days 274–317), when the pipeline isn't on autopilot.</summary>
    public bool TryGetPendingRecruitingChoice(out PendingRecruitingChoice pending)
    {
        RefreshPendingCache();
        if (!_pendingRecruitingKnown)
        {
            _hasPendingRecruiting = ComputePendingRecruiting(out _pendingRecruiting);
            _pendingRecruitingKnown = true;
        }
        pending = _pendingRecruiting;
        return _hasPendingRecruiting;
    }

    private bool ComputePendingRecruiting(out PendingRecruitingChoice pending)
    {
        pending = default;
        string? avatarId = ActiveAvatarId();
        int dayOfSeason = _state.DayOfSeason;
        if (AutopilotPipeline || avatarId is null
            || dayOfSeason < PipelineCalendar.OffersDay || dayOfSeason >= PipelineCalendar.SigningDay)
        {
            return false;
        }
        int seasonYear = _state.SeasonYear;
        var offers = new List<RecruitingOfferRow>();
        if (_queries.LoadOffersFor(avatarId, seasonYear, offers) == 0)
        {
            return false;
        }
        foreach (RecruitingOfferRow offer in offers)
        {
            if (offer.Status != RecruitingOfferStatus.Open)
            {
                return false; // answered
            }
        }
        // A walk-on is a single offer from a program below every floor; the phone only needs the count.
        pending = new PendingRecruitingChoice(seasonYear, offers, offers.Count == 1 && IsWalkOnOffer(avatarId, seasonYear, offers[0].TeamId));
        return true;
    }

    private bool IsWalkOnOffer(string avatarId, int seasonYear, int teamId)
    {
        Snapshot s = LoadSnapshot(seasonYear, fielding: false, HsAndCollege);
        if (!IsAvatarHsSenior(s, avatarId, out RosterPlayerRow row))
        {
            return false;
        }
        (List<int> _, bool walkOn, _) = ComputeAvatarOffers(s, seasonYear, in row);
        return walkOn;
    }

    /// <summary>The phone's answer: the chosen program's offer is accepted, the others declined (persisted at once).</summary>
    public void ResolveRecruiting(int teamId)
    {
        if (!TryGetPendingRecruitingChoice(out PendingRecruitingChoice pending))
        {
            throw new InvalidOperationException("No recruiting decision is pending.");
        }
        bool found = false;
        foreach (RecruitingOfferRow offer in pending.Offers)
        {
            found |= offer.TeamId == teamId;
        }
        if (!found)
        {
            throw new ArgumentException($"Team {teamId} made the avatar no offer.", nameof(teamId));
        }
        string avatarId = ActiveAvatarId()!;
        _db.RunInBatch(() =>
        {
            foreach (RecruitingOfferRow offer in pending.Offers)
            {
                RecruitingOfferRow row = offer;
                row.Status = offer.TeamId == teamId ? RecruitingOfferStatus.Accepted : RecruitingOfferStatus.Declined;
                _queries.UpsertOffer(in row);
            }
        });
        _pendingCacheDay = -1;
    }

    private int AvatarAcceptedCount(string? avatarId, int seasonYear)
    {
        if (avatarId is null)
        {
            return 0;
        }
        var offers = new List<RecruitingOfferRow>();
        _queries.LoadOffersFor(avatarId, seasonYear, offers);
        int accepted = 0;
        foreach (RecruitingOfferRow offer in offers)
        {
            accepted += offer.Status == RecruitingOfferStatus.Accepted ? 1 : 0;
        }
        return accepted;
    }

    /// <summary>One recruit in Signing Day's pool: an HS senior (rostered) or an outside recruit (rolled in memory).</summary>
    private sealed class Recruit
    {
        public PipelineChoice Choice;
        public LeagueGenerator.GeneratedPlayer? Outside;
    }

    /// <summary>
    /// Day 318, Signing Day (D2-A, Amendments 2 and 4): the avatar's choice
    /// first (unanswered → the best-ranked offer), then programs in
    /// national-rank order take, round by round, the best available recruit
    /// for their most-needed role (with the position-coverage tiebreak),
    /// until every opening is filled. The pool is the HS seniors plus outside
    /// recruits rolled in memory; only outside recruits who sign are written
    /// (unrostered until the rollover). Accepted rows get status 1, anything
    /// left open expires (3).
    /// </summary>
    public void RunSigningDay(int seasonYear, long day)
    {
        string? avatarId = ActiveAvatarId();
        // The done key is Signing Day's own output: accepted rows beyond the
        // avatar's, which the phone may have written before the day.
        if (_queries.CountAcceptedOffers(seasonYear) > AvatarAcceptedCount(avatarId, seasonYear))
        {
            return;
        }
        string? frozen = FrozenAvatarId();
        RunOffers(seasonYear, day); // a crash catch-up may arrive without the day-274 step
        Snapshot s = LoadSnapshot(seasonYear, fielding: true, HsAndCollege);
        List<(int TeamId, int Rank)> ranked = RankedPrograms(s, seasonYear);
        if (ranked.Count == 0)
        {
            return;
        }
        HashSet<string> drafted = DraftedIn(seasonYear);
        Dictionary<int, int[]> open = OpenSpots(s, ranked, drafted, avatarId);
        var rankOf = new Dictionary<int, int>();
        foreach ((int teamId, int rank) in ranked)
        {
            rankOf[teamId] = rank;
        }

        // ---- 1. the avatar ----
        int avatarTeam = 0;
        var avatarOffers = new List<RecruitingOfferRow>();
        bool avatarChoseManually = false;
        RosterPlayerRow avatarRow = default;
        if (avatarId is not null && IsAvatarHsSenior(s, avatarId, out avatarRow)
            && _queries.LoadOffersFor(avatarId, seasonYear, avatarOffers) > 0)
        {
            foreach (RecruitingOfferRow offer in avatarOffers)
            {
                if (offer.Status == RecruitingOfferStatus.Accepted)
                {
                    avatarTeam = offer.TeamId;
                    avatarChoseManually = true;
                }
            }
            if (avatarTeam == 0)
            {
                int bestRank = int.MaxValue;
                foreach (RecruitingOfferRow offer in avatarOffers)
                {
                    int rank = rankOf.TryGetValue(offer.TeamId, out int r) ? r : int.MaxValue;
                    if (offer.Status == RecruitingOfferStatus.Open && (rank < bestRank || (rank == bestRank && offer.TeamId < avatarTeam)))
                    {
                        bestRank = rank;
                        avatarTeam = offer.TeamId;
                    }
                }
            }
            if (avatarTeam != 0 && open.TryGetValue(avatarTeam, out int[]? spots))
            {
                int r = RoleIndex(avatarRow.Role);
                spots[r] = Math.Max(0, spots[r] - 1);
            }
        }

        // ---- 2. the pool: HS seniors + outside recruits ----
        var pool = new List<Recruit>();
        int[] hsByRole = new int[3];
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (!s.TierByTeam.TryGetValue(row.TeamId, out LeagueTier tier) || tier != LeagueTier.HS
                || string.Equals(row.PlayerId, avatarId, StringComparison.Ordinal)
                || string.Equals(row.PlayerId, frozen, StringComparison.Ordinal)
                || !s.Players.TryGetValue(row.PlayerId, out PlayerRow p) || p.Age < SchoolGrades.SeniorAge)
            {
                continue;
            }
            hsByRole[RoleIndex(row.Role)]++;
            pool.Add(new Recruit
            {
                Choice = new PipelineChoice
                {
                    PlayerId = row.PlayerId,
                    Role = row.Role,
                    Position = s.PositionOf(row.PlayerId, row.Role),
                    Value = ValueOf(s, in row, LeagueTier.HS, neutral: false, dock: false),
                },
            });
        }
        int[] totalOpen = new int[3];
        foreach (int[] spots in open.Values)
        {
            for (int r = 0; r < 3; r++)
            {
                totalOpen[r] += spots[r];
            }
        }
        RngState outsideRng = PipelineRules.Stream(Seed, PipelineProfile.OutsideRecruitsTag, seasonYear);
        RngState outsideFieldRng = PipelineRules.Stream(Seed, PipelineProfile.OutsideRecruitsFieldingTag, seasonYear);
        int rolled = 0;
        for (int r = 0; r < 3; r++)
        {
            PitcherRole role = PipelineRules.Roles[r];
            int count = Math.Max(0, totalOpen[r] - hsByRole[r]) + PipelineProfile.OutsideRecruitSurplusFor(role);
            for (int i = 0; i < count; i++)
            {
                LeagueGenerator.GeneratedPlayer g = LeagueGenerator.RollOutsideRecruit(
                    role, RatingSpread, PipelineProfile.OutsideRecruitAge, PipelineProfile.OutsideRecruitMeanOffset,
                    PipelineProfile.OutsideRecruitDiscountAge, ref outsideRng, ref outsideFieldRng);
                var row = new RosterPlayerRow
                {
                    PlayerId = g.PlayerId, Role = role, IsPitcher = role != PitcherRole.None,
                    BatPower = g.Ratings.BatPower, BatContact = g.Ratings.BatContact, BatDiscipline = g.Ratings.BatDiscipline,
                    PitStuff = g.Ratings.PitStuff, PitControl = g.Ratings.PitControl, PitStamina = g.Ratings.PitStamina,
                    Fielding = g.Ratings.Fielding,
                };
                var c = new PipelineCandidateRow
                {
                    Ratings = row, Age = g.Player.Age, HealthCeiling = g.Player.HealthCeiling,
                    HasPotential = true, Potential = g.Potential, Tier = LeagueTier.HS,
                };
                pool.Add(new Recruit
                {
                    Choice = new PipelineChoice
                    {
                        PlayerId = g.PlayerId,
                        Role = role,
                        Position = g.Fielding.DefensivePosition,
                        Value = PipelineRules.Value(in c, LeagueTier.HS, 0.0, 0.0, neutralPerformance: true, recordDock: false),
                    },
                    Outside = g,
                });
                rolled++;
            }
        }
        pool.Sort(static (a, b) => PipelineRules.CompareChoice(a.Choice, b.Choice));
        var choices = new List<PipelineChoice>(pool.Count);
        foreach (Recruit recruit in pool)
        {
            choices.Add(recruit.Choice);
        }

        // ---- 3. coverage: returning position players' primaries (+ the avatar's) ----
        var covered = new Dictionary<int, bool[]>();
        foreach ((int teamId, _) in ranked)
        {
            covered[teamId] = new bool[FieldingPositions.Field.Length + 1];
        }
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (row.Role == PitcherRole.None && covered.TryGetValue(row.TeamId, out bool[]? cov)
                && s.Enrollment.TryGetValue(row.PlayerId, out CollegeEnrollmentRow en)
                && en.ClassYear < PipelineProfile.FinalClassYear && !drafted.Contains(row.PlayerId))
            {
                cov[(int)s.PositionOf(row.PlayerId, row.Role)] = true;
            }
        }
        if (avatarTeam != 0 && avatarRow.Role == PitcherRole.None && covered.TryGetValue(avatarTeam, out bool[]? avatarCov))
        {
            avatarCov[(int)s.PositionOf(avatarId!, PitcherRole.None)] = true;
        }

        // ---- 4. the NPC assignment: rank-ordered rounds by role need ----
        var signings = new List<(int TeamId, int PoolIndex)>();
        Span<bool> fillable = stackalloc bool[3];
        Span<int> target = stackalloc int[3];
        for (int r = 0; r < 3; r++)
        {
            target[r] = PipelineProfile.CollegeTargetFor(PipelineRules.Roles[r]);
        }
        bool progress = true;
        while (progress)
        {
            progress = false;
            for (int r = 0; r < 3; r++)
            {
                fillable[r] = false;
            }
            foreach (PipelineChoice c in choices)
            {
                if (!c.Taken)
                {
                    fillable[RoleIndex(c.Role)] = true;
                }
            }
            foreach ((int teamId, _) in ranked)
            {
                int[] spots = open[teamId];
                if (!PipelineRules.TryMostNeededRole(spots, target, fillable, out PitcherRole role))
                {
                    continue;
                }
                int pick = PipelineRules.PickRecruit(choices, role, covered[teamId]);
                if (pick < 0)
                {
                    continue;
                }
                PipelineChoice taken = choices[pick];
                taken.Taken = true;
                choices[pick] = taken;
                spots[RoleIndex(role)]--;
                if (role == PitcherRole.None)
                {
                    covered[teamId][(int)taken.Position] = true;
                }
                signings.Add((teamId, pick));
                progress = true;
            }
        }

        // ---- 5. one batch ----
        int outsideSigned = 0;
        _db.BeginBatch();
        try
        {
            if (avatarTeam != 0)
            {
                foreach (RecruitingOfferRow offer in avatarOffers)
                {
                    RecruitingOfferRow row = offer;
                    if (offer.TeamId == avatarTeam)
                    {
                        row.Status = RecruitingOfferStatus.Accepted;
                        _queries.UpsertOffer(in row);
                    }
                    else if (avatarChoseManually && offer.Status == RecruitingOfferStatus.Open)
                    {
                        row.Status = RecruitingOfferStatus.Declined;
                        _queries.UpsertOffer(in row);
                    }
                }
            }
            foreach ((int teamId, int index) in signings)
            {
                Recruit recruit = pool[index];
                if (recruit.Outside is not null)
                {
                    LeagueGenerator.WriteGeneratedPlayer(_players, _baseball, recruit.Outside, teamId: null);
                    outsideSigned++;
                }
                _queries.UpsertOffer(new RecruitingOfferRow
                {
                    PlayerId = recruit.Choice.PlayerId, TeamId = teamId, SeasonYear = seasonYear,
                    Status = RecruitingOfferStatus.Accepted,
                });
            }
            _queries.ExpireOpenOffers(seasonYear);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        SigningDaysRun++;
        _pendingCacheDay = -1;
        LastRecruitsSigned = signings.Count;
        LastOutsideRecruitsSigned = outsideSigned;
        LastOutsideRecruitsRolled = rolled;
        if (avatarTeam != 0)
        {
            _bus?.Publish(new RecruitSignedEvent(avatarId!, avatarTeam, seasonYear, day));
        }
    }

    // ------------------------------------------------------------------
    // The rollover (D2-A steps 1–7 in the C2 order)
    // ------------------------------------------------------------------

    /// <summary>
    /// The rollover pass into <paramref name="newSeasonYear"/>: the last
    /// <see cref="SeasonRolledOverEvent"/> handler (after aging and
    /// development). Every decision is planned up front from one snapshot;
    /// then the sims flush, one batch applies the plan, every sim and the
    /// micro-sim re-initialize once, and the avatar re-activates last (or the
    /// heir activates after a takeover). A pass that moves nobody is a
    /// complete no-op for the sims (no flush, no re-init).
    /// </summary>
    public void RunRollover(int completedSeasonYear, int newSeasonYear)
    {
        long day = _state.CurrentDay;
        Snapshot s = LoadSnapshot(completedSeasonYear, fielding: true);
        if (s.Teams.Count == 0)
        {
            return;
        }
        string? avatarId = ActiveAvatarId();
        string? frozen = FrozenAvatarId();
        bool dock = AvatarRecordDock(avatarId);
        bool takeoverPhase = Career is { HasAvatar: true, InParentPhase: true, IsLineageOver: false };

        var teamWrites = new List<(string Id, int? TeamId)>();
        var flags = new List<(string Id, string Flag, bool Active)>();
        var enrollmentDeletes = new List<string>();
        var freshmen = new List<CollegeEnrollmentRow>();
        // HS departures to refill 1-for-1: (team, role, primary position, departed id).
        var hsDepartures = new List<(int TeamId, PitcherRole Role, FieldingPosition Position, string Id)>();
        var moved = new HashSet<string>(StringComparer.Ordinal);
        // Where everyone ends up (rostered team id), for the release and conservation passes.
        var teamOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (RosterPlayerRow row in s.Roster)
        {
            teamOf[row.PlayerId] = row.TeamId;
        }
        int rosteredRetirements = 0, faRetirements = 0, graduates = 0, unrecruited = 0, outsideJoined = 0;
        int draftees = 0, undrafted = 0, releases = 0, displacedCount = 0;
        bool avatarMoved = false, avatarReleased = false, avatarHasChildren = false;
        LeagueTier avatarReleasedFrom = LeagueTier.HS;

        void Move(string id, int? teamId)
        {
            teamWrites.Add((id, teamId));
            moved.Add(id);
            if (teamId is int t)
            {
                teamOf[id] = t;
            }
            else
            {
                teamOf.Remove(id);
            }
        }

        void ReleaseAvatar(LeagueTier from)
        {
            Move(avatarId!, null);
            flags.Add((avatarId!, PipelineFlags.Retired, true));
            avatarReleased = true;
            avatarReleasedFrom = from;
            var children = new List<PlayerRow>();
            avatarHasChildren = _players.LoadChildrenOf(avatarId!, children) > 0;
        }

        // ---- 1. free agents carried in retire first (a release from this rollover always gets one signable season) ----
        var pool = new List<FreeAgentRow>();
        _queries.LoadFreeAgentPool(pool);
        foreach (FreeAgentRow fa in pool)
        {
            if (fa.SetOnDay >= day)
            {
                continue; // flagged today (a same-day re-run): not carried in
            }
            int entered = _state.SeasonYearForDay(Math.Max(1, fa.SetOnDay));
            if (PipelineRules.ShouldRetireFreeAgent(fa.Age, fa.HealthCeiling, newSeasonYear, entered))
            {
                flags.Add((fa.Ratings.PlayerId, PipelineFlags.FreeAgent, false));
                flags.Add((fa.Ratings.PlayerId, PipelineFlags.Retired, true));
                faRetirements++;
            }
        }

        // ---- 2. rostered retirements (age 42 / health 40) ----
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (string.Equals(row.PlayerId, avatarId, StringComparison.Ordinal)
                || string.Equals(row.PlayerId, frozen, StringComparison.Ordinal)
                || !s.Players.TryGetValue(row.PlayerId, out PlayerRow p)
                || !PipelineRules.ShouldRetireRostered(p.Age, p.HealthCeiling))
            {
                continue;
            }
            Move(row.PlayerId, null);
            flags.Add((row.PlayerId, PipelineFlags.Retired, true));
            rosteredRetirements++;
            LeagueTier tier = s.TierByTeam[row.TeamId];
            if (tier == LeagueTier.College)
            {
                enrollmentDeletes.Add(row.PlayerId);
            }
            else if (tier == LeagueTier.HS)
            {
                hsDepartures.Add((row.TeamId, row.Role, s.PositionOf(row.PlayerId, row.Role), row.PlayerId));
            }
        }

        // ---- 3. HS graduation, and signed outside recruits join ----
        var accepted = new List<RecruitingOfferRow>();
        _queries.LoadAcceptedOffers(completedSeasonYear, accepted);
        var acceptedTeam = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (RecruitingOfferRow offer in accepted)
        {
            acceptedTeam[offer.PlayerId] = offer.TeamId;
        }
        int avatarCollegeTeam = 0;
        int avatarHsTeam = 0;
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (moved.Contains(row.PlayerId) || s.TierByTeam[row.TeamId] != LeagueTier.HS
                || string.Equals(row.PlayerId, frozen, StringComparison.Ordinal)
                || !s.Players.TryGetValue(row.PlayerId, out PlayerRow p) || p.Age <= SchoolGrades.SeniorAge)
            {
                continue;
            }
            bool isAvatar = string.Equals(row.PlayerId, avatarId, StringComparison.Ordinal);
            hsDepartures.Add((row.TeamId, row.Role, s.PositionOf(row.PlayerId, row.Role), row.PlayerId));
            if (acceptedTeam.TryGetValue(row.PlayerId, out int college) || isAvatar)
            {
                if (isAvatar && !acceptedTeam.ContainsKey(row.PlayerId))
                {
                    // D4a-A guarantee: a senior who reached the rollover with no
                    // signed offer (a career created after Signing Day) walks on.
                    (List<int> offers, _, _) = ComputeAvatarOffers(s, completedSeasonYear, in row);
                    college = offers.Count > 0 ? offers[0] : 0;
                }
                if (college == 0)
                {
                    continue;
                }
                Move(row.PlayerId, college);
                freshmen.Add(new CollegeEnrollmentRow
                {
                    PlayerId = row.PlayerId, TeamId = college, EntrySeason = newSeasonYear, ClassYear = 1, DeclaredSeason = null,
                });
                graduates++;
                if (isAvatar)
                {
                    avatarMoved = true;
                    avatarCollegeTeam = college;
                    avatarHsTeam = row.TeamId;
                }
            }
            else
            {
                Move(row.PlayerId, null);
                flags.Add((row.PlayerId, PipelineFlags.Retired, true));
                unrecruited++;
            }
        }
        foreach (RecruitingOfferRow offer in accepted)
        {
            // Signed outside recruits are the accepted, unrostered players.
            if (!s.Players.ContainsKey(offer.PlayerId) && !moved.Contains(offer.PlayerId))
            {
                Move(offer.PlayerId, offer.TeamId);
                freshmen.Add(new CollegeEnrollmentRow
                {
                    PlayerId = offer.PlayerId, TeamId = offer.TeamId, EntrySeason = newSeasonYear, ClassYear = 1, DeclaredSeason = null,
                });
                outsideJoined++;
            }
        }

        // ---- 4. College: class years advance; draftees go Pro; finished seniors leave ----
        HashSet<string> draftedPrev = DraftedIn(completedSeasonYear);
        var draftTeam = new Dictionary<string, int>(StringComparer.Ordinal);
        var picks = new List<DraftPickRow>();
        _queries.LoadDraftPicks(completedSeasonYear, picks);
        foreach (DraftPickRow pick in picks)
        {
            draftTeam[pick.PlayerId] = pick.TeamId;
        }
        var newDraftees = new HashSet<string>(StringComparer.Ordinal);
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (moved.Contains(row.PlayerId) || s.TierByTeam[row.TeamId] != LeagueTier.College
                || string.Equals(row.PlayerId, frozen, StringComparison.Ordinal))
            {
                continue;
            }
            bool isAvatar = string.Equals(row.PlayerId, avatarId, StringComparison.Ordinal);
            if (draftTeam.TryGetValue(row.PlayerId, out int proTeam))
            {
                Move(row.PlayerId, proTeam);
                enrollmentDeletes.Add(row.PlayerId);
                newDraftees.Add(row.PlayerId);
                draftees++;
                avatarMoved |= isAvatar;
                continue;
            }
            if (s.Enrollment.TryGetValue(row.PlayerId, out CollegeEnrollmentRow en) && en.ClassYear >= PipelineProfile.FinalClassYear)
            {
                enrollmentDeletes.Add(row.PlayerId);
                undrafted++;
                if (isAvatar)
                {
                    ReleaseAvatar(LeagueTier.College); // D4b-C: an undrafted senior is released
                }
                else
                {
                    Move(row.PlayerId, null);
                    flags.Add((row.PlayerId, PipelineFlags.FreeAgent, true));
                }
            }
        }

        // ---- 5. Pro releases to exactly 15/4/5, lowest value first per role (the avatar included, D4b-C) ----
        var proGroups = new Dictionary<(int, int), List<PipelineChoice>>();
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (!teamOf.TryGetValue(row.PlayerId, out int teamId) || !s.TierByTeam.TryGetValue(teamId, out LeagueTier tier)
                || tier != LeagueTier.Pro || string.Equals(row.PlayerId, frozen, StringComparison.Ordinal))
            {
                continue;
            }
            bool isAvatar = string.Equals(row.PlayerId, avatarId, StringComparison.Ordinal);
            var key = (teamId, RoleIndex(row.Role));
            if (!proGroups.TryGetValue(key, out List<PipelineChoice>? group))
            {
                group = new List<PipelineChoice>();
                proGroups[key] = group;
            }
            // A draftee has no Pro line yet: neutral P (Argon Q6), so a new pick isn't cut before he plays.
            bool neutral = newDraftees.Contains(row.PlayerId);
            group.Add(new PipelineChoice
            {
                PlayerId = row.PlayerId,
                Role = row.Role,
                Value = ValueOf(s, in row, LeagueTier.Pro, neutral, dock: isAvatar && dock),
            });
        }
        var released = new List<string>();
        foreach (KeyValuePair<(int TeamId, int Role), List<PipelineChoice>> entry in OrderedGroups(proGroups))
        {
            int frozenHere = frozen is not null && teamOf.TryGetValue(frozen, out int ft) && ft == entry.Key.TeamId
                && RoleOfRostered(s, frozen) == entry.Key.Role ? 1 : 0;
            PipelineRules.SelectReleases(entry.Value, PipelineProfile.ProCapFor(PipelineRules.Roles[entry.Key.Role]) - frozenHere, released);
        }
        foreach (string id in released)
        {
            if (string.Equals(id, avatarId, StringComparison.Ordinal))
            {
                ReleaseAvatar(LeagueTier.Pro); // D4b-C: a cut Pro avatar takes the same path
                avatarMoved = false;
            }
            else
            {
                Move(id, null);
                flags.Add((id, PipelineFlags.FreeAgent, true));
            }
            releases++;
        }

        // ---- the takeover (in the parent phase, before HS intake) ----
        bool avatarGameOver = false;
        LineageFailure gameOverReason = LineageFailure.None;
        string? takeoverHeir = null;
        int takeoverTeam = 0;
        string? takeoverDisplaced = null;
        bool evaluateTakeover = Career is not null && (takeoverPhase || (avatarReleased && avatarHasChildren));
        if (avatarReleased && !avatarHasChildren)
        {
            avatarGameOver = true;
            gameOverReason = LineageFailure.NoHeirs;
        }
        if (evaluateTakeover)
        {
            var candidates = new List<HeirCandidate>();
            SuccessionOutcome outcome = Career!.EvaluateTakeover(candidates);
            if (outcome.Kind == SuccessionOutcomeKind.Succeeded)
            {
                if (Career.AutopilotSuccession)
                {
                    takeoverHeir = outcome.HeirId!;
                    takeoverTeam = Career.FamilyHsTeamId();
                    takeoverDisplaced = WeakestRemaining(s, takeoverTeam, takeoverHeir, teamOf);
                    if (takeoverDisplaced is not null)
                    {
                        moved.Add(takeoverDisplaced);
                        teamOf.Remove(takeoverDisplaced);
                        displacedCount++;
                    }
                    moved.Add(takeoverHeir);
                    teamOf[takeoverHeir] = takeoverTeam;
                }
            }
            else if (outcome.Kind == SuccessionOutcomeKind.GameOver)
            {
                avatarGameOver = true;
                gameOverReason = outcome.Reason;
            }
        }

        // ---- the walk-on displacement: the avatar over his program's target ----
        string? walkOnDisplaced = null;
        if (avatarCollegeTeam != 0 && avatarId is not null)
        {
            PitcherRole role = RoleOfRosteredRole(s, avatarId);
            int count = 0;
            foreach (KeyValuePair<string, int> kv in teamOf)
            {
                if (kv.Value == avatarCollegeTeam && RoleOfRosteredRoleOrOutside(s, kv.Key) == role)
                {
                    count++;
                }
            }
            if (count > PipelineProfile.CollegeTargetFor(role))
            {
                walkOnDisplaced = WeakestRemainingCollege(s, avatarCollegeTeam, role, avatarId, teamOf, freshmen);
                if (walkOnDisplaced is not null)
                {
                    Move(walkOnDisplaced, null);
                    flags.Add((walkOnDisplaced, PipelineFlags.Retired, true));
                    enrollmentDeletes.Add(walkOnDisplaced);
                    displacedCount++;
                }
            }
        }

        // ---- 6. HS intake at 16, per (team, role, position) ----
        hsDepartures.Sort(static (a, b) =>
        {
            int byTeam = a.TeamId.CompareTo(b.TeamId);
            if (byTeam != 0)
            {
                return byTeam;
            }
            int byRole = a.Role.CompareTo(b.Role);
            return byRole != 0 ? byRole : string.CompareOrdinal(a.Id, b.Id);
        });

        bool anyMove = teamWrites.Count > 0 || hsDepartures.Count > 0 || takeoverHeir is not null;
        bool anyWrite = anyMove || flags.Count > 0 || enrollmentDeletes.Count > 0 || freshmen.Count > 0
            || s.Enrollment.Count > 0 || avatarGameOver;
        if (!anyWrite)
        {
            LastRun = new PipelineSummary(completedSeasonYear, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, false, false, false);
            return;
        }

        // ---- flush before re-init (the new season's day-1 state must lose nothing) ----
        if (anyMove)
        {
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                if (_leagues.TryGet((LeagueTier)t, out LeagueSimulator sim))
                {
                    sim.FlushPending();
                }
            }
        }

        // ---- 7 (no backfill of unplanned College departures) — one batch ----
        int intake = 0;
        _db.BeginBatch();
        try
        {
            foreach ((string id, int? teamId) in teamWrites)
            {
                _players.SetTeam(id, teamId);
            }
            foreach ((string id, string flag, bool active) in flags)
            {
                _players.SetFlag(id, flag, active, day);
            }
            foreach (string id in enrollmentDeletes)
            {
                _queries.DeleteEnrollment(id);
            }
            _queries.AdvanceClassYears();
            foreach (CollegeEnrollmentRow row in freshmen)
            {
                if (!string.Equals(row.PlayerId, walkOnDisplaced, StringComparison.Ordinal))
                {
                    _queries.UpsertEnrollment(in row);
                }
            }
            // A row whose player left College some other way (displaced by an
            // avatar's creation, a succession) goes; a College-rostered straggler
            // without a row (e.g. a mid-career succession) gets one.
            foreach (string id in s.Enrollment.Keys)
            {
                bool stillCollege = teamOf.TryGetValue(id, out int at) && s.TierByTeam.TryGetValue(at, out LeagueTier atTier)
                    && atTier == LeagueTier.College;
                if (!stillCollege && !enrollmentDeletes.Contains(id))
                {
                    _queries.DeleteEnrollment(id);
                }
            }
            // Any College-rostered straggler without a row (e.g. a mid-career succession) gets one.
            foreach (RosterPlayerRow row in s.Roster)
            {
                if (teamOf.TryGetValue(row.PlayerId, out int teamId) && s.TierByTeam.TryGetValue(teamId, out LeagueTier tier)
                    && tier == LeagueTier.College && !s.Enrollment.ContainsKey(row.PlayerId)
                    && !freshmen.Exists(f => f.PlayerId == row.PlayerId)
                    && s.Players.TryGetValue(row.PlayerId, out PlayerRow p))
                {
                    WriteBootEnrollment(row.PlayerId, teamId, p.Age, newSeasonYear);
                }
            }
            if (avatarHsTeam != 0)
            {
                _gameState.SetInt64(GameStateKeys.FamilyHsTeamId, avatarHsTeam);
            }
            if (avatarReleased)
            {
                Career!.WriteReleaseStateInBatch(avatarHasChildren, day);
            }
            if (takeoverHeir is not null)
            {
                Career!.WriteTakeoverInBatch(takeoverHeir, takeoverTeam, takeoverDisplaced, day);
            }
            else if (avatarGameOver && gameOverReason != LineageFailure.NoHeirs)
            {
                Career!.WriteGameOverInBatch(gameOverReason);
            }

            RngState intakeRng = PipelineRules.Stream(Seed, PipelineProfile.HsIntakeTag, completedSeasonYear);
            RngState intakeFieldRng = PipelineRules.Stream(Seed, PipelineProfile.HsIntakeFieldingTag, completedSeasonYear);
            foreach ((int teamId, PitcherRole role, FieldingPosition position, _) in hsDepartures)
            {
                (string id, int stuff) = LeagueGenerator.GeneratePlayer(
                    _players, _baseball, teamId, role, RatingSpread, ref intakeRng,
                    PipelineProfile.IntakeMinAge, PipelineProfile.IntakeAgeSpan);
                if (role != PitcherRole.None)
                {
                    LeagueGenerator.GenerateArsenal(_baseball, id, stuff, RatingSpread, ref intakeRng);
                }
                _baseball.UpsertFielding(LeagueGenerator.RollFielding(id, position, RatingSpread, ref intakeFieldRng));
                intake++;
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        // ---- every sim and the micro-sim re-initialize once; the avatar last ----
        if (anyMove)
        {
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                if (_leagues.TryGet((LeagueTier)t, out LeagueSimulator sim))
                {
                    sim.Initialize();
                }
            }
            Micro?.Initialize();
        }
        if (Career is not null && avatarReleased)
        {
            Career.ApplyReleaseState(avatarHasChildren);
        }
        if (Career is not null && takeoverHeir is not null)
        {
            Career.CompleteTakeover(takeoverHeir, takeoverTeam);
        }
        else
        {
            if (Career is not null && avatarGameOver && gameOverReason != LineageFailure.NoHeirs)
            {
                Career.ApplyGameOver(gameOverReason);
            }
            if (Career is { HasAvatar: true } && anyMove)
            {
                Career.ReactivateAvatar();
            }
            if (Career is not null && evaluateTakeover && takeoverHeir is null && !avatarGameOver && !Career.AutopilotSuccession)
            {
                Career.ParkTakeoverChoice();
            }
        }
        if (avatarReleased)
        {
            _bus?.Publish(new AvatarReleasedEvent(avatarId!, (byte)avatarReleasedFrom, newSeasonYear, day, avatarHasChildren));
        }

        LastRun = new PipelineSummary(
            completedSeasonYear, rosteredRetirements, faRetirements, graduates, unrecruited, outsideJoined,
            draftees, undrafted, releases, intake, displacedCount,
            avatarMoved, avatarReleased, takeoverHeir is not null, avatarGameOver, anyMove);
    }

    private static IEnumerable<KeyValuePair<(int TeamId, int Role), List<PipelineChoice>>> OrderedGroups(
        Dictionary<(int, int), List<PipelineChoice>> groups)
    {
        var keys = new List<(int, int)>(groups.Keys);
        keys.Sort();
        foreach ((int teamId, int role) in keys)
        {
            yield return new KeyValuePair<(int, int), List<PipelineChoice>>((teamId, role), groups[(teamId, role)]);
        }
    }

    private static int RoleOfRostered(Snapshot s, string playerId) => RoleIndex(RoleOfRosteredRole(s, playerId));

    private static PitcherRole RoleOfRosteredRole(Snapshot s, string playerId)
    {
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (row.PlayerId == playerId)
            {
                return row.Role;
            }
        }
        return PitcherRole.None;
    }

    /// <summary>A role for a rostered player, or (for a signed outside recruit, unrostered in the snapshot) his Pitcher_Roles row.</summary>
    private PitcherRole RoleOfRosteredRoleOrOutside(Snapshot s, string playerId)
    {
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (row.PlayerId == playerId)
            {
                return row.Role;
            }
        }
        return _baseball.TryGetRatings(playerId, out PlayerRatingsRow ratings) && ratings.IsPitcher
            ? (_baseball.TryGetPitcherRole(playerId, out PitcherRole role) && role != PitcherRole.None ? role : PitcherRole.Starter)
            : PitcherRole.None;
    }

    /// <summary>
    /// The heir's displacement on the family HS team: the weakest same-role
    /// player by raw role sum (ties by player_id) among those still there
    /// after this pass's departures (the succession displacement rule).
    /// </summary>
    private string? WeakestRemaining(Snapshot s, int hsTeamId, string heirId, Dictionary<string, int> teamOf)
    {
        if (!_baseball.TryGetRatings(heirId, out PlayerRatingsRow heirRatings))
        {
            return null;
        }
        PitcherRole role = heirRatings.IsPitcher
            ? (_baseball.TryGetPitcherRole(heirId, out PitcherRole r) && r != PitcherRole.None ? r : PitcherRole.Starter)
            : PitcherRole.None;
        string? weakest = null;
        int weakestSum = int.MaxValue;
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (row.Role != role || !teamOf.TryGetValue(row.PlayerId, out int t) || t != hsTeamId)
            {
                continue;
            }
            int sum = PipelineRules.RawRoleSum(in row);
            if (sum < weakestSum)
            {
                weakestSum = sum;
                weakest = row.PlayerId;
            }
        }
        return weakest;
    }

    /// <summary>The walk-on's displacement (D4a-A): the weakest same-role returning player on the avatar's program (not a freshman, not the avatar).</summary>
    private static string? WeakestRemainingCollege(
        Snapshot s, int teamId, PitcherRole role, string avatarId, Dictionary<string, int> teamOf, List<CollegeEnrollmentRow> freshmen)
    {
        var fresh = new HashSet<string>(StringComparer.Ordinal);
        foreach (CollegeEnrollmentRow row in freshmen)
        {
            fresh.Add(row.PlayerId);
        }
        string? weakest = null;
        int weakestSum = int.MaxValue;
        foreach (RosterPlayerRow row in s.Roster)
        {
            if (row.Role != role || row.PlayerId == avatarId || fresh.Contains(row.PlayerId)
                || !teamOf.TryGetValue(row.PlayerId, out int t) || t != teamId)
            {
                continue;
            }
            int sum = PipelineRules.RawRoleSum(in row);
            if (sum < weakestSum)
            {
                weakestSum = sum;
                weakest = row.PlayerId;
            }
        }
        return weakest;
    }

    // ------------------------------------------------------------------
    // Free agency at the Sunday lock (brief §4 item 5)
    // ------------------------------------------------------------------

    /// <summary>
    /// Before the lock's designations: a Pro team below 15/4/5 signs the best
    /// free agent for its most-needed role (valued at neutral P), the most
    /// short-handed team first. The fast path reads the Pro sim's per-team
    /// role counts: 0 queries, 0 B. A lock with a signing commits one batch,
    /// re-initializes the Pro sim and the micro-sim once, refreshes the
    /// avatar's slot, and publishes ProContractSignedEvent(wasDrafted: false)
    /// for every first contract.
    /// </summary>
    public int FillBeforeLock(int seasonYear, long lockDay)
    {
        LastLockSignings = 0;
        bool haveSim = _leagues.TryGet(LeagueTier.Pro, out LeagueSimulator sim);
        if (haveSim)
        {
            bool short_ = false;
            for (int team = 0; team < sim.TeamCount && !short_; team++)
            {
                short_ = sim.PositionOrder(team).Length < PipelineProfile.ProCapBatters
                    || sim.StarterRank(team).Length < PipelineProfile.ProCapStarters
                    || sim.RelieverRank(team).Length < PipelineProfile.ProCapRelievers;
            }
            if (!short_)
            {
                return 0;
            }
        }
        return SignFreeAgents(lockDay, haveSim ? sim : null);
    }

    private int SignFreeAgents(long lockDay, LeagueSimulator? sim)
    {
        var proRoster = new List<RosterPlayerRow>(RosterCapacity.MaxTeamsPerTier * RosterCapacity.MaxRoster);
        _baseball.LoadRosterByTier(LeagueTier.Pro, proRoster);
        var proTeams = new List<TeamRow>(RosterCapacity.MaxTeamsPerTier);
        _baseball.LoadTeamsByTier(LeagueTier.Pro, proTeams);
        if (proTeams.Count == 0)
        {
            return 0;
        }
        var counts = new Dictionary<int, int[]>();
        foreach (TeamRow team in proTeams)
        {
            counts[team.TeamId] = new int[3];
        }
        foreach (RosterPlayerRow row in proRoster)
        {
            counts[row.TeamId][RoleIndex(row.Role)]++;
        }
        var pool = new List<FreeAgentRow>();
        _queries.LoadFreeAgentPool(pool);
        var potentials = new Dictionary<string, PlayerPotentialRow>(StringComparer.Ordinal);
        _baseball.LoadAllPotential(potentials);
        var byRole = new List<PipelineChoice>[3] { new(), new(), new() };
        foreach (FreeAgentRow fa in pool)
        {
            if (fa.Age >= HeirGenetics.HeirGeneticsProfile.MandatoryRetirementAge
                || fa.HealthCeiling <= HeirGenetics.HeirGeneticsProfile.HealthRetirementFloor)
            {
                continue;
            }
            var c = new PipelineCandidateRow
            {
                Ratings = fa.Ratings, Age = fa.Age, HealthCeiling = fa.HealthCeiling,
                HasPotential = potentials.TryGetValue(fa.Ratings.PlayerId, out PlayerPotentialRow pot), Potential = pot,
                Tier = LeagueTier.Pro,
            };
            byRole[RoleIndex(fa.Ratings.Role)].Add(new PipelineChoice
            {
                PlayerId = fa.Ratings.PlayerId,
                Role = fa.Ratings.Role,
                Value = PipelineRules.Value(in c, LeagueTier.Pro, 0.0, 0.0, neutralPerformance: true, recordDock: false),
            });
        }
        for (int r = 0; r < 3; r++)
        {
            byRole[r].Sort(PipelineRules.CompareChoice);
        }
        var next = new int[3];
        var signings = new List<(string Id, int TeamId)>();
        Span<int> openScratch = stackalloc int[3];
        Span<int> caps = stackalloc int[3];
        Span<bool> fillable = stackalloc bool[3];
        for (int r = 0; r < 3; r++)
        {
            caps[r] = PipelineProfile.ProCapFor(PipelineRules.Roles[r]);
        }
        while (true)
        {
            int bestTeam = 0;
            int bestOpen = 0;
            foreach (TeamRow team in proTeams)
            {
                int[] c = counts[team.TeamId];
                int total = 0;
                bool canFill = false;
                for (int r = 0; r < 3; r++)
                {
                    int open = Math.Max(0, caps[r] - c[r]);
                    total += open;
                    canFill |= open > 0 && next[r] < byRole[r].Count;
                }
                if (canFill && (total > bestOpen || (total == bestOpen && team.TeamId < bestTeam)))
                {
                    bestOpen = total;
                    bestTeam = team.TeamId;
                }
            }
            if (bestTeam == 0)
            {
                break;
            }
            int[] teamCounts = counts[bestTeam];
            for (int r = 0; r < 3; r++)
            {
                openScratch[r] = Math.Max(0, caps[r] - teamCounts[r]);
                fillable[r] = next[r] < byRole[r].Count;
            }
            if (!PipelineRules.TryMostNeededRole(openScratch, caps, fillable, out PitcherRole role))
            {
                break;
            }
            int ri = RoleIndex(role);
            signings.Add((byRole[ri][next[ri]].PlayerId, bestTeam));
            next[ri]++;
            teamCounts[ri]++;
        }
        if (signings.Count == 0)
        {
            return 0;
        }
        var proSigned = new List<string>();
        _baseball.LoadActiveFlagPlayerIds(PipelineFlags.ProSigned, proSigned);
        var firstContract = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string id, _) in signings)
        {
            if (!proSigned.Contains(id))
            {
                firstContract.Add(id);
            }
        }
        sim?.FlushPending();
        _db.BeginBatch();
        try
        {
            foreach ((string id, int teamId) in signings)
            {
                _players.SetTeam(id, teamId);
                _players.SetFlag(id, PipelineFlags.FreeAgent, false, lockDay);
                if (firstContract.Contains(id))
                {
                    _players.SetFlag(id, PipelineFlags.ProSigned, true, lockDay);
                }
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        sim?.Initialize();
        Micro?.Initialize();
        Career?.RefreshAvatarSlot();
        LockReinitializations++;
        LastLockSignings = signings.Count;
        FreeAgentSignings += signings.Count;
        foreach ((string id, int teamId) in signings)
        {
            if (firstContract.Contains(id))
            {
                _bus?.Publish(new ProContractSignedEvent(id, teamId, lockDay, wasDrafted: false));
            }
        }
        return signings.Count;
    }

    // ------------------------------------------------------------------
    // The read model (13-6b's phone cards and Draft board)
    // ------------------------------------------------------------------

    /// <summary>
    /// A snapshot for the phone and the Draft board (read-only): this
    /// season's class year, offers, projected round, draft board and free
    /// agents by role.
    /// </summary>
    public PipelineStatusView GetStatusView()
    {
        int seasonYear = _state.SeasonYear;
        string? avatarId = Career is { HasAvatar: true } c ? c.AvatarPlayerId : null;
        Snapshot s = LoadSnapshot(seasonYear, fielding: false, CollegeOnly);
        List<BoardEntry> board = BuildBoard(s, avatarId, AvatarRecordDock(avatarId));
        var entries = new List<DraftBoardEntry>(board.Count);
        int avatarSlot = 0;
        for (int i = 0; i < board.Count; i++)
        {
            BoardEntry e = board[i];
            bool isAvatar = string.Equals(e.Choice.PlayerId, avatarId, StringComparison.Ordinal);
            if (isAvatar)
            {
                avatarSlot = i + 1;
            }
            entries.Add(new DraftBoardEntry(e.Choice.PlayerId, e.TeamId, e.Choice.Role, e.ClassYear, e.Age, e.Choice.Value, i + 1, isAvatar));
        }
        var offers = new List<RecruitingOfferRow>();
        if (avatarId is not null)
        {
            _queries.LoadOffersFor(avatarId, seasonYear, offers);
        }
        var picks = new List<DraftPickRow>();
        _queries.LoadDraftPicks(seasonYear, picks);
        var pool = new List<FreeAgentRow>();
        _queries.LoadFreeAgentPool(pool);
        int[] faByRole = new int[3];
        foreach (FreeAgentRow fa in pool)
        {
            faByRole[RoleIndex(fa.Ratings.Role)]++;
        }
        int classYear = avatarId is not null && s.Enrollment.TryGetValue(avatarId, out CollegeEnrollmentRow en) ? en.ClassYear : 0;
        return new PipelineStatusView
        {
            SeasonYear = seasonYear,
            AvatarClassYear = classYear,
            AvatarProjectedSlot = avatarSlot,
            AvatarProjectedRound = PipelineRules.ProjectedRound(avatarSlot, ProTeamCount()),
            AvatarOffers = offers,
            DraftBoard = entries,
            DraftPicks = picks,
            FreeAgentBatters = faByRole[0],
            FreeAgentStarters = faByRole[1],
            FreeAgentRelievers = faByRole[2],
        };
    }
}
