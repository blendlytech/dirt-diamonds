# Steam Platform Layer (Phase 11)

`SteamIntegration.cs` is the **only** class in the codebase that calls the Steam SDK
(Facepunch.Steamworks exclusively, per `CLAUDE.md`). Everything else goes through its
`IsAvailable`-guarded wrappers — a missing Steam client is the normal development state,
never an error (`docs/design/steam_publishing_ship_it.md` §2).

## `lib/` — vendored managed assemblies (committed)

Facepunch.Steamworks **2.5.2** (net6.0, Release), MIT-licensed, vendored from the
official GitHub release because the distribution has no complete package form:
NuGet.org stalled at 2.3.3 (Feb 2020) and has never carried the Posix build.

- Source: <https://github.com/Facepunch/Facepunch.Steamworks/releases/tag/2.5.2>
  (`Facepunch.Steamworks.2.5.2.zip` → `Release/net6.0/`), release commit `5a22fa2`.
- `Facepunch.Steamworks.Win64.dll` — P/Invokes `steam_api64`; referenced for every
  build except `linux-x64`.
- `Facepunch.Steamworks.Posix.dll` — P/Invokes `libsteam_api`; referenced only when
  publishing with `-r linux-x64` (the Godot Linux export). Same API surface, different
  native name + struct packing, which is why the platform split exists.
- The `.xml` doc files ride along for IDE IntelliSense.

Upgrading: replace all four files from a newer release zip's `Release/net6.0/` folder
and update the version + commit here and in `DirtAndDiamonds.csproj`.

## `native/` — Steam native redistributables (git-ignored drop)

`steam_api64.dll` (Windows x64) and `libsteam_api.so` (Linux x64 / Steam Deck) are
**not committed** (`.gitignore` — "copied at build time, not committed"). Drop them
here from the same release zip (`Release/Unity/redistributable_bin/win64/` and
`linux64/`); the `.csproj` copies whichever exist to the build output root
(`validate_steamworks_native` checks exactly this wiring).

Without the drop the project still builds and boots — `SteamClient.Init` fails on the
missing native, `SteamIntegration` catches it, and the session runs with Steam
features disabled. That degraded boot is the everyday dev/CI/headless path.

Both subfolders carry a `.gdignore` so the Godot importer never scans the binaries;
MSBuild reaches them by filesystem path, not `res://`.

## Steam Auto-Cloud configuration (11c — partner-site work, entered at 11e)

Cloud saves use **Auto-Cloud, not the RemoteStorage API** (design doc §5.3): Steam
syncs the save file around the process lifetime, and `GameManager._ExitTree` runs
`DatabaseManager.CheckpointForSync()` (`PRAGMA wal_checkpoint(TRUNCATE)`) just before
the handle releases, so the single `.db` uploaded after a clean exit is the complete
save. The partner site can't be configured against the Spacewar dev appid, so the
values below are recorded here for the real appid at 11e (Steamworks → App Admin →
Cloud):

- **Byte quota / file count:** 200 MB / 2 files (one live save today; headroom for a
  future second slot — resize freely, it just caps, never truncates).
- **Auto-Cloud root paths** (the save is Godot's `user://dirt_and_diamonds.db`,
  resolved per-OS from `config/name="Dirt & Diamonds"`):

  | OS | Root | Subdirectory | File mask |
  | :- | :--- | :----------- | :-------- |
  | Windows | `WinAppDataRoaming` | `Godot/app_userdata/Dirt & Diamonds` | `dirt_and_diamonds.db` |
  | Linux | `LinuxXdgDataHome` | `godot/app_userdata/Dirt & Diamonds` | `dirt_and_diamonds.db` |

- **The file mask is the exact filename, not a wildcard** — this resolves the design
  doc's §10 open question: Auto-Cloud path patterns take literal names, so the
  `-wal`/`-shm` sidecars are excluded by construction (`dirt_and_diamonds.db*` would
  wrongly sweep them in; a machine-A `-wal` against a machine-B `.db` is undefined
  behavior). The checkpoint leaves the `-wal` zero-length anyway — belt and braces.
- **OS sync:** enable "Cloud on all platforms" for the two rules above only; conflict
  handling stays Steam's own last-clean-exit-wins dialog (no in-game conflict UI, §5.5).

Verify after entering (§9.5, real Steam client + real appid): play, quit cleanly,
confirm `dirt_and_diamonds.db-wal` is zero-length beside the save, confirm the upload
in the Steam client's cloud status, then clear the local folder and relaunch — the
download must open at the same day/season.

## Rich presence (11d — partner-site localization entered at 11e)

`RichPresenceWriter.cs` is the design doc's §6 presence writer: a bus subscriber
(`AvatarChangedEvent` / `SeasonRolledOverEvent`, plus the boot sync) whose only side
effect is `SteamIntegration.SetRichPresence`. Event-driven only, never per-frame, and
every call no-ops without a Steam client. **No display wording lives in C#** (the
Phase-10 localization posture): the code sets stable tokens and raw values; the
partner site's rich-presence localization file owns every player-visible string.
Against the Spacewar dev appid nothing renders — expected until the real appid exists.

Keys the game sets:

| Key | Value | When |
| :-- | :---- | :--- |
| `steam_display` | `#Status_Career` or `#Status_BetweenCareers` | every publish |
| `season` | season year, invariant digits | career publishes |
| `tier` | a `#Tier_*` sub-token (values starting with `#` localize too) | career publishes (tier resolves) |
| `player` | avatar first + last name | career publishes (row resolves) |
| `age` | avatar age, invariant digits | career publishes (row resolves) |

Tokens to define at 11e (Steamworks → App Admin → Rich Presence localization file),
English set — wording is 11e's to polish, the token ids are the contract:

- `#Status_Career` — `Season %season% · %tier% · %player%, age %age%`
- `#Status_BetweenCareers` — `Between careers`
- `#Tier_HS` — `High School`
- `#Tier_College` — `College`
- `#Tier_MinorA` — `Class A`
- `#Tier_MinorAA` — `Double-A`
- `#Tier_MinorAAA` — `Triple-A`
- `#Tier_MLB` — `MLB`

(The tier display names mirror `BaseballDashboard`'s exported `TierNamesCsv` defaults
so the friends list and the in-game scouting card agree.)

Publish cadence: avatar creation / succession / the 9c promotion republish (all on
`AvatarChangedEvent`), the season tick (year + yearly-aged age), and boot. A boot with
no avatar — or a save whose `lineage_over_reason` is set, which still carries the
parked retiree's avatar pointer — publishes `#Status_BetweenCareers`; a game-over
rollover flips to it live.
