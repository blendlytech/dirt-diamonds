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

## Achievements & Stats (11b — partner-site definitions minted at 11e)

The API names below are code-side consts in `AchievementManager.cs`; the partner
site (Steamworks → App Admin → Stats & Achievements) must mint them **verbatim** —
renaming one there means renaming the const too, the same wire-contract warning as
the gritty-event content-id families. Display names/descriptions are the site's to
own (no player-facing achievement strings live in C#, the Phase-10 localization
posture); the English copy below is the 11e suggestion. "Hidden" = the Steam
spoiler flag.

| API name | Display name | Description (suggested) | Hidden |
| :------- | :----------- | :---------------------- | :----- |
| `ACH_WENT_PRO` | Went Pro | Sign your first professional contract. | no |
| `ACH_THE_SHOW` | The Show | Reach the major leagues. | no |
| `ACH_NEXT_OF_KIN` | Next of Kin | Welcome a child into the family. | no |
| `ACH_DYNASTY` | Dynasty | Carry the bloodline into its third generation. | no |
| `ACH_END_OF_THE_LINE` | End of the Line | Watch a lineage end. | yes |
| `ACH_RAP_SHEET` | Rap Sheet | Get arrested. | yes |
| `ACH_MOONLIGHTING` | Moonlighting | Take your first step into the underworld. | yes |
| `ACH_JUICED` | Juiced | Face the consequences of performance enhancement. | yes |
| `ACH_JOURNEYMAN` | Journeyman | Play ten seasons of professional baseball. | no |

**Stat:** `STAT_SEASONS_PLAYED` — INT, default 0, increment only. The game calls
`AddStat(+1)` on each season rollover while a bloodline holds a career (pre-creation
and post-game-over seasons don't count) and never reads it back.

**`ACH_JOURNEYMAN` is stat-linked, not code-fired:** set its "Progress Stat" to
`STAT_SEASONS_PLAYED` with **unlock min value 10**. The game never references the
`ACH_JOURNEYMAN` id — Steam evaluates the stat-vs-threshold rule server-side — so
the threshold (and this one id) can be tuned on the site freely with no code
change. All eight code-fired achievements are idempotent re-asserts; Steam owns
unlock state (design doc §4.3, why the save schema never changed).

## Store compliance (11e — design doc §7)

Filed with the store submission; recorded here so the partner-site entry is a
transcription job, not a judgment call.

### AI-generated-content disclosure

Steam's survey distinguishes **pre-generated** AI content (made with AI during
development, reviewed before shipping) from live-generated. As of 11e the build
ships **zero raster portraits** — `PortraitTile` renders its procedural
initials-tile fallback for every character (10e, deliberate). The plan of record
(Phase 10 §6) is pre-made AI-generated 2D character portraits in the
gritty-polaroid house style, human-reviewed, with no live generation in the
shipped build. Disclosure text to file **if and when portrait art lands in the
depot**:

> Some character portraits are pre-generated 2D images created during development
> with the assistance of generative-AI tools. All such images were reviewed by the
> developers before inclusion. The game does not generate content with AI at
> runtime.

If the launch depot still ships only the procedural fallback, file "no AI-generated
content" instead — do not carry a disclosure for art that isn't in the build. Either
way this item closes at submission review against the actual depot contents.

### Mature-content questionnaire

Truthful answers for the current content set (BUILD_PLAN §11: "drug dealing,
gambling references"):

- **Some Nudity or Sexual Content / Frequent Nudity / Adult-Only Sexual Content:**
  No (none anywhere).
- **Frequent Violence or Gore:** No — violence is implied through text-based
  narrative events (beanball grudges, syndicate intimidation), never depicted.
- **General Mature Content:** Yes. Mature-content description (the free text shown
  to users on the store page):

> Dirt & Diamonds contains mature themes presented through text-based narrative
> events: illegal drug dealing (an abstract narcotics-hustle system), gambling
> depictions (Texas Hold'em played for in-game currency only — no real-money
> wagering and nothing purchasable), performance-enhancing-drug use and its
> health/legal consequences, bribery and organized-crime storylines, arrests,
> and alcohol use. All of it is fictional, consequence-bearing, and rendered as
> narrative text and menus rather than graphic depiction.

No loot boxes, no microtransactions, no real-money gambling (relevant to the IARC
regional rating follow-ups the questionnaire feeds). The export presets that build
the submitted depots live at the repo root (`export_presets.cfg`, 11e — the
`.sql`/`.json` include filters there are load-bearing, see the file header).
