# Dungeon Keeper 2 Reimplementation

## Mission

Create a behaviorally faithful reimplementation of Dungeon Keeper 2
using C# and Unity.

## Critical rule

Do not invent original DK2 behavior when it can be researched.

Distinguish:

- CONFIRMED
- STRONGLY INFERRED
- HYPOTHESIS
- UNKNOWN

## Existing research

**The binary is the primary source. Go to Ghidra when in doubt.**
(Owner, 2026-09-04.) `DKII.EXE` is the actual DK2: every other written source is
somebody's reading of it. So when a question is open, decompile before you
infer, and before you take a community document's word for it.

Before implementing DK2 behavior, check, in this order:

1. **The binary, via Ghidra** — `reverse-engineering/README.md` has the method
   and ADR-0046 has the rule. Facts may cross into `docs/research/`; decompiler
   output may never cross, anywhere, at all.
2. **The shipped data files** — also the actual DK2, and cheaper to read.
3. **Original game experiments** — the running install is the oracle for
   anything observable, and it outranks a misread disassembly.
4. OpenKeeper
5. Flame
6. Ember
7. DK2 reverse-engineering documentation, and the DK2 wiki (CC BY-SA: facts in
   our own words with citations, never its prose)

## Architecture

Game simulation must remain independent from Unity wherever practical.

Unity is presentation/integration.

## Original assets

Original DK2 assets may be used for development/reference.

Do not assume they are redistributable.

All asset access must go through an abstraction layer.

## Fidelity

Do not "fix" strange DK2 behavior without documenting the deviation.

## AI collaboration

If you discover something important:

DOCUMENT IT.

Do not leave important knowledge only inside chat history.

## Session handoff

Read docs/HANDOFF.md first: current state, next steps, decisions already made,
environment gotchas, and the research method in use.

## Before changing architecture

Read:

docs/ARCHITECTURE.md
docs/DECISIONS.md

## Before implementing a reverse-engineered mechanic

Read:

docs/RESEARCH_STATUS.md
docs/EXPERIMENTS.md







# Long-Term Project Goals

The project should be designed with the following long-term goals in mind.

These are **end-state objectives**, not requirements for the initial prototype. Development should proceed incrementally, but architectural decisions should avoid unnecessarily preventing these capabilities later.

---

## Goal 1 — Modern Dungeon Keeper 2 Engine

Create a modern C#/Unity implementation of the Dungeon Keeper 2 game engine.

The engine should reproduce the original game's core simulation, including:

* dungeon construction
* digging
* territory
* rooms
* creatures
* creature AI
* jobs
* needs
* economy
* combat
* spells
* traps
* doors
* heroes
* enemy dungeons
* level scripting
* campaign mechanics
* objectives
* saving/loading

The implementation does not need to reproduce the original source code or internal architecture. The target is **behavioral and gameplay fidelity**.

---

## Goal 2 — Original DK2 Campaign Compatibility

Where technically and legally appropriate, support loading and playing original Dungeon Keeper 2 campaign content.

Ideally, the engine should be capable of consuming original game data such as:

* maps
* terrain
* rooms
* creatures
* objects
* level configuration
* scripts
* objectives
* campaign progression

The long-term objective is to make it possible to take an original DK2 level and run it through the modern engine with minimal manual conversion.

Original DK2 assets may be used during development and testing, subject to applicable rights.

---

## Goal 3 — Modern Rendering of the Original Game

Separate the game simulation from the rendering system so that the original game can eventually be presented using a modern renderer.

The target is a visual presentation that could resemble a hypothetical modern remaster of Dungeon Keeper 2 while retaining the original gameplay.

Potential improvements include:

* modern lighting
* physically based materials
* improved shadows
* ambient occlusion
* modern particle effects
* improved water/lava
* improved visual effects
* high-resolution rendering
* 1440p/4K support
* ultrawide displays
* modern animation techniques
* modern UI

Rendering improvements must not require rewriting the underlying game simulation.

---

## Goal 4 — Replaceable Asset Pipeline

Create an asset abstraction layer that separates game logic from the actual visual and audio assets.

During development, original DK2 assets may be used where appropriate.

However, the engine must not fundamentally depend on proprietary assets.

The long-term architecture should allow:

```text
Game Definition
      ↓
Asset Reference
      ↓
Asset Provider
      ↓
Actual Asset
```

This should make it possible to eventually replace:

* models
* textures
* animations
* sounds
* music
* UI graphics
* effects

with independently created assets without modifying the game simulation.

The project must not assume that original Bullfrog/EA assets can be redistributed.

---

## Goal 5 — High Behavioral Fidelity

Reproduce Dungeon Keeper 2's behavior as accurately as practically possible.

"1:1" should primarily mean **observable behavioral equivalence**, rather than identical source code.

Particular emphasis should be placed on:

* creature decision making
* AI priorities
* job selection
* pathfinding
* combat
* resource calculations
* timers
* cooldowns
* state transitions
* room behavior
* spell behavior
* creature needs
* enemy behavior
* level scripting
* game timing

When the original game exhibits unusual or seemingly incorrect behavior, do not automatically "fix" it.

Instead:

1. investigate it
2. determine whether it is reproducible
3. document it
4. decide whether it should be reproduced

Unknown behavior must be explicitly identified rather than silently invented.

---

## Goal 6 — Automated Original-vs-Modern Compatibility Testing

Build a systematic behavioral testing framework that allows the modern implementation to be compared against the original Dungeon Keeper 2.

The original game should be treated as a **behavioral oracle**.

Create controlled experiments that establish:

* initial game state
* player actions
* simulation conditions
* expected outcomes
* timing
* state transitions
* resource changes
* AI decisions

The modern implementation should be able to run equivalent scenarios.

Over time, build a large regression suite covering the game's major systems.

The long-term objective is to move from:

> "It feels like DK2."

toward:

> "Given the same state and inputs, the modern implementation produces the same observable behavior."

---

## Goal 7 — Modern DK2 Level Editor

Eventually create a modern level/editor environment using the same underlying engine.

The editor should ideally allow creation and modification of:

* terrain
* rooms
* dungeon layouts
* creatures
* heroes
* traps
* doors
* objects
* resources
* scripts
* triggers
* objectives
* AI dungeon configuration
* campaign progression

The editor should be significantly easier to use than relying exclusively on the original 1990s-era tooling.

Where practical, support importing and/or exporting compatible original DK2 data.

---

## Goal 8 — Full Modding Framework

Turn the engine into a data-driven and moddable platform.

Game content should eventually be defined through structured data rather than hard-coded implementation.

The system should ideally support custom:

* creatures
* rooms
* spells
* traps
* doors
* objects
* heroes
* AI parameters
* balance parameters
* maps
* campaigns
* scripted events

A mod should be able to add or modify content without requiring changes to the engine itself wherever practical.

The long-term objective is to make the modern engine useful not only for reproducing DK2 but also for creating new content based on its mechanics.

---

## Goal 9 — Deterministic and High-Speed Simulation

The core game simulation should be deterministic whenever practical.

Given:

* identical initial state
* identical inputs
* identical simulation parameters
* identical random seed

the simulation should produce reproducible results.

Avoid uncontrolled randomness in gameplay logic.

The simulation should also be capable of running without rendering.

This should eventually allow:

* accelerated simulation
* automated testing
* large-scale simulation
* AI training
* balance testing
* replay
* deterministic debugging
* multiplayer experimentation

Ideally, the simulation should be capable of running significantly faster than real time when rendering is disabled.

---

## Goal 10 — AI/Bot Interface

Expose the game simulation through a clean programmatic interface so that an external AI agent can play Dungeon Keeper 2.

The engine should eventually allow an AI to:

1. observe the game state
2. select an action
3. submit the action
4. advance the simulation
5. observe the result
6. repeat

Conceptually:

```text
             Game Simulation
                    ↓
              Game State
                    ↓
                   AI
                    ↓
                 Action
                    ↓
             Game Simulation
```

This should eventually support experimentation with:

* scripted bots
* traditional game AI
* automated dungeon management
* reinforcement learning
* machine-learning agents
* AI-vs-AI games

The simulation architecture should therefore avoid unnecessarily coupling gameplay to human keyboard/mouse input.

---

## Goal 11 — Multiple Presentation Modes

The same underlying simulation should eventually support multiple presentation layers.

For example:

```text
                    DK2 Simulation
                          │
            ┌─────────────┼─────────────┐
            │             │             │
            ▼             ▼             ▼
      Classic Look   Modern Renderer   Headless
            │             │             │
      Original-style   Modern visual   AI/testing
       presentation    presentation    simulation
```

The important principle is that changing the renderer must not change the game rules.

This would make it possible to create:

* a classic DK2-style presentation
* a modern remastered presentation
* a headless simulation environment

using the same underlying game.

---

## Goal 12 — Long-Term Game Preservation

The project should ultimately serve as a modern preservation/reimplementation of Dungeon Keeper 2.

The objective is to make the game's mechanics and content less dependent on:

* 1990s operating systems
* obsolete graphics APIs
* legacy hardware
* original resolution limitations
* old driver behavior
* aging executable/runtime assumptions

A successful result should allow future systems to reproduce the original gameplay using a modern technology stack.

The preservation goal is not merely to preserve screenshots, models, or files.

It is to preserve the **playable behavior and simulation of the game**.

---

# Ultimate Vision

The ultimate vision is to reach a point where the following architecture is possible:

```text
                         DUNGEON KEEPER 2
                         GAME SIMULATION
                               │
          ┌────────────────────┼────────────────────┐
          │                    │                    │
          ▼                    ▼                    ▼
    Original DK2         Modern Renderer        Headless
       Content               + UI              Simulation
          │                    │                    │
          ▼                    ▼                    ▼
    Original-style       Modern/remastered       AI / tests /
      experience            experience          simulation
          │                    │                    │
          └────────────────────┼────────────────────┘
                               │
                         Modding Layer
                               │
              ┌────────────────┼────────────────┐
              │                │                │
              ▼                ▼                ▼
          New Maps        New Creatures      New Rooms
              │                │                │
              └────────────────┼────────────────┘
                               │
                               ▼
                         New Campaigns
```

The project should therefore not be architected as a one-off recreation of individual DK2 levels.

It should be architected as a **general-purpose, behaviorally faithful, modern Dungeon Keeper 2 simulation engine** capable of hosting the original game's content and eventually supporting entirely new content.

These goals should guide architectural decisions, but **they should not dictate premature implementation**. Early development should focus on proving the core simulation and reverse-engineering methodology with a small subset of the game before expanding toward the complete vision.
