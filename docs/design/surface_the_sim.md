# Surface the Sim — Dashboard Read-Model Placement (Phase 12c)

**Owner of this doc:** Opus 4.8 (placement design only — NO code). Per the standing Phase-12
split, Opus owes this placement sketch *before* Sonnet 5 builds the card volume; **Fable 5** wires
the new seams and reviews. 12c is the "surface-the-sim" step of the Phase-12 "Saleable" arc
(12a render integrity → 12b layout → **12c surface-the-sim** → 12d at-bat presentation → …).
12d (at-bat presentation) is the sibling step with its own pending Opus pass; it is **out of scope
here**.

Grounded against the live code this session (No Blind Queries applies to design too):
`Assets/UI/BaseballDashboard.tscn` + `.cs` (the 12b card row), `Assets/UI/TwoPanelShell.tscn`,
`Assets/UI/Main.tscn`, `Assets/Data/Database/BaseballQueries.cs`, `SchemaDefinitions.sql`, and
`Assets/Simulation/Baseball/LeagueSimulator.cs` (the W/L decision write, `LeagueSimulator.cs:494-503`
+ `:824`). `ui_conventions.md` and `presentation_layer_narrative.md` (Phase 10, the shell/scouting
foundation this builds on) are the acceptance frame.

---

## 1. Thesis & Scope

Phase 10 gave the dashboard the avatar's **own** picture (scouting grades, development). Phase 12b
freed the dashboard's vertical space (the dead scoreboard is gone; the card row owns idle days). The
game still shows the player almost nothing about the **world their avatar plays in** — no standings,
no league leaders, no season stat-line, no durable record of the game they just played. A baseball
sim that can't tell you where your team sits or who leads the league does not read as a baseball
sim. **12c surfaces the simulation the engine already runs**, as read-only cards on the dashboard.

**Four hard rules, carried verbatim from `ui_conventions.md` and Phase 10:**

1. **UI is read-only over sim state.** Every new card renders DTOs. No card writes the DB or
   mutates sim state. 12c adds **read** query surface only — zero new writes.
2. **No schema change.** Every surface below is sourced from tables that already exist (§2 proves
   it, table by table). `PRAGMA user_version` does not move; `SchemaValidator` re-runs green
   unchanged. This is asserted, not assumed.
3. **Dirty-flag refresh, never per-frame.** All of these read-models move only when the day ticks
   (the `RefreshScoutingCard` cadence — day-advance is the only thing that changes standings, a
   stat-line, or leaders). No query, LINQ, or string-format runs in `_Process`.
4. **Thin vertical slice.** 12c ships as sequenced sub-slices (§6), each leaving the game bootable
   and every prior screen reachable — not a big-bang dashboard rewrite.

**Non-goals:** no `Assets/Simulation/` touch (so `run_monte_carlo_batch` is inert by construction —
state it with a `git diff --stat -- Assets/Simulation` proof, the 10/11/12b precedent), no at-bat
presentation work (that's 12d), no new hustle/economy surface.

---

## 2. Seam Audit — What Is Actually Sourceable (the load-bearing section)

A placement sketch that proposes a card the engine can't feed is worse than useless. Every surface
below was checked against the live query layer and schema this session:

| Surface | Source seam (live) | New query? | Cost |
| :------ | :----------------- | :--------- | :--- |
| **Schedule** | `ScheduleScreen`, already docked in the 12b `CardsRow` | none | **ships already** — 12c only *repositions* it (§3) |
| **Last-game recap** | the `MicroGameResult` already in hand at `BaseballDashboard.FinishInteractiveGame` (`AwayScore/HomeScore/Innings/HumanPa`) | none (attended games) | cache one struct across day-advances |
| **Avatar season stat-line** | `Batting_Stats` / `Pitching_Stats` row for `(avatar, current season)` | 1 trivial single-row `SELECT` | rides the `UNIQUE(player_id, season_year)` index |
| **League leaders** | `Batting_Stats` / `Pitching_Stats`, top-N by a stat, tier-scoped | 1 `ORDER BY … LIMIT` query | + name resolve (join or `Players.TryGetById`) |
| **Standings (W-L)** | **`Pitching_Stats.w`/`.l` summed per team** — see below | 1 tier-scoped aggregation query | **confirmed: no schema change** |

**The standings finding (the one that could have forced a schema change, and didn't).** There is no
`Team_Records`/`Schedule`/`Results` table and no stored game scores. But `LeagueSimulator` credits
**exactly one W to the winning team's starter and one L to the losing team's starter every macro
game** (`LeagueSimulator.cs:494-503`, flushed at `:824`). So a team's record is *exactly* recoverable
as `SUM(w)` / `SUM(l)` over its pitchers for the season — no reliever ever vultures a macro decision,
so `team W + team L = team games played` by construction, and `Σ team W = Σ team L` league-wide.
Standings are a **new aggregation query, not new storage.** (Verify point for Sonnet: confirm the
*attended* path — `MicroGame` — credits a starter W/L the same way, or the avatar's own team reads
one game light on the days you played. If it doesn't, that's a one-line parity fix in the flush, not
a standings redesign.)

---

## 3. The Placement Decision

The dashboard's lower region is a single `CardsRow` HBox today (Scouting 3 / Dev 2 / Schedule 2,
per 12b). Four-plus new surfaces will not fit one row. **Two structural moves** carry all of 12c:

### Move 1 — the recap owns the center slot when idle

12b already made `AtBatView` a swap-in occupant of the dashboard's prime central region (visible
during a game, hidden otherwise). **Give that same slot a second occupant: a `RecapCard` that shows
the last game's result when idle.** During a game → `AtBatView`; between games → the last game's
final line + your box line. This is the most salient answer to "what just happened when I advanced
the day," it reuses the 12b visibility swap verbatim, and it costs **zero new rows and zero new
queries** (the cached `MicroGameResult`). The recap persists across off-days (it caches the *last*
result; an off-day does not blank it).

### Move 2 — split the card region into a "me" row and a "league" row

Rename the busy `CardsRow` into two purpose-named rows:

```
BaseballDashboard (PanelContainer)
└── Layout (VBox)
    ├── HeaderBand            ← unchanged (portrait, day, status one-liner, Play/Skip)
    ├── AvailabilityCard      ← unchanged (absence/rusty warning)
    ├── CenterSlot            ← 12b swap region, now dual-occupant
    │   ├── AtBatView         ← visible during a game (unchanged)
    │   └── RecapCard         ← NEW — visible when idle (last game's final + your line)
    ├── MeRow (HBox)          ← was CardsRow — "how's my guy"
    │   ├── ScoutingCard (3)  ← unchanged
    │   ├── DevCard (2)       ← unchanged
    │   └── StatLineCard (2)  ← NEW — avatar's current-season line
    └── LeagueRow (HBox)      ← NEW — "the world my guy plays in"
        ├── StandingsCard     ← NEW — avatar's tier, ranked by W-L
        ├── LeadersCard       ← NEW — top-N this season (tier-scoped)
        └── ScheduleScreen    ← MOVED here from CardsRow (it is league/calendar context)
```

**Why this grouping.** The stat-line is the cumulative-season companion to the scouting card (both
answer "how is *my* guy doing"), so it belongs in `MeRow`. Standings + leaders + schedule are all
league context ("the world"), so they form `LeagueRow`. Moving `ScheduleScreen` down is a pure
reparent within the same scene — its 12b `Card`-variation PanelContainer root and its hustle-bridge
path (`…/BaseballDashboard/Layout/CardsRow/ScheduleScreen` in `Main.cs`) update to the new
`…/Layout/LeagueRow/ScheduleScreen` node path; nothing about the scene's internals changes.

**The one density fork, decided with a fallback.** Two stacked card rows plus the center slot is
denser than 12b's single row. That density is *correct* for the genre (OOTP/MLB-dashboards show
everything at once; hiding standings behind a tab is a step backward for an at-a-glance dashboard),
so the recommendation is **two rows**. The only thing that can overturn it is the *real rendered
height* overflowing the left panel at the shipping window size — a build-time observation, not a
design unknown. **Fallback if it overflows:** collapse `LeagueRow` into a single tabbed card
(`Standings | Leaders | Schedule` as a `TabContainer`), which trades at-a-glance for height. Sonnet
makes that call against the live render in 12c-3; the architecture above is identical either way
(same cards, same seams — only whether they sit side-by-side or behind tabs).

---

## 4. Per-Card Spec

Each card is a `PanelContainer` with the `Card` theme variation, refreshed on the
`RefreshScoutingCard` cadence (day-advance), player-facing templates on `[Export]` strings (the
`OutcomeNamesCsv`/`OfpFormat` precedent — no new C# string literals):

- **RecapCard** — "Final: AWAY n – HOME n (k inn) · You: 2-for-4, HR". Renders the cached
  `MicroGameResult`; hidden until the first game of the save resolves. First cut = attended games
  only; a follow-on reads the avatar's most-recent `Game_Logs` box row so *skipped* days also recap
  (that's the only part needing a `Game_Logs` read, and it's optional in-slice).
- **StatLineCard** — the avatar's current-season slash line + counting stats, role-aware (batting
  line for hitters, `IP/W-L/ERA/WHIP` for pitchers — the `isPitcher` split the scouting card already
  branches on). Source: the new single-row query (§5).
- **StandingsCard** — the avatar's tier, teams ranked by win pct, `W-L · GB`, the avatar's own team
  highlighted (the "diamond" accent). Source: the standings aggregation (§5) merged with `Teams`
  names (`LoadTeamsByTier` already loads city/name/abbr/tier).
- **LeadersCard** — top-N (≈5) for a small set of categories (HR, AVG, OPS for hitters; ERA, W, SO
  for pitchers), tier-scoped, avatar highlighted if present. Source: the top-N query (§5). Rate
  categories (AVG/OPS/ERA) carry a **min-PA / min-IP qualifier** so a 1-for-1 cameo can't "lead the
  league" — a batting-title floor, flagged as a real correctness detail, not polish.

---

## 5. The New Query Surface (precise shapes for Sonnet — all No-Blind-Queries checked before use)

All three are new methods on `BaseballQueries` (the only class that opens SQL, per
`database_rules.md`), acquired-once/prepared/reused like every sibling. **Validate each plan via the
SQLite MCP before writing the C#** — the standing rule, non-optional for the joins.

1. **Standings (tier-scoped team records):**
   ```sql
   SELECT p.team_id, COALESCE(SUM(ps.w),0), COALESCE(SUM(ps.l),0)
   FROM Players AS p
   JOIN Team_Tiers AS tt ON tt.team_id = p.team_id
   LEFT JOIN Pitching_Stats AS ps ON ps.player_id = p.player_id AND ps.season_year = @seasonYear
   WHERE tt.tier = @tier
   GROUP BY p.team_id;
   ```
   `GB = ((leaderW − teamW) + (teamL − leaderL)) / 2`, computed in C# after sorting by win pct.

2. **Avatar season stat-line (single row, both tables):**
   ```sql
   SELECT pa, ab, h, doubles, triples, hr, bb, so, rbi, sb, avg, obp, slg, ops
   FROM Batting_Stats WHERE player_id = @playerId AND season_year = @seasonYear;
   ```
   (+ the `Pitching_Stats` counterpart.) Rides the `UNIQUE(player_id, season_year)` index — a PK-shaped
   probe, no plan surprise. Returns "no line yet" cleanly when the avatar hasn't played this season.

3. **League leaders (tier-scoped top-N, one category):**
   ```sql
   SELECT bs.player_id, bs.hr
   FROM Batting_Stats AS bs
   JOIN Players AS p ON p.player_id = bs.player_id
   JOIN Team_Tiers AS tt ON tt.team_id = p.team_id
   WHERE bs.season_year = @seasonYear AND tt.tier = @tier
   ORDER BY bs.hr DESC LIMIT @n;
   ```
   Rate categories add `AND bs.pa >= @minPa`. Names resolve via `gm.Players.TryGetById` over the ≤N
   rows (cheap, off the hot path) or by widening the join to `first_name`/`last_name`.

Standings + leaders should be exposed as **tier-scoped** — the sim runs six tiers; the dashboard
shows the avatar's league (the `TryGetTeamTier(avatar.TeamId)` the scouting card already resolves).

---

## 6. Sequenced Sub-Slices (thin, one demoable step each)

| Slice | Deliverable | Owner |
| :---- | :---------- | :---- |
| **12c-1** | `RecapCard` in the center slot (cached `MicroGameResult`); the `MeRow`/`LeagueRow` split + `ScheduleScreen` reparent (scene structure + `Main.cs` path update). Zero new query — the layout foundation. | **Sonnet** (build) + **Fable** (path/gate review) |
| **12c-2** | `StatLineCard` (query #2, role-aware). | **Sonnet** |
| **12c-3** | `StandingsCard` (query #1) + `LeadersCard` (query #3), the `LeagueRow`; **the rows-vs-tabs density call against the live render**. | **Sonnet** (build, over these seams) + **Fable** (No-Blind-Queries plan check + review) |

12c-1 is load-bearing (it establishes the two-row shape everything else drops into) and lands first.
12c-2 and 12c-3 can run in either order once 12c-1 is in.

---

## 7. Verification (the standing UI discipline, in full)

1. **Node paths verified before every `GetNode<T>()`** — `godot_scene_mapper` against the edited
   `BaseballDashboard.tscn` (and the `Main.cs` reparent path) *before* writing the script
   (review-blocking if skipped).
2. **Live headless boot** on the real v10 save with the new cards in the tree across frames —
   **zero debug-output errors**, confirming every new `GetNode` resolves (the AdvanceDay-is-
   UI-button-only stand-in; no headless input injection exists).
3. **Each new query's plan validated via the SQLite MCP** before its C# is written (No Blind
   Queries — the two joins in standings/leaders are the ones that matter).
4. **No schema change — asserted:** `SchemaValidator` green unchanged, `user_version` stays 10.
5. **No sim touch — asserted:** `git diff --stat -- Assets/Simulation` empty ⇒ `run_monte_carlo_batch`
   inert by construction (the 10/11/12b precedent).
6. **Manual playtest (human sign-off):** advance an avatar through part of a season, confirm the
   recap updates on each played game, the stat-line accrues, standings reorder as teams win/lose, and
   the avatar's team/name highlights in standings + leaders. The same manual-playtest posture every
   prior UI entry carries.

---

## 8. Disclosed Simplifications & Open Questions

- **Standings-by-pitcher-decision** is exact for macro games (only starters get decisions); the one
  verify point is that the *attended* (`MicroGame`) path credits a decision the same way — else the
  avatar's own team reads a game light on days you played (a one-line flush parity fix, not a
  standings redesign).
- **RecapCard first cut = attended games only** (cached `MicroGameResult`, zero query); the
  skipped-day recap via a `Game_Logs` box read is an in-slice stretch, not required for 12c-1.
- **Rows-vs-tabs for `LeagueRow`** is settled at build time against the real rendered height, not in
  this doc — two rows recommended, tabbed `LeagueRow` the height fallback. Same cards/seams either way.
- **Leaders rate categories need a min-PA/min-IP floor** or a cameo leads the league — a real
  correctness detail called out for the implementer.
- **12d (at-bat presentation) is the sibling step**, with its own pending Opus design pass — not
  designed here.

---

## 9. Cross-References

- `docs/design/presentation_layer_narrative.md` (Phase 10 — the two-panel shell, the scouting card,
  the dirty-flag/read-only/no-new-C#-literal conventions this extends).
- `.claude/rules/ui_conventions.md` (read-only-over-sim, one-scene-per-surface, pooled elements, no
  per-frame LINQ/string-format, thin vertical slices).
- `.claude/rules/database_rules.md` (all SQL through `DatabaseManager`/typed query classes,
  parameterized, No Blind Queries, prepared/pooled — the §5 methods obey it).
- `Assets/Data/Database/BaseballQueries.cs` (the sibling query methods the §5 additions mirror;
  `LoadTeamsByTier`, `LoadSeasonBattingLines`, the tier-scoped aggregate precedent).
- `Assets/Simulation/Baseball/LeagueSimulator.cs:494-503` (the W/L decision write that makes
  standings recoverable without a schema change).
- `Assets/UI/BaseballDashboard.cs` (the 12b card row + `RefreshScoutingCard` dirty-flag cadence the
  new cards join).
