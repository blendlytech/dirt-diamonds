---
name: validate_sqlite_schema
description: Ensures the massive generational SQLite database remains structurally sound as Fable 5 writes new code.
---
# validate_sqlite_schema

**Purpose:** Ensures the massive generational SQLite database remains structurally sound as Fable 5 writes new code.

**Execution Details:**
When invoked, run a lightweight script to check for orphaned foreign keys, valid indexing on high-traffic tables (like Game_Logs and Relationships), and data type consistency.
If no script exists, guide the user to create or run one, or perform the SQL checks using available database tools if possible.
