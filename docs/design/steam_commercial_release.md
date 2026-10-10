# Steam Commercial Release — Selling Dirt & Diamonds on Steam (Phase 17)

**Owner:** Gemini 3.1 Pro (achievements/presence code, builds, RC QA) · Gemini 3.8 Flash (store copy drafts, checklists, notices file) · **the user** (Steamworks account, payments, legal sign-off, pricing, every external submission)
**Status:** design complete 2026-10-09. **Authority:** [GAME_VISION.md](../GAME_VISION.md) §6.
**Builds on:** [steam_publishing_ship_it.md](../reference/steam_publishing_ship_it.md) (Phase 11, shipped): Facepunch.Steamworks only, native library resolver, graceful degradation when Steam is absent, achievements as a bus consumer, cloud saves with the WAL checkpoint, rich presence, Windows + Linux export presets. That doc stays the technical reference; this doc covers what it takes to **sell** the game.

> **Verify against current Steamworks documentation at submission time.** Steam's fees, review times, asset sizes and survey wording change. The figures here are planning values, not authority.

---

## 1. Release scope

- **Platforms at launch:** Windows x64 (primary) and Linux x64 (Phase 11 presets). **Steam Deck:** target "Playable" at launch (the 16:10 layout and text-size setting from [visual_redesign.md](visual_redesign.md) §4); "Verified" requires full controller support, a post-launch goal. **macOS:** not at launch (notarization and Apple hardware testing are a separate project).
- **Language:** English (all text in strings tables, so localization stays possible).
- **Business model:** a premium one-time purchase. No microtransactions, no ads, no online services beyond Steam's own (cloud, achievements, presence).

---

## 2. Steamworks setup and timeline (user-owned steps marked 👤)

1. 👤 Steamworks partner account, tax and bank info, **Steam Direct fee** paid for the app (planning value: US$100, recoupable; new partners face a waiting period before first release, so check the current rule).
2. 👤 App ID created; Pro wires it into `steam_appid.txt` for dev builds (never shipped in the depot) and into the build scripts.
3. Depots: Windows x64 and Linux x64, from the existing export presets. Launch options per OS.
4. 👤 **Store page "Coming Soon"** published early to collect wishlists. Steam requires the page to be live for a minimum period before release (planning value: 2 weeks; plan for months).
5. Optional: a **demo** (HS freshman season only) for a Steam Next Fest.
6. 👤 Store page review and build review submitted (planning value: several business days each; leave slack).
7. 👤 Release date set; launch.

---

## 3. Achievements and rich presence (17a)

Phase 11's discipline holds: **every achievement traces to a bus event or persisted state, never a new tracking write.** Each also gets an achievement cutscene card ([cutscenes_motion_comic.md](cutscenes_motion_comic.md) §6).

| Achievement | Signal | Status |
| :-- | :-- | :-- |
| **Signed** — commit to a college | `RecruitingCommittedEvent` | new |
| **Walk-On** — commit via the walk-on guarantee | `RecruitingCommittedEvent` (walk-on flag) | new |
| **Draft Day** — get drafted | `DraftCompletedEvent` (avatar picked) | new |
| **First-Rounder** — drafted in round 1 | `DraftCompletedEvent` (round == 1) | new |
| **The Show** — play your first Pro game | `AvatarChangedEvent` tier == Pro + first attended game | **redefined** (was MLB tier) |
| **State Champs** / **National Champs** / **Ring Bearer** | `ChampionshipWonEvent` (tier, avatar's team) | new |
| **Walk-Off** / **Grand Slam** | `AvatarGameSummaryEvent` (new, additive, published by `CareerManager` after the attended-game flush, carrying primitive flags) | new |
| **Benched** — be made inactive for a week | `RosterDesignationChangedEvent` (avatar, inactive) | new |
| **Short-Handed** — your team forfeits | `GameForfeitedEvent` (avatar's team) | new |
| **Going Straight** — take the door out | `GrittyEventResolvedEvent` (`dirt_going_straight`) | new |
| **Banned for Life** | `GrittyEventResolvedEvent` (`dirt_permanent_ban`) | new |
| **Next of Kin**, **Dynasty**, **End of the Line**, **Rap Sheet**, **Moonlighting**, **Juiced**, **Journeyman** | As shipped in Phase 11 | kept |
| ~~Went Pro~~ (first minor-league tier) | — | **removed** (no minors) |

`AchievementManager` publishes **`AchievementUnlockedEvent`** after a successful set (or locally when Steam is unavailable) for the cutscene director. Achievement definitions and icons live on the Steamworks partner site 👤. Icons come from the Phase 15 icon family.

**Rich presence tokens** (`RichPresenceWriter`, [Assets/Platform/Steam/README.md](../../Assets/Platform/Steam/README.md)): `#Tier_HS` "High School", `#Tier_College` "College", `#Tier_Pro` "Pro" (the minor-league and MLB tokens are retired), plus `#Phase_Playoffs` and `#Phase_DraftNight`. Example: *"Season 3 · College · #7 nationally"*.

---

## 4. Legal, disclosures and licensing package (17b)

**AI content disclosure (Steam content survey).** The game contains **pre-generated** AI content only, no live-generated content:

- **Voice:** all announcer lines (the only voiced lines; voice is broadcasters only as of 2026-10-10) are synthesized with **ElevenLabs** during development, reviewed by humans, and shipped as audio files.
- **Art:** any AI-assisted art (portraits, scene and cutscene art) is generated during development and curated and edited by humans.
- **Guardrails to state:** no runtime generation; no voices or likenesses of real people; every line and image is human-reviewed before release.

**Mature content survey.** Answer it truthfully for: **drug use and dealing** (the Narcotics hustle, PEDs), **simulated gambling** (Texas Hold'em, sports betting/debt; no real money), **crime and violence** (robbery, arrest, syndicate), **alcohol**, **mature relationship themes** (dating, pregnancy; non-explicit), and **strong language** (street contacts; announcers are clean).

**Licensing records** (`docs/legal/`):

- `elevenlabs_generation_log.md`: every generation run, the plan tier with commercial rights at the time, and the voices used ([voice_and_broadcast_audio.md](voice_and_broadcast_audio.md) §2). Voice Library voices: keep each voice's license terms.
- Font licenses (`Assets/UI/Fonts/LICENSES/`), music licenses (when a soundtrack is chosen), SFX library licenses, art tool terms (e.g. the Midjourney plan tier at generation time).
- **`THIRD_PARTY_NOTICES.txt`** shipped in the build: Godot Engine (MIT), Facepunch.Steamworks (MIT), the .NET runtime (MIT), Microsoft.Data.Sqlite / SQLitePCLRaw (their licenses), SQLite (public domain), fonts, and any other dependency. Generate it from the `.csproj` package list and audit it by hand.
- **EULA / privacy:** the Steam Subscriber Agreement covers distribution. The game collects no personal data and has no telemetry; say so on the store page. If telemetry is ever added, it needs a privacy policy first.

---

## 5. Trademark and naming clearance (17b)

👤 Before the store page goes public, screen these against trademark databases and major sports properties:

- **The game title** "Dirt & Diamonds".
- **League names:** the Pro league's brand (still open, [league_restructure.md](league_restructure.md) §16; **not** "Diamond League", a real athletics brand), the College league ("CBL" placeholder) and conference names.
- **All 68 team names and logos** ([league_restructure.md](league_restructure.md) Appendix A). Real city names are fine; the pairing of city + nickname must not match a real professional or college team. Logos are original designs, never AI-traced from real marks.
- **Announcer names and catchphrases:** must not echo real broadcasters' famous calls.

---

## 6. Store page assets (17c)

- **Capsules and library art:** header, small, main, vertical, library capsule, library hero, library logo, page background. 👤 Check current pixel sizes in Steamworks. All in the Phase 15 style, with **no review quotes or award laurels on capsules** unless real.
- **Screenshots:** at least 5 (planning: 8–10) showing both worlds: the at-bat broadcast view, the phone mid-deal, Draft Night, the schedule planner, a cutscene panel, a hustle.
- **Trailer:** 60–90 s, built from real gameplay and cutscenes: open on a high-school field, the announcer's home-run call, the phone buzzing with a shady text, the slide into the dirt, Draft Night, a Pro walk-off, the heir picking up the bat. The first 5 seconds must show gameplay. Captioned.
- **Copy:** short description (one hook sentence + the two worlds), long description with feature bullets (the pipeline, the announcers, the dark side, the legacy), tags (Sports, Simulation, Life Sim, Baseball, Crime, Choices Matter, Narrative, Mature), system requirements (measured in RC QA), and content descriptors matching §4.

---

## 7. Release-candidate QA (17d)

- **Gate suite green:** MonteCarlo (per-tier bands + Pro guard), GrittyEvents, CoreLoop, NeedsDecay, SchemaValidator, HustleHarness, HoldemHarness, PipelineHarness, AudioManifestHarness, CutsceneHarness.
- **Full-career playthrough** (manual checklist, extending `docs/reference/hs_manual_playtest_checklist.md`): new career → HS seasons and playoffs → recruiting → college → declare → Draft Night → Pro seasons, lock weeks, a forfeit, playoffs → a crime arc to arrest and the door out → marriage and child → retirement → succession → the heir's first game.
- **Platform:** clean installs on Windows 10/11 and a mainstream Linux distro; Steam Deck smoke test; Steam running vs not running (graceful degradation); Steam Cloud round-trip across two machines, including the conflict prompt.
- **Budgets:** cold boot to title < 10 s on the minimum spec; no frame drops below 60 fps in the shell or cutscenes on the recommended spec; **install size < 2 GB** (planning: about 1 GB with ~150 MB of voice); a day-advance batch transaction within its benchmark.
- **Stability:** a 10-season soak test with no crash; logs at `user://logs`, with a "Open log folder" button in Settings for bug reports.
- **Content:** every subtitle present; every achievement unlockable in a test build; no placeholder art or synthesized placeholder tones left (an `AudioManifestHarness` / asset-manifest check).

---

## 8. Save compatibility policy

- **Pre-1.0:** schema v14 ([league_restructure.md](league_restructure.md) §14) is a clean break; old saves are refused and backed up.
- **From 1.0 on (binding):** every schema bump ships a **migration** keyed on `PRAGMA user_version`, a **migration harness check** that loads a frozen save from every prior shipped version, and a backup of the pre-migration file. Breaking saves after launch is a release blocker.
- Cloud saves keep the Phase 11 rule: checkpoint(TRUNCATE) on clean exit, and only the `.db` syncs.

---

## 9. Launch operations (17e)

- 👤 **Price** and regional pricing (Steam's suggested regional pricing as the baseline); optional launch discount.
- **Branches:** `default` (live), `beta` (opt-in previews), `staging` (internal RC). Every live build passes the RC gate suite.
- **Patch cadence:** a hotfix window for the first two weeks, then regular updates with patch notes posted as Steam events.
- **Community:** Steam discussions with a bug-report template pointing at the log folder; reply to reviews.
- **Post-launch roadmap candidates:** controller support / Deck Verified, a Pro color commentator, a soundtrack expansion, localization, macOS.

---

## 10. Sub-slices

| Slice | Content | Owner | Gate |
| :-- | :-- | :-- | :-- |
| **17a** | Achievements + rich presence refresh (§3), new Core events, `AchievementUnlockedEvent` | Pro | MC/Gritty inert; `validate_steamworks_native`; every achievement fires in a scripted harness run |
| **17b** | Legal and disclosure package (§4–§5): notices file, licensing logs audited, trademark screen | Flash drafts → 👤 user sign-off | Checklist complete |
| **17c** | Store page assets (§6) | Flash + 👤 | 👤 approves capsules, trailer and copy |
| **17d** | Release-candidate QA (§7) | Pro + 👤 playtest | Every §7 item checked |
| **17e** | Steamworks submission and launch (§2, §9) | 👤 with Pro support | Store review + build review passed |
