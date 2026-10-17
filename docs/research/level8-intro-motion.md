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

Artifacts: `build/level8-motion-before/motion.csv` and
`build/level8-motion-final/motion.csv`; intermediate capture: `build/level8-motion-after/`.
The correction applies to all levels, not a Level8-specific rendering rule.
