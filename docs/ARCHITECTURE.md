# Architecture

How this tree is put together, and the constraints a change must respect.
Rationale: `DK2_REIMPLEMENTATION_PLAN.md` §4. Decisions and their reasons:
`docs/DECISIONS.md`. What currently exists, mechanic by mechanic, and the
counts: `docs/HANDOFF.md` and `dk2tools status`.

## Layering (dependencies point downward only)

```
unity/DK2.Unity            the only assembly that references UnityEngine
        │
DK2.Tools / DK2.Harness            net10.0 executables
        │
        ├──────────────► DK2.Presentation ──► DK2.Assets
        │                       │                  │
DK2.AI ──────► DK2.Simulation ───► DK2.Pathfinding  │
        │              │                  │        │
        └──────► DK2.Data.Model ◄── DK2.Data (parsers)
                       │
                   DK2.Core                netstandard2.1 (Unity-loadable)
```

| Project | Holds |
|---|---|
| `DK2.Core` | primitives, no game rules: `Fp32`, `GameTick`, `TilePos`, `Hash64`, `SimRandom`, `Confidence`, `Dk2BehaviorAttribute` |
| `DK2.Data.Model` | immutable definitions, no IO — the data/simulation seam |
| `DK2.Data` | readers for the shipped files (WAD, KWD/KLD, STR, SDT, `.SAV`) |
| `DK2.Simulation` | all game rules and all mutable state |
| `DK2.Pathfinding` | a known-divergent stand-in behind `IPathfinder` (ADR-0012, ADR-0155) |
| `DK2.Assets` | logical asset keys and their resolution (ADR-0034) |
| `DK2.Presentation` | world → drawable, renderer-independent (ADR-0035) |
| `DK2.AI` | the computer keeper as an input source (`docs/KEEPER_AI.md` §12) |
| `DK2.Convert` | original art → glTF/GLB and PNG |
| `DK2.Harness` | headless scenario runner |
| `DK2.Tools` | the `dk2tools` CLI |
| `unity/DK2.Unity` | the adapter; `unity/DK2.Unity.Compile` builds it against Unity's modules |

`docs/HANDOFF.md` §"Where the code lives" lists the types in each.

- **Eight assemblies target netstandard2.1** so Unity can load them: `DK2.Core`,
  `DK2.Data.Model`, `DK2.Data`, `DK2.Pathfinding`, `DK2.Simulation`,
  `DK2.Assets`, `DK2.Presentation`, `DK2.AI`. `DK2.Convert`, the tools, the
  harness and the tests target net10.0.
- The framework comes from **`src/Shared/UnityBound.props`, imported from inside
  each project's own body** — *not* as a condition in `Directory.Build.props`,
  where MSBuild's evaluation order made the condition unsatisfiable and every
  assembly silently built as net10.0 for four months (ADR-0002 correction,
  ADR-0036). The props file's own comment has the detail.
- **No simulation assembly may reference Unity**, and **the Unity-loadable ones
  must actually be netstandard2.1.** Both are asserted from the *built*
  assemblies by `ArchitectureTests` and `AssetArchitectureTests`, reading
  `TargetFrameworkAttribute` rather than the project files — because the project
  files were the thing that was wrong.

## Simulation rules

1. Time advances only through `GameSimulation.Tick(in InputFrame)`; `GameTick` is
   integer.
2. All randomness comes from `SimRandom` (seeded, serialisable). `System.Random`
   and `Guid.NewGuid()` are forbidden in simulation code.
3. All gameplay-affecting numbers come from the parsed KWD database
   (`DK2.Data.Model`). A literal balance constant in simulation code is a bug,
   and `GameDatabase[VariableTypeId.X]` throws rather than defaulting. The one
   sanctioned exception is `ObservedCorrections` (ADR-0023), which lists five
   admission conditions.
4. Gameplay math is integer/fixed-point. `float` is presentation-only (ADR-0001).
5. Every implemented DK2 mechanic carries `[Dk2Behavior(confidence, note)]`
   pointing at its research note in `docs/research/`.
6. **Rules ask flags, never ids.** A rule keyed on terrain id 2, a room id or a
   creature id is a rule about the reference install, not about DK2. The
   `*OnRealDataTests` guards check each partition still holds against the shipped
   tables.
7. **Ticks relate to seconds only in `SimulationTiming`.** `TicksPerSecond` is 4
   and a tick is 250 ms — DK2's own rate, read from the binary and since observed
   at 3.94 ticks/s on a running instance (ADR-0089, ADR-0102 amendment).
   `TicksPerSecond` (ours, refinable) and `OriginalTicksPerGameSecond` (DK2's, a
   fact) are equal and kept separate on purpose. A rate read from a creature
   record goes through `LoaderRatePerTick`, DK2's own integer divide by 4 floored
   at 1.

## System order

`SimulationSystems.Default()` is the **one** declaration of order (ADR-0013), and
order is part of the simulation's definition: two systems touching the same state
in a different order are a different game. The declared sequence:

```
HandSystem, TileTagSystem, RoomSellSystem, RoomBuildSystem, AutoFurnitureSystem,
TempleSacrificeSystem,
CreatureMovementSystem, FogOfWarSystem, ImpTaskSystem, TileWorkSystem,
BridgeBurnSystem, ManaSystem, ImpSupplySystem, PortalSystem,
HatcherySystem, CreatureEatSystem, CreatureSleepSystem, CreatureHealthSystem,
PrisonSystem, PrisonerEatSystem, PrisonerWanderSystem, PaydaySystem,
CreatureJobSystem, CreatureAngerSystem, CreatureExperienceSystem
```

The load-bearing constraints, with the per-position reasoning in the file's own
XML doc: the hand acts before any creature does; marks land before the work that
reads them; movement precedes every system that *sets* a path, so a creature is
noticed on the tick it arrives rather than the next; work queued on a tick
resolves on that tick, so nothing survives a tick boundary half-done; mana is
paid after everything that could change what a keeper owns, and imp supply after
the mana it spends; hunger is asked before tiredness and pay after both, which is
the order the original's own needs routine has; the prison settles a captive's
second alone, so exactly one system does; experience is last, so nothing that
died or walked out this second is promoted on the way through the door.

**UNKNOWN: DK2's own per-tick subsystem ordering.** Flame's disassembly names the
subsystems but not their sequence. This order is ours, chosen to be explicable
rather than to match something nobody has measured.

## World state

`WorldState` is the whole mutable game. Its collections, in hash order:

| | |
|---|---|
| `Tiles` (`TileState[]`, `Width` × `Height`) | terrain id, owner, health, gold held, a tag bit and a discovery bit per keeper |
| `Players` (`PlayerState`) | mana, the `Hand` list, the payday clock. **No gold counter** (ADR-0015) |
| `PendingWork` (`TileWorkOrder`) | a FIFO queue, never sorted or deduplicated, because order is behaviour (ADR-0010) |
| `Creatures` (`CreatureState`) | in spawn order, which decides who gets a job |
| `Objects` (`ObjectState`) | in placement order (ADR-0054) |
| `ActionPoints` (`ActionPointState`) | in THINGS-file order, because several original lookups walk the collection linearly (ADR-0103) |
| `Doors`, `Traps` | verbatim authored state in THINGS-file order, without behaviour (ADR-0106) |
| `HeroParties`, `EffectGenerators` | verbatim authored state in THINGS-file order, without spawn/generation behaviour (ADR-0108) |
| `TempleSacrifices` (`TempleSacrificeState`) | a sorted persistent three-offering ring per derived Temple anchor (ADR-0149) |
| `Triggers` (`TriggerState`) | the level-global flag and timer banks (ADR-0102) |

Three rules govern it:

- **A field exists only when a mechanic reads it.** `TileState`, `CreatureState`
  and `ObjectState` all follow it, which is also why a field has *left*: bed
  ownership moved onto the object.
- **New state must be reachable from `WorldState`** — added to `HashInto` *and*
  to `WorldSnapshot` in the same commit, or it is silently non-deterministic and
  lost on load.
- **Nothing in the simulation may read an event back.** `ISimulationEventSink`
  (`SimulationEvents.cs`) is write-only observation; attaching a recorder with
  `ObserveWith` must not change behaviour.

## Determinism (M2)

The load-bearing contract: **same database + same initial world + same seed +
same input frames produce the same `GameSimulation.StateHash()` at every tick, on
any machine.**

- The RNG's position is part of the state hash — two runs that look identical but
  have consumed different draws are about to diverge.
- `Hash64` is FNV-1a. `GetHashCode` is forbidden in the hash path, because .NET
  randomises string hashing per process.
- One pinned literal in
  `DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines` guards
  cross-machine stability. Changing simulation state deliberately means bumping
  `GameSimulation.HashVersion`, keeping `WorldSnapshot.FormatVersion` equal to
  it, and re-pinning the literal in the same commit. Never loosen the assertion.
  **The pin's world holds two creatures** (Q206), so a change to
  `CreatureState.HashInto` does fail it. It registers no creature *system*,
  so what it guards is the fields, not behaviour over ticks.
- `WorldSnapshot` round-trips are verified by continuing both runs for hundreds
  of ticks, not by comparing bytes.
- `.github/workflows/ci.yml` declares the gate on Linux and Windows with
  `DK2_DIR=/nonexistent`, so every install-gated test is reported skipped there.
  **No CI is actually running** (`d03066a`), which makes the commit-msg hook —
  `git config core.hooksPath .githooks`, running `dk2tools docs` and
  `dk2tools status --check` — the only gate in force, and leaves the pin's
  cross-platform claim resting on local runs.
- A test that needs the shipped game data uses `[InstallFact]`/`[InstallTheory]`
  and **skips** without an install; an early `return` on a null install is the
  defect ADR-0100 removed and `InstallGateTests` fails the build on it.

Full rationale: `docs/architecture/simulation-loop.md`.

## Presentation (M3)

**A renderer never reads `WorldState` directly.** `DK2.Presentation.WorldView`
turns it into `TileView` / `CreatureView` / `ObjectView` — readonly structs
carrying asset keys, fixed-point positions and the few state bits a player can
see. Four decisions live there and nowhere else: which asset key a terrain
implies, which animation an activity implies, how a room's floor differs from its
furniture, and what occludes what. Two renderers disagreeing about any of them
would look exactly like a simulation bug (ADR-0035).

```
WorldState ──► WorldView ──► ScenePresenter ──► IRenderTarget
                                  │                   ├── DungeonScene   (Unity)
                             AssetProviderChain        └── (a text view redraws
                                  │                        from WorldView direct)
              replacement ──► converted ──► placeholder
```

- **The view has a viewer.** `WorldView(GameDatabase database, byte
  viewingPlayerId = 1)` produces one keeper's screen: `TileView.Tagged` is the
  viewer's own mark, and a tile that keeper has not discovered is yielded as
  terrain 27 (`Fog Of War`) with terrain 27's assets, solid and taggable, so the
  real terrain never reaches the renderer at all. The six terrain types flagged
  `IsRevealedThroughFog` show through (ADR-0076, `fog-of-war.md`). A multiplayer
  caller builds one view per keeper.
- **`ScenePresenter` diffs** and pushes only what changed, in a fixed order —
  tiles row-major, then objects, then creatures, both in spawn order with
  removals ascending — so a recorded call sequence is reproducible. An unchanged
  world pushes nothing. Objects precede creatures because a creature stands on
  one, and that precedence is decided here so two renderers cannot disagree.
- **`IRenderTarget` is nine methods with no logic in them**: create and update a
  tile; create, update and remove a creature; create, update and remove an
  object; flush. Everything an engine adapter could get wrong is upstream of it
  and tested.
- An object crosses the boundary as a tile, a mesh key and its gold amount — no
  sub-tile position, because the simulation has none.
- **`TextRenderer` is the second renderer**, and the one that runs in the test
  suite. It redraws everything, because a character grid is free — deliberately
  the opposite strategy, which is what proves the boundary assumes neither.

## Assets (M3)

Goal 4's `Game Definition → Asset Reference → Asset Provider → Actual Asset`
(ADR-0034). `AssetRef` is a validated logical key — dot-separated, `[a-z0-9_]`
per segment — built from a record's **name**, never its id, and it is the only
validator. `AssetProviderChain` resolves it: replacement pack, then converted
art, then `PlaceholderProvider`, which is **total**. Every key the shipped
database implies resolves with no DK2 install and no art pack, and
`AssetCatalogueTests` checks exactly that over the whole shipped database.
**No filename literal exists outside `DK2.Assets`**, where `FileAssetProvider` is
the only type that turns a key into a path.

`DK2.Convert` (`dk2tools convert`) writes KMF → glTF/GLB and engine textures →
PNG, and every key it produces round-trips through `FileAssetProvider`. **No
renderer loads its output yet**: Unity ships no runtime glTF importer and
`DungeonScene` has no branch for it.

## Unity (M3)

`unity/DK2.Unity` — Unity 6 LTS `6000.6.0f1` (ADR-0036). It consumes the
netstandard2.1 DLLs from `Assets/Plugins/DK2` (synced by
`unity/sync-assemblies.ps1`; build output, gitignored) and owns no game state:
`DungeonRunner` ticks a `GameSimulation` and calls `ScenePresenter.Present`, and
`DungeonScene` implements `IRenderTarget` in GameObjects. Deleting every
GameObject would change nothing, because the simulation references none of it. It
renders campaign level 1 as coloured boxes rather than DK2's walls (Q71).
`DungeonRunner` projects mouse rays to tiles, moves the camera and passes button
edges to `PlayerInputTranslator`; gesture-to-command behaviour remains in the
Unity-free presentation assembly (ADR-0104).

`unity/DK2.Unity.Compile` compiles the adapter against Unity's real module
assemblies, which is the only way "do not commit code you have not built" reaches
Unity code. It is outside `DK2.slnx` on purpose. See `unity/README.md`.

## Scenarios (M2)

`scenarios/**.yaml` are declarative experiments, run headlessly by `DK2.Harness`
(`dotnet run --project src/DK2.Harness -- run scenarios`). The runner reports
every part of a scenario it could not honour — unknown keys, unapplied setup,
observations nothing produces yet — so a run can never look more complete than it
is, and `TextRenderer` writes a `world.txt` per scenario. No scenario carries an
expectation block and `docs/experiments/` holds no oracle-versus-ours run, so the
corpus is a regression suite against ourselves and not yet against DK2. See
`scenarios/README.md`.

## Tile work (M3)

Digging, mining, claiming, reinforcing, repairing, attacking and taking over a
rival's room are one uniform, entirely data-driven rule; the numbers come from the
terrain table and the health-delta variables, and none appears in the code.

- `TileWorkRules` is pure and static — no world, no RNG, no time. It answers
  "does this kind of work apply to this tile" and "what does one application do",
  and nothing else.
- Work lands **once a tick, four a second**, at `delta × efficiency ÷ 4`
  (ADR-0089). An *upward* job costs the tile's health **deficit**, not its whole
  maximum (ADR-0089, reversing ADR-0024).
- `TileWorkSystem` drains `PendingWork`. `TileWorkKind.ConvertRoom` is the one
  kind whose outcome is not confined to its own tile: the rules convert the worked
  tile and the system spreads it across the room instance, because a room is
  claimed whole (ADR-0044, Q92). That split is why the rules can stay pure.
- `TileTagSystem` applies `TagTilesCommand`. Markers are per player, one bit
  each, cleared when a tile converts into terrain that cannot carry one — so a gem
  face, which collapses into itself, keeps its mark.
- **Not "filling in" — see Q219.** A dug-out tile never closes;
  `TileWorkKind.FillIn` was deleted, and `FillInHealth`, the `FillInable` flag
  and Claimed Path → Rock were a shape in the data only.

Evidence, confidence and the three anomalies the data throws up:
`docs/research/tile-work.md`.

## Workers and movement (M3)

Anything flagged `IsWorker` — the Imp and the Dwarf, asked by flag — chooses a
job, walks to it and works.

- `ImpTaskSystem` decides on a two-tier chooser (ADR-0058,
  `imp-task-priority.md`) and **queues** `TileWorkOrder`s; it never applies them.
  One outward search answers "which job is nearest", "how do I get there" and "is
  anything reachable at all" at once. Carrying and the slap bonus are task kinds
  in the same chooser (ADR-0063/0065/0069).
- `CreatureMovementSystem` walks. Speed is read as tiles per second, spent per
  tick, with the remainder carried so nothing drifts slow.
- `DK2.Pathfinding` holds the search behind `IPathfinder` and knows nothing of
  terrain or creatures: `WorldPathGrid` decides what "walkable" means for one
  particular mover, from terrain flags and that creature's own flags.
- **Routing is ours, not DK2's** (Q5, ADR-0012): eight-connected with 10/14
  octile costs (ADR-0025, Q48), deterministically tie-broken, and an admitted
  placeholder behind an interface. DK2's own routing is now read and is a
  navigation mesh plus a steering system, so this differs in kind and not
  merely in tie-breaking (ADR-0155, `research/navigation-and-steering.md`). An imp with no reachable job stands still where
  DK2 would have it wander (Q41).

Evidence and confidence: `docs/research/imp-tasks.md`.

## Rooms and gold (M3)

- A room is a **derived instance** (`RoomInstance`, ADR-0027) over the tiles
  carrying its terrain, indexed by `RoomIndex`; there is no authoritative room
  object to keep in step with the grid.
- `RoomRules` says where a room may go, what it costs and what it does to the
  tile. Where it may go is read from the room record's own placeable-on flags, so
  no code knows what a bridge is (ADR-0016).
- `BuildRoomCommand` + `RoomBuildSystem` are the command path, alongside
  `TagTilesCommand` + `TileTagSystem`: a tile at a time, in the order the command
  gives, with every refusal counted by reason in the event log.
- `GoldStore` is the only way gold moves, and **a player has no gold counter**.
  `TileState.GoldHeld` is where gold is and a keeper's total is the sum over the
  tiles they own (ADR-0015); `CreatureState.GoldHeld` is a carrier's hands
  (ADR-0019, Q31) and `FloorGold` is a pile nobody could store
  (ADR-0055/0056). Every limit DK2 sets on gold is per place, which is what
  forced the shape.

Four behaviours then fall out rather than being written: a keeper with no
treasury cannot keep gold, destroying a treasury takes the gold with it, a
keeper's first treasury is necessarily paid for out of the dungeon heart, and a
load too big for one treasury tile spills into the next — because the carrier
puts down what fits and looks again, not because anything implements spillover.

Which rooms have behaviour, and which are inert terrain their owner holds:
`docs/HANDOFF.md` §"What exists". Evidence:
`docs/research/rooms-and-gold.md`.

## Mana (M3)

- `ManaRules` is the whole model: a keeper's income is the sum of
  `TerrainDef.ManaGain` over the tiles they own, plus a flat 30 if they have a
  dungeon heart, capped by `MaximumManaGainPerSecond` = 500.
  `MaximumManaThreshold` is a flat 200000 ceiling for every keeper (CONFIRMED by
  observation), soft rather than a clamp: mana above it drains back.
- The heart's 30 is counted **once per keeper, not once per heart tile**, and that
  is exact rather than a simplification — a keeper has at most one dungeon heart,
  ever, and losing it is defeat.
- `ManaSystem` pays it once a game second (ADR-0017).
- Mana stays a player-level number, unlike gold. The asymmetry is the data's:
  every limit DK2 sets on gold is per place, and every variable it sets on mana is
  per player.

**Mana income is territory**, and no code says so. Three terrain types out of 41
pay anything, and they are exactly the ownable non-room ones — so claiming ground
raises income, losing it lowers it, and building a room on your own floor takes
that floor off the payroll, all without a rule about claiming, losing or
building. `scenarios/mana/land_pays_a_keeper.yaml` shows the last of those: the
rate falls from 38/s to 35/s when three of eight claimed tiles become a treasury.

Nothing spends mana but Create Imp; every other spender is a spell (M7). Nothing
happens when a heart falls — defeat belongs with objectives at M6 (Q40,
Q207/Q93). Evidence: `docs/research/mana.md`.

## Creatures (M4)

Every rate comes from the record or the balance table; none is in the code.
`CreatureState` carries position in fixed point, activity, target, timer, route,
health, hunger, anger, level and experience — and only because a mechanic reads
each. It has **21 activities against the 281 states read from the binary**
(ADR-0072), which is the honest measure of how much of the creature loop exists.

Three shapes matter to a change:

- **The mood is derived, not stored.** `CreatureAnger` is six counters, each
  saturating at 65533, and the mood is banded on the **largest** rather than the
  sum (ADR-0081). A bed's −550/s and an unpaid wage's +60/s therefore never meet.
- **A creature's capability comes from its flags and a room's job from the room
  table** (`JobRules.RoomFor`, ADR-0088) — never from a name or an id.
- **The bed relation lives on the object, not the creature.** `Lair.CreatureId`
  is how DK2 records whose bed a bed is; this project had kept the same fact on
  the creature as a tile index, and two copies of one fact is the shape behind
  three of its longest-lived defects. A bed exists only while owned, so occupancy
  needs no bookkeeping at all.

**⚠ The lair *relation* is right and the way a creature reaches it is not.** The
original acquires a lair by walking to the nearest reachable one with a free slot
and testing tiles on arrival, attempted by any idle bedless creature whatever its
mood; `CreatureSleepSystem` claims a bed on the first tick one is free, from
anywhere. The replacement rule is fully read and awaits building
(`docs/research/lair-acquisition.md`).

The Hand of Evil is part of the input path rather than a creature mechanic:
`PlayerState.Hand` is a list of `HandItem`, creature or object, and
`PickUpCreatureCommand`, `PickUpObjectCommand`, `DropCommand` and
`SlapCreatureCommand` are `PlayerCommand`s that
hash into the replay like every other (ADR-0062/0075/0077).

Which needs, jobs and consequences are built: `docs/HANDOFF.md` §"What exists".

## Players and territory

- `PlayerIds`: 0 unowned, 1 the hero side, 2 neutral — which is what owns an
  unclaimed room — and 3–7 keepers (ADR-0043). Only a keeper is staffed
  (`PlayerIds.IsKeeper`, ADR-0052), so a test world must not call the hero side a
  keeper. In the MAP record player **2** is the unowned marker, not 0.
- A tile nobody can own carries no owner (ADR-0045). Hostile expansion is direct
  claiming (ADR-0033).
- Fog of war is **simulation** state, not presentation: one discovery bit per
  keeper on the tile, revealed within each creature's `PerceptionRange`, hashed
  and snapshotted (`FogOfWarSystem`, ADR-0076). DK2 itself stores one bit per
  *machine* (`fog-of-war.md` §10), so the per-keeper mask is ours. Off by
  default.
- Levels play on their own VARIABLES layered over the global table (ADR-0040).
- `DK2.AI.KeeperAI` is an **input source** that emits `PlayerCommand`s (step 1 of
  `docs/KEEPER_AI.md` §12.7): it digs to gold, sites and places rooms in the
  shipped order, and changes no simulation surface.

## Objects (M6)

The storage shape was read out of the binary rather than chosen (ADR-0054,
`docs/research/objects.md`).

- **DK2's object is one type with an 18-byte tagged union.** Its own debug dump
  prints the header `Union Data:` and then one of twenty-three blocks, every one
  reading from the same offset; the tag is the object's definition id. So a bed, a
  chicken, a boulder and a gold pile are one kind of thing in different states,
  not four kinds of thing.
- **`ObjectState` mirrors the semantics and not the layout.** Fidelity is
  required of behaviour, not of storage, and the behavioural content of a union is
  that at most one kind's state is live. Ours is flat, with a documented owner per
  field and **every field hashed unconditionally** — a kind-dependent hash would
  let a stray write hide.
- **Rules ask the definition's type flag, not the id**, because the original's
  own dispatch does and gets more from it: one `TypeLair` branch serves all twelve
  bed objects. The exception is where DK2 itself names an id — the Chicken Coop
  carries no type bit at all — and `HatcheryRules` says so at the point of use.

## A level's placed THINGS (M6)

`ThingProjection.Place(world, database, things)` turns a level's own THINGS
records into world contents and returns a `ThingProjectionResult`, so a caller can
assert on the counts (ADR-0101, `docs/research/placed-things.md`).

- Creatures and objects are placed. ROOM records are recognised and deliberately
  **not** placed: the MAP grid already holds their terrain, and all 275 agree with
  it on terrain id — five disagree on *owner*, Q223.
- Action points, doors, traps, hero parties and effect generators become ordered,
  hashed and snapshotted state (ADR-0103/0106/0108). Party members remain
  unspawned templates and do not enter `WorldState.Creatures`.
- File order is preserved for every projected record type.
- Coordinates are **bare tile indices**, not `Fp32` subtile units;
  `InitialHealth` is a **percentage** of the maximum; `RoomThing.RoomType` is a
  **terrain** id. All three had been transcribed from an external field list and
  all three were wrong.
- The harness opts in with `setup.placed_things: true`; every shipped parsed
  record now projects, pinned by `PlacedThingsOnRealDataTests`.

## Action points (M6)

`WorldState.ActionPoints` holds every point in THINGS-file order, because several
original lookups walk the collection linearly (ADR-0103,
`docs/research/action-points.md`). Each carries the low-word inclusive rectangle,
action-point id, trigger id, next-waypoint id, the five flag bits the loader
copies (`0x3D`), and the saved passage counter and wait timer. All of it is hashed
and snapshotted.

- **There is no availability field.** None of trigger actions 16, 17, 27 or 31
  writes the object, and the runtime layout has no such field; ADR-0102's contrary
  statement was drawn from a repeated parameter *name*.
- **`WaitDelay` is multiplied by the original tick rate once, at projection**,
  reproducing the loader at `0x00521221` — truncation to the low 16 bits included.
- The **name** is not runtime state and is not hashed. **Trigger repeat count** is
  omitted until trigger records are projected, because the original derives it by
  resolving the record.
- **No action-point behaviour runs**: flag meanings, timer and counter consumers,
  rectangle-entry cadence and waypoint traversal are Q235–Q238.

## Trigger flags and timers (M6)

`TriggerState` on `WorldState` (ADR-0102, `docs/research/trigger-state.md`): 128
flag bytes, 16 timer dwords, one opaque special value. Hashed and snapshotted.

- DK2's own banks sit at `+0x3F70` and `+0x3FF0`, `0x80` apart, which is what
  fixes the flag bank at 128; the timer bank is bounded at sixteen three
  independent ways.
- **Level-global, not per-player.** The player-context dispatcher `0x004C8620`
  holds a player as its own `this` and still loads the global.
- A *stamp* in the timer bank is a tick count. An *authored* threshold in a
  trigger record is in **seconds**, which DK2's level trigger initialiser
  multiplies by variable 19 once at load (`0x0050C2DB`). So
  `HasElapsed(timerId, seconds, now)` converts, and when the `.kld` trigger
  loader is written the conversion must happen exactly once — there or at the
  comparison, never both.
- Flag id 128 and timer id 16 are one past their banks and are neither a flag nor
  a timer: the first is carried as an opaque value, the second refused (Q229,
  Q232). The byte an external field list calls `Flag` is an **operation and
  addressing mode**; the flag it acts on is a different byte.
- Parameter offsets, widths and signedness come from DKII.EXE reads, not editor
  labels (ADR-0092, `docs/research/trigger-semantics.md`). Pinned by
  `TriggerStateOnRealDataTests`.
- **Trigger execution is not built.** Flag and ordinary timer conditions can be
  evaluated singly. Q228 fixes the cadence and `trigger-tree.md` fixes traversal
  and repeat semantics; the executor and actions remain absent.

## Absences with architectural weight

`docs/HANDOFF.md` §"What does not exist" is the full list. Four of them constrain
design rather than merely waiting their turn:

- **Trigger execution.** The banks, the parameter views and the action points are
  state; nothing consumes them, so no level is winnable and objectives have no
  host.
- **The pathfinder is an admitted placeholder** (ADR-0012, Q5), and since
  ADR-0155 a *measured* divergence rather than an unknown one. Everything that
  depends on route length or arrival order is provisional.
- **Simulation speed has never been measured** (goal 9), so "significantly faster
  than real time" is an unverified claim about this loop.
- **The oracle comparison rig is one-sided.** A live-memory reader against a
  running DKII exists and has settled two facts, but no scenario has been captured
  from both sides and there is no divergence report, so nothing here is yet
  checked against DK2 automatically.

## Licence boundaries in the tree

- Every source file carries the three-line SPDX notice,
  `SPDX-License-Identifier: GPL-3.0-or-later`, enforced by `LicenseHeaderTests`
  (ADR-0041).
- **No Flame identifier in `src/`, `tests/` or `unity/`**, comments included
  (`FlameIdentifierTests`, ADR-0080). A document may *cite* a Flame name as
  Flame's.
- Decompiler output never enters the repository, anywhere (ADR-0046);
  `reverse-engineering/` is gitignored except its README.
- OpenKeeper facts carry provenance in `THIRD-PARTY-NOTICES.md`; wiki content is
  put in our own words with a citation, never its prose.
