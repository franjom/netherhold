---
systems: [presentation-unity]
---
# Level6a Editor performance investigation

## Result (2026-11-15)

Unity FPS optimization is cancelled under the owner's Godot decision (ADR-0805).
Whole-level Editor performance remains UNRESOLVED. The engine-neutral
`ClassicWorldLightCache` optimization is built; no whole-level FPS gain is established.
Hash/save remains 521.

## Owner capture

Unity 6000.6.0f1, Mono, Vulkan, 1920x1080, VSync 1, `targetFrameRate = -1`, seed 1.
The owner recorded about 50 seconds of manual Level6a Play, immediately pressing Escape.
`frame_ms` spans the preceding Update start to this start; Update/path belongs to
that previous row. Select intervals using the previous row, including its time cutoff.
Medians average the middle pair; p99 uses nearest rank `ceil(0.99 * N)`.

| Previous-row selection | Intervals | Median ms | p99 ms | Maximum ms |
|---|---:|---:|---:|---:|
| Post-Escape path 0 | 2370 | 19.371 | 43.500 | 103.078 |
| Path 0, interval starts after 10 s | 2060 | 18.969 | 33.396 | 73.832 |

The recovered raw profile contains frames 0-1082 (`analysis-002`); the earlier
loader's memory limit omitted 0-209 from `analysis-001`, not from the capture.
Both analyses are retained. Steady raw `PrepareLighting` reached 3.110 ms.
No single panel or Hierarchy operation is established as the universal cause.

## Scoped lighting cache result

`ClassicWorldLightCache` reuses ordered light tables when exact inputs, visibility
and public table revisions permit. Same-frame behavior, source order and eviction
remain covered. The optimization lives in `DK2.Presentation`.

A synthetic Unity Mono comparison measures `PrepareFrame` only: 324 visible tiles,
eight warmups, 400 alternating timed pairs per scenario. Values are means in ms.
The retained candidate matches current source after namespace/newline normalization.

| Scenario | Before | After |
|---|---:|---:|
| Stable | 0.869541 | 0.044673 |
| Hand changes every third frame | 0.788722 | 0.301970 |
| Hand changes every frame | 0.843288 | 0.853444 |
| Visibility and torch changes | 0.940320 | 0.248671 |

Changing inputs every frame was 1.20% slower. These are not whole-level FPS results.

## Evidence

Evidence is add-only outside the clone:
- `F:/DK2Evidence/studies/level6a-owner-editor--2026-11-14--001`:
  `session-snapshot-001` has SHA-256-verified CSV, metadata, scene and launch copies;
  `analysis-002` retains recovered raw data; `mono-cache-001/cache-timings.csv` and
  `mono-cache-001/implementation-001` retain benchmark inputs, provenance and hashes.
- `F:/DK2Evidence/studies/level6a-editor-controlled--2026-11-14--001`
  retains the failed maximized-layout/renderer controls. These established no layout
  remedy or screen-presentation/FPS acceptance.
- `F:/DK2Evidence/studies/level6a-editor-postescape--2026-11-14--001`
  retains earlier hitch evidence, including the Hierarchy refresh at baseline `a759b9bd`.
