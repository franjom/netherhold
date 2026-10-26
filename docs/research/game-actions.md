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
| 54 | cast a keeper spell at a place; Call To Arms built | `call-to-arms.md` |
| 63 | remove Call To Arms: the slap, built | `call-to-arms.md` |
| 88 | activate selected Special | `specials.md` |
| 135 | Temple keeper-spell offering, handler `0x005133A0` | `sacrifice.md` §3 |

None was used to *derive* the layout; all six were checked against it after.

## 2.1 The recordings name thirteen more, and the two tables are one id space

DK2's own packet recorder writes the player's commands as `[startTile, endTile,
zero, action id, word]` (`navigation-captures.md`, Q356). **Those action ids are
these ids.** Three of §2's anchors — 40 tag, 41 untag, 62 slap — came back from
recorded sessions unchanged; action 88 activates a selected Special (`specials.md`).

So the two halves name each other. The recordings say what a command *is*, from
an owner performing a numbered list; this table says what code *runs*. The names
folded into §4's map below are the recordings', and each one now has a handler to
read when its turn comes. Thirteen were added this way; **93 and 106 appeared in
a recording against no action the owner named, and stay UNKNOWN.**

## 3. `FUN_00514830` is game action 26, not "the healing handler"

`combat.md` §3 named it from its `HpFromChicken` arm and recorded the table's
handler at `+0x10`. Both are corrected here: the handler is at `+4`, and the
function is the handler for **game action `0x1A` (26)**. Its other arms start
creature actions 16 and 17 (`creature-action-table.md`) and set a creature's
level at a Hero Lair (`creature-levels.md` §2.1).

**Q340, now STRONGLY INFERRED: action 26 is the possessed creature acting.** Its
three arms stop looking unrelated once §2.1's names land on its neighbours: 21 is
*leave possession* and 25 is *the per-turn stream while possessing*, so 26 sits
inside the possession cluster. A possessed creature swings (actions 16 and 17,
the spell and melee instances) and eats a chicken (`HpFromChicken`) — two of the
three arms are what possession does, and combat's own entry does not go through
here (`combat-entry.md`). **Cheap confirmation, not yet run**: possess a creature
and attack with `-PSAVE` running; the prediction is action 26 in the stream.

## 3.1 Actions 75 and 134 are what happens after the level's result (2026-09-28)

The owner's Level 2 win (`l2win928`, `navigation-captures.md`) holds one of each:
75 at turn 3289, 26 turns after the win, and 134 at 3525, the turn the probe saw
player 3's `Status` go from 2 back to 0. Both carry zero fields.

- **75 reveals the whole map, locally.** Handler `0x00505D70` compares the record's
  player word with the world's local player (virtual `+0x264`). Only on a match
  does it call `0x00451EF0` on the runtime map, which explores every tile
  (`fog-reveal-sources.md` §3.3). DK2 keeps one fog bit per machine, so this is a
  view of the issuing player's own map.
- **The cheat issues 75 too.** Ctrl+Alt+C (scancode `0x2E` with both modifier bits,
  `0x0040A600`, not in game mode 3) opens cheat entry; `0x0040A680` matches typed
  characters against eight phrases from `0x0068F550`, and `0x0040A760` queues one action
  each. "now the rain has gone" queues 75, so it reveals the map as the result screen
  does, and travels in the packet record like any action.
- **134 lets the player carry on.** Handler `0x00512950` calls `0x004C0120` on the
  player and clears bit `0x10` of an interface field (`+0xE3C`), then calls
  `0x0044DB90(0)`. `0x004C0120` sets player `+0x76` bits: `0x440000` after a win,
  `0x400000` (clearing `0x40000`) otherwise, adding `0x80000` for status 1. It then
  writes `Status = 0` and posts an event with code `0x74` (`0x0050E5C0`). It calls
  `0x0048AD30(1)` on each owned creature except the Horned Reaper (descriptor flag
  `0x4000`, the flag Summon Horny's cast check tests, Q447), or where `+0x3E6` bit 0
  is set or `+0x3CB` is non-zero. In single player it calls `0x00491B70` on the
  keeper's Horned Reaper, if there is one.
- **Who issues them.** The key handler `FUN_00405010` runs this arm only while
  player `Status` is non-zero (`0x004054F0`). In the Won, single-player branch,
  **Escape** (scancode `0x01`) sets player `+0x109E` and `+0x76` bit `0x100000`,
  then issues 75. **Space** (`0x39`) issues action 1 in every branch: leave the game
  (`0x00510F40` -> world slot `+0xC` `0x00510E90`), which for the local player sets the
  world flag `+0x723B` that ends the session loop (`0x00509850`, `0x00526590`). In the
  other game modes, Escape issues 134 and then 75 at once. The single-player 134
  comes from `0x0042C390`. Once two interface fields are set, it arms a deadline
  of `0x005B5E90() + 500`, and one of the two is set when the end-screen layout
  routine `0x0042BE90` (scaled to 1280 × 800) returns 0. When the deadline passes
  with bit `0x100000` on the player, it issues 134 and clears the bit.

STRONGLY INFERRED from the binary, fitted to one recording. The clock's unit is
assumed to be milliseconds (UNKNOWN). So are the meanings of `0x0048AD30`,
`0x00491B70` and event `0x74` (Q448). For a replay these are
**after the result**: a comparison that stops at the win need not decode them.

## 4. The map

| id | handler | id | handler | id | handler |
|---|---|---|---|---|---|
| `0x01` | `0x00512930` | `0x35` | `0x005132a0` | `0x53` | `0x00506050` |
| `0x14` | `0x00514560` | `0x36` | `0x005132d0` cast a spell at a place | `0x54` | `0x005060e0` |
| `0x15` | `0x00514590` leave possession | `0x37` | `0x00513340` cast a spell on a creature | `0x55` | `0x00506100` |
| `0x19` | `0x005146b0` the per-turn stream while possessing | `0x38` | `0x00513370` | `0x58` | `0x00513760` activate Special |
| `0x1a` | `0x00514830` **the possessed creature acts — Q340** | `0x39` | `0x00513480` take a creature into the hand | `0x5c` | `0x005137e0` the window lost or regained focus |
| `0x1b` | `0x005163c0` | `0x3a` | `0x005134e0` | `0x5d` | `0x00513800` |
| `0x1c` | `0x00516ec0` | `0x3b` | `0x005135b0` take gold into the hand | `0x64` | `0x00513850` |
| `0x1d` | `0x00517270` | `0x3c` | `0x00513600` | `0x65` | `0x00513860` |
| `0x1e` | `0x00517190` | `0x3d` | `0x00513650` drop what the hand holds | `0x66` | `0x00513870` |
| `0x1f` | `0x00517220` | `0x3e` | `0x00513690` slap | `0x69` | `0x00513ce0` |
| `0x20` | `0x005172b0` | `0x3f` | `0x005136e0` remove Call To Arms | `0x6a` | `0x00513dc0` |
| `0x21` | `0x005172c0` | `0x40` | `0x00513730` | `0x71` | `0x00513e50` |
| `0x22` | `0x00517310` | `0x42` | `0x00505830` | `0x72` | `0x00513eb0` select a category and an item |
| `0x24` | `0x005169e0` | `0x43` | `0x00505a10` | `0x76` | `0x00513ee0` |
| `0x25` | `0x00516a20` | `0x44` | `0x00505aa0` | `0x77` | `0x00514100` |
| `0x26` | `0x00516e40` | `0x45` | `0x00505ad0` | `0x78` | `0x005141b0` |
| `0x27` | `0x005171d0` | `0x46` | `0x00505b30` | `0x79` | `0x00514240` |
| `0x28` | `0x005129a0` tag a tile for digging | `0x47` | `0x00505bb0` | `0x7a` | `0x00513f60` |
| `0x29` | `0x005129e0` untag | `0x48` | `0x00505c30` | `0x7b` | `0x00513ff0` |
| `0x2a` | `0x00512ee0` | `0x49` | `0x00505c90` | `0x7c` | `0x00514280` |
| `0x2c` | `0x005172b0` | `0x4a` | `0x00505d00` | `0x7d` | `0x00512940` |
| `0x2d` | `0x005127b0` | `0x4b` | `0x00505d70` reveal the map after a result (§3.1) | `0x81` | `0x005142e0` |
| `0x2e` | `0x00512800` | `0x4c` | `0x00505da0` | `0x82` | `0x00514320` |
| `0x2f` | `0x00512850` build a room | `0x4d` | `0x00505dd0` | `0x86` | `0x00512950` carry on after a result (§3.1) |
| `0x30` | `0x00512890` | `0x4e` | `0x00505e20` | `0x87` | `0x005133a0` Temple keeper-spell offering |
| `0x31` | `0x005128f0` sell | `0x4f` | `0x00505e80` | `0x88` | `0x00514420` |
| `0x32` | `0x00513210` place a door | `0x50` | `0x00505ec0` | `0x89` | `0x00514470` |
| `0x33` | `0x00513240` | `0x51` | `0x00505f00` |  |  |
| `0x34` | `0x00513270` place a trap | `0x52` | `0x00505f80` |  |  |

View actions (4, 6, 15, 17, `0x73`, `0x74`, `0x84` and 30 more) have no entry here; the bridge
handles them from its own table at `0x0066CE30` ([campaign-win-sequence.md](campaign-win-sequence.md) §4).
## Confidence

**CONFIRMED (binary):** the base, the 20-byte stride, the 86 records and the
`[id, handler]` layout; that 85 ids run 1–137; that `FUN_00514830` is id 26.

**CONFIRMED (cross-reference):** the six anchors of §2.

**UNKNOWN:** what all but those six ids mean, and the 52 gaps — whether they are
unused ids or actions handled somewhere else entirely.

**50 places a door and 52 a trap** (corrected 2026-10-01; the table had them swapped). Both
handlers pass the tile (x low, y high) and the id in the next field's low byte, to `0x004C33B0`
(door) and `0x004C3860` (trap). Level 3's capture (`level3-live1`) shows it: DK2's door records
appear on the five tiles of action 50 at ticks 1223–1270, its trap records on the fourteen of 52.
