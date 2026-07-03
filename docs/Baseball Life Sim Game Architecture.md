# Architectural and Engineering Specification for "Dirt & Diamonds"

A Hybrid Life and Baseball Simulation

## Table of Contents

* [Part I: Concept and Vision](#part-i-concept-and-vision)
  * Executive Summary and Systemic Paradigm Shift
  * Architectural Synthesis
* [Part II: Technical Infrastructure](#part-ii-technical-infrastructure)
  * Development Orchestration via the Claude Code Infrastructure
  * Project Tree Structure and Codebase Topography
* [Part III: Core Simulation Engines](#part-iii-core-simulation-engines)
  * The Deep Baseball Simulation Engine
  * The Utility-Based AI and Life Simulation Mechanics
* [Part IV: Gameplay Systems and Narrative](#part-iv-gameplay-systems-and-narrative)
  * Lifecycle, Relationships, and Generational Legacy
  * The Gritty Event Framework: Consequence and Cascading Logic
  * Economic Scarcity and Hustle Mechanics
* [Part V: Publishing](#part-v-publishing)
  * Platform Deployment and Steamworks Architecture

## Part I: Concept and Vision

### **1\. Executive Summary and Systemic Paradigm Shift**

The conceptualization of "Dirt & Diamonds" represents an unprecedented synthesis of two immensely complex simulation paradigms: the rigorously statistical, stochastic world of deep sports management (analogous to *Out of the Park Baseball*) and the emergent, utility-driven architectural framework of advanced life simulators (analogous to *The Sims*). The fundamental design restricts the player's agency to a single entity—a baseball player navigating a turbulent career—while the surrounding universe operates entirely autonomously. This universe encompasses complete league rosters, deep statistical generation, complex interpersonal relationships, and multi-generational legacy mechanics.

Diverging from traditional, sanitized sports simulators, the architecture of this title necessitates a "gritty" event framework. The simulation forces the player's avatar to navigate severe moral, legal, and personal dilemmas. Decisions regarding substance abuse, performance-enhancing drugs (steroids), illicit side-hustles (narcotics trafficking, fencing stolen goods), and deeply personal crises (such as unplanned pregnancies and abortions) form the core of the narrative engine. These choices are not isolated narrative text boxes; they are deeply integrated mechanical levers that directly mutate the underlying mathematical matrices dictating on-field baseball performance and life simulation utility values.

The ultimate objective is to guide a player from the nascent stages of high school athletics, through the collegiate level and minor leagues, and ultimately into the Major League Baseball Hall of Fame. However, the simulation features a strict generational continuation mechanic. The game only terminates when the player's lineage ceases to produce capable baseball players, demanding that the user balance elite athletic progression with the complex social requirements of raising an heir.

### **2\. Architectural Synthesis**

"Dirt & Diamonds" represents a masterclass in systemic design, requiring the flawless orchestration of deterministic sports modeling and emergent narrative life simulation. By leveraging the Claude Code environment—specifically utilizing Opus for macro-statistical design, Sonnet for logical refactoring, and Fable 5 for direct file manipulation—the immense scope of this project can be rigorously and successfully managed.

The foundational architecture relies on strict memory management within CLAUDE.md, ensuring the AI agents do not suffer context degradation while navigating the C\# framework. The strict separation of concerns between the Utility-Based AI managing the chaotic life simulation and the rigid Monte Carlo and Markov Chain engines driving the baseball statistics guarantees that the application remains performant. By abstracting gritty, illicit elements into engaging mechanical loops, and anchoring all actions to a persistent, multi-generational SQLite database, the game will offer a deeply replayable, highly dynamic simulation where every off-field decision echoes throughout a generational baseball legacy.

## Part II: Technical Infrastructure

### **3\. Development Orchestration via the Claude Code Infrastructure**

To successfully compile a project characterized by overlapping deterministic and stochastic systems, the development environment must be strictly orchestrated utilizing the Claude Code ecosystem. The development pipeline requires a tri-model approach: Claude Opus 4.8 will serve as the macro-architectural planner for complex mathematical modeling and probability matrices; Claude Sonnet 5 will handle rapid logical iterations and syntax refactoring; and the newly released Claude Fable 5 will serve as the primary execution agent, handling granular code generation, direct file manipulation, and project assembly.

#### **3.1 The Hierarchical Memory Architecture**

A critical component of this ecosystem is the memory architecture. Claude Code does not operate with a single, undifferentiated monolithic memory; rather, it utilizes a highly specific hierarchical system of memory layers that dictate how the AI agents interpret the project's parameters. Understanding and configuring this system is the most vital step prior to writing the first line of application code.

| Location Path | Scope Classification | Priority Level | Architectural Functionality |
| :---- | :---- | :---- | :---- |
| /Library/.../CLAUDE.md | Managed Policy | 1 (Highest) | Organization-wide policies and system root overrides. |
| ./CLAUDE.md | Project Memory | 2 | Shared architectural standards, core rules, and primary directives. |
| ./.claude/rules/\*.md | Project Rules | 3 | Modular, path-specific rules to maintain a clean root configuration file. |
| \~/.claude/CLAUDE.md | User Memory | 4 | Developer-specific habits applied globally across all local projects. |
| ./CLAUDE.local.md | Project Local | 6 | Private development notes intentionally excluded from version control. |
| .../memory/MEMORY.md | Auto Memory | 7 (Lowest) | Claude's automatic notes, strictly capped at a 200-line index limit. |

The separation between explicit memory and learned memory forms the foundation of the Fable 5 workflow. The CLAUDE.md file serves as the explicit, durable instruction layer written by the engineering team. It operates as a hidden policy engine, forcing compliance regarding coding conventions, architectural standards, testing protocols, and API rules. Because the AI loads this file at the start of every session, it must remain highly concise to prevent context bloat and reasoning degradation.

Conversely, the memory.md file represents the "Auto Memory." Relying on Auto Memory for core game logic is a critical vulnerability; explicit rules regarding the life simulation and baseball engines must be codified in the Project Memory.

#### **3.2 Formulating the CLAUDE.md Directives for Fable 5**

Before initiating Fable 5, the following strict directives must be encoded into the project's root ./CLAUDE.md:

1. **Macro-Architecture Paradigm:** The project mandates a strict separation of concerns. The baseball simulation must utilize a highly optimized Entity-Component-System (ECS). Conversely, the life simulation logic must operate on a Utility-Based AI architecture.  
2. **Data Persistence Standards:** All statistical generation, relationship graphs, and historical legacy data must be persisted via SQLite.  
3. **Language and Framework Selection:** The codebase will be constructed in C\# utilizing the Godot 4+ engine.  
4. **Platform Integration Protocol:** The project demands strict adherence to the Facepunch.Steamworks library for Steam API integration. Fable 5 is explicitly forbidden from implementing Steamworks.NET.

#### **3.3 Required Model Context Protocol (MCP) Servers and Developer Extensions**

To maximize Fable 5's autonomy, specific Model Context Protocol (MCP) servers must be initialized:

* **SQLite Database Explorer MCP:** Allows Fable 5 to execute raw SQL queries and dynamically validate schema creation.  
* **C\# Abstract Syntax Tree (AST) Linter MCP:** Ensures Fable 5 adheres to strict C\# memory management protocols (structs, object pooling, and deterministic memory allocation).  
* **File System Manipulation Extensions:** Grants Claude Code the requisite permissions to create deep, modular directory structures.

### **4\. Project Tree Structure and Codebase Topography**

The organization of the codebase must strictly separate the high-frequency, frame-dependent life simulation loops from the heavily optimized, batch-processed baseball simulation loops.

| Directory Path | Primary Purpose | Key Component Examples |
| :---- | :---- | :---- |
| /Assets/Core/ | Global entry points, main loops, and state management. | GameManager.cs, TimeManager.cs, GlobalState.cs |
| /Assets/Simulation/Life/ | Utility-Based AI, Need decay formulas, and Relationship Graph. | NeedsEngine.cs, UtilityCalculator.cs, RelationshipGraph.cs |
| /Assets/Simulation/Baseball/ | Stochastic modeling, Markov chains, Monte Carlo engines. | AtBatResolver.cs, LeagueSimulator.cs, StatsNormalizer.cs |
| /Assets/Data/Database/ | SQLite configurations, schemas, and ORM layer. | DatabaseManager.cs, SchemaDefinitions.sql, PlayerQueries.cs |
| /Assets/Narrative/Events/ | Gritty event state machine, branching paths, consequence dispatchers. | EventDispatcher.cs, ConditionEvaluator.cs |
| /Assets/Economy/Hustles/ | Isolated state machines and UI for economic mini-games. | TexasHoldemLogic.cs, NarcoticsTerritoryManager.cs |
| /Assets/Platform/Steam/ | Facepunch.Steamworks wrappers and achievements. | SteamIntegration.cs, AchievementManager.cs |
| /.claude/rules/ | Modular Claude Code rules for Fable 5's context windows. | database\_rules.md, ui\_conventions.md |

## Part III: Core Simulation Engines

### **5\. The Deep Baseball Simulation Engine**

While the life simulation provides the narrative context, the deepest and most computationally expensive part of the game is the baseball simulation itself. The engine must simulate thousands of other players across high school, college, minor, and major leagues simultaneously.

#### **5.1 Relational Database Architecture and Schemas**

A robust relational database is mandatory to track the immense volume of data generated by a full baseball universe. SQLite provides zero-configuration setup, local storage, and high-speed execution of complex joins.

| Database Table Name | Primary Key | Critical Foreign Keys & Fields | Architectural Purpose |
| :---- | :---- | :---- | :---- |
| Players | player\_id | name, age, team\_id, recklessness, health\_ceiling | Core biographical data and hidden life-sim attributes. |
| Batting\_Stats | stat\_id | player\_id, season\_year, PA, HR, AVG, OPS | Historical and active batting statistics. |
| Pitching\_Stats | stat\_id | player\_id, season\_year, IP, ERA, WHIP, SO | Historical and active pitching statistics. |
| Relationships | rel\_id | player\_1\_id, player\_2\_id, affinity\_score | Bidirectional graph mapping all connections. |
| Game\_Logs | log\_id | game\_id, player\_id, event\_type, inning | Pitch-by-pitch data for Hall of Fame legacy tracking. |

#### **5.2 Macro-Simulation: The Stochastic Monte Carlo Framework**

For games where the user's avatar is not active, the engine utilizes a highly optimized Monte Carlo simulation framework. The Monte Carlo model estimates offensive production by generating random sequences of plate appearances. The engine calculates the percentage chance of specific outcomes by comparing the batter's underlying database attributes against the pitcher's attributes and the defensive team's fielding percentages. This allows the game to simulate an entire day's worth of league games in milliseconds.

#### **5.3 Micro-Simulation: Markov Chain Resolution**

When the user's avatar is directly involved in a game, the macro-simulation pauses, and the game enters the Micro-Simulation phase operating on a strict Markov Chain model.

In baseball mathematics, there are exactly 25 distinct base-out states in any half-inning (e.g., State 1: 0 outs, bases empty; State 25: 3 outs). The engine forms a transition matrix ![image1][image1] of size 25x25 for each potential baseball event ![image2][image2]. The mathematical entry ![image3][image3] represents the exact statistical probability of transitioning to state ![image4][image4] given that the current game state is ![image5][image5] and event ![image2][image2] occurs.

#### **5.4 Performance Enhancing Drugs (PEDs) and Attribute Mutation**

The utilization of steroids or other PEDs provides a temporary, massive artificial multiplier to the power and stamina variables utilized in both the Monte Carlo and Markov Chain matrices. However, utilizing PEDs introduces an accumulative hidden variable called detection\_risk and applies a permanent, slow-acting decay to the player's health\_ceiling attribute.

### **6\. The Utility-Based AI and Life Simulation Mechanics**

Both the non-player characters (NPCs) and the player's own avatar operate on a robust Utility-Based AI architecture.

#### **6.1 The Needs Engine and Non-Linear Decay**

The protagonist and NPCs track five primary biological and psychological needs: Hunger, Sleep, Hygiene, Social, and Fitness. Each need possesses a unique float value that decays at an independent, non-linear rate.

The mathematical model programmed for calculating the decay of a specific need ![image6][image6] at time ![image7][image7] is expressed as:

![image8][image8]Where ![image9][image9] represents the base decay rate, ![image10][image10] represents an environmental multiplier, and ![image11][image11] represents the current emotional or stress state.

#### **6.2 Utility Calculation, Scoring, and Action Selection**

The AI evaluates potential actions by computing a mathematical utility score for each available option. The general form of the utility function for an action ![image12][image12] evaluated in state ![image13][image13] is computed as:

![image14][image14]Where ![image15][image15] represents individual considerations (time cost, financial cost, event risk) and ![image16][image16] represents the weighted psychological importance.

#### **6.3 Integrating Emotions, Toxicity, and Stress Overlays**

An "Emotions" layer acts as active modifiers to available interaction menus. If the player engages in illicit hustles, an invisible "Toxicity" or "Stress" variable rapidly accumulates. Emotions fundamentally alter the Utility AI's weights (![image16][image16]). A player in a "Highly Tense" state might exhibit forced autonomous behaviors, such as skipping batting practice or autonomously seeking alcohol.

## Part IV: Gameplay Systems and Narrative

### **7\. Lifecycle, Relationships, and Generational Legacy**

The simulation spans the entirety of a human lifecycle through strictly defined phases: High School, College, Minor Leagues, and Major Leagues.

#### **7.1 The Bidirectional Relationship Graph**

Social relationships are mapped as a complex bidirectional directed graph. Every entity is represented as a node. Edges represent the affinity\_score and relationship\_type. The Utility AI continuously reads and adjusts based on this graph. A high rivalry score with a teammate negatively modifies the probability of on-field success, bridging off-field behavior with on-field performance.

#### **7.2 The Strict Generational Succession Mechanic**

The game continues indefinitely *only* if the player successfully produces an heir who actively pursues a career in baseball. When a child is born, they are initialized as an autonomous NPC with a genetic blend of hidden stats. The system checks the child's hidden baseball\_interest variable, secretly modified over the years by parenting interactions. Upon the main character's retirement, if an heir is eligible, control shifts to this new character. If no heir is eligible, the simulation definitively terminates.

### **8\. The Gritty Event Framework: Consequence and Cascading Logic**

The architecture forces players to navigate severe moral, legal, and personal dilemmas that permanently alter their trajectory.

#### **8.1 State-Driven Triggers and Conditionals**

Events are procedurally driven by the player's underlying database state. An event contains:

1. **Prerequisites:** Boolean evaluations (e.g., Funds \< $500 AND Recklessness \> 75).  
2. **Probability Weight:** Percentage chance of firing.  
3. **Narrative Payload:** The descriptive prompt.  
4. **Branching Choices:** Options containing hidden state modifiers and nested events.

#### **8.2 Cause, Effect, and Ripple Mechanics**

Choices trigger cascading events. Accepting a bribe generates a persistent compromised\_syndicate boolean flag. This flag serves as a prerequisite for secondary, vastly more destructive events months or years down the line. Unintended pregnancies require decisions regarding child support or abortion, causing massive fluctuations in Stress utility loops and permanently altering relationship graphs.

### **9\. Economic Scarcity and Hustle Mechanics**

Extreme financial scarcity is a primary driver of the early-to-mid gameplay loops. The player must balance Fitness and Sleep against the urgent need to generate income through isolated state-machine mini-games ("Hustles").

#### **9.1 Legal Employment and Minimum Wage Struggles**

Standard employment options (bartending, retail) provide low payouts. Mechanically, these are time-skip events that drastically drain the Sleep and Energy needs.

#### **9.2 The Narcotics Hustle: Territory Control Architecture**

The illicit drug-selling hustle abstracts the process into a strategic resource management game:

1. **The Drop:** Inventory/temporal slot management without exceeding a suspicion\_threshold.  
2. **The Cut:** Risk-reward abstraction. Higher profit multipliers increase the probability of a "toxic" product, damaging local demand\_value and triggering police raids.  
3. **The Corner & Territory:** A node-based territory control map using the Relationship Graph to assign NPC "runners."

#### **9.3 Fencing Operations and Texas Hold'em**

* **Fencing:** A simulated commodities market. Selling items with high heat\_levels requires parsing the Relationship Graph and engaging in a negotiation mini-game based on Charisma.  
* **Texas Hold'em:** A statistically accurate mathematical card simulation. AI opponents assess pot odds and incorporate randomized "bluffing" coefficients based on personality traits.

## Part V: Publishing

### **10\. Platform Deployment and Steamworks Architecture**

Because "Dirt & Diamonds" is targeted exclusively for PC deployment via Steam, integrating Valve's infrastructure is a foundational requirement.

#### **10.1 The Facepunch.Steamworks Paradigm**

The engineering mandate requires the use of Facepunch.Steamworks for C\# integration, drastically reducing boilerplate code. This library handles:

* **Cloud Saves:** Critical for preserving multi-megabyte SQLite generational databases.  
* **Achievement Hooks:** Linked directly to the Event Dispatcher for both baseball and life milestones.  
* **Rich Presence:** Displaying the player's current life phase and Markov state on their Steam profile.

#### **10.2 Cross-Platform Compilation and Native Library Resolution**

To ensure cross-platform compatibility (particularly for Linux/Steam Deck), the build pipeline must be configured to preserve native libraries directly in the .csproj file. The C\# initialization sequence must utilize a Native Library Resolver (NativeLibrary.SetDllImportResolver) to map system paths correctly across Windows (steam\_api64.dll) and Linux (libsteam\_api.so).

[image1]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAB0AAAAaCAYAAABLlle3AAAB80lEQVR4Xu2UwSsEcRTHd1ukkMSa7M7O7MymbVeK9sCBiyhbyMHdwWGVclhJyU3OSkrh4qJIOXHe+APcpOSwF1dnF3y/6/djeq3saqzD+tanmfm9N++9+b03v0CgXhVKJBLdtm33OI5jkEwm0yidtMLhcCt9NdFotBPLQen3nWqf1HXddsuypuPx+BWCvCqy0o9SCS/Bs2IV7w1Jv4qEpB0IsA9uCALlpA8FvznYTuFzTZLJZJv0qVgIkELANVzPFFvSB3YX6wvgAuwQ6VOVUP0sgmTBIcHzkbaxvwRri0jcB3sxFovNEG+MqvUnSRFondvHLVbbXODQ0IbnCYKkw7iOw/YIUkTGqVgcBgTYNAyjhVNM8PzAX4e/Er+QwDXI4nRBuqgfSVWd5z0SjhBuIbcSyebxH5oE9822GiLxvgPO2SLitX0p3U/e623jl4Il2Ca1HxOzmDK9bECBe/pdYSuvWifl0cU+bXCIuGB/Hm9FcOw9Dr1D9BEBYu9R3AmPQ8L2YM32+pSEF8fAneIF3IJBPSAMwp6qAg4UT/b7EVmAfYAwlpqDbf0bwb6C52WZ01chSR7cI9EUSafTTVgOST8/Veqn2pVdgq8dNU2zXzr6qdonjUQiXUzKObDUaYZe55C0V/r6qaDqYele8av9/Fed6w04hpltqhO1hAAAAABJRU5ErkJggg==>

[image2]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAsAAAAeCAYAAAD6t+QOAAABFElEQVR4Xu2SvWrCUBiGI+ogKg41RPOfUAliwSG7UwYXvQEXF9tZOrR3IF17A95Ar6B0cXLo4A94K86+78kJKRHBbBV84eHkfOfhnO8coij/J2EYlolt2z0wIr7vN7KeiGmaBnFd98NxnAPRdb2Z9URyyUkgv0D8IpiWsutJCgS9LsEbyQp/c73M/giO30CMCO5QwfiM1uaEryVkFEOC4h5Sh2A+w3yKDXaStpDxMZH8QniXtDzPG5ztnFf+lBzBmlBU4ounCYKgjv5WxLKsMXgi7B81n+sEajGfzAKO3Uq6OL5POMerPGJ8JXxKyhF2+SaqqtYMw3ggEH6wtkB9SETPuWQ+iaZpVZJeO65f/KfvudGcAMdwXyy1waNRAAAAAElFTkSuQmCC>

[image3]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEcAAAAbCAYAAAAu/JKTAAAEO0lEQVR4Xu2YX4gVVRzHZ9k1lJQS3S7unzv33t1adlep5RZWWEIZJLgi4kOgGBqxQYvERm1uIoH4kEIbERTpS4GGIViktQ+CorBBLz6ZgojsSy8++NxL9vnOnLM7/py5d9Ld2GC+8OXOnN+f8zu/c87vnLlBUGDxoq+vbwU/rbZ9vlGpVJZ6Wtmixf8pOa09PT1PhGG4plqtlsR6vb7EKnm0t7cvl65nZ2fnKppbHBuCILeJ2I0GOfQfFrVa7TGR/o4SZ5eVN4WMy+XyMEFfwsldx81WT3CJ+QX+5fgBduutXhq6urp60T8hyo+VK3hk0/jbKFp5XmD7Hn5OJ1cL46vTdqzRpGcC45UYfwOviDgesToCejuQ/YDOZdFtjzxowXYS29dFKxTwtwbZR729ve2ilecFPt7A187g3pXZovFl9d0QGPYT/Lgy7njY6iCv0f4WPAe/EK1OFtCtYj/lt62V/xfo7u7eShwneWxzzIciOQ1QiYvkZnhc5P1bL9M+FWl7hwEOIp9RR2LSRyPIt7ZjcH9gUSEnYS+j8xn176mELDdcvTosEuPbabUFWT/87V9PEEYTWhlaPW4FXfRFk/fXRAb3PL+bkP3pOuq3frLgfN630mjfICIbxf8eeCqIj/jcxzwDDbHb72sVvi5oMlL0Suj9zu/TopWnQkUVZ4dKpdKjBDos8n5TznTEa8WIqKqoTfjEpZ04WQjjGR237fgdEemnG53vef7E6jQDdjs1sVo9bgVdV8KtnuKl/UdYF608FUVyGiCMt8iYnuVU5H2G30GCfdN3yvPS0BVjY1+FZ0R0tiVlHsiOpyXHw8VwjTq21sryAv+bRPlJqykPlBwNKHR71AUp3oTvVhL3AjcrUTGes47QRmdfibI1sghKTJPkjCKf0oVUK1i0Os3g+8DX6SDlNHI75Gze5EQnBQYHUK6pIZz7LJiBJ5NV381KVIxnPQSzhe6UqE8J9DaoSCZ13PaZPQE9fCLweT6Mt8ernkEc28c8f83teplo7T0Sq1qMdoGFG9e0xurHmwqUXoHXHf+Gf8AhX0s00HJ8bMvhMcc7YfxpcRH5M6J8KRlwUnR3ifd53mf6ewH+qtlL3qo1KEfduo/ACX91QNzG+3fwdrMThsR1ondVVDxWLhDbc8h/blovwyI52cmZT9DhGLwhEtiWgYGBRwJzT3FfxmdVcDOKbiu2K4N4q98D2neHTe5V5XjLR9+FHR0dq61cQDYq2vaFhC/Gg26lfcngX2Im11lF5LvgQdHKsqA6g/6HtuZoFYVzK//FML7Vfy4GKQlWLUTnhK4MVrZg0CwpOX6p8jxOgCMM5Emr6z5BPhUJdMjK0+A+U5617fjYiI9bIs8/0e9kxikXHTqV+G+M7Ua2sCiS0xgtrsbMvgcNvov8P3PaKgT8uJXPN5RYkcTsDVK2W4ECBQoUWMT4BzuvbSW7qb2VAAAAAElFTkSuQmCC>

[image4]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAoAAAAZCAYAAAAIcL+IAAAAz0lEQVR4XmNgGKJATk7OV15e/hgIS0tLC6PLw4GioqKdgoJCIQgbGxuzossTB1RUVPhAGGhdFdCkSnFxcW4QRlGkpKTED3RXPQjLyMgIARUuBGooAmHyFAIVuAAFrUBYXV2dF0gfBuJoEEZRiAyAkppAE28Bfa4PwujycCArK+sHVHgKaIsgCKPLwwHQxElAPAddHA5ERUV5QBio6ABBt0HxVRCNLg8HBBUCBc8BcSDQ4eUgDGRvBoYlJ7o6kMLrQF9uBNKLQRjIlkBXM3IBAABwMbbMy4TGAAAAAElFTkSuQmCC>

[image5]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAcAAAAeCAYAAADgiwSAAAAAqElEQVR4XmNgGGAgJyeXKi8vPxuEgVxmkiSdYRhFgizADDQmDoQVFBQKjY2NWUEYLAO03BuIg6B4KxBbgjBMMkpRUVEdhIHsw0ATlECYsCQIyMrK+oEw0M5VQC4LFIMBI1DlfBAG6oxWV1fnBWEGaCDglgTaJQ4UPA7CQAXGQKPTQRhstLS0tDBQYg8IAwU7gIr1QRhsIV5JEAAKcoCwqKgoD1xwFBABAA+SL8Y1PC0kAAAAAElFTkSuQmCC>

[image6]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABcAAAAaCAYAAABctMd+AAABg0lEQVR4Xu1TPUsDURC8YBBFURTPw/v+AvGwEbEQbASLFBZioyBaCIKIlaU2QjrBTrRJn79hJdrYaivYC5YWwRl4R9ZHIofHgYgDw0tmZyeX3XuG8VcwkCTJdBAEMzxJatIQhuEQ65LUpKcfqgt3XXcSxg003IEdEt8b0gNtzvf9G5wv4BFp2/aU9PQFjWi4Bh8V25Dr0oPwNej7UisENC7iaQ/RfKz45nnevPSwTp/UCgFNe+AKQiPFV4Sd53U18yY8E6KtGKoMrzEIi3X4mUTQFfgURZFFsqZ+jPXi4NMg6MwQC+Rsob0jcJvkMnFuibZiyJepyXWEt8F7xVN9mXEcj0Pb5cik/gX5vHUdTQ2Efije6vO2LGsE+oLx3aiqCs+X13Qcx9WL/NtiLK1cx3KHSWirPW8pnmIdxWfFDr4/4K2Y1X1B91LtCG2TRM8BzhPpL4uaaZqjJIIv9VtcGkH3orUQvpSm6Zju+TGqDl9WvFC76Pm2lEKWZYNGFcH/+F34BCHHaojLyjXuAAAAAElFTkSuQmCC>

[image7]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAcAAAAfCAYAAAAr19clAAAAu0lEQVR4XmNgGGAgLS0tIyUlJQLC6HI4JVlAWEFBYZW8vLwnCMNlFBUVxUEYKHgciDVBmLAk0ChzOTm5chAGsm8BcSIIy8jISIMkBYASeSAM1LEaZoqxsTErfkkQAApOguIiuENAQFxcnBsouBmEgbptiJcECigBJU6AMCiEtLS02EAYbCdQ0gXoqF0grKKiwgdUlAPCysrKYvglgc5WB3L2gTBQYTuQ9gZhuL2ioqI8MIzkHCIkRwEeAAAbejig9RJywgAAAABJRU5ErkJggg==>

[image8]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAmwAAAA1CAYAAAD8i7czAAALVklEQVR4Xu3dbawUVx3H8V0vao1PVEWECztzH4RAtbbiQ2nU1AKpD8GQPqS1DxCtBmuxPoJ9sNa0JUErpRIKCFRSDWltKm1iVdo02qjRRl70jdXG+MIY6jtDYgzvjP5+O2eWs+fO7gXu7t7FfD/Jye6cMzM758xwz3/PnFlqNQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAC9sHTp0tfrZSTNt25lwyLP83Oc0vx+G0Db1MNn1NMCoMqKFSteOX/+/Nem+QCAITBv3rzXZVm2wMnvo6J6mT86OvpmL0dlTQp01ql8U1WZLV68+D0qv90dQVrWK9732NjY/PQ443q5vOoYlK/ibOf4+Pgb07J+63fb+Lz4/JTL/pxGo7Gq1uFc9YM+f26Ha6spPnedztHZJq5zmjr9OxoWbn8d552qwwVpGQBgCKgjX68/1PuVTiitLPP1/rsLFy58S7xuSdvs0B/2j6T52ua5uHNetGjRqPJ+5g45Xq+XtP9lOp49ej2mY/pWlP9p5a+OVm3xsbsOtagD1fK4tvmTXldEq54xj9xpf5uV7lN6Sfv9elzer7bRfl+jgPAdab7M0eftqg0waFCdt+gzf+J6xiOZ4Zzt0OvLOtb3xtuc7VSnnUpHVb9zyzzXMVxvQy+9TgEAw6HuIEcdzLuV/hx36CH4qezc9Uf9SEWg4VG5nRV5P9C+rknye0bHslYd4id87K5DyG7Wy0FRvG4wR2UPpwFn2McfOwWpZyqM9j1X0RH2pW20z5UO2tJ80zH8XeVjaX43FefZ3L55mhkLAes9+sxb4gAmjORsyopg9sVet/dMqe3e5JTmT0xMLJ5uFDA61we1WFfbne9z4S8Bao/PpesPIx/rbIw6AwC6cGfpTrVWBDEO3I6VHbqWr05Wb1HZY3qZEy2/X3/or9e29+v9ZZOTk6+OyjYq/0C53Gva9+2NYnRspdIJ58X1SlZvBiA6pl97mzJP76/U+oeUfuf3Kn9bvM1MdAnY+tI22t9X0rySA7b8NANEtVeWdOAO1r6kz7k8yptidHR0UbimPqr0N6UFzs+L0c0Vbu8ysEk2nVWuq45tp+sd57vOtWmOVdstU/qn0nVe1jbbPC8sBIFVXx6Gjv/tqO7vSvMBALMo/ubv22jqaI5nxby0Zln72ie5Q4uXtY+5SpdpHx9M5+q4w1bZM9HqLaGD29cpTRdchGDoXr+GkYwj041ohGDht/HIjraZVN7vlTY7oJtuJOV0dAvYurXNmdL+Hk7zSg6Q9JmP1yoC2W603Z4QtJ1SsGbaZrXWvdrt7UCxvJ6Ut2FiYuKt2seLer8x3W4YOMDS8e4tg7ZQ367BmoVR2v8o/VTpIaWt6TrDzgGmr8s0HwAwi/SH+Qp1TB8Ii57jdEDpeQctjWgOTqpD8HFd1eTy0GE/meb3gvY7rnRLueyOVWml8tZH9WrjfK3z8/hYvZ+sh/PXYt0Ctj60jc+hA7JK/qysGAFalpZ144BW2+7NixGzaYM103o3e6QmKybde4TNQUDdwZrndGn5WD/au1fKoE3H+TUHqWl5lax9/tpIqHPzNrCX29ceXjr+tWkeAGAWuQOOb9Vk4bai/mBvqUUjClpelUdPj1UEH/VGcXtrij4EJS3uWOLOxaNAOs4fqw5b43r59lz53p2o0q/igG26+WsquzGrGAEsU9w2qTMN2Kb7zHR9C6Mjz6b55rZpFLetPVexOYpa8vmNlys4+PAThM+c6vymrJi/dq6T3h/1aJrSRS7z+6x389dOKxA6jdFTzzG8VukFHW+eFqbK85xFt7jDCF1zuoG/BEWrnxb/RIv2u9vvw3m8IW9/iONB5R0Jo9vl7eiuo9PdxP+mAABDQH+Yv5Dm+Q+9OoDj5XK4XXhxLQrg8uS2m0dSlPcX7e88pS/W2oO9tVmXUZ8z5Q5Sn7kt/N5YS1bMw2vN45qcnHyDjmFNVL5M2/0hC3OqQt6zDiJ8O9Wda9nx9cI0AVvP2yZLbld7pMj1axRBeBmU/6MRRrei89uJg7XWqJqDkMbJW6Qd5dEcSF8vjeK26HiYXzjldqiDTV9HteLaqWv/S/Lip2M8QnexguqFLve59HnyflT+KZVdGm/jNvU5D6OCq3y8Wu+S5cuXv8r7UTo0TaDoLx9X5dGcNdc1BF8duY2U/hXq0OTASXmHw1O7dZeF4/IXDQezDtg9EtwKOl1nr1Muh/XXeF3XW/v7cHI7/53Ku788vvJ6D7+p1tYmTlrvQz4PnX5zzddrOCYAwGzLi9+L8hybE3p/Wy2a0zRW/DbWD8N7vc2ucIfr/HId5f0iDpTCnKTfKG1NO/KseChgSrAyE9rn55VeVvqv0hPJsd0XdTi+RXitlu8qy8MIxdPxz0mofg9ovQeVtscd7kxlxSjZL7NiXtPxPBmJ60fb5MXISjPQyIsnNZ9Q2leOLIVJ9c8r3aPyPAvnt20nEZeneb4utM2tab5lRYDlzzzeCF8I9H5zXswp9Pl4KSvO21/zELSF68xP+T6l90v1uknbXqkAY55eDzaKkUEHvb517adLPffvHC2v1+vcsN+7tXyVn+b0+sq/SGldo/j5kP1ue9Vdi41Hu42yhe1uqoU2LGkfd6ZfDiy04SNK/w71Oqq0r1HcfvayR9zmhHo5qPODLQ7UD/u61ea3uT5hX+tCvfaU62v5kyHo8kjl5Xr9bBZ9IQm3bj1f0CNurevd+8ySNsmKKRDf1Os3vE25j5iDzHh0GgBwFgijEh6R8eTp+EGCp9Lf+gqdYFsnF0YEDqXrDtCIg0l1TnvjzLyYixXfFqy74+vWkfdav9pG+xyLA9iq/1XBnbtHEf00b3R+Z4tHtJq/6+dRn/DwyyG3T7gVeLhRBGbN4E6v326EgNzn0a+N4kGSF0Iw4h8N9lO+Iy53YFKONjWKgG9/65MHKPxbutTH77pkxZOkdb3/no/f5ywrRlub0wscaHnZ+S5X/ivCaO32+JpxG0Xnu3W9h/212kRpQfiy4t8s9BzGyocofB5qHcoAAENMf9zvcOfhWy9lnpavd0cQr1fFT426Q6qd5lOJvZQVAcl33GGWeeE24OPpaOAg9bFtHASc8vyl8vym+YMSgpBHHExpcSTMJ2wGkA5MHLyUQVoIXg6Onby16MDGtxVXZ+0/jzInBH4/CiNGb8+LUWWPzHlEq3U7fJDKQE1pl+ugVw8tHtD781V2id4/5Drq/WN6XePlWjEHbqPqeF65vkeHfXvT+3S75e3z2ZrXe3iwo61Nwm1k356ufKAo/EberAS0AICZ8S2W7Uo3u9MrM/2H3Z2o8i+MV46V3+bHppn7028OXnScd6T5WXF7acocvkHod9uobrvjBy26aJ3ftGCQdI5u0jHcqNcNIZje7QBGbbQjzAPbF34fzLcJfXvzmiVLljj4+L6DHben3m/T+/ep/DMOhrTtXUpPNor5Yg5Im4Gs0q2DHEmNNEfTsmIOZTNoCse5Q+mGuaKyXa6H0gUhkL1b6eMqfzQEoM1gLAujc96pg9X4Q8rrXeljaZuEIO7eeP1YVtyCPaUnYgEAw2ekqoMLQceWvJhDNEVePBXY8enJQcqr/4N3d6AbGrPw0xL9bpsQ5Hw1HlXsovL8DlpyrPVkudMoZJxfr5hIn+6n07UwKOXxdqrPtEJ9mg9Z6Bxf2Cge8mkT1TFtE29X+VRtoxip+/IwXAsAAAD/FzwHUUHW01n4uQ8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIAZ+h8j7KRAg8W58AAAAABJRU5ErkJggg==>

[image9]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABIAAAAbCAYAAABxwd+fAAABOElEQVR4Xu2TPUrEUBSFZ1ALEcZCIZD/hFgoCkJAUASFAbGwUkT34ArExsbSxs5KtHEB4h5srdzAoLgEO7+DbzBzC3kDwUYPfEXuuffkzZ28TudvqCiKDHaCIJix3lgqy3I2y7I7eIjjeFrYHm+labpJ0CBJkmVhfW+5Uz3leX4urO+t1oIkBShMKNj63mJPNSHvQjuzvrcY3iPkw3FlfW+1EsTgPju6hlPB8wsfadDs4UV9vNVmbUQMHcCj++cWHa8M7Tb7CFoKw3C+WZO6NB4Jhp51TVx9UlC7Z/BGfYKvvaK24Z6/RcA2xpsgZG3E/PKP8QYEbAmdmtqt/bntBdV1PaXb/sON70ZRNFdVVU8QtA4XqtvGsUTIGTs75HQr1vPVcPmXcELQgm3wVWtBQ01or7b4r1/UJwOMTc+Cf2Z/AAAAAElFTkSuQmCC>

[image10]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEQAAAAbCAYAAADFyymQAAADYUlEQVR4Xu2YzUtVQRiHvWjRd1ndLum953ivF0Slz0vUwj6xUqgIKloUSUUk1SKCyCwqsv4BkT60L1vYB26LllJ/QLto08JN29Ztqt/jeSdPY7ZRAy/nBw9nzsw7c2Ze33lnrhUViRIlmoIq6+vrV4VhuDqfz2egVCrN8Y2c0un0ImwdtbW1K1Sd8u1mrQqFwtIgCPbX1dW91wJ/Gu2+HTJnvBXfjcvqt9m3m/WSQ6q1uH7xEbTIs74Nkt1htb2WzQdoaGhY7NuUhbS4Ri32ip7Dxl3fRu0F1Z8Wb0Qv+DZlo8QhnrQNDmqB7eIR6H3QtZFgQXWdckqz2kdzudwBiI9RVtIiu4kAosQiZYQESpved4McskXPVrV9JaLAH6csRGLU4noymcxCThvQ+xeOX45jIgNkmsJxzlnOYWWnxCGeLPwvUZYzWoA8Qb6QIzp08cqCyvNCS6j+GNOsFH8kO9KndOGzMSr9+n/KJVTKLjcQIeK82tqcHU7BUTOdTIlMkjpM5Z5jEf+Uefttkwnvsw2uk1CpCMev5KNiKH6FjyfU3yOYtLVyat8H9KeOvlrcNiApZ7PZ+cVicUmsrpp6ldeBuqT0DIMosb8D9VnDWCxObXuMMVvGo7/BgdBC/5qampUgh54MowvkLuyNvwsj8dn4IT6JDS43aLBXQXTE4pwB41sYXetH1L4eGIsIExdtTHihhaf17NWki6DyTtEjDmncG6Byn9k9AS0mYz8NbotjwDtOkf0zPZeDyif0cyNgO5PjQHUvxRm+ydYG7MRNlZf5658gm3jikOkWi9Akhnm6C1xTU9Nc1V0AZ6e8swlHYadJ3ge9N1poj+UL1VXLtMq1gd7Z0jjsuWy6jL3Yqe+CINoq3KAHyDd8231TdrfU1ure/4v0wZIm81jFqlg1i3ogWlyFbM5pgp2+A/RsU9s1YCxFDKfZII4zZ28UQ7TFxq9k4dSJI6A+Vy2nNJNvXEK13BbvO7OyEO9TBGwNLcw1uR1BlFy7VT5qdBE9Fil3gP5h9NtozCGy2a5nXvSr3AEsiFMNO/7aQD2JFUeJe4BD1HYce/6dAap7qLZTsl3rz3vGlDhkEhGiFROPNraRw4l2LkqTXpbsqP/Dhi3ictS45bhifeLyv50oUaJEif6HfgFby0N1GALVZgAAAABJRU5ErkJggg==>

[image11]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEsAAAAaCAYAAAD/nKG4AAADf0lEQVR4Xu2XTWtTQRSGE1qlfi6qMW2S9uZLo0FbIaC0ilRUpAvFjwqFijtbQSluWqnVlYgoiiKiUIRaoaII6kbcuHAl+gP8A24EFwouC4rPmzuTDNeiWSSC5r7wMmfOnDNz58w5M0kkEiLE30BLLpdb53le558Yi8VWBp2bDWGwakUqlWpPp9Nj3d3dHwnIJxF5HA7BSfpPDRfgSNC/6ZBIJNYSiA/wshgcFwjoFGODQX3TgQwqEYhvXV1dB0XpyLhlmUymx9oQrFOyq3o1KVSGpvyyonT0+6S3NvQz8Xh8RdWrSREGq3ZECdAswfhK+0AkSE/of0beGTRuariXu9Ulk8k19Oe5s+JWRwDbrNy0sJe7+9Kp3NCPIraq/AzP049WPesDHpQEaz02HJeOdg+Hs1V0bTk8TwSPGJtzD9D6uPZ1h3tfBcdAlPEpUXdYcLBeYP6roi172qIyXgzYjYmMn6Td6I5ZH1dXd4TBqg0qqajuJvh6kZdOgdqvlBf1uwtdC3K/SkekJHr5yH35fH418i7DXvnaOcRsNruBuQ7h21mZHb1stUbaL6k51kjCvNawvtZWOvhSZM1JGxjZB3wipVJpiajvMQeg77Z/2/r1PSpba2fWWBw4DGL8XkT+DheQX9DOGL6DX+APeESUHxvajt1x+m9EZSPtBPpbhUJhlUj/jg0Y8hkRuyECGqOdtRkjPX4DtLvhQ5EDyNEeVeCYIy6aT9bBpT3z14vgbDHB8GTv+nh+QMrzKYisuRe5z9rB++hGzVybxEpg6gk2uZyFTthykC5tykd/skUFRBnCxjd7ftbOKytNIJ/ZfwlWb+QJsVgsLjUb08vsZpZe6JTmFm0VOPYVH8bPIb8SkSf5vmHYZrI2qaDqsKSzczcMNjiiAuCcqP3oa/R7PP8UK/81FTxt1OjLY9LTXjcbGvb8V3cau2PKHtGuq/W0tmh1xn/a9UG+jXxANCYKuMqwnEXo79FvdaZoHMJg1QhTSjPOvVNZHHnQ8BI8rLKhvSuywQHsbkpnif6CNkj7nPGzYkdHh+6lG/A0G18v2rXT/qs9IjqfpHVlX/HhoLZ5pqyZfwccRd/umbtX8zj+DYd7KuVTc/pBlO8d3S3Bgd+gxX2hJLPhEryi7BRd44i//i8+YmDdsl3EuQf/O5ApBTLiLbwY8Q+q8SUUIkSIEP8ofgIH9SmNFZJfIAAAAABJRU5ErkJggg==>

[image12]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAsAAAAdCAYAAAB8I5agAAAAzElEQVR4Xu2OMQrCMBSGK11EJ0UMJGmTmjPo5AEEPYDgJoibuAkewVFnB2cHJz2GOLh5Dnf/P7RQOtlFEPrg4/H+fHlJEFT184qiyBljdiSO4wXoCSGapOh+JydJMiCQzlrrNoE4wfxGHxIvOue6CO/EWjvKvdJH9sBFRXwIYYrwSaSUnUzGPANXnNdJeRn/2TDMDnLyHmyxoEEQhdy8BCdCyaYF8QXGWLYi/tVSslJKI7wROGv0Q8oFHMGcZN9jhQQbWui1lOJc1d/XB3c9P2ek2a6lAAAAAElFTkSuQmCC>

[image13]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAoAAAAZCAYAAAAIcL+IAAAAuklEQVR4XmNgGAXEABkZGU4QlpeXjwLiWQoKCpEgbGxszIqsjgUo2QPF3urq6rxAejMIy8nJuZCuECggCcRXoThYRUVFFEgXgzCIDVcIdRvYBCD+D8R/ge4rBGGgNCNMHaOioqIZEIuDsKysrDJQYR8QnwdhaWlpYbAqoKQ+UOA9EHuCMEgM6C4bIHsuCAO5LGCFIB1AK1YBcQYIAxXlARWsAxogD8Iwa2GAEaQBqkkAXRIZEK1wFFAGAF+4MKWMfMIcAAAAAElFTkSuQmCC>
### 10.2 Cross-Platform Compilation and Native Library Resolution

To ensure cross-platform compatibility (particularly for Linux/Steam Deck), the build pipeline must be configured to preserve native libraries directly in the .csproj file. The C# initialization sequence must utilize a Native Library Resolver (NativeLibrary.SetDllImportResolver) to map system paths correctly across Windows (steam_api64.dll) and Linux (libsteam_api.so).

[image1]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAB0AAAAaCAYAAABLlle3AAAB80lEQVR4Xu2UwSsEcRTHd1ukkMSa7M7O7MymbVeK9sCBiyhbyMHdwWGVclhJyU3OSkrh4qJIOXHe+APcpOSwF1dnF3y/6/djeq3saqzD+tanmfm9N++9+b03v0CgXhVKJBLdtm33OI5jkEwm0yidtMLhcCt9NdFotBPLQen3nWqf1HXddsuypuPx+BWCvCqy0o9SCS/Bs2IV7w1Jv4qEpB0IsA9uCALlpA8FvznYTuFzTZLJZJv0qVgIkELANVzPFFvSB3YX6wvgAuwQ6VOVUP0sgmTBIcHzkbaxvwRri0jcB3sxFovNEG+MqvUnSRFondvHLVbbXODQ0IbnCYKkw7iOw/YIUkTGqVgcBgTYNAyjhVNM8PzAX4e/Er+QwDXI4nRBuqgfSVWd5z0SjhBuIbcSyebxH5oE9822GiLxvgPO2SLitX0p3U/e623jl4Il2Ca1HxOzmDK9bECBe/pdYSuvWifl0cU+bXCIuGB/Hm9FcOw9Dr1D9BEBYu9R3AmPQ8L2YM32+pSEF8fAneIF3IJBPSAMwp6qAg4UT/b7EVmAfYAwlpqDbf0bwb6C52WZ01chSR7cI9EUSafTTVgOST8/Veqn2pVdgq8dNU2zXzr6qdonjUQiXUzKObDUaYZe55C0V/r6qaDqYele8av9/Fed6w04hpltqhO1hAAAAABJRU5ErkJggg==
[image2]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAsAAAAeCAYAAAD6t+QOAAABFElEQVR4Xu2SvWrCUBiGI+ogKg41RPOfUAliwSG7UwYXvQEXF9tZOrR3IF17A95Ar6B0cXLo4A94K86+78kJKRHBbBV84eHkfOfhnO8coij/J2EYlolt2z0wIr7vN7KeiGmaBnFd98NxnAPRdb2Z9URyyUkgv0D8IpiWsutJCgS9LsEbyQp/c73M/giO30CMCO5QwfiM1uaEryVkFEOC4h5Sh2A+w3yKDXaStpDxMZH8QniXtDzPG5ztnFf+lBzBmlBU4ounCYKgjv5WxLKsMXgi7B81n+sEajGfzAKO3Uq6OL5POMerPGJ8JXxKyhF2+SaqqtYMw3ggEH6wtkB9SETPuWQ+iaZpVZJeO65f/KfvudGcAMdwXyy1waNRAAAAAElFTkSuQmCC
[image3]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEcAAAAbCAYAAAAu/JKTAAAEO0lEQVR4Xu2YX4gVVRzHZ9k1lJQS3S7unzv33t1adlep5RZWWEIZJLgi4kOgGBqxQYvERm1uIoH4kEIbERTpS4GGIViktQ+CorBBLz6ZgojsSy8++NxL9vnOnLM7/py5d9Ld2GC+8OXOnN+f8zu/c87vnLlBUGDxoq+vbwU/rbZ9vlGpVJZ6Wtmixf8pOa09PT1PhGG4plqtlsR6vb7EKnm0t7cvl65nZ2fnKppbHBuCILeJ2I0GOfQfFrVa7TGR/o4SZ5eVN4WMy+XyMEFfwsldx81WT3CJ+QX+5fgBduutXhq6urp60T8hyo+VK3hk0/jbKFp5XmD7Hn5OJ1cL46vTdqzRpGcC45UYfwOviDgesToCejuQ/YDOZdFtjzxowXYS29dFKxTwtwbZR729ve2ilecFPt7A187g3pXZovFl9d0QGPYT/Lgy7njY6iCv0f4WPAe/EK1OFtCtYj/lt62V/xfo7u7eShwneWxzzIciOQ1QiYvkZnhc5P1bL9M+FWl7hwEOIp9RR2LSRyPIt7ZjcH9gUSEnYS+j8xn176mELDdcvTosEuPbabUFWT/87V9PEEYTWhlaPW4FXfRFk/fXRAb3PL+bkP3pOuq3frLgfN630mjfICIbxf8eeCqIj/jcxzwDDbHb72sVvi5oMlL0Suj9zu/TopWnQkUVZ4dKpdKjBDos8n5TznTEa8WIqKqoTfjEpZ04WQjjGR237fgdEemnG53vef7E6jQDdjs1sVo9bgVdV8KtnuKl/UdYF608FUVyGiCMt8iYnuVU5H2G30GCfdN3yvPS0BVjY1+FZ0R0tiVlHsiOpyXHw8VwjTq21sryAv+bRPlJqykPlBwNKHR71AUp3oTvVhL3AjcrUTGes47QRmdfibI1sghKTJPkjCKf0oVUK1i0Os3g+8DX6SDlNHI75Gze5EQnBQYHUK6pIZz7LJiBJ5NV381KVIxnPQSzhe6UqE8J9DaoSCZ13PaZPQE9fCLweT6Mt8ernkEc28c8f83teplo7T0Sq1qMdoGFG9e0xurHmwqUXoHXHf+Gf8AhX0s00HJ8bMvhMcc7YfxpcRH5M6J8KRlwUnR3ifd53mf6ewH+qtlL3qo1KEfduo/ACX91QNzG+3fwdrMThsR1ondVVDxWLhDbc8h/blovwyI52cmZT9DhGLwhEtiWgYGBRwJzT3FfxmdVcDOKbiu2K4N4q98D2neHTe5V5XjLR9+FHR0dq61cQDYq2vaFhC/Gg26lfcngX2Im11lF5LvgQdHKsqA6g/6HtuZoFYVzK//FML7Vfy4GKQlWLUTnhK4MVrZg0CwpOX6p8jxOgCMM5Emr6z5BPhUJdMjK0+A+U5617fjYiI9bIs8/0e9kxikXHTqV+G+M7Ua2sCiS0xgtrsbMvgcNvov8P3PaKgT8uJXPN5RYkcTsDVK2W4ECBQoUWMT4BzuvbSW7qb2VAAAAAElFTkSuQmCC
[image4]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAoAAAAZCAYAAAAIcL+IAAAAz0lEQVR4XmNgGKJATk7OV15e/hgIS0tLC6PLw4GioqKdgoJCIQgbGxuzossTB1RUVPhAGGhdFdCkSnFxcW4QRlGkpKTED3RXPQjLyMgIARUuBGooAmHyFAIVuAAFrUBYXV2dF0gfBuJoEEZRiAyAkppAE28Bfa4PwujycCArK+sHVHgKaIsgCKPLwwHQxElAPAddHA5ERUV5QBio6ABBt0HxVRCNLg8HBBUCBc8BcSDQ4eUgDGRvBoYlJ7o6kMLrQF9uBNKLQRjIlkBXM3IBAABwMbbMy4TGAAAAAElFTkSuQmCC
[image5]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAcAAAAeCAYAAADgiwSAAAAAqElEQVR4XmNgGGAgJyeXKi8vPxuEgVxmkiSdYRhFgizADDQmDoQVFBQKjY2NWUEYLAO03BuIg6B4KxBbgjBMMkpRUVEdhIHsw0ATlECYsCQIyMrK+oEw0M5VQC4LFIMBI1DlfBAG6oxWV1fnBWEGaCDglgTaJQ4UPA7CQAXGQKPTQRhstLS0tDBQYg8IAwU7gIr1QRhsIV5JEAAKcoCwqKgoD1xwFBABAA+SL8Y1PC0kAAAAAElFTkSuQmCC
[image6]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABcAAAAaCAYAAABctMd+AAABg0lEQVR4Xu1TPUsDURC8YBBFURTPw/v+AvGwEbEQbASLFBZioyBaCIKIlaU2QjrBTrRJn79hJdrYaivYC5YWwRl4R9ZHIofHgYgDw0tmZyeX3XuG8VcwkCTJdBAEMzxJatIQhuEQ65LUpKcfqgt3XXcSxg003IEdEt8b0gNtzvf9G5wv4BFp2/aU9PQFjWi4Bh8V25Dr0oPwNej7UisENC7iaQ/RfKz45nnevPSwTp/UCgFNe+AKQiPFV4Sd53U18yY8E6KtGKoMrzEIi3X4mUTQFfgURZFFsqZ+jPXi4NMg6MwQC+Rsob0jcJvkMnFuibZiyJepyXWEt8F7xVN9mXEcj0Pb5cik/gX5vHUdTQ2Efije6vO2LGsE+oLx3aiqCs+X13Qcx9WL/NtiLK1cx3KHSWirPW8pnmIdxWfFDr4/4K2Y1X1B91LtCG2TRM8BzhPpL4uaaZqjJIIv9VtcGkH3orUQvpSm6Zju+TGqDl9WvFC76Pm2lEKWZYNGFcH/+F34BCHHaojLyjXuAAAAAElFTkSuQmCC
[image7]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAcAAAAfCAYAAAAr19clAAAAu0lEQVR4XmNgGGAgLS0tIyUlJQLC6HI4JVlAWEFBYZW8vLwnCMNlFBUVxUEYKHgciDVBmLAk0ChzOTm5chAGsm8BcSIIy8jISIMkBYASeSAM1LEaZoqxsTErfkkQAApOguIiuENAQFxcnBsouBmEgbptiJcECigBJU6AMCiEtLS02EAYbCdQ0gXoqF0grKKiwgdUlAPCysrKYvglgc5WB3L2gTBQYTuQ9gZhuL2ioqI8MIzkHCIkRwEeAAAbejig9RJywgAAAABJRU5ErkJggg==
[image8]: data:image/png;base64,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
[image9]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABIAAAAbCAYAAABxwd+fAAABOElEQVR4Xu2TPUrEUBSFZ1ALEcZCIZD/hFgoCkJAUASFAbGwUkT34ArExsbSxs5KtHEB4h5srdzAoLgEO7+DbzBzC3kDwUYPfEXuuffkzZ28TudvqCiKDHaCIJix3lgqy3I2y7I7eIjjeFrYHm+labpJ0CBJkmVhfW+5Uz3leX4urO+t1oIkBShMKNj63mJPNSHvQjuzvrcY3iPkw3FlfW+1EsTgPju6hlPB8wsfadDs4UV9vNVmbUQMHcCj++cWHa8M7Tb7CFoKw3C+WZO6NB4Jhp51TVx9UlC7Z/BGfYKvvaK24Z6/RcA2xpsgZG3E/PKP8QYEbAmdmtqt/bntBdV1PaXb/sON70ZRNFdVVU8QtA4XqtvGsUTIGTs75HQr1vPVcPmXcELQgm3wVWtBQ01or7b4r1/UJwOMTc+Cf2Z/AAAAAElFTkSuQmCC
[image10]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEQAAAAbCAYAAADFyymQAAADYUlEQVR4Xu2YzUtVQRiHvWjRd1ndLum953ivF0Slz0vUwj6xUqgIKloUSUUk1SKCyCwqsv4BkT60L1vYB26LllJ/QLto08JN29Ztqt/jeSdPY7ZRAy/nBw9nzsw7c2Ze33lnrhUViRIlmoIq6+vrV4VhuDqfz2egVCrN8Y2c0un0ImwdtbW1K1Sd8u1mrQqFwtIgCPbX1dW91wJ/Gu2+HTJnvBXfjcvqt9m3m/WSQ6q1uH7xEbTIs74Nkt1htb2WzQdoaGhY7NuUhbS4Ri32ip7Dxl3fRu0F1Z8Wb0Qv+DZlo8QhnrQNDmqB7eIR6H3QtZFgQXWdckqz2kdzudwBiI9RVtIiu4kAosQiZYQESpved4McskXPVrV9JaLAH6csRGLU4noymcxCThvQ+xeOX45jIgNkmsJxzlnOYWWnxCGeLPwvUZYzWoA8Qb6QIzp08cqCyvNCS6j+GNOsFH8kO9KndOGzMSr9+n/KJVTKLjcQIeK82tqcHU7BUTOdTIlMkjpM5Z5jEf+Uefttkwnvsw2uk1CpCMev5KNiKH6FjyfU3yOYtLVyat8H9KeOvlrcNiApZ7PZ+cVicUmsrpp6ldeBuqT0DIMosb8D9VnDWCxObXuMMVvGo7/BgdBC/5qampUgh54MowvkLuyNvwsj8dn4IT6JDS43aLBXQXTE4pwB41sYXetH1L4eGIsIExdtTHihhaf17NWki6DyTtEjDmncG6Byn9k9AS0mYz8NbotjwDtOkf0zPZeDyif0cyNgO5PjQHUvxRm+ydYG7MRNlZf5658gm3jikOkWi9Akhnm6C1xTU9Nc1V0AZ6e8swlHYadJ3ge9N1poj+UL1VXLtMq1gd7Z0jjsuWy6jL3Yqe+CINoq3KAHyDd8231TdrfU1ure/4v0wZIm81jFqlg1i3ogWlyFbM5pgp2+A/RsU9s1YCxFDKfZII4zZ28UQ7TFxq9k4dSJI6A+Vy2nNJNvXEK13BbvO7OyEO9TBGwNLcw1uR1BlFy7VT5qdBE9Fil3gP5h9NtozCGy2a5nXvSr3AEsiFMNO/7aQD2JFUeJe4BD1HYce/6dAap7qLZTsl3rz3vGlDhkEhGiFROPNraRw4l2LkqTXpbsqP/Dhi3ictS45bhifeLyv50oUaJEif6HfgFby0N1GALVZgAAAABJRU5ErkJggg==
[image11]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEsAAAAaCAYAAAD/nKG4AAADf0lEQVR4Xu2XTWtTQRSGE1qlfi6qMW2S9uZLo0FbIaC0ilRUpAvFjwqFijtbQSluWqnVlYgoiiKiUIRaoaII6kbcuHAl+gP8A24EFwouC4rPmzuTDNeiWSSC5r7wMmfOnDNz58w5M0kkEiLE30BLLpdb53le558Yi8VWBp2bDWGwakUqlWpPp9Nj3d3dHwnIJxF5HA7BSfpPDRfgSNC/6ZBIJNYSiA/wshgcFwjoFGODQX3TgQwqEYhvXV1dB0XpyLhlmUymx9oQrFOyq3o1KVSGpvyyonT0+6S3NvQz8Xh8RdWrSREGq3ZECdAswfhK+0AkSE/of0beGTRuariXu9Ulk8k19Oe5s+JWRwDbrNy0sJe7+9Kp3NCPIraq/AzP049WPesDHpQEaz02HJeOdg+Hs1V0bTk8TwSPGJtzD9D6uPZ1h3tfBcdAlPEpUXdYcLBeYP6roi172qIyXgzYjYmMn6Td6I5ZH1dXd4TBqg0qqajuJvh6kZdOgdqvlBf1uwtdC3K/SkekJHr5yH35fH418i7DXvnaOcRsNruBuQ7h21mZHb1stUbaL6k51kjCvNawvtZWOvhSZM1JGxjZB3wipVJpiajvMQeg77Z/2/r1PSpba2fWWBw4DGL8XkT+DheQX9DOGL6DX+APeESUHxvajt1x+m9EZSPtBPpbhUJhlUj/jg0Y8hkRuyECGqOdtRkjPX4DtLvhQ5EDyNEeVeCYIy6aT9bBpT3z14vgbDHB8GTv+nh+QMrzKYisuRe5z9rB++hGzVybxEpg6gk2uZyFTthykC5tykd/skUFRBnCxjd7ftbOKytNIJ/ZfwlWb+QJsVgsLjUb08vsZpZe6JTmFm0VOPYVH8bPIb8SkSf5vmHYZrI2qaDqsKSzczcMNjiiAuCcqP3oa/R7PP8UK/81FTxt1OjLY9LTXjcbGvb8V3cau2PKHtGuq/W0tmh1xn/a9UG+jXxANCYKuMqwnEXo79FvdaZoHMJg1QhTSjPOvVNZHHnQ8BI8rLKhvSuywQHsbkpnif6CNkj7nPGzYkdHh+6lG/A0G18v2rXT/qs9IjqfpHVlX/HhoLZ5pqyZfwccRd/umbtX8zj+DYd7KuVTc/pBlO8d3S3Bgd+gxX2hJLPhEryi7BRd44i//i8+YmDdsl3EuQf/O5ApBTLiLbwY8Q+q8SUUIkSIEP8ofgIH9SmNFZJfIAAAAABJRU5ErkJggg==
[image12]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAsAAAAdCAYAAAB8I5agAAAAzElEQVR4Xu2OMQrCMBSGK11EJ0UMJGmTmjPo5AEEPYDgJoibuAkewVFnB2cHJz2GOLh5Dnf/P7RQOtlFEPrg4/H+fHlJEFT184qiyBljdiSO4wXoCSGapOh+JydJMiCQzlrrNoE4wfxGHxIvOue6CO/EWjvKvdJH9sBFRXwIYYrwSaSUnUzGPANXnNdJeRn/2TDMDnLyHmyxoEEQhdy8BCdCyaYF8QXGWLYi/tVSslJKI7wROGv0Q8oFHMGcZN9jhQQbWui1lOJc1d/XB3c9P2ek2a6lAAAAAElFTkSuQmCC
[image13]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAoAAAAZCAYAAAAIcL+IAAAAuklEQVR4XmNgGAXEABkZGU4QlpeXjwLiWQoKCpEgbGxszIqsjgUo2QPF3urq6rxAejMIy8nJuZCuECggCcRXoThYRUVFFEgXgzCIDVcIdRvYBCD+D8R/ge4rBGGgNCNMHaOioqIZEIuDsKysrDJQYR8QnwdhaWlpYbAqoKQ+UOA9EHuCMEgM6C4bIHsuCAO5LGCFIB1AK1YBcQYIAxXlARWsAxogD8Iwa2GAEaQBqkkAXRIZEK1wFFAGAF+4MKWMfMIcAAAAAElFTkSuQmCC
[image14]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAmwAAABOCAYAAACdbkoxAAAJGUlEQVR4Xu3dbYhcVwHG8bsklmo10uq6ZHdmzszuaMgqpiW+kJAKakqzvmFfoIZoUUuMltjS1lSbUqGUxbbfmsRUkmJMS1AkskpapKVg0CLYgi8fiqVVqKL4sSj4oYLE5+k9N3tzvLN7dzIz2er/B4c795xz78ye/XAf7su5WQYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA//fWhBC2NpvNyU6ns6nb7a7T8kP+rLaxtDMAAABGbHJy8k2tVuvGdru9x+v6vG18fPzNWh5rNBpTaX8AAABcAAprDziobdiw4S2dTmcihLBF5UF9fq/aLk77AwAAYIQc0hTOjkxOTr5dy42qWqvlnMp9Ktek/QEAAHBhrI1L7lkDAAAAAAAAAAAAAAAAAAzFzMxMM4Twkspf2+32F1qt1vVVRW33+kEELY9r+QeVMy5a35HuEwAAAAOm4HWdw5eC2Z1ZzYcMPLWHyk5td6rRaLwxbQcAAFitxjwlRlp5PuL+1qT1AzYWz5j9W6HtqrRxKQ5sKlvS+teDzZs3v2FiYuKStL5sROMPAAD6tGZmZuYdCiPrXTzTvyu9LOqSCWQdevaq7tOluvPWbDbfp/3ud7hI2wZJv/vn8RLni51OJ6Ttvah/W9t9Lat5Zm5Ixqanp9+l33GzymFfwvVZP5V3ph0L6v9W9T2w3N9ajH9aDwAAVgEd7C/zgV8H6z8pk3y+OLBrfaPKd9V2S/k1TTqwX6l+D2WL85cNjL5vn77vs2n9IOnv+4C+5xWHNr+CatgBcVBiYDytsqCyNeRBeoeWR/U/eX/aPxrT3/hN9bk2baji8U/rAADAKqGD+qU+C5PWq26uvO6zOao7oYDwnnL9oDgYav9P+BVRadsgKeh82YEtljvS9tVIv/MvDtRZcoZP9Xv9poZyXcH/J/+/6t575/Ef9tgDAIA+6aC+S6FtulyncHCx6u8r12n9dpX5cl2s96XTe7TNXel+elEw2KBtHnRRsJgp6rWPPap7pNx3GHwmUd/1okObls9OTU29Le1Th7bfr795d3GmzuHJZ/Gy/NLxLi/P3WLFvB+/KquTNpjqP5zWFdR2Sr9tW1pvVWMf64c+9gAAYOV82exYce9awcFLQeb+cp3WjztQlet0gP+4yoIvrartcn1+pdxeRX2u8L58I7wCw6e0/qNS25zanqq6ST7kl2kPqxzpVbTtZ9LtevHlXW3zasjPtB1J25fjkObLkdr2uRAfSNByPuShx+8lPZ2O60rFs44vZ30EPwfRkL8j9Ryqu6I89g7nRVuvsQcAABdQvBz6XEX9dh28byhVOYCcVP0niwqFia7DRDvOUebLaVr//eIm1bSPr4d46VPLufI+9Xmzyo/PN+jU5LB6Z4hPjqaNy4l//xb/zSqd+KDG6ZCfWXMwOpjVuNfPwU9jeFtV2PR4aD//TOvr0Ha/VFmf1nv8q8Y+to1q7AEAQF06cG/Sgfsfab0P6g4LxbrPuqjf00m4cvB6vriHSuvbvF6096J+VzkghTwo/dqXJ0ttowxsxX15nrLjTFYjXKW03bx+77Esv3TpM4AvFeOmzzeV+yqQfVB1W8t15suxqv+dykLaFvJA+Gpab/Hp3puL9W63O67v2JnFv0NtvwnVgc3jfyYkYx/bRjb2AACgJp8d04H77+W6GCDm05vV1fd4ObB5Xf1OZosBwfe4PbHUJbXiHi/P+6W+H1P5o8oXi/Z4ifFUVWiIU1ocChWXQoui33d9ut1yfGZL2z2Z1teh7/xFWDyj5su5z2pfl8b1c+Zt0/rGNCAVZmdnL6p6YlX7mtZ2f0vrTd91g9q3F+u+LB2fGH3t8mnIz/adE9iK8Q8VY2+hx9gDAIALSAfojsoLxXq8PPeAD+rlfqZ+B3xWrbQ+79Dmz57vq53PcbZf5SMOe+r7HX2+Oyvdf6X1R1SejqFurfp8L5SCjQNhKIXAYYvh5ZleQWo52vb5IsR6bEK8by3Oj3Y28PryaSt/AGClk9N6jB72+JYrPWaq353Fse12u+s0/ld7WerzTKt0ljTWvTb+WcXYx/aRjT0AAKjPl/I8EesulZt00P9hr3m9fCZKfR7NYkhwyJEFHfi/pOUBlYMqjzuwlALcC53SVBHqe2OIDwc4PGj9lmJ/FvKnLs+GwmHy79fv+Em/Yc30e+9Q+anKXv3uYyF/AGGfQ2/RR/XvVvmo6h7Sd20qb19HnPz2ZY9VHLdva/mVLIY/j7faPhfyV2/dXmynz98PyRxsxfhXjX1sH8nYAwCA4RlTULjXoSFtqOKgpgBwa5aEgl6GOSlvWQxAJxxw0rYlFK/jqvW3pPRdGzUWD2dD+tva+TQsR1ulaVXipe2ToceUICmPfzak3wcAAEYohp3DCgONtC2l8LC77tkr79eBpm7/fvmSr78n5BPm1g5fPkumUHRbWl9XyM9gnj37NWgOatr/Y+n9g538kq/nzlsyiBXjn9YDAIDXKYe1GHgGRmFoj8rlaf2g6Xfvc8lWENZM2/ws9PHyd5/5clEY+lY/29fhM3/a//aQ3y/4X/T9V6v9E2l92ajGHwAAYFkKNQernsbsJV7W9fs4+5oEVyHofm27oOU3VvK9K6Hf9yt9x6E6Zz0BAABWNYWa6xRufhAqpgNJiueb+7OW/wqL7xzdm+4PAAAAA+R7vHwmykGsn+I54NJ9AgAAAAAAAAAAAAAAAAAAAACA/zHdbne83W7vzJaZTNbiRMHXpPUAAAAYokajcVl8X+qSk+eq31Q7f3fnibQNAAAAQ9Ltdtd55n8v07YqCmvr1f94Wg8AAIAhmZ2dvUgB7C4Fsbm0rQqBDQAAYMT8Xk+FsKOeSDeu79D6PeWitlv9fk63E9gAAABGzEFNIeyxiYmJS9K2lO9hU/+vqv9vfd9b2g4AAIDBW6MAtl0B7O60AQAAAKtAs9m8UmHt0NTUVCNtAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAALC6/QejnORziaDohgAAAABJRU5ErkJggg==
[image15]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABYAAAAcCAYAAABlL09dAAABl0lEQVR4Xu2Vvy/EYBjHe3ESLCSEuPZa7SERg6FEXCxCxGKTkDBYRFzEYLIbWA3OYDJIbP4FsfkDxHrDJf4Cg83nkbe0j16Oc93um3zSt8+P79u+79s7y+qoiXJBEEwKnudVoOq67obgOE6vbdtcnAlBNzbUGMLoAe4NZRglvCYwvmaCp2KxOCfo/lRROEtjHYNdbnOGhMgfwnOhUBgSdD5VbTeWNRNoeIFTK8UwEsuwKGtuNZg4rpyYGerg64K4yC9hvqPjP8TO2hTXDLdWk6f4tTIz5rVCDN8MxzrfsjBbgHeBSdZ1PlKpVBoWqKvoHKdonng5EcQsIPgqULCfSMZEblOgfkXn6J3yfd9LBDMzRnmKrwQaH/l96NcFslzk9wRLbS6bPy5nm2FXPP4pMRMouMOkxvWISbYE7i+5HpjGr2Zqpg3L5C944plvxxTxBQ5StBo1hmHYrWtEfM59AqYDcEPPiK5JKDPjv0o2TvaHYV7n/iWMt712fVi8ek8ET3smp0bXtKQsjc8989fF+KTRBneUrT4Axwpr8ISZn5cAAAAASUVORK5CYII=
[image16]: data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABcAAAAaCAYAAABctMd+AAABc0lEQVR4Xu2STStGQRzFR5cSFpTr1n1/S8lCukUkG2sWts8HYMFGFLKwY6WwsJSFZGVpY2Ep1qx9A1/BOY+ZaZpEPU83FtfUr5k59z9n5s6MEI0a/Rv1ZFm2SJIkOcnzfFz58suM7/ujZVm6JE3TvSAIQjPgJ9UXHsfxAkI3CPqbmHwL20E/J/BfscAU2mnJB/xlO+codeDytFi3H76d8UXavLwisZQ3G/MvgnGA+bRRTe/gB9yRXCPbumK/GsCXZ/Iax76Fp1h7ckW/a3jsU7IdjxEUH4nF3TsRB8jMA7vnmiXlCjv9cnC6Rg1EthsxUAAAAASUVORK5CYII=