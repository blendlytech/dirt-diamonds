# Task Brief: Career Opening Sequence — "The Illegitimate Heir"

**Target:** Phase 16 (Cutscenes) & Narrative Events
**Reviewer:** Claude Opus 5.5 (Integration Lead & Chief Architect)
**Revised 2026-10-10 by Claude Opus** with the owner's decisions. **The full design is [../design/temptation_and_lineage.md](../design/temptation_and_lineage.md)**; this brief is the opening-sequence slice of it. Where they differ, the design doc wins.

## 1. Narrative concept

At the start of every new career, a prologue cutscene establishes the player's hidden lineage. It's gritty and mature, in keeping with the game.

- **The secret lineage:** the player is the biological child of a retired legendary ballplayer, the GOAT.
- **The scandal:** the player came from an extramarital affair. To protect the legend's reputation, the mother was sent away and the baby was placed for adoption at birth.
- **The upbringing:** despite the father's wealth and status, the player grows up **poor**. The opening shows only that poverty. **The player has no idea they're adopted**, and nothing in the opening hints at the truth.
- **The starting state:** the career begins in **Freshman** year of high school, unchanged from today. The player is an unknown, not yet a prospect.
- **The player can be male or female.** A female player's career is the Trailblazer route (design §8). The opening is the same for both.

## 2. Technical requirements (design §3–§4)

### A. Historical legends

- **Fictional legends with full backstories**, as content: `Assets/Data/Legends/legends.json`. Each legend is also an unrostered `Players` row. Backstories must be **position-agnostic** (`{position}`, `{record_stat}` tokens), because positions are assigned per save.
- **Dynamic assignment at character creation** (inside the `CreateAvatar` batch, on a seeded `Split()`):
  1. The player picks **gender, ethnicity and listed position** (stored in the new `Player_Identity` table; the sim never reads it).
  2. The father is chosen from the legends matching the player's ethnicity. If none match, the owner-authored `ChooseFatherFallback` rule decides.
  3. The father is the GOAT **at the player's position**, and the other legends fill the remaining nine positions. The result goes in `Legend_Assignments`.
- **The secret:** a `Biological_Parentage` row (father → player, `child_knows = 0`), not a column on `Players` and **not** a `Child` edge. Flags: `is_adopted` set; `knows_true_lineage` absent.
- **The father's asterisk:** in **50%** of careers, rolled once at creation and hidden, the father's record was juiced (design §4.3).
- **No staple mentors.** The legends fill the record book; only the father ever texts.

### B. Record map (trackable stats only, design §4.3)

| Pos | Record | Pos | Record |
| :-- | :-- | :-- | :-- |
| SP | Strikeouts | 2B | Hits |
| RP | Saves | 3B | RBIs |
| C | Walks | SS | Doubles |
| 1B | Home runs | LF | Slugging % (minimum career PA) |
| CF | Triples | RF | Total bases |

The four originals the sim can't track (caught stealing, double plays, stolen bases, outfield assists) were replaced so the player can actually chase the record. Record values are scaled to the 64-game Pro season by Pro Math.

### C. Poverty start

- The backstory's existing single percentile roll maps onto wealth tiers **0–1** instead of 0–4. The roll order is unchanged, so careers stay deterministic.
- Re-roll stays within poverty.
- The adoptive parents are the household `CreateAvatar` already builds.
- Low funds then flow through the shipped `Funds` gates and `UtilityCalculator` with no new code.

### D. Cutscene integration (Phase 16)

- The prologue is a `story` cutscene: a definition in `Assets/Cutscenes/Definitions/`, played by `CutscenePlayer` when the avatar is created. It shows only the poverty: dramatic stills of the upbringing.
- `father_portrait` and `father_name` slots exist for the later reveal; the **opening doesn't use them**.
- **Blocked on** slice 16a (`CutscenePlayer`) and the Phase 15 art direction.

## 3. Voice: broadcasters only (owner directive, 2026-10-10)

ElevenLabs voice is for `BroadcastBooth` and the announcers **only**. There is **no** voice-over in cutscenes, **no** phone voice notes and **no** character dialogue. All of it is text: captions, bubbles, subtitles. This brief's cutscenes and texts are text-only.

## 4. The father's timeline (phone texts only)

### Phase 1: first contact, at the first Pro contract

- **Trigger:** the player's **first Pro contract**, drafted or undrafted (`ProContractSignedEvent`, new, published by the 13-6 pipeline).
- **Sender:** "Unknown Number" (`unknown_fan`, a thread separate from the syndicate's `unknown`). The display name switches to his real name at the reveal; stored messages are never rewritten.
- **Script draft:**
  > **Unknown:** "Saw you signed. Congrats, kid. Pros now. Real pitching. For what it's worth, I'm pulling for you. Still, let's be honest: you'll never touch {father_name}'s {father_record_value} {father_record_stat}. 😉"
- Tokens are resolved when the message is written (`NarrativeTokenResolver`).

### Phase 2: the relationship

From there he keeps texting: milestones, slumps, scandals, and a record-chase ladder at 50/75/90/100% of his mark, where the needling sharpens and then turns proud. On the Trailblazer route he's texting the first woman ever to play pro, and he knows exactly who she is.

### Phase 3: the reveal

The truth comes out **only** on whichever comes first:

- the player wins the **Pro World Championship**, or
- the player **retires**.

It fires before succession hands the game to the heir. A fatal overdose also triggers it, posthumously (substances doc §2.6).

## 5. Gates

- `validate_sqlite_schema` (v15 tables).
- `run_monte_carlo_batch`: `CreateAvatar` changes; no band may move.
- `check_event_graph_integrity`, including #9 (no voice notes).
- `CutsceneHarness` for the prologue definition.
- Gemini Pro reviews the sim-touching diffs.

## 6. Slices (design §14)

| Slice | Covers |
| :-- | :-- |
| L-1 | Schema v15, `CreateAvatar` identity/legends/poverty, the New Game gender/ethnicity/position UI |
| L-2 | `legends.json`, the identity catalog, the record map (Flash) |
| L-3 | Token resolver, `unknown_fan`, the father's events, `ProContractSignedEvent`, reveal ordering |
| 16a + 16e | Prologue cutscene (after `CutscenePlayer` and the art direction) |
