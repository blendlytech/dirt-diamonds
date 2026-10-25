# Task Brief: 13-1a — Seed data for 13-1 (HS schools + home parks)

**Status:** ready to start once its base exists (below).
**Recommended model:** Gemini 3.8 Flash (TEAM.md §2, 13-1 row: "+ Flash: team seed data")
**Reviewer:** Claude Opus 5.5 (it consumes this data in 13-1)
**Owner gate:** none for the build. The names stay subject to the owner's trademark screen (17b, [steam_commercial_release.md](../design/steam_commercial_release.md) §5), and this task gives no clearance.
**Spec:** [league_restructure.md](../design/league_restructure.md) §2.2–§2.3, §12 (`Teams.home_park`), Appendix A; [13-1 brief](13-1_schema_v14_worldgen_brief.md) §3.3
**Branch / worktree:** `p13/13-1a-flash`, branched from **`p13/integration`** (it exists once the owner has merged the CS chain to `main`), in your own worktree (`git worktree add ..\dnd-flash-131a -b p13/13-1a-flash origin/p13/integration`). **Never merge to `main`:** the re-keyed `schools.json` breaks the shipped game's boot cross-check until 13-1's world-gen lands. Opus folds it into `p13/13-1-opus`.

## 1. Context

13-1 generates the new 68-team world: 12 HS, 24 College, 32 Pro, with the ids, conferences and divisions in Appendix A. Two pieces of player-facing content have to exist first, and they're data, not code:

1. **HS school branding** (`schools.json`) for 12 schools instead of 8. Boot cross-checks this file against the HS roster and fails loudly on any mismatch.
2. **A home park name for every team** (`Teams.home_park`, used by announcer intros and the UI). G-7's venue catalog replaces these later.

**Team ids follow Appendix A table order:** top to bottom, and left to right within a cell.
- HS: North 101–106 (Crestwood, Lakeside, Fairview, Oak Ridge, Brookhaven, Cedar Grove), South 107–112 (Pinehurst, Westfield, Summit, Riverton, Granite Falls, Hillcrest).
- College: 201–224 (Coastal 201–204 … Canyon 221–224).
- Pro: 301–332 (Northeast 301–304 … Pacific 329–332).

## 2. Hard constraints

- **Allowed paths:** `Assets/Data/Schools/schools.json`, the new `Assets/Data/League/home_parks.json`, and one line in `docs/progress.md`. Everything else is read-only. No C#, no `.tscn`, no images.
- **Fictional names only.** No real stadium or ballpark names or close echoes (e.g. Fenway, Wrigley, Yankee, Dodger, Camden, Coors, Busch, Comerica, Petco, Oracle, Rose Bowl), no corporate or sponsor names (no "`<Brand>` Field"), no real people's names. City names are fine; a city + nickname pairing must not match a real pro or college team.
- **Don't rename any team, mascot or abbreviation.** Appendix A is the source; renames are the owner's call (team-names gate).
- **Content lines:** clean, all-ages copy. Nothing about lineage, the father, crime or vice in school flavor text.

## 3. Inputs

- `Assets/Data/Schools/schools.json`: its `"//"` contract comment and the 8 shipped entries (101–108, East/West blocks).
- Appendix A of `league_restructure.md`: all 68 names and abbreviations.
- The loader contract in `Assets/Data/Schools/SchoolCatalogJson.cs` (required fields, including the three image paths) and `SchoolCatalog.ValidateAgainstRoster` (ids and abbreviations must match the live HS roster; no duplicates).

## 4. Outputs

1. **`schools.json`: 12 entries keyed 101–112** in the order above.
   - The 8 shipped schools keep every field byte for byte except `team_id`. That renumbers Pinehurst 105 → 107, Westfield 106 → 108, Summit 107 → 109 and Riverton 108 → 110.
   - Add 4 new full entries for Brookhaven Bulldogs (BRK, 105), Cedar Grove Comets (CDG, 106), Granite Falls Gators (GRF, 111) and Hillcrest Hurricanes (HLC, 112): `school_name`, `mascot`, `colors` (primary/secondary/accent, name + hex), `palette_description`, `flavor`.
   - Image paths follow the convention `res://Assets/Graphics/HighSchools/<ABBR>_{logo,school,field}.png`. **The image files for the 4 new schools don't exist yet**; don't create placeholders (art is the owner's 15d/G-8 pipeline).
   - All 12 primary colors must be distinguishable from each other.
   - Update the `"//"` comment: 12 HS teams, ids 101–112, North/South conferences.
2. **`Assets/Data/League/home_parks.json`**: a `"//"` contract comment plus `"parks"`, an array of 68 objects `{ "team_id", "abbreviation", "home_park" }` in id order.
   - HS: a school field (e.g. "Crestwood High Field").
   - College: a campus park (e.g. "`<Name>` Stadium", "`<Name>` Field").
   - Pro: an original ballpark name.
   - Unique names, 40 characters max, plain text (no emoji).

## 5. Tests and gates (data only: no build needed)

Write a one-off check script in your scratch area, not the repo, run it, and paste its output into the handoff. It must confirm:
- Both files parse as JSON.
- `schools.json` has 12 entries with ids exactly 101–112, unique abbreviations matching Appendix A for each id, and every required loader field present.
- The 8 shipped entries are identical to `main`'s apart from `team_id`.
- `home_parks.json` has 68 rows with ids exactly 101–112, 201–224 and 301–332, and abbreviations matching Appendix A for every id.
- Every `home_park` is unique and no longer than 40 characters.
- A case-insensitive scan of all new strings finds none of the §2 example names, none of `MLB` or `NCAA`, neither league's championship-series name, and not `Diamond League`.
- The `git diff --stat` shows only the §2 allowed paths.

Expected on this branch alone: the shipped game's boot fails the school cross-check (the live roster is still 101–108). That's why this branch never goes to `main`.

## 6. Definition of done

- [ ] Both files written per §4; every §5 check passes.
- [ ] Committed on `p13/13-1a-flash` and pushed. Not merged.
- [ ] Handoff note + one `progress.md` line. **Then stop.**

## 7. Out of scope

School or team art; renaming anything; venue data (G-7, `venues.json`); announcer personas (14c); any C#, including teaching 13-1 to read `home_parks.json` (Opus does that in 13-1).

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: schools.json 12 entries (101–112) + home_parks.json 68 rows  · Commit: <sha>
Gates: <check script output: parse / counts / ids / abbreviations / shipped entries unchanged / uniqueness / name scan / diff scope>
Open: art for BRK, CDG, GRF, HLC (owner pipeline); names await the owner's trademark screen
Recommended next: Claude Opus reviews 13-1a and folds it into 13-1 (reviewer of 13-1: GPT 6 Astra Pro)
```

Then add one compact line to [progress.md](../progress.md). Don't start 13-1.
