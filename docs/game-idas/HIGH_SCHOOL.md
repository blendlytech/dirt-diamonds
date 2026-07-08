# High School Phase - Technical Specification

## 1. Overview

The High School phase serves as the foundation of the player's cradle-to-grave
baseball career and personal life. It introduces core mechanics, establishes
the player's background, and forces them to balance academics, athletics,
relationships, and moral choices.

**Generational Succession (The Core Loop):** The overarching game does not end
when the initial player retires. Any offspring the player has (even those born
during High School via life-altering events) can also become baseball players.
The user can take control of these heirs to continue the legacy. The game only
truly ends if there are no more offspring playing baseball.

## 2. Architecture & Separation of Concerns

Following project rules, this phase is divided into distinct, decoupled engines:

* **Sports Simulation Engine:** Purely handles baseball statistics, game
  outcomes, physical stat decay/growth, and practice results.
* **Narrative & Event Generation Engine:** Handles dynamic life events,
  branching choices, and NPC interactions. *Crucially, all generated events
  must provide a minimum of three distinct choices with hidden probability
  outcomes tied to the player's internal stats.*

## 3. Data Structures & State Management

To handle the massive array of variables efficiently, group the player's state
into categorized, optimized sub-schemas:

### 3.1 Initial Setup (The Backstory)

* **Parents & Background:** Randomly generated income level and background.
  Determines initial social status, finances, and starting equipment.
* **Base Traits:** Player-chosen traits combined with randomly assigned
  physical attributes (looks, body type). Unattractive players have reduced
  initial opportunities but can compensate by grinding personality stats via
  activities.

### 3.2 Core Stat Categories

Consolidate the sprawling individual stats into manageable JSON objects:

* **Athletic & Health:** Weight, Body Fat, Batting Average, Home Runs, RBIs.
* **Academic & Mental:** GPA, Test Scores, Intelligence, Wisdom, Maturity,
  Stress, Happiness.
* **Social & Personality:** Charisma, Humor, Charm, Confidence, Reputation,
  Social Status, Leadership, Teamwork.
* **Moral & Ethical:** Alignment (Good/Neutral/Troubled), Integrity, Discipline,
  Responsibility, Recklessness, Patience, Humility, Generosity, Kindness.
* **Financial:** Cash, Savings, Income, Expenses.
* **Relationships:** Scalable mappings of `NPC_ID` to `Friendship`, `Romance`,
  and `Rivalry` values.

## 4. Core Game Loop & Mechanics

### 4.1 Time Management (The Calendar)

* **Day Cycle:** Split into distinct periods (e.g., Morning/School,
  Afternoon/Practice, Evening/Free Time).
* **Season Timeline:** Follows a realistic high school baseball season calendar.
* **Trade-offs:** Every action costs time. Going to church builds `Morality`
  but sacrifices `Athletics`. Playing video games builds `Happiness` but
  increases `Laziness`.

### 4.2 The Smartphone Interface (UI Hub)

* **Functionality:** The central text-based UI for the game. Serves as the hub
  for messages, calendar, relationships, and skill development activities.
* **Hardware Tier:** The tier of the phone is tied to wealth. A lower-quality
  phone will have fewer features and options for the player. Better phones
  unlock more features and run smoother UI transitions.
* **Phone Plans & Minutes:** The player must manage their phone connectivity.
  They have to purchase minutes to use features, unless they have wealthy
  parents who can afford an unlimited plan. Alternatively, players can
  connect to free Wi-Fi in specific locations to make calls and texts
  without spending minutes. Note that home Wi-Fi access is determined by
  the family's poverty level—the poorest families will not have home
  internet, forcing the player to travel to other locations for free Wi-Fi.

### 4.3 Emergent Relationships & NPCs

* **Autonomy:** NPCs must have their own simulated lives, forming independent
  friendships, rivalries, and romantic interests without player input.
* **Long-Term Impact:** Relationships can persist into college and professional
  phases (e.g., marrying a high school sweetheart or carrying a grudge).

### 4.4 Morality & Consequences

* **Peer Pressure:** Engaging in drinking, drugs, or illegal activities offers
  short-term social boosts or financial gain but carries severe hidden
  probabilities (stat decay, DUIs, unwanted pregnancy, team suspension, jail).
* **Jobs:** Poor players are forced to work to afford basic gear, reducing free
  time and increasing the temptation for illegal income.

### 4.5 Transportation & Independence

* **Transportation Status:** A player's initial transportation is determined by
  family wealth. Wealthy players may be gifted a car, saving them time and
  money. Poor players will have to purchase their own car or bike, but doing
  so provides unique stat boosts (e.g., Work Ethic, Discipline, Responsibility)
  that wealthy players do not inherently receive.

### 4.6 The Economy & Marketplace

* **Marketplace Hub:** An in-game storefront where players can purchase tiered
  items typical for high schoolers (e.g., cars, bikes, clothes, jewelry, food,
  and baseball gear).
* **Wealth Impact:** Wealthy parents may autonomously purchase high-quality items
  for the player. Poorer players must save up to buy these items themselves.
* **Status Modifiers:** Purchased items directly affect the player's underlying
  stats. For example, buying high-quality clothing, jewelry, or a fancy car
  provides passive bonuses to `Attractiveness`, `Social Status`, and
  `Reputation`. This allows a naturally less attractive player to compensate
  through strategic purchases.

### 4.7 Dating & Romance Mechanics

* **Stat Drain vs. Stat Boost:** Partners vary in their impact. A supportive
  partner may boost `GPA` and lower `Stress` but slightly hurt `Social Status`.
  A high-maintenance partner boosts `Reputation` but drains `Cash` and `Time`,
  causing baseball stats to slowly decay due to missed practice.
* **Clubhouse Cancer (The Rival's Ex):** Dating the ex of a teammate or rival
  drastically lowers the `Teamwork` stat, decreases team morale, and triggers
  confrontation events.
* **Parental Approval & Cut-offs:** Dating someone from a different social caste
  or someone with low `Morality` may cause parents to disapprove. Defying them
  can result in revoked car privileges, phone downgrades, or allowance cuts.
* **The "Sneaking Out" Risk-Reward:** Sneaking out at night provides massive
  boosts to `Romance` and `Happiness`. However, it triggers a hidden probability
  check (based on `Recklessness` and `Luck`). Getting caught leads to grounding
  (losing UI access) or extreme fatigue on game day, crippling `Athletics`.
* **Life-Altering Events (Pregnancies & Child Rearing):** Serious relationships
  carry a chance of an unplanned pregnancy. If funds allow, players can choose
  to abort, heavily impacting stats like morality, relationships, and public
  perception/fame. If they choose to keep the baby, the player must financially
  support and raise the child. The child developing into a baseball player to
  continue the game's lineage is *not* guaranteed; their traits develop
  dynamically over time based entirely on how the player raises them.
* **The Hometown Anchor:** As graduation approaches, the partner's autonomous
  stats determine if they stay local or move. If the player gets drafted or
  attends a distant college, a long-distance relationship decay mechanic begins,
  costing minutes and time to maintain.

## 5. Actionable Implementation Tasks for Claude

To begin building this phase, execute the following epics in order:

* [ ] **Epic 1: Data Schema Definition**
  Create optimized TypeScript interfaces (or equivalent) for `PlayerState`,
  `NPCState`, and `WorldState` encompassing the stats listed in Section 3.
* [ ] **Epic 2: The Initialization Engine**
  Implement the backstory generation logic, parent randomization, and initial
  trait assignment.
* [ ] **Epic 3: Smartphone UI Implementation**
  Create the clean, modular text-based interface components for the phone hub
  (adhering to the UI implementation rule).
* [ ] **Epic 4: The Time Manager**
  Build the day/week cycle loop that enforces the baseball schedule and daily
  action limits.
* [ ] **Epic 5: Narrative Event Generator**
  Draft the first set of branching High School events (Tutorial), ensuring the
  minimum 3-choice rule and strict decoupling from the sports engine.
