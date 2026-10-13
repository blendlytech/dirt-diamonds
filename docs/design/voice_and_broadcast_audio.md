# Voice & Broadcast Audio — ElevenLabs Team Announcers (Phase 14)

**Owner:** Gemini 3.1 Pro (audio runtime, pipeline tool, review) · Gemini 3.8 Flash (announcer personas, scripts, UI wiring) · Gemini 3.1 Pro Math (call-selection weights only)
**Status:** design complete 2026-10-09; **scope cut 2026-10-10** (below); no code yet. **Authority:** [GAME_VISION.md](../GAME_VISION.md) §5 and §7.

> **2026-10-10 owner directive: voice is for broadcasters only.** ElevenLabs voice is restricted to `BroadcastBooth` and the announcers. There are **no** phone voice notes, **no** cutscene voice-over and **no** character dialogue; those stay text (bubbles, captions, subtitles). This doc has been amended throughout: §8 is removed, slice 14e is cut, and the `Voice` bus and `Dialogue` channel are gone. Cutscenes may still use a **broadcaster call** as their audio bed (big plays, Draft Night, championships).
**Builds on:** [ui_audio_slice_d.md](../reference/ui_audio_slice_d.md) (the shipped `UiSfx` autoload, `SFX` bus, `user://settings.cfg` volume) and [at_bat_presentation.md](../reference/at_bat_presentation.md) (the beat sequencer the announcer rides on). Feeds [cutscenes_motion_comic.md](cutscenes_motion_comic.md) (broadcaster calls as cutscene audio beds).

---

## 1. Scope

**In scope at launch:**

1. **Play-by-play announcers.** One signature play-by-play voice for each of the 56 College and Pro teams, plus **3 shared HS local-radio voices**. Sound bites only: calls, taglines and reactions. No continuous narration.
2. **Broadcaster calls inside cutscenes.** A big-play, Draft Night or championship cutscene may use a booth's call as its audio bed (`announcer_of` + category, [cutscenes_motion_comic.md](cutscenes_motion_comic.md) §4). These are ordinary announcer clips from the banks below, not separate VO.

**Explicit non-goals:**

- **No character voices (2026-10-10).** No phone voice notes, no cutscene voice-over, no dialogue for any character, contact or NPC. All of it is text.
- **No runtime AI.** The shipped game never calls ElevenLabs or any other service. Every clip is a file in the build.
- **No spoken player names or avatar pronouns.** Pre-generated clips can't say a player-chosen avatar name or a procedurally generated NPC name, and they can't know the avatar's gender (the avatar can be male or female, [temptation_and_lineage.md](temptation_and_lineage.md) §8). Announcers call by role and team ("the kid", "the Barons' shortstop", "number one on the depth chart"), never "he" or "she" for the avatar. The name appears in the subtitle line. The only gendered avatar lines are the Trailblazer route's own calls, gated on `trailblazer_route`.
- **No real people.** No voice cloned from or imitating a real person or real broadcaster.
- **No live commentary track** for background-simulated games (§7.5 covers the highlight reel instead).
- **No color commentator at launch.** It's a stretch goal (§11).

---

## 2. Production pipeline (offline, developer machine only)

```text
 persona + script sheets (JSON, in repo)        voice profiles (JSON, in repo: voice ids + settings, no secrets)
            │                                              │
            └──────────────► Tools/VoiceGen ◄──────────────┘
                                 │  ELEVENLABS_API_KEY from the environment (never committed, never in builds)
                                 │  skip any line whose content hash is unchanged
                                 ▼
               raw renders → trim silence → loudness-normalize → encode Ogg Vorbis
                                 ▼
          Assets/Audio/Voice/<bank>/<clip_id>.ogg   +   Assets/Audio/Voice/voice_manifest.json
                                 ▼
             human listening pass (bump `take` per bad clip) →  AudioManifestHarness gate
```

- **Tool:** `Tools/VoiceGen` (a C# console project, alongside the existing harnesses). Input is **script sheets**, one per announcer bank (a College/Pro booth or an HS pool voice). Each line has `clip_id`, `text`, `category`, `voice_profile`, optional delivery notes, and `take` (an integer, default 1; bumping it requests a retake, and because it is part of the content hash, the retake is idempotent). Task brief: [docs/tasks/voicegen_task_brief.md](../tasks/voicegen_task_brief.md).
- **Voices:** created with ElevenLabs **Voice Design** (original synthetic voices) or taken from the Voice Library **only where that voice's terms permit commercial use**. Each voice profile records `elevenlabs_voice_id`, model, stability/similarity/style settings, and how it was created. Profiles live in `Tools/VoiceGen/voice_profiles.json`. Voice ids aren't secret; the API key is.
- **API key:** read from the `ELEVENLABS_API_KEY` environment variable only. Never written to the repo, logs, the manifest or any build. Add the key name to `.gitignore`-adjacent docs, and add a pre-commit grep for `sk_` / `xi-api-key` (§10).
- **Incremental:** each clip's content hash = hash(text + voice profile + settings + model). Unchanged hashes are skipped, so re-running after editing 10 lines costs 10 lines of credits.
- **Mastering:** trim leading/trailing silence to 40 ms; **loudness target −18 LUFS integrated for speech, true peak ≤ −1 dBTP**; mono, 44.1 kHz, Ogg Vorbis ~q4 (about 64–80 kbps). The measured loudness and duration go into the manifest.
- **Licensing log:** every generation run appends to `docs/legal/elevenlabs_generation_log.md`: date, ElevenLabs plan tier (it must be a plan granting commercial rights at generation time), model, number of clips, and voice profiles used. This log is part of the Steam release package ([steam_commercial_release.md](steam_commercial_release.md) §4).
- **Human review is mandatory.** Every clip gets a listening pass before it ships: mispronunciations, artifacts, wrong energy. Failures get their `take` bumped and re-render with a new seed (or adjusted settings).

### 2.1 Budget (planning estimate)

| Bank | Count | Lines each | Clips |
| :-- | :-- | :-- | :-- |
| Pro announcers | 32 | ~120 | ~3,840 |
| College announcers | 24 | ~62 | ~1,490 |
| HS radio pool | 3 | ~94 | ~280 |
| **Total** | | | **~5,610 clips** |

At about 70 characters per line, that's roughly 390k characters for one pass, so **plan for about 900k characters including retakes**. Pick the ElevenLabs plan tier from that figure at generation time. Ship size at about 2.5 s average per clip: **~140 MB** of Ogg audio. (The phone voice-note and cutscene VO banks, ~500 clips, were cut on 2026-10-10.)

---

## 3. Runtime architecture

### 3.1 Buses

The shipped layout (`Master → SFX`) is extended in `default_bus_layout.tres`:

| Bus | Sends to | Carries | Notes |
| :-- | :-- | :-- | :-- |
| `Master` | — | everything | |
| `SFX` | Master | UI tones (shipped), foley | Shipped in Slice D |
| `Announcer` | Master | play-by-play | Separate so players can turn the announcer down or off without losing story voice |
| `Music` | Master | soundtrack (content TBD, §11) | `AudioEffectCompressor` sidechained to `Announcer`: ducks ~8 dB under calls |
| `Ambience` | Master | crowd beds, stadium/street room tone | Sidechain duck ~5 dB under `Announcer` |

**Settings (phone → Settings, persisted in `user://settings.cfg` exactly like the shipped SFX slider):** Master, Music, SFX and Announcer volume sliders; an **Announcer on/off** toggle; **Subtitles** on/off (default **on**).

### 3.2 `VoiceDirector` (third autoload, `Assets/Audio/`)

Same idiom as `UiSfx`: `Instance`, `ProcessMode.Always`, pooled `AudioStreamPlayer`s, no allocation on the play path.

- **One channel:** `Booth` (announcer calls, including a call used as a cutscene's audio bed). It plays one clip at a time.
- **Priority** (higher interrupts lower with a 150 ms fade): big-play call (or cutscene bed) > routine call.
- **Loading:** `VoiceBank` resources are loaded asynchronously (`ResourceLoader.LoadThreadedRequest`). The avatar's team booth bank is preloaded at boot and whenever the avatar's team changes (about 120 clips, ~3–5 MB). Other banks (highlights, draft night, opponents' banks for highlights) load on demand and are released by LRU.
- **Subtitles:** every play request publishes a `SubtitleRequested(speakerLabel, textKey, durationMs)` signal, which the active scene's subtitle strip renders. Subtitle text comes from the strings table, keyed by `clip_id`.
- **Boundary:** `VoiceDirector` is presentation-only. It consumes beats and signals and never writes the DB or sim state. Its call-variant randomness uses its **own** presentation RNG, never the sim's `RngState`, so the baseball sim's determinism and the Monte Carlo guards are untouched.

### 3.3 Manifest

`Assets/Audio/Voice/voice_manifest.json` (generated by VoiceGen, committed):

```json
{ "clip_id": "pro_bay_home_run_03", "bank": "announcer/pro_bay", "category": "home_run",
  "file": "res://Assets/Audio/Voice/announcer/pro_bay/pro_bay_home_run_03.ogg",
  "text": "Tell the gulls to duck — that ball is GONE!", "voice_profile": "ray_calloway",
  "duration_ms": 2840, "lufs": -18.2, "content_hash": "9f2c…" }
```

---

## 4. Who calls which game

- **The avatar's team's booth calls every game the avatar's team plays**, home and away. These are radio homers: thrilled by their own team, deflated by the opponent. This makes "your" announcer the voice of your career. Changing teams (Signing Day, the Draft, a release) changes the voice in your ear, which is a deliberate emotional beat.
- **HS games** use the HS radio pool voice assigned to the avatar's school (§5.4).
- **Other booths** are heard in the **Around the League** highlight reel (§7.5), on **Draft Night** (the drafting team's welcome clip), and in championship cutscenes involving that team.

---

## 5. Announcer personas

### 5.1 Persona template (one JSON per announcer, `Assets/Audio/Voice/Announcers/personas/<team_abbr>.json`)

```json
{
  "announcer_id": "pro_bay",
  "team_abbr": "BAY",
  "name": "Ray Calloway",
  "archetype": "golden_age_crooner",
  "bio": "Forty years behind the Barons mic; calls every home run like it's the last one he'll see.",
  "energy": 6,
  "homer_bias": 8,
  "voice_profile": "ray_calloway",
  "home_park": "Lighthouse Field",
  "team_nicknames": ["the Barons", "the Bay boys"],
  "signature_calls": {
    "home_run":   "Tell the gulls to duck — that ball is GONE!",
    "win":        "Barons win! Ring the harbor bell!",
    "strikeout":  "Sit down, friend. Sit right down.",
    "grand_slam": "Four men home, and the whole bay's on its feet!",
    "walk_off":   "Goodnight, Bayport — the Barons walk it off!"
  },
  "do_not": ["profanity", "naming any real player or broadcaster"]
}
```

`signature_calls` are the **taglines**. Each appears verbatim in at least one clip of its category, and the other variants riff on it.

### 5.2 Archetype palette

No archetype is used more than 5 times across the 56, and **no two teams in the same division share one**:

| Archetype | Feel |
| :-- | :-- |
| `golden_age_crooner` | Velvet-voiced, lyrical, old radio |
| `hype_man` | Maximum volume, catchphrase machine |
| `dry_wit` | Understated, sarcastic, funny under pressure |
| `grizzled_vet` | Gravel-voiced ex-catcher, seen it all |
| `stats_nerd` | Gleeful about numbers, spray charts, history |
| `folksy_storyteller` | Small-town, metaphor-heavy, unhurried |
| `excitable_rookie` | Young, voice cracks on big plays |
| `poet` | Elevated language, treats baseball as literature |
| `deadpan` | Monotone until it really matters, then erupts |
| `bilingual_firecracker` | English with Spanish exclamations (subtitled) |
| `radio_purist` | Precise, by-the-book, counts and locations |
| `local_legend` | A former star from the team, emotional and loyal |

### 5.3 Worked examples (the remaining 53 are a Flash content batch)

| Team | Announcer | Archetype | Home-run tagline | Win tagline |
| :-- | :-- | :-- | :-- | :-- |
| Bayport Barons (Pro) | Ray Calloway | golden_age_crooner | "Tell the gulls to duck — that ball is GONE!" | "Barons win! Ring the harbor bell!" |
| Mesa Roja Rattlers (Pro) | Lupe "La Voz" Ibarra | bilingual_firecracker | "¡Adiós, pelota! The Rattlers strike!" | "Shake the tail, Mesa Roja — Rattlers win!" |
| Ashford State Admirals (College) | Teddy Marsh | excitable_rookie | "Oh — oh — OH, that's outta here, Admirals!" | "Anchors up! Admirals take it!" |

### 5.4 HS radio pool (3 shared voices)

| Voice | Persona | Assigned schools |
| :-- | :-- | :-- |
| `hs_pool_dale` | "Coach Dale", retired shop teacher on the town AM station; folksy | North conference, ids 101–106 |
| `hs_pool_kira` | Kira, a student broadcaster running the school stream; earnest, nervous, funny | South conference, ids 107–112 |
| `hs_pool_gus` | Gus, a volunteer dad with a megaphone voice | Playoffs and the State Championship |

Each pool voice records ~70 generic calls plus **24 school stingers** (12 full names, "…for the Crestwood Cardinals!", and 12 mascot-only, "…Cardinals!"). A call plays the generic line followed by the stinger. That's how three voices make every school sound personal.

---

## 6. Clip categories

Categories map **only to signals the at-bat sequencer actually emits**: PA outcome, pitch reveal, game start/end, inning change, score state. A category with no real signal is cut, not faked. The implementer confirms each row against the beat DTOs in slice 14d.

| Category | Trigger | Pro | College |
| :-- | :-- | :-: | :-: |
| `game_open_home` / `game_open_away` | First pitch | 4 / 3 | 2 / 2 |
| `late_tension` | Inning ≥ regulation−2, margin ≤ 2, runner in scoring position | 6 | 3 |
| `single` | Own-team single | 6 | 3 |
| `extra_base` | Own-team double/triple | 6 | 3 |
| `home_run` ★ | Own-team HR (signature tagline) | 8 | 4 |
| `grand_slam` ★ | Own-team HR, bases loaded | 2 | 1 |
| `walk_off` ★ | Home team takes the lead in its final half-inning | 3 | 2 |
| `walk` | Own-team walk | 3 | 2 |
| `strikeout_ours` ★ | Own pitcher records a K (tagline) | 6 | 3 |
| `strikeout_theirs` | Own batter strikes out (groan) | 4 | 2 |
| `routine_out` | Ball in play, out | 6 | 3 |
| `run_scores` | Own run scores | 5 | 3 |
| `lead_change` | Own team takes the lead | 4 | 2 |
| `game_tied` | Own team ties | 3 | 2 |
| `opponent_big_hit` | Opponent XBH/HR (deflated) | 6 | 3 |
| `final_win` ★ / `final_loss` | Last out | 6 / 5 | 3 / 3 |
| `extra_innings` | Regulation ends tied | 2 | 1 |
| `pitching_change` | Reliever enters | 3 | 1 |
| `career_first` | Avatar's first hit / first HR at this tier | 3 | 2 |
| `clinch` | Playoff berth / series win | 4 | 2 |
| `championship` ★ | Title won | 3 | 2 |
| `welcome` | Draft Night (Pro) / Signing Day (College) | 2 | 2 |
| `forfeit` | Team forfeits (§7 of the league doc) | 2 | 1 |
| `color` | Between-batter flavor: park, city, weather, team lore | 10 | 5 |
| `team_stinger` | "…and the Barons lead!" short tags | 5 | 2 |
| **Total** | | **~120** | **~62** |

★ = big play: always called, and eligible to trigger a big-play cutscene.

---

## 7. `BroadcastBooth` — the in-game call logic

`Assets/UI/Broadcast/BroadcastBooth.cs` is a child of the at-bat view (presentation layer).

1. **Input:** the at-bat beat sequencer's beats (`PitchReveal`, `PaReveal`, game start, half-inning change, game end) plus a game-context DTO (score, inning, outs, runners, which side is "ours"). **Never** the sim thread directly. *From Phase G, the arcade engine emits these same beats, plus an additive `PlayDetail` (exit velocity, distance, diving catch, double play, assist, walk-off) that new call categories may use. The booth is silent during skips ([arcade_baseball_overhaul.md](arcade_baseball_overhaul.md) §7.6).*
2. **Category resolution:** the most specific category wins (`grand_slam` beats `home_run` beats `extra_base`). Context overlays (`lead_change`, `walk_off`) take priority over the base outcome when both apply.
3. **Density:** big plays (★) always play. Routine categories play with a probability (Math-owned knobs, starting values: `routine_out` 0.25, `single` 0.7, `walk` 0.4, `color` once per 3–5 PAs when nothing else is playing). At least 1.5 s between routine calls.
4. **Variety:** weighted pick among the category's variants, excluding the last `min(3, n−1)` played (a per-category ring buffer). The signature tagline variant gets weight 2.
5. **Pacing:** the sequencer holds the next `AwaitingInput` until a `PaReveal` call finishes or 4 s pass, whichever comes first. **Fast-forward** mode plays big plays only. With Announcer off, the booth is silent and adds no delay.
6. **Big plays → cutscene:** a ★ call raises `BigPlay(kind, contextDto, callClipId)` to the `CutsceneDirector` (Phase 16). The call clip becomes the cutscene's audio bed, so the voice and the animation land together.

### 7.5 Around the League highlights

Once per game day, the Dashboard's **Around the League** card lists up to 3 standout background plays (from the box-score flush: walk-offs, grand slams, multi-HR games). Each has a play button that plays **that team's** booth signature call, with a subtitle. This is how the other 55 announcers get heard.

---

## 8. ~~Phone voice notes~~ (removed 2026-10-10)

Cut by owner directive: voice is broadcasters only. Phone messages stay text. There is no `voice_note` event field, no contact `voice_profile`, no voice-note bubble and no voice-note budget. Content that authors a `voice_note` fails `check_event_graph_integrity` #9.

---

## 9. Content rules

- **Fictional voices only.** No cloning, no "sounds like [famous broadcaster]" prompts.
- **Announcers are broadcast-clean:** no profanity, no slurs. Street contacts may swear (mature rating, [steam_commercial_release.md](steam_commercial_release.md) §4). No slurs anywhere.
- No real players, teams, leagues, brands or stadiums named in any line.
- Spanish (and any other language) lines are subtitled in English.
- Every line is reviewed by a human in context (listening pass) before ship.

---

## 10. Validation

**`AudioManifestHarness`** (`Tools/AudioManifestHarness`, run through the `validate_audio_manifest` skill) checks:

1. The manifest parses; every `file` exists; no `.ogg` under `Assets/Audio/Voice/` is missing from the manifest (no orphans).
2. **Coverage:** every College/Pro announcer has every category at ≥ its minimum count (§6), and each signature tagline appears verbatim. Every HS pool voice has all 24 stingers.
3. **Durations:** announcer clips ≤ 6 s.
4. **Loudness** within ±1 LU of −18 LUFS (measured values stored by VoiceGen).
5. **References:** every cutscene `announcer_of` + category and every persona `voice_profile` resolves, and **every voice reference anywhere is an announcer-bank clip** (no non-broadcaster bank may exist). Every `clip_id` has a strings-table subtitle.
6. **Secrets:** the repo contains no ElevenLabs API key pattern.

`check_event_graph_integrity` check #9 is now the inverse: **no event may author a `voice_note`** (2026-10-10). The Monte Carlo and Gritty harnesses stay **inert** for every Phase 14 slice, because no `Assets/Simulation/` code is touched.

---

## 11. Sub-slices

| Slice | Content | Owner | Gate |
| :-- | :-- | :-- | :-- |
| **14a** | Bus layout + sidechain ducking + `VoiceDirector` autoload + settings sliders/toggles + subtitle strip (placeholder synthesized clips, Slice D precedent) | Pro | Live boot; ear check; inert MC |
| **14b** | `Tools/VoiceGen` + manifest + mastering + licensing log + `AudioManifestHarness` / `validate_audio_manifest` | Pro | Harness green on a 20-clip pilot bank |
| **14c** | 56 personas + 3 HS pool personas + all announcer scripts | Flash → Pro review | Archetype distribution rule; tagline review by the user |
| **14d** | `BroadcastBooth` + at-bat integration + Around the League card | Pro (Flash: card UI) | Manual playtest; inert MC |
| ~~**14e**~~ | ~~Voice notes~~: **cut 2026-10-10** (broadcasters only) | — | — |
| **14f** | Full generation run + listening QA + retakes | Flash (run) + user (taste) | `validate_audio_manifest` 100% |

**Open items:** a Pro color commentator (a second voice per booth; stretch, doubles Pro cost); the **soundtrack** (licensed or commissioned music, not ElevenLabs; separate decision); **crowd/foley** beds for the Ambience bus (stock SFX libraries or ElevenLabs Sound Effects through the same pipeline and disclosure).
