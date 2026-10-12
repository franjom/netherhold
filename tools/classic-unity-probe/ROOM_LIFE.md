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
Artifacts: `D:/DungeonKeeperRemake/build/room-life-probe/20261012-180036/`.
The recorded HEAD is `96fa1942`; sources are the final working tree, including
the integration of master `3877e855` and save/hash version 233. SHA256 snapshots,
build/player logs, PNGs and `metrics.json` are retained in that directory.

- Editor/player build and runtime exited 0; zero compile, shader or runtime
  exception matches, including shutdown cleanup.
- Production `BuildRoomCommand.Rect` with RoomBuildSystem and RoomBuildWaveSystem
  made a 2x2 Lair in 1/3/4 tiles over three ticks, emitting four ordered requests.
- Replaying those requests immediately before capture rendered 256 native/model
  particles in two groups; the effect changed 5,376 pixels against its baseline.
  The first empty wall-clock update was observed at 1.989 seconds; both native
  and model counts were zero within the 2.2-second capture-slack bound.
- Rat_Run mesh and generic-object position changed. Goblin and Warlock entrance
  meshes changed and held their final meshes. DK2/ClassicEffect was supported.
- `effects-on.png`, `rat-moving-close.png`, `goblin-final-close.png` and
  `warlock-moving-close.png` were inspected: visible sprites and original
  textured models, with no magenta shader fallback.

This verifies the production construction command/wave and rendering paths.
The rat pose is controlled input; creature arrival placement, full Level8 play,
original-game visual equivalence and performance are outside this short smoke.
The expiry observation includes capture and scheduling delays; it is a bounded
cleanup check, not an exact lifetime measurement.
