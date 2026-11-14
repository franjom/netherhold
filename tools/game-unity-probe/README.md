# Game profiling probe

Plays a campaign level through the production `DungeonRunner` of `Assets/DK2/Scenes/Dungeon.unity`
in a standalone **Development** player, headless by default, and reports where slow frames went. One command:

```powershell
& tools/game-unity-probe/run.ps1                       # Level1, seed 1, 150 s
& tools/game-unity-probe/run.ps1 -Level Level8 -Seconds 300
# Extra columns, one per marker (unity/README.md#measuring-performance), to find a hotspot inside one:
& tools/game-unity-probe/run.ps1 -Level Level6a -Seconds 90 -Markers "DK2.present.tiles,DK2.Scene.TorchRefresh"
# A capture, then its main-thread self time and self GC allocation per sample path, frames with
# and without a tick, and the worst frames broken down (capture-report.txt; a batchmode Editor
# reads capture.raw in the scratch project):
& tools/game-unity-probe/run.ps1 -Level Level6a -Seconds 30 -CaptureFrom 14 -CaptureTo 24
& tools/game-unity-probe/report-capture.ps1 -RunDirectory <run directory>
# The game's own path (ADR-0458): ticks on the worker thread, and a wait_tick_ms column plus a
# summary line saying how often the next frame's join found the tick still running:
& tools/game-unity-probe/run.ps1 -Level Level6a -Seconds 90 -WorkerTicks
# The same player compiled ahead of time with IL2CPP (needs the editor's Windows IL2CPP module
# and Visual Studio's C++ tools; the build takes longer). backend.txt in the run directory says which:
& tools/game-unity-probe/run.ps1 -Level Level6a -Seconds 90 -WorkerTicks -Il2cpp
# Also the game's own release-player recorder (-recordFrames, unity/README.md), written to
# <run>/recorder, to check it against the probe's columns:
& tools/game-unity-probe/run.ps1 -Level Level6a -Seconds 30 -WorkerTicks -Il2cpp -RecordFrames
# Windowed at 1920x1080 (the window is shown; a hidden one draws nothing) instead of headless, so the
# cameras draw and the engine's own frame work (shader pipelines, uploads, present) is in the capture;
# -FrameCap -1 lifts the 60 FPS cap:
& tools/game-unity-probe/run.ps1 -Level Level6a -Seconds 32 -WorkerTicks -Il2cpp -Windowed -FrameCap -1 -CaptureFrom 25 -CaptureTo 28
# Settings lines for the run's game-settings.ini (';' separates them; without it every setting is default),
# and a screenshot.png at play second 20 (needs -Windowed):
& tools/game-unity-probe/run.ps1 -Level Level1 -Seconds 30 -Windowed -ProbeSettings 'Gamma=45;Msaa=4' -Screenshot 20
# The GPU cost of settings variants on a still, paused frame, interleaved so a clock change hits every
# variant alike (benchmark.txt; needs -Windowed; the player runs long enough whatever -Seconds says):
& tools/game-unity-probe/run.ps1 -Level Level1 -Seconds 60 -Windowed -FrameCap -1 -Il2cpp -Benchmark 'Msaa=1|Msaa=4' -BenchmarkRounds 12 -BenchmarkSegment 4
```

Requires an activated Unity 6000.6.0f1 editor with Windows standalone build support
(`-Il2cpp` also its Windows IL2CPP module, which the script checks for; `-Unity` names
another install) and
the install at `E:/GogGames/Dungeon Keeper 2` (`-Install` to override). It syncs this
checkout's DLLs, copies `Assets`, `Packages` and `ProjectSettings` into one scratch
project under the main clone's ignored `build/game-unity-probe/project` (reused for its
import cache; the first run seeds `Library` from a clone's), builds the player and runs it.
It never opens or saves the owner's project.

The scene is the game's `Assets/DK2/Scenes/Dungeon.unity` as it stands in the working tree, so the
camera, light, ambient, fog of war and every other Inspector value are the game's; the probe sets
only the level, seed, install path, worker-tick mode and the run's settings. A run reads and writes
only its own `game-settings.ini` and campaign profile in the run directory, never the player's. The
player's `-fogOfWar 0|1` argument overrides the scene's fog of war; `run.ps1` has no parameter for it.

The player runs at 60 frames a second (vSync off, `targetFrameRate` 60). Standing in for a
player, it tags every solid tile within 8 tiles of the first keeper's heart at 5 s of play
and within 16 at 60 s (play time starts at the first frame with a running simulation), through the runner's own input queue, so imps dig, the navigation mesh
rebuilds and terrain chunks redraw. Each run directory holds:

| File | What |
|---|---|
| `summary.txt` | Frame p50/p95/p99/max after the first 2 s of play, a `gpu` line (p50, p95, mean of the frames FrameTimingManager timed), frames over 33 ms, mean frame with and without a tick, each `-Markers` marker's mean with and without a tick, and the 15 worst frames broken down |
| `frames.csv` | One row per frame: frame time, `gpu_ms`, ticks run, grid searches, GC bytes and collections, and the time of each recorded marker |
| `game-settings.ini`, `campaign-profile.dk2p` | The settings (`-ProbeSettings` lines, else defaults) and profile this run used |
| `screenshot.png` | Only with `-Screenshot`: the window at that play second, read back after the frame (the project has no ScreenCapture module) |
| `benchmark.txt` | Only with `-Benchmark`: per variant, the median of the segment medians of GPU ms, the lowest and highest segment median and the frame count, and whether the camera stayed still |
| `capture.raw` | Only with `-CaptureFrom 20 -CaptureTo 50`: a Unity Profiler capture of that window. Open **Window → Analysis → Profiler**, then **Load**, and click a spike. A full capture adds overhead to the frames it records, so read its shape, not its numbers |
| `player.log`, `build.log` | Unity's logs |

The markers are those in [unity/README.md](../../unity/README.md#measuring-performance)
and ADR-0444. Without `-Windowed` the player runs with `-batchmode` and no window, and its cameras do not render:
`DK2.TransparentTerrain.Sort`, which runs before each camera draws, never fired in any
such run, and `gpu_ms` is empty. So this is the CPU work of simulation, presentation, animation and mesh updates,
and frame times are lower bounds for a windowed game. `gpu_ms` is FrameTimingManager's latest GPU frame
time, which lags a few frames. Compare GPU cost between settings with `-Benchmark`, not between
gameplay runs: those varied more between identical runs than between settings. A benchmark pauses the
simulation (speed 0), skips the fly-by, keeps the camera still, cycles the variants in
`-BenchmarkSegment` second segments `-BenchmarkRounds` times, and drops the first second of each. Rows are
formatted only after the run, so the GC columns are the game's own allocation. The tags go in at wall-clock times, so which tick they
land on, and the digging after, differs between runs: compare the shape over several
runs (worst frames, which marker they sit in), not one run's p99. A frame over 50 ms
with no DK2 marker in it is unexplained here: other work on the machine, or Unity's own;
open a capture to tell. A Development player also includes profiler overhead
that a release build strips. Never report these numbers as the finished game's frame rate.
