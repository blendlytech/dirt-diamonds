# Arcade Baseball Overhaul — real-time physics, 16-bit presentation, watch & skip

**Status:** **ADOPTED by the owner 2026-10-13.** Active engine spec for **Phase G**. Design only, no code yet. Author: Claude Opus 5.5 (first draft 2026-10-09; reconciled 2026-10-13 with GAME_VISION v2.1, the 3-tier league and the v14/v15 schema plan).
**Authority:** [GAME_VISION.md](../GAME_VISION.md) §3 pillar 2 and §5. [gameplay_core.md](gameplay_core.md) remains the Phase G umbrella (attributes, venues, handoff protocol). **This doc owns the attended game:** physics, controls, AI, watch/skip, the arcade calibration contract and the ballgame's look.
**Supersedes, once G-4 ships:** the attended-game input and presentation path, i.e. "Read the Pitch" ([at_bat_read_input_model.md](../reference/at_bat_read_input_model.md)), [at_bat_presentation.md](../reference/at_bat_presentation.md) and `AtBatView`. It also replaces gameplay_core's former §2.2 pitching mode and §2.3 fielding moments. Those reference docs move to `archive/` when G-4 lands.

**Locked by the owner (2026-10-09, confirmed 2026-10-13):**

| Decision | Choice |
| --- | --- |
| Outcome authority | **True arcade physics.** Bat contact, ball trajectory and fielding are decided by real-time physics and player skill — not by the PA outcome tables. |
| Who you control | **Your player by default**, plus a **"Take over the team" toggle** that hands you every batter, the pitcher and all fielders for your side. |
| Ratings | **Ratings shape the physics.** Power, bat speed, arm, foot speed etc. set what a player *can* do; timing and aim decide what *does* happen. Development keeps mattering. |
| Fielding feel | **Assisted.** The fielder auto-breaks toward the ball; you steer the final approach, time the catch, choose the throw. |
| Art | **16-bit** (SNES-era palette and animation density) plus modern game-feel effects. |
| Viewing | **Watch the entire game**, with skip to: next pitch, end of at-bat, end of half-inning, end of inning, my next involvement, end of game. |

---

## 1. Scope boundary — what changes and what doesn't

**Changes:** every *attended* game is played by a new real-time arcade engine instead of `MicroGame.PlayGame`, and that engine produces the box score. An attended game is any game the avatar's team plays: `CareerManager.PlayPendingGame`, one a week in HS and College, about three a week across the 64-game Pro season.

**Does not change:**

- **Every other league game** stays on the macro Monte Carlo (`LeagueSimulator`). That covers 68 teams across three tiers. Those games can't be physically simulated at that volume, and nobody watches them. The resolver guard and all macro bands stay exactly as Phase 13 sets them.
- `MicroGame` is **kept**, not deleted. It remains the harness subject, so the MC suites stay green and meaningful. It also becomes the **"Classic sim" fallback** behind a setting until the arcade engine is proven (§11).
- Life sim, narrative, hustles, economy, Steam, broadcast and cutscenes stay untouched, except where they read game results or at-bat beats. Results arrive through the same flush they do today. Beats arrive through the same vocabulary the booth already consumes (§7.6).

**The one hard consequence to accept:** the avatar's team's games now come from a *different stat-generating process* than the rest of the league. Everything that judges a player is **league-relative**: recruiting interest, the College National Ranking, draft stock, leaders, awards, and the record chase against the father's mark. So AI-vs-AI physics games must land in the same per-tier bands as the macro sim, or every teammate's numbers drift, and so do the league tables. §9 is the contract that prevents that, and it is the single biggest risk in this overhaul.

---

## 2. Architecture

```
Assets/Simulation/Arcade/        ← NEW, pure C#, engine-independent (no Godot types)
  ArcadeWorld        fixed-step simulation of one game: ball, players, rules state
  BallPhysics        pitch flight, bat-ball contact, batted-ball flight, bounces
  FielderKinematics  pursuit, reach, catch, throw
  RunnerKinematics   base paths, tag-ups, slides
  RulesEngine        counts, outs, force/tag plays, fouls, scoring, innings
  ArcadeAi           batter / pitcher+catcher / fielder / runner / manager AI
  ArcadeGame         game driver: owns ArcadeWorld, box score, skip/fast-forward
  ArcadeInput        per-tick input frame (from UI or AI) — the only way in
Assets/Simulation/Baseball/
  EffectiveRatings   NEW — the modifier stack extracted from MicroGame (§4.2),
                     shared by MicroGame AND ArcadeGame so both read identical ratings
Assets/UI/Arcade/                ← NEW, Godot presentation
  ArcadeViewport     320×180 SubViewport, integer-scaled (§10)
  PitchView / FieldView / Scoreboard / BoxScorePanel / SkipBar / ReplayPlayer
Tools/ArcadeHarness/             ← NEW headless calibration + rules harness (§9)
```

**Why a custom physics core instead of Godot's physics engine:**

1. **Headless harnesses.** Every calibration tool in this repo is a plain .NET console project with no engine. The arcade sim must run there to be calibrated (§9) — the `PitchChain`/`MicroGame` precedent.
2. **Determinism.** A seeded `RngState` + a recorded input stream must reproduce a game exactly. That powers skip-without-changing-results (§7), instant replay (§10.5), and bug reports.
3. **Speed.** Skip-to-end-of-game runs the remaining innings headless; a custom integrator with analytic contact does a full game in well under a second.

Godot only *renders* `ArcadeWorld` snapshots and *collects* input. Same read-only rule as `ui_conventions.md`: UI renders DTOs and emits intent; it never mutates sim state.

**Threading:** unlike the current blocking `PlayerIntentBridge` handshake, the arcade sim is **ticked from the main thread** in `_PhysicsProcess` while watching (one or more fixed steps per frame), and run in a tight loop on a background task while skipping. No semaphore round-trip per pitch.

**Seam:** `CareerManager.PlayPendingGame` gains a sibling, `PlayPendingArcadeGame(ArcadeGame game)`, that runs the same pre/post steps it does today — `RefreshAvailability(day)` before, `FlushGame` + `Normalizer.NormalizeSeason` after, `_gameInFlight` guard around — with `ArcadeGame` as the box-score producer. The autopilot path (`ResolvePendingWithAutopilot`: forfeits, absences, headless runs) also routes to `ArcadeGame` with every role AI-controlled, so attended-team stats come from **one** process no matter how the game was resolved.

---

## 3. The physics model

All units SI. Origin at the back point of home plate; **+y** toward center field, **+x** toward the first-base side, **+z** up. Fixed step **240 Hz** for ball and players; contact and catches are solved **analytically** at the exact crossing instant rather than by stepping into them, so results don't depend on frame rate.

### 3.1 Pitch flight

- Release point from the pitcher's arm slot (≈ 17.0 m from the plate, z ≈ 1.8 m).
- Velocity from the pitch's `velocity` rating mapped to a per-tier mph range (§4.1), times the fatigue multiplier (`PitcherFatigue`, reused as-is).
- Forces: gravity, drag `F = ½ρC_dAv²` (C_d ≈ 0.35), and Magnus lift from spin. Each pitch type has a spin axis + rate profile; **movement** rating scales the break.
- **Aftertouch (pitcher input):** for a short window after release the pitcher can bend the ball left/right/down, capped by movement. The RBI-style curveball steer, but bounded by the ratings.
- Location error: the pitcher's aim point is perturbed by a Gaussian whose σ shrinks with **Control** and grows with fatigue and a missed release-meter timing.

### 3.2 Bat-ball contact (the heart of the system)

The batter controls a **swing trigger** (timing) and a **contact cursor** (where in the zone the sweet spot goes). At the instant the ball crosses the contact plane:

- **Miss distance** `d` = distance between ball center and the sweet spot. If `d` > the bat's effective half-height, it's a whiff (or a foul tip near the edge).
- **Timing error** `t` (early/late in ms) → **spray angle**. Early = pull, late = opposite field, very early/late = foul.
- **Vertical offset** (ball center above or below the sweet spot) → **launch angle**. Under the ball = fly/pop-up, over it = grounder/chopper.
- **Exit velocity** from the standard collision model: `EV = q·v_pitch + (1+q)·v_bat`, where `q` (collision efficiency, ≈ 0.2 on the sweet spot) falls off with miss distance. `v_bat` comes from the **Power** rating (bat speed), ×1.5 power for active PEDs (the existing hook, read through `EffectiveRatings`, §4.2).
- **Spin** off the bat: backspin on balls hit under center (carry), topspin on balls hit over, sidespin from timing (hook/slice toward the lines).

This is what makes it *arcade physics* rather than a dice roll in costume: a ball squared up early at 25° really leaves the park, a ball topped late really rolls weakly to second.

### 3.3 Batted-ball flight and ground play

Same drag + Magnus integrator as the pitch. Ground contact: coefficient of restitution + rolling friction, both **per surface** (grass, dirt infield, warning track, turf at some parks). Fences have height and distance per park. **At 1.0 every park uses its tier's standard dimensions** (owner accepted R-1 Q06: park factors off for 1.0), so venue identity is presentation-only. The per-team distances in `Assets/Data/Venues/venues.json` (gameplay_core §3.1) drive the art, and they'd drive the physics only through a later approved Math slice. Foul lines decide fair/foul at the first bounce or the bag; foul poles decide home runs.

### 3.4 Fielders

- **Pursuit:** on contact, each fielder's AI predicts the landing/intercept point from the same integrator (fielders "know physics" — the skill is in reaction and range, not prediction). The best interceptor is assigned; others cover bases or back up.
- **Kinematics:** reaction delay (shorter with **Fielding**), acceleration and top speed (**Speed** rating), turn-rate penalty for balls over the head.
- **Catch:** a ball is caught if it passes within the fielder's **reach radius** (from Fielding; larger on a dive) while the fielder is set. Near the edge of reach there is a bobble/drop chance, so errors come from the physics rather than a separate dice table.
- **Throws:** throw speed from **Arm**, accuracy σ from Fielding (and a rushed throw is less accurate). A throw is a real ball in flight — it can be cut off, short-hopped, or pull the first baseman off the bag.

### 3.5 Runners

Running speed from **Speed**; turns cost time at each base; slides at the bag. Runner AI reads the batted ball (§6). Tag-ups on caught fly balls, forced runners vs. non-forced, and runners caught in the middle all fall out of real positions and timing.

### 3.6 Rules engine

Counts, outs, innings and the game end mirror the macro sim's rules exactly, so box scores are comparable: regulation innings from `TierRules.RegulationInnings(tier)` (**HS 7, College and Pro 9**, Phase 13), the home half skipped when leading after the top of the last regulation inning, extras while tied, and the HS mercy rule if the owner turns it on (league_restructure open item). On top of that, physics produces things the outcome tables folded away: force and tag outs, double plays, sacrifice flies, errors, reaching on error, foul outs, ground-rule doubles, hit-by-pitch. §8.3 says how they map into the existing stat columns.

**Out of scope for v1** (see §13): stolen bases and pickoffs, bunts, the infield-fly rule, balks, interference, ejections.

---

## 4. Ratings → physics

### 4.1 Mapping

| Rating | Exists today? | Drives |
| --- | --- | --- |
| `bat_power` | yes | Bat speed → exit velocity; PED ×1.5 power hook stays |
| `bat_contact` | yes | Sweet-spot size (contact cursor) and the timing window |
| `bat_discipline` | yes | **Human:** how early the pitch type and location are telegraphed (ball trail / spin cue). **AI:** swing/take decision quality |
| `pit_stuff` + arsenal `velocity` | yes | Pitch speed |
| arsenal `movement` | yes | Break magnitude + aftertouch budget |
| `pit_control` | yes | Location σ; width of the release-meter sweet spot |
| `pit_stamina` | yes | `PitcherFatigue` capacity (reused) — fatigue lowers velocity, widens σ |
| `fielding` | yes (currently baked into a team-level defense byte) | Reaction delay, reach radius, throw accuracy — **per player** now |
| **`speed`** | **NEW** | Runner and fielder top speed + acceleration |
| **`arm`** | **NEW** | Throw velocity (fielders, catcher pop time) |
| **`primary_position`** | **NEW** | Where the player stands; which fielding role the AI plays |

Every tier keeps its own feel through **per-tier physics ranges** (e.g. HS fastball 70–85 mph, College 80–94, Pro 88–101), driven by the tier rating deltas already applied at roster load (`TierRatingDeltas`, values set in Phase 13), plus the park's real dimensions (§10.4). The mph ranges are first-pass *proposals* for GPT 6 Astra Pro (Math).

### 4.2 One modifier stack for both engines

`MicroGame` currently builds effective ratings in two places: tier deltas (`TierEffects`) and person levers (`PersonEffects`) are baked at roster load in `Initialize`; then per PA it applies the equipment boost (`EquipmentEffects`) → rust dock (`ApplyRust`) → rivalry boost, live pitcher fatigue (`PitcherFatigue.EffectiveRatings`), and replacement call-ups for absent players (`IsShadowed`). Phase 13 removes call-ups (absent players are never replaced; a short team forfeits), so `EffectiveRatings` drops that step. The PED ×1.5 power multiplier is applied inside `AtBatResolver` off the `PedActive` flag. **Extract all of that into `EffectiveRatings`**, together with Phase L's `Active_Effects` and clutch stages. Expose the PED flag so the arcade applies the same ×1.5 to bat speed. Call it from all three engines (macro, `MicroGame`, arcade). It's a pure refactor, and the MC resolver guard proves it changed nothing. Afterwards gear, rivalries, rust, person stats, vices, PEDs and fatigue all affect arcade games for free and can never drift between engines.

---

## 5. Controls

Gamepad first (Steam Deck), full keyboard support, all bindings remappable.

### 5.1 Batting

| Action | Pad | Keyboard |
| --- | --- | --- |
| Move contact cursor | Left stick | WASD |
| Swing (tap) / check swing (release early) | A | Space |
| Shift in the box (in/out) | D-pad ←/→ before the pitch | Q / E |
| Power-swing modifier (bigger bat speed, smaller sweet spot) | Hold RT | Shift |

The cursor's size is the Contact rating made visible. Discipline shows up as how early the pitch "reads" (a faint spin-colored trail on the ball).

### 5.2 Pitching (when you pitch, or in takeover mode)

Pick pitch type (face buttons, from the pitcher's arsenal) → aim with the stick → **release meter** (tap on the sweet spot; Control sets its width, fatigue shrinks it) → **aftertouch** with the stick in the first fraction of a second of flight.

### 5.3 Fielding — assisted

The fielder breaks on contact automatically toward the predicted point. You steer the final approach (stick), **dive** (B), and the catch is automatic if the ball enters reach. Then throw by direction, RBI-style: **↑ second, ← third, → first, ↓ home**; hold to throw harder (faster, less accurate). In "my player" mode you only field balls your player is assigned; teammates field theirs.

### 5.4 Baserunning

On base: **advance / retreat** buttons. In takeover mode the d-pad selects which runner (same directional scheme as throws).

### 5.5 Take over the team

A toggle in the game HUD, usable between plate appearances. Off = you control only your player's at-bats, pitches (if a pitcher) and fielding chances. On = every batter, the pitcher and all fielders for your side. It can flip mid-game. Stats still credit whichever player was on the field — §13 asks whether takeover games should be flagged.

### 5.6 Difficulty / assists

**Rookie / Pro / All-Star / Legend** presets adjust timing-window width, cursor size multiplier, the pitch-read cue, and fielding auto-assist strength. They change only *human* input assists — AI behavior and ratings are identical at every setting, so difficulty never touches calibration.

---

## 6. AI — same physics, human-like error

AI players use **the same input surface as a human** (`ArcadeInput` frames), with noise:

- **Batter AI:** swing/take decision from pitch location vs. zone with Discipline-scaled recognition error; timing error ~ N(0, σ_t(Contact)); cursor aim error ~ N(0, σ_a(Contact)); a two-strike protect mode.
- **Pitcher + catcher AI:** pitch selection from the arsenal usage mix with count awareness (reuse `PitcherArsenal.DrawType` and the micro-sim's pitch-calling as a starting point); aim point by count and batter; release-meter noise from Control.
- **Fielder AI:** pursuit/assignment (§3.4), throw decision (lead runner if makeable, else safe out), cutoffs.
- **Runner AI:** read the ball (go on a hit, hold on a liner, tag on a deep fly), take an extra base when the throw is to the wrong bag.
- **Manager AI:** bullpen pulls reuse `MicroGame.BullpenPullThreshold` on the same fatigue multiplier, between half-innings.

The noise σ's are the **calibration knobs** (§9) — one table per role, data not logic, the same discipline as `AtBatResolver`'s named calibration tables.

---

## 7. Watch & skip

### 7.1 Controls

A skip bar on the game HUD:

| Control | Behavior |
| --- | --- |
| Play speed | 1× / 2× / 4× while watching |
| Next pitch | Fast-forward to the next pitch result |
| End of at-bat | Fast-forward to the PA's end |
| End of half-inning / End of inning | As named |
| **My next moment** | Fast-forward until your player bats, pitches, or has a fielding chance (or, in takeover mode, your team's next turn) |
| End of game | Straight to the final + box score |

### 7.2 What happens to your own moments during a skip

A setting, **"Stop skipping when I'm involved"** (default ON): any skip halts at your player's at-bat or fielding chance. With it OFF, your player is played by the AI using your ratings — same as the autopilot.

### 7.3 Determinism contract

Skipping runs the **same simulation** headless at maximum rate — no shortcut resolver. Given the game's seed and the inputs actually made, **watching and skipping produce identical results**. That is why one physics core is non-negotiable: if skips fell back to the outcome tables, a game you skipped through would be a different game.

### 7.4 Always available

A running play-by-play log, a retro line score (inning-by-inning runs, R/H/E), and a full box score panel. During a skip, the log fills in as the innings pass so you can see what you skipped.


### 7.5 Interaction with the game clock

The attended game runs inside the schedule's Game block. The real-time clock **soft-pauses for the whole game**, following the hustle-session precedent. The block's in-game hours are consumed in full when the game ends, however long you spent watching.

### 7.6 Broadcast and cutscenes (Phases 14 and 16)

The booth keeps its contract ([voice_and_broadcast_audio.md](voice_and_broadcast_audio.md) §8): it consumes beats and a game-context DTO, never the sim directly. `ArcadeGame` emits the same beat vocabulary the shipped sequencer does:

- `PitchReveal`, per pitch;
- `PaReveal`, per PA, carrying the folded `PaOutcome`;
- game start, half-inning change and game end;
- the context DTO: score, inning, outs, runners, and which side is "ours".

Physics also produces **real signals the tables never had**, so the voice spec's "real signal only" rule allows new call categories: exit velocity and distance on a home run, a diving catch, a double play, an outfield assist at the plate, a walk-off. These ride on `PaReveal` as an additive `PlayDetail` field. Phase 14 consumers that ignore it are unaffected.

- **★ big plays** still raise `BigPlay(kind, contextDto, callClipId)` to the `CutsceneDirector`. `ArcadeGame` supplies richer `kind` values. The motion-comic cutscene stays the default for ★ plays. The in-engine **instant replay** (§10.5) is a separate button, offered on any play.
- **During a skip** the booth is silent and no cutscenes fire. When play resumes, the booth may play one "here's where we stand" call (its existing inning-change category).
- **RNG boundary:** the arcade sim draws only from its own `RngState` stream. The booth and replay camera use presentation RNG, so watching, skipping and booth variety can never change a result.

---

## 8. Data

### 8.1 Schema: fold into v14

Phase 13's v14 is a pre-1.0 clean break that hasn't been built yet (13-1). **Fold one table into it**, so there's no backfill and no extra version:

**`Player_Fielding`**, with these columns:

- `player_id`: PK/FK.
- `defensive_position`: one of `P, C, 1B, 2B, 3B, SS, LF, CF, RF`.
- `secondary_position`: nullable.
- `speed`: 0–100.
- `arm`: 0–100.

Every player gets a row, pitchers included (`P`). Pitchers field comebackers and throw to bases. Appendix A world-gen generates the rows. If 13-1 has already merged when G-4 starts, the table ships as the next free version after v15 instead, since pre-1.0 saves are disposable either way. Validate through `validate_sqlite_schema` before any C# touches it.

**The identity invariant holds** (GAME_VISION §12, temptation_and_lineage §3.1). `Player_Identity.listed_position` (v15, Phase L) stays story-only, and the sim never reads it. At New Career, L-1's `CreateAvatar` writes the chosen position **twice**, once into each table:

- the story label into `Player_Identity`;
- the matching defensive assignment into `Player_Fielding`. SP and RP map to `P` plus the existing `Pitcher_Roles` row.

After that the two are independent. The sim reads only `Player_Fielding`. Mid-career position changes are out of scope for v1.

**Lineups** stay 9 position players with the pitcher not batting. That's a universal DH, matching the shipped sim and Phase 13's 9-starter active roster. Lineup construction (13-2/13-5) must now field a **legal defense**: one player at each of the 8 field positions plus a catcher. A player out of position plays with reduced reach and reaction (§3.4). The macro sim's team-defense byte is unchanged.

### 8.2 Ripples of the new ratings

| System | Change | Slice |
| --- | --- | --- |
| World-gen (league_restructure Appendix A) | Positions across each roster (≥ 1 per position, 2 catchers on Pro rosters, utility players), plus speed and arm | 13-1 / G-4 |
| `HeirGenetics` | Inherit speed and arm | G-4 |
| `DevelopmentManager` / decline curves | Speed peaks early and declines first; arm holds longer (Math values) | G-4 |
| New Career | The position pick (already in L-1) also seeds `Player_Fielding` | L-1 |
| Scouting card | Show position, speed and arm | G-5 |

### 8.3 Stats

`ArcadeGame` accumulates into the **same** `BattingLine` / `PitchingLine` shapes. It flushes through the shared path in its own batch: additive season upserts, `Game_Logs` play-by-play, and PED post-game costs. So everything downstream keeps working untouched: the normalizer, recruiting, the National Ranking, the draft, development, achievements, the record chase and the dashboard.

Richer events fold into existing columns the way `PaOutcome` already folds them: reached-on-error and sacrifices → Out, HBP → Walk. Fielding, error and stolen-base columns are a later, optional schema slice.

---

## 9. Calibration contract — the critical gate

**Why it's mandatory:** recruiting, the ranking, the draft and the league tables all compare your team's numbers to everyone else's (§1). Suppose AI-vs-AI physics produced a .290 league average while the macro sim produced .247. Every hitter on your team would then look like a top prospect.

**`Tools/ArcadeHarness`** is headless, plain .NET, and follows the `MonteCarloHarness` pattern. It plays thousands of AI-vs-AI arcade games and asserts:

1. **Pro at all-50 ratings** matches the macro baselines in `AtBatResolver` (.247/.315/.412, with K%, BB% and HR/PA from the same tables) within the MC harness's tolerances.
2. **Per-tier bands** match the Phase 13 bands for HS, College and Pro ([league_restructure.md](league_restructure.md) §11), with runs measured per 9 innings so 7-inning HS games compare cleanly.
3. **Rating monotonicity:** more Power → more HR; more Contact → fewer K; more Control → fewer BB; more Fielding or Speed → lower BABIP against.
4. **Batted-ball sanity:** the ground-ball / fly-ball / line-drive mix, BABIP, and the exit-velocity × launch-angle distribution against MLB shapes.
5. **Rules integrity:** no impossible base-out states, runs = runners crossing home, three outs per half, regulation innings per tier.
6. **Determinism:** the same seed + inputs → a byte-identical box score, and a watched game = the same game skipped.

**Ownership:** GPT 6 Astra Pro (Math) owns the noise tables and physics constants (TEAM.md). Tuning happens **only** through them, as data edits. The harness re-runs after any change under `Assets/Simulation/Arcade/`, the same mandate `run_monte_carlo_batch` carries for the rest of `Assets/Simulation/`. If a change touches the shared `EffectiveRatings` stack, both harnesses run.

**The human is deliberately not calibrated to the league.** A skilled player can beat their ratings; that's the point of the choice. What *is* tuned is **difficulty** (§5.6). The scripted-skill bot harness from gameplay_core §1.4 carries over with three bots: perfect, average and poor. At the default difficulty, the targets are the owner's **O1** skill band (2026-10-10): the average bot ≈ the ratings-only line, and the perfect and poor bots ≈ the 40-to-70 rating gap (±.100 OPS), capped. Math sets the constants. Higher tiers keep it a challenge, because pitches are faster and nastier.

---

## 10. Presentation — 16-bit

### 10.0 How it fits Phase 15

The **field itself** is the one pixel-art surface in the game. Everything around it follows the Phase 15 direction the owner picks at 15a, built from the 15b design system: the scorebug, lower-thirds, subtitle strip and box score. Under the recommended "Two Worlds" direction ([visual_redesign.md](visual_redesign.md) §2), the 16-bit ballgame sits inside the DIAMOND broadcast package. Tier progression lines up on both sides of the frame: the HS local stream with a weathered pixel field, a regional College broadcast, then the Pro prime-time package with a full stadium.

### 10.1 Resolution and scaling

The ballgame renders in a **320×180 SubViewport** with nearest-neighbor filtering, integer-scaled to the window: ×4 = 1280×720 (Steam Deck width), ×6 = 1080p, ×8 = 1440p, ×12 = 4K. The surrounding UI stays at native resolution.

### 10.2 Views

- **Pitch view:** from behind home plate looking out, with the pitcher at the top and the batter at the bottom (the RBI framing). Depth comes from sprite scaling and a ball **shadow**.
- **Field view:** on contact, the camera pans smoothly to an elevated angle that follows the ball. Ball height reads from the shadow's separation. Fielders run in eight directions. A small radar inset shows all the runners.
- **Scoreboard:** count, outs, bases diamond, inning and line score, in the 15b scorebug component.

### 10.3 Team colors for free

There's one sprite set, **palette-swapped by shader** per team (jersey, cap, trim), plus per-player skin tones. Team primary/secondary/accent colors come from the venue catalog and team seed data (gameplay_core §3.1; Flash owns team seed data). All 68 teams get their own look without anyone drawing it.

### 10.4 Every park is its own field

Each of the 68 venues gets a field built from **reusable park kits plus team dressing** (owner accepted R-1 Q13). The dressing draws on its catalog entry: dimensions, wall heights, surface, and one or two signature features such as a short porch, an ivy wall or a harbor beyond right. Tier sets the base kit:

- **HS:** chain-link, bleachers, odd dimensions.
- **College:** a real ballpark with a modest crowd.
- **Pro:** a full stadium, with night games under lights.

Crowd density scales with tier and the game's stakes. At 1.0 the physics runs on tier-standard dimensions (R-1 Q06), so the signature features are visual. Real geometry is a post-1.0 Math option (§13 Q5).

### 10.5 Game feel ("better than RBI")

- Hit-stop (a few frames' freeze) on solid contact, and a small screen shake on a home run.
- Dust puffs on slides and dives, and chalk spray on balls down the line.
- A ball trail on hard-hit balls, and a crack-of-the-bat pitch that varies with exit velocity.
- **Instant replay** of any play, free from determinism: re-run the recorded inputs.
- An optional CRT/scanline filter in settings. Every effect respects reduce-motion.

### 10.6 Art pipeline and budget

**Art is the dominant external cost of this overhaul, not code.** Production sprites come from a pixel artist, a licensed sprite pack, or curated AI-assisted pixel art. Anything AI-made is disclosed under the Phase 17 AI-content rule. To keep the build unblocked:

- G-0 through G-6 run on **programmatic placeholder sprites** (simple colored figures drawn in code), so every system is built and tuned before art arrives.
- Sprite sheets plug in through a fixed **animation contract**: named animations, frame counts, pivot points, 8-direction runs.

**Minimum animation list:**

- batter: stance, load, swing, check swing, run;
- pitcher: set, windup, release, follow-through, field;
- catcher: crouch, frame, throw;
- fielders: ready, run ×8 directions, dive, catch high/low, throw;
- runner: run, slide, tag;
- umpire: calls;
- plus crowd tiles, park tilesets per tier, ball + shadow, and dust/chalk effects.

All art is original. The RBI Baseball reference is for *style*, never its assets, and no real team, park or player likeness is used (GAME_VISION §12).

---

## 11. What this retires or supersedes

- **For attended games:** `AtBatView` and the "Read the Pitch" human path in `InteractiveBatterPolicy` / `PitchChain`. The sim code stays: it is `MicroGame`'s, and the harness covers it.
- **Planned work that is no longer built:**
  - gameplay_core's former §2.1 at-bat view upgrade, §2.2 pitching mode (the `IPitcherPolicy` human branch) and §2.3 fielding moments;
  - the At-Bat Read-Input §7.4 playtest and its re-band;
  - checklist items about the old at-bat view.
- `BaseballDashboard`'s attended-game play flow is rewired to launch the arcade scene.
- **"Classic sim" setting:** keep `MicroGame` selectable for attended games until the arcade engine has passed §9 and a full-season playtest. Then the owner decides whether to keep it as a "quick sim" option or retire it.

---

## 12. Phase G slices (model-tagged, one gate each)

These replace gameplay_core's former §4 table, and they reuse the IDs G-1…G-9. The old G-2 (pitching mode) and G-8 (Read v2) are retired and archived. Engine slices go to **GPT 6 Astra Pro**, per the owner's 2026-10-10 decision that it is the senior engineer for gameplay models. G-0a was built by Claude Opus before the two histories merged. The roles come from [TEAM.md](../TEAM.md) §1. Every task follows the handoff protocol (gameplay_core §5).

| Slice | Content | Recommended model | Reviewer | Gate |
| :-- | :-- | :-- | :-- | :-- |
| **G-0** | **Feel prototype:** standalone scene, one batter vs one pitcher, pitch flight + contact + batted-ball flight + landing readout. Placeholder sprites; no fielding, no DB. | Claude Opus 5.5 (physics) + Claude Sonnet 5.5 (scene) | G-0a: Claude Opus stand-in (done) → GPT 6 Astra Pro (independent); G-0b: GPT 6 Astra Pro | **Owner verdict: "hitting is fun."** Iterate here until it is. |
| **G-1** | Attribute audit (**done inside R-1**) → the ratings-to-physics mapping (§4.1): mph ranges, bat speed, reach, arm and speed curves | GPT 6 Astra Pro (mapping + Math) | Claude Opus | ~~Default difficulty target~~ decided (O1) |
| **G-2** | Physics core + rules engine (§3), engine-independent; `ArcadeHarness` rules + determinism suites | GPT 6 Astra Pro | Claude Opus (sim diff + merge gate) | Harness rules + determinism green |
| **G-3** | AI for every role (§6); AI-vs-AI games calibrated to the Phase 13 bands | GPT 6 Astra Pro (engine + Math) | Claude Opus | **§9 bands green, all three tiers** |
| **G-4** | Integration: `EffectiveRatings` extraction (incl. R1-EFFECTS projections); `Player_Fielding` + ripples (§8); `ArcadeGame` flush; `PlayPendingArcadeGame`; autopilot routing; Classic setting; booth beats (§7.6) | GPT 6 Astra Pro | Claude Opus | MC full suite (resolver guard byte-exact); season-long arcade harness |
| **G-5** | Arcade presentation: SubViewport, pitch/field views, 15b scorebug, controls + remapping, watch & skip, play-by-play, box score | Claude Sonnet 5.5 | GPT 6 Astra Pro | Clean boot; playable end to end; watched = skipped |
| **G-6** | Take-over-the-team toggle + full baserunning control | Claude Sonnet 5.5 + GPT 6 Astra Pro | each other's part | Owner playtest |
| **G-7** | Venue catalog (68 entries, incl. the dimensions physics reads) + venue screens + hotspots (gameplay_core §3) | Claude Sonnet 5.5 (+ Gemini 3.8 Flash: catalog data) | Claude Opus | Every team resolves a venue |
| **G-8** | Art: 16-bit sprite set, palette-swap shader, per-venue field tilesets, venue screen art | Owner + artist/AI art tools (Flash: integration); inside 15d | Claude Opus (consistency) | **Owner taste** |
| **G-9** | Tuning + difficulty presets (§5.6, §9) | GPT 6 Astra Pro (Math) + Claude Sonnet 5.5 (data) | Claude Opus | **Owner sign-off; Classic's fate (§11)** |

**Dependencies:**

- **G-0** has none: it's standalone, with no DB or league. **It starts now.** It's a few days of work, and it answers the only question no spec can: does it feel good to hit?
- **G-1** and **G-2** run alongside 13-0/13-1.
- **G-3** needs 13-3's per-tier bands.
- **G-4** needs 13-2 (roster capacities, regulation innings) and folds `Player_Fielding` into 13-1 if that hasn't merged.
- **G-5** can start on placeholders, but takes its chrome from 15b.
- **G-7** and **G-8** follow 15a/15b.

---

## 13. Open questions for the owner

1. ~~High-school game length~~ **Settled by Phase 13:** HS plays 7 innings, College and Pro 9.
2. **v1 extras:** stolen bases, pickoffs and bunts. In v1, or a follow-up slice? *Recommended: stolen bases in v1; pickoffs and bunts as follow-ups.*
3. **Takeover stats:** should games played in takeover mode count normally for teammates' stats, the draft and the father-record chase, or be flagged? *Recommended: count normally, so the record chase stays simple. Takeover doesn't touch the avatar's own at-bats.*
4. **Classic sim:** keep it permanently as a "quick sim" option, or retire it once the arcade engine is proven?
5. ~~Parks play differently?~~ **Settled for 1.0 by R-1 Q06: no.** The physics uses tier-standard dimensions; real geometry is a post-1.0 Math option.
6. **Art source:** commission a pixel artist, license a sprite pack, curated AI-assisted pixel art, or a mix (e.g. a pack for crowds and tiles, an artist for players)?

## 14. Risks

| Risk | Mitigation |
| --- | --- |
| AI-vs-AI physics can't hit the macro bands | Noise tables + physics constants are data knobs; G-3 gates everything downstream; the Classic fallback stays available |
| Hitting feels bad | The G-0 prototype gate comes before any integration work |
| Skilled humans break recruiting and the draft | Difficulty presets; tier-scaled pitching; every judgment stays league-relative and season-long |
| Phase 13 lands late or changes | G-0 to G-2 don't depend on it; G-3/G-4 wait on explicit 13-x gates |
| Art cost or schedule | Placeholders through G-6; a fixed animation contract; palette swap cuts team art to one set |
| Scope creep (bunts, pickoffs, managers, injuries on HBP…) | v1 scope fixed in §3.6; extras are follow-up slices |
