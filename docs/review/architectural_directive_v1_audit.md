# Architectural directive V1: repository audit and implementation plan

2026-10-26. Requested architecture review; implementation proposals, not completion claims or new scope approvals. Baseline: `update/seed-data-and-docs` at `2da33f9`, including the owner's untracked `docs/ARCHITECTURAL_DIRECTIVE_V1.md`. No runtime changes or test executions accompany this report.

## 1. Findings that change the implementation plan

1. **The four pivots are already accepted in GAME_VISION v3.0; their implementations are not present here.** `docs/design/scope_realignment.md` is the existing SR plan. This audit adds concrete integration and persistence requirements instead of treating its illustrative APIs as implemented code.
2. **This checkout is schema v13.** `Assets/Data/Database/SchemaDefinitions.sql:543` sets version 13. The inspected `p13/integration` tree at `31de035` is v14 and includes schedule, roster, playoff, recruiting and draft tables. Neither tree's DDL contains `Biological_Parentage`, `Indiscretions`, `Active_Effects` or `Player_Identity`. Branch existence and DDL do not prove runtime acceptance. Finish/review the integration chain before branching runtime SR work from it.
3. **Gate sequencing is not fully settled.** Directive §3 says finish Phase 13 before the freeze. `docs/BUILD_PLAN.md:152` explicitly calls the shortened 13-1–13-3 plus L-1-DDL gate a proposal. Default to the directive's full Phase 13 gate; the shorter gate is an optional owner decision, not an approval inferred from this audit. A literal ban on all backend mechanics would also prevent the mandated stance/outcome work: land SR-1a/SR-2a as the named core-freeze package, then freeze, or explicitly approve that bounded exception. Do not hide new mechanics in UI work.
4. **Keep daily simulation; remove daily player scheduling.** `LifeSimManager.cs:554` already charges weekly living costs, including the household board share. Adding an independent upkeep debit would double-charge. `DaySchedule` still carries school, travel, sleep and family accounting.
5. **A synchronous seven-day loop is unsafe.** `TimeManager.cs:64` commits the calendar and queues events; `AdvanceDays` does not settle each day's subscribers. `GameManager._Process` pumps the bus, while narrative polling runs separately. Week advancement must coordinate those boundaries and pending games, not simply call `AdvanceDays(7)` or put a global calendar loop in LifeSim.
6. **Pending narrative choices are not a reliable queue.** `EventConsequenceApplier.cs:162` auto-resolves the previous pending avatar choice when another arrives. `ResolveChoice` clears pending state before applying consequences. Gate 2 needs durable pending choices and failure-safe resolution.
7. **Narrative outcomes need more than parser changes.** `EventChoice` at `GrittyEventModel.cs:409` has one consequence array and one outcome/reaction. `NarrativeLogQueries.cs:51` stores narrative messages in `Game_Logs`. Persist the selected branch and its text; otherwise reloads can show the wrong outcome or reroll a job.
8. **Some commercial inputs are missing.** There is no historical illicit-earnings or peak-wealth ledger in this checkout's schema. `Family_Background.wealth_tier` describes the household background, not proven lifetime peak wealth. The actual contact registry has `unknown` and `the_bookie`, not the existing fence/crew contacts assumed by the SR draft.
9. **Voice ambiguity is resolved by canonical directives:** six individual personas, one per conference, two per tier. Do not interpret “2 pairs per tier” as twelve voices. This checkout has no `Assets/Audio`, `VoiceDirector`, `BroadcastBooth`, or implemented `Tools/VoiceGen`; these are planned systems, not files to delete.
10. **This is Godot, not Unity.** External content does not require rebuilding Unity assets. Existing worktree changes (the directive, theme files, and deleted older directive) are owner work and remain untouched.

## 2. Deprecation and modification manifest

Paths below are repository-relative. Brace lists enumerate individual files; `.cs/.tscn` means both. MODIFY includes necessary wiring changes; FREEZE preserves existing code and checks without exposing the retired path. NEW denotes proposed files, not files found in the repository. No SQLite table should be dropped for these pivots.

### 2.1 Core, life and daily UI

| Disposition | Exact files | Required work |
| --- | --- | --- |
| MODIFY | `Assets/Core/GameManager.cs` | Own stance submission, calendar context, service intents, week advancement, day settlement, durable interrupt state and save/load wiring; replace schedule-driven hustle launch. |
| MODIFY | `Assets/Core/CoreEvents.cs` | Add weekly intent/result DTO events. Game-state keys belong in `Assets/Data/Database/GameStateQueries.cs`. |
| MODIFY | `Assets/Core/TimeManager.cs` | Integrate calendar advancement with the settled-day checkpoint contract; keep existing daily API for consumers/tests. |
| FREEZE internal API | `Assets/Core/GameClock.cs` | Preserve intraday mechanics for legacy tests; production weekly flow must have one clock driver. |
| KEEP | `Assets/Core/{TimeManager,GameCalendar,SchoolCalendar,GlobalState,EventBus}.cs` | Preserve calendar/event semantics; TimeManager has the bounded persistence integration above. Do not add nested bus dispatch or a second simulation clock. |
| MODIFY | `Assets/Simulation/Life/LifeSimManager.cs` | Avatar-only stance inputs, automatic schedule, exactly-once weekly upkeep replacement, per-day summary and existing funds/person settlement. Keep no-stance behavior unchanged. |
| KEEP internal | `Assets/Simulation/Life/{DaySchedule,NeedsEngine,UtilityCalculator,ActionCatalog,SleepProfile,PersonDrift,HouseholdBoard,TravelTime,RelationshipGraph}.cs` | Remove no underlying need/household/travel/family mechanics. Use per-avatar weights at the call site rather than changing global UtilityCalculator defaults. Existing save correctness repairs remain prerequisites. |
| MODIFY | `Assets/Data/Database/{GameStateQueries,NeedsQueries,PersonQueries,PlayerQueries,NarrativeLogQueries}.cs` | Persist/reload new key contracts and authoritative snapshots; reuse existing parameterized funds/needs/person operations, extending only where the atomic checkpoint requires it. |
| MODIFY integration | `Assets/Economy/Family/{FamilyService,ChildRearingService}.cs` | Preserve daily aging/weekly funding and automatic family time; ensure subscription/phone costs are not charged again in stance upkeep. No family-system redesign. |
| RETIRE navigation | `Assets/UI/ScheduleScreen.cs/.tscn` | Remove manual slot editing only after weekly UI is accepted. Preserve files during transition. |
| MODIFY | `Assets/UI/{Main,BurnerPhone,BaseballDashboard,TimeControlBar,TutorialOverlay,TopBar,CalendarScreen,NewGameScreen}.cs` and corresponding existing `.tscn` files; `Assets/UI/TwoPanelShell.tscn` | Remove schedule embedding/node paths, daily controls and need chores; wire weekly intents, summaries, readable game calendar, tutorial and backstory. Main currently reaches ScheduleScreen through the phone's Calendar subtree. |
| MODIFY presentation | `Assets/UI/{UiColors,UiSfx}.cs`, `Assets/UI/Theme/` | Consolidate palette/focus/feedback into the chosen shared theme. Reconcile the owner's untracked theme files; do not overwrite or assume acceptance. |
| BOUNDED MODIFY | `Assets/Economy/Phone/PhoneService.cs`, phone controls in `BurnerPhone.cs/.tscn` | Guarantee core narrative/reply access without minutes chores. Keep plan/upgrade state and existing family minute behavior until its separate pacing decision; the directive criticizes phone minutes but §2 does not authorize deleting every phone mechanic. |

### 2.2 Hustles and narrative

| Disposition | Exact files | Required work |
| --- | --- | --- |
| RETIRE navigation | `Assets/UI/{FencingScreen,RobberyScreen}.cs/.tscn` | Disconnect phone buttons, scene instantiation and signal handlers together. Delete later only after narrative parity; no destructive removal is required for Gate 2. |
| MODIFY | `Assets/Economy/Hustles/HustleService.cs` | Reject production launch of retired activities; keep Build/ApplyFencing and Build/ApplyRobbery methods as frozen harness-compatible code. Preserve WorkActivity numeric values. Reuse shared side-effect conventions. |
| FREEZE | `Assets/Simulation/Hustles/{FencingNegotiation,RobberyHustle}.cs` | Keep balance source and old deterministic checks. Do not run these as hidden interactive state machines behind the new narrative UI. |
| KEEP | `Assets/Simulation/Hustles/{HustleResolution,NarcoticsHustle,HoldemSession,HoldemProfile,HoldemPreflopTable,HoldemHand,HoldemEvaluator,HoldemEquity,HoldemCards,HoldemAgent}.cs` | Retain Narcotics/Hold'em and shared contracts; do not renumber shared enums or refactor poker math. |
| MODIFY launch only | `Assets/UI/{NarcoticsHustleScreen,TexasHoldemTable}.cs/.tscn`, `Assets/UI/HustleFeel.cs` | Launch through service intents with one durable pending session, without requiring manual work-hour scheduling. Keep game-resolution logic. |
| FREEZE unused stubs | `Assets/Economy/Hustles/{NarcoticsTerritoryManager,TexasHoldemLogic}.cs` | Do not build duplicate systems in these placeholders. Deletion is unnecessary. |
| MODIFY | `Assets/Narrative/Events/{GrittyEventModel,GrittyEventJson,EventConsequenceApplier,EventDispatcher,ConditionEvaluator}.cs` | Typed weighted branches, strict validation, safe day polling, pending queue, field/flag evaluation and durable resolution. Preserve legacy choice behavior. |
| MODIFY | `Assets/Data/Database/NarrativePollQueries.cs`, `Assets/Narrative/Events/Content/{dirt_underworld_events,hs_onboarding_events}.json`, `Assets/Narrative/Contacts/contacts.json` | Snapshot allowed stats/flags, add job arcs and one HS onboarding temptation. Use unknown contacts deliberately or add actual registry entries. |
| KEEP | `Assets/Narrative/{Contacts/ContactRegistry,Social/NpcAutonomyService}.cs` | Existing contact parser and NPC cadence remain; validate new content against their contracts. |

### 2.3 Baseball, arcade, audio and tools

| Disposition | Exact files / roadmap scope | Required work |
| --- | --- | --- |
| KEEP authoritative | `Assets/Simulation/Baseball/{MicroGame,AtBatResolver,PitchChain,InteractiveBatterPolicy}.cs` | Existing attended game; no Gate 2 probability changes. Freeze resolver bytes and neutral behavior after Phase 13 calibration acceptance. |
| MODIFY orchestration only | `Assets/Simulation/Baseball/LeagueSimulator.cs` | Only if the existing game-pending/completion and flush seam cannot support the coordinator; expose status through events/DTOs, not LifeSim references. No loop/rating rewrites. |
| MODIFY | `Assets/UI/AtBatView.cs/.tscn`, `Assets/UI/BaseballDashboard.cs/.tscn` | Broadcast scoreboard, intent/input state, watch/skip, result and return-to-week flow. Optional clutch slot stays empty for Gate 2. |
| FREEZE for Gate 2; reuse for SR-3 | `Assets/Simulation/Arcade/{Vec3,ArcadeTuning,BallPhysics,PitchModel,ContactModel,BattedBallModel,ArcadeAtBat}.cs` | Timing/practice only; no full-game rules, fielding or baserunning production. |
| FREEZE then MODIFY | `Assets/UI/Arcade/{PrototypeSettings,PrototypeSession,ArcadePrototype,ArcadeFieldView}.cs`, existing prototype scenes | Later separate unscored practice and clutch overlay. Their physics outcomes must not become authoritative baseball results. |
| CANCEL / SHRINK | G-2/G-3 full arcade game and AI calibration; G-6; G-4; G-5/G-9 | Cancel full-game arcade/fielding work; shrink G-4 to shared ratings/clutch input; merge G-5/G-9 into SR-3 presentation. Preserve MicroGame watch/skip. |
| PAUSE | G-7/G-8 bulk venues; `Tools/SpriteBake/prototype/{stance_axis,render_dirs,render_anim,pixelize,inspect_fbx,field_bake,compose_mock}.py` | Freeze batch production, preserve inspection/one-batter/one-pitcher Golden Slice tooling. Do not delete the baking pipeline. |
| PAUSE then SPECIFY | Phase 14 / SR-9, planned `Tools/VoiceGen`, planned manifest harness, planned `VoiceDirector`/`BroadcastBooth` | Six conference personas; trigger catalog by recklessness, rivalry, public flags, streaks, revealed dynasty. No runtime AI calls. No current C# audio implementation found to prune. |
| PAUSE | Phase 16 motion comics | Gate 2 uses captions/text. Resume after playable loop and visual lock. |
| MODIFY test coverage | `Tools/{NeedsDecayHarness,CoreLoopHarness,GrittyEventsHarness,SchemaValidator}/Program.cs` and respective `.csproj` source includes as needed | Add stance/checkpoint/branch/schema checks without removing legacy tests. Harnesses link source files, so include new pure classes explicitly where required. |
| KEEP baseline; add targeted checks only | `Tools/{MonteCarloHarness,ArcadeHarness,HustleHarness,HoldemHarness}/Program.cs` and `.csproj` | Neutral replay and old hustle/arcade/poker contracts remain. Add balance comparisons to HustleHarness without rewriting old expectations. |
| MODIFY validation specification | `.agents/skills/check_event_graph_integrity/SKILL.md`, `.agents/skills/validate_audio_manifest/SKILL.md` | Traverse outcome branches; recognize stance engine flags; six-persona manifest coverage when audio resumes. The graph skill is instructions, not an existing standalone checker executable. |

### 2.4 SQLite disposition

All entries refer to `Assets/Data/Database/SchemaDefinitions.sql` plus the corresponding query classes. These are row/payload changes unless explicitly identified as Phase 13 DDL; no new stance/crime tables are needed.

| Tables / columns | Action |
| --- | --- |
| `Game_State` key/value | Add versioned keys for stance, effective week, pending week target, interrupt acknowledgement, last upkeep settlement, durable pending narrative/session state and content fingerprint. Persist avatar identity with career-scoped keys; reset on succession. Keep `avatar_stance` compatible with SR naming. |
| `Players.funds`, `recklessness`, `detection_risk`, `health_ceiling` | Keep columns; change authoritative writers to selected outcome/upkeep paths. `detection_risk` is shared exposure, not a new crime-only heat variable. |
| `Life_Needs`, `Life_Stress`, `Player_Person` | Keep schema and internal ticking; hide biological micromanagement. Preserve GPA, school attendance and stress consequences. |
| `Family_Background.wealth_tier`, `Child_Development`, `Child_Rearing_Commitment` | Keep; use existing household subsidies/funding and automatic family blocks. Do not equate background wealth with current wealth or exported lifetime peak. |
| `Entity_Flags` | Keep; mutually exclusive engine-owned stance flags; preserve hot_goods, robbery_bust, spoiled_goods and existing consequence chains. No event may forge ped_active or secrecy/reveal control flags. |
| `Game_Logs.payload` | Add versioned, uniquely identified pending/resolution/job/checkpoint payloads. Query pending records independently of phone history pagination. Store actual branch id/text and actual economic deltas. |
| `Phone_State` including `minutes_remaining`, `Player_Items`, `Player_Equipment` | Keep schema. Remove minutes friction from required story flow without destructively dropping plan/items data. |
| `Relationships`, `Relationship_History`, `Player_Absences` | Keep; jobs still affect faction standing, arrest/injury and availability through existing writers/events. |
| `Batting_Stats`, `Pitching_Stats`, `Player_Ratings`, `Player_Potential`, `Pitcher_Roles`, `Pitch_Arsenals` | Freeze after Phase 13; read career summary data through DTOs. No stance-specific rating columns. |
| `Teams`, `Team_Tiers` plus v14 `Conferences`, `Divisions`, `Player_Fielding`, `Pitcher_Rest`, `Playoff_Series`, `Schedule_Games`, `Team_Seasons`, `Roster_Designations`, `College_Enrollment`, `Recruiting_Offers`, `Draft_Picks` | Complete Phase 13 contracts, then freeze. Weekly coordinator reads calendar/game state through Core; mods validate capacities/FKs and seed new careers only. |
| Planned v15 `Biological_Parentage`, `Indiscretions`, `Active_Effects`, `Player_Identity` | Not existing commercial data sources in either inspected schema. Defer features requiring their runtime to Phase L. Do not introduce them during the freeze merely to support a UI feed. |

## 3. Weekly Lifestyle Stance implementation contract

### Files and API

NEW `Assets/Simulation/Life/LifestyleStance.cs`: stable byte enum CleanLiving/GrindMode/PartyLifestyle/LowProfile, immutable validated profile. NEW `Assets/Data/Life/lifestyle_stances.json`: tunable upkeep multiplier, free-time utility biases, weekly stress/recklessness changes and event propensity. NEW `Assets/Simulation/Life/StanceScheduleBuilder.cs`: build a DaySchedule from a plain Core-supplied day context (mandatory school/practice/game hours, availability, travel, family and job), without referencing Baseball. NEW `Assets/Simulation/Life/WeeklyUpkeep.cs`: pure calculation using current household board rules and the validated profile. NEW `Assets/Simulation/Life/WeekInterrupt.cs`: reason/result types. NEW `Assets/Core/WeekAdvanceCoordinator.cs`: state machine for the global week.

Proposed public service surface: `GameManager.SubmitWeeklyStance(LifestyleStance stance)`; `RequestWeekAdvance()`; `AcknowledgeWeekInterrupt(interruptId)`; `WeekSummaryDto` snapshots/events. The LifeSim surface supplies `SetAvatarStance(profile)` and an automatic day schedule through existing `SetTodaySchedule`; it does not advance the global calendar. Keep `SubmitDaySchedule` as a legacy/internal entry while tests still exercise it. Do not implement the draft's LifeSim-owned global `TickWeek`.

### Settlement and interrupt algorithm

1. Validate and commit the stance plus exactly one stance flag. Show its projected budget before advance. A changed stance becomes effective at the next unprocessed day; lock the upkeep multiplier already used for the current week's settlement. It cannot refund/recharge the same week.
2. Coordinator owns a fixed target boundary and cursor; UI submits an intent once and receives progress. Disable the old hourly driver while this request is active. Never advance while an attended game, hustle, pending narrative, succession or save transition is active.
3. Build each day's schedule from tier calendar and availability. Preserve minimum sleep, commute, school/GPA, practice and family-time constraints. Free hours use stance-biased avatar weights; NPCs and the no-stance path use identical defaults. Legal work is automatic; interactive Narcotics/Hold'em need an explicit service intent and a time-cost/session contract, not unattended auto-play.
4. Advance one day through TimeManager; settle subscribed Life, economy, family, league and narrative work before starting another day. At the coordination boundary, let the bus drain without calling DispatchPending recursively from a handler. Use explicit game-completion signals for worker work. Keep the narrative background poller out of competition with deterministic once-per-day evaluation: a single owner evaluates that day after committed snapshots are available.
5. Replace the existing avatar living-cost debit at `LifeSimManager.cs:554–569`, not supplement it. Charge once per calendar week using a persisted settlement identity; retain NPC behavior. Define included expense categories so food, family funding, phone refill and upkeep do not bill the same expense twice. Do not invent a current wealth-tier progression mechanic during this change.
6. After day settlement, choose the interrupt deterministically: pending player choice, critical stress crossing, funds crossing zero, game waiting for input, then target reached. Game setup must retain its existing pending-input hold; never simulate past Friday's attended game. If several reasons coexist, preserve all in the summary and resolve them in that order without advancing the day again.
7. Stress/funds are crossing conditions with persisted acknowledgement/rearm state, not a permanent `funds <= 0` loop. Start-of-request checks show an existing crisis once. Acknowledgement allows recovery gameplay; renewed deterioration or a new critical event interrupts again. Severe incapacitation/end-of-career retains its own existing restrictions.
8. Stop and checkpoint at every interrupt. Resume the original target and next unprocessed day, not another fresh seven-day period. Calendar rollovers, Sunday roster locks and multi-game College/Pro weeks must remain intact.

### Persistence and determinism

Use existing SQLite batches and prepared statements. Store calendar/checkpoint identity, stance/effective date, upkeep settlement, pending choices and narrative RNG state consistently. The current TimeManager date commit precedes day subscribers, so a collection of unrelated writes is not an atomic day. Before promising crash-safe weekly advancement, finish the accepted R1-SAVE repair: a root day-settlement transaction with participating writes, or a durable idempotent recovery journal. Pick the transaction design where feasible; prohibit nested BEGINs. Publish impulses after successful commit; on failure restore/reload affected in-memory mirrors and leave the day resumable. SaveNow/quit must flush league buffers too.

Separate RNG streams: baseball unchanged, narrative branch RNG persisted, presentation independent. Existing no-stance paths must consume identical draws and produce identical results. New stance balance is assessed separately; active stance effects are allowed to change life/economy, not silently rebaseline the baseball resolver.

Required new checks: split/resumed week equals uninterrupted week; restart at every interrupt; no duplicate upkeep; transaction failure/retry; no skipped school/practice/family/roster day; Friday hold; College/Pro multiple games; year rollover; debt/stress recovery without deadlock; repeated advance clicks; pending hustle/succession; no-stance byte identity.

## 4. Fencing and Robbery narrative implementation contract

### Model and JSON

Extend `EventChoice` with optional `EventOutcome[] Outcomes`. Each outcome has stable `id`, probability definition, consequence array, outcome text and optional delayed reaction. NEW `Assets/Narrative/Events/OutcomeProbability.cs` evaluates typed expressions from an immutable subject snapshot. The allowed field set includes supported player/person attributes and allowlisted faction-standing/flag inputs; add missing snapshot fields through NarrativePollQueries/DTOs rather than arbitrary SQL or reflection.

Keep the existing choice JSON valid. Proposed branch shape: `outcomes: [{ id, p: { base, mods: [{ field, per } or { flag, add }], min, max }, consequences, outcome, text_message }, { id, p: "rest", ... }]`. Branching choices must not also carry choice-level consequences/outcome/reaction: reject ambiguous mixed forms. `autopilot_weight` still chooses a player reply in autopilot; it is not the hidden success probability.

Validation: unique branch IDs; finite numbers; bounds in [0,1]; exactly one residual branch, last; non-residual probabilities total <=1 over allowed input ranges; reject unsafe profiles rather than silently normalize negative residuals. Initially require the sum of branch maxima <=1, which is conservative and easy to prove. Validate every branch's consequences, tokens, contacts, flags, ages and optional cutscene references. New job nodes have at least three choices; retain compatibility with older content whose parser allows fewer.

One narrative RNG draw selects one outcome after the choice is accepted, from pre-consequence values. No `outcomes` means no additional RNG draw. Persist the event-instance ID, selected branch, RNG continuation, consequences and rendered reaction in the same resolution transaction. Duplicate reply submissions return the committed result. Clear pending state only after commit; rollback restores RNG and leaves the choice available. Store a durable FIFO instead of auto-resolving a previous human decision. Cap/coalesce newly fired notifications without dropping unanswered choices.

### Conversion and balance

Robbery arc: lead/target -> preparation/approach -> commitment -> getaway/consequence. Fencing arc: source/lot -> buyer/terms -> commitment -> payment/sting/spoilage. Use flag-gated nodes with cooldowns and explicit terminal cleanup. Not every fencing job requires stolen goods: retain ordinary sourcing routes; only FreshFromAJob requires and consumes hot_goods. Robbery's clean getaway arms hot_goods once. Refusal, bust, spoiled goods and abandonment need terminal paths without rearming rewards.

Extract a balance matrix from `FencingNegotiation.FencingProfile`, source profiles, `RobberyHustle.RobberyProfile`, target/approach probabilities and HustleService side effects. Preserve gross versus net costs, standing and attribute modifiers, stress, exposure, health and availability implications. The SR draft's illustrative $1,800 outcome is not approved balance. Add a Math-reviewed comparison by risk/stat band and representative policy: expected net income per opportunity and per week, loss tail, bust rate, exposure and health cost. A shorter narrative must not multiply earnings by increasing job frequency. Exact interactive RNG sequence parity is not expected; old harness parity remains mandatory.

`GrittyEventJson` currently has no general direct recklessness consequence and only limited relationship targeting. Add narrowly typed operations for the actual needed recklessness delta and resolved fence/crew standing, or reuse an exactly equivalent existing operation; do not pretend a generic person-stat write changes Players.recklessness or a random relationship target is the faction representative. Preserve existing arrest chains so a bust does not both directly arrest and trigger a second duplicate arrest event.

Update `NarrativeLogQueries` and `CoreEvents` to carry actual resolved text/branch metadata; BurnerPhone displays the committed resolution. Add outcome-aware graph validation in NEW `Assets/Narrative/Events/EventGraphValidator.cs`, called from GrittyEventsHarness now and the mod loader later. Check engine-owned flags explicitly; content cannot write arbitrary reveal/stance/PED flags. Remove Fencing/Robbery UI routes only after both arcs pass balance, save/reload and reachability checks.

## 5. Ordered delivery plan and Gate 2 acceptance

Each runtime row needs its scoped brief in `docs/tasks/`, its own worktree from the accepted integration baseline, and the independent reviewer prescribed by TEAM. This report does not start those slices.

| Order | Files / deliverable | Exit and owner |
| --- | --- | --- |
| 0 | Complete Phase 13 via existing briefs: `SchemaDefinitions.sql`, `DatabaseManager.cs`, v14 `SaveCompatibility.cs`/`LeagueStructure.cs`, Baseball query/DTO and league/roster/calendar/playoff/pipeline sources, Steam achievement/presence ripple, existing harnesses | Full directive Gate 1 by default. Sol 6.1 implementation, Astra math/review. Preserve v13 save backup/clean-break consent; do not call v14 a migration that already exists. Record the accepted Phase 13 baseline once. |
| 1 | R1-SAVE dependencies plus `GameManager.cs`, `TimeManager.cs`, query classes and NEW `WeekAdvanceCoordinator.cs`; SR-1a model/parser/validator/applier changes above | Atomic resumable day and narrative resolution; no lost choices or duplicated rewards. Complete before core freeze or under the explicitly named SR exception. |
| 2 | SR-2a life files/profile JSON and SR-2b Core stance wiring; `GameStateQueries.cs`, `CoreEvents.cs` | Automatic weekly lifestyle, checkpointed upkeep and game/event stops. Sol 6.1; Astra validates magnitudes and replay. |
| 3 | SR-1b `dirt_underworld_events.json`, `contacts.json`; `hs_onboarding_events.json`; NEW `Assets/Core/DemoScenario.cs` | One reproducible HS career route using normal worldgen/calendar/events; branch-specific temptation payoff; no baseball outcome rigging. New-career demo flag chooses pacing/content, not alternate sim probabilities. |
| 4 | `Assets/UI/Theme/` assets, `UiColors.cs`, `UiSfx.cs`, existing three screen scenes/controllers; `Main.cs`, `TwoPanelShell.tscn`, `TopBar`, `TimeControlBar`, `TutorialOverlay`, `NewGameScreen`, `CalendarScreen` | Phase 15a/b/c limited to phone, dashboard/standings/stance and AtBatView. Map scene nodes first; keyboard/controller focus, readable feedback, back/continue and modal ownership. Sol 6 UI; Astra review. |
| 5 | `Main.cs`, `BurnerPhone.cs/.tscn`, retired Schedule/Fencing/Robbery scene routes; Narcotics/Hold'em launch wiring | No remaining manual schedule dependency, dead buttons or duplicate sessions. Preserve frozen old harness paths. |
| 6 | NEW `Assets/Core/SessionLog.cs`; GameManager milestone events; UI intent hooks; `Tools/CoreLoopHarness/Program.cs` | Local opt-out session log of boot/backstory/reply/stance/game-start/game-end/consequences timestamps, stalls and build/scenario version. No private lineage or message contents. Run real exported-build cold-start playtests; headless tests cannot establish “zero UI friction.” |
| 7 | Gate 3: selected batter/pitcher sheets, one stadium backdrop, BurnerPhone scene/theme; SpriteBake single-asset workflow | Owner visual acceptance before batch assets. Gate 2 may use coherent temporary art but is not evidence of Gate 3 completion. |
| 8 | Gate 4: remaining HS recruiting/pipeline wiring, `LeagueScreen`/calendar/recruiting surfaces and session logs | Complete ten-game HS season plus recruiting, closed external playtest and owner-operated Steam page/distribution. Commercial SR-6/7/8 and six-voice/Phase 16 production follow. |

Proposed 15-minute budget: boot/create/backstory 0–2 min; phone temptation 2–4; stance/budget 4–5; advance and Friday setup 5–6; attended seven-inning game with watch/skip 6–13; consequence text/standings/weekly summary 13–15. A normal scheduled HS Friday supplies stakes; do not require a new playoff subsystem solely for demo drama. Optional clutch and full pitcher intent are not Gate 2 prerequisites. If pitcher avatars cannot complete the existing route, label the demo's supported positions explicitly and retain full-career behavior outside the demo.

Acceptance requires fresh users to complete those steps unaided within 15 minutes on the exported build, plus save/resume at the reply, interruption and game boundaries; no missing scene nodes, blocked phone access, impossible debt loops, skipped decisions or hidden identity leaks. Record actual timings and observed failures; this report claims none have passed.

## 6. Commercial features: exact file roadmap

### 6.1 Dynasty Blackmail and Tabloid feed (SR-7)

NEW `Assets/Narrative/News/TabloidFeedService.cs`, `PublicScandalDto.cs`, and `Assets/Narrative/News/Content/tabloid_templates.json`. MODIFY `GameManager.cs` to build/inject sanitized read models, `NarrativeLogQueries.cs` for versioned persisted feed items, `BurnerPhone.cs/.tscn` and `BaseballDashboard.cs/.tscn` for News/rumor surfaces. NEW `Assets/UI/TabloidFeed.cs/.tscn` renders those DTOs. No direct DB writes or parentage queries in the widget.

First release uses current detection-risk bands, public event flags, known faction relationships and resolved events. Faction “heat” is not an assumed new column. Coalesce repeated source events and enforce per-day cooldown; use a presentation-only RNG or a hash of the public source identity. Persist the chosen text so reload cannot produce a changing rumor history. This is a read-only feed; actual blackmail payments/choices remain GrittyEvents with ordinary authored consequences.

After L runtime exists, only the planned `LineageQueries` may access Biological_Parentage; it supplies explicitly revealed facts. Indiscretions supplies only public/authorized scandal state. Before reveal, hidden father/child existence must not affect wording, timing, counts or severity. Never implement a lineage investigation progress bar that leaks a secret through its value. Tests compare two saves differing only in unrevealed lineage and require identical visible feed output.

### 6.2 Bloodline Summary and Rap Sheet export (SR-8)

NEW `Assets/Data/Database/CareerSummaryQueries.cs`, `Assets/Core/CareerSummaryService.cs`, `Assets/Core/CareerMilestoneRecorder.cs`, `Assets/UI/CareerSummaryCard.cs/.tscn`, `Assets/UI/CareerSummaryExporter.cs`. MODIFY `SuccessionScreen.cs/.tscn`, GameManager career-end hooks, `GameStateQueries.cs`/`Game_Logs` payload handling, and earnings write seams in `HustleService.cs` and `EventConsequenceApplier.cs`.

Capture an immutable summary before succession changes the avatar. Aggregate hits/at-bats across seasons (not averages of batting averages); distinguish HS/College/Pro titles with existing localization keys. Read only revealed/known children through the authorized lineage facade. Secret heirs remain excluded even after the father's reveal; those are separate unlocks. Cards may say “known heirs,” never infer missing child counts.

Begin versioned counters at career creation for gross illicit receipts, net illicit profit, and peak of the defined wealth measure. Track every relevant debit/credit once in its transaction. Current funds cannot reconstruct lifetime earnings, and background wealth is not peak wealth. Existing careers without evidence display unavailable/“since tracking began”; do not invent historical $400k totals. A new wealth progression system is outside this exporter.

Render the sanitized DTO in a fixed-size themed SubViewport, await render completion, save PNG to `user://exports/` and show/copy the path. No upload, social login or Steam publishing. Export never changes simulation state or RNG; filenames use sanitized IDs/timestamps. Tests cover zero at-bats, retirement/ban/death, repeated export, missing historical counters, Unicode/long names, revealed-child filtering and no RNG/save mutation; visual checks cover clipping and image dimensions.

### 6.3 External mods and Steam Workshop (SR-6)

NEW `Assets/Content/Mods/{ModManifest,ModCatalog,ModValidator,ContentOverlayResolver,ContentFingerprint}.cs`, NEW `Assets/Data/League/RosterCatalogJson.cs`; MODIFY `GameManager.LoadGrittyEventContent`, `Assets/Data/Schools/SchoolCatalogJson.cs`, `Assets/Economy/Items/ItemCatalogJson.cs`, `Assets/Simulation/Baseball/LeagueGenerator.cs`, `GameStateQueries.cs`; reuse NEW `EventGraphValidator.cs`. Add NEW `Tools/ModValidationHarness/Program.cs` and `.csproj` only with implementation of the loader. Workshop adapter: NEW `Assets/Platform/Steam/WorkshopContentProvider.cs`; MODIFY existing `SteamIntegration.cs`, `AchievementManager.cs`, `RichPresenceWriter.cs`; NEW `Assets/UI/ModManager.cs/.tscn` plus Main/phone settings entry.

Manifest contract: stable id/version/content-schema version/compatible game range, dependencies, ordered content declarations, explicit override targets and file hashes. Discover local `mods/<id>/mod.json` and downloaded Workshop roots through the same provider interface. Canonicalize every path under its root; reject traversal, escapes/reparse links, executable code, SQL and network-loaded content. Limit file/pack size, JSON depth, entity/event counts and error output. Parse into temporary catalogs, run all validations together, then publish an immutable accepted content set. Reject an entire invalid mod visibly without a partial override.

Events are additive and namespaced; duplicate IDs fail. Validate contact references, tokens, ages, protected flags, probability ranges and graph reachability using the runtime validators. Schools/items overrides are explicit. Roster packs validate unique IDs, team/conference/division references, ratings ranges, defensive positions, capacity and pitcher/position coverage; team-name mods change display metadata, never topology or tier ordinals. No live-save SQL patching.

Resolve dependencies and load order deterministically, reject cycles/conflicting overrides, sort files ordinally and hash canonical content plus versions/order. New careers store that fingerprint in Game_State. Resume requires the recorded content set; a missing or changed mod cannot silently fall back to vanilla. Offer to restore the matching pack or start a separate career. Workshop updates are staged until explicit activation outside an active career. Offline/local play remains available without Steam.

Persist a career-level modded marker and gate all Steam achievement/stat writes; unloading a mod must not launder an existing modded career. Keep the unmodded shipped-content path and baseline fingerprint unchanged. Workshop integration uses Facepunch only and validates downloaded packs exactly like local packs. Subscription/download support follows local-loader validation; publishing/upload remains the owner's action. Tests include malformed/oversized input, path escape, duplicate IDs, invalid roster/FKs, dependency cycles, deterministic load/hash, missing mods on resume, protected flags, no-Steam boot and achievement gating.

## 7. Regression, freeze and rollback policy

- **Audit validation:** source/DDL/branch inspection only. No build, Monte Carlo run, exported game or live-save inspection was performed. Historical counts in scope_realignment (354 MC, 117 Needs, 363 Gritty, 95 Hustle, 22 Core) are not current test results and must not be used as universal expected totals across branches.
- **Before implementation:** use the schema validation skill on a scratch database for the accepted integration DDL; never infer live-save compatibility from this report. Record the accepted resolver/season fingerprints. Only the Phase 13 design-sanctioned season rebaseline may change its guard; resolver remains byte-exact.
- **Per changed slice:** run build and relevant harnesses once after changes. Life changes: NeedsDecay, CoreLoop and required Monte Carlo skill; narrative changes: GrittyEvents, graph validation, Hustle balance comparison; UI: scene mapper and Godot/export smoke; shared ratings/clutch: Arcade plus Monte Carlo; schema: SchemaValidator and compatibility fixtures; Steam/project/export: native validation. Preserve Holdem checks when its launch path changes.
- **Integration acceptance:** required full regression set including schema, Needs, CoreLoop, Gritty, Hustle, Holdem, Arcade and Monte Carlo. Follow the Monte Carlo skill's mandated replay batch; do not repeatedly rerun a passing unrelated gate. Preserve old expected assertions and add new tests for new behavior rather than deleting checks or loosening tolerances.
- **Failure injection:** stop between day-date write and settlement, before/after upkeep, on outcome commit failure, after message publication, before game flush and at succession. Restored state must produce exactly one reward/upkeep/game result and the same resolved text/next RNG state. Allocation checks cover existing zero-GC baseball paths; JSON/UI allocation remains outside those hot loops.
- **Rollout:** small commits by SR slice; keep a compatibility harness path for legacy scheduling/hustles while production entry points switch. Roll back whole feature commits with their persistence-version handling; never open a new save with older code that ignores pending state. Do not drop old source files or tables as part of the first switch.
- **Open decisions:** strict full-Phase-13 Gate 1 versus the documented shortened proposal; timing of the named SR backend exception; stance/economy Math sign-off; chosen Phase 15 visual direction; demographic/position limits of the demo if existing game input is insufficient; definition of exported wealth and illicit gross/net earnings. None prevents writing this plan, and none is represented here as granted approval.

Recommended next implementation: an assigned Sol 6.1 core-freeze/settlement brief on the accepted Phase 13 baseline, with independent Astra review, followed by SR-1a and SR-2a. No runtime slice, merge, deletion or external publishing was performed by this audit.
