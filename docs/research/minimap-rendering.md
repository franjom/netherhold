# Minimap rendering

## Tile raster and palette (STRONGLY INFERRED, binary)

Read 2026-10-17: `0x0041A9B0` (zoomed) and `0x0041ADA0` (whole-map).
Both refresh a 128×128 RGB raster when the world turn changes or receiver
world field `+0x284` is nonzero. They sample the WORD class grid exposed by
player-interface slot `+0x1DC`; classification is recorded in
[00451C90](functions/00451C90.md), not duplicated here.

Its pre-fog class-13 predicate is the dig-tag highlight, not a Portal exception:
view vtable `0x0066CC94` slot `+0xD4` is `0x004412E0`, forwarding coordinates to
the presentation-grid bit-13 reader `0x004434B0` (see [tag-highlight.md §1](tag-highlight.md#1-the-state-presentation-grid-bit-13)).
The adjacent slot `+0xD0` is `0x0043A980`, returning view camera `+0x3E5` and
providing a positive control. Direct World slot `+0xD4` is instead `0x0050DEB0`,
a no-argument list-byte getter. Q197 remains open.

Class 1 samples the repeated rock backdrop at HUD `+0x13BB`; other classes index
the palette at `+0x138A`. Before rasterizing, the neutral player's three palette
entries (banks `+0x44`, `+0x68`, `+0x8C`) copy the entry selected by turn modulo 7.
This is presentation cycling, not a change to tile ownership.

The copies read the current palette, not an immutable copy of its initial colours.
When the modulo-7 phase equals the neutral player's Number, it copies that slot
to itself and retains the previous colour. `MinimapPalette` preserves this order;
its banks are tested headlessly. The class grid coalesces changed cells for GPU
publication; it does not classify tiles or establish marker visibility.

Zoomed mode uses `1 << zoom` pixels per tile, with the camera position retaining
its sub-tile fraction; `zoom == -1` selects the whole-map mode. The latter fits
the larger map dimension to 90 pixels, centres the shorter dimension, and leaves
the remaining raster as backdrop. Dimensions come from slots `+0x1CC/+0x1D0`.
HUD setup `0x00419180` initializes zoom to `0xFF` (whole-map).

Both paths draw thing overlays before updating the display texture. Zoomed mode
also draws the Heart direction trail. The display pass runs even without a new
raster, so camera rotation need not wait for a simulation turn.

## Overlays (partially read)

`0x0041CC50` places the viewer's Heart marker without the ordinary tile-colour
fog predicate. Its creature helper [0040DF80](functions/0040DF80.md) excludes
the local pending-pickup list and the viewer's hand; it does not test worker
carrying or every player's hand. Other players' creatures additionally require
the explored bit at their current tile. Trap/door slot `+0x2C4` resolves to
[00510000](functions/00510000.md), the bounds-checked reader of that same bit.
Own traps bypass exploration; other traps additionally require Hidden clear.
Own doors bypass exploration, and other doors require it. Debug fog disabling
bypasses the explored gate in the adapter, as it does for tile colours.

Creature and trap dots use palette bank 17 plus the player's Number on odd
world turns, and black on even turns. Door strokes use the opposite phase.
Door orientation byte `+0x119` zero draws vertically, otherwise horizontally;
whole-map stroke length is the difference of the projected current/next tile
boundary. The adapter preserves own-creature-first, other-creatures, traps,
doors layering. It captures before worker dispatch; rotation draws only retained
GPU data, with no simulation references on the draw path.

`0x005B33C0` forwards to the filled-circle slot; `0x005B3340` uses the outline
slot. The concrete 32-bit raster table is identified by
[005B3070](functions/005B3070.md). Radius-one dots are five cardinal pixels,
including the centre; the adapter uses a smooth circle within the same 3×3
raster footprint (ART_DIRECTION §13 level B), not a pixel-exact cross. Door
strokes are one raster unit wide. Markers render at display resolution and retain
the disc's alpha mask rather than punching holes at antialiased edges.

Tiles and markers share the native integer whole-map fit: major extent 90,
origin 20, with the smaller dimension centred using integer half-difference
before scaling. The previous shader's continuous centred fit was one pixel off
even for a square map. Marker projection uses integer arithmetic to avoid an
inexact GPU division rounding a boundary down by another pixel.

The local pickup preview is a bounded adapter approximation: suppress the
queued creature until its command's simulation tick settles, then read the
viewer's actual hand. Native pending-pickup animation lifetime is still unread.
The reporting-distance and possession/Alarm outlines below are built. Guard
Post links, room outlines, other transient signals and camera footprint remain
outside the current implementation.

`0x0041E620` draws a dotted direction trail between the minimap centre and the
viewer's Heart in zoomed mode. It normalizes the offset with integer square root,
uses four-pixel steps and turn modulo 4 for phase, and clips dots inside 128×128.
One scale input is carried in a register the decompiler did not recover; do not
port the apparent uninitialized value.

`0x0041C660` draws the displayed map using eight vertices and six triangles,
rotating texture coordinates from camera heading at `+0xD84` (2048 units per
revolution). The angular corner offset is approximately 0.4266275 radians.
Coordinates pass through the UI transform before submission. This is the source
for a modern smooth-rim representation, not a requirement to enlarge its pixels.

`0x0041B180` is the whole-map camera-footprint overlay: the inspected body
transforms projected view points by camera heading and clips them to the map
rectangle. Only the projection/rotation portion was inspected; input handlers
and the full clipping contract remain outside this reading.

[Q197](open-questions.md) is still open: these readings do not establish why
the owner's fogged Portals appear. Do not invent a Portal visibility exception.

## Native resource loading (STRONGLY INFERRED, binary and shipped data)

`0x0041E810` loads `GUI\map\MapColours.png`. The interface initializer
`0x00402D00` allocates 0x225 palette entries and first sets them to opaque white.
The loader consumes 37 RGB pixels from the decoded image in this slot order:

| Source pixels, zero-based | Palette indices |
|---|---|
| 0 | 0 |
| 1–15 | 2–16 |
| 16–22 | 18–24 |
| 23–29 | 27–33 |
| 30–36 | 36–42 |

Entries use opaque ARGB packing; class 1 is the separate backdrop. Later neutral
cycling uses the banks described above. The loader also publishes the three
seven-colour groups through interface slot `+0x294`; those consumers are unread.

The background call in `0x00419180` was checked in assembly at
`0x004192D0..0x0041930C`: it loads `Gui\Map\Map-bg.png` into HUD descriptor
`+0x13A3`, whose pixel pointer at `+0x18` is the raster's `+0x13BB`.
The decompiler omitted those call arguments, so its apparent argument-free call
is not evidence of a different resource. `0x0041FFA0` loads a PNG descriptor
and optionally applies a requested pixel format before decoding.

The reference install's `EngineTextures.WAD` contains `GUI\Map\MapColours.png`
and `Map-BG.PNG`; the latter is 128×128 rock, matching the raster indexing.
Its palette is 64×16. `FrontEnd.WAD` has a same-named palette but different RGB
content; do not use that copy for the game HUD merely because its filename fits.

The HUD frame dispatcher `0x00419DF0` calls the minimap renderer and then
`0x0041EDC0`, the heading-dependent north decoration (catalogue entry 36 at
resource offset `+0x3CC`). It sets draw size fields to 8×8 and uses camera
`+0xD84` with sine/cosine offsets about virtual centre (244,1680) and radius 140.
Final sprite extent is not established by these fields alone.

## Input and UI coordinate boundary (partially read)

`0x0052C5A0` maps virtual UI points to screen points by integer
`(x * width / 2560, y * height / 1920)`. `0x0052C5F0` performs the inverse.
These original per-axis transforms differ from the height-based scalable HUD
specified in ART_DIRECTION §13; retain that deliberate interface choice.

`0x0041A2D0` captures input position through the inverse transform, stores its
offset from virtual minimap centre `(244,1680)` at HUD `+0x1393/+0x1397`, saves
the camera origin in `+0x139B/+0x139F`, then invokes `0x0041A410`.
The latter rotates the offset by camera heading, divides by four toward zero,
and maps it either through the whole-map 90-pixel fit or the zoom factor. After
map bounds and interface slot `+0x1FC` pass, it submits command type 4 containing
the tile coordinates. The exact initiating button, drag lifetime and packet
consumer remain to be traced; this does not establish the full click contract.

The callback's pointer appears at static data `0x00690FCE`, immediately followed
by `0x0041A7D0`. Record loading and button-field meanings remain unread; adjacency
alone does not establish a left/right-button binding.

`0x0041A7D0`, when world `+0x284` is zero, submits command type 15/value 7,
then calls map-view slots `+0x1F0/+0x1E8`; those mode semantics remain unread.
Two adjacent candidates do not establish minimap input: `0x0041A190` draws a
top-right resource, and `0x0041A2B0` selects HUD mode 10 through a widget callback.

## Widget art and callback bindings (partially read)

The art loader `0x0052CB40` reads 277 catalogue records at `0x006A7DF0`, each
69 bytes: DWORD id, 64-byte name, byte type. Resource requests go through service
slot `+0x54`. The matching highest-resolution cache entries are in
`DK2TextureCache/EngineTextures.dir/.dat`; these GUI names are not PNG entries
in EngineTextures.WAD. Frame id 9 is `gui/map/map` (128×128); corner images below
are 32×32. Their alpha, including the frame's faint centre shading, is retained.

The HUD constructor supplies menu-pointer list `0x00693EF8` to `0x0052B8F0`.
Menu records are 72 bytes; their `+0x3E` points to 80-byte widget records,
terminated by id -1 at widget `+1`. `0x0052CED0` loads menu geometry;
`0x0052C6D0` creates widgets and `0x00528300` copies their fields.
Menu 20 at `0x00693958` has draw rectangle **(20,1460,460,460)**. Under the
chosen height-based reference transform this is left 8, bottom 0, size 184×184.
That menu rectangle is not the frame's texture extent. Frame callback
`0x00414210` installs catalogue entry 9, sets a 448×440 virtual clip at x=20
and the map widget's draw y, and puts transformed virtual 256 into both render
size fields before view slot `+0x5C` draws with flags 7. `0x0052C630` merely
scales rectangle components; it does not remove padded texture pixels.
The adapter's 204.8×204.8 texture extent is **STRONGLY INFERRED** from those
size inputs and alignment of the shipped ring with the native display centre.
It starts at reference (8,584) and clips to 179.2×176; the 128×128 cached image
includes unused padding. Captures at both reference and 1440p sizes align after
this correction. The active renderer's complete sprite sizing contract is still
unread; these captures establish alignment, not full original-game acceptance.

| Corner | Widget address | Id | Draw x,y,w,h | Anchor bits | Default/active art ids |
|---|---|---:|---|---:|---|
| upper left | `0x00690E88` | 1 | 0,0,124,120 | 0 | 28 / 32 |
| upper right | `0x00690ED8` | 255 | 20,0,120,120 | 2 | 29 / 33 |
| lower right | `0x00690F28` | 253 | 16,16,128,128 | 6 | 31 / 35 |
| lower left | `0x00690F78` | 2 | 0,20,128,128 | 4 | 30 / 34 |

`0x00528570` anchors bit 1 against the parent's right edge and bit 2 against
its bottom edge, subtracting offset and size. `0x0052CE50` supplies the parent
rectangle; `0x00528430` then tests the pointer using the positioned hit bounds.
The corner names are `mapbf-00`, `mapbf-01`, `mapbf-02` and `mapbf-03`, with
`selected-` variants. The lower-right default is unusually **selected-mapbf-03**
(id 31); its alternate is mapbf-03 (35). Keep that binding rather than normalize
the filenames. `0x004129E0` substitutes active art for upper-left mode 10,
upper-right mode 4 and lower-left field `+0x2722`; other behaviour is still open.
Unselected, unhovered minimap corners skip the sprite draw: their idle images
are already part of the frame. Drawing all four overlays continuously duplicates
that artwork.

The corner callback pairs are: upper left `0x0041A2B0`/none; upper right
`0x004163F0`/none; lower right `0x00416480`/`0x00416580`; lower left
`0x00428710`/none. The lower-right callbacks increment/decrement signed zoom,
clamped to -1..4. Crossing into 0 or -1 updates the interface mode and whole-map
rectangle. Selection cleanup in their tails remains unread. The other corners
select mode 10, select mode 4, and toggle message-like widget visibility;
their complete panel behaviour is not implemented by displaying the artwork.

Type-0 widget dispatcher `0x00528610` invokes the two callback fields once per
held channel. `0x0052C0A0` maps events F0/F1 to those channels and maintains
capture. `0x00406530` routes unconsumed F0 to Hand_Click and F1 to the secondary
hand handler. The physical event producer remains unread: this trace alone does
not justify naming F0/F1 left/right mouse buttons. Native map widget 3 at
`0x00690FC8` uses the same channels for navigation/mode callbacks.

Other inspected setup functions: `0x004152B0` loads four ten-sprite digit/overlay
families; `0x005282F0` returns 1. `0x0052CF90` is pixel-format dispatch, whose
F0 mask was a negative pointer-event search candidate.
`0x0041FB40` draws a queued icon array rather than the fixed minimap frame;
`0x0040CFD0` only stores a scalar into its receiver.
The sprite arm of `0x0059B0C0` multiplies resource dimensions by render size
fields before fixed-point conversion and submission. `0x0043A740` initializes
the view's forwarded renderer pointer `+0x9E` to zero; that constructor alone
does not identify which runtime renderer receives the frame.

## Shipped UI resources (data inventory)

The reference install's `Data/FrontEnd.WAD` contains 445 entries: 371 PNG, 42 BMP,
29 `.444`, and one each GIF/PSD/DAT. It includes `MapColours.png`,
`BackdropMap.png` and `map_template.png`. `Data/Sprite.WAD` has 24 entries:
9 `.spr` and 15 PNG, including creature/trap icon sets and hand/action imagery.
This inventories containers, not a confirmed mapping from each HUD widget to art.

`Data/Text/Default` contains the in-game `.bf4` family, including sizes 7 through
72. The existing `Bf4FontReader` reads these; text style selection remains in
`InterfaceFonts`. Resource access for the HUD must remain behind a provider.

### Reporting-distance outlines and pulses (CONFIRMED, binary)

Read 2026-10-18: the complete `0x0041CC50` body, the `0x0051CD90` variable
switch, and `0x004AA2F0` with its minimap call site. The low byte at `0x006F2964`
is variable 183, DungeonHeartReportingDistanceTiles; `0x006F2974` and
`0x006F2975` are variables 190/191, GuardRoomReportingDistanceTiles and
GuardPostReportingDistanceTiles. The switch writes bytes, so rendering uses
the low eight bits of the configured values. These are distances, not clocks.

The viewer's Heart always receives an RGB (150,150,255) outline centred on
the Heart-position tile. Its radius is `distance * fitWidth / mapWidth` in
whole-map mode, with integer truncation, or `distance * (1 << zoom)` when
zoomed. It is static even though other minimap outlines expand over time.
An owned, completed Guard Post (definition flags `0x800`, runtime `+0x10C`
bit `0x20` clear) receives the same colour and projection formula using
variable 191, unless its shot id is 13, whose branch takes precedence.
Owned Guard Rooms (room type 9) use variable 190 to expand their min/max tile
bounds before drawing a rectangle, clamping each axis to 1..mapExtent-1.

The own-creature list additionally draws a white outline when runtime
`+0x3E6` bit `0x80` is set. This is the existing Possessed field, not a combat
alert. Its radius is `worldTurn & 7` in raster units in both map modes.
It uses the same pickup/hand suppression as the creature dot and is drawn
immediately after that dot. Other-player creature loops have no such ring.

For an owned trap whose definition's **shot id** `+0xBB` is 13 (Alarm and Hero
Alarm in shipped data; see traps.md), the magenta RGB (220,50,255) outline is
drawn while unsigned `worldTurn <= RechargeAt`. The inclusive deadline comes
from `0x004AA2F0`; it is not the complete firing-readiness predicate. Radius
is `worldTurn % 30`, in raster units in both modes, with no distance scaling.
The ordinary trap marker is drawn first. This branch does not separately test
construction status. Transient signal slots, player flag `+0x76` bit 4 and the
six trailing target records still require producer/semantic research; their
visible rings alone do not establish what gameplay events should create them.
