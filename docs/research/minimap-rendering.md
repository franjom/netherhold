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
The reporting-distance and possession/Alarm outlines below are built, including
Guard Room rectangles. The camera footprint is built as described below;
Guard Post links and other transient signals remain outside the implementation.

`0x0041E620` runs after all markers in the refreshed zoomed raster, before upload/display.
For step `s=1<<zoom`, focus F and Heart tile H, each vector axis is
`V=(H-(F>>12)+64/s)*s-((F%4096)>>(12-zoom))-64` (signed remainder; no half-tile offset).
Let `L=isqrt(uint(Vx²+Vy²))` with wrapped 32-bit sum and `N=trunc(V*256/L)`. For L>8,
emit `(L-5)>>2` points `64+trunc(N*(L+(turn&3)-4*i)/256)`, only where both axes are 1..127.
`0x005B30C0`→`0x005C4B50` writes one opaque-white pixel per point; full-map mode omits this trail.

`0x0041C660` draws the displayed map using eight vertices and six triangles,
rotating texture coordinates from camera heading at `+0xD84` (2048 units per
revolution). The angular corner offset is approximately 0.4266275 radians.
Coordinates pass through the UI transform before submission. This is the source
for a modern smooth-rim representation, not a requirement to enlarge its pixels.

The whole-map camera-footprint geometry and remaining material boundary are
recorded in [Camera footprint](#camera-footprint-binary).

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

After the map, `0x00419DF0` calls north marker `0x0041EDC0`: catalogue entry 36,
`gui/map/north` (static record `0x006A87A4`, runtime resource `+0x3CC`). Let
`C=U(244,1680)`, `R=U(0,140).y`, `a=(2048-heading)&2047`, with Q16 tables
`0x006C1488`/`0x006C1C88`. Draw origin is
`(Cx+trunc(sin[a]*R/65536)-3, Cy-trunc(cos[a]*R/65536)-6)`; signed truncation
is toward zero. RGB is white, flags 7, size fields 8×8; offsets -3/-6 are
physical pixels after U. Cache `northMM0` is 16×16 with alpha; scalable HUD extent uses the 8px descriptor (inferred).

## Input and UI coordinate boundary (partially read)

`0x0052C5A0` maps virtual UI points to screen points by integer
`(x * width / 2560, y * height / 1920)`. `0x0052C5F0` performs the inverse.
These original per-axis transforms differ from the height-based scalable HUD
specified in ART_DIRECTION §13; retain that deliberate interface choice.

`0x0041A2D0` inverse-transforms the pointer, stores its offset from virtual
centre `(244,1680)` at HUD `+0x1393/+0x1397`, captures the combined camera focus
in `+0x139B/+0x139F`, then invokes `0x0041A410`. These offsets become signed
shorts. In zoomed mode only, first multiply each by float **1.0993103981018066**
(`0x0066C6E8`), truncate toward zero and narrow to signed short again.
With native Q16 sine/cosine tables (`TrapRules.Sine/Cosine`), rotate the combined
products: `rx=trunc0((u*cos-v*sin)/65536)/4`,
`ry=trunc0((v*cos+u*sin)/65536)/4`, each division truncating toward zero.
Products/sums retain native 32-bit arithmetic; do not truncate each product first.
Whole-map tile is `(W/2+W*rx/fitWidth,H/2+H*ry/fitHeight)` using integer fit and
signed division. Zoomed tile is `(capturedFocusRaw+((r<<12)/(1<<zoom)))>>12`
per axis, with arithmetic final shift. Bounds-only slot +0x1FC must pass;
discovery, ownership and walkability do not restrict navigation.

Command4 packs tile X/Y with its other values zero. Session drain `0x00527180`
uses view support/dispatch slots +0x20/+0x24, which resolve to `0x0043B150` and
`0x0043B180`. Initialization installs table `0x0066CE30` into view +0xA2;
its id4 entry reaches `0x00442650`. That handler requests the tile centre and
zero height, clamps through the camera move-target helper, and starts a timed
glide with unchanged yaw. Timing and movement guards have their canonical home
in [camera-paths.md section 8](camera-paths.md#8-fog-of-war-during-an-intro-and-the-return-to-the-heart).

Widget3's draw origin is virtual `(60,1460)`; hit offset `(0,44)`, extent356×312
gives `[60,1504,416,1816)`. Earlier upper-corner widgets win overlapping hits;
their regions are `[20,1460,100,1540)` and `[388,1460,468,1540)`. Lower corners
do not overlap it. The magnifier's actual hit region is `[384,1824,464,1904)`,
smaller than its artwork. Native input is rectangular, not a circle test.
The map's secondary callback remains [0041A7D0](functions/0041A7D0.md); its
full-map mode semantics are not established by the primary navigation reading.

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
rectangle. Their selection cleanup is described below. The other corners
select mode 10, select mode 4, and toggle message-like widget visibility;
their complete panel behaviour is not implemented by displaying the artwork.

Type-0 widget dispatcher `0x00528610` invokes the two callback fields once per
held channel. `0x0052C0A0` maps events F0/F1 to those channels and maintains
capture. `0x00406530` routes unconsumed F0 to Hand_Click and F1 to the secondary
hand handler. The physical-channel producer is described below. Native map widget 3 at
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
The producer [004E4590](functions/004E4590.md) stores direct slab-coordinate
minima and maxima, with the last included tile as the maximum. Do not add one
to these maxima before minimap projection. The concrete rectangle draw
[005C08E0](functions/005C08E0.md) uses exclusive right/bottom raster bounds;
its last pixels are right-1 and bottom-1. That combination is the native rule.
The adapter emits this pale outline for the viewer's Guard Rooms before
creature markers, without a fog gate. It reuses the retained room index and
renders a hollow, one-raster-unit border at display resolution.

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

## Camera footprint (binary)

Read 2026-10-18: complete `0x0041B180`, the whole-map renderer's tail, screen
unprojection and submission helpers, with assembly resolving implicit vector
destinations and vertex arrays. Geometry below is **CONFIRMED**; material
opacity and the active renderer's final clipping remain **UNKNOWN**.

The whole-map renderer `0x0041ADA0` draws the map first, then the footprint
unless `0x0044D870` returns 1: numeric camera modes 1,2,10,11 suppress it. This
tail runs even without a new simulation turn. Other HUD visibility gates apply
outside it; see [hud-visibility.md](hud-visibility.md).

Four full-viewport corners are intersected with horizontal render plane z=-1
by `0x00572920`. For origin C and transformed screen ray D, its floating XY
result is `C.xy + D.xy * ((-1-C.z)/D.z)`, without tile truncation. Its separate
integer pair is not consumed here. The helper contains no parallel-ray,
behind-camera or finite-value test. Mapping render z=-1 to Unity floor y=1
requires the adapter's established vertical sign conversion, not an assumed
zero-height ground plane.

For whole-map width W and height H, subtract `(W/2,H/2)` from each XY point;
rotate by heading h using `(cos(h)*x+sin(h)*y,-sin(h)*x+cos(h)*y)`, with
`h = cameraHeading * pi/1024`. Let U be the UI point transform from virtual
2560×1920 coordinates to screen coordinates. The continuous projection is
`P = U(238,1680) + rotated * S`, where
`S = (90/128)/max(W,H) * ((U(32,1584).x-U(238,1680).x)/(sin(1.5*pi-0.4266275)*0.5))`.
This is the native footprint's floating fit, not the integer tile-raster fit.

Compute centroid M of the four **unclamped** projected P. Independently clamp
each P to the rectangle `U(32,1472)..U(444,1888)`, giving Q. This is corner
clamping, not polygon/line clipping. Direction is `normalize(Q-M)`; border
vertices are `Q + 2*direction` and `Q - 2*direction`, in physical screen pixels.
No final circular mask appears in this body (rechecked 2026-10-18 after the
owner's zoom-out comparison); broader renderer clipping remains untraced.

Pair order is viewport TL,TR,BR,BL; each pair is outward then inward. Outer UV
is `(0.5,0)`, inner UV `(0.5,1)`. Triangle indices are
`0,2,1; 1,2,3; 2,4,3; 3,4,5; 5,4,6; 7,5,6; 0,7,6; 0,1,7`.
The material comes from catalogue resource 242, `GUI/Map/MapCameraBox`, a
16×16 cache image. Runtime art slot stride 27 and material offset 15 explain
the caller's `+0x1995` lookup; its static record is at `0x006ABF2A`.

Submitted RGB is white. The 0.75 argument is **vertex depth, not opacity**:
`0x005794B0` forwards it to `0x00578A80` field +24, and `0x00578C00` copies that
field into the third position component. Preserve the shipped texture alpha;
do not infer a uniform 75% opacity from that argument. Full material-flag
semantics are not established by this reading.

The adapter draws the native texture over an eight-vertex ribbon between turns,
with a circular presentation mask after the owner's zoom-out comparison; the
native downstream clipping remains untraced. The height-scaled HUD uses two
reference pixels per radial offset, rather than two physical pixels, and hides
invalid/behind-camera intersections; native unprojection has no such guard.
OpenKeeper was consulted as a facts-only reference (see THIRD-PARTY-NOTICES.md);
the projection, floor, clamp and border contracts above come from DKII.EXE.
Owner comparison in the original game remains pending.

## Zoom and pointer controls (binary)

Read 2026-10-18: zoom callbacks including tails, zoom raster and marker branches,
pointer-buffer decoder and channel remapping. Zoom is -1..4, initially -1.
The type-0 button callbacks are latched once per press, not repeated while held.
Magnifier F0 increments, F1 decrements. `0x005DDA90` maps buffered offsets
12/13/14/15 to F0/F1/F2/F3, preserving button state from data bit 7. These match
the standard mouse button 0/1 layout, making default F0-left/F1-right
**STRONGLY INFERRED**; concrete device-format setup is not yet checked.
`0x005DD560` swaps F0/F1 when its receiver byte +0x60 is set.

Zoom tails call selection cleanup and clear a queued selection only when its
descriptor's first byte is zero and second byte matches the widget argument.
Entering zoom 0 or whole-map -1 also changes the interface minimap mode and
refreshes the map rectangle. These are UI operations, not simulation zoom rules.

Zoom focus is fixed-point `camera(+0xD68)+camera(+0xD74)` in X, and
`camera(+0xD6C)+camera(+0xD78)` in Y, not the optical camera origin. For z=0..4,
step is `1<<z`. The raster's map-origin pixel is
`(0x400000-(focusRaw<<(z+4)))>>16`, with native 32-bit arithmetic and signed
shift. For positive focus this is `64-ceil(focusRaw*step/4096)`. Raster texels
sample `floor((pixel-origin)/step)`; out-of-map pixels use the backdrop.

Marker projection instead uses `64+(tile-focusTile)*step+entityFractionPixels-
focusFractionPixels`, with native signed fractional extraction. For nonnegative
positions this simplifies to `64+(entityRaw>>(12-z))-(focusRaw>>(12-z))`.
Thus tile-raster and marker phases differ at fractional camera pixels. Door
strokes omit the entity fraction along their span axis: vertical Y and
horizontal X start on the tile boundary, not the door's centre. Reporting
circle radius becomes the configured byte distance times step; possession and
Alarm pulse radii remain raster-unit counts. Room rectangles retain the half-tile
fraction of the preceding Heart/Guard Post point, matching centre-based bounds.

The adapter magnifier uses primary/secondary presses for zoom in/out. Its GPU
projection follows live camera panning between turns; DK2 refreshes that raster
on turn changes. This presentation choice moves no simulation entities.

Widget3 uses the same type-0 poll as the magnifier: once per primary press,
retaining capture until release without calling navigation again on movement.
The primary held channel takes priority over the secondary channel. Its complete
poll and GUI-manager capture paths establish click navigation, not continuous
drag panning. The independent secondary map-mode callback remains unimplemented.

The adapter forwards primary press edges to the existing camera glide without
world commands. Full held-channel capture arbitration remains part of Step6.3.
