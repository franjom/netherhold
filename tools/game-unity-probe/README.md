# Game profiling probe

Plays a campaign level through the production `DungeonRunner` in a standalone
**Development** player, headless, and reports where slow frames went. One command:

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
```

Requires an activated Unity 6000.6.0f1 editor with Windows standalone build support
(`-Il2cpp` also its Windows IL2CPP module, which the script checks for; `-Unity` names
another install) and
the install at `E:/GogGames/Dungeon Keeper 2` (`-Install` to override). It syncs this
checkout's DLLs, copies `Assets`, `Packages` and `ProjectSettings` into one scratch
project under the main clone's ignored `build/game-unity-probe/project` (reused for its
import cache; the first run seeds `Library` from a clone's), builds the player and runs it.
It never opens or saves the owner's project.

The player runs at 60 frames a second (vSync off, `targetFrameRate` 60). Standing in for a
player, it tags every solid tile within 8 tiles of the first keeper's heart at 5 s of play
and within 16 at 60 s (play time starts at the first frame with a running simulation), through the runner's own input queue, so imps dig, the navigation mesh
rebuilds and terrain chunks redraw. Each run directory holds:

| File | What |
|---|---|
| `summary.txt` | Frame p50/p95/p99/max after the first 2 s of play, frames over 33 ms, mean frame with and without a tick, each `-Markers` marker's mean with and without a tick, and the 15 worst frames broken down |
| `frames.csv` | One row per frame: frame time, ticks run, grid searches, GC bytes and collections, and the time of each recorded marker |
| `capture.raw` | Only with `-CaptureFrom 20 -CaptureTo 50`: a Unity Profiler capture of that window. Open **Window → Analysis → Profiler**, then **Load**, and click a spike. A full capture adds overhead to the frames it records, so read its shape, not its numbers |
| `player.log`, `build.log` | Unity's logs |

The markers are those in [unity/README.md](../../unity/README.md#measuring-performance)
and ADR-0444. The player runs with `-batchmode` and no window, and its cameras do not render:
`DK2.TransparentTerrain.Sort`, which runs before each camera draws, never fired in any
run. So this is the CPU work of simulation, presentation, animation and mesh updates,
and frame times are lower bounds for a windowed game. Rows are
formatted only after the run, so the GC columns are the game's own allocation. The tags go in at wall-clock times, so which tick they
land on, and the digging after, differs between runs: compare the shape over several
runs (worst frames, which marker they sit in), not one run's p99. A frame over 50 ms
with no DK2 marker in it is unexplained here: other work on the machine, or Unity's own;
open a capture to tell. A Development player also includes profiler overhead
that a release build strips. Never report these numbers as the finished game's frame rate.
