# Unity crowd performance probe

## Pinned campaign startup comparison

In the isolated `build/step5-lighting-project` only, batch entry point
`DK2.Unity.Editor.ClassicStartupCaptureProbe.Run` records1024×540 gameplay views
at fixed effect times0/500/1000/2000/4000/7000/9000ms and corresponding simulation
ticks. `DK2_STARTUP_CAPTURE_OUTPUT` selects the output directory;
`DK2_STARTUP_CAPTURE_FOG=1` enables fog. `DK2_STARTUP_CAPTURE_LEVEL` selects
Level1 (default), Level7 or Level8. `DK2_STARTUP_CAPTURE_STEADY=1` adds a15000ms
later image; metadata records the level and duration.
`DK2_STARTUP_CAPTURE_FOCUS=heart` explicitly centres the keeper's Heart instead
of the authored starting camera; this fixture rejects empty captures. Level8's
authored camera starts over hero territory, so Heart lighting comparisons use
this explicit focus. Production camera behaviour is unchanged.
`DK2_STARTUP_CAPTURE_ALIGN_HEART=1` shifts capture points by the level's authored
Heart-construction delay; Level8 overrides variable218 to16seconds. Metadata
records both the delay and capture origin, so comparisons use matching stages. It uses native lighting inputs, normal
torch lighting and High Walls. It does not save an owner scene or settings.
Original references need matching viewport and sequence phase; a screenshot
reported as untouched defaults does not establish its exact tick or ambient RGB.

For the unresolved original-input gap, leave retail DK2 running on Level1 and use
`capture-native-render-inputs.py --pid <pid> --exe <path> --expected-sha256 <digest> --output <ignored-json>`.
Use the digest of the executable imported into Ghidra, not an arbitrary live
executable. The script validates that digest and process path before fixed-address
reads. It requests read/query access only and changes no game memory or settings.
`--self-test` verifies process-memory reading against a known local buffer.
`--heart-inputs` also reads Heart furniture transforms/colours and engine lights.
Runtime captures belong under ignored `reverse-engineering/`.

The world-lighting diagnostic freezes its injected effect clock at0ms while
advancing simulation ticks; its final torch regression then supplies49/50ms.
Its tick70 lighting image is not a17.5-second animation reference. Use the
separate startup-sequence probe above for timed effect comparisons.

## Windowed Level8 frame capture

`run-window-frames.ps1` uses the already prepared, isolated
`D:/DungeonKeeperRemake/build/step5-lighting-project`. It builds a Development
player from a scratch copy of the production Dungeon scene, then runs that player
without `-batchmode` or explicit render requests. By default the launcher starts
it hidden and applies `SW_HIDE` only to windows belonging to that process. This
default mode never shows, focuses or activates a window. The owner project and
its open editor are untouched.
No other intensive job should run during the capture.

The production DungeonRunner loads Level8, seed1, fog off, normal torch lighting,
Classic Vertex Lighting on and Native Initial Ambient off. Speed0 preserves the
startup tick while the normal Update/presentation and screen-render loops run.
VSync and the frame cap are disabled, and background execution is enabled.
The isolated fixture suppresses scripted camera paths, glides and letterboxing;
sampled frames must retain their fixed position, rotation, field of view and full viewport.
After ten seconds of warmup per view it records 120 frames at the authored starting
focus, then at the centre of the densest 13x13 authored-torch region. This profiles
a fixed world rather than active gameplay or a population/combat stress scenario.

```powershell
& tools/classic-unity-probe/run-window-frames.ps1
python tools/classic-unity-probe/summarize-window-frames.py <run-directory>
```

For interactive profiling, a user at the computer can explicitly run
`run-window-frames.ps1 -VisiblePlayer`. Only this opt-in launches a normal visible
player window and skips window hiding. The default remains hidden; no visible
run was used for the automated hidden-window diagnostic.

Each output contains CSV, metadata, source/DLL fingerprints, logs, the launcher's
own-window hide count, and two screen PNGs. Exactly 120 sampled-frame main-camera
render callbacks per view are required for verified rendering; warmup callbacks do
not count. Player exit2 retains both view diagnostics with `RenderingVerified=False`
and makes the launcher fail.
Those loop/counter values do not describe rendered frame performance. The summary
reports valid sample counts, median and p95. Inspect the PNGs to verify geometry.
Hidden-window occlusion can suppress presentation or GPU timing on some drivers;
render callbacks alone do not establish GPU timing availability. Unavailable or
zero GPU times are NaN in CSV and null in the summary, never a zero-cost claim.

FrameTimingManager returns recent completed frames with a four-frame delay.
Only a changed frame-start timestamp contributes a fresh timing sample; recorder
and timing rows are not synchronized traces. Frame timings and converted profiler
time counters are milliseconds; allocations/memory are bytes. No submission
stopwatch is labelled GPU time. Development instrumentation and hidden-window
scheduling limit comparisons with an ordinary visible release player.
API references: [frame timing availability](https://docs.unity3d.com/Manual/frame-timing-manager.html),
[Windows Frame Timing Stats](https://docs.unity3d.com/Manual/playersettings-windows.html),
[profiler counters](https://docs.unity3d.com/Manual/profiler-counters-reference.html).

Run from the classic worktree after syncing its simulation/art DLLs:

```powershell
& tools/classic-unity-probe/run.ps1
& tools/classic-unity-probe/run.ps1 -ReuseLastProject -Representation vat
& tools/classic-unity-probe/run.ps1 -ReuseLastProject -Representation frames -Creature Giant
python tools/classic-unity-probe/summarize.py <run>/development.csv <run>/release.csv
```

Requires an activated Unity 6000.6.0f1 editor, Windows standalone build support,
and a graphics device. The default install is `E:/GogGames/Dungeon Keeper 2`;
override `-Install`, `-Unity` or `-OutputRoot` as needed. Each invocation makes a
new scratch project under the main clone's ignored `build/classic-unity-probe`.
`-Creature Giant` (or Salamander/Mistress) changes the crowd species; default is Imp.
Use `-ReuseLastProject` to update and reuse the last marked scratch project's import cache.
`-BaselineHead` replaces copied Unity C# sources with HEAD; its DLLs still come
from the synced adapter folder. Source snapshot and assembly hashes are recorded.
It copies Assets, Packages and ProjectSettings; it neither opens nor saves the
owner's project. No original assets are copied out of the install by the probe.
Do not invoke its build method directly in the owner's project.

The runtime loads Level8 definitions through `InstallArtProvider`, resolves Imp
movement, and creates 200 independent production `ClassicCreatureAnimation` mesh
instances in a 20 by 10 grid. All are animated each frame and viewed by one
camera into a 1280 by 720 RenderTexture, with one directional light and copied render settings.
URP's documented SingleCameraRequest submits each offscreen render explicitly;
batch players otherwise skip screen rendering even with a graphics device.
Player logs record the graphics threading mode; these batch captures use
`kGfxThreadingModeNonThreaded`, so submission costs differ from a windowed game.
VSync and the frame cap are disabled. After ten seconds of warmup it records
600 rendered frames in a standalone Development player and a release player.
Each process has a twenty-minute timeout. Keep other intensive jobs idle during
captures; compare metadata and worktree snapshots before comparing timings.

CSV `draw200_ms` times production clock, sampling, conversion, `SetVertices`
and `RecalculateBounds` together. Historical `draw_gc_bytes` values used an
uncalibrated per-thread allocation meter and are unverified; see [TRAPS.md](../../docs/TRAPS.md).
Development marker counters separately
report sampling/conversion and upload/bounds; these are nested inside draw time.
The mesh upload marker measures CPU submission, not asynchronous GPU execution.
`render_submission_ms` times synchronous URP request submission; GPU work can
continue afterward. `frame_ms` is the whole offscreen loop interval including
rendering and scheduling, without a visible window or swapchain presentation.
Recorder and FrameTimingManager values describe recent completed frames; their
asynchronous latency means rows are not synchronized individual-frame traces.
NaN means a counter or GPU timing was unavailable; release strips custom markers.
`total_used_bytes` is Unity's live used memory counter, not graphics memory alone.
Metadata records CPU, GPU, API, Unity version, resolution and quality settings.
Each capture includes a PNG verifying rendered geometry; the scratch manifest
adds Unity's image conversion module and retains a Lit material for shader inclusion.
The scratch adapter asmdef references URP for its explicit render request only.
API references: [URP render requests](https://docs.unity3d.com/Manual/urp/User-Render-Requests.html)
and [player memory counters](https://docs.unity3d.com/Manual/profiler-memory-counters-players.html).

This controlled crowd measures actual Unity mesh updates and rendering. It is
not a complete Level8 simulation, terrain, lighting, particles or UI benchmark,
nor an original-game fidelity check. Use the Level8 quiet/busy camera capture
procedure in [unity/README.md](../../unity/README.md) for whole-game acceptance.
Never report this probe's frame rate as the completed game's frame rate.

## Representations and local measurements

### Classic Step 5 comparison baseline

Use the production scene, not the synthetic crowd probe, for visual acceptance.
Use Level1's starting keeper focus (14,28), Level7's (5,15), and Level8's (75,9),
from the shipped player records. Add a wall-torch corridor, Level8's water and
lava portal areas, and a discovery boundary; record their actual focus coordinates.
Pin level, modern simulation seed1, turn, camera preset/yaw/zoom, fog and HighWalls.
For DK2 identify the original save/start condition and fixed comparison moment;
do not imply its random seed is controlled. Pause world advancement for torch-loss comparisons.
Start with a 640x480 reference and repeat at 1280x720; compare the same world region.
Record both games' actual drawable viewport and control-panel visibility, not just
window resolution. Pin DK2's ambient-light, gamma and graphics options (controls.md).
Record the active pipeline/renderer, colour space, ambient mode/colours, directional
lights, postprocessing, and visible torch counts. Change only camera position/yaw
when checking torch loss. Screenshots require a matching original-game view.
Use unity/README.md's existing quiet/busy Level8 profiling protocol for CPU/GPU,
frame-time distribution, allocations and draw calls; do not call submission time GPU time.
Windowed player captures and their performance limits are recorded in
[Step5 verification](../../docs/research/classic-lighting-verification.md).
Matched original-game visual acceptance remains pending. The controlled fixtures in
[classic-lighting.md section7](../../docs/research/classic-lighting.md#7-opt-in-unity-integration)
do not replace that baseline.

`-Representation cpu` uses the production renderer. `vat` builds one shared
RGBAFloat position texture per decoded segment and interpolates two texels in
URP's native Lit forward vertex pass. The original topology, UVs, static normals,
materials and RenderAnimationClock remain the inputs. A property block supplies
only the fractional frame; union bounds cover the complete segment. `frames`
builds shared immutable meshes and switches to the nearest decoded frame. It
loses fractional interpolation, so it is a diagnostic comparison rather than a
motion-preserving replacement. Both alternatives are isolated probe code.

2026-10-11, final working-tree animation sources and synced DLLs, HEAD
`2af2488e`, AMD Ryzen 7 7735HS, integrated AMD Radeon Graphics, Direct3D12,
Ultra quality. Each row has 600 warmed frames; its scoped animation-allocation
readings lack meter calibration and do not establish zero allocation.
PNG inspection and counters confirm 200 creatures and matching
39,080 Imp / 44,480 Giant triangles across representations. Values are median /
p95 milliseconds; memory is median Unity live used memory in decimal MB.

| Creature / representation | Build | Whole offscreen loop | Animation/update | URP submission | Memory MB |
|---|---|---:|---:|---:|---:|
| Imp CPU | Development | 4.567 / 5.361 | 2.343 / 2.831 | 1.763 / 2.267 | 365.09 |
| Imp CPU | Release | 4.618 / 5.613 | 2.440 / 2.943 | 1.736 / 2.345 | 242.00 |
| Imp VAT | Development | 2.347 / 3.312 | 0.042 / 0.075 | 1.866 / 2.741 | 358.72 |
| Imp VAT | Release | 3.450 / 4.651 | 0.059 / 0.115 | 2.888 / 3.805 | 237.30 |
| Imp frames | Development | 0.899 / 1.100 | 0.015 / 0.021 | 0.733 / 0.890 | 358.83 |
| Imp frames | Release | 2.074 / 2.750 | 0.026 / 0.059 | 1.545 / 2.065 | 237.39 |
| Giant CPU | Development | 4.996 / 5.989 | 2.614 / 3.127 | 1.862 / 2.428 | 382.46 |
| Giant CPU | Release | 4.065 / 4.854 | 2.342 / 2.833 | 1.414 / 1.856 | 254.34 |
| Giant VAT | Development | 1.569 / 2.183 | 0.036 / 0.055 | 1.252 / 1.778 | 360.74 |
| Giant VAT | Release | 1.542 / 2.172 | 0.037 / 0.059 | 1.243 / 1.780 | 235.05 |
| Giant frames | Development | 2.058 / 2.456 | 0.026 / 0.067 | 1.487 / 1.753 | 360.24 |
| Giant frames | Release | 1.984 / 6.632 | 0.026 / 0.067 | 1.473 / 5.187 | 237.68 |

CPU Development sampling/conversion median/p95: Imp 1.870/2.313 ms, Giant
1.970/2.364 ms. Upload/bounds: Imp 0.409/0.588 ms, Giant 0.565/0.737 ms.
Release strips custom markers and the whole-frame GC counter. GPU timings and
batch counters are unavailable in this offscreen environment. A completed
Direct3D11 diagnostic also provided no usable GPU timings. These measurements
show a promising CPU animation alternative, not a controlled speedup. VAT
and frame-cache captures overlapped the regression suite and eight-worker
packet replays. Cross-representation timings, especially whole-loop and
submission times, are indicative and confounded by that background load.
Quiet reruns are required before a quantitative production decision.
Whole-loop/render times
vary with scheduling and graphics load; single sequential captures do not
establish a general Development-versus-release speed ratio.

Position payloads summed over all declared segments:

| Creature | Vertices | Decoded/shared frame positions | Shared VAT texture | CPU dynamic mesh positions, 200 | CPU sample/conversion arrays, 200 |
|---|---:|---:|---:|---:|---:|
| Imp | 324 | 50,544 B | 67,392 B | 777,600 B | 1,555,200 B |
| Giant | 1,014 | 248,016 B | 330,688 B | 2,433,600 B | 4,867,200 B |

These are position payloads, not total GPU memory: frame meshes additionally
repeat normals, UVs and indices; dynamic meshes have other vertex streams and
engine overhead. VAT textures discard their CPU pixel copy after upload.
The prototype supports opaque forward rendering only; shadow, depth, motion
vector and transparent passes, mixed creature/action populations, resource
replacement/hold integration and whole-Level8 GPU acceptance are still required
before a production VAT migration. The measured frame-cache alternative changes
motion and its Giant p95 is worse; it does not justify that fidelity tradeoff.
The current CPU path spends roughly 2.3-2.6 ms per 200-creature update. VAT
provides a concrete modern representation candidate with much lower CPU work,
subject to the remaining production pass and GPU validation.

Artifacts under `D:/DungeonKeeperRemake/build/classic-unity-probe/`:

| Representation | Imp run | Giant run |
|---|---|---|
| Final CPU | `20261011-192326` | `20261011-192631` |
| VAT | `20261011-193234` | `20261011-193629` |
| Shared frames | `20261011-193450` | `20261011-193817` |

Each directory contains Development/release CSV, metadata, PNG, build/player
logs, source and DLL SHA256 fingerprints, commit and working-tree snapshot.
Earlier HEAD-source CPU baselines are `20261011-184034` (Imp) and
`20261011-185214` (Giant). Their release loop median/p95 was 4.302/4.973 ms and
4.679/6.198 ms respectively; final production changes show no consistent timing
regression across these single captures. Screen-only batch attempts are excluded
because their PNGs were black and triangle counts zero.
