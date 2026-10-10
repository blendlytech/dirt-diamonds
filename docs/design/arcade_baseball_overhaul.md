# Arcade Baseball Overhaul — real-time physics, 16-bit presentation, watch & skip

**Status:** ⚠ **PROPOSAL — NOT ADOPTED. Do not build from this doc until the owner decides.** Author: Claude Opus 5.5 (2026-10-09), spec only, no code. It was written *before* GAME_VISION v2 and the Oct-10 docs reorganization, and it **conflicts** with the current authority:
- [gameplay_core.md](gameplay_core.md) §2 and GEMINI.md §11 keep the outcome-table sim and put player skill on a human branch only. This doc instead replaces attended games with real-time physics.
- It was written against the old 6-tier league (MLB, minors). Phase 13 ([league_restructure.md](league_restructure.md)) is now 3 tiers (HS/College/Pro), with 7-inning HS games, which answers §13 Q1.
- Its "schema v14 `Player_Fielding`" collides with v14 (Phase 13) and v15 (Phase L). It would be a later version folded into those plans.

**Owner decision needed:** adopt the arcade direction (record it in GAME_VISION, then reconcile gameplay_core.md and this doc), or archive this doc. Until then, **gameplay_core.md is authoritative.** If adopted, it supersedes the attended-game *presentation and input* path (`AtBatView`, "Read the Pitch"); the macro league sim is untouched.

**Locked by the user (2026-10-09):**

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

**Changes:** every *attended* game (the one game per day the avatar's team plays — `CareerManager.PlayPendingGame`) is played by a new real-time arcade engine instead of `MicroGame.PlayGame`. That engine produces the box score.

**Does not change:**

- **Every other league game** stays on the macro Monte Carlo (`LeagueSimulator`). A season is thousands of games; they can't be physically simulated, and nobody watches them. The MLB bit-identity guard and all macro bands stay exactly as they are.
- `MicroGame` is **kept**, not deleted: it remains the harness subject (MC suites 5–7 stay green and meaningful) and becomes the **"Classic sim" fallback** behind a setting until the arcade engine is proven (§11).
- Life sim, narrative, hustles, economy, Steam — untouched except where they read game results, which arrive through the same flush they do today.

**The one hard consequence to accept:** the avatar's team's games now come from a *different stat-generating process* than the rest of the league. Promotion scores are **league-relative** (`PromotionScore.BatterPerformance` divides by league OPS, `PitcherPerformance` by league ERA), so AI-vs-AI physics games must land in the same per-tier bands as the macro sim or every teammate's numbers — and the league tables — drift. §9 is the contract that prevents that, and it is the single biggest risk in this overhaul.

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

Same drag + Magnus integrator as the pitch. Ground contact: coefficient of restitution + rolling friction, both **per surface** (grass, dirt infield, warning track, turf at higher tiers). Fences have height and distance per park (§10.4). Foul lines decide fair/foul at the first bounce or the bag; foul poles decide home runs.

### 3.4 Fielders

- **Pursuit:** on contact, each fielder's AI predicts the landing/intercept point from the same integrator (fielders "know physics" — the skill is in reaction and range, not prediction). The best interceptor is assigned; others cover bases or back up.
- **Kinematics:** reaction delay (shorter with **Fielding**), acceleration and top speed (**Speed** rating), turn-rate penalty for balls over the head.
- **Catch:** a ball is caught if it passes within the fielder's **reach radius** (from Fielding; larger on a dive) while the fielder is set. Near the edge of reach there is a bobble/drop chance, so errors come from the physics rather than a separate dice table.
- **Throws:** throw speed from **Arm**, accuracy σ from Fielding (and a rushed throw is less accurate). A throw is a real ball in flight — it can be cut off, short-hopped, or pull the first baseman off the bag.

### 3.5 Runners

Running speed from **Speed**; turns cost time at each base; slides at the bag. Runner AI reads the batted ball (§6). Tag-ups on caught fly balls, forced runners vs. non-forced, and runners caught in the middle all fall out of real positions and timing.

### 3.6 Rules engine

Counts, outs, innings and the game end mirror `MicroGame`'s rules exactly (9 innings, home half skipped when leading after 8½, extras while tied) so box scores are comparable. On top of that, physics produces things the outcome tables folded away: force and tag outs, double plays, sacrifice flies, errors, reaching on error, foul outs, ground-rule doubles, hit-by-pitch. §8.3 says how they map into the existing stat columns.

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

Every tier keeps its own feel through **per-tier physics ranges** (e.g. HS fastball 70–85 mph, MLB 88–101 mph), driven by the tier rating deltas already applied at roster load (`TierRatingDeltas`), plus the park (§10.4).

### 4.2 One modifier stack for both engines

`MicroGame` currently builds effective ratings in two places: tier deltas (`TierEffects`) and person levers (`PersonEffects`) are baked at roster load in `Initialize`; then per PA it applies the equipment boost (`EquipmentEffects`) → rust dock (`ApplyRust`) → rivalry boost, live pitcher fatigue (`PitcherFatigue.EffectiveRatings`), and replacement call-ups for absent players (`IsShadowed`). The PED ×1.5 power multiplier is applied inside `AtBatResolver` off the `PedActive` flag. **Extract all of that into `EffectiveRatings`** (exposing the PED flag so the arcade applies the same ×1.5 to bat speed) and call it from both engines. It's a pure refactor of `MicroGame` — the MC bit-identity guard proves it changed nothing — and afterwards gear, rivalries, rust, absences, PEDs and fatigue all affect arcade games for free and can never drift between engines.

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

The attended game runs inside the schedule's Game block. The real-time clock (Slice G) **soft-pauses for the whole game** (the hustle-session precedent), and the block's in-game hours are consumed in full when the game ends — however long you spent watching.

---

## 8. Data

### 8.1 Schema v14 (additive)

New table **`Player_Fielding`** (`player_id` PK/FK, `primary_position` C/1B/2B/3B/SS/LF/CF/RF/DH/P, `speed` 0–100, `arm` 0–100), the additive one-table-per-version pattern of `Pitcher_Roles` / `Pitch_Arsenals`. Migration backfill: assign positions per team from current lineup order (8 fielders + DH — lineups are already 9 position players with pitchers separate), and derive `speed` / `arm` from existing ratings with seeded variation so migrated saves get plausible, non-uniform players. `user_version` 13 → 14; validated through `validate_sqlite_schema` before any C# touches it.

### 8.2 Ripples of the new ratings

`LeagueGenerator` (generate them), `HeirGenetics` (inherit them), `DevelopmentManager` / decline curves (speed peaks early and declines first; arm holds longer), `BackstoryGenerator`, the new-game screen (**pick a position**, not just hitter/pitcher), the scouting card (show them). Each is its own small slice in §12.

### 8.3 Stats

`ArcadeGame` accumulates into the **same** `BattingLine` / `PitchingLine` shapes and flushes through the shared path — additive season upserts, `Game_Logs` play-by-play, PED post-game costs — in its own batch, so the normalizer, promotion, development, achievements and the dashboard keep working untouched. Richer events fold into existing columns the way `PaOutcome` already folds them: reached-on-error and sacrifices → Out, HBP → Walk. Fielding, error and stolen-base columns are a later optional schema slice.

---

## 9. Calibration contract — the critical gate

**Why it's mandatory:** promotion and the league tables compare your team's numbers to everyone else's (§1). If AI-vs-AI physics produced, say, a .290 league average while the macro sim produces .247, every hitter on your team would look like a prospect.

**`ArcadeHarness`** (headless, plain .NET, the `MonteCarloHarness` pattern) plays thousands of AI-vs-AI arcade games and asserts:

1. At all-50 ratings, the league line matches the macro baselines in `AtBatResolver` (.247/.315/.412; K%, BB% and HR rates from the same tables) within the MC harness's tolerances.
2. Per-tier bands match the macro per-tier bands (`tier_league_environments.md`).
3. **Rating monotonicity:** more Power → more HR; more Contact → fewer K; more Control → fewer BB; more Fielding/Speed → lower BABIP against.
4. Batted-ball sanity: ground-ball / fly-ball / line-drive mix, BABIP, and the EV–launch-angle distribution against MLB shapes.
5. Rules integrity: no impossible base-out states, runs = runners crossing home, three outs per half, determinism (same seed + inputs → byte-identical box score).

Tuning happens **only** through the AI noise tables and physics constants (§6), as data edits. Re-run after any arcade-sim touch — the same mandate `run_monte_carlo_batch` carries for `Assets/Simulation/`.

**The human is deliberately uncalibrated.** A skilled player can beat their ratings — that's the point of the choice. Difficulty presets (§5.6) and higher-tier pitching (faster, nastier) keep it a challenge, and promotion still requires sustained league-relative performance.

---

## 10. Presentation — 16-bit

### 10.1 Resolution and scaling

The ballgame renders in a **320×180 SubViewport** with nearest-neighbor filtering, integer-scaled to the window: ×4 = 1280×720 (Steam Deck width), ×6 = 1080p, ×8 = 1440p, ×12 = 4K. The rest of the game (phone, dashboard) stays at native resolution — only the diamond is pixel art.

### 10.2 Views

- **Pitch view:** behind home plate looking out, pitcher at the top and batter at the bottom (the RBI framing), with depth conveyed by sprite scaling and a ball **shadow**.
- **Field view:** on contact, a smooth pan to an elevated angle that follows the ball. Ball height reads from shadow separation. Fielders run in eight directions. A small radar inset shows all runners.
- **Scoreboard strip:** count, outs, bases diamond, inning, line score — styled like a stadium board.

### 10.3 Team colors for free

One sprite set, **palette-swapped by shader** per team (jersey, cap, trim) plus per-player skin tones. High-school teams already carry primary/secondary/accent colors in `schools.json`; the `Teams` table has no color columns, so college/minor/MLB teams need colors added — generated by `LeagueGenerator` into a small team-colors table or a data file alongside `schools.json` (folded into slice A3). Every team in every tier then gets its own look without drawing it.

### 10.4 Parks that grow with your career

Field tilesets per tier: a weathered high-school field (chain-link, bleachers, odd dimensions) → college → minor-league parks → MLB stadiums with full crowds. Fence distance/height and surface per park feed the physics (§3.3). Night games under lights. Crowd density scales with tier and the game's stakes.

### 10.5 Game feel ("better than RBI")

- Hit-stop (a few frames' freeze) on solid contact; a small screen shake on a home run.
- Dust puffs on slides and dives; chalk spray on balls down the line.
- Ball trail on hard-hit balls; a crack-of-the-bat pitch that varies with exit velocity.
- **Instant replay** of big plays (free from determinism §2: re-run the recorded inputs).
- Optional CRT/scanline filter in settings.

### 10.6 Art pipeline and budget

**Art is the dominant external cost of this overhaul, not code.** Production animated sprites should come from a pixel artist (or a licensed sprite pack) in Aseprite. To keep the build unblocked:

- Phases A0–A7 run on **programmatic placeholder sprites** (simple colored figures drawn in code), so every system is built and tuned before art is commissioned.
- Sprite sheets plug in by a fixed **animation contract**: named animations, frame counts, pivot points, 8-direction runs.

Minimum animation list: batter (stance, load, swing, check swing, run); pitcher (set, windup, release, follow-through, field); catcher (crouch, frame, throw); fielders (ready, run ×8 directions, dive, catch high/low, throw); runner (run, slide, tag); umpire (calls); plus crowd tiles, park tilesets per tier, ball + shadow, dust/chalk effects.

All art is original: the RBI Baseball reference is for *style*, never for its assets.

---

## 11. What this retires or supersedes

- `AtBatView` and the "Read the Pitch" human path in `InteractiveBatterPolicy` / `PitchChain` — superseded for attended games. The sim code stays (it is `MicroGame`'s, and the harness covers it).
- **Open follow-ups this makes moot:** the At-Bat Read-Input §7.4 playtest and its re-band; checklist items about the at-bat view.
- `BaseballDashboard`'s attended-game play flow is rewired to launch the arcade scene instead.
- **"Classic sim" setting:** keep `MicroGame` selectable for attended games until the arcade engine has passed §9 and a full-season playtest; then decide whether to keep it as a "quick sim" option or retire it.

---

## 12. Phased build plan (model-tagged, one gate per phase)

| # | Slice | Owner | Gate |
| --- | --- | --- | --- |
| **A0** | **Feel prototype:** standalone scene, one batter vs one pitcher, pitch flight + contact + batted-ball flight + landing readout; placeholder sprites; no fielding, no DB | Fable | **User verdict: hitting is fun.** If not, iterate here before anything else is built. |
| A1 | Physics core + rules engine (§3), engine-independent, with rules/determinism unit checks | Fable | `ArcadeHarness` rules + determinism suites green |
| A2 | AI for every role (§6); AI-vs-AI full games headless | Opus (noise model) → Fable | **§9 calibration bands green, all tiers** |
| A3 | Schema v14 + generator/heirs/development/backstory/new-game ripples (§8) | Fable schema → Sonnet ripples | `validate_sqlite_schema`; MC bit-identity unchanged |
| A4 | `EffectiveRatings` extraction (§4.2) | Fable | **MC 100% green, MLB bit-identity byte-exact** (pure refactor) |
| A5 | Integration: `ArcadeGame` box score + shared flush, `PlayPendingArcadeGame`, autopilot routing, Classic setting | Fable | CoreLoop + new season-long harness: an arcade-attended season flushes valid stats, promotion runs |
| A6 | Presentation: SubViewport, pitch view, field view, scoreboard, controls + remapping | Sonnet build → Fable review | Clean boot; playable game end to end with placeholders |
| A7 | Watch & skip (§7) + play-by-play + box score panel | Sonnet → Fable review | Determinism check: watched vs skipped seed → identical box score |
| A8 | Take-over-the-team toggle + full baserunning control | Sonnet → Fable review | User playtest |
| A9 | Art swap-in, palette-swap shader, parks per tier, audio, replay, juice (§10) | Sonnet + commissioned art → Fable review | User eye-gate |
| A10 | Playtest + tuning pass, difficulty presets | Sonnet data edits → harness re-runs | User sign-off; decide Classic's fate (§11) |

**A0 first, always.** It's a few days of work, and it answers the only question no spec can: does it feel good to hit? Everything after it is engineering.

---

## 13. Open questions for the user

1. **High-school game length.** Real HS games are 7 innings; the sim plays 9 at every tier. Switch HS to 7? (It also shortens HS games you watch.)
2. **v1 extras:** stolen bases, pickoffs and bunts — in v1, or a follow-up slice?
3. **Takeover stats:** should games played in takeover mode count normally for teammates' stats and promotions, or be flagged?
4. **Classic sim:** keep it permanently as a "quick sim" option, or retire it once the arcade engine is proven?
5. **Art:** commission a pixel artist, buy a sprite pack and adapt it, or both (pack for crowds/tiles, artist for players)?

## 14. Risks

| Risk | Mitigation |
| --- | --- |
| AI-vs-AI physics can't hit the macro bands | Noise tables + physics constants are data knobs; A2 gates everything downstream; Classic fallback stays available |
| Hitting feels bad | A0 prototype gate before any integration work |
| Skilled humans break the economy of promotion | Difficulty presets; tier-scaled pitching; promotion stays league-relative and season-long |
| Art cost or schedule | Placeholders through A8; fixed animation contract; palette-swap cuts team art to one set |
| Scope creep (stealing, bunts, managers, injuries on HBP…) | v1 scope fixed in §3.6; extras are follow-up slices |
