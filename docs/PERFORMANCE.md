# Performance acceptance

Owner requirement, revised 2026-10-25: minimum 60 FPS during gameplay on the
current laptop at 1920x1080 in a standalone build. Hardware, resolution, tiers, required
scenario coverage and the Godot harness ceilings are recorded in
[`performance-target.json`](../tools/tester-package/performance-target.json), each number with its
source.
The resulting frame budget is 1000/60 ms (about 16.67 ms). An average or percentile below it does
not excuse a slower frame.
The optimization goal remains 100+ FPS; meeting the 60 FPS floor does not end
performance work.
Desktop backend selection follows [ADR-0540](DECISIONS.md#adr-0540--prefer-vulkan-on-supported-desktop-platforms).
Selecting a backend does not certify the frame budget.

## Measurement and acceptance

- Capture real screen rendering in a release build, with the ordinary
  simulation, camera, lighting, effects and UI. Development captures diagnose
  markers; they cannot certify the release target.
- Include the first use of effects and assets. Level loading is measured
  separately; do not relabel a gameplay hitch as warmup. The arrival fixture
  freezes before introduction, not during it.
- Preserve CSV, CPU/GPU/API metadata, source and built-DLL SHA256 fingerprints,
  logs and a nonblank rendering proof. Screenshot encoding belongs outside
  the timed interval. Skipped rendering or incomplete presentation evidence
  invalidates acceptance.
- Record maximum frame interval, slow-frame count, longest slow-frame sequence,
  and p99. Use separate GPU diagnostics to investigate duration and waits;
  unavailable GPU data is never a zero-cost claim. Run comparisons serially and repeat
  release acceptance runs on the target machine. Report machine interference
  and failed runs; do not quietly remove slow samples.
- Verify visual ordering, lighting, blending, animation and effect content
  independently. A faster image with missing effects is a failed change.

Use `tools/tester-package/summarise_capture.py` to qualify current Godot release
captures. The regression checks run in CI without a GPU. Rendered acceptance
requires a capture on the target machine. See the [tester package](../tools/tester-package/README.md).

`tools/perf/frame_budget.py` evaluates legacy Level 2 portal-introduction captures
against the tier budget. It returns 0 for a passing capture, 1 for a budget
failure, 2 for invalid evidence, and 3 for a valid GPU diagnostic capture. The
current `compare_captures.py` also uses its screenshot validator.

## Coverage and current status

Godot measurements cover Level1, Level6a and Level7 below. Other required
scenarios still need rendered acceptance fixtures and results. A passing capture certifies only that capture, not the
whole game. New effects, renderers, shaders and gameplay workloads need
representative regression coverage before claiming the target is met.

The target is currently unmet. Arrival measurements and optimization evidence
have one home: portal arrival performance.
Engine-neutral cache measurements are in
Level6a Editor performance.

The headless simulation has its own opt-in budget
(50x real time, 100 ms per tick): [ADR-0575](DECISIONS.md), `GOAL_ALIGNMENT.md` gap 4.

## Regression guards

CI on Windows and Linux runs the allocation, state-copy, deferred-event and
worker draw-lag guards without a DK2 install, plus the tester capture checker
unit tests without a GPU. The steady-state budgets are zero managed bytes for
in-place world copying with unchanged population, concrete creature iteration
and cached `RoomAt` queries. Worker events must reach the frame thread only
on replay, in order and exactly once. With an installed game, the Level6a
seed-1 guard measures ticks 1501-1700 at at most 64 KiB per tick on average,
without the harness hash; CI skips this installed-data guard.

The local rendered regression gate uses at least three reviewed baseline
captures and three candidate captures, run serially on the same idle hardware
and settings: the release Godot export, Level6a, seed 1, 1920x1080, uncapped,
VSync off, windowed. Each capture lasts 40 wall seconds and must contain the
first 30 gameplay seconds. Analysis includes first use and the frame interval
crossing the 30-second endpoint; loading time does not replace gameplay time.
Commands are in the [tester README](../tools/tester-package/README.md#local-rendered-regression-gate).

The comparison checks frame p99 and maximum, share of frames over the floor
budget, tick p95, maximum chunk-rebuild time and GC collections per second.
It uses medians across runs except frame maximum, which uses the worst frame
across all runs. An increase fails above the larger of 20% or an absolute
allowance: 0.25 ms for timings, 0.001 for slow-frame share, or one extra GC
collection per analysis window. Independently, any candidate tick join over
1 ms or steady draw-clock bad-frame share over 0.015 fails.

Missing or inconsistent build manifests, hardware/settings metadata, player
arguments, clean player logs, nonblank desktop PNG, moving fly-by or contiguous
valid frame samples invalidate evidence. The desktop screenshot may differ in
size from the independently checked game resolution. Exit codes are 0 pass,
1 regression and 2 invalid evidence; the wrapper preserves `comparison.json`.

This adds local graphics regression machinery. No new real GPU baseline has
been captured or blessed, and no dedicated graphics runner or required remote
status is configured. Commission the baseline from three reviewed captures of
the current build, then pass their paths explicitly; the checker never blesses
a candidate automatically. Preserve captures and reports add-only under
`F:\DK2Evidence\` (AGENTS.md). A relative pass does not certify the absolute
FPS target or the remaining scenario coverage.

## Godot adapter

Changes to the Godot adapter are measured by its own harness: `--perf-report` and the recipe
`godot/perf.ps1`, compared with `godot/perf_compare.py` (`godot/README.md`, *Measuring performance*; migration plan
step 3o). It runs the editor binary with Debug C#, so its numbers compare changes to Godot, not against the floor;
the release export in the tester package (below) is what a capture against the floor or a tier runs.
The targets were set again on 2026-11-25 (migration plan, step 4): the tiers below for release captures, and per
level harness ceilings, the quiet medians at 1080p on the floor machine plus the regression gate's allowance, which
`godot/perf_targets.py` checks. The draw-clock metric reads Godot's `--record-frames` (`godot/README.md`, *Probes*).

Level6a's first release capture (2026-11-24) failed both tiers, and so did a re-run with the display presenting
(windowed 1920x1080, Vulkan, 40 s: floor 148 frames over budget, p99 24.0 ms, draw clock 19% of path frames). The
causes, all on the opening fly-by the owner saw stutter:

- **Pacing.** Godot's `physics_jitter_fix` bent the process delta toward its physics clock, and the frame ran its
  ticks after drawing, so each frame drew the last frame's time; the iGPU alternates short and long frames (3.3/6.5 ms).
  Fixed in `InGameHud`, which now ticks for the interval just ended and then draws. Draw clock
  19% to 0% of path frames; Movie Maker frames and the turn-400 hash unchanged.
- **Every frame.** `ScenePresenter` compared the whole tile map byte by byte each frame; it now uses the runtime's
  vectorised compare where there is one: present p50 1.03 to 0.68 ms, mean 1.40 to 0.97 ms.
- **The tick** (2026-11-25). A Level6a tick cost 16.6 ms p50 and 31-34 ms p95 in the release capture, 10 ms headless. Three
  causes. The export built `DK2.Simulation` and `DK2.Presentation` as `ExportRelease`, which the SDK leaves unoptimised
  (`DebuggableAttribute` 0x0107: the JIT never optimised them; `Directory.Build.props` now sets `Optimize`, TRAPS). Every
  room query read all of the map's tiles to check its cached sweep, and the structure mask and the navigation bytes did the
  same; each now compares one count of changes to a tile's terrain, owner, ruin bit or Secret Door bit
  (`TileState.KeyWriteCount`, `TileKeyCacheTests`, `TileKeyCachesOnRealDataTests`). And the 4 Hz ticks' methods stayed in
  tier 0 for seconds (runtime config in `DK2.Godot.csproj`, below). Headless Level6a ticks 1-200, seed 1, p50 / p95 ms
  (median of three rounds of eight runs after three warm-up runs, `GameSimulation.Tick` alone): as exported 10.08 / 28.8;
  optimised 1.69 / 3.61; room cache 0.84 / 2.80; structure mask and navigation check 0.72 / 2.67; trap lookup without a
  lock 0.70 / 2.72; exported now 0.76 / 2.75. Allocation stays 29 KB a tick (32.9 as exported). The state hashes of ten
  levels at five turns up to 3000, Level1 turn 400 (`F5C7BAE552173778`) and Level6a turn 400 are unchanged.
- **Release capture** (windowed 1920x1080, Vulkan, 40 s, two runs before and five after): tick p50 16.5 / 16.6 to
  2.9-3.2 ms, p95 31-34 to 7.5-7.7, max 92-100 to 10-11.5; frames over 16.67 ms 143 and 165 to 7-11, frame p99
  29.5-30.2 to 9.4-10.2 ms; after the warm-up the floor tier counts 135 and 151 frames over budget and then 2-4.
  The draw clock passes in all. No tick frame misses the budget now.
- **First use.** Level load now does the first-use work (`JitWarmUp`, `LevelWarmUp`; godot/README.md, *Measuring
  performance*): the first frame's update fell from 206-220 to 62-72 ms (lights 71-76 to 23-29, HUD 39-42 to 9-10), the
  first tick frame's from 127-129 to 25-27 ms (the tick 92-100 to 10-11), the first drawn frame's interval from 292-311 to
  167-181 ms. The load takes about 0.5 s longer. Still over the floor budget: that first draw (Godot's own), one render
  stall of 43-72 ms at 26.6 s when the path ends and the HUD first shows, and now and then a frame that rebuilds a chunk
  (18 ms). The floor tier still fails on them (2-4 frames), the desktop tier on 2-3% of frames over 8.33 ms.

`PackedSurface.Write` reuses the encoded normal and tangent words for consecutive bit-identical normals. A
packed-word probe matched the uncached encoders for 1,039,920 inputs. On the Level6a turn-200 save at 1920×1080
(D3D12, Debug C#), each run in three alternating fixed-delta pairs processed 177 packs and 6,155 surfaces.
Mean pack time fell from 2.130 to 1.780 ms (16.5%), and p95 fell from 3.966 to 3.311 ms. Across three real-time
pairs, whole-frame median p50 (10.75 to 10.82 ms) and p99 (30.16 to 30.49 ms) stayed within run-to-run variance.

When resuming a save, `CampaignLevelData.Load` reuses the simulation’s `GameDatabase` for presentation. Alternating
room lookups from creature rendering and the possession badge had used different database instances for the same
level, evicting the same per-world `RoomIndex` cache entry.
Three pairs of fixed-60 runs at 1920×1080 (D3D12, Debug C#) resumed the Level6a turn-200 save with seed 1.
Each run had 90 warm-up and 600 measured frames with zero dropped samples. Rebuilds fell from 1,198 per run
(599 per path) to zero.
The eliminated misses accounted for 499–546 ms and 29,940,416 B per run. All-thread managed allocations fell by
30,532,952–30,752,528 B. Frame p50 moved in both directions across the pairs.

## Hardware tiers and captures on other machines (2026-11-06)

The owner's laptop is the floor, not the goal. Revised targets, by tier:

| Tier | Hardware | Resolution | Minimum | Goal |
|---|---|---|---:|---:|
| floor | the owner's laptop (`performance-target.json`) | 1920x1080 | 60 FPS, every frame | 100+ |
| desktop | RTX 3060 class or better | 1920x1080 | 120 FPS, every frame | — |

A capture stands for the release only from the Godot .NET export with the release
template: a debug export or the editor's Debug C# measures something else, and the
recorder's metadata says which ran.

The desktop tier has no machine here. The tester package captures on other
hardware without this repository, Godot or the developer's install paths,
and returns files for review (ADR-0691):

1. **A tester package** (`tools/tester-package`, built by its `build.ps1`): one
   release export of the Godot game (Vulkan with the Direct3D12 fallback), the pinned
   PresentMon binary, and `capture.ps1`, which records the machine's
   CPU/GPU/driver/display, starts the trace, plays one campaign level with a
   fixed seed hands-off (its opening fly-by is the level's own camera path),
   stops the trace, takes a screenshot outside the timed interval and zips
   the result with `godot.log`, the game's own hardware metadata and the
   build's SHA256 manifest. `summarise_capture.py` judges the capture against
   a tier.
2. **Telemetry inside the release game** (`--record-frames <dir>`, `FrameTelemetry` and
   Godot's `FrameRecorder`, godot/README.md, *Probes*; ADR-0691): off by default, allocation-free on ordinary frames: per frame the
   interval, ticks run, the join wait, Present, the animated frame, chunk
   rebuilds, GC collections, the draw clock, the camera and whether a path
   plays, using stopwatch timestamps. Written at
   exit, with `perf-report.json` from the harness (`--perf-report`: GPU and render
   times, draw calls, allocation). `godot.log` is in the capture.
3. **The draw-clock evenness metric** (`tools/perf/draw_clock.py`,
   the Level8 motion metric) is in the same summary, since a fly-by can stutter
   at a flat 120 FPS. Level6a's release captures pass it since 2026-11-25 and fail
   both tiers on their frames (*Godot adapter* above).
4. **Scenario coverage**: the opening fly-by is covered; the dig burst
   (`terrain-edit-burst`) is not yet, since a release run has no scripted
   digging. The list in `performance-target.json` stands.

A desktop-tier number is still only as good as the capture behind it: a
tester's FPS counter is not one, and the policy above does not accept it. Evidence from another machine goes to
`F:\DK2Evidence\` like any capture that cannot be regenerated here.
Measurements so far, all on the owner's laptop and headless, are in the
[parallelism plan](plans/2026-10-17-parallelism-design.md), addendum 2026-11-06.
