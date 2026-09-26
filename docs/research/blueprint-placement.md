# Placing a door or trap blueprint during play

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25.
- **Built:** ADR-0231, `BlueprintPlacementRules`, `PlaceDoorCommand`, `PlaceTrapCommand`.
- Related: `workshop-crates.md` (the order, the crate and its delivery).

## Result in one sentence

A keeper places a door or trap blueprint on its own open floor, a door only between two walls
unless it is a Barricade. **The definition's `GoldCost` is paid at once** (confirmed by the owner,
2026-09-26), and the blueprint then
gets the same order and list entry an authored one does.

## 1. The door, `0x004C33B0(x, y, type)` — CONFIRMED

1. The definition (world slot `+0x138`). `Barricade` is `+0x8B` bit `0x40`.
2. The tile test, world slot `+0x244`, which is `0x00450DB0(x, y, owner, barricade)`. It needs all
   of:
   - the tile's owner is the keeper (cell `+8`);
   - it is not solid (the route map's bit 3);
   - it is not a room (cell `+2`);
   - no thing of layer 4 or layer 3 stands on it (`0x00453670`), which ours reads as no door or
     trap.

   Unless the door is a Barricade, it also needs the tiles on both sides solid, north and south
   or east and west (the table `0x006BC350`).
3. **The orientation**, for a door that is not a Barricade: 2, or 1 when west and east are solid,
   else 0 when north and south are.
4. **The price** is `+0x8F`, a word the loader copies from file offset `0x22E`. That is
   `GoldCost`: the copy `0x004A7100` reads the door record at a constant `0x74` from the file,
   and the door id, crate object and `ManufactureRequired` land where `KwdDoors` puts them.
   Short of gold (`player+0x7E`), message `0x37` and fail.
5. Otherwise:
   - it spends the gold (`0x004BA420(−cost)`) and adds it to `player+0x177`, the panel's row 3
     (`torture-information.md`);
   - it shows the cost (`0x0050FB70`) and creates the blueprint door (`0x004B7B50`, with the
     orientation);
   - it pushes the order (`0x005066E0(0, type, …)`) and lists the blueprint (`0x00505680`);
   - for the local keeper, it plays a sound;
   - it calls `0x004E8D30` and refreshes the tile.

## 2. The trap, `0x004C3860(x, y)` — CONFIRMED

The same, with:
- the tile test at world slot `+0x24C`, which is `0x00450EB0(x, y, owner)`: the first four tests
  and no neighbour test;
- the price at `+0x92` (from the trap record's `GoldCost`);
- `0x004B7C10` to create the blueprint;
- `0x005066E0(1, type, …)` for the order.

## 3. Callers

- The game action at `0x00513210` (the keeper's command), for both.
- The computer keeper, `0x004FFD80`.

## 4. Ours

- `BlueprintSystem` applies the two commands after room building. Gold comes out of the keeper's
  tiles through `GoldStore.TryWithdraw`, as a room's does.
- The blueprint is a `DoorState` or `TrapState` under construction. Its order is pushed through
  `ManufactureRules.QueueOne`, the same count as the level loader's, and it is listed through
  `CrateRules.Register`. From there the Workshop and the imps build it (ADR-0214).
- **The orientation** is `BlueprintPlacementRules.Orientation`, kept on the door (ADR-0233). The
  level loader `0x00520F40` gives an authored door the same: it starts at 2, skips the tests for a
  Barricade (`+0x8B` bit `0x40`), and tests west and east first. The door creator `0x004B7B50`
  takes it as its fourth argument. A door a trigger makes (`0x00518AD0`) is given 0; that path
  is not built.
- **Not built:**
  - `player+0x177`;
  - message `0x37`, the cost display, the sounds, `0x004E8D30`;
  - the computer keeper's placement.
