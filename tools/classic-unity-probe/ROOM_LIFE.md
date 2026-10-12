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

Final verification, 2026-10-12: Unity 6000.6.0f1, AMD Radeon Graphics, Direct3D12.
Artifacts: `D:/DungeonKeeperRemake/build/room-life-probe/20261012-200126/`.
Recorded HEAD `d1e67516`, final working-tree fixes and save/hash version 236.
Source/assembly SHA256 snapshots, build/player logs, PNGs and `metrics.json`
are retained there. Editor/player build and runtime exited 0; zero compile,
shader or runtime exception matches, including shutdown cleanup.

- Production `BuildRoomCommand.Rect` with RoomBuildSystem and RoomBuildWaveSystem
  made a 2x2 Lair in 1/3/4 tiles over three ticks, emitting four ordered requests.
- Replaying those requests immediately before capture rendered 256 native/model
  particles in two groups; the effect changed 5,376 pixels against its baseline.
  The first empty wall-clock update was observed at 1.598 seconds; native/model
  counts were zero within the 2.2-second capture-slack bound.
- Rat_Run mesh and generic-object position changed. Goblin and Warlock entrance
  meshes changed and held their final meshes. DK2/ClassicEffect was supported.
- Original Dwarf/Warlock meshes created through DungeonScene interpolated native
  Transform coordinates at fractions 0/.5/1. Dwarf XYZ: `(2,.5,-3)`,
  `(3,.75,-2)`, `(4,1,-1)`; Warlock: `(1,1,-2)`, `(2,.75,-3)`, `(3,.5,-4)`.
- The default Level8 campaign factory, seed 1, observed Guard22 at the projected
  Training Dummy centre during failed-hunt state 4, then visited state 87.
  Over 1,500 headless ticks it first moved at tick 991, was off centre on 510 rows and
  reached 2,385 raw units of displacement. No positions or reservations changed
  artificially; stationary furniture was checked through WorldView projection.
- `effects-on.png`, `rat-moving-close.png`, `goblin-final-close.png` and
  `warlock-moving-close.png` were inspected: visible sprites and original
  textured models, with no magenta shader fallback.

Companion final validation: 5,038 .NET tests passed, zero skipped. Modern packet
replays completed all 20 seeds per level (1..20): Level1 20/20 wins, Level2
4/20, Level3 5/20. Artifacts: `build/classic-step3-replays/level8-followup235/`
under the main clone; its directory label is old, its traces use version 236.
These are modern replay results, not original-game parity evidence.

This checks production construction, native scene interpolation and the natural
Level8 Guard22 progression. Rat poses and the interpolation endpoints are
controlled inputs; full Level8 visual play, actual bank terrain geometry,
original-game visual equivalence and performance are outside this short smoke.
Expiry observations include capture/scheduling delays; they verify bounded
cleanup rather than an exact lifetime.
