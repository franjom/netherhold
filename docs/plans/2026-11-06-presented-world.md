# The presented world: the frame reads a copy, the tick runs beside it (P1)

**Date:** 2026-11-06. **Status:** built on `perf/presented-world` (ADR-0693, ADR-0694) and
measured; see "Result" at the end. Continues [the parallelism plan](2026-10-17-parallelism-design.md),
whose P1 this is.

## Why now

Above 60 FPS the worker tick no longer fits inside a frame. Measured with the release
recorder (ADR-0691) on the owner's laptop, Level6a's opening fly-by, windowed, uncapped:
frames are 5–6 ms, the worker tick 6.8 ms p50 and 14 ms p95, and every tick (135 of 136)
makes the next frame wait, 4 ms p50 and up to 19 ms. After ADR-0692 removed the HUD's
first-draw frame, 41 of the 44 frames over 16.67 ms in 40 s are that wait. That is the
stutter the owner sees four times a second on a 165 Hz display, and the desktop tier
(120 FPS, 8.3 ms frames) cannot be met with a 7–14 ms tick joined every frame.

The plan's cheap variant (skip the join and every world reader on a frame whose tick is
not done) does not survive the reader audit: the input block reads the world, and
Unity's `GetMouseButtonDown` is true only on the frame of the press, so a skipped frame
drops a click. At 150 FPS one frame in twenty is a tick frame.

## The design

Three `WorldState` instances, one `GameSimulation`:

- **live**: the simulation's own, written by the tick on the worker, read by nothing on
  the main thread.
- **presented**: what the frame reads. Every `_simulation.World` in `DungeonRunner` (72
  sites), every `Capture(WorldState)` in DK2.Presentation and `ScenePresenter.Present`
  take this one. Their signatures do not change: it is a `WorldState`.
- **spare**: the worker copies live into it at the end of each tick batch
  (`presented.CopyFrom(live)` in place, by id), then signals done.

The join becomes a swap: at the top of `Update`, if the worker signalled, presented and
spare exchange references and the next tick, when due, starts at once, on the live world,
before the frame does anything else. The frame then reads presented while the worker
ticks live. The main thread blocks only if a tick is still running when the *next* tick
is due, 250 ms later, which no measured tick approaches. The copy is on the worker, off
the frame entirely.

Identity is kept: `CopyFrom` updates the presented world's `CreatureState`, `ObjectState`,
`ShotState` and the rest by id, creating and removing as the live world did, so a view
that holds a reference across frames (`WorldView._portalPoses` holds `CreatureState`)
keeps a valid one. Events (`RoomTilesChanged`, `HeartHealthChanged`) stay on the live
world and fire on the worker as they do today.

### The copy is verified by the state hash

Every state type already hashes itself field by field (`HashInto`). `CopyFrom` is
written beside each `HashInto`, and two tests hold it to the hash:

1. After `CopyFrom`, `copy.StateHash() == live.StateHash()` on a level's state at several
   ticks (a field left out shows at once).
2. Ticking the copy and the live world the same N ticks with the same inputs gives the
   same hashes at every tick (a shared reference instead of a copy shows at once).

Everything the state hash covers is copied; the caches it does not cover
(`StructureCache`, `RoomSweepCache`, the navigation mesh in `GameSimulation`) are
rebuilt lazily on the presented side or, better, the two readers of navigation and
structure (the minimap's capture and the room sweep) move to the worker's post-tick
step, beside `CaptureObjectPositions` which is already there.

### The draw clock

Today the draw clock runs one frame behind the tick clock (ADR-0687) because the tick
starts at the end of the frame that owes it. With the tick starting at the top of the
frame and taking up to 14 ms, the clock must stay behind by at least the tick's wall
duration, or it reaches the boundary before the poses exist and clamps. `WorkerDrawLag`
takes the worker's tick duration (an EMA, as it keeps for the frame advance) and lags by
`max(frame, tick + frame)`: 15–20 ms of display latency instead of 6, invisible in a
dungeon view and cheaper than a 19 ms stall four times a second. The release recorder's
`tick_ms` and `render_tick` columns measure both before and after.

### What stays as it is

- `GameSimulation.Tick`, the systems, the hash: untouched. No hash change.
- The input journal: commands still go to the first tick of the next batch, stamped with
  the live tick inside `RunTicks`.
- The front end, cutscenes, the campaign map: single-threaded paths, unchanged.
- Editor Play mode: the same code; Mono's slower tick benefits most.

## Work, in order

1. **Done 2026-11-06 (ADR-0693).** `CopyFrom` on `WorldState` and the state types it
   holds, generated from the declarations by `tools/state-copy/generate.py` rather than
   written by hand, with `StateCopy.Sync` keeping instances by id. Verified three ways:
   the state hash at several ticks, a reflection walk over every field that also refuses
   a shared mutable reference, and a copy ticked under its own simulation
   (`GameSimulation.Resume`) beside the live one for 300 ticks on Level6a. The in-place
   copy costs 0.10 ms and no allocation at tick 1500 of Level6a (Release), against a
   1.45 ms tick. The identity random is not copied: the copy never spawns.
2. **Done (ADR-0694).** `World` replaces `_simulation.World` for the frame; the hand-over
   replaces the join and is a copy into one presented instance rather than a swap of two,
   since the views key caches and hold instances by world; the tick still starts at the end
   of the frame (earlier in wall time than the next frame's top); the worker's post-tick
   step copies into the spare. The object-position capture is double-buffered.
3. **Done.** `WorkerDrawLag` observes the batch's wall time (rising at once, decaying
   slowly) and the runner presents a batch when the draw clock reaches it, so the clock
   neither clamps at the boundary nor jumps past it.
4. **Done.** Below.

Branch: `perf/presented-world`. Gates: the pinned state hash, the clean-baseline tables
in the parallelism plan, the draw-clock metric under 1.5%, and a release capture whose
frames over budget are no longer the join.

## Why the windowed tick is slower than the headless one (measured 2026-11-06)

The same release player, Level6a, 60 FPS cap: headless (`build/game-unity-probe/20261106-115127`,
the probe built with `-Release -RecordFrames`) ticks at 4.2 ms p50 and 6.4 p95; in the game's
window (`build/tester-package/20261106-114342/captures/20261106-114714`) 5.3 p50 and 13.6 p95,
max 21 ms. So the build is not the difference; the window is, and it doubles the tail. What
was tried, each one build or capture, none of it the cause:

| Lever | Tick p50 / p95 at 60 | uncapped | Reading |
|---|---:|---:|---|
| Thread pool (before) | 5.4 / 13.5 | 8.7 / 15.7 | baseline, idle machine |
| Dedicated above-normal thread | 5.3 / 13.6 | 7.5 / 14.3 | ~1 ms uncapped, inside run-to-run variance; kept, since P1 wants a thread of its own |
| 1280x720 window | 5.0 / 12.2 | 6.8 / 14.1 | not memory bandwidth: the frame rate rose a quarter, the tick barely moved |
| Unity `-job-worker-count 4` | 5.6 / 12.9 | 7.4 / 14.8 | not Unity's job workers |
| Unity `-job-worker-count 2` | 5.3 / 13.2 | 8.7 / 15.3 | same; and the owner heard the sound run ahead of the picture in the capped run, with nothing in its log or frames, so the setting is not used |

The tick's time follows how busy the rest of the process is (headless < window at 60 < window at
120 < uncapped) and nothing else: not resolution, not the pool, not Unity's workers, not thread
priority. On this laptop's CPU that is the boost clock falling as more cores are active, which
no setting of ours changes. Under P1 a 5–9 ms tick is irrelevant: it only has to finish before
the next tick is due, 250 ms later. The input journal is off (`DK2_INPUT_JOURNAL` unset), the
diagnostics hash runs in both players.

## Alternatives considered

- **Per-reader snapshots** (each reader captures what it needs into its own struct):
  several already do (resources, message tabs, flowers, hover text), but Present's
  creature and object passes, the input and hover pick, lights and shadows read the
  world directly, and each would need its own copy with its own correctness argument.
  The whole-world copy has one argument, the hash, and keeps every signature.
- **Start the tick earlier in the frame, keep the join**: gains the few milliseconds of
  HUD and lights after Present; the tick is 7–14 ms against a 6 ms frame, so the wait
  remains.
- **A frame cap**: measured 2026-11-06 with the same release package, 40 s each,
  windowed (`build/tester-package/20261106-111653/captures/20261106-112858`, `-112944`,
  `-113031`, `-113117`: uncapped, cap 120, cap 60, uncapped with VSync 1). The tick's
  own time does not depend on the cap: p50 7.9 / 6.9 / 6.0 / 7.1 ms, p95 15.3 / 15.9 /
  13.7 / 15.1, max 21–23 in all four, so the windowed tick is slower than the headless
  4 ms for another reason than the power budget. The join waits stay at the cap of 120
  (127 waits over 1 ms, max 19.9 ms, 56 of the 63 frames over 16.67 ms) and go only at
  the cap of 60 (6 waits). VSync 1 in a window did not hold the rate (150 FPS). Repeated on an idle
  machine (`-113607`, `-113653`, `-113739`, `-113826`): tick p50 8.7 / 6.3 / 5.4 /
  7.6 ms, p95 15.6 / 15.1 / 13.2 / 16.7, waits over 1 ms 134 / 108 / 3 / 137, and the
  first round's three long stalls at the cap of 60 (2.7 s, 1.3 s, 156 ms, nothing in
  the game's columns) did not recur, so they were the machine. A cap is therefore a 60
  Hz default, not a fix for anything above it; P1 is.

## Result (measured 2026-11-06)

Same release package (`build/tester-package/20261106-125325`), Level6a, 40 s, windowed, idle
laptop, against the dedicated-thread build before P1 (`20261106-114342`):

| | uncapped before | after | cap 120 before | after | cap 60 before | after |
|---|---:|---:|---:|---:|---:|---:|
| FPS | 159 | 186 | 117 | 118 | 60 | 60 |
| frame p50 / p99 ms | 5.9 / 16.8 | 5.0 / 12.0 | 8.4 / 14.4 | 8.3 / 12.7 | 16.7 / 16.9 | 16.7 / 17.1 |
| frames over 16.67 ms | 56 | 6 | 26 | 6 | 966 | 930 |
| of them after a join | 54 | 0 | 24 | 0 | 4 | 0 |
| joins over 1 ms | 137 | 0 | 94 | 0 | 4 | 0 |
| tick p50 / p95 ms | 7.5 / 14.3 | 6.5 / 15.3 | 5.8 / 13.4 | 6.0 / 13.1 | 5.3 / 13.6 | 5.3 / 12.1 |

The gate held: the draw clock's steady share over the limit 0.17 % (limit 1.5 %), its worst error
23.5 → 9.1 ms; the headless probe at 60 reports no join and no change to the hash. What the worst
frames are now, uncapped: terrain chunk rebuilds inside Present (10–20 ms frames with 3–6 ms of
rebuild, four in the fly-by's first five seconds) and one 30 ms lights frame at 5.7 s. Those are
the next targets; the tick is no longer one of them above 60 FPS.

Follow-up the same day (ADR-0695): the chunk rebuild keeps its mesh (upload 1.24 → 0.42 ms, allocation
815 → 428 KB), the frame allocates 20 KB instead of 48 (`Enum.HasFlag` gone from `src/`, wall sides and
light tiles cached), and two races the presented world exposed are closed: the object-position capture
and the event sinks moved off the tick thread. The collection that lands badly (26–31 ms once a run)
remains; it needs the tick thread's garbage counted next. The 60 FPS cap needs nothing: 1737 of 2043
frames sit within 0.2 ms of 16.67 and only 33 exceed 17 ms, the limiter's own precision.
