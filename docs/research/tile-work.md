# Tile work: digging, mining, claiming, reinforcing

| | |
|---|---|
| Source(s) | `Terrain.kwd` and `GlobalVariables.kwd` (reference install); `DKII.EXE` v1.70 — the applier `FUN_00452f60`, the imp's claim/reinforce/attack handlers, `FUN_00488930`; owner measurements 2026-08-29 to 2026-09-06, off the gold counter and the status bar |
| Confidence | **CONFIRMED**: the data, the deficit rule, the 273-gold swing, the once-a-tick cadence, Efficiency and its two bonuses, and — run 15, tile by tile — all four per-application deltas (ADR-0133). **STRONGLY INFERRED**: the unguarded upward-conversion path (Q215), that the deltas are per game second, per-player tagging. **UNKNOWN**: whether a second worker adds a second application; what moved the base work rate mid-session |
| Implementation | `TileWorkRules`, `Systems/TileWorkSystem`, `Systems/TileTagSystem`, `EfficiencyRules`, `ObservedCorrections`; `TileState.Health`; `SimulationTiming.WorkApplicationShift` (sizes an application) and `.WorkApplicationsPerSecond` (schedules it) |
| Tests | `TileWorkTests`, `TileWorkOnRealDataTests` (re-runs the rules against the real database and asserts the terrain transition graph terminates for every tile of a campaign level), `DugOutTilesStayDugOutTests` |
| Known exceptions | none: `ObservedCorrections` holds no entries since the `DigRockHealth` override was retired (Q20, ADR-0280) |
| Open questions | Q74 and the mid-session base-rate shift below. **Q215, Q292 and Q62 are answered; the rates below are measured, and the swing arithmetic in this file predates run 15** — `EXPERIMENTS.md` has the traces |

Every number comes from the reference install; none is written into the code.
Who does the work and how they choose it: `imp-tasks.md`.

## Efficiency — the work rate is a creature stat, and DK2 shows it

Moved to `tile-work-efficiency.md`, with "The slap confirms the whole model, and
quantises the bonus" and "The work rate does NOT change with level". What the
swing arithmetic below needs:

| Efficiency | health a swing off a seam | gold a swing | observed |
|---|---|---|---|
| **80%** | 300 × 0.8 / 4 = 60 exact | 272.7 | **272–273** |
| **90%** | 67.5 → **68** | 309.1 | **309–310** |
| **100%** | 75 exact | 340.9 | **341** |

## The data

Each terrain type carries `startingHealth`, `maxHealth`,
`maxHealthTypeTerrainId` and `destroyedTypeTerrainId`. The seven types tile work
touches, then the ten deltas from `GlobalVariables.kwd`:

```
 id name                health  maxHp   gold  ->maxHp  ->dead  flags
  1 Impenetrable Rock        1      1      0        1       1  solid, impenetrable
  2 Rock                   165    660      0        9       3  solid, taggable, dwarfCanDigThrough
  3 Dirt Path                1    660      0        8       3  fillInable
  6 Gold                   660    660   3000        6       3  solid, taggable, dwarfCanDigThrough
  7 Gems                   100    100   3000        7       7  solid, impenetrable, taggable
  8 Claimed Path           660    660      0        2       3  ownable, attackable, fillInable
  9 Reinforced Wall        900    900      0        9       2  solid, ownable, taggable, attackable

  4 ClaimTileHealth          825      12 ReinforceWallHealth     50
  5 AttackTileHealth        -140      13 RepairWallHealth        22
  6 RepairTileHealth         125      11 FillInHealth             5
  7 MineGoldHealth          -300      20 GoldMinedFromGems       25
  8 DigRockHealth          -5000
  9 DigOwnWallHealth        -130
 10 DigEnemyWallHealth        -8
```

Note what the *names* do not say. Neighbours in the same table are explicit about
units — `PayDayFrequencySeconds`, `ImpExperienceGainPerSecond`,
`HealthDecreaseWhenOnFirePerSecond` — and none of the ten work deltas is. That
silence was the whole of Q19, answered from the other end: they are per game
second, and an application is a fifth of one.

### Q69: attacking hostile floor is live; the vault rates are cut

**CONFIRMED from the binary (2026-09-07), re-read independently the same day.**
`AttackTileHealth` is installed at `0x006F2558` after division by the four ticks
per game second, so the shipped −140 becomes −35, and it has two live readers,
the work handlers at `0x00497670` and `0x004977F0`. Both require a tile owned by
somebody else whose terrain is `Ownable|Attackable` with `Solid` and `Room`
clear, scale the cached rate by the same `0x3af + 0x3b3` sum as every other work
handler, and apply the negative result to tile health — **−28 a tick** at 80%
Efficiency. One path ends when the hostile-floor predicate becomes false; the
other also removes trap (thing type 3) and door (type 4) things left on the tile
when it falls. The first switches to a different amount when creature state-flag
bit 7 (`0x3e6 & 0x80`) is set, built from two creature-definition fields and a
per-creature field at `+0x5c6`; what that flag marks is UNKNOWN and the ordinary
path is unaffected.

So `AttackTileHealth` is the imp's hostile-floor phase. The predicate
(`0x00454000`, re-read 2026-09-25) admits a tile whose owner is anyone but the
worker's keeper, neutral included, on `Ownable|Attackable` terrain without `Solid|Room`.
Claimed Path falls to its destroyed type, Dirt Path, unowned, and is then claimed:
**CONFIRMED by the owner, 2026-09-25**, who watched imps do it to a rival (Q68).
A bridge is a room, which is why it changed owner with no neutral pause.

**The two mana-vault values are CUT, not disabled.** A second variable
dispatcher, missed by the original global map, caches them — 4 becomes 1 at
`0x006F294F`, 20 becomes 5 at `0x006F2953` — and a complete `.text` address scan
finds those two stores and no readers. They prove nothing vault-specific: a
neutral Mana Vault goes through the ordinary neutral-floor claim predicate,
hostile attackable ground through the ordinary hostile-floor one.


## The rule

One application of work:

1. adds the kind's delta to the tile's health;
2. at or below zero the tile becomes its `destroyedTypeTerrainId` — via
   `BridgeRules.RevertsTo`, so a bridge beaten down leaves the water or lava it
   spanned even though every room terrain in the data claims to leave Claimed
   Path (Q64, Q68);
3. at or above `maxHealth` it clamps at 32000 and, **for upward work only**,
   becomes its `maxHealthTypeTerrainId`;
4. the new tile starts at its own terrain's `startingHealth` and keeps its dig
   markers only if the new terrain is still `Taggable`.

Step 4's second half matters for gems, whose destroyed type is themselves:
clearing the mark on every conversion collapsed a marked gem face and left the
imp nothing to do, where the original mines a marked gem face indefinitely from
one mark. STRONGLY INFERRED, on the same flag the mark is validated against.

Two exceptions where the terrain stays and only the owner changes.
`TileWorkKind.ConvertRoom` runs health *down* but means the opposite of
destruction, so `CompletesAsTakeover` converts the tile to its own terrain —
resetting health and remainder while keeping a burning bridge burning
(`portals.md`, ADR-0044). And a **claim of ground already of the claimable
kind** keeps its terrain, because following `MaxHealthTypeTerrainId` would turn a
rival's Claimed Path into Rock, a transition nothing in DK2 triggers (Q18, Q219;
`TileWorkRules.UpwardDestination`, keyed on `Ownable`, so floor, bridge and room
read alike).

Conversion `0x0044FC40` assigns neutral (`[0x006F2548]`) to non-ownable results.
Level 1's area at (14,23) then advances flag 5; owner 0 does not. The shipped-trigger
regression is in `TriggerExecutorOnRealDataTests`. Initial map owner 0 is preserved;
claimability is separate from stored ownership (ADR-0045). Room sale differs.

### Why the upward conversion is a property of the work

"A tile at full health becomes its max-health type" cannot be right. Claimed Path
ships at 660 out of 660 with a max-health type of Rock, so a level-triggered rule
would turn every claimed floor tile in every level to rock on the first tick. Six
terrain types have this shape. Making the conversion belong to the work resolves
it, and each upward job lands where the data points:

| terrain | upward job available to it | `->maxHp` | result |
|---|---|---|---|
| Rock | Reinforce | 9 | Reinforced Wall |
| Dirt Path | Claim | 8 | Claimed Path |
| Mana Vault | Claim | 32 | Claimed Vault |
| Claimed Path | **none — fill-in does not exist, below** | 2 | Rock, **unreachable** |

That table is also the answer to **Q18**: no second meaning, no second field.
`maxHealthTypeTerrainId` is the destination of whichever upward job the terrain
admits, and which job that is follows from the terrain's own flags. `FillInable`
is set on exactly five types — all floor. **Claimed Path's row is data, not
behaviour**, and was once read as a live transition and quoted outside the project.

**An upward job costs the deficit, not the whole maximum** (Q62, ADR-0089
reversing ADR-0024): reinforcing Rock is 660 − 165 = 495, fifty applications at
10 each. See the applier below.

### Why gems are inexhaustible

Gems are impenetrable and their `destroyedTypeTerrainId` is *themselves*. A gem
face at 100 health takes two swings of 60, collapses, and immediately reappears
at full health; `GoldMinedFromGems` (25) is what each cycle yields against a
seam's 3000. The data reproduces the thing every DK2 player knows — gems never
run out and are far slower than a seam — with nothing special-cased.


## How much one swing does

**CONFIRMED (owner, 2026-08-30): an imp mining a gold seam frees 273 gold a
swing.** One number, and it propagates through the whole table:

```
a gold seam        660 health, 3000 gold  (terrain 6, shipped)
observed           273 gold a swing
  => health a swing = 273 * 660 / 3000 = 60.06, i.e. 60
  => swings a seam  = 660 / 60          = 11
  => MineGoldHealth = -300, and -300 / -60 = 5
```

Two corroborations that were not looked for. **Eight of the ten tabled deltas
divide by five exactly** — 825, −140, 125, −300, −5000, −130, 5, 50 — where
chance would give two, and no other small divisor comes close:

| divisor | deltas that divide exactly |
|---|---|
| 2 | −140, −300, −5000, −130, −8, 50, 22 (not 825, not 125, not 5) |
| 4 | −140, −300, −5000, −8 |
| **5** | **825, −140, 125, −300, −5000, −130, 5, 50** |
| 10 | −140, −300, −5000, −130, 50 |

**825 is odd**, which alone kills "twice a second" and takes four and ten with
it. And claiming lands on a count observed a day earlier from a different
mechanic: `ClaimTileHealth` 825 at a fifth is 165 an application against a Dirt
Path running 1→660, so **four applications** — and Q46, measured by watching an
imp jump once per application, gave *"2-3-4 jumps MAX"*.

**The fifth is the size of an application, not its cadence.** The handlers read
the rate global already divided by variable 19 and then multiply by Efficiency,
so an application is `tabled ÷ 4 × 0.8` = `tabled ÷ 5`, landing **once a tick,
four a game second**. `SimulationTiming.TicksPerSecond` was the placeholder 20
for weeks precisely because "five a second" and "four ticks a game second" could
not both be scheduled; they never had to be (ADR-0089, Q1 settled at 250 ms).

| job | swings |
|---|---|
| claim a dirt path | **4** |
| mine out a seam | **11** |
| reinforce rock (the 495 deficit) | **50** |
| attack claimed floor | **24** |
| dig your own wall | **35** |
| dig an enemy's wall | **563** |

Two deltas do not divide by five — `DigEnemyWallHealth` (−8) and
`RepairWallHealth` (22) — so `TileState.WorkRemainder` carries the fraction
between applications; without it an enemy wall would take 900 swings instead of
563 and run 37% slow forever. The carry is taken **before** Efficiency scaling,
so the tabled per-second rate stays exact and the 80% case is unchanged.

One consequence nobody designed: an Imp's `maxGoldHeld` of 1400 is **five
swings** of 273 (1363), so a seam is three trips — 1363, 1364, 273 — which is
what an imp shuttling to and from a rock face looks like in the original. That
answers Q50 by arithmetic rather than by a rule guessed for it.


## Q20 — DigRockHealth: one strike, as shipped

Rock has 165 health and `DigRockHealth` is −5000, so one application destroys it.
That is what the original does. Both full-state captures (2026-09-28) show it
tile by tile: in Level 1, (14,32) is reinforced wall at 900 losing 26 a tick,
turns to Rock at 165 for one tick at 101 and is Dirt Path at 102; the interior
Rock tiles of the Hatchery square go from 165 to Dirt Path in one sample, one
every ten ticks while a single imp works them.

The −500 override (ADR-0023, owner-approved 2026-08-30) came from a count of two
swings, which was an animation count. It is retired (ADR-0280, owner agreed
2026-09-29), and `ObservedCorrections` keeps the mechanism with no entries.

## Q21 — how a seam divides its gold

Proportional to the rock removed: 273 gold a swing *is* the proportional rule
read backwards, since 273 × 660 / 3000 = 60 is exactly the health a swing takes.
The alternative — the whole 3000 in a lump on the final application — would give
a swing 0 gold and one swing 3000.

Gold is the difference of two running totals: how much has come out by the time
the seam is down to this health, minus how much had at the last swing. That makes
a fully mined seam yield **exactly** 3000, needs no state, and reproduces the
measurement — 273 on eight of the eleven swings and 272 on the other three.
Truncating each swing on its own would lose a coin a swing and yield 2992.

## Fill-in does not exist

**CONFIRMED (owner, 2026-09-04; binary, 2026-09-07 — Q219):** *"once a tile is
dug out, and maybe claimed, there is no possible way to fill in that tile to rock
again. DK2 doesn't have a game mechanic for filling up dug out tiles."*
`FillInHealth` (`0x006F2568`) has **no reader**. `TileWorkKind.FillIn` was
removed on 2026-09-07, and Q22 and Q214 — how long filling one in takes — are
WITHDRAWN with the premise.

The data is not wrong: `FillInHealth`, the `FillInable` flag on five terrain
types and Claimed Path's route back to Rock are three coordinated pieces of
shipped data. The engine carries the concept and the shipped game exposes no way
to reach it; `ImpTaskSystem` never chooses fill-in, and
`DugOutTilesStayDugOutTests` asserts both halves — no tile goes from open back to
solid over a long dig, and no worker ever queues the job.

Two candidate explanations closed (2026-09-04). **OpenKeeper carries both fields
and uses neither**: `FILL_INABLE(0x00008000)` and `FILL_IN_HEALTH(11)` are each
declared once and referenced nowhere, and its worker task list has no fill-in
task. And the level script closes tiles by a **different** mechanism,
`ALTER_TERRAIN_TYPE` — a direct terrain write with no health and no rate. Across
all 66 shipped maps (`dk2tools kwd alterterrain`) 94 of its 160 uses go the other
way, Impenetrable Rock → Dirt Path, opening sealed passages, and **exactly two**
turn an open tile solid, both in `Secret3`, both Claimed Path → Reinforced Wall.
Q74 keeps the remaining half: level editor, or cut feature. **M6 note:**
`ALTER_TERRAIN_TYPE` must be able to write solid terrain onto an open tile, so
the invariant above is about workers, not about the engine.


## Tagging

A dig marker is state on the tile, one bit per player. Per-player rather than
global is STRONGLY INFERRED: DK2 is a multiplayer game in which each keeper marks
and sees their own dig orders, and the terrain table carries a per-type
`Taggable` flag rather than a shared list. The bitmask is our representation, not
a claim about the original's storage.

Marking is refused on terrain that is not `Taggable`, and on a tile still hidden
by fog (CONFIRMED, Q193 — `fog-of-war.md`). Unmarking is always allowed: terrain
can change under a marker, and a player has to be able to clear one that is no
longer diggable. `TagTilesCommand` takes an explicit tile list and an explicit
tag/untag intent; the original's drag-toggle rule — does a drag beginning on a
marked tile unmark everything it touches? — is UNKNOWN and belongs to the
presentation layer.

## Who does the work

An imp, for marked work only. `TileWorkSystem` drains a queue that
`ImpTaskSystem` fills: it finds the nearest marked tile its keeper can reach,
walks a worker to it and queues one application per work period
(`imp-tasks.md`). Tests and the scenario harness can queue work directly, and
that remains the only way to exercise the kinds a worker does not do on its own
— claim, reinforce, repair, attack. Every scenario run that injects work says so
under *"Not answered by this run"*, and so does every run that places a worker.

Gold released by mining goes into the hands of the creature that swung, and that
creature carries it to a treasury or dungeon-heart tile (Q31, ADR-0019). The
rules stay pure: `TileWorkResult.GoldReleased` credits nobody, and
`TileWorkSystem` reads the creature id off the order and fills the right pair of
hands; work with nothing behind it still has its gold reported and dropped. What
an imp cannot hold is lost rather than left on the floor, because a lump is an
object (M6). Where the imp stops mining is Q50 — *"one more swing would free more
gold than I can hold"*, HYPOTHESIS, and `TileWorkRules.GoldYield` is what lets it
ask without applying.

A tile can also *lose* gold. Room terrain is ownable floor, so a rival can attack
a treasury down to nothing; when it converts to claimed path, which holds no
gold, the amount is reported as `GoldSpilled` and emitted as `gold_spilled`. That
gold stays on the floor of the tile it came off, as an object
(`gold-on-the-floor.md` §4): it comes off the keeper's total and stays in the
world.


## The applier, `FUN_00452f60`

DK2's tile-work applier, the function every work handler ends up calling.

```
delta < 0:   if (-delta >= health) health = 0
             else                  health -= -delta
delta >= 0:  if (health + delta > 32000) health = 32000
             else                        health += delta

then:  health == 0             -> become terrain[+0x1c6]   (destroyedType)
       health >= terrain+0x1ad -> become terrain[+0x1c5]   (maxHealthType)
```

**The tile's health IS the accumulator.** There is no separate progress field:
`ADD`/`SUB` straight into the tile's health word at `tile+6`, and the conversion
compare is against the terrain record's `MaxHealth` at `+0x1ad`. So an upward job
costs the **deficit** — reinforce Rock 165 → 660 is 495, fifty applications at 10
each; claim Dirt Path 1 → 660 is 659, four at 165 each.

ADR-0024 had charged the whole `MaxHealth` — 660, so 66 applications — fitted to
the owner's reinforce stopwatch under Q47's five-a-second cadence. Both were
wrong and they cancelled: 66 at five a second is 13.2 s, 50 at four a second is
12.5 s, and a hand-timed 13.3–13.4 s cannot tell those apart. **What tells them
apart is the sign of the residual**, a hand timer over-reading and never
under-reading:

| measured | read model | ADR-0024's model |
|---|---|---|
| reinforce unslapped **13.3–13.4 s** | 12.5 s (+0.85) | 13.2 s (+0.15) |
| reinforce slapped **11.5–11.9 s** | 11.25 s (+0.4) | 12.0 s (**−0.3**) |
| claim **1.30–1.40 s** | 1.0 s + linger | 0.8 s + linger |

**A tile has visual damage stages, and they are not what a player counts.**
`terrain+0x1c3` is a count of visual stages, and the applier fires a redraw when
`(oldHealth − 1) ÷ stageSize` differs from `(newHealth − 1) ÷ stageSize`, with
`stageSize = MaxHealth ÷ stages`. Q46's *"2-3-4 jumps"* is not that: the owner
watched the **imp** jump, once per application. A tile's *appearance* should step
`terrain+0x1c3` times as it is worked, which need not equal the swing count —
something separate to check. A consequence of health-as-progress in the other
direction: **a half-reinforced wall is a tougher wall and looks it.**

### Q215 — there is no work-kind guard

**STRONGLY INFERRED from DKII.EXE v1.70.** After excluding zero health,
`0x004532D9` loads `MaxHealth`, `0x004532E0` compares the new health, and
`0x004532E9` loads `MaxHealthTypeTerrainId`; no instruction reads the delta or
caller. An unchanged destination only clamps health (`0x004535C7`).

The safety is in reachability. `RepairTileHealth` and fill-in have no path
(Q219), while wall and room repair target terrain whose destination is itself.
Our `CompletesUpward` names that caller invariant, not a binary guard.

## How this was established

- The `maxHealth` column had not been extracted before this milestone; adding it
  to `dk2tools kwd terrain` exposed the Claimed Path contradiction and forced the
  work-triggered rule.
- `dk2tools kwd terrainflags` prints the full flag set; `FillInable` falling on
  exactly the five floor types turned the fill-in reading from a guess into an
  argument.
- The tile rules are written against terrain **flags**, never terrain ids. A rule
  written against id 2 would be a rule about the reference install rather than
  about DK2.
