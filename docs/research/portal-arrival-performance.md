# Portal arrival rendering cost

## Finding

CONFIRMED in the controlled remake probe, 2026-10-25: moving Warlock arrival
geometry repeatedly rebuilds the shared transparent-terrain stream. The costly
part is run preparation/mesh upload, with additional render-submission cost;
the triangle depth sort itself is smaller. Independent `verify_arrivals` review
checked the source, all seven phase aggregates and the rendered close-up.

The owner's Level2 Warlock-introduction screenshot reports 14 FPS, 71.86ms
average and 75.69ms slowest in a half-second window. The probe establishes a
substantial rendering cost. The ordinary-player comparison below reproduces
that frame interval and isolates the arrival effect as a major contributor.
The baseline sections below precede the renderer optimization described at the end.

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

## Baseline mechanism

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

## Ordinary player CPU/GPU capture

CONFIRMED in the remake, 2026-10-25: a rendered Development player reproduces
roughly the reported frame interval. Suppressing only arrival effect165
substantially reduces CPU frame interval and measured GPU duration. Repeating
the full effect after the control reproduces the slowdown.

`PortalIntroductionProfile.cs` runs the production Level2 scene at1920x1080,
seed1, native ambient, fog, classic meshes/lighting, HUD and worker ticks.
It claims the portal and reveals its surroundings while simulation is frozen,
skips the initial cinematic, then invokes the existing trigger executor's
original action65531 ->65530 chain after10s warmup. This creates the Warlock
and starts authored camera path60 at AP4. Normal player rendering and simulation
then run for14s. The control drops only effect165's request; it retains the
creature, camera path, notifications and other world effects. Each capture
verified exactly one effect request and one path60 request.

Environment: Unity6000.6.0f1, Direct3D11, Ryzen7 7735HS and integrated Radeon,
VSync off and uncapped. These are serial captures of the same built player;
executable and managed DLL SHA256 fingerprints are retained per run. Artifacts
under `build/portal-introduction-live/`:

| Capture | Directory | Samples / rendered callbacks |
|---|---|---:|
| Full effect | `20261025-134403` | 3052 /3051 |
| Omit effect165 | `20261025-134444` | 3156 /3155 |
| Full effect repeated | `20261025-134536` | 3192 /3191 |

All three passed rendering verification, have nonblank screenshots showing the
Warlock camera introduction, and contain no warning/error/exception log entries.
The earlier hidden-window run `20261025-134254` had zero rendered callbacks and
a black screenshot; it failed verification and supplies no performance evidence.

Medians in milliseconds for introduction seconds3.0 through5.5, away from
injection, the screenshot at2s and delayed timing boundaries:

| Measurement | Full (36 frames) | Omitted (72 frames) | Full repeated (37 frames) |
|---|---:|---:|---:|
| Ordinary loop interval | 67.249 | 33.929 | 68.289 |
| FrameTiming CPU frame | 68.135 | 34.080 | 68.135 |
| FrameTiming GPU duration | 27.098 | 3.497 | 26.366 |
| Main thread frame counter | 13.593 | 7.602 | 12.792 |
| Render thread frame counter | 58.434 | 29.603 | 59.544 |
| Inclusive transparent sort marker | 5.965 | 2.943 | 5.665 |
| Depth ordering, nested in sort | 0.840 | 0.435 | 0.818 |
| Run uploads, nested in sort | 4.893 | 2.375 | 4.507 |
| Present wait marker | 39.458 | 22.521 | 42.733 |

Full/full-repeat loop nearest-rank p95 is90.222/88.751ms. Earlier introduction
seconds0.7-1.7 have loop medians88.286/83.194ms, versus23.488ms with the effect
omitted. At seconds8-13, after the cinematic, all three return to about7ms.
The inspected full screenshot displays13FPS /76.91ms; the control displays39FPS
/25.37ms. Those overlays summarize their own earlier half-second windows,
rather than the table's later window.

Interpretation: the effect adds repeatable transparent preparation work and a
large GPU-duration increase in this close-up. Rendering/presentation waits are
also substantial, including in the control. CPU frame, main/render thread,
GPU and wait counters overlap and describe delayed frames; do not add them to
reconstruct a frame or attribute the residual interval to one function.
These measurements do not isolate shader overdraw, driver overhead or the
specific cause of the remaining presentation waits. The control GPU counter
has intermittent26-29ms samples (p95 28.235ms); its low median does not imply
uniformly low GPU time. Only70 of72 control-window rows have valid GPU timing.
Draw-call and batch
recorders returned zero despite nonzero triangles and rendered images; those
counters are unavailable here, not evidence of zero draws.

## Limits

The player uses a prepared initial Level2 state with9 creatures after creation,
not the owner's tick998 state with5. It uses Direct3D11 rather than the earlier
Editor's Direct3D12. This is a controlled reproduction of the introduction,
not an exact replay of the owner's frame. Effect omission removes both its
geometry and lighting, so the GPU difference cannot be assigned exclusively
to either. The separate geometry/light exclusions above narrow the CPU path.
These baseline captures predate the renderer optimization below.

FrameTimingManager returns delayed completed frames; timestamps are deduplicated,
and boundary/screenshot windows are excluded. Recorder timing values convert
nanoseconds to milliseconds; FrameTiming values already use milliseconds.
The baseline probe formats CSV during capture, so allocation counters include probe overhead.
The inspected screenshots and rendering callbacks validate actual screen work.

The earlier Editor probe's edit-mode effect teardown logs Destroy errors;
all420 measured close-up samples recorded zero warnings/errors during timed work.
Cold and teardown frames are excluded from its aggregates. No exact allocation
claim is made; Mono's allocation-counter trap still applies.

This investigation does not recommend reducing authored particles, colour or
brightness to hide the performance cost.

## Renderer optimization and Release acceptance

CONFIRMED in the remake, 2026-10-25: the transparent renderer now uploads one
interleaved vertex stream shared by index-only material-run meshes. Run bounds,
indices and renderer state are retained where possible. Conservative triangle
frustum rejection precedes the existing stable depth ordering. The effect shader
uses a single material constant buffer, and the custom sprite path no longer
creates unused paused particle systems. Authored effect content is unchanged.
The retained MeshRenderers preserve per-object lighting; unsupported dynamic
batching or material instancing configurations fail explicitly.

The GPU probes passed 17 pixel cases, 96 reference-order cases, wide global
indices, inactive-run bounds refresh, frustum crossings/camera changes, and
portal animation/expiry checks. Vulkan also passed the no-unused-particle-system
check. Primary run meshes report zero vertex-buffer bytes; attributes come from
the shared additional vertex stream. These are bounded checks, not exhaustive
visual acceptance for every creature effect.

The final all-API Release player uses the same executable and managed DLLs for
the serial Vulkan and Direct3D12 captures below. VSync is off. FPS qualification
disables Unity Frame Timing Stats and profiler counters and uses a lightweight
PresentMon trace without GPU tracking. Raw sampling is buffered; screenshot
encoding and CSV formatting occur after the measured introduction. Native
QueryPerformanceCounter timestamps align the presentation window. GPU duration
is intentionally absent from these captures, not zero.

Artifacts: `build/portal-fps-final/20261025-154243/` (Vulkan, includes player)
and `20261025-154610/` (Direct3D12). Each retains CSV, logs, rendering proof and
source/built-assembly fingerprints. Rechecking with the owner's revised 60 FPS
policy gives valid evidence and a **failed** budget for both:

| API | CPU samples | CPU median / maximum ms | CPU frames above budget | Present median / maximum ms | Presents above budget |
|---|---:|---:|---:|---:|---:|
| Vulkan | 1169 | 7.790 / 52.400 | 233 | 8.118 / 68.719 | 241 / 1168 |
| Direct3D12 | 1221 | 8.735 / 60.437 | 210 | 8.630 / 89.498 | 219 / 1220 |

Typical intervals improved relative to the earlier baseline, but instrumentation,
API and build mode differ; these tables do not establish a controlled speedup
ratio. Serial API runs on a shared laptop do not establish a universal backend
ranking. Earlier full GPU traces were substantially slower; tracing overhead,
machine load and scheduling have not been separated by a matched repeat series.
The remaining hitches are unresolved. Resource preloading is not implemented.
Production graphics API defaults remain unchanged; Vulkan-first with Direct3D12
fallback is conditional on acceptance. See [performance policy](../PERFORMANCE.md)
for the target, scenario coverage and acceptance protocol.
