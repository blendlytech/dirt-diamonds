---
name: check_event_graph_integrity
description: Validates the "Gritty Event Framework" to prevent broken narrative chains.
---
# check_event_graph_integrity

**Purpose:** Validates the "Gritty Event Framework" to prevent broken narrative chains.

**Execution Details:**
When invoked, parse the JSON or SQLite event definitions to ensure no branching choices lead to dead ends. Check that all prerequisite boolean flags (e.g., `compromised_syndicate`) can actually be triggered in-game, and validate cyclical event loops.
This prevents orphaned events that the player can never experience.
