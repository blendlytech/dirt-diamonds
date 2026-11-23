using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Narrative.Roster;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.MonteCarloHarness;

// ----------------------------------------------------------------------
// Slice 13-5 (docs/tasks/13-5_roster_lock_forfeits_brief.md): the Pro Sunday
// lock and its designations (D4, D6-A; Math record §10.1 Q1–Q4, §10.5 C1–C3),
// attrition, forfeits in every tier (D2), the attended forfeit (D2f,
// Amendment 6), the regular-season StartUndo revert (D3-A item 4), the coach
// beats (D5-A, Q6), the guard world's D1-A invariance channels, and the
// allocation probes. The suites share two generated v14 worlds (Amendment 7):
// a flat spread-0 world and a spread-25 person-baked world. Fixtures key their
// own season years (5000+) so they never collide.
// `--only designations,attrition,forfeits,roster-beats,attended-forfeit,attended-undo`.
// ----------------------------------------------------------------------
internal static partial class Program
{
    /// <summary>First season year of the 13-5 fixtures (clear of 13-4's 4000+).</summary>
    private const int Slice135BaseYear = 5000;

    /// <summary>A 13-5 fixture's absolute day (the same convention as <see cref="Abs134"/>).</summary>
    private static long Abs135(int year, int dayOfSeason) => (long)(year - StartYear) * GlobalState.DaysPerSeason + dayOfSeason;

    private sealed class Slice135World : IDisposable
    {
        public readonly DatabaseManager Db;
        public readonly PlayerQueries Players;
        public readonly BaseballQueries Baseball;
        public readonly StatsNormalizer Normalizer;
        public readonly GameStateQueries GameState;
        public readonly ScheduleQueries Schedule;
        public readonly PersonQueries? Persons;
        private readonly LeagueSimulator?[] _sims = new LeagueSimulator?[LeagueDirectory.TierCount];

        public Slice135World(string schemaPath, string path, int spread, bool persons)
        {
            Db = new DatabaseManager(path);
            Db.InitializeSchema(schemaPath);
            Players = new PlayerQueries(Db);
            Baseball = new BaseballQueries(Db);
            Persons = persons ? new PersonQueries(Db) : null;
            var genRng = new RngState(LeagueSeed);
            LeagueGenerator.GenerateIfEmpty(Db, Players, Baseball, spread, ref genRng, Persons);
            Normalizer = new StatsNormalizer(Db, Baseball);
            GameState = new GameStateQueries(Db);
            Schedule = new ScheduleQueries(Db);
        }

        /// <summary>The world's shared sim per tier (person-baked in the person world, C3).</summary>
        public LeagueSimulator Sim(LeagueTier tier)
        {
            LeagueSimulator? sim = _sims[(int)tier];
            if (sim is null)
            {
                sim = NewSim(tier, 0x1350_0000UL + (ulong)tier);
                _sims[(int)tier] = sim;
            }
            return sim;
        }

        /// <summary>A dedicated sim (its own rest ledger and accumulators) for a fixture that must not share state.</summary>
        public LeagueSimulator NewSim(LeagueTier tier, ulong seed)
        {
            var sim = new LeagueSimulator(Db, Baseball, Normalizer, new RngState(seed), tier) { Persons = Persons };
            sim.Initialize();
            return sim;
        }

        public LeagueDirectory Leagues(params LeagueSimulator[] sims)
        {
            var directory = new LeagueDirectory();
            foreach (LeagueSimulator sim in sims)
            {
                directory.Register(sim);
            }
            return directory;
        }

        public void Dispose() => Db.Dispose();
    }

    private static Slice135World? _world135Flat;
    private static Slice135World? _world135Spread;
    private static readonly List<string> Slice135Paths = new();

    private static Slice135World World135Flat(string schemaPath) =>
        _world135Flat ??= new Slice135World(schemaPath, Slice135TempPath("flat"), spread: 0, persons: false);

    private static Slice135World World135Spread(string schemaPath) =>
        _world135Spread ??= new Slice135World(schemaPath, Slice135TempPath("spread"), spread: 25, persons: true);

    /// <summary>Main's finally: closes the 13-5 worlds and deletes their scratch files.</summary>
    private static void CleanupSlice135()
    {
        _world135Flat?.Dispose();
        _world135Spread?.Dispose();
        _world135Flat = null;
        _world135Spread = null;
        foreach (string path in Slice135Paths)
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
        Slice135Paths.Clear();
    }

    private static string Slice135TempPath(string tag)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dnd_135{tag}_{Guid.NewGuid():N}.db");
        Slice135Paths.Add(path);
        return path;
    }

    /// <summary>Publishes one absence into a ledger (Version moves, as the Narrative applier's publish does).</summary>
    private static void Absent(AvailabilityLedger ledger, string playerId, long untilDay, byte rust = 0, long rustUntil = 0)
    {
        var bus = new EventBus();
        ledger.AttachTo(bus);
        bus.Publish(new PlayerAbsenceChangedEvent(
            playerId, (byte)(rust > 0 ? AbsenceReason.Injury : AbsenceReason.Suspension), untilDay, rust, rustUntil));
        bus.DispatchPending();
        ledger.DetachFrom(bus);
    }

    /// <summary>One day's distinct scheduled games of a tier (game id, home, away), from the loaded season.</summary>
    private static List<(long GameId, int Home, int Away)> GamesOnDay(LeagueSimulator sim, int year, int dayOfSeason)
    {
        var games = new List<(long, int, int)>();
        var seen = new HashSet<long>();
        foreach (int teamId in sim.TeamIds.ToArray())
        {
            if (sim.TryGetScheduledGame(year, dayOfSeason, teamId, out long id, out int home, out int away) && seen.Add(id))
            {
                games.Add((id, home, away));
            }
        }
        return games;
    }

    private static string[] PositionIds(LeagueSimulator sim, int teamId)
    {
        int team = sim.IndexOfTeam(teamId);
        return sim.PositionOrder(team).ToArray().Select(sim.PlayerIdAt).ToArray();
    }

    private static string[] StarterIds(LeagueSimulator sim, int teamId)
    {
        int team = sim.IndexOfTeam(teamId);
        return sim.StarterRank(team).ToArray().Select(sim.PlayerIdAt).ToArray();
    }

    private static string[] RelieverIds(LeagueSimulator sim, int teamId)
    {
        int team = sim.IndexOfTeam(teamId);
        return sim.RelieverRank(team).ToArray().Select(sim.PlayerIdAt).ToArray();
    }

    private static string[] InitialLineupIds(LeagueSimulator sim, int teamId)
    {
        int team = sim.IndexOfTeam(teamId);
        return sim.InitialLineup(team).ToArray().Select(sim.PlayerIdAt).ToArray();
    }

    private static string[] GameLineupIds(LeagueSimulator sim, int teamId)
    {
        int team = sim.IndexOfTeam(teamId);
        return sim.GameLineup(team).ToArray().Select(sim.PlayerIdAt).ToArray();
    }

    private static DesignationCandidate Dc(string id, int bat, FieldingPosition? primary, FieldingPosition? secondary = null) => new()
    {
        PlayerId = id,
        BatScore = bat,
        Primary = primary,
        Secondary = secondary,
        TeamGameDays = 3,
    };

    private static DesignationCandidate Pc(string id, int quality, int stamina = 50) => new()
    {
        PlayerId = id,
        Quality = quality,
        Stamina = stamina,
        TeamGameDays = 3,
    };

    /// <summary>Runs the position-player selector over a group (sorted into LineupBuilder order, slots assigned); returns the active ids.</summary>
    private static HashSet<string> SelectActive(List<DesignationCandidate> group)
    {
        var ordered = group.ToList();
        ordered.Sort((a, b) => LineupBuilder.CompareOrder(a.AsLineupCandidate(), b.AsLineupCandidate()));
        for (int i = 0; i < ordered.Count; i++)
        {
            DesignationCandidate c = ordered[i];
            c.Slot = 1000 + i;
            ordered[i] = c;
        }
        var array = ordered.ToArray();
        var active = new bool[array.Length];
        RosterRules.SelectPositionPlayers(array, active, new LineupCandidate[array.Length]);
        return array.Where((_, i) => active[i]).Select(c => c.PlayerId).ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> SelectActivePitchers(List<DesignationCandidate> ranked, int target)
    {
        var array = ranked.ToArray();
        var active = new bool[array.Length];
        RosterRules.SelectPitchers(array, target, DesignationRole.Starter, active);
        return array.Where((_, i) => active[i]).Select(c => c.PlayerId).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The Q2/Q3 fixture's nine starters (bat 192–200), every field position covered by a primary.</summary>
    private static List<DesignationCandidate> FixtureStarters() => new()
    {
        Dc("s_c", 200, FieldingPosition.C), Dc("s_1b", 199, FieldingPosition.FirstBase),
        Dc("s_2b", 198, FieldingPosition.SecondBase), Dc("s_3b", 197, FieldingPosition.ThirdBase),
        Dc("s_ss", 196, FieldingPosition.Shortstop), Dc("s_lf", 195, FieldingPosition.LeftField),
        Dc("s_cf", 194, FieldingPosition.CenterField), Dc("s_rf", 193, FieldingPosition.RightField),
        Dc("s_dh", 192, FieldingPosition.FirstBase),
    };

    // ------------------------------------------------------------------
    // designations (D4a-A/b-A/c-A, D6-A; Q1–Q4; C1–C3)
    // ------------------------------------------------------------------

    private static void RunDesignationsSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-5 designations: the Sunday lock (768 rows, 12/3/3), unfeatured starters, Q2–Q4, C1–C3, reload ---");
        Slice135World w = World135Spread(schemaPath);
        LeagueSimulator pro = w.Sim(LeagueTier.Pro);
        int year = Slice135BaseYear;
        pro.PrepareSeason(year);
        RngState rngBefore = pro.RngSnapshot;
        int[] teamIds = pro.TeamIds.ToArray();

        // ---- subset-greedy parity: LineupBuilder.Build vs the allocation-free twin ----
        var parityRng = new RngState(0x135A_0001UL);
        int trials = 1000;
        int mismatches = 0;
        string firstMismatch = "";
        var twinSlots = new int[RosterCapacity.Lineup];
        var twinPositions = new sbyte[RosterCapacity.Lineup];
        for (int trial = 0; trial < trials; trial++)
        {
            int team = trial % teamIds.Length;
            List<LineupCandidate> all = pro.PositionOrder(team).ToArray().Select(pro.CandidateAt).ToList();
            int drop = parityRng.NextInt(all.Count - RosterCapacity.Lineup + 1);
            var kept = all.ToList();
            var dropped = new List<LineupCandidate>();
            for (int d = 0; d < drop; d++)
            {
                int at = parityRng.NextInt(kept.Count);
                dropped.Add(kept[at]);
                kept.RemoveAt(at);
            }
            int featured = (trial % 3) switch
            {
                0 => -1,
                1 => kept[parityRng.NextInt(kept.Count)].Slot,
                _ => dropped.Count > 0 ? dropped[0].Slot : -1,
            };
            // Build sees a shuffled list (it sorts); the twin the sorted eligible subset.
            var shuffled = kept.OrderBy(_ => parityRng.NextUInt64()).ToList();
            LineupResult reference = LineupBuilder.Build(shuffled, featured);
            var sorted = kept.ToList();
            sorted.Sort((a, b) => LineupBuilder.CompareOrder(in a, in b));
            int oop = LineupBuilder.BuildInto(sorted.ToArray(), featured, twinSlots, twinPositions);
            bool same = oop == reference.OutOfPosition;
            for (int k = 0; k < RosterCapacity.Lineup && same; k++)
            {
                sbyte expected = reference.Positions[k] is FieldingPosition p ? (sbyte)p : LineupBuilder.DesignatedHitter;
                same = twinSlots[k] == reference.Slots[k] && twinPositions[k] == expected;
            }
            if (!same)
            {
                mismatches++;
                firstMismatch = firstMismatch.Length > 0 ? firstMismatch
                    : $"trial {trial}: build [{string.Join(",", reference.Slots)}] twin [{string.Join(",", twinSlots)}]";
            }
        }
        Check($"lineup twin: BuildInto equals LineupBuilder.Build slot for slot (slots, positions, out-of-position) over {trials} random eligible subsets of the spread-25 Pro rosters, featured and unfeatured",
            mismatches == 0, mismatches == 0 ? $"{trials} trials" : $"{mismatches} mismatches; {firstMismatch}");
        {
            var probe = pro.PositionOrder(0).ToArray().Select(pro.CandidateAt).ToList();
            probe.Sort((a, b) => LineupBuilder.CompareOrder(in a, in b));
            LineupCandidate[] probeArray = probe.Skip(2).ToArray();
            int sink = 0;
            for (int i = 0; i < 100; i++)
            {
                sink += LineupBuilder.BuildInto(probeArray, -1, twinSlots, twinPositions);
            }
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                sink += LineupBuilder.BuildInto(probeArray, i % 2 == 0 ? -1 : probeArray[^1].Slot, twinSlots, twinPositions);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Check("lineup twin allocates zero bytes over 10k rebuilds", allocated == 0, $"{allocated} B (sink {sink})");
        }

        // ---- the lock: 768 rows, 12/3/3 per team, starters unfeatured, pitchers on baked quality (C3) ----
        var ledger = new AvailabilityLedger();
        var bus = new EventBus();
        var changed = new List<RosterDesignationChangedEvent>();
        bus.Subscribe<RosterDesignationChangedEvent>(e => changed.Add(e));
        var roster = new RosterManager(w.Db, w.Baseball, w.Leagues(pro)) { Availability = ledger, Persons = w.Persons };
        roster.AttachTo(bus);
        w.GameState.SetText(GameStateKeys.AvatarPlayerId, "");
        int lock1 = 98;
        long lock1Day = Abs135(year, lock1);
        int version0 = roster.Version;
        roster.Lock(year, lock1Day, lock1);
        long rows = CountRows(w.Db, $"SELECT COUNT(*) FROM Roster_Designations WHERE locked_through_day = {lock1Day + 7};");
        long activeRows = CountRows(w.Db, $"SELECT COUNT(*) FROM Roster_Designations WHERE locked_through_day = {lock1Day + 7} AND is_active = 1;");
        bool shape = true;
        bool startersActive = true;
        bool pitchersBaked = true;
        int rawOrderDiffers = 0;
        var rawRoster = new List<RosterPlayerRow>();
        w.Baseball.LoadRosterByTier(LeagueTier.Pro, rawRoster);
        long day = lock1Day + 2;
        foreach (int teamId in teamIds)
        {
            string[] pos = PositionIds(pro, teamId);
            string[] sp = StarterIds(pro, teamId);
            string[] rp = RelieverIds(pro, teamId);
            shape &= pos.Count(id => roster.IsActive(id, day)) == RosterRules.ActivePositionPlayers
                && sp.Count(id => roster.IsActive(id, day)) == RosterRules.ActiveStarters
                && rp.Count(id => roster.IsActive(id, day)) == RosterRules.ActiveRelievers;
            startersActive &= InitialLineupIds(pro, teamId).All(id => roster.IsActive(id, day));
            pitchersBaked &= sp.Take(3).All(id => roster.IsActive(id, day)) && rp.Take(3).All(id => roster.IsActive(id, day));
            var rawTop = rawRoster.Where(r => r.TeamId == teamId && r.IsPitcher && r.Role == PitcherRole.Starter)
                .OrderByDescending(r => r.PitStuff + r.PitControl).ThenByDescending(r => r.PitStamina)
                .ThenBy(r => r.PlayerId, StringComparer.Ordinal).Take(3).Select(r => r.PlayerId).ToHashSet();
            rawOrderDiffers += rawTop.SetEquals(sp.Take(3)) ? 0 : 1;
        }
        Check("the lock writes 768 rows (576 active, 192 inactive), locked_through_day = lock day + 7, Version +1",
            rows == 768 && activeRows == 576 && roster.LastLockRows == 768 && roster.LastLockActive == 576
            && roster.Version == version0 + 1 && roster.LockedThroughDay == lock1Day + 7,
            $"rows {rows}, active {activeRows}, version {version0}→{roster.Version}");
        Check("every Pro team: 12 position players, 3 SP, 3 RP active", shape);
        Check("Q1 / Amendment 2: each team's nine LineupBuilder starters (computed unfeatured) are active", startersActive);
        Check($"C3: the active SP and RP are the top 3 by the sims' baked CompareQuality (person-baked world; raw ratings would pick a different SP trio on {rawOrderDiffers} teams)",
            pitchersBaked);

        // ---- Q2 bench score (every weight, the catcher-bucket clear, the PlayerId tie) ----
        {
            var benchA = new List<DesignationCandidate>
            {
                Dc("b_c1", 135, FieldingPosition.C), Dc("b_c2", 152, FieldingPosition.C),
                Dc("b_mi", 140, FieldingPosition.Shortstop, FieldingPosition.SecondBase),
                Dc("b_of", 142, FieldingPosition.CenterField), Dc("b_1b", 156, FieldingPosition.FirstBase),
            };
            HashSet<string> a = SelectActive(FixtureStarters().Concat(benchA).ToList());
            var benchB = benchA.Select(c => c.PlayerId == "b_1b" ? Dc("b_1b", 158, FieldingPosition.FirstBase) : c).ToList();
            HashSet<string> b = SelectActive(FixtureStarters().Concat(benchB).ToList());
            HashSet<string> c = SelectActive(FixtureStarters().Concat(benchA)
                .Append(Dc("b_3b_util", 151, FieldingPosition.ThirdBase, FieldingPosition.FirstBase)).ToList());
            HashSet<string> d = SelectActive(FixtureStarters().Concat(benchA)
                .Append(Dc("b_z_3b_util", 151, FieldingPosition.ThirdBase, FieldingPosition.FirstBase)).ToList());
            bool starters9 = FixtureStarters().All(s => a.Contains(s.PlayerId));
            Check("Q2 bench: backup C 24 (b_c2 176), middle infield 18 + secondary 6 (b_mi 164), outfield 15 (b_of 157 > b_1b 156); the catcher bucket clears so b_c1 sits",
                starters9 && a.Count == 12 && a.Contains("b_c2") && a.Contains("b_mi") && a.Contains("b_of")
                && !a.Contains("b_c1") && !a.Contains("b_1b"),
                string.Join(",", a.Where(x => x.StartsWith("b_")).OrderBy(x => x)));
            Check("Q2 bench: bat score overrides coverage on merit (b_1b at 158 beats b_of's 157)",
                b.Contains("b_1b") && !b.Contains("b_of"), string.Join(",", b.Where(x => x.StartsWith("b_")).OrderBy(x => x)));
            Check("Q2 bench: an exact 157 tie breaks by PlayerId ordinal ascending (b_3b_util beats b_of; b_z_3b_util loses to it)",
                c.Contains("b_3b_util") && !c.Contains("b_of") && d.Contains("b_of") && !d.Contains("b_z_3b_util"));
        }

        // ---- Q3 avatar margin: each of ΔP, ΔH, ΔA flips the call; the minimum-PA gate; starting-calibre protection ----
        {
            bool AvatarActive(int bat, double delta, int missed = 0, byte rust = 0)
            {
                var group = FixtureStarters();
                group.Add(Dc("npc_b1", 160, FieldingPosition.FirstBase));
                group.Add(Dc("npc_b2", 155, FieldingPosition.FirstBase));
                group.Add(Dc("npc_b3", 150, FieldingPosition.FirstBase));
                group.Add(Dc("npc_b4", 140, FieldingPosition.FirstBase));
                DesignationCandidate avatar = Dc("avatar", bat, FieldingPosition.FirstBase);
                avatar.IsAvatar = true;
                avatar.AvatarDelta = delta;
                avatar.MissedGames = missed;
                avatar.Rust = rust;
                group.Add(avatar);
                return SelectActive(group).Contains("avatar");
            }
            double slump = RosterRules.BattingPerformanceDelta(60, 0.490, 0.700);
            double gated = RosterRules.BattingPerformanceDelta(16, 0.490, 0.700);
            double hot = RosterRules.BattingPerformanceDelta(60, 0.910, 0.700);
            double low = RosterRules.AffinityDelta(30, 30, 30);
            double high = RosterRules.AffinityDelta(70, 70, 70);
            double pit = RosterRules.PitchingPerformanceDelta(54, 14, 270, 42);
            bool values = Math.Abs(slump + 5.4) < 1e-9 && gated == 0.0 && Math.Abs(hot - 5.4) < 1e-9
                && Math.Abs(low + 6.0) < 1e-9 && Math.Abs(high - 6.0) < 1e-9 && Math.Abs(pit + 9.0) < 1e-9
                && RosterRules.AffinityDelta(50, 50, 50) == 0.0;
            Check("Q3 inputs: ΔP slump −5.4 (60 PA, .490 v .700), 16 PA gated to 0, hot +5.4; ΔA ±6.0 at 30s/70s and 0 at 50s; pitcher ΔP −9.0 (ERA 7.00 v 4.20, 18 IP)",
                values, $"slump {slump:F3} hot {hot:F3} low {low:F3} high {high:F3} pitcher {pit:F3}");
            bool baseline = AvatarActive(152, 0);
            Check("Q3 flip 1 (ΔP): 152 active by +2; a slump makes it 146.6 (inactive); under 20 PA the gate keeps it active; a hot 148 rises to 153.4 (active)",
                baseline && !AvatarActive(152, slump) && AvatarActive(152, gated) && !AvatarActive(148, 0) && AvatarActive(148, hot));
            Check("Q3 flip 2 (ΔH, C1): an absence covering Tuesday's game (return lock day + 3) is −12 → 140 (inactive); rust 2 is −6 → 146 (inactive); missing every game day sends him to the unhealthy pool",
                !AvatarActive(152, 0, missed: 1) && !AvatarActive(152, 0, rust: 2) && !AvatarActive(200, 0, missed: 3));
            Check("Q3 flip 3 (ΔA): low affinity 30/30/30 is −6 → 146 (inactive); high 70/70/70 lifts a trailing 148 to 154 (active)",
                !AvatarActive(152, low) && AvatarActive(148, high));
            var calibre = FixtureStarters().Where(s => s.PlayerId != "s_1b").ToList();
            DesignationCandidate star = Dc("avatar", 205, FieldingPosition.FirstBase);
            star.IsAvatar = true;
            star.AvatarDelta = RosterRules.BattingPerformanceDelta(120, 0.350, 0.700) + RosterRules.AffinityDelta(10, 10, 10);
            star.Rust = 8;
            calibre.Add(star);
            calibre.AddRange(new[] { Dc("npc_b1", 160, FieldingPosition.FirstBase), Dc("npc_b2", 155, FieldingPosition.FirstBase),
                Dc("npc_b3", 150, FieldingPosition.FirstBase), Dc("npc_b4", 140, FieldingPosition.FirstBase) });
            Check("Q3 protection (D4b-A): a starting-calibre avatar in the nine stays active whatever his ΔP, rust and ΔA",
                SelectActive(calibre).Contains("avatar"), $"delta {star.AvatarDelta:F1}, rust 8");
            List<DesignationCandidate> Staff(int avatarQuality, double delta)
            {
                DesignationCandidate a = Pc("avatar", avatarQuality);
                a.IsAvatar = true;
                a.AvatarDelta = delta;
                return new List<DesignationCandidate> { Pc("sp1", 120), Pc("sp2", 118), a, Pc("sp3", 100) };
            }
            Check("Q3 pitcher margin: a #3/#4 avatar at 102 beats the NPC's 100; ERA 7.00 over 18 IP (ΔP −9.0) makes it 93 (inactive)",
                SelectActivePitchers(Staff(102, 0), 3).Contains("avatar") && !SelectActivePitchers(Staff(102, pit), 3).Contains("avatar"));
        }

        // ---- C1 health, Q4 unhealthy fill, the injured ace (a real lock) ----
        int t3 = teamIds[3];
        int t4 = teamIds[4];
        int lock2 = 105;
        long lock2Day = Abs135(year, lock2);
        string[] sp3 = StarterIds(pro, t3);
        Absent(ledger, sp3[0], lock2Day + 10); // sp1: out all week
        Absent(ledger, sp3[1], lock2Day + 4);  // sp2: misses Tuesday 107, back Thursday 109
        Absent(ledger, sp3[2], lock2Day + 4);  // sp3: the same
        string mondayOnly = InitialLineupIds(pro, t3)[0];
        Absent(ledger, mondayOnly, lock2Day + 2); // out Monday 106 only: no game missed (C1)
        string[] sp4 = StarterIds(pro, t4);
        Absent(ledger, sp4[0], lock2Day + 3); // the ace misses Tuesday's game
        roster.Lock(year, lock2Day, lock2);
        long d2 = lock2Day + 2;
        Check("Q4 fill: with 1 healthy SP, sp4 is active and the two earliest returners (sp2, sp3, day 109) fill; sp1 (day 115) goes inactive",
            roster.IsActive(sp3[3], d2) && roster.IsActive(sp3[1], d2) && roster.IsActive(sp3[2], d2) && !roster.IsActive(sp3[0], d2));
        Check("C1: an absence that ends before the week's first game (Monday only) doesn't count — the starter stays active; the nine are unchanged",
            roster.IsActive(mondayOnly, d2) && InitialLineupIds(pro, t3).All(id => roster.IsActive(id, d2)));
        Check("an injured ace (out for Tuesday's game) is inactive; #2–#4 take the three active SP slots",
            !roster.IsActive(sp4[0], d2) && sp4.Skip(1).All(id => roster.IsActive(id, d2)));

        // ---- C2: WasActive only from the row that covered the week just ending ----
        int t5 = teamIds[5];
        string avatarX = PositionIds(pro, t5).First(id => !roster.IsActive(id, d2));
        w.GameState.SetText(GameStateKeys.AvatarPlayerId, avatarX);
        changed.Clear();
        roster.Lock(year, Abs135(year, 112), 112); // prior row covers (ltd 112): inactive → inactive
        bus.DispatchPending();
        bool consecutiveQuiet = changed.Count == 0;
        roster.Lock(year, Abs135(year, 133), 133); // prior row ltd 119 ≠ 133: stale → default active
        bus.DispatchPending();
        bool staleReadsActive = changed.Count == 1 && !changed[0].IsActive && changed[0].WasActive
            && changed[0].PlayerId == avatarX && changed[0].TeamId == t5 && changed[0].LockedThroughDay == Abs135(year, 140);
        var benchLedger = new AvailabilityLedger();
        foreach (string id in PositionIds(pro, t5).Where(id => roster.IsActive(id, Abs135(year, 134))
                     && !InitialLineupIds(pro, t5).Contains(id)))
        {
            Absent(benchLedger, id, Abs135(year, 147));
        }
        roster.Availability = benchLedger;
        changed.Clear();
        roster.Lock(year, Abs135(year, 140), 140); // the active bench is out: X re-activated, prior row covered
        bus.DispatchPending();
        bool reactivated = changed.Count == 1 && changed[0].IsActive && !changed[0].WasActive;
        roster.Availability = ledger;
        roster.Lock(year, Abs135(year, RosterRules.LastLockDayOfSeason), RosterRules.LastLockDayOfSeason);
        bus.DispatchPending();
        bool finalInactive = !roster.IsActive(avatarX, Abs135(year, RosterRules.LastLockDayOfSeason + 1));
        var nextBench = new AvailabilityLedger();
        int lockN = RosterRules.FirstLockDayOfSeason;
        pro.PrepareSeason(year + 1);
        foreach (string id in PositionIds(pro, t5).Where(id => roster.IsActive(id, Abs135(year, RosterRules.LastLockDayOfSeason + 1))
                     && !InitialLineupIds(pro, t5).Contains(id)))
        {
            Absent(nextBench, id, Abs135(year + 1, lockN + 7));
        }
        roster.Availability = nextBench;
        changed.Clear();
        roster.Lock(year + 1, Abs135(year + 1, lockN), lockN); // last season's final row is stale: no spurious re-activation
        bus.DispatchPending();
        bool newSeasonQuiet = changed.Count == 0 && roster.IsActive(avatarX, Abs135(year + 1, lockN + 1));
        Check("C2: WasActive reads only the row that covered the week just ending — a covered inactive→inactive lock is quiet, a stale row reads active (event active→inactive), a covered return fires inactive→active",
            consecutiveQuiet && staleReadsActive && reactivated,
            $"events: quiet {consecutiveQuiet}, stale {staleReadsActive}, reactivated {reactivated}");
        Check("C2: a season's first lock never reads last season's final inactive row (no spurious re-activation event)",
            finalInactive && newSeasonQuiet, $"final inactive {finalInactive}, events {changed.Count} ({string.Join(";", changed.Select(c => $"{c.IsActive}/{c.WasActive}"))}), active at 92 {roster.IsActive(avatarX, Abs135(year + 1, lockN + 1))}");

        // ---- idempotent re-lock, hydrate = last lock, boot reconcile (Amendment 1) ----
        string[] dump = { "SELECT player_id, team_id, is_active, locked_through_day FROM Roster_Designations ORDER BY player_id;" };
        string before = Sha12(DumpQueries(w.Db, dump));
        int v0 = roster.Version;
        roster.Lock(year + 1, Abs135(year + 1, lockN), lockN);
        Check("idempotent re-lock: the same week writes byte-identical rows (Version still moves once)",
            Sha12(DumpQueries(w.Db, dump)) == before && roster.Version == v0 + 1, before);
        var hydrated = new RosterManager(w.Db, w.Baseball, w.Leagues(pro));
        long midweek = Abs135(year + 1, lockN + 2);
        hydrated.Hydrate(midweek);
        bool hydrateEqual = teamIds.All(t => PositionIds(pro, t).Concat(StarterIds(pro, t)).Concat(RelieverIds(pro, t))
            .All(id => hydrated.IsActive(id, midweek) == roster.IsActive(id, midweek)));
        Check("hydrate: a fresh manager reading the persisted rows equals the last lock for every Pro player",
            hydrateEqual && hydrated.LockedThroughDay == Abs135(year + 1, lockN + 7));
        var reboot = new RosterManager(w.Db, w.Baseball, w.Leagues(pro)) { Availability = nextBench };
        bool missed = reboot.HydrateAndReconcile(Abs135(year + 1, lockN + 9), year + 1, lockN + 9);
        bool missedHorizon = reboot.LockedThroughDay == Abs135(year + 1, lockN + 14);
        var sunday = new RosterManager(w.Db, w.Baseball, w.Leagues(pro)) { Availability = nextBench };
        bool sundayCrash = sunday.HydrateAndReconcile(Abs135(year + 1, lockN + 14), year + 1, lockN + 14);
        var saturday = new RosterManager(w.Db, w.Baseball, w.Leagues(pro));
        bool saturdayCovered = !saturday.HydrateAndReconcile(Abs135(year + 1, lockN + 20), year + 1, lockN + 20);
        var offseason = new RosterManager(w.Db, w.Baseball, w.Leagues(pro));
        bool offseasonNone = !offseason.HydrateAndReconcile(Abs135(year + 1, 300), year + 1, 300);
        Check("boot reconcile: a missed lock is re-run on the most recent Sunday; on Sunday itself the old row no longer covers (strict >) so it reconciles; Saturday is covered; the offseason never locks",
            missed && missedHorizon && sundayCrash && sunday.LockedThroughDay == Abs135(year + 1, lockN + 21)
            && saturdayCovered && offseasonNone,
            $"missed {missed}, sunday {sundayCrash}, saturday covered {saturdayCovered}, offseason {offseasonNone}");
        Check("no RNG moved: LeagueSimulator._rng is unchanged across every lock, reconcile and hydrate",
            pro.RngSnapshot.Equals(rngBefore));
        Check("lock window: Sundays 91–273 (Pro opener 93, last postseason slot 275), reconcile through 279",
            RosterRules.FirstLockDayOfSeason == 91 && RosterRules.LastLockDayOfSeason == 273 && RosterRules.LastCoveredDayOfSeason == 279
            && RosterRules.IsLockDay(91) && !RosterRules.IsLockDay(280) && !RosterRules.IsLockDay(84) && !RosterRules.IsLockDay(95));
        w.GameState.SetText(GameStateKeys.AvatarPlayerId, "");
        roster.DetachFrom(bus);
    }

    // ------------------------------------------------------------------
    // attrition (spec §7): mid-week absences, no inactive replacement
    // ------------------------------------------------------------------

    private static void RunAttritionSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-5 attrition: an absent active isn't replaced by an inactive; the lineup reshuffles; 9 plays, 8 forfeits ---");
        Slice135World w = World135Flat(schemaPath);
        LeagueSimulator pro = w.NewSim(LeagueTier.Pro, 0x1350_0A71UL);
        int year = Slice135BaseYear + 10;
        pro.PrepareSeason(year);
        var ledger = new AvailabilityLedger();
        var roster = new RosterManager(w.Db, w.Baseball, w.Leagues(pro)) { Availability = ledger };
        pro.Availability = ledger;
        pro.Designations = roster;
        var bus = new EventBus();
        var forfeits = new List<GameForfeitedEvent>();
        bus.Subscribe<GameForfeitedEvent>(e => forfeits.Add(e));
        pro.Bus = bus;
        w.GameState.SetText(GameStateKeys.AvatarPlayerId, "");
        roster.Lock(year, Abs135(year, 98), 98);

        // Three teams from three different Thursday (102) games.
        List<(long GameId, int Home, int Away)> thursday = GamesOnDay(pro, year, 102);
        int teamA = thursday[0].Home;
        int teamB = thursday[1].Home;
        int teamC = thursday[2].Home;
        long wednesday = Abs135(year, 101);
        long nextWeek = Abs135(year, 120);
        string[] ActiveIds(int teamId) => PositionIds(pro, teamId).Where(id => roster.IsActive(id, wednesday)).ToArray();
        string[] InactiveIds(int teamId) => PositionIds(pro, teamId).Where(id => !roster.IsActive(id, wednesday)).ToArray();
        string[] starterA = InitialLineupIds(pro, teamA);
        string[] lineupB = InitialLineupIds(pro, teamB);
        string[] benchB = ActiveIds(teamB).Except(lineupB).ToArray();
        string[] lineupC = InitialLineupIds(pro, teamC);
        string[] benchC = ActiveIds(teamC).Except(lineupC).ToArray();

        // Tuesday 100 plays intact (warm-up); the absences start Wednesday.
        pro.ProcessDay(new DayAdvancedEvent(Abs135(year, 100), year, 100));
        Absent(ledger, starterA[0], nextWeek, 0, 0);
        foreach (string id in lineupB.Take(2).Append(benchB[0]))
        {
            Absent(ledger, id, nextWeek);
        }
        foreach (string id in lineupC.Take(1).Concat(benchC))
        {
            Absent(ledger, id, nextWeek); // 4 actives out: 8 eligible actives, 3 healthy inactives left over
        }
        int rebuildsBefore = pro.LineupRebuildCount;
        pro.ProcessDay(new DayAdvancedEvent(Abs135(year, 102), year, 102));
        string[] gameA = GameLineupIds(pro, teamA);
        string[] gameB = GameLineupIds(pro, teamB);
        string[] eligibleB = ActiveIds(teamB).Except(lineupB.Take(2)).Except(benchB.Take(1)).ToArray();
        Check("attrition: a starter out from Wednesday — Thursday's lineup reshuffles from the actives only (no absent, no inactive player bats)",
            !gameA.Contains(starterA[0]) && gameA.All(id => roster.IsActive(id, wednesday)) && gameA.Length == 9
            && gameA.Distinct().Count() == 9 && !gameA.Intersect(InactiveIds(teamA)).Any()
            && pro.LineupRebuildCount > rebuildsBefore,
            $"rebuilds {rebuildsBefore}→{pro.LineupRebuildCount}");
        Check("attrition: a team at exactly 9 eligible actives plays with those 9 (its 3 healthy inactives stay out)",
            eligibleB.Length == 9 && gameB.OrderBy(x => x).SequenceEqual(eligibleB.OrderBy(x => x)));

        // A macro forfeit day and a reshuffled game day, warm: 0 B (Saturday 104, the same absences).
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        pro.ProcessDay(new DayAdvancedEvent(Abs135(year, 104), year, 104));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        Check("allocation: a warm locked-week macro game day with a reshuffled lineup and a forfeit allocates zero bytes",
            allocated == 0, $"{allocated} B");

        int forfeitsQueued = pro.ForfeitCount;
        bool noEventBeforeFlush = forfeits.Count == 0;
        pro.FlushPending();
        bus.DispatchPending();
        long cForfeits = CountRows(w.Db,
            $"SELECT COUNT(*) FROM Schedule_Games WHERE season_year = {year} AND tier = 2 AND status = 2 AND (home_team_id = {teamC} OR away_team_id = {teamC});");
        long inactivePa = CountRows(w.Db,
            $"SELECT COALESCE(SUM(pa), 0) FROM Batting_Stats WHERE season_year = {year} AND player_id IN ({string.Join(",", InactiveIds(teamA).Concat(InactiveIds(teamB)).Concat(InactiveIds(teamC)).Select(id => $"'{id}'"))});");
        Check("attrition: at 8 eligible actives the team forfeits (Thursday and Saturday) though 3 healthy inactives sit — no inactive replaces an active; inactives never bat",
            cForfeits == 2 && forfeitsQueued == 2 && inactivePa == 0, $"forfeit rows {cForfeits}, inactive PA {inactivePa}");
        Check("forfeit events wait for the Sunday flush's commit (Amendment 5c), then publish once each",
            noEventBeforeFlush && forfeits.Count == 2 && forfeits.All(f => f.Forfeited(teamC) && f.Phase == 0));
        pro.Designations = null;
        pro.Availability = null;
    }

    // ------------------------------------------------------------------
    // forfeits, every tier (D2a–e)
    // ------------------------------------------------------------------

    private static void RunForfeitsSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-5 forfeits (D2): HS 7–0 / College & Pro 9–0, status 2, no stats/starts/PED, standings, double forfeits, events ---");
        Slice135World w = World135Flat(schemaPath);
        foreach (LeagueTier tier in new[] { LeagueTier.HS, LeagueTier.College, LeagueTier.Pro })
        {
            int year = Slice135BaseYear + 20 + (int)tier;
            int day = TierSeasonCalendar.GameDay(tier, 1);
            LeagueSimulator probe = w.Sim(tier);
            probe.PrepareSeason(year);
            List<(long GameId, int Home, int Away)> games = GamesOnDay(probe, year, day);
            (long GameId, int Home, int Away) g1 = games[0], g2 = games[1], g3 = games[2], g4 = games[3];
            int[] flagged = { g1.Home, g1.Away, g2.Home, g2.Away, g3.Home, g3.Away, g4.Home, g4.Away };
            // PED flags on all eight teams; g4 plays, so its flagged players prove the cost path is live.
            string flaggedIds = $"SELECT player_id FROM Players WHERE team_id IN ({string.Join(",", flagged)})";
            ExecSql(w.Db, $"INSERT INTO Entity_Flags (player_id, flag_name, is_active, set_on_day) SELECT player_id, '{LeagueSimulator.PedActiveFlagName}', 1, 1 FROM ({flaggedIds});");
            LeagueSimulator sim = w.NewSim(tier, 0x1350_F000UL + (ulong)tier);
            ExecSql(w.Db, $"DELETE FROM Entity_Flags WHERE flag_name = '{LeagueSimulator.PedActiveFlagName}';");
            sim.PrepareSeason(year);
            var ledger = new AvailabilityLedger();
            sim.Availability = ledger;
            var bus = new EventBus();
            var events = new List<GameForfeitedEvent>();
            bus.Subscribe<GameForfeitedEvent>(e => events.Add(e));
            sim.Bus = bus;
            long abs = Abs135(year, day);
            void PositionShort(int teamId)
            {
                foreach (string id in PositionIds(sim, teamId).Skip(8))
                {
                    Absent(ledger, id, abs + 1);
                }
            }
            void PitchersOut(int teamId)
            {
                foreach (string id in StarterIds(sim, teamId).Concat(RelieverIds(sim, teamId)))
                {
                    Absent(ledger, id, abs + 1);
                }
            }
            PositionShort(g1.Home);   // position-player-only failure
            PitchersOut(g2.Away);     // pitcher-only failure
            PositionShort(g3.Home);   // both sides fail: the home team is awarded it (D2d-A)
            PitchersOut(g3.Away);
            sim.ProcessDay(new DayAdvancedEvent(abs, year, day));
            bool quietBeforeFlush = events.Count == 0;
            sim.FlushPending();
            bus.DispatchPending();
            int runs = TierRules.RegulationInnings(tier);
            var rows = new List<ScheduledGameRow>();
            w.Schedule.LoadRegularSeason(year, tier, rows);
            string Score(long id)
            {
                long home = CountRows(w.Db, $"SELECT home_score FROM Schedule_Games WHERE game_id = {id};");
                long away = CountRows(w.Db, $"SELECT away_score FROM Schedule_Games WHERE game_id = {id};");
                return $"{rows.First(r => r.GameId == id).Status}:{home}-{away}";
            }
            bool scores = Score(g1.GameId) == $"2:0-{runs}" && Score(g2.GameId) == $"2:{runs}-0"
                && Score(g3.GameId) == $"2:{runs}-0" && rows.First(r => r.GameId == g4.GameId).Status == 1;
            Check($"[{tier}] forfeits score {runs}–0 with status 2: the position-only and pitcher-only failures go to the side that fields; a double forfeit to the home team",
                scores, $"g1 {Score(g1.GameId)}, g2 {Score(g2.GameId)}, g3 {Score(g3.GameId)}");
            string forfeitTeams = string.Join(",", new[] { g1.Home, g1.Away, g2.Home, g2.Away, g3.Home, g3.Away });
            long statRows = CountRows(w.Db,
                $"SELECT (SELECT COUNT(*) FROM Batting_Stats b JOIN Players p ON p.player_id = b.player_id WHERE b.season_year = {year} AND p.team_id IN ({forfeitTeams}))" +
                $" + (SELECT COUNT(*) FROM Pitching_Stats s JOIN Players p ON p.player_id = s.player_id WHERE s.season_year = {year} AND p.team_id IN ({forfeitTeams}));");
            long starts = CountRows(w.Db,
                $"SELECT COUNT(*) FROM Pitcher_Rest r JOIN Players p ON p.player_id = r.player_id WHERE r.last_start_day = {abs} AND p.team_id IN ({forfeitTeams});");
            long pedForfeit = CountRows(w.Db, $"SELECT COUNT(*) FROM Players WHERE team_id IN ({forfeitTeams}) AND (health_ceiling <> 100 OR detection_risk <> 0);");
            long pedPlayed = CountRows(w.Db, $"SELECT COUNT(*) FROM Players WHERE team_id IN ({g4.Home},{g4.Away}) AND health_ceiling < 100;");
            Check($"[{tier}] a forfeit credits nothing (D2b): no stat rows, no recorded start, no PED game cost (the played game's flagged players do pay)",
                statRows == 0 && starts == 0 && pedForfeit == 0 && pedPlayed > 0,
                $"stat rows {statRows}, starts {starts}, PED forfeit {pedForfeit}, PED played {pedPlayed}");
            var seasons = new List<TeamSeasonRow>();
            new PlayoffQueries(w.Db).LoadTeamSeasons(year, tier, seasons);
            TeamSeasonRow S(int id) => seasons.First(s => s.TeamId == id);
            bool standings = S(g1.Away).Wins == 1 && S(g1.Away).RunsScored == runs && S(g1.Home).Losses == 1 && S(g1.Home).RunsAllowed == runs
                && S(g2.Home).Wins == 1 && S(g2.Away).Losses == 1 && S(g3.Home).Wins == 1 && S(g3.Away).Losses == 1
                && S(g3.Away).RunsAllowed == runs;
            Check($"[{tier}] D2c-A: a forfeit is a W and an L in Team_Seasons and its {runs}–0 counts in runs scored and allowed",
                standings, $"g1 away {S(g1.Away).Wins}-{S(g1.Away).Losses} RS {S(g1.Away).RunsScored}");
            Check($"[{tier}] GameForfeitedEvent: once per forfeit, only after the flush commits, with the forfeit mask and the winner",
                quietBeforeFlush && events.Count == 3
                && events.Any(e => e.GameId == g1.GameId && e.ForfeitMask == GameForfeitedEvent.HomeForfeited && e.WinnerTeamId == g1.Away)
                && events.Any(e => e.GameId == g2.GameId && e.ForfeitMask == GameForfeitedEvent.AwayForfeited && e.WinnerTeamId == g2.Home)
                && events.Any(e => e.GameId == g3.GameId && e.ForfeitMask == 3 && e.WinnerTeamId == g3.Home)
                && events.All(e => e.Tier == (byte)tier && e.SeasonYear == year && e.Phase == 0),
                $"{events.Count} events");
        }

        // ---- the postseason: CanField in the macro adapter, the double forfeit to the high seed ----
        {
            int year = Slice135BaseYear + 30;
            LeagueSimulator hs = w.NewSim(LeagueTier.HS, 0x1350_F0A0UL);
            WriteSyntheticSeason(w.Db, hs, w.Schedule, year, HashPower, 0x1350_F0A1UL);
            var ledger = new AvailabilityLedger();
            hs.Availability = ledger;
            var playoffs = new PlayoffManager(w.Db, w.Baseball, w.Leagues(hs));
            var bus = new EventBus();
            var events = new List<GameForfeitedEvent>();
            bus.Subscribe<GameForfeitedEvent>(e => events.Add(e));
            playoffs.AttachTo(bus);
            int selection = PostseasonCalendar.SelectionDay(LeagueTier.HS);
            int qfDay = PostseasonCalendar.GameDay(LeagueTier.HS, 1, 1);
            DrivePostseason(playoffs, hs, year, selection, qfDay - 1, bus);
            var games = new List<PostseasonGameRow>();
            var series = new List<PlayoffSeriesRow>();
            var queries = new PlayoffQueries(w.Db);
            queries.LoadPostseasonGames(year, LeagueTier.HS, games);
            queries.LoadSeries(year, LeagueTier.HS, series);
            PostseasonGameRow qf1 = games.First(g => g.DayOfSeason == qfDay);
            PostseasonGameRow qf2 = games.Last(g => g.DayOfSeason == qfDay);
            PlayoffSeriesRow s1 = series.First(s => s.SeriesId == qf1.SeriesId);
            long qfAbs = Abs135(year, qfDay);
            foreach (string id in PositionIds(hs, qf1.HomeTeamId).Skip(8).Concat(PositionIds(hs, qf1.AwayTeamId).Skip(8)))
            {
                Absent(ledger, id, qfAbs + 1); // both sides short of nine: a double forfeit
            }
            foreach (string id in PositionIds(hs, qf2.AwayTeamId).Skip(8))
            {
                Absent(ledger, id, qfAbs + 1); // the position-player-only failure, postseason
            }
            DrivePostseason(playoffs, hs, year, qfDay, qfDay, bus);
            queries.LoadPostseasonGames(year, LeagueTier.HS, games);
            PostseasonGameRow r1 = games.First(g => g.GameId == qf1.GameId);
            PostseasonGameRow r2 = games.First(g => g.GameId == qf2.GameId);
            bool highAwarded = r1.Status == 2 && (r1.HomeTeamId == s1.HighSeedTeamId ? r1.HomeScore == 7 && r1.AwayScore == 0
                : r1.HomeScore == 0 && r1.AwayScore == 7);
            bool positionOnly = r2.Status == 2 && r2.HomeScore == 7 && r2.AwayScore == 0;
            long forfeitStarts = CountRows(w.Db,
                $"SELECT COUNT(*) FROM Pitcher_Rest r JOIN Players p ON p.player_id = r.player_id WHERE r.last_start_day = {qfAbs} AND p.team_id IN ({r1.HomeTeamId},{r1.AwayTeamId},{r2.HomeTeamId},{r2.AwayTeamId});");
            Check("postseason (D2d-A, I13): a double forfeit goes to the high seed 7–0; the macro adapter forfeits a team short of nine position players (CanField, Amendment 5b) and records no start",
                highAwarded && positionOnly && forfeitStarts == 0,
                $"qf1 {r1.HomeScore}-{r1.AwayScore} (high {s1.HighSeedTeamId}, home {r1.HomeTeamId}), qf2 {r2.HomeScore}-{r2.AwayScore}, starts {forfeitStarts}");
            Check("postseason GameForfeitedEvent: published once each after the result's batch commits, phase 2",
                events.Count == 2 && events.All(e => e.Phase == 2 && e.Tier == (byte)LeagueTier.HS)
                && events.Any(e => e.GameId == qf1.GameId && e.ForfeitMask == 3 && e.WinnerTeamId == s1.HighSeedTeamId)
                && events.Any(e => e.GameId == qf2.GameId && e.ForfeitMask == GameForfeitedEvent.AwayForfeited),
                $"{events.Count} events");
            playoffs.DetachFrom(bus);
        }
    }

    // ------------------------------------------------------------------
    // roster-beats (D5-A, Q6): three weekly locks
    // ------------------------------------------------------------------

    private static void RunRosterBeatsSuite(string schemaPath, string repoRoot)
    {
        Console.WriteLine("--- 13-5 coach beats (D5-A): inactive → still inactive → active = text +15, nothing, text −5 ---");
        Slice135World w = World135Flat(schemaPath);
        LeagueSimulator pro = w.Sim(LeagueTier.Pro);
        int year = Slice135BaseYear + 40;
        pro.PrepareSeason(year);
        var table = ParseCsv(File.ReadAllText(Path.Combine(repoRoot, "Assets", "Localization", "strings.csv")))
            .Skip(1).ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);
        var ledger = new AvailabilityLedger();
        var roster = new RosterManager(w.Db, w.Baseball, w.Leagues(pro)) { Availability = ledger };
        var state = new GlobalState();
        var bus = new EventBus();
        var writer = new RosterBeatWriter(w.Db, new NarrativeLogQueries(w.Db), w.GameState, w.Players, state)
        {
            ResolveText = key => table[key],
        };
        writer.AttachTo(bus);
        roster.AttachTo(bus);
        var stress = new List<float>();
        bus.Subscribe<StressImpulseEvent>(e => stress.Add(e.Delta));
        int teamId = ProTeam(12);
        w.GameState.SetText(GameStateKeys.AvatarPlayerId, "");
        // A dry lock before the window (ltd 84, stale at 91) finds a fringe bench player and the active bench.
        roster.Lock(year, Abs135(year, 77), 77);
        string avatar = PositionIds(pro, teamId).First(id => !roster.IsActive(id, Abs135(year, 78)));
        string[] activeBench = PositionIds(pro, teamId)
            .Where(id => roster.IsActive(id, Abs135(year, 78)) && !InitialLineupIds(pro, teamId).Contains(id)).ToArray();
        w.GameState.SetText(GameStateKeys.AvatarPlayerId, avatar);
        string Texts() => DumpQueries(w.Db, new[] { $"SELECT payload FROM Game_Logs WHERE event_type = '{NarrativeLogQueries.EventType}' AND player_id = '{avatar}' ORDER BY log_id;" });
        long TextCount() => CountRows(w.Db, $"SELECT COUNT(*) FROM Game_Logs WHERE event_type = '{NarrativeLogQueries.EventType}' AND player_id = '{avatar}';");
        void LockOn(int dayOfSeason)
        {
            state.SetCalendar(Abs135(year, dayOfSeason), StartYear);
            roster.Lock(year, Abs135(year, dayOfSeason), dayOfSeason);
            bus.DispatchPending();
        }

        // Week of 91: the active bench is out, so the fringe avatar is active (a stale prior row: no beat).
        foreach (string id in activeBench)
        {
            Absent(ledger, id, Abs135(year, 98));
        }
        LockOn(91);
        long t0 = TextCount();
        int s0 = stress.Count;
        bool activeFirst = roster.IsActive(avatar, Abs135(year, 92));
        LockOn(98);  // the bench is back: inactive after active
        long t1 = TextCount();
        List<float> s1 = stress.ToList();
        LockOn(105); // still inactive
        long t2 = TextCount();
        List<float> s2 = stress.ToList();
        foreach (string id in activeBench)
        {
            Absent(ledger, id, Abs135(year, 119));
        }
        LockOn(112); // active again
        long t3 = TextCount();
        string texts = Texts();
        Check("beats: the season's first lock with the avatar active sends nothing (no prior covering row: the default is active)",
            activeFirst && t0 == 0 && s0 == 0, $"active {activeFirst}, texts {t0}, stress {s0}");
        Check("beats: made inactive → one coach_reyes text and a +15 stress impulse",
            t1 == 1 && s1.Count == 1 && s1[0] == RosterBeatWriter.InactiveStressDelta && s1[0] == 15.0f,
            $"texts {t1}, stress [{string.Join(",", s1)}]");
        Check("beats: a second consecutive inactive week sends nothing (transitions only)", t2 == 1 && s2.Count == 1);
        Check("beats: re-activated → one text and a −5 stress impulse; both texts from coach_reyes with the strings-table copy, no {tokens}",
            t3 == 2 && stress.Count == 2 && stress[1] == RosterBeatWriter.ReactivatedStressDelta && stress[1] == -5.0f
            && texts.Contains(RosterBeatWriter.ProCoachContact, StringComparison.Ordinal)
            && texts.Contains("on the inactive list this week", StringComparison.Ordinal)
            && texts.Contains("back on the active roster this week", StringComparison.Ordinal)
            && !texts.Contains("roster.lock.", StringComparison.Ordinal) && !texts.Contains('{' + "player", StringComparison.Ordinal),
            $"texts {t3}, stress [{string.Join(",", stress)}]");
        Check("contacts: coach_college joins coach_hs and coach_reyes (the per-tier coach set), placeholder name not 'Vance'",
            RosterBeatWriter.CoachContactFor(0) == "coach_hs" && RosterBeatWriter.CoachContactFor(1) == "coach_college"
            && RosterBeatWriter.CoachContactFor(2) == "coach_reyes"
            && File.ReadAllText(Path.Combine(repoRoot, "Assets", "Narrative", "Contacts", "contacts.json"))
                .Contains("{ \"id\": \"coach_college\", \"display_name\": \"Coach Halloran\", \"role\": \"coach\" }", StringComparison.Ordinal));
        w.GameState.SetText(GameStateKeys.AvatarPlayerId, "");
        writer.DetachFrom(bus);
        roster.DetachFrom(bus);
    }

    // ------------------------------------------------------------------
    // attended-forfeit (D2f, Amendment 6)
    // ------------------------------------------------------------------

    private static void RunAttendedForfeitSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-5 attended forfeit (D2f): no park, no MicroGame, no draw; one text, no stress; the boot reclaim resolves it ---");
        Slice135World w = World135Flat(schemaPath);
        int year = Slice135BaseYear + 50;
        int avatarTeam = ProTeam(28);
        LeagueSimulator pro = w.NewSim(LeagueTier.Pro, 0x1350_A770UL);
        pro.PrepareSeason(year);
        var ledger = new AvailabilityLedger();
        var micro = new MicroGame(w.Db, w.Baseball) { LoggingEnabled = false, RestLedger = pro.RestLedger, Availability = ledger };
        micro.Initialize();
        var state = new GlobalState();
        var career = new CareerManager(w.Db, w.Players, w.Baseball, w.GameState, state, w.Leagues(pro), micro, new RngState(MicroSeed + 135))
        {
            Availability = ledger,
            AutopilotAttendedGames = false,
        };
        career.CreateAvatar("Forfeit", "Rookie", avatarTeam, new PlayerRatingsRow
        {
            IsPitcher = false, BatPower = 60, BatContact = 60, BatDiscipline = 60,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
        });
        pro.PrepareSeason(year);
        var bus = new EventBus();
        var events = new List<GameForfeitedEvent>();
        var stress = new List<float>();
        bus.Subscribe<GameForfeitedEvent>(e => events.Add(e));
        bus.Subscribe<StressImpulseEvent>(e => stress.Add(e.Delta));
        var writer = new RosterBeatWriter(w.Db, new NarrativeLogQueries(w.Db), w.GameState, w.Players, state);
        writer.AttachTo(bus);
        career.AttachTo(bus);
        string avatarId = career.AvatarPlayerId;
        long Texts() => CountRows(w.Db, $"SELECT COUNT(*) FROM Game_Logs WHERE event_type = '{NarrativeLogQueries.EventType}' AND player_id = '{avatarId}';");

        // Day 93: the opponent is decimated (seven position players out).
        int opening = TierSeasonCalendar.GameDay(LeagueTier.Pro, 1);
        pro.TryGetScheduledGame(year, opening, avatarTeam, out long g1, out int h1, out int a1);
        int opponent = h1 == avatarTeam ? a1 : h1;
        foreach (string id in PositionIds(pro, opponent).Skip(8))
        {
            Absent(ledger, id, Abs135(year, opening) + 1);
        }
        RngState careerRng = career.RngSnapshot;
        (string, string) startersBefore = micro.LastStarters;
        state.SetCalendar(Abs135(year, opening), StartYear);
        bus.Publish(new DayAdvancedEvent(Abs135(year, opening), year, opening));
        bus.DispatchPending();
        bool noPark = !career.HasPendingGame && career.AttendedForfeitCount == 1;
        long row1 = CountRows(w.Db, $"SELECT COUNT(*) FROM Schedule_Games WHERE game_id = {g1} AND status = 2 AND " +
            (h1 == avatarTeam ? "home_score = 9 AND away_score = 0" : "home_score = 0 AND away_score = 9") + ";");
        Check("attended forfeit, decimated opponent: no park, no MicroGame call, the career's _rng untouched; status 2, 9–0 to the avatar's team",
            noPark && row1 == 1 && career.RngSnapshot.Equals(careerRng) && micro.LastStarters == startersBefore,
            $"pending {career.HasPendingGame}, forfeits {career.AttendedForfeitCount}");
        Check("attended forfeit: GameForfeitedEvent once after the commit; one coach_reyes text, no stress impulse (Amendment 5)",
            events.Count == 1 && events[0].GameId == g1 && events[0].WinnerTeamId == avatarTeam && Texts() == 1 && stress.Count == 0
            && CountRows(w.Db, $"SELECT COUNT(*) FROM Game_Logs WHERE player_id = '{avatarId}' AND payload LIKE '%coach_reyes%' AND payload LIKE '%{RosterBeatWriter.ForfeitWonKey}%';") == 1,
            $"events {events.Count}, texts {Texts()}, stress {stress.Count}");

        // Day 95: his own team can't pitch (every pitcher out).
        int second = TierSeasonCalendar.GameDay(LeagueTier.Pro, 2);
        pro.TryGetScheduledGame(year, second, avatarTeam, out long g2, out int h2, out _);
        foreach (string id in StarterIds(pro, avatarTeam).Concat(RelieverIds(pro, avatarTeam)))
        {
            Absent(ledger, id, Abs135(year, second) + 1);
        }
        state.SetCalendar(Abs135(year, second), StartYear);
        bus.Publish(new DayAdvancedEvent(Abs135(year, second), year, second));
        bus.DispatchPending();
        long row2 = CountRows(w.Db, $"SELECT COUNT(*) FROM Schedule_Games WHERE game_id = {g2} AND status = 2 AND " +
            (h2 == avatarTeam ? "home_score = 0 AND away_score = 9" : "home_score = 9 AND away_score = 0") + ";");
        Check("attended forfeit, his own decimated team: no park, the opponent wins 9–0; a 'lost' coach text, still no stress",
            !career.HasPendingGame && row2 == 1 && career.AttendedForfeitCount == 2 && events.Count == 2
            && events[1].Forfeited(avatarTeam) && Texts() == 2 && stress.Count == 0 && career.RngSnapshot.Equals(careerRng));

        // Day 97, reloaded on game day (Amendment 6): a fresh career's boot reclaim forfeits instead of parking.
        int third = TierSeasonCalendar.GameDay(LeagueTier.Pro, 3);
        pro.TryGetScheduledGame(year, third, avatarTeam, out long g3, out int h3, out int a3);
        int opponent3 = h3 == avatarTeam ? a3 : h3;
        foreach (string id in PositionIds(pro, opponent3).Skip(8))
        {
            Absent(ledger, id, Abs135(year, third) + 1);
        }
        career.DetachFrom(bus);
        var rebootMicro = new MicroGame(w.Db, w.Baseball) { LoggingEnabled = false, RestLedger = pro.RestLedger, Availability = ledger };
        rebootMicro.Initialize();
        var rebooted = new CareerManager(w.Db, w.Players, w.Baseball, w.GameState, state, w.Leagues(pro), rebootMicro, new RngState(MicroSeed + 136))
        {
            Availability = ledger,
            AutopilotAttendedGames = false,
        };
        rebooted.LoadExistingAvatar();
        rebooted.AttachTo(bus);
        state.SetCalendar(Abs135(year, third), StartYear);
        bool parked = rebooted.ReclaimRegularSeasonGame(Abs135(year, third));
        bus.DispatchPending();
        Check("Amendment 6: a reload on a regular-season game day with a forfeit due resolves it at boot — no park, status 2, one more event and text",
            !parked && !rebooted.HasPendingGame && rebooted.AttendedForfeitCount == 1 && events.Count == 3 && events[2].GameId == g3
            && CountRows(w.Db, $"SELECT status FROM Schedule_Games WHERE game_id = {g3};") == 2 && Texts() == 3,
            $"parked {parked}, events {events.Count}");
        rebooted.DetachFrom(bus);
        writer.DetachFrom(bus);
    }

    // ------------------------------------------------------------------
    // attended-undo (D3-A item 4, Amendment 4)
    // ------------------------------------------------------------------

    private static void RunAttendedUndoSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-5 attended undo (D3-A): a cancelled or rolled-back regular-season game puts its starts and counters back ---");
        Slice135World w = World135Flat(schemaPath);
        int year = Slice135BaseYear + 60;
        int home = ProTeam(20);
        int away = ProTeam(21);
        long uDay = Abs135(year, 172);
        var ledger = new AvailabilityLedger();
        var uMicro = new MicroGame(w.Db, w.Baseball) { LoggingEnabled = false, Availability = ledger };
        uMicro.Initialize();
        var fresh = new MicroGame(w.Db, w.Baseball) { LoggingEnabled = false, Availability = ledger };
        fresh.Initialize();
        var sims = w.Sim(LeagueTier.Pro);
        string[] homeStarters = StarterIds(sims, home);
        string[] awayStaff = StarterIds(sims, away);
        string[] watched = homeStarters.Concat(awayStaff).Concat(RelieverIds(sims, away)).ToArray();
        // Every home starter went yesterday (a short-rest start today); every away starter is out (a bullpen game).
        foreach (string id in homeStarters)
        {
            uMicro.RestLedger.RecordStart(id, uDay - 1);
            fresh.RestLedger.RecordStart(id, uDay - 1);
        }
        foreach (string id in awayStaff)
        {
            Absent(ledger, id, uDay + 1);
        }
        string Snapshot(PitcherRestLedger l) => string.Join(",", watched.Select(id => l.TryGetLastStart(id, out long d) ? d.ToString() : "-"))
            + $"|dirty {l.DirtyCount}";
        int uHuman = uMicro.FindRosterSlot(PositionIds(sims, home)[0]);
        string s0 = Snapshot(uMicro.RestLedger);
        (int sr0, int bp0) = (uMicro.ShortRestStarts, uMicro.BullpenGames);
        RngState Stream() => new RngState(0x1350_0D00UL);

        bool cancelled = false;
        var cancelling = new CancellingBatterPolicy();
        RngState rngC = Stream();
        uMicro.RefreshAvailability(uDay);
        uMicro.SetGameDay(uDay);
        try
        {
            uMicro.PlayGame(home, away, uHuman, ref cancelling, ref rngC);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        string s1 = Snapshot(uMicro.RestLedger);
        bool countersBack = uMicro.ShortRestStarts == sr0 && uMicro.BullpenGames == bp0;
        var neutral = new NeutralBatterPolicy();
        RngState rngR = Stream();
        uMicro.SetGameDay(uDay);
        MicroGameResult replay = uMicro.PlayGame(home, away, uHuman, ref neutral, ref rngR);
        var replayStarters = uMicro.LastStarters;
        bool countersMoved = uMicro.ShortRestStarts == sr0 + 1 && uMicro.BullpenGames == bp0 + 1;
        var neutralF = new NeutralBatterPolicy();
        RngState rngF = Stream();
        fresh.RefreshAvailability(uDay);
        fresh.SetGameDay(uDay);
        MicroGameResult clean = fresh.PlayGame(home, away, uHuman, ref neutralF, ref rngF);
        Check("D3-A: a cancelled regular-season attended game leaves the rest ledger, ShortRestStarts and BullpenGames as it found them; the replay matches a never-cancelled game",
            cancelled && s1 == s0 && countersBack && countersMoved && replayStarters == fresh.LastStarters
            && replay.HomeScore == clean.HomeScore && replay.AwayScore == clean.AwayScore,
            $"cancelled {cancelled}, ledger {(s1 == s0 ? "restored" : "moved")}, starters {replayStarters} vs {fresh.LastStarters}, short-rest {sr0}→{uMicro.ShortRestStarts}, bullpen {bp0}→{uMicro.BullpenGames}");

        bool rolledBack = false;
        try
        {
            uMicro.FlushGame(year, checked((int)uDay), (_, _) => throw new InvalidOperationException("13-5 undo probe"));
        }
        catch (InvalidOperationException)
        {
            rolledBack = true;
        }
        long restRows = CountRows(w.Db, $"SELECT COUNT(*) FROM Pitcher_Rest WHERE last_start_day = {uDay};");
        (int srAfter, int bpAfter) = (uMicro.ShortRestStarts, uMicro.BullpenGames);
        bool retryRefused = Throws(() => uMicro.FlushGame(year, checked((int)uDay)), out string refusal);
        RngState rngR2 = Stream();
        uMicro.SetGameDay(uDay);
        var neutral2 = new NeutralBatterPolicy();
        MicroGameResult replay2 = uMicro.PlayGame(home, away, uHuman, ref neutral2, ref rngR2);
        Check("D3-A: a rolled-back regular-season flush puts the starts and counters back (nothing durable); a bare retry is refused and the replay picks the same starters",
            rolledBack && restRows == 0 && srAfter == sr0 && bpAfter == bp0
            && retryRefused && uMicro.LastStarters == fresh.LastStarters && replay2.HomeScore == clean.HomeScore,
            $"rest rows {restRows}, rolled back {rolledBack}, short-rest {srAfter}/{sr0}, bullpen {bpAfter}/{bp0}, starters {uMicro.LastStarters == fresh.LastStarters}, score {replay2.HomeScore}-{replay2.AwayScore} vs {clean.HomeScore}-{clean.AwayScore}; retry: {refusal}");
        // An absent reliever is skipped, never shadowed: a warm attended game allocates nothing.
        Absent(ledger, RelieverIds(sims, home)[0], uDay + 30);
        long gameDay2 = uDay + 2;
        for (int i = 0; i < 3; i++)
        {
            var warm = new NeutralBatterPolicy();
            RngState rw = new RngState(0x1350_0D10UL + (ulong)i);
            fresh.RefreshAvailability(gameDay2);
            fresh.SetGameDay(gameDay2);
            fresh.PlayGame(home, away, MicroGame.NoHuman, ref warm, ref rw);
        }
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5; i++)
        {
            var warm = new NeutralBatterPolicy();
            RngState rw = new RngState(0x1350_0D20UL + (ulong)i);
            fresh.RefreshAvailability(gameDay2);
            fresh.SetGameDay(gameDay2);
            fresh.PlayGame(home, away, MicroGame.NoHuman, ref warm, ref rw);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        Check("allocation: a warm attended game with an absent reliever (skipped) and a bullpen game allocates zero bytes",
            allocated == 0, $"{allocated} B over 5 games");
    }

    // ------------------------------------------------------------------
    // season-pipeline: the wired Sunday lock's D1-A invariance channels
    // ------------------------------------------------------------------

    /// <summary>
    /// Watches the guard world's lock from both sides of its handler (one
    /// subscription before RosterManager's, one after, both between the tier
    /// sim and the playoffs), so each lock's own writes, RNG and Version are
    /// measured exactly (D1-A channels 2, 5, 6, 8, 9).
    /// </summary>
    private sealed class RosterGuardProbe
    {
        private readonly DatabaseManager _db;
        private readonly LeagueSimulator _league;
        private readonly RosterManager _roster;
        private long _changesBefore;
        private int _versionBefore;
        private RngState _rngBefore;
        private readonly System.Diagnostics.Stopwatch _lockClock = new();
        public long LockMilliseconds => _lockClock.ElapsedMilliseconds;
        public readonly List<(int Year, int DayOfSeason)> Locks = new();
        public int BadLocks;
        public int StarterGaps;
        public int RngMoves;
        public int VersionViolations;
        public int WritesOffLock;
        public int OutsideCoverageDays;
        public int OutsideCoverageViolations;
        public int ForfeitEvents;
        public string FirstBadLock = "";

        public RosterGuardProbe(DatabaseManager db, LeagueSimulator league, RosterManager roster, EventBus bus)
        {
            _db = db;
            _league = league;
            _roster = roster;
            bus.Subscribe<DayAdvancedEvent>(BeforeLock);
            bus.Subscribe<GameForfeitedEvent>(_ => ForfeitEvents++);
        }

        public void AttachAfterLock(EventBus bus) => bus.Subscribe<DayAdvancedEvent>(AfterLock);

        private void BeforeLock(DayAdvancedEvent e)
        {
            _changesBefore = CountRows(_db, "SELECT total_changes();");
            _versionBefore = _roster.Version;
            _rngBefore = _league.RngSnapshot;
            _lockClock.Start();
        }

        private void AfterLock(DayAdvancedEvent e)
        {
            _lockClock.Stop();
            long changes = CountRows(_db, "SELECT total_changes();") - _changesBefore;
            int versionDelta = _roster.Version - _versionBefore;
            RngMoves += _league.RngSnapshot.Equals(_rngBefore) ? 0 : 1;
            if (!RosterRules.IsLockDay(e.DayOfSeason))
            {
                VersionViolations += versionDelta == 0 ? 0 : 1;
                WritesOffLock += changes == 0 ? 0 : 1;
                return;
            }
            Locks.Add((e.SeasonYear, e.DayOfSeason));
            VersionViolations += versionDelta == 1 ? 0 : 1;
            bool ok = changes == 768 && _roster.LastLockRows == 768 && _roster.LastLockActive == 576
                && _roster.LockedThroughDay == e.Day + GameCalendar.DaysPerWeek && _roster.LastLockDay == e.Day;
            if (!ok)
            {
                BadLocks++;
                FirstBadLock = FirstBadLock.Length > 0 ? FirstBadLock
                    : $"{e.SeasonYear}/{e.DayOfSeason}: changes {changes}, rows {_roster.LastLockRows}, active {_roster.LastLockActive}";
            }
            for (int team = 0; team < _league.TeamCount; team++)
            {
                foreach (int slot in _league.InitialLineup(team))
                {
                    StarterGaps += _roster.IsActive(_league.PlayerIdAt(slot), e.Day + 1) ? 0 : 1;
                }
            }
        }

        public void AfterDay(GlobalState state)
        {
            int d = state.DayOfSeason;
            if (d > RosterRules.FirstLockDayOfSeason - 1 && d <= RosterRules.LastCoveredDayOfSeason)
            {
                return;
            }
            OutsideCoverageDays++;
            bool ok = !_roster.IsLocked(state.CurrentDay) && _roster.CopyInactive(state.CurrentDay, new List<string>()) == 0;
            for (int team = 0; team < _league.TeamCount && ok; team++)
            {
                foreach (int slot in _league.PositionOrder(team))
                {
                    ok &= _roster.IsActive(_league.PlayerIdAt(slot), state.CurrentDay);
                }
                foreach (int slot in _league.StarterRank(team))
                {
                    ok &= _roster.IsActive(_league.PlayerIdAt(slot), state.CurrentDay);
                }
                foreach (int slot in _league.RelieverRank(team))
                {
                    ok &= _roster.IsActive(_league.PlayerIdAt(slot), state.CurrentDay);
                }
            }
            OutsideCoverageViolations += ok ? 0 : 1;
        }
    }

    private static void CheckGuardWorldRoster(
        DatabaseManager db, BaseballQueries baseball, LeagueSimulator league, RosterManager roster,
        RosterGuardProbe probe, int unwiredOutOfPosition)
    {
        // ch.1 is CheckProGuard above (byte-exact with the lock wired); ch.10 is CheckGuardWorldPostseason's ten Q7 checks.
        Check("D1-A ch.2: every lock, all 32 teams' nine LineupBuilder starters are active; LineupRebuildCount 0; OutOfPositionStarters as unwired",
            probe.StarterGaps == 0 && league.LineupRebuildCount == 0 && league.OutOfPositionStarters == unwiredOutOfPosition
            && probe.Locks.Count > 0,
            $"gaps {probe.StarterGaps}, rebuilds {league.LineupRebuildCount}, out-of-position {league.OutOfPositionStarters}/{unwiredOutOfPosition}");

        var gs = new Dictionary<(string, int), (long G, long Gs)>();
        using (Microsoft.Data.Sqlite.SqliteDataReader reader = db.ExecuteReader(db.GetPooledCommand(
                   "SELECT player_id, season_year, g, gs FROM Pitching_Stats;")))
        {
            while (reader.Read())
            {
                gs[(reader.GetString(0), reader.GetInt32(1))] = (reader.GetInt64(2), reader.GetInt64(3));
            }
        }
        var rested = new HashSet<string>(StringComparer.Ordinal);
        using (Microsoft.Data.Sqlite.SqliteDataReader reader = db.ExecuteReader(db.GetPooledCommand("SELECT player_id FROM Pitcher_Rest;")))
        {
            while (reader.Read())
            {
                rested.Add(reader.GetString(0));
            }
        }
        int splitViolations = 0;
        int benchStarts = 0;
        foreach (int teamId in league.TeamIds.ToArray())
        {
            (int starters, int relievers) = league.StaffCounts(teamId);
            foreach (int year in new[] { StartYear, StartYear + 1 })
            {
                long Gs(string id) => gs.TryGetValue((id, year), out var line) ? line.Gs : 0;
                splitViolations += Gs(league.StarterId(teamId, 0)) == 43 && Gs(league.StarterId(teamId, 1)) == 21
                    && Gs(league.StarterId(teamId, 2)) == 0 && Gs(league.StarterId(teamId, 3)) == 0 ? 0 : 1;
                for (int r = 0; r < relievers; r++)
                {
                    splitViolations += gs.ContainsKey((league.RelieverId(teamId, r), year)) ? 1 : 0;
                }
            }
            for (int k = 2; k < starters; k++)
            {
                benchStarts += rested.Contains(league.StarterId(teamId, k)) ? 1 : 0;
            }
            for (int r = 0; r < relievers; r++)
            {
                benchStarts += rested.Contains(league.RelieverId(teamId, r)) ? 1 : 0;
            }
        }
        Check("D1-A ch.3: no inactive pitcher is ever picked — every team's regular-season starts split 43/21/0/0 both seasons, no RP opens, SP #3/#4 and every RP never start (Pitcher_Rest); all four short-rest/bullpen counters 0",
            splitViolations == 0 && benchStarts == 0 && league.ShortRestStarts == 0 && league.BullpenGames == 0
            && league.PostseasonShortRestStarts == 0 && league.PostseasonBullpenGames == 0,
            $"split violations {splitViolations}, bench starts {benchStarts}");
        Check("D1-A ch.4: zero forfeits — no status 2 row (regular season or postseason), no GameForfeitedEvent",
            CountRows(db, "SELECT COUNT(*) FROM Schedule_Games WHERE status = 2;") == 0 && probe.ForfeitEvents == 0 && league.ForfeitCount == 0);
        Check("D1-A ch.5: LeagueSimulator._rng is unchanged across every lock (the season-end and postseason streams then match 13-4a: the two-season ledger is byte-exact)",
            probe.RngMoves == 0, $"{probe.RngMoves} moves");
        bool cadence = probe.Locks.Count == 54
            && probe.Locks.GroupBy(l => l.Year).All(g => g.Count() == 27
                && g.Select(l => l.DayOfSeason).SequenceEqual(Enumerable.Range(0, 27).Select(i => RosterRules.FirstLockDayOfSeason + 7 * i)));
        Check("D1-A ch.6: 27 locks a season on Sundays 91–273 (54 in all), each 768 rows (576 active, 192 inactive) with locked_through_day = lock day + 7, and nothing else written in the lock",
            cadence && probe.BadLocks == 0 && CountRows(db, "SELECT COUNT(*) FROM Roster_Designations;") == 768,
            probe.BadLocks == 0 ? $"{probe.Locks.Count} locks, {probe.LockMilliseconds} ms in the lock handler" : probe.FirstBadLock);
        var queries = new PlayoffQueries(db);
        var games = new List<PostseasonGameRow>();
        var series = new List<PlayoffSeriesRow>();
        string Outcome(int year)
        {
            queries.LoadPostseasonGames(year, LeagueTier.Pro, games);
            queries.LoadSeries(year, LeagueTier.Pro, series);
            return $"{series.Count}/{games.Count}/{series.First(s => s.Round == PostseasonCalendar.FinalRound(LeagueTier.Pro)).WinnerTeamId}";
        }
        string o2026 = Outcome(StartYear);
        string o2027 = Outcome(StartYear + 1);
        Check("D1-A ch.7: the postseasons match 13-4a (2026: 11 series, 30 games, champion 323; 2027: 11 series, 32 games, champion 319)",
            o2026 == "11/30/323" && o2027 == "11/32/319", $"2026 {o2026}, 2027 {o2027}");
        Check("D1-A ch.8: outside lock coverage (days 1–90 and 280–365) every Pro player reads active and nothing is written",
            // The run dispatches days 2–731 (731 is the third season's day 1).
            probe.OutsideCoverageDays == Enumerable.Range(2, 2 * GlobalState.DaysPerSeason).Count(d =>
                GlobalState.DayOfSeasonForDay(d) < RosterRules.FirstLockDayOfSeason
                || GlobalState.DayOfSeasonForDay(d) > RosterRules.LastCoveredDayOfSeason)
            && probe.OutsideCoverageViolations == 0 && probe.WritesOffLock == 0,
            $"{probe.OutsideCoverageDays} days, violations {probe.OutsideCoverageViolations}, off-lock writes {probe.WritesOffLock}");
        Check("D1-A ch.9: RosterManager.Version moves by exactly 1 per lock (54) and never on another day",
            roster.Version == 54 && probe.VersionViolations == 0, $"version {roster.Version}, violations {probe.VersionViolations}");
    }
}
