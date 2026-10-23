# Placement cursor: cage, tool icon and price

Read 2026-10-23 (claude) from DKII.EXE. This covers what DK2 draws at the pointer
while a room, sell, door, trap, keeper-spell or Query tool is selected: the
selection cage in the 3D view, the small tool icon next to the hand, the "No Go"
icon and the price digits. The hand sprites themselves, the rectangle
publication and the cage geometry and colour are in
[hand-cursor-rendering.md](hand-cursor-rendering.md).

The owner described the cursor from play, because DK2 screenshots do not capture it:
in room-build mode a flashing cage over the target tiles, blue-edged where the room can go,
with the price in pink/red digits beside the hand and a white "arrow down to the
ground" icon. Where the room cannot go, the cage is red, no price shows and a
crossed-circle icon floats near the hand. The binary agrees with that description. The
one-tile cage the owner sees while only hovering is not the selection rectangle: the room
arm fills none until a drag (§2). It is most likely the Hand light's own cage (LIKELY), which
`Presentation_Update 0x0043B220` marks drawn under the pointer whenever both published
rectangles are empty and an unread flag allows (§2a, read 2026-10-23).

## 1. Per-frame flow (CONFIRMED)

`0x0040BBE0` runs every frame. It first resets interface feedback byte
`+0x132E` to `(old & 0xF9) | 0x18`, which clears bits 0x02 and 0x04 and sets 0x08 and 0x10, and
zeroes the rectangle `+0x132F/+0x1333/+0x1337/+0x133B`. In the ordinary
dungeon view, with the pointer off the GUI (`+0x12B2 == 0`), it switches on the tool
mode `+0x13DB`:

| Mode | Arm | Tool |
|---|---|---|
| 0 | `0x0040AE80` | Hand |
| 1 | `0x0040B160` | rubber-band rectangle only (tool unidentified) |
| 3 | `0x0040B210` | room build |
| 4 | `0x0040B600` | sell |
| 6 | `0x0040B3C0` | door placement |
| 7 | `0x0040B4F0` | trap placement |
| 8 | `0x0040B7F0` | keeper spell (LIKELY; spell id in `+0x13E8`) |
| 10 | `0x0040BB60` | Query |

Over the GUI it requests the small pointer (state 2). For modes 3, 4, 6, 7 and 8 it also sets
bit 0x02, so the No Go icon shows over the HUD (see §3).

Bits of `+0x132E` that the arms use:
- 0x10: placement valid. `0x0040ACD0` copies it to bridge `+0x36`, which turns the cage blue.
- 0x02: invalid. It draws the No Go icon.
- 0x04: suppresses No Go.
- 0x01: hides the cursor overlay.

The arms set 0x10 and 0x02 as a pair:
`flags = (flags & 0xED) | (ok << 4) | ((ok == 0) << 1)`.

The price lives in `+0x13E4`, a signed int. A **negative** value is a cost and a **positive**
value is a refund. The room, door and trap arms write it every frame. Nothing clears it per
frame: only tool cancel `0x004094C0`, `0x00407580` and the reset `0x004033F0` zero it.

## 2. Validity and rectangle per tool (CONFIRMED unless marked)

**Room build, mode 3, `0x0040B210`.** If the hovered tile is diggable
(`+0x1084 == 1`, target flag bit 0), the arm shows PickAxeHold (state 4) and returns. Tagging
still works in room mode, and no icon or price is drawn. Otherwise the middle channel shows
the animated Point (state 1).
- **No button held** (`+0x13F0 == 0`): the arm does **not** fill the rectangle (its only
  rectangle stores are `0x0040B2F6..0x0040B313`, in the drag branch), so the rectangle cage
  is not drawn; the one-tile hover cage comes from the Hand light instead (§2a). Validity is `Room_PrecheckRectangle 0x004C5A20` on the
  1×1 rectangle at the hovered tile `(+0x1070,+0x1074)`. The tile also needs tile byte
  `+9` bit 0x20 and the tile's terrain record `+0x17B` bit 0 clear. That record comes from
  table `0x006DA4A8` indexed by tile byte `+0xA`.
- **Dragging** (`+0x13F0` non-zero): the left-press arm of `0x004075E0` sets `+0x13F0` to 1 at
  `0x00408165`, with anchor `+0x13E8/+0x13EC`, when world slot `+0x1FC` accepts the tile. The
  release sends packet 0x2F and clears it again at `0x00408274`. The rectangle is the anchor
  and the current tile, normalised and half-open. Each side is clamped to
  `RubberBandAreaLimit` (`0x006F2900`, u8, shipped value 8), so it is at most 9 tiles. The
  same precheck validates the whole rectangle.
- **Cost:** the precheck writes the room's per-tile price (`+0x43F`) times the number of
  tiles world slot `+0x22C` accepts (the bridge rooms 8, 17 and 22 count through `0x004C57F0`).
  It returns `money >= cost` (disassembly `0x004C5AF4..0x004C5B1E`). The arm stores
  `-cost`. When no tile is accepted the cost is 0, so no price is drawn.
- **No footprint.** The cursor never uses a fixed room shape such as 3×3. The rectangle is
  the drag and nothing else.

**Sell, mode 4, `0x0040B600`.**
- Dragging fills the same clamped rectangle. `+0x13E4` = `0x004BA2D0(rect)`, a positive
  refund, and valid means it is > 0.
- With no button held there is no rectangle. The refund is the first door (class 3) or trap
  (class 4) the player owns on the tile (`0x004AABB0`/`0x004A77E0`), otherwise
  `0x004BA2D0` for that tile.

**Door, mode 6, `0x0040B3C0`, and trap, mode 7, `0x0040B4F0`.** These arms **never fill the
rectangle, so doors and traps have no rectangle cage.** The hover cage of §2a has no
tool-mode test, so by the binary it shows here too, coloured by these arms' bit 0x10
(LIKELY: its visibility flag's source is unread; not owner-confirmed).
- With no Workshop (`Player_CountRoomsOfType(10)`), or no tile under the pointer (`+0x1FC`),
  the arm sets bit 0x02, clears 0x10 and leaves `+0x13E4` untouched (it may be stale).
- Otherwise it stores `-cost`: door cost is def `+0x8F`, trap cost is def `+0x92`. Valid
  means money ≥ cost and the world test passes: vtable `+0x244` for a door (it passes the
  Barricade bit, def `+0x8B` & 0x40) and `+0x24C` for a trap.
- The arm requests middle-channel state 1 (Point).

**Keeper spell, mode 8, `0x0040B7F0` (LIKELY tool identity).** It never fills the
rectangle, so there is no cage, and it never writes `+0x13E4`. Cancel clears the price when
the tool changes, so no price shows (LIKELY).
- It only sets 0x02 for an invalid target. The checks are tile reach (`+0x200`), an owned
  Portal at `-1`, the spell's tile test (`+0x254`), and the creature tests (`+0x258`/`+0x25C`)
  for target kinds 2, 3 and 4.
- Spell id 10 is refused on a creature at full health: `+0x40` equals max HP, scaled by
  level.
- The arm requests middle-channel icon id 0xC.

**Hand, mode 0, `0x0040AE80`.** This arm fills a 1×1 rectangle while the hand holds
something, and that is the drop cage. See hand-cursor-rendering.md.

## 2a. The hover cage: the Hand light's kind3 art (read 2026-10-23)

**Who draws it (LIKELY).** Bridge handle `+0x33E`, the Hand light
([classic-lighting.md](classic-lighting.md) §4). CONFIRMED (disassembly `0x0043BC10..0x0043BDAC`):
it carries the kind3 art and gets the drawn bit and edge word below each frame. That this is
the cage the owner sees rests on the owner's photos, the unread flag and the untraced draw path. Startup
`0x00447D50` builds it exactly as it builds `+0x35D`, the handle `0x0043CC30` uses for the
rectangle's boundary tiles: same factory (slot `+0xA4`), same post-creation block (render
record type byte 7, word 3, `0x0040CFD0`/`0x004482B0`/`0x004B37D0`, `0x00447EB6..0x00447F54`
against `0x00447FA6..0x00448044`). Only the light values differ (RGB 511, intensity 255,
radius `0x1C00`, against 255/128/0). So both carry the kind3 cage art. Each frame,
`Presentation_Update`, in the ordinary view (view modes 1, 2, 7 and, with `0x0075AEAF` clear,
10 place it at a thing or the camera focus instead, and the block below still runs), picks under the
pointer (bridge slot `+0x48` → renderer `+0x9E` slot `+0x48`, or `+0x4C`) and, when the
pointer is on an interior tile (x and y not 0 or dim−1), moves the light there through
`0x00446C70` and sets:
- `+0xD5` bit 0 (drawn), only when **both** published rectangles are empty: bridge
  `+0x10 == +0x18` and `+0x14 == +0x1C` (primary), and `+0x4E == +0x56` and `+0x52 == +0x5A`
  (secondary). The bit then takes a flag returned by the view host's `+0xC` object, slot
  `+0x20` (`0x0043B7DA..0x0043B7ED`), which is UNREAD. Otherwise bit 0 is cleared, so the
  hover cage and the rectangle cage never show together.
- `+0x81`, which is the art's edge word (render record at thing `+0x26`, field `+0x5B`):
  `0x1F` (all four edges plus the lower cage) or `0x3F` when bridge `+0x36` is set. Bit
  `0x20` is the blue bit in kind3, as in the rectangle path.
- `+0x111/+0x113/+0x115`, the light's RGB: `0x1FF` over an explored tile (byte `+9` bit
  0x20), else 0. This is the light, not the cage colour.
On a border tile it clears bit 0 and the RGB and does not move the light. Off the map, or
with `0x0044D620` false (letterbox camera), it clears bit 0 only (`0x0043BDD5`): the light is
not moved and its RGB is left. When the player thing has `+0x76` bit `0x80000` or `+0x19A` set (possession), it
clears bit 0 and publishes a zero primary rectangle (`0x0043BE0B`).

**Where it lands (LIKELY).** The light sits at the picked point, not at a tile centre, at
height `0x1C00` (`0x1E00` when the pick result's `+0x18` is set). Kind3 (`0x005A01A0`,
`0x005A077F..0x005A081C`) passes the position's x and y through `0x00637600` before
building the 27-point grid with `0x0059F3C0`. That CRT routine reports error op `0xB`, which
is floor, so the cage snaps to the tile under the pointer. With renderer `+0xC5` set it adds
the same terrain deformation (`0x005A1070`, `0x0059E480`, `0x0059E670`, `0x0059F4B0`,
`0x0059ED80`) from the position's integer tile. This is the same builder the rectangle cage
uses for each boundary tile, which fits the owner's photos: the hover cage follows the bent
terrain lattice like a full tile. How the light reaches the draw is not traced. It is LIKELY
the tile thing chain that `0x00446C70` updates, drawn by `0x0043CC30`'s thing pass.

**Colour while hovering (CONFIRMED chain).** The order is `0x00403D78` (`0x0040BFF0` →
`0x0040BBE0`: reset, then the tool arm), then `0x00403E52` (`0x0040ACD0`: publish). In room
mode the arm writes bit 0x10 from the 1×1 precheck on the hovered tile (explored, terrain
`+0x17B` gate, `Room_PrecheckRectangle`). `0x0040ACD0` copies it to bridge `+0x36`, and
`Presentation_Update` turns that into `0x3F` (blue) or `0x1F` (red). So the Claimed Path
cage is blue and the Water cage red, as photographed. In Hand mode the reset leaves 0x10
set, so the hover cage would be blue unless the hand arm clears it.

**Hover and drag do not share a path.** Dragging fills the interface rectangle. That is
published to bridge `+0x10` and `+0x4E`, which hides the light's cage, and `0x0043CC30` draws
the rectangle cage tile by tile through `+0x35D` (hand-cursor-rendering.md). Both draws use
the same kind3 art, edge masks and `+0x36` blue bit, so to the eye the one-tile cage grows
into the dragged rectangle.

**The secondary rectangle is not a hover source.** `0x0044E130` and `0x0044E1C0` replace the
current rectangle (`+0x10..+0x1C` / `+0x4E..+0x5A`) on every call. They keep the previous
one only in `+0x24..+0x30` / `+0x62..+0x6E`, raw on the first call and otherwise through
`0x005B7090` (unread, probably a union). The only callers that pass a non-zero rectangle are
in `0x0040ACD0`, which passes the interface rectangle to both. The rest pass zeros:
`0x0040386E/0x0040388B`, `0x0043B0D7/0x0043B0F4` and `0x0043BE0B`. So in this path the
secondary always equals the primary.

## 3. Tool icon and price drawing, `0x0040CAE0` (CONFIRMED)

The call comes from `0x00403FB0` at `0x00404D88`, with the pointer position in screen pixels
(`0x005589E0` on `0x00756EE8`). W and H below are the screen size, `0x00756EEC`/`0x00756EF0`.
The function returns at once when flag 0x01 is set or `+0x2722` is non-zero. Over the GUI it
first moves x left by `40·W/640`.

**Icon resources.** The 13 cursor icons are loaded by `0x0040C710` from the pointer table at
`0x0068F978`, read as bytes, into the 27-byte records at `[0x006CE958] + i·0x1B`:

| i | Resource | Used by |
|---:|---|---|
| 0 | `gui\cursors\I-Dig` | — |
| 1 | `gui\cursors\I-Dismiss` | — |
| 2 | `gui\cursors\I-Kill` | mode 5, special subtype 0x7F |
| 3 | `gui\cursors\I-Lv_up` | — |
| 4 | `gui\cursors\I-Max_Lv` | — |
| 5 | `gui\cursors\I-No_Go` | the invalid overlay |
| 6 | `gui\cursors\I-Place_Door` | mode 6 |
| 7 | `gui\cursors\I-Place_Room` | mode 3 |
| 8 | `gui\cursors\I-Place_Trap` | mode 7 |
| 9 | `gui\cursors\I-Query` | mode 10 |
| 10 | `gui\cursors\I-Resurrect` | — |
| 11 | `gui\cursors\I-Sell` | mode 4 |
| 12 | `gui\cursors\I-Transfer` | mode 5, special subtype 0x8C |

A dash means no use in this function. The I-Place_Room art is the owner's white
"arrow to the ground", and I-No_Go is the crossed circle (owner's description; the PNGs are
not yet checked in `Sprite.WAD`).

**Icon position.** The icon goes at `(x + 82·W/640, y + 10·H/480)`, and the draw passes size
`0x20×0x20`. The mapping of that size to screen pixels is unread. In room mode the icon is
skipped while the pickaxe dig-hover shows.

**Spell icon.** Mode 8 uses `0x0040D310` instead. It draws at the same offset, using art
taken from the spell definition (vtable `+0x110`; the exact field is unread). It then moves
the No Go anchor by `(-2, +2)`.

**Price.** When an icon was drawn and `+0x13E4 != 0`, `Hud_DrawResourceDigits 0x00415650` is
called with the absolute value. The digits are left-aligned at `(iconX + 30, iconY + 10)` in
unscaled pixels, in size mode 0. They use the HUD digit sprites: `Gui/icons/i-0..9` at width
≥ 640, `lo-0..9` below that (see [hud-resources.md](hud-resources.md)). The tint is a
colour word with red in the low byte, the order 0x00417B40 established:

| Case | Word | RGB |
|---|---|---|
| cost, money ≥ cost | `0xFF8080FF` | 255,128,128 (pink) |
| cost, money < cost | `0xFF0000FF` | 255,0,0 (red) |
| refund (sell) | `0xFFFFFF80` | 128,255,255 (LIKELY: assumes the same byte order) |

So an affordable room shows a pink price. A drag over placeable tiles that you cannot afford
shows a **red** price **and** No Go. A rectangle with no placeable tile shows No Go and no
price, as the owner described.

**No Go.** After the icon and price, if bit 0x02 is set and bit 0x04 is clear, the function
draws icon 5 (`I-No_Go`) at the same anchor, so it covers the tool icon. In Hand mode the
anchor comes from the held-item helper `0x0040CFE0`.

## 4. What differs by tool

| Tool | Cage | Icon | Price | Invalid |
|---|---|---|---|---|
| Room | only while dragging, blue or red | I-Place_Room | per-tile price × placeable tiles, pink or red | No Go |
| Sell | only while dragging | I-Sell | refund (128,255,255) | No Go when the refund is ≤ 0 |
| Door | none | I-Place_Door | door cost | No Go (no Workshop, unaffordable, world test fails) |
| Trap | none | I-Place_Trap | trap cost | as door |
| Keeper spell | none | the spell's own art | none (LIKELY) | No Go, offset (-2,+2) |
| Query | none | I-Query | — | — |

## 5. Unknowns

- The pixel scale of the icon's `0x20` size field and of the price's `+30/+10` offset.
- The spell art field, and the identity of mode 1.
- The 0xFFFFFF80 byte order (the sell refund colour).
- **Cage while hovering: the producer is read (§2a).** The owner's phone photos
  (2026-10-23, Water and Claimed Path, no button held) show a one-tile, full-height cage, red
  over water and blue over claimed path, growing over the tiles when dragged. The Hand
  light's kind3 cage most likely accounts for it (LIKELY, §2a). Still open: the visibility flag from the view
  host's `+0xC` slot `+0x20`, which decides whether the cage also shows in Hand, door, trap
  and spell modes and over a diggable tile; how the light thing reaches the draw; and
  `0x005B7090`.

The icons are engine textures, `GUI\Cursors\I-*` (32x32 with a 16x16 MIP), not `Sprite.WAD`.

## 6. Built (2026-10-23)

`PlacementCursor` (Presentation) returns each frame's cage, validity, icon, No Go and
signed price for room build, sell, door and trap, from `RoomRules` and
`BlueprintPlacementRules`; a room's price counts the tiles `CanBuild` or the span rule
accepts, and with none it is invalid and unpriced, as `0x004C5AF0` returns (CONFIRMED; our
count mirrors slot `+0x22C` without walking it). The room
drag clamps its end to `RubberBandAreaLimit` in `PlayerInputTranslator`, as the release
arm `0x00408194..0x004081FD` does. `ClassicTileHoverOutline` cages any rectangle, red or
blue; `ClassicPlacementOverlay` draws the icon at the pointer plus `(82*W/640, 10*H/480)`,
No Go over it, and the price in the gold counter's digits, pink, red when unaffordable,
(128,255,255) for a refund. Ours cages the hovered tile in room and sell mode, which matches
the Hand light's cage of §2a for those modes. It does not yet add that cage in door, trap or
Hand mode, or skip it on map-border tiles and during possession. The cursor
rectangle and the sell release keep the low corner (`0x0040B2B0`, `0x0040B661`, `0x004083DA`),
the room release the anchor (`0x00408194`); ours does each. Over the HUD ours forces No Go
even during a drag, where DK2's `+0x10C` gate runs the normal arm. Not
built: keeper-spell and Query arms, the hovered door/trap sell refund, the room hover's
terrain `+0x17B` gate, and a door/trap's stale price when invalid.
