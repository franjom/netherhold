# How an imp chooses a job: two tiers, read from the binary

**Source:** `DKII.EXE` v1.70 — the `ImpState.cpp` and `ImpUtils.cpp`
translation units, the tile-marking code, the per-player alliance table and
the job-icon name table, read 2026-09-05. Facts in our own words; ADR-0046
governs the boundary. **Owner observations:** 2026-08-29 (Q24), 2026-09-05
(Q141, later doubted), and 2026-09-05 again (Q147 — *"imps prioritise walls
over gold on the floor, in both cases"*).

**Implementation:** `ImpTaskSystem` (`TryTakeJob` is Tier A, `TrySweep` Tier B),
ADR-0058 and ADR-0069. **Tests:** `ImpFetchesGoldTests`, `ImpTests`,
`ImpClaimsWhatItDugTests`. **Questions:** Q24, Q25, Q141, Q146, Q147, Q153,
Q154, Q155, Q162.

---

## 1. The answer in one paragraph

An idle imp searches for work in **two tiers**. **Tier A is kind-first and
whole-map:** a fixed table of task kinds, each searched across the entire map
before the next is looked at — *claim the tile you just dug, carry loose
objects, build placed traps and doors, dig and mine marked tiles, claim floor,
carry the unconscious, carry corpses.* **Tier B is position-first and runs only
when Tier A is empty:** a ring scan outwards from the imp that, at every tile,
tries *reinforce or repair a wall, pick up gold, carry a loose object,* and one
unidentified kind. So a marked wall beats gold anywhere on the map; a wall to
reinforce beats gold **on the same tile**; and a nearer pile beats a farther
wall. That satisfies the owner's Q24 order and their Q147 observation, and it
answers the Q146 argument that gold placed last would never be collected: gold
is not *last*, it is *near*.

---

## 2. The machinery

### 2.1 A keeper-wide task list

The creature debug dump prints an **`ImpJobData`** block — `ScanningJob`,
`LastEvaluate`, `DroppedTask`, `JustDropped`, **`LastTaskType`**, `ImpTaskSlot`,
`ImpTaskSurface`, `ImpTaskPositionX/Y`, `ImpTaskID` — and the player dump
prints a **task list**: `TaskStack`, `TaskListHead`, `NumberInTaskList`,
`NumberOfFreeTasks`. A keeper accumulates tasks, each with a **kind** and a
tile, and an imp claims one.

Three functions carry the list and are used identically everywhere:

| | address | what it answers |
|---|---|---|
| **find at** | `00508200` | *is there a task of kind K at this tile?* |
| **raise** | `00507bb0` | *make a task of kind K at this tile* |
| **find any** | `00507e40` / next `00507e90` | *walk every task of kind K* |

Most kinds are raised by the very handler that consumes them — the handler
scans, finds work, raises the task, takes it. **Kinds 1, 2 and 14 are raised
from outside the imp code**, which is what makes them identifiable (§3).

### 2.2 Two ways to search for one kind

`FUN_00468520(kind, radius)` is *"find me a job of this kind within this
distance"*, and it has two shapes:

- **kinds 1 and 2** walk the task list (`FUN_00469af0`) and test each task's
  squared straight-line distance against the radius;
- **kinds 3, 6/16, 7, 8** run a **ring scan** (`FUN_00468d10`) outwards from
  the imp, calling the kind's handler on each tile in turn; kinds 9/10, 11,
  12, 13 have scanners of their own over the things or rooms they concern.

The ring scan walks concentric squares — the imp's own tile first, then the
ring at distance 1, then 2 — and skips a tile unless a flag bit (`tile+0xb &
0x20`, unread) is set or the tile carries a dig task. Within a ring the order
is fixed by the scan, not by distance.

### 2.3 The dispatch, and the two per-tile chains

`FUN_004687c0` is a `switch` on the kind to its handler and enumerates every
kind the imp knows: **1, 2, 3, 6, 7, 8, 9, 10, 11, 12, 13, 14, 16.**

Two *chains* call handlers in a fixed order and stop at the first that takes:

- **`FUN_00469240`, nine steps:** dig/mine (1,2) → claim (3) → traps/doors
  (9,10) → carry creature (13) → reinforce/fix (6,16) → **gold (7)** → objects
  (8) → corpse (12) → kind 11. This is what the first read found and called
  "the order". It is a per-tile callback and it is used exactly once — see
  §4(b).
- **`FUN_00469580`, four steps:** reinforce/fix (6,16) → **gold (7)** →
  objects (8) → kind 11. This is Tier B's per-tile order.

---

## 3. The kinds, identified

| kind | job | state | how it was identified |
|---|---|---|---|
| **1** | dig a marked wall | `0xd` | raised by the **tile-marking code** when the terrain's `GoldValue` is 0; handler requires Solid, not Impenetrable |
| **2** | mine a marked seam | `0x19` | same raiser when `GoldValue` ≠ 0; handler requires Solid with `GoldValue` ≠ 0, owner nobody |
| **3** | **claim floor** | `0xf` / `0x13` / `0x11` | tile **not** the imp's, **not** Solid/Water/Lava, owner nobody **or not allied** (alliance table `FUN_004bf3d0`), no room structure on it, and **orthogonally adjacent to the imp's own floor** (`FUN_00453b30`). Three states: plain unowned path; enemy floor (flag Attackable); enemy room tile |
| **6** | **reinforce a wall** | `0x15` | worked from an adjacent tile that is **not Solid and owned by the imp** — the wall-stance. Target: Solid, not Impenetrable, `GoldValue` 0, Ownable; either the imp's own with tile health below **half** a per-terrain ceiling, or nobody's, below half, and adjacent to own floor |
| **16** | **fix (repair) a wall** | `0x17` | same stance; target owned by the imp, Solid, not Impenetrable, `GoldValue` 0, tile health **≠** the ceiling, adjacent to own floor |
| **7** | **gold off the floor** | `0x1b` | object with definition flag `TypeGold` (0x40), owner = the level-wide floor-gold id; tile must be the imp's own **or water** (Q144) |
| 8 | carry a loose object to its room | `0x1e` | objects owned by nobody on own floor or water; the room is looked up from the object's definition (Library, Workshop…) |
| 9 / 10 | build a placed **trap** / **door** — fetch its crate from the **Workshop** | `0x22` / `0x24` | thing types 3 and 4 with an "unbuilt" flag; the player's rooms of type 10 are searched for the matching crate |
| 11 | *dig-like, unidentified* | `0x26` | requires a pre-existing kind-11 task and the tile's **tagged** bit (`tile+0xb & 1`); records itself as kind 1 with a flag. **No immediate-operand raise of kind 11 exists** among the 27 raise sites — it is raised some other way, unread |
| 12 | carry a **corpse** to the **Graveyard** | `0x28` | thing type 6, owner = the floor-gold id, on own floor; room type **14**; interface messages when there is no graveyard or it is full |
| 13 | carry an **unconscious creature** — own → **Lair**, enemy → **Prison** | `0x2a` | rooms **2** and **11**; a companion routine walks the keeper's own creature list for those flagged as needing carrying |
| **14** | **claim the tile you have just dug** | `0xf` | tile is plain unowned path adjacent to own floor (`FUN_00453fc0` + `FUN_00453b30`); a kind-14 task exists there; **none of the 8 neighbours carries a dig task**; then it raises kind 3 at the tile and takes it. Raised from `ImpState.cpp` (`00465774`, `00465b57`) for a tile passing the same two predicates |

The **state** column is the second argument the handler passes to "send the
imp to do this" (`FUN_004651a0`). Two families, odd `0xd`–`0x1b` and even
`0x1e`–`0x2a`.

**The job-icon table corroborates the identifications by name.** A string
table at `006be500` lists the interface's job sprites in one block:
`SJ-Dig, SJ-Claim, SJ-Reinforce, SJ-Fix, SJ-Take_Gold, SJ-Take_Crate,
SJ-Library, SJ-Graveyard, SJ-Prison, SJ-Temple, Imprison`. Every job above has
an icon; **"Fix" is the name of kind 16's icon**, which is why 6 and 16 are
called reinforce and fix here rather than "reinforce, first half / second
half". Which of the two is which is **STRONGLY INFERRED** from the order the
switch and the table share; the *pair* is CONFIRMED from the code.

### The terrain flag word, as the binary reads it

Every tile predicate reads the terrain record's flags at `terrain+0x17b`, and
the bits agree with our own `TerrainFlags` exactly: `0x1` Solid, `0x2`
Impenetrable, `0x4` Ownable, `0x8` Taggable, `0x10` Room, `0x20` Attackable,
`0x80` Water, `0x100` Lava. `terrain+0x185` is `GoldValue`. Both sit at a
constant −0x15 from their file offsets (`0x190`, `0x19a`).

**The ceiling the reinforce predicates compare tile health against is
`terrain+0x1ad`, and it is `MaxHealth`** (Q153, read 2026-09-05). The first
reading assumed the −0x15 shift extended to it and pointed at file `0x1c2`;
`Terrain.kwd` at `0x1c2` is zero for all 41 terrains. The in-memory record is
**not the file record shifted** — a loader reads the 552-byte record at the
offsets our parser uses and copies each field into its own runtime slot, and
the slot at `+0x1ad` is filled from file `0x1ff`. The shift held for the flags
and `GoldValue` by coincidence. So: **kind 6 (reinforce) takes solid rock whose
health is below half its `MaxHealth`** — fresh Rock is 165 of 660 — and **kind
16 (fix) takes anything whose health is not `MaxHealth`**, so a wall part-way
up is a *fix*, at the same step of the ring scan. The runtime ↔ file offset
table is in `kwd-terrain-rooms.md`.

Ours splits the pair differently and only half of it is a job. `TileWorkKind`
`.Reinforce` is solid ∧ not Impenetrable ∧ **not Ownable** ∧ the terrain is not
already its own reinforced type — plain rock, chosen by type rather than by
health — and it is the only kind `JobPriority.Reinforce` reaches. `TileWorkKind`
`.RepairWall` exists with its own rate (`RepairWallHealth` 22) but no job
priority selects it, so **kind 16 is not built as an imp job**.

---

## 4. The selection routine (`ImpState.cpp`, `00463e00`–`00464170`)

The routine is not a Ghidra-defined function — it was read from the
disassembly. In order:

**(a) A first block, keyed on an argument.** If the caller passed a kind, and
it differs from the imp's `LastTaskType` (`imp+0x9d`), a 36-byte record for
that kind (table at `006986a0`) is consulted flag by flag, and for each set flag
one kind is searched whole-map: 14, 13, 12, 9, 1, then the passed kind itself,
then kind 15 for kinds 1/2. **Not read further.** It reads as "an imp told to
do X also considers these"; where the argument comes from was not chased.

**(b) The `JustDropped` scan.** If `imp+0x99` is non-zero it is cleared and a
ring scan runs with the **nine-step chain** as its per-tile callback. This is
the one use of that chain. The flag's name is STRONGLY INFERRED from the dump's
`JustDropped`; the behaviour it produces is the familiar one — *drop an imp
beside something and it works on it.*

**It reaches three tiles, not the two it is handed.** The scan is given a radius
of `0x2000`, two tiles at `0x1000` to the tile, but the ring loop tests its
bound *after* scanning a ring, with a strict greater-than, from a counter
starting at 1 — so rings 1, 2 and 3 all scan. CONFIRMED: the owner counted it on
screen (2026-09-06, Q162) — three tiles pay, the fourth does not, and the bound
is measured to the tile the **work** is on. Reproduced rather than corrected
(ADR-0002, ADR-0069); `hand-of-evil.md` §6 has the instruction-level reading.

**(c) Tier A.** A byte table at `00698658` reads `0e 08 09 01 03 0d 0c ff`.
For each kind, whole-map (`radius = 0x7fffffff`), stop at the first that takes:

    14  claim the tile just dug (once nothing beside it is still marked)
     8  carry a loose object to its room
     9  build a placed trap / door        (10 via the same scanner)
     1  dig a marked wall                  (2, mine, via the same walk)
     3  claim floor
    13  carry an unconscious creature
    12  carry a corpse to the graveyard

**(d) Tier B.** A **whole-map ring scan** from the imp with the **four-step
chain** as its per-tile callback: reinforce/fix → gold → objects → kind 11.

**(e) Otherwise** the imp enters its idle/wander state (12).

### 4.1 Rescue is sixth to an idle imp and fourth to a dropped one

Worth stating outright, because the Tier A position reads alarmingly on its own
and the answer is two sections apart from the question.

**An idle imp ranks carrying the unconscious 6th of 7**, and Tier A is
unforgiving: the loop at `0x0046408B` walks the table strictly in order, passes
`0x7FFFFFFF` as the radius every time, and stops at the first kind that takes.
So an imp will dig a marked wall on the far side of the map before it will
rescue a creature bleeding out beside it. **Re-verified 2026-09-11** at both
ends — the table bytes read `0E 08 09 01 03 0D 0C FF` straight from
`0x00698658`, and the loop was read again — because the conclusion is
uncomfortable enough to be worth doubting twice.

**A dropped imp ranks it 4th of 9.** The nine-step chain of §2.3 is the
per-tile callback of the three-tile scan that runs when a keeper drops an imp,
and there carrying a creature comes **before** reinforce, gold, objects and
corpses — behind only dig/mine, claim and traps/doors.

**So DK2 gives the player the lever, and the asymmetry is the design.** Rescue
is not something the workforce does on its own initiative; it is something a
keeper *orders* by dropping an imp on the casualty. A reimplementation that
"fixed" the idle ranking would quietly remove a decision the original asks the
player to make.

**Neither reference project can check this.** OpenKeeper scores tasks as
`distance + priority` and picks the best — a weighted model, not an ordered
one — and its rescue task does not override the priority at all; Flame's map
names no symbol at the table's address. This is binary-only, and CONFIRMED there.

**What is *not* in Tier A is the finding.** Reinforcing and gold are never
searched whole-map by kind. They are found by distance, and only after every
whole-map kind has come up empty.

---

## 5. Gold, read end to end

- **Where it sits:** Tier B, second per tile, behind reinforce/fix. A pile two
  tiles away loses to bare rock beside the imp; a pile underfoot beats a wall
  across the room.
- **Whose ground:** the tile must be the imp's own or water (Q144), and the
  pile's owner must be the level-wide floor-gold id (Q136 — the two ends agree,
  the value is still unread).
- **When to stop fetching (Q142, read):** before the gold kind is even
  searched, `FUN_00464360(1)` tests the hands. An imp holding at least a
  **level-scaled share of `MaxGoldHeld`** (level 1: all of it; higher levels
  through a percentage table) less a global slack goes to **deposit**
  (`FUN_00464400`) instead. So an imp *does* top up a part-load, until its hands
  are nearly full.
- **Where it deposits:** `FUN_00464400` walks the keeper's rooms of type
  **Treasury (1)** or **Dungeon Heart (5)** with room left, reachable, and picks
  the **nearest**. That confirms `GoldStore.DepositInto`'s carrier route.
- **Kind 15** exists only in the first block: "deposit what you hold" after a
  dig/mine kind.

---

## 6. Claiming, read end to end

- **Kind 3** claims a tile that is not the imp's, not Solid/Water/Lava, carries
  no room structure, whose owner is nobody or a player the imp's keeper is
  **not allied with**, and which is orthogonally adjacent to the keeper's own
  floor. **Imps claim enemy floor**, in three flavours the state id records:
  plain path, enemy floor, enemy room tile.
- **Kind 14** is claiming's fast lane and is **first of everything**: the tile
  the imp has just dug is claimed as soon as **none of its eight neighbours
  still carries a dig task** — a kind-1 task, so a seam being mined next door
  does not hold it back — and a corridor is dug through and then claimed behind
  the digging front, which is the rhythm the original visibly has. **Built the
  same day** (Q154): `TileState.FreshlyDug` stands in for the task, set when
  excavation destroys a tile and cleared by the next change of terrain, and
  `JobPriority.ClaimFreshlyDug` is Tier A's first step. The *rank* and the
  eight-neighbour rule are read; the *trigger* — that the task is raised when
  the dig completes — is STRONGLY INFERRED from where the raise sits
  (`ImpState.cpp`, `00465774`/`00465b57`) and what it tests.

---

## 7. Task stealing, which this project does not have

Every handler, having found a task, asks who holds it (`FUN_00508110`). If
somebody does, and that somebody is a creature in a stealable state, the
handler compares **squared straight-line distance** — mine to the task versus
theirs to the task — and **takes the task if it is nearer**, re-assigning the
holder. The gold and reinforce handlers, the claim handler and the dig handler
all do it. Ours never re-assigns a job once taken (`ImpCrowding` refuses the
face instead). Recorded, not built (**Q155**).

---

## 8. What this project does, as of ADR-0058 and ADR-0069

| | DK2 | ours |
|---|---|---|
| Tier A | **14**, 8, 9/10, **1/2**, **3**, 13, 12 — whole map each | **all of it but kind 9/10**: claim the just-dug tile, **8** (ADR-0065), marked excavation, claim, **13** both branches (ADR-0063, ADR-0064), **12**. Six of seven, in the original's order. The one missing is kind 9/10 — fetch a crate for a placed trap or door — which needs traps, doors and manufacturing |
| Tier B | ring scan; per tile **6/16 → 7** → 8 → 11 | one nearest-first search; at the tile found, **reinforce → gold**. Kind 16 (fix a damaged own wall) is not a job here — §3 |
| distance | rings, squared straight-line | steps along the walkable grid (Q25) |
| kind 14 | claim the just-dug tile first, once no 8-neighbour has a dig task | **built** — `ClaimFreshlyDug`, same rule |
| stealing | nearer imp takes the task | not built (Q155) |
| just-dropped scan | three tiles, nine-step chain | **built** (ADR-0062, ADR-0069) — `TryTakeDroppedJob`, `DropScanTiles` = 3 Chebyshev measured to the work's tile, in DK2's drop order minus the two crate kinds and kind 11 (Q162) |
| the aimed bonus | `DroppedTask` carries Efficiency +10 for as long as work of that kind lasts; `DroppedTask == LastTaskType` retries that kind whole-map before it is cleared | **built** (ADR-0069) — `TryTakeJobOfKind`, `CreatureState.DroppedTask`/`JustDropped`/`LastTaskKind`. The bonus itself is `hand-of-evil.md` §6 |
| kind 13 | carry an unconscious creature: own to a Lair, an enemy's to a Prison. **The Combat Pit is a confirmed source**: its loser is knocked out, never killed (owner, Q310), so a pit fight feeds this kind first — and kind 12 only when no imp arrives before the dying window closes, which the owner confirms does kill it | **built, both branches** — the rescue to its own bed (ADR-0063) and the capture into a cell (ADR-0064). What the prison then does is `docs/research/prison.md` |
| kind 12 | carry a corpse to the Graveyard | **built** (ADR-0063); burial now feeds its per-room tally and Vampire threshold (Q165, ADR-0151) |

`ImpTaskSystem.TryTakeJob` is Tier A and `TrySweep` is Tier B, and the
`ImpFetchesGoldTests` "second tier" section pins the four cases: claim beats
gold underfoot; a nearer pile beats a farther wall; a wall beats gold on the
same tile; a nearer wall beats a farther pile.

---

## 9. Two traps in reading this code

**A fixed-order chain is not a priority list until you know who calls it and how
often.** The nine-step chain of §2.3 reads exactly like one and is the per-tile
callback of a scan that runs only after an imp has been dropped.

**"This kind tests flag F" is not an identification until the polarity is
checked.** Kind 12 tests for *owned* ground, not unowned, and carries corpses;
kind 3 tests for the *absence* of Lava and Water, and is claiming.

---

## 10. Open, in one place

- **Q153** — answered: the reinforce ceiling at `terrain+0x1ad` is `MaxHealth` (§3).
- **Q154** — answered and built: kind 14, claim-the-just-dug-tile. The trigger's
  timing is inferred, not read (§6).
- **Q162** — answered: the drop scan reaches three tiles (§4b).
- **Q155** — task stealing by distance: read, not built (§7).
- **Q25** — the dig search walks the task list within a radius; whether it
  takes the *nearest* qualifying task or the *first in list order* was not read.
  The owner's answer (nearest by walking distance) stands as the observation.
- kind 11's identity and raiser; the `tile+0xb & 0x20` scan pre-filter; the
  first block's argument and its per-kind record table.
