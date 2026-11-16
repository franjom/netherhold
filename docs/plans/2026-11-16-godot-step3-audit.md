# Godot migration step 3: the world-rendering audit

**Status:** audit, 2026-11-16, for [the migration plan](2026-11-15-godot-migration.md) step 3. It was a read-only
pass over the Unity world-rendering sources at HEAD 5afa63b7. Nothing was built or run. Line numbers are as of that
HEAD and go stale as moves land. Paths without a folder are under `unity/DK2.Unity/Assets/DK2/` (shaders under its
`Resources/`). Each section maps one world feature; the move order (3a-3n) is at the end.

The classes are step 2's: **B** is a DK2 rule still in Unity, which moves to `src/` with tests before Godot uses it
(ADR-0805); **P** is engine plumbing that Godot rewrites; **S** is already a call into `src/`. Lighting is the owner's
exception: its look is tuned per engine, so the fill, fade and exposure terms are P, while the DK2 light arithmetic the
shader copies is B.

## What Godot starts from

`godot/DK2.Godot/InGameHud.cs` runs Level1 inline (no tick worker, so no presented copy), owns one `WorldView`, a
`FrameDriver` and a `ScriptedViewDirector`, and draws the HUD panels through `HudCanvas`, over the `WorldScene` that 3a
added (a `Node3D` with its `Camera3D` fixed at the level-start pose). The minimap is drawn from the level-start focus
and `ClassicCamera.LevelStartYaw`, glides and focus moves are printed, and world hover only has its type text. The
project renders with Forward+ at 1920x1080.

## One frame decision for every feature

Every shared rule speaks the renderers' frame of `RenderWorld` (`src/DK2.Presentation/RenderWorld.cs`): X east, Y up,
Z north, tile `(x, y)` at `(x + 0.5, 0, -(y + 0.5))`; `RenderMesh.ToRenderFrame` maps KMF's `(x, -z, -y)`, and
`ClassicCameraPose` gives position, forward and up in it. Unity is left-handed; Godot is right-handed (camera looks down
local -Z). The same numbers in Godot draw the dungeon mirrored east-west, and every rotation and camera basis inherits
the mirror. Decided (open point 1): one explicit conversion in one place, `RightHandedFrame` in `DK2.Presentation` with
tests, since a mirrored root `Node3D` still needs the camera, the picking rays and every billboard facing flipped by hand.
It negates Z on every position, normal and direction entering Godot (and on rays leaving it) and turns a rotation `R`
into `S·R·S` with `S = diag(1, 1, -1)`; the camera gets the converted pose. Triangle indices keep their order: the
geometry and the camera are mirrored together, so the winding on screen is unchanged, and reversing it would draw the
back faces.

`RenderEulerRotation.cs` (18 lines) composes `Euler(0,Yaw,0)·AngleAxis(Roll,forward)·Euler(Pitch,0,0)` with no test;
3f moves that composition into `DK2.Presentation` as a `System.Numerics` matrix with tests so neither engine composes
it by hand.

## Terrain

- **Unity.** `TerrainChunks.cs` (697), driven by `DungeonScene.CreateTile`/`UpdateTile` (`DungeonScene.cs:365-406`)
  through `IRenderTarget`, and `DungeonScene.Flush` (`:902-909`). Chunks of 16x16 tiles, one `Mesh` per chunk with a
  submesh per opaque material; blended triangles go to `TransparentTerrain.cs` (572).
- **Shared (S).** `ScenePresenter` diffing and push order, `TileView` (pieces, keys, walls, tints, fog), `TerrainPieces`,
  `TerrainLattice.Deform`/`Invalidate`, `LatticeDirtyTracker`, `ClassicTerrainTint.PieceColour`, `RenderMesh.FromModel`,
  `TriangleDepthOrderBuffer` (DK2.Art), `TileHeights`.
- **B, moved in 3a.** The whole per-tile build is now `TerrainTileBuilder` (`Prepare` and `Build`, emitting positions,
  normals, UVs, light anchors and triangles per `TerrainMaterialKey`: texture, blend, self-illumination, variant plain,
  tint, tag, red, fog alpha or tag overlay). It holds which pieces a tile places with their quarter turns and quadrant
  offsets, the inverted lighting normals of tops and top quarters, a room tile's floor set, extra piece and walls (a wall
  tagged when the tile it faces is tagged, tinted by its neighbour except whole pieces below 7), hero faces with halves 4
  and 5 swapped, a liquid's or bridge's bed, the sight-fog overlay piece, the lattice bend in DK2's frame, and the key
  each triangle group takes (the additive tag pass is a second key). Unity maps a key to a `Material`, Godot to a
  `ShaderMaterial`.
- **B left in Unity, for 3c.** The dirty window: a change invalidates the lattice cell and marks the 5x5 tiles stale,
  and a tag or tint change dirties the neighbours (`TerrainChunks.cs:183-198`). The flush budget, the thread-pool build
  and lowest-chunk-first upload (`:206-244`, ADR-0457, ADR-0696) are a performance policy both engines want; the
  scheduler moves with the dirty window.
- **B in `ClassicMeshes.cs` (the material recipe).** The texture choice (first named, else the environment texture,
  `:351-358`); opaque, alpha or additive with no depth write and specular off (`:624-674`, `:741-757`); self-illumination
  added as emission (`:665-671`); `TagTint` (128,128,200) (`:405`) and `TagOverlayColour` (`:409`); a tagged twin adds
  the tag colour to the material's own (`:416-431`); `TerrainTinted` adds the piece colour (`:493-508`); the environment
  pass, which materials get it and its light (`:370-401`); a faded environment pass draws black (`:722-727`); the decay
  stage picking the face texture (`:179-198`). The texture choice, blend, depth write and added light became a
  `ClassicMaterialRecipe` value per key in `DK2.Presentation` (3a), so both engines build materials from one
  description; the rest follows in 3c and 3f.
- **Liquids (B).** `LiquidSurfaces.cs` is entirely rule: rest heights 0.9 and 0.85, amplitudes, the 30 per second frame,
  the two phase formulas, the 3x3 grid per tile and the textures (`:24-26`, `:82-170`). It moves as `LiquidSurfaceWave`
  filling a reused position buffer. Unity rewrites every vertex and recalculates normals each frame (`:141-163`).
- **Ceiling (S, P).** `TerrainCeilingChunks.cs` (202) uses `TerrainCeiling` and `TerrainCeilingField`; it reads
  `WorldState` in `Show` (`:78-98`), which Godot can do since it ticks inline. Light anchor kind 2 (`:26`, `:146`) is
  the possession roof's "black where no DK2 light reaches" switch in the shader.
- **Plumbing for Godot.** One `MeshInstance3D` per chunk with an `ArrayMesh`, one surface per material key. A rebuild
  happens on a tile change, not per frame; reuse the C# arrays and replace surfaces, and prefer the
  `RenderingServer` region updates when a chunk's vertex count is unchanged (check the 4.8 API with Context7). Textures
  come from `InstallArtProvider.LoadTexture` as `RgbaImage`, so Godot's PNG decoder and its gAMA handling (TRAPS) are
  not involved. A `ScenePresenter` takes `AssetProviderChain.Standard(install: art)`, which ends in the placeholder, not a
  bare `InstallArtProvider`. Unity caches about 8 KB of geometry a tile, while Godot builds each tile through one shared
  `TerrainTileGeometry`; 3c chooses.

## Blended terrain and the depth stream

- **Unity.** `TransparentTerrain.cs` collects every chunk's blended triangles, culls them to the frustum, sorts them back
  to front with `TriangleDepthOrderBuffer` and uploads runs, each a `GameObject` with a sorting order (`:327-454`); tag
  overlays take negative orders (`:402`). Status flowers and additive effect meshes join the same stream
  (`PlaceItems`, `:301-322`; `AddEffectMesh`, `:96-118`).
- **Godot.** Godot sorts transparent instances by distance and `sorting_offset`, and `render_priority` covers only
  -128..127. Reproducing a per-triangle order needs either one dynamic mesh whose surfaces are drawn in order, or runs
  as instances with forced sort keys. Which one holds in 4.8 is a prototype question for 3d (open point 4).

## Camera and projection

- **Unity.** `DungeonRunner.ControlCamera` (`DungeonRunner.cs:1239-1257`), `ApplyClassicCamera` (`:1415-1426`),
  `ApplyCameraPose` (`:1428-1445`), the glide (`:1314-1320`), set-up (`:518-532`); a free camera without the install
  (`:1259-1274`) and `LookAt` (`:1453-1477`) frame the whole map.
- **Shared (S).** `ClassicCamera.Pose(focus, CameraViewport)`, `ClassicCameraSetup.Create`, `ClassicCameraInput.Apply`
  (y-up pointer: `y = ScreenHeight - yTop`), `CameraGlide`, `CameraFocus`, `PossessionSession.TryCamera`.
- **Plumbing.** The pose's off-centre frustum: Unity writes `projection[1,2] = -ProjectionCentreShift`
  (`:1434-1440`). Godot's `Camera3D` has no skew in perspective mode; `PROJECTION_FRUSTUM` with `size` from the near
  plane and vertical field of view and a `frustum_offset` gives the same frustum (derive and pin the sign, verify the
  4.8 API). `keep_aspect` must be height, since the pose's field of view is vertical.
- **B, small.** `LookAt`'s whole-map framing (half-diagonal sphere in the vertical field, far plane x3, `:1453-1477`) is
  a renderer choice, not DK2's; Godot needs no no-install mode (open point 6), so it stays in Unity.
- **Note.** `DungeonScene.LightPose` (`DungeonScene.cs:892-899`) rebuilds a pose from Unity's camera with pitch, yaw and
  centre shift zero for the light publisher. Godot should pass `ClassicCamera`'s pose; whether the zero shift changes
  the visible-tile set is open point 5.

## World picking and hover

- **Unity.** `ScreenToTile` (`DungeonRunner.cs:1211-1225`), `ScreenToPosition` (`:1227-1234`), `ResolveWorldHover`
  (`:1558-1578`), `CaptureWorldHover` (`:1586-1643`), `AimHandLight` (`:1547-1556`); the measures in
  `DungeonScene.PickHoverEntity` (`DungeonScene.cs:185-241`), `TryPickSphere` (`:269-287`), `NearestTriangle`
  (`:292-311`).
- **Shared (S).** `TilePicker.Pick`, `TileTopPlane.PositionOn`/`HandLightPoint`, `WorldHoverPicker` (terrain cast,
  `Holds`, `Admits`, `Offer`, the sweep, Moller-Trumbore), `HoverPickRules`, `HoverCursorResolver`, `HoverOverlayPlan`.
- **Plumbing.** Godot needs no physics: the ray comes from `Camera3D.ProjectRayOrigin`/`ProjectRayNormal`, converted to
  the shared frame. The KMF sphere is read from UV2 in Unity; Godot keeps the C# copy (`RenderMesh` sphere, the
  animation player's last sample) instead of reading meshes back. The view frustum (`GeometryUtility` at `:220` and in
  the shadows) should be a `ViewFrustum.FromPose` in `src/`, since `Camera3D.GetFrustum` returns a Godot collection.
- **B, small.** The entity cull before the pick list, cited to 0x00575D70 (`:217-220`), is a plain frustum-AABB test;
  it moves with `ViewFrustum`.

## Creatures and animation

- **Unity.** `DungeonScene.CreateCreature`/`UpdateCreature` (`:408-458`), `ConfigureCreatureClip` (`:486-501`),
  `DrawCreatureFrame` (`:547-589`), `PositionCreature` (`:1065-1085`); `ClassicMeshes.ConfigureAnimation` (`:90-121`)
  builds three segment meshes; `ClassicCreatureAnimation.cs` (175) is the `ICreatureAnimationSurface` (`Install`
  `:65-89`, `Show` `:91-95`, `Upload` `:97-108`).
- **Shared (S).** `CreatureAnimationPlayer<T>`, `CreatureAnimationArt`, `AnimationClipRequest`, `PoseInterpolator`,
  `MeshOrientation.Mesh`, `CreatureHeadTable`, `GemSparkleGate`, `RenderAnimation`.
- **B left.** The per-frame order in `DrawCreatureFrame`: position from `PoseInterpolator`, animation draw, hide the
  possessed model, footstep request, burning-red overlay, crackle, head point, gem sparkle (`:559-588`), then objects and
  the torch refresh. Each step is shared; the order is not. A `SceneFrame` sequence in `DK2.Presentation` over an
  `ISceneFrameHost` keeps Godot from reordering it (open point 7: move it, or document it in the Godot port).
- **Plumbing.** One `MeshInstance3D` per creature, one `ArrayMesh` per segment. `Upload` rewrites positions every frame;
  Godot writes them with `RenderingServer.MeshSurfaceUpdateVertexRegion` from a reused `byte[]`, which needs the 4.8
  vertex stream layout (positions with or without interleaved normals; check before choosing). Hiding the possessed
  model is `Visible = false`, not a destroy. Control points stay in C# (`TryGetControlPoint`, `:125-131`).

## Objects, structures and torches

- **Unity.** `CreateObject`/`UpdateObject`/`ConfigureObjectClip` (`DungeonScene.cs:641-699`), `PositionObject`
  (`:1095-1116`), heart beats (`:591-613`), structures (`:713-762`), torches (`:767-853`), `ClassicTorchGeometry.cs`
  (71), `ClassicTorchFlames.cs` (119).
- **Shared (S).** `ObjectView` (`DrawnTexturePhase`, `MeshScale`, `MeshScaleY`, `Alpha`, `AddedLight`), `HeartPulse`,
  `DungeonHeartVisual.TextureIndex`, `TorchLayout`, `TorchGeometry`, `ClassicTorchFlame`, `ClassicTorchFlameAnimation`.
- **B left.** In the recipe: the texture-phase material set (`ClassicMeshes.cs:123-150`), the alpha fade keeping depth
  writes (`:687-739`), the blueprint swap to the tag overlay (`:588-598`), the door's instance tint (`:483-491`), the
  hover-texture twins one queue later (`:547-564`). The flame phase offsets from `Dk2Lcg(1)` drawn in first-seen order
  (`ClassicTorchFlames.cs:30`, `:77`) and the flame point bent by the lattice (`:95-98`). KMF group part offsets
  (`ClassicMeshes.cs:257-272`).
- **Plumbing.** Static meshes are shared `ArrayMesh`es built once per entry; many identical objects (gold, eggs) can
  become a `MultiMeshInstance3D` later. Flames are camera-facing quads with an additive unshaded material.

## Lighting

- **Unity.** `ClassicVertexLit.shader` (316) with `ClassicPointLighting.hlsl` (75); buffers `ClassicTileLightBuffer.cs`
  (159, an `ITileLightSink`), `ClassicTorchLightBuffer.cs`, `ClassicTileFillBuffer.cs`; `DungeonScene.PrepareClassicLighting`
  (`:869-890`); globals from `ApplyClassicLook` and `ReadAmbientKeys` (`DungeonRunner.cs:1279-1300`).
- **Shared (S).** `ClassicLightPublisher`, `ClassicLightTable` (24 sources a tile), `ClassicWorldLightCache`,
  `ClassicPointLight` (`ContributionAt`, `RadialAt`, the factories), `ClassicInstancePointLight`, `ClassicLightFlags`,
  `PortalCentreLights`, `ClassicAmbientLight`, `EnvironmentMapCoordinates`, `TileView.LitAsExplored`.
- **What the vertex shader computes.**
  - Ambient `_DK2AmbientColour` as an RGB addition (`:214`).
  - The tile's light table (`:140-188`): the table of the anchor tile, or the tile under the object origin. With light
    kind 1 (bounded) a source past radius plus bound sphere is skipped. Instance-lit vertices add the radial term under
    flag 0x04 and the instance point term under 0x100; terrain vertices add the point term once under 0x60 and once more
    under 0xA0 or 0x01.
  - The point term (`.hlsl:7-30`): a 256-entry distance table `0.25 + 16/index`, incidence, radius falloff, clamped.
  - A fill: Unity's SH plus the main light, tinted, weighted by the tile's fill (unlit tiles keep `_DK2UnlitFill`),
    zero on the roof, faded where DK2's own light is bright (`:218-233`, `:270`).
  - Emission, a distance fade from the eye (`:272-280`), the environment-map UV (`:282-292`) and `DK2PackedDiffuse`,
    floor to 1/255 and clamp (`:296`).
  - The fragment is texture x diffuse, alpha only when transparent (`:300-308`).
- **B left.** The CPU references exist for each term, but the per-flag sum (`:164-185`), the bound-sphere skip
  (`:164-171`), the anchor-or-origin table choice (`:144-151`) and the packed diffuse (`.hlsl:32-35`) exist only in HLSL.
  They move as `ClassicVertexLight.Sum` with tests; each engine's shader then copies one tested reference. The intensity
  packing `round(I*128) | flags << 8` (`ClassicTileLightBuffer.cs:84`) is P.
- **Owner-tuned (P, ADR-0522).** The fill tint (0.55, 0.44, 0.28), fade 0.5 to 1, unlit fill 0.25, fill floor 0.5
  (`DungeonRunner.cs:92-104`, same values in the scene), and Unity's fill source: the default skybox SH and a white
  directional light at 1.1 (`Scenes/Dungeon.unity`). Godot gets its own values for each.
- **Godot.** A spatial `ShaderMaterial` with `render_mode unshaded` (or a `light()` that does nothing), the sum in
  `vertex()` and a varying to `fragment()`. Godot spatial shaders have no structured buffers: the 24 sources a tile go
  in a float texture (two texels a source), the counts in another, read with `texelFetch` in `vertex()`; the fill and
  globals are global shader uniforms set through `RenderingServer`. `ClassicTileLightBuffer`'s dirty range becomes an
  `Image` region update. Exposure (`ClassicExposure.shader`, `pow(c, 30/Gamma)`) is a full-screen canvas pass.

## Creature shadows

- **Unity.** `ClassicCreatureShadows.cs` (355): per caster an opaque-parts bound (`:163-192`), the three nearest torches
  within 2.5 tiles with ties broken by tile index (`:194-217`), the fade distance taken from the fourth (`:144-145`),
  `ClassicMeshShadow.TryProject` per source, silhouettes drawn 8x supersampled into an atlas (`:251-291`,
  `ClassicShadowMask.shader:23-25`), reduced by averaging 64 samples (`ClassicShadowReduce.shader:30`), and receiver
  quads multiplied over the scene (`:293-324`, `ClassicShadowReceiver.shader:13`, `:34`). The ground height is the
  view's plus 0.01 (`DungeonScene.cs:513-515`).
- **B left.** The source selection and fade (`:144-145`, `:194-217`) and the silhouette projection formula
  (`ClassicShadowMask.shader:23-25`). Both move into `ClassicMeshShadow` with tests.
- **Godot.** Either a `SubViewport` that renders the casters' silhouettes into an R8 atlas, or a CPU rasterizer of the
  KMF triangles into 32x32 coverage (the texture size `ClassicMeshShadow.TextureSize` names) uploaded as one `Image`.
  The CPU path is testable in `src/` and needs no second render of the creatures (open point 8). Receivers are one
  dynamic mesh with a `blend_mul` unshaded material.

## Construction effects

- **Unity.** `ClassicConstructionEffects.cs` (386): `Draw` (`:106-196`) and `DrawFlat` (`:199-238`) over
  `EffectDrawList`; wiring in `DungeonRunner.StartGame` (`DungeonRunner.cs:386-417`).
- **Shared (S).** `EffectDrawList` (defaults, stream order, colour, frame pick, quads, blend, group keys),
  `EffectQuadBuffer`, `EffectParticleStream`, `MeshOrientation.Particle`, `RenderWorld.ToNative`.
- **B left.** The effect filters built as lambdas in the runner: visibility by `CanShowTile`, the solid-tile nav test,
  the Heart opening effects only for the local player, cited to 0x004D9A60 (`:391-395`), terrain flags, shot visibility,
  creature ground (`:396-408`); the gem sparkle's owner taken as the viewer (`:414-416`); the footstep gate on the
  camera (`:439-446`). They move as an `EffectFilters` factory over `WorldState`, `WorldView` and the nav grid.
- **Plumbing.** Mesh particles: a `MultiMesh` per (mesh, frame, material) with per-instance colour, filled through one
  buffer call a frame. Billboard and flat quads: one dynamic mesh per group, updated in place within capacity.
  `AA_` materials join the blended stream (`:154-155`).

## Status flowers and the dollar

- **Unity.** `ClassicStatusFlowers.cs` `Draw` (`:46-97`), `Art` (`:135-156`); `ClassicFlowerDollar.cs` (62);
  `ClassicEffect.shader` (56).
- **Shared (S).** `StatusFlowers`, `StatusFlowerLayers.Build`, `StatusFlowerRules.LayerPlacement`/`WorldSize`,
  `FlowerDollarClock`.
- **B left (shader).** The ring tinted by luminance x2, clamped (`ClassicEffect.shader:41`; owner-matched to a
  screenshot, `ClassicStatusFlowers.cs:150-152`), and half colour outside the experience wedge (`ClassicEffect.shader:43-50`).
  The flowers hide with the HUD (`:49`). These become a flower colour reference in `StatusFlowerLayers` with tests;
  each engine's shader copies it.
- **Godot.** Camera-facing quads in the blended stream; per-instance colour and wedge as `MultiMesh` custom data.

## Lightning

- **Unity.** `ClassicLightning.cs` (199), called from `Update` (`DungeonRunner.cs:988`, `:1023-1025`) and per creature
  (`DungeonScene.cs:578`).
- **Shared (S).** `LightningBolts`, `LightningCracklePairs`, `LightningLink`, `LightningRibbon`, `LightningControlPoint`.
- **B left.** Which shots draw lightning (mesh resource type 7) and their interpolation, the caster point (none for a
  corpse) (`:70-97`); the target per `FightTargetKind`, door, trap, Heart pillar and shot cases included (`:99-133`);
  procedural effects: lightning kinds, an end one tile up, three bolts for the triple kind (`:135-144`); the crackle on
  a clip install (`:146-157`). It reads `WorldState` directly. All of it moves as a `LightningDrawList` that takes the
  view matrix and fills a reused ribbon buffer; bolts are built in camera space (`:165-180`).
- **Godot.** One dynamic mesh, additive unshaded, no depth write (`ClassicLightning.shader:13`).

## 3D overlays: tile cage, hover tint, placement cursor

- **Tile cage.** `ClassicTileHoverOutline.cs` (106). B left: the edge mask per boundary tile (`:58-66`), the 0.05 lift
  around the lattice bend (`:81-83`), the camera-facing ribbon side (`:91`), red or blue x `BluePeak` (`:70`), and the
  pulse level read before it advances (`:54-55`). Moves as `TileHoverOutline.Build` into a reused ribbon buffer.
  The ribbon profile (exponent 2.15, x1.6, `ClassicHoverOutline.shader:31-34`) is owner-matched: P.
- **Hover tint.** `DungeonScene.DrawHoverOverlay` (`:139-178`) draws `HoverOverlayPlan`'s red pass and the gold
  object's texture swap. Godot: a `next_pass` or a second surface override on the hovered instance for that frame,
  additive, `HoverOverlayColour` (`ClassicMeshes.cs:512`, B, moves with the recipe).
- **Placement cursor.** `ClassicPlacementOverlay.cs` (225) is a UI Toolkit overlay whose `Draw` is all rule: icon names
  per tool (`:137-153`), the draw order (`:42-48`), the No Go shift of (-2, +2) (`:85-101`), held icons and the
  I-Dismiss (`:158-186`), price digits, family, colours and offsets (`:102-117`). It moves as a `PlacementCursorHud`
  draw list (a `HudFrame`), which Godot's `HudCanvas` draws as it draws the panels.

## Hand cursor and held things

- **Unity.** `ClassicHandCursor.cs` (143); `CaptureWorldHover` updates it (`DungeonRunner.cs:1630-1641`).
- **Shared (S).** `HandCursor.Sprite`/`Place`/`OverlayAnchor`/`Reach`, `HandCursorAnimation`, `HeldItemStack`,
  `HandCursorEventSink`.
- **B left.** The pure green key to transparent on the sheet (`:102-106`) moves to an image pass beside
  `HudImagePixels`. The 768-line scale and the HUD factor compensation (`:114-125`) are UI Toolkit plumbing.
- **Godot.** A quad with a UV sub-rect in a `HudFrame` on the top canvas layer; the OS pointer hidden while it shows.
  Held items are the placement cursor's icons; nothing held is drawn in 3D.

## Minimap camera footprint and heading

- **Unity.** `DungeonMinimap.Draw` (`:169-187`), `Heading` (`:92-93`), `MinimapFootprintVisual` (`:230-291`).
- **Shared (S).** `MinimapCameraFootprint.TryBuild`, `MinimapHud`, `MinimapDiscImage` (Godot's CPU disc).
- **B left.** The focus as the view centre's hit on the floor plane when no focus is given (`:173-179`); the four
  viewport corners in order on the floor plane (`:262-268`); the heading as yaw x 2048/360 (`:92-93`); the ribbon's
  index list, UV and placement constants (`:232`, `:247-251`, `:288-289`). The corners move as a pose-based helper
  beside `MinimapCameraFootprint`; the ribbon needs a triangle-list `HudDrawKind`, since `HudDrawKind` has only `Quad`,
  `Sector` and `Fill` (open point 9; `CanvasItemAddTriangleArray` has a 4.8 trap in TRAPS).

## Possession camera

- **Unity.** `DungeonRunner.Possession.cs` (83): `UpdatePossession` (`:31-53`), `ApplyPossessionCamera` (`:56-67`), the
  pointer lock (`:70-75`); the ceiling and wall latch in `Update` (`DungeonRunner.cs:963-971`).
- **Shared (S).** `PossessionSession` (input, camera, hidden id, commands, sounds), `PossessionCamera`, `TerrainCeiling`.
- **B, small.** The roof shows exactly while a model is hidden (`:970-971`), and the lattice and view take the
  possession latch (`:963-966`). Both join the `SceneFrame` sequence (open point 7).
- **Godot.** Same `Camera3D`; `Input.MouseMode = Captured` for the lock; the ceiling as a chunked mesh like the terrain.

## Scripted camera paths, letterbox, jitter and the full-map preview

- **Unity.** `ApplyPathCamera` (`DungeonRunner.cs:1326-1340`), `ApplyLetterbox` (`:1304-1310`), `DrawCinematic` bars
  (`:1344-1361`), the camera jitter (`:995-1000`, removed next frame at `:856-857`), `SeekCamera` (`:981-987`).
  `ClassicFullMap.PreparePreview` (`ClassicFullMap.cs:106-124`) renders a second camera straight down, north up (`:120`).
- **Shared (S).** `ScriptedViewDirector` (`Pose`, `LetterboxBar`, `CameraJitter`, `PathPlaying`), `CameraPathPose`
  (`VerticalFieldOfView`, `ViewDistance`), `EngineCameraPaths`, `FullMapHud.Layout().Preview`.
- **B, small.** The jitter's mapping to the render frame (`:997-998`) belongs on `ScriptedViewDirector`; the preview's
  down-and-north orientation belongs on `FullMapPreview`.
- **Godot.** The letterbox shrinks the drawn area, so the 3D goes in a `SubViewportContainer` sized to the rect, or the
  frustum is offset to match (prototype in 3n). The preview is a `SubViewport` sharing the main `World3D`. The light
  publisher already joins the preview camera at view distance 10.

## Move order

Each sub-step is one commit, leaves Unity working (its compile checked, and Unity calling any moved rule), and leaves
Godot visibly better. Each is checked by unit tests for what moved and by a Movie Maker capture (below). The first
three are the terrain and the camera; after that each step adds one kind of thing to the world.

- **3a. Static terrain from the level-start camera.** Done 2026-11-16 (plan step 3a). Moved: `RightHandedFrame`,
  `TerrainTileBuilder` with `TerrainMaterialKey` and the opaque, alpha and additive part of `ClassicMaterialRecipe`.
  Godot: a `WorldScene` node under `InGameHud` implementing `IRenderTarget` for tiles (creatures, objects, structures and
  torches ignored), one `MeshInstance3D` per chunk, an unshaded texture material per key, and a `Camera3D` at
  `ClassicCamera.Pose(LevelStartFocus)` including the centre shift. Checked: the Level1 start frame shows the Heart
  room from DK2's camera with the HUD on top; builder tests pin the output against the old build.
- **3b. Camera control and the minimap.** Godot: `ClassicCameraInput` (keys, edges, wheel, held buttons; y-up sample),
  `CameraGlide` for the minimap left click and the script's glide, the query follow and turn, the minimap's focus and
  heading from the live pose, and the footprint. The 1.2 zoom-out limit scale exists only in Unity's inspector
  (`DungeonRunner.cs:120`) and Godot uses 1, DK2's bound; 3b makes it a shared default. Move: the floor-plane corners and centre, and the triangle-list
  `HudDrawKind` (open point 9). Check: a capture with a test glide (a `--glide-to x,y` launch argument in
  `LaunchArguments`) ends at the target, and the footprint matches the view.
- **3c. Terrain that changes.** Move: the dirty window and the budgeted, threaded flush; the material variants (tint,
  tag, red, fog, decay, tag overlay); `LiquidSurfaceWave`. Godot: rebuilds on tile pushes, liquids waving. Check: the
  frames at turn 1 and after the imps claim tiles differ where expected; tests for the wave and the variant choice.
- **3d. The blended stream.** Godot: blended terrain sorted back to front with `TriangleDepthOrderBuffer`, after the
  4.8 sorting prototype (open point 4). Check: a capture across the sight fog and a blended piece shows no order
  popping over 120 frames.
- **3e. Pointer, cage and placement: the level becomes playable.** Godot: picking rays into `TilePicker`,
  `TileTopPlane` and the terrain cast, `WorldPointerInput.PointerPosition`, the hand cursor sprite, the cage and the
  placement cursor. Move: `ViewFrustum`, `TileHoverOutline.Build`, `PlacementCursorHud`, the green key. Check: a capture
  driven by a recorded pointer (open point 10) shows the cage and the dig tag on a wall; tests for the moved pieces.
- **3f. Objects, structures and torch meshes.** Move: the Euler composition, the rest of the recipe (texture phase,
  fade, blueprint, instance tint, hover twins), the flame phase offsets, group parts. Godot: static meshes, the Heart
  and its beat, doors and traps, torch meshes and flames. Check: the Heart, gold and torches in the start frame.
- **3g. Creatures.** Godot: KMF segment meshes, the `CreatureAnimationPlayer` surface with in-place vertex upload,
  pose interpolation, the head table and footstep requests. Move: the `SceneFrame` order if the owner takes open
  point 7. Check: imps walk and dig over 300 frames; frame times show no GC spikes (`GC.CollectionCount` before and
  after a run, printed).
- **3h. Entity hover and the Hand.** Godot: entity offers into `WorldHoverPicker`, the red pass and gold swap, pickup
  and drop through the shared flows, hover text in the world. Check: a recorded-pointer capture shows the red tint on an
  imp and the held icon after a pickup.
- **3i. Lighting.** Move: `ClassicVertexLight.Sum` and the packed diffuse with tests. Godot: the vertex-lit shader, the
  light-table textures, the publisher, ambient keys, torch, portal, object, effect and hand lights, the environment
  pass, the exposure pass, and Godot's own fill and fade (open point 2). Check: captures of the start frame and a lit
  room, compared with what open point 3 settles.
- **3j. Creature shadows.** Move: source selection, fade and the silhouette projection. Godot: the chosen path (open
  point 8). Check: a capture of an imp beside a torch; tests for the selection ties and the projection.
- **3k. Effects and status flowers.** Move: `EffectFilters`, the flower colour reference. Godot: billboards, flat
  quads, mesh particles, flowers and the dollar in the blended stream. Check: dig debris and a flower over an imp.
- **3l. Lightning.** Move: `LightningDrawList`. Godot: the ribbon mesh. Check: a capture with a staged lightning shot
  (Level1 has none; a test level or `--stage-battle` with a caster, open point 11).
- **3m. Possession.** Godot: the possession camera, pointer lock, the roof and the hidden model. Check: a capture after
  `--possess-first-creature` (a new launch argument) shows the first-person view and the roof.
- **3n. Scripted paths and the full-map preview.** Godot: path camera, letterbox, jitter, subtitles stay in step 4, and
  the full map's preview camera. Check: Level1's opening path played from turn 0 in a capture, letterboxed.

## How a step is checked here

Build with `dotnet build godot/DK2.Godot/DK2.Godot.csproj` and run the moved rules' tests in `tests/DK2.Presentation.Tests`
filtered to the touched classes. The frame check cannot use `--headless`, which has no renderer; it runs unattended with
the pinned console executable, a temporary `godot/DK2.Godot/override.cfg` for 1920x1080 (TRAPS), and the output kept
outside the repository (`godot/README.md`, *Verifying*). Level1's opening path hides the HUD for about 1,000 frames at
30 fps, so a capture that shows the HUD needs a save made after the path (`--save-as <name> --save-tick 130`, then
`--load <name>`) or a skip argument; 120 frames never show it:

```powershell
& "$env:LOCALAPPDATA\Programs\Godot\4.8-beta3\Godot_v4.8-beta3_mono_win64\Godot_v4.8-beta3_mono_win64_console.exe" `
  --path godot/DK2.Godot res://InGameHud.tscn --write-movie <temp>/f.png --fixed-fps 30 --quit-after 120 `
  -- --settings <temp>/settings.ini
```

It opens a window and needs the GPU. Every step that changes a Unity file reruns the stand-in compile
(`unity/DK2.Unity.Compile`) and Unity's own batch checks (`unity/README.md`, *Verifying it inside Unity*: the compile,
`DungeonSetup.SmokeTest` and `SaveSmokeTest`; Unity 6000.6.0f1 is installed and licensed). Only the owner's open editor
must not be driven.

## Open points for the owner

1. **The frame.** Decided (2026-11-16, option 1): an explicit Z flip through the tested `RightHandedFrame`.
2. **How Godot's lighting is tuned.** The DK2 terms are shared; the fill (source, tint, floor, unlit share), the
   distance fade and the exposure are Godot's. Starting from Unity's ADR-0522 values with a Godot fill (an
   `Environment` ambient colour and one `DirectionalLight3D` read in the shader, or a fixed colour) is one way; a
   separate set of owner-tuned values, kept in one Godot resource, is the other. Decided by the owner (2026-11-16):
   start from Unity's ADR-0522 values and tune by eye.
3. **What to compare against.** Candidates: the owner's DK2 screenshots ADR-0522 was tuned against, new DK2 captures of
   Level1 at fixed camera poses, and Unity probe captures of the same state (`ClassicWorldLightingProbe`, run in batch
   mode). Settled with point 2 (2026-11-16): 3i and 3j are checked by eye, and a Unity probe capture can serve as a
   comparison.
4. **Blended ordering in Godot.** Per-triangle order across chunks may not be reachable with `render_priority`
   (-128..127) and instance sorting; a 3d prototype decides between one ordered dynamic mesh and sort-key runs, and
   whether Godot may deviate (sorting per run only) for a first version.
5. **The light publisher's pose.** Unity gives it a pose with zero centre shift, pitch and yaw (`DungeonScene.cs:892-899`).
   Should Godot pass `ClassicCamera`'s full pose (and Unity follow), or match Unity's zero shift for now?
6. **A no-install mode.** Decided (2026-11-16): Godot requires the install, like the HUD, with no placeholder mode and
   no free camera.
7. **The scene frame order.** Move the `DrawCreatureFrame` and `Update` world sequence into a shared `SceneFrame`, or
   copy the order into Godot with a comment pointing at Unity's lines until Unity is deleted.
8. **Shadow silhouettes.** CPU rasterizing in `src/` (testable, no second creature render) or a GPU `SubViewport`.
9. **The footprint ribbon.** A new triangle-list `HudDrawKind` for every HUD renderer, or a Godot-only draw.
10. **Pointer-driven captures.** Moving the OS pointer during a capture moves the owner's cursor (TRAPS). A launch
    argument that feeds a recorded pointer path (or replays an input journal) would make 3e, 3h and 3m checkable
    unattended.
11. **Content for 3l and 3m.** Level1 has no lightning shot; a staged test level or launch argument is needed, built
    with `tools/levelgen` or a new argument.

Out of scope here: the front end's 3D campaign map and its land picking (`DungeonScene.PickFrontEndMap`, `:245-264`), the
level sound driver and subtitles (step 4), and the probes (step 4).
