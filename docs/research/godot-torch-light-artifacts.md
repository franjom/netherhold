---
systems: [presentation-godot]
---
# Godot torch-light and ambient-occlusion artifacts

## Scope and current acceptance

This is an adapter-rendering correction under ADR-0813, not evidence of DKII.EXE behavior.
The unchanged classic per-vertex path and shared `ClassicVertexLight` remain the reference.
The reviewer found no source issues; final performance, six-scene acceptance and build receipts are pending.
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

## Scene acceptance and performance

Final before/after scene comparisons and measured performance are pending. No speedup, allocation
improvement or full-scene acceptance is established by the focused probes.

## Reproduce and inspect

Commands and isolated user-folder arguments are in the [Godot README](../../godot/README.md#torch-light-and-ao-probes).
The reproducible capture and probe artifacts are outside the checkout at
`C:/Users/franj/AppData/Local/Temp/dk2-torch-bivo0s2l/`: `history-matrix.json`,
`history-pixel.png`, `head-flag-matrix.png`, `point-red/`, and
`ssao/probe-{ao,aoneg,point}-{d3d12,vulkan}/results.json`.
