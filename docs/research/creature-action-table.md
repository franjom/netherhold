# Mechanic: the creature action table — how an attack is entered

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-16. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- The reference install's `Text.str`, via `DK2.Data` — as a **negative** result

Original version: DK2 v1.70 (GOG)

Questions: **Q338** (answered here). Follows `combat.md` §6.0, which found the
melee attack registered into this table and could not say what it was.

**Nothing here is built.** It is what M7 was blocked on.

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
| `+0` | dword | **the handler**, installed for 24 of the 81 |
| `+4`, `+8`, `+12`, `+16` | dword | written, meaning UNKNOWN |
| `+20` | byte | a small id, 3 to `0x25`; §3 |
| `+21` | dword | written, meaning UNKNOWN |
| `+25` | word | **not** a `Text.str` id — §5 |
| `+27` | byte | 0 for all but four records |
| `+28` | byte | **1 in every record** |

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
this byte. Not tested, and not named on the strength of a shape.

## 4. Who starts what

Every call of `FUN_00496700` and its three wrappers was read for the literal
pushed before it: **109 sites** (26 direct, 83 through the wrappers), of which
**4** compute the index instead and are not in this map. **55** of the 81 records
are started by a literal.

| index | sites | where |
|---|---|---|
| 1 (`0x01`) | 6 | `0x00465679` `0x00485e7d` `0x00514fc9` +3 |
| 2 (`0x02`) | 2 | `0x00465a5d` `0x00485e45` |
| 3 (`0x03`) | 1 | `0x00456931` |
| 4 (`0x04`) | 2 | `0x00465d83` `0x00514ea9` |
| 5 (`0x05`) | 1 | `0x00465fbe` |
| 6 (`0x06`) | 1 | `0x00514ee0` |
| 7 (`0x07`) | 1 | `0x0046645b` |
| 8 (`0x08`) | 1 | `0x004667c0` |
| 9 (`0x09`) | 1 | `0x00458dba` |
| 10 (`0x0a`) | 1 | `0x00466220` |
| 11 (`0x0b`) | 3 | `0x0045845b` `0x004585ad` `0x00458661` |
| 13 (`0x0d`) | 1 | `0x00459002` |
| 15 (`0x0f`) | 1 | `0x00515394` |
| 16 (`0x10`) | 4 | `0x0051529f` `0x005152b8` `0x0045fb96` +1 |
| 17 (`0x11`) | 2 | `0x00514da4` `0x00460103` |
| 18 (`0x12`) | 1 | `0x0046f37f` |
| 19 (`0x13`) | 1 | `0x0046f21d` |
| 20 (`0x14`) | 1 | `0x00480fc7` |
| 22 (`0x16`) | 1 | `0x0048ba4f` |
| 24 (`0x18`) | 1 | `0x0046fc50` |
| 25 (`0x19`) | 1 | `0x00482ff7` |
| 26 (`0x1a`) | 5 | `0x004639d5` `0x00463bc2` `0x004738fc` +2 |
| 27 (`0x1b`) | 2 | `0x00459499` `0x00472e42` |
| 29 (`0x1d`) | 2 | `0x00475e00` `0x00475e4c` |
| 30 (`0x1e`) | 2 | `0x00485692` `0x00485794` |
| 31 (`0x1f`) | 1 | `0x0048571e` |
| 32 (`0x20`) | 1 | `0x0047053e` |
| 35 (`0x23`) | 9 | `0x00457d1a` `0x00456f1f` `0x00456e84` +6 |
| 36 (`0x24`) | 2 | `0x00484461` `0x004846d0` |
| 37 (`0x25`) | 2 | `0x004846fb` `0x00484ec4` |
| 40 (`0x28`) | 1 | `0x00478fdd` |
| 41 (`0x29`) | 1 | `0x0047351c` |
| 43 (`0x2b`) | 9 | `0x0047044c` `0x00475cb6` `0x00481d2a` +6 |
| 44 (`0x2c`) | 4 | `0x00457c2f` `0x0048554f` `0x00464305` +1 |
| 45 (`0x2d`) | 1 | `0x00474c56` |
| 46 (`0x2e`) | 3 | `0x00474865` `0x004743d6` `0x00474d08` |
| 47 (`0x2f`) | 1 | `0x004745e4` |
| 49 (`0x31`) | 2 | `0x004dcbe2` `0x004dd0c7` |
| 50 (`0x32`) | 1 | `0x00489572` |
| 52 (`0x34`) | 2 | `0x00481062` `0x00491855` |
| 53 (`0x35`) | 3 | `0x004811c8` `0x004813e2` `0x004815e2` |
| 54 (`0x36`) | 2 | `0x00474e5e` `0x00474f5b` |
| 55 (`0x37`) | 2 | `0x00461109` `0x004611b6` |
| 56 (`0x38`) | 1 | `0x00456d1e` |
| 57 (`0x39`) | 2 | `0x00456f02` `0x004bfb46` |
| 59 (`0x3b`) | 1 | `0x00474326` |
| 60 (`0x3c`) | 1 | `0x0046534b` |
| 61 (`0x3d`) | 1 | `0x00465415` |
| 62 (`0x3e`) | 1 | `0x0048b828` |
| 63 (`0x3f`) | 1 | `0x00483f94` |
| 64 (`0x40`) | 1 | `0x00481573` |
| 68 (`0x44`) | 1 | `0x00456460` |
| 76 (`0x4c`) | 1 | `0x00483aa2` |
| 79 (`0x4f`) | 1 | `0x00475976` |
| 80 (`0x50`) | 1 | `0x00483961` |

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

## 5. The `+25` word is not a label

`creature-state-table.md` recovered that table's labels because its `+0x14` word
is a `Text.str` id. The same move fails here: this table's `+25` values —
`0xC5`–`0xE1`, `0x109`, `0x10C`, `0x12D`, `0x334` — resolve in the install's own
`Text.str` to *Search For LAN Games*, *Refresh*, *Browse* and *Jelly*. That is
the multiplayer menu bank, so either the field is not a string id or it indexes a
different file. Recorded so the next reader does not spend the hour again.

## 6. What this leaves

- The enumeration has **no name** here, and 57 of the 81 records carry no
  handler. Whether those are unused or handled by their `+20` alone is UNKNOWN.
- Why index 21 is singled out is UNKNOWN.
- `+4`, `+8`, `+12`, `+16`, `+21`, `+25` are all written and none is read by any
  site found so far.

## Confidence

**CONFIRMED (binary):** the base, the 29-byte stride, the 81-record extent and
both of the facts that bound it; the index field and its `29n` arithmetic; the
null and `0x15` guards; the 34-byte action block and that `FUN_00496AA0` clears
it whole; that `FUN_00496700` begins an action from a stack argument; the 24
handler installs and the 24 call sites; that record 17's handler is the melee
attack and that `FUN_00514830` starts it with a literal `0x11`.

**CONFIRMED (negative):** `+25` is not a `Text.str` id.

**STRONGLY INFERRED:** that `+20` selects a creature animation block (§3; two actions checked by name).

**UNKNOWN:** the table's own name, the other six fields, index 21, and what the
57 handler-less records do.
