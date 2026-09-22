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

Not yet investigated beyond the field location printed by DK2's dumper.

## 3. Task ids

Not yet investigated.

## 4. `Task` and event tasks

Not yet investigated.

## What is left

- Trace every reader and writer of the `Task` and `NextTask` bit fields.
- Establish whether the visible switch is the complete dispatcher and name its
  supported ids only where strings, assertions or field effects justify it.
- Read the three-entry event ring and its relationship to ordinary tasks.

## Confidence

- **CONFIRMED:** the addresses, direct call chain, player gates, once-per-world-
  turn cadence, `TaskTimeCounter` increment, 1,680-turn timeout, and work-
  allowance byte are direct reads of `DKII.EXE`.
- **STRONGLY INFERRED:** none in the answered section.
- **HYPOTHESIS:** none.
- **UNKNOWN:** questions 2-4 and the semantic names of the player status values.
