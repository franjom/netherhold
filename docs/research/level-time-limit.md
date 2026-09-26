# The level time limit, and trigger actions 9, 23 and 47

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-26.
- **Built:** ADR-0235, `TriggerState.LevelTimeLimit`, `TriggerState.LevelTimeUp`, `TriggerState.SpecialInfoNextRoomTypeId`.
- Related: `trigger-state.md` §5 (timer id 16), `trigger-dispatch-tables.md`, `win-and-defeat.md`.

## Result in one sentence

Action type 23 sets a countdown in game turns that the world counts down once a turn. The
"timer" conditions that name id 16 read it. When it runs out, a world bit is set and, in game
modes 2 and 3 only, every keeper loses.

## 1. Where the three actions go — CONFIRMED

The player action dispatcher `0x004C8620` (types 10–50, byte table `0x004C8A98`, jump table
`0x004C8A60`) has no arm for 9, 15, 23 or 47. Its default `0x004C8A3E` passes the record to the
world dispatcher `0x00518AD0` (byte table `0x00519930`, jump table `0x005198C4`). There:

| type | arm | what it does |
|---|---|---|
| 9 | `0x005191A3` | `Available` (byte 2) non-zero: `0x00510260(MakeType, TargetId, Value)`; zero: reset the block at `world+0x7291` |
| 15 | failure exit `0x00519893` | nothing at world level; the creature dispatcher handles it |
| 23 | `0x0051916F` | `world+0x72AA := Value` (dword 4); the timer id byte is not read |
| 47 | `0x005197B1` | world slot `+0x378` = `0x005098C0`: `world+0x72BC := TargetId` (byte 0) |

## 2. `LevelTimeLimit`, `world+0x72AA` — CONFIRMED

- **The name** is DK2's own: the world dumper prints it at `0x0050ED6A` (`LevelTimeLimit %d`).
- **The countdown**, in the per-turn world update `0x0050A450`, at `0x0050A64E`:
  - a limit of zero is left alone;
  - otherwise it drops by one;
  - on reaching zero, bit `0x08` of the world options `world+0x72A1` is set.

  The countdown is skipped while `[[world+4]+0x284]` is non-zero, which is **UNKNOWN**.
- **Running out** (`0x0050A67A`). Only when the game mode `[0x0075AD4B]` is 2 or 3: for every
  player in the world list whose `Number` (`+0x10FC`) is 3 or more, `0x004BE630(3)`, the defeat
  verdict (`win-and-defeat.md`), then an event through the world's handler table at `+0x1684`.
  In single player (mode 1) only the bit is set.
- **The condition.** Type 2 with timer id 16 (`0x005184EB`) reads it through slot `+0x34C`
  (`0x00509660`, `MOV EAX,[ECX+0x72AA]`) and compares it with the raw `Value` dword under the
  record's comparison code. There is no started test, no seconds scale and no flag test.
- **The other writers** are the world initialiser `0x005099A0` (`0x00509BFE`) and the level
  setup `0x0050C800` (`0x0050C9BC`). The values they store are not read here. Besides slot
  `+0x34C`, it is read by the world dumper and by `0x0050F3D0` (`0x0050F567`), which is also
  not read.

## 3. `SpecialInfoNextRoomTypeId`, `world+0x72BC` — CONFIRMED

Named by the world dumper at `0x0050F102`. Its readers:
- the getter `0x005098B0`, world slot `+0x374`, which is called only from the interface at
  `0x00427BA0`;
- the world dumper;
- `0x0050F3D0` (`0x0050F4FF`), whose role is not read.

The only reader whose role is known is the display.

## 4. Action 9 is interface state — STRONGLY INFERRED

`0x00510260` stores `MakeType`, `TargetId` and `Value` at `world+0x7291..0x7296`, and at
`+0x7297` a stamp from `0x005B5E90`. The world update clears the block once that clock passes
`stamp + Value × 1000` (`0x0050A61D`). The × 1000 reads as milliseconds, so `0x005B5E90` is taken
to be a wall clock and `Value` real seconds; `0x005B5E90` itself is not read. A deterministic
simulation cannot hold a wall clock. What the block drives is **UNKNOWN**; its readers are world
slots near `0x005096F0`.

## 5. Ours

- `TriggerState.LevelTimeLimit` is set by action 23. `GameSimulation.Tick` counts it down right
  after the trigger run, emitting `level_time_up` at zero, and `LevelTimeUp` is the world bit.
  Where DK2 counts it down relative to the trigger walk inside `0x0050A450` is not pinned.
- `SpecialInfoNextRoomTypeId` is set by action 47.
- Both are hashed and snapshotted (`HashVersion` 122).
- **Not built:**
  - the defeat on running out, since game modes are not modelled;
  - the `+0x284` gate;
  - action 9, which stays partial;
  - action 15 in a creature's tree.
