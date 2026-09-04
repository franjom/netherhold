# The Unity project

Presentation only. It holds no game state, decides no rule and stores no
balance number — see `docs/DECISIONS.md` ADR-0002, ADR-0035 and ADR-0036.

## Running it

```
winget install --source winget --id Unity.UnityHub
winget install --source winget --id Unity.Unity.6000     # 6000.6.0f1
```

Then **sign in once in Unity Hub**. Unity Personal is enough. Without it the
editor has no licence and exits with code 198 (`Found 0 entitlement groups`) —
it will not compile or run anything, however correct the code is.

```
powershell -File unity/sync-assemblies.ps1
```

Unity cannot reference a `.csproj`, so the simulation arrives as DLLs in
`DK2.Unity/Assets/Plugins/DK2`. Run this after any change under `src/`. The DLLs
are build output and are gitignored.

Then open `unity/DK2.Unity` in Unity 6000.6.0f1, put `DungeonRunner` on a
GameObject in a scene, and press Play. Point it at a DK2 install with the
component's **Install Directory** field or the `DK2_DIR` environment variable.

You should get a campaign level as coloured boxes: walls tall, floors flat, room
furniture standing on the floor, creatures as small uprights that turn gold when
carrying gold. The colours are derived from each asset's key, so a terrain is
always the same colour — see `PlaceholderProvider`.

## Verifying the adapter without opening Unity

```
dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj
```

Compiles `Assets/DK2/*.cs` against the editor's real `UnityEngine` module
assemblies. **This is how the project's "do not commit code you have not built"
rule reaches Unity code**, and it is the only way it can: a `MonoBehaviour` that
does not compile otherwise sits in a repository for months looking finished.

It is **not** in `DK2.slnx` on purpose. A machine without Unity cannot build it
and CI has no Unity, so including it would break the build for everyone who has
not installed the editor.

Pass `-p:UnityManagedDir=...` if the editor is not in `Program Files`.

## What it does not do

- **It does not load art.** A `ResolvedAsset` with a `FilePath` points at
  converter output, and **there is no converter yet** (plan §7.1) — so nothing
  has ever produced one of those files, and writing a runtime glTF loader now
  would be writing an importer for input that does not exist. Files draw as
  placeholders and log a warning once. `DungeonScene.Instantiate` is the method
  that gains a branch when the converter lands.
- **It does not take input.** No selecting, no digging, no building. Commands
  reach the simulation as an `InputFrame` and nothing here builds one yet.
- **It does not claim a speed.** The tick rate comes from
  `SimulationTiming.TicksPerSecond`, the unmeasured Q1 placeholder. Tick counts
  are faithful; wall-clock speed is not a claim, and the on-screen diagnostics
  say so.
- **It draws boxes, not DK2's walls.** DK2 picks wall pieces by the size and
  shape of the room behind them and swaps textures for damage states
  (`docs/research/wall-rendering.md`). One mesh per terrain is a known
  simplification, and the variant set waits on Q71 rather than being invented.

## Where the logic actually lives

Almost nothing is in this folder, deliberately. `ScenePresenter`
(`src/DK2.Presentation`) decides what changed, in what order, which asset each
thing needs, and create-versus-update-versus-remove — all of it tested against a
real campaign level. `DungeonScene` implements six methods with no judgement in
them beyond "file or placeholder".

**Read `src/DK2.Presentation/TextRenderer.cs` before changing anything here.**
It is the same job against a character grid, it runs in CI, and a disagreement
between the two is a bug in one of them rather than a matter of taste.

## Verifying it inside Unity, from the command line

Two headless checks, both of which have been run and pass. They need an
activated editor (`Preferences → Licenses → Add → free personal license`);
without one Unity exits **198** with `Found 0 entitlement groups`.

**Does Unity compile the adapter?**

```
"C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe" -batchmode -quit -nographics ^
  -projectPath unity\DK2.Unity -logFile build\unity-batch.log
```

Exit 0 means Unity built `DK2.Unity.dll` against the seven netstandard2.1
assemblies in `Assets/Plugins/DK2`. Stronger than `DK2.Unity.Compile`, because
it is Unity's own compiler resolving Unity's own references.

**Does the whole pipeline actually run?**

```
"C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe" -batchmode -quit -nographics ^
  -projectPath unity\DK2.Unity -logFile build\unity-smoke.log ^
  -executeMethod DK2.Unity.Editor.DungeonSetup.SmokeTest
```

Loads a real campaign level, builds the simulation, presents it into a
`DungeonScene`, ticks it 20 times and presents again — then fails unless there is
one GameObject per tile. **This is the check worth having**, because everything
it exercises was never going to fail at compile time and was always going to fail
on first Play: whether `GameDatabaseLoader` can read a DK2 install from inside
Unity's Mono runtime, whether the netstandard2.1 assemblies load there at all,
and whether the presenter produces objects.

Current result:

```
DK2 smoke test: 30x38 level, 1140 tile objects, 0 creatures,
art: 1178 asset(s) resolved: 1178 from placeholder,
hash aae0f0e17a769a21 -> 8e38409cdbd8d7c0
DK2 smoke test: OK
```

**Zero creatures is correct, not a bug.** A level's creatures live in the THINGS
record, which is unparsed until M6 — so a campaign level currently loads with
terrain and no occupants. The imps in the harness scenarios are placed by the
scenario file, not by the map.

It skips cleanly with exit 0 when there is no DK2 install, exactly as the
install-gated tests do.
