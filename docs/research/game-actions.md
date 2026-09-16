# Mechanic: the game action table — every player command, and what handles it

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-16. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)

Original version: DK2 v1.70 (GOG)

Questions: opens Q340. Came out of correcting `combat.md` §3, which named one of
these handlers after one of its arms.

**A research multiplier rather than a finding**, in the shape of
`balance-variables-in-the-binary.md`: it answers *what code runs when the player
does N?* DK2 is command-driven (`InputFrame`), so this is the whole surface
between a person and the simulation.

---

## 1. The table

**86 records of 20 bytes based at `0x006A54B8`**, filled by a straight-line run
in `FUN_00511A80`. Each record is `[id at +0, handler at +4]` with three dwords
left zero — the shape of a list node linked at run time, which is why **no code
references the table's address**: it is never indexed, only walked.

85 of the 86 carry an id, running **1 to 137 with 52 gaps**. Every handler takes
the action record on the stack and a context in `ECX`, and returns 1.

## 2. Why the identification is safe

The layout was got wrong first — `[handler, …, id]` rather than `[id, handler]`,
a base four bytes late — and **six facts this project had already established
independently caught it**. All six land on a handler here:

| id | what prior research calls it | source |
|---|---|---|
| 40 | tag a tile for digging | `KEEPER_AI.md` |
| 41 | untag | `KEEPER_AI.md` |
| 62 | slap | `KEEPER_AI.md` |
| 63 | remove Call To Arms | `KEEPER_AI.md` |
| 88 | pick up Special | `KEEPER_AI.md` |
| 135 | Temple keeper-spell offering, handler `0x005133A0` | `sacrifice.md` §3 |

None was used to *derive* the layout; all six were checked against it after.

## 3. `FUN_00514830` is game action 26, not "the healing handler"

`combat.md` §3 named it from its `HpFromChicken` arm and recorded the table's
handler at `+0x10`. Both are corrected here: the handler is at `+4`, and the
function is the handler for **game action `0x1A` (26)**. Its other arms start
creature actions 16 and 17 (`creature-action-table.md`) and set a creature's
level at a Hero Lair (`creature-levels.md` §2.1). What the action *is* is
**Q340** — a name taken from one arm is what caused this, and one arm is all the
evidence there is for any of the three.

## 4. The map

| id | handler | id | handler | id | handler |
|---|---|---|---|---|---|
| `0x01` | `0x00512930` | `0x35` | `0x005132a0` | `0x53` | `0x00506050` |
| `0x14` | `0x00514560` | `0x36` | `0x005132d0` | `0x54` | `0x005060e0` |
| `0x15` | `0x00514590` | `0x37` | `0x00513340` | `0x55` | `0x00506100` |
| `0x19` | `0x005146b0` | `0x38` | `0x00513370` | `0x58` | `0x00513760` pick up Special |
| `0x1a` | `0x00514830` **UNKNOWN — Q340** | `0x39` | `0x00513480` | `0x5c` | `0x005137e0` |
| `0x1b` | `0x005163c0` | `0x3a` | `0x005134e0` | `0x5d` | `0x00513800` |
| `0x1c` | `0x00516ec0` | `0x3b` | `0x005135b0` | `0x64` | `0x00513850` |
| `0x1d` | `0x00517270` | `0x3c` | `0x00513600` | `0x65` | `0x00513860` |
| `0x1e` | `0x00517190` | `0x3d` | `0x00513650` | `0x66` | `0x00513870` |
| `0x1f` | `0x00517220` | `0x3e` | `0x00513690` slap | `0x69` | `0x00513ce0` |
| `0x20` | `0x005172b0` | `0x3f` | `0x005136e0` remove Call To Arms | `0x6a` | `0x00513dc0` |
| `0x21` | `0x005172c0` | `0x40` | `0x00513730` | `0x71` | `0x00513e50` |
| `0x22` | `0x00517310` | `0x42` | `0x00505830` | `0x72` | `0x00513eb0` |
| `0x24` | `0x005169e0` | `0x43` | `0x00505a10` | `0x76` | `0x00513ee0` |
| `0x25` | `0x00516a20` | `0x44` | `0x00505aa0` | `0x77` | `0x00514100` |
| `0x26` | `0x00516e40` | `0x45` | `0x00505ad0` | `0x78` | `0x005141b0` |
| `0x27` | `0x005171d0` | `0x46` | `0x00505b30` | `0x79` | `0x00514240` |
| `0x28` | `0x005129a0` tag a tile for digging | `0x47` | `0x00505bb0` | `0x7a` | `0x00513f60` |
| `0x29` | `0x005129e0` untag | `0x48` | `0x00505c30` | `0x7b` | `0x00513ff0` |
| `0x2a` | `0x00512ee0` | `0x49` | `0x00505c90` | `0x7c` | `0x00514280` |
| `0x2c` | `0x005172b0` | `0x4a` | `0x00505d00` | `0x7d` | `0x00512940` |
| `0x2d` | `0x005127b0` | `0x4b` | `0x00505d70` | `0x81` | `0x005142e0` |
| `0x2e` | `0x00512800` | `0x4c` | `0x00505da0` | `0x82` | `0x00514320` |
| `0x2f` | `0x00512850` | `0x4d` | `0x00505dd0` | `0x86` | `0x00512950` |
| `0x30` | `0x00512890` | `0x4e` | `0x00505e20` | `0x87` | `0x005133a0` Temple keeper-spell offering |
| `0x31` | `0x005128f0` | `0x4f` | `0x00505e80` | `0x88` | `0x00514420` |
| `0x32` | `0x00513210` | `0x50` | `0x00505ec0` | `0x89` | `0x00514470` |
| `0x33` | `0x00513240` | `0x51` | `0x00505f00` |  |  |
| `0x34` | `0x00513270` | `0x52` | `0x00505f80` |  |  |
## Confidence

**CONFIRMED (binary):** the base, the 20-byte stride, the 86 records and the
`[id, handler]` layout; that 85 ids run 1–137; that `FUN_00514830` is id 26.

**CONFIRMED (cross-reference):** the six anchors of §2.

**UNKNOWN:** what all but those six ids mean, and the 52 gaps — whether they are
unused ids or actions handled somewhere else entirely.
