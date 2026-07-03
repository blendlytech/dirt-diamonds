---
name: run_monte_carlo_batch
description: Tests the baseball simulation engine's statistical accuracy in isolation.
---
# run_monte_carlo_batch

**Purpose:** Tests the baseball simulation engine's statistical accuracy in isolation.

**Execution Details:**
When invoked, run a headless C# script that simulates 10,000 at-bats using the `AtBatResolver.cs` and output the resulting slash line (AVG/OBP/SLG) to the terminal.
This allows models to mathematically tweak the baseball probability matrices (PED effects, player stats) and instantly see if the resulting league averages mirror real-world baseball statistics.
