# ARCHITECTURAL & PRODUCT DIRECTIVE: *Dirt & Diamonds* Scope Realignment

## 0. META-INSTRUCTIONS FOR AI ARCHITECTS (Claude Opus / Gemini Pro / GPT Pro)
* **Role:** Lead Systems Architect & Technical Producer.
* **Objective:** Ingest the product critique and architectural adjustments below, cross-reference them against the existing *Dirt & Diamonds* codebase/roadmap, and generate a deterministic, phased **Refactoring & Execution Plan**.
* **Core Constraint:** Preserve the existing high-performance C# backend architecture (Zero-GC simulation loops, pooled SQLite prepared statements, strict Data/Simulation/Presentation assembly isolation, and Monte Carlo validation harnesses) while aggressively pruning front-end bloat, micromanagement friction, and high-risk asset pipelines.
* **Required Output from Reviewing Model:**
  1. **Deprecation & Pruning Manifest:** Exact files, services, SQLite tables/columns, and roadmap phases to freeze, deprecate, or delete.
  2. **System Refactoring Specs:** Concrete C# interface changes, data-flow updates, and JSON schema modifications required to implement the architectural pivots below.
  3. **Updated Phase Sequence:** A re-ordered milestone plan prioritizing the "15-Minute Steam Next Fest Loop" and Phase 13/15 integration.

---

## 1. EXECUTIVE SUMMARY & RISK DIAGNOSTIC

* **Current Commercial Viability (Unchanged Trajectory):** 20–25%
* **Target Commercial Viability (Post-Refactor):** 60–70% (Cult-Hit / Niche Profitability)
* **Core Diagnosis:** The backend C# simulation and SQLite data layer are over-engineered to a 99th-percentile standard, while the front-end player experience suffers from severe scope sprawl. 

### Primary Failure Modes to Eliminate
1. **The "Phase G" Arcade Pivot Trap:** Transitioning attended games from the `MicroGame` Markov chain to a deterministic real-time physics arcade engine (`Assets/Simulation/Arcade/`) introduces multi-year physics/animation polish risks that threaten to tank Steam reviews if movement or bat-to-ball contact feels stiff.
2. **Gameplay Loop Whiplash & Micromanagement Fatigue:** Forcing the player to manage 5 decaying biological needs (`Hunger`, `Sleep`, `Hygiene`, `Social`, `Fitness`), hourly schedules (`DaySchedule`), phone minutes (`PhoneService`), 4 standalone criminal mini-games, Burner Phone events, and baseball gameplay creates severe pacing friction in a multi-generational dynasty sim.
3. **Art Pipeline Fragility ("Frankenstein" Aesthetics):** Combining Rodin Gen-2.5 AI 3D meshes -> Mixamo rigging -> 16-bit sprite baking (`Tools/SpriteBake`) with layered PNG motion comics (Phase 16) and a UI overhaul (Phase 15) creates high visual inconsistency and Steam AI-disclosure scrutiny.
4. **Audio Production Bloat:** Generating and validating 59 distinct announcer voices (56 College/Pro + 3 HS) via offline ElevenLabs without spoken player names/pronouns yields massive file management overhead with repetitive in-game output.

---

## 2. MANDATORY ARCHITECTURAL PIVOTS (ACTION ITEMS)

### Pivot 2.1: Demote/Scope-Lock the Real-Time Arcade Engine (`Phase G`)
* **Directive:** Retain the Markov-chain `MicroGame` (pitch selection, location/timing intent, high-tension broadcast presentation) as the **primary, authoritative way to play attended games**.
* **Constraint on `Assets/Simulation/Arcade/`:** 
  * Do **NOT** build full 9-inning real-time fielding, baserunning, and physics simulation.
  * If `Assets/Simulation/Arcade/` is retained, restrict its scope strictly to an optional **"Clutch Moment / Batting Practice" timing resolver** (offense-only or high-leverage at-bat pitch/swing timing, similar to *MLB Power Pros* or *Retro Bowl*) that feeds its outcome directly back into the `MicroGame` state machine.

### Pivot 2.2: Abstract Daily Needs into Weekly Lifestyle Stances
* **Directive:** Eliminate hourly/daily manual chore-clicking for biological needs (`Hunger`, `Sleep`, `Hygiene`, `Social`, `Fitness`) and `DaySchedule` slot-filling.
* **System Refactor (`LifeSim` / `DaySchedule`):**
  * Replace daily need decay actions with a **Weekly Lifestyle & Budget Stance** selector (e.g., *Clean Living*, *Grind Mode*, *Party Lifestyle*, *Low Profile*).
  * Automatically resolve weekly upkeep against `Funds` and `WealthTier` during the weekly tick.
  * Only interrupt the player's core loop when boundary conditions are breached: `Stress` spikes above threshold, `Funds` drop to zero/debt, or a `GrittyEvent` triggers.

### Pivot 2.3: Consolidate Criminal Hustle Mini-Games (`HustleService`)
* **Directive:** Reduce standalone interactive crime UIs from 4 to 2.
* **System Refactor:**
  * **RETAIN & POLISH:** **Narcotics Turf Wars** and **Texas Hold'em** as the two flagship interactive mini-games.
  * **DEPRECATE UI / CONVERT TO NARRATIVE:** Remove standalone interactive state-machine UIs for **Fencing** and **Robbery**. Refactor Fencing and Robbery into high-stakes branching narrative nodes executed directly through the **Burner Phone** (`GrittyEventJson`) using existing skill/attribute checks, `Recklessness`, and `Entity_Flags`.

### Pivot 2.4: Slash Announcer Pipeline from 59 Voices to 6 Deep Personas (`Phase 14`)
* **Directive:** Cancel the 56 team-specific announcer voice generation plan.
* **System Refactor:**
  * Define exactly **2 elite announcer pairs per tier** (HS, College, Pro = 6 voices total).
  * Reallocate audio engineering budget toward **10x situational trigger depth** per voice: dynamic commentary branches reacting to player `Recklessness`, `Rivalry` intensity, active `Entity_Flags` (PED rumors, investigations), slump/hot streaks, and bloodline/dynasty heritage.

---

## 3. REVISED DEVELOPMENT ROADMAP & EXECUTION SEQUENCE

The reviewing AI model must restructure the project roadmap to enforce the following strict gating sequence:

### Gate 1: Schema & Core Simulation Freeze
* Complete **Phase 13 (3-Tier League Restructure: HS / College / Pro)** and corresponding SQLite migration (`v13` -> `v14/v15`).
* **Hard Freeze:** Prohibit any new SQLite simulation tables or backend mechanics after Phase 13 lands until the core front-end loop is playable.

### Gate 2: The "15-Minute Steam Next Fest Loop" (Priority Override)
* Pause **Phase 14 (Voice)** and **Phase 16 (Motion Comic Cutscenes)**.
* Execute **Phase 15 (Visual Redesign & UI Theme)** strictly scoped to the core playable vertical slice:
  1. **Burner Phone UI** (Narrative + Hustle hooks)
  2. **Dashboard / Standings / Weekly Stance UI**
  3. **At-Bat (`MicroGame`) Broadcast View**
* **Acceptance Criterion:** A new player can boot into the High School tier, understand their backstory, respond to a gritty Burner Phone temptation, adjust their weekly lifestyle stance, play one high-stakes Friday night game, and view the consequences within 15 minutes with zero UI friction.

### Gate 3: Visual Identity Lock (Pre-Mass Asset Generation)
* Halt batch generation of 20+ crowd variants and bulk character sprites in Rodin/Mixamo (`Tools/SpriteBake`).
* Produce **1 Golden Vertical Slice** at 100% shipping art quality:
  * 1 Batter sprite sheet
  * 1 Pitcher sprite sheet
  * 1 Stadium backdrop
  * 1 Burner Phone screen
* Validate silhouette readability, palette cohesion, and joint integrity before resuming batch asset generation.

### Gate 4: External Playtest & Steam Page Launch
* Deploy the High School Tier slice (10-game season + Recruiting Offers + HS Gritty Events) to closed playtesters.
* Instrument telemetry or session logs to measure player drop-off or confusion in the Weekly Lifestyle / Burner Phone loop.

---

## 4. HIGH-VALUE COMMERCIAL FEATURES TO ARCHITECT & INTEGRATE

The reviewing AI model must design technical specifications to integrate the following three high-leverage features using existing backend data structures:

### Feature 4.1: "Dynasty Blackmail & Tabloid" Visual Feed
* **Data Sources:** `Biological_Parentage`, `Indiscretions`, `detection_risk`, `Entity_Flags`, and syndicate heat.
* **Presentation Requirement:** Design a front-page "Scandal / Rumor Mill" tabloid widget or social feed on the Dashboard/Phone that surfaces creeping paranoia—showing how close investigative reporters, rival coaches, or crime syndicates are to exposing the bloodline's hidden ledger.

### Feature 4.2: Shareable "Bloodline Summary & Rap Sheet" Exporter
* **Data Sources:** Career stats, championships, peak `WealthTier`, illicit earnings (`HustleService`), `Biological_Parentage` (legitimate and secret heirs), and `Indiscretions`.
* **Presentation Requirement:** Architect a one-click PNG/card generator at character retirement or succession (e.g., *"3x Red Dirt Series Champ | $400k Narcotics Kingpin | 2 Secret Heirs | Career .312 AVG"*) formatted specifically for social/Reddit/Discord sharing.

### Feature 4.3: External `mods/` Directory & Steam Workshop Loader
* **Data Sources:** `Assets/Narrative/Events/Content/*.json`, `schools.json`, `items.json`, and roster tables.
* **Technical Requirement:** Expose an external `mods/` directory override system that runs external JSON files through the existing strict validation harnesses (e.g., `GrittyEventJson.Parse`) at boot. Ensure players can seamlessly mod in custom high school/college/pro team names, custom gritty event packs, and custom rosters without recompiling Unity assets.

---

## 5. PROMPT FOR OPUS 5.5 / GEMINI PRO / GPT PRO

> **Instruction to AI:** 
> You have just reviewed `ARCHITECTURAL_DIRECTIVE_V1.md`. Based on your knowledge of the *Dirt & Diamonds* codebase and roadmap:
> 1. Audit the current repository structure against **Section 2 (Mandatory Architectural Pivots)** and list every C# service, UI controller, SQLite table, and tool script that needs to be modified, frozen, or deprecated.
> 2. Write the concrete **Technical Implementation Plan** for converting the daily `LifeSim`/`DaySchedule` into the **Weekly Lifestyle Stance** system and folding **Fencing/Robbery** into `GrittyEventJson` Burner Phone narratives.
> 3. Detail the exact file-by-file roadmap to deliver **Gate 2 (The 15-Minute Steam Next Fest Loop)** and **Section 4 (Commercial Features)** with minimal regression risk to existing unit and Monte Carlo tests.