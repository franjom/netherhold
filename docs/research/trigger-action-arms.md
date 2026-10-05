# Trigger action arms read whole: 22, 24, 35, 39, 40 and 41 (Q301, Q227)

**Scope:** what these action arms do past the body fields `trigger-semantics.md`
lists. Which dispatcher owns which type is `trigger-walkers.md`, "A tree's context
decides only part of its meaning". Read in DKII.EXE on 2026-10-04 and checked by a
second reader on 2026-10-05.

## Action 22 writes terrain, and on a room tile it sells and builds (Q301)

The world arm is `0x00519425` (dispatcher `0x00518AD0`, byte table `0x00519930`
entry `0x15` gives jump slot 12). In order:

1. **Guards.** The tile at the one-based (x−1, y−1) comes from world slot `+0x1D8`.
   With no tile the arm returns 1 (`0x0051988C`). If the tile's terrain byte `+0x0A`
   already equals `TerrainId`, it returns 1 having done nothing (`0x00519484`).
2. **Owner.** The player record is `world[+0x84] + (PlayerId − 1) * 0x1125`. Its
   `TagId` (`+0x04`) is the owner passed on, the same indirection as action 1.
3. **Which path.** The new terrain's definition comes from slot `+0x70`, and bit
   `0x10` of its `+0x17B` (room terrain) picks the path (`0x0051949E`).
4. **Both paths first unmake a room tile.** When the tile has a room
   (`tile+0x02 & 0xFFF`), the tile's owner, the thing `[tile+0x08 & 0xFFF]`, calls
   `Player_SellRoomTile` `0x004C5DB0(x, y, 0)`. Pay flag 0 means no refund and no
   stop at the permission test. The call is bracketed by `0x004F0840` and
   `0x004F09B0` on world `+0x9BE` over the one-tile rectangle (`0x005194E4`–`0x005194FC`,
   and again at `0x0051961F`–`0x00519637`).
5. **Plain terrain** (bit clear): `Tile_SetTerrain` `0x0044FC40(x, y, TerrainId,
   TagId, 1, 1)`. That routine gives a non-ownable result to the neutral player
   `[0x006F2548]`.
6. **Room terrain** (bit set):
   - The room type comes from `0x0050B550`, which scans the room definitions for one
     whose `+0x44E` equals `TerrainId`. Its definition comes from slot `+0x98`.
   - If the room's flags `+0x43D` carry bit 0 (PlaceableOnWater), the tile is first set
     to the level's water terrain, world `+0x9BAE`, owned by the neutral player. A room
     with bits 0 and 1 makes that call twice, and lava never.
   - Else, with bit 1 (PlaceableOnLava), it gets the lava terrain `+0x9BAF`.
   - Else it gets `TerrainId`, owned by the player.
   - The terrain loader `0x0051EB50` fills `+0x9BAE`/`+0x9BAF` with the ids of the
     terrains flagged `0x80` (Water) and `0x100` (Lava).
   - Then `0x004F0390(room, owner, rect)`, `Player_BuildRoomTile`
     `0x004C5C30(x, y, room, turn, 1, 1, 8)` (bypass and skipTest: no charge, no
     placement test), and `0x004F04F0(room, owner, rect)`.
7. **Both paths end** with world slot `+0x38C(0x146, TagId, tile centre, …)` and
   return 1. That slot makes effects, as for `0x59`, `0x138` and action 41; that this
   one is presentation only is inference.

**CONFIRMED** for the control flow and arguments. `0x004F04F0` and `0x004F09B0`, the closers, are read
(`wall-torches.md` §6: torch recolour and room refresh); the openers `0x004F0390` and
`0x004F0840` are unread. The keeper's builder `0x004C5B40` uses the
same bracket (`bridge-placement.md` §3).

**Ours** (`TriggerExecutor.AlterTerrain`, ADR-0136) differs in three ways:
- It declines any room tile, new or old, instead of selling and building.
- For a non-ownable terrain it writes `PlayerIds.Unowned` (0), where DK2's owner is
  the neutral player (`PlayerIds.Neutral`, 2). Whether that difference is observable
  is open.
- It raises no effect.

## Action 24's second dword: only its low byte, the message record (Q227)

The player arm `0x004C8688` (byte table `0x004C8A98`, entry 14) calls `Message_Post`
`0x004C4600(0x15, TagId, SpeechId, 0, allocate)`. `allocate` is computed from the
**low byte** of the second dword as `byte == 0`, by `NEG DL; SBB; INC`. The other
three bytes are never read.

In `Message_Post`, `allocate = 0` skips creating a queue record. Type `0x15` still
plays its speech through `0x00568390` when the recipient is the local player.

That split holds only when `Message_Post` gets that far. It returns before any
record or speech in three cases:
- the recipient's `+0x19A` is set;
- `0x004C5530` refuses;
- an unexpired record of the same type for the same player exists (the table's
  `0x4000` flag lifts that check).

So, unless suppressed or deduplicated: 0 plays the speech **and** posts a message
record, and non-zero only plays the speech. That the record is what the message
list shows is STRONGLY INFERRED.

## Action 35's byte 00: vanish or die (Q227)

The creature arm (`0x0049AC80`, case `0x23`) reads byte 00:

- **0:** the creature's `+0x3E6` loses bit `0x20` (`DeadBodyOnDestroy`, the runtime
  copy of the definition's corpse flag) and gains `0x40` (`LeaveNoTrace`;
  `0x0048814B` then skips the carried-gold spill `0x0048F930`). Vtable slot `+0x20`,
  `0x004B2AD0`, then sets health `+0x40` to 0. The entity pass's second loop (slot
  `+0x28`, `0x004B5C30`, health below 1, then `+0x30`, `0x0048AF80`) removes it after
  every update of that turn: no corpse, no gold dropped.
- **1:** `Creature_EnterDeathFall` `0x00459560`, an ordinary death.
- **Anything else:** nothing; the arm returns 1.

**CONFIRMED.**

## Actions 39, 40 and 41: the byte or word at 00 is a type id (Q227)

All three place at the tile centre `(n << 12) + 0x800` from the words at body
offsets 04/06. The coordinates are zero-based, as action 1's are.

- **39** (`0x00518CAD`): byte 00 is a **door type**. It goes to `0x004B7B50`, which
  looks the definition up through slot `+0x134`, constructs it with `Door_Construct`
  `0x004A5070`, and uses the player's `TagId` and orientation 0.
- **40**: byte 00 is a **trap type**, passed to `World_CreateTrap` `0x004B7C10`.
- **41**: word 00 is an **effect id**, passed to world slot `+0x38C`. Its owner
  argument is the result of slot `+0x264`, the **local** player, not a body byte.

**CONFIRMED.** None of the three ships.

All five types are Unperformed in ours (`TriggerExecutor.Performs`). The reader
(`KwdAdditionalRecords`) still names the bytes `Unknown_*`, and reads action 24's
second field as a full dword.
