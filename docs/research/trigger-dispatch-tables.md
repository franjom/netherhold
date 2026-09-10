# The trigger dispatch tables, and the arms behind them

How a condition type becomes a rule, and what the arms read that
`trigger-semantics.md` does not already cover. Written 2026-09-10 while ranking
the campaign's remaining stalls by the walker that produces them.

## Reading a dispatcher

Each of the four dispatchers is two-level: a range check, a byte table indexed
by `type - base`, and a dword jump table indexed by that byte. Several types
therefore share one arm, and reading either table alone tells you nothing.

| context | check | byte table | count | jump table | base |
|---|---|---|---:|---|---:|
| world | `0x00518282` | `0x0051897C` | 67 | `0x00518950` | 1 |
| player | `0x004C7B30` | `0x004C85E8` | 56 | `0x004C8580` | 19 |
| area | `0x004B0D7F` | — | 32 | — | 39 |
| creature | `0x0049AAAD` | — | 63 | — | 3 |

`DispatchMap.py` prints `index + 1`, so a true type is **printed + base - 1** —
`+18` for the player table. Three arms already known independently confirm it:
printed 4, 29 and 54 land exactly on types 22 (`0x004C7D0F`), 47 (`0x004C7B4B`)
and 72 (`0x004C84B8`).

**A shared arm is a real finding.** The player table's `0x004C850C` is
`XOR EAX,EAX; RET` — types 48, 53 and 56–61 are *constant false* rather than
unimplemented, which is why declining them would be wrong.

## Type 46 is DK2's own silent false

The world table gives type 46 its own slot, and that slot's jump entry is
`0x00518941` — the tail of the unhandled path, **past** the
`Unhandled Trigger When [ Type %d, Index %d ]` printf at `0x0051892F`. So the
engine returns zero for it deliberately and says nothing, unlike the 57 types
that reach `0x00518929` and do print. One shipped record uses it, in level 1's
player tree, reaching the world arm by delegation. CONFIRMED from the tables.

## The three availability arms: 27, 28, 29

Four `{count, base}` availability tables hang off the player at stride 8, and
four near-identical lookups read them. The base offset each one loads settles
which table it is, with no inference:

| lookup | loads | table | condition |
|---|---|---|---|
| `0x004BAB20` | `+0x56`/`+0x5A` | `RoomInfo` | (types 24/25/68) |
| `0x004BACD0` | `+0x5E`/`+0x62` | `DoorInfo` | **27** |
| `0x004BAE30` | `+0x66`/`+0x6A` | `TrapInfo` | **28** |
| `0x004BAFA0` | `+0x6E`/`+0x72` | keeper spells | **29** (ADR-0120) |

Types 27 and 28 have parallel arms (`0x004C80CE`, `0x004C8240`) with **two
modes**, but their counting partitions are different:

- `Comparison == 0` is availability, the same question type 29 asks: a named
  `TargetId` is looked up and tested against AVAILABLE (3); zero walks the whole
  table for any entry at 3.
- `Comparison != 0` counts, and byte 02's bits 3 and 4 select the partition.
  Type 27 counts doors whose dword at `+0x109` is nonzero, or total doors minus
  those; this is **not** the trap bit and the field remains UNKNOWN (Q284).
  Its named-type helper `0x004BA850` walks owned doors and counts matching
  `thing+0xF6` type ids.
- Type 28 counts traps with `thing+0x10C & 0x20`, or total traps minus those.
  The bit is set by the trap constructor, prevents firing and ordinary updates,
  and is cleared when an existing trap is finished in place: **UnderConstruction**
  is STRONGLY INFERRED. `0x004BA890` is the same per-type walk over owned traps.
  If the named trap's shot id at definition `+0xBB` is 26, the Boulder shot,
  the arm also counts owned type-74 Boulder objects still in the world.

**All ten shipped records use the counting mode** — comparisons 3 and 5, never
0 — so implementing the availability half would have moved nothing. The
`player+0xC`/`+0x22` are the trap owned-list head and total. Neither condition
is built: door runtime state is absent, and trap construction, firing and the
live Boulder projectile are absent (Q249, Q281, Q284).

## Area type 70 is not type 39 asked for existence

Both walk the action point's rectangle and both end at the same predicate
`FUN_004B0A80(thing, category, typeId)`, but the existence walk
(`0x004B06D0`, behind type 70) applies **two filters the counter
(`0x004B0620`, behind type 39) does not**:

- a creature must have `thing+0x3E6` bit 7, `Possessed`, set;
- an object must have `thing+0x146` bit 3, also `Possessed`, set.

The names come independently from the creature and object debug dumpers
(`creature-object.md`, `objects.md`), while the condition arm establishes their
meaning here. Type 70 reads no comparison or value: byte 01 is player, byte 02
is subtype, and byte 03 `== 6` selects objects; anything else selects creatures.
All six shipped records select creatures owned by player 3, five with any
subtype and one with subtype 23.

The creature branch is built from `PlayerState.PossessedCreatureId`, the other
end of the same relation. The object branch still declines because this
simulation has no object-possession state; no shipped record reaches it (Q280).

## Four player arms read whole

| type | arm | reads | rule |
|---|---|---|---|
| 23 | `0x004C7DF0` | `+0x146`, else `+0x19E + 2n` | creatures **this** keeper killed: byte 02 zero is the total, a named player is that player's slot |
| 52 | `0x004C7E4E` | creature `+0x403` | count of owned non-worker creatures at or above byte 03's level |
| 71 | `0x004C8491` | `+0xEE6` | `NumberOfSlaps` |
| 73 | `0x004C8500` | `+0x134`, then `+0x488` | not possessing → **false without comparing**; possessing → compare the possessed creature's `+0x488` |

Types 22 and 23 are the two directions of one array. `A[P][n]`, the word at
player `P`'s `+0x19E + 2n`, is read by type 22 as `A[Q][me]` and by type 23 as
`A[me][Q]`; the totals beside them, `+0x173` and `+0x146`, are "creatures of
mine lost" and "enemy creatures killed" from the score block. Both therefore
wait on kill attribution, which waits on combat.

Type 52's byte 03 is a **level**: the arm compares it against the creature's own
`+0x403` with `JC`, so the count is "level >= byte 03", and workers are excluded
by the definition's `+0x686` bit 0 — the same bit type 19's unfiltered total
subtracts them by. The three shipped records ask for one creature at level 4,
one at 8, and twenty at 2, which is what a level threshold should look like.

## Type 64 counts party members no longer active

The world arm at `0x005187AD` reads byte 01 as a **party id**, not a player id.
It indexes the 24-slot party table at world `+0x4030` (stride `0x208`) and counts
the sixteen member records whose first byte is non-zero. The members begin at
slot `+0x24`, have stride `0x20`, and are the same sixteen records action type
14 walks before creating creatures.

It subtracts party creatures that are still active. The first walk starts at
the world creature-list head (`world+0x08`), skips creatures owned by the Good
player (`word[thing+0x28] == word[0x006F254C]`), and counts matching
`thing+0x417` party ids whose `thing+0x3E6` bit 21 is clear. The second walk
starts at `ThingsOwnedList[0]` on **player-array entry 0**, the Good player, and
applies the same party-id and bit-21 filters. It does not index the player array
by the body byte. Bit 21 is `CreatureDying`, so the measured quantity is:

`occupied member templates - active, non-dying creatures in that party`.

The comparison tail at `0x00518883` uses that signed difference as the left
operand and the body dword as the right, through **its own inline jump table**
at `0x00518AB0` rather than the shared routine `0x00519980`. That table is
**not in address order** — entry 3 is `0x005188FF` (`SETG`) and entry 4 is
`0x005188EA` (`SETGE`), so its codes mean what the shared routine's mean, 4 `>`
and 5 `>=`, and reading the arms in address order would swap them. Second
instance of that hazard after Q272; assume nothing from listing order.

All 37 shipped records exercise this path: comparisons are 35 `>=` and two
`==`; values span 2–8; party ids span 0–8, and **8 is outside the player-id
range** any other condition uses, which is the corpus half of the `PartyId`
correction. **STRONGLY INFERRED** from DKII.EXE v1.70 and the shipped corpus.

This is deliberately not built. Action 14 only arms subtrees here; it creates
no party creatures, and `CreatureState` carries no party id. Computing from
templates alone would therefore count every party member as missing as soon as
the subtree is armed and could fire twelve reached conditions early (Q283).

## Action type 1 creates one creature

The world arm is `0x00518B12`, and it is the largest action arm built so far.
Read whole 2026-09-10.

**Its coordinates are zero-based.** The body's two words become a spawn point as
`(n << 12) + 0x800`, the centre of tile *n*, and **nothing decrements them** —
where type 22's arm, twelve slots along the same jump table, `DEC`s both first
(ADR-0136). Two neighbouring arms read the same pair of words under different
conventions, and the parameter values cannot tell you which: type 1's smallest
shipped coordinate is 2, so no record would be out of range either way. Where the
creature lands does tell you. Read as written, **none of the 268 shipped records
puts a creature inside solid terrain; read one tile up and left, 67 do.**

| step | what the arm does |
|---|---|
| owner | body `PlayerId` selects the player record at index `PlayerId - 1` (stride 4389 from `world+0x84`) and the **word at its `+0x04`, `TagId`**, is what the creature is created with — an indirection, not the byte itself |
| creation | `FUN_00519B20`. Failure returns 1 having done nothing |
| level | one-based, and applied **by repetition**: `Level - 1` calls to the level-up routine `0x0048B120`, which itself stops at 10. `CMP EAX,1; JBE` means 0 and 1 are the same request |
| flags | the body's `Flag` byte, bit for bit — below |
| collapsed | only when `Flag` bit 7 is set — below |

`FUN_00519B20` has a **Good-player-only** preamble: when the owner is the Good
player (`word[0x006F254C]`) and the target's room type is 19, 20 or 23, it moves
the spawn to a walkable neighbour picked at random (`WorldTrigger.cpp:1377`) and
afterwards orders the creature to walk to the tile the record asked for. Every
other owner reaches the factory `0x004B78D0` at the requested point directly.

### The `Flag` byte is the creature's own `Flags` byte

Eight one-line setters, and six of them write **`creature+0x46F`**, which DK2's
creature dumper calls `Flags` (`creature-object.md`) — at the *same* bit
positions. Bit 1 is never read. The other two are named fields:

| body bit | writes | shipped |
|---|---|---|
| 0 | `Flags` bit 0 **and** `+0x3EA` bit 7, **`DestroysRooms`** | all 268 |
| 2 | `+0x3EA` bit 11, **`DieInstantly`** | 22 |
| 3 | `Flags` bit 3 | all 268 |
| 5 | `Flags` bit 5 | 218 |
| 7 | `Flags` bit 7, and the collapsed arrival below | 11 |
| 4, 6 | `Flags` bits 4 and 6 | never |

The same two setters are called from action type 14's party creation
(`FUN_00519F90+0x5B4`/`+0x5E7`) and from a third site at `0x0052081F`, so the
byte's layout is shared rather than type 1's own.

### Bit 7 makes the creature arrive collapsed

The tail runs only when `Flags` bit 7 is set: clamp health down to
`maxHealthForLevel / 3` (`0x0048F1D0`, through `ModifyHealth`), then call
`0x0048B770`. That is the **same routine `ModifyHealth` calls at `0x004B2FB5`**
when a creature's health runs out and it is not to die outright — so bit 7
creates a creature already in the dying, rescuable state rather than standing.
The clamp-then-call idiom appears three more times, in the placed-things loader
at `0x0052040F`, `0x0052086C` and `0x00520B6B`.

Two field identifications fall out. **`creature+0x40` is Health**: `ModifyHealth`
writes it at that transition and the level-up routine reads it against the same
maximum. **`0x0048F1D0` is `maxHealthForLevel / 3`**, computed as
`((100 × mh) / 3) / 100` with both divisions floored.

### `DieInstantly` has exactly one reader

`0x0048E110`, its getter, is called from **one place in the binary**:
`ModifyHealth` at `0x004B2F8D`, in the branch that decides whether a creature
losing its last health gets the collapsed window or dies at once. It is consulted
**only when the creature is not in a room** (`+0x3B9` zero); standing in a room
of kind 11 or 12 skips the window instead. The getter also returns true
unconditionally for creature types 21, 28 and 29 — King Reginald, Stone
Knight and Lord Of The Land.

`CreatureDeathRules.HasDyingState` derives that window from the definition's
`GenerateDeadBody` flag instead. Those are different predicates and at least King
Reginald and Lord Of The Land are decided differently by them (Q303).

### What is built

ADR-0137 creates the creature — type, owner, zero-based tile, level and the
health that level carries. **All 268 records report partial**, and not because
the creature is missing: the `Flag` byte is dropped, and its bit 0 is
`DestroysRooms` on every one of them (Q302). The collapsed arrival, the `TagId`
indirection and the Good-player relocation are not modelled (Q303–Q305).

## What is implemented, and what is not

ADR-0121 built 46, 52, 71 and 73. Types 71 and 73 read player fields nothing
writes yet — the slap tally and the possessed creature — so both answer from
state rather than from a constant, and type 73 **declines** rather than guesses
the moment possession stops being zero. Whether `NumberOfSlaps` counts slaps
given or received is unread (Q278), and the possessed creature's `+0x488` is
unread (Q279).

Declined and why: 22/23 combat, 20 `creature+0x28A` (Q269), 27/28 missing door
and trap behaviour (Q281/Q284), 64 party spawning and party identity (Q283),
type 70's unused object branch (Q280), 2 the special timer id 16 (Q232).

**Rank what is left by `gates`, not by declines** (ADR-0123). Type 28 forecloses
18 records from 5 declines; type 64 forecloses 12 from 12. The whole remainder
is small: 53 declines account for 145 of all 9,161 records, 1.6% (ADR-0125), so
arms are no longer where reach comes from.
