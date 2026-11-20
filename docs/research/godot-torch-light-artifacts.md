---
systems: [presentation-godot]
---
# Godot torch-light and ambient-occlusion artifacts

## Scope and current acceptance

This is an adapter-rendering correction under ADR-0813, not evidence of DKII.EXE behavior.
The unchanged classic per-vertex path and shared `ClassicVertexLight` remain the reference.
The reviewer approved the implementation and final receipts with no remaining findings. Focused GPU proof and the final scene comparison validate the torch-rim/AO correction;
measured costs and the isolated/live simulation distinction are below.
Items 16 and 17 of the modern-rendering audit are separate work.

## Where the artifacts came from

Archived builds were captured in four flag states: classic, `--light-perpixel`, per-pixel with
`--light-shadows`, and both with `--light-post`. The torch disc already appears with per-pixel lighting alone
in `ba6bee684`; it also appears in every requested historical revision. It did not first appear with
vertex animation, instancing or shadows. The per-pixel point kernel's first distance bin returned full
colour, producing a plateau and abrupt boundary; subsequent bins produced visible bands.

| Archived revision | Disc with per-pixel alone | Pixels changed by post flag | Shadow changes in wall ROI |
|---|---|---|---|
| `c40507ad` | yes | 0 (post flag absent) | 0 |
| `ca84200c` | yes | 0 (post flag absent) | 0 |
| `a26a5db64` | yes | 14,635 | 0 |
| `6301c6ea1` | yes | 14,635 | 0 |

The wall ROI is `(150, 235)-(430, 405)`. These captures isolate the disc from shadows in that ROI;
they do not establish that shadows have no effect elsewhere. The post-effect source first landed in
`05694afe8`. Its unfiltered ten-tap estimator added spatial noise independently of the point-light rim.

## Production rendering

Under `DK2_PER_PIXEL`, `WorldLight.gdshaderinc` evaluates a continuous point-light kernel:
`(0.25 + 1 / distanceSquared) * incidence * (radiusSquared - distanceSquared) / radiusSquared`.
The shader's endpoint guards handle the centre and radius. The existing light sources, tint, incidence,
clamp and packing remain; the original binned per-vertex branch is unchanged. This representation
change smooths the fragment result without changing game rules or the classic reference.

`--light-post` attaches a C# `WorldOcclusionHost` to the main and full-map-preview cameras.
Each host creates a fresh `WorldOcclusionEffect` on tree entry and releases its compositor and GPU
resources on exit, including reparenting. Shadow cameras do not attach the effect.
Two compute passes run after opaque drawing and before transparent effects: `WorldOcclusionRaw.glsl`
keeps the ten-tap estimator with texel-centre, border and degenerate-normal guards;
`WorldOcclusionFilter.glsl` applies a 5x5 depth/normal-aware filter. The former transparent-pass quad is gone.
This is modern AO, not a claim about native DK2 lighting.

The owner chose C# for the compositor. Its accepted callback-wrapper allocation is recorded once in
[TRAPS](../TRAPS.md); post-enabled rendering has no zero-allocation claim.

## Focused GPU proof

The production point-light probe runs the actual shader kernel, checking centre/radius endpoints,
RGB tint, backfaces, radius and adjacent-sample continuity. Both Vulkan and D3D12 measured the old
point-light jump at `0.8392157`; the corrected per-pixel jump is `0.003921576`, while the classic
reference remains `0.8392157`.

The AO probe's raw residual RMS is `0.001962974`; filtered RMS is `0.000418500`, a reduction of about
79%, with 95.6% of mean AO retained. Green probes exit 0; bypassing the filter exits 1. All five probe
images match byte-for-byte between Vulkan and D3D12: raw, filtered, resized 256, restored 512 and
reparented. Resize checks include dimensions, finite output and nonzero AO. Restore and reparent
reproduce the filtered image SHA-256:
`6530BD153957341169BAC873D40D4B2F6AF24F3B3643A4BBB491D4D4E03C9621`.

Shadow controls in the corrected renderer remain active: per-pixel versus per-pixel plus shadows changes
11,916 pixels on Level1 and 34,417 on Level7 (maximum channel differences 140 and 203), while the wall ROI
still has zero shadow changes. Those controls separate the rim correction from shadow visibility.

## Scene acceptance and performance

Performance and scene comparisons use an isolated `6301c6ea1` archive plus the graphics overlay in `fixed/`,
not the live checkout's concurrent simulation edits. `fixed-overlay-sha256.json` verifies the 491 Godot-project
files in that test basis, with the prior minimap geometry/HUD/test overlay supplied separately. Isolated Godot
build/import pass with zero warnings/errors.

Live-checkout Release solution and Godot builds exit 0; its full suite passes 11,532 tests with 30 skipped.
The isolated solution build and pinned import/build-solutions also exit 0. Isolated turn 400 retains
`B5C0CB842094AB59`; the live checkout reaches `E8B2BCEDE05E75D3`, both with exit 0 and no runtime errors.
The live hash includes concurrent simulation changes excluded from the graphics comparison; this pass makes
no claim that the live hash is unchanged.

Performance uses two runs of 1,800 measured frames per case. Each timing/draw/median-allocation value below
is the mean of the two run medians (archive -> corrected renderer); allocation means are per frame.

| Case | Frame ms | Partial GPU ms | Render CPU ms | Draws | Allocation median B | Allocation mean B |
|---|---|---|---|---|---|---|
| Flags off Level1 | 6.185 -> 5.224 | 2.941 -> 2.251 | 0.604 -> 0.472 | 122 -> 120.5 | 7,296 -> 7,296 | 7,566.8 -> 7,285.6 |
| Flags off Level7 | 7.554 -> 6.330 | 4.215 -> 2.815 | 0.866 -> 0.793 | 307.5 -> 305.5 | 10,312 -> 10,300 | 16,053.4 -> 13,994.4 |
| All flags Level1 | 11.702 -> 13.167 | 8.850 -> 10.493 | 0.471 -> 0.825 | 193 -> 192 | 7,296 -> 8,096 | 9,143.5 -> 10,221.7 |
| All flags Level7 | 13.902 -> 16.180 | 10.487 -> 11.559 | 0.745 -> 1.038 | 470.5 -> 468 | 10,344 -> 11,152 | 23,190.9 -> 24,686.3 |

The harness itself allocates zero in these runs. All-flags median allocation rises by 800 B/frame on Level1
and 808 B/frame on Level7. This includes more than the inside-callback component measured in [TRAPS](../TRAPS.md):
about half the delta is outside the instrumented callback body; an entry-wrapper attribution is a hypothesis,
not an independently isolated measurement. C# remains the owner's selected implementation; post-on has no
zero-allocation claim.

Main/preview GPU and render-CPU timings include the new main-camera AO passes but exclude unchanged torch-shadow
viewports and the minimap viewport. The measured partial GPU cost is about 1.1-1.6 ms with all flags, a quality
tradeoff rather than a gain. Other CPU load was 50-94% for the archive and 35-38% for the corrected runs;
flags-off variation establishes no speedup.

Final capture comparison covers 1,056 frame pairs in 20 groups: 960 pairs across six standard scenes in
classic/all-flags modes, plus 96 pairs across eight Level1/Level7 torch flag sets. Of these, 534 pairs are
byte-identical and 522 differ. Every one of the 480 standard flags-off pairs is byte-identical.
The extra loaded-Level7 flags-off set has an inherited one-pixel difference at `(1232, 549)` in 11 frames,
maximum channel difference 9, outside the torch crop. A real pre-fix minimap-only build with the old point
kernel matches the corrected renderer exactly and reproduces the same difference against the archive.
This control attributes that residual to prior minimap work, not this torch correction.

The torch triptychs and possession/lightning/full-map review sheets show the hard disc and distance-bin rings
removed, AO speckle filtered, and shadows retained. Geometry and HUD placement are unchanged; changed world
pixels remain visible beneath translucent UI. This is the scoped rendering validation, not the owner's final
acceptance of every modern-rendering item. Items 16 and 17 remain open.

## Reproduce and inspect

Commands and isolated user-folder arguments are in the [Godot README](../../godot/README.md#torch-light-and-ao-probes).
The reproducible capture and probe artifacts are outside the checkout at
`C:/Users/franj/AppData/Local/Temp/dk2-torch-bivo0s2l/`: `history-matrix.json`,
`history-pixel.png`, `head-flag-matrix.png`, `point-red/`, and
`ssao/probe-{ao,aoneg,point}-{d3d12,vulkan}/results.json`, `fixed-overlay-sha256.json`, `performance-summary.json`, `shadows-retained.json`, `gates.json`,
`gates-live-native.json`, `gates/live-hash/receipt.json`, `comparison-summary.json`, `comparison.html`
and `pre-fix-control.json`. `comparison.html` links every differing frame's crop; torch crops are
`crops/torch-Level{1,7}-{off,pixel,shadow,post}/torch.png`.
