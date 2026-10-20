# Parallelism for shorter, steadier frames

Status: draft roadmap, 2026-10-17. Documentation only; no architecture change
or implementation is accepted by this proposal.

## Problem and target

The aim is **shorter, steadier frames**, rather than maximum CPU/GPU utilization.
Simulation parallelism needs particular care to preserve DK2's processing order
and determinism.

The latest Level8 motion capture has 2.72% uneven-timing frames against the
owner's 1–1.5% target; this is not a count of visible freezes. Definition,
conditions and evidence: [Level8 motion](../research/level8-intro-motion.md).
Its 144 FPS cap permits 6.94 ms per frame; 60 Hz permits 16.67 ms. ADR-0458's
historical tick median was about 15 ms, but that is not a current Level8 result.
Current opening tick durations, frame p95/p99 and join-blocking frequency remain
unmeasured. P0 must supply them before selecting an architectural change.

Re-reading that capture (`build/level8-motion-final/motion.csv`, render ticks
11..24, 2026-10-20) finds 10 error frames in 333. None is long: the slowest is
10.9 ms against an 8.25 ms median. All lie within about two frames of a tick
boundary. Six stop exactly on it (draw clock clamped at fraction 1) and four
over-advance just after it (catch-up). In this capture the target is missed at
tick boundaries in the draw clock, not in blocked frames. Threads cannot fix that.

Today `DungeonRunner` runs sequential ticks on one worker and joins in
`WaitForTick` before presenting the mutable world (ADR-0458). A join can return
immediately when the task has finished; otherwise it blocks. Moving simulation
off-thread and fixing the draw clock (ADR-0501) do not guarantee steady frames.

At 4 ticks per second a tick starts on roughly one frame in 25 (100 FPS) or 36
(144 FPS). The join is the next frame's first act. A tick longer than one frame
therefore blocks that frame, which would give an error share of about 3–4% by
itself. Whether it blocks depends on tick duration, so measure that first.

## P0 — Measure the slow frames and choose one change

- [ ] First, cheaply: the tick-duration distribution for the captured scenes
  (harness `RunClock` p50/p99/max). Then classify each error frame as boundary
  clamp, catch-up or long frame. Do this before building new instrumentation.
- [ ] Log per frame the ticks started (`maxTicksPerFrame` allows 2), the tick debt
  and the draw-clock fraction before and after clamping. A frame that starts two
  ticks after falling behind is slow even without a join wait.
- [ ] Repeat the motion capture at a 60 FPS cap. If join blocking is the cause,
  the error share should drop at 60 FPS, since a tick is more likely to finish
  within a 16.67 ms frame.
- [ ] Correlate worker queue delay, tick execution, main-thread join wait,
  presentation preparation, animation sample/upload, lighting, transparent sort,
  submission and GPU execution by frame and completed tick. Record how often
  the join blocks and its contribution specifically within the slowest 5% of frames.
- [ ] Record p50/p95/p99 frame time, missed frame deadlines, allocations/GC pauses,
  movement-endpoint holds and input latency. Separate startup from steady state.
- [ ] Capture the Level8 entrance, paused camera rotation and representative
  crowded gameplay with fixed seed, commands, camera, resolution and FPS cap.
  Log hardware, commit, Unity version, Mono/IL2CPP backend and incremental-GC mode.
- [ ] For .NET host allocation comparisons use `DOTNET_TieredCompilation=0`, as
  established in ADR-0466. This is not a Unity Mono/IL2CPP tuning switch. Keep
  warmup, backend and GC settings matched within each before/after comparison.
- [ ] Use instrumented Development captures for attribution. Confirm any claimed
  whole-frame gain in a visible release player with custom probes/recorders off,
  using an external presentation-time capture and matched settings. This machine
  is shared, and CPU times swing 30–45% between runs. The gap to close is about
  one percentage point, so three runs cannot resolve it. Size captures by error
  frames collected (e.g. ≥100 per variant, not frames or runs). Prefer
  alternating variants within one session, and report spread and probe overhead
  separately.

Files to start in: `DungeonRunner.cs`, `UnitySimulationProfiler.cs`, existing
animation/terrain markers and `tools/classic-unity-probe/`. Existing rendering
measurements and limits: [lighting verification](../research/classic-lighting-verification.md).

**Exit:** an attributed baseline and one ranked change with a predicted saving
on the critical path. If waiting is negligible, do not build P1. First remove
unnecessary work or allocations where that is cheaper than parallelization. If
error frames are boundary clamps or catch-up, as in the 2026-10-17 capture,
correct the draw clock at tick boundaries first. That is serial presentation
work without extrapolation, and it is outside this roadmap's threads.

## P1 — Detach render data only if the join matters

Run this milestone only if P0 shows simulation join waits materially contributing
to p95 frames. Compare a small detached handoff against the current joined path;
include capture/copy cost, retained memory and added input/display latency.
An older snapshot can avoid blocking while still showing a pause.

Try a cheaper variant first: when the tick task has not finished, skip the join
and the world presentation that frame. Keep camera, effects and creature drawing
from already-captured poses, and join on a later frame. Check first that frame's
readers. `DrawCreatureShadows` and `LightObjects` read the world, and the worker
writes `CaptureObjectPositions`. Creatures would also hold at the boundary pose,
which trades a blocked frame for a movement hold. Measure both.

Use **capture, compute, apply**: the simulation owner publishes completed data,
workers compute from it, and the main thread applies Unity object changes.

| Resource | Required ownership |
|---|---|
| World state and RNG | One simulation writer; no renderer reads during mutation |
| Render input | Coherent completed-tick poses and detached nested data, with tick/generation |
| Commands and events | Preserve input-to-tick assignment and exact event order; never drop, coalesce or reorder |
| Worker outputs | Exclusive buffers; apply only against matching camera/asset/publication revisions |
| Unity objects and pooled storage | Main-thread apply; no reuse or disposal while consumers still run |

- [ ] Audit frame readers, lazy caches and callbacks, including fog, shadows,
  attachments, camera paths and diagnostics. Do not copy `WorldSnapshot` each frame.
- [ ] Define bounded publication and event acknowledgement. Intermediate effects
  must survive superseded snapshots. Preserve existing command assignment to the
  first owed tick; explicitly define transfer if publication changes this boundary.
- [ ] Compare detached and live-world output serially before enabling concurrency.
  Exercise slow/failed workers, pause, catchup, load/reset and scene destruction.

**Exit:** lower measured tail latency without incorrect output or a material
input/display latency regression. Otherwise retain the joined path.

## Later, only if P0 calls for it

- **P2:** select one measured presentation kernel, such as animation sampling.
  Give it immutable captured inputs and disjoint outputs, coarse bounded batches
  and a serial fallback; include scheduling, conversion and upload in the result.
  This can precede P1 with a small safe input capture. Jobs/Burst remain adapter-only.
- **GPU:** not planned; revisit the existing [animation experiments](../../tools/classic-unity-probe/README.md)
  only if CPU preparation remains the bottleneck and classic fidelity is retained.
- **Simulation:** stays serial unless P0 shows ticks exceeding the 250 ms tick
  interval (4 ticks per second), at which point the game falls behind. A tick
  longer than one frame but shorter than that interval is a join-placement
  problem for P1, not grounds for parallel simulation. Even then, further
  research and a separate reviewed proposal are required.

## Startup loading (measured 2026-10-20)

Loading runs serially on the main thread inside `DungeonRunner.Start`. Art is
decoded lazily, but the first `Present` asks for the whole visible level.
`StartGame` now logs one `DK2 startup:` line per step, and a `DK2 first 120
frames:` line. One headless Development probe on Level8 (`game-unity-probe`,
seed 1, shared machine, no GPU upload) measured these times:

| Step | ms |
|---|---:|
| Database, level, art catalogue (file reads, many duplicated) | 519 |
| First tick | 267 |
| First `Present` | 2351 |
| — of which KMF and texture decode (199 models, 225 images) | 568 |
| — of which terrain chunk build | 606 |
| — remainder: Unity objects, texture conversion, presenter | ~1177 |
| Minimap | 328 |
| **Total `StartGame`** | **3655** |

The first 120 frames decoded 30 more models, adding about 70 ms. Parallel decode
can save at most the 568 ms, and the main-thread remainder is twice that. Next:
split the remainder, remove duplicate reads and the per-pixel texture copy, then
decode the first frame's art on workers ahead of `Present`. Each worker needs
its own archive stream; Unity objects stay on the main thread.

## Gates for any eventual implementation

Keep simulation Unity-free, integer-only and in the established processing order
([architecture](../ARCHITECTURE.md)). A stable merge at tick end does not preserve
what a creature reads after an earlier creature has written within that tick.
Do not alter RNG consumption, reservation order or shared-cache semantics.

No movement extrapolation, new clipping, animation shortcuts or silent extra tick
of interpolation delay. Preserve the shared camera/creature clock. Unchanged
simulation inputs must retain per-tick state, RNG and ordered events, including
save/load continuation; never re-pin a changed hash to accept parallelism.

Review one change at a time. Accept only repeatable frame-pacing gains above run
variance, progress toward the canonical motion target and no material fidelity,
latency or memory regression. Run relevant regression suites and the adapter
build; visually compare terrain, combat, effects and attachments. Unity worker
contention and low-core machines belong in that change's checks, not a plan to
occupy every core.

API references checked through Context7: [job dependencies](https://docs.unity3d.com/Manual/job-system-creating-jobs.html),
[scripting backends](https://docs.unity3d.com/Manual/scripting-backends-intro.html),
[incremental GC](https://docs.unity3d.com/Manual/performance-incremental-garbage-collection.html).
