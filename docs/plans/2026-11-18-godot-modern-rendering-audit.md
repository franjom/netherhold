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
   `NextPass` copies (`WorldScene.Objects.cs:404-440`). Creature mesh sharing is covered by item 8.
3. **4.8 APIs that fit:** `mesh_surface_update_index_region` (`rendering_server.h:230`) for index-only re-sort uploads;
   `instance_geometry_set_shader_parameter` (`rendering_server.h:782`) for per-instance uniforms.
4. **In-place vertex rewrites get no motion vectors** (`render_forward_clustered.cpp:1090-1093`): liquids and torch bends
   remain CPU-updated. Creature animation now uses item 8, whose phase has no previous value either, so TAA/FSR2 stay
   deferred (item 17); MSAA, SMAA and the render scale are settings (item 17).

## Items

Priority: P0 is a prerequisite, P1 the largest gain, P3 last. Sizes S, M, L. "Keep" marks an old technique that is
already right in Godot. Estimates are marked; the harness (item 0) replaces them with measurements.

- **0. P0, S. Performance harness. Done (3o, 2026-11-18).** `--perf-report` records rendering info (draw calls,
  objects, primitives, memory), CPU and GPU render time, frame time and allocation per frame, with `godot/perf.ps1` as
  the capture recipe and `godot/perf_compare.py` for comparisons (`godot/README.md`, *Measuring performance*; migration
  plan 3o). Baseline GPU p50 at 1080p on a heavily loaded iGPU: classic Level1 1.9 ms, Level7 2.4 ms; modern 6.2 and
  9.3 ms; draw calls classic 143 and 320, modern about 490 and 995. The quiet re-run (2026-11-25, classic) is the
  harness ceilings' baseline in `tools/tester-package/performance-target.json`. It makes items 3, 4 and 8
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
  The per-pixel torch rim and AO noise have adapter corrections: continuous fragment point lighting and camera-owned
  C# post-opaque AO compute passes with depth/normal filtering. The classic vertex reference is unchanged; historical
  cause, current acceptance and measured costs: [torch-light artifacts](../research/godot-torch-light-artifacts.md).
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
  overlay material. Loaded-machine frame p50 spreads: Level1 HEAD 4.94 to 5.73/new 5.07 to 5.33 ms,
  Level7 HEAD 6.51 to 7.23/new 6.23 to 6.45 ms; these runs do not isolate a speed gain from load variation.
  Final artifact root: `C:/Users/franj/AppData/Local/Temp/dk2-3pb-20261119/`; capture manifest/crops:
  `captures/comparisons-newbatch/results.json`; performance: `batch-perf/comparison.txt` and `summary.json`;
  drag samples: `captures/{headtiming,newbatchtiming}/drag-off/drag-timing.json`.
- **4. P2, M-L. Texture arrays: measured, not worth it (2026-11-20)** on the tested 1080p classic
  Level1/Level7 workloads. Keep the opaque surfaces per texture key; no texture-array implementation was built.
  Two interleaved rounds used the same turn-200 saves and 1,800-frame camera script on the integrated AMD GPU.
  Loaded terrain had 34 surfaces in 6 chunks (maximum 13) on Level1 and 135 in 20 (maximum 45) on Level7;
  hiding opaque chunks removed about 33/112 visible draws. Render-CPU p50 fell by 0.157/0.136 ms and
  0.288/0.285 ms respectively: median upper bounds of 0.147/0.287 ms, not measured array savings.
  A scratch HEAD probe split each surface into four, retaining its triangles and material, adding about 96/318
  visible draws. GPU p50 control/split was 2.582/1.978 then 1.852/1.853 ms on Level1, and 2.341/2.320 then
  2.492/2.808 ms on Level7. GPU benefit remains unresolved within noise (Level7 split repeat spread 0.488 ms).
  Allocation p50 stayed 7,296 bytes/frame on Level1 and 10,296-10,304 on Level7. Modern lighting was not measured;
  no pixel-equality or array-GPU claim follows. Revisit if another workload establishes a material submission cost.
  Raw reports and probe sources: `C:/Users/franj/AppData/Local/Temp/dk2-item4-20261120/`; summary: `summary.txt`,
  visibility runs: `probe/`, draw-sensitivity runs: `sensitivity/`. These are diagnostics, not product changes.
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
- **8. P2, L. GPU vertex animation** (`WorldScene.VertexAnimation.cs`, `CreatureAnimationPlayer.cs`).
  One shared mesh and RGBF position texture per clip replaces per-instance copies and frame uploads. CUSTOM2
  identifies source vertices; the world shader interpolates by instance phase before lighting in every pass.
  The CPU retains the clock, samples control/lightning vertices individually and lazily caches full poses for exact
  picking after sphere rejection. Clip-wide bounds replace the arbitrary AABB; blended ordering keeps the first-frame
  centre. A retained scene-owned 4 MiB CPU staging pool avoids late-bake allocation and clears texture padding; reload frees resources.
  Done (2026-11-20), independently reviewed with no findings. Verification: 11,513 Release tests passed, 30 skipped;
  focused animation 38 and pick/hover 299 passed; both builds, pinned Godot import and solution builds clean.
  Turn-400 hash: B5C0CB842094AB59. Captures: 18 pairs, 1,380 frames, 988 byte-identical and 392 differing;
  maximum 290 pixels/frame (0.014%), sparse rounding/raster precision differences invisible at 1080p inspection.
  Static lava also varies between equivalent captures; differences cannot all be attributed to VAT interpolation.
  Inspection found no geometry, culling or timing change. Main, hover/red, possession, preview and shadow paths reviewed.
  Performance: 32 reports, four measurements per build/mode/level (two interleaved single repeats plus a literal
  `-Repeat 2` run), Debug adapter/Vulkan, 1920x1080, 120 warm-up and 1,800 measured frames each, zero dropped samples.
  Pairs are HEAD -> new; medians of four run p50s, with pooled mean allocation. Modern is per-pixel/shadows/post.

  | Mode / level | Frame ms | GPU ms | Render CPU ms | Draw calls | Allocation B/frame p50 | Allocation B/frame mean |
  |---|---:|---:|---:|---:|---:|---:|
  | classic / Level1 | 4.864 -> 4.585 | 1.984 -> 2.036 | 0.531 -> 0.462 | 123 -> 120.5 | 7296 -> 7296 | 7267.6 -> 7190.7 |
  | classic / Level7 | 6.873 -> 6.540 | 2.554 -> 2.177 | 0.853 -> 0.675 | 311 -> 305 | 10296 -> 10296 | 14533.5 -> 14011.1 |
  | modern / Level1 | 9.686 -> 9.835 | 6.366 -> 6.793 | 0.553 -> 0.583 | 211 -> 193 | 7296 -> 7296 | 8582.2 -> 8583.3 |
  | modern / Level7 | 11.609 -> 10.781 | 6.791 -> 7.025 | 0.502 -> 0.578 | 482 -> 472.5 | 10328 -> 10328 | 21052.6 -> 20389.0 |

  Classic frame and render-CPU gains repeat. Modern Level1 frame timing is inconclusive under load variation; Level7 frame
  time improves, but render CPU rises in both levels. Modern GPU cost increases overall; Level1 GPU p50 rises
  6.7%, higher in all four paired runs. Other CPU load ranged from 12.7% to 47.1%; no statistical significance is claimed.
  Allocation p50 is unchanged in all cases. Mean allocation falls except modern Level1 (+1.16 B/frame, 0.014%,
  within roughly 100 B/frame run spread); steady allocation falls in every run, including modern Level1.
  Level1/Level7 peak private clip copies fall from 9/63 to zero, with zero animation position uploads. Shared clip
  textures number 3/16, with RGBF payloads 811,152/2,389,416 bytes. Reported classic texture memory grows by
  about 3.4/3.8 MiB; GPU storage exceeds the packed payload.
- **9. P3, S-M. Liquid waves. Done (2026-11-23).** Each liquid is one static mesh, rebuilt only when its tiles change,
  whose vertices carry their grid point (`LiquidSurfaceWave.Points`, CUSTOM1); `Shaders/WorldLiquid.gdshader` (the world
  shader's opaque state with `DK2_LIQUID`) computes position and normal from it and DK2's frame, the one uniform set when
  the frame moves. The phase is DK2's integer mask (water's 4096-multiples reduced to a 32-entry index, lava's 128) into
  a sine table built once by the old `Phase` expression; rest, signed amplitude and phase steps are uniforms from
  `LiquidSurfaceWave`, whose `Vertex` mirrors the shader and is pinned bit for bit to `Wave` (positions and normals,
  overflowing frames included). Was two `SurfaceUpdateVertexRegion`s a frame (16 B/vertex), whose 4-byte normal write
  into Godot's 8-byte normal-tangent block garbled the liquids' normals (`docs/TRAPS.md`). Liquid upload per frame:
  Level9 (503 water tiles) 72,432 B -> 0, Level7 (43 lava) 6,192 B -> 0, Level1 (none) 0 -> 0.
  Captures (Movie Maker 1920x1080, `--pointer-path`, clean `git archive` HEAD, HEAD plus this change, and a scratch
  reference "fix" = HEAD rebuilding the liquid mesh every frame so Godot encodes the CPU normals correctly): Level1 to
  turn 70 flags off and all three on, 1,080 of 1,080 HEAD/new pairs identical (no liquids). Level9 (water) and Level7
  (lava), 300 frames each, off and on: new differs from HEAD only on liquid surfaces (Level9 every frame, up to 281,571
  pixels, max channel 21; Level7 80 of 300, up to 10,925 pixels, max 18), as HEAD vs fix does to within a pixel count:
  HEAD's normals were wrong. New against fix: Level9 off 72 of 300 frames differ (at most 8,762 pixels), on 300 (at most
  336), Level7 off 3 (70), on 69 (12), every difference 1 in one channel: the shader's float normal against Godot's
  16-bit octahedral one, and GPU against CPU rounding; positions and phase match (no edge or timing differences).
  A HEAD repeat of Level7 matched 300 of 300. `--possess-first-creature` from turn-200 saves of Level9 and Level7,
  flags off (and Level9 on): new = fix in all 900 frames (HEAD differs in one Level9 frame by 7 pixels, max 3); the
  possessed creatures stayed away from liquid, so possession over liquid is not covered. Crops (HEAD | new |
  difference x16): `C:/Users/franj/AppData/Local/Temp/dk2-item9/results/<case>/<a>-vs-<b>/`.
  Turn-400 hash `F5C7BAE552173778` on clean HEAD and new copies alike. Performance, `perf.ps1` 1080p, two interleaved
  repeats (medians of run p50s, HEAD -> new; Debug, iGPU, other CPU load 10-17%):

  | Case | Frame ms | GPU ms | Render CPU ms | Draws | Allocation B/frame p50 | mean |
  |---|---:|---:|---:|---:|---:|---:|
  | classic Level1 | 4.672 -> 4.716 | 1.989 -> 2.047 | 0.485 -> 0.493 | 120 -> 120 | 7,296 -> 7,296 | 6,935 -> 6,950 |
  | classic Level9 | 10.023 -> 9.288 | 3.260 -> 3.227 | 0.786 -> 0.871 | 404 -> 403 | 9,056 -> 9,064 | 38,687 -> 36,696 |
  | all flags Level1 | 11.954 -> 11.296 | 9.473 -> 8.938 | 0.752 -> 0.738 | 192 -> 192 | 8,088 -> 8,088 | 9,315 -> 9,315 |
  | all flags Level9 | 16.784 -> 16.976 | 10.163 -> 10.958 | 1.206 -> 1.066 | 594 -> 594 | 9,936 -> 9,936 | 67,387 -> 66,542 |

  Level1 has no liquid, so its changes are noise. Level9's process time falls 11-17% (the CPU wave of 4,527 vertices is
  gone), but render CPU and all-flags GPU move in opposite directions between modes (classic render CPU +0.085 ms;
  all-flags GPU +0.80 ms with a candidate frame p50 spread of 16.29-17.66 ms); no GPU gain or loss is established. The
  shader evaluates 8 face normals a vertex in every pass that draws the liquid. Raw reports:
  `C:/Users/franj/AppData/Local/Temp/dk2-item9/perf/`.
- **10. Done, 2026-11-20. Minimap disc.** `MinimapGpuDisc` replaces the CPU raster/upload
  with a reused RGBA8 viewport, a base canvas shader and ordered marker quads. Pipeline and parity command:
  [Godot README](../../godot/README.md#gpu-minimap). `MinimapDiscImage` remains the independent CPU oracle;
  Q861's top-row-first rock and the classification, clock, navigation and hit rules are preserved.
  Source verifier: no findings. Vulkan and D3D12 each pass 434 RGBA byte/hash comparisons, six fractional-display
  checks and reparenting, including two same-area map dimension changes. Cases include diameter 239 (default at 1080p)
  and 96, plus one-pixel output replicated in Godot's minimum 2x2 target. The missing-marker counterfactual fails 366/434
  cases (exit 1); restoring the shader passes (exit 0).
  HEAD/new capture comparison: 1,141/1,200 frame pairs are byte-identical; all minimap regions match. The other
  59 pairs (Level7 modern only) differ at one world pixel, (1232, 549), outside the minimap, maximum channel difference
  15. A HEAD repeat matches its original 60/60 frames and the new build only 1/60, so the cause remains unresolved;
  the preserved context crop shows a visually negligible wall texel at 1080p. No world or shadow fix is part of this change.
  At the 239-pixel default, CPU disc rasters and 228,484 RGBA upload bytes per refresh fall to zero.
  Render CPU/GPU timings cover main/preview only and exclude sparse minimap and torch-shadow viewports;
  they cannot establish a total GPU improvement. Reviewer: approved with that measurement caveat.
  Interleaved HEAD/new classic and all-flags batches, two runs of 1,800 frames per case; other CPU load 17-59%.
  Each timing/draw value below is the mean of the two run medians (HEAD -> new); allocation means are per frame.

  | Case | Frame ms | Partial GPU ms | Partial render CPU ms | Draws | Allocation median B | Allocation mean B |
  |---|---|---|---|---|---|---|
  | Classic Level1 | 5.374 -> 4.732 | 1.896 -> 2.240 | 0.548 -> 0.493 | 119.5 -> 120.5 | 7,296 -> 7,296 | 7,363.4 -> 7,214.9 |
  | Classic Level7 | 7.367 -> 6.479 | 2.154 -> 3.285 | 0.831 -> 0.802 | 304 -> 305 | 10,296 -> 10,304 | 15,105.3 -> 14,714.3 |
  | All flags Level1 | 11.384 -> 10.773 | 8.369 -> 7.569 | 0.466 -> 0.581 | 193 -> 193 | 7,296 -> 7,296 | 9,085.6 -> 8,828.7 |
  | All flags Level7 | 12.507 -> 13.083 | 8.866 -> 9.000 | 0.694 -> 0.682 | 473 -> 473 | 10,336 -> 10,336 | 21,657.9 -> 21,694.2 |

  Results are mixed and inconclusive within the measurement noise; no overall speedup is established. All-flags Level7's
  mean allocation rises 0.17%; warm marker geometry allocates zero. Full suite: 11,520 passed, 30 skipped; Release build
  has zero warnings/errors. Raw parity, counterfactual, capture and performance receipts:
  `C:/Users/franj/AppData/Local/Temp/dk2-minimap-8ni3r_3e/` (`parity-final-vulkan/`, `parity-final-d3d12/`,
  `counterfactual434-receipt.json`, `comparison.html`, `comparison.json`, `performance-summary.json`, `final-verification.json`).
- **11. P3, M. HUD text, gauges and glows. Done (2026-11-23).** Set text drawn one texel per pixel (every HUD text) is
  `HudTextPixels.Pieces` out of one luminance-alpha atlas per BF4 font, shared by all canvases; pieces never ink a pixel
  twice (glyph ink meets in 0.9-14% of character pairs of the shipped fonts; `Render`'s winner keeps the pixel), so nearest
  sampling gives `Render`'s pixels. A stretched text keeps its own texture. Gauges: a `SubViewport` per gauge filled by
  `HudGauge.gdshader` from `HudGaugeMask` ranks (exact against `HudGaugeFraction.Contains`). Glows: baked once as two
  textures (full glow; quarters 1-3 in RGB), `HudGlow.gdshader` reading the second as the normal map so the hardware filters
  each quarter as before. Front-end label bitmaps keep a texture per label: they are drawn scaled and bilinear across glyph
  edges, which pieces cannot reproduce, and change only with the page ([README](../../godot/README.md#hud-text-atlas-gauges-and-glows)).
  Captures (Movie Maker 1920x1080, `--pointer-path`, clean `git archive` HEAD against HEAD plus this change; Level1 start
  to turn 70 and with all three lighting flags, Level1 after the path hovering a creature, wall, floor and panel buttons
  (tooltips) flags off and on, Level7 start, possession, full map, query, message panel, in-game options root, Game
  Options and Save pages, `--stage-lightning`, the front end): 2,470 of 2,470 frame pairs byte-identical; 990 of them again with
  the final code. The front end and the options panel read the OS cursor (`docs/TRAPS.md`), so their hover, label glows
  and scroll-bar glows were checked with a scratch-only patch feeding `--pointer-path` to both sides: 1,040 of 1,040
  identical (740 again with the final code); the front-end set and `L1hover` also identical on D3D12. Two first runs differed only in hover state (an
  options entry lit by the real cursor; a bottom-panel tooltip while another window took focus); both reruns were
  identical. Crops: `C:/Users/franj/AppData/Local/Temp/dk2-item11/results-prelim/optroot-run1/`,
  `.../results/L1hoverflags-run2/`. Turn-400 hash `F5C7BAE552173778` on clean HEAD and new copies alike.
  Performance, `perf.ps1` 1080p, two interleaved repeats (medians of run p50s, HEAD -> new; Debug, iGPU, other load
  17-22%); the hover run cycles the pointer over world and panel targets every 30 frames:

  | Case | Frame ms | GPU ms | Render CPU ms | Draws | Objects | Allocation B/frame p50 | mean |
  |---|---:|---:|---:|---:|---:|---:|---:|
  | Level1 | 4.238 -> 4.420 | 1.986 -> 2.010 | 0.436 -> 0.459 | 119 -> 119.5 | 172 -> 217 | 7,296 -> 7,296 | 7,228 -> 6,932 |
  | Level7 | 6.038 -> 6.010 | 2.290 -> 2.221 | 0.669 -> 0.652 | 305 -> 304.5 | 352 -> 397 | 10,304 -> 10,304 | 13,705 -> 13,400 |
  | Level1 hover | 4.195 -> 4.264 | 1.909 -> 1.983 | 0.434 -> 0.445 | 119 -> 119 | 172 -> 219 | 7,296 -> 7,296 | 8,905 -> 8,014 |

  Frame and GPU differences are inside the repeat spread and load; draws do not change (pieces batch on one atlas), the
  object count rises by the glyph rects, allocation p50 is unchanged and its mean falls 2-10%. Video memory +0.2 MiB.
  Texture churn (scratch-instrumented copies, hover run, 1,920 frames): HEAD 529 HUD texture creations, 6 of them after
  frame 300, and 1 gauge upload; new 517, none after frame 300, no uploads, 5 GPU gauge fills. Live HUD texture bytes at
  the end 9.95 -> 10.08 MB (peak 10.10 -> 10.22 MB): the atlases and the gauges' rank and viewport textures cost more than
  the few text textures they replace. Raw reports: `.../dk2-item11/perf/`.
- **12. Keep, P3, S-M. CPU-built merged terrain chunks. Done (2026-11-24): the vertex bytes are packed on the build
  task.** The pieces are bent by the lattice and picking needs the CPU geometry, so the chunks stay CPU-built; no
  MultiMesh per piece, no occlusion culling, no mesh LOD, no visibility ranges. `TerrainChunkScheduler` takes an
  optional `ITerrainChunkPacker` (`DK2.Presentation`, unit-tested; Unity sets none): its build task, after the tiles it
  rebuilt, packs every chunk holding one of them, and a chunk whose changed tiles all stopped drawing is packed inline,
  so uploads land on the same flush as before. Godot's packer (`WorldScene.ChunkUpload.cs`) gathers the opaque keys
  as the frame thread did and writes the bytes `_surface_set_data` writes (positions, then octahedral normal and
  the derived tangent, then UV and CUSTOM0; 16-bit indices); the frame thread only calls `RenderingServer.MeshAddSurface`
  on one `RenderingServer` mesh and instance per chunk (no `ArrayMesh` or `MeshInstance3D`). The blended stream and the
  overlay batch stay on the frame thread, and every key's material is still looked up there in tile order. The format
  is read from a surface Godot built at open and its strides checked (docs/TRAPS.md). Attribute compression is not
  used: `ARRAY_FLAG_COMPRESS_ATTRIBUTES` quantises positions to 16 bits over the surface's box and UVs to 16 bits,
  which is lossy for these floats (not captured).
  Byte check (scratch build comparing each packed surface with `MeshAddSurfaceFromArrays` from the same arrays via
  `MeshGetSurface`): 2,787 surfaces (644,502 vertices) over Level1 to turn 70 with flags off and on, Level7, possession
  and the dig scene; format, bounds, vertex, attribute and index bytes all equal. Captures (Movie Maker 1920x1080,
  `--pointer-path`, clean `git archive` HEAD against HEAD plus this change): Level1 to turn 70 flags off and with all
  three lighting flags, Level7 start both ways, `--possess-first-creature`, and a dig scene (a turn-200 Level1 save,
  two dig areas tagged by the scripted pointer, 81 chunk rebuilds in 900 frames) both ways: 3,480 of 3,480 HEAD/new
  frame pairs byte-identical. Turn-400 hash `F5C7BAE552173778` on both copies.
  Rebuild timing (scratch-instrumented copies, Debug, no Movie Maker, two interleaved runs each; one rebuild is one
  chunk's `BeginChunk` to `EndChunk` on the frame thread, the level's opening build excluded):

  | Case | Rebuilds | Frame-thread ms per rebuild, median | p95 | Flushes that uploaded, median ms | Build-task pack ms, median (p95) |
  |---|---:|---:|---:|---:|---:|
  | Dig scene | 162 | 1.096 -> 0.387 | 3.836 -> 0.803 | 1.126 -> 0.413 | 1.170 (1.546) |
  | Level1 to turn 70 | 42 | 0.762 -> 0.299 | 8.537 -> 7.855 | 2.618 -> 1.070 | 0.790 (4.175) |
  | Level7, 600 frames | 276 | 2.061 -> 1.046 | 3.416 -> 1.511 | 3.063 -> 1.402 | 1.555 (2.010) |

  Level1's p95 is the first use of a material (the shader compile in the gather), the same on both. The pack moves
  0.8-1.6 ms a chunk onto the build task, which a budgeted flush does not wait for (docs/TRAPS.md).
  Performance, `perf.ps1` 1080p, two interleaved repeats (medians of run p50s, HEAD -> new; Debug, iGPU, other CPU
  load 8-15%). The display was off during these runs (`Couldn't get Vulkan surface capabilities` every frame), so
  Godot measured no render CPU or GPU time and the frame times are not comparable with the items above:

  | Case | Frame ms | Process ms | Render CPU ms | GPU ms | Draws | Allocation B/frame p50 | mean |
  |---|---:|---:|---:|---:|---:|---:|---:|
  | Level1 | 8.205 -> 8.053 | 3.415 -> 3.345 | n/a | n/a | 123 -> 122.5 | 11,120 -> 11,120 | 11,288 -> 10,582 |
  | Level7 | 9.539 -> 9.621 | 4.189 -> 4.289 | n/a | n/a | 304.5 -> 305 | 14,152 -> 14,148 | 20,815 -> 19,398 |

  The perf camera script rebuilds few chunks, so steady frames do not change beyond the repeat spread (8.14-8.27 and
  7.96-8.14 ms on Level1, 9.50-9.58 and 9.53-9.71 ms on Level7); the allocation mean falls 6-7% (the frame thread no
  longer builds the arrays). Rerun with the display on for the GPU columns. Raw reports and comparison logs:
  `C:/Users/franj/AppData/Local/Temp/dk2-item12/` (`perf/`, `results/`, `runs/`).
- **13. Keep. C# frustum tests** (`ViewFrustum`): the pre-sort cull, DK2-derived hover admission (gameplay), the Heart's
  beat point test and light visibility. Changing them risks picking.
- **14. Keep. HUD from `HudFrame` triangles** through canvas items, `HudTriangles.ClipToCircle`; re-recording only on
  change is the efficient path.
- **15. P2, S-M. About 48 runtime shader variants** built from code strings (`WorldScene.cs:387-413`; the flame and the
  cage). Replace with a few `.gdshader` resources (an uber shader plus uniforms) and precompilation or the shader baker
  (verify in 4.8). Gain: fewer first-use hitches. Low risk. Depends on lighting.
- **16. P2, S. `skip_vertex_transform` workaround. Done (2026-11-23): cause found, workaround removed.** Godot draws the
  depth prepass of an opaque `cull_back` shader that neither writes `VERTEX` nor discards with its default material
  (`uses_shared_shadow_material`, scene_shader_forward_clustered.h at c51ff06e8), and with the prepass the opaque pass
  tests `COMPARE_OP_EQUAL` (scene_shader_forward_clustered.cpp). The 3i lit shaders only read `VERTEX`, so the two
  programs' depths had to match bit for bit; on this machine's AMD Vulkan driver they do not for transformed models, and
  whole triangles kept the clear colour. Writing `VERTEX` (the workaround's real effect) gives the shader its own prepass.
  Reproduced on HEAD's shader (Level7 start, 90 frames): no `VERTEX` write and no discard draws the Heart, torch holders
  and creatures black (all 90 frames differ); the same with `rendering/driver/depth_prepass/enable=false`, or with only a
  `VERTEX = VERTEX + vec3(0.0)` added, draws them correctly, the two byte-identical. Today's world shader writes `VERTEX`
  (vertex animation) and discards (possession tiles), pinned by `EveryWorldShaderKeepsItsOwnDepthPrepass`, so Godot now
  places the vertex: `skip_vertex_transform` and the placement lines are gone from the four `World*.gdshader` states
  (the possession fade reads `MODELVIEW_MATRIX * VERTEX`). `Lightning.gdshader` keeps it on purpose (camera-space
  corners). Main, possession, full-map preview and torch-shadow cameras all use the same shaders: HEAD and new frames
  are byte-identical in every scenario of item 17's capture set, flags off and with all three lighting flags.
- **17. P2, S. AA and scaling. Done (2026-11-23).** Settings-file keys `RenderMsaa`, `RenderSmaa`, `RenderScale`,
  `RenderScaleFsr` (`GameSettings`, off by default; Unity's `Msaa` stays its own), applied by
  `WorldScene.RenderQuality.cs` ([Godot README](../../godot/README.md#anti-aliasing-and-render-scale)). Checked by eye:
  additive and blended terrain, alpha-blended KMF surfaces, sprites, lightning and the possession view change only at
  edges; the torch-shadow views never take MSAA; the full-map preview takes MSAA/SMAA (preview pixels only change).
  `--light-post` under MSAA was broken by design (Godot resolves the multisampled colour after the transparent pass,
  dropping the compute write), so the filter writes a factor image that a full-screen draw multiplies into the samples;
  its 400 B/frame callback allocation is HEAD's (`GetRenderSceneData`). TAA and FSR 2: deferred. In Godot's
  motion-vector pass `vertex()` runs again with the previous transform but the same instance uniforms, so the
  vertex-animation phase has no previous value, and the in-place rewrites (liquids, torch bends) have none either:
  animated surfaces would get zero motion and ghost. Needs a previous-phase instance uniform first.
  Separate render thread (`rendering/driver/threads/thread_model=2`, experimental warning in 4.8): no gain, keep the default.
  Captures (Movie Maker 1920x1080, `--pointer-path`, clean `git archive` HEAD against HEAD plus this change; Level1 to
  turn 70, Level7 start, `--possess-first-creature`, `--stage-lightning`, `--open-full-map`; flags off and all three
  lighting flags): 2,560 of 2,560 HEAD/new frame pairs byte-identical. Each AA setting against new with the same
  lighting differs as expected: MSAA 4x at most 109,805 pixels (edges), SMAA 147,544 (edges and texture detail),
  scale 75% and FSR 75% every pixel, the full map only in its preview (at most 368 pixels). Crops (HEAD | new |
  difference x16): `C:/Users/franj/AppData/Local/Temp/dk2-item1617-20261123/results/` (`<a>_vs_<b>/<scenario>/`).
  Turn-400 hash `F5C7BAE552173778` on clean HEAD and new copies alike. Performance, `perf.ps1` 1080p, two interleaved
  repeats (medians of run p50s, HEAD -> new; Debug, iGPU, other CPU load 15-63%):

  | Case | Frame ms | GPU ms | Render CPU ms | Draws | Allocation B/frame p50 | mean |
  |---|---:|---:|---:|---:|---:|---:|
  | classic Level1 | 4.397 -> 4.959 | 2.166 -> 2.664 | 0.430 -> 0.517 | 119.5 -> 121 | 7,296 -> 7,296 | 7,213 -> 7,275 |
  | classic Level7 | 5.432 -> 5.473 | 2.851 -> 2.965 | 0.631 -> 0.678 | 302.5 -> 303 | 10,304 -> 10,300 | 13,368 -> 13,629 |
  | all flags Level1 | 14.109 -> 13.172 | 11.368 -> 10.854 | 0.784 -> 0.784 | 192 -> 192 | 8,096 -> 8,096 | 10,529 -> 10,385 |
  | all flags Level7 | 16.522 -> 15.928 | 11.968 -> 11.517 | 0.940 -> 0.921 | 468 -> 468.5 | 11,156 -> 11,148 | 24,545 -> 24,722 |

  The frames are identical and the default path runs no new per-frame code (the AO callback adds one `GetMsaa3D`), so
  these differences are load noise (classic Level1's new run 1 ran at 63% other load; ranges overlap elsewhere).
  Settings cost against new, frame / GPU ms, Level1 then Level7: MSAA 4x +0.45/+0.75 and +1.62/+0.64 (+104 MiB video);
  SMAA +0.69/+0.93 and +1.98/+0.84; scale 75% bilinear -0.77/-0.80 and +0.25/-0.71; FSR 75% -0.12/+0.11 and +1.13/+0.07;
  MSAA 4x with all flags +8.12/+8.07 and +6.11/+7.98 (+120 MiB). Separate thread: classic +0.10/-0.56 and +1.63/-0.23,
  all flags +0.76/-0.68 and +2.70/-0.43, main-thread process p50 +2.0 to +12.5 ms. Draws and main-thread allocation p50 do not grow for any setting (the
  separate thread moves the AO callback's off the main thread). Raw reports: `.../dk2-item1617-20261123/perf/`.

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
9. Item 4 is measured/no-go above; the minimap shader is complete with the limits in item 10; items 9, 11, 12, 16 and
   17 are done. No replacement remains.
10. The rest of step 4 unchanged; its performance targets are set from 3o's numbers.

Acceptance for a modern item: the owner's eye, the harness numbers and gameplay correctness (the turn-400 hash, the pick
and hover tests, scripted-pointer captures). Byte-identity with Unity no longer applies.

## Built pieces to replace, in order

(Done in 3q: `ZeroOrderOffset`, the origin-order run keys and the run vertex rewrites; creature copies/uploads: item 8.) The `NextPass` red and
environment chains and the per-alpha and per-tint materials; the terrain variant keys and overlay triangles; the
flame's per-frame materials; the per-string HUD text
textures; the CPU liquid wave (done, item 9); the gamma lookup (with the prototype).

## Shared `DK2.Presentation` code that becomes Unity-only or dead

- **Unity-only:** `BlendedTerrainStream.SortingOrder`; the tag-overlay predicate; the flower and effect slots
  (`PlaceItems`, `RunsBehind`, `ItemOrders`, `AddEffect`, `RepeatEffect`), while collect, cull, depth order and runs
  stay; per-key terrain surfacing for tagged, red, tinted and overlay (`TerrainMaterialKey.For` and the
  `TerrainMaterialLook` colours stay as rules); `LiquidSurfaceWave.Wave` and `LiquidSurfaceField.Animate` (Godot calls `Refresh` and waves in
  `WorldLiquid.gdshader`; the constants, sine tables, phase steps, `Points` and the `Vertex` mirror stay shared); `ICreatureAnimationSurface.Upload` (the frozen Unity path; Godot uses the pose surface from item 8); `HudImagePixels.Fill`; `ClassicMeshShadow` and the silhouette code; `EffectQuadBuffer`.
- **Dead in production:** `MinimapDiscImage` (retained as the independent shader-test oracle).
- **Stays shared:** `HudTextPixels.Measure`, `HudTriangles`, `ViewFrustum`, `TerrainChunkScheduler`,
  `TerrainTileBuilder`.
- **Godot code that goes:** (`ZeroOrderOffset`, `SortFromOrigin`'s scheme and `RunSlot`'s vertex expansion went in 3q; `Copy` was removed in item 8)
  `_redMaterials`, `_chainedMaterials`, `GammaOutput`, `GammaTable`.
- Unity keeps its emulation as the frozen classic reference until step 5 (owner stopped Unity performance work
  2026-11-15); no modern work is ported to it. Godot's classic path stays a switch until the owner accepts the modern
  look.

## Not emulation, separate changes

- Simulation runs on the main thread in Godot's `InGameHud` (Unity uses a tick worker) and competes for frame time.
- `ScenePresenter.Present` allocates 2.8 to 10.6 KB a frame (3c open item).
