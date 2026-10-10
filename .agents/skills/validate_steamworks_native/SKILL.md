---
name: validate_steamworks_native
description: Ensures deployment compatibility for Steamworks native libraries.
---
# validate_steamworks_native

**Purpose:** Ensures deployment compatibility.

**Execution Details:**
When invoked, check the `.csproj` file to verify that the Facepunch.Steamworks native libraries (`steam_api64.dll`, `libsteam_api.so`) are correctly configured to copy to the output directory upon build.
This ensures the game won't instantly crash when attempting to compile a Linux/Steam Deck build.
