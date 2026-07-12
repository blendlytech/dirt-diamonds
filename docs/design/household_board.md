# Household Board — parents cover a wealth-tier share of food & living costs

*Spec + build: Fable 5, 2026-07-12 (user-directed, from the A2b playtest conversation: "Eating food should be taken care of by parents if the kid isn't poor, but if poor the kid needs to find a way to eat"). Single-session slice: spec, build, and gates together.*

## 1. Problem

The HS avatar pays adult survival costs out of his own pocket regardless of family wealth: $12 per autopilot meal (`ActionCatalog.Eat.FinancialCost`) and the $70/wk cost-of-living bill (`LifeSimManager.WeeklyCostOfLiving`, avatar-only since 8a). A Wealthy-tier 16-year-old buying his own dinners is thematically wrong, and it flattens the backstory ladder: Destitute's survival pressure is supposed to be the game's on-ramp into the Dirt, while a Middle-class kid's money pressure should be about *wants* (gear, dates, phone), not food.

## 2. Rule

While the avatar is a **high-schooler with a Family_Background row** (the exact same population `FamilyService`'s `parentalSupport` gate already serves allowance/gifts to), the household covers a wealth-tier share of **board** — defined as exactly the two survival charges above. One table, constants-as-data:

| §3 wealth tier | Kid's own share (`HouseholdBoard.BoardShareByTier`) | Meaning |
| --- | --- | --- |
| 0 Destitute | **1.00** | Family can't cover him — he pays his own keep in full. Survival pressure intact; this IS the dirt on-ramp. |
| 1 Working-class | **0.50** | Family covers half; the kid chips in. |
| 2 Middle | **0.00** | Fridge at home, bills paid. |
| 3 Comfortable | **0.00** | ditto |
| 4 Wealthy | **0.00** | ditto |

- The share multiplies the Eat action's `FinancialCost` (autopilot meals) AND the weekly cost-of-living debit. Nothing else — allowance, marketplace, hustles, LegalWork income all untouched.
- **Time is never covered.** Eating still takes a free hour; parents can pay for dinner, not eat it. (The tutorial's Free-hours teaching survives unchanged.)
- Coverage ends at graduation exactly like the rest of parental support: the signal rides `GameManager.SyncLifeSimAvatar` (same update path as `AvatarSchoolAvailable`), gated on `LeagueTier.HS` — deliberately NOT `HS or College` — matching `FamilyService.IsHighSchooler`'s HS-only support boundary.

## 3. Mechanism

- **New `Assets/Simulation/Life/HouseholdBoard.cs`** — static table + `ShareFor(int wealthTier)`; any tier outside 0–4 (notably the -1 sentinel) returns **1.0**.
- **`LifeSimManager.AvatarBoardWealthTier`** (int property, default **-1**): set by `GameManager.SyncLifeSimAvatar` from the persisted `FamilyBackgroundRow.WealthTier` when the avatar's tier is HS and a family row exists; -1 otherwise (pre-HS-2 saves, College/pro, succession adults, every harness world). Setter caches the share once — no per-hour table lookup.
- Eat's discounted cost is applied in BOTH places it must agree:
  - **decision** — `UtilityCalculator.SelectAction` gains an optional `eatCostShare = 1f` parameter scaling only the Eat row's `FinancialCost` before `FinancialCostScore`, so a broke-but-covered kid's autopilot correctly treats meals as free instead of flinching at a $12 he'd never pay;
  - **execution** — the avatar's `TickHour`/`ApplyAction` path debits `FinancialCost × share` for Eat only. Avatar detection is one cached `ReferenceEquals` against an `_avatarRuntime` field set in `SetAvatar` (zero-alloc, no string compare in the hot loop).
- Cost-of-living debit becomes `WeeklyCostOfLiving × share` (it was already avatar-only). Exposed as `LifeSimManager.AvatarWeeklyCostOfLiving` for the UI.
- **NPCs byte-identical:** non-avatar paths pass share 1.0; the bill never touched them. **Neutral identity:** tier -1 multiplies everything by 1.0, so every pre-slice save and every existing harness trace is byte-identical by construction.

## 4. UI

Bank tab FundsCard cost-of-living line becomes tier-aware (dirty-flagged on the effective bill):
share 0 → *"Cost of living: covered by your family."* · 0<share<1 → *"Cost of living: your share ${N}/wk — family covers the rest. Next bill in {d}d."* · share 1 → shipped string unchanged. Tutorial step 8 copy generalized ("how much of it your family covers depends on how well-off they are").

## 5. Gates

Build 0/0 · NeedsDecayHarness + new `RunHouseholdBoardChecks` (table pins incl. the -1 sentinel; covered avatar's meal debits $0 / Destitute debits full — same world, same week, funds differ by exactly the meal spend; cost-of-living scaling ×1/×0.5/×0; broke covered kid still selects Eat; bystander NPC byte-identical across tiers) · MC re-run, MLB guard byte-exact (Baseball untouched) · GrittyEvents (World runs at -1 ⇒ unchanged) · live boot.

## 6. Explicitly out of scope

NPC teens' households (background economy stays as calibrated); a "parents stop covering you" narrative lever (a future gritty event could set the tier or a flag — trivial once this exists); College-tier partial support; allowance/autobuy changes (already shipped in FamilyService).
