# The dig-tag highlight

How DKII.EXE draws a tile the viewing player has tagged for digging. Read
2026-10-10 from the binary (claude). The owner's screenshots show a static
blue-violet tint over the tile's whole visible mesh, texture still visible.

## 1. The state: presentation-grid bit 13

- The bridge's per-tile word grid (bridge +0x326, cell helper
  [0x004435E0](functions/004435E0.md)) carries the tag look in bit 13 (0x2000).
  [0x00443540](functions/00443540.md) writes it. CONFIRMED.
- Bridge command 7 ([0x00447D50](functions/00447D50.md)) clears it on every
  tile, then sweeps the map. On a Solid terrain (+0x17B bit 0) it asks the task
  list of the bridge's own player (thing table [bridge vt+0xBC] +0x1105) for the
  tile's task kind ([0x00508290](functions/00508290.md)). Kind 1 or 2 sets the
  bit; anything else clears it. Each change also calls world slot +0x1F8(x, y,
  on/off) (0x004481BD..0x00448268). CONFIRMED for the code; that kinds 1 and 2
  are dig and mine gold is LIKELY (the kind scan's own record).
- Two more writers: [0x0043D9B0](functions/0043D9B0.md) (Ghidra splits it at
  0x0043D9E3), called by the event handlers 0x00442570 (on) and 0x004425C0 (off),
  writes bit 13 over a rectangle under its own conditions (tile word +8 bit
  0x2000, byte +0xB bits 0x40 and 1, the terrain's Taggable and
  RevealThroughFogOfWar bits; not read further); 0x0043DC40 is called by
  0x00442610. All three handlers first compare the event's player (+0x10) with
  vt+0xBC, so vt+0xBC is the local player. So the rule above is read for the
  command-7 sweep only; ours keys the look on the viewer's tag.
- [0x004484A0](functions/004484A0.md) clears the bit when the terrain is not
  Taggable (+0x17B bit 3), for example once the tile is dug, on a tile that is
  explored (+9 bit 0x20), or unexplored with +0xB bit 0x40 on a
  RevealThroughFogOfWar terrain. CONFIRMED.
- So only the viewer's own tags show, and the look does not depend on the owner's
  colour. Tags of other players are never drawn. LIKELY (it follows
  from which task list is read).

## 2. From the tile to its pieces

- [0x0043E860](functions/0043E860.md) (non-room terrain) sets bit 0 of
  list +0x2EC, a flag for the whole tile, in either of two cases:
  - the cell has bit 13 (0x0043E928, 0x0043FC64); or
  - the tile lies inside the bridge's live drag rectangle (+0x10/+0x14
    inclusive, +0x18/+0x1C exclusive) and its terrain is Taggable. A closed
    secret door in the rectangle also qualifies when its tile word +8 & 0xFFF
    differs from world vt+0x264 (0x0043FC6A..0x0043FCD1). This is the live
    preview while the button is held.

  The 2026-10-06 reading that the flag is "set for every PlayerColouredWall"
  was wrong: the value tested at 0x0043FC9B is the secret-door result of
  0x004434F0, stored at 0x0043E8F8. CONFIRMED.
- Room walls use a per-piece bit 0 instead (+0x1A on a quarter, +0x15 on a whole
  piece). It is set when the wall tile outside the room face is tagged or is in
  the rectangle and Taggable (wall-pieces.md, Notes). So when a room wall
  replaces a tagged rock's face, that wall piece is tinted too. CONFIRMED.
- [0x0043C310](functions/0043C310.md) submits every quarter and whole piece with
  a mode:
  - mode 1 when the tile flag or the piece flag has bit 0;
  - otherwise mode 2 when either has bit 1;
  - mode 2 always when bridge +0x139B is set and world vt+0x2D8(x, y) is true.

  This covers the top and every face the builder emitted, so a face hidden by a
  solid neighbour (never emitted) shows no tint. CONFIRMED. What sets bit 1, and
  what +0x139B means, are UNKNOWN.

## 3. The look: one colour, one overlay material, frame 0

- The sinks [0x0059A050](functions/0059A050.md) (whole piece) and
  [0x0059A200](functions/0059A200.md) (quarter) handle the modes:
  - **Mode 1** replaces the piece colour with **(128, 128, 200)**: floats
    0x3F008081, 0x3F008081 and 0x3F48C8C9, each a value /255. It sets render
    flag 0x200 and attaches the overlay {material id = global 0x006FCD77,
    frame fraction 0.0}. CONFIRMED.
  - **Mode 2** is **(255, 0, 0)** with the material id at global 0x006FCD73.
    CONFIRMED.
- The draw ([0x00586190](functions/00586190.md), slot 2 of vtable 0x0066FD8C)
  handles a highlighted piece (flags & 0x300) submesh by submesh. It resolves the
  piece's own texture and the overlay material's texture (0x0057C780), and pins
  the overlay to frame 0. Over an opaque base it queues one batch entry with two
  passes, one texture each: the base texture in the base state, then the overlay
  in the overlay state with 0x280 cleared. Over a translucent base it queues two
  entries, base then overlay. The base is always drawn first. CONFIRMED for the
  control flow. The flush is 0x0058F640 → 0x0058A150, which draws pass i with
  state i (§6).
- **No animation.** The overlay frame fraction is the constant 0.0, the colour
  is constant, and nothing in the path reads a clock. CONFIRMED for this path.

## 4. What the remake should do

For every emitted piece of a tile with bit 13 (top, faces, and the room-wall
pieces facing it):

1. add (128, 128, 200) to the piece's vertex lighting, in 0..255 units;
2. draw the usual texture × light;
3. draw an additive (ONE, ONE) pass of BlueTexture over it, without depth
   writes. Flat (63, 64, 255) is a HYPOTHESIS: if the pass reuses the base
   piece's diffuse, it scales with the light instead (§5, §6).

Do not animate it. The highlight follows the viewer's own dig/mine tasks on
Taggable Solid terrain: Rock, Gold, Gems, Reinforced Wall, and whatever else is
Taggable. During a drag, the Taggable tiles inside the bridge rectangle
(+0x10..+0x1C, half-open on the max side; Solid not tested) show it too. Who
fills that rectangle, for which tools and with which corners, is unread: the
remake's preview for the tag tool only, corners inclusive, is a HYPOTHESIS. The terrain art 3 "… TOP TAGGED" (+0x60) is not part of this path: no piece
builder in 0x0043xxxx..0x0044xxxx reads terrain +0x60. Its consumer stays
UNKNOWN (floor-and-terrain-pieces.md §5).

## 5. Open

- The trigger of mode 2 (red).
- Whether, and where, the vertex light sum is clamped (0x0057BF00 is unread).
  Whether the overlay pass's vertices also carry its material's own colour
  term, which is (255, 255, 255) for an AA_ material.
- The hover outline at the cursor tile: not looked at.

## 6. The overlay material and the blend (2026-10-10, follow-up)

- **What the globals are.** 0x006FCD73 and 0x006FCD77 are world fields +0xA36B
  and +0xA36F of the world object 0x006F2A08. A custom base+displacement scan
  found every other base-and-offset pair empty. The world initialiser
  [0x005099A0](functions/005099A0.md) fills them through the render engine's
  load-material slot +0x54 ([0x0059A6A0](functions/0059A6A0.md)), with flags
  0x20000 (0x00509FDF..0x0050A0B7):

  | Field | Global | Texture | Kind |
  |---|---|---|---|
  | +0xA363 | 0x006FCD6B | RedTexture | 2 |
  | +0xA367 | 0x006FCD6F | BlueTexture | 2 |
  | +0xA36B | 0x006FCD73 | RedTexture | 3, the red highlight |
  | +0xA36F | 0x006FCD77 | BlueTexture | 3, the tag highlight |
  | +0xA373 | — | RedTexture | 1 |

  The table runs on after +0xA373; that part is unread. CONFIRMED.
- **Material names.** Kind 1 names the material "S_<tex>", kind 2 "A_<tex>" and
  kind 3 "AA_<tex>". The kind sets material flags 0, 4 and 0x10 (0x0059A6A0). So
  the tag overlay is the material **"AA_BlueTexture"**, built on the texture
  "BlueTexture". CONFIRMED.
- **The texture.** The install has `DK2TextureCache\EngineTextures.dir/.dat`
  entry **BlueTextureMM0**: 16×16, no alpha, every pixel **(63, 64, 255)**.
  RedTextureMM0 is (255, 64, 65). CONFIRMED by decoding them with our
  EngineTextureArchive.
- **The blend is additive.** The material constructor
  [0x0057C420](functions/0057C420.md) gives flag 0x10 the render state
  (base & ~0xC0) | 0x201. That clears z-write (0x80) and forces the material's
  own colour to (255, 255, 255). The state applier
  [0x00589250](functions/00589250.md) maps state bit 0x1 to SRCBLEND = ONE,
  DESTBLEND = ONE with blending on: **additive**. For comparison, 0x20 ("A_",
  alpha) is SRCALPHA/INVSRCALPHA and 0x1000 is ZERO/INVSRCCOLOR. Texture stage 0
  is MODULATE(TEXTURE, DIFFUSE) at 1×, never 2× (0x005899F0, 0x00589250).
  CONFIRMED.
- **The colour is added light, not a multiply.** The vertex-colour loops
  (0x0057B6D0, 0x0057B0E0) build each vertex's diffuse as the sum of four terms:
  - the vertex's own light (+0x1C..+0x24);
  - the material colour (+0x21..+0x29);
  - the render item's colour (instance +0x14..+0x1C, from the sink);
  - the global ambient 0x00760A98..0x00760AA0.

  CONFIRMED for the sum. So mode 1 **adds (128, 128, 200)** (0..255 units) to the
  piece's lighting, which brightens and blues the base pass. The additive
  BlueTexture pass then adds about (63, 64, 255) × its own diffuse on top. Hence
  a tile brighter and more violet than its neighbours. The clamp is unread.
- **The colour of an untagged piece.** The builders write zero colour bytes
  (+8..+10), so it adds nothing. A terrain with +0x17B bit 0x20000000 copies its
  own +0x1B9/+0x1BB/+0x1BD in instead. So 128 means "+128 of light", not "half"
  and not 1.0 under a 2× modulate. CONFIRMED for the builders that were read
  (0x0043E860).

## Built (2026-10-10)

`TileView.Tagged` is the viewer's tag, or a Taggable tile inside a held tag drag
(`WorldView.PreviewTags`); the pieces no longer change with it (art 3 is not drawn). Unity's
`TerrainChunks` draws every piece of such a tile, and a room wall facing one, with its
material's tagged twin (`ClassicMeshes.Tagged`: URP emission of texture × (128, 128, 200), standing
for DK2's added light), then again with `AA_BlueTexture` as flat (63, 64, 255) (HYPOTHESIS, §4) additively, no depth
write. URP's lighting is not DK2's, so the sum is the same shape, not the same numbers (step 5).
