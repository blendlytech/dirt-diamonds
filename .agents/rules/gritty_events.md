# **Gritty Events & Hustle Mechanics**

Full contract: `docs/reference/gritty_event_framework.md`. Validate every batch with `check_event_graph_integrity`.

## **Event Dispatcher**

Runs on a background polling loop checking the Entity_Flags and Players tables.

Event JSON structure:

- **Prerequisites** (e.g. Funds < 500 AND Recklessness > 75). `tier` uses `LeagueTier` ordinals: **HS 0, College 1, Pro 2** from schema v14.
- **Probability weight** (chance of firing).
- **Branching choices.**
- **Delivery:** `contact` (the phone thread the event arrives on); **no `voice_note`** (voice is broadcasters only, 2026-10-10; check #9 rejects it); optional **`cutscene`** (a cutscene id, on the event or on a choice).

Choices must write immediate consequences (e.g. deduct funds) AND write hidden flags to Entity_Flags to trigger cascading events years later.

## Content lines (owner directives 2026-10-10)

- **Never abortion.** Every pregnancy reaches a birth: kept, or placed for adoption (planned check #11).
- **Adults only for rewards.** Temptation and substance **boosts** need age ≥ 18 **and** tier ≥ College. HS romance stays off-screen and is never rewarded.
- **Nothing sexual is explicit** at any age.
- **Secrecy.** No event text may hint at the father or a hidden child before its reveal.
- **Tokens.** Message text may use `{tokens}` from the `NarrativeTokenResolver` vocabulary; unknown tokens fail at load (planned check #12).
- Specs: `docs/design/temptation_and_lineage.md`, `docs/design/substances_and_addiction.md`.

## Hustle State Machines

Hustles operate as isolated interactive nodes:

- **Narcotics:** 3-tier system (Inventory Drop → Profit/Toxicity Cut → Territory Control vs Factions), with re-up and supplier haggle.
- **Fencing:** alternating-offer negotiation, multi-lot.
- **Robbery:** case → approach → execute → press-your-luck → getaway.
- **Texas Hold'em:** direct mathematical card simulation evaluating pot odds.
- **Legal Work:** a Work block that drains Energy and Fitness for modest pay.

Hustle engines write the documented engine flags (`narc_watchlist`, `bad_product`, `controls_turf_local`, `hot_goods`, `robbery_bust`, `spoiled_goods`, `gambling_bust`) that content may gate on.
