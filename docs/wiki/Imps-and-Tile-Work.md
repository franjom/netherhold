# Imps and tile work

`Research: read` · `Simulation: built` · `Verified vs original: measured` · `Unity: partial`

Digging, claiming, reinforcing, gold mining and every other imp job on tiles: how an imp picks a job, walks to it and changes the tile.

## Behaviour, in our words

**Choosing a job.** An imp's selection state is a fixed routine (STRONGLY INFERRED, read in the binary). It first retries the kind of job it did last, so an imp that has just claimed a tile claims again before it digs. Then comes **Tier A**, a whole-map search by kind: loose objects and crates, then marked excavation by straight-line distance, then claiming by an outward ring scan. If that finds nothing, **Tier B** scans tiles outward from the imp and mixes reinforcing, repair, gold on the floor and objects by distance, so a near pile of gold can beat a far wall while a wall beats gold on the same tile. The owner saw walls beat gold on the floor, near and far (CONFIRMED, Q147); the two tiers themselves are read in the binary (STRONGLY INFERRED). Detail: [imp-task-priority.md](https://github.com/franjom/netherhold/blob/master/docs/research/imp-task-priority.md), [imp-selection-evidence.md](https://github.com/franjom/netherhold/blob/master/docs/research/imp-selection-evidence.md).

**Claiming and reinforcing.** Both need a tile that touches the keeper's own floor (STRONGLY INFERRED). A freshly dug or mined tile is not claimed by a search of its own: the imp claims it only when a walk passes over it and no neighbour is still marked for digging (STRONGLY INFERRED, Q154, Q489, [imp-claim-path.md](https://github.com/franjom/netherhold/blob/master/docs/research/imp-claim-path.md)). A rival's floor is attacked down to Dirt Path first and then claimed (CONFIRMED by the owner, Q68). When a reinforcer finishes a wall it may also fill the diagonal corner walls (STRONGLY INFERRED, Q694, [wall-corner-fill.md](https://github.com/franjom/netherhold/blob/master/docs/research/wall-corner-fill.md)).

**Doing the work.** Every job ends in one tile applier: it adds a signed health delta to the tile and converts the terrain when health reaches zero or the terrain's maximum (STRONGLY INFERRED in the binary; the per-application deltas were measured tile by tile, CONFIRMED, [tile-work.md](https://github.com/franjom/netherhold/blob/master/docs/research/tile-work.md)). A miner's first strikes land two and six ticks after it starts, then every tick (CONFIRMED in a capture, [mining-cadence.md](https://github.com/franjom/netherhold/blob/master/docs/research/mining-cadence.md)). Mined gold goes into the imp's hands and is deposited later; workers share three slots on each face of a wall, and a nearer imp can take a job from a farther one (STRONGLY INFERRED, [imp-task-stealing.md](https://github.com/franjom/netherhold/blob/master/docs/research/imp-task-stealing.md)).

**With nothing to do.** The owner saw idle imps run, stop, seem to wait and run again (CONFIRMED, Q41). The binary explains it: the imp rests, then wanders to an owned tile a few steps out or into an owned room (STRONGLY INFERRED, [imp-idle-and-wandering.md](https://github.com/franjom/netherhold/blob/master/docs/research/imp-idle-and-wandering.md)). The idle code never targets scenery or creatures, so the earlier account of imps interacting with them has no support there (STRONGLY INFERRED, ADR-0018 amendment).

## Numbers from the game database

- **Terrain records:** `startingHealth`, `maxHealth`, `maxHealthTypeTerrainId`, `destroyedTypeTerrainId` and the flags (Solid, Taggable, Ownable, Attackable and others). The terrain a tile becomes comes only from these fields; see [tile-work.md](https://github.com/franjom/netherhold/blob/master/docs/research/tile-work.md), "The data".
- **`GlobalVariables`:** the ten work deltas, listed by name in the same note. A game second is four ticks (ADR-0089), and one application of work is a fraction of it.
- **The worker's rate:** a creature efficiency, 80% at spawn, plus two ten-point bonuses (the slap is one); level does not change it ([tile-work-efficiency.md](https://github.com/franjom/netherhold/blob/master/docs/research/tile-work-efficiency.md)). The 80% comes from DK2's creature constructor, not from a data file, and lives in `EfficiencyRules`.
- **Idle timing:** DK2 never reads the shipped `ImpIdleDelayBeforeReevaluationSeconds` (ADR-0224).

## Evidence

| What | Where |
|---|---|
| State 10, the task selection (retries, Tier A, Tier B, idle deadline) | [`00463DF0`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/00463DF0.md) |
| Idle wait and wandering | [`00464240`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/00464240.md) |
| Dig or mine job choice, nearest face slot | [`00469AF0`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/00469AF0.md) |
| Per-kind search and its ring scan | [`00468520`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/00468520.md), [`00468D10`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/00468D10.md) |
| The tile applier every job ends in | [`00452F60`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/00452F60.md) |
| Claim and reinforce precondition (touches own floor) | [`00453B30`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/00453B30.md) |
| State 22, reinforcing and corner fill | [`00466390`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/00466390.md) |
| Research notes | [imp-task-priority](https://github.com/franjom/netherhold/blob/master/docs/research/imp-task-priority.md), [imp-tasks](https://github.com/franjom/netherhold/blob/master/docs/research/imp-tasks.md), [tile-work](https://github.com/franjom/netherhold/blob/master/docs/research/tile-work.md), [wall-corner-fill](https://github.com/franjom/netherhold/blob/master/docs/research/wall-corner-fill.md) |
| Native Level 2 capture: strike cadence | [`mining-cadence.md`](https://github.com/franjom/netherhold/blob/master/docs/research/mining-cadence.md) (Q480) |
| Native Level 4 captures, two DK2 seeds against ours | [`imp-fidelity-integration.md`](https://github.com/franjom/netherhold/blob/master/docs/research/imp-fidelity-integration.md), [`level4-outcomes.md`](https://github.com/franjom/netherhold/blob/master/docs/research/level4-outcomes.md) |
| Owner observations | Q24, Q41, Q68, Q147 in [`open-questions.md`](https://github.com/franjom/netherhold/blob/master/docs/research/open-questions.md) |

## Our implementation

- **Code:** [`ImpTaskSystem`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Systems/ImpTaskSystem.cs) and its partial files (selection, work, gold, wandering and more) in [`Systems/`](https://github.com/franjom/netherhold/tree/master/src/DK2.Simulation/Systems); [`TileWorkSystem`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Systems/TileWorkSystem.cs) and [`TileTagSystem`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Systems/TileTagSystem.cs); rules in [`TileWork.cs`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/TileWork.cs), [`ReinforcementRules`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/ReinforcementRules.cs), [`OwnFloorRules`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/OwnFloorRules.cs), [`MiningSlots`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/MiningSlots.cs), [`TaskStealing`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/TaskStealing.cs) and [`Efficiency.cs`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Efficiency.cs).
- **Tests:** in [`tests/DK2.Simulation.Tests`](https://github.com/franjom/netherhold/tree/master/tests/DK2.Simulation.Tests): `ImpTests`, `ImpFetchesGoldTests`, `ImpClaimsWhatItDugTests`, `ImpClaimSearchTests`, `ImpWanderTests`, `TileWorkTests`, `DugOutTilesStayDugOutTests`, `ReinforceEligibilityTests`, `ReinforcementCornerTests`.
- **Scenarios:** [`scenarios/imps`](https://github.com/franjom/netherhold/tree/master/scenarios/imps) and [`scenarios/tiles`](https://github.com/franjom/netherhold/tree/master/scenarios/tiles).
- **Decisions:** ADR-0018 (priority order, amended 2026-11-05 after a binary check), ADR-0056 and ADR-0058 (gold and the two tiers), ADR-0089 (a tick is 250 ms), ADR-0224 (rest and wander), ADR-0286 (claim on the way), ADR-0605 (corner fill); the full list is [[Decisions by System|Decisions-by-System]].

## Divergences and open questions

- **Level 4 Imp deaths.** Against two native recordings (DK2 Imp deaths 0 and 19), ours sat inside the native range on every count except Imp deaths in two of five seeds (compared at hash 286). No rule difference is found; the residue is **Q710** (OPEN), after Q665 was answered ([level4-outcomes.md](https://github.com/franjom/netherhold/blob/master/docs/research/level4-outcomes.md)).
- **No equivalence claim.** The integration note states that no complete chooser, action-scheduler or campaign equivalence is established, and the corner fill and navigation-refresh timing have no native transition capture ([wall-corner-fill.md](https://github.com/franjom/netherhold/blob/master/docs/research/wall-corner-fill.md)).
- **Q563** (OPEN, part built): DK2's Drako claims the hole in Morgana's wall much later than ours.
- **Q415** (OPEN, STRONGLY INFERRED no): nothing enters states 9 or 88.
- **Fill-in (Q74, mostly answered).** No fill-in task is known: `FillInHealth` and the Claimed Path route to Rock are most likely editor-only or cut, and the shipped level triggers write terrain directly; ours has no fill-in job.
- See [[Open Questions|Open-Questions]] for the full list.

---

*Last verified against `2f91419d` on 2026-11-05.*
