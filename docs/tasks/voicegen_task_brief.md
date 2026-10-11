# Task Brief: Build `Tools/VoiceGen`, the Offline ElevenLabs Voice Generator

**For:** an outside coding model (GPT Astra 6) working in this repo with no prior context.
**Phase / slice:** Phase 14, slice 14b ([docs/design/voice_and_broadcast_audio.md](../design/voice_and_broadcast_audio.md) §2, §3.3).
**Reviewer:** Claude Opus 5.5, matching TEAM.md §2. Commit the slice on its own branch and hand off the commit; no push or merge without owner direction. No new production spend is authorized (R-1 Q15).

---

## 1. Context (read this first)

*Dirt & Diamonds* is a commercial Godot 4 + C# baseball and life-sim game heading to Steam. Every spoken line in the game is a **broadcaster** line (team play-by-play announcers and the HS radio pool; voice is broadcasters only as of 2026-10-10, with no voice notes or cutscene VO), **pre-generated offline** with the ElevenLabs text-to-speech API and shipped as Ogg Vorbis files. The shipped game never calls ElevenLabs.

Your job is the offline tool that does the generating: a standalone C# console app that reads **script sheets** (JSON lists of lines to speak), calls ElevenLabs only for lines that are new or changed, masters the audio with ffmpeg, writes `.ogg` files into the game's asset folder, and maintains a **manifest** the game and validators read.

Read before starting (short):

1. [docs/design/voice_and_broadcast_audio.md](../design/voice_and_broadcast_audio.md) §2 (pipeline), §3.3 (manifest), §10 (validation, for context only).
2. [.agents/rules/audio_voice_cutscenes.md](../../.agents/rules/audio_voice_cutscenes.md) (voice production rules).
3. [GEMINI.md](../../GEMINI.md) §5 (audio rules) and §3 (C# naming conventions).
4. Any existing harness for house style, e.g. `Tools/HustleHarness/Program.cs` and its `.csproj`.

---

## 2. Hard constraints

- **Touch only:** `Tools/VoiceGen/**` (new), `DirtAndDiamonds.sln` (add the project under the existing `Tools` solution folder), `.gitignore` (temp folders only), and the sample files listed in §8. **Do not touch** `Assets/` code, `Assets/Simulation/`, `DirtAndDiamonds.csproj`, other tools, or game scenes.
- **No secrets anywhere.** The API key comes from `ELEVENLABS_API_KEY`: first the process environment variable; if that's unset, the tool reads it from the **repo-root `.env` file** (git-ignored and untracked; the owner keeps the key there). Parse `.env` minimally: `KEY=value` lines, `#` comments, optional surrounding single or double quotes stripped, whitespace trimmed. Read only that one key and ignore the rest. Never write the key to disk, the manifest, logs or console output; never accept it as a CLI argument; never print `.env` contents; redact the key in any error or HTTP dump. If neither source has the key, a real `generate` exits 2 with a message naming both places to put it.
- **Never stage or commit `.env`**, and never create a `.env` in the repo yourself.
- **No network during self-test.** The self-test must pass on a machine with no API key and no internet.
- **Project settings:** `net8.0`, `LangVersion 12.0`, `Nullable enable`, `ImplicitUsings enable`, `OutputType Exe`, `RootNamespace DirtAndDiamonds.Tools.VoiceGen`, matching the other `Tools/*` projects.
- **Dependencies:** BCL only (`System.Net.Http`, `System.Text.Json`, `System.Security.Cryptography`, `System.Diagnostics.Process`). **No NuGet packages.** Audio processing shells out to `ffmpeg`. It's installed on the owner's machine (winget `Gyan.FFmpeg`, v9.x, on PATH); also allow an `--ffmpeg <path>` override.
- **Build clean:** `dotnet build DirtAndDiamonds.sln -c Release` with 0 warnings and 0 errors.
- **Determinism and safety:** writes are atomic (write to a temp file, then rename). An interrupted run must leave the manifest valid and resumable.

---

## 3. Inputs

### 3.1 Script sheets — `Tools/VoiceGen/Sheets/**/*.json`

One sheet per bank (one announcer, one contact, or one cutscene):

```json
{
  "bank": "announcer/pro_bay",
  "output_dir": "Assets/Audio/Voice/announcer/pro_bay",
  "default_voice_profile": "ray_calloway",
  "lines": [
    {
      "clip_id": "pro_bay_home_run_01",
      "category": "home_run",
      "text": "Tell the gulls to duck — that ball is GONE!",
      "voice_profile": null,
      "delivery": "Big, joyful, building to the last word.",
      "take": 1
    }
  ]
}
```

- `clip_id`: `^[a-z0-9_]+$`, unique **across all sheets**. It becomes the file name `<clip_id>.ogg`.
- `category`: a free string (the announcer categories are in spec §6). The tool only stores it.
- `text`: 1–500 characters after trimming. It's spoken verbatim and stored as the subtitle text.
- `voice_profile`: optional per-line override of `default_voice_profile`.
- `delivery`: an optional human note. **Not** sent to the API and **not** part of the hash.
- `take`: an integer ≥ 1, default 1. **Bumping `take` requests a retake.** It's part of the hash, so the line regenerates. (This replaces the `retake: bool` mentioned in spec §2. A counter is idempotent; a bool would regenerate on every run until someone cleared it.)

### 3.2 Voice profiles — `Tools/VoiceGen/voice_profiles.json`

```json
{
  "profiles": {
    "ray_calloway": {
      "elevenlabs_voice_id": "REPLACE_ME",
      "model_id": "REPLACE_ME",
      "voice_settings": { "stability": 0.45, "similarity_boost": 0.8, "style": 0.35, "use_speaker_boost": true },
      "origin": "voice_design",
      "notes": "Golden-age crooner, 60s, warm baritone. Original synthetic voice; not modeled on any real person."
    }
  }
}
```

- Voice ids aren't secret and are committed. A profile whose `elevenlabs_voice_id` or `model_id` is `REPLACE_ME` is valid for `--dry-run` and `--self-test`, but a real run must **refuse** to generate its lines, with a clear message.
- `origin` is `voice_design` or `voice_library`. It's recorded in the licensing log.

---

## 4. CLI

```text
dotnet run --project Tools/VoiceGen -c Release -- <command> [options]

commands:
  generate        Render new/changed lines (the default command)
  status          Print per-bank counts: up-to-date / new / changed / orphaned; no network, no writes
  self-test       Run the offline test suite (§7); exit 0 = all pass

options:
  --sheets <glob>        Default: Tools/VoiceGen/Sheets/**/*.json
  --only <clip_id>       Restrict to one clip (repeatable)
  --dry-run              Validate + plan + print the character count and estimated credits; no API calls, no writes
  --force                Regenerate even if the hash is unchanged (still respects --only / --sheets)
  --concurrency <n>      Parallel API requests, default 2, max 8
  --plan-tier "<name>"   REQUIRED for a real generate; the ElevenLabs plan name, written to the licensing log
  --prune                Delete .ogg files + manifest entries for clips no longer in any sheet (default: only report them)
  --ffmpeg <path>        Override the ffmpeg location
  --repo <path>          Repo root, if not run from inside the repo
```

Exit codes: `0` success; `1` some clips failed (others succeeded and were saved); `2` configuration/validation error (nothing generated); `3` quota or authentication failure (run stopped; progress so far saved).

---

## 5. Pipeline (per line)

1. **Validate everything first** (before any API call): sheets parse; `clip_id`s unique and well formed; every `voice_profile` exists; text lengths are legal; output dirs are under `Assets/Audio/Voice/`. Report **all** problems, then exit 2 if any.
2. **Hash:** `content_hash = SHA-256` over a canonical UTF-8 string of `text \n voice_id \n model_id \n canonical-JSON(voice_settings) \n take \n PIPELINE_VERSION`. `PIPELINE_VERSION` is a constant in code; bumping it forces a full re-master when the mastering chain changes. Lowercase hex.
3. **Skip** when the manifest already has this `clip_id` with the same `content_hash` **and** the `.ogg` exists (unless `--force`).
4. **Request** (ElevenLabs text-to-speech; **verify these details against current ElevenLabs API docs** and note any differences in your hand-back):
   - `POST https://api.elevenlabs.io/v1/text-to-speech/{voice_id}?output_format=mp3_44100_192` (if the plan allows, `pcm_44100` is acceptable as a lossless intermediate)
   - Headers: `xi-api-key: <from env>`, `Content-Type: application/json`, `Accept: audio/mpeg`
   - Body: `{ "text": ..., "model_id": ..., "voice_settings": {...}, "seed": <uint derived from content_hash> }`. The seed makes re-renders as repeatable as the API allows; a new `take` gives a new seed.
   - **Retries:** 429 and 5xx get exponential backoff with jitter (1 s, 2 s, 4 s, 8 s, 16 s; max 5 attempts), honoring `Retry-After` if present. 401, or any response indicating the quota is exhausted, means stop the whole run (exit 3) after saving progress. Other 4xx: mark that clip failed, continue.
   - Write the raw response to a temp folder (`Tools/VoiceGen/.work/`, git-ignored), never into `Assets/`.
5. **Master** with ffmpeg (two-pass loudness normalization):
   - Pass 1: trim leading/trailing silence (threshold about −50 dB, keep 40 ms pads) and measure with `loudnorm` (`print_format=json`).
   - Pass 2: apply `loudnorm` with the measured values, targeting **I = −18 LUFS, TP = −1 dBTP, LRA = 11**, downmix to mono, resample to 44.1 kHz, encode **Ogg Vorbis** (`libvorbis`, `-q:a 4`).
   - Measure the final file (integrated LUFS, true peak, duration in ms). If the result is off target by more than ±1 LU, retry mastering once, then mark the clip failed.
6. **Place:** atomic rename into `<output_dir>/<clip_id>.ogg`. Don't create Godot `.import` files; the Godot editor generates those on next open, and the repo commits them.
7. **Recover each clip:** persist a per-clip recovery record with hash and validated artifact metadata before final placement; atomically place the file, then atomically update the manifest for that clip. On restart reconcile journal/file/manifest state; completed valid clips must not trigger another paid request. Never wait 25 clips to make progress recoverable. Test interruption at each boundary (R-1 Q15).

After the run, **append** one entry to `docs/legal/elevenlabs_generation_log.md` (create it with a header if missing): UTC timestamp, `--plan-tier` value, model ids used, profile ids with their `origin`, number of clips generated, total characters sent, and the tool's `PIPELINE_VERSION`. **No key, no voice settings dump.** Dry runs and self-tests don't write the log.

Finish with a summary: generated / skipped / failed (with clip ids and reasons) / orphaned, plus characters sent.

---

## 6. Manifest — `Assets/Audio/Voice/voice_manifest.json`

```json
{
  "pipeline_version": 1,
  "generated_utc": "2026-10-10T18:22:05Z",
  "clips": [
    {
      "clip_id": "pro_bay_home_run_01",
      "bank": "announcer/pro_bay",
      "category": "home_run",
      "file": "res://Assets/Audio/Voice/announcer/pro_bay/pro_bay_home_run_01.ogg",
      "text": "Tell the gulls to duck — that ball is GONE!",
      "voice_profile": "ray_calloway",
      "model_id": "…",
      "take": 1,
      "duration_ms": 2840,
      "lufs": -18.2,
      "true_peak_db": -1.4,
      "content_hash": "9f2c…",
      "sheet": "Tools/VoiceGen/Sheets/announcer/pro_bay.json"
    }
  ]
}
```

Sort `clips` by `clip_id` and pretty-print with 2-space indentation, so diffs stay readable and stable. Entries for clips that fail keep their previous successful version, if any.

---

## 7. Self-test (`self-test` command): offline, harness style

Print one `PASS`/`FAIL` line per check and a **recap of every failed check name at the end**, and exit non-zero on any failure (match the other harnesses' output style). Use an `ITtsClient` interface with a **fake client** that returns a short generated tone (write a valid WAV in code), plus a fake clock/delay so retry tests run instantly. Required checks:

1. A valid sheet and profile set loads; the sample sheets in §8 load.
2. Validation catches, and reports **together**: duplicate `clip_id` across two sheets, a bad `clip_id` pattern, an unknown `voice_profile`, empty and over-500-character text, and an output dir outside `Assets/Audio/Voice/`.
3. The hash is stable across runs and ignores `delivery`; it changes when text, take, voice id, model, settings or `PIPELINE_VERSION` change.
4. Incremental: run twice and the second run makes zero client calls; edit one line's text and exactly one call is made; bump one `take` and exactly one call is made; `--force` + `--only` makes exactly one call.
5. Retries: fake 429 ×2 then success → generated; fake 500 ×5 → that clip fails and the others continue (exit 1); fake 401 → run stops with exit 3 and the manifest still holds earlier successes.
6. A `REPLACE_ME` profile is refused in a real run and allowed in a dry run.
7. **Secret hygiene:** with `ELEVENLABS_API_KEY` set to a sentinel value, run a fake generate; the sentinel appears in **no** file the tool wrote (manifest, log, work files) and **not** in captured console output. Repeat with the sentinel supplied only through a temp-dir `.env` (quoted with single quotes, with a comment line and an unrelated key present): the key resolves correctly, the environment variable wins when both are set, and the sentinel still never leaks. Never point this test at the real repo-root `.env`.
8. Atomicity: simulate a crash mid-run (throw after N clips); the manifest on disk parses and contains exactly the completed clips.
9. Manifest round-trip: write → read → identical; ordering is stable.
10. Orphans: a clip removed from its sheet is reported by `status`; `--prune` deletes its file and entry; without `--prune`, nothing is deleted.
11. **ffmpeg path (skipped with a `SKIP` line, not a failure, if ffmpeg isn't found):** master the fake tone; the output is a valid Ogg Vorbis file, mono, 44.1 kHz, with measured loudness within ±1 LU of −18 and true peak ≤ −1 dBTP.

All self-test file output goes to a temp directory, never into `Assets/`.

---

## 8. Deliverables

1. `Tools/VoiceGen/VoiceGen.csproj` + source (keep it small and readable: e.g. `Program.cs`, `Cli.cs`, `SheetModels.cs`, `Validator.cs`, `ContentHash.cs`, `ElevenLabsClient.cs` (implements `ITtsClient`), `Mastering.cs`, `Manifest.cs`, `GenerationLog.cs`, `SelfTest.cs`).
2. Project added to `DirtAndDiamonds.sln` under the `Tools` folder.
3. `.gitignore`: add `Tools/VoiceGen/.work/`.
4. `Tools/VoiceGen/voice_profiles.json` with the `ray_calloway` placeholder profile (§3.2).
5. **Pilot sheet** `Tools/VoiceGen/Sheets/announcer/pro_bay.json` with exactly these 20 lines (Bayport Barons, announcer Ray Calloway, persona in spec §5.1); use `clip_id` = `pro_bay_<category>_<nn>`:

   | category | text |
   | :-- | :-- |
   | game_open_home | Good evening from Lighthouse Field, where the harbor lights are on and the Barons are home. |
   | game_open_away | We're on the road tonight, friends, but the Barons brought the bay with them. |
   | late_tension | Two on, two out, late in this one. You can hear the whole park holding its breath. |
   | single | Lined into center — a base hit for the Barons. |
   | extra_base | Into the gap — a race for second! |
   | home_run | Tell the gulls to duck — that ball is GONE! |
   | home_run | High, deep, and over the wall. The Barons strike! |
   | home_run | Oh, that one's still climbing. Goodbye, baseball! |
   | grand_slam | Four runs home, and the whole bay's on its feet! |
   | walk_off | Goodnight, Bayport — the Barons walk it off! |
   | strikeout_ours | Sit down, friend. Sit right down. |
   | strikeout_theirs | Caught looking. That one will sting. |
   | routine_out | Bounced to short, and that'll do it. |
   | run_scores | Here comes the runner — and the Barons push one across! |
   | lead_change | And just like that, the Barons are back in front! |
   | opponent_big_hit | Well. That one hurt. That one hurt quite a bit. |
   | final_win | Barons win! Ring the harbor bell! |
   | final_loss | Not tonight, friends. We'll get 'em tomorrow. |
   | color | The fog's rolling in off the water — perfect night for the old ballpark. |
   | team_stinger | …and the Barons lead! |

6. `Tools/VoiceGen/README.md`: purpose, prerequisites (ffmpeg, env var, a commercial-rights ElevenLabs plan), commands with examples, how retakes work (`take`), and the secret-handling rules.
7. **Hand-back note** (in your final message, not a file): files changed, build and self-test output, any place you deviated from this brief and why, and anything you found different in the current ElevenLabs API docs.

---

## 9. Out of scope

- Running a real generation (the owner runs the pilot with their own key).
- `Tools/AudioManifestHarness` (a separate task), the game-side `VoiceDirector`, any Godot or scene code, subtitle strings tables.
- Writing the other 55 announcer sheets.

## 10. Definition of done

- `dotnet build DirtAndDiamonds.sln -c Release` gives 0 warnings, 0 errors.
- `dotnet run --project Tools/VoiceGen -c Release -- self-test` exits 0 with every check `PASS` (check 11 may `SKIP` only if ffmpeg is genuinely absent).
- `dotnet run --project Tools/VoiceGen -c Release -- generate --dry-run` validates the pilot sheet, prints the plan (20 new clips) and the character total, refuses nothing (it's a dry run), and writes nothing.
- `git status` shows changes only in the paths allowed by §2.

---

## After hand-back (owner's steps, not the coding model's)

1. Review the diff (Gemini 3.1 Pro).
2. In ElevenLabs, design the Ray Calloway voice (Voice Design, original), then put its voice id and the chosen model id into `voice_profiles.json`.
3. Keep `ELEVENLABS_API_KEY` in the repo-root `.env` (git-ignored and untracked as of 2026-10-10) or in a system environment variable. Never commit it.
4. Run `generate --plan-tier "<your plan>"` on the pilot sheet, listen to all 20 clips, bump `take` on any bad ones, re-run.
5. Commit the tool, the sheet, the 20 `.ogg` files, their Godot `.import` files (after opening the editor) and the manifest. Then move on to slice 14c (the other announcers) and the AudioManifestHarness.
