# League Restructure — HS → College → Draft → Pro (Phase 13)

**Owner:** per-slice authors and reviewers are in [TEAM.md](../TEAM.md) §2 (owner-confirmed 2026-10-10), which **overrides the "Owner" column in §15 below**. Summary: Claude Opus 5.5 (13-0…13-3, 13-6 engine) · Gemini 3.1 Pro (13-4, 13-5; reviews Opus's slices) · Gemini 3.1 Pro Math (calibration) · Gemini 3.8 Flash (team seed data) · Claude Sonnet 5.5 (league UI, 13-7)
**Status:** design complete 2026-10-09; no code yet. **Authority:** [GAME_VISION.md](../GAME_VISION.md) §3 and the user-authored source spec in [BUILD_PLAN.md](../BUILD_PLAN.md) ("Phase 13 Source Spec"). Where this doc *interprets* the source spec, §16 says so explicitly.
**Supersedes:** [tier_league_environments.md](../archive/tier_league_environments.md) (6-tier ladder), [promotion_advancement_gates.md](../archive/promotion_advancement_gates.md) (tier-ladder promotion), and the league portions of [phase_8_9_interleave_plan.md](../archive/phase_8_9_interleave_plan.md).

---

## 1. What changes

| | Shipped (Phase 9, schema v13) | New (Phase 13, schema v14) |
| :-- | :-- | :-- |
| Tiers | 6: HS, College, MinorA, MinorAA, MinorAAA, MLB | **3: HS, College, Pro** |
| Teams per tier | 8 everywhere | **HS 12 · College 24 · Pro 32** (68 total) |
| Structure | 2 four-team "divisions" per tier | **Conferences and divisions** (§2) |
| Season | 154-game round-robin (HS: 26 games, Tue/Fri) | **HS 10 + playoffs (Fri) · College 14 + tournament (Sat) · Pro 64 + playoffs (Tue/Thu/Sat)** |
| Innings | 9 | **HS 7**, College/Pro 9 |
| Rosters | Fixed 9 + 5 SP + 3 RP for every team | **Per tier** (§5): Pro has strict org limits with active/inactive designations |
| Absences | A nameless replacement-level call-up fills the slot | **No replacements.** The slot stays empty; the lineup reshuffles; too few players means a forfeit (§7) |
| Advancement | `PromotionManager` ranks players and moves them up one tier each offseason | **Graduation → college recruiting → the Draft** (§9) |
| Playoffs | None | **Every tier** (§8) |
| Old saves | v13 | **Refused.** Clean break (§14) |

The **calibrated core is untouched.** `AtBatResolver`'s probability tables, the Markov matrices, PED/rivalry/fatigue modifiers and the tier-delta mechanism all stay. What changes is everything *around* the plate appearance: who is in the league, who plays, how often, for how many innings, and what happens at season's end.

---

## 2. Tiers, identity and team assignment

### 2.1 The enum

```csharp
public enum LeagueTier : byte { HS = 0, College = 1, Pro = 2 }
```

Keeping **Pro = 2** means every shipped content gate written as `tier >= 2` ("pro ball") keeps its intended meaning with no content edits. The unrostered sentinel `-1` is unchanged (§13).

### 2.2 Team ids and league labels

`TierTeamIdBase(tier) = (tier + 1) * 100`, uniformly. The frozen MLB ids 1–8 special case is retired.

| Tier | Ids | `Teams.league` label (display only) |
| :-- | :-- | :-- |
| HS | 101–112 | `HS` |
| College | 201–224 | `CBL` (College Baseball League; name clearance in [steam_commercial_release.md](steam_commercial_release.md) §5) |
| Pro | 301–332 | `PRO` (placeholder; the league brand is a §16 open item. **Not** "Diamond League", which is a real athletics brand) |

### 2.3 Conferences and divisions

- **HS:** 2 conferences of 6 (North, South). No divisions.
- **College:** 2 conferences (East, West) × 3 divisions × 4 teams.
- **Pro:** 2 conferences (East, West) × 4 divisions × 4 teams.

The full team assignment is **Appendix A**. Pro reuses all 32 existing pro-level seeds (the 8 former MLB teams plus the 24 former minor-league teams), so no shipped team identity is lost. College keeps its 8 and adds 16. HS keeps its 8 schools and adds 4. Names marked *new* are placeholders a content pass may rename (trademark screening per the Steam doc §5).

---

## 3. Season calendar

Day-of-season 1 = January 1 (`GameCalendar`). All three tiers share the calendar year. The avatar is only ever in one tier, so overlapping seasons never compete for the avatar's time.

| Tier | Game day(s) | Regular season | Postseason | Practice days |
| :-- | :-- | :-- | :-- | :-- |
| HS | **Friday** | 10 weeks from the first Friday of March | 3 weeks (bracket of 6) | Mon–Thu after school |
| College | **Saturday** | 14 weeks from the second Saturday of February | Play-in week + conference tournaments + National Championship (≤ 6 weeks) | Mon–Fri |
| Pro | **Tue / Thu / Sat** | 22 weeks from the first Tuesday of April (weeks 1–21: 3 games; week 22: 1 game = 64) | Bo3 rounds + Bo5 Championship (Sept–Oct) | Mon, Wed, Fri; **Sunday night = roster lock** |

### 3.1 Schedule construction (deterministic, generated once per season)

`SeasonScheduler.Generate(tier, seasonYear, rng)` runs at season rollover and writes `Schedule_Games` rows (§12). The schedule moves from "pure function of the day" to **persisted rows**, because conference/division formats and playoffs are not a simple circle method. Generation is seeded, so the same save seed produces the same schedule.

- **HS (10 games):** double round-robin inside each 6-team conference (5 opponents × 2). Home/away alternates.
- **College (14 games):** division double round-robin (3 × 2 = 6) plus one game against each of the 8 other teams in the conference (8). No cross-conference regular-season games; the National Championship is the only cross-conference matchup.
- **Pro (64 games):** division opponents × 8 (3 × 8 = 24), other conference teams × 2 (12 × 2 = 24), other conference's teams × 1 (16). Home/away balanced at 32/32.

Every team plays at most one game per game day. The scheduler's constraint is "each team plays exactly once per game day". For Pro, `PairingsPerGameDay = 16`.

### 3.2 What happens to the shipped HS calendar

The shipped `HsSeasonCalendar` (26 games, Tue/Fri, Mar 1–May 31) is **replaced** by `TierSeasonCalendar`, which covers all three tiers per the table above. The source spec's "1 game per week" wins. School days, practice blocks and the planner's forced blocks keep their shipped mechanics, keyed off the new calendar. Consequence: HS players get roughly 2.5× fewer games and more practice days. The parked "practice-lever retune" follow-up (progress.md) is folded into slice 13-3's development re-check.

---

## 4. Game rules

- **Regulation innings:** `TierRules.RegulationInnings(tier)`: HS 7, College 9, Pro 9. `MicroGame` and the macro game loop take it as a parameter in place of the hard-coded `9` (`LeagueSimulator.cs` ~L518–530 and `MicroGame` game-flow rules). The home half is skipped when the home team leads after the top of the final regulation inning; extra innings work as shipped.
- **No ties, no mercy rule.** (A mercy rule for HS is a §16 option, off by default.)
- **Forfeits** are recorded as a 7–0 (HS) or 9–0 (College/Pro) final with **no player stats credited**, status `forfeit` (§7).
- The 25 base-out states and the per-PA model are unchanged at every tier.

---

## 5. Rosters

### 5.1 Per-tier limits

| | HS | College | Pro |
| :-- | :-- | :-- | :-- |
| Starting pitchers | no limit (generated 3–4) | ≤ 4 | **≤ 4 (≤ 3 active, ≥ 1 inactive slot)** |
| Relievers | no limit (generated 2–4) | ≤ 5 | **≤ 5 (3 active, 2 inactive)** |
| Position players | no limit (generated 11–14) | ≤ 17 | **exactly 15 (12 active: 9 starters + 3 bench; 3 inactive)** |
| Active cap | everyone | **20** (Pro's 18 + 2 open spots, §16-I1) | **18** |
| Organization cap | none (generated 16–22) | 26 | **24** |
| Designations | none (all active) | coach's choice each game day | **Sunday lock** (§7) |

### 5.2 Engine representation (zero-allocation preserved)

The `LineupSize = 9 / RotationSize = 5 / BullpenSize = 3` constants become **capacities**. Fixed-size buffers stay fixed-size, so `stackalloc` and array sizing in `MicroGame` and `LeagueSimulator` remain allocation-free:

```csharp
public static class RosterCapacity
{
    public const int Lineup     = 9;   // always 9 batters/defenders
    public const int MaxBench   = 8;
    public const int MaxStarters = 6;
    public const int MaxRelievers = 6;
}
```

Each team carries `startersAvailable` / `relieversAvailable` / `benchAvailable` counts per game day. Loops run to the count, never to the capacity. **This is the refactor the Phase 8/9 interleave plan deliberately deferred.** It is slice 13-2 and goes through the full Monte Carlo gate.

### 5.3 Generation

`LeagueGenerator` seeds each tier's rosters to the generation targets above, on the shipped rating bell (spread 25). Age rolls: HS 15–18 (consistent with `SchoolGrade`), College 18–22, Pro 21–36 (the frozen MLB roll).

---

## 6. Pitching usage — "ride the ace"

The source spec says teams "heavily rely on their aces, often employing a 2-man rotation and starting their #1 pitcher in every game possible." This **emerges** from one rule, so no special-case AI is needed:

> **Starter selection:** on each game day, the highest-rated available starter whose `days since last start ≥ MinRestDays(tier)` starts. If none qualifies, the best-rested starter starts on short rest; if no starter is available at all, a reliever opens (a bullpen game).

| Tier | Games/week | `MinRestDays` | Result |
| :-- | :-- | :-- | :-- |
| HS | 1 | 4 | The ace starts every game |
| College | 1 | 4 | The ace starts every game |
| Pro | 3 (Tue/Thu/Sat) | **3** | The ace starts Tue → Sat → Tue → …, two of every three games; #2 takes the Thursdays. A natural 2-man rotation |

**Workload risk (Math-owned calibration):** a start on short rest docks the starter's in-game stamina capacity by `ShortRestCapacityPenalty` and adds `OveruseInjuryRisk` per start, rolled through the shipped injury absence path (Phase 8c). Riding the ace wins games and wears out arms, which is the trade-off the spec implies. The macro sim stays complete-game (its §8 calibration is pinned to that shape), so macro starters follow the same selection rule without in-game fatigue.

---

## 7. Sunday roster lock and the attrition & forfeit rule (Pro)

- **Lock:** at **Sunday 23:59** game time, every Pro team's active/inactive designations for the coming Monday–Sunday are written to `Roster_Designations` and locked. NPC teams are set by `RosterManager`'s coach AI: the best healthy players by role-weighted rating; injured or absent players go inactive when the roster allows.
- **The avatar is a player, not a GM.** The coach designates the avatar too. Performance, health and coach affinity (`RelationshipGraph`) drive the call. Being made **inactive** means no games that week (practice only), a coach text, and a stress hit. Being activated after a stint inactive is a narrative beat.
- **Attrition:** if an active player becomes absent mid-week (injury, suspension, arrest via the shipped `AvailabilityLedger`), **no inactive player may replace them.** The lineup reshuffles from the remaining active players.
- **Forfeit:** before each game, `RosterManager.CanField(team, day)` requires **9 available active position players and at least 1 available active pitcher**. If that fails, the team forfeits (§4). This applies to both NPC teams and the avatar's team. The avatar gets a coach text, and the at-bat view never opens.
- **College/HS:** no lock. The coach picks from the whole roster each game day, and absent players simply aren't picked. The same `CanField` check applies, so a decimated HS team forfeits too.
- **Retired everywhere:** the "nameless replacement-level call-up" that `AvailabilityLedger` currently uses to shadow absent players. With no minor leagues there's nobody to call up. Absence now means a real hole.

---

## 8. Playoffs

Higher seeds host. Standings tiebreakers in order: head-to-head, run differential, runs allowed, then a seeded coin flip.

### 8.1 HS — bracket of 6, single elimination

- The top 3 per conference qualify; **#1 seeds get a bye.**
- **Week 1:** N2 vs N3, S2 vs S3. **Week 2 (conference finals):** N1 vs winner, S1 vs winner. **Week 3:** State Championship (North champion vs South champion).

### 8.2 College — 18-team tournament

- **National Ranking** (`RankingCalculator`, recomputed weekly, shown in the UI as "#7 nationally"):
  `RankScore = 0.50·Win% + 0.30·SOS + 0.20·RD_norm`, where `SOS` is opponents' combined win% and `RD_norm` is run differential per game mapped to [0,1] (clamped at ±5 runs/game). Weights are Math-owned knobs.
- **Selection:** ranks 1–17 qualify automatically. **Play-in:** #18 vs #19, single game, for the 18th spot (played the week after the regular season).
- **Conference tournaments:** each conference's qualifiers form a single-elimination bracket seeded by national rank. Top seeds get byes up to the next power of two. One game per week.
- **National Championship:** a single game between the two conference tournament champions.
- With uneven conference counts (e.g. 10 + 8), the byes absorb the difference automatically. The postseason is at most 6 weeks (play-in + up to 4 rounds + the final).

### 8.3 Pro — series format (interpreted, §16-I3)

- **Field: 12 teams.** Per conference, the 4 division winners (seeds 1–4 by record) and 2 wild cards (seeds 5–6). Seeds 1–2 get a bye.
- **Wild Card round (Bo3):** 3 v 6, 4 v 5. **Division round (Bo3):** 1 v lowest remaining, 2 v other. **Conference Final (Bo3).** **Championship Series (Bo5)**, East champion vs West champion.
- Series games are played on consecutive Tue/Thu/Sat game days. **Bo3 hosting:** the higher seed hosts games 1 and 3. **Bo5 hosting:** 2-2-1 (higher seed hosts games 1, 2 and 5).
- `Playoff_Series` rows track each series. Starter rest rules (§6) apply, so the ace matters even more in October.

---

## 9. The pipeline — graduation, recruiting, the Draft

```text
 HS (4 grades; the avatar starts as a freshman at 16, generated intake is 15–16)
   │ senior fall: recruiting offers → Signing Day (avatar chooses)
   ▼
 College (class years 1–4)
   │ after sophomore season or later: declare for the Draft (or return)
   ▼
 Draft (July, 4 rounds × 32 = 128 picks, reverse Pro standings)
   │ drafted → signs → reports to the Pro team next spring
   ▼
 Pro (until retirement / release + unsigned / ban / death)
   │ career end → succession (heir) or lineage over
```

### 9.1 HS graduation and recruiting

- HS grade comes from age via the shipped `SchoolGrade` (freshman start, merit-based graduation, held-back mechanic all unchanged).
- **Recruiting (each fall, for seniors):** each College program has `OpenSpots` (graduating seniors plus departures). Programs rank recruits by **recruit value** = the shipped 9c advancement score (performance `P` + scouting `S`, [promotion_advancement_gates.md](../archive/promotion_advancement_gates.md) §2, kept as the value function). Better programs (by last season's national rank) pick first. Each program extends offers to recruits within its band.
- **Avatar:** in senior fall, the avatar receives **1–5 offers** through the phone (`coach_hs` / `agent_diaz` threads) and chooses one. That's **Signing Day**, a cutscene. Guarantee: if no program offers, the avatar gets a **walk-on** offer from the lowest-ranked program, so the career always continues.
- **Population gap (disclosed):** 12 HS teams graduate about 55 seniors a year, but 24 College teams need about 150 freshmen. The remainder are **generated recruits from outside the simulated HS league** (same rating bell, slightly lower mean). The pipeline is strictly linear for the avatar and every simulated HS player. The wider world fills the rest.

### 9.2 College class years and eligibility

- `College_Enrollment.class_year` runs 1–4 and advances at season rollover. After year 4, eligibility ends.
- **Draft eligibility:** a player who has **completed their sophomore season** (class_year ≥ 2 finished) may declare. Seniors who finish year 4 are automatically in the pool.
- **NPC declaration:** declare if the projected draft slot (by recruit value against the class) is within the first 2 rounds, or if age ≥ 22. Otherwise return.
- **Avatar declaration:** a phone decision after the College season ("Declare for the Draft?"), with the scouting report's projected round shown. Undrafted juniors may return for their next year. Undrafted seniors enter **Pro free agency** (§9.4).

### 9.3 The Draft

- **When:** the second Saturday of July (after the College National Championship and before the Pro stretch run).
- **Order:** reverse Pro regular-season standings. Non-playoff teams first; playoff teams ordered by round of elimination; the champion picks last. No lottery.
- **Rounds:** 4 (128 picks). That matches steady-state turnover: 768 Pro roster spots ÷ about 6-season average careers ≈ 128 a year. Math owns the conservation check: in the PipelineHarness, Pro population stays within ±5% of capacity across 20 simulated seasons.
- **Team AI:** best available by recruit value, with a role-need tiebreak (a team short on SP takes the SP).
- **Avatar:** **Draft Night** is a cutscene plus a phone moment. The drafting team's announcer gets a "we've drafted…" clip. The avatar signs (a signing bonus via `PlayerQueries.AdjustFunds`, a money spike that feeds the life-sim temptation loop) and **reports next spring**. Between the draft and Opening Day, the avatar lives a "signed prospect" offseason (training, money, risk).

### 9.4 Pro roster turnover

- After the draft and before the next season, any team over its 24-player org cap **releases** its lowest-value players to free agency.
- **Free agency** is open only at the Sunday lock (the lock is the only roster-change moment) and only below the org cap. Unsigned free agents retire at season rollover if age ≥ 30 or after 2 unsigned seasons.
- **Retirement** works as shipped (age, health floor, decline), and so does succession for the avatar.
- **First Pro contract (Phase L hook).** `PipelineManager` publishes the additive Core event `ProContractSignedEvent(playerId, teamId, day, wasDrafted)` for **every** first Pro contract: the drafted signing (§9.3) **and** a free-agent signing of an undrafted player (§9.2/§9.4). The father's first text keys on it ([temptation_and_lineage.md](temptation_and_lineage.md) §4.2), and so does the Trailblazer's "first" (§8.1 there). The playoffs' `ChampionshipWonEvent` (also Phase 16b) drives the lineage reveal (§4.4 there).

### 9.5 What replaces `PromotionManager`

`PromotionManager` is retired. **`PipelineManager`** owns graduation, recruiting, draft declaration, the Draft, releases and free-agent retirement. It reuses: the 9c advancement score as the value function, the 9c avatar tier-transfer handoff (`ReactivateAvatar` republish, roster-invariant discipline) for HS→College and College→Pro moves, and the succession handoff's displacement/backfill rules.

---

## 10. The avatar's journey (presentation hooks)

| Moment | Where it surfaces | Cutscene ([cutscenes_motion_comic.md](cutscenes_motion_comic.md)) | Voice |
| :-- | :-- | :-- | :-- |
| New game | HS team assigned | Opening story cutscene | — |
| HS playoff clinch / State title | Dashboard + phone | Milestone (title only) | HS radio pool call |
| Recruiting offers → Signing Day | Phone decision | **Signing Day** | Text from the new college coach |
| First college game | At-bat view | Short milestone | College team announcer |
| National Ranking milestones | Dashboard | — | — |
| Declare for the Draft | Phone decision | — | Text (agent) |
| **Draft Night** | Phone + dashboard | **Draft Night** | Drafting team's announcer "we've drafted…" clip |
| First Pro game | At-bat view | Milestone | Pro team announcer intro |
| Made inactive this week | Phone (coach) | — | Coach text |
| Team forfeits | Phone + dashboard | — | — |
| Playoff series win / Championship | Dashboard | **Championship** | Announcer signature win call |

---

## 11. Calibration

### 11.1 Tier environments

The tier-delta mechanism ([tier_league_environments.md](../archive/tier_league_environments.md) §2) carries over **unchanged in kind**, now with three rows. The HS and College values stay; Pro takes the MLB row:

| Tier | pitStuff / pitControl / teamDefense delta | Batter deltas |
| :-- | :-- | :-- |
| HS | −20 | 0 |
| College | −16 | 0 |
| Pro | **0** (the shipped Phase 3 calibration) | 0 |

### 11.2 Acceptance bands (`run_monte_carlo_batch`)

The AVG/OBP/SLG/K%/BB%/HR-per-PA bands for HS, College and Pro reuse the shipped HS, College and MLB rows verbatim. **Runs are expressed per 9 innings (R/9)** so 7-inning HS games compare cleanly. HS R/9 band 6.4–8.0 (equivalent to about 5.0–6.2 runs per 7-inning game).

### 11.3 The regression guards

- **Resolver guard (unchanged, must stay byte-exact):** the per-PA §8 batch and §5 fixtures. `AtBatResolver` isn't touched, so these must not move.
- **Season-pipeline guard (re-baselined once, sanctioned here):** the shipped bit-identity guard (PA 48384 / H 10969 / ER 5237) is tied to the 8-team, 154-game MLB season and **cannot** survive a 32-team, 64-game Pro league. Slice 13-3 retires it and records a **new Pro guard** (the same three totals for a fixed-seed Pro season) on the first fully green run. From then on, the new guard is byte-exact like the old one.
- **Development gradient:** the 9d talent-gradient check becomes HS < College < Pro (time-averaged mean talent, strictly monotone), retuned per slice 13-3.

---

## 12. Schema v14

Clean break (§14). Bump `PRAGMA user_version` to 14. Every table and column goes through `validate_sqlite_schema` / the sqlite MCP first (No Blind Queries).

```sql
CREATE TABLE Conferences (conference_id INTEGER PRIMARY KEY, tier INTEGER NOT NULL, name TEXT NOT NULL);
CREATE TABLE Divisions   (division_id INTEGER PRIMARY KEY, conference_id INTEGER NOT NULL REFERENCES Conferences, name TEXT NOT NULL);

-- Teams: tier is 0..2; new columns
ALTER-equivalent: Teams(conference_id INTEGER NOT NULL REFERENCES Conferences,
                        division_id   INTEGER NULL REFERENCES Divisions,   -- NULL for HS
                        home_park     TEXT NOT NULL)                         -- announcer intros, UI

CREATE TABLE Schedule_Games (
  game_id INTEGER PRIMARY KEY, season_year INTEGER NOT NULL, tier INTEGER NOT NULL,
  day_of_season INTEGER NOT NULL, home_team_id INTEGER NOT NULL, away_team_id INTEGER NOT NULL,
  phase INTEGER NOT NULL,            -- 0 regular, 1 play-in, 2 playoff
  series_id INTEGER NULL REFERENCES Playoff_Series, game_in_series INTEGER NULL,
  status INTEGER NOT NULL DEFAULT 0, -- 0 scheduled, 1 final, 2 forfeit
  home_score INTEGER NULL, away_score INTEGER NULL);
CREATE INDEX ix_sched_day ON Schedule_Games(season_year, tier, day_of_season);

CREATE TABLE Playoff_Series (
  series_id INTEGER PRIMARY KEY, season_year INTEGER NOT NULL, tier INTEGER NOT NULL,
  round INTEGER NOT NULL, best_of INTEGER NOT NULL,
  high_seed_team_id INTEGER NOT NULL, low_seed_team_id INTEGER NOT NULL,
  high_wins INTEGER NOT NULL DEFAULT 0, low_wins INTEGER NOT NULL DEFAULT 0, winner_team_id INTEGER NULL);

CREATE TABLE Team_Seasons (            -- materialized standings + ranking
  team_id INTEGER NOT NULL, season_year INTEGER NOT NULL, wins INTEGER, losses INTEGER,
  runs_scored INTEGER, runs_allowed INTEGER, national_rank INTEGER NULL, seed INTEGER NULL,
  PRIMARY KEY (team_id, season_year));

CREATE TABLE Roster_Designations (     -- Pro weekly lock (College/HS: unused)
  player_id TEXT PRIMARY KEY REFERENCES Players, team_id INTEGER NOT NULL,
  is_active INTEGER NOT NULL, locked_through_day INTEGER NOT NULL);

CREATE TABLE College_Enrollment (
  player_id TEXT PRIMARY KEY REFERENCES Players, team_id INTEGER NOT NULL,
  entry_season INTEGER NOT NULL, class_year INTEGER NOT NULL, declared_season INTEGER NULL);

CREATE TABLE Recruiting_Offers (
  player_id TEXT NOT NULL REFERENCES Players, team_id INTEGER NOT NULL, season_year INTEGER NOT NULL,
  status INTEGER NOT NULL DEFAULT 0,  -- 0 open, 1 accepted, 2 declined, 3 expired
  PRIMARY KEY (player_id, team_id, season_year));

CREATE TABLE Draft_Picks (
  season_year INTEGER NOT NULL, overall_pick INTEGER NOT NULL, round INTEGER NOT NULL,
  team_id INTEGER NOT NULL, player_id TEXT NOT NULL REFERENCES Players,
  PRIMARY KEY (season_year, overall_pick));
```

All existing v13 tables (Players, Player_Ratings, stats, Relationships, Entity_Flags, Player_Person, Player_Absences, Game_State, …) carry forward unchanged in shape.

**Added for Phase G (owner, 2026-10-13):** `Player_Fielding` (`player_id`, `defensive_position`, `secondary_position`, `speed`, `arm`) folds into v14, so Appendix A world-gen fills it for every player. Spec: [arcade_baseball_overhaul.md](arcade_baseball_overhaul.md) §8.1. If 13-1 merges first, it ships in the next free version instead.

---

## 13. Engine change map

| File / system | Change |
| :-- | :-- |
| `BaseballDtos.cs` | `LeagueTier` → `HS, College, Pro` |
| `SchemaDefinitions.sql`, `DatabaseManager` | v14 DDL; boot refuses `user_version < 14` (§14) |
| `LeagueGenerator.cs` | Appendix A seeds, conferences/divisions, per-tier roster generation, uniform id bases |
| `LeagueSimulator.cs` | `TeamCount` per tier instance; `RosterCapacity` + per-team counts; regulation innings; reads `Schedule_Games` |
| `LeagueSchedule.cs` | Replaced by `SeasonScheduler` (generate) + `ScheduleQueries` (read) |
| `HsSeasonCalendar.cs` | Replaced by `TierSeasonCalendar` (all tiers, §3) |
| `MicroGame.cs` | Regulation innings parameter; roster counts; starter selection (§6) |
| `AvailabilityLedger.cs` | Replacement call-up retired; absence leaves a hole |
| *new* `RosterManager` | Sunday lock, coach designations, `CanField` / forfeit |
| *new* `PlayoffManager` | Seeding, brackets, series, champions; publishes `PlayoffSeriesDecidedEvent`, `ChampionshipWonEvent` |
| *new* `RankingCalculator` | College national ranking (weekly) |
| `PromotionManager.cs` → *new* `PipelineManager` | Graduation, recruiting, draft, releases (§9); publishes `RecruitingOffersEvent`, `DraftCompletedEvent` |
| `CareerManager.cs` | Avatar pairing from schedule rows; draft/recruit handoffs (reusing the 9c handoff) |
| `TierEffects.cs` | Three rows (§11.1) |
| `DevelopmentManager.cs` | Three-tier gradient; minor-league notes removed |
| `StatsNormalizer.cs` | Already per-tier; season shape changes only |
| `AchievementManager.cs`, `RichPresenceWriter.cs` | New tiers and milestones ([steam_commercial_release.md](steam_commercial_release.md) §3) |
| `ConditionEvaluator` | `tier` ordinals 0–2; the `-1` unrostered sentinel unchanged; the missing-tier COALESCE default becomes Pro(2) |
| UI | Standings (conferences/divisions), Calendar (weekly game days + postseason), Schedule screen game-day blocks, **new:** Playoff Bracket, Recruiting Offers, Draft (declare + Draft Night board), Roster Status (active/inactive this week) |

Architectural boundaries are unchanged: `PipelineManager`, `RosterManager` and `PlayoffManager` live in `/Simulation/Baseball/`, talk to the Life sim only through bus events and the DB, and the UI stays read-only over DTOs.

---

## 14. Saves: a clean break

The game hasn't released. On boot, a save with `user_version < 14` gets a dialog: *"This save is from an older version of Dirt & Diamonds and can't be loaded. Start a new career?"* The old file is **renamed** to `dirt_and_diamonds.v13.bak`, never deleted, and a new world is generated. **From 1.0 onward, every schema change ships a migration** ([steam_commercial_release.md](steam_commercial_release.md) §8).

---

## 15. Build slices (each one gated; owners per BUILD_PLAN)

| Slice | Content | Owner | Gate |
| :-- | :-- | :-- | :-- |
| **13-0** | Prerequisites: sqlite MCP repaired; the 344/345 MC flake identified and seeded (failed-check recap on the harness tail) | Pro | MC green 20/20 runs |
| **13-1** | Schema v14 + clean-break boot + `LeagueTier` collapse + Appendix A world-gen (68 teams, conferences/divisions, per-tier rosters) | Pro (Flash: seed tables) | SchemaValidator; world-gen round-trip; FK integrity |
| **13-2** | `RosterCapacity` refactor + regulation innings + starter selection (§6) | Pro | **MC full suite**; zero-alloc checks; resolver guard byte-exact |
| **13-3** | `SeasonScheduler` + `TierSeasonCalendar` + schedule-driven sims; per-tier bands; **new Pro guard recorded**; development gradient retune | Pro + Math | MC per-tier bands; new guard; CoreLoop |
| **13-4** | `PlayoffManager` + `RankingCalculator` + bracket UI | Pro (Flash: UI) | Playoff checks (every tier crowns exactly one champion; series lengths legal; seeding/tiebreaks) |
| **13-5** | `RosterManager`: Sunday lock, attrition, forfeits; call-up retirement | Pro | Availability suite rewritten; forfeit fixtures |
| **13-6** | `PipelineManager`: graduation, recruiting, draft, releases; Recruiting/Draft UI; avatar decisions | Pro + Math (Flash: UI) | **PipelineHarness**: 20-season run, population conservation ±5%, a generated player goes HS → College → drafted → Pro |
| **13-7** | Ripple: achievements/rich presence, content tier docs, Steam README tokens, dashboard/standings/leaders cards | Flash → Pro review | GrittyEvents, full suite, live boot |

---

## 16. Interpretations and open items

Places where the source spec was ambiguous. Each is a recommended default that the user may overrule.

- **I1. College "2 extra open starting spots".** Read as **+2 active roster slots usable at any position** (active cap 20 vs Pro's 18). The alternative is 2 extra *lineup* spots (an 11-batter lineup), which would need resolver/lineup changes and is not recommended.
- **I2. HS/College regular-season length.** The spec gives cadence (1/week) but not length. Chosen: HS 10 games (conference double round-robin), College 14 (division double + conference single).
- **I3. Pro playoff field.** The spec gives formats (Bo3 early, Bo5 final) but not field size. Chosen: 12 teams (division winners + 2 wild cards per conference, top 2 seeds bye).
- **I4. Draft timing and reporting.** Draft in July; picks report the following spring. No mid-season call-ups, because there are no minors and the Sunday lock is the only roster-change moment.
- **I5. Lock scope.** The Sunday lock and forfeit-without-replacement are Pro rules (spec §5). College and HS have no lock, but the "can't field 9" forfeit applies everywhere, because no call-ups exist.
- **Open: league and brand names.** The Pro league's name, conference names and the College league name all need a trademark screen before ship.
- **Open: HS mercy rule** (10-run rule after 5 innings). Off by default.
- **Open: college academic eligibility** (GPA floor to play). Not in v1; the GPA system already exists if wanted.

---

## Appendix A — Team assignment (68)

### Pro (32) — ids 301–332

| Conference | Division | Teams |
| :-- | :-- | :-- |
| East | Northeast | Bayport Barons (BAY) · Kingsbridge Krakens (KBR) · Harborview Herons (HBV) · Scranton Steamers (SCR) |
| East | Atlantic | Charlotte Crowns (CLT) · Durham Drakes (DRM) · Roanoke Ridgerunners (RNK) · Palmetto Pelicans (PLM) |
| East | Great Lakes | Ironvale Foxes (IRV) · Toledo Titans (TLD) · Galena Gliders (GLN) · Norwood Nailers (NRW) |
| East | Gulf | Hattiesburg Hornets (HTB) · Chattahoochee Catfish (CHT) · Kannapolis Knights (KNP) · Laredo Lobos (LRD) |
| West | Plains | Omaha Outlaws (OMA) · Cedar Rapids Colts (CDR) · Sioux Bend Storm (SXB) · Twin Forks Trout (TWF) |
| West | Mountain | Bozeman Bighorns (BZM) · Provo Prospectors (PRV) · Albuquerque Aviators (ABQ) · Dustfield Drifters (DST) |
| West | Southwest | Mesa Roja Rattlers (MRJ) · Sablewood Stallions (SBW) · Fresno Firebirds (FRS) · Modesto Mustangs (MDS) |
| West | Pacific | Silverlake Sharks (SLK) · Cascadia Wolves (CAS) · Tacoma Thunder (TAC) · Yuba City Yellowjackets (YBC) |

### College (24) — ids 201–224 (*new* = placeholder)

| Conference | Division | Teams |
| :-- | :-- | :-- |
| East | Coastal | Ashford State Admirals (ASH) · Blue Valley Bison (BLV) · Glenhaven Gulls (GLH) *new* · Saltmarsh State Stingrays (SMS) *new* |
| East | Capital | Carverton Tech Chargers (CVT) · Dunmore Dukes (DNM) · Marbury Miners (MRB) *new* · Wexford Wildcats (WXF) *new* |
| East | Valley | Lorain Valley Lancers (LRV) *new* · Northgate Nighthawks (NGT) *new* · Union Mills Millers (UNM) *new* · Vance County Vipers (VNC) *new* |
| West | Lakes | Eastlake Eagles (ELU) · Midland Mavericks (MAM) · Kessler State Kodiaks (KSS) *new* · York Plains Pioneers (YKP) *new* |
| West | Frontier | Fort Garland Grizzlies (FTG) · Hollis Hawks (HOL) · Oxbow A&M Otters (OXB) *new* · Redstone Tech Rockets (RST) *new* |
| West | Canyon | Pell City Pilots (PLC) *new* · Quarry Hill Quarrymen (QRH) *new* · Tolland Timberwolves (TOL) *new* · Zion Ridge Zephyrs (ZNR) *new* |

### High School (12) — ids 101–112

| Conference | Teams |
| :-- | :-- |
| North | Crestwood Cardinals (CRW) · Lakeside Longhorns (LKS) · Fairview Falcons (FRV) · Oak Ridge Owls (OKR) · Brookhaven Bulldogs (BRK) *new* · Cedar Grove Comets (CDG) *new* |
| South | Pinehurst Panthers (PNH) · Westfield Warhawks (WSF) · Summit Spartans (SMT) · Riverton Rams (RVT) · Granite Falls Gators (GRF) *new* · Hillcrest Hurricanes (HLC) *new* |

The 4 new HS schools need `schools.json` entries and art (logo / school / field) per [scene_art_guide.md](../archive/scene_art_guide.md). Every College and Pro team needs a logo, a `home_park` name, and an announcer persona ([voice_and_broadcast_audio.md](voice_and_broadcast_audio.md) §5).
