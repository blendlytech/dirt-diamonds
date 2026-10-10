# Audio, Voice & Cutscene Rules

Specs: `docs/design/voice_and_broadcast_audio.md` (Phase 14) and `docs/design/cutscenes_motion_comic.md` (Phase 16).

## Scope: broadcasters only (owner directive, 2026-10-10)

- ElevenLabs voice is **only** for `BroadcastBooth` and the announcers (56 College/Pro booths + 3 HS pool voices).
- **No** phone voice notes, **no** cutscene voice-over, **no** character, contact or NPC dialogue. Those are text: bubbles, captions, subtitles.
- A cutscene may use a **broadcaster call** as its audio bed (`announcer_of` + category, or an announcer-bank `clip`). Nothing else may carry a `voice` entry.
- Reject any `voice_note` field, any contact `voice_profile`, and any non-announcer voice bank or script sheet.

## ElevenLabs Voice Production

- **Offline only.** Voices are rendered with `Tools/VoiceGen` on a developer machine and shipped as Ogg Vorbis files listed in `Assets/Audio/Voice/voice_manifest.json`. No runtime TTS, no network calls from the game.
- **Secrets:** the API key is `ELEVENLABS_API_KEY`, read from the environment or, failing that, the repo-root `.env` (git-ignored, **never tracked**; check with `git ls-files .env`, which must print nothing). Never commit, log, print or embed it. A key in the repo is a release blocker, and a key that ever reached git history must be revoked in ElevenLabs.
- **Rights:** generate only on an ElevenLabs plan that grants commercial rights at generation time, and log every run in `docs/legal/elevenlabs_generation_log.md`. Use Voice Design (original) voices, or Voice Library voices whose terms allow commercial use.
- **No real people:** never clone or imitate a real person, athlete or broadcaster. All announcers are fictional personas.
- **No spoken names** of avatars or generated NPCs, and **no avatar pronouns** (the avatar can be male or female). Call by role and team; names go in subtitles. Only the Trailblazer route's own calls may gender the avatar.
- **Mastering:** −18 LUFS integrated, ≤ −1 dBTP, mono 44.1 kHz, silence trimmed. Cap: announcer ≤ 6 s.
- **Review:** every clip gets a human listening pass. Bad clips get their `take` number bumped in the script sheet and are re-rendered.

## Announcers

- One persona per College and Pro team (56) with an archetype and signature taglines, plus 3 shared HS radio-pool voices with school stingers.
- No archetype is used more than 5 times, and no two teams in a division share one.
- Announcers are broadcast-clean (no profanity or slurs). Clip categories must map to real at-bat beat signals; unsupported categories are cut, not faked.

## Cutscenes

- In-engine motion comics defined in JSON. Art is layered PNG at 1920×1080 base (~110% oversize for camera moves), made in the approved Phase 15 direction.
- Kinds and caps: story 15–60 s, milestone 8–30 s, achievement 3–5 s, big play 3–6 s. All skippable.
- Triggers come only from bus events or `BroadcastBooth.BigPlay`. Presentation only; never write state.

## Gates

- `validate_audio_manifest` after any voice asset, manifest or persona change.
- `Tools/CutsceneHarness` after any cutscene definition or art change.
- `check_event_graph_integrity` after any event that adds a `cutscene` reference (and #9 rejects any `voice_note`).
