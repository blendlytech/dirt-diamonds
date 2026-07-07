# Steam & Publishing — Design (Phase 11, "Ship It")

**Owner of this doc:** Opus 4.8 (design only — NO code). Per the standing split and
`CLAUDE.md`'s Steamworks directive, **Fable 5 owns the implementation volume** (Facepunch.Steamworks
wiring, native-library targeting, export presets, `validate_steamworks_native`); Opus owns exactly
the two decisions where getting it *wrong* ships a broken build or a corrupted save — the
**achievement seam / graceful-degradation contract** (§4) and the **cloud-save WAL correctness
problem** (§5). Everything else is a precise spec for Fable. This is the terminal phase → Milestone
M7 "Ship It".

Grounded against the live code this session (No Blind Queries applies to design too):
`Assets/Core/GameManager.cs` (the composition root + frame pump + quit teardown),
`Assets/Data/Database/DatabaseManager.cs` (the WAL connection + the deliberate `Pooling = false`
handle-release), `Assets/Core/EventBus.cs` + `Assets/Core/CoreEvents.cs` (the achievement signal
source), `Assets/Simulation/Baseball/CareerManager.cs` (`ReactivateAvatar`/`LastSuccession`),
`Assets/Data/Database/GameStateQueries.cs` (`DynastyGeneration`/`LineageOverReason`),
`DirtAndDiamonds.csproj`, `project.godot`, and the two **empty stub files**
`Assets/Platform/Steam/{SteamIntegration,AchievementManager}.cs` that Phase 0 reserved. The
BUILD_PLAN §11 exit criteria, `CLAUDE.md` §"Third-Party Integrations", and the
`validate_steamworks_native` skill are the acceptance frame.

---

## 1. Thesis & Scope

Every phase through 10 built the game; Phase 11 makes it **shippable on Steam** without changing what
the game *is*. Four deliverables, straight from `BUILD_PLAN.md:156-161`: **cloud saves** for the
SQLite `.db`, **achievements** hooked to the event dispatcher, **rich presence**, and the
**store/compliance** work (native-library export targets + the mature-content questionnaire + the
AI-portrait disclosure carried forward from Phase 10e). It is a pure **platform + packaging** phase.

**Two hard rules, non-negotiable through the phase:**

1. **Steam is optional at runtime — the game must be fully playable and every harness fully green
   with no Steam client present.** The dev loop, the headless Godot-MCP boot, and all eight
   `Tools/*` harnesses run with no Steam process, no `steam_api64.dll` loaded, no appid. **Every
   Steam call therefore no-ops silently when the SDK failed to initialize** (§2). A missing Steam
   client is the *normal* development state, not an error path — this is the Phase-11 analog of
   Phase 10's "UI is read-only over sim state": the load-bearing invariant the whole phase is
   pressure-tested against. A build that throws (or refuses to boot) because Steam isn't running is
   a review-blocking defect.

2. **No schema change, and no sim/UI-logic change.** Steam is the source of truth for achievement
   *state* (unlocks are idempotent server-side, §4.3), so the game persists **nothing new** to track
   them — `PRAGMA user_version` stays at **10**. The only Data-layer touch in the entire phase is
   one *additive, engine-independent* method on `DatabaseManager` (`CheckpointForSync`, §5.3) that
   changes no schema and no query. `Assets/Simulation/` is **never touched**, so
   `run_monte_carlo_batch` is inert by construction — identical to Phase 10's discipline, asserted
   the same way (`git show --stat` proving no `Simulation/` file moved).

**Explicit non-goals (out of scope for M7):** no in-game cloud-conflict-resolution UI (last-writer-
wins via Steam's own sync, §5.5 disclosed); no Steam Workshop / user-generated content; no
multiplayer / leaderboards / matchmaking; no microtransactions or Inventory Service; no localization
extraction (the Phase-10 "no *new* C# player-facing string literals" rule still holds, and
achievement display strings live on the Steamworks partner site, not in code); no macOS export
(Windows + Linux/Steam Deck only, per BUILD_PLAN §11 "Windows + Linux export builds").

---

## 2. The Steam Lifecycle & Graceful Degradation (the load-bearing robustness rule)

### 2.1 Where it lives — `SteamIntegration` owns the SDK, `GameManager` owns its lifecycle

`Assets/Platform/Steam/SteamIntegration.cs` and `AchievementManager.cs` exist today as **empty
stubs** (0 bytes) with reserved `.uid`s — Phase 0 left the namespace here on purpose. They fill in
now:

- **`SteamIntegration` (new content)** is the *single* owner of the Facepunch `SteamClient` lifecycle
  and the *only* class that calls the Steam SDK. It exposes a hard `IsAvailable` bool and thin,
  self-guarding wrappers (`TrySetAchievement`, `SetRichPresence`, `RunCallbacks`, `Shutdown`).
  Every wrapper is a no-op when `!IsAvailable`. Nothing else in the codebase references
  `Steamworks.*`.
- **`GameManager` drives it**, mirroring exactly how it already owns the `DatabaseManager` and
  `EventDispatcher` lifecycles ([GameManager.cs:14-24](../../Assets/Core/GameManager.cs#L14-L24)
  documents this ownership role). Three touch-points, all on seams that already exist:

| Godot callback | Existing body | Phase-11 addition |
| :------------- | :------------ | :---------------- |
| `_Ready` ([GameManager.cs:125](../../Assets/Core/GameManager.cs#L125)) | opens DB, builds bus, attaches all sims | **Init Steam first thing** (before the DB open, §5.4), then, at the *end* of `_Ready` (after the bus + every sim are attached), construct + attach the `AchievementManager` bus subscriber (§4). |
| `_Process` ([GameManager.cs:392-395](../../Assets/Core/GameManager.cs#L392-L395)) | `Events.DispatchPending();` | append `_steam.RunCallbacks();` — Facepunch requires pumping Steam callbacks once per frame, a perfect fit beside the existing bus pump. |
| `_ExitTree` ([GameManager.cs:397-421](../../Assets/Core/GameManager.cs#L397-L421)) | stops poller, final Life/Relationship flush, disposes DB | **checkpoint the WAL for sync (§5.3) before `_database.Dispose()`**, then `_steam.Shutdown()` *last* (after the DB handle releases, so a Steam-triggered auto-cloud upload sees the finished file). |

`SteamIntegration` references only Facepunch + `Assets/Core` (bus, state). Because `GameManager` is
already "the only file in `Assets/Core` allowed to touch Godot types"
([GameManager.cs:21-23](../../Assets/Core/GameManager.cs#L21-L23)) and lives outside every harness's
compile set, **all Steam wiring lands outside every harness by construction** (§9). The
`using System.Runtime.InteropServices;` the DLL resolver needs is already imported at
[GameManager.cs:1](../../Assets/Core/GameManager.cs#L1).

### 2.2 The degradation contract, concretely

`SteamClient.Init(appId)` throws when the Steam client isn't running / the appid is unknown.
`SteamIntegration.Initialize()` wraps that in try/catch, sets `IsAvailable = false` on any failure,
logs one line (`GD.Print`, the same diagnostic idiom as the boot line at
[GameManager.cs:384-389](../../Assets/Core/GameManager.cs#L384-L389)), and returns. From that point:

- Every `AchievementManager` unlock call routes through `SteamIntegration.TrySetAchievement`, which
  is a bare `if (!IsAvailable) return;` when Steam is down. **The bus subscription still runs** — it
  just discards its Steam side effect — so the achievement wiring is exercised identically whether
  Steam is up or not (no divergent code path to rot).
- `RunCallbacks` / `SetRichPresence` / `Shutdown` are equally guarded.
- A dev running from the Godot editor with no Steam client sees the game boot exactly as it does
  today, plus one "[Steam] not available — achievements/cloud disabled this session" log line.

`steam_appid.txt` (the appid in a plain text file beside the executable) lets the game `Init`
successfully against a *running* Steam client during development without a store page; it is a dev
artifact and is **git-ignored, never shipped inside the depot** (it is added to the build folder by
the export step, not committed — same class as the `.db` never being committed).

---

## 3. Native Libraries & Export (Fable-owned, `validate_steamworks_native` gate)

### 3.1 The package + native redistributables

Per `CLAUDE.md`: **Facepunch.Steamworks EXCLUSIVELY; Steamworks.NET is forbidden.** Add the managed
package as a `<PackageReference>` in `DirtAndDiamonds.csproj` (alongside the existing
`Microsoft.Data.Sqlite` reference at [DirtAndDiamonds.csproj:14](../../DirtAndDiamonds.csproj#L14)).
The Steam **native** redistributables ship *beside* the managed assembly and must copy to the output
directory on build:

- `steam_api64.dll` (Windows x64)
- `libsteam_api.so` (Linux x64 / Steam Deck)

These are added as `<None Include="..." CopyToOutputDirectory="PreserveNewest" />` items (or the
package's own runtime targets, whichever Facepunch's distribution provides — Fable validates the
exact mechanism against the real package at build time). **This `.csproj` copy configuration is
precisely what `validate_steamworks_native` checks** (skill: "verify that the Facepunch.Steamworks
native libraries `steam_api64.dll`, `libsteam_api.so` are correctly configured to copy to the output
directory upon build … so the game won't instantly crash when attempting a Linux/Steam Deck build").
Running that skill green is a phase-close gate.

### 3.2 The DLL import resolver

Per BUILD_PLAN §11: `NativeLibrary.SetDllImportResolver` for `steam_api64.dll` / `libsteam_api.so`.
Godot's `EnableDynamicLoading` is already `true`
([DirtAndDiamonds.csproj:4](../../DirtAndDiamonds.csproj#L4)), and the Godot .NET runtime loads the
game assembly from inside the export; the resolver ensures the P/Invoke into the Steam native lib
finds the file next to the executable across both platforms' naming/pathing. Registered **once**,
early in `SteamIntegration.Initialize()` before `SteamClient.Init` — never per-call. `AllowUnsafeBlocks`
is already on ([DirtAndDiamonds.csproj:11](../../DirtAndDiamonds.csproj#L11)) if any marshalling
needs it.

### 3.3 Export presets — the first time this project has them

`export_presets.cfg` **does not exist yet** (verified — no file). Phase 11 authors the first Windows
and Linux export presets. Two carry-forward hazards the code has already flagged must be honored in
the include filters:

- **`SchemaDefinitions.sql`** — `.sql` is not a Godot resource type; the `_Ready` DDL read
  ([GameManager.cs:29-31](../../Assets/Core/GameManager.cs#L29-L31)) reads it via `FileAccess` from
  inside the `.pck`, so it **must** be added to the export include filter or the exported build can't
  initialize its schema. The comment at [GameManager.cs:29-30](../../Assets/Core/GameManager.cs#L29-L30)
  is an explicit note-to-Phase-9-then-11 about exactly this.
- **`Content/*.json` + `contacts.json`** — same non-resource caveat
  ([GameManager.cs:33-36](../../Assets/Core/GameManager.cs#L33-L36)): the gritty-event content batches
  and the Burner-Phone contact registry load through `FileAccess`, so every `*.json` under
  `Assets/Narrative/**` must be in the export include filter or an exported build boots with zero
  events / a missing contact registry (a loud throw at [GameManager.cs:362-366](../../Assets/Core/GameManager.cs#L362-L366)).

The presets set the native-lib copy targets per platform, the Steam depot layout, and the
`config/features` already declared in `project.godot` (`"4.7"`, `"C#"`). Both presets must produce a
build that **launches, opens the save, and boots to `Assets/Main.tscn`** (the `run/main_scene`).

---

## 4. Achievements as a Bus Consumer (Opus-owned seam)

### 4.1 The architectural framing: `AchievementManager` is another ledger

The single cleanest decision in this phase: **the `AchievementManager` is structurally identical to
the `RivalryLedger` / `AvailabilityLedger` / `EquipmentLedger`** — it is a pure **bus subscriber**
that reacts to already-published events, holds no authority over sim state, and writes nothing to the
database. The only difference is that its side effect is a `SteamIntegration.TrySetAchievement("ID")`
call instead of updating an in-memory cache. It attaches at the end of `_Ready` exactly as the
ledgers do ([GameManager.cs:172-208](../../Assets/Core/GameManager.cs#L172-L208) is the pattern), and
it subscribes through `EventBus.Subscribe<T>` ([EventBus.cs:44](../../Assets/Core/EventBus.cs#L44)).

This keeps the wall intact: achievements are a **platform read-model over the event stream**, never a
new simulation concern. The sims don't know achievements exist.

### 4.2 The mapping discipline (the normative Opus deliverable)

**Every achievement must trace to a signal the game already emits — a bus event or a persisted
`Game_State` field — never a new tracking write.** This is the rule; the roster below is the starter
set proving it is sufficient. Fable finalizes the exact ids/art on the Steamworks partner site (where
achievement *definitions* live); in code the achievement is only ever a `TrySetAchievement(id)` call
on the mapped signal.

| Achievement (working title) | Signal it hooks | Source |
| :-------------------------- | :-------------- | :----- |
| **Went Pro** | `AvatarChangedEvent` where `Baseball.TryGetTeamTier(TeamId)` first resolves to a Minor tier | [CoreEvents.cs:208](../../Assets/Core/CoreEvents.cs#L208) |
| **The Show** | `AvatarChangedEvent` where the derived tier == `LeagueTier.MLB` — fires on the promotion `ReactivateAvatar` republish | [CareerManager.cs:607](../../Assets/Simulation/Baseball/CareerManager.cs#L607) |
| **Next of Kin** | `ChildBornEvent` (first heir) | [CoreEvents.cs:181](../../Assets/Core/CoreEvents.cs#L181) |
| **Dynasty** | `SeasonRolledOverEvent` → read `GameStateKeys.DynastyGeneration` reaches **3** (the game's own 3-generation exit criterion) | [GameStateQueries.cs:23](../../Assets/Data/Database/GameStateQueries.cs#L23) |
| **End of the Line** | `SeasonRolledOverEvent` / succession → `GameStateKeys.LineageOverReason` present | [GameStateQueries.cs:34](../../Assets/Data/Database/GameStateQueries.cs#L34) |
| **Rap Sheet** | `PlayerAbsenceChangedEvent` with `Reason == 3` (Arrest), subject == avatar | [CoreEvents.cs:231](../../Assets/Core/CoreEvents.cs#L231) |
| **Moonlighting** | `GrittyEventResolvedEvent` for a hustle/narcotics event id (choice taken) | [CoreEvents.cs:89](../../Assets/Core/CoreEvents.cs#L89) |
| **Juiced** | `GrittyEventResolvedEvent` for a PED event id (e.g. the `caught_juicing` family) | [CoreEvents.cs:89](../../Assets/Core/CoreEvents.cs#L89) |
| **Journeyman** *(counter)* | `SeasonRolledOverEvent` count reaches N — held as a **Steam Stat**, server-side (§4.3), never a local column | [CoreEvents.cs:41](../../Assets/Core/CoreEvents.cs#L41) |

Two achievements deliberately deferred as **stretch** (they need a signal that isn't on the bus yet):
a Texas Hold'em win (`HoldemHarness`-tested, but the session result is UI-local, not published) and a
"peak rating hits A+ / 90" milestone (readable from `DevelopmentManager.LastRun` but only at a
rollover). Neither is required for M7; each would need a small *additive* result signal if pursued,
and that is a content decision, not a platform one — flagged, not designed here.

### 4.3 Idempotency — why no local achievement state (and thus no schema change)

Steam's `SetAchievement` is **idempotent**: an already-unlocked achievement stays unlocked, and the
unlock toast fires exactly once (on the first `StoreStats` that transitions it). So the game **does
not need to remember which achievements it has granted** — Steam is the durable store. Consequences:

- Milestone achievements computed from *persisted* state (`DynastyGeneration == 3`, avatar tier ==
  MLB) can be re-evaluated and re-asserted on *every* relevant event — a reload that replays the
  condition just re-sets an already-set flag, a harmless no-op. No "have I already fired this?" bit
  is needed anywhere in the save.
- **Counter** achievements (Journeyman) use **Steam Stats** (`SteamUserStats`), which hold the
  running total server-side; the game increments the stat and Steam evaluates the stat-vs-threshold
  achievement rule. Still nothing in the local `.db`.
- Therefore `PRAGMA user_version` stays at 10 and `SchemaValidator` re-runs green unchanged — the §1
  no-schema-change rule holds *because* Steam owns the state, not despite it.

`AchievementManager` batches its unlocks behind a single `SteamUserStats.StoreStats()` per pump (or
Facepunch's equivalent flush) rather than one network round-trip per event, but this is an
optimization detail, not a correctness one — every unlock is idempotent regardless.

---

## 5. Cloud Saves & the WAL Correctness Problem (Opus-owned — the marquee decision)

This is the one place in Phase 11 where a naive implementation ships **save corruption**. The
save is a WAL-mode SQLite database; cloud-syncing it correctly is subtle, and the codebase was
*already designed* for this moment.

### 5.1 The hazard: the `.db` file alone is not the whole save

`DatabaseManager` opens the save in **WAL journal mode**
([DatabaseManager.cs:66-73](../../Assets/Data/Database/DatabaseManager.cs#L66-L73)) — mandatory, so
the Gritty Event dispatcher can poll from its read-only companion connection while a sim batch writes
(the whole reason WAL exists in this project). WAL means committed writes live in a **`-wal` sidecar
file** (`dirt_and_diamonds.db-wal`, plus a `-shm` shared-memory file) until a checkpoint folds them
back into the main `.db`. **A cloud sync that uploads only `dirt_and_diamonds.db` therefore captures
a save that is missing every un-checkpointed write** — potentially hours of play. Uploading the
sidecars too is worse: a `-wal` from machine A paired with a `.db` from machine B is undefined
behavior. Either way, naive file-sync of a live WAL database corrupts.

### 5.2 What the codebase already did for us

`DatabaseManager` **disables ADO connection pooling on purpose**, and the comment says why in as many
words: *"Single long-lived connection for the session; disabling ADO pooling guarantees the file
handle is released the moment we dispose, so save-file management (copy/backup/cloud sync) never
fights a lock"* ([DatabaseManager.cs:46-50](../../Assets/Data/Database/DatabaseManager.cs#L46-L50)).
The handle-release discipline this phase depends on is already in place. We only have to solve the
WAL-sidecar problem, and we solve it at the one moment we control: clean exit.

### 5.3 The decision — checkpoint(TRUNCATE) on clean exit, sync the `.db` only

Add **one additive, engine-independent method** to `DatabaseManager`:

```
/// Folds the WAL back into the main .db and truncates the sidecar to zero, so the
/// single .db file is a complete, self-contained snapshot safe for cloud sync.
public void CheckpointForSync()  // runs: PRAGMA wal_checkpoint(TRUNCATE);
```

- Called from `_ExitTree` **after** the final Life/Relationship flush and **before**
  `_database.Dispose()` (i.e. inserted at [GameManager.cs:415-419](../../Assets/Core/GameManager.cs#L415-L419),
  just ahead of the dispose). After it runs, `-wal` is zero bytes and `dirt_and_diamonds.db` holds
  the entire committed state.
- **Steam Auto-Cloud** (configured on the partner site with an Auto-Cloud path pattern matching
  **`dirt_and_diamonds.db` only** — *not* the `-wal`/`-shm` sidecars) then syncs that single finished
  file. No `ISteamRemoteStorage` API code is required for the MVP: Steam downloads the newer cloud
  copy to the `user://` path *before* the process launches and uploads *after* it exits, and our
  checkpoint guarantees the file it uploads is complete.
- This is engine-independent (a bare pragma), so it is harmless under the harnesses that construct
  their own `DatabaseManager`, and it is a **no-op when the WAL is already empty** — zero cost on the
  common path.

**Why Auto-Cloud over `ISteamRemoteStorage`:** Auto-Cloud needs no boot-time download code, no
FileRead/FileWrite marshalling, and no ordering dance against `DatabaseManager`'s open — Steam
handles the file transfer around the process lifetime. The RemoteStorage API buys explicit conflict
handling and in-process control, which we don't need for a single-slot single-player save at M7. If a
future phase wants a conflict-resolution UI (§5.5), RemoteStorage is the upgrade path; the checkpoint
method designed here is a prerequisite for that route too, so nothing is wasted.

### 5.4 Boot ordering

`SteamClient.Init` runs at the very top of `_Ready`, **before** `new DatabaseManager(databasePath)`
([GameManager.cs:132-133](../../Assets/Core/GameManager.cs#L132-L133)). Ordering rationale: Steam
Auto-Cloud has already placed the freshest `.db` at the `user://` path by the time our process
starts, so simply opening the file at the existing path picks up the synced save with **no code
change to the open path at all** — `SaveFilePath` / `GlobalizePath`
([GameManager.cs:27](../../Assets/Core/GameManager.cs#L27),
[GameManager.cs:132](../../Assets/Core/GameManager.cs#L132)) are untouched. Initializing Steam first
also means `SteamIntegration.IsAvailable` is known before any achievement subscriber is wired.

### 5.5 Disclosed limits (honest MVP boundaries)

- **Crash / kill-9 before clean exit** leaves an un-checkpointed `-wal`. That is *safe locally* —
  SQLite replays the WAL on next open on the same machine — but that session's tail was never
  uploaded (no clean exit → no auto-cloud upload), and it will be recovered locally and
  re-checkpointed on the next clean exit. No data is lost on the machine that wrote it; a *different*
  machine syncing in the meantime simply gets the last cleanly-exited state. This is standard
  cloud-save behavior and is disclosed, not fixed.
- **Cross-machine conflict** resolves **last-clean-exit-wins** via Steam's own sync arbitration; there
  is **no in-game "which save?" conflict UI** in M7 (explicit non-goal, §1). Steam's built-in cloud-
  conflict dialog covers the pathological case at the client level.
- The godot-MCP headless boot force-kills via `stop_project` (no `_ExitTree`), so **the checkpoint
  path is verified through the `--headless --quit` boot** (which *does* run `_ExitTree`), the same
  clean-exit stand-in every prior phase used for flush-on-quit verification.

---

## 6. Rich Presence (low-risk)

`SteamIntegration.SetRichPresence(key, value)` (Facepunch `SteamFriends.SetRichPresence`) surfaces a
friends-list status string. The content is a read over already-live state — **no new data**:

- A `steam_display` localized token configured on the partner site, fed values derived from
  `GlobalState.SeasonYear` ([GlobalState.cs:27](../../Assets/Core/GlobalState.cs#L27)),
  `Career.HasAvatar` / the avatar's tier, and the avatar's name — e.g. *"Season 3 · Double-A · 22 y/o
  1B"* or *"Between careers"*.
- **Update cadence:** set on `AvatarChangedEvent` (creation / succession / promotion changes the tier
  or name) and on `SeasonRolledOverEvent` (the season number ticks). Both are already subscribed
  channels; rich presence rides them as one more tiny handler inside `SteamIntegration` (or the
  `AchievementManager`, implementer's call — same bus, same guard). No `_Process` string formatting
  (the `ui_conventions.md` no-per-frame-formatting rule generalizes: presence updates are
  event-driven, not polled).
- Fully guarded by `IsAvailable` like every other Steam call.

---

## 7. Content Compliance & Store Submission (Fable-owned, carries Phase-10 debt)

Not code, but a phase-close deliverable per BUILD_PLAN §11 and `GAME_IDEA.md`:

- **AI-portrait disclosure.** Phase 10e generated pre-made 2D portraits in the gritty-polaroid house
  style and explicitly **carried the Steam AI-content disclosure forward to Phase 11** (recorded in
  `presentation_layer_narrative.md` §6 and the 10e progress entry). Steam's store submission requires
  disclosing AI-generated art; this phase files that disclosure. It is the one Phase-11 artifact the
  presentation phase pre-generated for us.
- **Mature-content questionnaire.** The game contains **drug-dealing** (the Narcotics hustle, Phase
  8b) and **gambling references** (Texas Hold'em, Phase 8d). Complete Steam's mature-content
  questionnaire accurately for the appropriate age rating (BUILD_PLAN §11: *"drug dealing, gambling
  references"*). This is a truthful-disclosure task, not a design choice.
- **Depot / store page** basics: the two export presets (§3.3) produce the Windows + Linux depots;
  the store page copy, screenshots (the Phase-10 dark-mode shell is the shipping look), and the appid
  are partner-site work.

---

## 8. Sequenced Sub-Plan (one session each, house cadence)

| Step | Deliverable | Owner |
| :--- | :---------- | :---- |
| **11a — Native + lifecycle + degradation** | Facepunch package + native-lib copy targets in `.csproj`; `SetDllImportResolver`; `SteamIntegration` SDK lifecycle (Init/RunCallbacks/Shutdown) wired into `GameManager`'s three callbacks; the **graceful `IsAvailable` no-op contract** (§2) — the load-bearing step. Boots clean with **and without** a Steam client. | **Fable 5** |
| **11b — Achievements** | `AchievementManager` as a bus subscriber (§4); the §4.2 starter roster mapped to existing signals; idempotent unlocks + the Steam-Stats counter; **no schema change** asserted. | **Fable 5** |
| **11c — Cloud saves** | `DatabaseManager.CheckpointForSync` (§5.3) called in `_ExitTree` before dispose; Auto-Cloud path config (`.db` only); boot-order confirmation (§5.4). The WAL-correctness deliverable. | **Fable 5** |
| **11d — Rich presence** | `SetRichPresence` off `AvatarChangedEvent` / `SeasonRolledOverEvent` (§6); partner-site `steam_display` token. | **Fable 5** |
| **11e — Export + compliance** | First `export_presets.cfg` (Windows + Linux) with the `.sql`/`.json` include filters (§3.3); AI-portrait disclosure + mature-content questionnaire (§7); `validate_steamworks_native` green; Windows + Linux export builds launch. | **Fable 5** |

**11a must land first** — everything else calls through `SteamIntegration`. 11b/11c/11d are
independent and can run in any order behind 11a. 11e is the phase-close (the export builds can't be
validated until the native-lib targeting from 11a is in place).

---

## 9. Acceptance Criteria & Verification

Phase 11 touches **no** `Assets/Simulation/` code and **no** UI-logic, so the sim-band and UI-read-
only disciplines are satisfied by *absence of change* — but state it explicitly (`git show --stat`
proving no `Simulation/Baseball`, `Simulation/Life`, or `Assets/UI/` logic file moved is the standing
evidence). Specifically:

1. **The degradation proof (the phase's pressure-test).** The game boots and is fully playable with
   **no Steam client running** — one "[Steam] not available" log line, zero exceptions, every screen
   reachable. Verified via the live **`--headless --quit` Godot boot** against the real dev save
   (schema v10) with no Steam process — the established stand-in, and here it doubles as the
   no-Steam-present case (the MCP environment has no Steam client, so this is the *default* boot).
2. **Harness inertness — asserted, not assumed.** All eight `Tools/*` harnesses build and pass at
   their standing tallies with **no Facepunch reference leaking into any harness `.csproj`**. The
   Steam code lives only in `GameManager` (Godot, outside every harness compile set) and
   `Assets/Platform/Steam/` (which no harness globs). Confirm no `Tools/**/*.csproj` includes
   `Assets/Platform/` or references the Steam package. `run_monte_carlo_batch` **266/266**, no band
   moved (inert by construction — the sim assembly is never touched).
3. **`validate_steamworks_native` green** — the `.csproj` copies `steam_api64.dll` + `libsteam_api.so`
   to output (§3.1). A phase-close gate.
4. **No schema change — asserted.** `PRAGMA user_version` stays at **10**; `SchemaValidator` re-runs
   green unchanged; `DatabaseManager.CheckpointForSync` is the only Data touch and it alters no schema
   and no query (§5.3). No hardcoded `user_version` bump anywhere (contrast every schema phase's
   known "bump the three checks" gotcha — Phase 11 has none).
5. **Cloud round-trip smoke test (manual, real Steam client).** With a live Steam client + the appid:
   play, quit cleanly, confirm `dirt_and_diamonds.db-wal` is zero-length after exit (checkpoint ran),
   confirm the file uploaded to Steam Cloud, and confirm a fresh machine / cleared local save
   downloads and opens it. This is the human sign-off Steam integration always needs (no headless path
   exercises a real Steam backend).
6. **Achievement smoke test (manual, real Steam client).** Trigger at least one milestone
   (reach a pro tier → *Went Pro*) and one narrative choice (*Moonlighting*) and confirm the Steam
   toast fires once; reload and confirm the idempotent re-assert produces no duplicate toast.
7. **Export builds (BUILD_PLAN §11).** Windows and Linux export presets each produce a build that
   launches, reads `SchemaDefinitions.sql` + the content JSON out of the `.pck` (§3.3), opens the
   save, and reaches `Assets/Main.tscn`.

**BUILD_PLAN §11 / M7 exit bar:** cloud saves round-trip the SQLite DB; achievements fire off the
event stream; rich presence shows the career status; the Windows + Linux builds export and launch;
the AI-portrait disclosure + mature-content questionnaire are filed. With M7 green, the game is
shippable.

---

## 10. Disclosed Simplifications & Open Questions

- **Steam owns achievement state** — the game persists nothing to track unlocks (§4.3), so there is
  no local record of what fired; re-evaluation on reload is a harmless idempotent re-assert.
- **Auto-Cloud, not RemoteStorage** for the MVP (§5.3) — no in-game conflict-resolution UI;
  cross-machine conflicts resolve last-clean-exit-wins via Steam's own arbitration (§5.5).
- **Crash before clean exit** doesn't upload that session's tail (recovered locally on next open,
  re-checkpointed on next clean exit) — standard, disclosed (§5.5).
- **Two achievements deferred as stretch** (Hold'em win, peak-A+ rating) — they'd need a small
  additive result signal that isn't on the bus today; not required for M7 (§4.2).
- **`steam_appid.txt` is a dev-only artifact**, git-ignored, added by the export step, never
  committed to the depot (§2.2) — same posture as the `.db` never being committed.
- **Windows + Linux only** — no macOS export this phase (§1).
- **Open question for Fable in 11a:** whether Facepunch's current distribution supplies the native
  redistributables via its own NuGet runtime targets or requires the manual
  `<None ... CopyToOutputDirectory>` items — decide by inspecting the actual package at build time;
  `validate_steamworks_native` gates either answer (§3.1).
- **Open question for 11c:** confirm Steam Auto-Cloud's path-pattern config can exclude the
  `-wal`/`-shm` sidecars (sync `dirt_and_diamonds.db` only) — if the pattern is coarser than
  expected, the checkpoint(TRUNCATE) still leaves the sidecars harmless (zero-length wal), but the
  narrower pattern is preferred (§5.3). Verify on the partner site during 11c.
  **RESOLVED (11c, 2026-07-06):** Auto-Cloud file masks take exact literal filenames — a rule on
  `dirt_and_diamonds.db` syncs that file and nothing else, so the sidecars are excluded by
  construction. The full partner-site values (roots, subdirectories, masks, quota) are recorded in
  `Assets/Platform/Steam/README.md`; they are entered against the real appid at 11e (the Spacewar
  dev appid's cloud config isn't ours to edit), which is also when the §9.5 round-trip smoke test
  becomes possible.
- **RESOLVED (11e, 2026-07-06 — phase close; evidence in the 11e progress entry):**
  - **§3.3 presets exist and are committed** (`export_presets.cfg`, platforms `"Windows Desktop"` +
    `"Linux"` — the 4.3+ name); the Phase-0 `.gitignore` line hiding the presets file was removed
    (Godot 4 keeps secrets in `export_credentials.cfg`, which stays ignored).
  - **Beyond §3.3's include-filter concern, the exports needed an *exclude* filter:** Godot treats
    `.cs` and `.json` as resource types, so `all_resources` swept the `Tools/**` harness tree and
    `.mcp.json` into the first `.pck`. `exclude_filter="Tools/*, .mcp.json"` fixed it; game `.cs`
    path entries remain (scene script resolution) but ship content-stripped via
    `dotnet/include_scripts_content=false` — byte-verified on both depots.
  - **§3.1's copy items are now RID-scoped** to mirror the managed References (each native travels
    exactly with the assembly that P/Invokes it); the unscoped items had been shipping the Linux
    `.so` inside the Windows depot. `validate_steamworks_native` green at the `.csproj` level and
    against both real publish outputs — the Linux export was the first live `-r linux-x64` publish,
    proving the 11a reference switch.
  - **§9.7 launch proof:** Windows depot boots headless against the real save (pck'd `.sql` DDL →
    v10, pck'd JSON → 19 events + contacts, clean checkpoint exit, the §9.1 one-line degradation).
    **Disclosed:** the Linux depot is structurally verified (ELF64 template, Posix+`.so` publish
    output, same pck checks) but has no local launch — no Linux machine here; it queues on real
    Linux/Steam Deck hardware at submission.
  - **§7 compliance recorded in the Steam README** as verbatim partner-site text: the `ACH_*`
    minting table + `STAT_SEASONS_PLAYED` + the site-only stat-linked `ACH_JOURNEYMAN` (min 10),
    the AI-content disclosure worded around the truth that **zero raster portraits ship today**
    (file it only if art lands in the depot), and the mature-content questionnaire answers.
  - With 11e shipped, **every code-side §9 criterion is green** (1–4, 7); §9.5/§9.6 (live-Steam
    cloud round-trip + achievement toast) remain the manual, real-appid smoke tests.

---

## 11. Cross-References

- `docs/BUILD_PLAN.md` §11 (M7 exit criteria: Facepunch-only, cloud saves, achievements→dispatcher,
  rich presence, native-lib copy targets, `SetDllImportResolver`, content compliance, Win+Linux
  exports).
- `CLAUDE.md` §"Third-Party Integrations" (Facepunch.Steamworks EXCLUSIVELY; Steamworks.NET forbidden;
  native libs targeted in `.csproj`) + §"Required Workflow Procedures" (`validate_steamworks_native`).
- `.claude/skills/validate_steamworks_native/SKILL.md` (the `.csproj` native-copy gate).
- `Assets/Core/GameManager.cs` (composition root — `_Ready`/`_Process`/`_ExitTree` seams; the export-
  filter NOTEs at lines 29-36; `SaveFilePath`).
- `Assets/Data/Database/DatabaseManager.cs` (WAL config; the deliberate `Pooling = false` handle-
  release for cloud sync; the `CheckpointForSync` insertion point at `Dispose`).
- `Assets/Core/EventBus.cs` + `Assets/Core/CoreEvents.cs` (the achievement/presence signal source:
  `AvatarChangedEvent`, `ChildBornEvent`, `SeasonRolledOverEvent`, `PlayerAbsenceChangedEvent`,
  `GrittyEventResolvedEvent`).
- `Assets/Simulation/Baseball/CareerManager.cs` (`ReactivateAvatar` → `AvatarChangedEvent` republish
  on promotion; `LastSuccession`).
- `Assets/Data/Database/GameStateQueries.cs` (`DynastyGeneration`, `LineageOverReason` — the
  milestone-achievement read-model fields).
- `docs/design/presentation_layer_narrative.md` §6 (the Phase-10e AI-portrait Steam disclosure this
  phase files).
- `Assets/Platform/Steam/{SteamIntegration,AchievementManager}.cs` (the reserved stubs this phase
  fills).
