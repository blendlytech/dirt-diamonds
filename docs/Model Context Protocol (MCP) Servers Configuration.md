# **Recommended MCP Servers for "Dirt & Diamonds"**

To give Claude Fable 5 (via Claude Code) the autonomy and context required to build a hybrid simulation game using Godot, C\#, and SQLite, you must initialize the following Model Context Protocol (MCP) servers. These servers bridge the gap between the AI's language model and your local development environment.

## Installation Location (Project Tree)

For Claude Code to automatically find and use these servers, they must be configured in a `.mcp.json` file located directly in the root of the project tree (i.e., `c:/Users/DELL/dirt&diamonds/.mcp.json`). This project-scoped configuration ensures that Claude always has the right tools loaded when working in this repository.

## 1. SQLite Database Explorer MCP

* **Target:** sqlite-mcp-server (or similar officially supported SQLite MCP)  
* **Primary Function:** Grants Fable 5 direct, read/write query access to your local .db files.  
* **Why it's essential:** "Dirt & Diamonds" lives and dies by its database. When Fable 5 writes a C\# method to calculate a 15-year veteran's career WAR (Wins Above Replacement), it needs to actually query the Batting\_Stats table to ensure the SQL join syntax is correct and performant. This prevents the AI from writing blind database code.

## **2. Godot Scene & Resource Parser MCP**

* **Target:** Custom File/Godot MCP (parsing .tscn and .tres files)  
* **Primary Function:** Translates Godot's proprietary text-based scene files into a hierarchical data structure Claude can understand.  
* **Why it's essential:** When you ask the AI to "wire up the Texas Hold'em UI," Fable 5 needs to know the exact path of the UI nodes in your Godot scene. This MCP reads the .tscn file, allowing Fable to accurately write the GetNode\<Button\>("VBoxContainer/FoldButton") C\# code without guessing the node hierarchy.

## **3. C\# / Roslyn Language Server MCP**

* **Target:** C\# AST Linter / Omnisharp integration  
* **Primary Function:** Provides deep semantic analysis of the C\# codebase, abstract syntax tree (AST) validation, and memory allocation tracking.  
* **Why it's essential:** The Monte Carlo baseball simulator must run thousands of games in milliseconds. This requires highly optimized, garbage-collection-friendly code (using structs, object pooling, Span\<T\>). This MCP allows Fable 5 to analyze its own C\# code for performance bottlenecks *before* compiling.

## **4\. Graph Memory / Knowledge Base MCP**

* **Target:** memory-mcp or a custom Neo4j/graph database MCP  
* **Primary Function:** Manages long-term, complex narrative continuity and architectural rules outside of the limited CLAUDE.md context window.  
* **Why it's essential:** The "Gritty Event Framework" relies on cascading logic (e.g., a choice made in High School triggers an event 10 years later in the Majors). A Graph Memory MCP allows Fable 5 to map and query these prerequisite flags (like compromised\_syndicate) to ensure there are no dead-ends or logical paradoxes in the event chains.

## **5\. Sequential Execution / Terminal MCP**

* **Target:** Safe local terminal execution environment  
* **Primary Function:** Allows Fable 5 to run build commands, execute the scripts in your .claude/skills/ directory, and parse compiler errors.  
* **Why it's essential:** When Fable 5 writes the Steamworks API integration (Facepunch.Steamworks), it needs to execute a test build to see if the native libraries (steam\_api64.dll) copied correctly. This MCP allows the AI to run dotnet build, read the console output, and self-correct any compiler errors autonomously.

## **6\. Git / Version Control MCP**

* **Target:** git-mcp  
* **Primary Function:** Enables autonomous branching, staging, committing, and diff analysis.  
* **Why it's essential:** Because Fable 5 will be making sweeping changes to probability matrices and Utility AI weights, it should never experiment directly on the main branch. This MCP allows the AI to say, "I'm going to test a new Need decay formula, so I'll create a branch, run the simulate\_utility\_decay skill, and if it fails, I'll discard the branch."

## **7\. Memory MCP (Foundation)**

* **Target:** `@modelcontextprotocol/server-memory` (via npx)  
* **Primary Function:** A knowledge graph-based system that provides persistent, cross-session memory for Fable 5.  
* **Why it's essential:** Enables Fable 5 to remember game design decisions, architectural rules, and project-specific choices across multiple terminal sessions.

## **8\. Filesystem MCP (Foundation)**

* **Target:** `@modelcontextprotocol/server-filesystem` (via npx)  
* **Primary Function:** Grants secure read, write, and directory management access to the local project files.  
* **Why it's essential:** Provides a robust, standard interface for the AI to interact with the project's codebase, ensuring it can read and write files reliably.