# Dirt & Diamonds — Claude Code entry point

The binding project directives live in `GEMINI.md`, shared by every model on the team. They're imported here in full:

@GEMINI.md

## Claude-specific notes

- **Roles:** Claude Opus 5.5 is integration lead and chief architect. Claude Sonnet 5.5 is UI and feature engineer. **Claude Fable 5.1 is the independent full-project reviewer**: start from [docs/tasks/fable_5_1_full_review_brief.md](docs/tasks/fable_5_1_full_review_brief.md). Current roles live only in [docs/TEAM.md](docs/TEAM.md). Names like "Fable 5", "Opus 4.8" and "Sonnet 5" in older docs are past authors, not roles.
- **Start of every session:** read [docs/README.md](docs/README.md) (which doc is authoritative) and [docs/progress.md](docs/progress.md), then run `git status` and `git log`. An auto-committer and parallel sessions are active, so work in your own git worktree (TEAM.md §3).
- **Agent rules and skills** live in `.agents/rules/` and `.agents/skills/`. Read the rule file for any system you touch.
- **Build and gates:** `dotnet build DirtAndDiamonds.sln -c Debug` (expect 0 warnings, 0 errors), then the harness for the area you touched, e.g. `dotnet run --project Tools/GrittyEventsHarness -c Release` or `dotnet run --project Tools/MonteCarloHarness -c Release`.
