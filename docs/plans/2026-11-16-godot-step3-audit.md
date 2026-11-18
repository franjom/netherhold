# Godot migration step 3: the world-rendering audit

**Status:** audit, 2026-11-16, for [the migration plan](2026-11-15-godot-migration.md) step 3. It was a read-only
pass over the Unity world-rendering sources at HEAD 5afa63b7. Nothing was built or run. Line numbers are as of that
HEAD and go stale as moves land. Paths without a folder are under `unity/DK2.Unity/Assets/DK2/` (shaders under its
`Resources/`). Each section maps one world feature; the move order (3a-3n) is at the end.

The classes are step 2's: **B** is a DK2 rule still in Unity, which moves to `src/` with tests before Godot uses it
(ADR-0805); **P** is engine plumbing that Godot rewrites; **S** is already a call into `src/`. Lighting's fill, fade and
exposure terms are P (the owner's exception), while the DK2 light arithmetic the shader copies is B.

## What Godot starts from

`godot/DK2.Godot/InGameHud.cs` runs Level1 inline (no tick worker, so no presented copy), owns one `WorldView`, a
`FrameDriver` and a `ScriptedViewDirector`, and draws the HUD panels through `HudCanvas` over the `WorldScene` (a `Node3D`
with its terrain, liquids and `Camera3D`). The project renders with Forward+ at 1920x1080.

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

`RenderRotation` (3f) composes yaw, roll and pitch as a `System.Numerics` quaternion with tests; Unity's
`RenderEulerRotation` only converts it, and Godot takes it through `RightHandedFrame.Rotation`.

## Terrain

- **Unity.** `TerrainChunks.cs` (697), driven by `DungeonScene.CreateTile`/`UpdateTile` (`DungeonScene.cs:365-406`)
  through `IRenderTarget`, and `DungeonScene.Flush` (`:902-909`). Chunks of 16x16 tiles, one `Mesh` per chunk with a
  submesh per opaque material; blended triangles go to `TransparentTerrain.cs`.
- **Shared (S).** `ScenePresenter` diffing and push order, `TileView` (pieces, keys, walls, tints, fog), `TerrainPieces`,
  `TerrainLattice.Deform`/`Invalidate`, `LatticeDirtyTracker`, `ClassicTerrainTint.PieceColour`, `RenderMesh.FromModel`,
  `TriangleDepthOrderBuffer` (DK2.Art), `TileHeights`.
- **B, moved in 3a.** The whole per-tile build is `TerrainTileBuilder` (`Prepare` and `Build`): positions, normals, UVs,
  light anchors and triangles per `TerrainMaterialKey` (texture, blend, self-illumination, variant plain, tint, tag, red,
  fog alpha or tag overlay). It holds the pieces a tile places with their quarter turns, the inverted lighting normals of
  tops, a room tile's floor set, extra piece and walls (tagged or tinted by the neighbour), hero faces, a liquid's or
  bridge's bed, the sight-fog overlay piece, the lattice bend in DK2's frame and the key of each triangle group. Unity
  maps a key to a `Material`, Godot to a `ShaderMaterial`.
- **B, moved in 3c.** `TerrainChunkScheduler` over `ITerrainChunkSink`: the dirty window (a change forgets its lattice
  cells and marks the 5x5 tiles stale, a tag or tint change the 4 neighbours) and the 4 ms flush, at least one chunk,
  lowest first, inline while no chunk shows, with the thread-pool build (ADR-0457, ADR-0696). Unity's `TerrainChunks`
  is a sink over it.
- **B in `ClassicMeshes.cs` (the material recipe).** The texture choice, blend, depth write and added light became a
  `ClassicMaterialRecipe` per key (3a), and in 3c `TerrainMaterialLook` took one look per key: `TagTint` (128,128,200),
  `TagOverlayColour`, `HoverOverlayColour`, the red, tagged and tinted light and the fog alpha. Both engines build
  materials from it. The decay stage was already shared (`WorldView.DecayStage`, `TileView.FaceTexture`,
  `TerrainPiece.FromModel`, `ClassicMaterialRecipe.FromKmf`). The environment pass and the fade moved in 3f
  (`ObjectMaterialLook`); Godot's environment pass came in 3i.
- **Liquids (B), moved in 3c.** `LiquidSurfaceWave` and `LiquidSurfaceField` hold the rest heights 0.9 and 0.85, the
  amplitudes, the 30 per second frame, both phase formulas and the 3x3 grid per tile, filling reused buffers. Normals
  are the normalised sum of unit face normals; Unity takes them too, where it recalculated them each frame (the
  reviewer bounds the difference at 1.5 degrees, a one-byte light step at most). `LiquidSurfaces.cs` only uploads.
- **Ceiling (S, P).** `TerrainCeilingChunks.cs` (202) uses `TerrainCeiling` and `TerrainCeilingField`; it reads
  `WorldState` in `Show` (`:78-98`), which Godot can do since it ticks inline. Light anchor kind 2 (`:26`, `:146`) is
  the possession roof's "black where no DK2 light reaches" switch in the shader.
- **Plumbing for Godot.** One `MeshInstance3D` per chunk with an `ArrayMesh`, one surface per material key. A rebuild
  happens on a tile change, not per frame; reuse the C# arrays and replace surfaces, and prefer the
  `RenderingServer` region updates when a chunk's vertex count is unchanged (check the 4.8 API with Context7). Textures
  come from `InstallArtProvider.LoadTexture` as `RgbaImage`, so Godot's PNG decoder and its gAMA handling (TRAPS) are
  not involved. A `ScenePresenter` takes `AssetProviderChain.Standard(install: art)`, which ends in the placeholder, not a
  bare `InstallArtProvider`. Decided in 3c: the per-tile geometry cache stays, with its starting buffers cut (numbers
  in the plan's 3c).

## Blended terrain and the depth stream

- **Unity.** `TransparentTerrain.cs` only uploads: each run a `GameObject` with the sorting order the stream gives it.
- **B, moved in 3d.** `BlendedTerrainStream<TMaterial>` collects every chunk's blended triangles, culls them with a
  1e-4 margin, sorts them back to front through `TriangleDepthOrderBuffer` and splits them into runs by material.
  `SortingOrder(run, tagOverlay)` is run - 32767 for a tag overlay and run + 1 for everything else. It also holds the
  effect-mesh slot (`AddEffect`, `RepeatEffect`), the status-flower slot (`SetItems`, `Refresh`, `ItemOrders`) and Unity's
  approximate `==` change test (squared difference below 1e-10); its chunk table is a `SortedList`, so a re-collect
  allocates nothing. `ViewFrustum` (`FromPose`, `FromViewProjection`) is the frustum test.
- **Godot (open point 4, decided).** One mesh with ordered surfaces is impossible in 4.8 (engine commit c51ff06e8): the
  transparent sort is an unstable introsort, `MAX_MESH_SURFACES` is 256 and Level7 makes up to 192 runs, and
  `render_priority` is per material, -128..127. Each run is a `RenderingServer` instance at the origin with
  `sorting_offset` set to the stream's order: Godot's transparent depth is distance(camera, pivot) - `sorting_offset`, and a
  new instance's pivot is its AABB centre (`use_aabb_center` true), so `SortFromOrigin`
  (`InstanceSetPivotData(order, false)`) is the one place the runs and the cage set their order (TRAPS). A run is one
  triangle list, so the order is exact, run for run and triangle for triangle. Runs upload with
  `MeshSurfaceUpdateVertexRegion` and `AttributeRegion`.
- **Blending.** The 3c weakness was mostly double linearisation: Godot linearises a `Color` on a plain `vec4` uniform and
  the shader did it again. Now uniforms are `source_color, color_conversion_disabled`, shaders write stored bytes / 255
  and the camera's `Environment` shows the buffer values, so every blend is in gamma space as in Unity. The float buffer
  does not clamp between blends where Unity's 8-bit target might (open, plan 3d).

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
  (`:1558-1578`), `CaptureWorldHover` (`:1586-1643`), `AimHandLight` (`:1547-1556`); the entity measures, `DungeonScene.PickHoverEntity`.
- **Shared (S).** `TilePicker.Pick`, `TileTopPlane.PositionOn`/`HandLightPoint`, `WorldHoverPicker` (terrain cast,
  `TryAdmit`, `Offer`, the sweep, Moller-Trumbore), `HoverPickRules`, `HoverCursorResolver`, `HoverOverlayPlan`.
- **Plumbing, ray done in 3e.** Godot needs no physics. `WorldScene.TryPickRay` builds the ray from the placed pose
  through `MinimapCameraView.RayDirection`, not the `Camera3D.ProjectRayOrigin`/`ProjectRayNormal` first planned here:
  4.8's `ProjectRayNormal` assumes a symmetric frustum and drops `frustum_offset` (`projection.cpp` at c51ff06e8; TRAPS). The pose is computed once a frame. The KMF sphere is read from UV2 in Unity; Godot keeps the C# copy (`RenderMesh` sphere, the
  animation player's last sample) instead of reading meshes back. The view frustum (`GeometryUtility` at `:220` and in
  the shadows) is `ViewFrustum.FromPose` in `src/` (3d), since `Camera3D.GetFrustum` returns a Godot collection.
- **Godot, 3e and 3h.** `InGameHud`'s pointer runs through `TilePicker`, `TileTopPlane`, the fog test,
  `WorldHoverPicker.TryCastTerrain` and `HoverCursorResolver`; tagging and placing post through `WorldPointerInput` and
  `WorldClickCommands`. `WorldScene.Hover.cs` (3h) offers creatures, hover-target objects, doors and traps; the hover text is
  the screen-space hover HUD, not 3D text.
- **B, moved in 3h.** `PickBox`, `PickSphere`, `PickTriangles`, `WorldHoverPicker.TryAdmit` and `ViewFrustum.Excludes`. The cull
  is not a plain frustum-AABB test: 0x00575D70 tests the KMF sphere against four side planes ([record](../research/functions/00575D70.md));
  `Excludes` is Unity's box-against-six approximation.

## Creatures and animation

- **Unity.** `DungeonScene.CreateCreature`/`UpdateCreature`, `ConfigureCreatureClip`, `PositionCreature`;
  `ClassicMeshes.ConfigureAnimation` builds three segment meshes; `ClassicCreatureAnimation.cs` is the
  `ICreatureAnimationSurface`; `DrawCreatureFrame` is one call into `SceneFrame`, each step a host method.
- **Shared (S).** `CreatureAnimationPlayer<T>`, `CreatureAnimationArt`, `AnimationClipRequest`, `PoseInterpolator`,
  `MeshOrientation.Mesh`, `CreatureHeadTable`, `GemSparkleGate`, `RenderAnimation`.
- **B, moved in 3g (open point 7).** `SceneFrame` over `ISceneFrameHost`, with `SceneCreatures`: the hover overlay's frame
  and the Hearts' health, then per creature the place from `PoseInterpolator` (false skips the rest), the clip, the
  possessed model's hide, the footstep crossing and request, the burning red, the crackle, the head point and the gem
  sparkle, then the objects and the torch refresh. Two shared allocation fixes reach Unity too: `CreatureHeadTable.Set` used
  the `ConcurrentDictionary` indexer (48 B a creature a frame) and now rewrites one slot a creature under a lock (the worker
  reads it through `Attachment`); `CreatureAnimationPlayer` keeps one sample buffer per clip, not per segment.
- **Godot, 3g.** `WorldScene.Creatures.cs` (the plan's step 3g has the detail): shared KMF models, a node per creature, clips
  through `CreatureAnimationPlayer` with in-place vertex upload from per-template mesh copies (bounded by the distinct clips
  per entry; Unity releases its copies on a clip-set change), pose and head point through `RightHandedFrame`. The burning
  crackle (3l), footsteps and sparkle (3k) and the possession hide (3m) are no-op steps of the order; the burning red came in 3h. The query
  follow runs after `DrawFrame` on the drawn position, as Unity's, so the blended sort moved from `Flush` to `FinishFrame`.
- **Probe frames.** Unity's probe draws at fraction 1, so Godot's imp at tick 70 is one tick's offset straight after a
  load and on Unity's spot within nine frames; a single-frame comparison needs the fraction near 1.

## Objects, structures and torches

- **Unity.** `DungeonScene`'s `CreateObject`, `UpdateObject`, `ConfigureObjectClip`, `PositionObject`, heart beats,
  structures and torches, with `ClassicTorchGeometry.cs` and `ClassicTorchFlames.cs`.
- **Shared (S).** `ObjectView` (`DrawnTexturePhase`, `MeshScale`, `MeshScaleY`, `Alpha`, `AddedLight`), `HeartPulse`,
  `DungeonHeartVisual.TextureIndex`, `TorchLayout`, `TorchGeometry`, `ClassicTorchFlame`, `ClassicTorchFlameAnimation`.
- **B, moved in 3f.** `ObjectMaterialLook`, `ObjectPass` and `ObjectModelPasses`: the own look, the environment pass (which
  materials, its light), the blueprint overlay, the hover twin, the door's and an element's tint, the fade (blended, still
  writing depth; a faded environment pass draws black), the texture-phase set, one pass per submesh then the environment
  passes. `EffectGroupParts.Of`, `ClassicTorchFlame.RenderPoint` and `ClassicTorchFlamePhases` (`Dk2Lcg(1)` offsets).
- **Godot, 3f.** `WorldScene.Objects.cs` (the plan's step 3f lists what it draws): shared `ArrayMesh`es, and
  `ZeroOrderOffset` for the sorting-order-0 window. Identical objects (gold, eggs) may become a `MultiMeshInstance3D`.
- **Done in 3i.** The environment pass has the env-map coordinates in the vertex shader and draws over plan submeshes:
  DK2 draws a plan as the overlay plus one additive environment pass ([structure-drawing.md](../research/structure-drawing.md)
  §5); Unity draws the overlay twice, Godot the overlay and the pass. The cage (order 0) takes a `ZeroOrderOffset` key, so
  it sorts among blended objects by distance as in Unity.

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
- **B, moved in 3i.** The per-flag sum, the bound-sphere skip, the anchor-or-origin table choice and the packed diffuse
  existed only in HLSL; they are now `ClassicVertexLight` (`TableTile`, `TableSum`, `PackedDiffuse`) with tests, and each
  engine's shader copies that reference. The intensity packing `round(I*128) | flags << 8`
  (`ClassicTileLightBuffer.cs:84`) is P.
- **Owner-tuned (P, ADR-0522).** The fill tint (0.55, 0.44, 0.28), fade 0.5 to 1, unlit fill 0.25, fill floor 0.5
  (`DungeonRunner.cs:92-104`, same values in the scene), and Unity's fill source: the default skybox SH and a white
  directional light at 1.1 (`Scenes/Dungeon.unity`). Godot gets its own values for each.
- **Godot (built in 3i).** A spatial `ShaderMaterial` with `render_mode unshaded`, the sum in `vertex()` and a varying to
  `fragment()`. Godot spatial shaders have no structured buffers: the tables go in a float texture array, a layer per
  tile row, `ClassicLightTableLayout.TexelsPerTile` texels a tile (the count and fill weight, two a source), read with
  `texelFetch` in `vertex()`; a changed layer is uploaded alone. The globals are global shader uniforms. Exposure
  (`ClassicExposure.shader`, `pow(c, 30/Gamma)`) is folded into the camera's colour correction table, no canvas pass.

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

- **Tile cage, moved in 3e.** `TileHoverOutline.Build` with `TileHoverRibbon` holds the edge mask per boundary tile, the 0.05
  lift around the lattice bend, the camera-facing ribbon side, red or blue x `BluePeak` and the pulse level read before it
  advances; Unity's `ClassicTileHoverOutline` only uploads it. Godot's `CageRibbon` is one dynamic surface with its own
  shader, the owner-matched profile (P) pow(centre, 2.15) x 1.6, additive, no depth write, both faces. Its sort key 0
  (`SortFromOrigin`) puts it after the tag overlays and under every other blended run, as Unity's sortingOrder 0 did.
- **Hover tint, Godot in 3h.** `DungeonScene.DrawHoverOverlay` (`:139-178`) draws `HoverOverlayPlan`'s red pass and the gold
  object's texture swap. Godot: a cached `ShaderMaterial` copy with `NextPass` set to `ObjectMaterialLook.HoverOverlay` (also the
  burning red), the gold twin as a surface swap, re-applied only when the hover state changes.
- **Placement cursor, moved in 3e.** `PlacementCursorHud` is `ClassicPlacementOverlay.Draw` as a `HudFrame`: icon names,
  DK2's draw order, the No Go shift, held icons, I-Dismiss, the overflow icon and the price digits. Unity's overlay is a thin
  wrapper drawn by `HudCanvas`; Godot's `ClassicPlacementOverlay` draws it on canvas layer 100. The refund colour, the
  1/1200 scale, the +30/+10 offsets and the spell No Go shift stay HYPOTHESIS (placement-cursor.md).

## Hand cursor and held things

- **Unity.** `ClassicHandCursor.cs` (143); `CaptureWorldHover` updates it (`DungeonRunner.cs:1630-1641`).
- **Shared (S).** `HandCursor.Sprite`/`Place`/`OverlayAnchor`/`Reach`, `HandCursorAnimation`, `HeldItemStack`,
  `HandCursorEventSink`.
- **B, moved in 3e.** The pure green key to transparent is `HudImagePixels.KeyGreen` (`HudImageVariant.GreenKey`);
  `HandCursorHud` and `InstallHandCursorSheets` draw and load the sheets. The 768-line scale and the HUD factor
  compensation are UI Toolkit plumbing.
- **Godot.** `ClassicHandCursor` draws a `HudFrame` quad on canvas layer 101, with the OS pointer hidden while it shows.
  Held items are the placement cursor's icons; nothing held is drawn in 3D.

## Minimap camera footprint and heading

- **Unity.** `DungeonMinimap.Draw` (`:169-187`), `Heading` (`:92-93`), `MinimapFootprintVisual` (`:230-291`; deleted in 3b).
- **Shared (S).** `MinimapCameraFootprint.TryBuild`, `MinimapHud`, `MinimapDiscImage` (Godot's CPU disc).
- **B, moved in 3b.** `MinimapCameraView`: the view centre's and the four viewport corners' hits on the floor plane
  (rays through the off-centre frustum, `TileTopPlane.TryHit`), the yaw (Unity's `eulerAngles.y`) and the heading as yaw x
  2048/360, rounded half to even and unmasked. `MinimapCameraFootprint.Triangles` builds the ribbon's index list, UV and
  placement; `MinimapHud.SetCamera` makes it the frame's first draw, in whole-map mode; `HudTriangles.ClipToCircle` cuts it
  to the disc on the CPU, replacing Unity's rounded-overflow mask. `HudDrawKind.Triangles` (open point 9) carries it in
  both `HudCanvas`es (`CanvasItemAddTriangleArray` has a 4.8 trap in TRAPS).

## Possession camera

- **Unity.** `DungeonRunner.Possession.cs` (83): `UpdatePossession` (`:31-53`), `ApplyPossessionCamera` (`:56-67`), the
  pointer lock (`:70-75`); the ceiling and wall latch in `Update` (`DungeonRunner.cs:963-971`).
- **Shared (S).** `PossessionSession` (input, camera, hidden id, commands, sounds), `PossessionCamera`, `TerrainCeiling`.
- **B, small.** The roof shows exactly while a model is hidden (`:970-971`), and the lattice and view take the
  possession latch (`:963-966`). Both join the `SceneFrame` sequence (open point 7, decided in 3g).
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
Godot visibly better, checked by unit tests for what moved and a Movie Maker capture (below). The first three are the
terrain and the camera; then each step adds one kind of thing to the world. From 3j onward [the modern-rendering audit](2026-11-18-godot-modern-rendering-audit.md) supersedes this order (ADR-0813: 3o to 3q are new, 3j is folded into lighting).

- **3a. Static terrain from the level-start camera.** Done 2026-11-16 (the plan's step 3a has the detail): `RightHandedFrame`,
  `TerrainTileBuilder`, `TerrainMaterialKey`, `ClassicMaterialRecipe`; Godot's `WorldScene` and `Camera3D`.
- **3b. Camera control and the minimap.** Done 2026-11-16 (plan step 3b): `MinimapCameraView`, `HudDrawKind.Triangles`,
  `LaunchArguments.GlideTarget`; camera input, every glide, the minimap's live focus and footprint. No live input checked.
- **3c. Terrain that changes.** Done 2026-11-16 (plan step 3c, ADR-0805): `TerrainChunkScheduler`, `TerrainMaterialLook`,
  `LiquidSurfaceWave` and `LiquidSurfaceField`. No clean wave capture exists yet.
- **3d. The blended stream.** Done 2026-11-16 (plan step 3d, ADR-0805): `BlendedTerrainStream<TMaterial>`, `ViewFrustum`; one
  instance per run with `sorting_offset` (open point 4), blends in gamma space. Not captured: the sight fog.
- **3e. Pointer, cage and placement: the level becomes playable.** Done 2026-11-17 (plan step 3e, which has the numbers;
  ADR-0805). Moved: `TileHoverOutline.Build`, `PlacementCursorHud`, the green key, `HandCursorHud`, `PointerScript`. Godot:
  picking, the hand cursor, the cage and the placement cursor, checked by scripted-pointer captures.
- **3f. Objects, structures and torch meshes.** Done 2026-11-17 (plan step 3f, which has the numbers; ADR-0805). Moved:
  `RenderRotation`, `ObjectMaterialLook`, `EffectGroupParts`, the flame point and phases. Godot: the Heart, doors, traps,
  torch meshes and flames, checked against the Unity probe frames; the environment pass came in 3i.
- **3g. Creatures.** Done 2026-11-17 (plan step 3g, which has the numbers; ADR-0805). Moved: `SceneFrame`,
  `ISceneFrameHost`, `SceneCreatures` (open point 7). Godot: the imps and the query follow on the drawn position.
- **3h. Entity hover and the Hand.** Done 2026-11-17 (plan step 3h, which has the numbers; ADR-0805). Moved: the pick measures and the cull.
  Godot: entity offers, the red pass, the gold swap (uncaptured); pickup and drop were wired in 3e. The hover sound waits for
  step 4; the hand light came in 3i.
- **3i. Lighting.** Done 2026-11-18 (plan step 3i, which has the numbers; ADR-0805). Moved: `ClassicVertexLight` (the table sum
  and the packed diffuse) and `ClassicLightTableLayout`. Godot: the vertex-lit shader and light-table textures, the publisher,
  the ambient keys and the torch, portal, object and Hand lights, the environment pass (also over plans), the cage's key,
  the liquids' normals and the exposure in the colour correction. Against Unity's in-game frames: see open points 2 and 3.
- **3j. Creature shadows.** Move: source selection, fade and the silhouette projection. Godot: the chosen path (open
  point 8); the modern lighting it was folded into was rejected and removed (2026-11-18), so this is open again (ADR-0813). Check: a capture of an imp beside a torch; tests for the selection ties.
- **3k. Effects and status flowers.** Move: `EffectFilters`, the flower colour reference. Godot: billboards, flat
  quads, mesh particles, flowers and the dollar in the blended stream, given their order through sorting offsets
  (`PlaceItems`/`RunsBehind`), or Godot sorts them by real distance among the run instances. Check: dig debris and a
  flower over an imp.
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
`--load <name>`) or a skip argument; 120 frames never show it. A pointer-driven check adds `--pointer-path`:

```powershell
& "$env:LOCALAPPDATA\Programs\Godot\4.8-beta3\Godot_v4.8-beta3_mono_win64\Godot_v4.8-beta3_mono_win64_console.exe" `
  --path godot/DK2.Godot res://InGameHud.tscn --write-movie <temp>/f.png --fixed-fps 30 --quit-after 120 `
  -- --settings <temp>/settings.ini
```

It opens a window and needs the GPU. Every step that changes a Unity file reruns the stand-in compile
(`unity/DK2.Unity.Compile`) and Unity's own batch checks (`unity/README.md`, *Verifying it inside Unity*); only the
owner's open editor must not be driven.

## Open points for the owner

1. **The frame.** Decided (2026-11-16, option 1): an explicit Z flip through the tested `RightHandedFrame`.
2. **How Godot's lighting is tuned.** Decided by the owner (2026-11-16) and corrected in 3i (2026-11-18): the DK2 terms are
   shared; the target is Unity's in-game look from the owner's saved scene (native initial ambient, fog of war), which is
   DK2's light with the ADR-0522 distance fade and no fill or sun. `--light-fill` adds the ADR-0522 fill. The owner then
   asked for a prototype of modern Godot lighting (the plan's item after 3i).
3. **What to compare against.** Corrected in 3i: the in-game frames of `ClassicWorldLightingProbe` since cd4e04a0 (Level1 and
   Level7 in batch mode, recipe in the plan's step 3); the earlier probe frames had no grading and no sun. 3f and 3g
   compared against the earlier ones; 3i against the in-game ones, within 0.002 luminance and 0.004 saturation.
4. **Blended ordering in Godot.** Decided in 3d by prototype and engine source: one instance per run with
   `sorting_offset`; the exact per-triangle order is kept, with no deviation.
5. **The light publisher's pose.** Unity gives it a pose with zero centre shift, pitch and yaw (`DungeonScene.cs:892-899`).
   Decided in 3i: Godot matches Unity's zero shift, pitch and yaw; passing the full pose in both engines stays open.
6. **A no-install mode.** Decided (2026-11-16): Godot requires the install, like the HUD, with no placeholder mode and
   no free camera.
7. **The scene frame order.** Decided in 3g (ADR-0805): a shared `SceneFrame`, hosted by `DungeonScene` and `WorldScene`.
8. **Shadow silhouettes.** CPU rasterizing in `src/` (testable, no second creature render) or a GPU `SubViewport`.
9. **The footprint ribbon.** Decided in 3b: a shared triangle-list `HudDrawKind`, which Unity draws too.
10. **Pointer-driven captures.** Decided in 3e: `--pointer-path "frame:x,y[:LlRr];..."`, a scripted pointer pushed into
    the viewport, so a capture never moves the owner's cursor (TRAPS); not an input-journal replay. 3h and 3m use it.
11. **Content for 3d, 3l and 3m.** Level1 has no lightning shot, and nothing stages a Sight of Evil cast, so the sight
    fog (3d) is uncaptured; a staged test level or launch argument is needed, built with `tools/levelgen` or a new argument.

Out of scope here: the front end's 3D campaign map and its land picking (`DungeonScene.PickFrontEndMap`, `:245-264`), the
level sound driver and subtitles (step 4), and the probes (step 4).
