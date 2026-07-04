---
name: check_event_graph_integrity
description: Validates the "Gritty Event Framework" to prevent broken narrative chains.
---
# check_event_graph_integrity

**Purpose:** Validates gritty-event content batches so a shipped batch can never orphan a flag, dead-end a branch, or hide an event nothing can trigger.

**What to validate (run after EVERY content batch, before it merges):**

The content contract is `docs/design/gritty_event_framework.md` §2–§4; the loader (`GrittyEventJson.Parse`) already rejects malformed JSON loudly (unknown ops/types/fields, weight outside [0,1], duplicate ids, choiceless events), so this skill's job is the *graph-level* checks the loader cannot see:

1. **Load the whole Content folder together** — `Assets/Narrative/Events/Content/*.json` parse as one `GrittyEventLibrary` (cross-batch duplicate ids fail here). The fastest full check is simply `dotnet run --project Tools/GrittyEventsHarness -c Release` — its first check parses the shipped batches.
2. **No orphaned prerequisites:** every `flag_active`/`flag_inactive` flag name referenced by any prerequisite must be writable by some `set_flag` consequence in the library (or be a documented engine-written flag, e.g. `ped_active`). A prerequisite flag nothing sets = an unreachable event.
3. **No dead-end intent:** every `set_flag` consequence should be read by at least one prerequisite somewhere (a flag nothing reads is inert — allowed for forward hooks, but list them in the batch review).
4. **No unpaced loops:** an event whose every choice leaves its own prerequisites satisfied AND has `cooldown_days: 0` will fire daily forever. Require a cooldown, a self-blocking flag, or a prerequisite the consequences break.
5. **Scope sanity:** `avatar`-scoped events whose prerequisites can never hold for a rostered adult (e.g. `age < 16`) are unreachable until heirs can be avatars — flag them.
6. **Weight sanity:** `scope: any` × high weight multiplies by ~136 subjects; anything above ~0.1/day on an `any` event needs a justification (the engine's MaxFiresPerDay=8 valve caps the blast radius but won't fix spammy content).

**Field/op/consequence vocabulary** (loader-enforced, listed here for authoring): fields `funds|age|recklessness|health_ceiling|detection_risk|baseball_interest`; ops `< <= > >= == !=`; consequences `funds|stress|interest|set_flag|clear_flag|relationship|conceive_child` (relationship: `kind` rival/friend/partner, `target` teammate/opponent/league). `min_days_since` on `flag_active` is the cascade clock.

**Marriage & conception notes** (`docs/design/marriage_and_conception.md`): `conceive_child` is payload-free and loader-rejected on any non-`avatar` scope. A `relationship kind=partner` consequence is silently skipped when the subject already has a Partner edge (single-partner exclusivity) — an arc that assumes the marriage landed should gate its follow-ups on a flag the same choice sets (the §5 `married`/`expecting` cascade), never on the edge itself. Conception pacing (how many kids) is content's job via flags/cooldowns; the engine allows unlimited requests.
