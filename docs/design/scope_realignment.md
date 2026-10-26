# Scope Realignment (SR): Pruning Manifest, Refactor Specs, Gate Sequence

**Status:** active spec, 2026-10-25. Source: [architectural_refactoring_scope_alignment_directive.md](../architectural_refactoring_scope_alignment_directive.md) (the owner's directive; read it for the *why*). **Pivots 2.1-2.4 are accepted by the owner (2026-10-25) and recorded in [GAME_VISION](../GAME_VISION.md) v3.0.** Everything marked **PROPOSED** below (gate order, features, resolutions of conflicts) needs the owner's yes, listed in §9.

**Current staffing (owner, 2026-10-25):** default outstanding authors, reviewers and merge gates use the GPT-primary roster in [TEAM.md](../TEAM.md) section 1a: **GPT 6.1 Sol** core/integration, **GPT 6 Astra** math/sim/review, **GPT 6 Sol** UI/presentation/content. Gemini Argon is an optional, limited-budget reviewer for focused packets, with no mandatory gate or implementation slice. This replaces the original Opus/Sonnet/Flash assignments without changing scope, pauses or owner gates.

**Core constraint (directive §0):** the C# backend stays as it is: zero-GC loops, pooled SQLite statements, assembly isolation, Monte Carlo harnesses. Cuts land on the front end, on micromanagement and on risky asset pipelines. No change here touches `AtBatResolver` or the resolver guard.

**Corrections to the directive:** the engine is **Godot 4 + C#**, not Unity (4.3's "recompiling Unity assets" means "recompiling the game"). v14 is a clean break, not a migration.

---

## 1. Decisions (accepted)

| Pivot | Decision | Replaces |
| :-- | :-- | :-- |
| **2.1** | The Markov `MicroGame` is the **authoritative attended game**. `Assets/Simulation/Arcade/` shrinks to an optional **Clutch Moment** timing resolver (offense only) plus unscored batting practice. No real-time fielding, baserunning or 9-inning physics, ever, in 1.0. | GAME_VISION v2.1/v2.2 pillar 2; GEMINI §11; arcade spec §12 slices G-2…G-5, G-9 |
| **2.2** | The player picks a **Weekly Lifestyle Stance**. The week then plays out, and the game stops only for stress spikes, funds at or below zero, or a gritty event (and the game itself). | GAME_VISION §5 "Daily: split 24 hours" |
| **2.3** | Interactive hustles drop from 4 to 2: **Narcotics** and **Texas Hold'em**. **Fencing** and **Robbery** become Burner Phone narrative events. | Four mini-game screens |
| **2.4** | **6 announcer voices**: 2 per tier (HS, College, Pro), with about 10x the situational lines per voice. | 56 team announcers + 3 HS voices |

**What does not change:** HS 7 innings / College 9 / Pro 9; the 3-tier league and its schedules; the secrecy, no-abortion, adults-only and identity-never-touches-the-sim rules; Pro's World Championship being the only reveal trigger.

---

## 2. Deprecation & Pruning Manifest

Legend: **KEEP** untouched · **MODIFY** · **FREEZE** (stays in the repo and in its harness, unreachable from the game) · **RETIRE** (UI removed from navigation now, file deleted later) · **DELETE** (needs the owner's per-file yes: GEMINI "ask first"). Nothing is deleted by this document.

### 2.1 Arcade engine (Pivot 2.1)

| Item | Action | Note |
| :-- | :-- | :-- |
| `Assets/Simulation/Arcade/*` (Vec3, ArcadeTuning, BallPhysics, PitchModel, ContactModel, BattedBallModel, ArcadeAtBat) | **KEEP** | The Clutch Moment's core (§3.1). `Tools/ArcadeHarness` stays the gate (55/55). |
| `Assets/UI/Arcade/*` (G-0b scene, `PrototypeSession`) | **MODIFY** later | Becomes the Clutch Moment overlay and the batting-practice scene (SR-3b). |
| arcade slices **G-2** (physics core + rules), **G-3** (AI + AI-vs-AI band calibration) | **CANCEL** | No AI-vs-AI arcade games exist any more, so there are no arcade bands to calibrate. |
| arcade slice **G-4** | **SHRINK** | Only: build `ClutchRequest` from `EffectiveRatings` (one modifier stack). |
| arcade slices **G-5, G-9** | **MERGE** into SR-3b | A clutch overlay and clutch-only assists, no difficulty-preset framework. |
| **G-6**, fielding, baserunning, watch-and-skip, arcade §7.7 field-side reveals | **CANCEL** | G-6 was already deferred; the rest never started. |
| **G-7** venue screens / **G-8** art for 68 teams | **PAUSE** | Only the venue(s) the HS slice needs. See Gate 3 and §9 D-7. |
| `Player_Fielding` (schema v14, in the 13-1 brief) | **KEEP** | The sim reads defensive position from it (GEMINI §11). `speed`/`arm` columns stay reserved, so 13-1 is not reopened. Verify against the 13-1 brief. |
| GEMINI.md §11 | **REWRITE** | Done with this document. |

### 2.2 Needs, schedule, clock (Pivot 2.2)

| Item | Action | Note |
| :-- | :-- | :-- |
| `NeedsEngine.cs`, `UtilityCalculator.cs`, `ActionCatalog.cs`, `SleepProfile.cs`, `PersonDrift.cs` | **KEEP, byte-identical** | The stance drives them; they are not replaced. `Tools/NeedsDecayHarness` (117 checks) is the regression anchor. |
| `DaySchedule.cs` | **KEEP as an internal struct** | Players stop filling it. A builder fills it (§3.2). |
| `LifeSimManager.cs` | **MODIFY** (additive) | New entry point `TickWeek`; the daily path stays. |
| `Assets/UI/ScheduleScreen.cs/.tscn` | **RETIRE** | Delete after SR-2 is accepted. |
| `Assets/UI/TimeControlBar.cs/.tscn` | **MODIFY** | Becomes the week-advance bar: "Advance to Friday", stops on interrupts. Hour pause/slow/fast leaves the core loop. |
| `Assets/UI/CalendarScreen.cs` | **KEEP** read-only | Shows games, not slots. |
| `TutorialOverlay.cs` steps that mention the schedule | **MODIFY** | Rewritten for the 15-minute loop (SR-4). |
| `GameManager.SubmitDaySchedule` flow | **MODIFY** | Replaced by `SubmitWeeklyStance` + `AdvanceWeek`. The old method stays for the harnesses. |
| New | `LifestyleStance.cs`, `StanceScheduleBuilder.cs`, `WeeklyUpkeep.cs`, `WeekInterrupt.cs`, `Assets/Data/Life/lifestyle_stances.json` | §3.2 |
| Schema | **no new table.** One new `Game_State` key (`avatar_stance`) | `Game_State` is key/value ("single-row-per-key"). Confirm with `validate_sqlite_schema`. |

### 2.3 Hustles (Pivot 2.3)

| Item | Action | Note |
| :-- | :-- | :-- |
| `NarcoticsHustle.cs`, `NarcoticsHustleScreen.*`, Hold'em (`Holdem*.cs`, `TexasHoldemTable.*`) | **KEEP**; polish after Gate 2 | "Turf Wars" is not a name in the code. It is the Narcotics territory loop (`SetControlsTurfFlag`). |
| `Assets/UI/FencingScreen.cs/.tscn`, `RobberyScreen.cs/.tscn` | **RETIRE** now; **DELETE** after parity | Remove the Fencing and Robbery buttons from `BurnerPhone.tscn` (`HustleButtonsRow`) and `OnFencingPressed` / `OnRobberyPressed`. |
| `FencingNegotiation.cs`, `RobberyHustle.cs`, `HustleService.Build/Apply{Fencing,Robbery}*` | **FREEZE** | Keep until SR-1b mines their payout and risk numbers for event probabilities. `HustleHarness` (95 checks, 138 Fencing/Robbery refs) stays green untouched. |
| `WorkActivity.Fencing/Robbery` enum members | **KEEP**, mark obsolete | Don't renumber (stored values). |
| `Assets/Economy/Hustles/NarcoticsTerritoryManager.cs`, `TexasHoldemLogic.cs` | **DELETE** (0-byte stubs) | Needs the owner's yes. |
| `hot_goods` flag chain | **KEEP** | Robbery's clean getaway arms it; Fencing's `FreshFromAJob` source consumes it. Events must keep the same chain (§3.3). |

### 2.4 Announcers (Pivot 2.4)

| Item | Action | Note |
| :-- | :-- | :-- |
| 14a buses + `VoiceDirector`, 14b `VoiceGen` + manifest harness | **KEEP**, paused | Tier-agnostic tech. |
| 14c "56 personas + ~5,600 script lines", 14f full generation | **REWRITE** | Nothing generated yet, so nothing is lost (no `Assets/Audio` exists). |
| `validate_audio_manifest` coverage rule | **MODIFY** | "100% of 6 voices x trigger catalog", not per team. |
| Voice spec | **REWRITE** at resume | See §3.4. |

---

## 3. System Refactoring Specs

### 3.1 Clutch Moment (Pivot 2.1)

Pure C#, no Godot, fixed step, seeded: the arcade engine's existing contract. It produces **one scalar**, a contact-quality bucket, never a play result.

```csharp
// Assets/Simulation/Arcade/ClutchMoment.cs
public enum ClutchQuality : byte { Whiff, Weak, Solid, Barrel }

public readonly record struct ClutchRequest(
    ulong Seed, byte Inning, byte Outs, sbyte ScoreDiff, byte BaseMask,
    float Leverage,                       // from the win-probability table (Math value)
    in EffectiveRatings Batter, in EffectiveRatings Pitcher);   // built once, shared stack

public readonly record struct ClutchResult(ClutchQuality Quality, short TimingErrorMs);

public static class ClutchResolver
{
    // Runs ArcadeAtBat over the recorded input stream. A skipped moment passes an empty stream.
    public static ClutchResult Resolve(in ClutchRequest r, ReadOnlySpan<InputSample> input);
}
```

**Feeding `MicroGame`** (additive, resolver-style): a new `BatterIntentKind.Clutch` carries the `ClutchQuality`. It tilts the location-conditioned outcome weights by a capped table, `ClutchTilt[quality]`, in the same idiom as the shipped intent tilts.

- **Neutral = byte-exact.** No clutch intent (skipped, autopilot, background games) means no tilt, so the macro and micro harnesses stay bit-identical.
- **Centered.** The expected tilt for a blind or ratings-average input is zero, and the best and worst play stay inside the O1 skill band (about +-.100 OPS around ratings). A player who skips is never worse off than average.
- **Triggers.** Attended games only, the avatar's plate appearances only, `Leverage >= threshold`, at most N per game (threshold, N and the tilt table are Math values, owner **GPT 6 Astra Pro**).
- **Batting practice** is a separate scene on the same core: unscored, writes nothing, never uses the sim's `RngState`.
- **Gates:** `ArcadeHarness` (core edits) and `run_monte_carlo_batch` (the new intent kind; neutral path byte-exact).

### 3.2 Weekly Lifestyle Stance (Pivot 2.2)

The existing hourly machinery does the work. The stance chooses *how it runs*, so every decay curve keeps its tested behaviour.

```csharp
// Assets/Simulation/Life/LifestyleStance.cs
public enum LifestyleStance : byte { CleanLiving, GrindMode, PartyLifestyle, LowProfile }

public readonly record struct StanceProfile(
    float UpkeepMultiplier,            // x the WealthTier base upkeep
    ActionWeights AutopilotBias,       // biases the existing free-hour utility tick
    float WeeklyStressDelta, float RecklessnessDrift, float DetectionRiskDrift,
    float TemptationEventWeight);      // exposed to events through flags, see below

public static class StanceScheduleBuilder
{
    // Calendar-fixed blocks (School, Practice, Game, Work) become a DaySchedule; the rest is free hours.
    public static DaySchedule Build(LifestyleStance s, DayOfWeek day, in TierCalendar cal, bool hasJob);
}

public enum WeekInterruptKind : byte { None, StressSpike, FundsDepleted, EventFired, GameDay }

public readonly record struct WeekResult(
    NeedsState EndNeeds, double FundsDelta, float StressDelta,
    WeekInterruptKind Interrupt, byte StoppedOnDay);

// LifeSimManager (additive; TickDay stays):
public WeekResult TickWeek(string playerId, LifestyleStance stance, in WeekContext ctx);
```

- **Loop:** `AdvanceWeek` runs the day tick up to 7 times. After each day it checks the interrupt rules, in order: gritty event with choices queued, stress at or above the threshold, funds at or below zero, game day. On a hit it stops and hands control to the UI. The attended game is always the week's climax, so the week ends there and the loop resumes after it.
- **Upkeep:** weekly cost is `base(WealthTier) x UpkeepMultiplier`, settled through the existing funds-delta path. When funds run short, nothing special happens: needs decay under the unchanged engine, and `FundsDepleted` fires.
- **Stance reaches content through flags, no new prerequisite kind.** `GameManager` writes `stance_clean | stance_grind | stance_party | stance_lowprofile` into `Entity_Flags` (one active at a time, engine-written like `ped_active`). Events gate on `FlagActive`, and temptation events can weight themselves by stance.
- **Phase L fit:** hangovers, crash days and rehab remain per-day `Active_Effects`/absences, driven by the same day tick. Nothing in the substance design needs hourly player input.
- **Placeholder stance table (every number is a Math value, owner GPT 6 Astra Pro, anchored on `NeedsDecayHarness`):**

| Stance | Upkeep x | Autopilot leans toward | Weekly side effects |
| :-- | :-- | :-- | :-- |
| Clean Living | 1.0 | Sleep, Hygiene, Fitness | stress down, detection risk down, temptation events x0.5 |
| Grind Mode | 0.9 | Practice, Work | stress up, sleep short, small development bonus |
| Party Lifestyle | 1.6 | Social, Hangout | recklessness up, scandal hooks, temptation events x2 |
| Low Profile | 0.8 | Home, Idle | detection risk drains, fame stalls, mild isolation stress |

- **Gates:** `NeedsDecayHarness` 117/117 unchanged; `CoreLoopHarness` 22/22; with no stance set, the old path is bit-identical; `GrittyEventsHarness` green (the flags are ordinary flags).

### 3.3 Fencing and Robbery as Burner Phone events (Pivot 2.3)

The event framework today has deterministic consequences and **no stat-driven outcome branches** (choices resolve by the player's pick; the only randomness is the autopilot draw). So "using existing skill checks" needs one **additive** feature: weighted outcomes per choice, hidden from the player (the project rule: at least 3 choices, hidden probabilities tied to stats).

```json
{ "id": "fence_the_watches", "label": "Move them through the pawn shop",
  "outcomes": [
    { "p": { "base": 0.55, "mods": [ { "field": "detection_risk", "per": -0.004 },
                                       { "flag": "hot_goods", "add": -0.15 } ],
             "min": 0.10, "max": 0.90 },
      "outcome": "The owner doesn't look up from the counter.",
      "consequences": [ { "type": "funds", "amount": 1800 } ] },
    { "p": "rest",
      "outcome": "Two men in the doorway. One of them is a cop.",
      "consequences": [ { "type": "set_flag", "flag": "fencing_sting" },
                        { "type": "detection_risk", "amount": 20 } ] } ] }
```

- `Choice.outcomes[]` is optional. A choice without it is byte-identical to today. Exactly one `"p": "rest"` branch, last. Probabilities are `clamp(base + sum(mods), min, max)` over the subject's fields and flags, drawn from the **sim RNG** that `EventConsequenceApplier` already owns, and never displayed.
- **Chain preserved:** `robbery_*` events arm `hot_goods` on a clean getaway; `fencing_*` events gate on `FlagActive("hot_goods")` and clear it; `robbery_bust` / `fencing_sting` keep their meanings.
- **Where:** extend `dirt_underworld_events.json` (74 KB, contact: the existing fence and crew contacts). Each of the two jobs becomes a 3-4 event arc with >= 3 choices per node.
- **Numbers:** SR-1b derives base probabilities and payouts from `FencingNegotiation` / `RobberyHustle` (the calibrated balance), so the economy doesn't drift.
- **Gates:** `GrittyEventsHarness` 363/363 + new checks for `outcomes[]` (sum, `rest`, determinism per seed); `check_event_graph_integrity` extended so every outcome branch's flag reads and writes are reachable and nothing dead-ends; `HustleHarness` 95/95 untouched.

### 3.4 Six announcers (Pivot 2.4)

- **Mapping:** every tier has exactly 2 conferences, so **one booth per conference** (6 booths). Your team's conference booth calls your games. Team identity is carried by the subtitle strip and the scoreboard, not by spoken team names (no per-team clips).
- **Depth instead of breadth:** the old plan budgeted ~5,600 script lines across 59 voices (~95 each). Keep roughly the same total over 6 voices, ~900 lines each: **about 10x per voice, no new generation budget**. The extra depth is situational variants, not new events.
- **Trigger context** (`BroadcastContext`, built from DTOs and bus events, presentation-only, no names, no pronouns):

| Tag | Source |
| :-- | :-- |
| recklessness band | `Players.recklessness` |
| rivalry intensity band | rivalry graph |
| flags: PED rumor, investigation, scandal, `stance_party` | `Entity_Flags` |
| slump / hot streak | recent-PA window |
| dynasty depth ("a second-generation player") | generation count, **adoptive family only** |

- **Secrecy:** `BroadcastContext` has no field that can carry the biological father or a hidden child. Before the reveal, no line may allude to either. After the reveal, lineage lines unlock (`flag: lineage_revealed`).
- The Trailblazer route's gated call set is unchanged.
- **Open:** is a "booth" one voice, or a play-by-play + colour pair (12 voices)? See §9 D-5.

---

## 4. Revised Gate Sequence (PROPOSED, directive §3)

| Gate | Exit | Includes | Pauses |
| :-- | :-- | :-- | :-- |
| **0** | Docs aligned (this) | GAME_VISION v3.0, GEMINI §11, BUILD_PLAN, TEAM | |
| **1** Schema & core freeze | 13-1…13-3 merged to `p13/integration` (HS 10-game Friday season playable); v15 **DDL only** (L-1 schema) lands | the hard freeze starts after v15 DDL: **no new tables or backend mechanics** until Gate 4 | 13-4…13-7 and L-3…L-8 runtime run only if they are inside the already-designed v14/v15 and don't block Gate 2 |
| **2** 15-minute loop | A cold boot reaches the HS backstory, a Burner Phone temptation, a stance choice, one Friday game and its consequences in <= 15 minutes, zero UI friction | SR-1, SR-2, SR-4; 15a (owner picks art direction) -> 15b -> 15c for **3 screens + stance UI** | Phases 14, 16 |
| **3** Visual identity lock | 1 golden slice at shipping quality: batter sheet, pitcher sheet, stadium backdrop, Burner Phone screen | 15d, minimal | batch crowds / sprites / `Tools/SpriteBake` runs; 68-venue work |
| **4** External playtest | HS slice (10-game season + recruiting offers + HS events) with a local session log | HS half of 13-6 (graduation -> recruiting) | |

After Gate 4: resume Phase 13's remainder, Phase L runtime, Phase 14 (6 voices), Phase 16, SR-5…SR-8, Phase 17.

**Proposed refinement of directive Gate 1 (D-2):** the directive says finish all of Phase 13 first. The 15-minute loop only needs 13-1…13-3, and Gate 4 only needs the HS half of 13-6. Finishing 13-4 (playoffs), 13-5 and the College/Pro halves first delays the demo by four slices for screens the demo never shows. Recommended: gate on 13-1…13-3; the rest continues in parallel and merges behind Gate 2.

**Why v15 DDL before the freeze (D-3):** the freeze forbids new tables, but Phase L's tables (`Biological_Parentage`, `Player_Identity`, `Active_Effects`, `Indiscretions`) are already designed and a pre-1.0 save is disposable. One more clean break now is cheaper than a migration after 1.0. The alternative is folding v15 into 13-1, which widens the critical path.

---

## 5. Gate 2: file-by-file roadmap

| # | Files | Change | Model | Reviewer |
| :-- | :-- | :-- | :-- | :-- |
| 1 | `Assets/Simulation/Life/{LifestyleStance,StanceScheduleBuilder,WeeklyUpkeep,WeekInterrupt}.cs`, `LifeSimManager.cs` (+`TickWeek`), `Assets/Data/Life/lifestyle_stances.json`, `Tools/NeedsDecayHarness` (+stance cases) | SR-2a engine (§3.2) | GPT 6.1 Sol | GPT 6 Astra (Math on values) |
| 2 | `Assets/Core/GameManager.cs` | `SubmitWeeklyStance`, `AdvanceWeek`, `WeekSummaryDto`, stance flag writer, `Game_State.avatar_stance` | GPT 6.1 Sol | GPT 6 Astra |
| 3 | `GrittyEventModel.cs`, `GrittyEventJson.cs`, `EventConsequenceApplier.cs`, `Tools/GrittyEventsHarness`, event-graph checker | `outcomes[]` (§3.3), SR-1a | GPT 6.1 Sol | GPT 6 Astra |
| 4 | `dirt_underworld_events.json` + `contacts.json` | Fencing/Robbery arcs, SR-1b (scripts GPT 6.1 Sol, JSON GPT 6 Sol) | GPT 6 Astra | GPT 6.1 Sol (quality) |
| 5 | `Assets/Data/...` demo content + `hs_onboarding_events.json` | One scripted week-2 Burner Phone temptation with >= 3 choices, hidden-probability branches, reactive texts | GPT 6.1 Sol -> GPT 6 Sol | GPT 6.1 Sol |
| 6 | `Assets/UI/BaseballDashboard.cs/.tscn` | Stance selector, weekly summary card, interrupt list, standings | GPT 6 Sol | GPT 6 Astra |
| 7 | `Assets/UI/TimeControlBar.*` | Week-advance bar | GPT 6 Sol | GPT 6 Astra |
| 8 | `Assets/UI/BurnerPhone.cs/.tscn` | Remove Fencing/Robbery buttons; keep threads, events feed, Narcotics + Hold'em hooks; restyle | GPT 6 Sol | GPT 6 Astra |
| 9 | `Assets/UI/AtBatView.cs/.tscn` | Restyle as the broadcast view; reserve the Clutch overlay slot | GPT 6 Sol | GPT 6 Astra |
| 10 | `Assets/UI/NewGameScreen.cs`, `Main.cs`, `TutorialOverlay.cs` | A fixed "HS demo career" entry and a 15-minute tutorial; backstory as captions + first texts (the prologue cutscene is paused) | GPT 6 Sol | GPT 6 Astra |
| 11 | `Assets/UI/ScheduleScreen.*` | Remove from navigation (retire) | GPT 6 Sol | GPT 6 Astra |
| 12 | `Assets/UI/Theme/*` (15b) | Design system scoped to the screens above | GPT 6 Sol | GPT 6 Astra |

Gate 2 needs **15a** first, and that is the owner's call (art direction A/B/C, see [review/visual_direction_3d_proposal.md](../review/visual_direction_3d_proposal.md) and the G-0c brief). It is the long pole.

**Regression envelope:** a Gate 2 diff may touch `Assets/Simulation/Life/` additively, `Assets/Narrative/` additively, and `Assets/UI/`. It must leave `Assets/Simulation/Baseball/` alone. Required green at every merge: Build 0/0, MonteCarlo 354/354 20-run byte-identical, GrittyEvents 363/363 (+new), NeedsDecay 117/117, CoreLoop 22/22, Hustle 95/95, SchemaValidator.

---

## 6. Commercial features (PROPOSED, directive §4)

| Feature | Spec | Constraint |
| :-- | :-- | :-- |
| **4.1 Tabloid / Rumor Mill** | A Dashboard widget and a Phone thread. Items are generated from `detection_risk` bands, `Entity_Flags` (PED rumor, investigation, scandal), faction heat and (after L-5) `Indiscretions`. Presentation-only, rate-limited, own RNG. | **Secrecy:** feed items are built from a DTO with **no parentage field** (only `LineageQueries` reads `Biological_Parentage`). Nothing about the father or a hidden child before the reveal. After the reveal, lineage scandals unlock. The directive's "how close they are to exposing the bloodline's hidden ledger" is delivered as *generic* creeping paranoia (a reporter, a rival, a syndicate), never a lineage tell. Interim version needs no v15 data. |
| **4.2 Rap Sheet exporter** | Retirement/succession screen -> `SubViewport` -> `Image.SavePng` to `user://exports/`, with a one-click copy path. Lines: career stats, titles, peak `WealthTier`, illicit earnings, children **already revealed** (e.g. "2 heirs"). | Presentation-only, never writes the DB. A hidden child not yet found is not counted (reveal always precedes succession, but the *search* may still be open). |
| **4.3 `mods/` loader** | Folder next to the exe (and Steam's Workshop folder via Facepunch UGC). `mods/<id>/mod.json` = `{ id, name, version, game_version, content: [...], overrides: [...] }`. Each file runs through the **same** loaders and validators as shipped content at boot. | A failing mod is rejected whole with a visible log line, never a crash. Events are additive only; replacing `schools.json` or rosters is explicit (`overrides`) and applies to **new careers only** (a save records the mod-set hash). Mod worlds sit outside the calibrated bands, so achievements are disabled while a mod is active. No code in mods. The event-graph checks must be liftable into a library (`EventGraphValidator`) so the boot-time loader runs the same checks as the harness. Workshop **upload** is the owner's Steamworks action. |

Order: 4.1 interim and 4.2 are GPT 6 Sol slices after Gate 4; 4.3 follows once the content schema has stopped moving (after Gate 4), as does the Workshop wiring.

---

## 7. Slices

| Slice | What | Author | Reviewer | Owner gate |
| :-- | :-- | :-- | :-- | :-- |
| **SR-0** | This doc, GAME_VISION v3.0, GEMINI §11, BUILD_PLAN, TEAM | Claude (this session) | owner | **Read GAME_VISION v3.0** |
| **SR-1a** | `outcomes[]` in the event framework | GPT 6.1 Sol | GPT 6 Astra | — |
| **SR-1b** | Fencing + Robbery arcs; mine hustle numbers | GPT 6.1 Sol -> GPT 6 Sol | GPT 6 Astra | Story sign-off |
| **SR-1c** | Retire the two screens and the two phone buttons; delete after parity | GPT 6 Sol | GPT 6 Astra | **Delete approval** |
| **SR-2a/b** | Stance engine; `GameManager` + stance UI | GPT 6.1 Sol / GPT 6 Sol | GPT 6 Astra (+ Math values) | Stance table sign-off |
| **SR-3a** | `ClutchResolver` + `BatterIntentKind.Clutch` + tilt (Math) | GPT 6 Astra | GPT 6.1 Sol | **Clutch feels right** |
| **SR-3b** | Clutch overlay + batting practice scene | GPT 6 Sol | GPT 6 Astra | Playtest |
| **SR-4** | Gate 2 screens (rows 6-12 of §5) | GPT 6 Sol | GPT 6 Astra | Screenshot review |
| **SR-5** | Golden vertical slice (Gate 3) | Owner + AI art tools; GPT 6 Sol integration | GPT 6.1 Sol | **Taste** |
| **SR-6** | `mods/` loader | GPT 6 Sol | GPT 6 Astra | — |
| **SR-7** | Tabloid feed | GPT 6 Sol (runtime + content) | GPT 6.1 Sol | — |
| **SR-8** | Rap Sheet exporter | GPT 6 Sol | GPT 6 Astra | — |
| **SR-9** | Six booths: persona framework, trigger catalog, script sheets | GPT 6.1 Sol -> GPT 6 Sol | GPT 6 Astra | **Approve taglines** |

Superseded by this spec (read through it): [GAME_VISION v2.x §3 pillar 2, §5, §10](../GAME_VISION.md) (already rewritten), GEMINI.md §11 (rewritten), [arcade_baseball_overhaul.md](arcade_baseball_overhaul.md) §9 and §12, [gameplay_core.md](gameplay_core.md) venue/arcade slices, [voice_and_broadcast_audio.md](voice_and_broadcast_audio.md) booth counts, TEAM.md rows G-2…G-9 and 14c/14f.

---

## 8. Risks

- **Stance feels flat.** Mitigation: stance flags feed events and the tabloid, so a choice has visible downstream consequences, not only meter changes.
- **Clutch Moment tilt gets gamed.** Mitigation: capped, centered, Math-gated; a skip is neutral.
- **`outcomes[]` breaks event content.** Mitigation: optional field; harness suite unchanged for old content.
- **Art direction stalls Gate 2.** It is on the owner's critical path. Everything else can proceed in parallel.
- **Next Fest timing.** Registration and review lead times on Steam are the owner's to confirm; build backwards from them.

## 9. Open decisions (owner)

1. **D-1** Confirm the PROPOSED parts: the gate order (§4), the features (§6) and the conflict resolutions (§1-§3). Your yes covered the four pivots only.
2. **D-2** Gate on 13-1…13-3 (recommended) or all of Phase 13 first?
3. **D-3** Freeze after a v15-DDL-only L-1 (recommended), or fold v15 into 13-1?
4. **D-4** Pitcher avatars: keep neutral pitching until after Gate 4 (recommended), or un-archive [pitching_mode.md](../archive/pitching_mode.md) rev 2 (a complete MicroGame pitching design with Math constants already exists)?
5. **D-5** A "booth" is one voice (6 total, recommended) or a play-by-play + colour pair (12)?
6. **D-6** Per-file approval to delete (after parity): `FencingScreen`, `RobberyScreen`, the two 0-byte stubs, `ScheduleScreen`.
7. **D-7** G-7 venue screens: pause beyond the HS slice's venue(s) (recommended)?
8. **D-8** Art direction A/B/C (16-bit, stylized 3D, realistic): the Gate 2 long pole.
9. **D-9** Next Fest target date, to back-plan Gates 2-4.
