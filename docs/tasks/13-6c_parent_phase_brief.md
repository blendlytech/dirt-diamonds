# Task Brief: `13-6c` / `13-6d` — the parent phase: three jobs and backyard training

**Author:** Claude Opus 5.5 (integration lead), 2026-11-27. **Status: PROPOSAL.** The owner decides D1–D11 (§3) after Gemini 4 Carbon's Math check (§9) and Opus's reconciliation. **Nothing is built from this brief until §3 records the owner's decisions.**

**Recommended split (D11-A):** **13-6c, engine:** Claude Opus 5.5 authors it and Gemini 4 Carbon reviews. **13-6d, phone and content:** Claude Sonnet 5.5 builds it and Gemini 4 Neon reviews (Carbon reviews any part that touches a sim, schema or persistence path). This mirrors 13-6a/13-6b. D11-B keeps the owner's 2026-11-24 default instead: Sonnet builds everything and Carbon reviews. **Math:** Gemini 4 Carbon (every magnitude in §4 is a placeholder until §9 signs it). **Owner gates:** D1–D11 before the build; a playtest after 13-6d. **Spec:** [league_restructure.md](../design/league_restructure.md) §9.4 (`:246`, D4b-C); [13-6 brief](13-6_pipeline_draft_brief.md) §3 (`:80–84`), §4 item 8 (`:235–246`), §7 (`:343–350`); [heir_mechanics.md](../reference/heir_mechanics.md) §3, §4.2, §6; [high_school_person_layer.md](../reference/high_school_person_layer.md) §7 (nurture); [scope_realignment.md](../design/scope_realignment.md) §3.2 (stance). **Base:** `ac1a055` (`main` = `p13/integration`, schema v14). Every citation below was verified at `ac1a055`.

## 0. Preconditions for the build sessions

1. Your own worktree (TEAM.md §3) from `origin/p13/integration` at or after the commit that records §3's decisions. Clean tree; the write check passes; at least 2 GB free on `C:`.
2. §3 records the owner's D1–D11, and §9/§10 (Carbon's Math check and its reconciliation) are signed.
3. Baseline on that head before any edit: build 0/0; MC default **623/623** with the normalized log SHA-256 **`c9f2caf8a972c0e64288c2fec9a1a28f0d6937cc3fba6eacab6a5cbf45c92aa2`** (the post-13-7 D7 anchor: CRLF→LF, `\d+(\.\d+)? ms` → `N ms`); CoreLoop 43/43; Gritty 363/363; NeedsDecay, Pipeline and Hustle harnesses green.
4. 13-6d only: run `godot_scene_mapper` (or the Godot MCP) on `Assets/UI/BurnerPhone.tscn` before writing any `GetNode<T>()`.

## 1. Context

The owner's D4b-C (2026-11-24): an undrafted College senior, or a Pro avatar the release order cuts, is **released**. With no children the game is over. With children, the avatar **picks one of three jobs**, each with its own benefits and pitfalls, and in free time **trains the children** in a mini-game that succeeds or fails, until a willing child takes over at 16 as an HS freshman. 13-6a built the hook only. This brief specifies the jobs and the training and puts them to the owner.

The parent phase is where the game's "legacy" pillar ([GAME_VISION.md](../GAME_VISION.md) §2 pillar 8, `:44`) stops being a stat blend and becomes play: you can no longer play, so you raise the one who will. It also has to stay a gritty life: money, stress, vice and the household keep running.

### 1.1 What exists (verified at `ac1a055`)

| Piece | Where | What it gives this slice |
| :-- | :-- | :-- |
| The release and the parent-phase state | `CareerManager.WriteReleaseStateInBatch` / `ApplyReleaseState` (`CareerManager.cs:1361–1386`); `Game_State.parent_phase_since_day` (`GameStateQueries.cs:111`); `InParentPhase` (`:319`), `ReadParentPhase` (`:828`) | The phase flag. Its presence is the state, so no DDL |
| The unrostered avatar | `IsAvatarRostered` (`:316`); `ReactivateAvatar` → `ActivateUnrostered` (`:1252–1269`, `:838–854`): every sim's attended claim cleared, `AvatarChangedEvent(id, 0)` | No game, no clutch; the Life sim keeps running |
| The takeover at 16 | `PipelineManager.RunRollover` (`takeoverPhase` `:1527`, release/game-over `:1781–1811`, writes `:1925–1936`); `EvaluateTakeover` (`CareerManager.cs:1411`), `ParkTakeoverChoice`, `WriteTakeoverInBatch` (`:1522`), `CompleteTakeover` (`:1556`), interactive `TakeOver` (`:1569`); `PipelineProfile.TakeoverAge = 16` (`PipelineRules.cs:160`); `family_hs_team_id` (`GameStateQueries.cs:118`) | The exit, built and harness-tested |
| The release text | `PipelineBeatWriter.OnReleased` (`PipelineBeatWriter.cs:131–138`); `pipeline.release.{college,pro}.parent_phase` (`strings.csv:21, 23`: "Time to focus on your family.") | The phase's opening beat |
| **Nurture axes** | `Child_Development` (`SchemaDefinitions.sql:511–527`): `care`, `coaching`, `funding` (0–100, neutral 50), `neglect` (from 0). `PersonQueries.AdjustChildAxis` (`:714`, clamped in SQL; axis order `:231–236`, coaching = 1) | **Training's natural output, with no DDL** |
| The nurture fold | `NurtureBlend` (`NurtureBlend.cs`): `PotentialDelta` = 8 × (0.5 × coaching + 0.3 × funding − 0.5 × neglect terms), capped ±8 (`:33–39, 56–64`); `InterestDelta` from care and neglect, capped ±20 (`:45`). Folded **once** at takeover (`WriteHeirHandoffInBatch`, `CareerManager.cs:1189–1235`), then the axes reset to neutral. Neutral axes = no row, byte-identical (the §7.3 identity) | A calibrated, capped, identity-safe channel already wired to the takeover |
| The weekly family tick | `ChildRearingService.ProcessDay` (`ChildRearingService.cs:97–150`): every 7 days, `care`/`funding`/`neglect` move from family hours and the funding commitment; child support is charged (`FundsImpulseEvent` after commit). **It never writes `coaching`**: only gritty events do (`EventConsequenceApplier.ApplyChildDevelopment`, `:492`) | Training can own `coaching` without fighting the tick |
| The phone's Family tab | `BurnerPhone.RefreshFamilyTab` / `BuildChildCard` (`BurnerPhone.cs:1603–1651`): per-child Care/Coaching/Funding/Neglect bars and the weekly funding slider, refreshed once per day | The parent phase's home screen already exists |
| Decision cards | 13-6b `BurnerPhone.Pipeline.cs`: the engine exposes `TryGetPending…` and `Resolve…`; the card shows a deadline; an unanswered choice resolves by the engine's rule; the clock never stops (`:1–30`, `:301–318`) | The job-choice card's pattern |
| Presence and the League tab | 13-7: `#Status_Family` with the `tier` key cleared (`RichPresenceWriter.cs`); `LeagueScreen` hidden while the tier is unknown (`LeagueScreen.cs:106, 120, 153`) | Already correct for the phase |
| Work and pay | `LegalWork` pays $25/hr (`ActionCatalog.cs:150`); `HustleWork` has the same needs drain and $0 pay (`:167`); `DaySchedule.WorkHours`/`FamilyHours`; `GameManager.GetMandatoryBlocksFor` forces School/Practice/Game (`GameManager.cs:1201–1229`), `SubmitDaySchedule` enforces them (`:1246–1279`) | A job is a forced Work block, like School |
| The stance seam | `StanceScheduleBuilder.Build(…, bool hasJob)` ([scope_realignment.md](../design/scope_realignment.md) `:130`, SR-2a, not built) | Jobs plug into the stance engine later through `hasJob` |
| Seeded resolvers | Hustles: pure Layer 1 in `Assets/Simulation/Hustles/`, Layer 2 `HustleService` applies results in its own batch with its own RNG, seeded off the boot chain (`GameManager.cs:709–711`) | The resolver/applier split. A new boot draw would shift every later consumer (`GameManager.cs:597–608`), so the training seed must not take one |
| Events for an unrostered subject | The poll reads a `NULL` team as tier **−1** (`NarrativePollQueries.cs:103`), which defeats every shipped `>=`/`==` tier gate (`GrittyEventModel.cs:92–100`) | Today the parent sees only tier-free events; job content must gate on flags |
| Secrecy | A hidden child has **no `Child` edge** ([temptation_and_lineage.md](../design/temptation_and_lineage.md) `:121, :216`); every pool goes through `LineageQueries.IsKin` (`:288`) | Training lists `Child` edges only, so it is blind to hidden children by construction |
| Current ratings of a child | Born at potential − `ProspectDiscount` (20) × youth weight (`DevelopmentManager.cs:78, 222`; `CareerManager.cs:743–758`). "Unrostered people never develop" (`DevelopmentManager.cs:284`); growth closes `GrowthRate` 0.5 of the gap a season once rostered (`:36`) | A potential bump becomes visible through the heir's first seasons |
| Pacing | `GameClock.MinutesPerRealSecond`: Faster = 120 game-min/s, so one day takes 12 s and a season about 73 min (`GameClock.cs:55–61`); "traversing seasons is Skip Day's job" | **A 12-year phase is about 4,000 Skip Days.** D6 is not optional |

### 1.2 What's missing

1. No jobs: no choice, no wage, no schedule, no consequences.
2. No training: nothing a player does moves `coaching`, and nothing shows a child getting better.
3. No pacing: an undrafted senior is about 22. With a child born in HS or College, the child is about 0–5 at the release, so the phase can run 11–16 seasons with no baseball.
4. No content: at tier −1, almost no gritty event fires, so the phase is quiet.
5. Two 13-6 §7 carry-overs: retirement-age succession still requires 19 (`HeirGeneticsProfile.MaturityAge`, `HeirGenetics.cs:61`; `EvaluateSuccession`, `CareerManager.cs:958–1029`), so a 42-year-old with a 15-year-old willing child gets `NoPlayableHeir` game over; and `banned_for_life` (`PipelineRules.cs:18`) has no pipeline effect (`PipelineManager.cs:348`).
6. [GAME_VISION.md](../GAME_VISION.md) `:32` still says only "any willing child aged 19+" succeeds. The takeover at 16 isn't in the vision doc.

## 2. Hard constraints

- **Allowed paths (per sub-slice; everything else is read-only):**
  - **13-6c (engine, Opus):**
    - new `Assets/Simulation/Baseball/ChildTraining.cs` (pure rules: ints and `ChildDevelopmentRow`, no DB, no Godot; the `NurtureBlend` profile);
    - new `Assets/Economy/Family/ParentJobService.cs` and `ChildTrainingService.cs` (Layer 2: batches, axis writes, wage and stress impulses after commit);
    - new `Assets/Data/Family/parent_jobs.json` and its loader (job profiles as data);
    - `Assets/Data/Database/GameStateQueries.cs` (new keys only);
    - `Assets/Core/GameManager.cs` (wiring, the job's forced block, intent APIs, the D6 fast-forward), `Assets/Core/CoreEvents.cs` (only if D9 adds an event);
    - `Assets/Simulation/Baseball/CareerManager.cs` **only** for D7-A;
    - `Tools/CoreLoopHarness`, `Tools/MonteCarloHarness`, `Tools/PipelineHarness` (new checks);
    - docs: this brief, `progress.md`, spec §9.4, heir_mechanics §6, GAME_VISION `:32`.
  - **13-6d (phone and content, Sonnet):**
    - `Assets/UI/BurnerPhone*.cs` / `.tscn` (the job card and the Family tab's training UI);
    - a new drill scene (`Assets/UI/BackyardDrill.tscn/.cs`);
    - `Assets/UI/Theme/` (variations only);
    - `Assets/Localization/strings.csv` (`ui.family.*`, `ui.job.*`, `family.*` keys; re-import `strings.en.translation` and keep only the translation);
    - new `Assets/Narrative/Events/Content/parent_phase_events.json` (D10);
    - `Tools/MonteCarloHarness` `ui-strings` extension;
    - docs: this brief and `progress.md`.
- **No DDL under D9-A.** v14 stays; the new state is `Game_State` keys, existing `Child_Development` axes and engine-written `Entity_Flags`. Validate every key and column against a **scratch copy** (`validate_sqlite_schema`), never the live save. A D9-B table waits for the v15 freeze gate with L-1.
- **Calibration (GEMINI.md §10):** every magnitude is a Gemini 4 Carbon Math value. With **no job, no training session and no parent phase**, every path is bit-identical. The guard world has no avatar, so MC default stays byte-identical outside the lines the slice adds (reconstruction recipe from `c9f2caf8…`). Pro guard PA 81433 / H 18471 / ER 8803, ledger `932ACE26…8B84`. `ActionCatalog`, `NeedsEngine`, `UtilityCalculator` and `NurtureBlend`'s constants stay byte-identical (NeedsDecay 117/117) unless §9 changes a `NurtureProfile` value with an explicit sanction.
- **Boundaries (GEMINI.md §1):** the job is Life/Economy-side (schedule, wage, stress, flags), and training is a pure rules file on the Baseball side applied by an Economy service, exactly like `ChildRearingService` → `Child_Development` → `NurtureBlend`. Life and Baseball exchange nothing but the bus and the DB; the CoreLoop boundary scan stays clean.
- **Presentation never drives the sim** and never uses the sim's `RngState`. The drill scene renders, has its own RNG for its visuals, and sends one intent: a session with a drill score in [0, 1]. The resolver alone draws the result. Skipping the drill or autopilot sends the neutral score (no tilt). This is the Clutch Moment rule (GEMINI.md §11), applied here.
- **No boot draw.** The training seed is a `Game_State` key written once on first use (from the tick count, the hustle precedent); every roll derives from it by (day, child id, session index). A reload replays the same result, so saving and reloading can't reroll a failure.
- **Secrecy (GEMINI.md §10):** the trainable list is `PlayerQueries.LoadChildrenOf` over `Child` edges: born, adopted in, or reclaimed (L-6, when built). Never a `Biological_Parentage` read. No training UI, text or count may hint at a hidden child.
- **Content lines:** no avatar pronouns (the avatar can be any gender); names in text only. Nothing explicit. Temptation or substance **boosts** stay out of this slice: the adults-only loader guard keys on tier ≥ College and the parent reads tier −1 (Phase L owns that guard). A child is never harmed in play: a failed hard session costs `care` (burnout), never an injury.
- **Text:** every player-facing string is a strings-table key; phone texts resolve `{tokens}` at write time.
- **Styling and focus:** Theme family only; focus navigation on every new control (GEMINI.md §7). The drill is playable by keyboard/controller and honors reduce-motion.
- **Must not block Gate 2.** 13-6c's `GameManager` edits are additive and stay out of SR-2a's `SubmitWeeklyStance`/`AdvanceWeek` names. D6's fast-forward is written so that SR-2a's `AdvanceWeek` can call the same interrupt rules (§4 item 6).

## 3. Owner decisions (D1–D11)

Recommendations first. **Status: OPEN.** Every number below is a placeholder that Carbon's §9 replaces.

**D1 — The three jobs.**
- **A (recommended): Coach, Steady, Nightlife** (profiles in §4.1).
  - **Coach** (a youth and HS assistant coach): little money, the most training and the best drills.
  - **Steady** (shift work): the most money, the least time and the most fatigue.
  - **Nightlife** (a host at a sports bar that trades on your name): good, uneven money and free days, with nights of vice and absence.
- B: Coach, Steady, and **Own business** (a batting cage: capital up front, uneven income, debt risk) in place of Nightlife.
- C: two jobs and "between jobs" (no wage, all the time in the world, funds pressure).

**D2 — Choosing and changing the job.**
- **A (recommended):** a phone decision card at the release (the 13-6b pattern), with a 14-day deadline. Unanswered, it resolves to **Steady**. The card returns at every season rollover in the phase, so the job can change once a year.
- B: chosen once, for the whole phase.
- C: changeable any week.

**D3 — Job, schedule and stance.**
- **A (recommended):**
  - The job is a **forced Work block** on its days, added to `GetMandatoryBlocksFor` like School. Its hours tick the existing `HustleWork` drain ($0), so `ActionCatalog` stays byte-identical. `ParentJobService` settles the wage, stress and flags **weekly**, on the `ChildRearingService` cadence, after commit.
  - The non-job days keep the shipped schedule, including the hustles.
  - When SR-2a lands, the same profile feeds `StanceScheduleBuilder.Build(…, hasJob: true)`, and the stance multiplies upkeep on top. Grind + Steady compound fatigue; Party + Nightlife compound vice.
- B: wait for SR-2a and build the jobs on the stance engine only.

**D4 — The mini-game ("Backyard Drills").**
- **A (recommended): a short timing drill.**
  - Three reps on a moving meter: hitting (a front-toss timing window), pitching (a release window) or fielding (a glove window), by the child's role.
  - The drill's score in [0, 1] becomes a **capped, centered tilt** on a seeded success roll. 0.5 is no tilt; the cap is ±15 percentage points (placeholder). Skipping the drill and autopilot both play at 0.5.
  - About 20 seconds; skippable; reduce-motion swaps the meter for a three-choice prompt with the same scoring.
- B: choice only (focus + intensity), no skill input.
- C: wait for SR-3b's batting practice and reuse it (couples D4b to Gate 2 work).

**D5 — What training changes.**
- **A (recommended): the existing nurture axes only.**
  - Success raises `coaching`, plus `care` on Easy and Normal (time together). A failed **Hard** session lowers `care` (pushed too hard).
  - At takeover the shipped `NurtureBlend` folds them: coaching moves potential (max +4 a rating from coaching alone, +6 with funding; the cap is ±8), and care moves interest (cap ±20). That means a trained child is likelier to be **willing** as well as better.
  - No DDL, the identity is already proven, and the fold is already harness-tested.
- B: A plus a **readiness** head start: a share of each success also closes the child's current-to-potential gap at the takeover, so the 16-year-old plays better on day one. That needs a new column or table (v15 DDL with L-1) and new Math.

**D6 — Cadence and pacing.**
- **A (recommended):**
  - **The session budget:** a weekly budget set by the job (Coach 3, Steady 2, Nightlife 3), shared by every trainable child (ages 5–15, unrostered, a `Child` edge).
  - **The standing plan:** a per-child focus and intensity (a household setting like the funding slider). It **auto-resolves any unplayed session at week's end at neutral tilt**, so playing the drill is a bonus, never a chore.
  - **Fast-forward:** in the parent phase, two controls, **"Next week"** and **"Next season"**, run the day tick until the target or the first interrupt:
    - a gritty event with choices;
    - stress at or above the threshold;
    - funds at or below 0;
    - a decision card (the job, the takeover);
    - a child's birthday that opens or closes training (ages 5 and 16).
- B: "Next week" only.
- C: no fast-forward (Skip Day only). Not viable: about 4,000 presses for a 12-year phase.

**D7 — Retirement-age succession (13-6 §7 carry-over).**
- **A (recommended):** a rostered avatar who retires (age 42 or the health floor) with **no** eligible 19+ heir, but with at least one child under 19, **enters the parent phase** instead of `NoPlayableHeir` game over. The takeover at 16 (or the shipped 19+ succession, if a child is already of age and willing) follows. This is heir_mechanics §6's foreseen "wait for the kid to grow up" mode. `NoPlayableHeir` stays in the enum for saves and fixtures. It's a `CareerManager` change, so Opus authors it in 13-6c and Carbon reviews it.
- B: status quo (`NoPlayableHeir` ends the game).

**D8 — `banned_for_life`.**
- **A (recommended): no forced release.** `dirt_permanent_ban`'s own outcome has the lawyer cut it to a 60-day suspension (`dirt_underworld_events.json:545–571`); the flag is "the mark on your name". In the parent phase the flag **bars the Coach job** (`dirt_blacklisted`, `:573`: "the doors that used to open for you don't anymore").
- B: the flag forces the release path at the next rollover.

**D9 — Persistence.**
- **A (recommended): no DDL.** New `Game_State` keys:
  - `parent_job` (text id);
  - `parent_job_since_day`;
  - `parent_job_pending_since_day` (the card);
  - `training_seed`;
  - `training_week_start_day`;
  - `training_sessions_used`;
  - the per-child standing plan as one key per child (`training_plan:<childId>`, text `focus/intensity`) **or** packed in one text key.

  Outcomes go to `Child_Development` (existing). Engine-written `Entity_Flags`: `job_coach | job_steady | job_nightlife` and `parent_phase` (the stance-flag precedent), for content gating. No new bus event; the wage and stress ride `FundsImpulseEvent`/`StressImpulseEvent`.
- B: new tables (`Parent_Job`, `Child_Training_Plan`, `Child_Training_Log`) in v15 with L-1, for history and achievements.

**D10 — Content for a tier −1 parent.**
- **A (recommended):** a starter batch of about 9 events, 3 per job, **tier-free** and gated on the job flags and `parent_phase`. Coach: "daddy-ball" whispers, a parent confrontation, a recruiter sniffing around your kid. Steady: overtime, a layoff scare, an injury on the line. Nightlife: an old teammate at the bar, a bar fight, the pull of the back room. Plus 2 family events gated on `parent_phase` (missed recital, kid asks why you stopped playing).
  - Phone texts use `family_mom` and the existing contacts.
  - The event graph is checked. No content changes the tier sentinel.
- B: as A, plus remap tier −1 to "adult" in `ConditionEvaluator` so tier-gated adult content reaches the parent. That changes every event's reach; not recommended in this slice.

**D11 — Slicing and ownership.**
- **A (recommended):**
  - **13-6c engine (Opus, Carbon reviews):**
    - `ChildTraining` rules;
    - the two services;
    - the job data;
    - the `GameManager` wiring (forced block, intents, fast-forward, weekly hooks);
    - D7;
    - persistence;
    - the harness checks.
  - **13-6d phone and content (Sonnet; Neon reviews the UI, Carbon reviews the content's flag gates):**
    - the job card;
    - the Family tab's training UI;
    - the drill scene;
    - the texts;
    - the D10 events.

  13-6d starts after 13-6c merges. The same shape as 13-6a/13-6b.
- B: the owner's 2026-11-24 default: Sonnet builds all of it from this brief; Carbon reviews the sim/persistence parts, Neon the UI; Opus integrates.

**Proposed TEAM.md §2 rows** (Opus adds them once D11 is decided):

| Slice | Author | Reviewer | Owner gate |
| :-- | :-- | :-- | :-- |
| **13-6c** parent-phase engine (jobs, training rules, pacing, D7) | Claude Opus 5.5 (+ Gemini 4 Carbon: Math, §9) | Gemini 4 Carbon | D1–D11 |
| **13-6d** parent-phase phone, drill and content | Claude Sonnet 5.5 | Gemini 4 Neon (UI); Gemini 4 Carbon (content gates) | Playtest |

## 4. Outputs

### 4.1 Job profiles (D1-A; data in `parent_jobs.json`; every number a Carbon placeholder)

| Job | Days and hours | Wage | Sessions/week | Drill odds | Weekly side effects | Flag | Gate |
| :-- | :-- | :-- | :-- | :-- | :-- | :-- | :-- |
| **Coach** | Mon–Fri, 4 h (afternoon) | $15/h → $300 | 3 | +10 pp (you know how to teach) | stress −2; `care` +1 a week (the kid comes to practice) | `job_coach` | not `banned_for_life` (D8-A) |
| **Steady** | Mon–Fri, 8 h | $25/h → $1,000 (the `LegalWork` rate) | 2 | −5 pp (tired) | stress +3; the full Work-block needs drain | `job_steady` | none |
| **Nightlife** | Thu–Sun, 6 h (nights) | $600 + a seeded $0–900 tip roll | 3 | −5 pp the morning after a shift | recklessness +1; stress −1; `care` −1 a week unless family hours ≥ 3 | `job_nightlife` | none |

Reference: weekly child support is $20 a child, plus any funding commitment (`ChildRearingService.cs:37`).

### 4.2 Training rules (D4–D6-A; `ChildTraining.cs`, pure)

- **Trainable:** a `Child` edge of the avatar, age 5–15, unrostered.
- **Budget:** sessions a week = the job's count; one session trains one child.
- **Odds:** `p = clamp(base(intensity) + jobBonus + expertise(parent) + tilt(score) − fatigue(stress), 5%, 95%)`.
  - `base`: Easy 80%, Normal 60%, Hard 40%.
  - `expertise`: 0 to +10 pp from the parent's own role-rating sum. A former Pro teaches better.
  - `tilt`: ±15 pp, linear and centered on a score of 0.5.
  - `fatigue`: 0 to −10 pp above stress 60.
- **Outcome:**

  | Intensity | Success | Failure |
  | :-- | :-- | :-- |
  | Easy | `coaching` +1, `care` +1 | nothing |
  | Normal | `coaching` +2, `care` +1 | nothing |
  | Hard | `coaching` +4 | `care` −2 |

  Gains scale by `(100 − coaching) / 50`, clamped to [0, 1] (diminishing returns). **Carbon decides** whether `coaching` also decays toward 50 in a week with no session (skills fade), and the per-season cap. At these placeholders, Normal training saturates `coaching` within about a season, so a cap or the decay is required.
- **Roll:** `PipelineRules.Stream`-style: `RngState(training_seed ⊕ hash(childId) ⊕ day ⊕ sessionIndex)`, one draw per session.
- **Autopilot:** the standing plan at a score of 0.5; the plan's default is Normal, the child's role.

### 4.3 The engine (13-6c)

1. **`ParentJobService`** (Economy/Family):
   - It reads the job profile.
   - Each week it settles the wage, stress and side effects in one batch, then publishes `FundsImpulseEvent`/`StressImpulseEvent`.
   - It keeps the `job_*` flags and `parent_phase` (set at the release, cleared at the takeover).
   - The 13-6b-style API: `TryGetPendingJobChoice` / `ResolveJobChoice(jobId)`. The deadline resolves to Steady (D2-A).
2. **`ChildTrainingService`** (Economy/Family):
   - The weekly budget, plus `PlayDrill(childId, score)`, which spends one session and resolves it now, and `SetPlan(childId, focus, intensity)`.
   - At week's end, the plans auto-resolve the unplayed sessions (D6-A).
   - Axis writes go through `PersonQueries.AdjustChildAxis`, one batch per resolution.
   - `TryGetLastResult(childId)` for the UI (in-memory; the text log persists it).
3. **`GameManager`:**
   - The job's hours go into `GetMandatoryBlocksFor` (as read-only rows on the schedule) while the phase is on.
   - Wiring is subscribed after `ChildRearingService`, so its week reads the same week.
   - Intent pass-throughs.
   - **D6 fast-forward:** `AdvanceUntil(targetDay)`, which stops on §3 D6's interrupts and returns a `WeekSummaryDto`-shaped result. SR-2a reuses the interrupt rules.
4. **D7-A:** `CareerManager.EvaluateSuccession`'s `NoPlayableHeir` branch routes to `WriteReleaseStateInBatch(hasChildren: true)` semantics at the retirement rollover. The avatar is unrostered and `pipeline_retired`; the takeover at 16 follows.
5. **Docs:**
   - league_restructure §9.4 (`:246`) and §16 (a new I-row);
   - heir_mechanics §6 (`NoPlayableHeir` → parent phase);
   - GAME_VISION `:32` ("a willing child takes over at 16 from the parent phase, or at 19+ on retirement").

### 4.4 Phone and content (13-6d)

1. **The job card** (Events tab, the 13-6b decision-card pattern):
   - the three jobs with their trade-offs in plain words;
   - the deadline;
   - the Coach option disabled with a reason when D8 bars it.
2. **The Family tab:**
   - a job strip: job, this week's wage, next shift;
   - the training budget ("2 of 3 sessions left this week");
   - per child:
     - the existing bars;
     - a **Train** button: it opens the drill, and is disabled with a reason outside ages 5–15 or with no sessions left;
     - the standing-plan selectors;
     - the last result line.
   - Refreshed once per day change, as today.
3. **`BackyardDrill`** (scene):
   - three reps on a meter;
   - its own visual RNG;
   - it emits one `PlayDrill(childId, score)` intent;
   - a result card ("Nailed it" / "Not today") from strings;
   - skippable, focusable, reduce-motion variant.
4. **D6 controls:** "Next week" and "Next season" in the time bar while the phase is on. 13-6c provides the engine call.
5. **Texts and events:** the job's first-day text, payday texts (sparingly), the training milestones (the first success, the first time coaching tops 70) and the D10 batch.

## 5. Tests and gates

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings / 0 errors |
| MC default | `dotnet run --project Tools/MonteCarloHarness -c Release` (run alone) | The baseline 623 plus the new checks, all PASS. Removing exactly the added lines (and restoring the count lines) reproduces `c9f2caf8…`. Pro guard byte-exact. Record both SHA-256s |
| New MC checks (`--only parent-phase`) | | (1) `ChildTraining`: odds clamp at 5/95; the tilt is 0 at 0.5 and ±cap at 0 and 1; same seed and inputs → same outcomes; diminishing returns monotone; Easy never lowers an axis. (2) The identity: no job, no session → `Child_Development` and `Players` byte-identical through a takeover fold. (3) A trained child's takeover folds the trained axes (potential +k, interest +j, exactly `NurtureBlend`'s numbers). (4) Source scans: the trainable list reads `LoadChildrenOf` only, with no `Biological_Parentage`/`LineageQueries` read in either service; the drill scene never references `RngState`; no new `rng.NextUInt64()` in the boot chain. (5) `ui-strings` extended to the new keys (present, none unused, no pronoun) |
| CoreLoop | `dotnet run --project Tools/CoreLoopHarness -c Release` | 43/43 plus: the job card resolves by deadline to Steady; the wage settles once a week and survives a reload; the job's block is forced and a stale schedule can't skip it; the budget resets weekly; unplayed sessions auto-resolve once; a reload doesn't reroll a resolved session; fast-forward stops on each interrupt kind; D7-A: a 42-year-old with a willing 15-year-old enters the phase and the child takes over at 16; the Life ↔ Baseball boundary scan stays clean |
| Pipeline | `dotnet run --project Tools/PipelineHarness -c Release` | Green; the takeover fixtures unchanged except D7-A's new case |
| NeedsDecay | `dotnet run --project Tools/NeedsDecayHarness -c Release` | 117/117 (`ActionCatalog` untouched) |
| Gritty | `dotnet run --project Tools/GrittyEventsHarness -c Release` | 363/363 plus the D10 batch loading |
| Event graph | the `check_event_graph_integrity` skill | clean (new flags set before they're read; no `voice_note`; no tier < N leak) |
| Schema | `validate_sqlite_schema` on a scratch copy | every new key and column reference resolves; no DDL (D9-A) |
| Godot (13-6d) | headless `--import` (keep only the translation) + parity + `BurnerPhone.tscn` and `BackyardDrill.tscn` load with 0 errors | exit 0 |
| Live boot (13-6d) | Godot, scratch `APPDATA` | Drive a save to a release with a 4-year-old child: the job card appears; pick Coach; the Family tab shows the budget; play a drill to a success and a failure; "Next season" stops on a gritty event; a later rollover at 16 hands over to the child with the trained potential |

## 6. Definition of done

- [ ] §3 records the owner's D1–D11. §9 (Carbon's Math check) and §10 (Opus's reconciliation) are signed.
- [ ] 13-6c built, its gates green, Carbon's review passed, merged into `p13/integration`.
- [ ] 13-6d built, its gates green, its review passed, merged into `p13/integration`.
- [ ] Owner playtest of one parent phase, release to takeover.
- [ ] Handoff (§8) and a `progress.md` line per sub-slice. **Then stop.**

## 7. Out of scope

- A real-time or arcade training game beyond D4-A's meter; reusing SR-3b batting practice (D4-C is a later choice).
- The stance engine itself (SR-2a/2b). D3-A only leaves the `hasJob` seam ready.
- Phase L: the adults-only loader guard and tier −1 adults; hidden children's intake and reclaim (L-6); the father's arc; substance effects on the household. They read the same flags later.
- Avatar death, illness or a second career (S-1); divorce and custody effects on training.
- Achievements and presence for the phase beyond 13-7's `#Status_Family` (17a may add "Coach of the Year" and similar).
- Any schema change (D9-B waits for v15); any change to `NurtureBlend`'s constants without §9's sanction; the ±8/±20 caps.
- Merging to `main` without the owner's go-ahead; the next slice.

## 8. Handoff (each sub-slice; addressed to the human)

```text
Done: <what was built>  · Commit: <sha>  · Gates: build, MC (N/N; SHA-256 new + reconstructed c9f2caf8…), parent-phase suite, CoreLoop, Pipeline, NeedsDecay, Gritty, event graph, schema, Godot, live boot
Open: <anything unfinished, risks, owner questions>
Recommended next: <review by the §1a reviewer on a pre-created worktree>; then Opus reconciles and merges
```

Then add one compact line to [progress.md](../progress.md), and write the complete prompt for the next model (TEAM.md §3 step 8). Don't start the next slice.

---

## 9. Math check — Gemini 4 Carbon (2026-11-27)

**Verdict: PASS WITH AMENDMENTS.** Every citation in §§1–8 was verified against `55921c9` (`Assets/` and `Tools/` byte-identical to `ac1a055`). **Zero shipped constants in `NurtureProfile`, `HeirGeneticsProfile`, `DevelopmentProfile`, `ActionCatalog`, `NeedsEngine`, or `ChildRearingService` need to change (0 owner calibration sanctions required).** However, the brief contains **2 blockers (`B1–B2`)**, **3 should-fixes (`S1–S3`)** where the placeholder numbers or rollover/schedule seams fail as written, and **3 citation nits (`N1–N3`)**. Amendments `A1–A5` (§9.4) supply the signed replacement values and pure-int formulas for §4.1, §4.2, and §4.3.

**Verification artifacts & seeds (docs-only pass, 0 `.db` files created):** exact ports of `LifeSimManager` / `NeedsEngine` / `UtilityCalculator` / `ChildRearingService`, `AtBatResolver`, `HeirGenetics`, `NurtureBlend`, and `PostseasonStreams` in scratch Python (`fast_q1_q6.py`, `diag_upkeep.py`, `math_check_q2_q7.py`, `math_check_q3_q5_multi.py`, `tune_final_numbers.py`; seeds `0x136CCA4B`, `20261127`, `0x136C2026`).

---

### 9.1 Citation audit & findings (against `55921c9`)

All cited paths and line ranges in §§1–8 match `55921c9`, except for the mechanical and citation items below:

- **B1 (Blocker — D3-A / D6-A schedule, double-wage, and `ChildRearingService` decay on fast-forward; `LifeSimManager.cs:572–587, 679–680, 705–709`, `GameManager.cs:1201–1279`, `ChildRearingService.cs:113–121`):**
  1. *Unplanned days skip `GetMandatoryBlocksFor` completely:* `LifeSimManager.OnDayAdvanced` (`:572–587`) clears `_hasTodaySchedule = false` after one planned day and ticks 24h of free `TickHour` autopilot on every unplanned day. Putting the job's Work block in `GetMandatoryBlocksFor` (`GameManager.cs:1201`) alone does **nothing** during `AdvanceUntil` ("Next week" / "Next season") or `Skip Day`.
  2. *Planned job days double-pay `$25/h` `LegalWork`:* `SubmitDaySchedule` (`GameManager.cs:1275`) sets `AvatarWorkIsHustle = workActivity != WorkActivity.LegalWork`. Under the default `WorkActivity.LegalWork`, `TickScheduledDay` (`LifeSimManager.cs:679–680`) ticks `ActionCatalog.LegalWork` (`-$200 / 8h = +$25/h`, `ActionCatalog.cs:150–151`), crediting an extra `$500/wk` (Coach), `$1,000/wk` (Steady), or `$600/wk` (Nightlife) on top of `ParentJobService`'s weekly wage.
  3. *Fast-forward zeroes `FamilyHoursThisWeek` and triggers `ChildRearingService` decay/neglect across all jobs:* `LifeSimManager.cs:705–709` only accumulates `FamilyHoursThisWeek` on planned days (`0f` on unplanned days). Every 7 days (`day % 7 == 0`), `ChildRearingService.ProcessDay` (`:113–121`) then applies `careDelta = RoundAwayFromZero((0 − 3) / 2.0) = −2/wk` (driving `care` `50 → 0` in 25 weeks for **every** job, including ages 0–4 before training opens) and, when `weekly_funding == 0`, `fundingDelta = −1/wk` (`funding` `50 → 0` in 50 weeks) plus `neglectDelta = +3/wk` (`neglect` `0 → 100` in 34 weeks, costing `−4.0` raw potential and `−20` interest).
  - **Fix (`Amendment A3`):** While `Career.InParentPhase` and `!AvatarScheduleLocked`, `GameManager` synthesizes a standing parent-phase `DaySchedule` before each day tick whenever `!LifeSim.HasTodaySchedule`: `8h` `Sleep` (`SleepProfile.OptimalHours`), the active job's `WorkHours` on shift weekdays (`0` on off-days), `1h` `FamilyHours` on three off-days/week (`3h/wk = ChildRearingService.CareNeutralHoursPerWeek`, holding `ChildRearingService`'s `careDelta = 0` and `neglectDelta = −1` so `ParentJobService` and `ChildTrainingService` cleanly own parent-phase `care`; if manually scheduled with `FamilyHours ≥ 5h/wk`, `ChildRearingService` adds `+1 care/wk` and waives Nightlife's `care` drag), plus `TravelTime.ComputeHours(...)`. Force `LifeSim.AvatarWorkIsHustle = true` whenever a parent-phase job's `WorkHours > 0` on a shift day.
- **B2 (Blocker — Plain XOR stream in `§4.2:218` has a `66.4%` collision rate, and `.NET` `string.GetHashCode()` breaks reload stability; `PipelineRules.cs:730–731`, `PostseasonStreams.cs:26–35`):**
  - `§4.2 (:218)` writes `RngState(training_seed ⊕ hash(childId) ⊕ day ⊕ sessionIndex)`. Because `⊕` is commutative and cancels in the low bits, `(day = 2k, sessionIndex = 1)` yields the **exact same 64-bit seed** as `(day = 2k + 1, sessionIndex = 0)` for every integer `k` (`2k ^ 1 == (2k + 1) ^ 0`), and `(4k, 2) == (4k + 2, 0)`. Across one 365-day season's `1,095` `(day, sessionIndex ∈ {0,1,2})` pairs for one child, plain XOR produces only **`368 / 1,095` distinct seeds (`727` collisions)**. In addition, `string.GetHashCode()` in `.NET 8` is randomized per process boot and would fail reload determinism (`§2:75`, `§5`).
  - **Fix (`Amendment A5`):** Derive every session stream via chained `PostseasonStreams.MixCombine` over a dedicated 8-byte ASCII tag (`0x4348_4C44_5452_4E47UL`, `"CHLDTRNG"`) and a deterministic 64-bit FNV-1a ordinal hash of `childId`, ending in `| 1UL` (`1,095 / 1,095` distinct seeds, `0` collisions, process-invariant).
- **S1 (Should-Fix — Placeholder Coach `$300/wk` spirals into `FundsDepleted`, and Nightlife EV `$1,050/wk` strictly dominates Steady `$1,000/wk`; `§4.1:194–196`):** See Q1 proof below and `Amendment A1` (`Coach $650/wk`, `Steady $1,000/wk`, `Nightlife $450 + $0–$800` tips = `EV $850/wk`).
- **S2 (Should-Fix — `§4.2:213–217` integer rounding stalls Easy at `76` and Normal at `88`, and uncapped `coaching`/`care` saturate in `0.07–0.48` seasons; `SchemaDefinitions.sql:523`, `PersonQueries.cs:714`):** See Q2 proof below and `Amendment A2` (integer basis-point carry `100 bp = 1` axis pt packed into `training_plan:<childId>`, `+600 bp` seasonal cap, `−6 bp/wk` idle coaching decay).
- **S3 (Should-Fix — D7-A routing in `§4.3 item 4 (:238)` drops young `< 16` children with current `interest < 40`, breaks `MC` `lineage-failure` if unguarded, and omits unroster/re-init/`AvatarReleasedEvent`; `CareerManager.cs:999–1029, 1361–1385, 1757–1812`, `PipelineManager.cs:1527, 1961–1998`, `Tools/MonteCarloHarness/Program.cs:3204–3223`):**
  1. `EvaluateSuccession` (`CareerManager.cs:999–1005`) checks `revealedInterest < 40` *before* `other.Age < MaturityAge` (`19`). A retiring 42-year-old whose only child is age 8 with `natureInterest = 35` returns `GameOver(NoWillingHeir)`, not `NoPlayableHeir` — even though `EvaluateTakeover` (`:1437–1441`) treats any child `< 16` as `anyYoung = true` before checking interest so parent-phase `care` (`+20` max `InterestDelta`) can win them over before 16.
  2. `EvaluateSuccession` is read-only (`:943`), and `RunLineageFailureSuite` (`Tools/MonteCarloHarness/Program.cs:3204–3223`, part of the 623 baseline checks frozen by `§0:11`) asserts `GameOver(NoPlayableHeir)` on a `Pipeline`-less solo-Pro `CareerManager`.
  3. `WriteReleaseStateInBatch` (`:1361–1371`) writes only `parent_phase_since_day`; it does not unroster the retiree, set `pipeline_retired`, flush/re-init the origin tier sim and `MicroGame`, call `ActivateUnrostered`, or publish `AvatarReleasedEvent` (which `PipelineManager` does at `:1563–1564, 1961–1998`).
  - **Fix (`Amendment A4`):** Keep `EvaluateSuccession` read-only and unchanged for `Pipeline`-less callers. Gate D7-A on `Career.EnableParentPhaseOnRetirement` (default `false`; set `true` when `PipelineManager` attaches). In `CareerManager`'s retirement rollover path, when `EvaluateSuccession` returns `GameOver` (`NoPlayableHeir` or `NoWillingHeir`) and `EnableParentPhaseOnRetirement` is `true`, enter the parent phase iff **any child has `Age < 16` OR (`16 ≤ Age < 19` and `revealedInterest ≥ 40`)**: flush the retiree's tier sim, commit `SetTeam(retireeId, null)` + `SetFlag(retireeId, PipelineFlags.Retired, true)` + `WriteReleaseStateInBatch(true, day)` in one batch, re-init that tier sim and `_micro`, call `ApplyReleaseState(true)` + `ActivateUnrostered(retireeId)`, and publish `AvatarReleasedEvent` (note: for a willing `16–18` child, `PipelineManager.RunRollover` runs later in the same `SeasonRolledOverEvent` pump, sees `InParentPhase == true`, and immediately executes the takeover onto `FamilyHsTeamId`, so `ParentJobService` must clear any pending job choice on `AvatarChangedEvent` when rostered).
- **N1 (Nit — `§1.1:36` vs `§2:75`):** `GameManager.cs:709–711` seeds `HustleService` from `Environment.TickCount64 ^ 0xD1A5D1A5D1A5D1A5UL`, not the boot `rng` chain, and `HustleService.cs:88` persists faction-rep IDs (`:372`), not its RNG seed. `§2:75` ("from the tick count") is accurate; the persisted-once `Game_State` seed precedent is `SeasonScheduler.ScheduleSeedKey` (`GameManager.cs:496–500`) and `PipelineManager.EnsureSeed` (`:608`, `PipelineManager.cs:191–203`).
- **N2 (Nit — `§1.1:30`):** `ChildRearingService.ProcessDay` spans `ChildRearingService.cs:97–153` (`FundsImpulseEvent` publishes at `:151`).
- **N3 (Nit — `§1.1:34`):** `GameManager.GetMandatoryBlocksFor` spans `GameManager.cs:1201–1225` (`:1227–1229` is `SubmitDaySchedule`'s doc comment).

---

### 9.2 Answers to Q1–Q7

#### Q1. Economy — weekly upkeep, placeholder breakdown, and replacement wages

An unrostered parent has `AvatarBoardWealthTier = −1` (`GameManager.cs:1014–1017`), so `HouseholdBoard.ShareFor(−1) = 1.0` (`HouseholdBoard.cs:27–28`): the avatar pays `100%` of `LifeSimManager.WeeklyCostOfLiving = $70.00/wk` (`LifeSimManager.cs:112, 569`) **and** `100%` of free-hour autopilot actions (`Eat $12`, `SocializeEvening $40`, `Workout $10`, plus `DrinkAlone $25` when any need ≤ `20` or `stress ≥ 70`; `ActionCatalog.cs:113–126`, `UtilityCalculator.cs:98–120`). Because `HustleWork` (`ActionCatalog.cs:167–169`) multiplies `Sleep` decay by `2.0×` and `Fitness` decay by `1.8×` *before* `TickScheduledDay`'s night `Sleep` block (`LifeSimManager.cs:680, 720–737`), longer shifts push `Sleep ≤ 20` during evening free hours and trigger crisis `DrinkAlone` spend:

| Schedule (`8h` optimal Sleep, `A3` standing plan) | `Eat` / wk | `Socialize` / wk | `Workout` / wk | `DrinkAlone` / wk | Free-hour spend | `WeeklyCostOfLiving` | **Solvent living drain (before kids)** |
| :-- | --: | --: | --: | --: | --: | --: | --: |
| **Coach** (Mon–Fri `4h`, walk / car) | `$195.3` / `$205.9` | `$200.2` / `$199.6` | `$35.0` / `$35.0` | `$19.8` / `$25.7` | `$450.3` / `$466.2` | `$70.0` | **`$520.2/wk`** (walk) / **`$536.1/wk`** (car) |
| **Nightlife** (Thu–Sun `6h`, walk / car) | `$196.6` / `$194.0` | `$224.8` / `$211.0` | `$40.2` / `$37.7` | `$185.0` / `$95.2` | `$646.5` / `$537.9` | `$70.0` | **`$716.4/wk`** (walk) / **`$607.8/wk`** (car) |
| **Steady** (Mon–Fri `8h`, walk / car) | `$180.5` / `$194.8` | `$231.2` / `$234.7` | `$46.5` / `$45.1` | `$273.5` / `$237.2` | `$731.7` / `$711.7` | `$70.0` | **`$801.7/wk`** (walk) / **`$781.6/wk`** (car) |

*(Measured over `4,380` days = `12` seasons = `625.7` weeks at solvent `$500,000` initial funds, `diag_upkeep.py`, seed `0x136CCA4B`.)*

**Why §4.1's placeholders fail both Q1 criteria:**
1. **Coach (`$300/wk`) spirals into `FundsDepleted`:** `$300/wk` is `$220–$236/wk` *below* bare single-adult upkeep before a dollar of child support (`$20/child`). Over a 12-season phase (`start = $500`, walk, `$0` discretionary funding), Coach hits `$0` on **`363–365` post-payday days (`987–989` mid-week days)** out of `4,380`, halting `AdvanceUntil` almost every week (`net = −$0.8/wk`, clamped at `$0`).
2. **Nightlife (`$600 + $0–$900`, gross EV `$1,050/wk`) strictly dominates Steady (`$1,000/wk`):** Nightlife's gross EV is `$50/wk` *above* Steady's, **and** its 6h shifts cost `$85/wk` less in crisis `DrinkAlone` than Steady's 8h shifts. Placeholder Nightlife nets **`+$313.6 / +$295.0 / +$290.7/wk`** (1 / 2 / 3 kids) vs Steady's **`+$132.4 / +$112.0 / +$94.2/wk`** — out-earning Steady by **`+$181` to `+$197/wk`** while also granting `3` training sessions/wk instead of `2`.

**Replacement wages (`Amendment A1`):** keep **Steady = `$1,000/wk`** (`$25/h`, the exact `ActionCatalog.LegalWork` anchor), raise **Coach = `$650/wk`** (`$32.50/h` HS stipend + private lessons), and re-center **Nightlife = `$450 + $0–$800` seeded tip roll** (`EV = $850/wk`, range `$450–$1,250/wk`). Note that `ChildRearingService` (`:116–118`) requires `weekly_funding = $50/wk` to hold `funding = 50` neutral (`$0/wk` drifts `funding` by `−1/wk`); simulated 12-season (`4,380`-day) weekly nets from `start = $500` (walking) are:

| Job & wage | Discretionary `weekly_funding = $0/wk` (`$20·N` support only) | Discretionary `weekly_funding = $50/wk` (holds `funding = 50` neutral) | Post-payday `FundsDepleted` days (`4,380` d) |
| :-- | :-- | :-- | :-- |
| **Placeholder Coach (`$300`)** | 1k `−$0.8` · 2k `−$0.8` · 3k `−$0.8` *(pinned at `$0`)* | 1k `−$0.8` · 2k `−$0.8` · 3k `−$0.8` *(pinned at `$0`)* | **363–365** *(spiral)* |
| **Amended Coach (`$650`)** | 1k **`+$109.4`** · 2k **`+$86.9`** · 3k **`+$66.9`** | 1k **`+$57.7`** · 2k **`+$37.2`** · 3k **`+$13.7`** | **0** (1k, 2k; `1` on 3k at `$0`, `0` at `$50`) |
| **Amended Nightlife (`$450 + $0–$800`, EV `$850`)** | 1k **`+$117.3`** · 2k **`+$105.5`** · 3k **`+$72.7`** | 1k **`+$62.8`** · 2k **`+$47.7`** · 3k **`+$22.3`** | **0** (`min = $201` at `$0`; `0–1` on 3k at `$50`) |
| **Steady (`$1,000`, unchanged)** | 1k **`+$132.4`** · 2k **`+$112.0`** · 3k **`+$94.2`** | 1k **`+$92.4`** · 2k **`+$51.6`** · 3k **`+$42.3`** | **0** (`min = $103` across all cells) |

Every job sustains 1–3 children + neutral `$50/wk` funding over 12 seasons without a `FundsDepleted` spiral; Steady out-earns Nightlife's EV modestly in every column (`+$150/wk` gross, `+$15` to `+$30/wk` net), while Nightlife's top tip weeks (`$1,250`) beat Steady and Coach cannot fund `$150/wk` camp budgets (`funding → 100`) without Pro savings or side hustles.

---

#### Q2. Saturation, integer-rounding dead-zones, and recommended cap + decay

**1. Exact saturation of §4.2's placeholders (no cap, no decay):**
`Child_Development.coaching` is an `INTEGER` (`SchemaDefinitions.sql:523`), written via `PersonQueries.AdjustChildAxis` (`:714`). Scaling integer gains (`Easy +1, Normal +2, Hard +4`) per session by `(100 − coaching) / 50.0` with `RoundAwayFromZero` (`< 0.5 → 0`) creates **permanent integer stalls** whenever `baseGain · (100 − coaching) < 25`:
- **Easy (`+1`):** reaches `70` in `20` successes, then **stalls permanently at `coaching = 76`** (`1 × 24/50 = 0.48 → 0`; **never reaches `90`**).
- **Normal (`+2`):** `+2` for `50..62` (`7` wins → `64`), `+1` for `63..87` (`6` wins → `70`, `13` total; `24` wins → `88`, `31` total), then **stalls permanently at `coaching = 88`** (`2 × 12/50 = 0.48 → 0`; **never reaches `90`**).
- **Hard (`+4`):** reaches `70` in **`6` wins** and `90` in **`20` wins**, then **stalls at `coaching = 94`** (`4 × 6/50 = 0.48 → 0`).

At `52.143` weeks/season (`365 / 7`), expected seasons to `70` and `90` (`expertise = +5 pp`, autopilot `score = 0.5 → tilt = 0`, `fatigue = 0`, 1 child; both exact integer and unrounded continuous ODE shown):

| Job (`sessions/wk`, bonus) | Easy (`p`) — `70` / `90` (int \| cont) | Normal (`p`) — `70` / `90` (int \| cont) | Hard (`p`) — `70` / `90` (int \| cont) |
| :-- | :-- | :-- | :-- |
| **Coach** (`3/wk`, `+10 pp`) | `p = 0.95`: **`0.13`** / **STALL@76** (`0.17` / `0.54` yr) | `p = 0.75`: **`0.11`** / **STALL@88** (`0.12` / `0.35` yr) | `p = 0.55`: **`0.07`** / **`0.22` yr** (`0.08` / `0.23` yr) |
| **Nightlife** (`3/wk`, `−5 pp`) | `p = 0.80`: **`0.16`** / **STALL@76** (`0.21` / `0.65` yr) | `p = 0.60`: **`0.14`** / **STALL@88** (`0.15` / `0.44` yr) | `p = 0.40`: **`0.10`** / **`0.30` yr** (`0.12` / `0.33` yr) |
| **Steady** (`2/wk`, `−5 pp`) | `p = 0.80`: **`0.24`** / **STALL@76** (`0.31` / `0.96` yr) | `p = 0.60`: **`0.21`** / **STALL@88** (`0.21` / `0.65` yr) | `p = 0.40`: **`0.14`** / **`0.46` yr** (`0.15` / `0.48` yr) |

Every path hits `70` in **`3.6–12.5` weeks (`0.07–0.24` seasons)** and saturates or stalls in under half a season (`care` at `+1/win` also hits `100` in `0.3–0.5` seasons). Meanwhile, a whole-integer-only seasonal cap of `+5` or `+6` without sub-points would exhaust the entire year's budget in **Week 2 of January**, leaving `50` dead weeks per season.

**2. Recommended sub-point carry, per-season cap, and idle decay (`Amendment A2`):**
Follow `LifeSimManager.PersonRuntime.Unflushed`'s exact sub-point carry precedent (`LifeSimManager.cs:58–61`) using **pure `int` basis points (`100 bp = 1` axis point)** packed inside D9-A's existing `training_plan:<childId>` `Game_State` text key (`focus:intensity:coachBp:careBp:seasonYear:seasonCoachBp`; `0` when absent = byte-identical §7.3 identity; zero DDL; whole `100 bp` steps flush via `PersonQueries.AdjustChildAxis`):
- **Base `coaching` gain per success:** Easy **`5 bp`**, Normal **`9 bp`**, Hard **`15 bp`**, scaled by diminishing returns `gain = Math.Max(1, RoundAwayFromZero(baseBp * Math.Clamp((100 − coaching) / 50.0, 0.0, 1.0)))` while `coaching < 100` (the `Math.Max(1, ...)` floor eliminates every rounding stall up to `99`).
- **Per-season `coaching` cap (per child):** **`SeasonCoachingCapBp = 600` (`+6.0` coaching pts / season)**, reset when `seasonYear` advances.
- **Weekly idle `coaching` decay:** **`IdleCoachingDecayBp = 6` (`−0.06` pt/wk = `−3.1` pts/season toward `50`)** applied at week's end only to a trainable child (`age 5–15`) who received `0` sessions that week (never decays below `50`, i.e. `coaching == 50 && coachBp == 0`).

Simulated 10-season (`520`-week) and 11-season (`572`-week, full ages `5..15`) outcomes (`N = 2,000`, `tune_final_numbers.py`, seed `0x136CCA4B`, `expertise = +5 pp`, autopilot `score = 0.5`; round-robin plan for `2–3` kids):

| Job & kids | Intensity (`p`) | Seasons to `70` | Seasons to `90` | End `coaching` (10 yr / 11 yr) | Solo `NurtureBlend` `PotentialDelta` (`funding = 50`) |
| :-- | :-- | :-- | :-- | :-- | :-- |
| **Coach (1 kid)** | Easy (`0.95`) | `3.6` yr | never | **`88.0`** / **`89.5`** | **`+3`** |
| **Coach (1 kid)** | **Normal (`0.75`)** | **`3.3` yr** | **`8.4` yr** | **`92.2`** / **`93.6`** *(active `+10 pp` drill → `94.8`)* | **`+3`** autopilot · **`+4` with good manual drills (`≥ 94`)** |
| **Coach (1 kid)** | Hard (`0.55`) | `3.3` yr | `7.6` yr | **`94.2`** / **`96.0`** | **`+4`** *(costs `care`, see Q4)* |
| **Coach (2 / 3 kids)** | Normal (`0.75`) | `4.8` / `7.2` yr | never | **`82.3` (`+3`)** / **`75.0` (`+2`)** *(with `−6 bp` idle: `84.8` / `79.5`)* | **`+3`** (2k) / **`+2`** (3k) |
| **Nightlife (1 kid)** | Easy / **Normal** / Hard | `4.1` / **`3.4`** / `3.4` yr | never / **`9.7`** / `9.0` yr | `86.0` / **`90.0`** / `91.3` (10 yr) · `87.0` / **`91.9`** / `92.7` (11 yr) | **`+3`** |
| **Steady (1 kid)** | Easy / **Normal** / Hard | `6.1` / **`4.5`** / `4.1` yr | never | `78.0` (`+2`) / **`83.5` (`+3`)** / `85.6` (`+3`) (10 yr) | **`+2` to `+3`** *(up to **`+5`–`+6`** with Steady's `$150/wk` `funding → 100`)* |
| **Steady (2 / 3 kids)** | Normal (`0.60`) | `9.0` yr / never | never | **`73.6` (`+2`)** / **`58.3` (`+1`)** *(at `−6 bp/wk` idle)* | **`+2`** (2k) / **`+1`** (3k) |

Ten seasons of good play on Coach Normal lands at **`92.2` (`93.6` in 11 seasons)** — **near, not at, `100`**, and perched `1.8` points below `NurtureBlend`'s `94` (`+4`) cutoff so active drill play earns the final rating point without punishing autopilot (`+3`).

---

#### Q3. `NurtureBlend` deltas, dynasty drift bound, 3-generation league bands, and `NurtureProfile`

**1. Folded `NurtureBlend` deltas (`NurtureBlend.cs:56–73`, `MidpointRounding.AwayFromZero`):**
- `PotentialDelta(coaching, funding, neglect) = clamp(round(0.08·(coaching − 50) + 0.048·(funding − 50) − 0.04·neglect), −8, +8)`:
  - At neutral `funding = 50, neglect = 0`: `0..43` → `−4..−1`, `44..56` → **`0`**, `57..68` → **`+1`**, `69..81` → **`+2`**, `82..93` → **`+3`**, **`94..100` → `+4`** (solo coaching max).
  - With `funding = 100` (`$150+/wk` commitment, `+2.4` raw) and `neglect = 0`: `coaching = 50 → +2`, `52..63 → +3`, `64..76 → +4`, `77..88 → +5`, **`89..100 → +6`** (combined max; `+7` and `+8` remain unreachable because max raw is `4.0 + 2.4 = 6.4`).
  - With `funding = 0` (`$0/wk` commitment over `≥ 50` weeks, `−2.4` raw) and `neglect = 0`: `coaching = 92` yields `round(3.36 − 2.4) =` **`+1`** (funding matters).
- `InterestDelta(care, neglect) = clamp(round(0.4·(care − 50) − 0.2·neglect), −20, +20)`: spans **`−20`** (`care = 0`) through **`0`** (`care = 50, neglect = 0`) to **`+20`** (`care = 100, neglect = 0`).

**2. Bounded dynasty drift for `k = +4` to `+6` every generation:**
Let $d_n = \text{pot}_n - 50$ and $m = \text{parentB} - 50$. Note that `CareerManager.ConceiveChild` (`CareerManager.cs:717, 739`) blends `Player_Ratings` (*current* ratings $\le \text{potential}$), so $d_{n+1} \le 0.25\,d_n + 0.25\,m + k$ with equality only when parent A has fully developed to ceiling (`age ≈ 25–27`). Since the contraction factor is $|0.25| < 1$, the upper-bound recurrence converges globally from **any** founder rating $50 + d_1 \in [0, 100]$ to the unique fixed point:

$$d^* = \frac{0.25\,m + k}{0.75} = \frac{m + 4k}{3}$$

| Partner stock (`parentB`) | Nurture `k` | Fixed point `50 + d*` | 3-gen deterministic trace (`bell = 0`) from Founder `50` | 3-gen deterministic trace (`bell = 0`) from Founder `90` |
| :-- | --: | --: | :-- | :-- |
| **Lone / civilian (`50`, `m = 0`)** | `+0` | `50.00` | `50 → 50 → 50 → 50` | `90 → 60 → 53 → 51` |
| **Lone / civilian (`50`, `m = 0`)** | **`+4`** | **`55.33`** | `50 → 54 → 55 → 55` | `90 → 64 → 58 → 56` *(strictly regresses)* |
| **Lone / civilian (`50`, `m = 0`)** | **`+6`** | **`58.00`** | `50 → 56 → 58 → 58` | `90 → 66 → 60 → 59` *(strictly regresses)* |
| **Elite ballplayer (`80`, `m = +30`)** | **`+4`** | **`65.33`** | `50 → 62 → 65 → 65` | `90 → 72 → 67 → 66` *(strictly regresses)* |
| **Elite ballplayer (`80`, `m = +30`)** | **`+6`** | **`68.00`** | `50 → 64 → 67 → 68` | `90 → 74 → 70 → 69` *(strictly regresses)* |

*(Even at theoretical `m = +50` [100-rated partner] and `k = +8` cap, `50 + d* = 77.33 < 100`.)*

**3. League §8 acceptance bands over a 3-generation run (`AtBatResolver.cs:75–169`, `Program.cs:4099–4103`):**
Nurture writes **only the single active avatar heir's `Player_Potential` row**; all NPC rosters are untouched. Evaluating `AtBatResolver.ComputeProbabilities` for an extreme `+18`-point steady-state heir (`68/68/68` vs `50/50/50`, i.e., `m = +30, k = +6` at peak) diluted by 1 lineup slot (`1 / (9 · N_teams)` of tier PA) or 1 starter slot (`1 / (3 · N_teams)` of tier IP):
- **HS (`1/108` PA, `1/36` IP):** `ΔAVG +0.00060` (band half-width `±0.010`), `ΔOBP +0.00069` (`±0.010`), `ΔSLG +0.00113` (`±0.015`), batter `ΔR/9 +0.033` / SP `ΔR/9 −0.045` (`±0.80`).
- **College (`1/216` PA, `1/72` IP):** `ΔAVG +0.00029` (`±0.010`), `ΔOBP +0.00034` (`±0.010`), `ΔSLG +0.00056` (`±0.015`), batter `ΔR/9 +0.015` / SP `ΔR/9 −0.021` (`±0.65`).
- **Pro (`1/288` PA, `1/96` IP):** `ΔAVG +0.00020` (`±0.010`), `ΔOBP +0.00024` (`±0.0085`), `ΔSLG +0.00038` (`±0.0175`), batter `ΔR/9 +0.009` / SP `ΔR/9 −0.012` (`±0.35`).

Every shift is **`18×` to `50×` smaller** than its §8 band half-width; **no §8 band moves over 3 (or 30) generations.**

**4. `NurtureProfile` verdict:** **Keep `NurtureProfile` (`PotentialNurtureCap = 8`, `CoachWeight = 0.5`, `FundWeight = 0.3`, `NeglectWeight = 0.5`, `InterestNurtureCap = 20`), `HeirGeneticsProfile`, and `DevelopmentProfile` byte-identical. No owner sanction is required.**

---

#### Q4. Willingness at 16 (`interest ≥ 40`) — exact CDF and Nightlife's `care` cost

At conception (`CareerManager.cs:740`, `HeirGenetics.cs:157–162`), `BirthAffinity = 30` gives `affinityAdjust = 15 × 0.30 = +4.5` and `bell = (S − 1.5) / 1.5` where $S = U_1 + U_2 + U_3 \sim \text{Irwin–Hall}(3)$ on $(0, 3)$:

$$\text{raw} = 55 + 4.5 + 30 \cdot \frac{S - 1.5}{1.5} = 29.5 + 20S \implies \text{nature\_interest} = \lfloor 30 + 20S \rfloor \in [30, 89]$$

(`mean = 59.50`, `SD = 10.00`). Because $\min(\text{nature\_interest}) = 30$, for any folded `InterestDelta` $\Delta \in [-20, +20]$ (`NurtureBlend.cs:68–73`), willingness (`nature_interest + Δ ≥ 40`) has the **exact closed form**:

$$P(\text{willing} \mid \Delta) = \begin{cases} 1, & \Delta \ge +10 \;\;(\text{care} \ge 75\text{ at neglect }0) \\ 1 - F_{\text{IH3}}\!\left(\frac{10 - \Delta}{20}\right), & -20 \le \Delta < +10 \end{cases}$$

where $F_{\text{IH3}}(s) = s^3 / 6$ for $s \in [0, 1]$ and $[s^3 - 3(s-1)^3]/6$ for $s \in [1, 2]$.

**Calibrated `care` basis-point rules (`Amendment A2`, `100 bp = 1` `care` pt, `neglect = 0` under `A3`):**
- **Session `care` deltas:** Easy success **`+18 bp`**, Normal success **`+12 bp`**, Hard success **`0 bp`**, Hard failure **`−25 bp`**.
- **Weekly job `care` deltas (`ParentJobService`):** Coach **`+25 bp/wk`** (`+13.0 pts/yr`), Steady **`0 bp/wk`**, Nightlife **`−25 bp/wk`** (`−13.0 pts/yr`, waived in any week with manually scheduled `FamilyHours ≥ 5h`). *(Note: §4.1's placeholder `−1 pt/wk` = `−100 bp/wk` on Nightlife was 4× too steep — it drove `care` to `0` in `50` weeks even under full Easy training.)*

| Scenario (10 trainable seasons = `520` wks, 1 child unless noted) | End `care` | `InterestDelta` | **Exact `P(willing at 16)`** | Unwilling risk |
| :-- | --: | --: | --: | --: |
| **No training, Steady** (neutral baseline, `care = 50`) | `50.0` | `0` | **`47/48 = 97.92%`** | `2.08%` |
| **Easy or Normal training, Coach or Steady** (`1–3` kids) | `74.5–100.0` | `+10` to `+20` | **`100.00%`** | **`0.00%`** *(guarantees willingness)* |
| **Hard-only training, Coach** (`p = 0.55`, burnout vs `+25 bp/wk`) | `4.6` | `−18` | **`56.74%`** | `43.26%` |
| **Hard-only training, Steady** (`p = 0.40`, uncompensated burnout) | `0.0` | `−20` | **`50.00%`** | `50.00%` |
| **Nightlife, no training** (or Hard-only training) | `0.0` | `−20` | **`1/2 = 50.00%`** | **`50.00%`** *(coin-flip walkaway)* |
| **Nightlife, Normal training (1 kid, `p = 0.60`)** | `31.8` | `−7` | **`89.19%`** | `10.81%` *(bar nights still sting)* |
| **Nightlife, Easy training (1 kid, `p = 0.80`)** | `99.0` | `+20` | **`100.00%`** | **`0.00%`** *(attentive Easy play offsets nights)* |
| **Nightlife, Normal training (2 / 3 kids split)** | `0.0` | `−20` | **`50.00%`** per kid (`75.0%` / `87.5%` ≥ 1 willing) | `25.0%` / `12.5%` total-fail |

*(Wait-phase note: if a child enters the parent phase at age `0–4` under Nightlife before age-5 training opens, `4` untrained years × `−13 pts/yr` moves `care` `50 → 0`, which 10 seasons of Easy training [`+18.2 bp/wk` net = `+94.6` pts] still recovers back to `94+` → `100%` willing, while Normal stays near `0` → `50%` willing.)*

---

#### Q5. Drill tilt (`±15 pp`), parent expertise (`+10 pp`), and stress/fatigue double-counting

1. **`tilt(score)` = `±15 pp` (`RoundAwayFromZero(30.0 * (clamp(score, 0.0, 1.0) − 0.5))`):** **Right size — keep.**
   - At `score = 0.5` (skip or standing-plan autopilot), `tilt = 0 pp`: zero penalty vs the ratings/job baseline (GEMINI.md §11 parity).
   - At `score = 0.83–1.00` (`+10` to `+15 pp`), Coach Normal rises from `75%` to `85%–90%`, pushing a 10-season child from `92.2` (`+3` potential) across `94` (`+4` potential), and cutting Hard failure burnout by a third (`45% → 30%` fail rate).
2. **`expertise(parent)` = `0` to `+10 pp`:** **Right size — keep, but anchor to peak ceiling so age-42 decline doesn't erase baseball IQ.**
   - At age 42 (D7-A), `DevelopmentCurve` has eroded a veteran's *current* `Player_Ratings` by `~41` role-sum points across ages `28..42` (`DevelopmentManager.cs:145–151, 185`), while `Player_Potential` preserves their career ceiling. Use `PipelineRules.RoleRatingSum` (`PipelineRules.cs:247–260`, crediting relievers `+20` stamina so reliever parents aren't docked) over `max(RoleRatingSum(current), RoleRatingSum(potential))`:
     `expertisePp = Math.Clamp(RoundAwayFromZero(10.0 * (peakRoleSum − 135) / 90.0), 0, 10)`
     (`≤ 135` walk-on → `0 pp`; `150` league-avg → `+2 pp`; `180` solid College/Pro → `+5 pp`; `225+` Pro star → `+10 pp`).
3. **Does `fatigue(stress)` (`0` to `−10 pp` above `stress = 60`) double-count Steady's `−5 pp` and `stress +3`?**
   - **No in steady state:** `LifeSimManager` relaxes `StressRelaxationPerHour = 0.4f/hr = 9.6/day` (`LifeSimManager.cs:99, 816, 883`) plus `GoodNightStressRelief = 4.0/day` on an 8h sleep block (`SleepProfile.cs:28`) = **`13.6` stress relief/day (`95.2/wk`)**. Steady's weekly `+3` impulse decays to **`0.0` in `7.5` hours** and never approaches `60` without an external gritty-event stress spike.
   - **During gritty-event spikes (`stress > 60`):** `fatiguePp = Math.Clamp(RoundAwayFromZero(10.0 * (stress − 60.0) / 40.0), 0, 10)` (`0 pp` at `≤ 60`, `−5 pp` at `80`, `−10 pp` at `100`) stacks additively with Steady's `−5 pp` shift-fatigue modifier, floored cleanly above `5%` (`40% Hard − 5% Steady − 10% max stress − 15% worst 0.0 drill = 10%`). Separating chronic 40h shift tiredness (`−5 pp`) from acute crisis/event burnout (`stress > 60`) is sound.
   - **Weekly stress scaling (`Amendment A1`):** Because `13.6/day` passive relief erases `+3` in `7.5h`, rescale weekly job stress impulses to **Coach `−5`**, **Steady `+10`**, **Nightlife `−3`** so Steady's payday impulse meaningfully nudges an already-frayed (`55–60` stress) parent into the `≥ 70` crisis/interrupt band on payday without firing `fatigue(stress)` in calm weeks (`10 / 13.6 < 1` day).

---

#### Q6. D7-A edge cases — retirement at 42 (and health-floor retirement) with children aged 0–18

`CareerManager.OnSeasonRolledOver` (`CareerManager.cs:1757–1762`) runs `_players.AgeAllPlayers()` *before* evaluating succession, and `PipelineManager.RunRollover` (`:1527, 1786–1812`) runs later in the **same `SeasonRolledOverEvent` pump**. Let $a_c$ be the child's post-aging age at the retirement rollover (`avatar.Age == 42`; in live play a child born during the age-41 season just aged `0 → 1`, so live $a_c \in [1, 18]$, while a direct test fixture can seed $a_c = 0$):

| Child age $a_c$ at retirement rollover | What happens at rollover (`Amendment A4`) | Phase length (seasons / days) | Trainable seasons (`ages 5..15`) | Parent age at child's `16+` takeover | Heir's HS seasons (`ages a_takeover..19`) |
| :-- | :-- | :-- | :-- | :-- | :-- |
| **`16, 17, 18` (willing, `interest ≥ 40`)** | Enters parent phase in subscriber #2, **immediately takes over in subscriber #4** (`PipelineManager.RunRollover`, same pump) | **`0` seasons (`0` days)** | `0` | `42` | `4` (`@16`), `3` (`@17`), or `2` (`@18`) HS seasons before graduating after age `19` (`PipelineManager.cs:1627`) |
| **`16, 17, 18` (all children `≥ 16` & unwilling)** | Immediate `GameOver(NoWillingHeir)` | `0` | `0` | — | Game over |
| **`15`** *(CoreLoop §5 fixture)* | Enters parent phase | **`1` season (`365` d)** | `1` (age `15`) | `43` | `4` HS seasons (`16..19`) |
| **`5..14`** | Enters parent phase; immediately trainable | **`2–11` seasons (`730–4,015` d)** | `2–11` | `44–53` | `4` HS seasons (`16..19`) |
| **`1..4`** *(live newborn = `1`; seeded `0` = `16` yr)* | Enters parent phase; waits to age `5`, then trains `5..15` | **`12–15` seasons** (`4,380–5,475` d; `16` yr / `5,840` d if `0`-seeded) | **`11`** (ages `5..15`) | **`54–57`** (`58` if `0`-seeded) | `4` HS seasons (`16..19`) |

*(Health-floor retirement `health_ceiling ≤ 40` at age $a_p \in [20, 41]$ shifts parent takeover ages to $[a_p + 1, a_p + 15]$, e.g., `30 → 45`.)*

**Are parent ages `42 → 57` acceptable?**
- **Yes, mechanically and thematically:** `Players.age` has no upper CHECK constraint (`SchemaDefinitions.sql`) and no natural-death trigger (`heir_mechanics.md §11 #6`); `§1.2`'s `child.Age < parent.Age` invariant holds permanently ($a_p - a_c \ge 24 > 0$); an unrostered parent skips `DevelopmentManager` (`DevelopmentManager.cs:358`), so unclamped `AgeWeight(age > 42)` never runs; and a retired 42-year-old ballplayer coaching their kid in the backyard at 46–56 is completely natural.
- **One boundary note (non-blocking):** `CareerManager.OnChildConceptionRequested` (`:1710–1743`) has no parent-age cap, so if a marriage event fires `conceive_child` at age 52 during the parent phase and the older child walks away at 16, the newborn extends the phase to parent age 68. Phase L's fertility rules (`temptation_and_lineage.md §5`, `substances_and_addiction.md §2.1`) own biological conception gates; optionally gating parent-phase `conceive_child` events at `age ≤ 50` in content keeps ages inside the 50s.

---

#### Q7. Seed determinism — reload stability and zero boot draws

1. **Zero boot draws confirmed:** `GameManager.BootCareer` (`GameManager.cs:399–612`) advances the boot `rng` strictly for `GenerateIfEmpty`, `careerRng`, the three `LeagueSimulator`s, `scheduleSeed` (`Split()`), `DevelopmentManager`, `PipelineManager.EnsureSeed`, and `NpcAutonomyService` (`Split()`). Writing `Game_State['training_seed']` lazily on first training/auto-resolve use (from `unchecked((ulong)Environment.TickCount64) ^ 0x5452_4149_4E53_4544UL | 1UL` in live play, or a fixed seed in harnesses) touches **zero** boot draws and leaves all 623 baseline MC checks and `c9f2caf8…` byte-identical.
2. **Reload stability (`Amendment A5`, fixing `B2`):**
   - Replace `§4.2:218`'s plain XOR (`727 / 1,095` collisions/yr) and any `.NET` `string.GetHashCode()` call with deterministic FNV-1a 64-bit over `childId` ordinals + `PostseasonStreams.MixCombine` (`PostseasonStreams.cs:26–35`):
     ```csharp
     public const ulong TrainingStreamTag = 0x4348_4C44_5452_4E47UL; // "CHLDTRNG"

     public static ulong HashId64(string id)
     {
         ulong h = 0xCBF2_9CE4_8422_2325UL;
         for (int i = 0; i < id.Length; i++)
         {
             h = unchecked((h ^ id[i]) * 0x0000_0100_0000_01B3UL);
         }
         return h;
     }

     public static RngState SessionStream(ulong trainingSeed, string childId, long day, int sessionIndex)
     {
         ulong h = PostseasonStreams.MixCombine(trainingSeed, TrainingStreamTag);
         h = PostseasonStreams.MixCombine(h, HashId64(childId));
         h = PostseasonStreams.MixCombine(h, unchecked((ulong)day));
         h = PostseasonStreams.MixCombine(h, unchecked((ulong)sessionIndex));
         return new RngState(h | 1UL);
     }
     ```
   - `PlayDrill` commits the updated `Child_Development` axis + `training_sessions_used` + `training_plan:<childId>` in one batch on the press (`§4.3 item 2`), so reloading *after* a drill sees the committed result and remaining budget, while reloading *before* a drill (or before week-end auto-resolve) replays the exact same `SessionStream` draw (`1,095 / 1,095` distinct seeds/yr, `0` collisions).
   - For Nightlife's weekly `$0–$800` tip roll, derive `TipStream(trainingSeed, day)` the same way with tag `0x4E47_4854_5449_5053UL` (`"NGHTTIPS"`), `rng.NextInt(801)`, so reloading on a payday week cannot reroll tips either.

---

### 9.3 D-option math verdicts

| Decision | Math verdict | Notes |
| :-- | :-- | :-- |
| **D1 (Jobs)** | **D1-A holds** with `Amendment A1` | `Coach $650/wk`, `Steady $1,000/wk`, `Nightlife $450 + $0–$800` (`EV $850/wk`); stress `−5 / +10 / −3`; `care` `+25 / 0 / −25 bp/wk`. |
| **D3 (Schedule & stance)** | **D3-A holds** with `Amendment A3` (`B1` fix) | Standing parent-phase `DaySchedule` (`8h` Sleep + job `WorkHours` under forced `HustleWork` + `3h/wk` neutral off-day `FamilyHours` + `TravelHours`) on unplanned days prevents `LegalWork` double-pay and `ChildRearingService` fast-forward decay. |
| **D4 (Backyard Drills)** | **D4-A holds** as written | `tilt = ±15 pp` linear around `0.5` (`0 pp` on skip/autopilot) bridges Coach Normal from `+3` (`92.2`) to `+4` (`≥ 94`) under skilled manual play without punishing autopilot. |
| **D5 (What training changes)** | **D5-A holds** (over D5-B) | Zero DDL, zero `NurtureProfile` edits. Even without a D5-B day-one readiness bump, `DevelopmentCurve` (`GrowthRate = 0.5` + `80h` HS practice → `growthFrac = 0.485 / 0.443 / 0.402 / 0.360` at ages `17..20`) realizes `48%` of `PotentialDelta` after freshman year and `88.5%` by College entry, while `PromotionScore.Scouting` (`ProjectionBonus`, `PromotionScore.cs:230`) already credits remaining ceiling on HS Signing Day. |
| **D6 (Cadence & pacing)** | **D6-A holds** with `Amendment A2` | Budgets `3 / 2 / 3` sessions/wk + `600 bp/season` cap + `−6 bp/wk` idle decay create clean separation across jobs and `1–3` children. Include pending succession/takeover alongside the job card in `AdvanceUntil`'s decision-card interrupt. |
| **D7 (Retirement succession)** | **D7-A holds** with `Amendment A4` (`S3` fix) | Enter parent phase when any child is `< 16` (regardless of current interest) or `16–18` and willing; gate on `EnableParentPhaseOnRetirement` so `Pipeline`-less `MC` `lineage-failure` checks stay byte-identical; max live parent age at takeover is `57` (`58` if `0`-seeded). |

*(D2, D8, D9-A, D10-A, and D11-A have no math objections; `D9-A` packs `coachBp:careBp:seasonYear:seasonCoachBp` inside `training_plan:<childId>` with zero new tables.)*

---

### 9.4 Summary of proposed amendments (`A1–A5`)

1. **`Amendment A1` — Replacement §4.1 job profiles (`parent_jobs.json`):**

   | Job | Days and hours | Weekly wage | Sessions / wk | Drill odds | Weekly side effects (`ParentJobService`) | Flag | Gate |
   | :-- | :-- | :-- | --: | --: | :-- | :-- | :-- |
   | **Coach** | Mon–Fri, `4 h` | `$32.50/h` → **`$650`** | `3` | **`+10 pp`** | `stress −5`; **`care +25 bp`** (`+0.25 pt/wk`) | `job_coach` | not `banned_for_life` |
   | **Steady** | Mon–Fri, `8 h` | `$25.00/h` → **`$1,000`** (`LegalWork`) | `2` | **`−5 pp`** | `stress +10`; full `HustleWork` needs drain | `job_steady` | none |
   | **Nightlife** | Thu–Sun, `6 h` | **`$450` + seeded `$0–$800` tip roll** (`EV $850`, `"NGHTTIPS"`) | `3` | **`−5 pp`** | `recklessness +1`; `stress −3`; **`care −25 bp`** (`−0.25 pt/wk`, waived if weekly `FamilyHours ≥ 5`) | `job_nightlife` | none |

2. **`Amendment A2` — Replacement §4.2 training rules (`ChildTraining.cs`, pure `int` basis points, `100 bp = 1` axis pt):**
   - **Success probability (integer pp, clamped `[5, 95]`):**
     `pPp = Math.Clamp(basePp + jobBonusPp + expertisePp + tiltPp − fatiguePp, 5, 95)`
     - `basePp`: Easy `80`, Normal `60`, Hard `40`.
     - `expertisePp`: `Math.Clamp(RoundAwayFromZero(10.0 * (max(RoleRatingSum(current), RoleRatingSum(potential)) − 135) / 90.0), 0, 10)` (uses `PipelineRules.RoleRatingSum`'s `+20` reliever stamina credit).
     - `tiltPp`: `RoundAwayFromZero(30.0 * (Math.Clamp(score, 0.0, 1.0) − 0.5))` (`−15` to `+15`; `0` at `0.5`).
     - `fatiguePp`: `Math.Clamp(RoundAwayFromZero(10.0 * (stress − 60.0) / 40.0), 0, 10)` (`0` at `stress ≤ 60`, `10` at `stress = 100`).
   - **Session outcomes (`100 bp = 1` whole axis point flushed via `PersonQueries.AdjustChildAxis`; remainder kept in `training_plan:<childId>`):**
     - **Easy:** Success → `coaching` `+max(1, RoundAwayFromZero(5 * clamp((100 − coaching)/50.0, 0, 1)))` bp, `care` **`+18 bp`**; Failure → `0`.
     - **Normal:** Success → `coaching` `+max(1, RoundAwayFromZero(9 * clamp((100 − coaching)/50.0, 0, 1)))` bp, `care` **`+12 bp`**; Failure → `0`.
     - **Hard:** Success → `coaching` `+max(1, RoundAwayFromZero(15 * clamp((100 − coaching)/50.0, 0, 1)))` bp, `care` `0`; Failure → `care` **`−25 bp`**.
   - **Per-season cap & weekly idle decay:** `SeasonCoachingCapBp = 600` (`+6.0` coaching pts / child / season); `IdleCoachingDecayBp = 6` (`−0.06` pt/wk toward `50` in any week a trainable child `5–15` receives `0` sessions).
3. **`Amendment A3` — Standing parent-phase schedule (`B1`):** On unplanned days while `Career.InParentPhase` and `!AvatarScheduleLocked`, `GameManager` queues `8h` `Sleep` + shift-day job `WorkHours` (`AvatarWorkIsHustle = true` forced on shift days) + `1h` `FamilyHours` on three off-days (`3h/wk` neutral baseline for `ChildRearingService`) + `TravelHours`.
4. **`Amendment A4` — D7-A retirement-to-parent-phase seam (`S3`):** Keep `EvaluateSuccession` read-only; gate retirement entry into the parent phase on `Career.EnableParentPhaseOnRetirement` (set `true` by `PipelineManager`) when any child is `< 16` or (`16–18` and `revealedInterest ≥ 40`), performing the full tier-sim flush → batch (`SetTeam(null)`, `pipeline_retired`, `WriteReleaseStateInBatch`) → sim + micro re-init → `ActivateUnrostered` → `AvatarReleasedEvent` sequence.
5. **`Amendment A5` — Collision-free reload-stable streams (`B2`):** Replace plain `⊕` and `string.GetHashCode()` with `PostseasonStreams.MixCombine` + FNV-1a `HashId64(childId)` + ASCII tags `"CHLDTRNG"` (`0x4348_4C44_5452_4E47UL`) and `"NGHTTIPS"` (`0x4E47_4854_5449_5053UL`), `| 1UL`.
6. **Shipped profile sanction check:** **None required** (`NurtureProfile`, `HeirGeneticsProfile`, `DevelopmentProfile`, `ActionCatalog`, `NeedsEngine`, and `ChildRearingService` constants remain untouched).
