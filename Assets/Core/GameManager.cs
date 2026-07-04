using System.Runtime.InteropServices;
using DirtAndDiamonds.Data;
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
    public LeagueSimulator League { get; private set; } = null!;
    public MicroGame Micro { get; private set; } = null!;
    public CareerManager Career { get; private set; } = null!;
    public LifeSimManager LifeSim { get; private set; } = null!;

    /// <summary>Named Clock (not Time) to avoid shadowing the Godot.Time singleton.</summary>
    public TimeManager Clock { get; private set; } = null!;

    // Reused every day-tick persist so the handler doesn't allocate a fresh
    // array per calendar day (zero-GC mandate for the hot path).
    private readonly List<NeedsRow> _needsScratch = new();

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

        // Split the wall-clock stream before the league copies it, so the two
        // sims never replay each other's draws.
        var careerRng = new RngState(rng.NextUInt64() | 1UL);
        League = new LeagueSimulator(_database, Baseball, new StatsNormalizer(_database, Baseball), rng);
        League.Initialize();
        League.AttachTo(Events);

        // Career driver (Phase 5): micro-sim + avatar wiring. On a save with
        // an avatar, the attended team is reclaimed from the macro sim here;
        // otherwise the career lies dormant until the UI creates one. The
        // career handler is attached AFTER the league's so an attended day is
        // resolved once the rest of the league has played.
        Micro = new MicroGame(_database, Baseball);
        Micro.Initialize();
        Career = new CareerManager(
            _database, Players, Baseball, GameState, State, League, Micro, careerRng);
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

        LifeSim.AttachTo(Events);
        // Subscribed after LifeSim's own handler, so this always observes a day's
        // needs after that day's 24 hourly ticks have already run (EventBus
        // preserves per-channel subscriber order). Own batch transaction — Life-sim
        // writes never share the calendar tick's (database_rules.md).
        Events.Subscribe<DayAdvancedEvent>(PersistLifeSimNeeds);

        (string journalMode, bool foreignKeys, int schemaVersion) = _database.GetConnectionDiagnostics();
        GD.Print(
            $"[GameManager] Save open at '{databasePath}' — schema v{schemaVersion}, journal={journalMode}, " +
            $"fk={(foreignKeys ? "on" : "OFF")}, day {State.CurrentDay} (season {State.SeasonYear}, day {State.DayOfSeason}), " +
            $"league {(newLeague ? "generated" : "loaded")} ({Baseball.CountTeams()} teams), " +
            $"avatar {(avatarLoaded ? Career.AvatarPlayerId : "none")}, life-sim NPCs {LifeSim.NpcCount}.");
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
        if (_database is not null && LifeSim is not null)
        {
            // Final flush so a mid-day quit doesn't lose that day's progress —
            // the day-tick handler above only fires on a completed calendar day.
            PersistLifeSimNeeds(default);
        }
        _database?.Dispose();
        _database = null;
    }

    private void PersistLifeSimNeeds(DayAdvancedEvent _)
    {
        _needsScratch.Clear();
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
        }
        Needs.BulkUpsert(CollectionsMarshal.AsSpan(_needsScratch));
    }
}
