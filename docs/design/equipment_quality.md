# Equipment Quality (Phase 8e)

**Owner: Fable 5 (design + implementation + review).** Turns the interleave plan's one-liner —
"purchasable gear tiers as effective-ratings modifiers, same precedent as PED/fatigue/rivalry —
never touching `AtBatResolver`'s calibration tables directly, behind the `run_monte_carlo_batch`
band check" — into a full mechanic. This is the first system where Life-sim money buys on-field
performance: the funds → ratings bridge the economy has been accumulating toward since 8a.

## 1. Thesis & scope

Equipment quality is a **per-player scalar (0–3)** that shifts a player's *effective* ratings the
same way tier/rivalry/rust do — upstream of the unchanged `AtBatResolver`, never a calibration
edit. It is:

- **Modest.** Max boost +6 rating points (≈ +0.12 deviations on the resolver's 50-points-per-σ
  scale) on two knobs per role — a real edge over a 154-game season, nothing like PED's 1.5×.
- **Upgrade-only.** Quality never goes down and is never refunded; buying above your current tier
  is the only mutation. Gear doesn't wear out in v1 — *upkeep is already priced into 8a's weekly
  cost-of-living bundle, whose own line item reads "rent+food+gear".* The purchase is the capital
  expenditure; the $70/week is the maintenance.
- **Avatar-driven in v1, engine-generic forever.** Only the shop UI produces purchases, but every
  layer below it (table, event, ledger, sim caches) is keyed by `player_id` — NPC gear later is a
  content decision, not an engine change (the AvailabilityLedger discipline).
- **Per person, not per bloodline.** An heir starts at quality 0 — gear is sized to its owner, and
  re-buying it each generation keeps the sink alive across a legacy save.

## 2. The quality ladder

| Quality | Name (UI copy, lives in the scene file) | Boost | Price |
|---|---|---|---|
| 0 | Standard issue (no row — the default) | +0 | — |
| 1 | Quality gear | +2 | $750 |
| 2 | Premium gear | +4 | $2,500 |
| 3 | Custom pro gear | +6 | $7,500 |

- **What the boost touches:** batter **Power + Contact** (never Discipline — plate judgment is not
  equipment), pitcher **Stuff + Control** (never Stamina — endurance is the fatigue model's).
- **What it deliberately does not touch:** fielding / team defense. Team defense is a mean baked at
  Initialize; moving it per-purchase would force a rebake mid-season. Same disclosure 8c made for
  replacement call-ups. A v2 with glove slots can revisit.
- **Quality 0 is all-zero BY CONTRACT** (the MLB-tier-vector precedent): applying boost 0 is the
  identity, so a no-gear world's PA path is bit-identical to pre-8e — harness-pinned.
- **Pricing binds against the real 8a/8b economy:** Legal Work grosses ≈ $200/day against a $70/week
  cost of living; Narcotics EV is +$94–$1,444/session by policy; poker is skill-scaled. Quality 1 is
  about a week of clean work; Quality 3 is sustained hustle income or most of a season of grinding.
  Full sticker on every rung, no trade-in credit — laddering 1→2→3 costs $10,750 vs. $7,500 for
  saving straight to 3, a real save-or-spend decision.
- All four constants (boost table, price table) are first-pass and tunable as data edits behind
  `run_monte_carlo_batch`, exactly like the tier/rivalry deltas.

## 3. Persistence — schema v9

New **`Player_Equipment`** table, the additive Pitcher_Roles/Life_Stress/Team_Tiers/Player_Absences
pattern (never an ALTER on Players). No backfill: nothing produced equipment before v9.

```sql
CREATE TABLE IF NOT EXISTS Player_Equipment (
    player_id     TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    quality       INTEGER NOT NULL CHECK (quality BETWEEN 1 AND 3),
    purchased_day INTEGER NOT NULL CHECK (purchased_day >= 0)
) STRICT;
```

- **No row = quality 0.** Quality 0 is never stored (the `AbsenceReason.None` rule).
- **Upgrade-only is enforced in SQL**, mirroring the absence upsert's keep-later trick:
  `ON CONFLICT (player_id) DO UPDATE ... WHERE excluded.quality > Player_Equipment.quality` — a
  same-or-lower write is a wholesale no-op, so the ledger's in-memory keep-higher merge can apply
  the identical rule without a read-back and mirror and row can never disagree.
- Hydration is a full scan by design — at most one row per ever-equipped player.
- DDL idempotence + all new query plans are validated on a scratch db (sqlite3 CLI /
  `validate_sqlite_schema` path) BEFORE any C# — No Blind Queries.

## 4. Transport — the RivalryLedger pattern exactly

1. `EquipmentService` (Layer 2) writes the row in its **own batch**, commits, and only then
   publishes a primitives-only **`PlayerEquipmentChangedEvent(playerId, quality)`** (CoreEvents
   stays Data-free — the NeedsDecayHarness wildcard-compiles it).
2. **`EquipmentLedger`** (`Assets/Simulation/Baseball/EquipmentLedger.cs`) consumes it: a
   `Dictionary<string, byte>` + `Version`, `Seed(rows)` boot hydration, `AttachTo/DetachFrom`,
   `QualityFor(playerId)`, `CopyAll(list)`. Merge rule = SQL's keep-higher, applied identically.
3. `GameManager` hydrates the ledger from `LoadAllEquipment()` at boot and hands it to all six tier
   sims + the micro-sim (`public EquipmentLedger Equipment` — the UI's read surface, like
   `Absences`).
4. Sims rebuild a flat per-slot `byte[] _slotGearBoost` cache gated on **Version only** — gear
   never expires by calendar, so no day gating (unlike absences).

## 5. Sim consumption — order of operations

Per-PA effective-ratings chain, now:

> baked array value (tier) → **gear boost UP** → rust dock DOWN → rivalry → resolver's own PED 1.5×

- Gear applies **first**, to the tier-baked value: you bring your gear to the park; rust erodes the
  whole package. (Order only matters at the 0/100 clamps; it is pinned by the harness so it can
  never silently flip.)
- **The replacement call-up carries no gear** — a shadowed slot's stand-in is a stranger, the same
  rule that stops rivalries crossing a shadowed slot.
- **Macro (`LeagueSimulator`):** gear joins the PA fast-path conjunction
  (`rivalry == 0 && !shadowed && rust == 0 && gear == 0` → the exact pre-8e resolve call); pitcher
  gear lands inside `EffectivePitcher` beside rust.
- **Micro (`MicroGame`):** batter gear lands at the shared `ApplyRust` call site; pitcher gear
  lands in `EffectivePitcherBase` **before fatigue seeding**, so fatigue erodes geared ratings the
  same way it erodes rusty ones. The human's interactive pitch-chain at-bats flow through the same
  two sites, so the avatar's gear shifts the anchor p* with zero extra plumbing — macro/micro
  consistency by construction (the 9a argument).
- `EquipmentEffects` (new, `Assets/Simulation/Baseball/`) is the single data home: the boost table,
  `Batter(in ratings, boost)` / `Pitcher(in ratings, boost)` appliers (boost 0 = identity, one
  branch), reusing `TierEffects.Shift` for the clamp.

## 6. Purchase orchestration (Layer 2)

`Assets/Economy/Equipment/EquipmentService.cs` — the HustleService application discipline:

- `GetShopState(playerId)` → snapshot struct (funds, current quality) off one Players read + the
  ledger; the UI renders DTOs and emits intent only (ui_conventions).
- `TryPurchase(playerId, quality, day, out EquipmentPurchaseFailure reason)`:
  1. Validate: quality in 1–3, strictly greater than current, funds ≥ price. Failure = clean no-op
     with a typed reason (the UI disables buttons on the same predicate, but a stale click must
     never corrupt state).
  2. One batch: `AdjustFunds(playerId, -price)` (the atomic floor-clamped writer — never
     `UpdateFunds`) + `SetEquipment(playerId, quality, day)`. Commit; rollback rethrows without
     publishing.
  3. Post-commit only: publish `FundsImpulseEvent(playerId, -price)` (the Life sim's in-memory
     funds mirror) + `PlayerEquipmentChangedEvent(playerId, quality)`.
- Prices live on the service (`PriceForQuality`) — economy constants stay in the Economy layer;
  rating deltas stay in the Baseball layer.

## 7. UI (Layer 3)

`Assets/UI/EquipmentShopScreen.tscn` + `.cs`, the established always-present-overlay idiom (exact
anchor/visibility call made against the live tree via `godot_scene_mapper` before any
`GetNode<T>()` is written). Minimal demoable slice: funds label, current-gear label, three purchase
buttons whose `Disabled` states mirror `TryPurchase`'s own predicate (can't afford / not an
upgrade), dirty-flag gated label updates, all player-facing copy in the scene file.

## 8. Acceptance surface

**MonteCarloHarness — new "Phase 8e equipment quality" suite** (the sim assembly is touched, so the
full band check re-runs regardless):

1. **Neutrality guard:** a season with an attached-but-empty `EquipmentLedger` is **bit-identical**
   to a no-ledger season; the MLB bit-identity regression guard and the full 9a tier ladder stay
   exact/in-band.
2. **Arithmetic pin:** `EquipmentEffects.Batter/Pitcher` closed-form for every quality, including
   the 100-clamp and the boost-then-rust order (a geared+rusty+rivalrous PA input recomputed by
   hand).
3. **Directional league check:** an all-batters-geared (quality 3) world's league offense rises
   measurably vs. same-seed control; an all-pitchers-geared world's falls. Loose direction+magnitude
   bands, not exact figures — this is the "modifier does what it claims at scale" proof.
4. **Merge parity:** ledger no-ops a downgrade/same-quality event exactly as the SQL upsert no-ops
   the row write; SQL↔ledger round-trip agrees.
5. **Zero-alloc:** a warm geared game day stays ~0 B (the standing mandate).

**GrittyEventsHarness — Layer 2 integration** (it already compiles Data + Economy):

6. `TryPurchase` happy path: funds down by exactly the price, row exact, both events published
   post-commit; insufficient-funds / downgrade / same-quality rejections are true no-ops; the 1→3
   ladder works; the avatar-absent case doesn't block purchases (jail locks the *schedule*, not the
   wallet — an arrested player can still order gear for release day).

**SchemaValidator:** `Player_Equipment` joins RequiredTables; the three hardcoded `user_version`
checks bump 8→9 (the standing gotcha); live-save audit stays green.

**Migration proof:** the live save boots v8→v9 through two clean headless `--quit` boots (second
proves idempotence), players/teams/avatar intact.

## 9. Disclosed simplifications

- No durability/wear/theft — upkeep is abstracted into the 8a cost-of-living bundle (§1).
- No fielding/defense effect (§2) and no stamina effect.
- One bundled "gear quality" scalar — no per-slot bat/glove/cleats itemization in v1.
- NPCs never shop; the engine is generic but only the avatar's UI produces purchases, so league
  lines move only through the harness's synthetic worlds until content says otherwise.
- Prices are tier-flat: an HS kid and an MLB veteran pay the same $7,500 for pro gear (tier-relative
  pricing is a 9c-adjacent question once promotion moves players between economies).
- A dead/retired player's row persists harmlessly (CASCADE cleans it only on hard delete) — same as
  absences.
