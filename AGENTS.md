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
   output may never cross, anywhere, at all. Setup (since 2026-09-25): Ghidra
   **12.1.4** at `D:\Ghidra\ghidra_12.1.4_PUBLIC`, the one project `D:\Ghidra\DK2_RE`, cloned per run,
   scripts run as **PyGhidra (CPython 3)** through `pyghidraRun.bat -H`. 12.1
   ships no Jython. The command and the porting notes are in
   `docs/RESEARCH-QUEUE.md`, *How the binary work is done here*. The
   repeatable queries are the `ghidra-queries` skill
   (`.agents/skills/ghidra-queries/SKILL.md`): the runner
   `tools/ghidra/ghidra.py --agent <you>`, the script catalogue and the traps,
   and (since 2026-09-30) the pyghidra-mcp server, one per agent on its own
   project copy, which the client spawns itself through
   `tools/ghidra/mcp-serve.py --agent <you>`.
2. **The shipped data files** — also the actual DK2, and cheaper to read.
3. **Original game experiments** — the running install is the oracle for
   anything observable, and it outranks a misread disassembly.
4. **OpenKeeper** — cloned at `D:\OpenKeeper`. GPLv3: facts only, with
   provenance in `THIRD-PARTY-NOTICES.md`.
5. **Flame** — cloned at `D:\Flame` (pinned `f6e2a3d`, 2026-03-01).
   **Unlicensed: no reuse rights at all**, so facts only and *never* one of its
   identifiers in `src/`, `tests/` or the Unity adapter, comments included
   (`FlameIdentifierTests`); documents may *cite* a Flame name as Flame's
   (ADR-0080). Best for **structure** — struct
   layouts, vtable membership, cross-references — and measurably weak on
   creature behaviour: it names only 34% of the symbols in the address range
   where creature state, jobs and needs live. Use it to find and to confirm, not
   to answer. `DK2_REIMPLEMENTATION_PLAN.md` §2.2.1 has the measurements and the
   working recipe.
6. Ember
7. DK2 reverse-engineering documentation, and the DK2 wiki (CC BY-SA: facts in
   our own words with citations, never its prose)

## Library and tool documentation — use Context7

For how a **library, framework, SDK or tool** works, query Context7
(`resolve-library-id`, then `query-docs`) before answering from memory:
training data lags releases. That covers .NET and C# APIs, xUnit, YamlDotNet,
Unity (the adapter, packages, editor scripting), Ghidra's scripting API,
BenchmarkDotNet, and dotnet/git CLI flags.

It is **never a source for DK2 behaviour**: that order is the list above. Skip
it for this repo's own code, for refactoring, and for general programming
questions. If Context7 has nothing for a tool, say so and read the tool's own
docs.

## Architecture

Game simulation must remain independent from Unity wherever practical.

Unity is presentation/integration.

## Original assets

Original DK2 assets may be used for development/reference.

Do not assume they are redistributable.

All asset access must go through an abstraction layer.

## Visual fidelity

**Preserve design. Increase fidelity.** For animation: **preserve motion,
modernize representation.** Original DK2 visual design is the source of truth
for graphics exactly as original DK2 behaviour is for gameplay: modern assets
and Unity rendering re-express it and never redesign it, and photorealism is
out of scope. Before creating, generating or reviewing any modern asset, read
`docs/ART_DIRECTION.md` (the standard) and `docs/ASSET_PIPELINE.md` (how assets
are made and checked). ADR-0242.

## Fidelity

Do not "fix" strange DK2 behavior without documenting the deviation.

## AI collaboration — what to record, and how much

Do not leave important knowledge only in chat history. **Do not write it five
times either.** Both failures are live in this repo, and the second is now the
bigger one: `docs/` is ~600k tokens, one question row is 11,000 characters,
`HANDOFF.md` reached 6,491 lines, and one finding on 2026-09-08 was written into
five files plus its commit message.

### One fact, one home

Every fact has exactly **one** canonical location. Everywhere else links to it.

| what | home |
|---|---|
| a decision, and why | one ADR in `docs/DECISIONS.md` |
| evidence for a DK2 behaviour | one section of one `docs/research/*.md` |
| an open unknown, and how to close it | one row in `open-questions.md` |
| something surprising about *this* code | a comment in the code |
| why this change, now | the commit message |
| where the project currently stands | `docs/HANDOFF.md` |
| what a DKII.EXE function is, and how that verdict changed | its record in `docs/research/functions/` |

If you are about to write a paragraph that exists elsewhere, write `see
ADR-0102` instead. A second copy is not emphasis — it is a second thing that
can go stale, and stale claims are this repo's most frequent defect.

### HARD RULE: the function knowledge base is kept current

`docs/research/functions/<ADDRESS>.md` is the one home for what a DKII.EXE
function is, how sure we are, why, and how that changed. **Every new finding
or correction about a function goes into its record in the same change, with
a `History` line dated that day.** A document, ADR or source file that names a
`FUN_` without a record does not pass `dk2tools kb check`, and the commit hook
runs it (`docs/plans/` is exempt: a plan is a process document, not a claim).
Read the record before you decompile: a later session may already have
disputed what you are about to conclude. `implements` names our code that
mirrors the function, not code that consumes its output. `INDEX.md` there is
generated (`dk2tools kb index`); never edit it by hand. Design:
`docs/plans/2026-09-30-function-knowledge-base-design.md`.

### Replace, do not append

**The rule that matters most.** It is what produced a 6,491-line handoff and a
question file where 41 rows hold 126 KB. When something supersedes what is
written, **rewrite that place**.

- Do not stack `AMENDED` / `NARROWED` / `HALF ANSWERED` / `CONFIRMED` paragraphs
  onto a question row. Rewrite the row to say what is true now, and what is
  still open.
- Do not leave a wrong claim in place with a correction beneath it. Delete the
  claim; state what is true.
- Do not keep superseded prose "for the record". **Git is the record** — cite
  the commit if an old reading still matters.
- Keep a correction only when the *mistake* has a reusable shape, and keep
  **one sentence** of it, not the narrative.

### Budgets

These are triggers, not style preferences: over budget means the material
belongs somewhere else. They apply to anything you **create or edit** — you are
not required to fix the backlog, but do not add to it.

| | budget | currently |
|---|---|---|
| open-question row | **800 characters** | 452 rows, none over |
| XML doc comment | **12 lines** | worst is 113 |
| inline comment | **6 lines** | median is 3, so 6 is real headroom |
| comment lines per file | **40%** | 39% overall, worst 84% |
| commit message body | **15 lines** | recent ones run 40–64 |
| research doc | **400 lines** | 3 of 152 over |
| ADR | **50 lines** (amendment **15**) | already met — do not regress |
| `docs/HANDOFF.md` | **500 lines** | 412 |

An ADR carries the decision and the reason. The *evidence* goes in the research
doc. A commit message says why the change was needed, not what the
investigation felt like.

**`dk2tools docs` enforces this.** It checks only what you **changed**, so the
backlog cannot drown the signal, and a whole-file measure (length, comment
ratio) fails only if your change made it worse — a one-line fix to a long
document is not the moment to demand a split. `--all` reports the backlog
(1,153 items today, mostly oversized comment blocks). `--message <file>` checks
a commit body, for a commit-msg hook. Budgets live in one place,
`DocBudgets` in `src/DK2.Tools/Program.cs`; changing one there without changing
this table is a bug.

**Install the hook once per clone:** `git config core.hooksPath .githooks`.
CI (`.github/workflows/ci.yml`) builds and tests on Linux and Windows with no DK2 install; it does not run the doc budgets, so the hook is the only gate for those. It **blocks** on a budget
violation and merely **notes** a stale status line — almost every commit changes
one of that line’s numbers, and blocking each one would teach everybody to pass
`--no-verify`, which would switch off the budget check too.

### What is worth recording

Record: a decision and its reason; a DK2 fact and how it was established; an
unknown and how to close it; a constraint invisible from the code.

Do **not** record: what the code already says; a dead end that taught nothing;
session narrative ("first I looked at…"); reasoning anyone could re-derive in a
minute; how hard or long the work was; the same fact in a second voice.

### Tone

State the fact; do not argue for it at length. Cut:

- rhetorical framing — "the finding worth keeping", "this deserves naming",
  "worth repeating"
- self-narration — "I was wrong", "this cost a day". One sentence, and only
  where the lesson generalises.
- emphasis inflation. If a third of a paragraph is bold, none of it is.
- the same point restated in a second register for effect.

Long-form prose is deliberate in this repo. That is not a licence for length: a
reader must be able to act on a section without reading its history.

## Session handoff

Read docs/HANDOFF.md first: current state, next steps, decisions already made,
environment gotchas, and the research method in use.

It is a **reference, not a log**: state what is true now and let git hold the
history. It reached 6,491 lines of dated session narrative before being cut to
469 on 2026-09-08. Budget 500 lines. Do not append a sitting; edit the section
the fact belongs to.

## Before changing architecture

Read:

docs/ARCHITECTURE.md
docs/DECISIONS.md

## Before implementing a reverse-engineered mechanic

Read:

docs/RESEARCH_STATUS.md
docs/EXPERIMENTS.md


## Meaningful types over primitives

The simulation is integer-only by decision (ADR-0001) and its records mirror DKII.EXE's byte layout so that Ghidra offsets, the full-state recorder and hash comparisons line up one-to-one. Neither fact is a licence for primitive obsession in the code that *uses* those records. A `byte` that means "player 3" and a `byte` that means "room type 3" must not be interchangeable at a call site.

The rule: **a value that has a domain meaning gets a domain type when it crosses a signature.** Raw bytes and ints may live inside a record that mirrors the binary; they should not travel through rules, systems and commands unwrapped.

Existing types to reuse before inventing anything: `TilePos`, `GameTick`, `Fp32` (DK2.Core), the reserved ids in `PlayerIds`, and the enums for kinds and flags (`CreatureFlags`, `RoomTypeId`, `ImpTaskKind`, ...). A new `byte kind` parameter beside an existing enum is a defect, not a shortcut.

Where a domain type is warranted:

- **Identity.** Player ids, creature ids, room ids, slot numbers. Two ids of different kinds must not share a type. A sentinel (`-1`, `0xFF`, `0`) belongs on the type as a named member, never inlined at the call site.
- **Position.** One representation per layer. `TilePos` for tile coordinates; a flat tile index only inside grid storage, converted at the boundary rather than by `% world.Width` in callers; sub-tile fixed-point positions as `Fp32` or a named struct, never a bare int pair.
- **Time.** A tick *stamp* and a tick *duration* are different things. Use `GameTick` for stamps. A countdown or an interval gets its own type or, at minimum, a name ending in `Ticks` and never an `int turn` parameter.
- **Quantities.** Gold, mana, health, experience and research points are not interchangeable ints. When a function takes more than one of them, or when arithmetic between two of them would be a bug, wrap them.
- **Flags.** A bitmask is a `[Flags]` enum with the underlying width the binary uses, not a `byte Flags` field with masks in comments.

Where a primitive is the right choice:

- A record field whose offset and width are documented against the binary. Keep the raw type, name it precisely, and convert at the accessor.
- Loop counters, array indices into a local buffer, lengths, and intermediate arithmetic.
- Hot paths where the profiler shows a wrapper costs something. A `readonly record struct` over one field is free in .NET; assert that with a benchmark before choosing the primitive.

Signals that a signature has the problem:

- Three or more adjacent parameters of the same primitive type. Swapping two of them compiles and changes a number silently. Use a struct, named arguments at every call site, or split the function.
- A parameter compared to a magic literal (`owner == 0`, `id != -1`) inside the callee.
- The same conversion (`tileIndex % Width`, `tick - stamp > n`) repeated in more than two callers.

When touching existing code that has the problem, fix the signature you are changing and its callers; do not sweep the codebase. Wrappers go in the assembly that owns the concept (DK2.Core for engine-neutral values, DK2.Data.Model for ids the map format defines, DK2.Simulation for simulation-only state), and they must remain Unity-loadable like everything else there.

## Commit messages

- Match the language of recent commits in the repo (`git log --oneline -20`).
- Casing and length after the number: lowercase, one short phrase, no conventional-commit prefix (`feat:`, `fix:`, `chore:`).
- One imperative line. No bullet list unless the change genuinely spans unrelated scopes.
- Describe intent, not mechanics.
- Never include `Co-Authored-By`. For AI-assisted commits add trailer `Assisted-by: <Model>` (e.g. `Assisted-by: Claude:claude-fable-5`).


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
