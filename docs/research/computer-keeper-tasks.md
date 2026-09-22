# The computer keeper's task dispatcher

**Scope:** `DKII.EXE` v1.70, MD5
`2f3bbc21eae8c3aa468136baa303036d`. This document follows the computer
keeper object named by DK2's debug dumper; see `computer-keeper-object.md`.
Addresses are virtual addresses in that executable. Behaviour below is stated
in our own words; no decompiler output is reproduced.

## 1. Per-turn entry point

**CONFIRMED from the binary:** `0x00500C20` advances one computer keeper. Its
call chain is:

1. The game-session update at `0x00526590` invokes the world update through
   virtual slot `+0x18`. The slot contains `0x0050A450` in the world vtable at
   `0x0066E3EC`.
2. `0x0050A450` is the world-turn routine (its own diagnostics say
   `GameTurn`). During an active turn it calls `0x004C2310` directly, without a
   counter or modulus gate.
3. `0x004C2310` walks the world's player list and calls `0x004B9EC0` once for
   each player.
4. `0x004B9EC0` calls `0x00500C20` when the player's computer-control byte at
   `+0x10FA` is nonzero and player status at `+0x19A` is neither 1 nor 3. It
   passes the computer-keeper pointer stored at player `+0x1101`.

Therefore an eligible computer keeper is advanced **once every game turn**,
not every N turns and not when a countdown expires. `0x00500C20` independently
rechecks statuses 1 and 3, then increments `TaskTimeCounter` at keeper `+0x04`
on every accepted call. If the debug-named `TimesOut` flag is set and the
counter exceeds 1,680, it clears both `Task` and `NextTask`; that timeout changes
task state but does not schedule the routine.

The entry point receives a per-turn work allowance from the byte DK2's dumper
calls `TendancySpeed` at keeper `+0x4E3`. Its sub-operations consume that
allowance, so one invocation may perform several small AI actions even though
the invocation cadence is one per turn.

## 2. `Task`

**CONFIRMED from DK2's own dumper and the binary:** `Task` is not a separate
field. It occupies bits 22-25 of the computer keeper's dword at `+0x00`:

```text
task = (keeper[+0x00] >> 22) & 0x0f
```

`0x00500C20` extracts that value, rejects values above 8, and dispatches through
a compiler jump table. This is not a record array like the creature-action
table: the id is implicit in the table index and each four-byte entry is a jump
target.

| id | table entry | switch arm | destination |
|---:|---:|---:|---|
| 0 | `0x0050127C` | `0x00501103` | inline idle/planning arm |
| 1 | `0x00501280` | `0x00501171` | calls `0x004FCE70` |
| 2 | `0x00501284` | `0x00501184` | calls `0x004FD030` |
| 3 | `0x00501288` | `0x00501197` | calls `0x004FD2B0` |
| 4 | `0x0050128C` | `0x005011AA` | inline arm |
| 5 | `0x00501290` | `0x005011FF` | calls `0x004FD660` |
| 6 | `0x00501294` | `0x0050120F` | calls `0x004FD750` |
| 7 | `0x00501298` | `0x0050121F` | calls `0x004FDC50` |
| 8 | `0x0050129C` | `0x0050123E` | stops this turn's task loop |

Thus the table base is `0x0050127C`, stride is 4, count is 9, id is the index,
and the stored field is the switch-arm address. The bounds check precedes the
table lookup. An out-of-range id is changed to 4 and processing stops; arm 8
also stops without calling a handler.

The immediate-mask sweep found these computer-keeper writers:

- `0x004F8360`, the object initialiser, clears `Task` and the adjacent
  `NextTask` field.
- The inline idle/planning path `0x004FC690` clears `Task`, starts id 1, or
  starts id 5 according to the planning result.
- Handlers `0x004FCE70`, `0x004FD030`, `0x004FD2B0` and `0x004FD750` finish or
  replace their current task by masked writes to the same dword.
- `0x00500C20` clears it on timeout and when inline id 4 completes, and changes
  an invalid id to 4.
- `0x004FDC50` reads `NextTask`; its relationship to `Task` is covered in §4.

The only other whole-binary hits for the same mask operate on creature offset
`+0x3E6` in the combat update and are unrelated. Apart from DK2's debug dumper,
the extracted four-bit `Task` value is read only by `0x00500C20`; task handlers
change it but are reached through that one switch.

## 3. Task ids

Not yet investigated.

## 4. `Task` and event tasks

Not yet investigated.

## What is left

- Name the supported task ids only where strings, assertions or field effects
  justify it.
- Read the three-entry event ring and its relationship to ordinary tasks.

## Confidence

- **CONFIRMED:** the addresses, call chain, cadence, field layout, jump-table
  contents, bounds check, and listed readers/writers are direct binary reads.
- **STRONGLY INFERRED:** none in the answered section.
- **HYPOTHESIS:** none.
- **UNKNOWN:** questions 2-4 and the semantic names of the player status values.
