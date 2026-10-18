# Task Brief: CS-M — College 28-game Math plan (re-band, rest rule, National Ranking)

**Recommended model:** GPT 6 Astra Pro (Math) · **Reviewer:** Claude Opus 5.5 · **Owner gate:** none · **Spec:** `docs/design/league_restructure.md` as updated by CS-0 (§3, §3.1, §6, §8.2, §8.2a, §11.2, §16-I6…I8) · **Branch/worktree:** `p13/CS-M-gpt` in your own worktree (`git worktree add ..\dnd-gpt -b p13/CS-M-gpt origin/main`)
**Starts after CS-0 merges.** If the merged spec differs from this brief, the spec wins. *(Aligned with the CS-0 spec on 2026-10-18 by Claude Opus 5.5.)*

`GEMINI.md` §1–§11 are binding (GPT agents: via `AGENTS.md`). Session start: read `docs/README.md` and `docs/progress.md`, then `git fetch`, `git status`, `git log`.

---

## 1. Context
On 2026-10-18 the owner changed College from a **14-game Saturday season with a single-game final** to a **28-game Wednesday + Saturday season (14 weeks)**. The playoffs stay single elimination (18 teams: 17 by National Ranking + a play-in → conference tournaments), and the final becomes **"The Red Dirt Series"** (name pending the owner's trademark screen), **a best-of-3 on consecutive days, game 3 only if needed.** College still uses a 4-man rotation by rest days, but the gap between games falls from 7 days to 3–4. Every College magnitude tuned for the old cadence needs re-proving. Per the calibration rule, each one is a Math data value proven by a harness.

**What CS-0 settled (spec, not yours to change; take it as input):**

| Item | Settled in the spec |
| :-- | :-- |
| Calendar (§3) | College plays **Wednesday + Saturday**. The season opens on the Wednesday before the second Saturday of February and ends on the 14th Saturday: 28 game days. Practice Mon, Tue, Thu, Fri. **No tier plays on Sunday.** |
| Schedule shape (§3.1, §16-I6) | Every old 14-game pairing played twice: division opponents × 4 (12 games, 2 home / 2 away each) + the 8 other conference teams × 2 (16 games, 1 home / 1 away). 14 home / 14 away. No cross-conference regular season. 12 pairings per game day (6 per conference); feasibility checked by construction. Interleaving order is the 13-3 scheduler's seeded choice. |
| Conference tournaments (§8.2) | Single elimination, one round per week **on Saturday** (unchanged). |
| The Red Dirt Series (§8.2, §16-I7) | Bo3 between the two conference champions, **Thursday–Friday–Saturday** of the week after the conference finals; game 3 only at 1–1. The better national rank hosts games 1 and 3. `Playoff_Series.best_of = 3`. |
| Series starters (§6) | **Hard rule, not a tuning value:** no starter starts two games of one series. The short-rest fallback skips anyone who already started in the series; if nobody else qualifies, a reliever opens. |
| Reveal safety (§8.2a) | `ChampionshipWonEvent` carries the tier; only a Pro title fires the reveal. Not a Math item; listed so you don't model it. |

**What is yours (each a Math value, written "Math value (CS-M)" in the spec):** `MinRestDays(College)` and the ace-heavy starter selection at 3/4-day gaps; the short-rest capacity penalty and overuse injury risk at the College cadence; the College band restatement for 28 games; the National Ranking weights.

**This is not a separate code slice.** It is the Math plan for the College parts of 13-2, 13-3 and 13-4, which you co-own (TEAM.md). The values are proven when those slices build the 28-game schedule.

## 2. Hard constraints
- **Allowed paths now:** `docs/design/math/college_28_game.md` (new), and `docs/progress.md` (one entry). Read-only everywhere else.
- **Later (inside 13-2 / 13-3 / 13-4, under their briefs):** the College data values and harness targets those briefs allow. Not in this slice.
- **The Pro guard and the resolver guard stay byte-exact.** College changes must not move the Pro baseline (`run_monte_carlo_batch`: 354/354 at the 2026-10-10 baseline, 20/20 runs byte-identical, guard PA 48384 / H 10969 / ER 5237, or the new Pro guard if 13-3 has sanctioned one; check `docs/progress.md`).
- Zero-allocation sim code and determinism rules apply to anything you later touch in `Assets/Simulation/`.
- Don't reopen the settled items in §1. If the Math shows one of them is a problem (for example, the Thu–Sat series forces bullpen games too often), report it as a finding for the owner; don't change the spec.

## 3. Inputs
- `league_restructure.md` after CS-0: §3 calendar, §3.1 schedule shape, §6 rest rule and the series rule, §8.2 tournament and series, §11.2 bands, §16-I6…I8.
- The current College per-tier bands, the 13-3 re-band notes, the 13-2 rest rule and starter selection, and the 13-4 National Ranking method (`RankScore = 0.50·Win% + 0.30·SOS + 0.20·RD_norm`, weights Math-owned).
- The fatigue model and the contracts from R1-MATH.

## 4. Outputs
`docs/design/math/college_28_game.md`, listing for each item: the current value or method, what changes, the proposed method, the harness and command that will prove it, and the pass criterion.
1. **College per-tier bands:** which bands are per-game rates (they should hold) and which are season totals (they double). Restate the College targets for 28 games.
2. **The rest rule and starter selection** for the Wed → Sat (3-day) and Sat → Wed (4-day) gaps: recovery, eligibility and the ace-heavy weighting. Propose `MinRestDays(College)`. Show how often the ace starts per week and that no starter pitches on short rest more than the rule allows.
3. **National Ranking stability:** with 28 games, does the ranking still seed 17 teams sensibly? Measure seed volatility across seeds and runs, and propose any weight change.
4. **The Red Dirt Series:** starter usage under the rest rule plus the hard no-repeat rule on Thu–Fri–Sat (who starts games 1, 2 and 3 for a typical 4-SP staff; how often a reliever must open game 3), and the expected share of series that go to 3 games (a sanity number, not a target).
5. **Play-time note:** attended College regular-season games double (14 → 28), plus up to 3 in the final. Report the season count for the owner; it isn't a calibration value.
6. **Harness plan:** which `MonteCarloHarness` suites change, any new College suite (28 games per team, 14/14 home/away, pairing counts per §3.1, one game per team per game day, no Sunday games, Bo3 legality, no repeat starter in a series), and the expected pass counts.

## 5. Tests and gates
| Gate | Command | Expected |
| :-- | :-- | :-- |
| Docs only (this slice) | `git diff --stat origin/main` | only the allowed paths |
| Pro untouched | none in this slice; restate the check for 13-3: `dotnet run --project Tools/MonteCarloHarness -c Release` | Pro guard unchanged, byte-identical runs |
| Plan complete | Opus review | every item in §4 has a method, a harness and a pass criterion |

## 6. Definition of done
- [ ] `college_28_game.md` covers §4 items 1–6.
- [ ] No number is presented as settled before a harness proves it; proposals are labelled.
- [ ] Opus has reviewed.

## 7. Out of scope
Building the schedule, editing `Assets/Simulation/` in this slice, Pro values, the settled items in §1, the record-goals Math (RG-M), and UI.

## 8. Handoff
```text
Done: CS-M College 28-game Math plan · Commit: <sha> · Gates: diff-stat → docs only
Open: <values that need the built 28-game schedule to prove; any ranking-weight proposal; any finding against a settled item>
Recommended next: 13-3 schedules + calendars + re-band by Claude Opus + GPT (Math) (reviewers: each other's part)
```
Then one compact line in `docs/progress.md`. Don't start the next slice.
