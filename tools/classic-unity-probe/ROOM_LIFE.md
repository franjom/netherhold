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

Verification, 2026-10-13: Unity 6000.6.0f1, AMD Radeon Graphics, Direct3D12.
Artifacts: `D:/DungeonKeeperRemake/build/room-life-probe/20261013-193707/`.
Recorded HEAD `c1919a7f` with the crumbling debris adjustment, save/hash version 240.
Source/assembly SHA256 snapshots, build/player logs, 32 PNGs and
`metrics.json` are retained there. Editor build and player exited 0; no
script errors, shader errors or runtime exception matches occurred.

Production simulation fixtures create typed requests for a completed Dirt
Path claim, whole 3x3 Portal capture, Goblin bed creation, Rock digging and
Gold mining. They use ClassicConstructionEffects, original sprite resources
and the shared ClassicMeshes cache. An injected view clock advances strict
50ms gates independently of GPU readback.

| Effect | Requests | Born / model / native particles | Changed pixels |
|---|---:|---:|---:|
| 5, tile claim | 1 | 32 / 32 / 32 | 2,814 |
| 8, Portal capture | 9 | 288 / 288 / 288 | 50,955 |
| 17, bed creation | 1 | 256 / 256 / 256 | 3,076 |
| 6, Dig Rock | 1 | 72 / 72 / 72 | 4,789 |
| 7, Dig Gold | 1 | 60 / 60 / 60 | 4,504 |

Each effect begins empty, emits its scheduled waves and cleans up to zero
model/native particles. Each dig effect contains 48 native mesh particles.
Separate captures disable sprite renderers: Rock meshes alone change 4,062
pixels and Gold meshes alone change 3,506. Native particle rotation values
change across two 50ms updates. Both mesh-only frame pairs were inspected:
textured brown shards and yellow gold chips visibly change orientation and
position, with no magenta fallback. Portal and bed sprite captures were also
inspected; their existing checks pass.

The other checks pass: a production 2x2 Lair build advances in 1/3/4-tile
waves; four requests render 256 native/model particles in two groups and
change 5,904 pixels. Wall-clock expiry is observed at 1.555 seconds, inside
the 2.2-second cleanup bound. Rat mesh/position changes, Goblin/Warlock entrance
playback and final holds, and Dwarf/Warlock interpolation endpoints pass.
Level8 Guard22 visits failed-hunt and wander states, first moves at tick 601
and reaches 2,328 raw displacement over 1,500 ticks.

This verifies renderer integration, controlled emission counts and visible
mesh motion. Dig captures use an injected clock; construction expiry uses wall
time and includes scheduling delays. Full Level8 visual play, original-game
visual equivalence, bank geometry and performance remain outside this probe.
