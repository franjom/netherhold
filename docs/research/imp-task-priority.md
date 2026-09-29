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

**Order (read 2026-09-29).** A raise puts the new task at the **head** of its kind's list
(`00507bb0`: its next is the old head), so a walk visits the most recently raised first,
and a walk that keeps its best unless strictly nearer lets the newest win a tie. A dragged
mark files its tiles (`0x00512a20`, run under an emulator) in square shells around the
corner the drag began at: shell k from (k,0) to (k,k) to (0,k) when k is odd, the other way
when even, clipped to the rectangle. **Built (ADR-0288)** as `TileState.TagOrder`, the
dig walk's tie-break. DK2's diggers stand off a face's centre by the same fraction on both
axes (672/4096 of a tile in the Level 1 capture), so two marks one diagonal apart are
exactly equally near, and two of that game's 309 choices were decided by the list. Ours
stands elsewhere and rarely ties, so over 20 seeds the order changes no outcome (Q492).

Most kinds are raised by the very handler that consumes them — the handler
scans, finds work, raises the task, takes it. **Kinds 1, 2 and 14 are raised
from outside the imp code**, which is what makes them identifiable (§3).

### 2.2 Two ways to search for one kind

`FUN_00468520(kind, radius)` is *"find me a job of this kind within this
distance"*, and it has two shapes:

- **kinds 1 and 2** both call one walk, `FUN_00469af0` (read 2026-09-29): every
  kind-1 task, then every kind-2 task, each tested by squared **straight-line**
  distance from the imp to the task tile's centre. A task replaces the best so
  far only when strictly nearer, so a wall beats a seam at the same distance and
  list order breaks other ties. A task qualifies when one of its four sides,
  tried from the side facing the imp, is not Solid, passes a reachability test
  (`FUN_00471630`, flag `0x80`) and has a free holder slot of three
  (`FUN_00508110`), or a slot whose holder it may take (§7). For seams only, a
  slot is also refused while a creature stands in that slot's third of the face
  tile. **After a mining job** (`+0x98` = 2) seams are measured from the last
  task tile (`+0x8C`/`+0x90`), not the imp, which is what sends a miner back to
  its seam after a delivery. The walk writes `+0x98` = 1 and state 13 for a wall,
  2 and state 25 for a seam, and the task tile to `+0x8C`/`+0x90`;
- **kinds 3, 6/16, 7, 8** run a **ring scan** (`FUN_00468d10`) outwards from
  the imp, calling the kind's handler on each tile in turn; kinds 9/10, 11,
  12, 13 have scanners of their own over the things or rooms they concern;
- **kind 15** deposits: if the imp holds any gold (`FUN_00464360(0)`, `+0x3FB`
  non-zero), the store chooser `FUN_00464400`.

The ring scan walks **diamonds**, not squares (read 2026-09-29 by running the
function's own bytes under an emulator): the imp's own tile first, then every
tile at Manhattan distance 1, then 2, and one ring past the radius it is given.
Each ring is four sides walked clockwise corner to corner — north to east, east
to south, south to west, west to north — both corners visited, starting at side
`k` of a start side chosen once per scan. For claims (kind 3) the second
callback `0x00469340` chooses it: it grades the four neighbours in the order
north, east, south, west (`0x00469460`, ranked by the byte table at
`0x00698628`: a marked wall or seam 1, a rival's room 3, a rival's floor 4,
unowned floor 5 — each only if it touches the keeper's floor — a wall to
reinforce or repair 12, anything else 127) and keeps the first of the best. For
the other kinds, which pass no chooser, the side is `rand(4)` from the world
seed, drawn at every scan. A tile whose byte `+0xB` has bit `0x20` **set** is
skipped unless it carries a dig or mine task. The bit is clear on tiles touching
anything the keeper owns, walls included (level2-live1), so for claims it is
implied by the handler's own adjacency test; its writers are unread (Q487).
Built for claims (ADR-0285); the drop scan's rings are still squares here (Q488).

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
| **3** | **claim floor** | `0xf` / `0x13` / `0x11` | tile **not** the imp's, **not** Solid/Water/Lava, owner nobody **or not allied** (alliance table `FUN_004bf3d0`), no room structure on it, and **orthogonally adjacent to the imp's own floor** (`FUN_00453b30`: a neighbour counts when the keeper owns it, its terrain is Ownable and not Solid — so not the keeper's walls; built ADR-0285). Three states: plain unowned path; enemy floor (flag Attackable); enemy room tile |
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

**(0) The cap.** Past the special-owner exit, a keeper's per-player counter
(`player+0x7A`, zeroed each player frame) is tested: at 4 or more, the imp goes
to 12 with its deadline at now + 20 + rand(80), the draw taken once 12 accepts
it; otherwise the counter goes up and selection runs. A just-dropped imp skips
this (read 2026-09-29, `0x00463E2C`–`0x00463E9C`).

**(a) The kind just done** (read 2026-09-29). The byte at `+0x98` is not an
argument: every task handler writes its own kind there as it takes a task
(the walk 1 or 2, claim 3, reinforce/fix 6/16, gold 7, objects 8, crates 9/10,
corpse 12, carry 13), the claim state writes 3 again on finishing, and state 11
zeroes it on a drop. When it is non-zero: if it equals `+0x9D` (the aimed kind of
ADR-0069, `hand-of-evil.md` §6) that kind is searched whole-map first, and on
failure `+0x9D` is cleared. Then its 36-byte record (`006986a0` + kind × 36) is
read. Six dwords select searches: kinds 14, 13, 12, 9, 1, and the kind itself.
In the shipped table **only the last is ever set, and only for kinds 1–5**; 6, 7,
16 set only dwords this code never reads. So after a dig, a mine or a claim the
imp looks for more of the same anywhere first, and after a dig or a mine
deposits (kind 15) if nothing is found. `+0x98` is then zeroed, but not when the
deposit is taken: `0x00464005` returns before the zeroing at `0x00464010`, so the
walk to the store keeps kind 1 or 2 and can claim on the way (ADR-0288). This is what
keeps a claimer claiming and a digger digging, and why a corridor is claimed on
to the room at its end. Nothing before this step deposits: state 10 has no
"put the load down first", so a claimer carries its gold on (ADR-0288).

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
- **When to stop fetching (Q142, read):** `FUN_00464360(1)` tests the hands:
  full at the **level-scaled `MaxGoldHeld`** (level 1 unscaled; higher levels
  through the MaxGoldHeld row) less variable 74, which ships no value. The kind
  dispatcher's kind 7 (`0x00468520`) sends full hands to the **store chooser**
  (`FUN_00464400`); only the aimed retry reaches that arm. Tier B calls the gold
  handler `0x0046b6d0` directly, which makes the same test and only declines.
  So an imp tops up a part-load until its hands are full.
- **After the pick-up (state 27, `0x00466850`, read 2026-09-29):** the piles
  within `0x555` of the imp are taken up to the level-scaled cap, stopping once
  full. Full hands then go to the store chooser, falling back to 10 when no store
  has room; a part-load goes to 10 and stays in the hands.
- **Who deposits at all:** the store chooser has five call sites (a raw scan of
  every executable block): state 26 mining, state 27 above, the dispatcher's kind
  7 and kind 15, and state 76 (the hand-drop landing). An imp with gold and
  nothing to dig keeps it (the Level 1 capture: 1092 gold through 41 claims
  over 680 ticks, with room in the heart).
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
| the kind just done | after 1/2/3 search that kind first; after 1/2 deposit | **built** (ADR-0282) |
| the cap | 4 selections per keeper per tick | **built** (ADR-0282) |
| Tier A | **14**, 8, 9/10, **1/2**, **3**, 13, 12 — whole map each | **all of it**; 14 is a no-op in the original (its search has no arm), so none here (ADR-0286); **8** (ADR-0065), **9/10** (ADR-0214, `workshop-crates.md` §6), marked excavation, claim, **13** both branches (ADR-0063, ADR-0064), **12**, in the original's order |
| Tier B | ring scan; per tile **6/16 → 7** → 8 → 11 | one nearest-first search; at the tile found, **reinforce → gold**. Kind 16 (fix a damaged own wall) is not a job here — §3 |
| distance | rings; for 1/2 straight-line, seams from the last seam | the 1/2 walk straight-line (ADR-0282); other kinds steps along the walkable grid; seam slot occupancy not built |
| kind 14 | taken only on the way: `0x00464170` on entering a tile, category 3, more than two tiles from the task tile, last kind 1–7 or 16 | **built for deposit walks** (ADR-0286, Q489) — `TryClaimOnTheWay` |
| gold in hands | put down only by 26, 27 (full hands), 76, and in 10 by kind 15 or the aimed kind 7 (full hands) | **built** (ADR-0288); our pre-choice deposit is gone |
| equal distance | the newest task, the list's head | **built** for the dig walk (ADR-0288), `TileState.TagOrder` |
| stealing | nearer imp takes the task | not built (Q155); 7 of the Level 1 capture's choices and 38 of Level 2's were an imp losing its task this way |
| just-dropped scan | three tiles, nine-step chain | **built** (ADR-0062, ADR-0069) — `TryTakeDroppedJob`, `DropScanTiles` = 3 Chebyshev measured to the work's tile, in DK2's drop order minus kind 11 (Q162); the crate kinds are ADR-0214 |
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
- **Q154** — revised (ADR-0286): kind 14 is never chosen; it is claimed on the way (Q489).
- **Q162** — answered: the drop scan reaches three tiles (§4b).
- **Q155** — task stealing by distance: read, not built (§7).
- **Q25** — answered 2026-09-29: the nearest by straight-line distance, list
  order only on an exact tie (§2.2).
- kind 11's identity and raiser; the `tile+0xb & 0x20` scan pre-filter.
