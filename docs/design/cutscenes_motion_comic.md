# Cutscenes — In-Engine Motion Comics for Story, Achievements and Big Plays (Phase 16)

**Owner:** Gemini 3.1 Pro (player, director, triggers, review) · Gemini 3.8 Flash (cutscene definitions, scripts, asset integration) · the user (story and art sign-off)
**Status:** design complete 2026-10-09; **amended 2026-10-10** (no character voice-over: captions only, plus broadcaster calls as audio beds); no code yet. **Authority:** [GAME_VISION.md](../GAME_VISION.md) §5. User decision, 2026-10-09: **in-engine motion comic** (not pre-rendered video).
**Depends on:** [visual_redesign.md](visual_redesign.md) (art direction, which must be approved first: cutscene art is made in the new style), [voice_and_broadcast_audio.md](voice_and_broadcast_audio.md) (`VoiceDirector` booth calls as audio beds; voice is broadcasters only), [league_restructure.md](league_restructure.md) §10 (milestone moments).

---

## 1. What a cutscene is here

A **motion comic**: layered 2D art staged in Godot and animated with camera pans, zooms, parallax depth, panel reveals, captions and simple layer motion (slide, fade, shake, flicker), with SFX, music and, where it fits, a **broadcaster call** on top. **No character voice-over** (2026-10-10): every line a character says is a caption. It is **not** frame-by-frame animation or video.

Why this format (decided):

- **Live slots.** It can show *your* player's portrait, name, number, team logo, team colors and stat line. Pre-rendered video can't.
- **Small and patchable.** Layered PNGs plus a definition file are tiny next to video, and a typo fix is a text edit.
- **On-style.** It uses the same art pipeline as the rest of the redesigned game.

---

## 2. Kinds and length caps

| Kind | When | Length | Skippable | Example |
| :-- | :-- | :-- | :-- | :-- |
| `story` | Authored narrative beats | 15–60 s | Yes (hold to skip) | The syndicate's first ask; the indictment; a child's birth |
| `milestone` | Career moments from the pipeline | 8–30 s | Yes | Signing Day, Draft Night, first Pro game, Championship |
| `achievement` | A Steam achievement unlocks | 3–5 s | Yes (tap) | "RAP SHEET" stamped over a mugshot |
| `big_play` | A ★ play in the avatar's live game | 3–6 s | Yes (tap) | A home run trot over the announcer's tagline |

Every cutscene can be skipped (the first view of a `story` cutscene included, after 0.5 s). Skipping jumps to the end state, so nothing is lost.

---

## 3. Runtime architecture

```text
 bus events (GrittyEventResolved, AvatarChanged, DraftCompleted, ChampionshipWon,
             AchievementUnlocked)                    BroadcastBooth.BigPlay (at-bat, presentation-local)
                     │                                          │
                     └──────────────► CutsceneDirector ◄────────┘
                                 (queue, rate limits, settings)
                                         │ CutsceneRequested(def, slotValues)
                                         ▼
                                  CutscenePlayer  (permanent Main.tscn sibling, top overlay)
                                  ├─ Stage (layers, camera)   ├─ Captions/subtitles
                                  └─ Audio cues → VoiceDirector (booth call bed), UiSfx, Music
```

- **`CutsceneDirector`** (`Assets/UI/Cutscenes/`, presentation layer) subscribes to bus events and the booth's `BigPlay`. It maps events to cutscene ids, fills slot values from DTOs, and enqueues.
- **`CutscenePlayer`** is a permanent, self-hiding sibling in `Main.tscn` (the shipped overlay idiom). It plays one definition at a time.
- **Clock and input:** while a cutscene plays, the real-time `GameClock` **soft-pauses** (the same mechanism hustle sessions use, [real_time_clock_slice_g.md](../reference/real_time_clock_slice_g.md)), and the at-bat sequencer treats a big-play cutscene as one more **beat**: the next `AwaitingInput` waits for it ([at_bat_presentation.md](../reference/at_bat_presentation.md) §3).
- **Boundary:** presentation only. Cutscenes never write the DB or sim state and never block the sim thread beyond the at-bat beat gate that already exists. **Calibration-inert.**

### 3.1 Queue and rate limits

- One cutscene at a time; queue depth 3 (lowest-priority items are dropped beyond that). Priority: story > milestone > achievement > big_play.
- **Big plays:** at most one per half-inning; at most 3 per game (walk-offs and championship-winning plays always play).
- **Achievements:** they play at the next calm moment (never mid-at-bat). Several unlocking together play back-to-back as one sequence.
- **Settings:** *Story cutscenes* On/Off (Off shows a still card with the caption text); *Big-play cutscenes* All / Key plays only / Off; *Achievement cutscenes* On/Off; *Reduce motion* (static panels, no camera moves or shake).

---

## 4. Definition format

One JSON file per cutscene under `Assets/Cutscenes/Definitions/` (content-team friendly, harness-validatable, diff-able):

```json
{
  "id": "ms_draft_night",
  "kind": "milestone",
  "max_duration_ms": 24000,
  "music_cue": "draft_night_swell",
  "slots": ["avatar_portrait", "avatar_name", "drafting_team_logo", "drafting_team_colors", "draft_round", "draft_pick"],
  "panels": [
    {
      "duration_ms": 6000,
      "layers": [
        { "art": "res://Assets/Cutscenes/Art/draft/bg_ballroom.png",   "depth": 0.2 },
        { "art": "res://Assets/Cutscenes/Art/draft/mid_crowd.png",     "depth": 0.5 },
        { "slot": "drafting_team_logo", "depth": 0.8, "anchor": "center", "enter": "fade_up" }
      ],
      "camera": { "from": { "zoom": 1.15, "x": -0.05 }, "to": { "zoom": 1.0, "x": 0.0 }, "ease": "out_cubic" },
      "captions":[ { "key": "ms_draft_night.c1", "at_ms": 400, "style": "lower_third" } ],
      "sfx":     [ { "sound": "crowd_murmur_swell", "at_ms": 0 } ]
    },
    {
      "duration_ms": 5000,
      "layers": [ { "slot": "avatar_portrait", "depth": 0.6, "frame": "polaroid", "enter": "slam" } ],
      "captions": [ { "text_slot": "avatar_name", "at_ms": 300, "style": "headline" },
                    { "key": "ms_draft_night.c2", "args": ["draft_round", "draft_pick"], "at_ms": 900 } ],
      "voice": [ { "announcer_of": "drafting_team", "category": "welcome", "at_ms": 1200 } ]
    }
  ]
}
```

- **Art** comes from `Assets/Cutscenes/Art/<set>/`: layered PNGs at 1920×1080 base (oversized ~110% for camera moves), 3–5 depth layers per panel.
- **Slots:** `avatar_portrait`, `avatar_name`, `avatar_number`, `avatar_position`, `team_logo`, `team_colors`, `opponent_logo`, `stat_line`, `score`, `season_year`, `draft_round`, `draft_pick`, `partner_portrait`, `child_portrait`, `father_portrait`, `father_name` (the lineage reveal, [temptation_and_lineage.md](temptation_and_lineage.md) §4.4), plus kind-specific slots. Slot values arrive as DTO fields, and missing values fall back to a generic silhouette or text.
- **Voice: broadcasters only (2026-10-10).** A `voice` entry is `announcer_of: <team slot>` + `category`, which resolves to that team's booth (e.g. the drafting team's welcome call), or a fixed `clip` **from an announcer bank**. No commissioner, coach, family or contact ever speaks; their lines are captions.
- **Captions** are strings-table keys (localizable). Broadcaster calls are always captioned when subtitles are on.

---

## 5. Trigger wiring

| Trigger | Source | Cutscene |
| :-- | :-- | :-- |
| Gritty event with `"cutscene": "<id>"` on the event or a choice | `GrittyEventResolvedEvent` (+ the new optional `cutscene` field in event JSON) | Story |
| Signing Day | `PipelineManager` → `RecruitingCommittedEvent` (new) | Milestone |
| Draft Night | `DraftCompletedEvent` (new; avatar drafted) | Milestone |
| First game at a new tier | `AvatarChangedEvent` + first attended game | Milestone |
| Championship (any tier) | `ChampionshipWonEvent` (new) | Milestone |
| Arrest / sentencing | `PlayerAbsenceChangedEvent` (Reason = Arrest, avatar) | Story |
| Child born / succession / lineage over | `ChildBornEvent`, `AvatarChangedEvent` (succession), `LineageOverReason` | Story |
| Achievement unlocked | `AchievementUnlockedEvent` (**new**, published by `AchievementManager` after `TrySetAchievement` succeeds, or locally when Steam is unavailable) | Achievement |
| ★ big play in a live game | `BroadcastBooth.BigPlay` | Big play |

New bus events are **additive, primitives-only Core events** (the house rule). Publishing them is the only change on the simulation side.

---

## 6. Launch content list

**Story (~25):** the opening **poverty prologue** (captions only, [16-1 brief](../tasks/16-1_opening_sequence_brief.md)), first day of high school, the Trailblazer's **tryout**, **"Could she be the first?"** and **the first** ([temptation_and_lineage.md](temptation_and_lineage.md) §8.2), the household, first hustle, the gambling debt (DIRT-1), the syndicate's ask (DIRT-2), the shaved runs, the league investigation, product and turf (DIRT-3), the indictment, going straight (DIRT-4), arrest/booking, a PED scandal breaks, proposal/wedding, a child's birth, **placing a child for adoption**, a hidden child revealed **across the diamond** and **in the clubhouse**, **the reclaim**, **the lineage reveal** (the father; uses `father_portrait`/`father_name`), rehab, an **overdose / death**, a career-ending injury, retirement, succession (the heir picks up the bat), lineage over (game over).

**Milestone (~10):** Signing Day, first college game, National Championship, declare for the draft, Draft Night, first Pro game, first Pro home run, playoff clinch, Pro Championship, Hall-of-legacy career retrospective.

**Achievement (1 per achievement):** a shared template (stamp/badge reveal over a themed background) plus per-achievement art and icon. Full list in [steam_commercial_release.md](steam_commercial_release.md) §3.

**Big play (8 kits, all team-tinted, tier-styled HS/College/Pro):** home run, grand slam, walk-off, strikeout to end the game, game-saving catch (only if the sim exposes the signal; otherwise cut), no-hitter final out, championship final out, career-first home run.

**Art sets:** about 20 story sets + 10 milestone sets + 8 big-play kits × 3 tier styles + an achievement template. All in the approved Phase 15 direction. AI-assisted art follows the same pre-generated, human-curated, disclosed rule.

---

## 7. Validation — `CutsceneHarness`

`Tools/CutsceneHarness` (headless, no Godot runtime needed for the data checks):

1. Every definition parses; ids are unique; `kind` is valid; the sum of panel durations ≤ `max_duration_ms` ≤ the kind's cap (§2).
2. Every `art` path exists; every `clip` exists in the voice manifest **and belongs to an announcer bank** (any non-broadcaster voice fails); every caption key exists in the strings table; `announcer_of` + `category` resolves for every College/Pro team.
3. Every `slot` used is in the definition's `slots` list and in the known slot vocabulary.
4. **Reachability:** every gritty-event `cutscene` reference resolves to a definition; every achievement id has an achievement cutscene; every definition has at least one trigger (no orphans).
5. Plus a live-boot smoke: play each definition headlessly through `CutscenePlayer` with dummy slot values; the error log stays empty.

`check_event_graph_integrity` gains check #10 (`cutscene` references resolve).

---

## 8. Sub-slices

| Slice | Content | Owner | Gate |
| :-- | :-- | :-- | :-- |
| **16a** | `CutscenePlayer` + definition loader + slots + captions + reduce-motion + skip; one placeholder cutscene | Pro | `CutsceneHarness`; live boot |
| **16b** | `CutsceneDirector` + new Core events (`AchievementUnlocked`, `DraftCompleted`, `RecruitingCommitted`, `ChampionshipWon`) + queue/rate limits + settings + clock soft-pause + at-bat beat integration | Pro | MC/Gritty inert (only additive event publishes); CoreLoop |
| **16c** | Big-play kits wired to `BroadcastBooth` | Flash → Pro review | Manual playtest: call and animation land together |
| **16d** | Achievement cards for the full achievement list | Flash | Harness reachability (#4) |
| **16e** | Story + milestone content batches (art + captions + definitions), in arc order | Flash → Pro review → user story sign-off | Harness + GrittyEvents + check #10 |
