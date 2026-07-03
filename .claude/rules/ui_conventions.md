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
