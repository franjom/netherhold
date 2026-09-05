# Architecture

Full rationale: `DK2_REIMPLEMENTATION_PLAN.md` §4. This file is the working summary.

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

- Seven assemblies target **netstandard2.1** so Unity can load them: `DK2.Core`,
  `DK2.Data.Model`, `DK2.Data`, `DK2.Pathfinding`, `DK2.Simulation`,
  `DK2.Assets`, `DK2.Presentation`. `DK2.AI` does too.
- The framework comes from **`src/Shared/UnityBound.props`, imported from inside
  each project's own body**. It is *not* a condition in `Directory.Build.props`,
  and the comment in that props file explains at length why: it was written that
  way, the condition could never be true because of MSBuild's evaluation order,
  and every assembly silently built as net10.0 for four months (ADR-0002
  correction, ADR-0036).
- **No simulation assembly may reference Unity**, and **the Unity-loadable ones
  must actually be netstandard2.1.** Both are asserted from the *built
  assemblies* by `ArchitectureTests` and `AssetArchitectureTests` — reading
  `TargetFrameworkAttribute`, not the project files, because the project files
  were the thing that was wrong.
- Tools, harness and tests target net10.0.

## Simulation rules

1. Time advances only through `GameSimulation.Tick()`; `GameTick` is integer.
2. All randomness comes from `SimRandom` (seeded, serialisable). `System.Random`
   and `Guid.NewGuid()` are forbidden in simulation code.
3. All gameplay-affecting numbers come from the parsed KWD database
   (`DK2.Data.Model`). A literal balance constant in simulation code is a bug.
4. Gameplay math is integer/fixed-point. `float` is presentation-only (ADR-0001).
5. Every implemented DK2 mechanic carries a `[Dk2Behavior]` attribute with a
   confidence level and a pointer to its research note in `docs/research/`.

## Presentation (M3, done 2026-09-04)

**A renderer never reads `WorldState` directly.** `DK2.Presentation.WorldView`
turns it into `TileView` / `CreatureView` / `ObjectView` — readonly structs
carrying asset keys, fixed-point positions and the few state bits a player can
see — and every renderer consumes those. Four decisions live there and nowhere
else: which asset key a terrain implies, which animation an activity implies,
how a room's floor differs from its furniture, and what occludes what. Two
renderers disagreeing about any of them would look exactly like a simulation bug
(ADR-0035).

**Objects joined on 2026-09-05**, and it closed a hole rather than adding a
feature: beds, coops, eggs and piles of gold had been in the world since
ADR-0054 and every renderer drew bare floor where one stood, so a scenario
picture of a sold treasury showed the gold vanishing. An object crosses as a
tile, a mesh key and its gold amount — no sub-tile position, because the
simulation has none.

```
WorldState ──► WorldView ──► ScenePresenter ──► IRenderTarget
                                  │                   ├── DungeonScene   (Unity)
                             AssetProviderChain        └── (a text view redraws
                                  │                        from WorldView direct)
              replacement ──► converted ──► placeholder
```

- **`ScenePresenter` diffs** and pushes only what changed, in a fixed order
  (tiles row-major, then objects, then creatures — both in spawn order,
  removals ascending) so a recorded call sequence is reproducible. An unchanged
  world pushes nothing. Objects go before creatures because a creature stands on
  one.
- **`IRenderTarget` is nine methods with no logic in them.** Everything an
  engine adapter could get wrong is upstream of it and tested.
- **`TextRenderer` is the second renderer**, and the one that runs in CI. It
  redraws everything, because a character grid is free — deliberately the
  opposite strategy, which is what proves the boundary assumes neither.

## Assets (M3, done 2026-09-04)

Goal 4's `Game Definition → Asset Reference → Asset Provider → Actual Asset`
(ADR-0034). `AssetRef` is a validated logical key built from a record's **name**,
never its id. A chain resolves it — replacement pack, then converted art, then
`PlaceholderProvider`, which is **total**: every key the shipped database implies
resolves with no DK2 install and no art pack, and `AssetCatalogueTests` checks
exactly that. **No filename literal exists outside `DK2.Assets`.**

## Unity (M3, done 2026-09-04)

`unity/DK2.Unity` — Unity 6 LTS `6000.6.0f1` (ADR-0036). It consumes the
netstandard2.1 DLLs from `Assets/Plugins/DK2` (synced by
`unity/sync-assemblies.ps1`; they are build output and gitignored) and owns no
game state: `DungeonRunner` ticks a `GameSimulation` and calls
`ScenePresenter.Present`, and `DungeonScene` implements `IRenderTarget` in
GameObjects. Deleting every GameObject would change nothing, because the
simulation references none of it.

`unity/DK2.Unity.Compile` compiles the adapter against Unity's real module
assemblies, which is the only way "do not commit code you have not built"
reaches Unity code. It is outside `DK2.slnx` on purpose — CI has no Unity. See
`unity/README.md`.

## Determinism (M2)

The load-bearing contract: **same database + same initial world + same seed +
same input frames produce the same `GameSimulation.StateHash()` at every tick,
on any machine.** Enforced by `DK2.Simulation.Tests.DeterminismTests` and by the
CI gate, which runs it on Linux and Windows.

- `Tick(in InputFrame)` is the only way time moves; systems run in explicit
  registration order.
- The RNG's position is part of the state hash — two runs that look identical
  but have consumed different draws are about to diverge.
- `Hash64` is FNV-1a. `GetHashCode` is forbidden in the hash path (.NET
  randomises string hashing per process).
- One pinned hash literal guards cross-machine stability. Changing simulation
  state deliberately means bumping `GameSimulation.HashVersion` and updating the
  literal in the same commit.
- Events are write-only: attaching a recorder must not change behaviour.
- `WorldSnapshot` round-trips are verified by continuing both runs for hundreds
  of ticks, not by comparing bytes.

Full rationale: `docs/architecture/simulation-loop.md`.

## Scenarios (M2)

`scenarios/**.yaml` are declarative experiments, run headlessly by
`DK2.Harness`. The runner reports every part of a scenario it could not honour
— unknown keys, unapplied setup, observations nothing produces yet — so a run
can never look more complete than it is. See `scenarios/README.md`.

## Tile mechanics (M3)

The first real gameplay. Digging, mining, claiming, reinforcing, repairing,
attacking and filling in are one uniform, entirely data-driven rule in
`DK2.Simulation.TileWorkRules`; the numbers come from the terrain table and the
ten health-delta variables, and none of them appears in the code.

- `TileWorkRules` is pure and static: no world, no RNG, no time. It answers
  "does this kind of work apply to this tile" and "what does one application
  do", and nothing else.
- `TileWorkSystem` drains a FIFO queue of `TileWorkOrder`s on the world. Order
  is behaviour, so the queue is never sorted or deduplicated.
- `TileTagSystem` applies `TagTilesCommand` — the first real player command.
  Markers are per player, one bit each, and are cleared when a tile converts
  into terrain that cannot carry one (so a gem face, which collapses into
  itself, keeps its mark).
- The rules are written against terrain **flags**, never terrain ids. A rule
  keyed on id 2 would be a rule about the reference install rather than DK2.

Gold released by mining is reported and dropped rather than credited, because
the treasury it would go to does not exist yet.

Timing is the open wound: the health deltas carry no unit (Q19, tied to Q1), so
tick counts are not seconds. Everything that relates ticks to seconds lives in
`SimulationTiming` and nowhere else.

Evidence, confidence and the three anomalies the data throws up:
`docs/research/tile-work.md`.

## Workers (M3)

The producer the work queue was written to wait for. Anything flagged
`IsWorker` — the Imp and the Dwarf, never creature id 1 — looks for the nearest
tile its keeper has marked, walks to it, and works on it.

- `CreatureState` is the mutable creature state: position in fixed point,
  activity, target, timer and route, and — since the mechanics that read them
  arrived — health, hunger, anger, level and experience. **Only fields a mechanic
  actually reads are there**, which is also why one has since *left*: bed
  ownership moved to the object (see **Objects** below).
- `ImpTaskSystem` decides. One outward search from the worker answers "which job
  is nearest", "how do I get there" and "is anything reachable at all" at once.
  It queues `TileWorkOrder`s; it never applies them.
- `CreatureMovementSystem` walks. Speed is read as tiles per second
  (`docs/research/imp-tasks.md`), spent per tick, with the remainder carried so
  nothing drifts slow.
- `DK2.Pathfinding` holds the search behind `IPathfinder`. It knows nothing of
  terrain or creatures: `WorldPathGrid` decides what "walkable" means for one
  particular mover, from terrain flags and that creature's own flags.
- `SimulationSystems.Default()` is the one declaration of system order (ADR-0013).

Two things are deliberately absent, and both are reported by every scenario run
that places a worker. **Workers only do marked work** — claiming, reinforcing,
carrying gold and fleeing are real imp jobs whose trigger and priority order are
UNKNOWN (Q24, ADR-0014). And **routing is ours, not DK2's** (Q5, ADR-0012):
four-connected, uniform cost, deterministically tie-broken, and an admitted
placeholder behind an interface.

Nothing generates workers either. DK2 makes imps from a dungeon heart, from a
spell at a mana cost, and from specials; `WorldState.SpawnCreature` is what tests
and scenario files use in the meantime, and it says so.

Evidence and confidence: `docs/research/imp-tasks.md`.

## Rooms and gold (M3)

The second player command, and the first resource that is somewhere rather than
just a number.

- `RoomRules` says where a room may go, what it costs and what it does to the
  tile. Where it may go is read from the room's own placeable-on flags, so no
  code knows what a bridge is (ADR-0016).
- `BuildRoomCommand` + `RoomBuildSystem` are the command path, alongside
  `TagTilesCommand` + `TileTagSystem`. A tile at a time, in the order the command
  gives, with every refusal counted by reason in the event log.
- `GoldStore` is the only way gold moves. **A player has no gold counter**:
  `TileState.GoldHeld` is where gold is, and a keeper's total is the sum over the
  tiles they own (ADR-0015). Every limit DK2 sets on gold is per tile, which is
  what forced the shape.
- `CreatureState.GoldHeld` is the fourth place gold can be, and the reason mined
  gold ever reaches a treasury. An imp mines into its own hands up to the
  CREATURES record's `maxGoldHeld`, walks to the nearest tile of its keeper's
  with room, and puts down what fits (ADR-0019, Q31). `TileWorkOrder` names the
  creature that swung so the resolver knows whose hands to fill.

Four behaviours fall out rather than being written: a keeper with no treasury
cannot keep gold, destroying a treasury takes the gold with it, a keeper's first
treasury is necessarily paid for out of the dungeon heart, and a load too big for
one treasury tile spills into the next — because the carrier puts down what fits
and looks again, not because anything implements spillover.

What is still absent, and it is much less than it was: **most rooms do nothing.**
A treasury and a heart hold gold because a variable says how much per tile. Four
now do more — a **portal** produces creatures, a **lair** attracts them and holds
one bed per tile, a **hatchery** knows its coops and its flock, and a **temple**
has its rim capacity recorded if not used. Everything else is inert terrain its
owner holds. Rooms cannot be sold (Q34) and are built instantly — building over
time is another worker job absent from the priority order the owner gave (Q24).

Gold an imp cannot hold is still lost. DK2 leaves a lump on the floor for
somebody to come back for; gold on the floor is **three of the 145 objects** and
there is now a list to put an instance in, so what is missing is the mechanic
rather than the record. The imp keeps mining, which is the half that matters.

Evidence and confidence: `docs/research/rooms-and-gold.md`.

## Objects (M6)

The third list in `WorldState`, after tiles and creatures — and the shape was
read out of the binary rather than chosen (ADR-0054, `docs/research/objects.md`).

- **DK2's object is one type with an 18-byte tagged union.** Its own debug dump
  prints the header `Union Data:` and then one of twenty-three blocks, every one
  reading from the same offset; the tag is the object's definition id. So a bed,
  a chicken, a boulder and a gold pile are one kind of thing in different states,
  not four kinds of thing.
- **`ObjectState` mirrors the semantics and not the layout.** Fidelity is
  required of behaviour, not of storage (brief item 5), and the behavioural
  content of a union is that at most one kind's state is live. Ours is flat, with
  a documented owner per field, **every field hashed unconditionally** — a
  kind-dependent hash would let a stray write hide — and fields added only when a
  mechanic reads them, the same rule `TileState` follows.
- **`WorldState.Objects` is one list in placement order**, and the order is
  behaviour for the same reason the creature list's is.
- **A bed is an object, and the object owns the relation.** `Lair.CreatureId` is
  how DK2 records whose bed a bed is; this project had kept the same fact on the
  creature as a tile index, and **two copies of one fact is the shape behind
  three of its longest-lived defects**. There is now one, on the object's end.
  A bed exists only while owned — claiming a lair tile creates one of the
  creature's own kind, releasing it destroys it — so occupancy needs no
  bookkeeping at all. CONFIRMED by the owner: a lair tile is bare until somebody
  moves in.
- **Rules ask the definition's type flag, not the id**, because the original's
  own dispatch does and gets more from it: one `TypeLair` branch serves all
  twelve bed objects. The exception is where DK2 itself names an id — the Chicken
  Coop carries no type bit at all — and `HatcheryRules` says so where it does it.

## Creature needs (M4)

What a creature is, beyond where it is standing. Every rate comes from the
record or the balance table; none is in the code.

- **Health** — maximum from the record scaled by level, regeneration on your own
  land (two creatures out of 46 have any), healing at 100 a second **only while
  asleep in its own bed**, and terrain damage gated on the crossing flags.
- **Hunger** — a clock that fills `ChickensNeeded` and cannot yet be satisfied.
  It costs **no health on purpose**: there is no starvation anywhere in the
  balance table, because what going unfed costs is anger.
- **Anger** — a measured ceiling of 32767 (the owner, twice, two methods), a
  ladder of content → unhappy → angry, and a consequence: an angry evil creature
  walks to the nearest portal and leaves; a converted one turns turncoat.
- **Levels** — a per-level scaling table of 32 stats by 9 levels that had been
  parsed and read by nothing. Imps gain experience by working; the training room
  and combat pit caps are shipped variables.
- **Death is a process**, not an event: collapse at zero, a rescuable window,
  then removal. A corpse is **a creature in a state**, not an object — a
  correction that shrank the graveyard from "parse a record" to "write a carry
  task".

## Mana (M3)

The first thing in the simulation that changes because time passed, rather than
because a player or a worker did something.

- `ManaRules` is the whole model: a keeper's income is the sum of
  `TerrainDef.ManaGain` over the tiles they own, plus a flat 30 if they have a
  dungeon heart, capped by `MaximumManaGainPerSecond`. `MaximumManaThreshold` is
  a flat 200000 ceiling for every keeper (CONFIRMED by observation), soft rather
  than a clamp: mana above it drains back.
- The heart's 30 is counted **once per keeper, not once per heart tile**, and
  that is exact rather than a simplification — a keeper has at most one dungeon
  heart, ever, and losing it is defeat. So no room-instance concept is needed to
  count hearts, which was the thing blocking it.
- `ManaSystem` pays it once a game second (ADR-0017) and runs last, after
  everything that could change what a keeper owns.
- Mana stays a player-level number, unlike gold. The asymmetry is the data's:
  every limit DK2 sets on gold is per place, and every variable it sets on mana
  is per player.

**Mana income is territory**, and no code says so. Three terrain types out of 41
pay anything, and they are exactly the ownable non-room ones — so claiming ground
raises income, losing it lowers it, and building a room on your own floor takes
that floor off the payroll, all without a rule about claiming, losing or
building. `scenarios/mana/land_pays_a_keeper.yaml` shows the last of those: the
rate falls from 38/s to 35/s when three of eight claimed tiles become a treasury.

What is deliberately absent: **nothing spends mana** (every spender is a spell,
M7), and **nothing happens when a heart falls** — losing it is defeat in DK2, and
defeat is a win-conditions mechanic that belongs with objectives at M6 (Q40).

Evidence and confidence: `docs/research/mana.md`.
