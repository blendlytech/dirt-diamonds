---
name: launch-godot
description: Launches the Dirt & Diamonds Godot project.
---

# Launch Godot Game

This skill provides instructions on how to launch the Dirt & Diamonds Godot project.

## Instructions

**Important**: Because agents run their terminal commands in an isolated background session, any GUI app launched by an agent (like Godot) will be invisible to the user. **You cannot launch Godot directly for the user.**

Instead, instruct the user to run one of the provided batch scripts in their project folder.

1. **To play the game directly**, tell the user to run:
   `.\launch_game.bat`

2. **To open the project in the Godot Editor**, tell the user to run:
   `.\launch_editor.bat`

If they prefer to run it from their own terminal, the absolute path to Godot is:
`C:\Users\DELL\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe`
