# Dirt & Diamonds — Claude Code entry point

The binding project directives live in `GEMINI.md`, shared by every model on the team. They're imported here in full:

@GEMINI.md

## Claude-specific notes

- **Roles (owner, 2026-11-19; amended 2026-11-25):** Claude Opus 5.5 is integration lead and chief architect; Claude Sonnet 5.5 is UI and feature engineer. **Gemini 4 Carbon** (it replaced Gemini 4 Argon completely on 2026-11-25) is calibration/math owner and the independent reviewer of Claude's sim and persistence diffs; Gemini 4 Neon reviews Sonnet UI/content; Gemini Hydrogen does mechanical waves under the Neon Orchestrator, which also runs content waves now that the Argon Orchestrator is gone. The GPT models (6.1 Sol, 6 Astra, 6 Sol) are no longer assigned; GPT 6 Astra Pro ran the R-1 review ([brief](docs/tasks/R-1_full_review_brief.md)). Current roles live only in [docs/TEAM.md](docs/TEAM.md). Names like "Fable 5", "Opus 4.8", "Sonnet 5" and the GPT models in older docs are past authors, not roles.
- **Start of every session:** read [docs/README.md](docs/README.md) (which doc is authoritative) and [docs/progress.md](docs/progress.md), then run `git status` and `git log`. An auto-committer and parallel sessions are active, so work in your own git worktree (TEAM.md §3).
- **Agent rules and skills** live in `.agents/rules/` and `.agents/skills/`. Read the rule file for any system you touch.
- **Build and gates:** `dotnet build DirtAndDiamonds.sln -c Debug` (expect 0 warnings, 0 errors), then the harness for the area you touched, e.g. `dotnet run --project Tools/GrittyEventsHarness -c Release` or `dotnet run --project Tools/MonteCarloHarness -c Release`.
