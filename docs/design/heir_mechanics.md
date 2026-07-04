# Design — Heir Mechanics: Genetic Blending, Hidden Interest, Succession & Lineage Failure

**Author:** Claude Opus 4.8 (architecture / mathematical design) · **Phase:** 6 (Legacy & Lineage — second bullet; exit criteria = a simulated 3-generation run with succession handoffs) · **Status:** design only — **no code written this pass.** This document is the spec Claude Sonnet 5 implements the blending math / heir generation / queries / harness checks against, and Claude Fable 5 implements the succession handoff + 3-generation harness suite against (model split in §9.4).

This document specifies how the player's bloodline continues across generations: how an heir's baseball attributes are **genetically blended** from their parents, how the hidden `Players.baseball_interest` stat is rolled and **revealed**, how the avatar **retires and hands off** to an heir, and when a lineage **fails and ends the game**.

It is a **spec, not code.** The two sims never reference each other (CLAUDE.md architectural boundary); lineage lives on the Baseball/career side (it produces `Player_Ratings` rows and re-points `CareerManager`) and touches the Life side only through the DB — the `Child` `Relationships` row and `Players.baseball_interest`, never `RelationshipGraph` directly (§9.3).

**No probability surface.** Nothing here modifies `AtBatResolver`, the §4 matchup weights, or any calibrated table — it only writes ordinary `Player_Ratings` / `Players` rows that flow through the *unchanged* resolver. That is why this is Sonnet's to build (arithmetic on rows), not Fable's calibrated-core burden — with one exception, the succession *handoff* itself (§5.4, §9.4), which re-enters the Phase 5 career-wiring path where roster mistakes silently drift stat composition. The genetic model's own calibration safety (that a dynasty **cannot** ratchet the league toward 100) is proven analytically in §3 and is a standing `run_monte_carlo_batch` acceptance item.

Every constant below is a **calibration knob**, not a law. They live in a `static readonly` table (`HeirGeneticsProfile`), tuned as data — the same precedent as `NeedDecayProfile` and the `AtBatResolver` matrices. Never hard-code a magic number in the blending code.

---

## 1. The lineage model

### 1.1 Entities

- **The bloodline** is a single chain of avatars: `founder → heir → heir → …`. Exactly one member is the **active avatar** at any time (the existing `Game_State.avatar_player_id`). The chain advances only through **succession** (§5).
- **An heir** is an ordinary `Players` row — created young, unrostered (`team_id = NULL`), with a `Player_Ratings` row (§2) and a hidden `baseball_interest` (§4), plus a `Child` `Relationships` row linking it to each parent. It is *not yet* the avatar. Heirs are created by `ConceiveChild` (§9.2), the same way `CreateAvatar` inserts the founder — one Players + one Player_Ratings row — plus the parent link and (if a pitcher) a role + stuff-derived arsenal, exactly mirroring `CareerManager.CreateAvatar`.
- **Parents.** Parent A is always the current avatar (the bloodline spine). Parent B is the avatar's **`Partner`** relationship counterpart when one exists (Phase 7 dating/gritty events author it), else the **degenerate league-average parent** (§2.3) — so heirs can be produced today, before any partner system ships.
- **The retired avatar** persists forever as a `Players` row (the DB is the single source of truth for legacy — never deleted). On retirement it leaves its roster (`team_id → NULL`); its career `Batting_Stats`/`Pitching_Stats` rows stay as history. The heir starts a fresh stat ledger under its own `player_id` — no migration, only new rows.

### 1.2 Recovering parent→child direction (load-bearing invariant)

`Relationships` is an unordered, canonically-ordered pair with a `type_enum`; it carries **no direction**. For a `Child` edge we must know which endpoint is the parent. The invariant that resolves it, maintained by construction:

> **A `Child` edge's older endpoint is the parent; the younger is the child.** An heir is always born *during* a parent's career, so `heir.age < parent.age` holds for every `Child` edge, permanently.

So "the avatar's children" = walk the avatar's `Child` edges (`PlayerQueries.LoadRelationshipsFor`), keep each endpoint whose `age < avatar.age`. No schema column, no new query shape. This invariant is a harness assertion (§8).

### 1.3 Lineage state (Game_State, additive keys — no schema change)

New `GameStateKeys` string constants, stored through the existing `GameStateQueries` KV path (same as `avatar_player_id`):

| Key | Type | Meaning |
|-----|------|---------|
| `dynasty_generation` | INTEGER | 1 at founder creation; `+1` on every successful handoff. The 3-generation exit criterion asserts this reaches 3. |
| `dynasty_founder_id` | TEXT | The gen-1 avatar's `player_id` (legacy/records display). |
| `lineage_over_reason` | TEXT | Absent while in play; set to a `LineageFailure` reason string (§6) when the game ends. Its presence **is** the game-over flag. |

---

## 2. Genetic stat blending

An heir's seven `Player_Ratings` (`bat_power/contact/discipline`, `pit_stuff/control/stamina`, `fielding`) are each blended independently by the **same** three-step law. All seven are blended and stored regardless of the heir's position, so latent genes survive for *their* future children (a position player still carries pitching genes forward).

### 2.1 The blending law (per rating `r`)

```
midparent_r =  (parentA_r + parentB_r) / 2                       # average of the two parents
blended_r   =  Mean + Heritability · (midparent_r − Mean)        # regression toward the league mean
child_r     =  clamp( round( blended_r + Spread · bell ),  0, 100 )   # the genetic lottery
```

- **`Mean = 50`** — the league-average rating anchor the whole ratings system already uses (50 = average on every 0–100 scale). Regression pulls offspring toward it.
- **`Heritability ∈ (0,1)`** — narrow-sense heritability. An offspring's *expected* deviation from the mean is `Heritability ×` the midparent's deviation. Default **0.5**: a couple 30 points above average produces a child expected 15 above average. This is the single most important knob (§3 shows its dynasty-drift consequence).
- **`bell`** — a zero-centred triangular draw, the **same shape** `LeagueGenerator.RollRating` already uses: `bell = (u1 + u2 + u3 − 1.5) / 1.5` for three independent `rng.NextDouble()` in `[0,1)`, giving `bell ∈ (−1, 1)` concentrated near 0. **`Spread = 12`** scales it — the genetic lottery, meaningful but subordinate to heritability.
- **Rounding** is `Math.Round(x, MidpointRounding.AwayFromZero)` then clamp to the schema's `[0,100]`. (Pin the midpoint mode explicitly — the harness fixtures in §7 depend on it.)

The lottery is **mean-zero**, so it adds variance (the "genetic lottery" drama — a lucky roll off average parents, an unlucky roll off elite ones) without biasing the steady state. `Spread` and `Heritability` are the two levers; §3's drift math is written in terms of both.

### 2.2 Position & role inheritance

- **`is_pitcher`** is inherited from **parent A (the avatar)** — a pitching bloodline stays pitching, a hitting bloodline stays hitting, for narrative continuity. (The succession UI may later offer a per-heir override; not required for the harness.)
- **Pitcher role** (starter/reliever), when the heir `is_pitcher`, is inherited from parent A if parent A is a pitcher of a role, else defaults `Starter`. Arsenal is generated stuff-derived via `LeagueGenerator.GenerateArsenal` exactly as `CreateAvatar` does for a pitcher avatar.
- All seven ratings are blended in every case; `is_pitcher` only decides which cluster the sim *uses*.

### 2.3 The degenerate league-average parent (no partner)

When the avatar has no `Partner`, parent B is a synthetic all-`Mean` vector (every rating = 50) and does not exist as a row. Substituting into §2.1:

```
midparent_r = (parentA_r + 50) / 2
blended_r   = 50 + Heritability · (parentA_r − 50) / 2
```

i.e. a lone parent's genes regress **twice as hard** (the absent mate is dead average). This is deliberate: **marrying well is the dynasty strategy** (§3) — cultivate a strong `Partner` (Phase 7) or watch the line fade to mediocrity. A Partner who is a non-baseball NPC (no `Player_Ratings` row) is treated as the same 50-vector; a Partner who *is* a ballplayer contributes their real ratings.

---

## 3. Dynasty drift & calibration safety

The reason regression-to-the-mean is non-negotiable: without it, a bloodline of avatars each blending upward would ratchet ratings toward 100 over generations and **break the league's §8 calibration**. With it, lines mathematically converge to the mean absent deliberate marry-well play.

Let `d_n = avatarRating_n − 50` be a bloodline's deviation in some rating (lottery averaged out, `bell → 0`).

**Lone-parent line** (mate always 50), `Heritability = 0.5`:
```
d_{n+1} = 0.5 · ( (avatar_n + 50)/2 − 50 ) = 0.25 · d_n
```

| Gen | Founder +40 (rating 90) | Founder +30 (rating 80) |
|----:|:-----------------------:|:-----------------------:|
| 1 | 90 | 80 |
| 2 | 60 | 57.5 |
| 3 | 52.5 | 51.9 |
| 4 | 50.6 | 50.5 |

The line fades to league-average within two generations. A single near-mean heir among 136 rostered players cannot move a league band — the same argument the rivalry doc used for a single avatar, now proven to *stay* true across generations.

**Marry-well line** (mate always deviation `+m`): steady state `d* = 0.25·(d* + m)` ⇒ `d* = m/3`. A partner rated 80 (`m=30`) sustains a `d*=10 → rating 60` line indefinitely; a partner rated 90 sustains `rating ≈ 63`. Elite dynasties are *possible* but require sustained investment in elite partners — never runaway. Both traces are §8 harness fixtures.

**Calibration-safety summary:** heir generation writes only `Player_Ratings`/`Players` rows; it never touches `AtBatResolver` or any weight table, and regression bounds every bloodline near 50. Re-running `run_monte_carlo_batch` after a 3-generation run must show **no §8 band moved** (standing rule).

---

## 4. Hidden `baseball_interest` & the reveal model

Not every child wants the game. `Players.baseball_interest` (0–100, already in schema, already hidden — no UI surfaces it) gates whether an heir can succeed.

### 4.1 Rolling interest at conception

Interest in this model is **nurture + luck, not nature** — a superstar's genes don't predispose a child to *want* baseball; the household environment and the parent-child bond do. So interest does **not** blend parent interest values (which would be dragged wrong anyway — NPCs carry interest 0). Instead:

```
interest = clamp( round( InterestBaseline + affinityAdjust + InterestSpread · bell ),  0, 100 )
affinityAdjust = InterestAffinityMax · (childEdgeAffinity / 100)
```

- **`InterestBaseline = 55`** — "raised in a baseball household" starts the child leaning positive.
- **`affinityAdjust`** couples interest to the **parent-child `Child`-edge affinity**: a warm, present parent nudges interest up; an absent or toxic one (Phase 7 gritty flags / low affinity) nudges it down. `InterestAffinityMax = 15`; the `Child` edge's **birth affinity default is `+30`** (`BirthAffinity`), so a freshly-born heir gets `+4.5`. Phase 7 events that move that affinity later re-weight a not-yet-revealed heir's interest at reveal time (read the affinity live at §5.2, not at birth).
- **`InterestSpread = 30`** on the same triangular `bell` — a wide draw spanning "loves it" to "wants nothing to do with it." This is the failure engine: with baseline ~59 and spread 30, a meaningful minority of single-child lineages roll below the play threshold.

### 4.2 The reveal

Interest is **stored at conception but never surfaced** while the heir is a child — "hidden" is a UX/timing rule, not schema masking (the value lives plainly in `Players.baseball_interest`, exactly as `detection_risk` is stored-but-hidden). It is **revealed at the succession decision** (§5.2), when the game presents each candidate heir's interest for the first time.

Reveal gate (binary, on the live value at reveal time):

- **Willing** — `interest ≥ InterestPlayThreshold` (**40**): a viable successor.
- **Unwilling** — below threshold: this heir "walks away from baseball" and is **not** a valid successor. A bloodline whose only heirs are all unwilling **fails** (§6).

(A graduated three-band reveal — *Willing / Reluctant / Devoted* with a mid-band rating malus — is a natural extension; the binary gate is what the exit criteria need and what §7/§8 fix.)

---

## 5. Succession on retirement

### 5.1 Retirement triggers

The avatar's playing career ends on the first of:

- **Age** — `avatar.age ≥ MandatoryRetirementAge` (**42**). The deterministic trigger the harness drives. (Voluntary earlier retirement is a UI action; same handoff path.)
- **Health** — `avatar.health_ceiling ≤ HealthRetirementFloor` (**40**). This is the **PED coupling**: PED use erodes `health_ceiling` (existing mechanic), so abuse forces an *earlier* retirement and earlier succession pressure. Emergent, no new machinery.

Retirement is evaluated once per year, on `SeasonRolledOverEvent` (the existing hook `LeagueSimulator` already subscribes to). **This requires a yearly aging tick that does not exist yet** — see §5.5.

### 5.2 Eligibility & selection

On a retirement trigger, gather the avatar's children (§1.2). Each child is **eligible** iff:

1. **Willing** — `baseball_interest ≥ InterestPlayThreshold` (reveal, §4.2), read live; and
2. **Of age** — `age ≥ MaturityAge` (**19**, reuse `CareerManager.StartingAge`).

Then:

- **≥ 1 eligible heir** → the player chooses one (UI); headless/autopilot picks the eligible heir with the **highest summed role ratings** (deterministic, ties broken on `player_id` — same ordering rule `FindDisplacedPlayer` already uses). → **succeed** (§5.3).
- **0 eligible heirs** → **game over** (§6), with the reason distinguishing *no children*, *none willing*, or *none of age*.

### 5.3 The handoff

Succession is a **`CreateAvatar`-shaped operation on an already-existing Players row** (the heir already exists; we don't insert it, we *promote* it). It must preserve every invariant `CreateAvatar` preserves:

1. **Retire the outgoing avatar:** `team_id → NULL` (leaves the roster; stats persist).
2. **Roster the heir**, preserving the per-team **9 + 5 + 3 = 17** invariant (§5.4).
3. **Re-point the bloodline:** `Game_State.avatar_player_id → heir.id`; `dynasty_generation += 1`.
4. **Re-initialize both sims** (`_league.Initialize()`, `_micro.Initialize()`) and re-activate (`ActivateAvatar(heir.id, heir.teamId)`) — identical to the tail of `CreateAvatar`, so `CareerManager`'s `_avatarSlot`/`_avatarTeamIndex`/attended-team filter all re-resolve against the new roster.

Steps 1–3 are one batch transaction (like `CreateAvatar`); the sim re-inits follow the commit.

### 5.4 Roster preservation (the exact rule)

The retiree leaving opens a slot; the heir joining fills one. Two cases keep every team at 17:

- **Same team & same role** (the common dynasty case — son follows father's position on the family franchise): the heir **inherits the retiree's exact slot**. Retiree → FA, heir → same team, same role. Net roster change zero; no backfill needed.
- **Role mismatch or different team** (pitcher father, hitter son; or heir signs elsewhere): treat as two independent invariant-preserving ops — (a) the heir joins its team by benching the **weakest same-role player** to FA (exactly `FindDisplacedPlayer` / `CreateAvatar`), and (b) the retiree's vacated slot on the old team is **backfilled** by promoting the strongest same-role free agent, or, if none exists, generating a replacement-level filler via `LeagueGenerator.GeneratePlayer` (the same path `EnsureV4` uses to invent relievers).

The reference rule for the harness: **default the heir to the retiring avatar's team and inherit the slot when roles match; otherwise displace-and-backfill.** This is Fable's to wire precisely (§9.4) — it re-enters the Phase 5 path where a miscount silently corrupts stat composition rather than failing loudly, so it carries the "prove it with the harness" burden.

### 5.5 Dependency: the yearly aging tick (does not exist yet)

`Players.age` is written only at creation; **nothing increments it.** Succession-by-age and heir maturity both need it. The succession work must add an engine-free yearly aging step on `SeasonRolledOverEvent` that increments every living player's `age` in one batch (Baseball-side; a set-based `UPDATE Players SET age = age + 1` is one statement — add it to `PlayerQueries`/`BaseballQueries`). This is a genuine new surface and belongs to the succession-handoff owner (Fable, §9.4), not the pure-math half. Until it lands, `ConceiveChild` and the blending math are fully testable by seeding ages directly (the harness sets ages), so Sonnet is unblocked.

---

## 6. Game-over on lineage failure

The bloodline ends — and the game with it — when a retirement trigger fires with no eligible heir. `lineage_over_reason` (§1.3) is set (its presence is the flag) to one of:

| Reason | Condition |
|--------|-----------|
| `NoHeirs` | The avatar has no `Child` edges at all — nothing to succeed to. |
| `NoWillingHeir` | Children exist, but every one's `baseball_interest < InterestPlayThreshold`. |
| `NoPlayableHeir` | Willing children exist but all are `age < MaturityAge` at the forced retirement. |

A `SuccessionOutcome` return type carries either `Succeeded(heirId)` or `GameOver(reason)`. (`NoPlayableHeir` could later soften into a "wait for the kid to grow up" mode; for now it is a terminal failure so the 3-generation run is deterministic and terminating.) Multiple children are the natural hedge against `NoWillingHeir` — an emergent incentive to raise more than one heir, needing no extra rules.

---

## 7. Worked examples (double as harness fixtures)

All with defaults `Mean=50, Heritability=0.5, Spread=12, InterestBaseline=55, InterestAffinityMax=15, InterestSpread=30, BirthAffinity=30, InterestPlayThreshold=40`. `bell` values are stated so each is a deterministic fixture (feed a fixed `RngState` or inject `bell` directly).

**F1 — two elite parents → good-not-great heir.** Father `bat_power 85`, mother `bat_power 75`. midparent 80 → blended `50 + 0.5·30 = 65`. Lottery `bell=+0.667 → +8` → **`bat_power 73`**. The superstar couple's kid is very good, not a clone.

**F2 — lone-parent regression.** Father `bat_power 85`, no partner (mate 50). midparent 67.5 → blended `50 + 0.5·17.5 = 58.75`. Lottery `bell=−0.42 → −5` → **`bat_power 54`**. Visible decline without a strong partner.

**F3 — genetic phenom from average stock.** Both parents `55`. midparent 55 → blended `52.5`. Lucky `bell=+0.9·1.? → +19` → **`72`**. The lottery can mint an outlier — the draft-day surprise.

**F4 — interest reveal fails → game over.** Single heir, `BirthAffinity=30 → affinityAdjust +4.5`, unlucky `InterestSpread·bell = −0.85·30 ≈ −25.5` → `interest = 55 + 4.5 − 25.5 = 34` → **below 40**. At the founder's age-42 retirement, the only child is unwilling → `GameOver(NoWillingHeir)`.

**F5 — dynasty drift (calibration proof).** Lone-parent line from a founder rated 90 in one rating, lottery suppressed (`Spread=0`): `90 → 60 → 52.5 → 50.6`, converging to 50 (§3 table). Marry-well variant (mate 80 each gen) holds steady at `rating 60`. Asserts no upward ratchet.

---

## 8. Acceptance suite & the 3-generation exit criteria

Extends `Tools/MonteCarloHarness` (the blending/interest/eligibility checks — Sonnet) and adds a lineage suite (the 3-generation run — Fable). All ± tolerances tight because the math is exact given a fixed `bell`.

**Blending & interest (Sonnet):**
1. §7 F1–F4 reproduce exactly under injected `bell` (all seven ratings for at least one full-vector case).
2. **Regression identity:** with `Spread=0`, `child_r == round(50 + Heritability·(midparent−50))` for a sweep of parent pairs (both two-parent and lone-parent forms).
3. **Bounds:** every generated rating and interest ∈ `[0,100]`; extreme parents (0/0 and 100/100) never clamp-overflow.
4. **Lottery is mean-zero:** over N draws, mean child rating ≈ `blended` within tolerance; no directional bias.
5. **Interest gate:** threshold classification (willing/unwilling) matches `interest ≥ 40` across the reveal band.
6. **Direction invariant (§1.2):** for a generated `Child` edge, the older endpoint resolves as parent for a sweep of ages.

**Lineage / succession (Fable):**
7. **3-generation run:** founder (gen 1) → conceive heir(s) → age to 42 → succeed (gen 2) → conceive → succeed (gen 3). Assert `dynasty_generation == 3`, two handoffs, `avatar_player_id` re-pointed each time, retiree `team_id` NULL with stats intact, heir on a valid roster.
8. **Roster invariant across handoffs:** every team stays exactly 9 + 5 + 3 after each succession (both same-role-slot-inherit and mismatch-backfill paths exercised); GS/W/L accounting identities still hold.
9. **Drift bound (§3/F5):** after 3 generations the bloodline's ratings sit near the mean (no upward ratchet); **re-run the full `run_monte_carlo_batch` season and confirm no §8 band moved.**
10. **Lineage failure:** a seeded single-unwilling-heir lineage yields `GameOver(NoWillingHeir)`; a childless retirement yields `NoHeirs`; both set `lineage_over_reason`.
11. **Reload fidelity:** save mid-lineage (heir conceived, not yet succeeded), reboot, and confirm children/generation/founder all rehydrate (Game_State keys + `Child` edges via `LoadRelationshipsFor`).

---

## 9. Implementation contract

### 9.1 `HeirGenetics.cs` (new, Baseball sim — pure static, engine-free)

Pure functions, no Godot types, no DB — the same profile as `AtBatResolver`. Deals only in `PlayerRatingsRow` (a `Data` type) and `int`, so it is compiled by the baseball assembly and is Data-clean (it must **not** reference `Simulation.Life`).

- `static PlayerRatingsRow BlendRatings(in PlayerRatingsRow parentA, in PlayerRatingsRow parentB, bool isPitcher, ref RngState rng)` — applies §2.1 to all seven ratings; sets `IsPitcher = isPitcher`; leaves `PlayerId` for the caller.
- `static PlayerRatingsRow AverageParent()` — the §2.3 all-`Mean` vector, for the lone-parent case.
- `static int RollInterest(int childEdgeAffinity, ref RngState rng)` — §4.1.
- Constants in a nested `static readonly HeirGeneticsProfile` (`Mean`, `Heritability`, `Spread`, `InterestBaseline`, `InterestAffinityMax`, `InterestSpread`, `BirthAffinity`, `InterestPlayThreshold`, `MaturityAge`, `MandatoryRetirementAge`, `HealthRetirementFloor`) — **constants, not literals**, tuning is a data edit. Expose the `bell` computation so the harness can inject a fixed draw.

### 9.2 `CareerManager.ConceiveChild(...)` (extend `CareerManager` — Sonnet)

Mirrors `CreateAvatar` minus the roster mutation (the heir is unrostered):

- Resolve parent A = current avatar's `PlayerRatingsRow`; parent B = the `Partner` counterpart's ratings if any, else `HeirGenetics.AverageParent()`.
- One batch: insert the heir `Players` row (`team_id = NULL`, `age = 0` or a caller-supplied birth age, `baseball_interest =` §4.1, `funds = 0`); insert `Player_Ratings` (§2); if pitcher, `UpsertPitcherRole` + `GenerateArsenal`; upsert the `Child` `Relationships` row for **each** parent via `PlayerQueries.UpsertRelationship(avatar, heir, BirthAffinity, RelationshipType.Child)`.
- Returns the heir `player_id`. No sim re-init (the heir is off-roster; the sims don't see it until succession).

### 9.3 Ownership & the sim boundary

- Lineage orchestration is **Baseball-side** (`CareerManager`) — it *is* avatar/career continuity, and it reaches the Life-relevant data (`Child` rows, `baseball_interest`) through the **Data layer** (`PlayerQueries`), never through `RelationshipGraph`. This respects "the two sims never reference each other": `CareerManager` already inserts `Players`/`Player_Ratings` via Data queries with no Life reference; a `Child` `Relationships` row is the same kind of Data write.
- **Eventual consistency with `RelationshipGraph`:** the Life sim's in-memory `RelationshipGraph` hydrates all `Relationships` rows at boot, so a `Child` edge written by `CareerManager` this session is picked up on next boot. Mid-session it is invisible to the graph — **acceptable because `Child` edges have no live Life-sim consumer** (they drive neither needs nor rivalry; only lineage reads them, and lineage reads the DB). This is the same "shipped before its live consumer" precedent as the PED flag and the rivalry writers. Document it; when a live consumer appears, publish a small `LineageChangedEvent` on the bus for the Life side to mirror (the clean upgrade — do not add it speculatively now).

### 9.4 Model split (the assignment)

- **Sonnet 5** — §9.1 `HeirGenetics` (pure math), §9.2 `ConceiveChild`, the `Child`-edge/interest **queries** (reuse `PlayerQueries.UpsertRelationship`/`LoadRelationshipsFor`; add the new `GameStateKeys`; add a set-based `age = age + 1` update stub if convenient but see below), and the §8 checks 1–6. All of this is arithmetic on rows + generation — no roster mutation, no sim re-init, no calibration surface. Fully unblocked (seed ages directly in fixtures).
- **Fable 5** — the §5.3–§5.5 **succession handoff** (`CareerManager.Succeed(...)` / `EvaluateSuccession(...)` → `SuccessionOutcome`), the roster-preservation + backfill wiring, the **yearly aging tick** (§5.5), the `dynasty_generation`/`lineage_over_reason` state transitions, and the §8 checks 7–11 (the 3-generation suite). This re-enters the Phase 5 career-wiring path where a roster miscount silently drifts stat composition, so it carries the `run_monte_carlo_batch` "no band moved" burden (check 9).

Seam: Sonnet builds everything that **produces and evaluates** heirs (pure, testable, no mutation); Fable builds the one dangerous **mutation** that swaps the avatar and everything it must keep invariant.

---

## 10. Schema & data notes

- **No schema change.** `Players.baseball_interest` and `age`, `Player_Ratings`, and the `Child` `Relationships` kind all already exist; lineage state rides additive `Game_State` keys through the existing KV path (§1.3). Re-validate the live save's schema via the `sqlite` MCP before any query work per No Blind Queries, but no DDL is expected.
- **Parent-child direction** is recovered by age, never stored (§1.2) — a maintained invariant, not a column.
- **`age = age + 1`** yearly update (§5.5) is the only new *query* the design needs beyond reuse; it is a set-based `UPDATE` with no join, so it needs only a routine plan check, not a fresh schema pass.
- Writes originating here are Baseball-sim writes and must not share a transaction with a Life-sim write (`database_rules.md`) — `ConceiveChild` and `Succeed` each open their own batch, as `CreateAvatar` already does.

---

## 11. Deferred / known gaps (deliberate)

1. **No partner system yet** — parent B defaults to the league-average vector (§2.3) until Phase 7 authors `Partner` relationships. The two-parent path is designed and testable now (seed a partner row), just not reachable in normal play.
2. **No procreation trigger in normal play** — `ConceiveChild` is a callable seam; *when* it fires in a real game is a Phase 7 life-event decision (marriage, time). The harness calls it directly (same "shipped before its writer" pattern).
3. **Interest is nurture-only** (§4.1) — no heritable interest term. A small one could be added if playtesting wants "baseball families breed baseball love"; left out to keep the failure engine legible.
4. **Binary interest gate** — the Reluctant/Devoted mid-bands (§4.2) are deferred; the exit criteria need only willing/unwilling.
5. **`NoPlayableHeir` is terminal** (§6) — a "wait for the heir to mature" grace mode is a future softening, not in the deterministic 3-generation spec.
6. **Aging is uniform** — the §5.5 tick ages everyone by 1/year with no decline curve on ratings; age-based rating decay (veterans fading) is its own future design, independent of lineage.
