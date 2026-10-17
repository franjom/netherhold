# Classic Step 5 verification

This records checks of our implementation. Binary behavior evidence lives in
[classic-lighting.md](classic-lighting.md), [visibility](classic-light-visibility.md)
and [Sight of Evil](keeper-spell-sight.md). A passing fixture is not a matched
original-game frame.

## Controlled GPU checks — 2026-10-15

Unity6000.6.0f1, URP, Direct3D12, Gamma colour space, SRP Batcher enabled.
All runs use the isolated `build/step5-lighting-project`; no owner scene or
project setting is saved. Original art stays in the installed game.

| Probe entry point in DK2.Unity.Editor | Result | Coverage |
|---|---|---|
| ClassicTileLightProbe.Run | 52 pixel cases and7 lifecycle checks pass | Source flags, all24 slots, emitting-tile anchors, rotated/scaled animated instances, authored bounds, strict sphere tangency, removal/Clear/disposal |
| ClassicTagMaterialProbe.Run | 7 cases pass | Tag replacement, material brightness, boosted-byte tint, additive/unlit handling |
| ClassicSightFogProbe.Run | 7 cases pass | Original Fog terrain27 side material/texture, packed opacity63/254, fragment alpha, blending, shared-material preservation |

Maximum measured channel error for tile lighting and Sight fog is
`2.9802322387695313E-08`. The singular zero-incidence bin remains outside the
ordinary arithmetic contract; non-finite results are not counted as a pass.
The tile fixture's uniform/nonuniform transforms test the documented arithmetic;
the native draw-radius scale is a separate uniform scalar, not a guessed maximum axis.

Local evidence directories under the main clone's ignored `build/`:
`step5-instance-classified/result-final.json`,
`step5-tag-final-classified/result2.json` and `step5-sight-fog-final/result2.json`.
The probes neither save the owner project nor bundle original assets.

## Campaign scenes

`ClassicWorldLightingProbe.Run` passes on Level1, Level7 and Level8 with
seed1, normal lighting, Native Initial Ambient off, automatic source publication
and the authored camera. Each tests startup, a fixed-world camera shift,
directional/sky isolation and turn70. Torch on/off comparisons now remove the
actual cache sources as well as disabling Unity lights; they do not mistake a
leftover native buffer for an unlit control.

All three runs confirm a nonzero torch contribution in all four comparisons.
Images were inspected for rendered geometry, camera framing and visible actors;
they have not been accepted against matched original-game screenshots.
Level8 verifies emitting-tile identity on127793 opaque,5688 transparent and4518
water/lava vertices, including a consistent anchor across each triangle.
Evidence: `build/step5-campaign-final-Level1`, `...-Level7`, `...-Level8`.

Reproduce using the protocol in [the probe README](../../tools/classic-unity-probe/README.md)
and environment variables `DK2_LIGHTING_PROBE_LEVEL=Level1|Level7|Level8`,
`DK2_LIGHTING_VERIFY_ANCHORS=1`, `DK2_LIGHTING_PROFILE=1` for profiling,
and `DK2_LIGHTING_PROBE_OUTPUT=<directory>`. Do not add `-quit` to the batch
entry point: its deferred editor callback exits when the check finishes.

## Level8 CPU comparison

These October15 timings predate the October16 camera-distance and static vertex
input corrections. They are historical measurements, not current-path timings.

AMD Ryzen7 7735HS, integrated AMD Radeon Graphics, Direct3D12,1024×576,
Unity6000.6.0f1. Fixed world at turn70; the second view is selected by maximum
authored torch density in a13×13 window. Each mode warms20 render requests and
records120. The legacy preview and native tables use the same world, camera
and assets within a view; they intentionally use different lighting algorithms.

| View/mode | Preparation median/p95 ms | URP submission median/p95 ms |
|---|---:|---:|
| Quiet / legacy preview | .0025 / .0036 | 1.9183 / 3.1541 |
| Quiet / native tables | .3205 / .3658 | 1.5351 / 2.1886 |
| Torch-heavy / legacy preview | .0025 / .0033 | 1.9374 / 4.0393 |
| Torch-heavy / native tables | .3276 / .3896 | 1.8469 / 4.1568 |

Both views prepare76 tile tables; selected-source totals are912 and892 respectively.
Scoped managed-allocation measurements are unavailable in this Unity runtime;
the earlier zero readings were invalid (see calibration below).
The preparation interval includes visibility-cache reuse, source gathering,
table staging and GPU-buffer submission. The render interval measures CPU URP
request submission, not GPU execution, displayed frame time or simulation cost.
No tests/builds run alongside these samples; machine load and thermal state
are uncontrolled, and a preceding capture measured lower absolute times.
Do not interpret the ordering as a proven speedup or report a game frame rate.

This editor probe has no GPU timing result and does not advance a full player loop.
These measurements
support keeping persistent buffers and avoiding unchanged-frame rebuilds; they
do not justify changing DK2's animation or reducing original visual detail.

## Windowed player and counter calibration

The2026-10-17 visible Development player capture uses master d220c077 runtime
sources, the fixed-camera fixture and the shader correction below at1280×720. It
uses Direct3D11, Gamma, Ultra quality and VSync off on the Ryzen7 7735HS/Radeon
Graphics machine. Level8 is paused at turn1, with normal native lighting,
Native Initial Ambient off and fog off. The isolated fixture suppresses scripted
camera paths/glides/letterboxing and verifies fixed gameplay cameras. Each view
has120 samples with120 matching main-camera render callbacks; both screenshots
were inspected. This is a player-loop capture, not a busy-gameplay benchmark.

| View | CPU frame median / p95 ms | GPU median / p95 ms | Fresh GPU samples |
|---|---:|---:|---:|
| Starting focus | 6.1621 / 7.2953 | 2.14180 / 5.55085 | 96 /120 |
| Torch-heavy | 7.1622 / 9.5300 | 3.39462 / 8.59824 | 92 /120 |

Evidence: `build/step5-rack-transparency-20261017/20261017-124813/summary.json`,
metadata, CSV, PNGs and `source-revision.txt`; all eight player assemblies match
the preceding master capture. Starting focus is(75.5,1,-9.5), torch-heavy is(42.5,1,-18.5).
The global GC counter reports median144706/144723 bytes/frame respectively; its
origin is unassigned. Unity used memory medians are316.3/320.8MiB respectively.
The draw-call counter returns zero despite rendered
geometry and is unusable. Hidden-player attempts produced no render callbacks
and fail verification; unavailable GPU timings never become zero-cost claims.
Reproduce with the README's windowed capture protocol and explicit `-VisiblePlayer`.
The earlier122831 run captured the new scripted fly-over and is excluded from
this gameplay-camera baseline. Source/material/camera changes prevent treating
the older `step5-visible-player-final` timings as a controlled before/after test.

Standalone runtime materials: the preceding123325 capture rendered black weapon
cards despite correct decoded textures and alpha blend states. Unity stripped
the classic shader's runtime-selected transparency/emission `shader_feature`
variants. Retaining both axes with `multi_compile_local` restores the rack in
the matched124813 image; near-black pixel counts in the two rack regions drop
from1209/1467 to0/1. This fixes runtime material variants generally, without
changing authored blends. The follow-up capture passes all rendering/camera
checks and was independently reviewed; it does not prove a performance gain.

`build/step5-allocation-calibration/result.json` demonstrates that Unity6000.6.0f1
returns zero from the per-thread allocation API around a live65536-byte array.
The bounded `GC.Alloc` recorder reports nanoseconds, not bytes. Neither provides
validated scoped byte counts here; preparation, shadow and phase outputs now
carry availability flags and use−1 for unavailable bytes. Earlier Unity zero
readings do not establish allocation-free code. CoreCLR cache allocation tests
remain separate valid evidence. Sustained simulation, particles and allocation
call stacks still need the existing Unity Profiler protocol.

## Review and completion limits

The October16 corrections and their remaining visual limits are recorded below.

The initial October16 Release solution check with installed game data passed5503 tests,
with22 reported skips and no failures. This includes471 presentation and3542
simulation tests. The Unity adapter builds with zero warnings/errors; its eight
Unity-loadable DLLs are synchronized after the build.

The current hash/snapshot schema is tracked in [hash-versions.md](hash-versions.md).
The integration solution run
exposed a stale navigation reference on master; its missing stamp stage was
restored and the entire384-test behavior assembly then passed. Other assemblies
passed in the full run. Merge review confirmed both parents' saved fields survive.

Independent reviews cover camera startup/projection, visibility geometry, source
selection/cache, material and shader paths, discovery/temporary Sight, hidden
torches and ordered effect admission across worker tick batches. Review found
and corrected the animated flag4 interpretation, terrain selector precedence,
torch visibility and stale Sight-event ordering. Regression tests retain those seams.

The milestone checklist and final acceptance gate are in
[the Step5 plan](../plans/2026-10-14-classic-look-step5.md).

## Level1 reference corrections — 2026-10-16

The owner reports untouched original DK2 defaults. This is an observational
reference, without a recorded original tick or live renderer state.
Fresh native-only captures exposed two integration defects: the camera's native
twice-zoom visibility minimum was omitted, and missing static vertex inputs
selected animated lighting on D3D12. Both are corrected; see camera research
and TRAPS. The original `KTorch` companion is now rendered separately from its mount.

The campaign probe now verifies explicit static input streams, GPU count/source
readback against prepared tables, and a substantial Level1 image contribution
from torches, rather than accepting a few changed bulb pixels. The static-stream
regression failed before the fix and passes afterward. Level1's mean torch image
difference rose from about0.00062 to0.10017; this measures our on/off control,
not error against original DK2. Level8 also passes with opaque, transparent and
liquid stream checks and zero count/source mismatches.

Evidence: ignored `build/step5-static-inputs-green`, `step5-parity-final-Level8`
and `step5-startup-parity-{fog,no-fog}` in the main clone. Startup captures use
1024×540, fixed simulation ticks and an injected50ms effect clock at seven
recorded times through9seconds. They include the Heart opening and original flames.
481 presentation tests and138 conversion tests pass; the adapter builds cleanly.
Independent reviews cover placement, visibility, resource clearing, capture clocks,
camera setup and the read-only native-input probe; its local-memory self-test passes.

The black rock-cap/arch inputs and wrong dust-colour mask from these older
captures are corrected below. Level1 camera/arches/walls and corrected torch
appearance/playback have owner acceptance; matched startup-smoke and Levels7/8
acceptance remain open. Q604 retains timing/phase-stream fidelity limits. The native-input capture requires a
running, hash-verified original game; it changes no memory or settings.

An explicit Windows UAC launch succeeded after ordinary and sandbox-escalated
reads failed with error5. Live Level1 inputs are now captured: ambient/settings
in classic-lighting.md section8, palette/getter in room-construction-effects.md
section6. These are later live samples, not a recorded startup sequence.
The running`DKII-DX.exe` has SHA256
`a5957e8c52b6dee80ddf6cc1d3ea5079aaaad4d11b40e0f067d34ff4157ce9bb`;
offline comparison found identical PE section layouts and executable code
sections to the researched`DKII.EXE`. No game settings or memory were changed.

Further binary tracing found ordinary-top normal inversion and the separate
instance-lighting path for ordinary static objects. Both are built; evidence lives
in static-mesh-lighting.md. The neutral-player cycling-colour selector remains
research in room-construction-effects.md section6, without a colour change.

The Rock-only production-geometry regression fails with top inversion disabled;
the object-role regression fails on the old static marker. Both pass after the
corrections. Final native-only captures in ignored`build/step5-draw-modes-Level1`
and`step5-draw-modes-Level8` pass: Level1 checks1484 Rock-top normal vertices,
5370 static-object lighting markers and648 torch markers; Level8 checks22810
static-object and6723 torch markers. Level8 has no top-only Rock fixture, so it
does not provide that particular normal assertion. GPU source/count readbacks
still match. The adapter builds with zero warnings/errors; independent review
checks source preservation, draw-role scope and lighting-mode cache isolation.

`build/step5-draw-modes-startup-fog` records the seven pinned startup images with
fog enabled. The corrected scanned rock tops now receive their authored warm
light. These captures predate authored object-light publication below.

Authored object-light publication passes the Level1/Level8 probes in ignored
`build/step5-object-lights-Level1` and`step5-object-lights-Level8`:2 and3 source
objects respectively. Level1 asserts dynamic sources reach the camera's tables;
Level8's initial camera does not include its source-bearing objects, so it checks
publication availability and the existing GPU table/count regressions. Headless
checks cover source removal, tile changes, fog-independent prepared border cells,
Heart physical height and normal/cheap classifications. Warm publication/gather
allocates0 bytes in .NET; this is not a Unity allocation claim.

All489 Presentation and396 Data tests pass; the adapter builds with0 warnings
and0 errors. An existing light-window allocation test failed once during a full
run, then passed alone and in the complete rerun. Independent review found no
blocking conversion/cache defects. Held/carried and general furniture-position
limits remain in static-mesh-lighting.md section4.

The object-light-only Level1 image restored orange contribution but still left
several arch faces black; the added-colour correction below supersedes that state. The seven pinned FOW-on frames in
`build/step5-object-lights-startup-fog` reproduce these remaining dark faces and
unmatched smoke colour. Authored inputs
are now published; these captures do not establish full visual parity (Q603/Q605).

Reinforced-wall cap correction: the production regression in ignored
`build/step5-wall-caps-red` fails on the missing normal inversion. The fixture
checks decoded cap assets have upward plane normals before requiring the
produced terrain normals to point downward. This covers cap-only tiles without
mixing wall-face geometry into the assertion. The corrected Level1 capture
restores wall-top textures and red markers; it does not fix the Heart arches.
Final captures in`build/step5-wall-caps-Level1` and`step5-wall-caps-Level8`
pass, checking320 and120 produced cap vertices respectively. Existing normal,
source-role and GPU table/count assertions also pass. Independent review
confirms the top-quarter scope; the adapter builds with0 warnings/errors.

Room-furniture added colour: the new install-backed regression failed before
ObjectView populated the native room colour and passes after the correction.
All490 Presentation tests pass; adapter compilation has0 warnings/errors.
Production Unity captures in`build/step5-room-object-colour-Level1` and
`step5-room-object-colour-Level8` pass, each verifying all five Arch materials
against captured RGB70/30/10 and preserving existing cap/source/GPU assertions.
Seven pinned FOW-on startup captures in`build/step5-room-object-colour-startup-fog`
also pass; the9000ms image visibly restores brown arch/step detail. Review found
no blocking code/material defect. Association/animation limits and original live
capture evidence are in static-mesh-lighting.md section5. These checks do not
close the original-view acceptance or smoke-colour gates.

Raw art-flag review (2026-10-17): master ec92cfae already corrects keeper-colour
selection to mask2. The five-case regression separates it from play-once mask128;
all503 Presentation tests pass and the adapter builds without warnings/errors.
The seven pinned captures in`build/step5-raw-colour-flag-startup-fog` complete
with exit0; the9000ms image shows grey plug dust instead of red. Binary/data
provenance and the narrowed palette boundary are in room-construction-effects.md
section6. This is not a matched original-game timing/colour acceptance.

Torch playback correction (2026-10-17): all509 Presentation tests pass, including
30/60/144/240FPS schedules. The adapter builds without warnings/errors.
The production regression in`build/step5-torch-speed-red` reproduces early
advancement with the per-Unity-update code. The corrected Level1 regression in
`build/step5-torch-speed-Level1` passes: actual paused Runner updates at49ms do
not advance,50ms advances once, and ten more same-time updates do not accelerate.
An allocation test failed in the first full run, then passed alone and in the
complete rerun; its existing measurement caveat above still applies.

Independent review cleared the playback code and the explicit ADR-0494 policy.
The lighting diagnostic's shared effect clock is frozen at0ms during its images,
so tick70 there is not a17.5-second effect reference. Dedicated startup captures
supply their own sequence times: all seven frames in
`build/step5-torch-speed-startup-fog` complete with exit0, and the9000ms image
was inspected. torch-animation.md retains native cadence facts
and outstanding timed-original/phase-stream comparison boundaries.

## Final comparison preparation

The complete shipped startup closure bypasses keeper/cycling palettes (Q605;
heart-intro.md). Its install-backed regression passes with the complete510-test
Presentation suite; the adapter builds without warnings/errors. The startup
capture now accepts Levels1/7/8 and an optional15000ms later view. These
captures advance simulation and effects together; they are distinct from the
fixed-effect-clock lighting diagnostic. No additional palette adjustment is
needed to prepare the original-game comparison. Heart-focused captures are
explicit manual comparison positions, preserving the production startup camera.
Level8's authored start points into fogged hero territory; the earlier authored
capture was black and is not an accepted lighting reference. Its construction
also starts16seconds later (heart-intro.md section1), so the comparison timeline
uses times relative to construction and records the absolute map clock.

All eight Heart-focused images per level pass, including the nonempty-image
guard. Levels1/7 use`build/step5-final-heart-comparison-Level{1,7}`;
Level8 uses`build/step5-final-aligned-heart-Level8` at16..31seconds. The later
views and Level1 smoke frame were inspected. All24 images and metadata links
are validated in the local interactive gallery`build/step5-comparison/index.html`.
Independent review covers the closure and capture boundary; these artifacts are
ready for the owner's comparison, not a substitute for it.

## Owner acceptance

| View or behaviour | Status |
|---|---|
| Ordinary camera orientation/framing | Accepted |
| Corrected Level1 Heart arches and wall-top lighting | Accepted2026-10-16 |
| Corrected torch appearance and frame-independent playback | Accepted2026-10-17 |
| Current overall appearance, including standalone rack transparency/emission | Accepted2026-10-17 |
| Corrected grey Heart startup smoke, at matching sequence times | Pending |
| Matched Level7/8 lighting and fog views | Pending |

The owner's current-appearance verdict was "all looks excellent" after the
standalone material correction. It does not establish matched original-game
colour/timing parity. Q605 is closed by the shipped-data closure in heart-intro.md;
the remaining visual comparisons above still apply.

## Camera-rotation stutter follow-up

The owner reports stutter while rotating the camera (2026-10-17). The fixed-view,
paused-world player samples above do not exercise that case. The debug FPS/frame-time
overlay is built and the adapter compiles without warnings/errors. It supplies a
visible frame-time indicator, not a measured cause or fix. Use the repeatable
rotation/Profiler protocol in [Unity performance guidance](../../unity/README.md#measuring-performance).
