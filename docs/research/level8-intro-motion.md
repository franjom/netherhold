---
systems: [campaign-triggers, camera-lighting]
---
# Level8 opening motion

## Reproduction

The 2026-10-17 capture follows authored Level8 seed 1 with worker ticks, speed 1,
fog off, ordinary cinematic paths and a 1280×720 standalone Development player.
`Level8MotionProbe` records the immutable creature poses, actual drawn roots,
viewport positions and render clock without reading the worker-owned world.
The first Lord moves smoothly at simulation-tick boundaries, yet the rendered
motion stutters at about 100 median FPS. Wizard contact is separate; see
[creature-neighbour-query.md](creature-neighbour-query.md).

## Timing correction (ADR-0501)

`Time.deltaTime` is sampled before the current worker wait and presentation
preparation. A long frame therefore advances the old draw clock too little,
then catches up on the following frame. The path also used the earlier fraction.
Elapsed time is now sampled after synchronization and after preparation, within
the same per-frame catchup budget. The camera seeks the final creature draw clock.

In the first Lord's straight walking interval, render ticks 11..24, define draw
clock error as `abs(deltaRenderTick / deltaWallSeconds - 4)`:

| Measure | Before | After |
|---|---:|---:|
| p95 error, ticks/second | 3.688 | 1.681 |
| Frames with error above 2 ticks/second | 11.44% | 2.72% |

These are single captures, not an FPS speedup claim. Remaining boundary clamping
and worker/presentation stalls remain measurable. The renderer does not extrapolate
beyond known creature positions. Owner visual comparison remains pending.
The 2.72% counts timing-error frames, not confirmed visible freezes. Further work
must attribute worker waits and presentation costs and measure tick availability;
these captures do not establish that every remaining pause can be eliminated.

Owner acceptance target (2026-10-17): at most 1–1.5% timing-error frames under
this metric and capture setup. Do not extrapolate creature movement to reach
that target; avoiding additional clipping takes priority. Reduce actual delays.

**Level6a fly-by, 2026-11-06 (master 6507d0eb).** The same metric from
`tools/game-unity-probe` (`-WorkerTicks`, 40 s, 60 FPS cap; `frames.csv` now
carries `render_tick`, the camera and `path`), run `build/game-unity-probe/20261106-073731`,
over the 1571 frames of Level6a's opening path: 12.17% of frames above 2
ticks/s (p95 error 3.48), with frame time flat at p95 16.85 ms. The error is a
three-frame step pattern once per tick (clamp at the boundary, catch-up,
short), so it is the boundary clamp, not a slow frame. Details:
[parallelism plan](../plans/2026-10-17-parallelism-design.md), addendum
2026-11-06.

**After ADR-0687** (the draw clock one frame behind the tick clock, one
elapsed sample per frame), same probe and path, run `20261106-075815`:

| Level6a opening path, ~1567 frames | Before | After |
|---|---:|---:|
| Frames with error above 2 ticks/s | 12.17% | 0.26% (4 frames) |
| p95 error, ticks/second | 3.48 | 0.24 |
| Frames holding at a boundary (tick step under half the median) | 102 | 0 |
| Frame time p95 (ms) | 16.85 | 17.02 |

The remaining four are the first frames, while the lag settles, and one long
frame. A repeat (run `-080042`) met machine interference, 31 frames over 33
ms, and read 2.62%, all catch-ups after long frames and no boundary hold.
Counting only steady frames (neither the frame nor the one before over 25
ms), the three runs read 12.03% before and 0.00% / 0.00% after. This meets
the 1–1.5% target for the draw clock on this path in a headless Development
player; what remains above 2 ticks/s is frame-time hitches, which the clock
cannot hide. The camera's own step per frame still varies
along the path, as an authored path accelerates and turns; that is not a
timing error and is not counted. Owner visual comparison in the editor is
still pending, and the Level8 capture has not been repeated.

Artifacts: `build/level8-motion-before/motion.csv` and
`build/level8-motion-final/motion.csv`; intermediate capture: `build/level8-motion-after/`.
The correction applies to all levels, not a Level8-specific rendering rule.
