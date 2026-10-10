# **Gritty Events & Hustle Mechanics**

Full contract: `docs/design/gritty_event_framework.md`. Validate every batch with `check_event_graph_integrity`.

## **Event Dispatcher**

Runs on a background polling loop checking the Entity_Flags and Players tables.

Event JSON structure:

- **Prerequisites** (e.g. Funds < 500 AND Recklessness > 75). `tier` uses `LeagueTier` ordinals: **HS 0, College 1, Pro 2** from schema v14.
- **Probability weight** (chance of firing).
- **Branching choices.**
- **Delivery:** `contact` (the phone thread the event arrives on); optional **`voice_note`** (a clip id from the voice manifest; the note's script is the message text verbatim); optional **`cutscene`** (a cutscene id, on the event or on a choice).

Choices must write immediate consequences (e.g. deduct funds) AND write hidden flags to Entity_Flags to trigger cascading events years later.

## Hustle State Machines

Hustles operate as isolated interactive nodes:

- **Narcotics:** 3-tier system (Inventory Drop → Profit/Toxicity Cut → Territory Control vs Factions), with re-up and supplier haggle.
- **Fencing:** alternating-offer negotiation, multi-lot.
- **Robbery:** case → approach → execute → press-your-luck → getaway.
- **Texas Hold'em:** direct mathematical card simulation evaluating pot odds.
- **Legal Work:** a Work block that drains Energy and Fitness for modest pay.

Hustle engines write the documented engine flags (`narc_watchlist`, `bad_product`, `controls_turf_local`, `hot_goods`, `robbery_bust`, `spoiled_goods`, `gambling_bust`) that content may gate on.
