# Tile work: digging, mining, claiming, reinforcing

Milestone M3. What happens to a tile when somebody works on it.

Implemented by `DK2.Simulation.TileWorkRules`, `TileWorkSystem` and
`TileTagSystem`. Every number below comes from the reference install; none is
written into the code.

---

## Summary of confidence

| Claim | Status |
|---|---|
| The ten health deltas, the terrain table's health/transition fields, the taggable and fill-inable flags | **CONFIRMED** — DK2's own data |
| Health reaching zero converts a tile to its `destroyedTypeTerrainId` | **CONFIRMED** by the data's shape (see below) |
| Upward conversion is triggered by the *work*, not by the health level | **STRONGLY INFERRED** — the level-triggered reading is refuted by the data |
| `maxHealthTypeTerrainId` is the destination of whichever upward job that terrain admits | **STRONGLY INFERRED** — resolves Q18 |
| A gold seam releases gold in proportion to the rock removed | **HYPOTHESIS** (Q21) |
| A gem face yields `GoldMinedFromGems` per collapse | **STRONGLY INFERRED** — see the gems argument below |
| Tagging is per player | **STRONGLY INFERRED** |
| How often an application happens, i.e. what these rates are *per* | **UNKNOWN** (Q19, tied to Q1) |
| Whether one worker applies the whole delta | **UNKNOWN** (Q20) |

---

## The data

Each terrain type carries `startingHealth`, `maxHealth`,
`maxHealthTypeTerrainId` and `destroyedTypeTerrainId`. The seven types that
tile work touches:

```
 id name                health  maxHp   gold  ->maxHp  ->dead  flags
  1 Impenetrable Rock        1      1      0        1       1  solid, impenetrable
  2 Rock                   165    660      0        9       3  solid, taggable, dwarfCanDigThrough
  3 Dirt Path                1    660      0        8       3  fillInable
  6 Gold                   660    660   3000        6       3  solid, taggable, dwarfCanDigThrough
  7 Gems                   100    100   3000        7       7  solid, impenetrable, taggable
  8 Claimed Path           660    660      0        2       3  ownable, attackable, fillInable
  9 Reinforced Wall        900    900      0        9       2  solid, ownable, taggable, attackable
```

And the ten deltas, from `GlobalVariables.kwd`:

```
  4 ClaimTileHealth          825      12 ReinforceWallHealth     50
  5 AttackTileHealth        -140      13 RepairWallHealth        22
  6 RepairTileHealth         125      11 FillInHealth             5
  7 MineGoldHealth          -300      20 GoldMinedFromGems       25
  8 DigRockHealth          -5000
  9 DigOwnWallHealth        -130
 10 DigEnemyWallHealth        -8
```

Note what the *names* do and do not say. Their neighbours in the same table are
explicit about units — `PayDayFrequencySeconds`, `ImpExperienceGainPerSecond`,
`HealthDecreaseWhenOnFirePerSecond`. None of the ten work deltas is. That
silence is the whole of Q19.

## The rule

One application of work:

1. adds the kind's delta to the tile's health;
2. at or below zero the tile becomes its `destroyedTypeTerrainId`;
3. at or above `maxHealth` it clamps, and — **for upward work only** — becomes
   its `maxHealthTypeTerrainId`;
4. the new tile starts at its own terrain's `startingHealth`, and keeps its dig
   markers only if the new terrain is still `Taggable`.

Step 4's second half was refined once the first worker existed (2026-08-29). The
markers used to be cleared on every conversion, which is right for rock becoming
floor and wrong for a gem face, whose destroyed type is itself: mining a marked
gem face collapsed it, cleared the mark, and left the imp with nothing to do,
where the original mines a marked gem face indefinitely from a single mark.
STRONGLY INFERRED, on the same flag the mark is validated against. See
`imp-tasks.md`.

Ownership is written only when upward work converts a tile into an ownable
type. It is never cleared: DK2's map data gives every tile an owner id including
solid rock, and which id means "nobody" is UNKNOWN (Q17), so leaving the byte
alone lets the level file's own neutral id propagate through a dug-out tile
instead of us inventing one.

### Why the upward conversion is a property of the work

The obvious reading — "a tile at full health becomes its max-health type" —
cannot be right. Claimed Path ships at 660 out of 660 with a max-health type of
Rock. Under a level-triggered rule, every claimed floor tile in every level
would turn to rock on the first tick. Six terrain types have this shape.

Making the conversion belong to the work instead resolves it, and each of the
three upward jobs lands exactly where the data points:

| Terrain | Upward job available to it | `->maxHp` | Result |
|---|---|---|---|
| Rock | Reinforce | 9 | Reinforced Wall |
| Dirt Path | Claim | 8 | Claimed Path |
| Claimed Path | Fill in | 2 | Rock |
| Mana Vault | Claim | 32 | Claimed Vault |

That table is also the answer to **Q18**. There is no second meaning and no
second field: `maxHealthTypeTerrainId` is the destination of whichever upward
job that terrain admits, and which job that is follows from the terrain's own
flags. The `FillInable` flag is set on exactly five types — Dirt Path, Claimed
Path, Mana Vault, Claimed Vault and Hero Lair — all of them floor, which is what
"fill in" would have to mean.

### Why gems are inexhaustible

Gems are impenetrable and their `destroyedTypeTerrainId` is *themselves*. A gem
face at 100 health takes one `MineGoldHealth` application (−300), collapses, and
immediately reappears at full health. The separate `GoldMinedFromGems` variable
(25) is what each of those cycles yields, against a seam's 3000. The data
therefore reproduces the thing every DK2 player knows — gems never run out and
are far slower than a seam — without anything being special-cased for them.

---

## What does not fit, and is implemented anyway

### Q20 — DigRockHealth is 30× the health of the rock it digs

Rock has 165 health. `DigRockHealth` is −5000. One application destroys it with
4835 to spare, so under any reading where an application is a discrete event,
digging earth is instantaneous. It is not, in the original: a single imp takes
a few seconds.

Every other rate is sane under a per-second reading — reinforce 10 applications,
mine a seam 3, dig your own wall 7, dig an enemy's 113 — which makes
`DigRockHealth` the odd one out rather than evidence that the whole reading is
wrong.

Candidate explanations, none verified:

- an application is much shorter than a second, and the other rates are simply
  slow;
- a worker contributes a fraction of the delta, scaled by a per-creature work
  rate (`DwarfDiggingMultiplier` = 184 shows that a multiplier concept exists,
  and `f3AF_workModifier` / `f3B3_workEnhancement` are named in Flame — Q6);
- `DigRockHealth` applies to something other than terrain 2, and ordinary earth
  is dug by a variable we have not identified.

The implementation follows the data exactly and the tests assert the
one-application result, because a number invented to make digging *feel* right
would be worse than a faithful one that is visibly wrong. This is written down
so nobody later mistakes it for a bug in the rules.

### Q21 — how a seam divides its gold

A seam carries 3000 gold over 660 health and gives up 300 health per
application, so the natural reading is proportional: 3000 × removed ÷ 660.
Integer truncation then makes a fully mined seam yield 2998 rather than 3000.
The alternative — the whole 3000 in a lump on the final application — is also
possible but fits the original's visible behaviour worse, since an imp mining a
seam carries gold away repeatedly rather than once. Implemented as proportional,
truncation kept rather than rounded away, since rounding it away would be an
invention of its own.

### Q22 — fill-in's accounting

`FillInHealth` is 5, which is small enough to suggest many applications, but the
tiles it applies to are already at maximum health. Under the uniform rule one
application converts them. Either fill-in restarts the health accounting or the
tile is damaged first. UNKNOWN; the uniform rule is what ships, tagged
HYPOTHESIS.

---

## Tagging

A dig marker is state on the tile, one bit per player. Per-player rather than
global is STRONGLY INFERRED rather than confirmed: DK2 is a multiplayer game in
which each keeper marks and sees their own dig orders, and the terrain table
carries a per-type `Taggable` flag rather than a shared list. The bitmask is our
representation, not a claim about the original's storage.

Marking is refused on terrain that is not `Taggable`. Unmarking is always
allowed — terrain can change under a marker, and a player has to be able to
clear one that is no longer diggable.

`TagTilesCommand` takes an explicit list of tiles and an explicit tag/untag
intent. The original's interface is a drag-toggle whose exact rule (does a drag
beginning on a marked tile unmark everything it touches?) is UNKNOWN and belongs
to the presentation layer, not to the simulation.

## Who does the work

An imp, since 2026-08-29 — for marked work only. `TileWorkSystem` still drains a
queue that something else fills; the something is now
`DK2.Simulation.Systems.ImpTaskSystem`, which finds the nearest marked tile its
keeper can reach, walks a worker to it and queues one application per work
period. Everything about how it chooses, routes and times that is in
`imp-tasks.md`, and much of it is inferred rather than known.

Tests and the scenario harness can still queue work directly, and that remains
the only way to exercise the kinds a worker does not do on its own — claim,
reinforce, repair, attack, fill in. Every scenario run that injects work says
exactly that under "Not answered by this run", and so does every run that places
a worker.

Gold released by mining is reported as an event and dropped. Crediting a player
directly would be inventing an economy rather than reproducing one: in the
original the imp picks the gold up and carries it to a treasury, and neither the
carrier nor the treasury exists yet.

---

## How this was established

Everything here came from the data, in the order the research method prescribes
— nothing needed a disassembler.

- The `maxHealth` column had not been extracted before this milestone; adding it
  to `dk2tools kwd terrain` is what exposed the Claimed Path contradiction and
  therefore forced the work-triggered rule.
- `dk2tools kwd terrainflags` (added here) prints the full flag set. The
  `FillInable` flag falling on exactly the five floor types is what turned the
  fill-in reading from a guess into an argument.
- The tile rules are written against terrain *flags*, never terrain ids. A rule
  written against id 2 would be a rule about the reference install rather than
  about DK2; `DK2.Behavior.Tests.TileWorkOnRealDataTests` re-runs the rules
  against the real database to keep that honest, and asserts that the terrain
  transition graph terminates for every tile of a real campaign level.
