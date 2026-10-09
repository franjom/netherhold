# Mechanic: how DK2 draws its walls (rendering, not simulation)

Source(s):
- Simon Schreibt, *Dungeon Keeper 2 – Walls*, 6 June 2013 —
  https://simonschreibt.de/gat/dungeon-keeper-2-walls/ (read 2026-09-04, at the
  owner's suggestion). One of the seven links on OpenKeeper's README Resources
  list; recorded in `external-resources.md` as "for the Unity view, not yet",
  and the Unity view is now.
- Referenced from that article: trass3r's KMF→OBJ conversion scripts, and
  *Life of a frame in Dungeon Keeper 2* (a capture of every draw call in one
  frame).

Original version: the article does not say which patch; screenshots are of a
retail/GOG install, so v1.7 is likely and unconfirmed.

**Read this first: none of it is simulation.** Wall *appearance* is downstream of
tile state and changes no rule. It is filed here because the Unity view is the
next milestone and because two claims in our own asset layer turned out to be
answerable from it — one corroborated, one shown to be a simplification.

---

## What the article establishes, and how

The author verified things two ways: a wireframe capture of the running game, and
extracting the original meshes from `Meshes.WAD` and opening them in 3ds Max. He
is explicit about which conclusions rest on those and which are inference.

### CONFIRMED (he looked)

1. **The dungeon is not a grid of cubes.** In wireframe there are "no cubes
   standing side by side" — it is *"solid mass where the needed intersections are
   cut into"*, and the polygon topology changes around a cut. So the visible
   terrain is not one mesh instanced per tile.
2. **The authored wall meshes are straight and undeformed.** The extracted
   training-room wall meshes are *"just straight lines"* apart from a small
   alcove and a shield relief. In game the same meshes are visibly bent.
3. **Which wall pieces appear depends on the room's size and shape.** Building a
   training room one tile at a time makes the surrounding walls rearrange:
   *"the different tiles are sorted depending on the size of the room"*.
4. **Damage is a texture change, not a mesh change.** *"before walls are
   destroyed, they get different textures for the damage state"*.
5. **The top faces of walls are textured too**, not left blank.

### The author's INFERENCE, which he flags as such

**A runtime deformation — a "noise modifier" — displaces wall vertices.** He
reaches this from (2) plus *"most times I can't see repeating patterns"*, and
names the alternative he rejects: the pieces *could* be authored in several
variants and assembled by code. He also notes the displacement is sometimes
large, not a subtle jitter — visible when a column of earth is dug out.

Worth keeping the alternative in view. Both readings produce "no visible
repetition"; the extracted meshes make runtime deformation the better
explanation, not a proven one.

### UNKNOWN, and the article does not claim otherwise

Nothing about texture sizes or atlases, no mesh or sub-quad counts, no
adjacency/autotiling rule beyond "depends on room size", nothing about lighting
or torches, and nothing about how floor edges are handled. **Do not cite this
note for any of those.**

---

## What it changes here

### It corroborated a guess, which is the smaller half

`TileView.HealthPermille` was added on the reasoning that "a renderer should be
able to show damage without knowing the terrain table". Fact (4) says DK2 does
exactly that and does it by swapping textures — so the fraction is the right
thing to hand a renderer, and the eventual damage assets are *textures per state*
rather than meshes. That was a guess and is now corroborated.

### It refuted a simplification, which is the larger half

`AssetCatalogue` asks for **one mesh per solid terrain**. Fact (3) says a DK2 wall
is drawn from a *set* of pieces chosen by the room's size and shape, and fact (4)
adds per-damage-state textures. **One key per terrain is therefore too coarse for
a faithful renderer**, and the note in that file now says so instead of
presenting one-per-terrain as complete.

Deliberately **not** changed yet, and the reason matters: `AssetRef` already
carries a variant segment (`terrain.rock.mesh.corner` costs nothing to express),
so the shape is right and the enumeration is not. Enumerating variants now would
mean **inventing the variant set** — how many pieces, keyed on what — from an
article that says only "depends on the size of the room". That is precisely the
kind of plausible invention this project refuses (`AGENTS.md`: *do not
confidently invent undocumented DK2 behavior*). The catalogue grows a variant
when something can name the variants.

### It gave the converter its route

The article's tutorial is the extraction path for `Meshes.WAD`: DK2WADTOOL, then
Hoenir's 010 Editor template `Dungeon Keeper 2 KMF.bt`, and trass3r's later
**KMF→OBJ scripts**. Two things follow. The 010 template is the same category of
third-party format documentation this project already uses for KWD, so it is
inside the licence boundary — *facts and format, cite the source*. And **no
converted mesh may be committed** (plan §7.1); the converter is a step the user
runs against their own copy, which is exactly what `FileAssetProvider` assumes by
only ever reading `build/converted`.

---

## Piece selection in DKII.EXE (Q71)

Read 2026-10-05 in Ghidra. **CONFIRMED** for the dispatch below; which art slot
is which piece is **STRONGLY INFERRED**.

- The terrain renderer `0x0043C310` asks `0x0043E320` for the tile's piece list
  and submits each entry through its render sink's vt`+0xF0` / vt`+0xF4`.
- `0x0043E320`: an unexplored, unrevealed tile is drawn as terrain `0x1B` (or
  `0x26` in one tile-flag case). A terrain without the room flag (`+0x17B` bit
  `0x10`) goes to `0x0043E860`, **not read**. A room terrain goes to
  `0x00443610`.
- `0x00443610` switches on the room definition's `TileConstruction` byte
  (`+0x452`): 2 Quad, 3 3×3, 4 3×3 rotated, 5 Normal, 7 DoubleQuad, 8 5×5
  rotated, 9–13 the hero gates each have their own builder; 1 Complete uses one
  art slot (`+0x2A`); 6 CenterPool draws nothing here. Arms 1, 5, 7, 9, 12 and 13
  then call the wall chooser `0x00444F10`.
- `0x00444F10`, per side (N, E, S, W): when the neighbour outside the room is a
  wall, it picks a mesh from the room definition's art slots `+0x13C`, `+0x157`,
  `+0x172`, `+0x18D` or `+0x1A8`, by whether the neighbouring sides continue the
  wall (ends and corners) and, in a room with a wall-furniture phase (room
  `+0x92`/`+0x96` ≥ 0), by x or y parity plus the phase — the phase the torches
  use (`wall-torches.md` §2). Room type `0x12` picks by offset from the room
  origin (`+0x2D`/`+0x2E`) and reads `+0x121`; type `0x15` adds no wall; type
  `0x17` picks by the gate's orientation (`+0x9B`) and reads slot
  `+0x7F + index·0x1B`.

So the article's "depends on the size of the room" is the `TileConstruction`
arms and the neighbour tests, not a size test in the wall chooser. Plain rock
and earth walls (`0x0043E860`) remain unread. The deformation (Q72) is now read:
`terrain-lattice.md`. It uses no CRT `rand`; its jitter is the game's LCG on seeds
made from the tile coordinates.

---

## Open questions this raises

- **Q71 — what picks a wall piece?** Room walls: above. Open: the art-slot names
  in `Rooms.kwd`, and plain terrain's `0x0043E860`.
- **Q72 — is the wall deformation deterministic per tile?** If a keeper digs a
  wall and rebuilds it, does the same tile deform the same way? If yes, it is a
  function of tile position (a hash — which is how `PlaceholderProvider` derives
  its colours, for the same reason) and a renderer can reproduce it without
  storing anything. If no, it is per-instance random and belongs in view state.
  **The owner can answer this by looking**, and it is on the observation
  checklist. Pure presentation either way: nothing in the simulation may depend
  on the answer.
  **Answered from the binary (2026-10-09): yes.** A per-tile lattice bends every
  vertex; it is a pure function of the tile's position and its 3×3 neighbourhood
  (`terrain-lattice.md` §9).

Confidence: **CONFIRMED** (facts 1–5, from the author's own wireframe and mesh
extraction) / **CONFIRMED** (runtime vertex deformation, read from the binary
2026-10-09 — the author's inference was right; algorithm, constants and tables in
`terrain-lattice.md`) / **UNKNOWN** (plain-terrain pieces, textures, lighting,
floor edges).

Implementation: the general room wall chooser is built (`RoomWalls`, `WorldView`, the
`wall4`..`wall8` room assets; ADR-0392). The full read, room walls and the floor and
plain-terrain pieces, is in `wall-pieces.md` and `floor-and-terrain-pieces.md`.

Tests: none — no claim here is testable against the simulation, which is the
point of filing it as rendering.
