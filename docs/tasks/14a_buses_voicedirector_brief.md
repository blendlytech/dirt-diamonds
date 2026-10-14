# Task Brief: 14a — Audio buses, `VoiceDirector` and the subtitle strip

**Recommended model:** Claude Sonnet 5.5 (TEAM.md §2; reassigned from Gemini 3.1 Pro, unavailable from 2026-10-13, §1a)
**Reviewer:** Claude Opus 5.5
**Owner gate:** **ear check**: the owner listens to the ducking and the subtitle strip in a live boot
**Spec:** [voice_and_broadcast_audio.md](../design/voice_and_broadcast_audio.md) §3.1, §3.2, §11 (row 14a); GEMINI.md §1, §3, §5, §7
**Branch / worktree:** `p14/14a-sonnet` in your own worktree, e.g. `git worktree add ..\dnd-sonnet-14a -b p14/14a-sonnet origin/main`. Your sandbox needs write access to `C:\Users\DELL\dirt_and_diamonds\.git`, because worktree commits live there.

## 1. Context

Every game the avatar plays is called by their team's own announcer (GAME_VISION; voice is **broadcasters only**, owner 2026-10-10). 14a builds the **runtime plumbing** those calls run through: the bus layout with ducking, a `VoiceDirector` autoload that plays one booth clip at a time with priority, settings, and an on-screen subtitle strip. Real clips arrive with 14b (VoiceGen, GPT 6 Astra Pro, in parallel) and the call logic with 14d (`BroadcastBooth`). This slice proves the pipe with **placeholder synthesized clips**, the Slice D precedent `UiSfx` set.

## 2. Hard constraints

- **Allowed paths:** `default_bus_layout.tres`; `project.godot` (the autoload entry only); new `Assets/Audio/**` (`VoiceDirector`, `VoiceBank`, placeholder generation); the subtitle strip scene/script and its Theme hookup under `Assets/UI/**`; the phone Settings screen (sliders and toggles); `Tools/` only for a self-test if you add one; docs (`docs/progress.md`, a new `docs/reference/` note if useful). **Everything else is read-only.**
- **Presentation never drives simulation (GEMINI.md §1):** `VoiceDirector` consumes bus events and signals and emits nothing back into the sim. It never writes the DB or touches `RngState`; call-variant randomness uses its own presentation RNG.
- **No `Assets/Simulation/` changes.** `git diff --stat` must show no `Assets/Simulation/` path, so the MC and Gritty harnesses stay **inert** (TEAM.md §3 gates).
- **Voice is broadcasters only:** one channel, `Booth`. No voice-note, cutscene-VO or character-dialogue channel or bus. There is no `Voice` bus (GEMINI.md §5: Master → SFX / Announcer / Music / Ambience).
- **Zero allocation on the play path:** pooled `AudioStreamPlayer`s; no `new` and no string building per play request (GEMINI.md §3). Cache `GetNode<T>()` in `_Ready()`.
- **Theme only:** the subtitle strip takes its styling from `Assets/UI/Theme/`, never per-scene overrides; every new interactive control gets focus navigation (GEMINI.md §7).
- **No ElevenLabs, no network, no keys.** Never read or print `.env`.

## 3. Inputs

- Spec §3.1 (the buses, sidechain ducking ~8 dB Music and ~5 dB Ambience under `Announcer`, and the settings) and §3.2 (`VoiceDirector`: `Instance`, `ProcessMode.Always`, pooled players, one `Booth` channel, priority big-play/cutscene-bed > routine with a 150 ms fade, async `VoiceBank` loading with LRU, the `SubtitleRequested(speakerLabel, textKey, durationMs)` signal).
- `Assets/UI/UiSfx.cs` and its slider persistence in `user://settings.cfg`: copy this idiom exactly.
- The current `default_bus_layout.tres` (Master → SFX) and the `[autoload]` section of `project.godot` (GameManager, UiSfx).
- The manifest shape, spec §3.3. 14b writes it; for 14a, a hand-made placeholder manifest or bank is fine.

## 4. Outputs

1. `default_bus_layout.tres` with `Announcer`, `Music` and `Ambience` sending to Master (SFX unchanged), plus compressor sidechains on Music (~8 dB) and Ambience (~5 dB) keyed from `Announcer`.
2. `VoiceDirector` autoload (`Assets/Audio/VoiceDirector.cs`) per §3.2: `Play(clipRef, priority)`, an interrupt with a 150 ms fade, `Stop()`, the `SubtitleRequested` signal, and the bank preload/LRU API. It honors the Announcer on/off toggle and never blocks the main thread on load.
3. A small `VoiceBank` resource or loader (manifest-driven, so 14b's real manifest drops in without code changes).
4. Placeholder clips: synthesized tones or noise bursts of varied lengths, generated or committed under `Assets/Audio/Voice/placeholder/`, with the placeholder manifest entries they need. They must not look like real announcer clips to `validate_audio_manifest` (keep them in a clearly placeholder bank).
5. A subtitle strip (scene + script) that renders `SubtitleRequested`, fades after `durationMs`, and respects the Subtitles toggle (default **on**).
6. Phone → Settings: Master, Music, SFX and Announcer sliders; an Announcer on/off toggle; a Subtitles on/off toggle. All persist in `user://settings.cfg` like the shipped SFX slider.
7. A short reference note (or progress entry) on how 14d's `BroadcastBooth` should call `VoiceDirector`.

## 5. Tests and gates

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings, 0 errors |
| Inert sim | `git diff --stat main...HEAD -- Assets/Simulation` | empty |
| MC inert | `dotnet run --project Tools/MonteCarloHarness -c Release` | green, same count as `main` |
| Gritty inert | `dotnet run --project Tools/GrittyEventsHarness -c Release` | 363/363 |
| Scene paths | `godot_scene_mapper` / the Godot MCP on every `.tscn` you touch, before writing node paths | verified |
| Live boot | launch the game (the `launch-godot` skill) | boots clean; no errors in the log; a test hook plays a placeholder, the music ducks audibly, the subtitle shows, and a big-play request interrupts a routine one |
| Allocation | your choice: a profiler capture or a self-test around 100 warm `Play` calls | 0 B per warm play |
| Secrets | `git ls-files .env` | prints nothing |

## 6. Definition of done

- [ ] Buses and sidechains exist and duck as specified; SFX behaves as before.
- [ ] `VoiceDirector` is an autoload with the §3.2 API; one Booth channel; priority interrupt with the fade; async banks with LRU; zero allocation on warm play.
- [ ] The subtitle strip renders `SubtitleRequested` and honors its toggle.
- [ ] Settings sliders and toggles persist across restarts.
- [ ] All gates above pass; no `Assets/Simulation/` changes.
- [ ] Committed on `p14/14a-sonnet`, with one compact Session Log entry in `docs/progress.md`. **Then stop.**

## 7. Out of scope

`BroadcastBooth` and any call selection or game-event wiring (14d); `Tools/VoiceGen`, the real manifest and `AudioManifestHarness` (14b, GPT 6 Astra Pro); real announcer audio or any ElevenLabs use; music and ambience content (an owner decision); cutscene audio beds (16b); the visual redesign of the strip beyond Theme defaults (15b).

## 8. Handoff (required: end your session with this, addressed to the human)

What was done; the commit; each gate and its result; anything open (including what the owner should listen for in the ear check); and **"Recommended next: 14a review by Claude Opus 5.5, then the owner's ear check; 14d BroadcastBooth after 14b lands."**
