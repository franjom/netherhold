---
systems: [creatures]
---
# Mechanic: the creature action table — how an attack is entered

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-16. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- The reference install's `Text.str`, via `DK2.Data` — as a **negative** result

Original version: DK2 v1.70 (GOG)

Questions: **Q338** (answered here). Follows `combat.md` §6.0, which found the
melee attack registered into this table and could not say what it was.

**What is built is counted by `dk2tools coverage actions`** (`CreatureActionCoverage`, one row per
action). Ours has no action table: each rule that mirrors a starting state plays the action's art
slot with a hold of its own, and `CreatureActionTable` keeps the `+25` sound words.

---

## 1. The table

**81 records of 29 (`0x1D`) bytes based at `0x006E4198`**, running to `0x006E4AC4`
inclusive, so indices **0–80**. It sits in the same uninitialised `.data` block
as the creature-state table and is filled by one straight-line run of `MOV`
instructions in `FUN_004950C0`, between `0x004950C6` and `0x00495E00`.

Two independent facts agree on the bound: the last byte the filler writes is
record 80's last byte exactly, and **the highest index any caller passes is
`0x50` = 80** (§4).

The record, recovered by walking the filler tracking register values:

| offset | width | what is known |
|---|---|---|
| `+0` | dword | **the handler**, installed for 32 of the 81 (§4): 29 routines, 16 and 31 sharing `0x00498870` and 36–38 `0x00499300` |
| `+4` | dword | the handler's `this` offset into the creature (§6); 0 in every record |
| `+8`, `+12` | dword | meaning UNKNOWN; `+8` is 0 in every record, `+12` is −1 exactly where `+0` is null, else 0 |
| `+16` | dword | the record's own index (§6) |
| `+20` | byte | a small id, 3 to `0x25`; §3 |
| `+21` | dword | the mode, read by the runner (§6) |
| `+25` | word | a sound id in the creature's bank, **not** a `Text.str` id — §5 |
| `+27` | byte | nonzero in six records: 0 (1), 20 (8), 25 (4), 33 (8), 34 (8), 42 (`0x30`); nonzero skips the animation start (§6) |
| `+28` | byte | **1 in every record**; the end-action test `0x00496D20` reads it, so every action but 21 can be ended |

## 2. It is indexed by the creature's current action

The index is `dword[creature+0x3A9]`, and the address arithmetic is why the
stride was hard to find: there is **no `IMUL` by 29 anywhere in the image**. The
compiler builds the index in three steps: `8n`, minus `n` to give `7n`, then
`n + 4 * 7n` = `29n` added to the table base `0x006E4198` in one address
computation.

**Index 0 is the null entry** — every reader guards with `SETNZ` on the field
before indexing — and **index `0x15` (21) is excluded by name** at both sites
that do so, for a reason this page cannot give.

`creature+0x3A9` is one field of a **34-byte block at `+0x38D`–`+0x3AE`** that
`FUN_00496AA0` clears as a unit: six dwords of parameters, the last-use stamp
`+0x3A5` (`combat-cadence.md` §4), the index `+0x3A9`, and two bytes. That block
is the creature's **current action**, and `+0x3A5`/`+0x3A9` are always written
together — *what* it is doing and *when* it started.

## 3. `FUN_00496700` begins an action

It takes the index as a stack argument, fills the parameter block, stamps
`+0x3A5` from the game turn, clears `+0x3AD`/`+0x3AE`, and sets
`creature+0x3EA` bit 12 (`PlayInstanceAnimation`) on the way in. Three thin
wrappers at `0x00496980`, `0x004969D0` and `0x00496A30` call it and then copy
two or three dwords into `+0x38D` — the same entry point with different argument
shapes.

**STRONGLY INFERRED (2026-09-28)**: `+20` selects which of the creature
record's ~45 `ArtResource` animation blocks to play, counting 84-byte blocks from
record offset `0x20`. Actions 1 and 2 hold 5, the Imp's block 5 is `Imp_Dig`;
action 49 holds 30, block 30 is every portal creature's Entrance
(`portal-creature-action.md`); action 41 holds 9, Angry (`0x00495C9C`, prison.md §6.3). The filler runs past `0x00495E00`: record 49 is
written at `0x00495E0A`–`0x00495EBD`. Formerly a hypothesis from the shape only. Its range stops at `0x25`,
and records 36, 37 and 38 share one handler (`0x00499300`) and differ only in
this byte. Not tested, and not named on the strength of a shape. The handler plays the Torture
Wheel's and Electric Chair's sounds ([record](functions/00499300.md)).

## 4. Who starts what

All 81 records were read on 2026-11-05 by replaying the filler's stores with its registers
tracked (every byte of the table is written; the values agree with every record read singly before).
**109 calls** reach `FUN_00496700` and its three wrappers (26 direct, 83 through the wrappers), three
of them the wrappers' own. Of the other 106, **102 push a literal index**, and six of those are shared
tails that a second path jumps into with a different index pushed (2 at `0x00514FC9`, 36 at `0x00484EC4`,
39 at `0x00456D1E`, 57 at `0x00456E84`, `0x0045775F` and `0x004633B7`), so 108 literals name 55
indices. **4 compute it**: state 148's table pick (0, 65, 66, 67 or 68, casino.md §5), the Combat Pit spectator's 69 or 70
(`0x004580A5`, combat-pit-spectators.md), and state 272's two {low, high, action} tables at
`0x00699F30` and `0x00699EB8` (71–78 on a `random(100)`). So **67 indices are begun and 14 are not**:
the null 0, the excluded 21, and 3, 12, 14, 23, 28, 33, 34, 38, 42, 48, 51 and 58. The only other
writers of the index, found by its offset `+0x3A9` (the runner `0x00496C09`/`0x00496C72`, `0x00496D68`,
`0x00496DAC`, `0x004997DE` in the torture handler) and block-relative (`0x004966B6` in the block clear
`0x004966A0`, called by the begin and the melee swing `0x00498FCF`), clear the block, so none of the 14 is
begun another way. The calls were found by a scan of every section for `E8`/`E9` targets and absolute
references to the four entry points: all 109 are in `.text`, none in `cseg` or a pointer table.

The first reading (2026-09-16) took the last literal pushed, which at three sites is the count, not
the index: it had action 1 at state 272's two calls and an action 3 at state 148's. Neither is so.

A starter is the state whose handler holds the call, or the routine named. Art is `+20`, mode `+21`,
sound `+25`:

| index | art | mode | handler | sound | begun by |
|---|---|---|---|---|---|
| 0 | 0 | 3 | — | — | none: the null action (state 148 passes 0 for no action) |
| 1 | 5 | 3 | `0x00496E00` | `0x334` | state 14 `0x00465679`; state 186 `0x00485e7d`; possession `0x00514fc9`; state 38 `0x0046773d` |
| 2 | 5 | 3 | `0x004971D0` | `0x109` | state 26 `0x00465a5d`; state 186 `0x00485e45`; possession `0x00514fc9` |
| 3 | 5 | 3 | `0x00484940` | — | **none** |
| 4 | 13 | 3 | `0x004974C0` | — | state 16 `0x00465d83`; possession `0x00514ea9` |
| 5 | 13 | 3 | `0x00497670` | `0xe5` | state 20 `0x00465fbe` |
| 6 | 13 | 0 | `0x004977F0` | `0xe5` | possession `0x00514ee0` |
| 7 | 12 | 3 | `0x00497960` | — | state 22 `0x0046645b` |
| 8 | 12 | 3 | `0x00497AC0` | `0x10c` | state 24 `0x004667c0` |
| 9 | 5 | 3 | `0x00498BB0` | `0x109` | state 185 `0x00458dba` |
| 10 | 13 | 3 | `0x00497BF0` | — | state 18 `0x00466220` |
| 11 | 5 | 3 | `0x00497DF0` | `0xd7` | state 180 `0x0045845b`; state 181 `0x004585ad`; state 267 `0x00458661` |
| 12 | 13 | 3 | `0x00498120` | — | **none** |
| 13 | 5 | 0 | `0x00498240` | — | state 206 `0x00459002` |
| 14 | 6 | 0 | `0x004982B0` | `0xd8` | **none** |
| 15 | 6 | 0 | `0x004984D0` | `0xd8` | possession `0x00515394` |
| 16 | 6 | 3 | `0x00498870` | `0xd8` | possession `0x0051529f` `0x005152b8`; state 200 `0x0045fb96` `0x00460042` |
| 17 | 5 | 3 | `0x00498E30` | `0xd7` | possession `0x00514da4`; state 200 `0x00460103` |
| 18 | 11 | 0 | `0x00499A40` | — | state 78 `0x0046f37f` |
| 19 | 4 | 3 | — | `0xc5` | state 77 `0x0046f21d` |
| 20 | 21 | 0 | — | `0xdb` | state 207 `0x00480fc7` |
| 21 | 4 | 0 | — | `0xdb` | **none**; excluded by the runner |
| 22 | 24 | 0 | — | — | the hand slap `0x0048ba4f` |
| 23 | 24 | 0 | — | `0xe6` | **none** |
| 24 | 12 | 0 | — | `0xce` | state 83 `0x0046fc50` |
| 25 | 9 | 0 | — | `0xde` | state 85 `0x00482ff7` |
| 26 | 13 | 0 | — | `0xcd` | state 93 `0x004639d5`; eating `0x00463bc2`; state 118 `0x004738fc`; the creature update `0x00489329`; possession `0x00514c27` |
| 27 | 23 | 0 | — | `0xd4` | state 74 `0x00459499`; state 104 `0x00472e42` |
| 28 | 23 | 0 | — | `0xe0` | **none** |
| 29 | 14 | 0 | — | `0xd1` | state 54 `0x00475e00` `0x00475e4c` |
| 30 | 5 | 3 | `0x00498CA0` | `0xd3` | state 111 `0x00485692` `0x00485794` |
| 31 | 6 | 3 | `0x00498870` | `0xd3` | state 111 `0x0048571e` |
| 32 | 22 | 3 | `0x00498D20` | `0xd2` | state 64 `0x0047053e` |
| 33 | 8 | 0 | — | `0xca` | **none** |
| 34 | 9 | 0 | — | `0xcc` | **none** |
| 35 | 8 | 0 | — | `0xca` | state 140 `0x00457d1a`; state 152 `0x00456f1f` `0x00456f95`; state 153 `0x00456e84`; state 158 `0x0045775f`; state 159 `0x004633b7`; state 68 `0x00470b88`; state 60 `0x00476258`; the celebration `0x00481694` |
| 36 | 17 | 0 | `0x00499300` | — | state 130 `0x00484461`; state 131 `0x004846d0`; state 136 `0x00484ec4` |
| 37 | 25 | 0 | `0x00499300` | — | state 131 `0x004846fb`; state 136 `0x00484ec4` |
| 38 | 26 | 0 | `0x00499300` | — | **none** |
| 39 | 19 | 0 | — | `0xe1` | state 146 `0x00456d1e` |
| 40 | 5 | 0 | `0x00499800` | `0xd7` | the Library's intruder watch (`0x00478F00`) `0x00478fdd` |
| 41 | 9 | 0 | — | `0xd0` | state 116 `0x0047351c` |
| 42 | 16 | 0 | — | `0xd0` | **none** |
| 43 | 20 | 3 | — | — | state 63 `0x0047044c`; state 53 `0x00475cb6`; state 250 `0x00481d2a`; state 198 `0x0045a218`; state 12 `0x004642cf`; job 16's start `0x0047526e`; the idle (`0x004776C0`, state 5 and others) `0x0047772a`; state 88 `0x00480fa3`; state 176 `0x00486dd8` |
| 44 | 31 | 3 | — | — | state 139 `0x00457c2f`; state 108 `0x0048554f`; state 12 `0x00464305`; the idle `0x004777b0` |
| 45 | 25 | 3 | `0x00498260` | `0xe6` | state 240 `0x00474c56` |
| 46 | 35 | 0 | — | `0xe6` | state 237 `0x00474865`; state 231 `0x004743d6`; state 242 `0x00474d08` |
| 47 | 23 | 0 | — | `0xe6` | state 233 `0x004745e4` |
| 48 | 26 | 0 | — | `0xe6` | **none** |
| 49 | 30 | 0 | — | — | portal arrival `0x004dcbe2`; special arrival `0x004dd0c7` |
| 50 | 23 | 0 | `0x00499840` | `0xd4` | the creature update `0x00489572` |
| 51 | 14 | 0 | — | `0xd1` | **none** |
| 52 | 24 | 0 | `0x00499880` | — | state 209 `0x00481062`; the stun `0x00491855` |
| 53 | 27 | 3 | — | — | state 255 `0x004811c8`; routine `0x004813C0` `0x004813e2`; state 263 `0x004815e2` |
| 54 | 32 | 0 | — | — | state 258 `0x00474e5e`; state 262 `0x00474f5b` |
| 55 | 35 | 0 | — | — | the victory arm `0x00461109`; state 259 `0x004611b6` |
| 56 | 37 | 0 | — | — | state 146 `0x00456d1e` |
| 57 | 28 | 0 | — | — | state 152 `0x00456f02`; state 153 `0x00456e84`; state 158 `0x0045775f`; state 159 `0x004633b7`; the win celebration `0x004bfb46` |
| 58 | 9 | 0 | — | `0x12d` | **none** |
| 59 | 3 | 0 | — | — | state 230 `0x00474326` |
| 60 | 32 | 3 | `0x00499B80` | — | the imp teleport `0x0046534b` |
| 61 | 32 | 3 | `0x00499BE0` | — | state 51 `0x00465415` |
| 62 | 7 | 3 | — | — | the collapse `0x0048b828` |
| 63 | 32 | 3 | `0x00499C20` | `0xcf` | state 128 `0x00483f94` |
| 64 | 24 | 3 | — | — | state 210 `0x00481573` |
| 65 | 14 | 0 | — | `0xd1` | state 148 (computed) |
| 66 | 8 | 0 | — | `0xca` | state 148 (computed) |
| 67 | 9 | 0 | — | `0xde` | state 148 (computed) |
| 68 | 20 | 0 | — | — | state 150 `0x00456460`; state 148 (computed) |
| 69 | 8 | 0 | — | `0xca` | state 143 (computed) |
| 70 | 20 | 0 | — | — | state 143 (computed) |
| 71 | 20 | 3 | — | — | state 272 (computed) |
| 72 | 31 | 3 | — | — | state 272 (computed) |
| 73 | 5 | 3 | — | — | state 272 (computed) |
| 74 | 9 | 3 | — | — | state 272 (computed) |
| 75 | 8 | 3 | — | — | state 272 (computed) |
| 76 | 28 | 3 | — | — | state 276 `0x00483aa2`; state 272 (computed) |
| 77 | 37 | 3 | — | — | state 272 (computed) |
| 78 | 19 | 3 | — | — | state 272 (computed) |
| 79 | 29 | 3 | — | — | state 251 `0x00475976` |
| 80 | 5 | 0 | `0x00499CA0` | `0xd7` | state 275 `0x00483961` |

Four begun actions cannot happen in the shipped game: 13, 20, 54 and 80 are begun only in states DK2
never enters (206, 207, 258 and 262, 275); 275 and 276 are the Crypt's, which is dead code that never
shipped (the owner, 2026-11-05; Q375), so 76 is reachable only through state 272.

Only three addresses matter for combat, and they are the subject of
`combat-entry.md`: **17** is the melee instance and **16** the spell instance,
each started from exactly two places — game action 26's handler, which is the
player possessing a creature (`game-actions.md` §3), and `spFighting` at
`0x0045f030`, which is the creature fighting by itself. Nothing else in the
executable starts either with a literal.

**So combat is entered by starting creature action 17**, not by a creature state
— but the *decision* is a creature state, 200 "engaging in battle", whose handler
is `spFighting`. `combat.md` §6.0's withdrawn hypothesis had the attack routine
itself in the 281-record state table at `0x006DA8B0`; the attack is in this
table and the state that reaches it is in that one.

That `FUN_00514830` starts both 16 and 17 is worth flagging: `combat.md` §3 names
it "the healing handler" from its `HpFromChicken` arm, and it also holds a Hero
Lair level setter (`creature-levels.md` §2.1). Three unrelated jobs in one
routine means **§3's name describes an arm, not the function** — and once 21 and
25 are known to be *leave possession* and *the possession stream*, the three stop
being unrelated (`game-actions.md` §3).

## 5. The `+25` word is a sound id, not a label

`creature-state-table.md` recovered that table's labels because its `+0x14` word
is a `Text.str` id. The same move fails here: this table's `+25` values —
`0xC5`–`0xE1`, `0x109`, `0x10C`, `0x12D`, `0x334` — resolve in the install's own
`Text.str` to *Search For LAN Games*, *Refresh*, *Browse* and *Jelly*. That is
the multiplayer menu bank, so either the field is not a string id or it indexes a
different file. Recorded so the next reader does not spend the hour again.

**It is a sound id** (read 2026-10-04). `FUN_00496700` reads it at `0x004968BC` and, unless the
animation's previous-turn phase is below its current phase and the current is nonzero (`0x004968AC`: a clip kept part way is silent), and the word is nonzero, calls the
object at `[0x0075B468]` through vtable `+0x20` with the creature's handle `+0x2E`, the
definition's bank `def+0x6E3`, the id and the position `+0x1A`, keeping the returned handle
in `+0x2E`. The other two users of the record lookup `FUN_00496DC0` (`0x0040C290`, on a turn
modulus, and `0x0045A200`) read only this word, for the same call. The call shape is
STRONGLY INFERRED; the words exist as `SFX.map` groups in the creature's category (`0x334`, `0x109` and `0x10C` in `creature_imp`), and the table is read in full in
[sound-effects-continuous.md section 2](sound-effects-continuous.md#2-the-action-table).

## 6. What this leaves

- The enumeration has **no name** here. 49 of the 81 records carry no handler: such an action
  runs no code of its own each turn, and the runner ends it by its mode, the `+27` age test and the
  animation ([0x00496AA0](functions/00496AA0.md)). Record 3's handler
  `0x00484940` returns 0, and nothing begins it.
- Why index 21 is singled out is UNKNOWN. The runner `0x00496AA0`, `0x00496D00` and
  `FUN_00496700` treat it as no action, no literal site starts it, and its record carries
  animation block 4, a hit recoil.
- The fields, re-read 2026-10-04: `+4` is the handler's `this` offset and `+21` the mode,
  both read by the runner (its record); `+16` is the record's own index (every literal store
  equals it; records 0–5 and 42–44 re-checked); `+25` is §5's sound id. `+8` and `+12`
  (−1 or 0) have no reader in the indexed operands of the table, the three callers of
  `FUN_00496DC0` and `0x004997A6`; the record pointers built in `FUN_00496700`
  (`0x00496750`) and `0x00496DCB` were not followed through every use. `+27` is not
  simply a duration (the runner compares it with the action's age, its record): a nonzero
  value makes `FUN_00496700` skip the animation start (`0x00496791`–`0x00496799`).

## 7. The handlers

All 29 routines are read (issue #8, 2026-11-05); each has its record. A handler runs at the head of
the creature's update while its action is current; returning 0 ends the work, and a mode-3 action
then waits for its counted animation too (`training-actions.md`). Most time themselves from the start
stamp `+0x3A5`.

| action | handler | what it does |
|---|---|---|
| 1 | [`0x00496E00`](functions/00496E00.md) | the Imp's dig: strikes the wall at the action point while it can be dug |
| 2 | [`0x004971D0`](functions/004971D0.md) | the Imp's mine: strikes the gold seam at the action point |
| 3 | [`0x00484940`](functions/00484940.md) | returns 0; nothing begins 3 |
| 4 | [`0x004974C0`](functions/004974C0.md) | the claim strike on a claimable tile |
| 5 | [`0x00497670`](functions/00497670.md) | the attack on another player's floor |
| 6 | [`0x004977F0`](functions/004977F0.md) | one floor attack, the possessed claim |
| 7 | [`0x00497960`](functions/00497960.md) | the reinforce strike |
| 8 | [`0x00497AC0`](functions/00497AC0.md) | the wall repair |
| 9 | [`0x00498BB0`](functions/00498BB0.md) | wall work at the action point, with a post-work breach check (state 185's starter not read; action unbuilt) |
| 10 | [`0x00497BF0`](functions/00497BF0.md) | one room conversion application |
| 11 | [`0x00497DF0`](functions/00497DF0.md) | one room attack application |
| 12 | [`0x00498120`](functions/00498120.md) | the room repair; nothing begins 12 |
| 13 | [`0x00498240`](functions/00498240.md) | holds six turns (the disarm attempt, state 206, which DK2 never enters) |
| 14 | [`0x004982B0`](functions/004982B0.md) | a spell shot after the spell's delay; nothing begins 14 |
| 15 | [`0x004984D0`](functions/004984D0.md) | the free-aimed spell |
| 16, 31 | [`0x00498870`](functions/00498870.md) | the targeted spell |
| 17 | [`0x00498E30`](functions/00498E30.md) | the melee swing |
| 18 | [`0x00499A40`](functions/00499A40.md) | a sound for the viewer's possessed creature |
| 30 | [`0x00498CA0`](functions/00498CA0.md) | the training hit |
| 32 | [`0x00498D20`](functions/00498D20.md) | the Anvil's forge sound after the work interval |
| 36, 37, 38 | [`0x00499300`](functions/00499300.md) | the torture device; nothing begins 38 |
| 40 | [`0x00499800`](functions/00499800.md) | turns to face the target |
| 45 | [`0x00498260`](functions/00498260.md) | after eight turns calls its live class-2 target's virtual `+0x20` (state 240's chicken; the effect is unread) |
| 50 | [`0x00499840`](functions/00499840.md) | a possessed creature's prayer: heals each turn while `+0x3E6` bit `0x2000` holds |
| 52 | [`0x00499880`](functions/00499880.md) | asks the clip to stop and holds while the state is 255, stunned |
| 60 | [`0x00499B80`](functions/00499B80.md) | the teleport: after three turns moves to the action point |
| 61 | [`0x00499BE0`](functions/00499BE0.md) | the teleport's arrival: after one turn sets `+0xF3` bit 0 |
| 63 | [`0x00499C20`](functions/00499C20.md) | the whip: after five turns spawns effect `0xED` at the victim |
| 80 | [`0x00499CA0`](functions/00499CA0.md) | the Crypt rest, by creature kind (anger drain, heal, gold, objects, effects); dead code |

The other 49 actions have no handler: the runner ends them by their mode, the `+27` age test and the
animation alone.

## Confidence

**STRONGLY INFERRED (binary):** the base, the 29-byte stride, the 81-record extent and
both of the facts that bound it; the index field and its `29n` arithmetic; the
null and `0x15` guards; the 34-byte action block and that `FUN_00496AA0` clears
it whole; that `FUN_00496700` begins an action from a stack argument; the 32
handler installs; the 109 begin calls and their indices (§4); that record 17's handler is the melee
attack and that `FUN_00514830` starts it with a literal `0x11`.

**STRONGLY INFERRED (negative):** `+25` is not a `Text.str` id; the 14 indices never begun (§4: a static scan of code and data, so a restored save is outside it).

**STRONGLY INFERRED (2026-10-04):** `+16` is the record's index; `+25` is passed to the creature's sound call (§5).

**STRONGLY INFERRED:** that `+20` selects a creature animation block (§3; two actions checked by name).

**UNKNOWN:** the table's own name, the meaning of `+8` and `+12`, and why index 21 is
excluded.
