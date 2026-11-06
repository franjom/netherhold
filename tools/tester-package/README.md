# Tester package

A zip someone on other hardware can run without this repository, Unity or our
paths, that comes back as files we can read (docs/PERFORMANCE.md, the desktop
tier). It holds a release player of the game (IL2CPP, ADR-0690), the pinned
PresentMon, `capture.ps1` and its instructions (`README.txt`).

```powershell
# Build and zip the package (shares the game probe's scratch project; never alongside a probe run):
& tools/tester-package/build.ps1                     # -> build/tester-package/dk2-tester-<stamp>.zip
# Read a capture that came back (unzip it first):
python tools/tester-package/summarise_capture.py <capture folder> --tier desktop
```

`build.ps1 -Development` builds the same player with Unity's profiler in it, for
`capture.ps1 -ProfileFrom 25 -ProfileTo 28`, whose `recorder/capture.raw`
`tools/game-unity-probe/report-capture.ps1` turns into the frame breakdown that names
engine-side time (ADR-0692 found its 74 ms present wait this way). Such a capture is
for diagnosis; `summarise_capture.py` marks a development player invalid.

`build.ps1` refreshes the scratch project from this checkout through
`tools/game-unity-probe/prepare-project.ps1`, builds `Assets/DK2/Scenes/Dungeon.unity`
as a non-development IL2CPP player (`ReleasePlayerBuild.cs`), verifies and copies
PresentMon 2.6.0 (`tools/classic-unity-probe/install-presentmon.ps1`: hash and
Intel's signature), and writes `manifest.txt` with the commit, whether the
worktree was clean, and the SHA256 of the player, `GameAssembly.dll`,
`UnityPlayer.dll` and the adapter's input DLLs.

The tester runs `capture.ps1 -Install <their DK2>`. It records the machine
(`machine.txt`: CPU, GPU with driver version and date, display mode, Windows),
starts PresentMon on `DK2.exe`, launches the player full screen at 1920x1080 with
`-level level6a -seed 1 -recordFrames`, waits the run out hands-off (the level's
opening fly-by plays on its own), takes a screenshot after the timed interval,
closes the game's window (which is when the game's frame recorder writes
`recorder/frames.csv` and `metadata.txt`, see unity/README.md) and zips the
folder. PresentMon needs an elevated PowerShell; without one the script warns
and the capture has the recorder's frames only.

`summarise_capture.py` judges the capture against a tier from
`tools/classic-unity-probe/performance-target.json`: every frame after two seconds
of play within the tier's budget, by the recorder's intervals and by PresentMon's
presents when traced; the opening fly-by's draw clock by `tools/game-unity-probe/draw_clock.py`;
and the ten worst frames with what each spent on the tick join, Present, the
animated frame and chunk rebuilds. A Mono or development player, another
resolution, or metadata without a CPU and GPU makes the capture invalid rather
than failed. Exit 0 pass, 1 fail, 2 invalid.

What a capture cannot do yet: play the dig burst (the probe tags walls through
the runner's input queue; a release player has no test controller), or
certify against `frame_budget.py`, whose capture shape is the Level2 portal
introduction probe's. Evidence from another machine goes to `F:\DK2Evidence\`.

## Local rendered regression gate

Budgets, evidence requirements and baseline policy live in
[PERFORMANCE.md](../../docs/PERFORMANCE.md#regression-guards). First commission
three reviewed current-build baseline captures with the settings below, using
distinct output folders; review the logs and rendered content before retaining
them as the baseline. Keep captures and reports under `F:\DK2Evidence\` add-only.

```powershell
# In an elevated PowerShell, capture each baseline separately (repeat with new output paths):
& 'F:/DK2Evidence/packages/current/capture.ps1' -Install 'C:/Games/Dungeon Keeper 2' `
    -Level level6a -Seed 1 -Seconds 40 -Width 1920 -Height 1080 -Windowed `
    -FrameCap -1 -VSync 0 -Output 'F:/DK2Evidence/performance/baseline-1'

# Use a PowerShell array for baseline paths, including paths containing spaces.
$baseline = @('F:/DK2Evidence/performance/baseline-1',
              'F:/DK2Evidence/performance/baseline-2',
              'F:/DK2Evidence/performance/baseline-3')
& tools/tester-package/run-regression.ps1 -Package 'F:/DK2Evidence/packages/candidate' `
    -Install 'C:/Games/Dungeon Keeper 2' -Baseline $baseline `
    -OutputRoot 'F:/DK2Evidence/performance/candidates'

# Or compare already captured runs; each variable is an array of capture directories:
$candidate = @('F:/DK2Evidence/performance/candidate-1',
               'F:/DK2Evidence/performance/candidate-2',
               'F:/DK2Evidence/performance/candidate-3')
python tools/tester-package/compare_captures.py --baseline @baseline --candidate @candidate
```

The wrapper records three candidate runs serially in a unique output directory
and writes `comparison.json` there. It uses the supplied reviewed baseline
paths and returns the comparison's exit code. It does not replace the baseline.
Rebuild the package to include the current `capture.ps1`: its structured
`player-arguments.json` preserves arguments containing spaces. The checker
prefers it and falls back to legacy `player-arguments.txt`; old captures whose
unquoted paths contain spaces need fresh captures from the updated package.
