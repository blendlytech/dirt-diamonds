# Task Brief: `13-6c` / `13-6d` — the parent phase: three jobs and backyard training

**Author:** Claude Opus 5.5 (integration lead), 2026-11-27. **Status: DECIDED (owner, 2026-11-27: D1–D12 all A).** Gemini 4 Carbon's Math check (§9, PASS WITH AMENDMENTS) is reconciled in §10, and §§1–5 carry the one signed set of numbers and contracts. 13-6c (engine) may start from `p13/integration` at or after the commit that records these decisions.

**Recommended split (D11-A):** **13-6c, engine:** Claude Opus 5.5 authors it and Gemini 4 Carbon reviews. **13-6d, phone and content:** Claude Sonnet 5.5 builds it and Gemini 4 Neon reviews (Carbon reviews any part that touches a sim, schema or persistence path). This mirrors 13-6a/13-6b. D11-B keeps the owner's 2026-11-24 default instead: Sonnet builds everything and Carbon reviews. **Math:** Gemini 4 Carbon (every magnitude in §4 signed in §9, reconciled in §10). **Owner gates:** D1–D12 before the build; a playtest after 13-6d. **Spec:** [league_restructure.md](../design/league_restructure.md) §9.4 (`:246`, D4b-C); [13-6 brief](13-6_pipeline_draft_brief.md) §3 (`:80–84`), §4 item 8 (`:235–246`), §7 (`:343–350`); [heir_mechanics.md](../reference/heir_mechanics.md) §3, §4.2, §6; [high_school_person_layer.md](../reference/high_school_person_layer.md) §7 (nurture); [scope_realignment.md](../design/scope_realignment.md) §3.2 (stance). **Base:** `ac1a055` (`main` = `p13/integration`, schema v14). Every citation below was verified at `ac1a055`.

## 0. Preconditions for the build sessions

1. Your own worktree (TEAM.md §3) from `origin/p13/integration` at or after the commit that records §3's decisions. Clean tree; the write check passes; at least 2 GB free on `C:`.
2. §3 records the owner's D1–D12, and §9/§10 (Carbon's Math check and its reconciliation) are signed.
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
| The weekly family tick | `ChildRearingService.ProcessDay` (`ChildRearingService.cs:97–153`): every 7 days, `care`/`funding`/`neglect` move from family hours and the funding commitment; child support is charged (`FundsImpulseEvent` after commit, `:151`). Neutral: 3 family hours a week (`CareNeutralHoursPerWeek`, `:47`) and $50/wk funding (`:116–118`); 0 hours gives `care` −2/wk, and 0 hours with $0 funding gives `neglect` +3/wk (`:113–121`). `GameManager.OnChildRearingDayAdvanced` peeks the week's family hours and clears them in the same handler (`GameManager.cs:1425–1436`). **It never writes `coaching`**: only gritty events do (`EventConsequenceApplier.ApplyChildDevelopment`, `:492`) | Training can own `coaching` without fighting the tick |
| The phone's Family tab | `BurnerPhone.RefreshFamilyTab` / `BuildChildCard` (`BurnerPhone.cs:1603–1651`): per-child Care/Coaching/Funding/Neglect bars and the weekly funding slider, refreshed once per day | The parent phase's home screen already exists |
| Decision cards | 13-6b `BurnerPhone.Pipeline.cs`: the engine exposes `TryGetPending…` and `Resolve…`; the card shows a deadline; an unanswered choice resolves by the engine's rule; the clock never stops (`:1–30`, `:301–318`) | The job-choice card's pattern |
| Presence and the League tab | 13-7: `#Status_Family` with the `tier` key cleared (`RichPresenceWriter.cs`); `LeagueScreen` hidden while the tier is unknown (`LeagueScreen.cs:106, 120, 153`) | Already correct for the phase |
| Work and pay | `LegalWork` pays $25/hr (`ActionCatalog.cs:150`); `HustleWork` has the same needs drain and $0 pay (`:167`); `DaySchedule.WorkHours`/`FamilyHours`; `GameManager.GetMandatoryBlocksFor` forces School/Practice/Game (`GameManager.cs:1201–1225`), `SubmitDaySchedule` enforces them (`:1246–1279`) and picks the Work definition (`AvatarWorkIsHustle`, `:1275`; `LifeSimManager.cs:679`). **Only a planned day runs blocks:** an unplanned day ticks 24 free autopilot hours (`LifeSimManager.cs:575–587`), and family hours accrue only on planned days (`:709`) | A job is a forced Work block, like School, but only on a planned day; see A3 (§4.3) |
| The stance seam | `StanceScheduleBuilder.Build(…, bool hasJob)` ([scope_realignment.md](../design/scope_realignment.md) `:130`, SR-2a, not built) | Jobs plug into the stance engine later through `hasJob` |
| Seeded resolvers | Hustles: pure Layer 1 in `Assets/Simulation/Hustles/`, Layer 2 `HustleService` applies results in its own batch with its own RNG, seeded from `TickCount64 ^ 0xD1A5…` outside the boot chain (`GameManager.cs:709–711`). The persisted-once seed precedent is `SeasonScheduler.ScheduleSeedKey` (`GameManager.cs:496–500`) and `PipelineManager.EnsureSeed` (`PipelineManager.cs:191`); derived streams are `PostseasonStreams.MixCombine` chains over ASCII tags (`PostseasonStreams.cs:19–35`, `PipelineRules.cs:164–168, 730–731`) | The resolver/applier split. A new boot draw would shift every later consumer (`GameManager.cs:597–608`), so the parent-phase seed must not take one |
| Events for an unrostered subject | The poll reads a `NULL` team as tier **−1** (`NarrativePollQueries.cs:103`), which defeats every shipped `>=`/`==` tier gate (`GrittyEventModel.cs:92–100`) | Today the parent sees only tier-free events; job content must gate on flags |
| Secrecy | A hidden child has **no `Child` edge** ([temptation_and_lineage.md](../design/temptation_and_lineage.md) `:121, :216`); every pool goes through `LineageQueries.IsKin` (`:288`) | Training lists `Child` edges only, so it is blind to hidden children by construction |
| Current ratings of a child | Born at potential − `ProspectDiscount` (20) × youth weight (`DevelopmentManager.cs:78, 222`; `CareerManager.cs:743–758`). "Unrostered people never develop" (`DevelopmentManager.cs:284`); growth closes `GrowthRate` 0.5 of the gap a season once rostered (`:36`) | A potential bump becomes visible through the heir's first seasons |
| Pacing | `GameClock.MinutesPerRealSecond`: Faster = 120 game-min/s, so one day takes 12 s and a season about 73 min (`GameClock.cs:55–61`); "traversing seasons is Skip Day's job" | **A 12-year phase is about 4,000 Skip Days.** D6 is not optional |

### 1.2 What's missing

1. No jobs: no choice, no wage, no schedule, no consequences.
2. No training: nothing a player does moves `coaching`, and nothing shows a child getting better.
3. No pacing: an undrafted senior is about 22. With a child born in HS or College, the child is about 0–5 at the release, so the phase can run 11–16 seasons with no baseball.
4. No content: at tier −1, almost no gritty event fires, so the phase is quiet.
5. Two 13-6 §7 carry-overs: retirement-age succession still requires 19 (`HeirGeneticsProfile.MaturityAge`, `HeirGenetics.cs:61`; `EvaluateSuccession`, `CareerManager.cs:958–1029`), so a 42-year-old with a 15-year-old willing child gets `NoPlayableHeir` game over, and one whose only child is a young child with interest below 40 gets `NoWillingHeir` (the interest check runs before the age check, `:997–1004`), even though the parent phase's `care` could still win that child over; and `banned_for_life` (`PipelineRules.cs:18`) has no pipeline effect (`PipelineManager.cs:348`).
6. [GAME_VISION.md](../GAME_VISION.md) `:32` still says only "any willing child aged 19+" succeeds. The takeover at 16 isn't in the vision doc.

## 2. Hard constraints

- **Allowed paths (per sub-slice; everything else is read-only):**
  - **13-6c (engine, Opus):**
    - new `Assets/Simulation/Baseball/ChildTraining.cs` (pure rules: ints and `ChildDevelopmentRow`, no DB, no Godot; the `NurtureBlend` profile);
    - new `Assets/Economy/Family/ParentJobService.cs` and `ChildTrainingService.cs` (Layer 2: batches, axis writes, wage and stress impulses after commit);
    - new `Assets/Data/Family/parent_jobs.json` and its loader (job profiles as data);
    - `Assets/Data/Database/GameStateQueries.cs` (new keys only);
    - `Assets/Core/GameManager.cs` (wiring, the job's forced block, intent APIs, the D6 fast-forward), `Assets/Core/CoreEvents.cs` (only if D9 adds an event);
    - `Assets/Simulation/Baseball/CareerManager.cs` and `PipelineManager.cs` **only** for D7-A (the retirement-release hand-off, §4.3 item 4);
    - `Assets/Simulation/Baseball/PipelineRules.cs` **only** for one additive pure overload of `RoleRatingSum` over a role and three tool values (the existing `RoleRatingSum(in RosterPlayerRow)` keeps byte-identical results), for `expertisePp` (§4.2);
    - `Tools/CoreLoopHarness`, `Tools/MonteCarloHarness`, `Tools/PipelineHarness` (new checks);
    - docs: this brief, `progress.md`, spec §9.4, heir_mechanics §6, GAME_VISION `:32`.
  - **13-6d (phone and content, Sonnet):**
    - `Assets/UI/BurnerPhone*.cs` / `.tscn` (the job card and the Family tab's training UI);
    - a new drill scene (`Assets/UI/BackyardDrill.tscn/.cs`);
    - `Assets/UI/Theme/` (variations only);
    - `Assets/Localization/strings.csv` (`ui.family.*`, `ui.job.*`, `family.*` keys; re-import `strings.en.translation` and keep only the translation);
    - new `Assets/Narrative/Events/Content/parent_phase_events.json` (D10);
    - D12-A only: `Assets/Narrative/Events/Content/marriage_and_family_events.json`, adding the subject `age <= 50` condition to the events whose choices `conceive_child` and nothing else;
    - `Tools/MonteCarloHarness` `ui-strings` extension;
    - docs: this brief and `progress.md`.
- **No DDL under D9-A.** v14 stays; the new state is `Game_State` keys, existing `Child_Development` axes and engine-written `Entity_Flags`. Validate every key and column against a **scratch copy** (`validate_sqlite_schema`), never the live save. A D9-B table waits for the v15 freeze gate with L-1.
- **Calibration (GEMINI.md §10):** every magnitude is a Gemini 4 Carbon Math value (signed in §9, reconciled in §10; §4.1/§4.2 hold the final numbers). With **no job, no training session and no parent phase**, every path is bit-identical. The guard world has no avatar, so MC default stays byte-identical outside the lines the slice adds (reconstruction recipe from `c9f2caf8…`). Pro guard PA 81433 / H 18471 / ER 8803, ledger `932ACE26…8B84`. `ActionCatalog`, `NeedsEngine`, `UtilityCalculator` and `NurtureBlend`'s constants stay byte-identical (NeedsDecay 117/117) unless §9 changes a `NurtureProfile` value with an explicit sanction.
- **Boundaries (GEMINI.md §1):** the job is Life/Economy-side (schedule, wage, stress, flags), and training is a pure rules file on the Baseball side applied by an Economy service, exactly like `ChildRearingService` → `Child_Development` → `NurtureBlend`. Life and Baseball exchange nothing but the bus and the DB; the CoreLoop boundary scan stays clean.
- **Presentation never drives the sim** and never uses the sim's `RngState`. The drill scene renders, has its own RNG for its visuals, and sends one intent: a session with a drill score in [0, 1]. The resolver alone draws the result. Skipping the drill or autopilot sends the neutral score (no tilt). This is the Clutch Moment rule (GEMINI.md §11), applied here.
- **No boot draw.** One parent-phase seed, `Game_State['parent_phase_seed']`, is written once on first use by either service (live: `unchecked((ulong)Environment.TickCount64) ^ 0x5452_4149_4E53_4544UL | 1UL`; harnesses: a fixed seed), the `ScheduleSeedKey`/`EnsureSeed` precedent. Every roll is a `MixCombine` stream over it (§4.2, A5): never plain XOR, never `string.GetHashCode()` (randomized per process in .NET). A reload replays the same result, so saving and reloading can't reroll a failed session or a tip week.
- **Secrecy (GEMINI.md §10):** the trainable list is `PlayerQueries.LoadChildrenOf` over `Child` edges: born, adopted in, or reclaimed (L-6, when built). Never a `Biological_Parentage` read. No training UI, text or count may hint at a hidden child.
- **Content lines:** no avatar pronouns (the avatar can be any gender); names in text only. Nothing explicit. Temptation or substance **boosts** stay out of this slice: the adults-only loader guard keys on tier ≥ College and the parent reads tier −1 (Phase L owns that guard). A child is never harmed in play: a failed hard session costs `care` (burnout), never an injury.
- **Text:** every player-facing string is a strings-table key; phone texts resolve `{tokens}` at write time.
- **Styling and focus:** Theme family only; focus navigation on every new control (GEMINI.md §7). The drill is playable by keyboard/controller and honors reduce-motion.
- **Must not block Gate 2.** 13-6c's `GameManager` edits are additive and stay out of SR-2a's `SubmitWeeklyStance`/`AdvanceWeek` names. D6's fast-forward is written so that SR-2a's `AdvanceWeek` can call the same interrupt rules (§4 item 6).

## 3. Owner decisions (D1–D12)

**Status: DECIDED (owner, 2026-11-27): D1-A, D2-A, D3-A, D4-A, D5-A, D6-A, D7-A, D8-A, D9-A, D10-A, D11-A, D12-A.** The numbers below are Carbon's signed §9 values as reconciled in §10 (A1–A5 folded in). The B/C options stay as the record of what was weighed.

**D1 — The three jobs.**
- **A (recommended): Coach, Steady, Nightlife** (profiles in §4.1).
  - **Coach** (a youth and HS assistant coach): $650/wk for 20 hours, 3 training sessions a week with +10 pp drill odds, stress −5 and `care` +0.25 a week. The least money, the best training and the warmest household.
  - **Steady** (shift work): $1,000/wk for 40 hours (the `LegalWork` rate), 2 sessions with −5 pp odds, stress +10 a week. The most money, the least time and the most fatigue.
  - **Nightlife** (a host at a sports bar that trades on your name): $450 plus a seeded $0–$800 tip roll (expected $850; $450–$1,250 a week) for 24 night hours, 3 sessions with −5 pp odds, recklessness +1, stress −3 and `care` −0.25 a week, waived in any week with 5+ family hours. Uneven money, free days and nights away. **Its pitfall is real:** with no training, a child's `care` falls from 50 to 0 over about 4 years, and a child at `care` 0 is willing at 16 only half the time (§9 Q4). Easy training fully offsets it.
  - All three stay solvent with 1–3 children and the $50/wk neutral funding over 12 seasons (§9 Q1); Steady out-earns Nightlife's expected wage by about $15–$30/wk net.
- B: Coach, Steady, and **Own business** (a batting cage: capital up front, uneven income, debt risk) in place of Nightlife.
- C: two jobs and "between jobs" (no wage, all the time in the world, funds pressure).

**D2 — Choosing and changing the job.**
- **A (recommended):** a phone decision card at the release (the 13-6b pattern), with a 14-day deadline. Unanswered, it resolves to **Steady**. The card returns at every season rollover in the phase, so the job can change once a year.
- B: chosen once, for the whole phase.
- C: changeable any week.

**D3 — Job, schedule and stance.**
- **A (recommended):**
  - The job is a **forced Work block** on its shift days, added to `GetMandatoryBlocksFor` like School. Its hours tick the existing `HustleWork` drain ($0), so `ActionCatalog` stays byte-identical and the job never also earns `LegalWork`'s $25/h. `ParentJobService` settles the wage, stress, `care` and flags **weekly**, on the `ChildRearingService` cadence, after commit.
  - **A standing day plan (A3):** while the phase is on, any day the player doesn't plan (including every fast-forwarded day) runs a standing plan: 8 h sleep, the job's shift on shift days, and 1 family hour on three days of each 7-day tick (3 h a week, `ChildRearingService`'s neutral). Without it, an unplanned day skips the job block and the family hours, so fast-forward alone would drain every child's `care` by 2 a week and pile on `neglect` (§9 B1).
  - The non-job days keep the shipped schedule when the player plans them, including the hustles.
  - When SR-2a lands, the same profile feeds `StanceScheduleBuilder.Build(…, hasJob: true)`, and the stance multiplies upkeep on top. Grind + Steady compound fatigue; Party + Nightlife compound vice.
- B: wait for SR-2a and build the jobs on the stance engine only.

**D4 — The mini-game ("Backyard Drills").**
- **A (recommended): a short timing drill.**
  - Three reps on a moving meter: hitting (a front-toss timing window), pitching (a release window) or fielding (a glove window), by the child's role.
  - The drill's score in [0, 1] becomes a **capped, centered tilt** on a seeded success roll. 0.5 is no tilt; the cap is ±15 percentage points (Carbon: the right size). Skipping the drill and autopilot both play at 0.5. Good drills are what carry a Coach child from +3 to +4 potential (§9 Q5); autopilot is never punished.
  - About 20 seconds; skippable; reduce-motion swaps the meter for a three-choice prompt with the same scoring.
- B: choice only (focus + intensity), no skill input.
- C: wait for SR-3b's batting practice and reuse it (couples D4b to Gate 2 work).

**D5 — What training changes.**
- **A (recommended): the existing nurture axes only.**
  - Success raises `coaching`, plus `care` on Easy and Normal (time together). A failed **Hard** session lowers `care` (pushed too hard). Gains accrue in hundredths of a point (basis points), so progress is steady across the whole childhood, capped at +6 `coaching` a season per child, with a slow fade in weeks a child isn't trained (§4.2).
  - At takeover the shipped `NurtureBlend` folds them: coaching moves potential, and care moves interest (cap ±20). That means a trained child is likelier to be **willing** as well as better.
  - **What it's worth (§9 Q2–Q4):** about 10 seasons of Coach + Normal training ends near `coaching` 92: **+3 to every potential rating** on autopilot, **+4** with good drills, up to **+6** with $150/wk funding. Steady with one child: about +3; Steady split across three children: about +1 each. Easy or Normal training makes a child **certain to be willing** at 16 (baseline 97.9%); Hard-only training or untrained Nightlife leaves it a coin flip.
  - No DDL, `NurtureProfile` stays byte-identical (no sanction needed), the identity is already proven, and the fold is already harness-tested. The 3-generation dynasty drift is bounded (fixed point 55–58 for a civilian partner, 65–68 for an elite one) and moves no league band (§9 Q3).
- B: A plus a **readiness** head start: a share of each success also closes the child's current-to-potential gap at the takeover, so the 16-year-old plays better on day one. That needs a new column or table (v15 DDL with L-1) and new Math.

**D6 — Cadence and pacing.**
- **A (recommended):**
  - **The session budget:** a weekly budget set by the job (Coach 3, Steady 2, Nightlife 3), shared by every trainable child (ages 5–15, unrostered, a `Child` edge).
  - **The standing plan:** a per-child focus and intensity (a household setting like the funding slider). It **auto-resolves any unplayed session at week's end at neutral tilt**, so playing the drill is a bonus, never a chore.
  - **Fast-forward:** in the parent phase, two controls, **"Next week"** and **"Next season"**, run the day tick until the target or the first interrupt:
    - a gritty event with choices;
    - stress at or above the threshold;
    - funds at or below 0;
    - a decision card (the job, a pending succession or takeover choice);
    - a child's birthday that opens or closes training (ages 5 and 16).
- B: "Next week" only.
- C: no fast-forward (Skip Day only). Not viable: about 4,000 presses for a 12-year phase.

**D7 — Retirement-age succession (13-6 §7 carry-over).**
- **A (recommended):** a rostered avatar who retires (age 42 or the health floor) with **no** eligible 19+ heir **enters the parent phase** instead of game over (`NoPlayableHeir` or `NoWillingHeir`), when the takeover rule says the family still has a future: a child under 16 (whatever the child's interest today, since the phase's `care` can still win them over), or a willing, unrostered child of 16–18. A willing 16–18-year-old takes over in the same rollover (0 seasons in the phase); a child under 16 takes over at 16. The phase runs 0–15 seasons, and the parent is 42–57 at the takeover (§9 Q6; a health-floor retiree is younger). This is heir_mechanics §6's foreseen "wait for the kid to grow up" mode. Only a save with the pipeline attached gets it, so `NoPlayableHeir` and the `lineage-failure` MC fixtures stay exactly as they are. It's a `CareerManager`/`PipelineManager` change, so Opus authors it in 13-6c and Carbon reviews it.
- B: status quo (`NoPlayableHeir` ends the game).

**D8 — `banned_for_life`.**
- **A (recommended): no forced release.** `dirt_permanent_ban`'s own outcome has the lawyer cut it to a 60-day suspension (`dirt_underworld_events.json:545–571`); the flag is "the mark on your name". In the parent phase the flag **bars the Coach job** (`dirt_blacklisted`, `:573`: "the doors that used to open for you don't anymore").
- B: the flag forces the release path at the next rollover.

**D9 — Persistence.**
- **A (recommended): no DDL.** New `Game_State` keys:
  - `parent_job` (text id);
  - `parent_job_since_day`;
  - `parent_job_pending_since_day` (the card);
  - `parent_phase_seed` (one seed for training sessions and Nightlife tips, A5);
  - `training_week_start_day`;
  - `training_sessions_used`;
  - one text key per child, `training_plan:<childId>` = `focus:intensity:coachBp:careBp:seasonYear:seasonCoachBp` (the standing plan, the signed sub-point carries and the season cap's running total, A2). It is written on first use only, so a child no one trains or no job touches has no key.

  Outcomes go to `Child_Development` (existing), in whole points. Engine-written `Entity_Flags`: `job_coach | job_steady | job_nightlife` and `parent_phase` (the stance-flag precedent), for content gating. No new bus event; the wage and stress ride `FundsImpulseEvent`/`StressImpulseEvent`.
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

**D12 — A conception age cap in the parent phase (new, from §9 Q6).** Nothing caps the parent's age for `conceive_child` (`CareerManager.OnChildConceptionRequested`, `:1710–1743`). If a marriage event conceives a child at 52 and the older child walks away at 16, the newborn stretches the phase until the parent is about 68. A playing avatar retires at 42, so in practice only the parent phase reaches these ages.
- **A (recommended):** a content gate. 13-6d adds the subject `age <= 50` condition (event conditions already read `age`, `GrittyEventJson.cs:195`) to the shipped events whose choices `conceive_child` (`marriage_and_family_events.json`), and the new parent-phase events never conceive. No engine change; Phase L's fertility rules can replace it later.
- B: no cap (long phases are rare and the player can still steer away).
- C: leave it to Phase L's fertility rules.

**TEAM.md §2 rows** (D11-A; added to TEAM.md 2026-11-27):

| Slice | Author | Reviewer | Owner gate |
| :-- | :-- | :-- | :-- |
| **13-6c** parent-phase engine (jobs, training rules, pacing, D7) | Claude Opus 5.5 (+ Gemini 4 Carbon: Math, §9) | Gemini 4 Carbon | D1–D12 |
| **13-6d** parent-phase phone, drill and content | Claude Sonnet 5.5 | Gemini 4 Neon (UI); Gemini 4 Carbon (content gates) | Playtest |

## 4. Outputs

### 4.1 Job profiles (D1-A; data in `parent_jobs.json`; Carbon's signed values, §9 A1)

| Job | Shift days and hours | Weekly wage | Sessions/week | Drill odds | Weekly side effects (`ParentJobService`) | Flag | Gate |
| :-- | :-- | :-- | --: | --: | :-- | :-- | :-- |
| **Coach** | Mon–Fri, 4 h (afternoon) | **$650** ($32.50/h: an HS stipend plus private lessons) | 3 | **+10 pp** (you know how to teach) | stress **−5**; `care` **+25 bp** (+0.25 a week: the kid comes to practice) | `job_coach` | not `banned_for_life` (D8-A) |
| **Steady** | Mon–Fri, 8 h | **$1,000** ($25/h, the `LegalWork` rate) | 2 | **−5 pp** (tired) | stress **+10**; the full `HustleWork` needs drain | `job_steady` | none |
| **Nightlife** | Thu–Sun, 6 h (nights) | **$450 + a seeded $0–$800 tip roll** (expected $850; the `NGHTTIPS` stream, §4.2) | 3 | **−5 pp** | recklessness **+1**; stress **−3**; `care` **−25 bp** (−0.25 a week), waived in a week with ≥ 5 family hours | `job_nightlife` | none |

- **The wage is a fixed weekly amount**, settled on the 7-day tick whatever the calendar's shift count that week (weekdays restart each 365-day season, so one tick a year holds an extra Monday and no Sunday: 6 shifts for Coach/Steady, 3 for Nightlife).
- **The side effects** are weekly: stress rides `StressImpulseEvent` (signed), the wage rides a funds write plus `FundsImpulseEvent` after commit, and recklessness uses the person-stat write `HustleService` already uses. `care` goes to **every** `Child`-edge child under 16 who is unrostered, through the child's basis-point carry (§4.2), including children aged 0–4.
- **Why these numbers (§9 Q1):** an unrostered parent pays the whole $70/wk cost of living and all free-hour spending (board share 1.0), about $520 (Coach), $716 (Nightlife) and $802 (Steady) a week before children. The placeholder $300 Coach spiraled into `FundsDepleted` almost every week, and the placeholder Nightlife (expected $1,050) beat Steady on money and sessions alike. At the signed values every job stays solvent over 12 seasons with 1–3 children and $50/wk neutral funding.
- **Stress (§9 Q5):** passive relief is about 13.6 a day, so a +3 vanished within hours. +10 nudges an already frayed Steady parent toward the ≥ 70 interrupt on payday without touching a calm week.

Reference: weekly child support is $20 a child, plus any funding commitment (`ChildRearingService.cs:37`); $50/wk holds `funding` at its neutral 50, and $0/wk lets it drift down 1 a week.

### 4.2 Training rules (D4–D6-A; `ChildTraining.cs`, pure)

Pure `int` arithmetic throughout (rounding: `MidpointRounding.AwayFromZero`, the `NurtureBlend` discipline). Carbon's signed values (§9 A2, A5).

- **Trainable:** a `Child` edge of the avatar, age 5–15, unrostered.
- **Budget:** sessions a week = the job's count (Coach 3, Steady 2, Nightlife 3), shared by every trainable child; one session trains one child. Autopilot spreads unplayed sessions round-robin over the trainable children, ordered by `player_id`.
- **Odds (integer percentage points, clamped to [5, 95]):** `pPp = clamp(basePp + jobBonusPp + expertisePp + tiltPp − fatiguePp, 5, 95)`.
  - `basePp`: Easy 80, Normal 60, Hard 40.
  - `jobBonusPp`: §4.1 (Coach +10, Steady −5, Nightlife −5).
  - `expertisePp = clamp(round(10 × (peakRoleSum − 135) / 90), 0, 10)`, where `peakRoleSum = max(RoleRatingSum(current), RoleRatingSum(potential))` over the parent's role (`PipelineRules.RoleRatingSum`'s reliever stamina credit, through the pure overload in §2). The peak keeps a 42-year-old's age decline from erasing what they know: 135 or less gives 0, a league-average 150 gives +2, 180 gives +5, 225 or more gives +10.
  - `tiltPp = round(30 × (clamp(score, 0, 1) − 0.5))`: −15 to +15, and exactly 0 at 0.5.
  - `fatiguePp = clamp(round(10 × (stress − 60) / 40), 0, 10)`: 0 at stress ≤ 60, 10 at 100. It stacks with Steady's chronic −5 pp; this one is acute (event spikes), so they don't double-count.
- **Outcome, in basis points (100 bp = 1 axis point):** `scale = clamp((100 − coaching) / 50, 0, 1)` reads the child's current whole `coaching`.

  | Intensity | Success: `coaching` | Success: `care` | Failure |
  | :-- | :-- | :-- | :-- |
  | Easy | `+max(1, round(5 × scale))` bp | +18 bp | nothing |
  | Normal | `+max(1, round(9 × scale))` bp | +12 bp | nothing |
  | Hard | `+max(1, round(15 × scale))` bp | 0 | `care` −25 bp |

  The `max(1, …)` floor removes the integer stalls whole-point gains hit (Easy at 76, Normal at 88, Hard at 94; §9 Q2). No `coaching` gain once the axis is 100.
- **The carry:** `coachBp` and `careBp` live in `training_plan:<childId>` (§3 D9) and are signed. Whenever a carry reaches ±100 or beyond, the whole points move to `Child_Development` through `PersonQueries.AdjustChildAxis` (truncating toward zero), and the remainder stays, so a carry is always within (−100, +100). This is `LifeSimManager.PersonRuntime.Unflushed`'s discipline (`LifeSimManager.cs:58–62`).
- **The season cap:** `SeasonCoachingCapBp = 600` (+6 `coaching` a child a season). `seasonCoachBp` resets when `seasonYear` advances, and a gain is trimmed to what the cap still allows.
- **The fade:** `IdleCoachingDecayBp = 6` (−0.06 a week, about −3 a season). At week's end, every trainable child who got **no** session that week loses 6 bp of `coaching`, never below 50 (no fade at `coaching` 50 with a zero carry).
- **Streams (A5, replacing plain XOR, which repeats 727 of a season's 1,095 seeds, and `string.GetHashCode()`, which changes per process):**

  ```csharp
  public const ulong TrainingStreamTag = 0x4348_4C44_5452_4E47UL; // "CHLDTRNG"
  public const ulong NightTipsStreamTag = 0x4E47_4854_5449_5053UL; // "NGHTTIPS"

  public static ulong HashId64(string id) // FNV-1a 64 over UTF-16 code units
  {
      ulong h = 0xCBF2_9CE4_8422_2325UL;
      for (int i = 0; i < id.Length; i++) { h = unchecked((h ^ id[i]) * 0x0000_0100_0000_01B3UL); }
      return h;
  }

  public static RngState SessionStream(ulong seed, string childId, long day, int sessionIndex)
  {
      ulong h = PostseasonStreams.MixCombine(seed, TrainingStreamTag);
      h = PostseasonStreams.MixCombine(h, HashId64(childId));
      h = PostseasonStreams.MixCombine(h, unchecked((ulong)day));
      h = PostseasonStreams.MixCombine(h, unchecked((ulong)sessionIndex));
      return new RngState(h | 1UL);
  }

  // Tips: MixCombine(MixCombine(seed, NightTipsStreamTag), day) | 1UL, then NextInt(801) dollars.
  ```

  One draw per session (`NextInt(100) < pPp` succeeds). `sessionIndex` is the session's ordinal within its week (0..budget−1). Both tags are distinct from the five `PostseasonStreams` tags and the five `PipelineProfile` tags.
- **Autopilot:** at week's end, each unplayed session resolves under the child's standing plan at a score of 0.5 (no tilt). The plan's default is Normal, in the child's role.

**What the signed rules produce (§9 Q2/Q4; Opus re-ran the integer model and reproduced Coach + Normal exactly):**

| Job and children | Plan | `coaching` after 10 seasons | Potential fold (`funding` 50) | `care` (one child) | Willing at 16 |
| :-- | :-- | --: | :-- | --: | :-- |
| Coach, 1 | Normal (autopilot) | 92.2 (70 at 3.3 seasons, 90 at 8.4) | +3; +4 with good drills | 100 | 100% |
| Coach, 2 / 3 | Normal | 82.3 / 75.0 | +3 / +2 | 100 (each) | 100% |
| Steady, 1 | Normal | 83.5 | +3 (up to +6 with $150/wk funding) | 100 | 100% |
| Steady, 3 | Normal | about 57 | +1 | about 75 (each) | 100% |
| Nightlife, 1 | Easy / Normal | 86.0 / 90.0 | +3 | 100 / about 32 | 100% / 89% |
| Nightlife, 1 | no training | 50 | 0 | 0 | 50% |
| Any job | Hard only | 86–94 | +3/+4 | near 0 | 50–57% |

### 4.3 The engine (13-6c)

1. **`ParentJobService`** (Economy/Family):
   - It reads the job profile.
   - Each week (the 7-day tick) it settles the wage (Nightlife's tips from the `NGHTTIPS` stream), stress, recklessness and the job's `care` bp in one batch, then publishes `FundsImpulseEvent`/`StressImpulseEvent`.
   - **It receives the week's family hours as an argument** (for Nightlife's ≥ 5 h waiver): `GameManager.OnChildRearingDayAdvanced` peeks them once and passes the same value to `ChildRearingService.ProcessDay` and to `ParentJobService` before the clear (`GameManager.cs:1432–1435`). A separate subscriber after it would always read 0.
   - It keeps the `job_*` flags and `parent_phase` (set at the release, cleared at the takeover).
   - The 13-6b-style API: `TryGetPendingJobChoice` / `ResolveJobChoice(jobId)`. The deadline resolves to Steady (D2-A).
   - On the takeover (`AvatarChangedEvent` to a rostered heir), it clears the job, its flags and any pending job card. This includes a card raised and taken over in the same rollover (D7-A with a willing 16–18-year-old).
2. **`ChildTrainingService`** (Economy/Family):
   - The weekly budget, plus `PlayDrill(childId, score)`, which spends one session and resolves it now, and `SetPlan(childId, focus, intensity)`.
   - `PlayDrill` commits the axis flush, `training_sessions_used` and `training_plan:<childId>` in **one batch** on the press, so a reload after a drill sees the result and the spent session, and a reload before it replays the same stream.
   - At week's end, the plans auto-resolve the unplayed sessions (D6-A), and then the idle fade applies (§4.2).
   - Axis writes go through `PersonQueries.AdjustChildAxis` in whole points, from the basis-point carry, one batch per resolution.
   - `TryGetLastResult(childId)` for the UI (in-memory; the text log persists it).
3. **`GameManager`:**
   - The job's hours go into `GetMandatoryBlocksFor` (a new `WorkHours` on `MandatoryBlocks`, shown as read-only rows) on shift days while the phase is on.
   - **The standing day plan (A3, fixing §9 B1):** while `Career.InParentPhase` and `!AvatarScheduleLocked`, whenever `!LifeSim.HasTodaySchedule`, `GameManager` queues the standing plan for `CurrentDay + 1` (the `SubmitDaySchedule` convention) at phase entry, at boot, after each day tick and after `ClearDaySchedule`. The plan is 8 h `Sleep` (`SleepProfile.OptimalHours`), the job's `WorkHours` on shift days, 1 h `FamilyHours` on the days where `day % 7 ∈ {1, 3, 5}`, and `TravelTime.ComputeHours(…)`. The family days follow the tick, not weekdays: weekdays restart each 365-day season, so three fixed weekdays would give 2 or 4 hours in one tick a year, and `RoundAwayFromZero(±0.5)` moves `care` by ±1. Coach and Steady also have only two off-days. The plan goes straight to `LifeSim.SetTodaySchedule` and sets `LifeSim.AvatarWorkIsHustle = true` on shift days. It **never** touches `_plannedWorkActivity`, which would arm a hustle session (`GameManager.cs:1353–1355`). A player's own `SubmitDaySchedule` replaces it for that day.
   - **A planned shift day:** `SubmitDaySchedule` forces the job's `WorkHours` like School and sets `AvatarWorkIsHustle = true`, whatever `WorkActivity` was sent, so the job never also pays `LegalWork`'s $25/h (no double wage). On off-days the shipped behavior is unchanged: `LegalWork` side work pays and hustles arm as today.
   - Intent pass-throughs.
   - **D6 fast-forward:** `AdvanceUntil(targetDay)`, which stops on §3 D6's interrupts and returns a `WeekSummaryDto`-shaped result. SR-2a reuses the interrupt rules.
4. **D7-A (A4, fixing §9 S3; Opus's §10 R2 shape):**
   - **Opt-in:** a `Career.EnableParentPhaseOnRetirement` property, default `false`, set `true` by `PipelineManager` when it attaches. A save or fixture without the pipeline (the MC `lineage-failure` suite, `Tools/MonteCarloHarness/Program.cs:3204–3223`) runs today's code exactly. `EvaluateSuccession` stays read-only and unchanged.
   - **The rule:** in `CareerManager.OnSeasonRolledOver`, in each branch (autopilot, a stale pending choice, interactive), a succession outcome of `GameOver(NoPlayableHeir | NoWillingHeir)` with the opt-in on, **and** `EvaluateTakeover(...)` not `GameOver`, does **not** persist the game-over. It sets an in-memory `RetirementReleasePending` instead. `EvaluateTakeover` already says "a child under 16, or a willing, unrostered 16+ child with ratings", the same rule the pipeline applies at 16, so the entry rule and the takeover can never disagree.
   - **The release:** `PipelineManager.RunRollover`, later in the same `SeasonRolledOverEvent` pump (the avatar is still rostered, so `ActiveAvatarId()` returns them), calls its existing `ReleaseAvatar(tier)` for a pending retirement release, guarded so no later pass touches the avatar again. Everything after that is shipped code: `Move(null)` + `pipeline_retired` + `WriteReleaseStateInBatch` in the rollover batch, every sim and `Micro` re-initialized, `ApplyReleaseState`, `ReactivateAvatar` → `ActivateUnrostered`, `AvatarReleasedEvent`, and `evaluateTakeover = avatarReleased && avatarHasChildren`, which takes a willing 16–18-year-old over in the same pump. No release machinery is duplicated in `CareerManager`, which owns no tier sims.
5. **Docs:**
   - league_restructure §9.4 (`:246`) and §16 (a new I-row);
   - heir_mechanics §6 (with the pipeline attached, a retirement `NoPlayableHeir`/`NoWillingHeir` with a future takeover → the parent phase);
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
| New MC checks (`--only parent-phase`) | | (1) `ChildTraining`: odds clamp at 5/95; `tiltPp` is 0 at 0.5 and ±15 at 0 and 1; `expertisePp` is 0 at 135, +2 at 150, +5 at 180, +10 at 225, from the peak of current and potential; `fatiguePp` is 0 at 60 and 10 at 100; same seed and inputs → same outcomes; diminishing returns monotone; the `max(1, …)` floor gives a gain at every `coaching` up to 99; Easy never lowers an axis. (2) The carry: whole-point flushes truncate toward zero and a carry stays within (−100, +100); the season cap stops at exactly 600 bp and resets with `seasonYear`; the fade never takes `coaching` below 50. (3) Streams: one child over 12 seasons × 3 sessions gives all-distinct `SessionStream` seeds; `HashId64` is a fixed FNV-1a vector (a pinned id → a pinned hash); both tags differ from every `PostseasonStreams`/`PipelineProfile` tag; no `GetHashCode` in either service. (4) The identity: no job, no session → `Child_Development` and `Players` byte-identical through a takeover fold, and no `training_plan:*` key exists. (5) A trained child's takeover folds the trained axes (potential +k, interest +j, exactly `NurtureBlend`'s numbers). (6) Source scans: the trainable list reads `LoadChildrenOf` only, with no `Biological_Parentage`/`LineageQueries` read in either service; the drill scene never references `RngState`; no new `rng.NextUInt64()` in the boot chain. (7) `ui-strings` extended to the new keys (present, none unused, no pronoun). (8) The MC `lineage-failure` suite unchanged and green (no pipeline attached → today's `NoPlayableHeir`) |
| CoreLoop | `dotnet run --project Tools/CoreLoopHarness -c Release` | 43/43 plus: the job card resolves by deadline to Steady; the wage settles once a week and survives a reload; **A3:** a fast-forwarded parent-phase week runs the standing plan (the shift's `HustleWork` drain, 3 family hours, `care`/`neglect` neutral from `ChildRearingService`), and **no `LegalWork` wage** is credited on a shift day, planned or not; a planned shift day forces the job's hours whatever `WorkActivity` was sent, and arms no hustle session; Nightlife's waiver reads the same family hours `ChildRearingService` read; the budget resets weekly; unplayed sessions auto-resolve once; a reload doesn't reroll a resolved session or a tip week; **economy:** 52 fast-forwarded weeks per job with one child and $50/wk funding end solvent with no `FundsDepleted` interrupt, and Steady's net is above Nightlife's expected net (Carbon's §9 Q1 measured in the real loop); fast-forward stops on each interrupt kind; **D7-A:** a 42-year-old with a willing 15-year-old enters the phase and the child takes over at 16; a 42-year-old whose only child is 8 with interest 35 enters the phase (not `NoWillingHeir`); a 42-year-old with a willing, unrostered 17-year-old is released and taken over in the same rollover, with no pending job card left; a 42-year-old whose children are all 16+ and unwilling gets `NoWillingHeir`; the Life ↔ Baseball boundary scan stays clean |
| Pipeline | `dotnet run --project Tools/PipelineHarness -c Release` | Green; the takeover fixtures unchanged except D7-A's new cases (the retirement release runs through `ReleaseAvatar`, and no later pass touches the released avatar) |
| NeedsDecay | `dotnet run --project Tools/NeedsDecayHarness -c Release` | 117/117 (`ActionCatalog` untouched) |
| Gritty | `dotnet run --project Tools/GrittyEventsHarness -c Release` | 363/363 plus the D10 batch loading (D12-A: every `conceive_child` event carries the `age <= 50` condition) |
| Event graph | the `check_event_graph_integrity` skill | clean (new flags set before they're read; no `voice_note`; no tier < N leak) |
| Schema | `validate_sqlite_schema` on a scratch copy | every new key and column reference resolves; no DDL (D9-A) |
| Godot (13-6d) | headless `--import` (keep only the translation) + parity + `BurnerPhone.tscn` and `BackyardDrill.tscn` load with 0 errors | exit 0 |
| Live boot (13-6d) | Godot, scratch `APPDATA` | Drive a save to a release with a 4-year-old child: the job card appears; pick Coach; the Family tab shows the budget; play a drill to a success and a failure; "Next season" stops on a gritty event; a later rollover at 16 hands over to the child with the trained potential |

## 6. Definition of done

- [x] §3 records the owner's D1–D12 (all A, 2026-11-27). §9 (Carbon's Math check) and §10 (Opus's reconciliation) are signed.
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

---

## 10. Reconciliation — Claude Opus 5.5 (2026-11-27)

**Verdict: §9 ACCEPTED, with the A3 and A4 seams reshaped (R1–R6 below).** Every finding stands. I re-checked each load-bearing citation and formula at `a2b9e26` (`Assets/` and `Tools/` byte-identical to `ac1a055`) and folded A1–A5, B1–B2, S1–S3 and N1–N3 into §§1–5, so §§1–5 are now the one set of numbers and contracts for the owner. **No shipped constant changes and no owner calibration sanction is needed.** `Status` stays PROPOSAL until the owner answers D1–D12.

### 10.1 What I re-verified

| Item | How | Result |
| :-- | :-- | :-- |
| **B1** schedule | Read `LifeSimManager.cs:575–587` (an unplanned day = 24 × `TickHour`), `:679` (Work = `LegalWork` unless `AvatarWorkIsHustle`), `:709` (family hours on planned days only), `GameManager.cs:1201–1225, 1246–1279`, `ActionCatalog.cs:150–151` (−$200 over 8 h = $25/h), `ChildRearingService.cs:47, 62–65, 113–121` | **Confirmed.** 0 h gives `care` `round(−1.5)` = −2/wk; 0 h and $0 give `neglect` +3/wk; 3 h gives 0 and −1; the job wage plus `LegalWork` doubles pay ($500/$1,000/$600 a week) |
| **B2** streams | Recounted in Python: plain `day ^ sessionIndex` over 365 days × 3 sessions; decoded every 64-bit tag in `Assets/` (`PostseasonStreams.cs:19–23`, `PipelineRules.cs:164–168`); ran the A5 chain over 12 seasons × 3 children × 3 sessions | **Confirmed.** 368 distinct seeds out of 1,095 (727 repeats). `CHLDTRNG`/`NGHTTIPS` decode correctly and match none of the 10 shipped tags. The A5 chain gave 39,420 distinct seeds out of 39,420 |
| **S1** economy | Checked the inputs: board share 1.0 when unrostered (`GameManager.cs:1014–1017`, `HouseholdBoard.cs:27–28`), $70/wk (`LifeSimManager.cs:112, 569`), autopilot prices (`ActionCatalog.cs:113–126`), funding neutral at $50 | **Inputs confirmed.** I did not re-run Carbon's 12-season upkeep simulation (`diag_upkeep.py` isn't in the repo). The CoreLoop **economy** check (§5) now measures the real loop's nets during the build |
| **S2** training | Recomputed the stalls by hand (Easy 76, Normal 88, Hard 94: `g × (100 − c)/50 < 0.5`) and re-ran the A2 integer model myself (300 runs per cell) | **Confirmed.** Coach Normal, 1 child: 92.2 after 10 seasons, 93.6 after 11, 70 at 3.3 seasons, 90 at 8.4: exactly Carbon's. Nightlife Easy 86.0 (Carbon 86.0); Nightlife Normal `care` 32.6 (31.8); Steady 3 children 56.6 (58.3) |
| **Q3/Q4** fold and willingness | Read `NurtureBlend.cs:56–73` and `HeirGenetics.cs:51–67` | **Confirmed.** Coaching 94 → 3.52 → +4, 93 → +3; 89 at funding 100 → 5.52 → +6; P(willing) at Δ = 0 is 1 − 0.5³/6 = 47/48; at Δ = −20, 1/2 |
| **S3** D7-A | Read `CareerManager.cs:958–1029` (the interest check `continue`s before the age check), `:1411–1467` (`EvaluateTakeover`), `:1757–1812` (the rollover branches), `PipelineManager.cs:326–329, 1527, 1561–1569, 1592, 1780, 1926–1998`, `Program.cs:3200–3223` | **Confirmed**, with the reshaping in R2 |
| **Q5/Q7** | `StressRelaxationPerHour` 0.4 (`LifeSimManager.cs:100`), `GoodNightStressRelief` 4 (`SleepProfile.cs:28`), `StressImpulseEvent` is a signed float (`CoreEvents.cs:121`, `LifeSimManager.cs:489–495`), `RngState.NextInt` exists, `BootCareer` draws unchanged | **Confirmed** |
| **N1–N3** | `GameManager.cs:709–711, 496–500`, `ChildRearingService.cs:97–153, 151`, `GameManager.cs:1201–1225` | **Confirmed**; §1.1 corrected |

Small citation drift, not material: S3's "`:999–1005`" is `:997–1004` at `a2b9e26`, and `Unflushed` is `LifeSimManager.cs:62`.

### 10.2 Reconciliation decisions (R1–R6)

- **R1 — A3's family hours follow the tick, not "three off-days".** Coach and Steady have only two off-days (Sat–Sun). Weekdays also restart each 365-day season (`GameCalendar.cs:79–84`; 365 % 7 = 1), so a tick that crosses the boundary has two Mondays and no Sunday. Three named weekdays would give 2 or 4 hours that week, and `RoundAwayFromZero(±0.5)` moves `care` by ±1 once a year. The standing plan therefore puts 1 h on the days where `day % 7 ∈ {1, 3, 5}`: exactly 3 h in every tick window (§4.3 item 3).
- **R2 — D7-A reuses the pipeline's release instead of duplicating it.** A4 has `CareerManager` flush and re-initialize tier sims and `Micro`, but `CareerManager` owns no tier sims. `PipelineManager.RunRollover` already does the whole sequence for the undrafted/cut release (`ReleaseAvatar` → batch → re-init → `ApplyReleaseState` → `ReactivateAvatar` → `AvatarReleasedEvent`, plus the same-pump takeover). So `CareerManager` only *defers* the game-over (`RetirementReleasePending`), and the pipeline, later in the same pump, calls its own `ReleaseAvatar`. The entry predicate is `EvaluateTakeover(...) != GameOver` rather than A4's hand-written "child < 16 or 16–18 willing". The two agree except that `EvaluateTakeover` also requires the 16+ heir to be unrostered with a ratings row. With A4's version, a willing but rostered 17-year-old would be released and then immediately hit `NoWillingHeir`. A4's opt-in flag (`EnableParentPhaseOnRetirement`) is kept, so the MC `lineage-failure` suite is untouched. This adds `PipelineManager.cs` to 13-6c's D7-A paths (§2). §4.3 item 4 has the shape.
- **R3 — The family-hours hand-off.** §4.3's old "subscribed after `ChildRearingService`" would read 0: `OnChildRearingDayAdvanced` clears the week's family hours in the same handler (`GameManager.cs:1432–1435`). `ParentJobService` now receives the peeked value from that handler, so Nightlife's ≥ 5 h waiver works.
- **R4 — The standing plan never arms a hustle.** It sets `LifeSim.SetTodaySchedule` + `AvatarWorkIsHustle = true` directly and leaves `_plannedWorkActivity` alone. Going through `SubmitDaySchedule` with a non-`LegalWork` activity would queue a narcotics/fencing session (`GameManager.cs:1353–1355`). A player-planned shift day is forced the same way.
- **R5 — One seed key for both streams.** Nightlife tips can fall before any child turns 5. "`training_seed` written on first training use" would leave the tip stream without a seed, so the key is `parent_phase_seed`, written on first use by either service (§2, §3 D9).
- **R6 — `expertisePp` needs a pure `RoleRatingSum` overload.** `PipelineRules.RoleRatingSum` takes a `RosterPlayerRow` (`:253`), and an unrostered parent's potential isn't one. 13-6c adds an additive overload over role + three tools, and the existing method keeps byte-identical results (§2).

Also folded: A1 into §3 D1 and §4.1; A2 into §3 D5/D6/D9 and §4.2; A5 into §2, §3 D9 and §4.2; the pending-succession interrupt into D6; S3's same-pump job-card clear into §4.3 item 1. §5 gains the stream, carry, cap, fade, A3, economy and D7-A cases. **New D12** (from §9 Q6's boundary note): an `age <= 50` content gate on `conceive_child` events.

### 10.3 For the owner

D1–D12 (§3) are open; the recommendation is A throughout. The calls the Math makes real:

- **D1:** Nightlife is the risky job. Untrained, its kids end at `care` 0 and are willing at 16 only half the time. Coach is the best for the kids; Steady is the safest money.
- **D5:** training is worth about **+3 to every potential rating** over a childhood (+4 with good drills, up to +6 with funding), and Easy or Normal training makes the child **certain to be willing**.
- **D7:** the parent phase can last up to 15 seasons, and the parent can be 57 at the takeover.
- **D12 (new):** cap conception at 50 in content, or allow phases into the parent's late 60s.

The build starts only after §3 records the answers.

---

## 11. Build record — 13-6c engine (Claude Opus 5.5, 2026-11-27)

Built on `p13/13-6c-opus` from `fb15873`. Every signed number in §4.1/§4.2 is used as written; **no `ActionCatalog`, `NeedsEngine`, `UtilityCalculator`, `NurtureBlend`, `HeirGeneticsProfile`, `DevelopmentProfile` or `ChildRearingService` constant changed.** Where the build had to choose something §§2–5 leave open, or departs from them, it's listed here for Carbon's review.

### 11.1 Where it lives

| Piece | File |
| :-- | :-- |
| Pure rules: odds, tilt, expertise, fatigue, outcomes, carry, cap, fade, `HashId64`, `SessionStream`, `TipStream`, the `TrainingPlan` record | `Assets/Simulation/Baseball/ChildTraining.cs` |
| R6 overload `RoleRatingSum(role, tool1, tool2, tool3)` (the row overload untouched; MC proves equality on every rostered player) | `Assets/Simulation/Baseball/PipelineRules.cs` |
| Job data and loader | `Assets/Data/Family/parent_jobs.json`, `ParentJobCatalog.cs` |
| Keys (D9-A, no DDL) | `Assets/Data/Database/GameStateQueries.cs` |
| Job card, weekly settlement, flags, takeover clear, standing-plan shape, the D6 interrupt rules (`AdvanceInterruptRules`, `AdvanceProbe`, `AdvanceSummary`) | `Assets/Economy/Family/ParentJobService.cs` |
| Budget, plans, `PlayDrill`, week-end autopilot and fade | `Assets/Economy/Family/ChildTrainingService.cs` |
| Wiring, `MandatoryBlocks.WorkHours`, the A3 standing plan, shift-day forcing, intents, `AdvanceUntil` / `AdvanceToNextWeek` / `AdvanceToNextSeason` | `Assets/Core/GameManager.cs` |
| D7-A: `EnableParentPhaseOnRetirement`, the deferral in all three rollover branches; the release after step 2 through `ReleaseAvatar` | `CareerManager.cs`, `PipelineManager.cs` |

### 11.2 Build decisions (B1–B10)

- **B1 — `training_plan` has a seventh field.** `focus:intensity:coachBp:careBp:seasonYear:seasonCoachBp:weekStart`. `weekStart` is the training week of the child's last session (0 = never). The week-end fade ("a child with no session that week") and the autopilot's spread need it across a reload; an in-memory set would forget a drill on reload. Still one text key per child, still no DDL.
- **B2 — the yearly card keeps the held job when unanswered.** The release's card resolves to Steady (D2-A). The card that returns at each rollover resolves to the job already held (Steady only when that job is now barred), so ignoring the yearly card never changes a Coach into Steady.
- **B3 — one key day per week.** Every session of a training week, played or autopilot, draws `SessionStream(seed, child, weekStartDay, ordinal)`, where `weekStartDay = day − day % 7` and the ordinal is the session's index in the week (0..budget−1). §4.2's `day` parameter is that week-start day. The (week, ordinal) pairs are unique, and waiting a day can't reroll a session.
- **B4 — the autopilot's spread.** The unplayed sessions go round-robin over the trainable children (ordered by `player_id`), continuing across weeks (session g of the phase → child g mod n), children with no session that week first. With no drills played it's exactly a continuous round-robin (Carbon's 2–3-child model).
- **B5 — training needs a job.** No job means a budget of 0, and the week-end does nothing: no autopilot, no fade, no key. That is the identity (MC (4)) and covers the card's 14-day window.
- **B6 — the week-end runs once.** `ChildTrainingService.ProcessDay` returns when the tick has already opened the new week, so a repeated call on a tick day moves nothing (CoreLoop).
- **B7 — fast-forward stops are edge-triggered.** Stress stops the day it crosses 70 (`UtilityCalculator.StressOverrideThreshold`), funds the day they cross 0, and a decision card the day it opens. A parent already over a line, or with a card already open (it resolves itself at its deadline), can still fast-forward. What `CanAdvanceDay` refuses (a gritty choice, a pending succession) blocks before a tick. Two stops beyond D6's list: `GameDay` (SR-2a's; never in the phase) and `AvatarChanged` (the takeover at 16 ends the phase). "Next week" runs to the next 7-day tick (payday); "Next season" to the next season's day 1.
- **B8 — `AdvanceUntil` is synchronous.** It pumps the bus after each day, so it must be called from an intent, never a handler. The gritty dispatcher polls on its own thread, so an event can surface a day or two into a long fast-forward rather than on its exact day. The time bar's intraday clock reset stays with the caller (13-6d).
- **B9 — where the interrupt rules live.** They're pure statics in `ParentJobService.cs`, because §2's 13-6c paths offer no other Godot-free home; SR-2a can move them beside the stance engine.
- **B10 — the D7-A deferral sets `LastSuccession` to `NotTriggered`.** The UI never sees a game-over that didn't happen. `RunSuccessionCheck` (public) is unchanged; only the rollover's own path defers.

### 11.3 Outside §2's paths

- `Tools/GrittyEventsHarness/GrittyEventsHarness.csproj`: its `Economy/Family/*.cs` wildcard would compile the two new services without the Baseball files they read, so they're **excluded** there (a one-attribute change; the Gritty harness tests none of them). CoreLoop and MC compile and test them. Without it the solution doesn't build.

### 11.4 Measured in the real loop (for Carbon)

CoreLoop's economy check: 52 fast-forwarded weeks, one child aged 6, $50/wk funding, from $500, walking:

| Job | Net / week | Lowest funds | Carbon §9 Q1 (1 child, $50) |
| :-- | --: | --: | --: |
| Coach | +$41.3 | **$14** | +$57.7 |
| Steady | +$92.7 | $100 | +$92.4 |
| Nightlife (actual tips) | +$30.8 | $187 | — |
| Nightlife (tips re-centred on $400) | +$58.8 | — | +$62.8 |

All three pass the gate (solvent, no `FundsDepleted`; Steady above Nightlife's expected net). Steady matches Carbon to $0.30. **Coach runs about $16/wk under Carbon's estimate, and its 52-week low is $14**, so a 2–3-child Coach household at $50/wk funding is likely to touch $0. That's a Math call for Carbon (the wage is a signed value; the build didn't change it).

### 11.5 For 13-6d

- A D7-A retiree's `AvatarReleasedEvent` carries the tier left (Pro, or College/HS for a health-floor retiree), and `PipelineBeatWriter` maps every non-College tier to the Pro release text ("The club is letting you go"). A retirement wants its own line (the age is on the Players row; no new event is needed).
- Engine surface: `GameManager.TryGetPendingJobChoice`, `ChooseParentJob`, `LoadTrainableChildren`, `GetTrainingBudget`, `CanTrainChild` (`TrainingBlock` reasons), `PlayDrill(childId, score)`, `SetTrainingPlan`, `ParentJobs.TryGetLastSettlement`, `Training.TryGetLastResult`, `AdvanceToNextWeek` / `AdvanceToNextSeason` / `AdvanceUntil` (`AdvanceSummary.Stop`), `StandingPlanQueued`, and `MandatoryBlocks.WorkHours` for the ScheduleScreen's read-only shift row. Job names and reasons are `ui.job.<id>.*` keys the strings table supplies; `parent_jobs.json` holds no player-facing text.

---

## 12. Independent review — Gemini 4 Carbon (2026-11-28)

**Reviewer:** Gemini 4 Carbon (Simulation, calibration and math owner; primary independent reviewer of Claude Opus 5.5's sim, persistence and schema diffs, [TEAM.md](../TEAM.md) §1/§1a). **Branch / worktree:** `p13/13-6c-review-carbon` in `.worktrees/13-6c-review-carbon` at `740932e`, reviewing `fb15873..740932e` (`23` files changed, `3,973 insertions(+), 13 deletions(-)`; `0` DDL changes; `0` shipped-profile constant changes). **Docs only:** only this section and one session line in [progress.md](../progress.md) edited.

**Verdict: PASS WITH FIXES** — **0 blockers, 2 surgical `GameManager.cs` should-fixes (`S1–S2`), 3 nits (`N1–N3`)**. Every pure math/simulation rule in `ChildTraining.cs`, `PipelineRules.cs`, `CareerManager.cs`, `PipelineManager.cs`, `ParentJobCatalog.cs`, `parent_jobs.json`, and `GameStateQueries.cs` matches §§2–5 and §§9–10 (`0` `Assets/Simulation/` changes needed); build decisions `B1–B10` (§11.2) and `GrittyEventsHarness.csproj`'s exclusion (§11.3) are accepted; and §11.4's real-loop Coach question is resolved below with **zero change to the signed wages (`$650 / $1,000 / $450 + $0–$800`, `0` owner sanctions required)**.

### 12.1 Independent gates reproduced on `740932e`

| Gate | Command | Result |
| :-- | :-- | :-- |
| §0 Preconditions & scope | Empty-commit write check + soft reset; `git status`; `C:` free space; `git ls-files .env`; `git diff --stat fb15873..740932e` | Write check clean; clean tree at `740932e` (`origin/p13/integration = fb15873`); **`4.91 GB`** free on `C:` (`≥ 2 GB`); `.env` untracked; `22/23` files inside §2 (`1` justified `.csproj` exclusion in §11.3, sanctioned below); `SchemaDefinitions.sql`, `ActionCatalog`, `NeedsEngine`, `UtilityCalculator`, `NurtureBlend`, `HeirGenetics`, and `DevelopmentManager` untouched |
| Build (Debug) | `dotnet build DirtAndDiamonds.sln -c Debug` | **0 warnings / 0 errors** (all 10 projects) |
| MC default (run solo) | `dotnet run --project Tools/MonteCarloHarness -c Release` | **646/646** (`623` baseline + `23` `parent-phase`). Normalized (`CRLF → LF`, `\d+(\.\d+)? ms → N ms`) SHA-256 **`67c480c25087f011b4dcaf7b9b19adefded102289748d7c93ee784d1d0c5b6af`** (matches Opus's `67c480c2…`). Removing the `25` added `13-6c` / `parent-phase` lines and restoring `646/646 → 623/623` reproduces **`c9f2caf8a972c0e64288c2fec9a1a28f0d6937cc3fba6eacab6a5cbf45c92aa2`** **byte-exact**. Pro guard **`PA 81433 / H 18471 / ER 8803`**, ledger **`932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`**, D1-A twin `0129DA4404F6`; `16/16` allocation probes **`0 B`**; `0` leaked `%TEMP%\dnd_*` files |
| Resolver guard | `dotnet run --project Tools/MonteCarloHarness -c Release --no-build -- --only resolver` | **13/13** (`0 B`; LF SHA-256 **`6d92422863f22d73c3539306a2a2442a65bcdedf505ddc5bb24611782e9eda89`**, byte-exact) |
| CoreLoopHarness | `dotnet run --project Tools/CoreLoopHarness -c Release` | **61/61** (`43` prior + `18` `13-6c` checks: card deadline, weekly settlement + reload, `A3` standing plan + shift forcing + zero `LegalWork`/hustle leak, Nightlife waiver, weekly budget + idempotent week-end autopilot, pre/post-drill & tip reload determinism, 52-wk economy, fast-forward interrupt edges/order, `D7-A` `15`/`8@35`/`17`/`18@0`, `Life <-> Baseball` boundary scan clean) |
| PipelineHarness | `dotnet run --project Tools/PipelineHarness -c Release` & `-- --full` | Default **13/13** (`10` prior + `3` `D7-A` Pro/College/HS retirement-release fixtures, `52.7 s ≤ 60 s`; `0.0/128` arrival cuts, `128.0` arrivals/yr, `6.00`-yr career, `479` `HCP`, batters `+6.19/+17.49`, pitchers `+5.40/+16.17`, drift `2.07`, twin `89E669FF9CD0BFF1`). `--full` **7/7** (`18.9 s ≤ 240 s`); `0` leaks |
| NeedsDecay / Hustle / Gritty | `NeedsDecayHarness`, `HustleHarness`, `GrittyEventsHarness` (`-c Release`) | **117/117**, **95/95**, **363/363** (D10/D12 content gates belong to `13-6d`) |
| Schema & scratch SQLite | `SchemaValidator` (`-c Release`) + scratch v14 DB (`scratch_s11_4_and_schema.py`) | **174/174** (`user_version = 14`). Scratch v14 check verified all `7` `Game_State` keys (incl. negative `Int64` `unchecked((long)ulong)` `parent_phase_seed` and 7-field `training_plan:<childId>`), `5` `Entity_Flags`, `Child_Development` `0..100` clamps, `Child_Rearing_Commitment`, `foreign_key_check = 0`, `integrity_check = ok`, scratch `.db` deleted |

### 12.2 Verification of `R1–R6` (§10.2), `B1–B10` (§11.2), and §11.3

- **Simulation & stream contracts (`ChildTraining.cs:169–450`, `PipelineRules.cs:269–273`):** Pure `int` percentage/basis points and `MidpointRounding.AwayFromZero` throughout; `PeakRoleSum` (`:308–318`) credits reliever stamina via the additive `PipelineRules.RoleRatingSum` overload (`1,576` generated rows, `315` relievers, `0` mismatches); `CoachingGainBp` (`:348–363`) enforces `Math.Max(1, ...)` up to `99` and `0` at `100`; `AddCoachingGain` (`:398–410`), `FlushWholePoints` (`:417–422`), and `ApplyIdleFade` (`:431–443`) preserve signed carries within `(-100, +100)`, cap seasonal gains at `600 bp`, and floor idle decay at `50` (`5,000` effective bp); `HashId64` (`:246–254`), `SessionStream` (`:261–268`), and `TipStream` (`:271–272`) are process-invariant and collision-free (`13,140 / 13,140` sessions, `625 / 625` tip weeks), with `0` boot RNG draws (`GameManager.cs` keeps `4` `NextUInt64` and `2` `Split` calls) and `0` `Biological_Parentage` / `LineageQueries` reads (`LoadChildrenOf` only in `ParentJobService.cs:372` and `ChildTrainingService.cs:196, 224, 466`).
- **Build decisions `B1–B10` & §11.3:** **Accepted.** In particular, `B1` (`7`-field `training_plan` adding `weekStart`, `:86–113`) keeps D9-A single-key/no-DDL while making reload-safe autopilot ordering and idle fade exact; `B2` (`ResolveAtDeadline` keeping an unbarred held job on yearly rollover cards, `ParentJobService.cs:302–308`) prevents accidental annual resets from Coach/Nightlife to Steady while keeping Steady as the initial release default; `B3–B6` (`weekStartDay = day - day % 7` + weekly ordinal + continuous round-robin + `TrainingWeekStartDay >= day` idempotency, `ChildTrainingService.cs:287–399`) prevent mid-week rerolls and duplicate tick closes; `B7–B9` (`AdvanceInterruptRules` in `ParentJobService.cs:600–646`) match `scope_realignment.md §3.2`'s interrupt order; `B10` (`TryDeferRetirementGameOver` in `CareerManager.cs:1879–1893` + `PipelineManager.RunRollover` `:1528, 1626–1646`) cleanly reuses `ReleaseAvatar` across all three rollover branches while leaving `Pipeline`-less callers unchanged; and §11.3 (`Tools/GrittyEventsHarness/GrittyEventsHarness.csproj:79–81`) is retroactively sanctioned so `GrittyEventsHarness` stays decoupled from `Simulation/Baseball/PipelineRules.cs`.

### 12.3 Math ruling on §11.4 (Coach's real-loop `+$41.3/wk` and `$14` low)

**Ruling: KEEP the signed §4.1 wages (`Coach $650/wk`, `Steady $1,000/wk`, `Nightlife $450 + $0–$800` tips) unchanged (`0` calibration edits, `0` owner sanctions).**

Replaying the exact `R1` loop over both `52` weeks and `624` weeks (`12` seasons, `scratch_s11_4_and_schema.py` & `check_r1_economy.py`) reproduces Opus's `52`-week `CoreLoop` figure to the dime (`+$41.35/wk`, `min = $14.0`, `0` `FundsDepleted`) and isolates both causes:
1. **Why `min = $14` occurs once (Day 13 of Week 1):** `RunParentEconomyChecks` (`ParentPhaseChecks.cs:636`) seeds `startDay = 7` with `$500` cash (`7` full unpaid days — `~$518.74/wk` solvent `R1` single-adult upkeep — before the first `$650` paycheck settles on `day = 14`). Cash dips to `$14` on Day `13`, receives `+$650` on Day `14`, and rises monotonically every week thereafter (`$2,648` at Week `52`; in live play, release happens on rollover `day = 366`, only `5` days before `day = 371`'s first tick, typically with College/Pro savings).
2. **Why Year 1 nets `+$41.3/wk` while Years 2–12 converge to `+$58.1–$59.5/wk`:** During Weeks `1–12`, while cash climbs out of the `$100–$400` band, `UtilityCalculator.FinancialCostScore` (`1 - cost / fundsAvailable`, `UtilityCalculator.cs:77–78`) temporarily penalizes `$40` `SocializeEvening`, allowing `Social` to cross `≤ 20` and trigger `+$19.1/wk` of extra `$25` `DrinkAlone` (`$38.9/wk` across Year 1 vs `$19.8/wk` once solvent). Over a full `12`-season (`624`-week) phase, `R1` Coach `$650/wk` nets **`+$59.5/wk` from `$500` (`+$58.1/wk` from `$1,000`, `min = $704`, `0` `FundsDepleted`)** for `1` child + `$50/wk` funding — matching §9 Q1's `+$57.7/wk` — and nets **`+$38.5–$38.7/wk`** (`2` children + `$50/wk` funding) and **`+$13.3–$14.8/wk`** (`3` children + `$50/wk` funding; `+$56.7/wk` at `$0/wk` discretionary funding). A 3-child Coach household starting at `$500` that also commits `$50/wk` discretionary funding on top of `$60/wk` child support (`$110/wk` child outlay on a 20h stipend) feels genuine early cash pressure (`0–1` `FundsDepleted` stop before savings build), which is the intended `D1-A` trade-off (*"The least money, the best training and the warmest household"*, with Steady `$1,000/wk` or lowering the Family-tab funding commitment available).

### 12.4 Findings

#### Blockers
None (`0`).

#### Should-fixes (`GameManager.cs` + `CoreLoopHarness` only; `0` `Assets/Simulation/` changes)

- **S1 — Interactive takeover (`Career.AutopilotSuccession == false`, live `GameManager.cs:575`) opens and exposes a spurious job card (and transient `parent_phase` flag on `D7-A` `16–18` retirement) while `Career.HasPendingSuccessionChoice` is parked (`GameManager.cs:1578–1596, 1613–1621`, `PipelineManager.cs:1827, 2009, 2027, 2032`, `ParentPhaseChecks.cs:832`):**
  - In live play (`Career.AutopilotSuccession = false`, `GameManager.cs:575`), when `Career.EvaluateTakeover` returns `Succeeded` at rollover — either at age `16` after a parent phase, or on the same rollover as a `D7-A` age-42 retirement with a willing, unrostered `16–18`-year-old child — `PipelineManager.RunRollover` does **not** complete the takeover synchronously (`takeoverHeir` stays `null` at `:1827`). Instead, it calls `Career.ApplyReleaseState(true)` (`:2009`), `Career.ParkTakeoverChoice()` (`:2027`, setting `Career.HasPendingSuccessionChoice = true`), and publishes `AvatarReleasedEvent` (`:2032`).
  - `GameManager.OnParentPhaseRolledOver` (`:1590–1596`), `OnParentPhaseReleased` (`:1578–1587`), and `TryGetPendingJobChoice` (`:1613–1621`) check `Career.InParentPhase` without `!Career.HasPendingSuccessionChoice` (the comments at `:777` and `:1575–1576` assumed synchronous handoff, and `ParentPhaseChecks.cs:832` only tested `d7_17` with `autopilot: true`). Because `ResolvePendingSuccession` (`CareerManager.cs:1350–1377`) only accepts an eligible heir and `CanAdvanceDay` (`GameManager.cs:1182`) blocks day advance until answered, the phase is guaranteed to end before another day can tick; yet while the interactive takeover modal is awaiting the player's click, `OpenJobCard` sets `parent_phase` + `parent_job_pending_since_day`, and `TryGetPendingJobChoice` returns `true` to `13-6d`'s Burner Phone Events card/banner (contrary to `§5:340`).
  - **Fix:** Guard `OnParentPhaseReleased` (`:1580`), `OnParentPhaseRolledOver` (`:1592`), and `TryGetPendingJobChoice` (`:1615`) with `&& !Career.HasPendingSuccessionChoice`, and add an `autopilot: false` `d7_17` assertion in `ParentPhaseChecks.RunRetirementReleaseChecks` verifying `!jobs.HasPendingJobChoice` while `Career.HasPendingSuccessionChoice` is parked (and after `ResolvePendingSuccession(child)`).
- **S2 — Mid-day arrest (`AvatarScheduleLocked`) leaves the pre-queued standing parent plan active on the first jail day (`GameManager.cs:1215–1226, 1532–1570`):**
  - `OnParentPhaseDayAdvanced` (`:1563–1570`) calls `QueueStandingParentPlan()` (`:1532–1549`) at the end of `DayAdvancedEvent(d)`, immediately writing `LifeSim.SetTodaySchedule(...)` (`workHours`, `familyHours`) and `LifeSim.AvatarWorkIsHustle = workHours > 0` for `d + 1`.
  - If a gritty-event `arrest` consequence (`EventConsequenceApplier` → `PlayerAbsenceChangedEvent`) arrests the parent later on day `d`, `AvatarScheduleLocked` (`:1215–1226`) becomes `true` for `d + 1`, but nothing clears the standing plan already loaded into `LifeSim`, so the parent works their shift and logs family hours on their first day in jail (`d + 1`), violating `:1523–1524`'s jail invariant until `d + 2`.
  - **Fix:** Subscribe to `PlayerAbsenceChangedEvent` in `GameManager.BootCareer` (after `Absences.AttachTo(Events)`): when `AvatarScheduleLocked && _standingPlanQueued` (or `AvatarScheduleLocked && LifeSim.HasTodaySchedule`), clear `LifeSim.ClearTodaySchedule()`, reset `LifeSim.AvatarWorkIsHustle = false`, and set `_standingPlanQueued = false` (plus a `CoreLoop` check).

#### Nits (non-blocking; author's discretion)

- **N1 — Payday tick ordering & idempotency symmetry (`GameManager.cs:1487–1495`, `ParentJobService.cs:348–359`):** On `e.Day % 7 == 0`, `ChildRearing.ProcessDay` runs before `ParentJobs.ProcessDay`, so if pre-payday cash is below `weeklyFunding + 20·N` (e.g. Day `14` from `$500`), `AdjustFunds` / `OnFundsImpulse` clamp at `0` before the wage lands (see §12.3; harmless in live play, so optional). Also, `ChildTrainingService.ProcessDay` (`:322`) persists a same-tick repeat guard (`TrainingWeekStartDay >= day`) whereas `ParentJobService.ProcessDay` relies on `DayAdvancedEvent` firing once per day.
- **N2 — `TrainingPlan.LastSessionWeekStart = 0` sentinel vs `WeekStartFor(1..6) == 0` (`ChildTraining.cs:55, 76–77`, `ChildTrainingService.cs:134, 389, 431`):** `WeekStartFor(day)` is `0` on `days 1..6`, matching `TrainingPlan.Default`'s `0` ("never trained") sentinel. Unreachable in live play (release requires `day >= 366`, `week >= 364`); leave `ChildTraining.cs` untouched unless `Assets/Simulation/` is already opened later.
- **N3 — `GrittyEventsHarness.csproj` (§11.3) & `AdvanceUntil` `InteractiveGameInFlight` (`GameManager.cs:1180, 1714–1715`):** `GrittyEventsHarness.csproj`'s `Exclude` is sanctioned. In `GameManager.AdvanceUntil`, `BlockingStop` omits `CanAdvanceDay`'s `InteractiveGameInFlight` check (`:1180`); unreachable in the parent phase (no games), worth passing `InteractiveGameInFlight` into `GamePending` (`Career.HasPendingGame || InteractiveGameInFlight`) so SR-2a inherits the full `CanAdvanceDay` contract.

**Recommended next:** Claude Opus 5.5 reconciles §12 (`S1`, `S2`, and optional `N3` in `GameManager.cs` + `ParentPhaseChecks.cs`; `0` `Assets/Simulation/` edits), Gemini 4 Neon (or Gemini 4 Carbon per `TEAM.md §1a`) runs the quick delta check on that `GameManager.cs` patch, and Opus merges `13-6c` `--no-ff` into `p13/integration` before `13-6d` (Claude Sonnet 5.5).

## 13. Reconciliation of §12 — Claude Opus 5.5 (2026-11-28)

**Branch:** `p13/13-6c-opus`, fast-forwarded `740932e → c408835` (Carbon's §12, docs only), then this patch. **Scope:** `Assets/Core/GameManager.cs` and `Tools/CoreLoopHarness/ParentPhaseChecks.cs` only. `Assets/Simulation/`, `Assets/Data/`, `SchemaDefinitions.sql` and every shipped constant are byte-identical to `c408835`.

**Citations spot-checked at `c408835`:** all hold. S1: `GameManager.cs:575` sets `AutopilotSuccession = false`; `PipelineManager.cs:1827` assigns `takeoverHeir` only on autopilot, and the interactive branch parks the choice (`:2027`) before it publishes `AvatarReleasedEvent` (`:2032`); `ResolvePendingSuccession` (`CareerManager.cs:1350–1377`) only accepts a listed heir and calls `TakeOver` in the phase; the existing `d7_17` check (`ParentPhaseChecks.cs:832`) ran `autopilot: true` only. S2: `QueueStandingParentPlan` reads `AvatarScheduleLocked` only when it queues, so an arrest published later the same day left the queued shift in place.

| Finding | Ruling | Change |
| :-- | :-- | :-- |
| **S1** | **Fixed** | `OnParentPhaseReleased` (`GameManager.cs:1604`), `OnParentPhaseRolledOver` (`:1616`) and `TryGetPendingJobChoice` (`:1639`) also require `!Career.HasPendingSuccessionChoice`. The parked choice must name the heir and `CanAdvanceDay` blocks until it does, so no card is needed or shown: the takeover's `AvatarChangedEvent` → `ClearForTakeover` runs as before. Mirrored in `ParentWorld` and the D7-A fixture's handlers. New check `d7_17_choice` (`ParentPhaseChecks.cs:898`, `autopilot: false`): while parked, no card and no `parent_phase` flag; after `ResolvePendingSuccession(child)`, the child is the avatar, out of the phase, with no card, job or flag. |
| **S2** | **Fixed** | New `OnParentPhaseAbsenceChanged` (`GameManager.cs:1582`), subscribed at `:797`, after `Absences.AttachTo` (`:456`), so the ledger already holds the arrest. When the standing plan is queued and the avatar is now locked, it clears the plan, resets `AvatarWorkIsHustle` and drops `_standingPlanQueued`. It acts on the **standing plan only**: a player-set plan keeps the 8c rule, so nothing changes outside the parent phase. `ParentWorld` gains the ledger, `AvatarScheduleLocked`, the guard in its `QueueStandingParentPlan` (a replica gap: the ledger was missing there) and the handler. New check (`ParentPhaseChecks.cs:479`): the arrest lands after a shift day's plan was queued; both jail days tick with no plan and the plan returns on release day. |
| **N1** | Declined | The order (child rearing, then the job, on the peeked hours) is R3 as signed; reordering moves only the harness's day-7 / $500 start (live release is day ≥ 366 with savings) and would re-cut the §12.3 economy Carbon just ruled on. `DayAdvancedEvent` fires once per day, so a repeat-tick key would add a write per tick for a case the clock can't produce. |
| **N2** | Left | Unreachable (release needs day ≥ 366); `Assets/Simulation/` stays byte-identical. |
| **N3** | **Fixed** | `AdvanceUntil` passes `Career.HasPendingGame \|\| InteractiveGameInFlight` (`GameManager.cs:1740`), so it inherits `CanAdvanceDay`'s full contract. The replica has no interactive game (its probe input stays `false`). |

**Gates:** Debug build **0 / 0**. CoreLoop **63/63** (61 + the two new checks). Negative control: removing the S1 guard from the fixture and the S2 subscription from `ParentWorld` fails exactly those two (61/63); the file was restored byte-exact (SHA-256 prefix `d47b07c3fb89b317` before and after). MC solo **646/646**, normalized SHA-256 **`67c480c25087f011b4dcaf7b9b19adefded102289748d7c93ee784d1d0c5b6af`** (unchanged); the reconstruction gives **`c9f2caf8a972c0e64288c2fec9a1a28f0d6937cc3fba6eacab6a5cbf45c92aa2`** byte-exact; Pro guard PA 81433 / H 18471 / ER 8803, ledger `932ACE26…8B84`; 0 leaked `%TEMP%\dnd_*`. Nothing else compiles `GameManager.cs` or the changed harness file, so the other harnesses are as reproduced in §12.1.

**Next:** Gemini 4 Carbon delta-reviews `c408835..<this commit>` (TEAM.md §1a: Opus-authored orchestration in `GameManager.cs`); then Opus ticks §6 box 2 and merges 13-6c `--no-ff` into `p13/integration`; then 13-6d (Claude Sonnet 5.5).

---

## 14. Delta review of §13 — Gemini 4 Carbon

**Reviewer:** Gemini 4 Carbon ([TEAM.md](../TEAM.md) §1/§1a). **Branch / worktree:** `p13/13-6c-rereview-carbon` in `.worktrees/13-6c-rereview-carbon` at `aeaa04a`, delta-reviewing `c408835..aeaa04a` (`4` files changed, `129 insertions(+), 12 deletions(-)`; only `Assets/Core/GameManager.cs`, `Tools/CoreLoopHarness/ParentPhaseChecks.cs`, `docs/tasks/13-6c_parent_phase_brief.md`, and `docs/progress.md`). **Docs only:** only this section and one session line in [progress.md](../progress.md) edited.

**Verdict: PASS** — **0 blockers, 0 should-fixes, 0 nits.** `13-6c` is approved to merge into `p13/integration`.

### 14.1 Rulings on §12.4 / §13 items and open item

- **Scope:** `git diff --stat c408835..aeaa04a -- Assets/Simulation Assets/Data Assets/Data/Database/SchemaDefinitions.sql` is empty (`0` files). Every shipped constant and simulation/schema/data file is byte-identical to `c408835`.
- **S1 — FIXED (`GameManager.cs:1604, 1616, 1639`; `ParentPhaseChecks.cs:291, 302, 824, 831, 898–915`):**
  - `OnParentPhaseReleased` (`GameManager.cs:1604`), `OnParentPhaseRolledOver` (`:1616`), and `TryGetPendingJobChoice` (`:1639`) all guard `!Career.HasPendingSuccessionChoice`, mirrored in `ParentWorld` (`ParentPhaseChecks.cs:291, 302`) and the D7-A `Compose` subscribers (`:824, 831`).
  - **Takeover clear intact:** Answering `Career.ResolvePendingSuccession(heirId)` (`CareerManager.cs:1350–1374`) clears `_hasPendingSuccession = false` (`:1369`) and runs `TakeOver` → `CompleteTakeover` → `ActivateAvatar` (`:1584–1621, 930`), publishing `AvatarChangedEvent(heirId, hsTeamId)` (`hsTeamId != 0`), so `OnParentPhaseAvatarChanged` (`GameManager.cs:1626–1628`) still runs `ParentJobs.ClearForTakeover` after any multi-season parent phase.
  - **Cannot swallow a legitimate job card:** `CareerManager.OnSeasonRolledOver` resets `_hasPendingSuccession = false` at the top of every parent-phase rollover (`CareerManager.cs:1801–1805`), D7-A deferral leaves `_hasPendingSuccession == false` (`:1836–1841, 1878–1892`), and `PipelineManager.RunRollover` calls `Career.ParkTakeoverChoice()` (`PipelineManager.cs:2025–2028`, `CareerManager.cs:1503–1511`) which sets `_hasPendingSuccession = true` *only* when `EvaluateTakeover` returns `Succeeded`. On rollovers where all children are `< 16` or `16+` children are unwilling alongside a younger `< 16` sibling (`EvaluateTakeover` → `NotTriggered`, `CareerManager.cs:1495`), `HasPendingSuccessionChoice` stays `false` and the card opens normally (covered by `d7_8` at `ParentPhaseChecks.cs:873–881`); when all children are `16+` and unwilling (`GameOver(NoWillingHeir)`, `PipelineManager.cs:1842–1846, 2017–2020`), the phase and lineage end. New `d7_17_choice` (`ParentPhaseChecks.cs:898–915`, `autopilot: false`) verifies both the parked wait and post-`ResolvePendingSuccession` cleanup.
- **S2 — FIXED (`GameManager.cs:456, 797, 1582–1592`; `EventBus.cs:49, 165–171, 197–200`; `ParentPhaseChecks.cs:38, 168–169, 191–198, 277–287, 479–507`):**
  - `EventBus` appends handlers per channel (`EventBus.cs:165–171`) and `Channel<T>.DispatchOne` invokes them in subscription order (`:197–200`). `Absences.AttachTo(Events)` (`GameManager.cs:456`, `AvailabilityLedger.cs:89, 134–152`) subscribes well before `OnParentPhaseAbsenceChanged` (`GameManager.cs:797`), so the ledger merges the arrest before `OnParentPhaseAbsenceChanged` (`:1585`) evaluates `AvatarScheduleLocked` (`:1219–1230`).
  - **Outside-phase & player-plan identity:** `_standingPlanQueued` is set `true` solely in `QueueStandingParentPlan` (`GameManager.cs:1538, 1552`, which requires `Career.InParentPhase`) and cleared by `SubmitDaySchedule` (`:1333`). Outside the parent phase `_standingPlanQueued` is identically `false`, so `OnParentPhaseAbsenceChanged` returns at its first test (`:1584`), and inside the phase a player-submitted day plan preserves Phase 8c's contract. `ParentWorld` now mirrors `Absences`, `AvatarScheduleLocked`, the `QueueStandingParentPlan` lock guard (`ParentPhaseChecks.cs:197`), and the handler, verified across arrest + 2 jail days + release recovery by `RunArrestDropsStandingPlanCheck` (`:479–507`).
- **N3 — FIXED (`GameManager.cs:1183–1186, 1738–1740`):** `AdvanceUntil` passes `Career.HasPendingGame || InteractiveGameInFlight` into `AdvanceInterruptRules.BlockingStop`, matching `CanAdvanceDay`'s full contract for SR-2a reuse.
- **N1 — DECLINE ACCEPTED:** Keeping `ChildRearing.ProcessDay` ahead of `ParentJobs.ProcessDay` (`GameManager.cs:1491–1496`) preserves R3 and the §12.3-verified economy trajectory (relevant only to the harness's synthetic `day = 7, $500` start; live releases occur at `day >= 366`), and omitting a redundant weekly repeat-tick `Game_State` write in `ParentJobService.ProcessDay` (`ParentJobService.cs:348–359`) avoids pointless per-week DB churn for an event `TimeManager` emits once per calendar day.
- **Open item ruling (jailed parent's weekly wage settling during arrest) — ACCEPTABLE AS-IS (not a 13-6d engine item):** `ParentJobService.ProcessDay` (`ParentJobService.cs:348–359`) settles a flat weekly wage regardless of shift count (§4.1 `:208`), and `13-6d` (`§2:65–72`) is UI/content-only (`ParentJobService.cs` and `GameManager.cs` are read-only there). Skipping settlement on `day % 7 == 0` when jailed would create an arbitrary calendar cliff (5 jailed Mon–Fri days paid in full vs 1 payday arrest losing the entire week's `$650–$1,000`), while shift-proration would require new persisted state beyond D9-A. With `S2` active, jail days already cut `FamilyHoursThisWeek` (costing `ChildRearingService` `care` or Nightlife's `≥ 5h` waiver) while upkeep and child outlays keep running, and gritty arrest events price bail/fines directly via negative `funds` consequences (`EventConsequenceApplier.cs:457–479`). Any `13-6d` D10 event depicting lockup or unpaid missed shifts can price that hit directly in its choice `funds` consequences.

### 14.2 Gates reproduced on `aeaa04a`

| Gate | Command | Result |
| :-- | :-- | :-- |
| Preconditions & scope | Empty-commit write check + soft reset; `git status`; `git log -4`; `git ls-files .env`; `origin/p13/integration`; `C:` free space; `git diff --stat c408835..aeaa04a` | Write check clean; clean tree at `aeaa04a` (parent `c408835`); `origin/p13/integration = fb15873`; `.env` untracked; **`3.73 GiB`** free on `C:`; only the `4` expected files touched (`0` `Assets/Simulation`, `Assets/Data`, or `SchemaDefinitions.sql` paths) |
| Build (Debug) | `dotnet build DirtAndDiamonds.sln -c Debug` | **0 warnings / 0 errors** (all 10 projects) |
| CoreLoopHarness + negative control | `dotnet run --project Tools/CoreLoopHarness -c Release` | **63/63** passed. Negative control (dropping `&& !w.Career.HasPendingSuccessionChoice` at `ParentPhaseChecks.cs:824` and `Bus.Subscribe<PlayerAbsenceChangedEvent>` at `:169`) failed **61/63** — **only** the `2` new S1/S2 checks failed; reverted and verified byte-exact (`git hash-object` `1ea030a8ef81aa18109d7edf59f74f9bf71bc858`), re-verified **63/63** |
| MC default (run solo) | `dotnet run --project Tools/MonteCarloHarness -c Release` | **646/646** (`parent-phase: 23/23`). Normalized SHA-256 **`67c480c25087f011b4dcaf7b9b19adefded102289748d7c93ee784d1d0c5b6af`** (exact). Deleting the `25` `13-6c` / `ok parent-phase` lines and restoring `623/623` gives **`c9f2caf8a972c0e64288c2fec9a1a28f0d6937cc3fba6eacab6a5cbf45c92aa2`** (byte-exact). Pro guard **`PA 81433 / H 18471 / ER 8803`**, ledger **`932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`**; `0` leaked `%TEMP%\dnd_*` files |

**Recommended next:** Claude Opus 5.5 fast-forwards `p13/13-6c-opus` to this commit, ticks §6 box 2, and merges `13-6c` `--no-ff` into `p13/integration`; then `13-6d` by Claude Sonnet 5.5 (Gemini 4 Neon reviews UI, Gemini 4 Carbon reviews content gates).
