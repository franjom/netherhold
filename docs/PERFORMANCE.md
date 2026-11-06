# Performance acceptance

Owner requirement, revised 2026-10-25: minimum 60 FPS during gameplay on the
current laptop at 1920x1080 in a standalone build. Hardware, resolution and required
scenario coverage are recorded in
[`performance-target.json`](../tools/classic-unity-probe/performance-target.json).
The resulting frame budget is 1000/60 ms (about 16.67 ms). An average or percentile below it does
not excuse a slower frame.
The optimization goal remains 100+ FPS; meeting the 60 FPS floor does not end
performance work.
Desktop backend selection follows [ADR-0540](DECISIONS.md#adr-0540--prefer-vulkan-on-supported-desktop-platforms).
Selecting a backend does not certify the frame budget.

## Measurement and acceptance

- Capture real screen rendering in a release player, with the ordinary
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

The local command is documented in
[the probe README](../tools/classic-unity-probe/README.md#portal-arrival-performance-research).
`frame_budget.py` defaults to lightweight FPS qualification: Unity Frame Timing
Stats and profiler recorders are disabled, and PresentMon captures presentation
intervals without GPU tracking. Every CPU loop and native presentation interval
must fit the budget. This checks application cadence, not physical display refresh.
GPU diagnostic mode cannot certify FPS acceptance. The checker returns 0 only
for a valid passing FPS capture, 1 for a budget failure, 2 for invalid evidence,
and 3 for a valid diagnostic capture.
Its regression tests run in CI without a GPU. CI does not run the actual
graphics benchmark; a green CI build is not a performance acceptance result.

## Coverage and current status

The implemented acceptance fixture is Level2's Warlock introduction. Other
entries in the target's required scenario list still need rendered acceptance
fixtures and results. A passing capture certifies only that capture, not the
whole game. New effects, renderers, shaders and gameplay workloads need
representative regression coverage before claiming the target is met.

The target is currently unmet. Arrival measurements and optimization evidence
have one home: [portal arrival performance](research/portal-arrival-performance.md).

This target is the Unity frame. The headless simulation has its own opt-in budget
(50x real time, 100 ms per tick): [ADR-0575](DECISIONS.md), `GOAL_ALIGNMENT.md` gap 4.

## Hardware tiers and captures on other machines (2026-11-06)

The owner's laptop is the floor, not the goal. Revised targets, by tier:

| Tier | Hardware | Resolution | Minimum | Goal |
|---|---|---|---:|---:|
| floor | the owner's laptop (`performance-target.json`) | 1920x1080 | 60 FPS, every frame | 100+ |
| desktop | RTX 3060 class or better | 1920x1080 | 120 FPS, every frame | — |

Players are built with IL2CPP (ADR-0690); a Mono player measures the editor's
runtime, not the game's, and the game probe says which backend a run used.

The desktop tier has no machine here. Someone else will run it, on hardware
we cannot see, so the capture must work without this repository, Unity or the
developer's install paths, and must come back as files we can read. Today it
cannot: `run-window-frames.ps1` needs the editor, a scratch project under this
clone's `build/`, PresentMon installed by our script and the DK2 install at
its path, and a release player records nothing about its own frames. What a
desktop-tier capture needs, and what exists (ADR-0691, 2026-11-06):

1. **A tester package** (`tools/tester-package`, built by its `build.ps1`): one
   release player (IL2CPP; Vulkan with the Direct3D12 fallback), the pinned
   PresentMon binary, and `capture.ps1`, which records the machine's
   CPU/GPU/driver/display, starts the trace, plays one campaign level with a
   fixed seed hands-off (its opening fly-by is the level's own camera path),
   stops the trace, takes a screenshot outside the timed interval and zips
   the result with `Player.log`, the player's own hardware metadata and the
   build's SHA256 manifest. `summarise_capture.py` judges the capture against
   a tier. `frame_budget.py` takes `--tier` as well, for the Level2 portal
   introduction capture its shape belongs to.
2. **Telemetry inside the release player** (`FrameRecorder`, unity/README.md):
   off by default, on by `-recordFrames [directory]` or a `record-frames` file
   beside the executable, allocation-free on ordinary frames: per frame the
   interval, ticks run, the join wait, Present, the animated frame, chunk
   rebuilds, GC collections, the draw clock, the camera and whether a path
   plays, in the game probe's columns, from stopwatches of its own since the
   profiler's recorders exist only in development players. Written at exit.
   The hitch lines (first-use art by kind) stay in `Player.log`, which the
   capture includes. Checked against the probe's recorders on the same run:
   Present, the animated frame and chunk rebuilds agree to 0.01 ms.
3. **The draw-clock evenness metric** (`tools/game-unity-probe/draw_clock.py`,
   the Level8 motion metric) is in the same summary, since a fly-by can stutter
   at a flat 120 FPS. The 2026-11-06 Level6a measurement failed it at 12% with
   frame time flat, before ADR-0687.
4. **Scenario coverage**: the opening fly-by is covered; the dig burst
   (`terrain-edit-burst`) is not yet, since a release player has no test
   controller to tag walls with. The list in `performance-target.json` stands.

A desktop-tier number is still only as good as the capture behind it: a
tester's FPS counter is not one, and the policy above does not accept it. Evidence from another machine goes to
`F:\DK2Evidence\` like any capture that cannot be regenerated here.
Measurements so far, all on the owner's laptop and headless, are in the
[parallelism plan](plans/2026-10-17-parallelism-design.md), addendum 2026-11-06.
