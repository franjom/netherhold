# The first worker: how an imp finds work and gets to it

Milestone M3. Who does the tile work that `tile-work.md` describes, and how they
come to be doing it.

Implemented by `DK2.Simulation.Systems.ImpTaskSystem`,
`DK2.Simulation.Systems.CreatureMovementSystem`,
`DK2.Simulation.WorldPathGrid` and `DK2.Pathfinding.GridPathfinder`.

---

## Summary of confidence

| Claim | Status |
|---|---|
| Imp and Dwarf are the game's workers, identified by a flag rather than by id | **CONFIRMED** — `IsWorker` falls on exactly those two |
| A marked tile is dug, mined or dug-through according to its terrain flags and owner | **CONFIRMED** — the four kinds are mutually exclusive on the shipped data |
| An idle worker re-evaluates every `ImpIdleDelayBeforeReevaluationSeconds` (5) | **CONFIRMED** — the variable names exactly this |
| Creature `speed` is in tiles per second | **STRONGLY INFERRED** — see below |
| Walkability follows `CanWalkOnWater` / `CanWalkOnLava` | **STRONGLY INFERRED** — the flags exist for nothing else |
| **Job priority: marked excavation, then claiming, then reinforcing** | **CONFIRMED** — owner observation of the original (Q24); implemented as a strict ordering, ADR-0018 |
| A tile is worked from an orthogonal neighbour | **CONFIRMED** — twelve imps a block at three a face is four faces (Q27) |
| An imp with none of the three available wanders and interacts with scenery and creatures | **CONFIRMED but NOT IMPLEMENTED** (Q41) — ours stands still |
| Three imps a face, twelve a block, is a hard cap | **CONFIRMED but NOT IMPLEMENTED** (Q26/Q43) — ours converge without limit |
| Claiming and reinforcing are taken from ground the keeper already holds | **STRONGLY INFERRED** (Q42) — supported by Q33, where a room tile must touch owned land |
| A worker takes the marked tile fewest steps away | **HYPOTHESIS** (Q25) |
| A worker that finishes a job starts the next one at once rather than waiting out the idle delay | **STRONGLY INFERRED** — an imp that has just finished has not been idle |
| Imps never eat | **CONFIRMED** — owner observation; the Imp's hunger rate of 32767 does mean "never" (Q12) |
| An imp carries mined gold to a treasury or heart tile itself | **CONFIRMED** — owner observation (Q31); the tail of a mining job, not a job |
| Where an imp breaks off mining to go and unload | **HYPOTHESIS** — when one more swing would free more than it can hold |
| An imp with nowhere to unload keeps mining | **CONFIRMED** (Q31) — but the floor lump it should leave is NOT IMPLEMENTED, so the overflow is lost |
| Everything about *routing* | **UNKNOWN** (Q5) |

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

Three things in that record matter more than they look.

**The Imp has no jobs.** Its happy-job table is a single entry, `Explore` at
100%, and its unhappy and angry tables are empty. Every other creature carries a
weighted list of rooms to work in. So the things an imp actually does — dig,
claim, reinforce, carry gold, drag bodies — are **not** in the job tables at all:
they are engine behaviour attached to the `IsWorker` flag. STRONGLY INFERRED, and
it is why `ImpTaskSystem` exists as a separate system rather than as a case in a
future generic job system.

**The Imp has no needs.** `hungerRate` and `timeAwake` are both 32767, the
sentinel Q12 identifies as "never", and `pay` is 0. An imp does not eat, sleep or
get paid, which is why a worker can be built before hunger, sleep and payday
exist.

**`AlwaysFlee` is set.** Imps run from combat rather than fighting. Not
implemented; recorded here because it is the next thing that will interrupt a
job.

The global variables that name imps directly:

```
  30 ImpIdleDelayBeforeReevaluationSeconds          5
  94 MinimumImpThreshold                            4
  95 TimeBeforeFreeImpGeneratedSeconds              5
 110 InsufficientManaImpEvaluationPeriodSeconds    10
 144 ImpPopCountdownTimeSeconds                    10
 179 ImpExperienceGainPerSecond                    20
 188 SpecialReceiveImpsAmount                      10
 184 DwarfDiggingMultiplier                         4
```

Only the first is read today. Numbers 94, 95 and 144 are imp *generation*, which
needs a dungeon heart and mana; 110 likewise. They are listed because
`ImpsOnRealDataTests` asserts they are present, so that the half of imp behaviour
that is not implemented cannot be built on a variable that turns out to be
missing.

## Speed is tiles per second

`CreatureDef.Speed` is a 4096-denominator fixed-point number and nothing in the
creature record states its unit. The global variables table settles it by using
the identical scale for values that *do* state theirs:

```
 158 BoulderSpeedTilesPerSecond                  4915   = 1.2
 185 TriggerTrapTriggerSpeedTilesPerSecond       4096   = 1.0
 220 DefaultTorchLightRadiusTiles                6144   = 1.5
```

So the Imp's 7536 is 1.84 tiles per second, and the Dwarf's 3686 is 0.9 — which
matches the original, where an imp is visibly quicker than a dwarf. STRONGLY
INFERRED rather than confirmed, because the inference is by analogy with
neighbouring variables rather than from a statement about the creature record.

How long a second is remains UNKNOWN (Q1). What the implementation does fix is
that walking and working are driven from the same place: `SimulationTiming`. An
imp's travel time and its dig rate stay in proportion however Q1 is answered.

Movement keeps the sub-tick remainder rather than truncating it. Integer division
alone would leave an imp at 7536 units per second covering 7520 in twenty ticks —
0.2% slow, forever, on every creature. The remainder is state, hashed and
snapshotted like everything else.

## The rule

Once per tick, for every creature flagged `IsWorker`, in spawn order:

1. If it is carrying gold, it deals with that first — see *The round trip* below.
2. If it has a job and the target is no longer marked, or the work no longer
   applies to it, the job ends. It looks again on the next tick.
3. If it has gold in its hands and no job, it looks for somewhere to put the gold
   before it looks for work.
4. If it is idle, it counts down; at zero it searches. A search that finds
   nothing resets the count to `ImpIdleDelayBeforeReevaluationSeconds`.
5. If it is walking and now occupies a tile orthogonally beside its target, it
   starts working. If it has walked its whole route and is still not beside the
   target, the job ends.
6. If it is working, it counts down; at zero it either queues one application of
   work, or — if that application would free more gold than it can hold, and
   there is somewhere reachable to put what it already has — breaks off and
   carries.

Movement is a separate system that runs *before* task selection, so a worker that
arrives beside its job is noticed on the tick it arrives.

### The round trip

**CONFIRMED (Q31).** An imp mines into its own hands up to `maxGoldHeld` and
carries the load to the nearest tile of its keeper's that has room for gold —
which, given the data, means a treasury or dungeon-heart tile and nothing else.
It puts down as much as fits and, if it is still carrying anything, looks again;
that is what makes a load spill from one treasury tile into the next without a
spillover rule existing anywhere.

Two decisions in that are ours rather than the game's, and both are stated in the
code:

- **Where it stops mining.** "One more swing would not fit" — HYPOTHESIS. See
  `rooms-and-gold.md` §*Where an imp decides to stop mining*, where the shipped
  numbers turn out to say something about Q47 as well.
- **What it does with nowhere to unload.** It keeps mining, which is DK2's
  behaviour, and the gold it cannot hold is lost, which is not: the original
  leaves a lump on the floor. The alternative — stand still holding a full
  load — would stop a keeper with no treasury from mining at all, and would be a
  worse wrong answer than losing the overflow loudly.

**Carrying is not in the priority order.** An imp does not weigh "carry" against
"claim"; it mines, and carrying is what happens next. That is the whole reason
`CreatureActivity.DeliveringGold` is a state and `JobPriority` has no entry for
it (ADR-0018).

### The search is the selection

There is no candidate list, no sort and no reachability filter. The worker walks
outwards from where it stands over walkable tiles, and stops at the first tile
that has a workable marked tile beside it. That one search answers three
questions at once: which job is nearest in steps, how to get there, and whether
anything is reachable at all. Unreachable marks are never candidates because the
search never arrives at them.

This is `IPathfinder.TryFindNearest`, and it is a different question from
`TryFindPath`. "Where is the nearest thing I can get to" is the question a worker
looking for work actually asks; answering it by pathing to each candidate in turn
would be both slower and a different tie-breaking rule.

### Which work a mark asks for

A mark does not say what to do with the tile; the tile does. Four kinds can be
called for — dig, mine, dig your own wall, dig an enemy's — and which applies
follows from the terrain's flags and the tile's owner, never from a terrain id.
The four are mutually exclusive on the shipped data, so the order they are tried
in carries no policy, and both `TileWorkTests` and `ImpsOnRealDataTests` assert
that exclusivity so a data change could not turn the order into a hidden
priority rule.

### A mark survives a conversion the tile can still carry

Refined from the M3 tile rules, and the imp is what exposed it. Marks used to be
cleared on every terrain conversion. A gem face's destroyed type is itself, so
mining one collapsed it, cleared the mark, and left the imp with nothing to do —
where the original mines a marked gem face indefinitely from one mark. The rule
is now that the mark is cleared only when the new terrain cannot carry one:
rock becoming floor loses it, a gem face keeps it. STRONGLY INFERRED, on the
same `Taggable` flag the mark itself is validated against.

---

## What is not implemented, and is not guessed at

**Claiming and reinforcing are now implemented** — Q24 was answered by the owner
and the priority order is CONFIRMED (ADR-0018). What is still absent:

- **Wandering.** An imp with no excavation, nothing to claim and nothing to
  reinforce stands still. In the original it runs around the dungeon and
  interacts with the environment and with creatures (CONFIRMED). It is not
  implemented because it has no simulation consequence we model, and a walk we
  invented would put imps where DK2 would not. Q41.
- **The crowding cap.** Three imps a face and twelve a block is a hard limit in
  the original (CONFIRMED). Ours converge without limit. Implementing it means
  choosing between reservation — a fourth imp picks a different job — and
  crowding — a fourth imp walks over and waits — which is Q43 and is not
  observed yet.
- **Dragging the unconscious to a prison and the dead to a graveyard**, and
  **fleeing a fight.** Neither has a trigger anybody has stated, and neither has
  anything to act on: there is no combat, no unconsciousness and no prison.
- **Picking up gold somebody else dropped.** Carrying gold an imp mined itself is
  implemented as the tail of a mining job; a pile already on the floor needs a
  trigger of its own and is not in the priority order the owner gave.
- **The floor lump an imp leaves when there is nowhere to unload.** The
  *decision* is DK2's and is implemented — an imp with nowhere to put gold keeps
  mining rather than standing still — but what it cannot hold is reported as
  `gold_uncollected` and lost, where the original grows a lump up to
  `MaxGoldPileOutsideTreasury` (2000) that somebody can come back for. A lump is
  an OBJECT and the OBJECTS record is not parsed until M6. Q31.
- **Building rooms.** In DK2 an imp walks to a marked room tile and constructs
  it; here `RoomBuildSystem` lays the terrain down instantly and no worker is
  involved. That is a fourth thing an imp does and it is not in the priority
  order the owner gave, so where it would sit is UNKNOWN.

Note what is *not* on this list any more: the priority order itself, and carrying
gold. Both were among the biggest unknowns in this file and both were answered in
one sentence each.

## Routing is ours, not DK2's

`GridPathfinder` is a four-connected shortest-path search: A* with a Manhattan
heuristic for a known destination, breadth-first for the nearest goal. Uniform
step costs, a fixed neighbour order, and tile index as the final tie-break.

**None of that is evidence about the original.** Q5 is the project's largest open
question; Flame names a 36-byte `Follow_Path` struct with not one named field,
and nobody has reconstructed it. Three of the choices above are visible in
gameplay:

- **Four-connected.** Chosen because it is conservative: an eight-connected
  search can return a route that slips diagonally between two solid tiles, and
  whether DK2 allows that is UNKNOWN. Every four-connected route is walkable by
  an eight-connected mover, so a replacement can only widen the set of paths.
- **Uniform cost.** Water is visibly slower to cross in the original. UNKNOWN
  whether that is a path cost or only a speed modifier.
- **Tie-breaking.** Most routes have equally short alternatives, and which one
  comes back is decided by our neighbour order. Reproducible, which the
  determinism gate needs — but arbitrary.

Walkability is data-driven: not solid, and no hazard the creature lacks the flag
for. Bridges are UNKNOWN and deliberately not guessed at — a bridge is a room
built on a hazard tile, rooms do not exist, and MAP byte 3 is bridge-related but
unread (Q16).

---

## How this was established

- `dk2tools kwd creature Imp` (extended here to print raw fixed-point values and
  the full flag word) is where the empty job table, the sentinel needs and the
  `IsWorker` flag came from. The decimal forms it printed before were lossy —
  the fixture's first transcription of `hungerRate` was 32767 where the raw value
  is 134213632, which is exactly the drift the real-data guard exists to catch,
  and it caught it on the first run.
- The tiles-per-second reading came from `dk2tools kwd vars`: three variables on
  the same fixed-point scale that state their unit in their own names.
- `ImpsOnRealDataTests` re-runs the fixture's numbers against the install and
  runs imps on a shipped campaign level, so neither the transcription nor the
  synthetic map is load-bearing on its own.
- Q1 was probed and not answered. The VARIABLES id space contains an id 74 named
  `GameTicks` in the community template, which would be the tick rate if it were
  shipped. It is not: no editor KWD and none of the 71 map KWDs in the reference
  install carries a record with that id. Q1 cannot be answered from KWD data.
