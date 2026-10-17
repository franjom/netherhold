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

Today `DungeonRunner` runs sequential ticks on one worker and joins in
`WaitForTick` before presenting the mutable world (ADR-0458). A join can return
immediately when the task has finished; otherwise it blocks. Moving simulation
off-thread and fixing the draw clock (ADR-0501) do not guarantee steady frames.

## P0 — Measure the slow frames and choose one change

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
  using an external presentation-time capture and matched settings. Compare at
  least three runs per variant; report spread and probe overhead separately.

Files to start in: `DungeonRunner.cs`, `UnitySimulationProfiler.cs`, existing
animation/terrain markers and `tools/classic-unity-probe/`. Existing rendering
measurements and limits: [lighting verification](../research/classic-lighting-verification.md).

**Exit:** an attributed baseline and one ranked change with a predicted saving
on the critical path. If waiting is negligible, do not build P1. First remove
unnecessary work or allocations where that is cheaper than parallelization.

## P1 — Detach render data only if the join matters

Run this milestone only if P0 shows simulation join waits materially contributing
to p95 frames. Compare a small detached handoff against the current joined path;
include capture/copy cost, retained memory and added input/display latency.
An older snapshot can avoid blocking while still showing a pause.

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
- **Simulation:** stays serial unless P0 shows ticks exceeding the tested frame
  budget. Even then, further research and a separate reviewed proposal are required.

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
