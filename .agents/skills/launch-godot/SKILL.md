---
name: launch-godot
description: Launches the Dirt & Diamonds Godot project.
---

# Launch Godot Game

This skill launches the Dirt & Diamonds Godot project in the editor or runs the game.

## Instructions

1. To launch the Godot editor for this project, run the following command in the terminal from the project root (`c:\Users\DELL\dirt&diamonds`):

    ```powershell
    godot -e .
    ```

2. To launch the game directly (without the editor), run:

    ```powershell
    godot .
    ```

**Note**: If `godot` is not recognized as a command, it means the Godot executable is not in the system's `PATH`. In this case, you may need to ask the user for the path to their Godot 4+ executable.
