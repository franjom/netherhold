# Room life Unity smoke verification

Run from the final synced classic worktree:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/classic-unity-probe/run-room-life.ps1
```

The helper requires a marked scratch project (`-Project` overrides the cached
crowd-probe project). It copies current Assets, Packages, ProjectSettings and
synced DLLs, verifies the production particle module, then builds and runs a
Development standalone player. The owner's Unity project is never opened.
Offscreen captures use a graphics device and URP SingleCameraRequest.

Verification, 2026-10-12: Unity 6000.6.0f1, AMD Radeon Graphics, Direct3D12.
Artifacts: `D:/DungeonKeeperRemake/build/room-life-probe/20261012-215841/`.
Recorded HEAD `1448bc1b`, save/hash version 238. Source/assembly SHA256
snapshots, build/player logs, 22 PNGs and `metrics.json` are retained there.
Editor build and player exited 0; no script, shader, sprite-loading or runtime
exception matches occurred. The owner's project was not opened.

Production simulation fixtures create the typed requests for a completed Dirt
Path claim, whole 3x3 Portal capture and Goblin bed creation. Those requests
use ClassicConstructionEffects and the original sprite provider, with an
injected view clock advancing strict 50ms gates independently of GPU readback.

| Effect | Requests | Born / model / native particles | Changed pixels |
|---|---:|---:|---:|
| 5, tile claim | 1 | 32 / 32 / 32 | 2,125 |
| 8, Portal capture | 9 | 288 / 288 / 288 | 51,110 |
| 17, bed creation | 1 | 256 / 256 / 256 | 3,221 |

Each effect begins empty, emits across its scheduled waves and cleans up to
zero model/native particles. The three `authored-*-on.png` captures were
inspected: visible authored sprites, red claim/capture particles and mixed
bed colours, with no magenta shader fallback.

The existing checks also pass: a production 2x2 Lair build advances in
1/3/4-tile waves; its four requests render 256 native/model particles in two
groups and change 5,376 pixels. The first empty wall-clock update occurs at
1.554 seconds, inside the 2.2-second cleanup bound. Rat mesh/position changes,
Goblin/Warlock entrance playback and final holds, and native Dwarf/Warlock
interpolation endpoints all pass. Level8 Guard22 visits failed-hunt and wander
states, first moves at tick 1,057 and reaches 2,315 raw displacement over
1,500 ticks without artificial position changes.

This verifies renderer integration and controlled emission counts. The new
effect captures use an injected clock; the existing construction expiry uses
wall time and includes scheduling delays. Full Level8 visual play, original
game visual equivalence, bank geometry and performance remain outside this probe.
