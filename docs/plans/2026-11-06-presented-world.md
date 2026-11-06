# The presented world: the frame reads a copy, the tick runs beside it (P1)

**Date:** 2026-11-06. **Status:** proposed, awaiting the owner's decision. Continues
[the parallelism plan](2026-10-17-parallelism-design.md), whose P1 this is.

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

1. `CopyFrom` on `WorldState` and the state types it holds (`TileState` array copy,
   `PlayerState` with its lists, `CreatureState` (259 fields), `ObjectState`, `ShotState`,
   `ActionPointState`, the door and trap registries, hero parties, effect generators,
   temple, graveyards, tremors, room waves, pending work, messages, triggers, nav
   stamps, the id counters and the identity random), with the two hash tests. Two to
   three days; the field lists are long but mechanical, and the tests catch omissions.
2. `DungeonRunner`: `_presented` replaces `_simulation.World` for the frame; the swap
   replaces the join; the tick starts at the top of the frame when due; the worker's
   post-tick step copies. One day, with the reader audit as the checklist.
3. `WorkerDrawLag` takes the tick duration. Half a day with its tests.
4. Measure: the release recorder, uncapped and at 120, before and after, on the same
   package; the headless probe for the hash and the clean baselines; the fly-by's draw
   clock. Half a day.

Branch: `perf/presented-world`. Gates: the pinned state hash, the clean-baseline tables
in the parallelism plan, the draw-clock metric under 1.5%, and a release capture whose
frames over budget are no longer the join.

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
  the cap of 60 (6 waits). VSync 1 in a window did not hold the rate (150 FPS). A cap is
  therefore a 60 Hz default, not a fix for anything above it; P1 is.
