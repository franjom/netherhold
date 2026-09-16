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
compiler emits

```
LEA ECX,[EAX*0x8 + 0x0]
SUB ECX,EAX                              ; ECX = 7n
LEA EAX,[EAX + ECX*0x4 + 0x6e4198]       ; n + 28n = 29n
```

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

**HYPOTHESIS, from the shape only**: `+20` selects which of the creature
record's ~45 `ArtResource` animation blocks to play. Its range stops at `0x25`,
and records 36, 37 and 38 share one handler (`0x00499300`) and differ only in
this byte. Not tested, and not named on the strength of a shape.

## 4. Who starts what

Twenty-four call sites across the game pass a literal. The ones whose caller this
project has already identified:

| index | started by | what that routine is |
|---|---|---|
| `0x10` (16) | `FUN_00514830` | `combat.md` §3 |
| **`0x11` (17)** | `FUN_00514830` | **the melee attack** — record 17's handler is `0x00498E30` |
| `0x28` (40) | `FUN_00478F00` | the prison cluster |
| `0x4C`, `0x50` | `0x00483961`, `0x00483AA2` | the torture cluster |

**So combat is entered by starting creature action 17**, not by a creature state.
`combat.md` §6.0's withdrawn hypothesis had it in the 281-record, 25-byte state
table at `0x006DA8B0`; it is in this one.

That `FUN_00514830` starts both 16 and 17 is worth flagging: `combat.md` §3 names
it "the healing handler" from its `HpFromChicken` arm, and it also holds a Hero
Lair level setter (`creature-levels.md` §2.1). Three unrelated jobs in one
routine means **§3's name describes an arm, not the function**.

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

**HYPOTHESIS:** that `+20` selects a creature animation block (§3).

**UNKNOWN:** the table's own name, the other six fields, index 21, and what the
57 handler-less records do.
