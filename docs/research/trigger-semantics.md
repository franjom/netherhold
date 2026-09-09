# TRIGGERS parameter reads in DKII.EXE v1.70

**Scope:** the two 16-byte TRIGGERS records in the shipped v1.70 editor data.
This is a static-data reading. It does not specify when a trigger fires or add
trigger execution to the reimplementation.

## Summary of confidence

| Claim | Confidence | Basis |
|---|---|---|
| The shipped corpus has 3,753 generic records (tag 213, 46 types) and 5,408 action records (tag 214, 38 types) | **Known** | Exhaustive read of all `*Triggers.kld` files; `dk2tools kwd trigger-types` reports counts and level membership |
| The file's 16 bytes are copied unchanged into a 20-byte runtime record | **Strongly inferred** | Loader disassembly at `0x00508600` and `0x00508810`; the extra runtime dword is state, not file data |
| Type is read at record offset `0x0E` by the dispatch family below | **Strongly inferred** | Direct disassembly of every listed dispatcher and its jump/branch table |
| Parameter offsets, widths and signedness marked **B** or **C** below | **Strongly inferred** | Direct instruction reads and argument flow in DKII.EXE |
| Names marked **T** | **Strongly inferred as layouts; semantic name unverified** | Name remains a transcription from OpenKeeper because the binary read does not settle its role |
| Bytes marked **N** | **Strongly inferred as unreached by the handler** | No instruction in that type's dispatch path reads the byte; the complete dispatcher family was checked |

`Confirmed` is deliberately not used: no original-game observation was made.

## Re-checking the shipped scope

Run:

```text
dk2tools kwd trigger-types
```

The command prints one row per tag/type pair, its count, and the complete list
of levels containing it. The 2026-09-08 count distribution was:

```text
tag 213: 1:1375 2:303 3:22 4:37 6:158 8:88 10:4 12:2 13:4 16:30
         19:205 20:2 22:26 23:1 24:24 25:85 27:3 28:7 29:80 30:11
         31:9 32:1 34:325 36:40 39:183 40:92 41:69 42:89 44:13 46:1
         47:72 52:3 53:159 56:1 57:1 58:1 60:1 62:32 64:37 65:1
         68:5 69:5 70:6 71:2 72:137 73:1
tag 214: 1:268 2:5 4:1 6:303 7:1417 8:291 9:78 10:40 11:47 14:259
         15:565 16:109 17:291 18:27 20:16 21:2 22:160 23:86 24:827
         26:100 27:18 28:10 30:15 31:60 32:67 33:72 34:2 35:27
         37:22 38:34 42:59 43:7 44:2 45:18 47:84 49:3 50:3 51:13
```

That is 9,161 records and 84 observed tag/type combinations. The install-gated
test pins every count; the command preserves the more useful level-by-level
enumeration without hard-coding level ids into the reader.

## Dispatcher location and shape

The root tree evaluator is at `0x00518150`. It resolves generic and action
records through lookups at `0x00518090` and `0x005180F0`, respectively. The
world-level generic dispatcher is `0x00518270`; the world-level action
dispatcher is `0x00518AD0`. Each reads byte `+0x0E` and dispatches through a
jump table or an equivalent compact branch table.

Those two functions intentionally delegate or reject context-owned types. The
complete dispatch family is:

| context | generic | action |
|---|---:|---:|
| world | `0x00518270` | `0x00518AD0` |
| player | `0x004C7B20` | `0x004C8620` |
| creature | `0x0049AAA0` | `0x0049AC80` |
| action point / area | `0x004B0D70` | delegates to world action |

The event filters at `0x004C7A50` and `0x0049AA30` account for types whose
occurrence is the condition: they match a record's type byte against an event id
and run its children without evaluating it, so its eight-byte body is never read.
Treating the world switch's default arm as proof that such a type was unsupported
would therefore be wrong. Which code walks which tree, and how often, is
`trigger-walkers.md`.

**The world context computes only nine condition types.** Resolving the
dispatcher's two-level table for all 67 in range gives distinct arms for types
**1, 2, 34, 35, 43, 44, 45, 64 and 67** only; **46 returns 0 without even
logging**, and the other 57 share the default arm at `0x00518929`. A world-tree
record of any other type fails, whatever its parameters say. Types owned by the
player, creature or area contexts are reached by *their* walkers, not this one —
which is the caveat above, restated with the table filled in.

**Type 34 is level time, and it is how a level bootstraps.** Its arm at
`0x0051829D` takes byte `00` as the comparison, the dword at `+4`, and calls
`FUN_00517490` — the game-turn accessor (`trigger-state.md` §6). The loader
scales that dword by variable 19 (Q233), so the authored value is **seconds
since the level began**. 325 records ship it. Campaign level 1's world root is
trigger 89: type 34, comparison 5 (`>=`), value 0 — true on turn 1 — with
`next = 0` and two type-14 children.

**Corrected 2026-09-09: the level does not hang off that node.** Trigger 89's
whole chain is those two party spawns. The chain that runs the level —
1 → 10 → 15 → 31, carrying the timers and flags the live captures show moving —
hangs off trigger **1**, which is level 1's single **player** root, walked by
`FUN_004C79A0` and not by the world walker at all. Implementing type 34 was
still necessary and it is still what unblocks trigger 89; it is simply not
sufficient, and the earlier reading of the first two-sided capture — one blocked
type-34 record, therefore one missing rule — mistook the world tree for the
level. Levels distribute their script across four walkers, and this one keeps
almost none of it here.

**A dispatcher's range check is a size, not a top type.** Each one does
`LEA reg,[type - base]` and then `CMP reg, size`, so the type it accepts runs
from the base to `base + size`: world **1..67** (`0x00518282`), creature
**3..65** (`0x0049AAAD`), player **19..74** (`0x004C7B30`), area **39..70**
(`0x004B0D7F`). Reading those `CMP` immediates as top types is wrong and was
wrong here until 2026-09-09; it is what produced the claim that no context
reaches a type-72 arm.

**Out of range does not mean unhandled** — it means the next dispatcher. Each
default arm delegates: player and area to the world (`0x004C856C`,
`0x004B0EC9`), and **creature to the player** with the creature's owning player
as `this` (`0x0049AC08`). Only the world's default gives up, formatting
`Unhandled Trigger When [ Type %d, Index %d ]` and returning **0**
(`0x00518929`). So the fallback chain is creature → player → world, and a type is
unhandled only when it survives to the end of it.

**Type 72 therefore holds in a player tree.** The player context owns it and
computes it (`0x004C84B8`); the world, creature and area contexts do not, so it
fails there. All **137** shipped records carry an empty body, both dwords zero,
which is consistent with a condition that reads no parameters. Level 1 holds 49,
all in its player tree, where declining them costs real divergence — see
`trigger-walkers.md` for what the arm tests and Q242 for what is still unknown
about it.

The common comparison routine at `0x00519980` implements codes 1 through 6 as
signed `<`, `<=`, `==`, `>`, `>=`, and `!=`. Consequently generic comparison
values at `+0x04` are `s32`, not `u32`.

## The area context

Every one of the seven types `0x004B0D70` owns measures something over the
action point's own **inclusive** rectangle and hands the result to `0x00519980`.
Three of the seven do not read byte 00 as a comparison at all; their arm supplies
a literal, which is why the parameter table below marks that byte unread.

| type | left-hand quantity | comparison | right-hand side |
|---|---|---|---|
| 39 | things in the rectangle passing `FUN_004B0A80` | byte 00 | `Value` |
| 40, 42 | tiles matching `PlayerId` and `TerrainId` (`FUN_004B07E0`) | byte 00 | `Value` |
| 41 | the same count | literal `==` | the rectangle's own tile count |
| 62 | tiles carrying `PlayerId`'s excavation task (`FUN_004B0960`) | byte 00 | `Value` |
| 69 | the same count | literal `>=` | tiles whose terrain is taggable (`FUN_004B0A00`) |
| 70 | **existence** of one thing, not a count (`FUN_004B06D0`) | — | — |

40 and 42 share one arm, so **they are the same condition**; nothing in the
dispatcher distinguishes them.

`FUN_004B07E0` treats zero as "any" on each axis, and both zero short-circuits to
the rectangle's area without reading a tile. With a terrain type named, ownership
is decided by the tile's owner alone; with none named, the tile must also carry
terrain flag `0x04`, our `Ownable`. That asymmetry is the original's.

The task pool behind 62 and 69 is per player, holds up to 500 entries, and is
keyed by coordinate. `FUN_00512D20` puts one entry in it per tile the player
marks, choosing kind 1 or kind 2 by whether the terrain carries gold; **62 and 69
count both**, so the split does not reach either condition and one tag bit per
player per tile is the same quantity. `FUN_004B0A00` tests terrain flag `0x08`,
our `Taggable`, which is the same flag `FUN_00512D20` requires before it will
create the entry — so 69 asks whether the player has marked everything here that
can be marked.

39 and 70 count and test **things standing in the rectangle**, filtered by a
category dword at thing `+0x12` and then by `FUN_004B0A80`. Neither is
implemented; what that filter reads is UNKNOWN.

## What an arm returns, and what it does not decide

**The world action dispatcher (`0x00518AD0`) has exactly two exits.** `0x0051988C`
is `MOV EAX,1`; `0x00519893` formats `Unhandled Trigger When [ Type %d, Index
%d ]` and returns 0. Which one a type reaches is fixed by the jump table, not by
what the arm does, so "does DK2 succeed" has a static answer per type:

| | action types |
|---|---|
| world dispatcher **succeeds** | 1, 2, 4, 5, 6, 7, 8, 9, 14, 16, 17, 18, 22, 23, 25, 32, 39, 40, 41, 44, 45, 46, 47, 48, 49, 51 |
| world dispatcher **fails**, printing the message | 3, 10, 11, 12, 13, 15, 19, 20, 21, 24, 26, 27, 28, 29, 30, 31, 33, 34, 35, 36, 37, 38, 42, 43, 50 |

The failure column is mostly not failure. Thirteen of those types belong to the
player dispatcher and ten to the creature dispatcher, each reaching its own
`MOV EAX,1` before delegation is ever considered (`trigger-walkers.md`, the
context table). Subtract both and **only types 3, 12, 13 and 19 are refused
everywhere — and no shipped level authors one.** Two arms were read end to end
to check the rule rather than assume it: world type 17 (`0x00519257`) iterates
its action point's rectangle and finishes `MOV EAX,1`, jumping to the same exit
when the point does not resolve; player type 24 (`0x004C8688`) queues a message
of kind `0x15` and finishes the same way. Type 5's world arm and type 34's
player arm (`0x004C8A37`) *are* the success exit: they do nothing and succeed.

### What the return value does not decide

A walk descends into `ChildTriggerId` and **every one of the 5,408 shipped
action records has zero there.** Actions are leaves; only conditions branch.
So an action's return value cannot change where any shipped walk goes, and
un-blocking every action type in this executor moved no flag, no timer and not
one blocked condition across all 66 levels (`docs/EXPERIMENTS.md`, 2026-09-09).

It is still the answer to give, for two reasons that are not about reach. The
walker retires a successful action under an inherited repeat of 1, so declining
one leaves an active bit set that DK2 clears. And it keeps the census honest:
what an executor cannot do is then exactly what it reports.

### The asymmetry that remains

**A condition is the opposite case.** Declining one is indistinguishable from it
being false, so it costs nothing *until* the condition would have been true, and
nothing short of implementing it can say which. Conditions are also the only
records that branch. That is where the campaign's script actually stalls, and
`docs/EXPERIMENTS.md` has the census.

## Parameter provenance

Offsets are body-relative. `u8`, `u16`, `s16`, `u32`, and `s32` are the exact
loads performed by the executable. Status suffixes mean:

- **B** — the binary agrees with and establishes the name;
- **C** — corrected from the previous external-reader name;
- **T** — the binary establishes the access but not the inherited name;
- **U** — the binary reads the field, but no semantic name is claimed;
- **N** — the handler does not read these bytes. The reader exposes them as
  `Unreached_<offset>` only to keep every source byte visible.

### Generic records, tag 213

Every shipped type is in this table. Types sharing an identical access pattern
are grouped; the count beside each type is its shipped record count.

| types (count) | body reads and provenance |
|---|---|
| 1 (1375) | `Comparison:u8@00` **B**, `TargetId:u8@01` **T**, `Flag:u8@02` **T**, `FlagId:u8@03` **B**, `Value:s32@04` **B** |
| 2 (303) | `Comparison:u8@00` **B**, `TimerId:u8@01` **C**, `Flag:u8@02` **T**, `IndirectTimerId:u8@03` **C**, `Value:s32@04` **B** — *corrected 2026-09-08: byte 01 is the timer the condition tests (`CMP DL,0x10` at `0x005184EB`, then indexed at `0x0051851A`); byte 03 is the indirect operand's timer, read only in indirect mode and 0 in all 303 shipped records. See `trigger-state.md` §5.* |
| 3 (22), 4 (37), 6 (158), 8 (88), 10 (4), 12 (2), 13 (4), 16 (30), 46 (1), 56 (1), 57 (1), 58 (1), 60 (1), 65 (1), 72 (137) | body `00..07` **N** |
| 19 (205), 20 (2) | `Comparison:u8@00` **B**, `CreatureId:u8@01` **B**, `Value:s32@04` **B**; `02..03` **N** |
| 22 (26) | `Comparison:u8@00` **B**, `PlayerId:u8@03` **B**, `Value:s32@04` **B**; `01..02` **N** |
| 23 (1) | `Comparison:u8@00` **B**, `Unknown:u8@02` **U**, `Value:s32@04` **B**; `01,03` **N** |
| 24 (24), 25 (85), 68 (5) | `Comparison:u8@00` **B**, `RoomId:u8@01` **B**, `Value:s32@04` **B**; `02..03` **N** |
| 27 (3), 28 (7) | `Comparison:u8@00` **B**, `TargetId:u8@01` **T**, `Flag:u8@02` **T**, `Value:s32@04` **B**; `03` **N** |
| 29 (80) | `TargetId:u8@01` **T**; all other body bytes **N** |
| 30 (11), 31 (9), 32 (1), 34 (325), 36 (40), 71 (2), 73 (1) | `Comparison:u8@00` **B**, `Value:s32@04` **B**; `01..03` **N** |
| 39 (183) | `Comparison:u8@00` **B**, `TargetId:u8@01` **C**, `TargetType:u8@02` **C**, `PlayerId:u8@03` **C**, `Value:s32@04` **B** |
| 40 (92), 42 (89) | `Comparison:u8@00` **B**, `PlayerId:u8@01` **B**, `TerrainId:u8@02` **B**, `Value:s32@04` **B**; `03` **N** |
| 41 (69) | `PlayerId:u8@01` **B**, `TerrainId:u8@02` **B**; `00,03..07` **N** |
| 44 (13), 64 (37) | `Comparison:u8@00` **B**, `PlayerId:u8@01` **C**, `Value:s32@04` **B**; `02..03` **N** |
| 47 (72) | `CreatureId:u8@00` **C**; `01..07` **N** |
| 52 (3) | `Comparison:u8@00` **B**, `Level:u8@03` **C**, `Value:s32@04` **B**; `01..02` **N** |
| 53 (159) | `TargetType:u8@00` **T**, `TargetId:u8@01` **T**; `02..07` **N** |
| 62 (32) | `Comparison:u8@00` **B**, `PlayerId:u8@01` **B**, `Value:s32@04` **B**; `02..03` **N** |
| 69 (5) | `PlayerId:u8@01` **B**; all other body bytes **N** |
| 70 (6) | `TargetId:u8@01` **C**, `TargetType:u8@02` **C**, `PlayerId:u8@03` **C**; `00,04..07` **N** |

The parser also has cases for unshipped generic types. The binary reads type 21
like 19/20; type 26 like 24/25/68; type 35 as comparison, creature id and
signed value; types 37, 49 and 67 as comparison and signed value; type 45 like
44/64; and type 74 as comparison, creature id and signed value. Types 33, 43,
59 and 61 have no body read on their event paths. These are binary readings but
have **no shipped-data corroboration**; Q225 keeps custom-map verification open.

### Action records, tag 214

| type (count) | body reads and provenance |
|---|---|
| 1 (268) | `CreatureId:u8@00`, `PlayerId:u8@01`, `Level:u8@02`, `Flag:u8@03`, `PosX:u16@04`, `PosY:u16@06` — all **B** |
| 2 (5) | `ObjectId:u8@00`, `PlayerId:u8@01`, `PosX:u16@04`, `PosY:u16@06` — all **B**; `02..03` **N** |
| 4 (1) | `ObjectiveId:u16@00` **C**, `ActionPointId:u8@04` **B**; `02..03,05..07` **N** |
| 6 (303) | `PlayerId:u8@00` **B**, `MakeType:u8@01` **T**, `TargetId:u8@02` **T**, `Available:u8@03` **B**; `04..07` **N** |
| 7 (1417) | `FlagId:u8@00` **B**, `Flag:u8@01` **T**, `Value:u32@04` **B**; `02..03` **N** |
| 8 (291) | `TimerId:u8@00` **B**; `01..07` **N** |
| 9 (78) | `MakeType:u8@00` **T**, `TargetId:u8@01` **T**, `Available:u8@02` **B**, `Value:u32@04` **B**; `03` **N** |
| 10 (40), 11 (47), 20 (16), 21 (2), 43 (7) | body `00..07` **N** |
| 14 (259) | `PartyId:u8@00`, `PartyType:u8@01`, `ActionPointId:u8@04` — all **B**; `02..03,05..07` **N** |
| 15 (565) | `PlayerId:u8@00`, `ObjectiveType:u8@01`, `ActionPointId:u32@04` — all **B**; `02..03` **N** |
| 16 (109) | `ActionPointId:u8@00`, `Available:u8@01`, `Value:s16@04` — all **B**; `02..03,06..07` **N** |
| 17 (291) | `ActionPointId:u8@00`, `Available:u8@01` — both **B**; `02..07` **N** |
| 18 (27) | `PlayerOneId:u8@00`, `PlayerTwoId:u8@01`, `Available:u8@02` — all **B**; `03..07` **N** |
| 22 (160) | `TerrainId:u8@00`, `PlayerId:u8@01`, `PosX:u16@04`, `PosY:u16@06` — all **B**; `02..03` **N** |
| 23 (86) | `TimerId:u8@00` **B**, `Value:u32@04` **B**; `01..03` **N** |
| 24 (827) | `SpeechId:u32@00` **B**, `Unknown:u32@04` **U** |
| 26 (100) | `ActionPointId:u8@00` **C**; `01..07` **N** |
| 27 (18) | `ActionPointId:u8@00`, `Available:u8@01`, `Angle:s16@02`, `Time:u32@04` — all **B** |
| 28 (10) | `CreatureId:u8@00`, `Level:u8@01` — both **B**; `02..07` **N** |
| 30 (15) | `Value:u32@00` **T**; `04..07` **N** |
| 31 (60) | `PathId:u8@00`, `ActionPointId:u8@01`, `Available:u8@02` — all **B**; `03..07` **N** |
| 32 (67) | `PosX:u16@04`, `PosY:u16@06` — both **B**; `00..03` **N** |
| 33 (72), 37 (22), 38 (34), 44 (2), 45 (18), 51 (13) | `Available:u8@00` **B**; `01..07` **N** |
| 34 (2) | body `00..07` **N** |
| 35 (27) | `Unknown:u8@00` **U**; `01..07` **N** |
| 42 (59), 47 (84) | `TargetId:u8@00` **T**; `01..07` **N** |
| 49 (3) | `PlayerId:u8@01`, `PosX:u16@04`, `PosY:u16@06` — all **B**; `00,02..03` **N** |
| 50 (3) | `Value:u32@04` **T**; `00..03` **N** |

For unshipped action types, 3, 5, 25 and 29 have no body read. Type 36 reads
only `Available:u8@00`; types 39/40 read an unknown byte at `00`, player id at
`01`, and two coordinates at `04/06`; type 41 reads an unknown word at `00`
and the coordinates; type 46 reads availability and the coordinates; type 48
reads generator id and availability. These cases are binary-read but lack a
retail corpus example; Q225 records the verification method.

## Corrections to the previous reader

The previous parameter dictionary treated the external field list as if it
were evidence. Direct binary reads showed these claims false:

- all generic comparison values were exposed as unsigned; DK2 compares them as
  signed 32-bit integers;
- generic type 39's three selector bytes were shifted/misnamed: the binary uses
  target id at `01`, target type at `02`, and player id at `03`;
- generic type 47's byte `00` is a creature id, not a player id, and its old
  `Value` is never read;
- generic type 52's byte `03` is a level, not a player id;
- generic type 70 had the same shifted selector labels as type 39, but reads no
  comparison or value at all;
- action type 4 reads a 16-bit objective id, not a 32-bit one;
- action type 8 reads only its timer id; the old `Value` is untouched;
- action type 16 reads a signed 16-bit value at `04`, not a 32-bit value;
- action type 26 resolves byte `00` through the action-point lookup, not a
  generic target lookup;
- action type 27 reads its angle as signed 16-bit;
- action type 24 reads the old `Text`, `Introduction`, and `PathId` bytes as one
  dword whose meaning is not yet established; those three names were false;
- action types 3 and 5 do not read their former `ActionPointId` (nor type 5's
  `InformationId`), type 25 does not read its former `TextId`, and type 34 does
  not read its former `Value`;
- the generic event types 56–61 do not read their former player, creature, or
  room ids, and generic type 3 does not read its former `Value`;
- numerous padding/residue fields previously named `Flag`, `PlayerId`, `Value`,
  `TextId`, or `ActionPointId` are not read by their handlers. They are now
  explicitly `Unreached_*` rather than retaining an authoritative-looking name.

## Names still resting on the external list

The **T** entries are the complete remaining list: generic `TargetId` and
`Flag` for types 1/2, generic `TargetId`/`Flag` for 27/28, generic `TargetId`
for 29, generic `TargetType`/`TargetId` for 53; action `MakeType`/`TargetId` for
6 and 9, action `Flag` for 7, action `Value` for 30 and 50, and action
`TargetId` for 42 and 47. Their offsets and widths are binary readings; their
semantic names are not.

## Provenance and method controls

- Primary binary: retail v1.70 `DKII.EXE`; facts above were transcribed in our
  own words from instruction and call-flow reads. No decompiler output entered
  the repository.
- Corroboration: all shipped `Data/editor/maps/*Triggers.kld` records.
- The scan walked every initialized memory block, including `.text` and `cseg`
  (`0x00653000`–`0x0066BFFF`). In the same run, the required positive control
  at `0x006F2590` produced 226 references.
- External comparison: OpenKeeper's GPLv3 field list, as already attributed by
  ADR-0091. It supplied navigation labels only; no implementation was copied.
