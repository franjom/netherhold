# Placement cursor: cage, tool icon and price

Read 2026-10-23 (claude) from DKII.EXE: what DK2 draws at the pointer while a room, sell,
door, trap, keeper-spell or Query tool is selected, or the Hand holds something: the selection
cage, the tool or held-item icons beside the hand, "No Go" and the price digits. Hand sprites,
rectangle publication and cage geometry are in [hand-cursor-rendering.md](hand-cursor-rendering.md).

The owner's description from play (DK2 screenshots do not capture the cursor) matches the
binary: in room mode a flashing cage, blue where the room can go, a pink/red price and a white
"arrow to the ground" icon; where it cannot, a red cage, no price and a crossed circle. The
one-tile cage while only hovering is not the selection rectangle (the room arm fills none until
a drag, §2) but most likely the Hand light's own cage (LIKELY), drawn where `0x004096B0` allows
it (§2a, CONFIRMED).

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
| 8 | `0x0040B7F0` | keeper spell (CONFIRMED: the click arm posts the cast actions; spell id in `+0x13E8`) |
| 10 | `0x0040BB60` | Query |

Over the GUI it requests the small pointer (state 2). For modes 3, 4, 6, 7 and 8 it also sets
bit 0x02, so the No Go icon shows over the HUD; `0x0040CAE0` adds Query (§3).

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
rectangle, so doors and traps have no rectangle cage.** The hover cage of §2a shows here on
every interior tile, coloured by these arms' bit 0x10 (CONFIRMED: `0x0040C120` returns 1
for modes 6 and 7; not owner-confirmed).
- With no Workshop (`Player_CountRoomsOfType(10)`), or no tile under the pointer (`+0x1FC`),
  the arm sets bit 0x02, clears 0x10 and leaves `+0x13E4` untouched (it may be stale).
- Otherwise it stores `-cost`: door cost is def `+0x8F`, trap cost is def `+0x92`. Valid
  means money ≥ cost and the world test passes: vtable `+0x244` for a door (it passes the
  Barricade bit, def `+0x8B` & 0x40) and `+0x24C` for a trap.
- The arm requests middle-channel state 1 (Point).

**Keeper spell, mode 8, `0x0040B7F0` (CONFIRMED, re-read 2026-10-23).** No rectangle, no
`+0x13E4` or bit 0x10 write; it sets 0x02 where the cast would be refused. Its tests (record
[0040B7F0](functions/0040B7F0.md)): a place spell needs the tile in the map, then is valid on a
Temple interior tile (the click's action 0x87, the offering) or where the position permission
`0x004BB8A0` passes; a thing spell asks `0x004BBA40` on a creature (hover kinds 2, 3),
`0x004BBBC0` on an object (4), and on a tile (1) passes only the Temple interior; Heal is
refused on a creature at exactly its level-scaled maximum. Each gets cast flags 2 (6 when
interface `+0x3E6F` is 1): location selector 4 (`0x00517F10`) then also wants tile flag 0x2000; the other selectors are unread. The click arm of
`0x004075E0` runs the same tests and flags before posting 0x36/0x37/0x38/0x87, so DK2's No Go is
where its click does nothing.

**Query, mode 10, `0x0040BB60` (CONFIRMED).** Point cursor, hover id; never No Go off the GUI.

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
  (secondary). The bit then takes the interface's hover-cage test (below). Otherwise bit 0
  is cleared, so the hover cage and the rectangle cage never show together.
- `+0x81`, which is the art's edge word (render record at thing `+0x26`, field `+0x5B`):
  `0x1F` (all four edges plus the lower cage) or `0x3F` when bridge `+0x36` is set. Bit
  `0x20` is the blue bit in kind3, as in the rectangle path.
- `+0x111/+0x113/+0x115`, the light's RGB: `0x1FF` over an explored tile (byte `+9` bit
  0x20), else 0. This is the light, not the cage colour.
On a border tile it clears bit 0 and the RGB and does not move the light. Off the map, or
with `0x0044D620` false (letterbox camera), it clears bit 0 only (`0x0043BDD5`): the light is
not moved and its RGB is left. When the player thing has `+0x76` bit `0x80000` or `+0x19A` set (possession), it
clears bit 0 and publishes a zero primary rectangle (`0x0043BE0B`).

**The visibility flag (CONFIRMED, read 2026-10-23).** At `0x0043B7DA..0x0043B7ED` the bridge's
`+4` is the session container (`GameSession_UpdateLoop 0x00526590`; the bridge is its `+0x10`
component). Container `+0xC` is the player interface, the global `0x006CEA10`, which
`GameSession_Run` attaches through `0x00526FF0` at `0x005256D4` (and detaches through
`0x00527020`) unless global `0x0075AEAF` is set. Its only vtable is `0x0066C4A4`, installed by
`Interface_Construct 0x00402670` (slot `+4` is `Interface_Initialize 0x00402D00`). So `+0x20` is a
**virtual call**, slot 8, to `0x004096B0`; with no interface attached the flag is 0. That test
returns 0 when any of these holds:
- Options is open (interface `+0x2722 == 1`);
- the presentation's view mode (`+0x10` of the container, vtable `+0xCC`) is 1, 2 or 10;
- the pointer is over the GUI (`+0x12B2 == 1`) and no 3D-view press is captured (`+0x10C`
  zero; the press arms of `0x004075E0` set it, their releases clear it, LIKELY meaning);
- `Camera_AllowsOrdinaryInput 0x0044D590` is false (path camera, counted letterbox).

Otherwise it returns 1 when the tool allows it (`0x0040C120`, on tool mode `+0x13DB`) or the
hover target is a dig-taggable tile (`0x0040C1C0`: `+0x1084 == 1` and `+0x1088` bit 0, the test
that also shows PickAxeHold):

| Mode | Cage on an interior tile (rectangles empty) |
|---|---|
| 3 room, 4 sell, 6 door, 7 trap, 1, 2 | always |
| 0 Hand | while the hand holds something (world vtable `+0x26C`, `0x005094B0`: player `+0x1BF`, the hand count, non-zero), or over a dig-taggable tile |
| 8 keeper spell, 10 Query, 5, 9 | only over a dig-taggable tile |

In Hand mode a held item usually also fills the 1×1 drop rectangle (`0x0040AE80`), which
clears this bit, so the drop cage is drawn by the rectangle path; the light cage shows with a
full hand only when that arm finds no item to drop (CONFIRMED by reading; rare). Over a
dig-taggable tile the Hand and room arms return before writing bit 0x10, so the reset's 0x10
stays and that cage is **blue** (CONFIRMED chain). With an empty hand over floor, a creature
or a non-taggable wall, Hand mode shows no hover cage.

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
current rectangle (`+0x10..+0x1C` / `+0x4E..+0x5A`) on every call. The outgoing one goes
into `+0x24..+0x30` / `+0x62..+0x6E`: raw on the first call (flag `+0x20` / `+0x5E` clear),
otherwise as `0x005B7090(old, outgoing)`, the bounding-box union (min x0, min y0, max x1,
max y1; CONFIRMED for `0x0044E130`). So that field is a running union of replaced
rectangles, a dirty region; who clears the flag is unread. The only callers that pass a non-zero rectangle are
in `0x0040ACD0`, which passes the interface rectangle to both. The rest pass zeros:
`0x0040386E/0x0040388B`, `0x0043B0D7/0x0043B0F4` and `0x0043BE0B`. So in this path the
secondary always equals the primary.

### The drop cage's colour

Read 2026-10-23 in disassembly: `0x0040AE80` (`0x0040AF09..0x0040B13A`), `0x0040D8F0`, the
three CWorld slots behind it and the click arm of `0x00408EE0`.

**Which item (CONFIRMED).** The first pending-pickup record (`+0x142`, stride `0x36`, count
`+0xEF8`) whose dword `+0x0C` is 0. That dword is set to 1 when a drop is queued for a pickup the
simulation has not yet confirmed (`0x0040914B`). With no such record, the top of the hand:
player `+0x1BF` is the count, and `0x004BCAB0(count − 1)` gives the last item in. The click
drops the same item, so the cage tests the next drop.

**The colour (CONFIRMED).** Bit 0x10 ends as the inverse of bit 0x02. So the cage is blue unless
the arm sets 0x02 (red):
- class 0, a creature: `0x0040D8F0` false;
- class 2, an object: world slot `+0x234` false;
- class 6, a dead body: never red. The click still tests slot `+0x238`.
For an own creature over an owned Portal centre, or an IsWorker+IsEvil creature over an owned
Heart cell with byte `+0x0C == 0x40`, the arm sets 0x04, which hides No Go. A refusal clears it.

**The rules (CONFIRMED; `0x00450910`, `0x00450BE0` and `0x00450D40`).** "Allied" is the tile
owner's alliance with the player, which includes the player itself, and never the neutral player.
Every class refuses a navigation-solid tile (grid bit 3) and a door tile (`0x004528E0`).
- **Creature, worker:**
  - refused on a Combat Pit floor slot;
  - refused on another keeper's Heart inner 3×3;
  - an IsEvil worker is refused on a Portal or Hero Portal centre and on a Prison floor slot;
  - a non-evil worker is refused on any Heart inner 3×3;
  - otherwise accepted on allied ground, or on neutral roomless ground that is neither water nor
    lava.
- **Creature, non-worker:**
  - allied ground only;
  - a Heart's inner 3×3 is refused;
  - a Combat Pit floor slot needs the player's own pit with a free slot (`0x004D8430`);
  - another keeper's Portal centre is refused.
- **Creature, all:** `0x0040D8F0` then adds these flags from `+0x3EA`:
  - a creature the player does not own is refused on a Portal centre;
  - IsTool (0x100) without HasBeenKnockedOut (0x400) is refused on Prison, Temple and Combat Pit
    floor slots and anywhere in a Torture Chamber, but is accepted on Hero Lair (terrain 35)
    even where the map test refused.
- **Object:**
  - CanBeDroppedOnAnyLand (definition `+0xF4` bit 13; only Call To Arms) goes anywhere open;
  - everything else goes onto allied ground;
  - a Chicken (9) or Call To Arms (10) is refused on a Portal centre and on a Heart's inner 3×3.
- **Dead body:** only onto a tile the player itself owns.

**Where the rule lives (CONFIRMED by search).** Only `0x0040AE80` and `0x0040D8F0` call through
slots `+0x230`, `+0x234` and `+0x238`. The three bodies are reached only through their thunks.
The click arm submits a drop only when `0x0040D8F0` passes, so a refused drop never becomes a
command. The simulation does not test the tile again.

**Ours (ADR-0526).** `HandRules.CanDrop` is this gate, asked when the drop executes; the cage
and the drop both ask `HandRules.DropLands`, which adds our walkability guard. The Portal centre
is `0x004EF730`'s code 4, a tile with its own room on all four sides; the Heart's `0x40` is its
centre only (`0x004EFBE0`'s table). The dead-body arm is not built.
- "Neutral" is the one id `[0x006F2548]`, our player 2; a loaded map gives it non-ownable ground.
- The floor-slot test takes all eight neighbours, the `+0x0C == 0xFF` interior byte
  (room-floors-and-liquids.md §1). `RoomIndex.InteriorTileIndices` and
  `AutoFurnitureRules.FloorSlotSet` use four; they are left as they are.
- An Imp on its own Heart's eight platform tiles around the centre: the gate accepts it, and so
  does ours, past the walkability guard (ADR-0526 amendment). The owner's photos of 2026-10-23
  show a blue cage there and the Imp landing; on the centre the cage is blue with the rubbish
  bin, and the Imp is disposed of (structure-blocks.md §4).

## 3. Tool icon and price drawing, `0x0040CAE0` (CONFIRMED)

The call comes from `0x00403FB0` at `0x00404D88`, with the pointer position in screen pixels
(`0x005589E0` on `0x00756EE8`). W and H below are the screen size, `0x00756EEC`/`0x00756EF0`.
The function returns at once when flag 0x01 is set or `+0x2722` is non-zero. Over the GUI it
first moves x left by `40·W/640`.

**Icon resources.** The 13 cursor icons are loaded by `0x0040C710` from the pointer table at
`0x0068F978`, read as bytes, into the 27-byte records at `[0x006CE958] + i·0x1B`. Index 1 `I-Dismiss` is the held-item bin (`0x0040D450`), 2 `I-Kill` and 12 `I-Transfer` mode 5
(special subtype 0x7F / 0x8C), 5 `I-No_Go` the invalid overlay, 6/7/8 `I-Place_Door`/`_Room`/`_Trap`
modes 6/3/7, 9 `I-Query` mode 10 and 11 `I-Sell` mode 4; `I-Dig`, `I-Lv_up`, `I-Max_Lv` and
`I-Resurrect` (0, 3, 4, 10) are unused here (full table in [0040C710](functions/0040C710.md)). They
are engine textures (32×32 with a 16×16 MIP), not `Sprite.WAD`. I-Place_Room is the owner's white
"arrow to the ground", I-No_Go the crossed circle (owner's description, unchecked until
2026-10-23, when I-No_Go and I-Dismiss were decoded: a crossed circle and a red bin).

**Icon position.** The icon goes at `(x + 82·W/640, y + 10·H/480)`, and the draw passes size
`0x20×0x20`. The mapping of that size to screen pixels is unread. In room mode the icon is
skipped while the pickaxe dig-hover shows.

**Spell icon (CONFIRMED, read 2026-10-23).** Mode 8 calls `0x0040D310` and leaves the icon
index at 13, so no I-* icon and **no price**. It draws, at the same anchor and `0x20×0x20` size,
the spell's GUI icon: the KeeperSpells record's first art (file `+0x20`, `gui\spells\<name>`, the
bottom panel's resource), never the upgraded `…-2`. Then the No Go anchor moves by `(-2, +2)`.
Record [0040D310](functions/0040D310.md). **Over the GUI** this function sets bit 0x02 for every
mode with an icon (3–8, 10), so Query too shows No Go over the HUD (CONFIRMED).

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

**Held items in Hand mode (CONFIRMED, read 2026-10-23).** `0x0040CAE0` returns at once while
Options is open (`+0x2722`; owner-confirmed: the icons hide) or bit 0x01 is set (`0x004039A0`,
once the player has a result); its caller `0x00403FB0` calls it every frame, and nothing on that
path tests possession or the full map. Mode 0 draws no I-* icon or price; with bit 0x08 (cleared
over a dig-taggable tile with nothing to pick up) and off the GUI it calls `0x0040CFE0`: each
held thing, top of the hand first, gets a `0x20×0x20` icon in a 4×2 grid of 32-pixel steps from
the tool-icon anchor, and past eight `gui\Creatures\more_creatures` (owner-confirmed with nine or
more). A creature shows its **Icon1Resource** (Creatures.kwd `+0xC44`, `gui\creatures\evil\imp`),
an object its **GuiIconResource** (Objects.kwd `+0x74`). Inside icon 0's draw, for a creature
with bit 0x04 (§2a: an own creature over its own Heart's centre if IsWorker+IsEvil, else over its
own Portal's centre, drop accepted), `0x0040D450` draws `I-Dismiss`, the red bin, 2 px left.
Whether DK2 keeps cell `0x40`, and so the bin, once the Heart is ruined is unread. No Go draws
last, at icon 0 moved by `(-2, +2)`, when the drop is refused; bit 0x04 hides it. Order: icon 0,
bin, icons 1–7, overflow, No Go ([0040CFE0](functions/0040CFE0.md), [0040D450](functions/0040D450.md)).

## 4. What differs by tool

| Tool | Cage | Icon | Price | Invalid |
|---|---|---|---|---|
| Room | one-tile hover cage, rectangle while dragging, blue or red | I-Place_Room | per-tile price × placeable tiles, pink or red | No Go |
| Sell | one-tile hover cage, rectangle while dragging | I-Sell | refund (128,255,255) | No Go when the refund is ≤ 0 |
| Door | one-tile hover cage | I-Place_Door | door cost | No Go (no Workshop, unaffordable, world test fails) |
| Trap | one-tile hover cage | I-Place_Trap | trap cost | as door |
| Keeper spell | hover cage over a dig-taggable tile only | the spell's GUI icon (file `+0x20`) | none (CONFIRMED) | No Go where the click casts nothing, offset (-2,+2) |
| Query | hover cage over a dig-taggable tile only | I-Query | none | No Go only over the HUD |
| Hand, holding | drop cage on the hovered tile | each held thing's GUI icon, up to 8, then more_creatures; I-Dismiss over the first where the drop disposes | none | No Go over the first icon where the drop is refused |

## 5. Unknowns

- The pixel scale of the icon's `0x20` size field and of the price's `+30/+10` offset.
- The identity of mode 1, and of interface `+0x3E6F == 1` (cast flag 4 in the spell arms).
- The 0xFFFFFF80 byte order (the sell refund colour).
- **Cage while hovering** (producer read, §2a): the owner's photos (2026-10-23) show a one-tile
  cage, red over water, blue over claimed path; the Hand light's kind3 cage most likely is it
  (LIKELY). Open: how the light reaches the draw, and who clears the union flag `+0x20`/`+0x5E`.
- For the owner, from play: is the cage on every interior tile with a door or trap selected,
  and in Hand mode only over a dig-taggable wall (blue)? That would confirm the link. Does Call
  to Arms show No Go on some open floor; is the Temple interior valid for a place spell; Heal
  slightly below full; Create Imp over enemy or unclaimed floor.

## 6. Built (2026-10-23)

`PlacementCursor` (Presentation) returns each frame's cage, validity, icon, No Go and signed price
for room, sell, door and trap from `RoomRules` and `BlueprintPlacementRules`; a room's price counts
the tiles `CanBuild` or the span rule accepts, and with none it is invalid and unpriced, as
`0x004C5AF0` returns (CONFIRMED; our count mirrors slot `+0x22C` without walking it). The room drag
clamps its end to `RubberBandAreaLimit` (`PlayerInputTranslator`, as `0x00408194..0x004081FD`); the
cursor rectangle and sell release keep the low corner (`0x0040B2B0`, `0x0040B661`, `0x004083DA`),
the room release the anchor (`0x00408194`); ours does each.
`ClassicTileHoverOutline` cages any rectangle; `ClassicPlacementOverlay` draws the icon beside
the rendered Hand (anchor correction below), No Go over it, and the price in the gold counter's digits,
pink, red when unaffordable, (128,255,255) for a refund. The hover cage follows `0x004096B0`
(`HoverCageShows`, `DungeonRunner.HoverCaged`; in Hand mode over a dig-taggable tile, hit kind
Tile), hidden on border tiles, with Options open and
during possession; blue over a dig-taggable tile in Hand, room, spell and Query mode, else the
arm's validity (`HoverCageBlue`). `HandDrop` asks `HandRules.DropLands`, the gate `0x0040D8F0`
plus our walkability guard (ADR-0526). Not applied: `0x004096B0`'s view-mode, over-GUI and camera
gates; over the HUD ours forces No Go even during a drag, where DK2's `+0x10C` gate runs the
normal arm. Not built: the hovered door/trap sell refund, the room hover's terrain `+0x17B` gate,
and a door/trap's stale price when invalid.
**Anchor correction (2026-10-24).** Native photo `D:\DK2Evidence\room-placement-anchor-20261024-87734497223a40fab8abbd9ced40495e\IMG20261023083831.jpg` confirms the icon/price beside the right wrist.
`HandCursor.OverlayAnchor` follows the sprite's native hotspot after the chosen claw anchor moves it, using the Hand's uniform height scale (ART_DIRECTION §13).
This adapts §3's native per-axis offsets; the old pointer/width scaling displaced the overlay relative to the Hand. Icon, price and No Go move together.
The GUI's leftward shift uses the same height scale; `HandCursorTests` checks the wrist relationship at 480, 1080 and 2160 pixels high.

**Keeper spell and Query (2026-10-23).** `PlacementCursor.Spell`: the spell's icon, no cage or
price, No Go where the simulation refuses the command our click sends (`CanCast` on the hovered
creature, `CanCastAtPlace` at the tile's centre), asked for the next tick on the navigation bytes
as last refreshed, never refreshing them. Our click differs from DK2's, simulation unchanged, to
follow up: the flag-2 tile test (selector 4,
tile flag 0x2000); object targets (kind 4); the Temple offering (0x87); Heal refused at or above
the maximum where DK2 refuses only equality (a reincarnated Vampire is above it,
creature-death.md), and our `TargetAtMaxHealth` covers any positive Modify Health shot. Only
creature kinds publish the hover id (`0x0040B7F0`). `CreateImp` (DK2's mode 8): No Go only when
the mana is short, as our click casts on the Heart; DK2 tests the tile. `Query`: I-Query alone.
Both show No Go over the HUD. The spell's No Go shift of 2 pixels uses the price's scale (HYPOTHESIS).

**Held items (2026-10-23).** `HandDrop` returns `PlacementIcon.Held`, No Go where `DropLands`
refuses, and `Dispose` for a drop that lands. `HeldItemStack.Disposes` asks the click's own rule
(`CreatureLands`' disposal: `DisposalRules`, no prisoner, the Heart not ruined), including
both own Portal and Hero Portal centres. The owner's Hero Portal confirmation is recorded in
[structure-blocks.md](structure-blocks.md#confidence). Ours has no queued drops, so icon 0 is the
hand's top. `DungeonRunner.HeldIcons` gates the icons apart from the cage: Options hides them,
possession and the full map do not; the result bit 0x01 is not mirrored. The overlay draws them in
DK2's order at the tool icon's scale (HYPOTHESIS).
