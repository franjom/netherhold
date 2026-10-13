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
Artifacts: `D:/DungeonKeeperRemake/build/room-life-probe/20261013-230048/`.
Recorded HEAD `548ffc14` plus the Heart change, save/hash version 243.
Source/assembly SHA256 snapshots, build/player logs, 56 PNGs and
`metrics.json` are retained there. Editor build and player exited 0; no
script errors, shader errors or runtime exception matches occurred.

Production simulation fixtures create typed requests for a completed Dirt
Path claim, whole 3x3 Portal capture, Goblin bed creation, Rock digging and
Gold mining. They use ClassicConstructionEffects, original sprite resources
and the shared ClassicMeshes cache. An injected view clock advances strict
50ms gates independently of GPU readback.

| Effect | Requests | Born / model / native particles | Changed pixels |
|---|---:|---:|---:|
| 5, tile claim | 1 | 32 / 32 / 32 | 2,878 |
| 8, Portal capture | 9 | 256 / 256 / 256 | 31,910 |
| 17, bed creation | 1 | 256 / 256 / 256 | 3,034 |
| 6, Dig Rock | 1 | 72 / 72 / 72 | 4,789 |
| 7, Dig Gold | 1 | 60 / 60 / 60 | 4,504 |

Room capture suppresses the interior tile; floor capture retains its native
centre origin. Separate captures verify Goblin recovery, Salamander/Mistress
sleep assets and the Mistress bed's entry clip. Mesh endpoint measurements
rule out a significant vertical gap between Entrance and idle.

The startup fixture resolves `Imp-PoseFrame.kmf`, holds its mesh and position
across five render fractions at zero velocity, then resolves `Imp_Run.kmf`.
The moving mesh and interpolated position change, and its initial rate uses
the current native speed cap. All four startup metrics pass.

The Heart fixture creates a native 5x5 Heart at 100%, 50% and 10% health through
the `WorldView` object/decoration stream and production `DungeonScene`. Six black-background captures
(`heart-<percent>-start/pulse.png`) show the health stages and one heartbeat tick.
Metrics record the requested phase step, vertical scale and decay phase alongside
Unity's actual Y/X scale, material names and texture names. The pass gate checks
authored texture alternatives, health-dependent scale, changed mesh vertices and
the Unity mesh against the native clip sampled at the requested phase step.
The healthy Heart also exercises the production visibility/beat gate: after
rendering it, a pending beat at tick 10 records wall-time 5000ms; tick 28 at
6000ms remains blocked, and tick 29 at 6001ms requests effect 59 at the native
Heart position. An injected effect clock samples rising elements 98/100 near
0.5 seconds and their falling replacements 99/101 near 1.5 seconds, then checks
model/native cleanup at 3.1 seconds. `heart-mana-rising/falling.png` and separate
request, element-id and motion metrics cover this path. Smoke framing encloses
the Heart and every live particle; matched particle-disabled captures establish
visible green sparkle/flake pixels in both motion stages. These Heart fixtures
pass. Rising and falling particles change 1,210 and 1,232 green pixels;
both stages were inspected. The same-tick two-call health advance check
passes. Full regression verification passes 5,215 tests with no failures or skips.

After the smoke check, a fresh healthy Heart receives two observed health setter
calls (zero damage and zero regeneration). The scene queue must advance its mesh
twice without advancing simulation time: at tick 1, fraction 0, its sampled phase
is three native steps rather than one. `heartHealthAdvanceMatched` checks all mesh
vertices, and `heart-health-setter-advances.png` captures the resulting pose.

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
change 5,904 pixels. Wall-clock expiry is observed at 1.575 seconds, inside
the 2.2-second cleanup bound. Rat mesh/position changes, Goblin/Warlock entrance
playback and final holds, and Dwarf/Warlock interpolation endpoints pass.
Level8 Guard22 visits failed-hunt and wander states, first moves at tick 601
and reaches 2,328 raw displacement over 1,500 ticks.

This verifies renderer integration, controlled emission counts and visible
mesh motion. Dig captures use an injected clock; construction expiry uses wall
time and includes scheduling delays. Full Level8 visual play, original-game
visual equivalence, bank geometry and performance remain outside this probe.
