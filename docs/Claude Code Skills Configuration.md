# **Recommended Skills for .claude/skills/ Directory**

To maximize the efficiency of Claude Fable 5 when developing "Dirt & Diamonds," you need to equip it with custom, project-specific scripts. These skills act as repeatable macros that the AI can execute to validate complex logic without needing to launch the entire Godot game engine.

Here are the essential skills to define in your .claude/skills directory:

## **1\. validate\_sqlite\_schema**

* **Purpose:** Ensures the massive generational SQLite database remains structurally sound as Fable 5 writes new code.  
* **Execution:** Runs a lightweight script to check for orphaned foreign keys, valid indexing on high-traffic tables (like Game\_Logs and Relationships), and data type consistency.  
* **Why it's critical:** With thousands of simulated NPCs and decades of baseball stats, a malformed query will tank performance. This skill lets Fable 5 self-correct its database interactions.

## **2\. run\_monte\_carlo\_batch**

* **Purpose:** Tests the baseball simulation engine's statistical accuracy in isolation.  
* **Execution:** A headless C\# script that simulates 10,000 at-bats using the AtBatResolver.cs and outputs the resulting slash line (AVG/OBP/SLG) to the terminal.  
* **Why it's critical:** It allows the Opus and Fable models to mathematically tweak the baseball probability matrices (PED effects, player stats) and instantly see if the resulting league averages mirror real-world baseball statistics.

## **3\. simulate\_utility\_decay**

* **Purpose:** Balances the life simulation aspects (The Sims elements).  
* **Execution:** Simulates 168 in-game hours (one week) of passive Need decay (Hunger, Sleep, Fitness, Social, Hygiene) for a standard NPC and outputs a text-based graph of their states.  
* **Why it's critical:** Non-linear decay formulas can easily spiral out of control. This skill helps Fable 5 tune the ![][image1] (base decay rate) so players aren't starving to death after a 3-hour baseball game.

## **4\. check\_event\_graph\_integrity**

* **Purpose:** Validates the "Gritty Event Framework" to prevent broken narrative chains.  
* **Execution:** Parses the JSON or SQLite event definitions to ensure no branching choices lead to dead ends, checks that all prerequisite boolean flags (e.g., compromised\_syndicate) can actually be triggered in-game, and validates cyclical event loops.  
* **Why it's critical:** With cascading consequences (e.g., taking a bribe leading to a crisis 5 years later), this prevents Fable 5 from creating orphaned events that the player can never experience.

## **5\. godot\_scene\_mapper**

* **Purpose:** Bridges the gap between C\# scripts and Godot's visual node tree.  
* **Execution:** Scans the Godot .tscn (scene) files and cross-references them with the C\# \[Export\] variables in the UI scripts.  
* **Why it's critical:** When Fable 5 builds the mini-game UIs for the Hustles (like the Texas Hold'em table or the Drug Territory map), this skill ensures the AI doesn't write C\# code that references UI buttons that don't exist in the Godot scene.

## **6\. validate\_steamworks\_native**

* **Purpose:** Ensures deployment compatibility.  
* **Execution:** Checks the .csproj file to verify that the Facepunch.Steamworks native libraries (steam\_api64.dll, libsteam\_api.so) are correctly configured to copy to the output directory upon build.  
* **Why it's critical:** Steam API integration is notoriously finicky. This ensures the game won't instantly crash when you attempt to compile a Linux/Steam Deck build.

[image1]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABIAAAAbCAYAAABxwd+fAAABOElEQVR4Xu2TPUrEUBSFZ1ALEcZCIZD/hFgoCkJAUASFAbGwUkT34ArExsbSxs5KtHEB4h5srdzAoLgEO7+DbzBzC3kDwUYPfEXuuffkzZ28TudvqCiKDHaCIJix3lgqy3I2y7I7eIjjeFrYHm+labpJ0CBJkmVhfW+5Uz3leX4urO+t1oIkBShMKNj63mJPNSHvQjuzvrcY3iPkw3FlfW+1EsTgPju6hlPB8wsfadDs4UV9vNVmbUQMHcCj++cWHa8M7Tb7CFoKw3C+WZO6NB4Jhp51TVx9UlC7Z/BGfYKvvaK24Z6/RcA2xpsgZG3E/PKP8QYEbAmdmtqt/bntBdV1PaXb/sON70ZRNFdVVU8QtA4XqtvGsUTIGTs75HQr1vPVcPmXcELQgm3wVWtBQ01or7b4r1/UJwOMTc+Cf2Z/AAAAAElFTkSuQmCC>