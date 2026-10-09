# The classic look in Unity (gap 5)

Decision: ADR-0408. Goal: the Unity scene draws a level as DK2 draws it, from the player's own
install, so a level can be played and judged by eye. Today it draws coloured boxes
(`unity/README.md`). Not started; nothing below is built.

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

## Step 3: objects and furniture

Static meshes the view already lists (`ObjectView`). Read any state a mesh depends on (a gold
pile's size, a chicken's egg) before keying it.

Gate: a Hatchery, a Treasury with gold, a Lair with beds, beside DK2's.

## Step 4: creatures

1. Play `KmfAnimation` frames in Unity: one mesh per frame or a vertex-animation texture, chosen by
   measuring both on a crowd. Interpolate between key entries as DK2 does (ADR-0390).
2. Research which clip DK2 plays in which state from the binary, and close Q442's remaining link
   (the animation phase to the drawer's time; the rate from the descriptor is built, ADR-0390).
3. Settle Q443 (the second UV family) for the clips that use it.
4. Smooth a creature's position between ticks; today it jumps once a tick (`unity/README.md`).

Gate: imps digging and claiming, and a creature walking, eating and sleeping, beside DK2's.

## Step 5: camera and lighting

Research first; none is read yet. DK2's camera angle, field of view, zoom range and pan; its
lighting model (what lights a tile, how torches fall off, how undiscovered and fogged tiles are
darkened). This is the step that turns DK2's meshes into DK2's look.

Gate: the owner, at the same spot in both games, cannot tell which camera is which at a glance.

## Not in this plan

The HUD and panel, the Hand of Evil's cursor, spell and trap effects, sound, and the modern look
(M11), which ADR-0408 puts on the same pipeline and which is judged against this one.

## Open

- Q71 (water and lava beds, the renderer's use of the piece lists), Q442, Q443.
- New research to file as questions when each step starts: clip per state, camera, lighting,
  owner colours if unread.
