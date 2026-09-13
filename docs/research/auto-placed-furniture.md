# Auto-placed room furniture

Status: floor placement, wall placement, Temple Hand placement, and the five
furniture-count capacity arms are **STRONGLY INFERRED** from `DKII.EXE`. The
room-specific decorative passes remain **UNKNOWN**.

## 1. The identifiers belong to the room definition

The room instance stores a pointer at `room+0x19` to its loaded `RoomDef`. The
bytes consulted by furniture placement are that definition's `ObjectIds`,
loaded from the room record at file offset `+0x46E`. They are not six bytes of
player state, correcting the former Q270 reading. `KwdRooms` already preserves
all eight identifiers.

## 2. Generic floor pass

`FUN_004EDFC0` runs the generic pass only for a room with `SpecialTiles` and a
nonzero first object id.

- A slot is a room tile whose eight neighbours all belong to that same room.
- Object ids 0 through 3 are floor choices. The first zero ends the list.
- If all four are populated and the room also has `SpecialWalls`, only the
  first three are floor choices; index 3 begins the overlapping wall list.
- Guard Room, Workshop, and Casino keep one checkerboard parity. The phase is
  the parity of the eligible tile with the lowest x coordinate
  (`FUN_004EF5E0`).
- The chosen object type depends only on tile coordinates, not simulation RNG.

The selector advances a 32-bit value with
`seed = seed * 0x0019660D + 0x3C6EF35F`, rotates it right 13 bits, and takes a
modulus. It combines a y step modulo `0x2286` with an x step modulo `0x16F2` by
XOR, then steps that result modulo the choice count. Arithmetic wraps at 32
bits. This makes rebuilding the same room produce the same types at the same
coordinates.

## 3. Temple Hand

The Temple-specific pass at `FUN_004F1240` places object 66 only where the
candidate tile and every one of its eight neighbours are themselves generic
interior slots. A solid 5x5 Temple therefore gets a Hand at its centre; a 3x5
Temple gets none. This agrees with the owner's 2026-09-11 observation and
explains the Temple attraction contribution in `binary-entrance.md`.

## 4. Wall pass

The second half of `FUN_004EDFC0` uses object ids 3 through 5, stopping at the
first zero. It considers each room tile in north, east, south, west order. A
direction qualifies when:

- the adjacent outside tile and both outside diagonals are wall-capable;
- the two room tiles along that edge belong to this same room.

This excludes corners and needs a straight wall at least three tiles long.
North/south edges keep every other x coordinate; east/west edges keep every
other y coordinate. Each phase begins with the first eligible tile in the
room's linked-list order. The wall type uses the same coordinate selector as
the floor pass.

The binary additionally requires a bit in its separate wall-geometry grid and
then checks `TerrainFlags.AllowRoomWalls`. `TileState` has no equivalent of the
first bit, so the implementation uses `AllowRoomWalls` as its proxy. The exact
meaning and writers of that grid bit remain **UNKNOWN**. The simulation sorts
room tiles rather than preserving DK2's linked-list insertion order, so the
every-other phase can differ when construction order differs.

DK2 also assigns rotation and special sub-tile offsets after creating wall
objects. Those are presentation state the tile-level `ObjectState` cannot yet
represent; they do not affect furniture counts.

## 5. TotalCapacity

`FUN_004E3ED0` walks the room's auto-placed-object list and compares object
types against the room definition. The relevant arms are:

| room | TotalCapacity |
|---|---:|
| Library, Training Room, Workshop, Casino | zero with no match; otherwise matching objects + 1 |
| Torture Chamber | matching objects |

The count is furniture, not room area. With both generic passes represented,
the five arms can count every `RoomDef.ObjectIds[0..5]` object they consult.

## 6. Implementation boundary

`AutoFurnitureSystem` deterministically reconciles room-owned objects after
room construction. It currently owns:

- the generic floor pass;
- the generic wall pass, at tile-level precision;
- the Temple Hand;
- Hatchery coops, whose existing placement rule now marks them auto-placed.

The `AutoPlaced` bit is snapshot and hash state. Reconciliation removes only
objects carrying that bit, so a keeper-, creature-, or level-placed object on
the same tile is preserved.

Still open: the wall-geometry bit, construction-order phase, presentation
offsets, and special decorative passes such as Guard Posts and Workshop
pillars. None of those decorative objects enters the five furniture capacity
arms. Portal attraction remains on its existing boolean rule as the next
separate implementation step.
