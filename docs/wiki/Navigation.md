# Navigation

`Research: read` · `Simulation: built` · `Verified vs original: measured` · `Unity: drawn`

Route finding, the navigation mesh, steering bodies and movement: how a Dungeon Keeper 2 creature decides where to walk and how it gets
there.

## Behaviour, in our words

**DK2 does not route on a tile grid.** It triangulates the map's per-tile navigation bytes into a mesh, searches that mesh greedily from
both ends, keeps the shorter of the two funnelled results, and walks the midpoints of the gates between triangles (STRONGLY INFERRED,
binary: [mesh and search](https://github.com/franjom/netherhold/blob/master/docs/research/navigation-mesh-and-search.md), [funnel and
gates](https://github.com/franjom/netherhold/blob/master/docs/research/navigation-and-steering.md)). The owner's walk of ten tiles across
and three up ran as one straight line, which a tile route cannot produce (CONFIRMED, owner). The search keeps no distance travelled, only
the straight-line distance to the goal, so it is neither Dijkstra nor A\* and can take a longer corridor (STRONGLY INFERRED).

**What may be walked** comes from one byte per tile: solid, water, lava, a door's owner and lock bits, or a portal's centre hole. A
creature's own flags and the option word its state passes decide which of those it may enter, so most walkers refuse other keepers' doors
while an Imp on its way ignores doors altogether (STRONGLY INFERRED, binary; CONFIRMED against the route log, Q357). When a tile's byte
changes, DK2 edits the mesh in place around that tile instead of rebuilding it ([per-tile
rebuild](https://github.com/franjom/netherhold/blob/master/docs/research/navigation-partial-rebuild.md), STRONGLY INFERRED).

**Movement is a steering system.** Each creature has a steering body holding a position, a velocity and a window of up to five route points.
Eight behaviours are named in the binary; three are stubs, one is never registered, and four are summed as clamped forces and integrated by
plain Euler steps: avoid walls, repel obstacles, follow path and separation ([steering
behaviours](https://github.com/franjom/netherhold/blob/master/docs/research/steering-behaviours.md), STRONGLY INFERRED). A creature turns to
face its next point before it moves, and an Imp cuts a corner half a tile early where other creatures cut a quarter. Creatures push each
other (CONFIRMED, owner, Q345); the push is built from the binary's overflowing arithmetic, reproduced rather than corrected (STRONGLY
INFERRED).

**How close is ours?** DK2 can log every routing request it makes. Replayed on DK2's own dumped navigation bytes, 21,288 of 21,298 Level 7
requests agree. On a Level 10 played by a keeper, the first replay explained all but three of 86,140 requests by one of its mover
profiles, and corridor lengths are exact for 18,273 of the 20,926 requests scored for them
([captures](https://github.com/franjom/netherhold/blob/master/docs/research/navigation-captures.md)). These are measured: the log is
DK2's, the agreement is our run. That measures
our router against DK2's bytes, not a full simulated game. The captures are game-derived and not in the repository, so the note is the
record.

## Numbers from the game database

- **Creature `Speed` and `RunSpeed`** set the body's top speed (`CreatureMovementSystem.MaxSpeedOf`); status effects and water adjust it.
- **Footprint radius** (creature record, offset `0xEA0`) sets the body radius and each creature's share of the seek's crowd term; **size
  class** (`0xEA8`) indexes the separation table.
- **Turn rate** (creature record, offset `0xF20`) becomes `SimulationTiming.TurnRatePerTick`.
- **`CanWalkOnWater` and `CanWalkOnLava`** (definition flags) decide whether categories 1 and 3 are passable to a mover.
- **Terrain, door and room records** decide each tile's navigation byte ([structure-blocks.md
  §5.4](https://github.com/franjom/netherhold/blob/master/docs/research/structure-blocks.md)).

The behaviour weights, force and speed defaults, the 1024-entry search heap and the cost shifts are literals in DKII.EXE, not database
records. They are kept as rule constants citing their note.

## Evidence

| What | Where |
|---|---|
| Route driver: both searches, keep the shorter | [`004CD920`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004CD920.md), [`004CD000`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004CD000.md) |
| Step cost (solid, doors, water, lava, portal hole) | [`004C8BD0`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004C8BD0.md) |
| Corridor to waypoints (funnel) and gate walk | [`004CEF40`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004CEF40.md), [`004CF490`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004CF490.md) |
| Per-tile mesh edit | [`004CE770`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004CE770.md) |
| Steering integrator and `Follow_Path` | [`004D4760`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004D4760.md), [`004D3C40`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004D3C40.md) |
| Research notes | [`navigation-mesh-and-search.md`](https://github.com/franjom/netherhold/blob/master/docs/research/navigation-mesh-and-search.md), [`navigation-and-steering.md`](https://github.com/franjom/netherhold/blob/master/docs/research/navigation-and-steering.md), [`steering-behaviours.md`](https://github.com/franjom/netherhold/blob/master/docs/research/steering-behaviours.md) |
| Replay of DK2's route log (Level 7, Level 10) | [`navigation-captures.md`](https://github.com/franjom/netherhold/blob/master/docs/research/navigation-captures.md); [GOAL_ALIGNMENT.md](https://github.com/franjom/netherhold/blob/master/docs/GOAL_ALIGNMENT.md) row 5 |
| Mesh edits against a Level 10 capture | [`navigation-partial-rebuild.md`](https://github.com/franjom/netherhold/blob/master/docs/research/navigation-partial-rebuild.md), Q591 |
| Body keeps its 16.16 position between steps | [`steering-body-retention.md`](https://github.com/franjom/netherhold/blob/master/docs/research/steering-body-retention.md), Q735 |

## Our implementation

- **Code:** the router lives in [`src/DK2.Pathfinding`](https://github.com/franjom/netherhold/tree/master/src/DK2.Pathfinding):
  [`NavMesh`](https://github.com/franjom/netherhold/blob/master/src/DK2.Pathfinding/NavMesh.cs),
  [`MeshRouter`](https://github.com/franjom/netherhold/blob/master/src/DK2.Pathfinding/MeshRouter.cs),
  [`Funnel`](https://github.com/franjom/netherhold/blob/master/src/DK2.Pathfinding/Funnel.cs) and
  [`GateWalk`](https://github.com/franjom/netherhold/blob/master/src/DK2.Pathfinding/GateWalk.cs). The behaviours, route window and
  integrator are in [`src/DK2.Simulation/Steering`](https://github.com/franjom/netherhold/tree/master/src/DK2.Simulation/Steering).
  [`WorldSteering`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Navigation/WorldSteering.cs) joins them to the
  world and
  [`CreatureMovementSystem`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Systems/CreatureMovementSystem.cs) runs
  the mover's tick.
- **Tests:** in [`tests/DK2.Simulation.Tests`](https://github.com/franjom/netherhold/tree/master/tests/DK2.Simulation.Tests):
  `MeshRouterTests`, `GateWalkTests`, `NavMeshBuildTests`, `NavMeshPartialTests`, `NavMeshLocateTests`, `SeparationTests`,
  `SteeringBodyRetentionTests` and the [`Steering`](https://github.com/franjom/netherhold/tree/master/tests/DK2.Simulation.Tests/Steering)
  folder. [`tests/DK2.Oracle.Tests`](https://github.com/franjom/netherhold/tree/master/tests/DK2.Oracle.Tests) (`CapturedSteeringTests`,
  `OriginalGateWalkTests`) and
  [`NavMeshBuildOnRealDataTests`](https://github.com/franjom/netherhold/blob/master/tests/DK2.Behavior.Tests/NavMeshBuildOnRealDataTests.cs)
  need a DK2 install or capture and are skipped without one.
- **Scenarios:** none is dedicated to navigation; every scenario that moves a creature runs it.
- **Decisions:** ADR-0155 and ADR-0157 (the rebuild on the original's mesh), ADR-0461 and ADR-0486 (per-tile edit, DK2's locate), ADR-0513
  (a route request zeroes velocity), ADR-0626 (native neighbour order), ADR-0632 (walks end at a whole route progress), ADR-0644 (body keeps
  its fractions). Full list: [[Decisions by System|Decisions-by-System]].

## Divergences and open questions

- **Grid code survives.** `GridPathfinder` still chooses *which* work to walk to by walking distance, and `ReachableTiles` floods the grid
  for reachability (ADR-0012, ADR-0453); the route to the chosen target comes from the mesh. DK2 chooses by kind lists and ring scans.
- **Not built:** the mesh's region ids and width classes (Q348; the shipped game never engages the classes, and ours relies on the search
  failing); the remembered-entity term of separation (Q345, its writer is UNKNOWN); the radius reset DK2 applies in its hostile-door branch.
- **Mesh details:** on a Level 10 capture our whole-map build matches DK2's first dump in 2,826 of 2,828 triangles, and our per-tile edits
  reproduce 130 of 140 dump-to-dump intervals slot for slot (ADR-0486). No route log was captured with those dumps, so whether the remaining
  differences change any route or a campaign outcome is open (Q591).
- **Unmeasured:** whether any shipped map has a gate of sixteen tiles or more, and whether a layout reaches the original's possible division
  fault in the gate cap (UNKNOWN; ours reports no crossing).
- **Open:** a steered creature may cross between two solid corners, though routing refuses it; which the owner saw is open (Q28). The
  route-window gap behind Level 5 Heart defence is part of Q712. See [[Open Questions|Open-Questions]].

---

*Last verified against `2f91419d` on 2026-11-05.*
