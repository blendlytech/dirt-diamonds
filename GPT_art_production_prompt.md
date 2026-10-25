# Task: 15d-ART — produce every team, venue and item image with GPT image generation

**You are:** GPT Luna, working autonomously on the Dirt & Diamonds repo (`C:\Users\DELL\dirt_and_diamonds`).
**Assigned by:** the owner, 2026-10-25 (this replaces the Gemini image pipeline, which ran out of quota). This is the art-production part of slices 15d / G-8 (docs/TEAM.md §2).
**Reviewer:** Claude Opus 5.5 checks consistency. **Owner gate:** taste, at the pilot and at the end.
**Branch / worktree:** `p15/15d-art-gpt` in your own worktree, `C:\Users\DELL\dnd-gpt-art` (TEAM.md §3). Never work in the main checkout and never build in another model's worktree.

```
PILOT_GATE = ON      # The owner sets this to OFF to skip the stop after the pilot (Stage 2).
```

Read these first: `GEMINI.md` (the binding rules), `docs/TEAM.md` §3–4, `docs/design/visual_redesign.md` §2 and §6, `docs/design/gameplay_core.md` §3, `docs/reference/asset_generation_status.md` (the Gemini catalog, with logo concepts and palettes), `Assets/Data/Schools/schools.json`, `Assets/Data/Venues/venues.json` and `Assets/Data/Items/items.json`.

---

## Stage 0 — Preflight (stop on any failure)

1. **Git write check.** Your sandbox needs write access to the shared `C:\Users\DELL\dirt_and_diamonds\.git` (a worktree's refs and locks live in `.git\worktrees\<name>\`). Create the worktree:
   `git worktree add ..\dnd-gpt-art -b p15/15d-art-gpt origin/main`
   Then run `git commit --allow-empty -m "write check"` and `git reset --soft HEAD~1`. If this fails with a permission or lock error, stop and ask the owner to grant `.git` write access. The fallback is a standalone `git clone`.
2. **Seed data must be on main.** In your worktree, confirm that:
   - `Assets/Data/Venues/venues.json` exists and has **68** entries (12 HS, 24 College, 32 Pro), and
   - `Assets/Data/Schools/schools.json` has **12** schools (team_id 101–112).
   These arrive with slice **13-1a** (Gemini Flash seed data). If either is missing or short, **stop**. Report "blocked on 13-1a merge" and do nothing else.
3. **Image tool.** Use your image-generation tool (the newest GPT image model available to you). If you can only reach it through the OpenAI Images API, read the key from the `OPENAI_API_KEY` environment variable or the git-ignored repo-root `.env`. The owner has to add it, because it isn't there today. **Never print, log or commit any key or `.env` value.**
4. **Local tools.** Check for Python 3 with Pillow and for `oxipng` (optional). Find the Godot binary in `.agents/skills/launch-godot/SKILL.md` (Godot 4.7 mono).
5. **Pick up the art worth keeping.** Copy these files from the main checkout's `Assets\Graphics\HighSchools\` into the same folder in your worktree, if they exist there. They're untracked on main, so treat the main checkout as read-only:
   `BRK_school.png`, `BRK_field.png`, `CDG_school.png`. They already match the target style. Keep them, but crop or resize them to the spec in §3 if they're off-size.

---

## 1. Hard rules

**Allowed write paths. Everything else is read-only.**
- `Assets/Graphics/**` (new images, the Godot `.import` sidecars, `art_manifest.json`, `_superseded/`)
- `Assets/Data/Venues/venues.json` and `Assets/Data/Items/items.json`: path and key edits from §5 only. Never change a name, color, dimension, id or price.
- `docs/legal/ai_art_generation_log.md` (new), `docs/reference/asset_generation_status.md` and `docs/progress.md` (append only)

**No C# changes, nothing under `Assets/Simulation/`, and no schema changes.** If a mapping can't be done without code, write it down as an open item.

**Legal and content rules for every image** (the game ships commercially on Steam):
- **Original marks only.** No resemblance to any real MLB, MiLB, NCAA, NFL, NBA or NHL logo, wordmark, letterform or mascot design. Avoid well-known motifs: an Old English "D", interlocking letters, a famous team's exact animal pose. If an output looks like a real team's mark, discard it.
- **No real ballparks.** Don't reproduce any real stadium's signature features (famous scoreboards, a green-monster wall, ivy plus marquee combinations, named landmarks). Cities can be suggested, but don't depict a real, identifiable skyline.
- **No legible text anywhere**: no signage words, sponsor names, brand logos, scoreboard numbers or jersey names. Signs and scoreboards are blank or abstract. Logos are **emblems with no lettering**, because the UI adds typography later.
- **No identifiable people.** Venue scenes are empty or show distant, faceless silhouettes. No celebrity likenesses. People who appear in logos (the PRV prospector, the KNP knight, the OMA outlaw) are stylized and generic.
- **No Indigenous or Native American imagery, headdresses or formline art.** **CAS (Cascadia Wolves) override:** a plain, geometric timberwolf profile. Ignore "tribal-influenced" in the catalog.
- **OMA (Omaha Outlaws) override:** a masked bandit with **crossed bats**, no guns.
- Nothing sexual, no drugs, no gore. A logo can be aggressive but never bloody.

---

## 2. The style bible. Put the matching style block at the end of every prompt, word for word.

The working direction is the **gritty graphic-novel and motion-comic** look already used for BRK and CDG. Formal 15a sign-off is still open, which is why the pilot gate exists. When your tool accepts reference images, pass `BRK_school.png` or `BRK_field.png` as the style reference for every scene.

**STYLE_SCENE:**
> Gritty graphic-novel motion-comic illustration, hand-inked linework with textured crosshatching, bold shadow shapes, atmospheric lighting, slightly desaturated palette with the team's colors as the dominant accents, cinematic wide establishing shot, eye-level or slightly elevated camera, clean composition with open midground (UI hotspots are overlaid later), no text, no legible signage, no logos of real brands, no people in the foreground, 16:9.

**STYLE_LOGO:**
> Original sports emblem, flat 2D vector style, bold uniform outline, sharp clean silhouette, 2–4 flat colors from the given palette plus white or black, no gradients, no photographic texture, no lettering or numbers, centered, the mark fills about 85% of the canvas, readable at 64 px, transparent background.

**STYLE_ICON:**
> Single object game UI icon, flat vector with bold dark outline, simple two-tone shading, three-quarter view, centered, fills about 80% of the canvas, consistent light from top-left, no text, transparent background.

**Technical spec:**

| Class | Generate at | Deliver as | Notes |
| :-- | :-- | :-- | :-- |
| Logo | 1024×1024, transparent background | 1024×1024 RGBA PNG | Make sure the alpha really is transparent, with no charcoal box. Target ≤ 1.5 MB. |
| Scene (school, field, venue, kit) | The largest landscape size (e.g. 1536×1024) | **1920×1080 RGB PNG** | Center-crop to 16:9 first (keep the horizon and the field), then Lanczos-resize to 1920×1080. Target ≤ 3 MB, hard cap 4 MB. Run `oxipng -o 4` if you have it, otherwise Pillow `optimize=True`. |
| Item icon | 1024×1024, transparent background | 512×512 RGBA PNG | Target ≤ 300 KB. |

Don't enable Git LFS or change `.gitattributes`. That's the owner's decision. Report the total MB you add.

---

## 3. The inventory (about 314 new images)

| # | Set | Count | Output path | Source data for the prompt |
| :-- | :-- | :-- | :-- | :-- |
| A | Team logos, all 68 | 68 | HS: `Assets/Graphics/HighSchools/{ABBR}_logo.png` · College: `Assets/Graphics/Logos/College/{ABBR}_logo.png` · Pro: `Assets/Graphics/Logos/Pro/{ABBR}_logo.png` | Mascot and colors from venues.json/schools.json; emblem concepts from asset_generation_status.md §4–6 (for CRW Cardinals and LKS Longhorns, write your own from the mascot) |
| B | HS school + field (`schools.json` `images.school` / `images.field`) | 24 − 3 kept = **21** | `Assets/Graphics/HighSchools/{ABBR}_school.png`, `{ABBR}_field.png` | `school_name`, `flavor`, `palette_description`, plus the venue's `signature_features`. **HS fields are Friday-night games under lights.** Reuse the prompts in asset_generation_status.md §4 for CDG, GRF and HLC. |
| C | College + Pro team scenes | 56 × 3 = **168** | Keep the existing per-team paths in venues.json: `Assets/Graphics/Venues/{College\|Pro}/{ABBR}_exterior.png`, `_field_day.png`, `_field_night.png` | `park_name`, `city`/`state` mood, `capacity` (sets the scale: College is 2–8k, Pro 30k+), `surface`, `signature_features`, `colors` |
| D | HS interior kits | 3 hotspots × 4 variants = **12** | `Assets/Graphics/Venues/Kits/HS/{dugout_locker\|gym\|parking_lot}_v{1-4}.png` | Generic small-town HS. Use **neutral, desaturated** team-color areas (banners, lockers, bleachers) so the UI can tint them later. |
| E | College and Pro interior kits | 5 hotspots × 3 variants × 2 tiers = **30** | `Assets/Graphics/Venues/Kits/{College\|Pro}/{concourse\|clubhouse\|manager_office\|training_room\|players_lot}_v{1-3}.png` | The production value climbs by tier (visual_redesign §2): College is regional, Pro is prime-time. Keep team-color areas neutral, as in D. |
| F | Item icons | **15** | `Assets/Graphics/Items/{item_id}.png` | `name` and `category` from items.json |

**Why kits:** gameplay_core §3.4 and visual_redesign §6 say venues are "reusable park kits with team-specific dressing", not 68 bespoke scene sets. Team identity comes from the logo, the exterior and the two field shots. The interiors are shared.

**Kit variant assignment** (deterministic, so other tools can reproduce it):
- HS: `v = ((team_id − 101) % 4) + 1`
- College: `v = ((team_id − 201) % 3) + 1`
- Pro: `v = ((team_id − 301) % 3) + 1`

**Superseded art.** `CRW_*`, `LKS_*` and the BRK and CDG **logos** are in an older or opaque style. Before you write their replacements, move them (with their `.import` files) into `Assets/Graphics/_superseded/HighSchools/`. Create `Assets/Graphics/_superseded/.gdignore` (an empty file) so Godot doesn't import them. Never delete art.

---

## 4. Building the prompts

Build every prompt from the data files. Don't invent palettes. Always name colors **with their hex values**.

- **Logo:** `"{tier-appropriate opener} logo for the {team_name}: {emblem concept}. Palette: primary {primary.name} {primary.hex}, secondary {secondary.name} {secondary.hex}, accent {accent.name} {accent.hex}. " + STYLE_LOGO`
  The openers are "High school varsity baseball", "Collegiate baseball" and "Professional baseball franchise". HS marks are a little simpler and scrappier, Pro marks the most polished.
- **HS school:** `"Exterior of {school_name}, {flavor}. {palette_description} " + STYLE_SCENE`, with school-day light (morning or afternoon).
- **HS field:** `"{park_name}, a high school baseball diamond: {signature_features}. Friday-night game lights, {capacity}-seat bleachers, outfield wall in {primary.name} {primary.hex}. " + STYLE_SCENE`
- **Exterior:** the park's façade and entry plaza at golden hour, scaled to `capacity`, with `signature_features` and team colors on the architecture. You can pass the team's finished logo as a reference image for the palette only, so no lettering appears.
- **field_day:** the view from behind home plate in daylight, with `signature_features` and `surface` showing.
- **field_night:** **make it as an edit of that team's field_day image** (pass it as the input image): "the same ballpark and camera at a night game under stadium lights". The two shots must show the same park.
- **Kits:** describe the room for its tier. For example, Pro clubhouse v2 is "a modern big-league home clubhouse, wood lockers, a carpeted center, neutral gray team-color panels". Make the variants clearly different from each other: layout, era, materials.
- **Icons:** `"{name} ({category}) game item icon" + STYLE_ICON`. Make prestige readable in the icon: `fake_chain` looks cheap, `gold_watch` looks luxe, `used_sedan` is a dented older car, `new_coupe` is shiny.

---

## 5. Mapping. These are the only data edits.

Edit the files with a script that preserves their existing formatting: two-space indent, and schools.json keeps its `\uXXXX` escapes. Diff carefully afterwards, so that only these keys change.

1. **`schools.json`:** no edits. Its `images.logo/school/field` paths already match §3. Just verify that all 36 files exist.
2. **`venues.json`:**
   - Add `"logo": "res://..."` to **all 68 entries**, pointing to the paths in set A. Put it right after `"hotspots"`.
   - **HS entries:** set `hotspots.exterior` to `res://Assets/Graphics/HighSchools/{ABBR}_school.png` and `hotspots.field` to `res://Assets/Graphics/HighSchools/{ABBR}_field.png`, so each image has one source file and nothing is duplicated. Point `dugout_locker`, `gym` and `parking_lot` at the team's assigned HS kit variant.
   - **College and Pro entries:** keep `exterior`, `field_day` and `field_night` as they are. Point `concourse`, `clubhouse`, `manager_office`, `training_room` and `players_lot` at that tier's assigned kit variant.
   - Add one sentence to the top-level `"//"` comment: "`logo` is the team mark; interior hotspots point at shared tier kits (`Venues/Kits/`, variant by team_id), per gameplay_core §3.4."
3. **`items.json`:** add `"icon": "res://Assets/Graphics/Items/{id}.png"` to every item, right after `"category"`. Add `icon` to the entry contract in the top-level `"//"` comment. `ItemCatalogJson` reads keys with `TryGetProperty`, so the new key is ignored safely until UI code reads it.
4. When you're done, **every `res://` path in schools.json, venues.json and items.json must resolve to a file**. Write a small checker script (keep it in your scratch space, don't commit it), run it, and include its output in the handoff.

---

## 6. Workflow

**Stage 1. Style lock.** Write the three style blocks to the top of `docs/legal/ai_art_generation_log.md`.

**Stage 2. Pilot (28 images).**
- Logos: HLC (HS), MRB (College), BAY (Pro), CAS (Pro, the sensitivity override)
- The GRF school and field, plus HS kit v1 (3)
- The ASH exterior, field_day and field_night, plus College kit v1 (5)
- The BAY exterior, field_day and field_night, plus Pro kit v1 (5)
- Icons: `used_sedan`, `gold_watch`, `energy_drink`

Build a contact sheet for these (a single PNG grid with each file name under its image) in `Build/art_review/pilot_contact_sheet.png`. `Build/` is git-ignored. Commit the pilot.

**If `PILOT_GATE = ON`, stop here.** Hand off to the owner with the contact sheet path and wait for "PILOT APPROVED" or for style notes. Apply any notes to the style blocks and regenerate the pilot before you go on.

**Stage 3. Bulk.** Work in this order and commit after each set: A logos → B HS scenes → D HS kits → C College scenes → C Pro scenes → E kits → F icons. Pilot images count toward the totals, so don't regenerate them. The run must be **resumable**: skip any target that already exists and is marked `generated` or `approved` in the manifest.

**QA every image before you save it.** Reject and regenerate (up to 3 attempts) if any of these is true:
- legible or garbled text appears
- it resembles a real team mark or a real ballpark
- the team colors are wrong or missing
- a logo's background isn't really transparent
- a field shot has broken geometry (no diamond, wrong number of bases, the infield in the outfield)
- a night shot doesn't match its day shot
- there are extra limbs or faces in the foreground
- it breaks the style bible

After 3 failed attempts, keep the best candidate, mark it `needs-owner` in the manifest, and move on. Never stall the run.

**Stage 4. Map, import, verify.** Apply §5. Then run Godot's headless import so every PNG gets its `.import` sidecar. The repo commits `.import` files (see `.gitignore`):
`& "<godot exe from launch-godot SKILL.md>" --headless --path C:\Users\DELL\dnd-gpt-art --import`
Make one contact sheet per set in `Build/art_review/`.

---

## 7. Logs and manifest

- **`Assets/Graphics/art_manifest.json`:** one entry per delivered image: `path`, `set` (A–F), `team_id` or `item_id`, `prompt` (the full text you sent), `model`, `generated_size`, `delivered_size`, `edited_from` (for night shots), `attempts`, `status` (`generated` | `needs-owner` | `approved` | `kept-existing`), `date`. The three kept images are `kept-existing` with `model: "gemini-3.1-flash-image"`. The 15d gate ("asset manifest complete") and the Steam AI-content disclosure both rely on this file.
- **`docs/legal/ai_art_generation_log.md`:** one row per run or batch: date, model, set, count, attempts, rejects and why, and the total MB. This mirrors the ElevenLabs generation log rule (GEMINI.md §5).
- **`docs/reference/asset_generation_status.md`:** rewrite §1–4 as the live catalog: GPT is the generator, plus counts per set, `needs-owner` items, and the kit-assignment table. Remove the Gemini quota notice and the `.gemini/...` image links, which point outside the repo and break.
- **`docs/progress.md`:** append one compact dated entry.

---

## 8. Gates (run them all and paste the output into the handoff)

1. `git diff --stat origin/main` lists **only** paths allowed in §1. Nothing under `Assets/Simulation/` and no `.cs` files.
2. `dotnet build DirtAndDiamonds.sln -c Debug` gives 0 warnings and 0 errors.
3. `dotnet run --project Tools/GrittyEventsHarness -c Release` and `dotnet run --project Tools/MonteCarloHarness -c Release` are both green. Both parse items.json and schools.json. If a check fails, run the same harness on a clean `origin/main` checkout. If it fails there too, it's pre-existing (for example, the school-count check waiting on 13-1a): report it, don't fix it.
4. The path checker from §5.4 finds 0 missing files.
5. Every delivered file meets the size and format spec in §2. List any file over its target size.

---

## 9. Commit and hand off

- Commit on `p15/15d-art-gpt` in small commits per set. End each message with your attribution line. **Don't merge to main.** Claude Opus merges after review and the owner's taste pass.
- Never stage `.env`. Run `git status` before each commit.
- **Handoff note to the owner:**
  - what you produced, with counts per set
  - the commits
  - the gate results
  - the total MB added
  - the `needs-owner` list
  - the contact-sheet paths
  - the open items, including:
    - UI code doesn't read `venues.json` `logo` or `items.json` `icon` yet (a G-7 / 15c wiring task)
    - team-color tinting of the kits is a runtime UI job
    - the formal 15a direction sign-off
  - "Recommended next: Claude Opus consistency review of `p15/15d-art-gpt`, then the G-7 venue screens by Claude Sonnet 5.5"

**Stop when this is done.** Don't start any other slice, don't produce portraits, location scenes or cutscene kits, and don't wire UI.
