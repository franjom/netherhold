# The classic look in Unity (gap 5)

Decision: ADR-0408. Goal: the Unity scene draws a level as DK2 draws it, from the player's own
install, so a level can be played and judged by eye. Steps 0–3 are merged at
6005ff9f. Step 4 is underway on classic-look/step4; see
2026-10-10-classic-look-step4.md for the current batch and remaining gates.

Rules for every step:

- What a tile, object or creature shows (piece, rotation, texture, clip) is decided in
  `DK2.Presentation` and tested headlessly. `DungeonScene` places what the view lists.
- Every reading of DK2's renderer goes through the binary first (AGENTS.md source order) and lands
  in its research note, as `wall-pieces.md` and `floor-and-terrain-pieces.md` did.
- Original files and anything decoded from them stay out of the repository.
- Each step ends at a gate: a screenshot of ours beside DK2's at the same place and camera, and the
  owner's acceptance. A step is not done on a green build alone.
- Run `unity/sync-assemblies.ps1` and build `unity/DK2.Unity.Compile` after every change under
  `src/` or `Assets/DK2`.

## Step 0: one piece on screen

Proves the route of ADR-0408 end to end before anything is built on it.

1. Make the readers Unity-loadable: `KmfReader`, `EngineTextureArchive`, `Dk2TextureDecoder` and
   the WAD access they need on netstandard2.1 through `UnityBound.props`. Either all of
   `DK2.Convert`, or a reader assembly split from the writers if they need net10.0 APIs. Add it to
   `sync-assemblies.ps1`, with an engine-neutrality test asserting netstandard2.1 as
   `AssetArchitectureTests` does for `DK2.Assets`.
2. Define the engine-neutral output: mesh data (positions, normals, UVs, triangles, one material
   index per primitive, texture names) and the existing `RgbaImage`. No Unity type in `src/`.
3. Let the converted tier resolve a key to an install entry (ADR-0408 §2), with the placeholder
   still answering when no install is present.
4. Install URP (`com.unity.render-pipelines.universal`), a pipeline asset and a renderer, and move
   the placeholder materials to URP shaders. Keep URP's API out of the adapter's code where a
   material or shader name will do: `DK2.Unity.Compile` builds against the engine's module
   assemblies, not package assemblies, and would need the URP DLLs added otherwise.
5. Build one static piece in `DungeonScene`: one Lair wall piece or a gold pile, with its texture.
6. Settle the frame conversion: DK2's axes, handedness and triangle winding against Unity's, and
   the size of one tile. `GlbWriter`'s `(x, -z, y)` conversion to glTF's right-handed frame is
   the reference: the converted Imp holds its pick in the right hand, as DK2's does (Q568). Unity
   is left-handed, so that frame is a starting point, not the answer. Pin the result with a test
   on an asymmetric piece; a symmetric one cannot show a mirror.

Gate: the piece in Unity, beside the same piece in DK2, the same way round, the same size and
textured the same.

Status (2026-10-09, branch `classic-look/step0`): items 1–3, 5 and 6 are built and tested.
`DK2.Art` holds the readers, `ArtSourceMap` the key-to-KMF map, `InstallArtProvider` the
install-backed tier, `RenderMesh` the frame, `ClassicMeshes` the Unity side; the runner draws
objects from the install. Item 6 found that the map itself was mirrored (ADR-0411).
Item 4 has run: the project renders through `Assets/DK2/Rendering/DK2-URP.asset`.
`ClassicLookSetup.RenderStep0` writes the gate's pictures to `build/classic-look/`: the Lair's
five wall pieces textured and facing the right way, LAIR4 and LAIR5 mirror twins, and Level 1
from the opening camera. The owner compared that Level 1 layout with DK2's: the same, only the
camera angle differs, so the map is no longer mirrored (ADR-0411). Level 1 starts with one
object (a Special box in rock), so play mode also draws creatures (the idle KMF's first frame,
unturned; step 4 animates them) and wall torches from the install. In play mode the owner saw the
imps textured: the SRP Batcher fault is the batch render's only. Ordinary floor is 0x1000, one
tile (`0x004545C0`, read), and every solid terrain KMF spans 1 to 2, so a wall stands one tile
above the floor and a torch's 1.5 is mid-wall (STRONGLY INFERRED: the KMF's heights are taken to
share the floor's frame); the owner judged an imp beside a wall against DK2's Level 1 and found
the proportions right.
Found on the way: the gold pile is a textured card whose black needs its material's
transparency (KMF flags, UNKNOWN); a wall's part below the floor (0 to 1) is never seen in DK2
(owner, 2026-10-09), so step 1 need not draw it (STRONGLY INFERRED from that view; the engine
side is unread);
a terrain's side piece faces out of its block, so it is culled from inside.

## Step 1: plain terrain

1. Build the floor and plain-terrain piece choice from `floor-and-terrain-pieces.md` §4 (the
   TileConstruction arms; the Normal floor's quarters by the neighbour mask) into the view, next to
   `RoomWalls`. This ends ADR-0034's one mesh per solid terrain: the variant keys come from it.
2. Tests on real data: the piece and rotation for known tiles of Level 1, against the note.
3. In Unity, combine the pieces into chunks and rebuild only the chunk a changed tile is in. A
   whole map as one object per piece will not hold the frame rate.
4. Check whether the claimed floor's owner colour and the damage texture swap
   (`wall-rendering.md`) are read; read them in DKII.EXE if not.

Gate: Level 1 at the start, rock, earth, gold and the dug-out area, beside DK2's.

Status (2026-10-09, branch `classic-look/step1`): items 1 to 3 built. `WorldView` picks each
plain tile's `TerrainPieces` (faces, top, whole floor, player quarters; floor-and-terrain-pieces.md
§5), `AssetCatalogue.ForTerrainPieces` keys them, and Unity's `TerrainChunks` draws them merged per
16×16 chunk, rebuilding only a changed tile's chunk. The owner compared Level 1 with DK2's: the
pieces look right, but DK2's walls are bent and their corners softened: a runtime lattice
(`terrain-lattice.md`, Q72 answered: a pure function of the map), now built as `TerrainLattice`
and applied per vertex by `TerrainChunks`, with DK2's HighWalls option on by default. Item 4: the
owner colour is the claimed sets (player id − 1, STRONGLY INFERRED); the damage frame's texture
index is unread. **Accepted** by the owner (2026-10-09) on a same-spot comparison with DK2: the
shapes match, and only the colour differs (step 5).

## Step 2: walls and rooms

1. Draw the room wall pieces `RoomWalls` already chooses (ADR-0392), including piece 8 at the
   furniture phase.
2. Room floors for each TileConstruction arm a shipped room uses (`floor-and-terrain-pieces.md`
   §4): 3×3 (Portal) and 3×3 rotated, DoubleQuad (Prison, Temple, Combat Pit), the 5×5 Heart and
   its destroyed variant, Quad (bridges), and arms 9–13. Arm 1 is used by no shipped room. Which
   rooms use 4 and 9–13 is still to check.
3. Read the water and lava beds (`0x0043E440`, Q71's open part) and draw them.
4. Torches are already lights (ADR-0391); give them their meshes.

Gate: a played Level 1 with every room it has, beside DK2's. Measure the frame rate on the
largest shipped map here.

Status (2026-10-09, branch `classic-look/step2`): items 1 to 3 built from DK2's builders
(`room-floors-and-liquids.md`). `WorldView` gives each room tile a `PieceSet` (Normal, Quad,
3×3, 3×3 rotated, the Heart, DoubleQuad) and its walls, piece 8 on the room's wall phase; each
liquid tile, and each bridge, a bed; and a `LiquidSurface`, which Unity's `LiquidSurfaces` waves
as DK2's sine mode does. Not built: the hero gates, the destroyed Heart, the Temple's pool
surface, the ripple water, water's texture animation. Item 4 was step 0's. The owner compared
level 7: rooms and lava look right, a Lair's decorated walls alternate as DK2's do; they found the
Hatchery's coops on its rim, a simulation bug (the coop walk takes only interior tiles,
`hatchery.md` §2), fixed alongside. The frame rate is not yet measured.

## Step 3: objects and furniture

Static meshes the view already lists (`ObjectView`). Read any state a mesh depends on (a gold
pile's size, a chicken's egg) before keying it.

Gate: a Hatchery, a Treasury with gold, a Lair with beds, beside DK2's.

Status (2026-10-10, branch `classic-look/step3`): read from the binary into `object-drawing.md`
(the draw path, art slots by state, position, heading, scale) and `furniture-placement.md` (where
each room pass puts its objects). `ObjectView` carries a point, height, heading and scale;
`WorldView` places the simulation's objects by the placing arm (floor and wall furniture, coops,
beds, the Temple Hand, the lever, crypts) and adds, as `Decorations`, what DK2's rooms place that
ours does not hold: the Heart's set, room pillars, Guard Posts, Workshop pillars, candlesticks, the
Prison door, the Crypt bell, roulette wheels, the wheel beside Torture coals, and a treasury or
heart tile's gold as Urn1, Urn2 or the Chest by fill. Torture wheels beside coals/water are now
simulation devices. A floor pile scales with its gold, a Book Case
shows its selector's variant, and a sleeping creature's bed is hidden. With the install, an object
with no art draws nothing, as DK2's does. Supported object-state animations are built
in step 4 (ADR-0438). The headings DK2 draws on the simulation stream for beds and store
piles remain unbuilt. Tilts and offsets,
Prison door facing, KMF transparency, Hero Gate floors/faces and ruined Heart floors are built.
Q581's wall-machine working positions and the room initialization/navigation fixes are built
under ADR-0419, hash/save version 215. Owner screenshot checks remain in HANDOFF.md.

## Step 4: creatures

1. Play `KmfAnimation` frames in Unity: one mesh per frame or a vertex-animation texture, chosen by
   measuring both on a crowd. Interpolate between key entries as DK2 does (ADR-0390).
2. Research which clip DK2 plays in which state from the binary, and close Q442's remaining link
   (the animation phase to the drawer's time; the rate from the descriptor is built, ADR-0390).
3. Settle Q443 (the second UV family) for the clips that use it.
4. Smooth a creature's position between ticks; today it jumps once a tick (`unity/README.md`).

Gate: imps digging and claiming, and a creature walking, eating and sleeping, beside DK2's.

Implementation and verification status: 2026-10-10-classic-look-step4.md.
The owner comparison gate remains pending.

## Step 5: camera and lighting

Research first; none is read yet. DK2's camera angle, field of view, zoom range and pan; its
lighting model (what lights a tile, how torches fall off, how undiscovered and fogged tiles are
darkened). This is the step that turns DK2's meshes into DK2's look.

Gate: the owner, at the same spot in both games, cannot tell which camera is which at a glance.

## Not in this plan

The HUD and panel, the Hand of Evil's cursor, spell and trap effects, sound, and the modern look
(M11), which ADR-0408 puts on the same pipeline and which is judged against this one.

Deferred beyond Step 4's creature playback scope (owner observations, 2026-10-11):

- Room construction visual effects for single-tile placement and drag-release builds
  are absent. Research the original effect selection, placement and timing before adding playback.
- Prison rat spawning and wandering are absent. The shipped Rat is object 11
  (objects.md); its original spawn and movement rules need binary research.

## Open

- Q71 (water and lava beds, the renderer's use of the piece lists), Q442, Q443.
- New research to file as questions when each step starts: clip per state, camera, lighting,
  owner colours if unread.
