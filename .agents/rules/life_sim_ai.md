# **Utility-Based AI & Life Simulation Rules**

## **Needs Engine**

Track five core needs: Hunger, Sleep, Hygiene, Social, Fitness.

Needs decay using non-linear math. Decay accelerates as the value approaches zero to simulate desperation.

Formula logic: New_Need = Old_Need - (Base_Decay Environmental_Multiplier * Stress_Modifier).

Action Selection (Utility Calculation)
NPCs and the player's autonomous actions are chosen by calculating the highest utility score.

Utility = Sum of (Consideration * Weight).

Considerations include: Need Deficit (how hungry are they?), Temporal Cost (how long does it take?), Financial Cost, and Risk factor.

Stress & Emotion Overlay
High stress/toxicity from gritty events (e.g., police raids, blackmail) alters the Utility weights.

High stress can override queued actions, forcing the entity to autonomously seek stress-relief actions (alcohol, arguments) regardless of temporal cost.
