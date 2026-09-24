# Runtime entity references and DKII.EXE's own field names (Q264)

DK2 ships eleven debug dumpers: functions that write an object to a log, one
`printf` per field, **with the field's name in the format string**. They are the
strongest evidence this project has short of running the game — names the
original's authors wrote, not inferences from behaviour, editor labels or
community documents.

This is the canonical index for established runtime layouts. Each linked entity
page owns its offsets, names, object links and confidence; mechanic research
owns what those fields *do* and links back rather than redefining the layout.

## The eleven

| dumper | fields | object | layout home | related layout or traversal |
|---|---:|---|---|---|
| `0x00490272` | 199 | creature | [creature-object.md](creature-object.md) | [job and needs](creature-job-object.md) |
| `0x004BCF2F` | 133 | player | [player-object.md](player-object.md) | [task pool](player-task-pool.md), [owned rooms](room-list.md) |
| `0x004E70B6` | 122 | room instance | [room-object.md](room-object.md) | [player and slab lists](room-list.md) |
| `0x00503F61` | 122 | computer keeper | [computer-keeper-object.md](computer-keeper-object.md) | — |
| `0x0049D336` | 77 | placed object | [object-thing-object.md](object-thing-object.md) | — |
| `0x0050EAE9` | 74 | world | [world-object.md](world-object.md) | [map and tiles](runtime-map.md), [trigger banks](trigger-state.md) |
| `0x0047920D` | 64 | a creature's job and needs | [creature-job-object.md](creature-job-object.md) | embedded in creature state |
| `0x004D5C0F` | 30 | steering body | [below](#the-steering-body--0x004d5c0f) | — |
| `0x005070FE` | 29 | research list and level statistics | [below](#level-statistics--0x005070fe) | — |
| `0x005083C5` | 21 | imp task pool | [below](#the-imp-task-pool--0x005083c5) | [task-pool traversal](player-task-pool.md) |
| `0x00526292` | 26 | *not an object* — a call-count profiler | — | — |

Runtime layouts without one of these dumpers live in
[action-points.md](action-points.md#runtime-layout-and-load) and the related
pages linked from the world row. Definition records loaded from KWD files are
not runtime entities; their layouts stay with the relevant format research.

When an offset gains a name or stronger interpretation, update its layout home.
If evidence changes an old reading, replace it there; git retains the history.

## How to read another one

1. `CallScanAll.py <out> 005595c0` lists all 1,144 call sites of the log
   `printf`. Cluster them: consecutive sites less than `0x120` apart are one
   dumper, and any cluster of twenty or more is worth reading.
2. `Disasm.py <lo> <hi> <out>` over the cluster.
3. Pair each format string with the most recent `[reg + 0xNNN]` load before it.

## Running one: the world log (`0x0050EA70`)

Everything above reads the dumpers for their *names*. One of them can be run, and
then it gives values.

**CONFIRMED (static, 2026-09-23).** `0x0050EA70` is the world logger: `this` is the
world object at `0x006F2A08`, it reads the game tick and formats `Logging World [GT %d]...`,
and the world dumper at `0x0050EAE9` above is inside it. Nothing calls it directly. It is
reached through the world's vtable at `+0x38`, from three sites in one routine,
`0x00526590` (`0x00526828`, `0x005268AE`, `0x00526DCF`) - the out-of-sync handler, whose
messages read *GamePacket says we're OOS*. Each site is gated on a global at `0x0075B174`
whose **only writer** is the command-line parser `0x005A63B0`, at `0x005A6C6E`. So it runs
only for a multiplayer desync under `-LOGOOS`, and **never in ordinary play**.

**REPRODUCED (owner, 2026-09-24).** Called directly on a loaded level it writes a complete
world dump to `MESSAGE.LOG` (belgianguy, OpenKeeper #505, 2026-08-11, under Wine and GDB:
`ECX` = `0x006F2A08`, call `0x0050EA70`; ufdada gives a Cheat Engine recipe that does the
same from a created thread, 2026-09-12). The owner ran the recipe four times in a
skirmish on Hopping, started from the GOG launcher (`DKII-dx.EXE`). The launcher puts no
level on the command line; the map was identified from the dump: 81×81 (only Hopping
and Level16 are), four players, and Hopping's placed heroes and four captives. **Each dump is appended** to the one
`MESSAGE.LOG` of the session, and the game's call-count profiler (`0x00526292`,
`Navigation:`, `Imps:`, `World Time:`) is added at exit. Kept as
`samples/worldlog-owner-4dumps.log.gz` (262,843 lines, SHA-256 of the log
`d9b05ad1…bceddb`); the file lives beside the executable.

**OBSERVED in one outside dump (2026-09-24).** belgianguy attached the file they made; it is
kept byte-for-byte as [samples/worldlog-envmapshowcase-gt231.log](samples/worldlog-envmapshowcase-gt231.log)
(26,346 CRLF lines, 606,516 bytes, SHA-256 `1bda4b04…22d27`; `DKII.exe -q -level
EnvMapShowcase` under Wine, `World Log at GameTick 231`). The world log does **recurse into
the other dumpers**. In file order:

- **Header** between `*` rules: the tick; `Checksums: Exe, Level, Data, TM, Map, RM, PM`
  (hex); the running `Checksum` and `Seed`; `ME Player TagId`. Then `Data Checksums:` for
  nine subsystems (`Terrain`, `Room`, `Creature`, `Door`, `Trap`, `Object`, `CreatureSpell`,
  `KeeperSpell`, `Shot`), decimal.
- **`PlayerManager:`** and each player: the player dumper's fields, `Statistics:`,
  `TaskStack:`, `ManufactureList:`, `PlayerRoomPlacmentInfo:` (sic), `RoomRemovalInfo:`.
- **`ThingManager`** twice: first over rooms (`Room TagId N`, from line 11,064), then over
  things (`NumberOfCreaturesCreated 15`, `NumberOfObjectsCreated 55`, ..., then one
  `Thing TagId N` block per thing from line 11,325). A creature's block holds the creature dumper's fields with sub-blocks `RenderInfo:`,
  `Pilot Navigation:` (the steering body: `pos`, `vel`, `path_*`, `max_speed`, `radius`),
  `State:` (`CurrentStateId`, `GameTickLastUpdated`, `StartedAt`, `Age`), `Anger:` (the job
  and needs object: `StateBeforeUtilityState`, `SleepNeeded`, `CurrentlyDoingJobId`,
  `UtilityNewState`, ...), `Utility State Data:` and `Fight:`.
- **`Map:`**, **`NavigationSystem:`**, **`NavigationSystemMap:`**, **`PlayerMessageQueue:`**
  (line 19,765 on), then the world's own fields from the world dumper above (`LevelScore`,
  `NeutralPlayerId`, `HornedReaperTypeId`, the trigger counts, ...), the level-intro
  overrides, and the line `End World Log`.

The order is read from the output, not from `0x0050EA70`'s call list; reading that list is
what would promote this to CONFIRMED.

**What the dump says about creature state.** Every id in it names a state in
[creature-state-table.md](creature-state-table.md):

| tag | `TypeId` | `CurrentStateId` | `StateBeforeUtilityState` (`+0x40`) | `UtilityNewState` (`+0xDF`) | `Utility State Data` |
|---|---:|---|---|---:|---|
| 440 | 29 | 176 idle | 176 | 1 | not in utility state |
| 442, 444, 445, 447 | 30, 3, 22, 4 | 250 guarding | 0 | 0 | not in utility state |
| 446 | 23 | 3 doing nothing | 0 | 0 | not in utility state |
| 443 | 13 | **7 en route** | 89 idle | **80** making a home | `RoomId 1125` |
| 448, 449, 451 | 1 (imp) | 20 claiming territory | 10 idle | 1 | not in utility state |
| 450 | 1 | 10 idle | 10 | 1 | not in utility state |
| 452, 455 | 1 | 12 idle | 87 wandering | 1 | not in utility state |
| 453 | 1 | 10 idle | 87 | 1 | not in utility state |
| 454 | 1 | **6 en route** | 87 | 1 | `MoveTo (22527, 22527)` |

Creature 443 is Q414's reading on a live game: the room route helper put it in state 7 with
`+0xDF` = its errand's state and the room in the utility data. It also adds a fact Q414 did
not have: the five creatures that have apparently never travelled hold **0** in `+0xDF`,
not the 1 an arrival writes. So an empty slot reads 0 before a creature's first trip and 1
after it.

**Why it matters here.** One call gives every creature's current state id and resume pair
at a known tick: the check the ADR-0135 retirements otherwise have no way to make.
`tools/worldlog.py` reads a dump (plain or gzipped) and prints each creature's state,
resume and saved state; `travel-states.md` §10 is its first use. Two ways to take one:

- **By hand, used:** Cheat Engine, run as administrator (the GOG game runs elevated),
  attached to the game, Auto Assemble, Execute once per dump:
  `alloc(newmem,2048)` / `newmem:` / `mov ecx,006F2A08` / `call 0050EA70` / `ret 4` /
  `createthread(newmem)`. **Pause the game first.** The created thread runs alongside
  the game's main thread, so a dump taken while the game is running can mix two ticks.
- **Scripted, not built:** `dk2watch.py` opens the process read-only. Calling the function
  needs `PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_CREATE_THREAD` (elevated), then
  `VirtualAllocEx`, `WriteProcessMemory` for a 13-byte stub (`B9 08 2A 6F 00` `E8 rel32`
  `C2 04 00`: a thread procedure is stdcall with one argument, so `ret 4`; the Cheat Engine
  script's plain `retn` survives only because the thread exits at once), and
  `CreateRemoteThread`. The same tick-mixing caution applies.

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
placeholder pathfinder (Q5). That reading is now confirmed from the other side:
this object is the argument the `Follow_Path` behaviour operates on, and the
whole subsystem is described in
[navigation-and-steering.md](navigation-and-steering.md).

## The imp task pool — `0x005083C5`

A task is `Position (%d, %d)`, `Type`, twelve `Worker` words, `Data`, `Next`,
`Id`, `Allocated` — a linked free list. The pool holds `NumberOfFreeTasks`
`+0x4650`, `NextUniqueId` `+0x4652`, `FreeListHead` `+0x4654`.

**A fifth caution, learned here: pair against the record, not the cursor.**
This dumper's loop advances a pointer that sits at `record + 4`, so offsets
read off its loads are four low. Against the record the layout is `x` `+0x00`,
`y` `+0x02`, `Type` `+0x04`, `Worker[12]` `+0x05`, `Data` `+0x1D`, `Next`
`+0x1F`, `Id` `+0x21`, `Allocated` `+0x23` — 36 bytes, 500 of them. The
corrected `Next` is the offset the pool's own walker reads at `0x005082DA`,
which the earlier reading contradicted; see `player-task-pool.md`.

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
