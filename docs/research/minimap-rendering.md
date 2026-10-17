# Minimap rendering

## Tile raster and palette (STRONGLY INFERRED, binary)

Read 2026-10-17: `0x0041A9B0` (zoomed) and `0x0041ADA0` (whole-map).
Both refresh a 128×128 RGB raster when the world turn changes or receiver
world field `+0x284` is nonzero. They sample the WORD class grid exposed by
player-interface slot `+0x1DC`; classification is recorded in
[00451C90](functions/00451C90.md), not duplicated here.

Its pre-fog class-13 predicate uses a nested view interface, not the direct
World vtable. Direct World slot `+0xD4` is `0x0050DEB0`, a no-argument list-byte
getter; that does not resolve the predicate or Q197.

Class 1 samples the repeated rock backdrop at HUD `+0x13BB`; other classes index
the palette at `+0x138A`. Before rasterizing, the neutral player's three palette
entries (banks `+0x44`, `+0x68`, `+0x8C`) copy the entry selected by turn modulo 7.
This is presentation cycling, not a change to tile ownership.

Zoomed mode uses `1 << zoom` pixels per tile, with the camera position retaining
its sub-tile fraction; `zoom == -1` selects the whole-map mode. The latter fits
the larger map dimension to 90 pixels, centres the shorter dimension, and leaves
the remaining raster as backdrop. Dimensions come from slots `+0x1CC/+0x1D0`.

Both paths draw thing overlays before updating the display texture. Zoomed mode
also draws the Heart direction trail. The display pass runs even without a new
raster, so camera rotation need not wait for a simulation turn.

## Overlays (partially read)

`0x0041CC50` was read beyond its previously documented trap arm. It places the
viewer's Heart marker without the ordinary tile-colour fog predicate. Its own
creature-list markers use a visibility/helper test; other players' creature-list
markers additionally require the explored bit at their current tile. The helper
and virtual visibility slots still need tracing before implementing those tests.
Traps retain the documented hidden-bit gate; doors are strokes whose orientation
comes from byte `+0x119`. Marker colours blink against black by world-turn parity.

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
`0x0041EDC0`, a heading-dependent decoration. The latter installs an 8×8 resource,
uses camera `+0xD84` and sine/cosine offsets; the exact sprite binding is not yet
identified. This is not evidence of a Portal overlay.

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

`0x0041A7D0`, when world `+0x284` is zero, submits command type 15/value 7,
then calls map-view slots `+0x1F0/+0x1E8`; those mode semantics remain unread.
Two adjacent candidates do not establish minimap input: `0x0041A190` draws a
top-right resource, and `0x0041A2B0` selects HUD mode 10 through a widget callback.

## Shipped UI resources (data inventory)

The reference install's `Data/FrontEnd.WAD` contains 445 entries: 371 PNG, 42 BMP,
29 `.444`, and one each GIF/PSD/DAT. It includes `MapColours.png`,
`BackdropMap.png` and `map_template.png`. `Data/Sprite.WAD` has 24 entries:
9 `.spr` and 15 PNG, including creature/trap icon sets and hand/action imagery.
This inventories containers, not a confirmed mapping from each HUD widget to art.

`Data/Text/Default` contains the in-game `.bf4` family, including sizes 7 through
72. The existing `Bf4FontReader` reads these; text style selection remains in
`InterfaceFonts`. Resource access for the HUD must remain behind a provider.
