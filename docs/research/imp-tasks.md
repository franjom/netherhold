# The first worker: how an imp finds work and gets to it

Milestone M3. Who does the tile work that `tile-work.md` describes, and how they
come to be doing it.

**Source(s):** reference install (`dk2tools kwd creature Imp`, `dk2tools kwd
vars`); owner observations 2026-08-29 to 2026-09-06 (Q24, Q26/Q27, Q31, Q41–Q44,
Q47); `DKII.EXE` v1.70 for the priority order (`imp-task-priority.md`).
**Original version:** DK2 v1.70.
**Implementation:** `DK2.Simulation.Systems.ImpTaskSystem`,
`DK2.Simulation.Systems.CreatureMovementSystem`, `DK2.Simulation.WorldPathGrid`,
`DK2.Simulation.WorkStance`, `DK2.Simulation.ImpCrowding`,
`DK2.Pathfinding.GridPathfinder`.
**Tests:** `ImpTests`, `WorkStanceTests`, `GoldCarryingTests`,
`ImpFetchesGoldTests`, `ImpClaimsWhatItDugTests`, `PathfindingTests`,
`TileWorkTests.EveryTerrainAdmitsAtMostOneKindOfMarkedWork`;
`ImpsOnRealDataTests` against the install; scenarios under `scenarios/imps/`.

---

## Summary of confidence

| Claim | Status |
|---|---|
| Imp and Dwarf are the game's workers, identified by a flag rather than by id | **CONFIRMED** — `IsWorker` falls on exactly those two (`ImpsOnRealDataTests.TheWorkerFlag_FallsOnExactlyTheImpAndTheDwarf`) |
| A marked tile is dug, mined or dug-through according to its terrain flags and owner | **CONFIRMED** — the four kinds are mutually exclusive on the shipped data |
| An idle worker re-evaluates every `ImpIdleDelayBeforeReevaluationSeconds` (5) | **CONFIRMED** — the variable names exactly this |
| Creature `speed` is in tiles per second | **STRONGLY INFERRED** (Q29) — see below |
| Walkability follows `CanWalkOnWater` / `CanWalkOnLava` | **STRONGLY INFERRED** — the flags exist for nothing else |
| **Job priority is two tiers: marked excavation then claiming, each whole-map; then the nearest of reinforcing and floor gold, wall first on a tie** | **READ from the binary and CONFIRMED by the owner twice** — the order in 2026-08 (Q24, ADR-0018), and that a marked wall beats gold near or far on 2026-09-05 (Q147). Gold's place is DK2's (Q146, ADR-0058); the distance metric is ours (Q25). `imp-task-priority.md` |
| A **wall** is worked from an orthogonal neighbour | **CONFIRMED** — twelve imps a block at three a face is four faces (Q27) |
| **Floor is worked standing on the tile itself** | **CONFIRMED** for claiming (Q44) — *"imps claim tiles by jumping on them"*; ADR-0028 |
| Repairing and attacking floor use the same stance as claiming | **STRONGLY INFERRED** (Q49) — identical geometry, and no observation distinguishes them |
| The ~500 ms an imp lingers on a tile it has just claimed | **UNKNOWN** (Q49) — cooldown or animation, indistinguishable from outside; not modelled |
| An imp with none of the three available wanders and interacts with scenery and creatures | **CONFIRMED but NOT IMPLEMENTED** (Q41) — ours stands still |
| Three imps a face for a wall, and the surplus goes elsewhere | **CONFIRMED and IMPLEMENTED** (Q26/Q43, ADR-0026) — `ImpCrowding.MaxWorkersPerFace` = 3 |
| **One** imp for a tile it stands on to claim | **CONFIRMED and IMPLEMENTED** (Q43, ADR-0030) — `ImpCrowding.MaxWorkersOnTheTile` = 1; the cap is per job, not per tile: three share a tile to reach a wall and one may stand on floor |
| Claiming and reinforcing spread from ground the keeper already holds | **CONFIRMED** (Q42, restated by the owner 2026-09-03: imps claim tiles adjacent to claimed tiles) — supported by Q33, where a room tile must touch owned land. Which tile carries the condition follows from the stance (ADR-0028) |
| Whether "adjacent" there includes diagonals | **CONFIRMED — no** (Q66, owner 2026-09-07). Orthogonal is what is implemented, and it only started mattering once movement went eight-connected (ADR-0025) |
| A worker takes the marked tile fewest steps away | **HYPOTHESIS** (Q25) |
| A worker that finishes a job starts the next one at once rather than waiting out the idle delay | **STRONGLY INFERRED** — an imp that has just finished has not been idle |
| Imps never eat | **CONFIRMED** — owner observation; the Imp's hunger rate of 32767 does mean "never" (Q12) |
| An imp carries mined gold to a treasury or heart tile itself | **CONFIRMED** — owner observation (Q31); the tail of a mining job, not a job |
| Where an imp breaks off mining to go and unload | **STRONGLY INFERRED** (Q50) — when one more swing would not fit; the shipped numbers make that five swings and 1363 gold |
| An imp with a part-load tops up from a pile it passes | **READ** (Q142) — until its hands hold a level-scaled share of `MaxGoldHeld`; ours gets the same outcome because a loaded imp is delivering before selection runs |
| An imp with nowhere to unload keeps mining | **CONFIRMED** (Q31) — but the floor lump it should leave is NOT IMPLEMENTED, so the overflow is lost (Q138, Q140) |
| Routing: connectivity and step cost | **CONFIRMED** eight-connected with cut corners (Q28); **CONFIRMED** no terrain weighting (Q30); **STRONGLY INFERRED** octile 10/14 (Q48). Tie-breaking is ours. The original's algorithm is **UNKNOWN** (Q5) |

---

## The data

The Imp, from the reference install:

```
Imp (id 1)  class=Scout style=NonFighter
  hp=500 dmg=30 pay=0 fear=5 threat=2
  speed=1.84 run=1.84 see=4 hear=4
  hungerRate=32767 hungerFill=0 awake=32767 sleep=0
  exp/lvl=3000 exp/s=1200
  happy   : Explore(100%)
  flags   : 0xB000089F IsWorker, CanBePickedUp, CanBeSlapped, AlwaysFlee,
            CanWalkOnWater, IsEvil, ..., IsMale
  raw     : speed=7536 run=7536 shuffle=4096 height=2048 see=16384 hear=16384
```

- **The Imp has no jobs.** Its happy-job table is a single entry, `Explore` at
  100%; its unhappy and angry tables are empty. Dig, claim, reinforce, carry
  gold and drag bodies are engine behaviour attached to the `IsWorker` flag, not
  job-table entries. STRONGLY INFERRED; it is why `ImpTaskSystem` is a separate
  system from the creature job system (`job-selection.md`).
- **The Imp has no needs.** `hungerRate` and `timeAwake` are both 32767 — the
  "never" sentinel (Q12) — and `pay` is 0. An imp does not eat, sleep or get
  paid.
- **`AlwaysFlee` is set.** Imps run from combat. Not implemented; there is no
  combat system to flee from.

The decimal forms above are lossy; the fixture's first transcription of
`hungerRate` was 32767 where the raw value is 134213632, which
`ImpsOnRealDataTests.TheImpFixture_MatchesTheInstall` exists to catch.

Global variables that name imps directly (`dk2tools kwd vars`):

| id | variable | value | read by |
|---|---|---|---|
| 30 | `ImpIdleDelayBeforeReevaluationSeconds` | 5 | `ImpTaskSystem` |
| 94 | `MinimumImpThreshold` | 4 | `ImpSupplyRules` (`imps-and-mana.md`) |
| 95 | `TimeBeforeFreeImpGeneratedSeconds` | 5 | `ImpSupplyRules` |
| 110 | `InsufficientManaImpEvaluationPeriodSeconds` | 10 | nothing (Q57) |
| 144 | `ImpPopCountdownTimeSeconds` | 10 | nothing yet |
| 179 | `ImpExperienceGainPerSecond` | 20 | `CreatureLevelRules` |
| 188 | `SpecialReceiveImpsAmount` | 10 | nothing yet |
| 184 | `DwarfDiggingMultiplier` | 4 | nothing yet |

`ImpsOnRealDataTests.TheImpsOwnBalanceVariables_AreAllInTheGlobalTable` asserts
30, 94, 95, 144 and 179 are present with these values, so an unread one surfaces
as a failing test rather than a `KeyNotFoundException` mid-run.

## Speed is tiles per second

`CreatureDef.Speed` is a 4096-denominator fixed-point number and the creature
record does not state its unit. Three global variables on the identical scale
state theirs:

```
 158 BoulderSpeedTilesPerSecond                  4915   = 1.2
 185 TriggerTrapTriggerSpeedTilesPerSecond       4096   = 1.0
 220 DefaultTorchLightRadiusTiles                6144   = 1.5
```

So the Imp's 7536 is 1.84 tiles per second and the Dwarf's 3686 is 0.9, which
matches the original, where an imp is visibly quicker than a dwarf. STRONGLY
INFERRED (Q29): the inference is by analogy with neighbouring variables, not
from a statement about the creature record.

A game second is **4 ticks of 250 ms** — ANSWERED and BUILT (Q1, ADR-0089), from
variable 19 in all 66 shipped levels, from the creature update's own effect timers,
and from the owner's portal stopwatch — and `SimulationTiming.TicksPerSecond` is 4.
Walking and working are both driven from `SimulationTiming`, so travel time and
dig rate stay in proportion.

Movement keeps the sub-tick remainder (`CreatureState.MoveRemainder`, hashed and
snapshotted). Integer division alone would leave an imp at 7536 units per second
covering 7520 in twenty ticks — 0.2% slow, forever
(`ImpTests.SpeedDoesNotDriftFromRoundingItAwayEveryTick`).

## The rule

Once per tick, for every creature flagged `IsWorker`, in spawn order:

1. If it is already carrying or fetching something, that runs to completion and
   nothing else is considered. Six activities take this exit — delivering gold,
   fetching floor gold, fetching and carrying a body, fetching and carrying an
   object — and *The round trip* is the first of them.
2. If it has a job and the target is no longer marked, or the work no longer
   applies to it, the job ends. It looks again on the next tick.
3. If it has gold in its hands and no job, it looks for somewhere to put the gold
   before it looks for work. The idle timer gates the search, so an imp with
   nowhere to unload does not sweep the dungeon every tick.
4. If it is idle, it counts down; at zero it searches. A search that finds nothing
   resets the count to `ImpIdleDelayBeforeReevaluationSeconds`.
5. If it is walking and is now in the working position for its job — **on** the
   target tile for floor work, orthogonally **beside** it for a wall (ADR-0028) —
   it starts working. If it has walked its whole route and is still not in
   position, the job ends.
6. If it is working, it counts down; at zero it either queues one application of
   work, or — if that application would free more gold than it can hold and there
   is somewhere reachable to put what it already has — breaks off and carries. An
   application lands **once a tick** (`SimulationTiming.WorkApplicationsPerSecond`
   = 4) at `EfficiencyRules.BasePercent` (80 %) of a quarter of the tabled delta,
   i.e. a fifth of the delta per swing — 60 health of `MineGoldHealth`'s −300
   (Q47, ADR-0020 as corrected by ADR-0089). **Trap: the fifth is the size of an
   application, not its rate.**

`CreatureMovementSystem` runs before `ImpTaskSystem` (`SimulationSystems`), so a
worker that arrives at its job is noticed on the tick it arrives.

### Where a worker stands

**A wall is worked from beside it; floor is worked from on top of it.**
Twelve imps to a block at three a face is four faces, and only orthogonal
neighbours give four (Q27, CONFIRMED). *"Imps claim tiles by jumping on them"*,
stay about half a second, then move to an adjacent unclaimed tile (Q44,
CONFIRMED). ADR-0028 closed the defect where both used the wall stance.

The rule is **derived rather than enumerated** (`WorkStance`). A worker works
from the target tile when it can stand there *both while it works and when the
job finishes*:

- **standable now** rules out every wall job without naming one — those tiles
  are `Solid`, and the question is asked of the same walkability grid the mover
  routes over, so a creature that cannot cross water does not plan to stand in
  it (`WorkStanceTests.AWorkerThatCannotCrossWaterDoesNotStandInIt`);
- **standable when it finishes** was the clause that ruled out fill-in, a floor
  job whose result is rock. **Fill-in is not a DK2 action** (Q219, answered
  2026-09-07: `FillInHealth` has no reader, the word is OpenKeeper's) and the
  `FillIn` work kind was removed; the clause stays in `WorkStance` and no
  current kind exercises it.

Repairing and attacking floor are therefore STRONGLY INFERRED rather than
guessed: they are not separate decisions. Two physical clauses answer all nine
`TileWorkKind`s.

**The ownership condition follows the stance.** Claiming and reinforcing spread
from ground the keeper already holds (Q42). A wall job is taken standing on the
keeper's own land, so the **standing** tile must be owned. A floor job is done
standing on the tile being claimed, so it is the **target** that must touch
owned land. Territory grows outwards, never in islands
(`ImpTests.ClaimedTerritoryCreepsOneTileAtATime`).

Not modelled: the ~500 ms linger (Q49).

### The round trip

**CONFIRMED (Q31).** An imp mines into its own hands up to `MaxGoldHeld` and
carries the load to the nearest tile of its keeper's that has room for gold —
given the data, a Treasury or Dungeon Heart tile (`GoldStore.DepositInto`;
`imp-task-priority.md` §5 confirms the route from the binary). It puts down as
much as fits and, if still carrying, looks again; a load spills from one
treasury tile into the next without a spillover rule
(`GoldCarryingTests.AFullTileSpillsIntoTheNextOne`).

Two decisions are ours rather than the game's, and both are stated in the code:

- **Where it stops mining.** "One more swing would not fit" — STRONGLY INFERRED
  (Q50). With the shipped numbers: five swings, a 1363 load, a seam in three
  trips; see `rooms-and-gold.md`, "Where an imp decides to stop mining".
- **Nowhere to unload.** It keeps mining, which is DK2's behaviour; the gold it
  cannot hold is reported as `gold_uncollected` and lost, which is not — the
  original leaves a lump on the floor. Standing still holding a full load would
  stop a keeper with no treasury from mining at all. The lump is representable
  (`FloorGold`) and still not made, because whether the original leaves one
  from *mined* surplus is not established (Q138; Q140 for an imp that cannot
  deliver). `MaxGoldPileOutsideTreasury` governs which spot an imp puts a load
  down in, not how big a lump grows (ADR-0055).

**Carrying is not in the priority order.** `CreatureActivity.DeliveringGold` is
a state and `JobPriority` has no entry for it (ADR-0018).

**Water is not isolation.** The owner (2026-09-06) marked a gem tile cut off
from the dungeon by water, with no bridge: imps waded, mined it, and carried the
gold to the treasury. `CanWalkOnWater` is on 44 of the 46 creatures
(`WorldPathGrid`). This supersedes an earlier record of the same setup that
said none of the gold reached a treasury. Q144's rule — the floor-gold task
refuses a tile that is neither water nor the worker's own — is about picking
loose piles *up* and never rested on this observation.

### The search is the selection

There is no candidate list, no sort and no reachability filter. The worker walks
outwards from where it stands over walkable tiles and stops at the first tile
from which a job can be worked — the tile underfoot first, then the four around
it. One search answers three questions: which job is nearest in steps, how to
get there, and whether anything is reachable. Unreachable marks are never
candidates (`ImpTests.AnImpIgnoresMarkedTilesNothingCanReach`).

This is `IPathfinder.TryFindNearest`, a different question from `TryFindPath`:
pathing to each candidate in turn would be slower and a different tie-breaking
rule.

**Two searches come before it, and both belong to the keeper's hand.** A worker
just dropped looks three tiles around where it landed, in an order of DK2's own
that is not the priority chain's; and a worker still on the kind it was aimed at
retries that kind whole-map before anything else, which is what carries the
Efficiency bonus from one marked tile to the next. `hand-of-evil.md` §6 and
ADR-0069 have the evidence; `ImpTaskSystem.TryTakeDroppedJob` and
`TryTakeJobOfKind` are the code.

### Which work a mark asks for

A mark does not say what to do with the tile; the tile does. Four kinds can be
called for — dig, mine, dig your own wall, dig an enemy's — and which applies
follows from the terrain's flags and the tile's owner, never from a terrain id.
The four are mutually exclusive on the shipped data, so the order they are
tried in carries no policy; `TileWorkTests.EveryTerrainAdmitsAtMostOneKindOfMarkedWork`
and `ImpsOnRealDataTests.NoTerrainInTheGame_AdmitsTwoKindsOfMarkedWork` assert
it.

### A mark survives a conversion the tile can still carry

A gem face's destroyed type is itself; if marks were cleared on every
conversion, mining one would clear the mark and idle the imp, where the original
mines a marked gem face indefinitely from one mark. The mark is cleared only
when the new terrain is not `Taggable`: rock becoming floor loses it, a gem face
keeps it (`TileWorkRules`, `ImpTests.AnImpKeepsMiningAGemFace_BecauseItNeverRunsOut`).
STRONGLY INFERRED.

---

## What is not implemented, and is not guessed at

| Behaviour | DK2 | Here | Question |
|---|---|---|---|
| **Wandering** with nothing to do | Runs around the dungeon, interacts with scenery and creatures (CONFIRMED) | Stands still; a walk we invented would put imps where DK2 would not | Q41 |
| ~500 ms linger after claiming | Observed | Goes straight to the next job; cooldown or animation is indistinguishable from outside | Q49 |
| Fetching a Workshop crate for a placed trap or door (kinds 9/10) | Tier A task | Nothing to act on: no traps, doors or manufacturing | `imp-task-priority.md` §8 |
| **Fleeing a fight** (`AlwaysFlee`) | Imps flee | No combat system; the carrying tasks begin *after* a fight and scenarios place creatures at zero health | — |
| Floor lump from mined surplus with nowhere to unload | Lump grows in front of the imp | `gold_uncollected`, lost | Q138, Q140 |
| **Building rooms** | An imp walks to a marked room tile and constructs it | `RoomBuildSystem` lays the terrain down instantly; where the job sits in the order is UNKNOWN | — |

The other task kinds **are** built: carrying the unconscious (kind 13, own → Lair,
enemy → Prison; ADR-0063, ADR-0064), carrying corpses (kind 12, ADR-0063),
carrying loose objects to their room (kind 8, ADR-0065), fetching floor gold
(kind 7, ADR-0056 as amended, ADR-0058 for its Tier B place; an imp refuses gold
that is not on its keeper's own ground or water, Q144), and claiming the tile just
dug (kind 14, Q154). Open around fetching: Q143 (whether the pick-up costs time),
Q145 (whether it interrupts a swing).

## Routing is ours, not DK2's

`GridPathfinder` is an eight-connected shortest-path search: A* with an octile
heuristic for a known destination, Dijkstra for the nearest goal. Step costs 10
orthogonal, 14 diagonal (Q28/Q48, ADR-0025); a fixed neighbour order; tile index
as the final tie-break.

| Choice | Status |
|---|---|
| Eight-connected, corners may be cut | **CONFIRMED** by observation (Q28): a creature passes diagonally between two solid tiles. Four-connected was the earlier, conservative choice and overestimated every route |
| Octile 10/14 | **STRONGLY INFERRED** (Q48): speed is tiles per second through world space, so a diagonal step must take √2 as long; the mover charges the same. At four ticks a second the ratio no longer survives rounding, so `ImpTests.WalkingOnTheBias_TakesRootTwoTimesAsLong` asserts within one tick — DK2 steps at the same rate and has the same quantisation (ADR-0089) |
| No terrain weighting | **CONFIRMED** (Q30, owner 2026-08-29): the shortest path wins even through water |
| Tie-breaking | Ours. Reproducible, which the determinism gate needs — arbitrary otherwise |
| The original's algorithm | **UNKNOWN** (Q5). Flame names a 36-byte `Follow_Path` struct with no named field |

Walkability is data-driven: not solid, and no hazard the creature lacks the flag
for. Bridges are rooms on hazard tiles and are read by `BridgeRules` (MAP byte 3
marks a bridge over lava — Q16, ADR-0031; `rooms-and-gold.md`, "Bridges: what is
underneath").

---

## How to re-check this

`dk2tools kwd creature Imp` prints the raw fixed-point values and the full flag
word — the empty job table, the sentinel needs, `IsWorker`. `dk2tools kwd vars`
lists the imp variables and the three that state their unit in their names.
`ImpsOnRealDataTests` re-runs the fixture's numbers against the install and runs
imps on a shipped campaign level (`ImpsDigOutMarkedRock_OnARealCampaignLevel`).

**Trap: a name in a community template is not a shipped field.** That template
names VARIABLES id 74 `GameTicks`, which would have been the tick rate; no editor
KWD and none of the 71 map KWDs in the reference install carries a record with
that id. Q1 was answered from the binary instead — variable 19 and the LEVEL
record (ADR-0089).
