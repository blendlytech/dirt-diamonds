# Dirt & Diamonds — Game Vision

**Status:** canonical as of 2026-10-09. This file is the single source of truth for *what the game is*. It replaces `GAME_IDEA.md`, which older design docs cite but which is no longer in the repo. If any other doc disagrees with this one, this one wins, and the other doc gets a banner pointing here.

---

## 1. The pitch

**Raise a ballplayer from a high-school field to a pro stadium, and survive everything that happens off the field along the way.**

*Dirt & Diamonds* is a commercial PC game for **Steam** (Windows + Linux). It combines a deep baseball simulation with a gritty life sim. You live your player's days hour by hour: school, practice, games, work, sleep. Meanwhile money, relationships, stress and temptation pull at you: hustles, gambling debts, PEDs, a syndicate that wants a favor. Every game is called by **your team's own play-by-play announcer**. Story moments, achievements and big plays land as **animated motion-comic cutscenes**. The people in your life reach you through a **burner phone**, with texts and voice notes.

When your player's career ends, your **heir** can pick up the bat. The game is over when the bloodline runs out.

## 2. Design pillars

1. **The Pipeline.** Progression is strictly High School → College → the Draft → Pro. There are no minor leagues. Every rung is a real league with real rules, standings and playoffs (§3).
2. **The Dirt.** Life pushes back. Needs decay, rent comes due, and the easy money is illegal. Crime and vice are real systems with real teeth: arrest, injury, suspension, permanent bans.
3. **The Broadcast.** Baseball should *sound* like baseball. Every College and Pro team has a signature announcer with a personality and tagline calls. HS games get local-radio voices. Big plays explode into short cutscenes.
4. **The Phone.** Story arrives the way it does in real life: threaded texts and voice notes from coaches, girlfriends, family and shady contacts. Your choices are replies.
5. **The Legacy.** Generational play. Genetics, upbringing and baseball interest pass to heirs, and the dynasty is the long game.
6. **Simulation honesty.** Behind the presentation, the numbers are calibrated. Pro ball plays to real MLB norms, and every system is proven by headless test harnesses.

## 3. The league universe

The full rules live in [design/league_restructure.md](design/league_restructure.md). The user's source spec is reproduced in [BUILD_PLAN.md](BUILD_PLAN.md) under "Phase 13 Source Spec".

| | High School | College | Pro |
| :-- | :-- | :-- | :-- |
| **Teams** | 12 | 24 | 32 |
| **Structure** | 2 conferences × 6 | 2 conferences × 3 divisions × 4 | 2 conferences × 4 divisions × 4 |
| **Game length** | 7 innings | 9 innings | 9 innings |
| **Schedule** | 1 game / week | 1 game / week | 64 games, ~3 / week |
| **Rosters** | Wide open | Pro limits + 2 extra active spots | Strict: 4 SP, 5 RP, 15 position players (active/inactive) |
| **Playoffs** | Top 3 per conference, single elimination, #1 seeds get a bye | 18-team tournament (17 by National Ranking + 1 play-in winner), conference tournaments → single-game National Championship | Best-of-3 series → Best-of-5 Championship Series |
| **Exit** | Graduate → College recruiting | Draft-eligible after sophomore year | Career ends in retirement, a ban or death; then the heir |

Pro-only rules: the **Sunday roster lock** (active/inactive designations are fixed for the week) and the **attrition & forfeit rule** (injured players can't be replaced by inactive ones; a team that can't field 9 defenders plus a valid pitcher forfeits).

## 4. The player's loop

- **Daily:** use the Schedule screen to split 24 hours across Sleep / School / Practice / Game / Work / Free, then watch the real-time clock run. Pause, slow it down or speed it up whenever you like.
- **Weekly:** HS and College play one game a week, and that week builds toward it. Pro plays about three games a week and sets rosters on Sunday night.
- **Game day:** watched games run through the interactive at-bat view (timing + pitch read), called live by the team's announcer. All other games simulate in the background.
- **Off the field:** hustles (Legal Work, Narcotics, Fencing, Robbery, Texas Hold'em), the household, dating, marriage, kids and school grades all feed needs, money, stress and narrative flags.
- **Seasonal:** playoffs, then recruiting (HS seniors) or the draft (College sophomores and up), then development and aging, then the next season.

## 5. Presentation & audio

- **Complete visual redesign (user directive, 2026-10-09).** The shipped UI is a functional thin slice built from plain Godot controls. It is **not** the shipping look and may be replaced entirely. The new visual identity is designed from scratch in Phase 15 ([design/visual_redesign.md](design/visual_redesign.md)). What carries over is the *information architecture*, not the visuals: a baseball side (stats, scouting, standings, calendar, the at-bat) and a street side (the burner phone: contacts, messages, deals, funds). The UI also stays read-only over sim state (DTOs in, player-intent signals out).
- **Voice (ElevenLabs):** every voice line is **pre-generated offline** with ElevenLabs and shipped as audio files. The shipped game never calls an AI service at runtime. Three uses at launch:
  1. **Play-by-play announcers:** one signature announcer per College and Pro team (56 unique), plus 3 shared HS local-radio voices. They're sound bites only (calls, taglines, reactions), not live narration. See [design/voice_and_broadcast_audio.md](design/voice_and_broadcast_audio.md).
  2. **Phone voice notes:** story-beat texts from key contacts come with a playable voice note.
  3. **Cutscene voice-over:** lines spoken inside motion-comic cutscenes.
- **Cutscenes (in-engine motion comic):** layered 2D art animated in Godot (camera pans, parallax, panel reveals, captions), with voice and SFX on top. Live slots show your player's portrait, name, team and stat line. Used for **story moments**, **achievements** and **big plays**. See [design/cutscenes_motion_comic.md](design/cutscenes_motion_comic.md).
- **UI sound:** the shipped `UiSfx` service (Slice D), extended with Voice, Announcer and Music buses.

## 6. Commercial release (Steam)

The game ships for sale on Steam. Technical integration (Facepunch.Steamworks, cloud saves, achievements, rich presence, export presets) shipped in Phase 11. The commercial launch plan is [design/steam_commercial_release.md](design/steam_commercial_release.md): store page, the AI-content disclosure (pre-generated art and ElevenLabs voices), the mature-content survey (drugs, simulated gambling, crime), licensing records, trademark clearance of team and league names, the release-candidate QA checklist, and a post-1.0 save-compatibility policy.

## 7. Firm constraints

- **No runtime AI generation.** Voices and art are generated during development, reviewed by a human, and shipped as files. API keys never enter the repo or the build.
- **No real people, teams or broadcasters.** All teams, leagues, players and announcers are fictional. No ElevenLabs voice may clone or imitate a real person.
- **No live player names in voice.** Pre-generated clips can't say a player-chosen name. Announcers call the play by role and team ("the kid", "the Barons' shortstop"). Names appear in subtitles and captions.
- **Presentation never drives simulation.** Announcers, cutscenes and voice notes consume events and never write game state. The baseball sim stays deterministic and calibrated.
- **Pre-1.0 saves are disposable.** The Phase 13 schema (v14) is a clean break: older saves are refused with a "start a new career" message. **From 1.0 on, every schema change ships a migration.**
- **English only at launch**, but all player-facing text and subtitles go through strings tables so localization stays possible.

## 8. What's already built (as of 2026-10-09)

Phases 0–12 and the post-12 arcs are complete: database core, event bus, macro and micro baseball sims, the life sim, relationships and heirs, gritty events, economy and hustles, the presentation shell, the Steam technical integration, the High School life layer, onboarding, the real-time clock and the criminal-underworld storyline. The **league model shipped in Phase 9** (6 tiers including Single-A, Double-A and Triple-A, 8 teams per tier) is **superseded** by §3 and gets rebuilt in Phase 13. See [BUILD_PLAN.md](BUILD_PLAN.md) and [progress.md](progress.md).
