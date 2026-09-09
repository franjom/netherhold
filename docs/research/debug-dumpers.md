# DKII.EXE's own field names (Q264)

DK2 ships eleven debug dumpers: functions that write an object to a log, one
`printf` per field, **with the field's name in the format string**. They are the
strongest evidence this project has short of running the game — names the
original's authors wrote, not inferences from behaviour, editor labels or
community documents.

This is the index and the method. Each object's fields live in its own doc.

## The eleven

| dumper | fields | object | doc |
|---|---:|---|---|
| `0x00490272` | 199 | creature | `creature-object.md` |
| `0x004BCF2F` | 133 | player | `player-object.md` |
| `0x004E70B6` | 122 | room instance | `room-object.md` |
| `0x00503F61` | 122 | computer keeper | `computer-keeper-object.md` |
| `0x0049D336` | 77 | placed object | `object-thing-object.md` |
| `0x0050EAE9` | 74 | world | `world-object.md` |
| `0x0047920D` | 64 | a creature's job and needs | `creature-job-object.md` |
| `0x004D5C0F` | 30 | steering body | below |
| `0x005070FE` | 29 | research list and level statistics | below |
| `0x005083C5` | 21 | imp task pool | below |
| `0x00526292` | 26 | *not an object* — a call-count profiler | — |

## How to read another one

1. `CallScanAll.py <out> 005595c0` lists all 1,144 call sites of the log
   `printf`. Cluster them: consecutive sites less than `0x120` apart are one
   dumper, and any cluster of twenty or more is worth reading.
2. `Disasm.py <lo> <hi> <out>` over the cluster.
3. Pair each format string with the most recent `[reg + 0xNNN]` load before it.

## Four cautions

- **A name is not a mechanism.** It says a field exists and roughly what it
  holds — never its unit, its writer, or the rule that changes it.
- **A name loses to an arithmetic path.** Where the player dumper's
  `ResearchCompleted` / `ResearchPoint` disagreed with a reading taken from what
  the adder accumulates, the adder won and nothing was changed (Q265).
- **A repeated offset is a bitfield**, one dword shifted and masked per push,
  not many fields. Which bit is heuristic: a shift with no `AND 1` may be a
  counter rather than a flag, and a top-bit test needs no mask.
- **A multi-value format string** (`pos (%d, %d)`) pushes more than one value
  and only the first source is recorded here.

## The steering body — `0x004D5C0F`

Lower-case names throughout, unlike every other dumper. `pos` `+0x84`, `vel`
`+0x8C`, `local_xaxis` `+0x94`, `local_yaxis` `+0x9C`, `path_target_pos`
`+0xA4`, `projection_vector` `+0xAC`, `radius` `+0xDC`, `max_force` `+0xDE`,
`max_speed` `+0xE2`, `path_total_dist` `+0xFA`, `path_subpath_dist` `+0xFE`,
`path_tail_dist` `+0x102`, `path_last_progress` `+0x106`, `path_stuck_buffer`
`+0x10A`, `path_total_count` `+0x10C`, `path_flags` `+0x10D`,
`path_buffer_count` / `path_count` `+0x10E`, `tag_id` `+0x110`; bits
`path_finished` `+0x10E` bit 6, `ignore_blocker` and `escape_stuck_mode`
`+0x10F` bits 0 and 1.

**`max_force`, `max_speed` and `projection_vector` say the mover is a steering
system**, not a waypoint follower — which bears directly on ADR-0012's admitted
placeholder pathfinder (Q5).

## The imp task pool — `0x005083C5`

A task is `Task`, `Position (%d, %d)`, `Type`, `Data` `+0x19`, `Next` `+0x1B`,
`Id` `+0x1D`, `Allocated` `+0x1F` — a linked free list. The pool holds
`NumberOfFreeTasks` `+0x4650`, `NextUniqueId` `+0x4652`, `FreeListHead`
`+0x4654`.

This is the pool `trigger-semantics.md` reasons about for area conditions 62 and
69, which count a player's marked tiles.

## Level statistics — `0x005070FE`

A struct of twenty counters, and **the twelve the player's score weights**
(`player-object.md`) are its first twelve, in order:

| offset | name | | offset | name |
|---|---|---|---|---|
| `+0x00` | `DungeonHeartsDestroyed` | | `+0x24` | `CreaturesCommanded` |
| `+0x04` | `EnemyCreaturesKilled` | | `+0x28` | `CreaturesConverted` |
| `+0x08` | `HeroesDestroyed` | | `+0x2C` | `CreatureTraining` |
| `+0x0C` | `RoomsTakenOver` | | `+0x30` | `LevelWon` (byte) |
| `+0x10` | `RoomExtraPoints` | | `+0x31` | `OwnCreaturesKilled` |
| `+0x14` | `LandOwned` | | `+0x35` | `MoneySpent` |
| `+0x18` | `GoldMined` | | `+0x39` | `NumberOfThingsResearched` |
| `+0x1C` | `ManaStored` | | `+0x3D` | `TimeTaken` |
| `+0x20` | `ItemsManufactured` | | `+0x41`–`+0x43` | `SpecialsFound`, `SpecialsMissed`, `SecretLevelsMissed` (bytes) |

The same dumper also prints a research list: `NumberOfItems` `+0x10`,
`NumberOfUsedItems` and `Item` `+0x14`, and per entry `ResearchType`, `TypeId`
and `ResearchRequired` at `+0x0C`.
