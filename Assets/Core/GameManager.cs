using System.Runtime.InteropServices;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Data.Schools;
using DirtAndDiamonds.Economy.Equipment;
using DirtAndDiamonds.Economy.Family;
using DirtAndDiamonds.Economy.Hustles;
using DirtAndDiamonds.Economy.Items;
using DirtAndDiamonds.Economy.Phone;
using DirtAndDiamonds.Narrative.Contacts;
using DirtAndDiamonds.Narrative.Events;
using DirtAndDiamonds.Narrative.Social;
using DirtAndDiamonds.Platform.Steam;
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
    // NOTE: same export-filter caveat as GrittyEventContentDir above.
    private const string ContactsResourcePath = "res://Assets/Narrative/Contacts/contacts.json";
    // NOTE: same export-filter caveat again — items.json must join the Phase 9
    // export include filters alongside the .sql and the other content .json.
    private const string ItemsResourcePath = "res://Assets/Data/Items/items.json";
    // NOTE: same export-filter caveat — schools.json (HS team branding) must
    // join the Phase 9 export include filters like the other content .json.
    private const string SchoolsResourcePath = "res://Assets/Data/Schools/schools.json";
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

    /// <summary>HS-2: the schema-v11 person-layer query surface — public like Needs/Baseball so the creation UI (backstory reveal, trait picker) and the HS-3 phone/marketplace screens read through it.</summary>
    public PersonQueries Persons { get; private set; } = null!;

    /// <summary>The tier → macro-sim map (Phase 9a): one LeagueSimulator per ladder tier.</summary>
    public LeagueDirectory Leagues { get; private set; } = null!;
    public MicroGame Micro { get; private set; } = null!;
    public CareerManager Career { get; private set; } = null!;

    /// <summary>Phase 9d offseason development pass — public like Promotions so a player-development UI card can read <see cref="DevelopmentManager.LastRun"/>.</summary>
    public DevelopmentManager Development { get; private set; } = null!;

    /// <summary>Phase 9c offseason promotion/relegation — public like Career so a ladder/news UI can read <see cref="PromotionManager.LastRun"/>.</summary>
    public PromotionManager Promotions { get; private set; } = null!;
    public LifeSimManager LifeSim { get; private set; } = null!;
    public RelationshipGraph Relationships { get; private set; } = null!;

    /// <summary>HS-5 (person-layer doc §8): the weekly NPC social-autonomy tick — public like Relationships so a future news/feed UI can read its per-tick counters.</summary>
    public NpcAutonomyService Autonomy { get; private set; } = null!;

    /// <summary>Named Clock (not Time) to avoid shadowing the Godot.Time singleton.</summary>
    public TimeManager Clock { get; private set; } = null!;

    /// <summary>
    /// Slice G: the cosmetic intraday wall clock (real_time_clock_slice_g.md
    /// §3.1/§5) — read-only time-of-day for any screen (dashboard bar, phone
    /// status bar); the G-2 TimeControlBar becomes its sole driver. Distinct
    /// from <see cref="Clock"/>, which remains the calendar authority.
    /// </summary>
    public GameClock TimeOfDay { get; private set; } = null!;

    /// <summary>Phase 8c roster availability — public like Relationships so the UI can render "out until day N".</summary>
    public AvailabilityLedger Absences { get; private set; } = null!;

    /// <summary>Phase 8e purchased gear — public like Absences so the UI can render the owned tier.</summary>
    public EquipmentLedger Gear { get; private set; } = null!;

    /// <summary>HS-6 follow-up: the attended game's live §6.2 person-lever mirror (per-game refresh; empty = the Initialize bake stands).</summary>
    public PersonLedger PersonLevers { get; private set; } = null!;

    /// <summary>HS-3: the loaded items.json catalog (person-layer doc §5) — public like Contacts so the Marketplace tab and the §3.2 autobuy tick read one shared instance. Ownership-audited against Player_Items at boot; content edits that break a shipped id fail here, never at a shop render.</summary>
    public ItemCatalog Items { get; private set; } = null!;

    /// <summary>HS-3 Layer 2 orchestration — Marketplace purchase application over the catalog above.</summary>
    public ItemService ItemShop { get; private set; } = null!;

    /// <summary>The loaded schools.json branding for the HS-tier teams (name/mascot/colors) — public like Items so the avatar-creation team picker and any team/scouting screen read one shared instance. Cross-checked against the live HS roster at boot; a missing/orphaned/mismatched entry fails the boot, never a render.</summary>
    public SchoolCatalog Schools { get; private set; } = null!;

    /// <summary>HS-3 §4.2: the phone minutes economy (spend / carrier bundle / hardware upgrade). The §4.3 never-gates invariant is structural — nothing in Narrative references this service.</summary>
    public PhoneService Phone { get; private set; } = null!;

    /// <summary>HS-3 §3/§3.2: the weekly family tick — allowance, Basic-plan minute refill, parental auto-purchase.</summary>
    public FamilyService Family { get; private set; } = null!;

    /// <summary>HS-5 §7.1: the weekly child-rearing tick — the avatar's own children, the reverse direction from <see cref="Family"/>.</summary>
    public ChildRearingService ChildRearing { get; private set; } = null!;

    /// <summary>Phase 8e Layer 2 orchestration — gear-shop snapshots and purchase application.</summary>
    public EquipmentService GearShop { get; private set; } = null!;

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

    // Phase 9d-2: the avatar's planned Practice hours for the submitted day —
    // the accumulate half of the §4 Life→Baseball bridge, captured at submit
    // exactly like the Work-activity intent above (one-shot, consumed by the
    // day tick, dropped with the plan). LifeSim ticks the block; this mirror
    // is what lets the credit accrue without the Baseball side ever seeing
    // the Life sim.
    private int _plannedPracticeHours;

    // Reused every day-tick persist so the handler doesn't allocate a fresh
    // array per calendar day (zero-GC mandate for the hot path).
    private readonly List<NeedsRow> _needsScratch = new();
    private readonly List<StressRow> _stressScratch = new();
    private readonly List<RelationshipSeed> _relationshipScratch = new();
    private readonly List<(string PlayerAId, string PlayerBId)> _relationshipHistoryScratch = new();

    // HS-4 person-layer bridge scratch: the settle list holds what the flush
    // batch actually applied per hydrated person (only the avatar this arc)
    // so LifeSim is settled strictly AFTER the commit, the funds discipline;
    // the items list backs the §5.3 transport re-projection reads.
    private readonly List<(string PlayerId, PersonStats Applied)> _personSettleScratch = new();
    private readonly List<PlayerItemRow> _avatarItemsScratch = new();

    // HS-5 §7.1: ScheduleScreen's FamilyRow visibility — recomputed on avatar
    // sync (creation/succession/load) and kept live by OnChildBorn, so the UI
    // never queries the DB from its own per-frame poll.
    private readonly List<PlayerRow> _avatarChildrenScratch = new(4);
    private bool _avatarHasChildren;

    /// <summary>Whether the avatar has at least one living child — gates ScheduleScreen's Family row and BurnerPhone's Family tab.</summary>
    public bool AvatarHasChildren => _avatarHasChildren;

    // Calendar bridge: the avatar's league tier, cached at every avatar sync
    // (creation/succession/load/promotion all funnel through SyncLifeSimAvatar)
    // so the daily mandatory-blocks projection never queries the DB. False
    // when no tier row resolved (scratch saves) — the projection then mandates
    // nothing beyond school availability, which is gated separately.
    private LeagueTier _avatarTier;
    private bool _avatarTierKnown;

    // HS-5 §8: the weekly autonomy projection's pooled buffers — a full
    // Players + Player_Person read once per tick week, never per day.
    private readonly List<PlayerRow> _autonomyPlayersScratch = new(900);
    private readonly Dictionary<string, PersonRow> _autonomyPersonsScratch = new(900);
    private readonly List<SocialSeed> _autonomySeedsScratch = new(900);

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

    /// <summary>Phase 10b: the Burner Phone contact registry — public like GrittyEvents so the phone UI can resolve a message's contact_id to display metadata.</summary>
    public ContactRegistry Contacts { get; private set; } = null!;

    /// <summary>Phase 10b: the narrative-message read-model — public like Baseball/Players so the phone UI can read the avatar's thread history.</summary>
    public NarrativeLogQueries NarrativeLog { get; private set; } = null!;

    /// <summary>Phase 11a: the sole Steam SDK owner — public like Career/Hustles so 11b's achievement subscriber and 11d's presence writer route through its guarded wrappers, never Steamworks directly.</summary>
    public SteamIntegration Steam { get; private set; } = null!;

    // Phase 11b: the achievements bus subscriber — private like _rivalryLedger
    // (nothing reads it; it exists exactly as long as the bus does).
    private AchievementManager _achievements = null!;

    // Phase 11d: the rich-presence bus subscriber — same posture as
    // _achievements (nothing reads it; it lives exactly as long as the bus).
    private RichPresenceWriter _presence = null!;

    public override void _Ready()
    {
        Instance = this;
        // The bus must keep pumping while the SceneTree is paused (menus, at-bat
        // freeze) — pausing the sims is the sims' concern, not the dispatcher's.
        ProcessMode = ProcessModeEnum.Always;

        // Phase 11a (steam_publishing_ship_it.md §5.4): Steam comes up before
        // the save opens — Steam Auto-Cloud has already delivered the freshest
        // .db to user:// by the time this process runs, so the existing open
        // path needs no change, and IsAvailable is known before any achievement
        // subscriber wires (11b). A failed Init degrades to no-op wrappers; it
        // never blocks the boot.
        Steam = new SteamIntegration();
        Steam.Initialize();

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
        Persons = new PersonQueries(_database);
        Clock = new TimeManager(_database, GameState, State, Events);
        Clock.Initialize(NewGameStartYear);

        // Slice G-1 (real_time_clock_slice_g.md §3.4): seed the intraday wall
        // clock from its two Game_State KV keys. Absent keys — any pre-G save
        // — default to the canonical 08:00 wake at Normal speed; the G-2
        // driver's checkpoint writes are what first create them. Reads happen
        // exactly once, here.
        TimeOfDay = new GameClock();
        TimeOfDay.Restore(
            GameState.TryGetInt64(GameStateKeys.TimeOfDayMinutes, out long bootMinute)
                ? bootMinute
                : GameClock.WakeMinute,
            GameState.TryGetInt64(GameStateKeys.TimeSpeed, out long bootSpeed)
                ? bootSpeed
                : (long)TimeSpeed.Normal);

        // World-gen on first boot only; an existing save keeps its league.
        // In-game runs are not required to be replay-deterministic (that is
        // the harness's contract), so a wall-clock seed is fine here.
        var rng = new RngState(unchecked((ulong)System.Environment.TickCount64) | 1UL);
        // HS-2: Persons wired in so fresh worlds seed the §2.5 organic
        // person-stat spread at generation (from the per-player fork — the
        // rating draws are untouched); migrated saves keep their backfilled
        // neutral rows (INSERT OR IGNORE never clobbers).
        bool newLeague = LeagueGenerator.GenerateIfEmpty(
            _database, Players, Baseball, LeagueGenerator.DefaultRatingSpread, ref rng, Persons);
        // v3→v4 save top-up: invents the relievers the DDL backfill cannot
        // (roles/arsenals for migrated pitchers come from the schema script).
        LeagueGenerator.EnsureV4(_database, Players, Baseball, LeagueGenerator.DefaultRatingSpread, ref rng, Persons);
        // v6→v7 world top-up (Phase 9a): seeds the five ladder tiers below MLB
        // on both migrated saves and fresh worlds. No-op once they exist.
        LeagueGenerator.EnsureTierLeagues(_database, Players, Baseball, LeagueGenerator.DefaultRatingSpread, ref rng, Persons);

        // Split the wall-clock stream before the sims copy it, so no two
        // consumers ever replay each other's draws.
        var careerRng = new RngState(rng.NextUInt64() | 1UL);

        // Rivalry ledger before any RelationshipGraph publications are pumped
        // (dispatch is deferred, so subscribing anywhere inside _Ready is
        // early enough — the first _Process pump delivers boot publications).
        _rivalryLedger = new RivalryLedger();
        _rivalryLedger.AttachTo(Events);

        // Phase 8c: roster availability. Hydrate the ledger from the persisted
        // Player_Absences rows still in effect today (RelationshipGraph's
        // hydrate-then-attach pattern), then hand it to every sim below.
        Absences = new AvailabilityLedger();
        var absenceRows = new List<PlayerAbsenceRow>();
        Players.LoadActiveAbsences(State.CurrentDay, absenceRows);
        Absences.Seed(absenceRows);
        Absences.AttachTo(Events);

        // Phase 8e: purchased gear. Hydrate the ledger from the persisted
        // Player_Equipment rows (the Absences pattern exactly — gear never
        // expires, so the scan is unconditional), then hand it to every sim
        // below alongside the rivalry/availability ledgers.
        Gear = new EquipmentLedger();
        var equipmentRows = new List<PlayerEquipmentRow>();
        Players.LoadAllEquipment(equipmentRows);
        Gear.Seed(equipmentRows);
        Gear.AttachTo(Events);

        // HS-6 follow-up: live person levers for the attended game. NO boot
        // hydration BY DESIGN (unlike Absences/Gear): the sims' Initialize
        // below already bakes every persisted Player_Person row, so the
        // ledger carries only post-boot movement — an empty ledger IS the
        // boot state's identity.
        PersonLevers = new PersonLedger();
        PersonLevers.AttachTo(Events);

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
            // HS-6: person levers bake at Initialize, so the source must be
            // wired first (unlike the per-PA ledgers below).
            tierSim.Persons = Persons;
            tierSim.Initialize();
            tierSim.Rivalries = _rivalryLedger;
            tierSim.Availability = Absences;
            tierSim.Equipment = Gear;
            tierSim.AttachTo(Events);
            Leagues.Register(tierSim);
        }

        // Career driver (Phase 5): micro-sim + avatar wiring. On a save with
        // an avatar, the attended team is reclaimed from its tier's macro sim
        // here; otherwise the career lies dormant until the UI creates one.
        // The career handler is attached AFTER the leagues' so an attended day
        // is resolved once the rest of the league has played.
        Micro = new MicroGame(_database, Baseball);
        // HS-6: wired before Initialize for the same bake-time reason as the
        // tier sims above.
        Micro.Persons = Persons;
        Micro.Initialize();
        Micro.Rivalries = _rivalryLedger;
        Micro.Availability = Absences;
        Micro.Equipment = Gear;
        // The per-game person refresh is the micro's alone — the tier sims
        // above deliberately stay on the §6.2 season-stable Initialize bake.
        Micro.PersonLevers = PersonLevers;
        Career = new CareerManager(
            _database, Players, Baseball, GameState, State, Leagues, Micro, careerRng);
        Career.Availability = Absences;
        // 12c review: an attended game's flush re-normalizes its tier's rate
        // columns, so the post-game dashboard refresh never reads them stale.
        Career.Normalizer = normalizer;
        // HS-2: avatar creation seeds the v11 person layer (backstory, parents,
        // phone, transport) and Succeed writes the heir's inherited household.
        Career.Persons = Persons;
        // The real game pauses succession for the heir-reveal/choice UI;
        // headless/harness callers construct their own CareerManager and
        // never touch this flag, so their autopilot pick is unaffected.
        Career.AutopilotSuccession = false;
        bool avatarLoaded = Career.LoadExistingAvatar();
        Career.AttachTo(Events);

        // Phase 9d: the offseason development pass. Attached AFTER CareerManager
        // (ages are post-aging) and BEFORE PromotionManager (develop-before-sort,
        // development doc §5: the sweep's scouting reads the just-developed
        // ratings, so a prospect climbs BECAUSE he developed). Its only RNG is
        // the §2.4 jitter, from a dedicated forked stream.
        Development = new DevelopmentManager(
            _database, Players, Baseball, GameState, Leagues, Micro,
            new RngState(rng.NextUInt64() | 1UL));
        Development.Career = Career;
        Development.AttachTo(Events);

        // Phase 9c: the offseason promotion/relegation pass. Attached AFTER
        // CareerManager so the promotion-doc §4 order holds on every season
        // rollover (sims flush → world ages + succession → promotion sweep);
        // its only RNG draws (HS intake generation) come from a dedicated
        // forked stream so the six sims' and the career's streams are never
        // perturbed. Career wired in so the avatar rides the same advancement
        // ranking and moves via the careful ReactivateAvatar path (§6).
        Promotions = new PromotionManager(
            _database, Players, Baseball, GameState, Leagues, Micro,
            new RngState(rng.NextUInt64() | 1UL));
        Promotions.Career = Career;
        Promotions.AttachTo(Events);

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

        // Relationship_History (schema v13): the ever-partner ledger
        // hs_clubhouse_cancer's graph-reaching prerequisite reads. Hydrated
        // right after Seed, same non-dirtying idempotent shape.
        var historyPairs = new List<(string PlayerAId, string PlayerBId)>();
        Players.LoadAllRelationshipHistory(historyPairs);
        Relationships.SeedHistory(historyPairs);

        Relationships.AttachTo(Events);
        // HS-5 (person-layer doc §8): the weekly NPC autonomy tick, on its own
        // rng fork (Split never advances the parent stream — the schema-v4
        // precedent, so world-gen draws above stay untouched). Subscribed
        // BEFORE PersistRelationships: per-channel subscriber order then
        // flushes a tick day's edge writes in that same day's upsert.
        Autonomy = new NpcAutonomyService(Relationships, rng.Split());
        Events.Subscribe<DayAdvancedEvent>(OnAutonomyDayAdvanced);
        Events.Subscribe<DayAdvancedEvent>(PersistRelationships);

        // Phase 8b: Hustles Layer 2. Subscribed AFTER LifeSim's own
        // DayAdvancedEvent handler (LifeSim.AttachTo above) so the day's Work
        // block has already ticked — arming/forfeiting a session here always
        // reflects that day's actual schedule, never a stale one.
        Hustles = new HustleService(
            _database, Players, GameState, Relationships, Events,
            unchecked((ulong)System.Environment.TickCount64) ^ 0xD1A5D1A5D1A5D1A5UL);
        Events.Subscribe<DayAdvancedEvent>(OnHustleDayAdvanced);

        // Phase 9d-2: the practice-credit accumulate half of the §4 bridge —
        // subscribed after LifeSim's own handler like the hustle seam above,
        // so the day it credits is a day whose Practice block actually ticked.
        Events.Subscribe<DayAdvancedEvent>(OnPracticeDayAdvanced);

        // Phase 8e: the gear shop. Pure request/response against the UI — no
        // day-tick subscription; the purchase itself publishes the ledger and
        // funds-mirror events post-commit.
        GearShop = new EquipmentService(_database, Players, Events);

        // HS-3: the item catalog (person-layer doc §5) — pure content like
        // Contacts below, then the loud boot-time audit: every Player_Items
        // row in the save must still resolve against the file (ids are
        // never removed or renamed once shipped; categories never change).
        string itemsJson = FileAccess.GetFileAsString(ItemsResourcePath);
        if (string.IsNullOrWhiteSpace(itemsJson))
        {
            throw new InvalidOperationException(
                $"Could not read '{ItemsResourcePath}' ({FileAccess.GetOpenError()}) — the item catalog is missing.");
        }
        Items = ItemCatalogJson.Parse(itemsJson);
        var ownedItemsAudit = new List<PlayerItemRow>();
        Persons.LoadAllItems(ownedItemsAudit);
        Items.ValidateOwnership(ownedItemsAudit);

        // HS team branding (schools.json) — pure content like the item catalog
        // above, then the loud boot-time cross-check against the live HS roster
        // (seeded by EnsureTierLeagues earlier this boot): every HS team needs a
        // school, abbreviations must agree, no orphan entries. A content edit
        // that breaks the join fails here, never at the team picker.
        string schoolsJson = FileAccess.GetFileAsString(SchoolsResourcePath);
        if (string.IsNullOrWhiteSpace(schoolsJson))
        {
            throw new InvalidOperationException(
                $"Could not read '{SchoolsResourcePath}' ({FileAccess.GetOpenError()}) — the school catalog is missing.");
        }
        Schools = SchoolCatalogJson.Parse(schoolsJson);
        var hsRosterAudit = new List<TeamRow>();
        Baseball.LoadTeamsByTier(LeagueTier.HS, hsRosterAudit);
        Schools.ValidateAgainstRoster(hsRosterAudit);

        // HS-3: Marketplace purchase orchestration — the same request/response
        // shape as GearShop above, over Player_Items instead of
        // Player_Equipment.
        ItemShop = new ItemService(_database, Players, Persons, Items, Events);

        // HS-3 §4.2/§3.2: minutes economy + the weekly family tick. The tick
        // rides DayAdvancedEvent like the hustle/practice seams above — its
        // own batch, never sharing a transaction with the Life or Baseball
        // flushes. Funds writes go through AdjustFunds + a post-commit
        // FundsImpulseEvent, so the Life sim's in-memory mirror stays true.
        Phone = new PhoneService(_database, Players, Persons, Events);
        Family = new FamilyService(_database, Players, Persons, Baseball, Items, Phone, Events);
        Events.Subscribe<DayAdvancedEvent>(OnFamilyDayAdvanced);

        // HS-5 §7.1: the weekly child-rearing tick — subscribed after LifeSim
        // so today's Family-block hours (if any) have already accumulated
        // into PeekFamilyHoursThisWeek before this handler reads it.
        ChildRearing = new ChildRearingService(_database, Players, Persons, Events);
        Events.Subscribe<DayAdvancedEvent>(OnChildRearingDayAdvanced);
        Events.Subscribe<ChildBornEvent>(OnChildBorn);

        // HS-4 §5.3: ownership changes re-project the avatar's transport
        // refund; the boot-time avatar sync above ran before the catalog
        // existed, so a loaded avatar gets the projection topped up here.
        Events.Subscribe<PlayerItemAcquiredEvent>(OnItemAcquired);
        if (avatarLoaded)
        {
            RefreshAvatarTransport(Career.AvatarPlayerId);
        }

        // Phase 7: Gritty Events. Content loads from every batch file in the
        // Content folder (a new Sonnet batch is a dropped-in file); the applier
        // subscribes on the main pump; the dispatcher polls from its own
        // thread against a read-only WAL view. Wall-clock seeds, same
        // determinism contract as the league (the harness seeds its own).
        GrittyEvents = GrittyEventJson.Parse(LoadGrittyEventContent());
        // Phase 10b: the Burner Phone's contact registry + narrative read-model.
        // Contacts is pure content (no schema, no sim reference); NarrativeLog
        // is a typed query class like every other Data-layer surface.
        string contactsJson = FileAccess.GetFileAsString(ContactsResourcePath);
        if (string.IsNullOrWhiteSpace(contactsJson))
        {
            throw new InvalidOperationException(
                $"Could not read '{ContactsResourcePath}' ({FileAccess.GetOpenError()}) — the contact registry is missing.");
        }
        Contacts = ContactJson.Parse(contactsJson);
        NarrativeLog = new NarrativeLogQueries(_database);
        GrittyEventChoices = new EventConsequenceApplier(
            _database, Players, Persons, GrittyEvents, Relationships, Events, GameState, State, NarrativeLog,
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

        // Phase 11b (steam_publishing_ship_it.md §4): achievements are a
        // platform read-model over the event stream — another ledger, attached
        // after every publisher above so its SeasonRolledOverEvent milestone
        // reads always see that same rollover's succession/game-over writes.
        // The subscription runs whether Steam is up or not (§2.2); a loaded
        // avatar gets the boot-time sync the bus never replays, which doubles
        // as the §4.3 idempotent re-assert for already-earned milestones.
        _achievements = new AchievementManager(Steam, Baseball, GameState, GrittyEvents);
        _achievements.AttachTo(Events);
        // Phase 11d (§6): rich presence rides the same two channels as one more
        // guarded read-model — event-driven only, never per-frame. An
        // avatar-less (or lineage-over) boot publishes the between-careers
        // token so a friends list never shows a stale career.
        _presence = new RichPresenceWriter(Steam, Baseball, Players, GameState, State);
        _presence.AttachTo(Events);
        if (avatarLoaded)
        {
            _achievements.SyncFromBoot(Career.AvatarPlayerId, Career.AvatarTeamId);
            _presence.SyncFromBoot(Career.AvatarPlayerId, Career.AvatarTeamId);
        }
        else
        {
            _presence.SetBetweenCareers();
        }

        (string journalMode, bool foreignKeys, int schemaVersion) = _database.GetConnectionDiagnostics();
        GD.Print(
            $"[GameManager] Save open at '{databasePath}' — schema v{schemaVersion}, journal={journalMode}, " +
            $"fk={(foreignKeys ? "on" : "OFF")}, day {State.CurrentDay} (season {State.SeasonYear}, day {State.DayOfSeason}), " +
            $"league {(newLeague ? "generated" : "loaded")} ({Baseball.CountTeams()} teams), " +
            $"avatar {(avatarLoaded ? Career.AvatarPlayerId : "none")}, life-sim NPCs {LifeSim.NpcCount}, " +
            $"relationships {Relationships.EdgeCount}, gritty events {GrittyEvents.Count} (dispatcher polling), " +
            $"clock {TimeOfDay.MinuteOfDay} min @ {TimeOfDay.Speed}.");
    }

    public override void _Process(double delta)
    {
        Events.DispatchPending();
        // Phase 11a: Facepunch runs with async callbacks off, so its callback
        // queue is pumped once per frame beside the bus — a bare branch no-op
        // whenever Steam is down.
        Steam.RunCallbacks();
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
        // Slice G-2: the intraday clock's exact minute/pace ride every quit
        // (§3.4) — before the WAL fold below so the single file carries them.
        PersistTimeOfDay();
        // Phase 11c (steam_publishing_ship_it.md §5.3): fold the WAL into the
        // single .db before the handle releases, so the one file Steam
        // Auto-Cloud uploads after exit is the complete save. Runs after the
        // flushes above committed and after the poll view — the only other
        // reader — closed; an incomplete checkpoint is loud, not fatal (the
        // save stays consistent locally, only the cloud copy trails).
        if (_database is not null && !_database.CheckpointForSync())
        {
            GD.Print("[GameManager] WAL checkpoint incomplete on exit — cloud copy may trail the local save.");
        }
        _database?.Dispose();
        _database = null;
        // Phase 11a: Steam goes down last — after the DB handle releases, so a
        // Steam-triggered auto-cloud upload sees the finished file (§2.1).
        Steam?.Shutdown();
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

    private void OnAvatarChanged(AvatarChangedEvent e)
    {
        // 9d-2 §4: a new bloodline (creation or succession) starts at zero
        // practice credit — the retiree's unconsumed hours must never leak to
        // the heir. Belt to the DevelopmentManager succession guard's braces:
        // that guard is what protects the rollover itself against dispatch
        // ordering; this clear covers mid-season handoffs and fresh creations.
        _plannedPracticeHours = 0;
        if (GameState.TryGetInt64(GameStateKeys.AvatarPracticeCredit, out long credit) && credit != 0)
        {
            GameState.SetInt64(GameStateKeys.AvatarPracticeCredit, 0);
        }
        SyncLifeSimAvatar(e.AvatarPlayerId, e.TeamId);
    }

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
        _avatarTierKnown = Baseball.TryGetTeamTier(teamId, out LeagueTier tier);
        _avatarTier = tier;
        LifeSim.AvatarSchoolAvailable =
            _avatarTierKnown && tier is LeagueTier.HS or LeagueTier.College;

        // HS-5 §7.1: covers a loaded save whose avatar already has children
        // from a prior session — OnChildBorn keeps this live for a birth that
        // resolves mid-session, after this sync already ran.
        _avatarHasChildren = Players.LoadChildrenOf(avatarId, _avatarChildrenScratch) > 0;

        // HS-4: hydrate the avatar's person stats (the v11 backfill
        // guarantees a row for every player, so the miss branch only covers
        // a hand-built scratch save). PersonRow (Data) is projected into the
        // Life-side PersonStats here at the persistence boundary, the exact
        // RelationshipRow→RelationshipSeed pattern.
        if (Persons.TryGet(avatarId, out PersonRow personRow))
        {
            LifeSim.SetPersonStats(avatarId, ToPersonStats(in personRow));
        }
        // §5.3 transport projection — guarded because the boot-time sync runs
        // before the item catalog block loads; boot tops it up right after.
        if (Items is not null)
        {
            RefreshAvatarTransport(avatarId);
        }
    }

    private static PersonStats ToPersonStats(in PersonRow row) => new()
    {
        Gpa = row.Gpa,
        Intelligence = row.Intelligence,
        Maturity = row.Maturity,
        Happiness = row.Happiness,
        Charisma = row.Charisma,
        Confidence = row.Confidence,
        Reputation = row.Reputation,
        SocialStatus = row.SocialStatus,
        Attractiveness = row.Attractiveness,
        Teamwork = row.Teamwork,
        Morality = row.Morality,
        Discipline = row.Discipline,
        WorkEthic = row.WorkEthic,
    };

    /// <summary>
    /// HS-4 §5.3: projects the avatar's best owned Transport item into the
    /// Life sim's schedule refund — computed at read from the catalog, never
    /// persisted (the ItemEffects discipline). Re-run whenever ownership
    /// changes (<see cref="OnItemAcquired"/>) and on every avatar sync.
    /// </summary>
    private void RefreshAvatarTransport(string avatarId)
    {
        Persons.LoadItemsFor(avatarId, _avatarItemsScratch);
        LifeSim.AvatarTransportHoursSaved =
            (float)ItemEffects.BestTransportHoursSaved(_avatarItemsScratch, Items);
    }

    /// <summary>
    /// HS-4: a Player_Items row landed (marketplace buy or §3.2 parental
    /// gift) — re-project the transport refund when it's the avatar's.
    /// </summary>
    private void OnItemAcquired(PlayerItemAcquiredEvent e)
    {
        if (Career.HasAvatar && string.Equals(e.PlayerId, Career.AvatarPlayerId, StringComparison.Ordinal))
        {
            RefreshAvatarTransport(e.PlayerId);
        }
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

    // ------------------------------------------------------------------
    // Slice G: the shared day-advance gate (real_time_clock_slice_g.md §4.1)
    // ------------------------------------------------------------------

    /// <summary>
    /// Set while the interactive at-bat's background task is running, by
    /// whoever owns the launch (BaseballDashboard today; the G-2
    /// TimeControlBar when the day-advance driver moves there) — the one
    /// pending state <see cref="CanAdvanceDay"/> needs that lives outside
    /// GameManager.
    /// </summary>
    public bool InteractiveGameInFlight { get; set; }

    /// <summary>
    /// Set while the day-1 onboarding walkthrough is on screen
    /// (TutorialOverlay.Open/Close). A soft stop like
    /// <see cref="HasPendingHustleSession"/>, deliberately NOT part of
    /// <see cref="CanAdvanceDay"/>: the TimeControlBar holds the clock while
    /// the player reads (onboarding_tutorial_overlay.md §7 assumes a paused
    /// surface) without disturbing their chosen Speed, and Skip Day stays
    /// reachable — the overlay is day-agnostic and resumes across any
    /// advance.
    /// </summary>
    public bool TutorialOverlayOpen { get; set; }

    /// <summary>
    /// THE day-advance gate (real_time_clock_slice_g.md §4.1): every driver —
    /// the dashboard's Play/Skip today, the G-2 continuous clock next — must
    /// consult this one predicate before <see cref="TimeManager.AdvanceDay"/>,
    /// so the definition can never drift between surfaces. Aggregates only
    /// the cross-cutting pending-decision states; a driver's own transient
    /// in-progress-advance flags stay its own.
    ///
    /// <see cref="HasPendingHustleSession"/> is deliberately NOT gated here
    /// (deviating from the design sketch): the hustle screens' shipped
    /// contract is that advancing the day IS the no-deal forfeit
    /// (NarcoticsHustleScreen's abandoned-run note), and a can't-afford
    /// session's start panel has no other exit — hard-gating it would
    /// soft-lock the game. The G-2 clock treats it as an auto-pause signal
    /// (soft stop) instead, keeping Skip-to-forfeit reachable.
    /// </summary>
    public bool CanAdvanceDay =>
        !InteractiveGameInFlight
        && !GrittyEventChoices.HasPendingChoice
        && !Career.HasPendingSuccessionChoice;

    /// <summary>
    /// Slice G-2 (real_time_clock_slice_g.md §3.4): checkpoint-writes the
    /// intraday clock's two Game_State KV keys so a reload resumes the exact
    /// minute and pace. Rides the existing save moments (<see cref="SaveNow"/>,
    /// <see cref="_ExitTree"/>) plus the bar's manual pause/speed changes —
    /// user-driven and infrequent, never per frame (database_rules.md). The
    /// sanctioned write path for the TimeControlBar, like
    /// <see cref="SetWeeklyChildFunding"/> is for the Family tab.
    /// </summary>
    public void PersistTimeOfDay()
    {
        if (_database is null || TimeOfDay is null)
        {
            return;
        }
        GameState.SetInt64(GameStateKeys.TimeOfDayMinutes, TimeOfDay.MinuteOfDay);
        GameState.SetInt64(GameStateKeys.TimeSpeed, (long)TimeOfDay.Speed);
        // Checkpoint cadence only (quit/SaveNow/pause/speed-change), so this
        // print is rare — and it timestamps every clock-state write, which is
        // the observability that pins down any spurious speed change.
        GD.Print($"[GameManager] Clock persisted — {TimeOfDay.MinuteOfDay} min @ {TimeOfDay.Speed}.");
    }

    /// <summary>
    /// True while the avatar will still be in jail when the next day ticks —
    /// an arrested player doesn't get to plan their day (the whole day
    /// autopilots; injury and suspension leave life scheduling alone, they
    /// only take the Game away). The ScheduleScreen hides/disables on this;
    /// SubmitDaySchedule enforces it regardless, so a stale click can never
    /// plan a jail day.
    /// </summary>
    public bool AvatarScheduleLocked
    {
        get
        {
            if (!Career.HasAvatar || !Absences.TryGet(Career.AvatarPlayerId, out AbsenceEntry entry))
            {
                return false;
            }
            return entry.Reason == AbsenceReason.Arrest
                && entry.StateOn(State.CurrentDay + 1) == SlotAvailability.Absent;
        }
    }

    /// <summary>
    /// The calendar-mandated School/Practice/Game blocks for an absolute day —
    /// the projection both the ScheduleScreen (read-only rows) and the
    /// CalendarScreen (browsable week view) render, and the values
    /// <see cref="SubmitDaySchedule"/> enforces server-side. School follows
    /// SchoolCalendar whenever the 9b school gate is open (HS/College);
    /// Practice/Game are calendar-locked only in the HS tier
    /// (HsSeasonCalendar's sparse spring season) — College/pro tiers keep the
    /// legacy player-reserved sliders until they grow calendars of their own,
    /// which <see cref="MandatoryBlocks.PracticeGameLocked"/> tells the UI.
    /// </summary>
    public MandatoryBlocks GetMandatoryBlocksFor(long day)
    {
        int dayOfSeason = GlobalState.DayOfSeasonForDay(day);
        int schoolHours = LifeSim.AvatarSchoolAvailable ? SchoolCalendar.HoursForDayOfSeason(dayOfSeason) : 0;
        if (!_avatarTierKnown || _avatarTier != LeagueTier.HS)
        {
            return new MandatoryBlocks(schoolHours, 0, 0, practiceGameLocked: false);
        }
        return new MandatoryBlocks(
            schoolHours,
            HsSeasonCalendar.IsPracticeDay(dayOfSeason) ? HsSeasonCalendar.PracticeHours : 0,
            HsSeasonCalendar.IsGameDay(dayOfSeason) ? HsSeasonCalendar.GameHours : 0,
            practiceGameLocked: true);
    }

    /// <summary>
    /// The ScheduleScreen's Confirm path: submits the day's block allocation
    /// AND which Work activity to run under it in one call, so the two are
    /// always captured together (§2) — LifeSim only ever learns whether
    /// today's Work block should use the HustleWork definition, never which
    /// named hustle, keeping the Life↔Baseball/Economy wall intact.
    /// A jailed avatar's submission is dropped outright (8c).
    /// School/Practice/Game are FORCED from <see cref="GetMandatoryBlocksFor"/>
    /// for the day the plan will tick (CurrentDay + 1, the same convention
    /// <see cref="AvatarScheduleLocked"/> uses) — the calendar drives those
    /// blocks, whatever the caller sent, so a stale or hand-crafted submission
    /// can never skip school or invent a game (defense in depth over the UI's
    /// read-only rows). Travel hours (§5.3, revised) are forced the same way,
    /// from <see cref="TravelTime.ComputeHours"/> over the same forced
    /// School/Practice/Game plus the caller's Work/FreeTime — a stale client
    /// value can never under-charge the commute. Throws the DaySchedule
    /// over-allocation error when the caller's editable blocks don't leave
    /// room for the mandated ones.
    /// </summary>
    public void SubmitDaySchedule(in DaySchedule schedule, WorkActivity workActivity)
    {
        if (AvatarScheduleLocked)
        {
            return;
        }
        MandatoryBlocks blocks = GetMandatoryBlocksFor(State.CurrentDay + 1);
        int practiceHours = blocks.PracticeGameLocked ? blocks.PracticeHours : schedule.PracticeHours;
        int gameHours = blocks.PracticeGameLocked ? blocks.GameHours : schedule.GameHours;
        int travelHours = TravelTime.ComputeHours(
            blocks.SchoolHours, practiceHours, gameHours, schedule.WorkHours,
            schedule.FreeTimeHours, schedule.FreeTimeActivity, LifeSim.AvatarTransportHoursSaved,
            out _);
        var forced = new DaySchedule(
            schedule.SleepHours,
            blocks.SchoolHours,
            practiceHours,
            gameHours,
            schedule.WorkHours,
            schedule.FreeTimeHours,
            schedule.FreeTimeActivity,
            schedule.FamilyHours,
            travelHours);
        LifeSim.SetTodaySchedule(forced);
        LifeSim.AvatarWorkIsHustle = workActivity != WorkActivity.LegalWork;
        _plannedWorkActivity = workActivity;
        _plannedWorkHadHours = forced.WorkHours > 0;
        _plannedPracticeHours = forced.PracticeHours;
    }

    /// <summary>The ScheduleScreen's Clear path — drops the plan AND the activity selection together, so a stale selection can never arm a session for a day that ends up autopiloted.</summary>
    public void ClearDaySchedule()
    {
        LifeSim.ClearTodaySchedule();
        LifeSim.AvatarWorkIsHustle = false;
        _plannedWorkActivity = WorkActivity.LegalWork;
        _plannedWorkHadHours = false;
        _plannedPracticeHours = 0;
    }

    /// <summary>
    /// The phone Settings tab's Save button — a checkpoint intent, not a new
    /// persistence path. Every mutation already commits as it happens
    /// (database_rules.md); what a mid-day "save" can still trail on is the
    /// in-memory life-sim accumulators and relationship dirt that normally
    /// flush on the day tick (or <see cref="_ExitTree"/>), so this runs those
    /// same two flushes, then folds the WAL into the single .db
    /// (<see cref="DatabaseManager.CheckpointForSync"/>) — the exact
    /// _ExitTree sequence, minus teardown. Returns false without touching
    /// anything when a batch transaction is somehow open (RunInBatch scopes
    /// never span a frame, so a button press can't land inside one today —
    /// but a throwing Save button is worse than a no-op one). An incomplete
    /// checkpoint (the gritty poll view reading across the truncate) is
    /// still a successful save: the flushes committed and SQLite replays the
    /// WAL on next open — only the single-file cloud snapshot trails, same
    /// loud-not-fatal posture as _ExitTree's.
    /// </summary>
    public bool SaveNow()
    {
        if (_database is null || LifeSim is null || Relationships is null)
        {
            return false;
        }
        if (_database.IsBatchActive)
        {
            GD.PushWarning("[GameManager] SaveNow skipped — a batch transaction is active.");
            return false;
        }
        PersistLifeSimState(default);
        PersistRelationships(default);
        PersistTimeOfDay();
        if (!_database.CheckpointForSync())
        {
            GD.Print("[GameManager] SaveNow: WAL checkpoint incomplete — the save is consistent; only the single-file copy trails until the next checkpoint.");
        }
        return true;
    }

    /// <summary>
    /// HS-5 §7.1: BurnerPhone's Family-tab write path for the player's
    /// standing weekly funding commitment — a direct preference write (not
    /// day-tick math), read back by ChildRearingService's next tick. The one
    /// sanctioned exception to "UI never writes directly": every other
    /// screen mutates through a Service call exactly like this one.
    /// </summary>
    public void SetWeeklyChildFunding(string playerId, int weeklyFunding) =>
        Persons.UpsertChildRearingCommitment(new ChildRearingCommitmentRow { PlayerId = playerId, WeeklyFunding = weeklyFunding });

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
    /// Phase 9d-2 (development doc §4): banks the day's actually-ticked
    /// Practice hours into the additive Game_State credit the offseason
    /// development pass consumes — the one place the Life-sim daily clock
    /// feeds the Baseball-sim career, and it is a KV write, not a reference.
    /// Subscribed after LifeSim's handler, so the plan this mirrors has
    /// already run (LifeSim consumed it this same pump; a plan cleared or
    /// replaced before the tick updated the mirror with it). Atomic in SQL
    /// (AdjustInt64), one-shot like the Work-activity intent above.
    /// </summary>
    private void OnPracticeDayAdvanced(DayAdvancedEvent e)
    {
        int hours = _plannedPracticeHours;
        _plannedPracticeHours = 0;

        // Calendar top-up: mandatory HS team practice happens whether or not
        // the player planned the day — an unplanned (autopiloted/skipped)
        // practice day still banks the mandated hours, so multi-day skips no
        // longer zero the development lever now that Practice is
        // calendar-locked rather than a free slider. Only when the avatar
        // actually showed up: an absent day (jail, injury, suspension) banks
        // nothing. A planned day arrives here with the same forced value, so
        // both paths credit identically.
        if (hours == 0 && Career.HasAvatar && !Career.IsAvatarAbsentOn(e.Day))
        {
            MandatoryBlocks blocks = GetMandatoryBlocksFor(e.Day);
            if (blocks.PracticeGameLocked)
            {
                hours = blocks.PracticeHours;
            }
        }
        if (hours > 0)
        {
            GameState.AdjustInt64(GameStateKeys.AvatarPracticeCredit, hours);
        }
    }

    /// <summary>
    /// HS-3: the weekly family tick (person-layer doc §3/§3.2/§4.2). The
    /// cadence gate lives inside FamilyService so the harness drives the
    /// identical path; this handler only supplies the avatar. No avatar (or a
    /// pre-HS-2 save with no Family_Background row) is a clean no-op.
    /// </summary>
    private void OnFamilyDayAdvanced(DayAdvancedEvent e)
    {
        if (Career.HasAvatar)
        {
            Family.ProcessDay(Career.AvatarPlayerId, e.Day);
        }
    }

    /// <summary>
    /// HS-5 §7.1: the weekly child-rearing tick — the avatar's own children.
    /// The cadence gate lives inside ChildRearingService (the FamilyService
    /// discipline); this handler supplies the avatar plus the one piece of
    /// state ChildRearingService can't reach itself — LifeSimManager's
    /// Data-free FamilyHoursThisWeek accumulator (LifeSimManager.cs's own
    /// "deliberately decoupled from PlayerQueries/Data" contract). The
    /// PeekFamilyHoursThisWeek / ClearFamilyHoursThisWeek pair mirrors
    /// PersistLifeSimState's peek-then-clear-after-commit funds discipline:
    /// peeked before the tick (so today's contribution is included), cleared
    /// only once ChildRearingService's own batch has committed.
    /// </summary>
    private void OnChildRearingDayAdvanced(DayAdvancedEvent e)
    {
        if (!Career.HasAvatar)
        {
            return;
        }
        string avatarId = Career.AvatarPlayerId;
        ChildRearing.ProcessDay(avatarId, e.Day, LifeSim.PeekFamilyHoursThisWeek(avatarId));
        if (e.Day % ChildRearingService.TickCadenceDays == 0)
        {
            LifeSim.ClearFamilyHoursThisWeek(avatarId);
        }
    }

    /// <summary>Keeps AvatarHasChildren live for a birth resolving mid-session, after SyncLifeSimAvatar's own snapshot already ran.</summary>
    private void OnChildBorn(ChildBornEvent e)
    {
        if (Career.HasAvatar && string.Equals(e.AvatarId, Career.AvatarPlayerId, StringComparison.Ordinal))
        {
            _avatarHasChildren = true;
        }
    }

    /// <summary>
    /// HS-5 §8: the weekly NPC-autonomy projection + tick. The cadence gate
    /// is checked here FIRST (via the service's own IsTickDay) so the full
    /// Players/Player_Person projection cost is paid once a week, not daily;
    /// ProcessDay re-checks it regardless. Charisma/teamwork default to the
    /// neutral 50 for any player missing a person row (impossible after the
    /// v11 backfill, but the projection stays total). The avatar passes
    /// through as the exclusion id — the service never touches their edges.
    /// </summary>
    private void OnAutonomyDayAdvanced(DayAdvancedEvent e)
    {
        if (!NpcAutonomyService.IsTickDay(e.Day))
        {
            return;
        }
        Players.LoadAll(_autonomyPlayersScratch);
        Persons.LoadAll(_autonomyPersonsScratch);
        _autonomySeedsScratch.Clear();
        for (int i = 0; i < _autonomyPlayersScratch.Count; i++)
        {
            PlayerRow player = _autonomyPlayersScratch[i];
            int charisma = 50;
            int teamwork = 50;
            if (_autonomyPersonsScratch.TryGetValue(player.PlayerId, out PersonRow person))
            {
                charisma = person.Charisma;
                teamwork = person.Teamwork;
            }
            _autonomySeedsScratch.Add(new SocialSeed(player.PlayerId, player.TeamId, charisma, teamwork));
        }
        Autonomy.ProcessDay(e.Day, Career.HasAvatar ? Career.AvatarPlayerId : null, _autonomySeedsScratch);
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
        _personSettleScratch.Clear();
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

                // HS-4: person-stat deltas ride the same atomic-delta
                // discipline funds do (AdjustStat/AdjustGpa clamp in SQL, so
                // this flush composes with any in-batch person writer like
                // the §3.1 reward instead of clobbering it). The INTEGER
                // columns take only each delta's accumulated whole part —
                // the fraction stays banked in LifeSim until it fills a
                // point (SettlePersonDeltas' contract). False for every
                // never-hydrated NPC, so this adds no work to the 800-row
                // background population.
                if (!LifeSim.TryPeekPersonDeltas(ids[i], out PersonStats personDeltas))
                {
                    continue;
                }
                PersonStats applied = default;
                bool anyApplied = false;
                for (int s = 0; s < PersonStats.StatCount; s++)
                {
                    var stat = (PersonStatId)s;
                    int whole = (int)personDeltas.Get(stat);
                    if (whole != 0)
                    {
                        Persons.AdjustStat(ids[i], s, whole);
                        applied.Set(stat, whole);
                        anyApplied = true;
                    }
                }
                if (personDeltas.Gpa != 0.0)
                {
                    Persons.AdjustGpa(ids[i], personDeltas.Gpa);
                    applied.Gpa = personDeltas.Gpa;
                    anyApplied = true;
                }
                if (anyApplied)
                {
                    _personSettleScratch.Add((ids[i], applied));
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
        for (int i = 0; i < _personSettleScratch.Count; i++)
        {
            (string playerId, PersonStats applied) = _personSettleScratch[i];
            LifeSim.SettlePersonDeltas(playerId, in applied);
        }
        // HS-6 follow-up (per-game person refresh): any settled movement on a
        // §6.2 sim lever re-announces the player's COMMITTED lever values so
        // the attended game's PersonLedger re-bakes at the next game start
        // instead of waiting for a re-Initialize beat. Read back AFTER the
        // commit above — the ledger only ever mirrors persisted rows (the
        // AvailabilityLedger discipline) — and as absolutes, because
        // AdjustStat clamps in SQL and only the row knows what actually moved.
        for (int i = 0; i < _personSettleScratch.Count; i++)
        {
            (string playerId, PersonStats applied) = _personSettleScratch[i];
            if (applied.Get(PersonStatId.Happiness) == 0f
                && applied.Get(PersonStatId.Confidence) == 0f
                && applied.Get(PersonStatId.Teamwork) == 0f)
            {
                continue;
            }
            if (Persons.TryGet(playerId, out PersonRow personRow))
            {
                Events.Publish(new PersonLeversChangedEvent(
                    playerId, (byte)personRow.Happiness, (byte)personRow.Confidence, (byte)personRow.Teamwork));
            }
        }
    }

    /// <summary>
    /// Upserts every relationship edge mutated since the last flush — one
    /// batch transaction, own cadence, mirroring the needs persist above.
    /// A no-op almost every day until Phase 7's writers exist.
    /// </summary>
    private void PersistRelationships(DayAdvancedEvent _)
    {
        int dirtyEdges = Relationships.CollectDirty(_relationshipScratch);
        int dirtyHistory = Relationships.CollectDirtyHistory(_relationshipHistoryScratch);
        if (dirtyEdges == 0 && dirtyHistory == 0)
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
            for (int i = 0; i < _relationshipHistoryScratch.Count; i++)
            {
                (string playerAId, string playerBId) = _relationshipHistoryScratch[i];
                Players.InsertRelationshipHistory(playerAId, playerBId);
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

/// <summary>
/// The calendar-mandated blocks of one day — <see cref="GameManager.GetMandatoryBlocksFor"/>'s
/// projection. When <see cref="PracticeGameLocked"/> is false (non-HS tiers,
/// this pass), Practice/Game stay legacy player-reserved sliders and only
/// School is calendar-driven.
/// </summary>
public readonly struct MandatoryBlocks
{
    public readonly int SchoolHours;
    public readonly int PracticeHours;
    public readonly int GameHours;
    public readonly bool PracticeGameLocked;

    public MandatoryBlocks(int schoolHours, int practiceHours, int gameHours, bool practiceGameLocked)
    {
        SchoolHours = schoolHours;
        PracticeHours = practiceHours;
        GameHours = gameHours;
        PracticeGameLocked = practiceGameLocked;
    }

    /// <summary>Total mandated hours — what the editable blocks must leave room for.</summary>
    public int TotalHours => SchoolHours + PracticeHours + GameHours;
}
