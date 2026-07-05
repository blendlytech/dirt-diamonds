using System.Runtime.InteropServices;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Economy.Hustles;
using DirtAndDiamonds.Narrative.Events;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Life;
using Godot;
using FileAccess = Godot.FileAccess;

namespace DirtAndDiamonds.Core;

/// <summary>
/// Autoload root of the game (registered in project.godot). Sole owner of the
/// <see cref="DatabaseManager"/> lifecycle: opens the save on boot, applies
/// the idempotent schema, and disposes on quit so the file handle releases
/// cleanly for backups/cloud sync. Also the frame driver for the
/// <see cref="EventBus"/> — one pump per <c>_Process</c>.
///
/// This is the only file in Assets/Core allowed to touch Godot types; the
/// rest of Core stays engine-independent so Tools/CoreLoopHarness can compile
/// it headless (and stops building if anyone breaks that).
/// </summary>
public sealed partial class GameManager : Node
{
    private const string SaveFilePath = "user://dirt_and_diamonds.db";
    // NOTE: .sql is not a Godot resource type — when export presets are set up
    // (Phase 9), SchemaDefinitions.sql must be added to the export include
    // filter or FileAccess won't find it inside the .pck.
    private const string SchemaResourcePath = "res://Assets/Data/Database/SchemaDefinitions.sql";
    // NOTE: like the .sql above, .json is not a Godot resource type — Phase 9
    // export presets must add Content/*.json to the export include filters.
    private const string GrittyEventContentDir = "res://Assets/Narrative/Events/Content";
    private const int NewGameStartYear = 2026;

    public static GameManager? Instance { get; private set; }

    private DatabaseManager? _database;

    public DatabaseManager Database =>
        _database ?? throw new InvalidOperationException("GameManager has not booted yet.");

    public EventBus Events { get; private set; } = null!;
    public GlobalState State { get; private set; } = null!;
    public GameStateQueries GameState { get; private set; } = null!;
    public PlayerQueries Players { get; private set; } = null!;
    public NeedsQueries Needs { get; private set; } = null!;
    public BaseballQueries Baseball { get; private set; } = null!;

    /// <summary>The tier → macro-sim map (Phase 9a): one LeagueSimulator per ladder tier.</summary>
    public LeagueDirectory Leagues { get; private set; } = null!;
    public MicroGame Micro { get; private set; } = null!;
    public CareerManager Career { get; private set; } = null!;
    public LifeSimManager LifeSim { get; private set; } = null!;
    public RelationshipGraph Relationships { get; private set; } = null!;

    /// <summary>Named Clock (not Time) to avoid shadowing the Godot.Time singleton.</summary>
    public TimeManager Clock { get; private set; } = null!;

    /// <summary>Phase 8b Layer 2 orchestration — Narcotics/Fencing context building and resolution application.</summary>
    public HustleService Hustles { get; private set; } = null!;

    // Phase 8b: the avatar's selected Work activity for the planned day (§2) —
    // GameManager-owned intent, mirroring the schedule bridge it already owns.
    // One-shot like DaySchedule itself: reset to LegalWork once the day tick
    // consumes it (or the plan is cleared before that happens).
    private WorkActivity _plannedWorkActivity = WorkActivity.LegalWork;
    private bool _plannedWorkHadHours;
    private PendingHustleSession _pendingHustleSession;
    private bool _hasPendingHustleSession;

    // Reused every day-tick persist so the handler doesn't allocate a fresh
    // array per calendar day (zero-GC mandate for the hot path).
    private readonly List<NeedsRow> _needsScratch = new();
    private readonly List<StressRow> _stressScratch = new();
    private readonly List<RelationshipSeed> _relationshipScratch = new();

    // Phase 6 rivalry transport: subscribes to RivalryChangedEvent and feeds
    // both baseball sims. Owned here because it exists exactly as long as the
    // bus does; the sims only hold the optional reference.
    private RivalryLedger _rivalryLedger = null!;

    // Phase 7 Gritty Events: the background poller (own read-only WAL view)
    // and its main-thread consequence applier. GameManager owns both
    // lifecycles; the dispatcher must stop before the database disposes.
    public GrittyEventLibrary GrittyEvents { get; private set; } = null!;
    private DatabaseManager.ReadOnlyView? _pollView;
    private EventDispatcher? _grittyDispatcher;

    /// <summary>The main-thread consequence applier — public like Career/League/Micro so the event-choice UI can read PendingChoice/ResolveChoice directly.</summary>
    public EventConsequenceApplier GrittyEventChoices { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;
        // The bus must keep pumping while the SceneTree is paused (menus, at-bat
        // freeze) — pausing the sims is the sims' concern, not the dispatcher's.
        ProcessMode = ProcessModeEnum.Always;

        string databasePath = ProjectSettings.GlobalizePath(SaveFilePath);
        _database = new DatabaseManager(databasePath);

        string ddl = FileAccess.GetFileAsString(SchemaResourcePath);
        if (string.IsNullOrWhiteSpace(ddl))
        {
            throw new InvalidOperationException(
                $"Could not read '{SchemaResourcePath}' ({FileAccess.GetOpenError()}) — cannot initialize the save schema.");
        }
        _database.ApplySchema(ddl);

        Events = new EventBus();
        State = new GlobalState();
        GameState = new GameStateQueries(_database);
        Players = new PlayerQueries(_database);
        Needs = new NeedsQueries(_database);
        Baseball = new BaseballQueries(_database);
        Clock = new TimeManager(_database, GameState, State, Events);
        Clock.Initialize(NewGameStartYear);

        // World-gen on first boot only; an existing save keeps its league.
        // In-game runs are not required to be replay-deterministic (that is
        // the harness's contract), so a wall-clock seed is fine here.
        var rng = new RngState(unchecked((ulong)System.Environment.TickCount64) | 1UL);
        bool newLeague = LeagueGenerator.GenerateIfEmpty(
            _database, Players, Baseball, LeagueGenerator.DefaultRatingSpread, ref rng);
        // v3→v4 save top-up: invents the relievers the DDL backfill cannot
        // (roles/arsenals for migrated pitchers come from the schema script).
        LeagueGenerator.EnsureV4(_database, Players, Baseball, LeagueGenerator.DefaultRatingSpread, ref rng);
        // v6→v7 world top-up (Phase 9a): seeds the five ladder tiers below MLB
        // on both migrated saves and fresh worlds. No-op once they exist.
        LeagueGenerator.EnsureTierLeagues(_database, Players, Baseball, LeagueGenerator.DefaultRatingSpread, ref rng);

        // Split the wall-clock stream before the sims copy it, so no two
        // consumers ever replay each other's draws.
        var careerRng = new RngState(rng.NextUInt64() | 1UL);

        // Rivalry ledger before any RelationshipGraph publications are pumped
        // (dispatch is deferred, so subscribing anywhere inside _Ready is
        // early enough — the first _Process pump delivers boot publications).
        _rivalryLedger = new RivalryLedger();
        _rivalryLedger.AttachTo(Events);

        // Phase 9a: one macro-sim per ladder tier, each bulk-loading only its
        // own 8-team league, each with its own forked rng stream. All six run
        // the same day loop off the bus; the avatar's tier is resolved by
        // CareerManager through the directory.
        Leagues = new LeagueDirectory();
        var normalizer = new StatsNormalizer(_database, Baseball);
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var tierSim = new LeagueSimulator(
                _database, Baseball, normalizer, new RngState(rng.NextUInt64() | 1UL), (LeagueTier)t);
            tierSim.Initialize();
            tierSim.Rivalries = _rivalryLedger;
            tierSim.AttachTo(Events);
            Leagues.Register(tierSim);
        }

        // Career driver (Phase 5): micro-sim + avatar wiring. On a save with
        // an avatar, the attended team is reclaimed from its tier's macro sim
        // here; otherwise the career lies dormant until the UI creates one.
        // The career handler is attached AFTER the leagues' so an attended day
        // is resolved once the rest of the league has played.
        Micro = new MicroGame(_database, Baseball);
        Micro.Initialize();
        Micro.Rivalries = _rivalryLedger;
        Career = new CareerManager(
            _database, Players, Baseball, GameState, State, Leagues, Micro, careerRng);
        // The real game pauses succession for the heir-reveal/choice UI;
        // headless/harness callers construct their own CareerManager and
        // never touch this flag, so their autopilot pick is unaffected.
        Career.AutopilotSuccession = false;
        bool avatarLoaded = Career.LoadExistingAvatar();
        Career.AttachTo(Events);

        // Life sim (Phase 5 remainder): needs/utility tracking, seeded once from
        // the same Players rows the baseball sims already loaded.
        var playerRows = new List<PlayerRow>();
        Players.LoadAll(playerRows);
        var npcSeeds = new NpcSeed[playerRows.Count];
        for (int i = 0; i < playerRows.Count; i++)
        {
            npcSeeds[i] = new NpcSeed(playerRows[i].PlayerId, playerRows[i].Funds);
        }
        LifeSim = new LifeSimManager();
        LifeSim.Seed(npcSeeds);

        // Needs persistence (life_sim_needs_decay.md §11, schema v5): hydrate any
        // NPC with a saved row over the FullySatisfied default Seed() just applied.
        // A migrated v4 save or a freshly generated NPC simply has no row yet —
        // Life_Needs is purely additive, no backfill (see SchemaDefinitions.sql).
        var persistedNeeds = new Dictionary<string, NeedsRow>();
        Needs.LoadAll(persistedNeeds);
        foreach (KeyValuePair<string, NeedsRow> entry in persistedNeeds)
        {
            NeedsRow row = entry.Value;
            LifeSim.SetNeeds(entry.Key, new NeedsState
            {
                Hunger = row.Hunger,
                Sleep = row.Sleep,
                Hygiene = row.Hygiene,
                Social = row.Social,
                Fitness = row.Fitness,
            });
        }

        // Stress persistence (gritty_event_framework.md §9, schema v6): same
        // hydrate-over-default pattern as needs above. A migrated v5 save or a
        // freshly generated NPC has no row — Seed()'s stress 0 default stands,
        // matching the pre-persistence in-memory behavior exactly.
        var persistedStress = new Dictionary<string, float>();
        Needs.LoadAllStress(persistedStress);
        foreach (KeyValuePair<string, float> entry in persistedStress)
        {
            LifeSim.SetStress(entry.Key, entry.Value);
        }

        LifeSim.AttachTo(Events);
        // Subscribed after LifeSim's own handler, so this always observes a day's
        // needs after that day's 24 hourly ticks have already run (EventBus
        // preserves per-channel subscriber order). Own batch transaction — Life-sim
        // writes never share the calendar tick's (database_rules.md).
        Events.Subscribe<DayAdvancedEvent>(PersistLifeSimState);

        // 9b daily-clock bridge: keep the Life sim's avatar pointer and the
        // tier-derived school gate in sync. Creation/succession arrive via the
        // bus; the boot-time load activated before the career was attached, so
        // that one syncs directly here.
        Events.Subscribe<AvatarChangedEvent>(OnAvatarChanged);
        if (avatarLoaded)
        {
            SyncLifeSimAvatar(Career.AvatarPlayerId, Career.AvatarTeamId);
        }

        // Phase 6: relationship graph — hydrate every Relationships row, then
        // attach; AttachTo announces the hydrated rivalries so the baseball
        // ledger needs no separate handshake. RelationshipKind mirrors Data's
        // RelationshipType (the Life folder is compiled Data-free by its
        // harness), mapped explicitly here at the persistence boundary.
        var relationshipRows = new List<RelationshipRow>();
        Players.LoadAllRelationships(relationshipRows);
        var relationshipSeeds = new List<RelationshipSeed>(relationshipRows.Count);
        foreach (RelationshipRow row in relationshipRows)
        {
            relationshipSeeds.Add(new RelationshipSeed(
                row.Player1Id, row.Player2Id, row.AffinityScore, ToKind(row.Type)));
        }
        Relationships = new RelationshipGraph();
        Relationships.Seed(relationshipSeeds);
        Relationships.AttachTo(Events);
        Events.Subscribe<DayAdvancedEvent>(PersistRelationships);

        // Phase 8b: Hustles Layer 2. Subscribed AFTER LifeSim's own
        // DayAdvancedEvent handler (LifeSim.AttachTo above) so the day's Work
        // block has already ticked — arming/forfeiting a session here always
        // reflects that day's actual schedule, never a stale one.
        Hustles = new HustleService(
            _database, Players, GameState, Relationships, Events,
            unchecked((ulong)System.Environment.TickCount64) ^ 0xD1A5D1A5D1A5D1A5UL);
        Events.Subscribe<DayAdvancedEvent>(OnHustleDayAdvanced);

        // Phase 7: Gritty Events. Content loads from every batch file in the
        // Content folder (a new Sonnet batch is a dropped-in file); the applier
        // subscribes on the main pump; the dispatcher polls from its own
        // thread against a read-only WAL view. Wall-clock seeds, same
        // determinism contract as the league (the harness seeds its own).
        GrittyEvents = GrittyEventJson.Parse(LoadGrittyEventContent());
        GrittyEventChoices = new EventConsequenceApplier(
            _database, Players, GrittyEvents, Relationships, Events, GameState,
            unchecked((ulong)System.Environment.TickCount64) ^ 0x9E3779B97F4A7C15UL);
        // The real game pauses the avatar's own fires for the choice UI;
        // headless/harness callers construct their own applier and keep the
        // default true (autopilot), so this flip is local to the live game.
        GrittyEventChoices.AutopilotAvatarChoices = false;
        GrittyEventChoices.AttachTo(Events);
        _pollView = _database.CreateReadOnlyView();
        _grittyDispatcher = new EventDispatcher(
            GrittyEvents, new NarrativePollQueries(_pollView), Events,
            unchecked((ulong)System.Environment.TickCount64));
        _grittyDispatcher.Start();

        (string journalMode, bool foreignKeys, int schemaVersion) = _database.GetConnectionDiagnostics();
        GD.Print(
            $"[GameManager] Save open at '{databasePath}' — schema v{schemaVersion}, journal={journalMode}, " +
            $"fk={(foreignKeys ? "on" : "OFF")}, day {State.CurrentDay} (season {State.SeasonYear}, day {State.DayOfSeason}), " +
            $"league {(newLeague ? "generated" : "loaded")} ({Baseball.CountTeams()} teams), " +
            $"avatar {(avatarLoaded ? Career.AvatarPlayerId : "none")}, life-sim NPCs {LifeSim.NpcCount}, " +
            $"relationships {Relationships.EdgeCount}, gritty events {GrittyEvents.Count} (dispatcher polling).");
    }

    public override void _Process(double delta)
    {
        Events.DispatchPending();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        // Stop the poller before any teardown touches the database; its view
        // closes with it. Dispose is idempotent and joins the thread.
        _grittyDispatcher?.Dispose();
        _grittyDispatcher = null;
        _pollView?.Dispose();
        _pollView = null;
        if (_database is not null && LifeSim is not null)
        {
            // Final flush so a mid-day quit doesn't lose that day's progress —
            // the day-tick handler above only fires on a completed calendar day.
            PersistLifeSimState(default);
        }
        if (_database is not null && Relationships is not null)
        {
            PersistRelationships(default);
        }
        _database?.Dispose();
        _database = null;
    }

    /// <summary>
    /// Reads every gritty-event content batch (Content/*.json) through Godot
    /// FileAccess — res:// lives inside the .pck when exported, same reason
    /// the schema DDL loads this way. Loud when the folder or every batch is
    /// missing: the seed batch is checked in, so absence is a broken build.
    /// </summary>
    private static List<string> LoadGrittyEventContent()
    {
        using DirAccess dir = DirAccess.Open(GrittyEventContentDir)
            ?? throw new InvalidOperationException(
                $"Could not open '{GrittyEventContentDir}' ({DirAccess.GetOpenError()}) — gritty event content is missing.");

        var documents = new List<string>();
        foreach (string file in dir.GetFiles())
        {
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string path = $"{GrittyEventContentDir}/{file}";
            string json = FileAccess.GetFileAsString(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(
                    $"Could not read gritty event batch '{path}' ({FileAccess.GetOpenError()}).");
            }
            documents.Add(json);
        }

        if (documents.Count == 0)
        {
            throw new InvalidOperationException(
                $"No gritty event content batches found under '{GrittyEventContentDir}'.");
        }
        return documents;
    }

    private void OnAvatarChanged(AvatarChangedEvent e) =>
        SyncLifeSimAvatar(e.AvatarPlayerId, e.TeamId);

    /// <summary>
    /// Points the Life sim's daily clock at the (possibly new) avatar and
    /// projects its team's tier into the 9b school gate. A mid-session avatar
    /// (fresh creation, or an heir born this session) has no boot-time seed,
    /// so its Players row is projected in first — Seed is idempotent for ids
    /// already tracked. Rare path (avatar changes), so the tiny seed array is
    /// outside the zero-GC mandate's hot loops.
    /// </summary>
    private void SyncLifeSimAvatar(string avatarId, int teamId)
    {
        if (Players.TryGetById(avatarId, out PlayerRow row))
        {
            LifeSim.Seed(new[] { new NpcSeed(avatarId, row.Funds) });
        }
        LifeSim.SetAvatar(avatarId);
        LifeSim.AvatarSchoolAvailable =
            Baseball.TryGetTeamTier(teamId, out LeagueTier tier)
            && tier is LeagueTier.HS or LeagueTier.College;
    }

    // ------------------------------------------------------------------
    // Phase 8b: Hustles Work-activity selection & pending session
    // ------------------------------------------------------------------

    public bool HasPendingHustleSession => _hasPendingHustleSession;

    /// <summary>The interactive session waiting on the player, when <see cref="HasPendingHustleSession"/>.</summary>
    public bool TryGetPendingHustleSession(out PendingHustleSession pending)
    {
        pending = _pendingHustleSession;
        return _hasPendingHustleSession;
    }

    /// <summary>The Hustle screen calls this once its session resolves (deal/bust/walk/forfeit) to release the slot.</summary>
    public void ClearPendingHustleSession() => _hasPendingHustleSession = false;

    /// <summary>
    /// The ScheduleScreen's Confirm path: submits the day's block allocation
    /// AND which Work activity to run under it in one call, so the two are
    /// always captured together (§2) — LifeSim only ever learns whether
    /// today's Work block should use the HustleWork definition, never which
    /// named hustle, keeping the Life↔Baseball/Economy wall intact.
    /// </summary>
    public void SubmitDaySchedule(in DaySchedule schedule, WorkActivity workActivity)
    {
        LifeSim.SetTodaySchedule(schedule);
        LifeSim.AvatarWorkIsHustle = workActivity != WorkActivity.LegalWork;
        _plannedWorkActivity = workActivity;
        _plannedWorkHadHours = schedule.WorkHours > 0;
    }

    /// <summary>The ScheduleScreen's Clear path — drops the plan AND the activity selection together, so a stale selection can never arm a session for a day that ends up autopiloted.</summary>
    public void ClearDaySchedule()
    {
        LifeSim.ClearTodaySchedule();
        LifeSim.AvatarWorkIsHustle = false;
        _plannedWorkActivity = WorkActivity.LegalWork;
        _plannedWorkHadHours = false;
    }

    /// <summary>
    /// Subscribed after LifeSim's own DayAdvancedEvent handler, so today's
    /// Work block (if any) has already ticked. An unplayed pending session
    /// forfeits to the design's plain "no-deal default" — unlike
    /// CareerManager's autopilot-played game forfeit, a hustle forfeit needs
    /// no resolution applied at all (§2: "no buy-in, no sale, zero reward,
    /// zero added risk"), so this is just dropping the flag. Arms a fresh
    /// session only when today's Work block actually ran an interactive
    /// activity with hours committed to it.
    /// </summary>
    private void OnHustleDayAdvanced(DayAdvancedEvent e)
    {
        _hasPendingHustleSession = false;

        if (_plannedWorkActivity != WorkActivity.LegalWork && _plannedWorkHadHours)
        {
            _pendingHustleSession = new PendingHustleSession(_plannedWorkActivity, e.Day);
            _hasPendingHustleSession = true;
        }
        _plannedWorkActivity = WorkActivity.LegalWork;
        _plannedWorkHadHours = false;
    }

    /// <summary>
    /// Once-a-day (plus a final exit flush) persistence of everything
    /// LifeSimManager tracks in memory: Needs and Stress (straight overwrites,
    /// unchanged since Phase 7) and, since Phase 8a, Funds — via
    /// PlayerQueries.AdjustFunds (an atomic delta), NOT UpdateFunds. A blind
    /// overwrite would race a same-pump gritty-event funds consequence: the
    /// dispatcher fires off its own background thread and is bus-deferred, so
    /// its FundsImpulseEvent can still be queued (not yet applied to the
    /// mirror) when this handler runs for that same DayAdvancedEvent — an
    /// overwrite would clobber the DB value AdjustFunds just committed one
    /// step earlier in the same pump. Routing through AdjustFunds instead
    /// keeps it the sole writer of Players.funds end to end (deltas commute;
    /// overwrites don't). Clearing each NPC's accumulator is deferred until
    /// AFTER the batch commits, so a rollback leaves nothing lost — the next
    /// flush attempt naturally retries with whatever has accrued since.
    /// </summary>
    private void PersistLifeSimState(DayAdvancedEvent _)
    {
        _needsScratch.Clear();
        _stressScratch.Clear();
        IReadOnlyList<string> ids = LifeSim.TrackedPlayerIds;
        for (int i = 0; i < ids.Count; i++)
        {
            if (LifeSim.TryGetNeeds(ids[i], out NeedsState needs))
            {
                _needsScratch.Add(new NeedsRow
                {
                    PlayerId = ids[i],
                    Hunger = needs.Hunger,
                    Sleep = needs.Sleep,
                    Hygiene = needs.Hygiene,
                    Social = needs.Social,
                    Fitness = needs.Fitness,
                });
            }
            if (LifeSim.TryGetStress(ids[i], out float stress))
            {
                _stressScratch.Add(new StressRow { PlayerId = ids[i], Stress = stress });
            }
        }
        // One batch for the whole life-sim flush (both BulkUpserts join an
        // already-open batch) — still its own transaction, never the calendar
        // tick's (database_rules.md).
        Database.BeginBatch();
        try
        {
            Needs.BulkUpsert(CollectionsMarshal.AsSpan(_needsScratch));
            Needs.BulkUpsertStress(CollectionsMarshal.AsSpan(_stressScratch));
            for (int i = 0; i < ids.Count; i++)
            {
                double fundsDelta = LifeSim.PeekFundsDelta(ids[i]);
                if (fundsDelta != 0.0)
                {
                    Players.AdjustFunds(ids[i], fundsDelta);
                }
            }
            Database.CommitBatch();
        }
        catch
        {
            Database.RollbackBatch();
            throw;
        }
        for (int i = 0; i < ids.Count; i++)
        {
            LifeSim.ClearFundsDelta(ids[i]);
        }
    }

    /// <summary>
    /// Upserts every relationship edge mutated since the last flush — one
    /// batch transaction, own cadence, mirroring the needs persist above.
    /// A no-op almost every day until Phase 7's writers exist.
    /// </summary>
    private void PersistRelationships(DayAdvancedEvent _)
    {
        if (Relationships.CollectDirty(_relationshipScratch) == 0)
        {
            return;
        }
        Database.BeginBatch();
        try
        {
            for (int i = 0; i < _relationshipScratch.Count; i++)
            {
                RelationshipSeed edge = _relationshipScratch[i];
                Players.UpsertRelationship(edge.PlayerAId, edge.PlayerBId, edge.Affinity, ToType(edge.Kind));
            }
            Database.CommitBatch();
        }
        catch
        {
            Database.RollbackBatch();
            throw;
        }
    }

    private static RelationshipKind ToKind(RelationshipType type) => type switch
    {
        RelationshipType.Rival => RelationshipKind.Rival,
        RelationshipType.Friend => RelationshipKind.Friend,
        RelationshipType.Partner => RelationshipKind.Partner,
        RelationshipType.Child => RelationshipKind.Child,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static RelationshipType ToType(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Rival => RelationshipType.Rival,
        RelationshipKind.Friend => RelationshipType.Friend,
        RelationshipKind.Partner => RelationshipType.Partner,
        RelationshipKind.Child => RelationshipType.Child,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
