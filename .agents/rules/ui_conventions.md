# UI Conventions (Godot 4 / C#)

## Scene Organization

- One scene per interactive surface: each Hustle mini-game, the at-bat view, and each life-sim panel is an isolated `.tscn` under `Assets/UI/` (create the folder with the first scene). Scenes communicate through the event bus or signals — never by reaching into another scene's tree.
- Scene root nodes are named after their scene file in PascalCase (`TexasHoldemTable.tscn` → root `TexasHoldemTable`).
- **Verify before wiring:** run `godot_scene_mapper` (or the Godot MCP) to confirm actual node paths *before* writing any `GetNode<T>()` call. Hardcoded paths against unverified trees are a review-blocking defect.

## Node Access from C# Scripts

- Use `GetNode<T>("...")` with exact typed access; cache node references in `_camelCase` private fields during `_Ready()` — never call `GetNode` inside `_Process()` or simulation loops.
- Every field the editor must see uses an explicit `[Export]` attribute.
- UI scripts inherit from the matching Control-derived type (`Control`, `PanelContainer`, etc.), never plain `Node`, so layout behavior is inspectable in the editor.

## Signals & Data Flow

- UI is read-only over simulation state: it renders DTOs handed to it and emits player-intent signals upward. UI never writes to the database or mutates simulation state directly.
- C# events/signals follow Godot 4 conventions: signal names in PascalCase, connected in `_Ready()`, disconnected in `_ExitTree()` when the target outlives the scene.

## Performance

- Pooled UI elements for anything spawned in volume (game log lines, stat rows, floating text). Instantiating scenes mid-game is allowed only through the object pool.
- No per-frame string formatting or LINQ in `_Process()`; update labels only when the underlying value changes (dirty-flag pattern).
- Long-running simulation work never happens on the UI thread — the UI awaits results from the async event dispatchers.

## Style

- Thin vertical slices: every phase ships its UI as a minimal but demoable scene; no big-bang UI phase.
- Player-facing text lives in scene/resource files or a strings table, not in C# string literals, to keep future localization possible.

## Visual Redesign (Phase 15 — `docs/design/visual_redesign.md`)

- The shipped thin-slice look is not the shipping look. All styling comes from the Theme family in `Assets/UI/Theme/` (tokens + components). Per-scene style overrides are a review-blocking defect.
- Team colors come from `Assets/Data/Teams/team_colors.json` through theme variations.
- Every interactive control has focus neighbors and a visible focus state (keyboard, controller and Steam Deck reachability).
- Layouts must work at 16:9 and 16:10 (1280×800 minimum).
- Accessibility settings are honored everywhere: text size, reduce motion, subtitles, a colorblind-safe semantic palette.

## Audio, Voice & Subtitles (Phase 14 — `docs/design/voice_and_broadcast_audio.md`)

- Play UI sounds through `UiSfx` and announcer calls (including a call used as a cutscene bed) through `VoiceDirector`. Never create `AudioStreamPlayer`s ad hoc in scenes. Voice is broadcasters only: phone messages and cutscene dialogue are text.
- Every spoken line shows a subtitle from the strings table (keyed by `clip_id`) when subtitles are on.

## Cutscenes (Phase 16 — `docs/design/cutscenes_motion_comic.md`)

- Cutscenes play only through the permanent `CutscenePlayer` overlay, requested via `CutsceneDirector`. Scenes don't spawn their own cinematic overlays.
- Every cutscene is skippable and honors reduce-motion. The game clock soft-pauses while one plays.
