# Player room trigger conditions

**Scope:** player-context condition types 24, 25 and 68. Parameter layout and
dispatcher provenance remain in `trigger-semantics.md`; room and player field
names come from `room-object.md` and `player-object.md`.

All three compare a measured integer with `Value` through DK2's shared compare
helper. Their body is `Comparison:u8@00`, `RoomId:u8@01`, `Value:s32@04`; bytes
2–3 are unread. A zero `RoomId` means every room the asking keeper owns, while a
non-zero id filters those owned rooms by the room's `TypeId`.

## Type 25: room instances

With a non-zero `RoomId`, the arm at `0x004C7EB3` walks the keeper's room list
and increments once for each matching room object. It counts room instances,
not their tiles. Zero reads player `+0x10F9`, named `NumberOfRooms` by DK2's
debug dumper, and therefore counts owned instances of every type.

The filtered accumulator is the low byte and its caller masks the result to
eight bits. No shipped level approaches 255 rooms, so overflow is irrelevant to
the retail corpus and the reimplementation does not model it.

`Comparison == 0` selects a different sub-mode at `0x004C7ECF`: whether
`FUN_004BAB20(RoomId)` returns availability 3. That function looks up the
player's `RoomInfo[]`, one of four separate availability tables documented in
`player-object.md`. It asks whether a room can be built, not how many exist.
All 85 shipped type-25 records use comparisons 1–5: 1, 3, 30, 8 and 43 records
respectively. The availability mode is not implemented.

## Type 24: total room size

The arm at `0x004C7F8E` sums the room's `Size` field (`room-object.md`). Zero
`RoomId` walks every owned room; a non-zero id calls the filtered sum at
`0x004C6EB0`. Unlike type 25, the result is not truncated to a byte.

`Size` is the slab count, **CONFIRMED** against live 2×2, 3×3, 5×5 and 3×5
rooms (`room-list.md`). `RoomInstance.TileCount` is the reimplementation's
analogue.

## Type 68: total capacity

Type 68 sums the room's `TotalCapacity` field (`room-object.md`).
`FUN_004E3ED0` computes it by walking things in the room and matching their
type against the room definition's furniture identifiers; `0x004E4038` stores
the result. It is a count of accepted objects, not a property of room shape.

`AutoFurnitureRules.TryTotalCapacity` implements the five established capacity
arms (`auto-placed-furniture.md`). Trigger condition type 68 remains blocked
until that rule is wired into the evaluator.
