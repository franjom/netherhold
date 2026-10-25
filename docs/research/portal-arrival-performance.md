# Portal arrival rendering cost

## Finding

CONFIRMED in the controlled remake probe, 2026-10-25: moving Warlock arrival
geometry repeatedly rebuilds the shared transparent-terrain stream. The costly
part is run preparation/mesh upload, with additional render-submission cost;
the triangle depth sort itself is smaller. Independent `verify_arrivals` review
checked the source, all seven phase aggregates and the rendered close-up.

The owner's Level2 Warlock-introduction screenshot reports 14 FPS, 71.86ms
average and 75.69ms slowest in a half-second window. The probe establishes a
substantial rendering cost, not the cause of that entire frame interval.
No optimization or gameplay change was made.

## Controlled experiment

Run `tools/classic-unity-probe/run-tag-overlay.ps1 -PortalPerformance` with
an activated Unity editor. It uses a marked isolated project, not the owner's
open scene. `PortalPerformanceProbe.cs` uses the real Level2 data, effect165,
`ClassicConstructionEffects`, `DungeonScene` lighting and `TransparentTerrain`.
Simulation does not run. The camera is fixed; effect time advances 16ms per
callback after a 2s preadvance, so moving particles exercise the dirty path.
Each phase has 15 excluded warmup callbacks and 60 measured callbacks.

Environment: Unity6000.6.0f1, D3D12, Ryzen7 7735HS, Radeon integrated graphics,
1920x1080 render target, fog off, native ambient, classic vertex lighting.
The camera is a probe close-up, not the campaign path. The inspected image
shows the hole, crystals, orange beam and sparkles, without creature or HUD.

Authoritative close-up artifacts:
`build/tag-overlay-probe/arrival-performance-20261025-102039/`.
`frames.csv` contains raw samples; `metadata.txt` records setup;
`provenance.json` records source HEAD and assembly/probe SHA-256;
`probe-source.cs` preserves the executed script. Production source HEAD was
`de57a87caec82b04c1ea5af57f6ced77ce2b545e`.
Probe source SHA-256: `3E88145280F7808A73495E5BFEC7230D141A65781000B0540321AC21D325E0E4`.
These are regenerable remake captures, not native-game evidence.

An earlier, wider view is retained at
`build/tag-overlay-probe/arrival-performance-20261025-101830/`.
Its metadata/camera differ; its numbers must not be mixed with the close-up.

## Results

Medians in milliseconds. Stopwatch scopes are separate; the three profiler
markers are nested, so Upload/Depth must not be added to the inclusive Sort.
Render submission may include driver/GPU waits: it is not GPU duration, pure
CPU execution time, a normal game frame interval, or an FPS measurement.

| Phase | Particles | Light preparation | Effect draw | Sort inclusive | Depth marker | Upload marker | Render submission | Active runs |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Baseline | 0 | 1.149 | 0.005 | 0.005 | 0 | 0 | 1.558 | 317 |
| Full effect | 551.5 | 1.237 | 1.340 | 7.915 | 0.962 | 6.701 | 17.358 | 798 |
| Effect without light | 551.5 | 1.699 | 2.060 | 10.236 | 1.091 | 8.858 | 23.029 | 798 |
| Effect model only, no geometry/light | 551.5 | 1.345 | 0.001 | 0.009 | 0 | 0 | 2.080 | 317 |
| Frozen effect clock | 544 | 1.808 | 2.143 | 0.008 | 0 | 0 | 6.633 | 786 |
| Full effect repeated | 551.5 | 1.487 | 1.684 | 8.141 | 0.997 | 6.937 | 18.326 | 798 |
| Baseline afterward | 0 | 1.273 | 0.007 | 0.005 | 0 | 0 | 1.858 | 317 |

Full/full-repeat inclusive-sort nearest-rank p95 is11.240/11.173ms. The full effect adds
geometry to the baseline's 875 vertices/956 triangles: median3341/2183.
Active runs count configured active MeshRenderers, not measured GPU draw calls.
The frozen phase has slightly fewer particles, rather than identical geometry.

The geometry/light exclusion must be compared with the light-excluded phase
to isolate geometry submission. All moving variants use the same seed/time
sequence. The no-light phase being slower than full demonstrates phase/editor
noise; subtracting those light timings would not measure a precise light cost.
Both full phases retain the substantial moving-geometry cost, and the final
baseline returns close to the starting baseline.

First draw in the first full phase took52.31ms, versus1.34ms warmed median:
first-use artwork/mesh preparation is a separate cost from the steady work.
The source eagerly prepares sprite atlases and all beam animation meshes.
The measured first draw does not individually attribute that cold cost.

## Mechanism and optimization target

`ClassicConstructionEffects.Draw` rebuilds billboard/flat meshes and submits
them through `TransparentTerrain.AddEffectMesh`. Changed vertices, colours,
indices or transforms mark the shared stream dirty. `BeforeCamera` then:

1. `Collect` copies all registered transparent terrain and effect geometry,
   not only the effect which changed; it has no spatial/frustum filter.
2. It recomputes world positions and sorts all triangles in depth order.
3. `SelectRuns` discards retained triangle/vertex mappings when geometry changed.
4. It rebuilds and uploads every run's vertices, normals, UV channels, colours
   and triangles. Each material transition in sorted order creates a run.

The moving effect therefore defeats the retained-static optimization even with
a fixed camera. Freezing effect time exercises the unchanged-geometry early-out;
simulating the effect without submitting its geometry also removes the cost.
Effect lights update dynamic sources; they do not globally invalidate the
static light cache. Particle advancement itself is about0.005ms median here.

Next optimization target: preserve correct global depth ordering while reusing
unchanged terrain data and run geometry, then reduce effect material transitions
where ordering permits. Simply bypassing the shared stream would regress the
disc/chasm occlusion fixed in the arrival renderer. Any optimization needs the
existing terrain/effect ordering probes and matched moving-effect measurements.

## Limits and next measurement

This is an Editor batch probe with explicit render requests, not the ordinary
standalone/editor game loop. GPU duration, actual draw calls and GPU overdraw
were not measured. The exact Level2 camera, fog state, live creature animation,
HUD and worker waits in the owner's frame remain unprofiled. A full live or
standalone CPU/GPU capture of that introduction is required to attribute all72ms.

Edit-mode effect teardown logs Destroy errors; all420 measured close-up samples
recorded zero warnings/errors during their timed work. Cold and teardown frames
are excluded. Per-frame metric collection and screenshots are outside the scoped
timers but can perturb later editor/GC scheduling. Marker counters describe
recent completed editor frames; 15 warmup callbacks exclude phase boundaries.
No exact allocation claim is made; Mono's allocation-counter trap still applies.

This investigation does not recommend reducing the effect's authored particles,
colour or brightness to hide the performance cost.
