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

## 9. Math check — Gemini 4 Carbon (to be written)

The questions for Carbon, in this order:

1. **Economy.** At D1-A's wages, child support of $20 a child and the shipped Life upkeep, does each job sustain a household of 1–3 children over a 12-season phase without a forced `FundsDepleted` spiral? And does Steady out-earn Nightlife's expected value only modestly, so that the choice is a real trade-off? Give the weekly net per job, by child count.
2. **Saturation.** At §4.2's odds, budget and diminishing returns, how many seasons does `coaching` take to reach 70 and 90 for each job? Recommend the per-season cap and/or the weekly decay toward 50 so that a full phase (about 10 seasons) of good play lands **near, not at,** the ceiling.
3. **The fold and drift.** With trained axes at the end of a phase, what `NurtureBlend` deltas result (potential and interest)? With heir_mechanics §3's regression (`d* = (0.25·m + k) / 0.75` for a nurture shift `k`), is dynasty drift bounded for k = +4 to +6 every generation? Confirm that no §8 band moves when an avatar line keeps training (a 3-generation run). Say whether any `NurtureProfile` constant needs to change; changing one needs an explicit sanction.
4. **The willing threshold.** How much does sustained Easy/Normal training (care +1 a success) move the chance that a child is willing (interest ≥ 40) at 16? Compare with no training, and with Nightlife's −1 care a week.
5. **The tilt and expertise.** Are ±15 pp (drill) and +10 pp (expertise) the right size, so that playing is worth it but autopilot isn't punished? Is the fatigue term double-counting the Steady job's stress?
6. **The D7-A edge cases.** Retirement at 42 with children aged 0–18. How long can the phase then run, and are the ages it allows (an avatar in their 50s training a child) acceptable?
7. **Determinism.** Confirm that the seed rule (§2, §4.2) is reload-stable and has no boot draw.

Carbon writes §9 with findings and amendments; Opus reconciles in §10; the owner then decides §3.
