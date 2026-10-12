# Auto-placed room furniture

Status: floor placement, wall placement, Temple Hand placement, and the five
furniture-count capacity arms are **STRONGLY INFERRED** from `DKII.EXE`. The
room-specific decorative passes are read in `furniture-placement.md` §4–§5.

## 1. The identifiers belong to the room definition

Furniture placement follows the loaded `RoomDef` link documented in
`room-object.md`. The bytes it consults are that definition's `ObjectIds`,
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
room's slab-list order (`room-object.md`). The wall type uses the same
coordinate selector as the floor pass.

The separate byte-grid test is the cached solid-terrain classification, not an
independent wall-geometry rule. `FUN_0044FC40` derives value 8 exactly when the
terrain has `TerrainFlags.Solid`, writes that value through `FUN_004D1930` or
`FUN_004D1970`, and the wall predicate tests bit 8 before separately testing
`TerrainFlags.AllowRoomWalls`. Wall-capable terrain therefore needs both flags.
Every shipped terrain with `AllowRoomWalls` is also solid. The simulation sorts
room tiles rather than preserving DK2's linked-list insertion order, so the
every-other phase can differ when construction order differs.

Each object's heading and position within its tile, for both passes and the
room-specific passes, are in `furniture-placement.md`; they do not affect
furniture counts.

## 5. TotalCapacity

`FUN_004E3ED0` walks the room's auto-placed-object list and compares object
types against the room definition. The relevant arms are:

| room | TotalCapacity |
|---|---:|
| Library, Training Room, Workshop, Casino | zero with no match; otherwise matching objects + 1 |
| Torture Chamber | matching objects |
| Temple, Combat Pit | the tiles `Room_IsFloorSlot` rejects (the rim) |
| Graveyard | its floor slots |
| Portal | 0 |
| Prison | `0xFFFF`; its own count goes to `+0x4B` (floor slots + 1) |
| every other type: Treasury, Lair, Hatchery, Heart, Wooden and Stone Bridge, **Guard Room**, the hero rooms | the slab count `+0xA7` |

**The default arm, CONFIRMED (binary, 2026-10-12).** The switch is on type − 3, bounded at 13,
through the table at `0x004E42C0`. Entries 1, 2, 5 and 6 (Hatchery, Heart, Wooden Bridge, Guard
Room) and every type outside 3–16 go to `0x004E429A`, which copies the word at `+0xA7` into
`+0x67`. The portal's attraction contribution `0x004DCE70` returns `+0x67` for every type but
Temple, Prison and Combat Pit, so a Guard Room attracts as many Dark Elves as it has tiles. On
Level 4 the keeper cannot build one; it captures the three neutral Guard Rooms, and DK2's portal
then sent eleven Dark Elves (owner's recording, `level4-live1`, from capture tick 1943).

**Confirmed for the Training Room (owner, 2026-09-25):** a 3x3 room on open ground shows
capacity 2 (one dummy + 1), and against reinforced walls the dummy, plus every Training
Target, plus 1. So the +1 is real and wall furniture counts.

The furniture arms count furniture, not room area. With both generic passes represented,
the five arms can count every `RoomDef.ObjectIds[0..5]` object they consult.

## 6. Implementation boundary

`AutoFurnitureSystem` deterministically reconciles room-owned objects after
room construction. It currently owns:

- the generic floor pass;
- the generic wall pass, at tile-level precision;
- the Temple Hand;
- removal and retention of Hatchery coops; `HatcherySystem` creates them so its
  `coops_placed` event remains observable.

The `AutoPlaced` bit is snapshot and hash state. Any non-held object with the
right owner, tile and type satisfies a slot, including keeper-, creature- and
level-placed objects, so reconciliation never stacks an automatic duplicate on
it. Only unmatched auto-placed objects are removed. Held objects are excluded
defensively because their tile index is stale; shipped furniture cannot be held
(Q196). A desired object absent from the definition table is skipped rather
than letting this per-tick maintenance path abort the simulation.

Still open: preserving the construction-order phase, and building the
presentation offsets and the decorative passes (Guard Posts, Workshop and room
pillars, Temple candlesticks), which `furniture-placement.md` reads. None of
those decorative objects enters the five furniture capacity arms. Portal
attraction remains on its existing boolean rule as the next separate
implementation step.
