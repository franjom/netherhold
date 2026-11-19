# Godot modern-rendering audit

**Status:** audit, 2026-11-18, read-only, approved by the owner as proposed. Policy: [ADR-0813](../DECISIONS.md).
It re-orders the rest of step 3 of [the migration plan](2026-11-15-godot-migration.md) and supersedes the move order
of [the step-3 audit](2026-11-16-godot-step3-audit.md) from 3j onward. Nothing was built or run. File:line
references are as of HEAD 57bbd328 with the uncommitted lighting prototype, and go stale as work lands. Godot source
references are to the 4.8 engine tree (`render_forward_clustered.cpp`, `rendering_server.h`).

Owner, 2026-11-18: "my stance in general, we use all the modern features a new modern engine gives us... no gimmicky BS
from 20y ago, that enables us a better tuning experience to gain as much of performance as possible". Rendering and
presentation only; DK2's rules, simulation and AI stay faithful, and DK2's data drives the presentation.

## Findings that change the plan

1. **Draw order copied Unity, not DK2.** `ZeroOrderOffset` put every blended object, flame and the cage into Unity's
   sorting-order-0 slot, before every blended terrain run, so a blended object in front of a gem crystal drew under it.
   Settled in 3q (item 5): runs and objects now sort by real distance.
2. **Godot already instances** consecutive same-mesh, same-material opaque surfaces
   (`render_forward_clustered.cpp:896-900`), so MultiMesh for repeated objects gains little while materials are shared.
   What breaks batching is per-state materials: `Faded(alpha)` per alpha byte, cached forever
   (`ObjectMaterialLook.cs:85-90`, `WorldScene.Objects.cs:769`); one per `Tinted` added light; the red and environment
   `NextPass` copies (`WorldScene.Objects.cs:404-440`); per-creature mesh copies.
3. **4.8 APIs that fit:** `mesh_surface_update_index_region` (`rendering_server.h:230`) for index-only re-sort uploads;
   `instance_geometry_set_shader_parameter` (`rendering_server.h:782`) for per-instance uniforms.
4. **In-place vertex rewrites get no motion vectors** (`render_forward_clustered.cpp:1090-1093`): creatures, liquids and
   torch bends would ghost under TAA or FSR2. `project.godot` sets no AA, upscaling or frame pacing.

## Items

Priority: P0 is a prerequisite, P1 the largest gain, P3 last. Sizes S, M, L. "Keep" marks an old technique that is
already right in Godot. Estimates are marked; the harness (item 0) replaces them with measurements.

- **0. P0, S. Performance harness. Done (3o, 2026-11-18).** `--perf-report` records rendering info (draw calls,
  objects, primitives, memory), CPU and GPU render time, frame time and allocation per frame, with `godot/perf.ps1` as
  the capture recipe and `godot/perf_compare.py` for comparisons (`godot/README.md`, *Measuring performance*; migration
  plan 3o). Baseline GPU p50 at 1080p on a heavily loaded iGPU: classic Level1 1.9 ms, Level7 2.4 ms; modern 6.2 and
  9.3 ms; draw calls classic 143 and 320, modern about 490 and 995. A quiet re-run is pending. It makes items 3, 4 and 8
  measurable.
- **1. Gamma-space output** through the `srgb_to_linear` lookup (`WorldScene.cs:157`, `:415-451`;
  `color_conversion_disabled` `:404`; brightness `WorldScene.Lighting.cs:471-477`). Covered by the lighting prototype
  (linear HDR, tonemap, exposure). Risk: the look shifts; the tag overlay (+64/65), the hover red and the fog
  strengths need retuning by eye. Status: built in `--light-modern` (linear HDR, ACES, depth fog), then rejected by
  the owner for its look and removed (2026-11-18); open.
- **2. Classic vertex light**, its tables, fill, the environment-pass light and the 3j shadows
  (`WorldScene.Lighting.cs`). The real-lights prototype (128 pooled `OmniLight3D`) was rejected and removed (2026-11-18). New
  direction: DK2's light model stays and moves to modern means; step 1 is `--light-perpixel` (migration plan, *DK2 light
  per pixel*). Step 2, `--light-shadows` with it (`WorldScene.Shadows.cs`, 2026-11-18): the two torches nearest the
  focus each render a downward 150-degree distance map (one `SubViewport`, casters on their own layers; a dual
  paraboloid misplaced the terrain's large triangles and was dropped). A wall's shadow multiplies only that torch's DK2
  term; a creature's or object's darkens the whole per-pixel light by DK2's 1 - coverage x strength (the receiver
  quad's blend; owner, stronger shadows), with the caster rule in `ClassicShadowSourceSelector` (shared with Unity's
  `ClassicCreatureShadows`). A torch holding a map counts 1.5 tiles nearer, so the maps do not swap back and forth.
  Two torches (four cost +1.9 to +3.0 ms with the creature darkening). Level1 1080p, 2 repeats against per-pixel:
  GPU p50 3.80 to 4.27 ms, draw calls 133 to 223, video memory +18 MiB, 0 B a frame.
  Step 3, post effects (`WorldScene.PostEffects.cs`, 2026-11-19), judged one by one by the owner. Ambient occlusion
  (`--light-post`): a full-screen quad reading the opaque depth (Godot's SSAO skips unshaded materials), subtle at
  wall bases and corners; kept; GPU p50 3.76 to 5.71 ms at Level1 1080p (2 repeats over per-pixel and shadows).
  Glow: tried, judged worse by eye and dropped (gamma 0 to 1 buffer, the threshold needs values over 1, so an
  emitter's own colour shifts; a clean glow needs a separate emitter pass). Fog: skipped, the light shader already fades by ADR-0522's 0.5 to 1 of the far
  plane per pixel, and a depth fog would also dim the full-light draws. Flags off, the Level1 frames are unchanged.
- **3. P1, M. Terrain material per tile state. Done (3p-b, 2026-11-19).** Tag, red and tint changes
  update texels without rebuilding chunks; per-vertex sources preserve neighbour-wall and quarter-floor choices.
  Godot uses shared recipe materials and shader overlays; Unity retains its reference path. Fog opacity keeps its
  material/rebuild path. Rendering and texture-upload details: [Godot README](../../godot/README.md).
  Colour-reference and geometry tests pass. Movie Maker at 1080p with scripted input: eight of nine final frames
  are byte-identical, including the turn-200 red tutorial overlay with AO. The flags-off tag/cage frame differs in
  3,548 pixels by at most 1/255 per channel, invisible at native size (combined-shader versus framebuffer-add rounding).
  An earlier blink frame updates immediately while HEAD waits for a chunk rebuild. A warmed 30-frame dig-drag window:
  mean process 6.29 to 5.10 ms, p50 2.67 to 2.76 ms, p95 20.19 to 14.80 ms; mean allocation 24,205 to 8,953 B/frame,
  p50 unchanged at 3,120 B. Clean exclusive 1080p runs alternated HEAD/new for two repeats; medians of run p50s:
  Level1 draws 124.5 to 122.5, world materials 27 to 24, GPU 2.400 to 2.532 ms, frame 5.331 to 5.198 ms,
  allocation 7,296 B unchanged; Level7 draws 306 to 310.5, materials 94 to 95, GPU 3.434 to 3.053 ms,
  frame 6.869 to 6.341 ms, allocation 10,304 to 10,296 B. Level7 adds static overlay chunk draws and one shared
  overlay material. Loaded-machine frame p50 spreads: Level1 HEAD 4.94–5.73/new 5.07–5.33 ms,
  Level7 HEAD 6.51–7.23/new 6.23–6.45 ms; these runs do not isolate a speed gain from load variation.
  Final artifact root: `C:/Users/franj/AppData/Local/Temp/dk2-3pb-20261119/`; capture manifest/crops:
  `captures/comparisons-newbatch/results.json`; performance: `batch-perf/comparison.txt` and `summary.json`;
  drag samples: `captures/{headtiming,newbatchtiming}/drag-off/drag-timing.json`.
- **4. P2, M-L. One opaque surface per texture key per chunk** (`WorldScene.cs:285-293`). Replace with a
  `Texture2DArray`, a per-vertex layer and self-illumination: 1 to 2 surfaces per chunk. Gain: opaque draws drop from
  hundreds (estimate; measure with item 0) to about 2 a chunk. Risks: per-size arrays; mips and clamp identical; phase
  and decay need a layer-offset texel or a rebuild. Depends on 3 and 0.
- **5. P1, M. Blended terrain order. Done (3q, 2026-11-19).** The shared `BlendedRunSlots` keys each run by its
  distance to the eye (the nearest centroid, falling down the stream so the runs keep their order), Godot's own sort measure,
  so blended objects, flames, the cage and the effect batches, keyed by their box centres, fall between runs; terrain
  overlays now use item 3's batches. A run's slot keeps its vertices and a re-sort uploads its indices and the vertices it
  never held; `ZeroOrderOffset` and `SortFromOrigin` are deleted. Measured (Level7, 903 re-sorts): 8.3 B uploaded a drawn
  triangle, 94 run instances as before, re-sort CPU 0.28 to 0.36-0.39 ms mean, moving frame p50 6.84 to 7.37 ms; the runs'
  normals, written at the wrong vertices before (Godot's added tangent), are right now. Open (migration plan 3q): distance
  against DK2's depth along the view near the screen's corners; a blended object straddling a run; effect batches as one box.
- **6. P3, S. Torch flames. Done (3k, 2026-11-19)** but the glow: the frames are one `Texture2DArray`, one material,
  every flame an instance of one blended-item MultiMesh batch with its frame (`ClassicTorchFlameAnimation`, on the CPU)
  in the custom data, placed among the runs by its own distance; no material swaps. Was one RS instance per flame and one
  `ShaderMaterial` per frame. Frames byte-identical to before (migration plan 3k). Open: HDR emissive for the glow
  (with the post effects).
- **7. P1, S-M. Hover red, burning red and the environment pass** as `NextPass` chains on cached copies, and fade and
  tint as new materials (`WorldScene.Objects.cs:401-440`, `:742-784`; `WorldScene.Hover.cs:57-99`). Replace with one
  world shader with instance uniforms (`hover_red`, `alpha`, `added_light`), the environment pass folded in, and no
  outline shader (DK2's red tint is the design). Gain: one draw fewer per hovered surface; removes `_redMaterials`,
  `_chainedMaterials`, `Copy` and the unbounded per-alpha and per-tint materials, which restores batching. Risks: red is
  skipped on KMF-blended surfaces (a rule); a faded environment pass still draws black. Built together with the
  prototype's shader.
- **8. P2, L. Per-template mesh copies, CPU frame lerp and an in-place upload every frame** per creature and animated
  object (`WorldScene.Objects.cs:466-488`, `:856-958`; `CreatureAnimationPlayer.cs:176-178`;
  `RenderAnimation.cs:37-54`). Replace with a vertex animation texture lerped in `vertex()` by an instance-uniform
  phase, one mesh per clip (or blend shapes, for motion vectors); the CPU keeps the clock and samples on demand (control
  points, lightning, picking after the sphere test). Gain: removes the lerp and the upload (about 100 creatures x 500
  vertices, about 600 KB a frame, 0.3 to 0.6 ms; estimates), the copies' memory (3g open item 1) and the AABB hack.
  Risk: picking needs a lazy sample after the sphere test, a new entry in the shared player. Depends on the lighting
  `vertex()` and 3l.
- **9. P3, S-M. Liquid waves**: positions and normals on the CPU, uploaded twice a frame (`WorldScene.cs:550-651`;
  `LiquidSurfaceWave.Wave`). Replace with a vertex shader from (tile, i, j) and time; DK2's phase masks are exact in GLSL
  integers. Gain: small (under 0.2 ms, estimate); a continuous-motion option. Low risk. Depends on lighting.
- **10. P2, M. Minimap disc** rasterised on the CPU and uploaded per tick and heading change (`MinimapDiscImage.cs`;
  `ClassicMinimap.cs:160-183`). Replace with a `canvas_item` shader ported from Unity's `DK2Minimap.shader`, markers as
  quads. Gain: removes the O(d^2) raster and the upload (about 1 ms at 250 px, estimate). Keep `MinimapDiscImage` as the
  oracle; the rock flip is Q861.
- **11. P3, M. HUD text, gauges and glows** as CPU pixels, one texture per distinct string
  (`HudCanvas.Textures.cs:60-103`). Replace with a glyph atlas per BF4 font, a gauge canvas shader, glows baked once or
  in a shader. Gain: no texture churn per hover string or counter. Low risk.
- **12. Keep, P3, S-M. CPU-built merged terrain chunks** (`WorldScene.cs:278-300`, `:987-1009`): the pieces are bent
  by the lattice and picking needs the CPU geometry. Tweak only: Godot-format vertex bytes on the pool thread and
  `MeshAddSurface`; attribute compression. No MultiMesh per piece, no occlusion culling, no mesh LOD, no visibility
  ranges. Gain: fewer main-thread milliseconds per rebuild.
- **13. Keep. C# frustum tests** (`ViewFrustum`): the pre-sort cull, DK2-derived hover admission (gameplay), the Heart's
  beat point test and light visibility. Changing them risks picking.
- **14. Keep. HUD from `HudFrame` triangles** through canvas items, `HudTriangles.ClipToCircle`; re-recording only on
  change is the efficient path.
- **15. P2, S-M. About 48 runtime shader variants** built from code strings (`WorldScene.cs:387-413`; the flame and the
  cage). Replace with a few `.gdshader` resources (an uber shader plus uniforms) and precompilation or the shader baker
  (verify in 4.8). Gain: fewer first-use hitches. Low risk. Depends on lighting.
- **16. P2, S. `skip_vertex_transform` workaround**, cause unknown (`WorldScene.Lighting.cs:461-466`; `docs/TRAPS.md`).
  Find the cause before real shadows and TAA; it unblocks items 17 and 2.
- **17. P2, S. No AA, upscaling or frame pacing** (`project.godot`). MSAA 4x or SMAA now; TAA or FSR2 after items 8 and
  16; `scaling_3d` as a performance setting; a separate render thread model. Risk: TAA ghosting on in-place meshes.

## Planned steps under the policy

- **3j silhouette shadows: folded into lighting step 2** (item 2, `--light-shadows`): DK2's caster rule (the three
  nearest torches within 2.5 tiles, fading from the fourth) is `ClassicShadowSourceSelector` in `DK2.Presentation`;
  `ClassicShadowMask` projection does not move to `src/`.
- **3k effects and flowers:** keep the CPU DK2 particle simulation (`RoomConstructionParticles`, `Dk2Lcg`-seeded,
  deterministic); draw it through `MultiMesh` with per-instance colour, frame and wedge, the frames in texture arrays,
  alpha-blended instances sorted on the CPU; not through `BlendedTerrainStream`'s `AddEffect` and `PlaceItems`. Effect
  lights become real lights. Footsteps are sound only.
- **3l lightning:** keep the CPU ribbon (DK2's bolt rules), emissive for the glow, an optional `OmniLight3D` flash; lazy
  tagged points if item 8 lands first.
- **3m possession:** nothing to modernise; the roof is a chunked mesh and the black roof is lighting.
- **3n paths and the full-map preview:** the `SubViewport` sharing the `World3D` is already modern; render it at low
  resolution only while visible or changed.

## Revised order

1. The lighting prototype: built and adopted, tuning next, the default switch awaiting the owner's acceptance.
2. 3o performance harness (item 0): done.
3. 3p material model on the prototype's shader: items 7 and 15 done (2026-11-19, the migration plan's 3p entry);
   item 3 is done in 3p-b (see item 3 for final comparisons and measurements).
4. 3q blended ordering (item 5): done.
5. 3j folded into lighting: done as lighting step 2 (`--light-shadows`).
6. 3k on MultiMesh, with the flames (item 6): done (2026-11-19).
7. 3l.
8. 3m and 3n (independent, parallel).
9. The remaining replacements: texture arrays (4, once 3o shows it pays), vertex animation (8), minimap shader (10),
   AA (16 then 17), HUD text atlas (11), liquids (9), chunk upload tweak (12).
10. The rest of step 4 unchanged; its performance targets are set from 3o's numbers.

Acceptance for a modern item: the owner's eye, the harness numbers and gameplay correctness (the turn-400 hash, the pick
and hover tests, scripted-pointer captures). Byte-identity with Unity no longer applies.

## Built pieces to replace, in order

(Done in 3q: `ZeroOrderOffset`, the origin-order run keys and the run vertex rewrites.) The `NextPass` red and
environment chains and the per-alpha and per-tint materials; the terrain variant keys and overlay triangles; the
flame's per-frame materials; creature mesh copies and CPU uploads; the minimap's CPU disc; the per-string HUD text
textures; the CPU liquid wave; the gamma lookup (with the prototype).

## Shared `DK2.Presentation` code that becomes Unity-only or dead

- **Unity-only:** `BlendedTerrainStream.SortingOrder`; the tag-overlay predicate; the flower and effect slots
  (`PlaceItems`, `RunsBehind`, `ItemOrders`, `AddEffect`, `RepeatEffect`), while collect, cull, depth order and runs
  stay; per-key terrain surfacing for tagged, red, tinted and overlay (`TerrainMaterialKey.For` and the
  `TerrainMaterialLook` colours stay as rules); `LiquidSurfaceWave.Wave` (the constants stay shared, for the GLSL
  substitution); the full-buffer `RenderAnimation.Sample` and `ICreatureAnimationSurface.Upload` (the player stays, with
  a new lazy-sample entry); `HudImagePixels.Fill`; `ClassicMeshShadow` and the silhouette code; `EffectQuadBuffer`.
- **Dead in production:** `MinimapDiscImage` (keep as the shader-test oracle, or delete).
- **Stays shared:** `HudTextPixels.Measure`, `HudTriangles`, `ViewFrustum`, `TerrainChunkScheduler`,
  `TerrainTileBuilder`.
- **Godot code that goes:** (`ZeroOrderOffset`, `SortFromOrigin`'s scheme and `RunSlot`'s vertex expansion went in 3q) `Copy`,
  `_redMaterials`, `_chainedMaterials`, `GammaOutput`, `GammaTable`.
- Unity keeps its emulation as the frozen classic reference until step 5 (owner stopped Unity performance work
  2026-11-15); no modern work is ported to it. Godot's classic path stays a switch until the owner accepts the modern
  look.

## Not emulation, separate changes

- Simulation runs on the main thread in Godot's `InGameHud` (Unity uses a tick worker) and competes for frame time.
- `ScenePresenter.Present` allocates 2.8 to 10.6 KB a frame (3c open item).
