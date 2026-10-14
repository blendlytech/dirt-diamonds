# Dirt & Diamonds — Claude Code entry point

The binding project directives live in `GEMINI.md`, shared by every model on the team. They're imported here in full:

@GEMINI.md

## Claude-specific notes

- **Roles:** Claude Opus 5.5 is integration lead and chief architect. Claude Sonnet 5.5 is UI and feature engineer. **GPT 6 Astra Pro** is senior gameplay engineer, calibration/math owner and the independent reviewer of Claude's sim diffs; it ran the R-1 review ([brief](docs/tasks/R-1_full_review_brief.md)). Gemini 3.1 Pro and Pro Math are unavailable from 2026-10-13 (succession: TEAM.md §1a); Gemini 3.8 Flash stays. Claude Fable 5.1 is inactive (credit exhausted 2026-10-10); it authored the G-2 pitching design. Current roles live only in [docs/TEAM.md](docs/TEAM.md). Names like "Fable 5", "Opus 4.8" and "Sonnet 5" in older docs are past authors, not roles.
- **Start of every session:** read [docs/README.md](docs/README.md) (which doc is authoritative) and [docs/progress.md](docs/progress.md), then run `git status` and `git log`. An auto-committer and parallel sessions are active, so work in your own git worktree (TEAM.md §3).
- **Agent rules and skills** live in `.agents/rules/` and `.agents/skills/`. Read the rule file for any system you touch.
- **Build and gates:** `dotnet build DirtAndDiamonds.sln -c Debug` (expect 0 warnings, 0 errors), then the harness for the area you touched, e.g. `dotnet run --project Tools/GrittyEventsHarness -c Release` or `dotnet run --project Tools/MonteCarloHarness -c Release`.
