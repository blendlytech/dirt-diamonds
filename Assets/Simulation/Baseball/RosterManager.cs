using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// The Pro Sunday roster lock (slice 13-5; league_restructure.md §7, §16-I16;
/// brief D4, D6-A). On each Sunday tick inside the lock window (days 91–273),
/// after the Pro sim's Sunday flush, the coach sets every Pro team's 18
/// actives — 12 position players, 3 SP, 3 RP — by <see cref="RosterRules"/>,
/// and the lock commits all 768 rows to Roster_Designations in one batch with
/// <c>locked_through_day</c> = the next Sunday (exclusive). An active player
/// who goes down mid-week is not replaced by an inactive one (§7 attrition):
/// the sims treat inactive exactly like absent.
///
/// <b>Memory</b>: a <see cref="Version"/>ed mirror of the covering rows that
/// the sims cache per slot (the <see cref="AvailabilityLedger"/> pattern),
/// hydrated at boot and replaced after each lock commits. A player with no
/// covering row is active: before the first lock, in the offseason, and in
/// HS and College, which have no designations at all (D4d).
///
/// <b>Purity</b>: the lock is a function of persisted state (ratings as the
/// Pro sim baked them, the absence ledger, the avatar's stats and person row)
/// and draws no random number. Same save ⇒ same designations, reload included.
///
/// Lives in the Baseball sim and never references Life: the coach-affinity
/// proxy reads Player_Person through <see cref="PersonQueries"/> (D4c-A), and
/// the avatar's beats leave as a <see cref="RosterDesignationChangedEvent"/>.
/// </summary>
public sealed class RosterManager
{
    private readonly DatabaseManager _db;
    private readonly BaseballQueries _baseball;
    private readonly RosterQueries _queries;
    private readonly GameStateQueries _gameState;
    private readonly LeagueDirectory _leagues;
    private readonly Action<DayAdvancedEvent> _onDayAdvanced;
    private EventBus? _bus;

    // The covering rows (Pro only), by player, and the inactive ones the sims read.
    private readonly Dictionary<string, RosterDesignationRow> _rows = new(800, StringComparer.Ordinal);
    private readonly List<RosterDesignationRow> _inactive = new(256);
    private readonly List<RosterDesignationRow> _staged = new(800);
    private readonly List<RosterDesignationRow> _hydrateScratch = new(800);

    // Lock scratch, sized by capacity once.
    private readonly DesignationCandidate[] _positionGroup = new DesignationCandidate[RosterCapacity.MaxPositionPlayers];
    private readonly DesignationCandidate[] _starterGroup = new DesignationCandidate[RosterCapacity.MaxStarters];
    private readonly DesignationCandidate[] _relieverGroup = new DesignationCandidate[RosterCapacity.MaxRelievers];
    private readonly bool[] _activeScratch = new bool[RosterCapacity.MaxPositionPlayers];
    private readonly LineupCandidate[] _lineupScratch = new LineupCandidate[RosterCapacity.MaxPositionPlayers];
    private readonly long[] _gameDays = new long[GameCalendar.DaysPerWeek];
    private static readonly bool[] ProPostseasonSlotDay = BuildProPostseasonSlotDays();

    /// <summary>
    /// Roster availability (Phase 8c): who is absent on the locked week's
    /// game days (C1). Null reads as nobody absent — every guard-world and
    /// pre-8c harness path.
    /// </summary>
    public AvailabilityLedger? Availability;

    /// <summary>The coach-affinity proxy's source (D4c-A). Null reads as the neutral 50s (ΔA = 0).</summary>
    public PersonQueries? Persons;

    /// <summary>Bumped once per committed lock (and once by a boot hydrate that found rows); consumers rebuild caches when it moves.</summary>
    public int Version { get; private set; }

    /// <summary>Locks committed since construction (harness visibility: 27 a season).</summary>
    public int LockCount { get; private set; }

    /// <summary>Rows the last lock wrote, and how many of them were active (768 / 576 in a full Pro world).</summary>
    public int LastLockRows { get; private set; }

    public int LastLockActive { get; private set; }

    /// <summary>The last lock's day (absolute) and horizon (its <c>locked_through_day</c>); 0 before any lock.</summary>
    public long LastLockDay { get; private set; }

    public long LockedThroughDay { get; private set; }

    public RosterManager(DatabaseManager db, BaseballQueries baseball, LeagueDirectory leagues)
    {
        _db = db;
        _baseball = baseball;
        _queries = new RosterQueries(db);
        _gameState = new GameStateQueries(db);
        _leagues = leagues;
        _onDayAdvanced = OnDayAdvanced;
    }

    /// <summary>
    /// Subscribe AFTER the tier sims (the Pro sim's Sunday flush runs first,
    /// so the lock's performance inputs see the week's macro stats) and
    /// BEFORE the career driver (D6-A).
    /// </summary>
    public void AttachTo(EventBus bus)
    {
        _bus = bus;
        bus.Subscribe(_onDayAdvanced);
    }

    public void DetachFrom(EventBus bus)
    {
        bus.Unsubscribe(_onDayAdvanced);
        if (ReferenceEquals(_bus, bus))
        {
            _bus = null;
        }
    }

    private void OnDayAdvanced(DayAdvancedEvent e)
    {
        if (RosterRules.IsLockDay(e.DayOfSeason))
        {
            Lock(e.SeasonYear, e.Day, e.DayOfSeason);
        }
    }

    // ------------------------------------------------------------------
    // Reads: the sims, the career, the planner, tools and UI (all 0 B)
    // ------------------------------------------------------------------

    /// <summary>The player's designation on the day: false only under a covering inactive row (the default is active).</summary>
    public bool IsActive(string playerId, long day) =>
        !_rows.TryGetValue(playerId, out RosterDesignationRow row) || !row.Covers(day) || row.IsActive;

    /// <summary>True when some row covers the day (the week is locked).</summary>
    public bool IsLocked(long day) => LockedThroughDay > day;

    /// <summary>Fills <paramref name="destination"/> (cleared first) with every player inactive on the day.</summary>
    public int CopyInactive(long day, List<string> destination)
    {
        destination.Clear();
        for (int i = 0; i < _inactive.Count; i++)
        {
            if (_inactive[i].Covers(day))
            {
                destination.Add(_inactive[i].PlayerId);
            }
        }
        return destination.Count;
    }

    /// <summary>The player's covering row on the day, if any (harness and UI visibility).</summary>
    public bool TryGetDesignation(string playerId, long day, out RosterDesignationRow row) =>
        _rows.TryGetValue(playerId, out row) && row.Covers(day);

    /// <summary>
    /// The read-only <see cref="RosterRules.CanField"/> façade for tools and
    /// UI (D2e): the team's eligible (available ∧ active) position players
    /// and pitchers on the day, from the tier sim's roster. No series filter
    /// (the sims apply The Red Dirt Series' themselves). False for an unknown team.
    /// </summary>
    public bool CanField(int teamId, long day)
    {
        if (!TryFindTeam(teamId, out LeagueSimulator sim, out int team))
        {
            return false;
        }
        int positionPlayers = 0;
        foreach (int slot in sim.PositionOrder(team))
        {
            positionPlayers += IsEligible(sim.PlayerIdAt(slot), day) ? 1 : 0;
        }
        int pitchers = 0;
        foreach (int slot in sim.StarterRank(team))
        {
            pitchers += IsEligible(sim.PlayerIdAt(slot), day) ? 1 : 0;
        }
        foreach (int slot in sim.RelieverRank(team))
        {
            pitchers += IsEligible(sim.PlayerIdAt(slot), day) ? 1 : 0;
        }
        return RosterRules.CanField(positionPlayers, pitchers);
    }

    private bool IsEligible(string playerId, long day) =>
        IsActive(playerId, day)
        && (Availability is null || Availability.StateFor(playerId, day) != SlotAvailability.Absent);

    /// <summary>The team's designations as presentation reads them (D8-A). Every player reads active outside coverage.</summary>
    public RosterStatusView GetStatusView(int teamId, long day)
    {
        var active = new List<RosterStatusEntry>();
        var inactive = new List<RosterStatusEntry>();
        string? avatarId = ResolveAvatarId();
        bool hasAvatar = false;
        bool avatarActive = true;
        if (TryFindTeam(teamId, out LeagueSimulator sim, out int team))
        {
            AddStatus(sim, sim.PositionOrder(team), day, avatarId, active, inactive, ref hasAvatar, ref avatarActive);
            AddStatus(sim, sim.StarterRank(team), day, avatarId, active, inactive, ref hasAvatar, ref avatarActive);
            AddStatus(sim, sim.RelieverRank(team), day, avatarId, active, inactive, ref hasAvatar, ref avatarActive);
        }
        bool locked = IsLocked(day) && sim is not null && sim.Tier == LeagueTier.Pro;
        return new RosterStatusView(teamId, day, locked, locked ? LockedThroughDay : 0, active, inactive,
            hasAvatar, avatarActive);
    }

    private void AddStatus(
        LeagueSimulator sim, ReadOnlySpan<int> slots, long day, string? avatarId,
        List<RosterStatusEntry> active, List<RosterStatusEntry> inactive, ref bool hasAvatar, ref bool avatarActive)
    {
        foreach (int slot in slots)
        {
            string id = sim.PlayerIdAt(slot);
            bool isAvatar = avatarId is not null && string.Equals(id, avatarId, StringComparison.Ordinal);
            bool isActive = IsActive(id, day);
            (isActive ? active : inactive).Add(new RosterStatusEntry(id, isActive, isAvatar));
            if (isAvatar)
            {
                hasAvatar = true;
                avatarActive = isActive;
            }
        }
    }

    private bool TryFindTeam(int teamId, out LeagueSimulator sim, out int team)
    {
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            if (_leagues.TryGet((LeagueTier)t, out sim))
            {
                team = sim.IndexOfTeam(teamId);
                if (team >= 0)
                {
                    return true;
                }
            }
        }
        sim = null!;
        team = -1;
        return false;
    }

    // ------------------------------------------------------------------
    // Boot: hydrate, then reconcile a missed lock (D6-A, Amendment 1)
    // ------------------------------------------------------------------

    /// <summary>
    /// Boot path: loads the Pro rows covering <paramref name="today"/>, then,
    /// if none covers it on a day inside the window (91–279), locks now with
    /// the most recent Sunday as the nominal day — the crash between a day's
    /// commit and its lock, including on Sunday itself (under the strict rule
    /// last week's row stops covering on lock day). Call after the tier sims'
    /// Initialize and PrepareSeason. Returns true when it reconciled.
    /// </summary>
    public bool HydrateAndReconcile(long today, int seasonYear, int dayOfSeason)
    {
        Hydrate(today);
        if (dayOfSeason < RosterRules.FirstLockDayOfSeason || dayOfSeason > RosterRules.LastCoveredDayOfSeason
            || _queries.LatestLockedThroughDay(LeagueTier.Pro) > today)
        {
            return false;
        }
        int nominal = RosterRules.SundayOnOrBefore(dayOfSeason);
        Lock(seasonYear, today - (dayOfSeason - nominal), nominal);
        return true;
    }

    /// <summary>Replaces the in-memory mirror with the persisted rows covering the day (no write).</summary>
    public void Hydrate(long today)
    {
        _queries.LoadCovering(LeagueTier.Pro, today, _hydrateScratch);
        _staged.Clear();
        _staged.AddRange(_hydrateScratch);
        long horizon = 0;
        foreach (RosterDesignationRow row in _hydrateScratch)
        {
            horizon = Math.Max(horizon, row.LockedThroughDay);
        }
        Publish(horizon);
        if (_hydrateScratch.Count > 0)
        {
            Version++;
        }
    }

    private void Publish(long horizon)
    {
        _rows.Clear();
        _inactive.Clear();
        foreach (RosterDesignationRow row in _staged)
        {
            _rows[row.PlayerId] = row;
            if (!row.IsActive)
            {
                _inactive.Add(row);
            }
        }
        LockedThroughDay = horizon;
        _staged.Clear();
    }

    // ------------------------------------------------------------------
    // The lock
    // ------------------------------------------------------------------

    /// <summary>
    /// Locks every Pro team for the week starting on <paramref name="lockDay"/>
    /// (absolute; <paramref name="lockDayOfSeason"/> its Sunday): one batch of
    /// upserts, then the in-memory mirror, <see cref="Version"/>, and the
    /// avatar's <see cref="RosterDesignationChangedEvent"/> after the commit.
    /// Idempotent: a re-lock of the same week writes the same rows.
    /// </summary>
    internal void Lock(int seasonYear, long lockDay, int lockDayOfSeason)
    {
        if (!_leagues.TryGet(LeagueTier.Pro, out LeagueSimulator sim))
        {
            return;
        }
        sim.PrepareSeason(seasonYear);
        long lockedThrough = lockDay + GameCalendar.DaysPerWeek;
        string? avatarId = ResolveAvatarId();
        int avatarTeamId = 0;
        bool avatarSeen = false;
        bool avatarActive = true;

        // C2: the avatar's prior designation, read before the lock overwrites it.
        bool wasActive = true;
        if (avatarId is not null
            && _queries.TryGetPrior(avatarId, out bool priorActive, out long priorThrough)
            && priorThrough == lockDay)
        {
            wasActive = priorActive;
        }

        _staged.Clear();
        int activeCount = 0;
        _db.BeginBatch();
        try
        {
            for (int team = 0; team < sim.TeamCount; team++)
            {
                int teamId = sim.TeamIdAt(team);
                int gameDays = CollectGameDays(sim, seasonYear, lockDay, lockDayOfSeason, teamId);
                bool avatarHere = false;

                ReadOnlySpan<int> positions = sim.PositionOrder(team);
                for (int k = 0; k < positions.Length; k++)
                {
                    _positionGroup[k] = Candidate(sim, positions[k], gameDays, avatarId, DesignationRole.PositionPlayer, seasonYear);
                    avatarHere |= _positionGroup[k].IsAvatar;
                }
                RosterRules.SelectPositionPlayers(_positionGroup.AsSpan(0, positions.Length), _activeScratch, _lineupScratch);
                activeCount += Stage(_positionGroup.AsSpan(0, positions.Length), teamId, lockedThrough,
                    ref avatarActive, ref avatarSeen);

                ReadOnlySpan<int> starters = sim.StarterRank(team);
                for (int k = 0; k < starters.Length; k++)
                {
                    _starterGroup[k] = Candidate(sim, starters[k], gameDays, avatarId, DesignationRole.Starter, seasonYear);
                    avatarHere |= _starterGroup[k].IsAvatar;
                }
                RosterRules.SelectPitchers(_starterGroup.AsSpan(0, starters.Length), RosterRules.ActiveStarters,
                    DesignationRole.Starter, _activeScratch);
                activeCount += Stage(_starterGroup.AsSpan(0, starters.Length), teamId, lockedThrough,
                    ref avatarActive, ref avatarSeen);

                ReadOnlySpan<int> relievers = sim.RelieverRank(team);
                for (int k = 0; k < relievers.Length; k++)
                {
                    _relieverGroup[k] = Candidate(sim, relievers[k], gameDays, avatarId, DesignationRole.Reliever, seasonYear);
                    avatarHere |= _relieverGroup[k].IsAvatar;
                }
                RosterRules.SelectPitchers(_relieverGroup.AsSpan(0, relievers.Length), RosterRules.ActiveRelievers,
                    DesignationRole.Reliever, _activeScratch);
                activeCount += Stage(_relieverGroup.AsSpan(0, relievers.Length), teamId, lockedThrough,
                    ref avatarActive, ref avatarSeen);

                if (avatarHere)
                {
                    avatarTeamId = teamId;
                }
            }
            foreach (RosterDesignationRow row in _staged)
            {
                _queries.Upsert(row.PlayerId, row.TeamId, row.IsActive, row.LockedThroughDay);
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            _staged.Clear();
            throw;
        }

        LastLockRows = _staged.Count;
        LastLockActive = activeCount;
        LastLockDay = lockDay;
        Publish(lockedThrough);
        Version++;
        LockCount++;

        if (avatarSeen && avatarActive != wasActive)
        {
            _bus?.Publish(new RosterDesignationChangedEvent(avatarId!, avatarTeamId, avatarActive, wasActive, lockedThrough));
        }
    }

    private int Stage(ReadOnlySpan<DesignationCandidate> group, int teamId, long lockedThrough,
        ref bool avatarActive, ref bool avatarSeen)
    {
        int active = 0;
        for (int i = 0; i < group.Length; i++)
        {
            bool isActive = _activeScratch[i];
            _staged.Add(new RosterDesignationRow(group[i].PlayerId, teamId, isActive, lockedThrough));
            active += isActive ? 1 : 0;
            if (group[i].IsAvatar)
            {
                avatarSeen = true;
                avatarActive = isActive;
            }
        }
        return active;
    }

    /// <summary>
    /// The team's game days in the locked week (C1): its regular-season games
    /// on Monday–Saturday, plus Pro's postseason slot days (the D3 slots). A
    /// week with none counts every player as healthy.
    /// </summary>
    private int CollectGameDays(LeagueSimulator sim, int seasonYear, long lockDay, int lockDayOfSeason, int teamId)
    {
        int count = 0;
        for (int d = 1; d < GameCalendar.DaysPerWeek; d++)
        {
            int dayOfSeason = lockDayOfSeason + d;
            if (sim.TryGetScheduledGame(seasonYear, dayOfSeason, teamId, out _, out _, out _)
                || (dayOfSeason < ProPostseasonSlotDay.Length && ProPostseasonSlotDay[dayOfSeason]))
            {
                _gameDays[count++] = lockDay + d;
            }
        }
        return count;
    }

    private DesignationCandidate Candidate(
        LeagueSimulator sim, int slot, int gameDays, string? avatarId, DesignationRole role, int seasonYear)
    {
        string playerId = sim.PlayerIdAt(slot);
        var c = new DesignationCandidate
        {
            Slot = slot,
            PlayerId = playerId,
            TeamGameDays = gameDays,
            IsAvatar = avatarId is not null && string.Equals(playerId, avatarId, StringComparison.Ordinal),
        };
        if (role == DesignationRole.PositionPlayer)
        {
            LineupCandidate lc = sim.CandidateAt(slot);
            c.BatScore = lc.BatScore;
            c.Primary = lc.Primary;
            c.Secondary = lc.Secondary;
        }
        else
        {
            PitcherRatings baked = sim.BakedPitcherAt(slot);
            c.Quality = StarterSelection.Quality(in baked);
            c.Stamina = baked.Stamina;
        }

        if (Availability is not null && Availability.TryGet(playerId, out AbsenceEntry absence))
        {
            c.UntilDay = absence.UntilDay;
            bool rustRead = false;
            for (int g = 0; g < gameDays; g++)
            {
                SlotAvailability state = absence.StateOn(_gameDays[g]);
                if (state == SlotAvailability.Absent)
                {
                    c.MissedGames++;
                }
                else if (!rustRead)
                {
                    // Rust on the first game day he's available (Q3's d₀ under C1).
                    rustRead = true;
                    c.Rust = state == SlotAvailability.Rusty ? absence.RatingPenalty : (byte)0;
                }
            }
        }

        if (c.IsAvatar)
        {
            c.AvatarDelta = AvatarDelta(playerId, role, seasonYear);
        }
        return c;
    }

    /// <summary>The avatar's ΔP + ΔA (Q3, D4c-A): current-season Pro performance and the person-row affinity proxy.</summary>
    private double AvatarDelta(string avatarId, DesignationRole role, int seasonYear)
    {
        double performance;
        if (role == DesignationRole.PositionPlayer)
        {
            performance = 0.0;
            if (_baseball.TryGetBattingSeasonLine(avatarId, seasonYear, out BattingSeasonLine line))
            {
                LeagueBattingTotals tier = _baseball.LoadLeagueBattingTotals(seasonYear, LeagueTier.Pro);
                double ops = RosterRules.Ops(line.Ab, line.H, line.Doubles, line.Triples, line.Hr, line.Bb);
                double tierOps = RosterRules.Ops(tier.Ab, tier.H, tier.Doubles, tier.Triples, tier.Hr, tier.Bb);
                performance = RosterRules.BattingPerformanceDelta(line.Pa, ops, tierOps);
            }
        }
        else
        {
            performance = 0.0;
            if (_queries.TryGetPitchingCounts(avatarId, seasonYear, out long outs, out long er))
            {
                LeaguePitchingTotals tier = _baseball.LoadLeaguePitchingTotals(seasonYear, LeagueTier.Pro);
                performance = RosterRules.PitchingPerformanceDelta(outs, er, tier.OutsRecorded, tier.Er);
            }
        }
        double affinity = 0.0;
        if (Persons is not null && Persons.TryGet(avatarId, out PersonRow person))
        {
            affinity = RosterRules.AffinityDelta(person.WorkEthic, person.Discipline, person.Teamwork);
        }
        return performance + affinity;
    }

    private string? ResolveAvatarId() =>
        _gameState.TryGetText(GameStateKeys.AvatarPlayerId, out string id) && id.Length > 0 ? id : null;

    private static bool[] BuildProPostseasonSlotDays()
    {
        var days = new bool[GlobalState.DaysPerSeason + 1];
        for (int round = 1; round <= PostseasonCalendar.RoundCount(LeagueTier.Pro); round++)
        {
            for (int game = 1; game <= PostseasonCalendar.BestOf(LeagueTier.Pro, round); game++)
            {
                days[PostseasonCalendar.GameDay(LeagueTier.Pro, round, game)] = true;
            }
        }
        return days;
    }
}
