# A Prison's door and a jail break: states 122 and 121

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25.
- **Built:** ADR-0229, `JailBreakRules`.
- Related: `prison.md`, `state-category-hooks.md` §3.3 (134, the Torture Chamber's rescue),
  `room-object.md` (the Prison's fields).

## Result in one sentence

A rescuer that steps into an enemy Prison holding an ally goes to **122**. It walks to the Prison's
**door**, faces the cell, unlocks and opens the door, and releases every prisoner of its own side.
Each released prisoner walks out in **121** and is free once it stands off the cell's floor.

## 1. The door, `0x004E32C0` — CONFIRMED

The room's `DoorTileX`/`DoorTileY` (`+0x51`, `+0x55`, named by the room dumper):
- The door is the tile **north of the first floor tile**, in the room's tile chain, whose three
  northern neighbours (N, NW, NE) are all off the floor.
- Floor is the cell test `0x00454110` = 1.
- With no such tile, (0, 0), which every reader takes as "no door".

The same routine places object `0x6D` (109) and object `0x74` (116) on the door, their frames
following `DoorOpen` and `Activation`. It runs from the room's rebuild `0x004EDFC0`. The reset
`0x004E2910` zeroes the door, the auto-close count and `DoorOpen`.

**Ours walks the tiles in ascending index.** The first floor tile is then always in the topmost
floor row, so the NW and NE tests never decide. A negative control that drops them survives for
that reason.

## 2. The room's door state — CONFIRMED

- `Activation` (`+0x59`): toggled by `0x004E3680`. Off to on posts message `0x3F`; on to off
  closes the door and posts `0x3E`. The keeper's toggle is `0x00514280`.
- `DoorOpen` (`+0x5D`): toggled by `0x004E35C0`, which plays the door's sounds.
- `DelayAutoCloseDoorCount` (`+0x61`): set to `var19 × 2` only by the imps' two door arms
  (`0x004E2940`, `0x004E29E0`), and counted down by `0x004E2AC0`, which closes the door at 0.
- `PrisonJailBroken` (`+0x4D`): `0x004E2D40(1)`. Its first setting plays sound `0x3F` at the
  room's centre, and any change redraws every tile.

## 3. The way in, `0x004E8660`'s Prison arm — CONFIRMED

Run when a creature steps onto a new room tile. For a Prison, the stepping creature (`EDI`) must:
- not be a worker;
- have a Prison with prisoners in it (`+0x6B`, used capacity, non-zero);
- have `Flags` bit 5 (`+0x46F & 0x20`);
- not be allied with the room's owner, or the owner is the neutral player;
- not be in category 14 (a captive), and not in or walking to 122;
- not be in category 22.

Then, over the room's attached list, a prisoner that is not dying, is of a side allied with the
rescuer, and is not the neutral player's: **`SetState(122)` on the rescuer**
(`MOV ECX,EDI` at `0x004E879C`). The Torture Chamber's arm is the same shape with 134.

## 4. State 122, `0x00473C60` — CONFIRMED

1. Off a Prison tile: return 0.
2. No door: return 0.
3. Not on the door tile: `0x00471890(door, 122, 0x80)`.
4. On it: turn toward the room's `+0x27` tile, returning 1 while turning.
5. `Activation` off: `0x004E3680` turns it on.
6. `DoorOpen` off: `0x004E35C0` opens it, return 1.
7. Open: `0x004E2E70(1, rescuer's owner, 0)`, then `SetState(1)` and `SetState(87)`, a wander.

## 5. The release, `0x004E2E70(message, player, all)` — CONFIRMED

Over the room's floor tiles, each thing on the ground, not dying, whose owner is `player` (or
any, with `all`):
- **With a door:** a random point of the tile **south of the door** (`random(0x1000)` on each
  axis, x first), walked to resuming 121: options `0x68` if `0x0048EBF0` accepts the tile,
  otherwise `0x6C`.
- **Without:** a random rim tile (`0x004E8430`), walked to resuming 121.

With `message` it posts `0x42` to the room's owner, and it calls `0x004E2D40(1)`. Its callers:
- 122 (the rescuer's side);
- `0x004E2E60`, which is `(1, 0, 1)`, everyone. That is called from the claim `0x004E5A70`, the
  **overcrowding break**, and from `0x00514462`;
- `0x004E2C30` (`(0, room owner, 0)`, after setting the attached list to 126).

## 6. State 121, `0x004739E0` — CONFIRMED

1. Off a Prison's floor: return 0, idle. **This is how a prisoner goes free.** Idle leaves category
   14, and the captive is its own side's creature again.
2. No attached room: return 1.
3. No door: a random rim tile, resuming 121.
4. On the door tile: 1, then a walk resuming 87. This is unreachable, since the door is off the
   floor.
5. On the tile south of the door: a walk to the door's centre resuming 121. Taken, `Activation` and
   `DoorOpen` are turned on if off.
6. Anywhere else on the floor: a random point of the tile south of the door, resuming 121.

## 7. Ours

- The door's three flags are one byte, `PrisonDoor`, on the room's lowest tile, like
  `HatchTicks`.
- The overcrowding break now releases through the door. It used to free prisoners where they
  stood.
- A prisoner on a 3x3 Prison's rim is not on its floor, and no release reaches it, as in DK2.
  `PrisonTests` now puts all three on the floor tile.
- The release takes only prisoners, where DK2 takes every creature of that side standing on the
  floor.
- A prisoner pushed past the door off the room is freed by the cell-gone check instead.
- **Not built:**
  - the door objects and their frames;
  - the keeper's toggle and the imps' arms, with their auto-close;
  - messages `0x3E`, `0x3F` and `0x42`, and the redraw;
  - `0x004E2C30`'s path;
  - the `0x68`/`0x6C` option choice (ours routes loosely).
