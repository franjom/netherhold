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
Native preparation allocates zero managed bytes across all120 warmed samples.
The preparation interval includes visibility-cache reuse, source gathering,
table staging and GPU-buffer submission. The render interval measures CPU URP
request submission, not GPU execution, displayed frame time or simulation cost.
No tests/builds run alongside these samples; machine load and thermal state
are uncontrolled, and a preceding capture measured lower absolute times.
Do not interpret the ordering as a proven speedup or report a game frame rate.

The probe has no GPU timing result and does not advance a full player loop.
Windowed CPU/GPU frame distributions, frame allocations, particles and sustained
busy simulation still need the existing Unity Profiler protocol. These measurements
support keeping persistent buffers and avoiding unchanged-frame rebuilds; they
do not justify changing DK2's animation or reducing original visual detail.

## Review and completion limits

The final Release solution run with the installed game data passes5482 tests,
with25 reported skips and no failures. This includes471 presentation and3534
simulation tests. The Unity adapter builds with zero warnings/errors; its eight
Unity-loadable DLLs are synchronized after the build.

Independent reviews cover camera startup/projection, visibility geometry, source
selection/cache, material and shader paths, discovery/temporary Sight, hidden
torches and ordered effect admission across worker tick batches. Review found
and corrected the animated flag4 interpretation, terrain selector precedence,
torch visibility and stale Sight-event ordering. Regression tests retain those seams.

The milestone checklist and final acceptance gate are in
[the Step5 plan](../plans/2026-10-14-classic-look-step5.md).
