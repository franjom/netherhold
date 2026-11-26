# Tester package

A zip someone on other hardware can run without this repository, Godot or our
paths, that comes back as files we can read (docs/PERFORMANCE.md, the desktop
tier). It holds a release export of the Godot game, the pinned PresentMon,
`capture.ps1` and its instructions (`README.txt`), with `LICENSE.txt` and
`THIRD-PARTY-NOTICES.txt`.

```powershell
# Build and zip the package:
& tools/tester-package/build.ps1                     # -> build/tester-package/dk2-tester-<stamp>.zip
# Read a capture that came back (unzip it first):
python tools/tester-package/summarise_capture.py <capture folder> --tier desktop
```

`build.ps1` runs `godot/setup.ps1 -ExportTemplates` (the pinned editor and its
.NET export templates, both checked by SHA-512 against `godot/godot-version.json`
and the release's own checksum file), then exports `godot/DK2.Godot` headless
with the committed preset (`export_presets.cfg`, *Windows Desktop*) and a
temporary log. The export is `game/DK2.exe`, `game/DK2.pck` beside it (not
embedded, so the pack can be listed and hashed on its own) and the .NET folder
`game/data_DK2.Godot_windows_x86_64`. Godot exits 0 even when the .NET part of
an export fails, so the script also fails on an `ERROR:` line in the log or a
missing assembly. `pck_contents.py` then lists the pack and fails the build if
it holds anything that could be DK2 material (DK2's formats, any image, sound
or movie, a font other than the launcher's, a `Fonts/` path); the preset leaves
out `addons/` (agent tooling), `Fonts/` (the gitignored DungeonKeeperII.ttf) and
the probe scenes, and the game reads every DK2 file from the player's install.
PresentMon 2.6.0 is verified and copied by
`tools/tester-package/install-presentmon.ps1` (hash and Intel's
signature). `manifest.txt` names the commit, whether the worktree was clean,
the Godot version and the SHA256 of `DK2.exe`, `DK2.pck`, our assemblies and
GodotSharp, and one hash over every file of the .NET folder.
`build.ps1 -Development` exports with the debug template instead, for
diagnosis; `summarise_capture.py` rates its captures invalid.

The tester runs `capture.ps1 -Install <their DK2>`. It records the machine
(`machine.txt`: CPU, GPU with driver version and date, display mode, Windows),
starts PresentMon on `DK2.exe`, and runs the game with `--level Level6a --seed 1`
(an exported game cannot name a scene, so the main scene starts a `--level` run
in the level's), `--dk2Install` (and `DK2_DIR` for that process),
`--record-frames`, the performance harness (`--perf-report`, and `--perf-camera`
for the camera after the opening fly-by, keyed by frame), the pointer held at
the centre by `--pointer-path`, and its own profile, settings, saves, hi-scores
and launcher file under `player-files/` in the capture folder, so the player's
own are never read or written. Full screen takes the desktop's size;
`-Windowed` renders at `-Width` x `-Height` (`--perf-size`). V-Sync is off unless
`-VSync 1`; `-FrameCap` sets `--max-fps`. It waits the run out hands-off (the
level's opening fly-by plays on its own), takes a screenshot after the timed
interval (the screen, or only the game's window when windowed), closes the
game's window (which is when the recorder writes `recorder/frames.csv` and
`metadata.txt` and the harness `perf-report.json`, see godot/README.md,
*Probes*) and zips the folder with `godot.log`. PresentMon needs an elevated
PowerShell; without one the script warns and the capture has the recorder's
frames only.

`summarise_capture.py` judges the capture against a tier of the Godot targets,
`performance-target.json` beside it (tiers, warm-up, draw-clock limit, and the
harness ceilings `godot/perf_targets.py` reads; each number names its source):
every frame after two seconds of play within the tier's budget, by the
recorder's intervals and by PresentMon's presents when traced; the opening
fly-by's draw clock by `tools/perf/draw_clock.py`; and the ten worst
frames with what each spent on the tick, Present, the animated frame and chunk
rebuilds. A debug export, another backend than the Godot .NET export, a renderer
other than Vulkan or Direct3D 12, another resolution, metadata without a CPU and
GPU (a headless run), or an error line in `godot.log` (a display that is off
logs Vulkan error -13 every frame and presents nothing) makes the capture
invalid rather than failed. Exit 0 pass, 1 fail, 2 invalid. Captures from the
Unity player are no longer read.

What a capture cannot do yet: play the dig burst (no scripted digging in a
release run), or certify against `frame_budget.py`, whose capture shape is the
Level2 portal introduction probe's. Evidence from another machine goes to
`F:\DK2Evidence\`.

## Local rendered regression gate

Budgets, evidence requirements and baseline policy live in
[PERFORMANCE.md](../../docs/PERFORMANCE.md#regression-guards). First commission
three reviewed current-build baseline captures with the settings below, using
distinct output folders; review the logs and rendered content before retaining
them as the baseline. Keep captures and reports under `F:\DK2Evidence\` add-only.

```powershell
# In an elevated PowerShell, capture each baseline separately (repeat with new output paths):
& 'F:/DK2Evidence/packages/current/capture.ps1' -Install 'C:/Games/Dungeon Keeper 2' `
    -Level Level6a -Seed 1 -Seconds 40 -Width 1920 -Height 1080 -Windowed `
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
The checker reads Godot captures only (the summariser's release checks, the
`DK2.Godot.dll` hash in the manifest, a clean `godot.log`); it prefers the
structured `player-arguments.json` and falls back to `player-arguments.txt`,
ignoring the arguments that name the capture's own paths.
