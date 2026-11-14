---
systems: [presentation-unity]
---
# Level6a Editor performance investigation

## Result (2026-11-14)

UNRESOLVED. The owner's trigger is Unity Editor play of Level6a, immediately
pressing Escape to skip the intro. No production performance change was applied:
neither maximizing the Game view nor the scratch renderer-toggle variant improved
overall frame timing in the final controls.

## Controlled measurements

Source: `9ae32b8aba0d8e8161cfbe263ecfe655560ee941`, hash/save 521.
All three runs used PlayFocused, worker simulation, a fixed camera position,
rotation and FOV, 1920×1080, VSync 1, `targetFrameRate = -1` and raw profiling off.
The table covers seconds 8–18 after the runtime diagnostic started, after Escape.
Quantiles use the sorted sample at zero-based index `floor((N - 1) × p)`;
these p50 values are lower quantiles, not averaged medians for even sample counts.

| Control | Samples | p50 ms | p99 ms | Maximum ms |
|---|---:|---:|---:|---:|
| Normal layout | 565 | 16.9396 | 29.5083 | 69.4787 |
| Maximized Game view | 438 | 21.3118 | 38.8516 | 40.8269 |
| Scratch `MeshRenderer.enabled` toggle | 478 | 19.3559 | 37.2756 | 92.1331 |

The scratch variant replaced three pooled terrain-leaf `GameObject.SetActive`
calls with renderer toggles. It was rejected. Earlier VSync and hide-flag controls
moved the camera; an earlier apparent maximized-layout gain did not replicate.

All timed samples reported application and Game-view focus, the same camera pose,
and render callbacks advancing once per frame. Game-view RenderTexture proofs show
world and HUD. Native windows were visible, foreground, uncloaked and on screen,
but desktop captures were black or empty. This establishes neither screen
presentation/FPS acceptance nor a layout remedy.

## Earlier hitch and evidence

At baseline `a759b9bd`, raw profile frame 746 contains
`HierarchyView.RefreshItems` self time 68.1175 ms within a raw frame duration of
85.7022 ms. Paired recorder frame 747 has a 90.779 ms wall interval and
6.478 ms runner Update. Raw duration and wall interval are different measures;
this locates one earlier hitch, not a universal cause on the current source.

Evidence is add-only outside the clone:
- `F:/DK2Evidence/studies/level6a-editor-controlled--2026-11-14--001`
  (109 files; `focused-evidence/summary.json`,
  `focused-evidence/controlled-input-manifest.json`, and
  `editor-renderer-toggle/variant-summary.json`).
- `F:/DK2Evidence/studies/level6a-editor-postescape--2026-11-14--001` (59 files).

Independent review verified 13 source snapshots, nine DLL hashes and proof images.
Next: record an actual visible owner Editor session for 30 seconds with the
existing FrameRecorder and correlate its timeline. Owner availability is pending;
the ignored launcher is `build/level6a-owner-record.ps1`.
