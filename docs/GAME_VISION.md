# Dirt & Diamonds — Game Vision (v2)

**Status:** canonical. **v2.1, 2026-10-13**, owner-directed. v2.1 changes one thing, recorded in §3 and §5: **attended games are true arcade physics** (owner, 2026-10-13). Otherwise it's v2 (2026-10-10), which replaced v1 (2026-10-09). This is the single source of truth for *what the game is*. If any other doc disagrees with it, this one wins, and the other doc is wrong and gets fixed. A change of direction is made **here first**, then flows to the specs.

**Read next:** [BUILD_PLAN.md](BUILD_PLAN.md) (how we get there), [TEAM.md](TEAM.md) (who does what), [progress.md](progress.md) (where we are), [README.md](README.md) (the docs map). Detailed specs are linked from each section.

---

## 1. The pitch

**Raise a ballplayer from a high-school field to a pro stadium, survive everything that happens off the field, and find out who you really are.**

*Dirt & Diamonds* is a premium PC game for **Steam** (Windows + Linux). It combines a deep, calibrated baseball simulation with a gritty life sim. You live your player's days hour by hour: school, practice, games, work, sleep. Money, relationships, stress and temptation pull at you: hustles, gambling debt, a syndicate that wants a favor, affairs, steroids, painkillers, the bottle. Every game is called by **your team's own play-by-play announcer**. Story moments, achievements and big plays land as **motion-comic cutscenes**. The people in your life reach you through **your phone**, by text.

You grew up poor, and you don't know you were adopted. Your real father is **the greatest player who ever lived**. He gave you away to protect his name. The day you sign your first Pro contract, an unknown number texts you: *"You'll never touch his record."* He's talking about himself.

When your career ends, a willing child can pick up the bat, and the family carries on. When the bloodline runs out, the game is over.

## 2. Start to finish: one family's story

| Stage | What happens | Systems |
| :-- | :-- | :-- |
| **New Career** | Choose name, **gender**, **ethnicity**, **position** (SP, RP, C, 1B, 2B, 3B, SS, LF, CF, RF), **dating preference**, and your ratings budget. A legend matching your ethnicity becomes the GOAT at your position: your secret father. | §6 |
| **Prologue** | A captions-only motion comic: a childhood in poverty. No hint of the truth. | §6, §10 |
| **High School** (Freshman → Senior, from age 16) | Tryouts, the varsity team, school and GPA, the household, a first job, a first hustle, dating. **A woman's route begins here**: she's only the 3rd girl in the nation to make an all-male team, and her pro dream is a joke to everyone else. | §4, §9 |
| **Recruiting → Signing Day** | Offers arrive by phone; you pick a college (a walk-on offer guarantees one). | §4 |
| **College** (class years 1–4) | Bigger stages, the National Ranking, adult temptations (18+). In the **Trailblazer's senior year**, the joke becomes a real possibility. | §4, §7, §8, §9 |
| **The Draft / first Pro contract** | Drafted or signed as a free agent. **The father's first text arrives**: congratulations, a needle about his record, and "I'm pulling for you." For the Trailblazer, this is the day the first woman ever signs a pro contract. | §6, §9 |
| **The Pro career** | 64-game seasons, the Sunday roster lock, the record chase against your father's mark, money, fame, marriage, children, affairs, substances, scandals, injuries. | §4, §7, §8 |
| **The reveal** | Win the **Pro World Championship**, or **retire** (or die): whichever comes first, you learn the truth. | §6 |
| **Career end** | Retirement, a lifetime ban, or death. | §4, §8 |
| **Succession** | Any willing child aged 19+ (born, adopted in, or reclaimed) becomes the new player character. With no willing heir, the lineage is over and the game ends. | §7 |
| **The next generation** | Your heir lives their own career. Secrets you left behind (a child you placed for adoption) can step into the batter's box against them. | §7 |

## 3. Design pillars

1. **The Pipeline.** Progression is strictly High School → College → the Draft → Pro. No minor leagues. Every rung is a real league with real rules, standings and playoffs.
2. **Play it for real.** Your team's games are a **real-time arcade ballgame with true physics**, in a 16-bit style (owner, 2026-10-13). Bat contact, pitch and ball flight, fielding and baserunning are decided by physics and your hands, not by dice tables. **Ratings shape the physics:** they set what your player *can* do (bat speed, pitch speed and break, reach, arm, foot speed), and your timing and aim decide what *does* happen. You control your own player, or flip a toggle to take over the whole team. You can watch the entire game and skip ahead by pitch, at-bat, half-inning, inning, "my next moment" or to the final. Every team has its own ballpark or campus, and its real dimensions shape how it plays. Spec: [design/arcade_baseball_overhaul.md](design/arcade_baseball_overhaul.md), under the Phase G umbrella [design/gameplay_core.md](design/gameplay_core.md). This is a **high priority**.
3. **The Dirt.** Life pushes back. Needs decay, rent comes due, and the easy money is illegal. Crime and vice are real systems with real teeth: arrest, injury, suspension, permanent bans, death.
4. **Every edge has a price.** Affairs, steroids and painkillers really do make you better, dramatically and immediately. Every one of them also has a cost that's real, that escalates, and that's different for each vice.
5. **The Secret.** Lineage is a mystery you live inside: your hidden father, and the hidden children you may leave behind. The truth surfaces through play, not menus.
6. **The Broadcast.** Baseball should *sound* like baseball. Every College and Pro team has a signature announcer with taglines; HS games get local-radio voices.
7. **The Phone.** Story arrives the way it does in real life: threaded texts from coaches, partners, family, an unknown number and shady contacts. Your choices are replies. The press reaches you there too.
8. **The Legacy.** Genetics, upbringing and baseball interest pass to heirs. The dynasty is the long game.
9. **Simulation honesty.** Behind the presentation the numbers are calibrated: Pro ball plays to real MLB norms, and every system is proven by headless test harnesses. That includes the arcade engine: AI-vs-AI physics games must land in the same per-tier bands as the background sim, so your team's numbers compare fairly with the league's.

## 4. The league universe

Full rules: [design/league_restructure.md](design/league_restructure.md). The owner's source spec is reproduced in [BUILD_PLAN.md](BUILD_PLAN.md) (Phase 13 Source Spec).

| | High School | College | Pro |
| :-- | :-- | :-- | :-- |
| **Teams** | 12 | 24 | 32 |
| **Structure** | 2 conferences × 6 | 2 conferences × 3 divisions × 4 | 2 conferences × 4 divisions × 4 |
| **Game length** | 7 innings | 9 innings | 9 innings |
| **Schedule** | 10 games, 1 a week (Fridays) | 14 games, 1 a week (Saturdays) | 64 games, ~3 a week (Tue/Thu/Sat) |
| **Rosters** | Wide open | Pro limits + 2 extra active spots | Strict: 4 SP, 5 RP, 15 position players (active/inactive) |
| **Playoffs** | Top 3 per conference, single elimination, #1 seeds get a bye | 18-team tournament (17 by National Ranking + 1 play-in), conference tournaments → single-game National Championship | Best-of-3 rounds → Best-of-5 Championship |
| **Exit** | Graduate → recruiting | Draft-eligible after sophomore year | Retirement, a ban or death → succession |

Pro-only rules: the **Sunday roster lock** (designations fixed for the week) and the **attrition & forfeit rule** (absent players are never replaced; a team that can't field 9 position players plus a pitcher forfeits).

## 5. The player's loop

- **Daily:** split 24 hours across Sleep / School / Practice / Game / Work / Free, then watch the real-time clock run. Pause, slow it down or speed it up any time.
- **Weekly:** HS and College play one game a week, and the week builds toward it. Pro plays about three a week and sets rosters on Sunday night.
- **Game day:** you arrive at **your team's own venue** (an interactive stadium or school screen with its own art: field, clubhouse, coach's office, training room, stands). Then you **play the game** on the 16-bit arcade field, called live by your announcer:
  - **Batting:** time the swing and place the contact cursor. Contact sets the cursor's size, power sets the bat speed, and discipline sets how early you read the pitch.
  - **Pitching:** pick the pitch, aim it, hit the release meter, and steer the break. Control sets the meter and scatter, stuff and the arsenal set speed and movement, and stamina sets how long it all holds.
  - **Fielding and running (assisted):** your fielder breaks on the ball; you steer, dive, catch and choose the throw.
  - **Take over the team** at any time, or **watch and skip** the parts you don't play.
  - Every other game in the league simulates in the background from ratings alone.
- **Attributes:** every rating, person stat and need has a defined effect and size. On the arcade field, ratings become physical capabilities, and skill decides the result ([design/arcade_baseball_overhaul.md](design/arcade_baseball_overhaul.md) §4). In background games they drive the calibrated macro sim ([design/gameplay_core.md](design/gameplay_core.md) §1). Difficulty presets tune only the human's assists.
- **Off the field:** hustles (Legal Work, Narcotics, Fencing, Robbery, Texas Hold'em), the household, school, dating, marriage, children, temptation, substances. All of it feeds needs, money, stress, person stats and narrative flags.
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
- **Reclaiming a child:** once found, you choose whether to tell them. A told child becomes family (officially at 18). Reclaiming unfreezes the missing piece.
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
  2. **The possibility** (college senior year): scouts' notebooks come out, and the opposition gets organized.
  3. **The first** (her first Pro contract).
- She gets her own press articles, broadcaster calls, sponsorships and harassment. **She's the GOAT's daughter**, and he knows exactly who signed.

## 10. Presentation & audio

- **Complete visual redesign** ([design/visual_redesign.md](design/visual_redesign.md)). The shipped UI is a functional thin slice, not the shipping look.
  - What carries over is the *information architecture*: a baseball side (stats, scouting, standings, calendar, the at-bat) and a street side (the phone).
  - The UI is read-only over sim state.
- **Voice is broadcasters only** ([design/voice_and_broadcast_audio.md](design/voice_and_broadcast_audio.md)).
  - **What's voiced:** ElevenLabs play-by-play, pre-generated offline. That's 56 signature announcers (one per College and Pro team) and 3 shared HS radio voices, as sound bites only, plus booth calls used as cutscene audio beds.
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
- **No spoken player names, and no avatar pronouns in pre-recorded calls.**
- **Presentation never drives simulation.** Announcers, cutscenes, the phone and press consume events and never write state. The baseball sim stays deterministic and calibrated: the arcade engine is seeded and deterministic too, so a game you skip through is exactly the game you would have watched.
- **Gender, ethnicity and listed position never touch the sim.** Your on-field position is a separate baseball record (`Player_Fielding`), seeded from your New Career pick. The sim reads only that.
- **Adults-only rewards.** Temptation and substance boosts require 18+ and College or above.
- **Nothing sexual is explicit at any age.** It's always off-screen and implied.
- **Never abortion.** Every pregnancy ends in a birth, and adoption is always offered.
- **Secrecy.** Nothing in the UI hints at the father or a hidden child before the reveal. Dating pools silently exclude blood relatives.
- **Pre-1.0 saves are disposable.** Schema v14 is a clean break. From 1.0 on, every schema change ships a migration.
- **English only at launch**, with all player-facing text in strings tables so localization stays possible.

## 13. Built vs next (as of 2026-10-10)

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
  - **Attended games still run on the dice-table micro sim** with the "Read the Pitch" batting view. A pitcher player can't pitch yet, and nobody fields. The arcade engine replaces all of that in Phase G.
  - **Needs and stress never touch the field**, and only 3 of the 12 person stats do.
  - There are no venue screens. Only 2 HS fields have art.
  - The "place for adoption" choice doesn't create a child yet.
- **Next:** Phases 13–17, plus **Phase G** (gameplay core, **high priority**) and **Phase L** (lineage, temptation and vice), in [BUILD_PLAN.md](BUILD_PLAN.md).
