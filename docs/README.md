# Docs map

Read this first to know which doc is authoritative. **If two docs disagree, the higher row wins.**

| Folder | What's in it | When to read it |
| :-- | :-- | :-- |
| [`GAME_VISION.md`](GAME_VISION.md) | The canonical pitch: what the game is | Always, before anything else |
| [`BUILD_PLAN.md`](BUILD_PLAN.md), [`TEAM.md`](TEAM.md), [`progress.md`](progress.md) | The phase plan, who owns what, where we are | Every session |
| [`design/`](design/) | **Active specs** for work that isn't built yet: Phases 13–17, **gameplay core (Phase G, high priority)**, lineage and temptation, substances | Before building anything in that area |
| [`tasks/`](tasks/) | **Active task briefs**, each naming its recommended model and its handoff ([template](tasks/TASK_BRIEF_TEMPLATE.md)) | When you're assigned the task |
| `review/` | Review outputs, e.g. Claude Fable 5.1's full-project review (`review/fable_5_1/`, created by R-1). Findings and proposals only, so nothing there is canonical until the owner accepts it into GAME_VISION or a spec | During triage |
| [`reference/`](reference/) | Specs of **shipped** systems: the baseball sim, life sim, gritty events, hustles, HS arc, heirs, UI slices. Code comments cite them by filename and section. | When you change that system, to see its contract and why it's built that way |
| [`archive/`](archive/) | **Superseded or historical**: the 6-tier league and promotion docs, the old scene-art guide, the retired phone tutorial, the Phase 8/9 plan, the pre-vision ideas list, the long progress log | Rarely, for history. Never build from these. |

## Rules

- **One home per decision.** A change of direction is made in `GAME_VISION.md` or the active spec, not as a header note on an old doc.
- **Shipped → reference.** When a `design/` spec is fully built and signed off, move it to `reference/` (keep the filename, so code citations still resolve).
- **Replaced → archive.** When a doc is superseded, move it to `archive/` and point to its replacement on its first line.
- Status lines inside `reference/` docs ("design only, no code yet") are historical: they describe the doc when it was written. The system they describe has shipped.
