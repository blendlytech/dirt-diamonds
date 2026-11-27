# Dirt & Diamonds — Game Vision (v3.0)

**Status:** canonical. **v3.0, 2026-10-25**, owner-directed scope realignment ([design/scope_realignment.md](design/scope_realignment.md)): (1) the Markov `MicroGame` is the authoritative attended game and the arcade engine shrinks to an optional Clutch Moment; (2) daily needs and the hourly schedule become a **Weekly Lifestyle Stance**; (3) interactive hustles drop to **Narcotics and Texas Hold'em**, with Fencing and Robbery as Burner Phone events; (4) **6 announcer voices**, not 59. Where v2.x text below disagrees with this paragraph, this paragraph wins. v2.2 (2026-10-16) narrowed control, recorded in §3 and §5: **you play only your own player**, and taking over the team is deferred past 1.0 (owner, 2026-10-16). v2.1 (2026-10-13) made **attended games true arcade physics**. Otherwise it's v2 (2026-10-10), which replaced v1 (2026-10-09). This is the single source of truth for *what the game is*. If any other doc disagrees with it, this one wins, and the other doc is wrong and gets fixed. A change of direction is made **here first**, then flows to the specs.

**Read next:** [BUILD_PLAN.md](BUILD_PLAN.md) (how we get there), [TEAM.md](TEAM.md) (who does what), [progress.md](progress.md) (where we are), [README.md](README.md) (the docs map). Detailed specs are linked from each section.

---

## 1. The pitch

**Raise a ballplayer from a high-school field to a pro stadium, survive everything that happens off the field, and find out who you really are.**

*Dirt & Diamonds* is a premium PC game for **Steam** (Windows + Linux). It combines a deep, calibrated baseball simulation with a gritty life sim. You live your player's weeks: school, practice, games and work run on the calendar, and you choose a **lifestyle stance** for the week. Money, relationships, stress and temptation pull at you: hustles, gambling debt, a syndicate that wants a favor, affairs, steroids, painkillers, the bottle. Every game is called by **your league's own play-by-play announcers**. Story moments, achievements and big plays land as **motion-comic cutscenes**. The people in your life reach you through **your phone**, by text.

You grew up poor, and you don't know you were adopted. Your real father is **the greatest player who ever lived**. He gave you away to protect his name. The day you sign your first Pro contract, an unknown number texts you: *"You'll never touch his record."* He's talking about himself.

When your career ends, a willing child can pick up the bat, and the family carries on. When the bloodline runs out, the game is over.

## 2. Start to finish: one family's story

| Stage | What happens | Systems |
| :-- | :-- | :-- |
| **New Career** | Choose name, **gender**, **ethnicity**, **position** (SP, RP, C, 1B, 2B, 3B, SS, LF, CF, RF), **dating preference**, and your ratings budget. A legend matching your ethnicity becomes the GOAT at your position: your secret father. | §6 |
| **Prologue** | A captions-only motion comic: a childhood in poverty. No hint of the truth. | §6, §10 |
| **High School** (Freshman → Senior, from age 16) | Tryouts, the varsity team, school and GPA, the household, a first job, a first hustle, dating. **A woman's route begins here**: she's only the 3rd girl in the nation to make an all-male team, and her pro dream is a joke to everyone else. | §4, §9 |
| **Recruiting → Signing Day** | Offers arrive by phone; you pick a college (a walk-on offer guarantees one). | §4 |
| **College** (class years 1–4) | Bigger stages, the National Ranking, adult temptations (18+). Before the **Trailblazer's first eligible draft declaration** (senior-year fallback), the joke becomes a real possibility. | §4, §7, §8, §9 |
| **The Draft / first Pro contract** | Drafted or signed as a free agent. **The father's first text arrives**: congratulations, a needle about his record, and "I'm pulling for you." For the Trailblazer, this is the day the first woman ever signs a pro contract. | §6, §9 |
| **The Pro career** | 64-game seasons, the Sunday roster lock, the record chase against your father's mark, money, fame, marriage, children, affairs, substances, scandals, injuries. | §4, §7, §8 |
| **The reveal** | Win the **Pro World Championship**, or reach **career end** (retirement, permanent ban or death): whichever comes first, you learn the truth. | §6 |
| **Career end** | Retirement, a lifetime ban, or death. | §4, §8 |
| **Succession** | Any willing child (born, adopted in, or reclaimed) becomes the new player character: one aged 19+ when you retire, or, once you're released or retire with only younger children, one who takes over at 16 as an HS freshman after you've raised them in the parent phase (a job, backyard training, the household). With no willing heir, the lineage is over and the game ends. | §7 |
| **The next generation** | Your heir lives their own career. Secrets you left behind (a child you placed for adoption) can step into the batter's box against them. | §7 |

## 3. Design pillars

1. **The Pipeline.** Progression is strictly High School → College → the Draft → Pro. No minor leagues. Every rung is a real league with real rules, standings and playoffs.
2. **Play it for real (v3.0, owner 2026-10-25).** Your team's games use the broadcast at-bat sim: the Markov `MicroGame` resolves every plate appearance from ratings and intent. You control only your own player; team takeover remains outside 1.0. In high-leverage avatar at-bats an optional, offense-only **Clutch Moment** timing check supplies a capped, centered contact-quality tilt to the same chain. Real-time fielding, baserunning and full-game arcade physics are cancelled. Follow the game pitch by pitch or skip ahead; only revealed family relationships appear. Background games retain the calibrated macro sim. Governing spec: [design/scope_realignment.md](design/scope_realignment.md) §3.1; attribute contracts: [design/gameplay_core.md](reference/attribute_contracts.md) §1.
3. **The Dirt.** Life pushes back. Your weekly stance handles biological needs automatically; stress rises, upkeep comes due, and the easy money is illegal. Crime and vice are real systems with real teeth: arrest, injury, suspension, permanent bans, death.
4. **Every edge has a price.** Affairs, steroids and painkillers really do make you better, dramatically and immediately. Every one of them also has a cost that's real, that escalates, and that's different for each vice.
5. **The Secret.** Lineage is a mystery you live inside: your hidden father, and the hidden children you may leave behind. The truth surfaces through play, not menus.
6. **The Broadcast.** Baseball should *sound* like baseball. **Six deep announcer personas**, two per tier (HS, College, Pro), one booth per conference, with taglines and a large catalog of situational calls that react to recklessness, rivalry, scandal, streaks and dynasty.
7. **The Phone.** Story arrives the way it does in real life: threaded texts from coaches, partners, family, an unknown number and shady contacts. Your choices are replies. The press reaches you there too.
8. **The Legacy.** Genetics, upbringing and baseball interest pass to heirs. The dynasty is the long game.
9. **Simulation honesty.** Behind the presentation the numbers are calibrated: Pro ball plays to real MLB norms, and every system is proven by headless test harnesses. The Clutch Moment tilt is capped and centered, so a player who skips it is never worse than the ratings line and the per-tier bands hold.

## 4. The league universe

Full rules: [design/league_restructure.md](design/league_restructure.md). The owner's source spec is reproduced in [BUILD_PLAN.md](BUILD_PLAN.md) (Phase 13 Source Spec).

| | High School | College | Pro |
| :-- | :-- | :-- | :-- |
| **Teams** | 12 | 24 | 32 |
| **Structure** | 2 conferences × 6 | 2 conferences × 3 divisions × 4 | 2 conferences × 4 divisions × 4 |
| **Game length** | 7 innings | 9 innings | 9 innings |
| **Schedule** | 10 games, 1 a week (Fridays) | 28 games, 2 a week (Wednesday + Saturday) over 14 weeks | 64 games, ~3 a week (Tue/Thu/Sat) |
| **Rosters** | Wide open | Pro limits + 2 extra active spots | Strict: 4 SP, 5 RP, 15 position players (active/inactive) |
| **Playoffs** | Top 3 per conference, single elimination, #1 seeds get a bye | 18-team tournament (17 by National Ranking + 1 play-in), single-elimination conference tournaments → **The Red Dirt Series** (pending trademark screen): best-of-3 between the two conference champions on consecutive days, game 3 only if needed | Best-of-3 rounds → Best-of-5 Championship |
| **Exit** | Graduate → recruiting | Draft-eligible after sophomore year | Retirement, a ban or death → succession |

Pro-only rules: the **Sunday roster lock** (designations fixed for the week) and the **attrition & forfeit rule** (absent players are never replaced; a team that can't field 9 position players plus a pitcher forfeits).

## 5. The player's loop

- **Weekly (v3.0):** choose a **Lifestyle Stance** (Clean Living, Grind Mode, Party Lifestyle, Low Profile). School, practice, games and work run on the tier calendar, needs are handled automatically, and upkeep is settled against your funds and wealth tier. The week advances until something needs you: a **stress spike**, **funds at or below zero**, a **gritty event**, or the game.
- **Weekly:** HS plays one game a week (Friday), and the week builds toward it. College plays two (Wednesday and Saturday). Pro plays about three a week and sets rosters on Sunday night.
- **Game day:** you arrive at **your team's venue**, then you **play the game** in the broadcast at-bat view, called live by your conference's booth:
  - **Batting:** set your approach and read the pitch; your ratings and your read decide the result. In a high-leverage at-bat, an optional **Clutch Moment** (time the swing, place the contact cursor) decides contact quality.
  - **Pitching:** pitch selection and location intent, resolved by the same at-bat chain. A full pitching-intent mode is scoped after the first external playtest (scope_realignment D-4).
  - **Fielding and running:** automatic, from your ratings. The real-time fielding and baserunning engine is cancelled (owner, 2026-10-25).
  - **Batting practice:** an optional unscored timing mini-game on the same physics core. It writes nothing.
  - **Everything else you watch:** follow the game pitch by pitch, including family members' at-bats in the same game, or **skip** ahead. You play only your own player's moments (owner, 2026-10-16; taking over the team is deferred past 1.0).
  - Every other game in the league simulates in the background from ratings alone.
- **Attributes:** every rating, person stat and need has a defined effect and size. They drive the calibrated sims ([design/gameplay_core.md](reference/attribute_contracts.md) §1), and shape the Clutch Moment's physics ([design/arcade_baseball_overhaul.md](archive/arcade_baseball_overhaul.md) §4, batter side only).
- **Off the field:** hustles (Legal Work; **Narcotics** and **Texas Hold'em** as interactive games; **Fencing** and **Robbery** as high-stakes Burner Phone jobs with hidden odds), the household, school, dating, marriage, children, temptation, substances. All of it feeds needs, money, stress, person stats and narrative flags. Your stance is visible to events as a flag.
- **The phone:** threads from every person in your life, the Events feed, the bank, the marketplace, the calendar, and a **News** tab of press clippings.
- **Seasonal:** playoffs, then recruiting (HS seniors) or the Draft (College), then development and aging, then the next season.

## 6. The lineage: the GOAT and the secret

Full design: [design/temptation_and_lineage.md](design/temptation_and_lineage.md) §3–§4. Opening slice: [tasks/16-1_opening_sequence_brief.md](tasks/16-1_opening_sequence_brief.md).

- **The legends.** A roster of fictional all-time greats with full backstories. Positions are assigned per career, not fixed: the father is a legend **matching your ethnicity**, placed as the GOAT **at your position**, and the others fill the remaining positions in the record book.
- **His record** is the unbreakable mark at your position, chosen so you can actually chase it: SP strikeouts, RP saves, C walks, 1B home runs, 2B hits, 3B RBIs, SS doubles, LF slugging, CF triples, RF total bases. Values are scaled to the 64-game Pro season.
- **The asterisk.** In **half of all careers** (rolled once, hidden), his record was juiced. The press may start asking about that era during your chase.
- **The timeline:**
  1. Silence through high school and college.
  2. The **first text** on your first Pro contract.
  3. A relationship by text that needles, then grows proud, as you close in on his record.
  4. The **reveal** on a World Championship or at career end.
- **You were adopted.** Your household is your adoptive family. Poverty is guaranteed: every career starts in the bottom two wealth tiers.

## 7. Family, temptation and consequences

Full design: [design/temptation_and_lineage.md](design/temptation_and_lineage.md) §5–§7.

- **Dating either gender.** Partners are people with their own stored gender. Marriage, divorce and rekindling all exist.
- **Pregnancy:**
  - **Planned** pregnancies follow marriage. **Unplanned** ones come from dating, flings and affairs.
  - Conception needs a male–female pair. Any couple can **adopt**.
  - A woman avatar carries the pregnancy herself, with **maternity** time away from the game.
  - **There is never an abortion option.** Every pregnancy ends in a birth, and every child can be **kept or placed for adoption**.
- **Adopted-in children** are full family and can inherit.
- **Placing a child costs you "the missing piece."** One of your seven ratings, picked at random from the least significant to the most, **freezes exactly where it is** until you reclaim that child.
- **Hidden children live on in the world.** They grow up, and many play ball. They're revealed only:
  - by **crossing paths**: your line meets theirs as **opponents**, or as **teammates**, which has its own slow-burn reveal; or
  - by a **paid search**: it can't start until the child is 19; it costs a lot of money; it takes a random 1 week to a full season; and its stress drags your ratings down until it ends.
- **Reclaiming a child:** once found, you choose whether to tell them. A told child becomes family (officially at 18). Official reclaim at age 18+ unfreezes the missing piece; telling a younger child begins the relationship but does not unlock it.
- **Affairs:** each one gives **confidence at 100% plus a clutch boost** in high-pressure at-bats, and a small permanent ratings gain. The chance of getting **caught** and the chance of a **pregnancy** rise with **every** affair, and every roll is random. Discovery brings divorce, scandal, child support and a damaged clubhouse.
- **The mirror.** You can become your father. The systems are built so that story can repeat.
- **Succession:** any willing child aged 19+, of any gender (born, adopted in, or reclaimed), can take over.

## 8. Substances

Full design: [design/substances_and_addiction.md](design/substances_and_addiction.md).

| Substance | The edge | The price |
| :-- | :-- | :-- |
| **Steroids** | Power ×1.5 (hitters), stamina ×1.5 (pitchers) | Body breakdown, tests and suspensions, roid rage, lower fertility, a permanent **asterisk** on records |
| **Amphetamines** | Focus: contact, discipline, control; never tired | Wrecked sleep, crash days, heart events |
| **Cocaine** | Invincible: slumps and pressure can't touch you | The costliest habit, turning into dealer debt; paranoia; arrests; overdose |
| **Painkillers** | Never miss a game through injury | Hidden damage that surfaces as a severe injury; the fastest addiction; overdose |
| **Alcohol** | Stress relief, social life, liquid courage | Hangovers, DUIs, and damage at home that lowers your children's development |

- One shared addiction engine runs all five: dependence, tolerance (the high shortens), cravings, withdrawal, rehab and relapse.
- **An overdose can be fatal.** Death ends the career on the spot, with a posthumous reveal, then succession.
- **Boosts are adults-only** (18+, College and up). High school gets the story and the chance to say no.

## 9. The Trailblazer (a woman's route)

Full design: [design/temptation_and_lineage.md](design/temptation_and_lineage.md) §8.

- **Gender never touches the simulation.** Her ability is her ratings. Her obstacles are social, and they hurt on the field through confidence and happiness, the same math everyone shares.
- **Her goal is to be the first woman ever to play pro.** The route happens **once per save**, belonging to whoever gets there first. If she retires short of the Pros, a later daughter inherits the unfinished dream. Once someone has done it, later daughters play the standard route, and the press remembers who broke the barrier.
- **Three acts:**
  1. **The joke** (HS through junior year of college): the 3rd girl in the nation to make an all-male team, and nobody takes the pro dream seriously.
  2. **The possibility** (before the first eligible draft declaration; senior-year fallback): scouts' notebooks come out, and the opposition gets organized.
  3. **The first** (her first Pro contract).
- She gets her own press articles, broadcaster calls, sponsorships and harassment. **The founder is the GOAT's daughter; later heirs are the actual descendants.** Contact and press copy follow saved genealogy rather than replaying the founder's identity.

## 10. Presentation & audio

- **Complete visual redesign** ([design/visual_redesign.md](design/visual_redesign.md)). The shipped UI is a functional thin slice, not the shipping look.
  - What carries over is the *information architecture*: a baseball side (stats, scouting, standings, calendar, the at-bat) and a street side (the phone).
  - The UI is read-only over sim state.
- **Voice is broadcasters only** ([design/voice_and_broadcast_audio.md](design/voice_and_broadcast_audio.md)).
  - **What's voiced:** ElevenLabs play-by-play, pre-generated offline. That's **6 announcer personas** (2 per tier, one booth per conference) with about 10x the situational lines per voice, as sound bites only, plus booth calls used as cutscene audio beds.
  - **What isn't:** there are **no character voices**, no phone voice notes and no cutscene voice-over. Characters speak in text, captions and subtitles.
  - **Calls never say "he" or "she" about the player**, because the player can be either.
- **Cutscenes** ([design/cutscenes_motion_comic.md](design/cutscenes_motion_comic.md)): in-engine motion comics with layered art, camera moves and captions, plus SFX, music and broadcaster calls. Live slots show your portrait, name, team and stats. Used for story moments (including the prologue, the reveals and the Trailblazer's firsts), milestones, achievements and big plays.
- **Press clippings:** templated articles delivered to the phone's News tab, with an exclusive set for the Trailblazer.
- **UI sound:** the shipped `UiSfx`, extended with Announcer, Music and Ambience buses.

## 11. Commercial release (Steam)

[design/steam_commercial_release.md](design/steam_commercial_release.md):

- A premium one-time purchase, with no microtransactions.
- Steam's technical integration shipped in Phase 11.
- The launch adds the store page, the AI-content disclosure (pre-generated art and announcer voices), and the mature-content survey: drugs and dealing, PEDs and substance abuse, simulated gambling, crime and violence, alcohol, sexual themes (implied, never explicit), adultery and pregnancy, overdose death, and strong language.
- It also adds licensing records, a trademark screen, release-candidate QA, and a save-compatibility policy from 1.0.

## 12. Firm constraints

- **No runtime AI generation.** Voice and art are generated during development, human-reviewed, and shipped as files. Keys never enter the repo or the build.
- **No real people, teams or broadcasters.** Everything is fictional. No voice clones or imitations.
- **No spoken player names, and no avatar pronouns in generic pre-recorded calls.** Route-specific Trailblazer calls may use gendered pronouns only when gated on that route (owner accepted R-1 Q16).
- **Presentation never drives simulation.** Announcers, cutscenes, the phone and press consume events and never write state. The baseball sim stays deterministic and calibrated: the Clutch Moment is seeded and deterministic too, and a skipped moment is neutral.
- **Gender, ethnicity and listed position never touch the sim.** Your on-field position is a separate baseball record (`Player_Fielding`), seeded from your New Career pick. The sim reads only that.
- **Adults-only rewards.** Temptation and substance boosts require 18+ and College or above.
- **Nothing sexual is explicit at any age.** It's always off-screen and implied.
- **Never abortion.** Every pregnancy ends in a birth, and adoption is always offered.
- **Secrecy.** Only `LineageQueries` reads `Biological_Parentage`. Nothing in the UI, phone, press or Tabloid Rumor Mill reveals or hints at the biological father or a hidden heir before their narrative unlock. Dating pools silently exclude blood relatives.
- **Pre-1.0 saves are disposable.** Schema v14 is a clean break. From 1.0 on, every schema change ships a migration.
- **English only at launch**, with all player-facing text in strings tables so localization stays possible.

## 13. Built vs next (as of 2026-10-25)

- **Built (Phases 0–12 and the post-12 arcs):**
  - database core, event bus, macro and micro baseball sims;
  - the life sim, relationships and succession, gritty events, economy and hustles;
  - the presentation shell and the Steam technical integration;
  - the High School person layer, onboarding, the real-time clock, and the criminal-underworld storyline.
  - Shipped specs live in [reference/](reference/).
- **Known differences between the shipped build and this vision:**
  - The **6-tier league** is rebuilt in Phase 13.
  - The **steroid multiplier** exists but has no trigger.
  - Avatar creation has **no gender, ethnicity or position**, and partners come from the league pool.
  - **Attended games run on the Markov micro sim** with the "Read the Pitch" batting view; that is now the shipping design (v3.0). A pitcher player can't pitch yet (scope_realignment D-4). The arcade core exists as the G-0 prototype and becomes the Clutch Moment.
  - **Needs and stress never touch the field**, and only 3 of the 12 person stats do.
  - There are no venue screens. Only 2 HS fields have art.
  - The "place for adoption" choice doesn't create a child yet.
- **Next:** the gate sequence in [design/scope_realignment.md](design/scope_realignment.md) §4 (Phase 13 core → the 15-minute Next Fest loop → golden art slice → external playtest), then Phases 13–17 and **Phase L** in [BUILD_PLAN.md](BUILD_PLAN.md). Phase G is reduced to the Clutch Moment.
