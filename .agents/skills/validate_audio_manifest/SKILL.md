---
name: validate_audio_manifest
description: Validates the ElevenLabs voice manifest, announcer coverage, loudness and references before voice assets merge.
---
# validate_audio_manifest

**Status:** planned. The harness is built in Phase 14 slice 14b (`docs/design/voice_and_broadcast_audio.md` §10). Until it exists, this skill documents the contract.

**Purpose:** prove that every shipped voice clip is present, mastered, covered and referenced correctly, and that no secret leaked into the repo. Mandatory after any change to `Assets/Audio/Voice/`, announcer personas, voice profiles, voice-note references or cutscene VO.

**Execution (once built):**

```powershell
dotnet run --project Tools/AudioManifestHarness -c Release
```

**What it proves (exit code 0 = all pass):**

1. **Manifest integrity:** `Assets/Audio/Voice/voice_manifest.json` parses; `clip_id`s are unique; every `file` exists; no `.ogg` under `Assets/Audio/Voice/` is missing from the manifest.
2. **Announcer coverage:** every College and Pro team (56) has a persona and every clip category at ≥ its minimum count (spec §6); each signature tagline appears verbatim in its category; every HS radio-pool voice has all 24 school stingers.
3. **Durations:** announcer ≤ 6 s, voice note ≤ 25 s, cutscene VO ≤ its slot in the cutscene definition.
4. **Loudness:** measured integrated loudness within ±1 LU of −18 LUFS; true peak ≤ −1 dBTP.
5. **References:** every event `voice_note`, every cutscene `voice` clip, every `announcer_of` + category and every persona `voice_profile` resolves. Every `clip_id` has a strings-table subtitle.
6. **Persona rules:** no archetype used more than 5 times; no two teams in the same division share an archetype.
7. **Secrets:** no ElevenLabs API key pattern anywhere in the repo.
8. **Licensing log:** `docs/legal/elevenlabs_generation_log.md` has an entry covering every manifest `content_hash` generation batch.
