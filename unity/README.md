# The Unity project

Presentation only. It holds no game state, decides no rule and stores no balance
number — see `docs/DECISIONS.md` ADR-0002, ADR-0035 and ADR-0036.

## Running it

```
winget install --source winget --id Unity.UnityHub
winget install --source winget --id Unity.Unity.6000     # 6000.6.0f1
```

Then **sign in once in Unity Hub**. Unity Personal is enough. Without it the editor
has no licence and exits with code 198 (`Found 0 entitlement groups`) — it will not
compile or run anything, however correct the code is.

```
powershell -File unity/sync-assemblies.ps1
```

Unity cannot reference a `.csproj`, so the simulation arrives as DLLs in
`DK2.Unity/Assets/Plugins/DK2` — eight `netstandard2.1` assemblies, listed in
`sync-assemblies.ps1`. Run this after any change under `src/`. The DLLs are build
output and are gitignored.

Then open `unity/DK2.Unity` in Unity 6000.6.0f1 (`ProjectSettings/ProjectVersion.txt`
is the authority), put `DungeonRunner` on a GameObject in a scene, and press Play.
Point it at a DK2 install with the component's **Install Directory** field or the
`DK2_DIR` environment variable.

You should get a campaign level as coloured boxes, with its objects, creatures (still, in
their idle model's first frame) and wall torches drawn from the install's own meshes and
textures (ADR-0408): walls tall, floors flat, room furniture standing on the floor. Heights
are DK2's: the floor is at one tile (0x1000) and a wall's top at two. The component's
**Classic Meshes** box turns the real models off. The map is laid out
with X east and Z north, so the opening camera faces north (`DungeonScene.Ground`); until
ADR-0411 every level was drawn as its mirror image. **Height means one thing — whether the terrain fills its tile** — so
the dug-out parts of a level read as dug out. Colour is derived from each asset's
key, so a terrain is always the same colour; see `PlaceholderProvider`. A tile marked
for digging is tinted amber, and wall torches are placed by `TorchLayout`.

WASD or the arrow keys pan and the mouse wheel zooms. The default Hand (`1` or `5`)
uses left click to pick up a creature or toggle a diggable tile's excavation tag.
Left drag applies the inverse of the starting tile's tag state to a rectangle;
this drag policy is a modern interface choice, not a claim about DK2's exact drag behavior.
Right click drops the held creature, or slaps a creature when the hand is empty.
`3` selects Build, `4` Sell, and `7` Create Imp. `[` and `]` select the room used by Build; the Game view displays
its name. The DungeonRunner Inspector's Selected Room Id dropdown also selects
rooms by name. Choose Lair to build sleeping space. Drag Build and Sell rectangles;
click Create Imp. The tool hotkeys are interface choices (ADR-0104).

## Verifying the adapter without opening Unity

```
dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj
```

Compiles `Assets/DK2/*.cs` against the editor's real `UnityEngine` module assemblies.
**This is how the project's "do not commit code you have not built" rule reaches
Unity code**, and it is the only way it can: a `MonoBehaviour` that does not compile
otherwise sits in a repository for months looking finished.

It is **not** in `DK2.slnx` on purpose: a machine without Unity cannot build it and
CI has no Unity, so including it would break the build for everyone who has not
installed the editor. Pass `-p:UnityManagedDir=...` if the editor is not in
`Program Files`.

## What it does not do

- **It draws only objects from the original art.** `ClassicMeshes` builds a Unity mesh
  and materials from a KMF in the player's install (`InstallArtProvider`, `RenderMesh`,
  ADR-0408); terrain, rooms and creatures are still boxes, the next steps of
  `docs/plans/2026-10-09-classic-look.md`. Converter output (`dk2tools convert`, GLB and
  PNG) is never loaded. Every material is opaque, and textures are not yet chosen by
  owner or damage.
- **It runs at DK2's rate, and does not smooth between ticks.** `DungeonRunner.Update`
  accrues `Time.deltaTime × SimulationTiming.TicksPerSecond × speed` and ticks on
  whole ticks, so at `speed` 1 the wall clock is the game's (Q1, ADR-0089). What is
  missing is interpolation: a creature jumps one tick at a time. The component's
  tooltip and the on-screen diagnostics still call the rate the "unmeasured Q1
  placeholder", which Q1 no longer is; that text is stale.
- **It draws boxes, not DK2's walls.** DK2 picks wall pieces by the shape of the room
  behind them and swaps textures for damage states (`docs/research/wall-rendering.md`).
  `src/DK2.Presentation/RoomWalls.cs` computes the piece choice, but this adapter does
  not consume it: one mesh per terrain is a known simplification.

## Where the logic actually lives

Almost nothing is in this folder, deliberately. `ScenePresenter`
(`src/DK2.Presentation`) decides what changed, in what order, which asset each thing
needs, and create-versus-update-versus-remove, all tested against a real campaign
level. `DungeonScene` implements `IRenderTarget` (create, update and remove for
tiles, creatures, objects and torches, plus `Flush`) and adds a `Clear`, with no
judgement in it beyond "file or placeholder". `DungeonRunner` reads the pointer into a
`PlayerIntent`, translates it through `PlayerInput`, and sends the simulation only
`InputFrame`s; `TilePicker` decides which tile a ray hits.

**Read `src/DK2.Presentation/TextRenderer.cs` before changing anything here.** It is
the same job against a character grid, it runs in CI, and a disagreement between the
two is a bug in one of them rather than a matter of taste.

## The classic look's editor steps

```
"C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath unity\DK2.Unity -logFile build\unity-urp.log ^
  -executeMethod DK2.Unity.Editor.ClassicLookSetup.UseUrp
"C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe" -batchmode ^
  -projectPath unity\DK2.Unity -logFile build\unity-step0.log ^
  -executeMethod DK2.Unity.Editor.ClassicLookSetup.RenderStep0
```

`UseUrp` creates `Assets/DK2/Rendering/DK2-URP.asset` and puts every quality level on it.
The renderer and `UseUrp` use Forward+ because terrain is merged into 16×16-tile
meshes: Forward's four additional lights per renderer cannot cover a chunk's wall
torches. Forward+ selects lights by screen tile and removes that per-object cap
([Unity's rendering paths](https://docs.unity3d.com/Manual/urp/rendering/forward-rendering-paths.html)).
`RenderStep0` writes `build/classic-look/step0-pieces.png` (the gold pile, the rock wall's
side and the Lair's five wall pieces) and `step0-level1.png` (Level 1 from the runner's
opening camera). Neither takes `-nographics`, and the second no `-quit`: it waits on the
editor loop and exits by itself. Three traps it works around, each seen as a wrong picture:
a camera rendered before URP has a pipeline draws nothing; the editor's placeholder shader
stands in while a variant compiles (`ShaderUtil.allowAsyncCompilation` off); and with the SRP
Batcher on, every material in a batch-mode render took one texture (Direct3D 12) or none
(Direct3D 11), so the render turns it off for itself. Whether play mode shows the last one is
not yet seen. `UseUrp` must save the renderer asset before the pipeline names it, or URP has
no default renderer and draws nothing.

## Shaders in a built player

The adapter makes its materials at run time from shaders it finds by name (`ClassicMeshes`,
`ClassicConstructionEffects`). A player only contains shaders, and shader variants, that some
asset in the build references, so `Assets/DK2/Resources/Shaders` holds one material per shader
and keyword set the adapter uses: URP Lit opaque and blended, each with and without emission
(every one with specular and reflections off), URP Unlit opaque and additive, and the Standard
and Unlit/Color fallbacks. Anything under Resources is always built. A new keyword on a runtime
material needs a material here that carries it, or the player draws that material with a
missing-shader fallback, usually pink. The
`DK2/ClassicEffect` shader is itself under Resources, which includes every variant (ADR-0455).

## Measuring performance

`DungeonRunner` runs its ticks on a worker thread after each frame is presented (its
`tickOnWorkerThread` field, on by default; ADR-0458), so a tick overlaps the frame's rendering
and the view shows it one frame later. The probe below turns that off so its recorders see the
tick's own cost on the main thread.

Open **Window → Analysis → Profiler**, restart Play mode, and record the CPU
Timeline, Rendering, Memory and GPU modules where supported. Leave Deep Profile
off for baseline captures. The adapter exposes these CPU markers:

| Marker | Includes |
|---|---|
| `DK2.Simulation.Tick` | One simulation tick and the object position snapshot |
| `DK2.Scene.Present` | View generation, change detection and scene updates |
| `DK2.Scene.AnimatedFrame` | Position interpolation, animation and dirty torch geometry |
| `DK2.Animation.Clock` | Per-instance phase advancement and segment selection |
| `DK2.Animation.Sample` | KMF interpolation and Unity vertex conversion |
| `DK2.Animation.MeshUpload` | Vertex upload and mesh bounds recalculation |
| `DK2.Frame.Input` | Camera, tool keys and pointer input |
| `DK2.Simulation.CaptureObjectPositions` | The position snapshot, inside `DK2.Simulation.Tick` |
| `DK2.Sim.tick` | The tick inside the simulation; below it `DK2.Sim.messages`, `DK2.Sim.triggers` and one marker per system by name (ADR-0444) |
| `DK2.Sim.nav.refresh` / `nav.build` / `nav.route` | Re-reading the navigation bytes, rebuilding the mesh, one route query |
| `DK2.Sim.grid.nearest` / `grid.path` | One grid search by ImpTask's pathfinder |
| `DK2.Sim.ImpTask.*` | ImpTask's phases: claim on the way, wander, carry, job |
| `DK2.TerrainChunks.Rebuild` | One terrain chunk's mesh, inside `DK2.Scene.Present` |
| `DK2.TerrainChunks.Geometry` / `Upload` | Inside Rebuild: one changed tile's pieces regenerated (cached tiles are copied), and the chunk mesh set |
| `DK2.TransparentTerrain.SetChunk` / `Sort` | A chunk's blended faces kept; the per-camera depth sort of all of them |

Nested marker times overlap: do not add child totals to their parent's total.
Animation markers run once per animated instance; use the Hierarchy aggregate
to assess crowd cost. Tick count varies per rendered frame; report both tick
cost and whole-frame cost. These scopes do not change the simulation or its hash. At 4 ticks a second most
frames run no tick: select a spike and read the Hierarchy Calls column for how many
searches or refreshes it ran. The scenario tests print the same `DK2.Sim.` markers
without the prefix, with allocations and the worst tick (`RunTimings.Summarise`).

For a repeatable baseline, use Level8, seed 1, speed 1, the same camera,
resolution, quality settings and fog setting. Capture a quiet Heart view and a
busy view, recording the live/visible creature counts. Warm up for 10 seconds,
then save a 300-frame capture. Record CPU/GPU milliseconds, median and p95 frame
time, GC allocation/frame, draw calls, batches, triangles and memory; include
the commit, CPU/GPU, graphics API and VSync/frame cap. Compare equivalent captures
after each rendering change. Loading and asset decoding need a separate capture.

Check a **standalone Development Build** with **Autoconnect Profiler** as well
as Editor Play mode; Editor timings include Editor overhead. Keep Deep Profiling
Support off for the baseline. Use the Frame Debugger when rendering/batches are
the bottleneck, and CPU Timeline/GC allocation call stacks for script spikes.
Verify final performance in a release build, which strips profiler calls.

Frame budgets are 16.67 ms at 60 FPS and 8.33 ms at 120 FPS; these are targets,
not measured results. Inspect slow frames as well as averages. The existing
sampler-only crowd probe and its limits are in
[kmf-animation-playback.md](../docs/research/kmf-animation-playback.md).

Unity references: [collecting profiler data](https://docs.unity3d.com/Manual/profiler-profiling-applications.html),
[custom markers](https://docs.unity3d.com/Manual/profiler-add-markers-code.html),
[rendering profiling](https://docs.unity3d.com/Manual/profile-rendering.html).

## Verifying it inside Unity, from the command line

Both checks need an activated editor (`Preferences → Licenses → Add → free personal
license`); without one Unity exits **198** with `Found 0 entitlement groups`.

**Does Unity compile the adapter?**

```
"C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe" -batchmode -quit -nographics ^
  -projectPath unity\DK2.Unity -logFile build\unity-batch.log
```

Exit 0 means Unity built `DK2.Unity.dll` against the assemblies in
`Assets/Plugins/DK2`. Stronger than `DK2.Unity.Compile`, because it is Unity's own
compiler resolving Unity's own references.

**Does the whole pipeline actually run?**

```
"C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe" -batchmode -quit -nographics ^
  -projectPath unity\DK2.Unity -logFile build\unity-smoke.log ^
  -executeMethod DK2.Unity.Editor.DungeonSetup.SmokeTest
```

`SmokeTest` loads a real campaign level, builds the simulation, presents it into a
`DungeonScene`, ticks it 20 times and presents again, then fails unless there is one
GameObject per tile. It exercises what could not fail at compile time: whether
`GameDatabaseLoader` can read a DK2 install under Unity's Mono runtime, whether the
netstandard2.1 assemblies load there, and whether the presenter produces objects. It
builds the map pipeline only, so it shows no creatures; `DungeonRunner` also places
the level's decoded THINGS through `ThingProjection` (ADR-0101). The log ends with
`DK2 smoke test: OK`.

`DungeonSetup.SaveSmokeTest` does the same from each saved game in the install and
fails if half or less of the saved terrain agrees with the level's design map. Both
skip cleanly with exit 0 when there is no install (and, for saves, no saves), as the
install-gated tests do.

The smoke tests prove the objects exist; they cannot prove a camera points at them.
Framing, scale, colour and legibility need a person and a screenshot.
