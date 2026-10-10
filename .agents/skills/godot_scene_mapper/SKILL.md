---
name: godot_scene_mapper
description: Bridges the gap between C# scripts and Godot's visual node tree.
---
# godot_scene_mapper

**Purpose:** Bridges the gap between C# scripts and Godot's visual node tree.

**Execution Details:**
When invoked, scan the Godot `.tscn` (scene) files and cross-reference them with the C# `[Export]` variables in the UI scripts.
This ensures C# code doesn't reference UI buttons or nodes that don't exist in the Godot scene.
