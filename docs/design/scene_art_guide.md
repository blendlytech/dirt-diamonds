# Scene Art Guide — what to draw and where to put it

The dashboard's center card now **always** shows a scene: where the player is right now, or what's demanding their attention. Every scene renders its authored PNG if the file exists; otherwise it shows a school-color text placeholder. **Drop a file at the exact path below and the scene starts using it on the next boot — no code changes ever.**

All generic scene art lives in `Assets/Graphics/Scenes/` (create the folder with the first file). Per-school art stays in `Assets/Graphics/HighSchools/` per schools.json.

## Scene resolution (priority order)

1. **At-bat in progress** → card hidden (the at-bat view owns the slot).
2. **A gritty event is waiting on your answer** → `event_{category}.png`, falling back to `event.png`.
3. **School hours** (in-session weekday, 8:00 AM + 6h) → your school's `{ABBR}_school.png`, falling back to generic `school.png`.
4. **Game day** (outside school hours) → the **home** team's `{ABBR}_field.png` (away games show the opponent's park), falling back to generic `field.png`.
5. Otherwise, the **day phase**: `home_night` (before 6 AM / after 10 PM) → `home_morning` (6–8 AM) → `neighborhood` (daytime) → `home_evening` (6–10 PM).

School/field scenes are HS-tier only for now (college/pro have no schools.json entries); those tiers get scenes 2 and 5.

## The full file list

### Per school (6 remaining × 3 files) — `Assets/Graphics/HighSchools/`

| School | Files |
| --- | --- |
| ~~Crestwood~~ | ~~CRW_logo / CRW_school / CRW_field~~ ✔ DONE |
| ~~Lakeside~~ | ~~LKS_logo / LKS_school / LKS_field~~ ✔ DONE |
| Fairview | `FRV_logo.png`, `FRV_school.png`, `FRV_field.png` |
| Oak Ridge | `OKR_logo.png`, `OKR_school.png`, `OKR_field.png` |
| Pinehurst | `PNH_logo.png`, `PNH_school.png`, `PNH_field.png` |
| Westfield | `WSF_logo.png`, `WSF_school.png`, `WSF_field.png` |
| Summit | `SMT_logo.png`, `SMT_school.png`, `SMT_field.png` |
| Riverton | `RVT_logo.png`, `RVT_school.png`, `RVT_field.png` |

Match each school's palette from schools.json (`palette_description` is the art direction line).

### Generic scenes — `Assets/Graphics/Scenes/`

| File | Shows when | Suggested vibe |
| --- | --- | --- |
| `home_morning.png` | 6–8 AM | bedroom / kitchen, getting ready |
| `neighborhood.png` | daytime, no school/game | the block — streets, corner store |
| `home_evening.png` | 6–10 PM | living room, dusk out the window |
| `home_night.png` | 10 PM–6 AM | dark bedroom / streetlight through blinds |
| `school.png` | school hours, school has no exterior art yet | generic hallway/classroom |
| `field.png` | game day, home park has no art yet | generic HS diamond |
| `event.png` | an event is pending, its category file missing | the burner phone lit up |
| `event_hustle.png` | pending hustle event | alley / back room |
| `event_family.png` | pending family event | kitchen table |
| `event_school.png` | pending school event | principal's office / lockers |
| `event_baseball.png` | pending baseball event | dugout / locker room |
| `event_romance.png` | pending romance event | date spot |
| `event_career.png` | pending career event | office / scout's car |
| `event_general.png` | pending uncategorized event | the phone, generic |

**Sizing:** landscape, ~16:9 (e.g. 1280×720) reads best — the card letterboxes with KeepAspectCentered either way.

**After adding files:** the Godot importer must see them once — opening the editor does it, or the models run `godot --headless -e --quit --path .` (note: plain `--import` did NOT generate the sidecars on 4.7).

## Future (not built)

- Side-games living in the slot when nothing's happening (the user's "or have a side game" idea — candidate after R-2/R-3).
- College/pro park art (needs a venue catalog beyond schools.json).
- Per-event `image` field in event JSON for signature moments (the dispatcher's model would take an optional path the same way it took `text_message`).
