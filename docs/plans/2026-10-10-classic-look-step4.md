# Classic look step 4: creature playback

Execute step 4 of 2026-10-09-classic-look.md in the existing classic worktree,
branch classic-look/step4, following the owner's step-3 merge authorization.
The current playback batch is merged to master. Preserve local Unity editor state.

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

The owner's original-game experiment reopened Q582 (2026-10-11); its current
finding is in combat-acquisition.md §1/§6. Presentation changes remain separate from the researched
eating action boundary (ADR-0432).

Progress: supported creature and object playback, action restarts, queued
transitions, frozen holds and physical pose interpolation are built
(ADR-0431–0438). The phase-to-frame link and render-factor clock producer are
confirmed; see animation-transitions.md. Q443 is settled for classic clip
playback by retaining each clip's topology and UVs (ADR-0431).
The alternate clock's activation remains unknown; no supported simulation
state supplies it. Broader status/action selection remains outside this batch;
see kmf-animation-playback.md. Broader Q582 casualty parity remains open.
Actual Unity development/release crowd captures and representation comparison
are complete; retain validated CPU playback under ADR-0438.
Procedure and measurements: tools/classic-unity-probe/README.md.
The full Release suite passes without skips, all 60 Level 1–3 replays validate, and Unity
sync/adapter compile and independent review pass. Owner visual acceptance is
partial; the checked and remaining cases are recorded in HANDOFF.md.
