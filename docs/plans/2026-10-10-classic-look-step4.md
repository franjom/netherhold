# Classic look step 4: creature playback

Execute step 4 of 2026-10-09-classic-look.md in the existing classic worktree,
branch classic-look/step4, following the owner's step-3 merge authorization.
Master contains step 3 at 6005ff9f. Preserve local Unity editor state.

1. Recheck the binary's fractional mesh evaluator and phase interpolation.
   Record findings in kmf-animation-playback.md and the function records.
2. Add engine-neutral fractional sampling of decoded KMF frames; test endpoints,
   inclusive loop boundaries, descriptor timing and independent instance buffers.
3. Measure a crowd's CPU sampling and frame storage. Choose the Unity playback
   representation from those measurements; preserve each clip's topology/UVs.
4. Wire playback to the researched creature clip choices and original timing.
   Audit generic activity mappings before enabling their clips; missing art must
   retain a useful pose. Add render interpolation without changing simulation.
5. Build/test, sync Unity, compile its adapter, review and commit. The owner gate
   remains side-by-side digging/claiming, walking, eating and sleeping.

Q582 is the highest behavior priority. A separate user-authorized agent traces
the Level7 Giant/Imp encounter. Presentation changes remain separate from the
researched eating action boundary (ADR-0432).

Progress: first playback batch implemented. Fractional sampler and descriptor
tests pass; adapter compiles. Review found and corrected a stale clip-start clock.
The next batch adds run/claim/reinforce mappings, the ordinary quantized clock
and separate Start/End meshes. Hatchery meals now retain action 26's wait and
restart their clip from the entry stamp. Remaining: prison eating,
movement rate changes, other repeated action restarts, queued transitions, editor
profiling and the owner visual gate. The primary phase-to-frame link is closed;
the render factor's game-clock producer remains open (Q442).
Q582 trace is recorded in combat-acquisition.md §1; it remains unresolved.
