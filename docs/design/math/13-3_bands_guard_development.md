# 13-3 Math Record — Pooled Tier Bands, Pro Run-Floor Verdict (D5), College 28-Game Rate Analysis (M2), Development Retune (M4), and Avatar PA Bands

**Author:** Gemini 4 Argon (Math / calibration owner and independent reviewer, per [TEAM.md](../../TEAM.md) §1a 2026-11-19 succession).  
**Reviewer:** Claude Opus 5.5 ([TEAM.md](../../TEAM.md) §1a/§2).  
**Implementation head measured:** `7693b7d` (`p13/13-3-math-argon`, merging Opus's 13-3 build `5ec08b4` / docs `9d8dbf3` with `e404e43`).  
**Status (Claude Opus 5.5, 2026-11-20):** reviewed and accepted in [brief §10.7](../../tasks/13-3_schedules_calendars_reband_guard_brief.md#107-opus-review-of-argons-math-record-b9e0060-and-application-of-d5--m4--m5--claude-opus-55-2026-11-20). D5 settled by the owner as **Option (i)** (Pro R/9 4.1–4.8). M4 applied, with one added harness restatement (§10.7 R1: the §4 cap-asymptote probe moves to 20 e-folds). §3.3's reduced M2 holdout is approved and wired as `--college-analysis rates --reduced`. M3 is still pending. The body below is Argon's record as authored.  
**Update (Gemini 4 Argon, 2026-11-20, head `a2bf0c4`):** **M3 reproduced 20/20 byte-identical and signed** (`PA 81433 / H 18471 / ER 8803`, ledger SHA-256 `932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`, normalized log SHA-256 `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239`, D10 wall times `55.79–135.53 s ≤ 213.7 s`); **M2 reduced holdout executed once and passed 7/7** (`6.11 min`, 512 flat seasons `R/9 = 6.2355 [6.2217, 6.2494]` + 128 spread-25 diagnostic seasons `R/9 = 5.8668`); narrow M5 review of `a2bf0c4` (incl. R1 signature and R5 typo fix) recorded in §7 and [brief §10.8](../../tasks/13-3_schedules_calendars_reband_guard_brief.md#108-m3-pro-guard-reproduction-2020-m2-reduced-holdout-execution-d10-re-time-and-narrow-m5-review-of-a2bf0c4--gemini-4-argon-2026-11-20).

**Authority:** [13-3 brief](../../tasks/13-3_schedules_calendars_reband_guard_brief.md) §§2, 3.2 (D5, D6, D7, D10), 3.3 (M1–M5), 5, 8.1, 10.1–10.5; [CS-M brief](../../archive/CS-M_college_math.md) §§1, 6; [college_28_game.md](college_28_game.md) §§1, 6; [league_restructure.md](../league_restructure.md) §§11.2–11.3.

---

## 1. Executive Summary & Signed Deliverables

| Item | Deliverable | Result / Signed Value | Status |
| :-- | :-- | :-- | :-- |
| **M1** | Pooled 64 HS / 16 College / 8 Pro band measurement & SE check (`RunPooledTierBands`) | Reproduced byte-for-byte on `7693b7d`. All 18 slash/K/BB/HR point estimates and HS/College `R/9` (`6.931`, `6.237`) pass; all rate SEs (`≤ .0020`) and College/Pro `R/9` SEs (`.0312 ≤ .04`, `.0259 ≤ .03`) meet targets; HS `R/9` SE is `.0615` vs `.06` target (95% CI `[6.808, 7.054]` well inside `6.4–8.0`). Pooled Pro `R/9 = 4.164` (`[4.103, 4.225]`, `ER/GS = 4.191`, `outs/GS = 27.173`) is outside `4.2–4.8` (**FAIL**; interval crosses `4.2`, inconclusive calibration). | **Measured; D5 owner choice required** (§2) |
| **D5 Packet** | Owner choice on Pro run floor: (i) restate Pro `R/9` floor vs (ii) re-centre `BaseOutAdvancement` | Exact Markov chain `9 × RE(0,0) = 4.1439` and a pre-frozen 32-season holdout Pro pool (`32,768` games: `R/9 = 4.1519 [4.1311, 4.1727]`, `ER/GS = 4.1745 [4.1540, 4.1950]`, `outs/GS = 27.1468`) prove the unchanged model's true Pro `R/9` mean is `4.152 ± 0.021`. **Recommendation: Option (i)** — restate the Pro `R/9` band to **`4.1–4.8`** (preserving `BaseOutAdvancement`, `MicroGame`, `RE24`, and the Pro guard candidate). Option (ii) parameters are also quantified. | **Awaiting owner decision** (§2.3–§2.5) |
| **M2** | College 28-game rate re-band (`CS-M` §1/§6): confirm rows, `RunCollegeRateSmoke`, 32-season pilot, stop rule | `CS-M` §1 rows and `RunCollegeRateSmoke` (`2/2`) confirmed. 32-season training pilot (`worlds 1–8 × sched 1–2 × out 1–2`) completed in **`16.4 s` (`512 ms/season < 60 s`)** with all 7 pooled metrics in band (`R/9 = 6.2762 [6.1994, 6.3530]`, `ER/GS = 6.2450`). Full 16,384-season forecast is **`174.6 min` (`> 30 min` stop rule)** → full matrix not started; pre-frozen **512-season holdout reduced plan (`~7.1 min`)** recorded for Opus's review. | **Pilot green; reduced plan awaiting Opus review** (§3) |
| **M3** | New Pro guard 20/20 reproduction & sanction (D6) | Single-run candidate verified (`PA 81433 / H 18471 / ER 8803`, SHA-256 `932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`). Per brief sequence, 20× replay and M3 reproduction wait until D5 is settled by the owner and Opus re-runs the 20× replay. | **Deferred per sequence** |
| **M4** | Practice constants, `practice-seam` goldens, and spread-25 single-pass talent gradient (D7) | **Signed constants:** shared `PracticeFracPerHour = 0.001`, `PracticeFracCap = 0.25` (e-fold `250 h`; in-season fractions `.068463 / .090274 / .100176`). **Signed `practice-seam` goldens:** `§4` e-fold at `250 h` (`0.158030`), 1-hour Taylor tolerance `2.5e-6`, `§10.5` `2,190 h` (`frac 0.2500`) → `BatSum = 189` vs `idle 180` (`PlayersChanged = 1`, `PointsUp = 21`), `§4` succession guard → `30/30/30` (`0` growth, credit cleared); move `§10.9` below `§10.5`/`§4`. **Signed single-pass gradient:** `HS < College < Pro` on mean raw batter sum (0–300) with **0 negative tolerance** (default fixture pre `122.02 < 131.27 < 147.51`, post `132.70 < 135.81 < 147.23`; confirmed on 32 training + 32 holdout worlds). | **Signed for Opus data commit** (§4) |
| **Q7 / §5.4** | Career-wiring healthy everyday batter season PA bands | **Signed Pro 64-game band: `188–335` PA** (retains installed envelope; exact fixture = `294 PA`; 32-seed sweep across lineup slots/schedules: `mean = 278.66`, `SD = 17.26`, range `[258, 319]`). **Signed HS 10-game band: `24–48` PA** (fixture `38 PA`, 32-seed range `[32, 43]`, replacing naive `22–41`). **Signed College 28-game band: `90–160` PA** (fixture `129 PA`, 32-seed range `[116, 149]`, replacing naive `82–147`). | **Signed** (§5) |
| **D10** | Default `MonteCarloHarness` runtime budget (`≤ baseline + 30 s`, College `≤ 15 s`) | Warm Release `--no-build` default MC wall time: **`65.24 s`** (vs `124 s` Opus / `183.69 s` Astra baseline). Tier timing pilot (`-- --tier-pilot`): **`26.6 s` + `0.7 s` setup = `27.3 s ≤ 30 s`**; College share **`7.6 s ≤ 15 s`**. Actual pooled 64/16/8 execution inside `MathProbe`: **`21.77 s`** (`HS 11.19 s`, `College 6.76 s`, `Pro 3.81 s`). | **PASS** (§6) |

---

## 2. M1 — Pooled Band Measurements & Pro Run-Floor Verdict (D5)

### 2.1 Seed Manifest, Sample Design, and Cluster-SE Estimator

On head `7693b7d` (`Tools/MonteCarloHarness/Slice133Suites.cs:586–704`), `RunPooledTierBands` generates one flat (`ratingSpread: 0`, all ratings 50, neutral environment, no injuries/development/person/gear modifiers) v14 world from `LeagueSeed = 20260703` and simulates $k \in \{64, 16, 8\}$ independent replicate seasons per tier:
- **Replicate index:** $r \in \{0, \dots, k - 1\}$, season year $Y_r = 3000 + r$.
- **Schedule stream:** `SeasonScheduler.StreamFor(DefaultScheduleSeed, Y_r, tier)` where `DefaultScheduleSeed = 0x133D_5EED_0000_0001UL`:
  $$\text{seed}_{\text{sched}}(Y_r, t) = \text{0x133D\_5EED\_0000\_0001} \oplus (Y_r \times \text{0x9E3779B97F4A7C15}) \oplus ((t + 1) \times \text{0xC2B2AE3D27D4EB4F})$$
- **Outcome stream:** fresh `LeagueSimulator` (with a fresh `PitcherRestLedger`) seeded at:
  $$\text{seed}_{\text{out}}(t, r) = \text{0x1333\_0000\_0000} \oplus (t \ll 16) \oplus r$$
- **Ratio and cluster-SE estimator (§10.2 Q1):** for season-level numerator $X_i$ and denominator $D_i$ ($i = 1, \dots, k$), the pooled point estimate is $\hat{r} = \left(\sum_{i=1}^k X_i\right) / \left(\sum_{i=1}^k D_i\right)$ (scaled by $c = 27$ for $\text{R/9}$), with linearized season-cluster standard error:
  $$\text{SE}(\hat{r}) = c \cdot \frac{s_e}{\sqrt{k}\,\bar{D}}, \qquad s_e^2 = \frac{1}{k - 1}\sum_{i=1}^k (X_i - \hat{r} D_i)^2, \qquad \bar{D} = \frac{1}{k}\sum_{i=1}^k D_i$$
  and 95% confidence interval $\hat{r} \pm t_{0.975,\,k-1}\,\text{SE}(\hat{r})$. Both the harness's two-term Cornish–Fisher $t$ approximation (`Slice133Suites.cs:571`: `df=63 → 1.99834`, `df=15 → 2.13031`, `df=7 → 2.35631`) and exact Student-$t$ quantiles (`df=63 → 1.99834`, `df=15 → 2.13145`, `df=7 → 2.36462`) are reported below.

### 2.2 Reproduced Pooled Measurements on `7693b7d`

#### Counting Totals and Denominator Seam (`R`, `ER`, `outs`, `GS`)

| Tier | Seasons ($k$) | League Games | `GS` (team-games) | `PA` | `AB` | `H` | `2B` | `3B` | `HR` | `TB` | `BB` | `SO` | `R = ER` | `outs` | `outs / GS` (95% exact CI) |
| :-- | --: | --: | --: | --: | --: | --: | --: | --: | --: | --: | --: | --: | --: | --: | :-- |
| **HS** | 64 | 3,840 | 7,680 | 262,454 | 231,233 | 70,985 | 14,172 | 1,204 | 9,988 | 117,529 | 31,221 | 50,294 | 41,136 | 160,248 | **20.8656** `[20.7929, 20.9384]` (SE `.0364`) |
| **College** | 16 | 5,376 | 10,752 | 460,706 | 408,562 | 120,019 | 24,253 | 2,035 | 16,901 | 199,045 | 52,144 | 91,373 | 66,651 | 288,543 | **26.8362** `[26.7752, 26.8972]` (SE `.0286`) |
| **Pro** | 8 | 8,192 | 16,384 | 650,208 | 591,794 | 146,594 | 29,976 | 2,611 | 20,804 | 244,204 | 58,414 | 146,288 | 68,667 | 445,200 | **27.1729** `[27.0741, 27.2716]` (SE `.0418`) |

#### Pooled Rate Estimates, 95% $t$-Intervals, SE Achieved vs Target, and Band Verdicts

| Tier | Metric | Band | Point Estimate | Exact 95% $t$-Interval (CF 95% if different) | SE Achieved | SE Target | SE Met? | Band Verdict |
| :-- | :-- | :-- | --: | :-- | --: | --: | :--: | :--: |
| **HS** ($k=64$, $\text{df}=63$) | AVG | `.296–.316` | **.306985** | `[.304915, .309055]` | `.001036` | `≤ .0020` | Yes | **PASS** |
| | OBP | `.379–.399` | **.389424** | `[.387269, .391580]` | `.001079` | `≤ .0020` | Yes | **PASS** |
| | SLG | `.493–.523` | **.508271** | `[.504336, .512206]` | `.001969` | `≤ .0050` | Yes | **PASS** |
| | K% | `17.7–20.7%` | **19.1630%** | `[18.9937%, 19.3322%]` | `.000847` | `≤ .0020` | Yes | **PASS** |
| | BB% | `10.9–12.9%` | **11.8958%** | `[11.7641%, 12.0275%]` | `.000659` | `≤ .0020` | Yes | **PASS** |
| | HR/PA | `3.40–4.20%` | **3.8056%** | `[3.7384%, 3.8728%]` | `.000336` | `≤ .0020` | Yes | **PASS** |
| | **R/9** (gated) | `6.40–8.00` | **6.930957** | `[6.807972, 7.053942]` (`[6.808, 7.054]`) | `.061543` | `≤ .0600` | Marginal (`+.0015`) | **PASS** |
| | ER/GS (reported) | *(legacy R/G)* | **5.356250** | `[5.264440, 5.448060]` | `.045943` | — | — | Reported |
| **College** ($k=16$, $\text{df}=15$) | AVG | `.284–.304` | **.293760** | `[.292389, .295130]` | `.000643` | `≤ .0020` | Yes | **PASS** |
| | OBP | `.364–.384` | **.373694** | `[.372368, .375020]` | `.000622` | `≤ .0020` | Yes | **PASS** |
| | SLG | `.473–.503` | **.487184** | `[.484590, .489778]` | `.001217` | `≤ .0050` | Yes | **PASS** |
| | K% | `18.4–21.4%` | **19.8333%** | `[19.7183%, 19.9482%]` | `.000539` | `≤ .0020` | Yes | **PASS** |
| | BB% | `10.3–12.3%` | **11.3183%** | `[11.2165%, 11.4201%]` | `.000478` | `≤ .0020` | Yes | **PASS** |
| | HR/PA | `3.30–4.10%` | **3.6685%** | `[3.6171%, 3.7199%]` | `.000241` | `≤ .0020` | Yes | **PASS** |
| | **R/9** (gated) | `5.80–7.10` | **6.236772** | `[6.170283, 6.303262]` (CF `[6.1703, 6.3032]`) | `.031195` | `≤ .0400` | Yes | **PASS** |
| | ER/GS (reported) | *(legacy R/G)* | **6.198940** | `[6.131775, 6.266104]` | `.031511` | — | — | Reported |
| **Pro** ($k=8$, $\text{df}=7$) | AVG | `.240–.260` | **.247711** | `[.246680, .248742]` | `.000436` | `≤ .0020` | Yes | **PASS** |
| | OBP | `.308–.325` | **.315296** | `[.314098, .316494]` | `.000507` | `≤ .0020` | Yes | **PASS** |
| | SLG | `.395–.430` | **.412650** | `[.409750, .415551]` | `.001227` | `≤ .0050` | Yes | **PASS** |
| | K% | `20.0–25.0%` | **22.4986%** | `[22.3384%, 22.6589%]` | `.000678` | `≤ .0020` | Yes | **PASS** |
| | BB% | `7.5–10.5%` | **8.9839%** | `[8.9035%, 9.0643%]` | `.000340` | `≤ .0020` | Yes | **PASS** |
| | HR/PA | `2.70–3.70%` | **3.1996%** | `[3.1512%, 3.2480%]` | `.000205` | `≤ .0020` | Yes | **PASS** |
| | **R/9** (gated) | `4.20–4.80` | **4.164441** | `[4.103267, 4.225615]` (CF `[4.1035, 4.2254]`) | `.025871` | `≤ .0300` | Yes | **FAIL** (`< 4.20`; CI crosses `4.20`) |
| | ER/GS (reported) | *(legacy R/G)* | **4.191101** | `[4.130547, 4.251656]` | `.025609` | — | — | Reported (`< 4.20`) |

*Note on HS `R/9` SE:* Observed $\text{SE}(\text{R/9}) = 0.06154$ on 64 HS seasons is $0.00154$ above the planning target $0.0600$ (which assumed a team-game run SD of $3.5$; actual HS season-cluster R/9 SD is $0.4923$). Because the entire 95% confidence interval `[6.808, 7.054]` sits more than $0.40\text{ R/9}$ above the $6.40$ floor and $0.94\text{ R/9}$ below the $8.00$ ceiling, 64 HS seasons remain completely sufficient for band gating and stay within the D10 runtime budget.

### 2.3 Why Pro `R/9` Sits Below `4.20`: Exact Markov Chain & Pre-Frozen Holdout Evidence

To separate sampling noise from the true model mean before presenting the D5 choice, two exact diagnostics were executed:

1. **Exact Absorbing Markov-Chain Solution (`AtBatResolver` + `BaseOutMatrices.RunExpectancy`):**
   Using the exact flat-50 PA outcome probabilities from `AtBatResolver.ComputeProbabilities` and the exact 25-state transition matrix $T = \sum_e p_e A_e$ under `BaseOutAdvancement` (`SingleScoresFrom2nd = 0.60`, `DoubleScoresFrom1st = 0.45`), the fundamental-matrix equation $(I - Q)\,\text{RE} = r$ yields the exact expected runs per 3-out half-inning from `(bases empty, 0 outs)`:

   | Tier | Effective Pitcher / Defense Rating | Exact Markov `AVG / OBP / SLG` | Exact `K% / BB% / HR%` | Exact $\text{RE}(\text{empty}, 0\text{ out})$ | Exact $9 \times \text{RE}(\text{empty}, 0\text{ out})$ (runs per 27 outs) |
   | :-- | :--: | :--: | :--: | --: | --: |
   | **HS** (`delta −20`) | `30 / 30 / 30` | `.3062 / .3889 / .5077` | `19.19% / 11.92% / 3.80%` | `0.767222` | **6.9050** |
   | **College** (`delta −16`) | `34 / 34 / 34` | `.2938 / .3736 / .4877` | `19.85% / 11.29% / 3.68%` | `0.692660` | **6.2339** |
   | **Pro** (`delta 0`) | `50 / 50 / 50` | `.2473 / .3150 / .4121` | `22.50% / 9.00% / 3.20%` | `0.460438` | **4.1439** |

   In a pure absorbing half-inning chain where every 3-out half-inning begins at `(empty, 0 out)`, asymptotic `R/9` ($27 \times \text{runs} / \text{outs}$) equals $9 \times \text{RE}(\text{empty}, 0\text{ out}) = \mathbf{4.14394}$, plus a small positive boundary correction ($\approx +0.008\text{ to }+0.020\text{ R/9}$) from walk-off half-innings in the bottom of the 9th or extra innings where the winning run scores before all 3 outs are recorded.

2. **Pre-Frozen Holdout Pro Sample (`IDs 129–136`, $k=8$, and `IDs 129–160`, $k=32$ = $32,768$ games / $65,536$ `GS`):**
   We froze and executed the §10.2 Q1 holdout replicate IDs `129–136` ($k=8$) and `129–160` ($k=32$) with the same stream formula (`year = 3000 + id`, `outcomeSeed = 0x1333_0000_0000 ^ (2 << 16) ^ id`):

   | Pro Sample | Seasons ($k$) | Games / `GS` | `PA` | `R = ER` | `outs` | `outs / GS` (95% exact CI) | `R/9` (95% exact CI; SE) | `ER/GS` (95% exact CI; SE) |
   | :-- | --: | :-- | --: | --: | --: | :-- | :-- | :-- |
   | Default (`r = 0..7`) | 8 | 8,192 / 16,384 | 650,208 | 68,667 | 445,200 | `27.1729 [27.0741, 27.2716]` | **4.1644** `[4.1033, 4.2256]` (SE `.0259`) | **4.1911** `[4.1305, 4.2517]` (SE `.0256`) |
   | Holdout (`IDs 129..136`) | 8 | 8,192 / 16,384 | 650,468 | 68,772 | 444,900 | `27.1545 [27.0608, 27.2483]` | **4.1736** `[4.1319, 4.2154]` (SE `.0176`) | **4.1975** `[4.1621, 4.2329]` (SE `.0150`) |
   | **Holdout (`IDs 129..160`)** | **32** | **32,768 / 65,536** | **2,598,304** | **273,579** | **1,779,090** | **`27.1468 [27.1184, 27.1751]`** | **4.1519** **`[4.1311, 4.1727]`** (SE **`.0102`**) | **4.1745** **`[4.1540, 4.1950]`** (SE **`.0101`**) |

   **Conclusion:** On 32 holdout Pro seasons ($32,768$ games), the entire 95% confidence interval for Pro `R/9` (`[4.1311, 4.1727]`) lies **strictly below `4.20`** ($t = -4.71$, $p = 4.9 \times 10^{-5}$). Increasing the default Pro sample size cannot make `R/9 ≥ 4.20` pass, because the true population mean of the unchanged engine is **`R/9 = 4.152`** (`ER/GS = 4.174`).

3. **The `ER/GS → R/9` Denominator Seam (`outs/GS > 27` in Pro vs `< 27` in College):**
   Because $\text{R/9} = \text{ER/GS} \times (27 / (\text{outs/GS}))$, the relationship between `R/9` and `ER/GS` reverses between College and Pro:
   - In **College** (`~6.20 ER/GS`), higher scoring produces fewer 9th-inning ties (fewer extra innings) and more home wins where the bottom of the 9th is skipped (`outs/GS = 26.836 < 27`), so `R/9` (`6.237`) is **`+0.038` higher** than `ER/GS` (`6.199`).
   - In **Pro** (`~4.18 ER/GS`) on a flat spread-0 world where every matchup has equal strength, lower scoring increases 9th-inning tie frequency; extra-inning outs outweigh skipped home 9th innings, giving `outs/GS = 27.173` on the 8-season default pool (`27.147` on the 32-season holdout pool). Converting the denominator from `GS` (`R/G`) to `outs / 27` (`R/9`) therefore scales the Pro run rate **downward by `27 / 27.1729 = 0.993639` (`−0.636%`, or `−0.0267` runs/9)**.
   - Historically, the Phase 3 macro `4.2–4.8` band was gated on **`ER/GS` (`R/G`)** over a single 8-team, 154-game MLB season (`SeasonSeed = 777`, `1,228 GS`, retired guard `ER = 5237`, where that single seed drew `ER/GS = 4.265`; similarly, `SeasonSeed = 777` in 13-3's `season-pipeline` draws `2026 ER/GS = 4.30 / R/9 = 4.28` and `2027 ER/GS = 4.18 / R/9 = 4.16`). When pooled over 8–32 independent seasons and converted from `ER/GS` to `R/9`, the true flat-50 center is `4.152 R/9` (`4.164` on the 8-season default pool).

### 2.4 D5 Owner-Choice Packet

Per brief §3.2 D5 and `league_restructure.md` §11.2, 13-3 makes no sim change; the owner chooses between:

#### Option (i) — Restate the Pro `R/9` Band Floor (Recommended)
- **What changes:** In `league_restructure.md` §11.2 and `Tools/MonteCarloHarness/Program.cs` (`TierBands[(int)LeagueTier.Pro].RgLo`), restate the Pro `R/9` floor from **`4.2–4.8`** to **`4.1–4.8`** (or `4.05–4.80`). Keep `season-pipeline`'s reference band and `micro-game` §11.2 (`R/G 4.24` in `4.2–4.8`) untouched.
- **Evidence & rationale:**
  1. The unchanged Phase 3/4 `AtBatResolver` + `BaseOutAdvancement(0.60, 0.45)` model has an exact Markov half-inning expectancy of $9 \times \text{RE}(0,0) = 4.1439\text{ R/9}$ and a 32-season simulated Pro mean of $\text{R/9} = 4.1519 \pm 0.0208$ ($\text{ER/GS} = 4.1745 \pm 0.0205$).
  2. Switching the gated metric from `ER/GS` (`R/G`) to `R/9` ($27 \times \text{ER} / \text{outs}$) shifts Pro runs downward by $-0.0267$ because `outs/GS = 27.173 > 27.0` in flat-world Pro play.
  3. On the 8-season default pool (`R/9 = 4.1644`, `SE = 0.0259`), a `4.10` floor provides `$+0.0644\text{ R/9}$` ($2.49\text{ SE}$) clearance without touching any simulation probability, advancement constant, `MicroGame` outcome, `RE24` fixture, or the Pro guard candidate (`PA 81433 / H 18471 / ER 8803`, SHA-256 `932ACE26...`).

#### Option (ii) — Re-Centre Runs via `BaseOutAdvancement`'s Two Knobs (Separately Sanctioned Change)
- **What changes:** Increase `BaseOutAdvancement.SingleScoresFrom2nd` (currently `0.60`) and `BaseOutAdvancement.DoubleScoresFrom1st` (currently `0.45`) in `Assets/Simulation/Baseball/BaseOutAdvancement.cs:22–23` so that flat-50 Pro `R/9` centers comfortably inside `4.2–4.8`.
- **Exact Markov sensitivity grid across all three tiers:**

  | `(SingleScoresFrom2nd, DoubleScoresFrom1st)` | Pro $\text{RE}(0,0)$ (`0.42–0.56` gate) | Pro $9 \times \text{RE}(0,0)$ | $\Delta\text{Pro R/9}$ | Predicted 8-Season Pro `R/9` | College $9 \times \text{RE}(0,0)$ (`5.8–7.1`) | HS $9 \times \text{RE}(0,0)$ (`6.4–8.0`) |
  | :--: | --: | --: | --: | --: | --: | --: |
  | `(0.60, 0.45)` *(shipped)* | `0.46044` | `4.1439` | `+0.0000` | `4.164` | `6.2339` | `6.9050` |
  | `(0.64, 0.49)` | `0.46538` | `4.1884` | `+0.0445` | `~4.209` | `6.2945` | `6.9699` |
  | `(0.66, 0.51)` | `0.46785` | `4.2106` | `+0.0667` | `~4.231` | `6.3247` | `7.0024` |
  | **`(0.70, 0.55)`** *(Option ii candidate)* | **`0.47279`** | **`4.2551`** | **`+0.1112`** | **`~4.276`** | **`6.3852`** | **`7.0673`** |
  | `(0.72, 0.57)` | `0.47526` | `4.2773` | `+0.1334` | `~4.298` | `6.4155` | `7.0998` |

- **Trade-offs of Option (ii):**
  1. To clear `4.20` by $\ge 2.5\text{ SE}$ ($\approx +0.065\text{ R/9}$ above `4.20`, i.e. center $\approx 4.27$), the knobs must rise by $+10\text{ pp}$ each to **`(0.70, 0.55)`** (70% scoring from 2B on a single, 55% from 1B on a double — noticeably above MLB baserunning averages of ~60% and ~45%).
  2. Because `BaseOutAdvancement` is shared by `LeagueSimulator`, `MicroGame`, and `BaseOutMatrices`, changing it alters every macro and micro game score across all tiers and invalidates the current Pro guard candidate (`PA 81433 / H 18471 / ER 8803`, ledger `932ACE26...`), requiring a new guard recording and full regression pass under a separate sanction.

### 2.5 Argon Recommendation on D5
**Recommend Option (i): restate the Pro `R/9` band in `league_restructure.md` §11.2 and `MonteCarloHarness` to `4.1–4.8`.** The `4.164` pooled measurement is not a regression introduced by 13-1..13-3; it is the exact mathematical center ($9 \times \text{RE}(0,0) = 4.144$, 32-season mean $4.152$) of the untouched Phase 3/4 resolver and base-advancement matrices once evaluated on multi-season pooled `R/9` rather than single-seed `ER/GS`.

---

## 3. M2 — College 28-Game Rate Re-Band (`CS-M` §1 & §6)

### 3.1 Confirmation of `CS-M` §1 Rows and `RunCollegeRateSmoke`
- **Rate rows (`CS-M` §1):** Confirmed verbatim for the 28-game (`336` league games, `672` `GS`) Wednesday + Saturday College calendar on flat spread-0 rosters (`delta = −16`):
  - `AVG`: `.284–.304` (`H / AB`)
  - `OBP`: `.364–.384` (`(H + BB) / PA`, matching harness definition)
  - `SLG`: `.473–.503` (`TB / AB`)
  - `K%`: `18.4–21.4%` (`SO / PA`)
  - `BB%`: `10.3–12.3%` (`BB / PA`)
  - `HR/PA`: `3.3–4.1%` (`HR / PA`)
  - `R/9`: `5.8–7.1` ($27 \times \text{ER} / \text{outs}$, with legacy `ER/GS` reported beside it on the same sample).
- **`RunCollegeRateSmoke` (`2/2` in default MC, `Slice133Suites.cs:689–702`):** Confirmed passing on the first four College replicates:
  1. `college-rate-smoke 1`: all 4 seasons have `GS = 2 × 336 = 672`, `PA > 0`, `AB = PA − BB`, and `0 < outs ≤ PA − H − BB`.
  2. `college-rate-smoke 2`: all 7 computed metrics are finite and in-domain (`AVG, OBP, K%, BB%, HR/PA ∈ [0, 1]`, `SLG ∈ [0, 4]`, `R/9 ≥ 0`).

### 3.2 32-Season Training Pilot Timing, Stop-Rule Verdict, and Results
- **Command:** `dotnet run --project Tools/MonteCarloHarness -c Release --no-build -- --college-analysis rates --pilot`
- **Design (§10.2 Q3):** Training world IDs `w ∈ {1..8}` (`LeagueSeed ^ w`, `ratingSpread: 0`) $\times$ scheduler seeds `s ∈ {1, 2}` $\times$ outcome seeds `o ∈ {1, 2}` (`0xC5AA_0000_0000 ^ (w << 24) ^ (s << 12) ^ o`) = **32 seasons** (`10,752` games, `21,504` `GS`, `923,964` `PA`).
- **Timing & Stop-Rule Verdict:**
  - Pilot wall time: **`16.4 s` (`512 ms/season`)** in the CLI harness (`10.44 s` / `326.3 ms/season` without per-tuple console I/O) — **well under the `60 s` pilot stop limit**.
  - Full-matrix extrapolation ($16,384$ seasons = $512 \times 32$ seasons with $25\%$ overhead reserve):
    $$16,384 \times 512\text{ ms} \times 1.25 = \mathbf{174.6\text{ min}} \quad (\approx 2.91\text{ hours; raw } 139.7\text{ min})$$
  - **Stop rule applied (§10.2 Q3 / prompt §3):** Because `174.6 min > 30.0 min`, **the full 16,384-season matrix was NOT started**.
- **32-Season Pilot Pooled Results (World-Clustered $k=8, \text{df}=7$ and Tuple-Clustered $k=32, \text{df}=31$):**

  | Metric | Target Band | Pooled Point Estimate | World-Clustered SE ($k=8$) | World-Clustered 95% Exact CI ($\text{df}=7$) | Tuple-Clustered SE ($k=32$) | Tuple-Clustered 95% Exact CI ($\text{df}=31$) | Pilot Verdict |
  | :-- | :-- | --: | --: | :-- | --: | :-- | :--: |
  | **AVG** | `.284–.304` | **.294508** | `.000362` | `[.293651, .295364]` | `.000608` | `[.293269, .295747]` | **PASS** |
  | **OBP** | `.364–.384` | **.374739** | `.000564` | `[.373405, .376072]` | `.000650` | `[.373413, .376064]` | **PASS** |
  | **SLG** | `.473–.503` | **.487814** | `.000959` | `[.485546, .490082]` | `.001219` | `[.485327, .490300]` | **PASS** |
  | **K%** | `18.4–21.4%` | **19.8019%** | `.000405` | `[19.7060%, 19.8977%]` | `.000403` | `[19.7197%, 19.8840%]` | **PASS** |
  | **BB%** | `10.3–12.3%` | **11.3723%** | `.000444` | `[11.2673%, 11.4773%]` | `.000387` | `[11.2934%, 11.4512%]` | **PASS** |
  | **HR/PA** | `3.30–4.10%` | **3.6646%** | `.000237` | `[3.6086%, 3.7207%]` | `.000217` | `[3.6203%, 3.7090%]` | **PASS** |
  | **R/9** (gated) | `5.80–7.10` | **6.276207** | `.032486` | `[6.199389, 6.353025]` | `.028647` | `[6.217782, 6.334632]` | **PASS** |
  | **ER/GS** (reported) | *(legacy R/G)* | **6.244978** | `.032700` | `[6.167655, 6.322300]` | `.026862` | `[6.190192, 6.299764]` | Reported |

  *Counting totals (32 pilot seasons):* `PA = 923,964`, `AB = 818,888`, `H = 241,169` (`2B = 48,772`, `3B = 3,972`, `HR = 33,860`), `TB = 399,465`, `BB = 105,076`, `SO = 182,962`, `R = ER = 134,292`, `outs = 577,719`, `GS = 21,504`, `outs/GS = 26.8657` (`SE = 0.0120` world-clustered).

  *Training-only spread-25 timing & rate diagnostic (`worlds 1–4 × sched 1 × out 1`, $k=4$ seasons, $1,344$ games):* Completed in `4.39 s` (`1,096 ms/season`, including world-gen); `AVG = .2914`, `OBP = .3663`, `SLG = .4789`, `K% = 20.69%`, `BB% = 10.58%`, `HR/PA = 3.54%`, `R/9 = 5.9683`, `ER/GS = 5.9609`, `outs/GS = 26.9665`.

### 3.3 Pre-Frozen Reduced Holdout Plan for M2 (Awaiting Claude Opus Review Before Execution)

Because 32 seasons already achieve world-clustered $\text{SE}(\text{R/9}) = 0.0325$ (below the `0.04` default target) and rate SEs $\le 0.00096$, scaling by $16\times$ to **512 holdout seasons** cuts SEs by $4\times$ while keeping total runtime well inside the 30-minute limit:

- **Primary Calibration Matrix (Holdout Flat Worlds, `ratingSpread: 0`):**
  - **World IDs:** `w ∈ {129..160}` (**32 untouched holdout world IDs**, `LeagueSeed ^ (ulong)w`).
  - **Scheduler seeds:** `s ∈ {1, 2, 3, 4}` (**4 schedules per world**).
  - **Outcome seeds:** `o ∈ {1, 2, 3, 4}` (**4 outcome streams per schedule**, `0xC5AA_0000_0000UL ^ ((ulong)w << 24) ^ ((ulong)s << 12) ^ (ulong)o`).
  - **Total size:** $32 \times 4 \times 4 = \mathbf{512\text{ seasons}}$ = **$172,032$ games** ($344,064\text{ GS}$, $\approx 14.78\text{M PA}$).
  - **Expected precision ($\text{df} = 31$ world clusters):** $\text{SE}(\text{R/9}) \approx 0.0080$ (95% half-width $\approx \pm 0.016\text{ R/9}$ vs `1.30`-wide band `5.8–7.1`); $\text{SE}(\text{AVG}, \text{OBP}, \text{K\%}, \text{BB\%}) \le 0.00018$; $\text{SE}(\text{SLG}) \le 0.00032$; $\text{SE}(\text{HR/PA}) \le 0.00006$.
  - **Forecast wall time:** $512 \times 512\text{ ms} \times 1.25 = \mathbf{5.46\text{ min}}$ (raw $4.37\text{ min}$).
- **Separate Spread-25 Diagnostic Batch (Reported, Not Gated as Flat-World Calibration):**
  - **Tuples:** Holdout world IDs `w ∈ {129..160}` (`ratingSpread: 25`) $\times$ `s ∈ {1, 2}` $\times$ `o ∈ {1, 2}` = **128 seasons** ($43,008$ games).
  - **Forecast wall time:** $128 \times 650\text{ ms} \times 1.25 = \mathbf{1.73\text{ min}}$.
- **Combined M2 Wall-Time Forecast:** **$7.19\text{ min}$** ($\le 30\text{ min}$ stop rule, with a hard runtime kill at $20\text{ min}$).
- **Pass criteria:** All 7 pooled flat-world rates inside the §3.1 bands, world-clustered ($\text{df}=31$) and tuple-clustered ($\text{df}=511$) 95% $t$-intervals reported, and spread-25 diagnostic distribution logged separately.

---

## 4. M4 — Development Values, Practice Exposure, Goldens, and Single-Pass Gradient (D7)

### 4.1 Signed Practice Constants (`DevelopmentProfile`)

We sign the shared candidate constants for `Assets/Simulation/Baseball/DevelopmentManager.cs:90,100`:
- **`PracticeFracCap = 0.25`** (unchanged; hard seasonal cap on extra gap fraction / veteran decline relief).
- **`PracticeFracPerHour = 0.001`** (updated from `0.0005`; e-fold moves from `500 h` to **`250 h`**).
- **No per-tier practice multiplier:** the per-tier in-season gradient (`80 h < 112 h < 128 h`) comes naturally from `TierSeasonCalendar`'s D3-A locked practice days.

### 4.2 In-Season and Offseason Exposure Table (`0`, `2`, and `6 h/day` Offseason)

Under $f(h) = \text{cap}\cdot(1 - \exp(-\text{slope}\cdot h / \text{cap}))$ with $\text{cap} = 0.25$, nonlinear saturation means offseason hours contribute the marginal fraction $\Delta f_{\text{off}} = f(h_{\text{in}} + h_{\text{off}}) - f(h_{\text{in}})$:

| Tier | In-Season Window (Days) | In-Season Practice (`2 h/day`) | Free-Slider Offseason Days | Offseason Scenario | Offseason Hours ($h_{\text{off}}$) | Annual Hours ($h_{\text{tot}}$) | Current `.0005` $f(h_{\text{in}})$ | Current `.0005` $f(h_{\text{tot}})$ | Current `.0005` $\Delta f_{\text{off}}$ | **Signed `.0010` $f(h_{\text{in}})$** | **Signed `.0010` $f(h_{\text{tot}})$** | **Signed `.0010` $\Delta f_{\text{off}}$** |
| :-- | :--: | --: | --: | :--: | --: | --: | --: | --: | --: | --: | --: | --: |
| **HS** | `57–124` (68 d) | 40 d = **80 h** | **297 d** | `0 h/day` | 0 h | **80 h** | `.036964` | `.036964` | `.000000` | **.068463** | **.068463** | **.000000** |
| | | | | `2 h/day` | 594 h | **674 h** | `.036964` | `.185060` | `.148096` | **.068463** | **.233131** | **.164669** |
| | | | | `6 h/day` | 1,782 h | **1,862 h** | `.036964` | `.243966` | `.207002` | **.068463** | **.249854** | **.181392** |
| **College** | `36–132` (97 d) | 56 d = **112 h** | **268 d** | `0 h/day` | 0 h | **112 h** | `.050171` | `.050171` | `.000000` | **.090274** | **.090274** | **.000000** |
| | | | | `2 h/day` | 536 h | **648 h** | `.050171` | `.181594` | `.131423` | **.090274** | **.231282** | **.141009** |
| | | | | `6 h/day` | 1,608 h | **1,720 h** | `.050171` | `.241984` | `.191813` | **.090274** | **.249743** | **.159469** |
| **Pro** | `92–240` (149 d) | 64 d = **128 h** | **216 d** | `0 h/day` | 0 h | **128 h** | `.056465` | `.056465` | `.000000` | **.100176** | **.100176** | **.000000** |
| | | | | `2 h/day` | 432 h | **560 h** | `.056465` | `.168430` | `.111966` | **.100176** | **.223385** | **.123209** |
| | | | | `6 h/day` | 1,296 h | **1,424 h** | `.056465` | `.235510` | `.179045` | **.100176** | **.249160** | **.148984** |
| **Fixture (`§10.5`)** | — | — | 365 d | `6 h/day` all year | — | **2,190 h** | — | `.246869` | — | — | **.249961** | — |

### 4.3 Identical-Player & Identical-RNG Comparisons (`0` vs `.0005` vs `.001`)

Evaluating `DevelopmentCurve.DevelopRating` on identical players at `bell = 0` (deterministic center) and averaged over $1,000$ identical RNG draws (`seed = 0x9D2DE71UL`):

| Player Archetype | Age at Rollover | Start $\to$ Ceiling (Gap) | Hours ($h$) | No Practice (`0`) `bell=0` (1k-RNG mean) | Current `.0005` `bell=0` (1k-RNG mean) | **Signed `.0010` `bell=0` (1k-RNG mean)** | Why `.0010` Improves Calibration |
| :-- | :--: | :--: | --: | :--: | :--: | :--: | :-- |
| **HS Freshman (`in-season`)** | `17` ($w=10/12$) | `60 → 74` (14) | 80 h | `66` (`65.860`) | `66` (`66.350`) | **`67` (`66.812`)** | Under `.0005`, 80 h of team practice gives **0 deterministic points** above no-practice (`66 vs 66`) on the standard 14-point avatar headroom; `.001` yields a clean **`+1` rating point** (`67 vs 66`). |
| **HS Freshman (`in-season`, gap 20)** | `17` ($w=10/12$) | `50 → 70` (20) | 80 h | `58` (`58.330`) | `59` (`59.081`) | **`60` (`59.710`)** | Full in-season HS practice adds `+1.37` unrounded (`+2` integer at `bell=0`, `+1.38` mean). |
| **HS Freshman (`+2 h/d offseason`)** | `17` ($w=10/12$) | `50 → 70` (20) | 674 h | `58` (`58.330`) | `62` (`62.046`) | **`63` (`63.006`)** | Steady offseason training reaches `+4.66` additional points (`+5` at `bell=0`). |
| **College Soph (`in-season`, gap 20)** | `20` ($w=7/12$) | `50 → 70` (20) | 112 h | `56` (`55.842`) | `57` (`56.846`) | **`58` (`57.641`)** | In-season College practice adds `+1.81` unrounded (`+2` integer at `bell=0`, `+1.80` mean). |
| **College Soph (`+2 h/d offseason`)** | `20` ($w=7/12$) | `50 → 70` (20) | 648 h | `56` (`55.842`) | `59` (`59.467`) | **`60` (`60.464`)** | Offseason training lifts growth by `+4.63` points (`+4` at `bell=0`). |
| **Pro Rookie (`in-season`, gap 20)** | `23` ($w=4/12$) | `50 → 70` (20) | 128 h | `53` (`53.241`) | `54` (`54.439`) | **`55` (`55.247`)** | In-season Pro practice adds `+2.00` points (`53 → 55`). |
| **Pro Rookie (`+2 h/d offseason`)** | `23` ($w=4/12$) | `50 → 70` (20) | 560 h | `53` (`53.241`) | `57` (`56.813`) | **`58` (`57.900`)** | Offseason work closes an extra `+4.66` points (`53 → 58`). |
| **Peak-Pinned 16yo-Built (`in-season`)** | `27` ($w=0$) | `60 → 74` (14) | `80/112/128 h` | `60` (`60.000`) | `61` (`61.000`) | **`61` (`61.000`)** | At `80/112/128 h`, $14 \times f(h) = 0.96 / 1.26 / 1.40 \to +1\text{/rating}$ (`BatSum = 183`, `PointsUp = 7`). |
| **Peak-Pinned 16yo-Built (`+2 h/d off`)** | `27` ($w=0$) | `60 → 74` (14) | `674/648/560 h` | `60` (`60.000`) | `63/63/62` | **`63/63/63`** | Under `.001`, all three tiers reach $+3\text{/rating}$ (`BatSum = 189`, `PointsUp = 21`) with `2 h/d` offseason practice. |
| **Peak-Pinned 16yo-Built (`§10.5 2190h`)** | `27` ($w=0$) | `60 → 74` (14) | 2,190 h | `60` (`60.000`) | `63` (`63.000`) | **`63` (`63.000`)** | $14 \times 0.249961 = 3.4995 < 3.5 \to +3\text{/rating}$ (`BatSum = 189`, `PointsUp = 21`, `PlayersChanged = 1`). |
| **Pro Veteran (`age 40→41`, physical)** | `41` ($a=(14/15)^2$) | `70 → 70` (0) | 128 h / 560 h | `67` (`67.396`) | `68` (`67.538 / 67.816`) | **`68` (`67.646 / 67.954`)** | Mitigates decline from $-3$ to $-2$ (`staying in shape`). |

### 4.4 Signed `practice-seam` (`RunPracticeSeamSuite`) Updates and Goldens

When Opus applies `PracticeFracPerHour = 0.001` in `DevelopmentManager.cs`, three harness updates in `RunPracticeSeamSuite` (`Tools/MonteCarloHarness/Program.cs:6119–6339`) are signed:

1. **Pure `§4` conversion e-fold, first-hour slope, and cap-asymptote checks (`Program.cs:6124–6163`):**
   - With `PracticeFracPerHour = 0.001` and `PracticeFracCap = 0.25`, the e-fold (`PracticeFracCap / PracticeFracPerHour`) is **`250 h`** (where $f(250) = 0.25(1 - e^{-1}) = 0.15803013970713942$), with doubled anchors $f(500) = 0.21616617919084683$ and $f(1000) = 0.24542109027781644$ (slices `0.1580 → 0.0581 → 0.0293`).
   - At $h = 1$, $f(1) = 0.25(1 - e^{-0.004}) = 0.000998002664$, so $|f(1) - \text{PracticeFracPerHour}| = 1.997336 \times 10^{-6} \approx \text{slope}^2 / (2\cdot\text{cap}) = 2.0 \times 10^{-6}$. Update the first-hour check tolerance from `< 1e-6` to **`< 2.5e-6`** (or `<= DevelopmentProfile.PracticeFracPerHour * DevelopmentProfile.PracticeFracPerHour / DevelopmentProfile.PracticeFracCap`).
   - **Cap-asymptote probe (§10.7 R1, signed 2026-11-20):** At `.001`, a literal `10,000 h` is $40$ e-folds ($\exp(-40) \approx 4.25 \times 10^{-18} < 2^{-53}$), which rounds $f(10000)$ to `0.25` in IEEE-754 `double`. Restate the finite probe to **`20 · PracticeFracCap / PracticeFracPerHour = 5,000 h`** (20 e-folds, matching the dimensionless probe at `.0005`), where $f(5000) = 0.2499999994847116 < 0.25$, while `PracticeFraction(1_000_000_000) == 0.25`.
2. **`§10.5` End-to-End Practice World and `§4` Succession Guard Goldens (`Program.cs:6241–6338`):**
   - **`§10.5` accumulate path:** `10 days × 6 h = 60 h` accrued in `Game_State`; rollover consumes and clears `avatar_practice_credit` to `0` (`CreditAccrued == true && CreditCleared == true`).
   - **`§10.5` Practice lever (`2,190 h`, `frac = 0.249961 ≈ 0.2500`):** moves the peak-pinned 16yo-built avatar (`start 60/60/60 bat = 180, 50/50/50/50 pit/fld; ceiling 74/74/74 bat = 222, 64/64/64/64 pit/fld; gap = 14`) by **`+3` per rating** ($14 \times 0.249961 = 3.4995 \to 3$):
     - **`practiced.BatSum == 189`**
     - **`idle.BatSum == 180`**
     - **`practiced.PlayersChanged == 1 && practiced.PointsUp == 21 && idle.PlayersChanged == 0`**
     - **`practiced.NpcFingerprint == idle.NpcFingerprint`** (avatar-only; every NPC bit-identical).
     *(Only the check description string's `(frac 0.2469)` updates to `(frac 0.2500)`; all integer goldens `189`, `180`, `1`, `21`, `0` are invariant.)*
   - **`§4` Succession guard:** retiring founder's `2,190` banked hours are discarded when `CareerManager` hands off to the peak-age heir (`start 30/30/30 bat, potential 90/90/90 bat, gap 60`); **`heirRow.BatPower == 30 && heirRow.BatContact == 30 && heirRow.BatDiscipline == 30`** (`0` leaked growth) and **`keyCleared == true`**.
3. **Suite ordering fix (`Program.cs:6160–6234` vs `6241–6338`):**
   - Currently, `§10.9 scouting seam in the sweep` (`lines 6160–6234`, which calls `PromotionManager.RunOffseason` and throws `InvalidOperationException` on v14 until 13-6) sits *before* `§10.5` and `§4`, aborting `RunPracticeSeamSuite` at `5/6` before the 5 non-promotion checks in `§10.5` and `§4` run. Move the `§10.9` promotion block to the **end** of `RunPracticeSeamSuite` so all 10 non-promotion checks in `practice-seam` run and pass (`10/11`).

### 4.5 Spread-25 Single-Pass Raw Batter Talent Gradient (`RunDevelopmentTiersSuite`)

Per §10.3 Q6, the 13-3 single-pass gradient gates mean raw batter sum ($\text{BatPower} + \text{BatContact} + \text{BatDiscipline} \in [0, 300]$, position players ` !IsPitcher`, excluding effective tier/person/gear deltas) on a spread-25 generated world before and after one age tick (`AgeAllPlayers()`) and one offseason pass (`DevelopmentManager.RunDevelopment(2026)`), requiring **exact rational ordering $\text{HS} < \text{College} < \text{Pro}$ with zero negative tolerance** ($\text{Sum}_a \cdot \text{Count}_b < \text{Sum}_b \cdot \text{Count}_a$).

#### Fixed Default Fixture (`LeagueSeed + 133`, `devRng = LeagueSeed + 134`, `Slice133Suites.cs:859–907`)

| Stage | HS (`148` pos players) | College (`360` pos players) | Pro (`480` pos players) | Gap `College − HS` | Gap `Pro − College` | Cross-Product Check (`HS < Col`, `Col < Pro`) |
| :-- | :--: | :--: | :--: | --: | --: | :--: |
| **Pre-Pass** | `18,059 / 148 = 122.0203` | `47,258 / 360 = 131.2722` | `70,805 / 480 = 147.5104` | **`+9.2520`** | **`+16.2382`** | `6,501,240 < 6,994,184` (✓); `22,683,840 < 25,489,800` (✓) |
| **Post-Pass** (`1,199` changed, `+12,460 / −1,266`) | `19,639 / 148 = 132.6959` | `48,893 / 360 = 135.8139` | `70,670 / 480 = 147.2292` | **`+3.1179`** | **`+11.4153`** | `7,070,040 < 7,236,164` (✓); `23,468,640 < 25,441,200` (✓) |

#### Multi-Seed Robustness: 32 Training Worlds (`IDs 1–32`) and 32 Holdout Worlds (`IDs 129–160`)

| Sample ($n=32$, $\text{df}=31$) | Stage | Mean HS `[95% CI]` | Mean College `[95% CI]` | Mean Pro `[95% CI]` | Paired Gap `Col − HS` (SE; 95% CI; `[min, max]`) | Paired Gap `Pro − Col` (SE; 95% CI; `[min, max]`) | Strict Seeds |
| :-- | :--: | :--: | :--: | :--: | :-- | :-- | :--: |
| **Training (`IDs 1–32`)** | Pre | `124.03 [123.51, 124.55]` | `132.64 [132.34, 132.93]` | `146.75 [146.45, 147.04]` | **`+8.61`** (SE `.282`; **`[+8.04, +9.18]`**; `[+5.29, +12.57]`) | **`+14.11`** (SE `.220`; **`[+13.66, +14.56]`**; `[+11.97, +16.71]`) | `32 / 32` |
| | Post | `134.52 [134.02, 135.01]` | `137.19 [136.91, 137.47]` | `146.44 [146.15, 146.72]` | **`+2.68`** (SE `.270`; **`[+2.13, +3.23]`**; `[−0.38, +6.39]`) | **`+9.24`** (SE `.209`; **`[+8.82, +9.67]`**; `[+7.31, +11.64]`) | `30 / 32` |
| **Holdout (`IDs 129–160`)** | Pre | `124.21 [123.74, 124.67]` | `132.52 [132.22, 132.81]` | `146.71 [146.47, 146.95]` | **`+8.31`** (SE `.284`; **`[+7.73, +8.89]`**; `[+5.01, +11.53]`) | **`+14.20`** (SE `.176`; **`[+13.84, +14.56]`**; `[+11.99, +17.01]`) | `32 / 32` |
| | Post | `134.76 [134.35, 135.18]` | `137.06 [136.78, 137.34]` | `146.43 [146.19, 146.68]` | **`+2.30`** (SE `.257`; **`[+1.77, +2.82]`**; `[+0.02, +5.66]`) | **`+9.37`** (SE `.173`; **`[+9.02, +9.72]`**; `[+7.31, +12.15]`) | **`32 / 32`** |

All pooled 95% lower bounds on `College − HS` (`+2.13` training, `+1.77` holdout) and `Pro − College` (`+8.82` training, `+9.02` holdout) are strictly positive ($p < 10^{-8}$), and `32/32` holdout seeds preserve strict $\text{HS} < \text{College} < \text{Pro}$ ordering after the unpromoted development pass. The 20-season time-averaged equilibrium with tier promotions/draft/graduation belongs to slice 13-6 (`PipelineHarness`, D7).

---

## 5. Career-Wiring Avatar PA Bands (§5.4 / §10.3 Q7)

In `RunCareerWiringSuite` (`Tools/MonteCarloHarness/Program.cs:1335–1428`), a healthy position-player avatar (`BatPower = 60, BatContact = 60, BatDiscipline = 60`) is created on `ProTeam(3)` (`team_id = 303`) and plays all scheduled games of the season via `CareerManager`'s autopilot `MicroGame` loop (`0` days absent). Because `CreateAvatar` inserts no `Player_Fielding` row, `LineupBuilder` places the avatar at DH and sorts the 9 chosen starters by `(team_id, player_id)` ordinal order; varying the career RNG seed varies the avatar's GUID across `[0000..., ffff...]` and therefore samples all 9 batting-order positions (`1..9`) as well as schedule and game-outcome variance.

We measured the exact fixture seed plus a 32-seed sweep per tier (`100%` availability, all `10 / 28 / 64` scheduled games played and attended):

| Tier | Season Length / Innings | Scheduled / Played / Attended Games | Exact Default Fixture PA (`MicroSeed + 9`) | 32-Seed Mean PA (SD) | 32-Seed Observed `[Min, Max]` | §10.3 Q7 Pilot Envelope | **Signed Acceptance Band** | Rationale |
| :-- | :--: | :--: | --: | :--: | :--: | :--: | :--: | :-- |
| **HS** | 10 games (7 inn) | `10 / 10 / 10` | **38 PA** | `38.56` (`2.91`) | `[32, 43]` | `22–41` *(3/32 seeds hit 42–43)* | **`24–48`** | Higher HS OBP (`.389`) yields `3.86 PA/game` in 7 innings; `41` was too tight at the top of the order. |
| **College** | 28 games (9 inn) | `28 / 28 / 28` | **129 PA** | `132.09` (`8.18`) | `[116, 149]` | `82–147` *(1/32 seed hit 149)* | **`90–160`** | Higher College OBP (`.374`) yields `4.72 PA/game`; `147` clipped a leadoff season (`149 PA`). |
| **Pro (`career-wiring`)** | 64 games (9 inn) | `64 / 64 / 64` | **294 PA** (`AVG .307`; day-149 game-25 check = `114 PA > 50`) | `278.66` (`17.26`) | `[258, 319]` | `188–335` *(32/32 inside)* | **`188–335`** | Installed `188–335` envelope in `Program.cs:1427` covers the exact fixture (`294 PA`) and all 32 seeds (`[258, 319]`) with generous margin across all 9 lineup slots; signed as installed (no code edit needed). |

---

## 6. Runtime Verification Against D10

- **Hardware / Environment:** Owner's Windows 11 PC (Intel Pentium Silver N5030 @ 1.10 GHz, .NET SDK 8.0.422).
- **Pre-13-3 Base Default MC Wall Time:** `183.69 s` (Astra §10.1) / `124 s` (Opus §8.1).
- **D10 Budget:** `≤ baseline + 30 s`, with College's added share `≤ 15 s`.
- **13-3 Measurements on `7693b7d`:**
  - **Tier Timing Pilot (`-- --tier-pilot`, 4 HS + 4 College + 2 Pro seasons):**
    - World setup: `691 ms` (`0.69 s`)
    - Per-season: HS `216 ms`, College `472 ms`, Pro `646 ms`
    - Predicted 64/16/8 addition: $64(0.216) + 16(0.472) + 8(0.646) = \mathbf{26.6\text{ s}}$ ($+0.7\text{ s}$ setup $= \mathbf{27.3\text{ s} \le 30.0\text{ s}}$ ✓); College share $16(0.472) = \mathbf{7.6\text{ s} \le 15.0\text{ s}}$ ✓.
  - **Actual 64/16/8 Pooled Execution Time (measured inside `MathProbe`):**
    - HS (64 seasons): `11.19 s` (`174.9 ms/season`)
    - College (16 seasons): `6.76 s` (`422.8 ms/season` $\le 15\text{ s}$ ✓)
    - Pro (8 seasons): `3.81 s` (`476.3 ms/season`)
    - Total pooled simulation time: **`21.77 s` ($\le 30\text{ s}$ ✓)**.
  - **Full Default `MonteCarloHarness` Wall Time (`dotnet run --project Tools/MonteCarloHarness -c Release --no-build`):**
    - **`65.24 s` end-to-end** (`402/410` checks; Opus measured `90 s` including build check). Replacing the interim 154-day daily round-robins (`1,848` College and `2,464` Pro games/season in multi-tier suites) with the real 10/28/64-game calendars reduced total default MC wall time by **`58.8 s` to `118.5 s` below baseline**, even after adding the 17,408-game pooled band suite.

---

## 7. M3 Pro Guard 20/20 Reproduction, D10 Re-Time, and M2 Reduced Holdout Execution (`a2bf0c4`)

### 7.1 M3 — 20/20 Default `MonteCarloHarness` Replay & Pro Guard Signature (D6, D10)

On head `a2bf0c4` (after Opus applied D5 Option (i) Pro `R/9` `4.1–4.8`, M4 `PracticeFracPerHour = 0.001`, M5 Finding 1 `practice-seam` reorder, R1 `20 · cap / rate = 5,000 h` asymptote probe, and R6 `.starters.r2.unit` cleanup), we first verified `-- --only resolver` (`13/13`, LF SHA-256 `6d92422863f22d73c3539306a2a2442a65bcdedf505ddc5bb24611782e9eda89`, `0` `%TEMP%` `dnd_*` files) and then executed `dotnet run --project Tools/MonteCarloHarness -c Release --no-build` **20 times** sequentially, saving `run_01.log`–`run_20.log`:

| Run | Wall Time (`s`) | Score | `tier-ladder` | 7 Expected Reds (`3/4, 0/1, 0/1, 0/1, 3/4, 10/11, 0/1`) | All 8 Alloc Lines `0 B` | Pro Guard (`PA 81433 / H 18471 / ER 8803` + Ledger `932ACE26…`) | `ms` Lines Normalized | `%TEMP%` `dnd_*` | `C:` Free (`GB`) | Normalized LF SHA-256 (`\d+(\.\d+)? ms` $\to$ `N ms`) |
| :--: | --: | :--: | :--: | :--: | :--: | :--: | :--: | :--: | --: | :-- |
| **01** | `104.30` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.34` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **02** | `89.96` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.34` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **03** | `55.79` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.34` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **04** | `69.82` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.34` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **05** | `62.06` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.34` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **06** | `67.26` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.37` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **07** | `99.67` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.36` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **08** | `59.79` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.36` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **09** | `63.45` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.36` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **10** | `70.19` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.36` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **11** | `56.61` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.36` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **12** | `66.94` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.36` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **13** | `67.76` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.36` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **14** | `114.05` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.35` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **15** | `120.10` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.36` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **16** | `132.51` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.36` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **17** | `114.16` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.35` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **18** | `108.77` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.35` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **19** | `135.53` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.35` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |
| **20** | `125.70` | `408/415` | `14/14` | Exact | Yes | Exact | `3` | `0` | `8.36` | `7430347AA53EB5CCCB1DD7B3D53C2D4B645A85628F9515B99D1F1E968CE4B239` |

- **D10 Re-Time Summary (`≤ 183.7 s + 30 s = 213.7 s` budget):** `min = 55.79 s`, `median = 80.08 s`, `mean = 89.22 s`, `max = 135.53 s` (all 20/20 runs `≤ 213.7 s` ✓).
- **M3 Guard Signature:** **SIGNED as reproduced 20/20 byte-identical by Gemini 4 Argon** (`PA 81433 / H 18471 / ER 8803`, ledger SHA-256 `932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`), ready for the owner's sanction.

### 7.2 M2 — Approved Reduced Holdout Execution (`--college-analysis rates --reduced`)

Executed once on `a2bf0c4` (`exit 0`, total wall time **`6.11 min` / `366.2 s` ≤ `20 min` hard stop**; `0` leaked `dnd_*` files; `C:` free `8.33 GB`):

#### Batch 1: Gated Flat-World Holdout (`worlds 129–160 × scheduler 1–4 × outcome 1–4 = 512 seasons`, `172,032` games, `344,064 GS`, `spread 0`, `279.5 s` / `546 ms/season`)

- **Counting totals:** `PA = 14,745,498`, `R = ER = 2,133,297`, `outs = 9,237,273`, `GS = 344,064`.
- **Reported denominators:** `ER/GS = 6.2003 [6.1871, 6.2135]` (`2,133,297 / 344,064 = 6.200291`), `outs/GS = 26.8475 [26.8352, 26.8599]` (`9,237,273 / 344,064 = 26.847543`).

| Metric | Target Band | Pooled Estimate | Exact Flat-34 Markov (§2.3) | World-Clustered ($k=32, \text{df}=31$) SE | World-Clustered 95% CI (CF / Exact-$t$) | Tuple-Clustered ($k=512, \text{df}=511$) SE | Tuple-Clustered 95% CI (CF / Exact-$t$) | Verdict |
| :-- | :--: | --: | --: | --: | :-- | --: | :-- | :--: |
| **AVG** | `.284–.304` | **0.2938** | `.2938` | `0.00013` | `[0.2936, 0.2941]` | `0.00012` | `[0.2936, 0.2940]` | **PASS** |
| **OBP** | `.364–.384` | **0.3736** | `.3736` | `0.00013` | `[0.3733, 0.3738]` | `0.00012` | `[0.3733, 0.3738]` | **PASS** |
| **SLG** | `.473–.503` | **0.4876** | `.4877` | `0.00029` | `[0.4870, 0.4882]` | `0.00026` | `[0.4871, 0.4881]` | **PASS** |
| **K%** | `.184–.214` | **0.1985** | `.1985` | `0.00011` | `[0.1983, 0.1987]` | `0.00011` | `[0.1983, 0.1987]` | **PASS** |
| **BB%** | `.103–.123` | **0.1129** | `.1129` | `0.00010` | `[0.1127, 0.1131]` | `0.00008` | `[0.1128, 0.1131]` | **PASS** |
| **HR/PA** | `.033–.041` | **0.0368** | `.0368` | `0.00006` | `[0.0367, 0.0369]` | `0.00005` | `[0.0367, 0.0369]` | **PASS** |
| **R/9** (gated) | `5.80–7.10` | **6.2355** (`6.235535`) | `6.2339` | `0.00679` | CF `[6.2216, 6.2494]` / **Exact-$t$ `[6.2217, 6.2494]`** (`[6.221686, 6.249383]`) | `0.00657` | CF `[6.2226, 6.2484]` / **Exact-$t$ `[6.2226, 6.2484]`** (`[6.222627, 6.248442]`) | **PASS** |

#### Batch 2: Spread-25 Diagnostic (`worlds 129–160 × scheduler 1–2 × outcome 1–2 = 128 seasons`, `43,008` games, `86,016 GS`, `spread 25`, `86.7 s` / `678 ms/season`, `[DIAG]`, Not Gated)

- **Counting totals:** `PA = 3,643,257`, `R = ER = 502,292`, `outs = 2,311,620`, `GS = 86,016`.
- **Reported denominators:** `ER/GS = 5.8395 [5.7894, 5.8896]` (`502,292 / 86,016 = 5.839518`), `outs/GS = 26.8743 [26.8487, 26.8999]` (`2,311,620 / 86,016 = 26.874302`).

| Metric | Reference Flat Band | Pooled Estimate | World-Clustered ($k=32, \text{df}=31$) SE | World-Clustered 95% CI | Tuple-Clustered ($k=128, \text{df}=127$) SE | Tuple-Clustered 95% CI | Mode |
| :-- | :--: | --: | --: | :-- | --: | :-- | :--: |
| **AVG** | `.284–.304` | **0.2889** | `0.00054` | `[0.2878, 0.2900]` | `0.00033` | `[0.2883, 0.2896]` | `[DIAG]` |
| **OBP** | `.364–.384` | **0.3655** | `0.00050` | `[0.3645, 0.3665]` | `0.00032` | `[0.3649, 0.3661]` | `[DIAG]` |
| **SLG** | `.473–.503` | **0.4746** | `0.00097` | `[0.4727, 0.4766]` | `0.00064` | `[0.4734, 0.4759]` | `[DIAG]` |
| **K%** | `.184–.214` | **0.2082** | `0.00041` | `[0.2073, 0.2090]` | `0.00028` | `[0.2076, 0.2087]` | `[DIAG]` |
| **BB%** | `.103–.123` | **0.1077** | `0.00029` | `[0.1071, 0.1083]` | `0.00020` | `[0.1073, 0.1081]` | `[DIAG]` |
| **HR/PA** | `.033–.041` | **0.0350** | `0.00014` | `[0.0347, 0.0353]` | `0.00011` | `[0.0348, 0.0352]` | `[DIAG]` |
| **R/9** | `5.80–7.10` | **5.8668** (`5.866756`) | `0.02450` | CF `[5.8169, 5.9168]` / Exact-$t$ `[5.8168, 5.9167]` | `0.01647` | CF `[5.8342, 5.8994]` / Exact-$t$ `[5.8342, 5.8993]` | `[DIAG]` |

