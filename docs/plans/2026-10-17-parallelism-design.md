# Parallelism for shorter, steadier frames

Status: draft roadmap, 2026-10-17; revised 2026-10-28 against
[the performance policy](../PERFORMANCE.md), ADR-0537 and the portal-arrival
research. Documentation only; no architecture change or implementation is
accepted by this proposal. Headless tick cost is a separate plan:
[simulation throughput](2026-10-28-simulation-throughput.md).

## Problem and target

The aim is **shorter, steadier frames**, rather than maximum CPU/GPU utilization.

Two targets apply:
- **Every frame within budget.** [The performance policy](../PERFORMANCE.md) sets
  60 FPS at 1920x1080 on the owner's laptop as the floor (16.67 ms per frame),
  aiming for 100+. ADR-0537 qualifies every CPU loop and presentation interval:
  one slow frame fails a capture, so an average or p95 cannot pass a change.
- **Even motion.** The latest Level8 motion capture has 2.72% uneven-timing
  frames against the owner's 1–1.5% target; this is not a count of visible
  freezes. Definition, conditions and evidence:
  [Level8 motion](../research/level8-intro-motion.md).

Tick duration: Release .NET runs of the keeper scenarios have an 8–13 ms
median tick (2026-10-28, measured for the throughput plan). Level8's opening
ticks, Unity Mono's tick durations and how often the join blocks remain
unmeasured. P0 must supply them before selecting an architectural change.

The portal-arrival captures (2026-10-25, [portal arrival
performance](../research/portal-arrival-performance.md)) show the join is one
suspect among several. Near one hitch a worker tick took 39 ms, and worker
blocking is STRONGLY INFERRED, not measured. In the same frames the main thread
spent about 50 ms creating a creature (28 ms of it a cold mesh build) and about
22 ms drawing effects.

Re-reading that capture (`build/level8-motion-final/motion.csv`, render ticks
11..24, 2026-10-20) finds 10 error frames in 333. None is long: the slowest is
10.9 ms against an 8.25 ms median. All lie within about two frames of a tick
boundary. Six stop exactly on it (draw clock clamped at fraction 1) and four
over-advance just after it (catch-up). In this capture the target is missed at
tick boundaries in the draw clock, not in blocked frames. Threads cannot fix that.
As of 2026-10-28 the draw clock has not changed since that capture, so this is
still open.

Today `DungeonRunner` runs sequential ticks on one worker and joins in
`WaitForTick` before presenting the mutable world (ADR-0458). A join can return
immediately when the task has finished; otherwise it blocks. Moving simulation
off-thread and fixing the draw clock (ADR-0501) do not guarantee steady frames.

At 4 ticks per second a tick starts on roughly one frame in 15 (60 FPS), 25
(100 FPS) or 36 (144 FPS). The join is the next frame's first act. A tick longer
than one frame therefore blocks that frame, which would give an error share of
about 3–7% by itself, and under ADR-0537 fails the capture outright. Whether it blocks depends on tick duration, so measure that first.

## P0 — Measure the slow frames and choose one change

- [ ] First, cheaply: the tick-duration distribution for the captured scenes
  (harness `RunClock` p50/p99/max in each run's `timings.txt`, built by the throughput plan's
  Task 0). The keeper scenarios are measured; Level8's opening is not.
  Then classify each error frame as boundary clamp, catch-up or long frame. Do
  this before building new instrumentation.
- [ ] Log per frame the ticks started (`maxTicksPerFrame` allows 2), the tick debt
  and the draw-clock fraction before and after clamping. A frame that starts two
  ticks after falling behind is slow even without a join wait.
- [ ] Repeat the motion capture at a 60 FPS cap. If join blocking is the cause,
  the error share should drop at 60 FPS, since a tick is more likely to finish
  within a 16.67 ms frame.
- [ ] Correlate worker queue delay, tick execution, main-thread join wait,
  presentation preparation, animation sample/upload, lighting, transparent sort,
  submission and GPU execution by frame and completed tick. The join wait has
  its own marker, `DK2.Simulation.WaitForTick` (`DungeonRunner.cs`). Record how
  often the join blocks, and its share of every frame over budget.
- [ ] Record what [the performance policy](../PERFORMANCE.md) requires (maximum
  frame interval, slow-frame count, longest slow sequence, p99), plus
  allocations/GC pauses, movement-endpoint holds and input latency. Separate
  startup from steady state.
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

**Exit:** captures that pass ADR-0537's per-frame gate where the joined path
failed, without incorrect output or a material input/display latency
regression. Otherwise retain the joined path.

## Later, only if P0 calls for it

- **P2:** select one measured presentation kernel, such as animation sampling.
  Before moving one to workers, try its serial reduction: for animation, the
  audit's C1 (`performance-audit-2026-10.md`) resamples and uploads every posed
  actor every frame, and skipping provably unchanged poses comes first.
  Give a kernel that still needs workers immutable captured inputs and disjoint outputs, coarse bounded batches
  and a serial fallback; include scheduling, conversion and upload in the result.
  This can precede P1 with a small safe input capture. Jobs/Burst remain adapter-only.
- **GPU:** not planned; revisit the existing [animation experiments](../../tools/classic-unity-probe/README.md)
  only if CPU preparation remains the bottleneck and classic fidelity is retained.
- **Simulation:** the tick itself stays serial. Shortening it, and the one
  overlap outside it, are in [simulation
  throughput](2026-10-28-simulation-throughput.md). Of that plan, the faster
  state hash also shortens Unity's tick while the input journal is on; the
  keeper-AI work does not, because Unity runs no computer keeper. A tick longer
  than one frame is a join-placement problem for P1.

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

Splitting the remainder (`ClassicMeshes.LoadBreakdown`, exclusive time per kind)
found `TerrainLattice.Invalidate` building each unbuilt cell through `Cell` only to
forget it: about 38,000 wasted cell builds at startup and 9 on every tile change.
After the fix, the remainder fell from 880–1870 ms to 383 ms and `StartGame` to
2.70 s. Present after the fix: decode 589, terrain 272, texture conversion 228,
animation 101, mesh 53, material 28, objects 15, other 383 ms.

The first 120 frames decoded 30 more models, adding about 70 ms.

Serial fixes after that: `FileAssetProvider` keeps each key's answer (the missing
art-pack checks were ~85 of the presenter's 110 ms in .NET), and `ToTexture`
copies bytes with `SetPixelData`. Duplicate KWD reads were left: all game data
loads in ~4 ms warm in .NET, so their player cost is first-run JIT, not I/O.

**Parallel art decode (2026-10-20).** `FirstFrameArt.Keys` lists what the first
`Present` asks for: creatures, objects, structures and torches through a fresh
`WorldView`, and `WorldView.TerrainArtKeys`, every piece set of each terrain on
the map (plus fog, scan and bridged liquid). On Level8 that is ~271 models and
~236 images against ~199 and ~225 used. The keys are read on the main thread,
because `WorldView` fills lazy caches on the world (`RoomSweepCache`, never
hashed; a test pins the state hash unchanged). `InstallArtProvider.Prefetch`
then decodes on up to 8 workers while the scene is built and the first tick
runs; `Present` waits for it. Archive reads are locked, decoding is not, and the
provider's caches keep the first value per name.

The keys are read before the first tick, which adds room furniture, open doors
and new creature clips (~45 ms of decode on Level8, left on request). Listing the
keys again after the tick cost about as much as it saved, so it was dropped.

One probe build, Level8, alternating `-prefetchArt 0/1`, three runs each:

| | Total `StartGame` | `Present` | On-request decode in `Present` |
|---|---:|---:|---:|
| Off | 1576 / 1599 / 1600 | 870 / 851 / 876 | 342–356 |
| On | 1374 / 1440 / 1380 | 519 / 580 / 517 | 51–54 |

The wait before `Present` was 0.1 ms: decoding was fully hidden. The net gain
is ~220 ms (~14%): listing the keys costs ~55 ms on the main thread and the
first tick ran ~35 ms slower beside the workers.

**Minimap.** Its build is mostly UI Toolkit creating its first panel
(`UIDocument` ~138 ms, `PanelSettings` and theme ~45 ms), which any UI pays once.
Its textures now come from the art provider's open archive: 49–77 ms → 3 ms.

**Classic HUD.** Its panels each opened the engine-texture archive (~13 ms warm in
.NET), `Text.str`, the fonts and the KWD art records again. `InstallHudSources`
opens them once (textures and art records through the art provider). Level8, two
runs each: the HUD's steps summed ~330 ms → ~170–182 ms; hover 37–45 → 7–10,
resources 38–53 → 7–9, status flowers 36–40 → 8–11, bottom panel 128–155 →
90–96 ms. The bottom panel's remainder is ~40 UI textures and its own panel.

**IL2CPP** is not measured: the Windows IL2CPP player module is not installed in
either Unity 6000.6.0f1 install (only Mono variations), and it needs the Visual
Studio C++ tools. Expected to remove most first-call JIT cost (data loading is
~4 ms warm in .NET against ~200–350 ms in the Mono player).

## Gates for any eventual implementation

Keep simulation Unity-free, integer-only and in the established processing order
([architecture](../ARCHITECTURE.md)). A stable merge at tick end does not preserve
what a creature reads after an earlier creature has written within that tick.
Do not alter RNG consumption, reservation order or shared-cache semantics.

No movement extrapolation, new clipping, animation shortcuts or silent extra tick
of interpolation delay. Preserve the shared camera/creature clock. Unchanged
simulation inputs must retain per-tick state, RNG and ordered events, including
save/load continuation; never re-pin a changed hash to accept parallelism.

Review one change at a time. Accept only captures that pass ADR-0537's per-frame
gate, repeatable frame-pacing gains above run variance, progress toward the canonical motion target and no material fidelity,
latency or memory regression. Run relevant regression suites and the adapter
build; visually compare terrain, combat, effects and attachments. Unity worker
contention and low-core machines belong in that change's checks, not a plan to
occupy every core.

API references checked through Context7: [job dependencies](https://docs.unity3d.com/Manual/job-system-creating-jobs.html),
[scripting backends](https://docs.unity3d.com/Manual/scripting-backends-intro.html),
[incremental GC](https://docs.unity3d.com/Manual/performance-incremental-garbage-collection.html).

## Addendum 2026-11-05: Level6a measurement answers P0 for that level

**Status (end of 2026-11-05):** the table and reading below are the baseline
taken before items 1 and 2 of the ranked order landed. Current numbers are in
those items: tick frames ~30 ms, Present 6.7 ms, AnimatedFrame 1.2 ms, tick
~13.4 ms. ADR-0454 and ADR-0456 are amended to match (commit 13a036ab).

Source: `tools/game-unity-probe`, Level6a, 90 s, headless Development
player, run `build/game-unity-probe/20261105-205627` (`frames.csv`,
`summary.txt`), measured by a parallel session and checked here. The owner
sees a stutter four times a second and about 30 FPS in the editor on Level6a.
The same probe at 7d47a14e, before the 2026-11-05 merges, gives the same
numbers (tick frames 70.3 ms, p95 54 ms), so the cost is structural.

| Measure | Value |
|---|---:|
| Frames after 2 s of play | 4189 |
| Frame p50 / p95 / p99 / max (ms) | 16.68 / 53.57 / 80.29 / 399.49 |
| Frames over 33 ms | 362, of which 352 carry a tick |
| Mean frame without a tick (ms) | 16.95 (Present 2.87, AnimatedFrame 0.99) |
| Mean frame with a tick (ms) | 65.35 |
| GC over 90 s | 1360 MB allocated, 97 gen-0 collections |

Tick-frame mean by marker (ms): `DK2.Simulation.Tick` 17.2, of which
`nav.refresh` 11.4; `DK2.Scene.Present` 21.1; `DK2.Scene.AnimatedFrame` 15.1
(about 1 ms on non-tick frames); chunk rebuild 4.3; imp job 1.6.

In the owner's editor the hitch line from commit c4749222 shows
`DK2.Simulation.WaitForTick` at 10–30 ms on tick frames and up to 83 ms during
the opening fly-over.

**Reading of the baseline.**

- The join blocked, and the cause is placement: `DungeonRunner` starts the tick
  on a worker at the end of a frame and joins at the start of the next
  `Update`, because that frame reads the live `World` (Present, sounds,
  flowers, minimap). The tick overlaps one render gap of 10–16 ms and a 17 ms
  tick cannot finish inside it. This is P1's cheap variant: let presentation
  read a snapshot so a tick can span the 250 ms turn.
- The join was not the main cost. Present plus AnimatedFrame on a tick frame
  (~36 ms) exceeded the tick itself, 7–15 times their non-tick cost. With
  the join at zero, tick frames would still have been over budget. That was
  serial presentation work on tick frames, not a threading problem (fixed by
  item 2).
- The worst frames were not the join. Frames 3567–3624 ran 170–400 ms with
  Present 67–128 ms and AnimatedFrame 112–144 ms, the portal-arrival pattern
  above (cold creature creation and mesh builds on the main thread). Two other
  spikes (frames 856, 2609) carry 13–14 MB of allocation and a gen-0 collection.
- Allocation was ~15 MB/s, now ~7.7 MB/s after items 1 and 2. The gates above
  say to remove it before parallelizing.

**Ranked order (the P0 exit for Level6a).**

1. Shorten the tick: `nav.refresh` was two thirds of it. Simulation-side, hash
   unchanged. Shrinks the join wait directly and does not touch P1's seam.
   **Landed 2026-11-05** as fa73ef5e (merge of d2150742): the refresh compares
   one packed key per tile (terrain, owner, RoomRuined, SecretDoor) instead of
   the whole 1 MB `TileState` array, about nine times a tick. Headless .NET,
   Level6a, 1200 ticks: `nav.refresh` total 321–435 ms → 176–206 ms,
   allocation 19.5 MB → 3.9 MB. Hash and pins unchanged. Not measured alone
   under Unity Mono; the item 2 reruns below, which include it, show the
   tick-frame `DK2.Simulation.Tick` mean at 13.3–13.5 ms against 17.2. A follow-up that adds a process-wide tile-write counter
   (perf/nav-refresh-epoch, 7288680a) was not merged: simulations share no
   state.
2. Cut Present and AnimatedFrame on tick frames to their non-tick cost: find
   why they are 7–15 times costlier when a tick completed, the cold mesh
   builds and the allocation sources. **Landed 2026-11-05** as 9377b14d (merge
   of 973c8a9a), inside the boundary: tick start, `WaitForTick` and the World
   the frame reads are untouched. Changes: an incremental tile pass that
   re-asks only tiles within 2 of a change (`IncrementalTiles=false` restores
   the full pass), local torch refresh, reused creature clip meshes, an
   AssetRef key cache and Mono allocation fixes. Same probe, Level6a, 90 s,
   runs `20261105-214219` and `-214441`:

   | Metric | Before | After |
   |---|---:|---:|
   | Tick-frame main thread, mean (ms) | 65.4 | 29.9 / 30.1 |
   | Tick-frame main thread, p95 (ms) | 127 | 35 |
   | Tick-frame Present (ms) | 21.1 | 6.7 |
   | Tick-frame AnimatedFrame (ms) | 15.1 | 1.2 |
   | `DK2.Simulation.Tick` on a tick frame (ms) | 17.2 | 13.3 / 13.5 |
   | p95, all frames (ms) | 53.6 | 26.5 / 26.9 |
   | p99, all frames (ms) | 80.3 | 31.7 / 32.1 |
   | Frames over 33 ms | 362 | 35 / 39 |
   | GC over 90 s | 1.36 GB, 97 gen-0 | 0.69 GB, 47–48 gen-0 |

   Left after this: the simulation tick is now the largest part of a tick
   frame (~13 ms of ~30), which is item 3's job to hide. The max frame
   (456–467 ms) is cold sound decode in `UpdateWorldSound` and speech: the
   first use of a category costs 194–650 ms and 27–67 MB in one frame (frames
   856 and 2609). That is outside P1's seam and is being fixed as a separate
   prefetch. The probe gained a `-Markers` option and
   `tools/game-unity-probe/report-capture.ps1`, a capture self-time report.
3. Then P1's cheap variant: skip the join and world presentation when the tick
   is not done, after the reader audit in P1. It conflicts with any branch
   restructuring the presenter, so run it after item 2 lands, not beside it.

Still unmeasured: Level8's opening tick durations, the error-frame
classification (boundary clamp, catch-up, long frame) for Level6a, and a 60 FPS
cap repeat. The Level6a numbers are a headless Development player on a shared
machine; a visible release confirmation per the P0 list is still owed.
