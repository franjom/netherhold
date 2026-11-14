# Remembered Guard job implementation plan

**Goal:** implement the captured remembered-job priority and Guard assignment lifecycle.
**Design:** preserve a typed job and room reference independently of CurrentJob.
Resolve tracked rooms by their existing RoomListPosition; zero-position fixture
rooms use a saved tile. This is an adapter, not a native room-tag allocator.
**Evidence:** [Q710 controls](../research/q710-fighter-decisions.md), functions
004778E0, 00462850, 00513480 and 004E53B0. Native routes remain outside this task.
**Execution:** inline implementation, independent reviewer, then documenter.

- [x] Write behavioral regression tests and observe failures on current master:
  remembered Guard before a fresh roll, dropped-job priority, refusal retention,
  saved-room preference/invalidation, capacity reservation, pickup and persistence.
- [x] Add ReturnToJob and a typed room reference to CreatureState, hash and snapshot.
  Default is no memory; bump hash/save together and update the determinism pin.
- [x] Retry the remembered job after dropped-job refusal. Refusal retains memory.
  Successful new Guard routing saves the room/job; reuse preserves the reference.
  Saved-room failure falls through to the ordinary search. Cursor pickup clears
  both fields; direct keeper preparation/insertion preserves them.
- [x] Count every same-owner remembered Guard assignment to that room in capacity,
  including the requesting creature; do not silently exclude its reservation.
- [x] Run focused regressions, simulation and relevant consumer tests. Inspect
  changed APIs for source/Unity compatibility; no existing signatures are removed.
- [x] Update binary records in the same change, retain partial adapter limits,
  get actual reviewer and documenter passes, and run gates.

**Integration:** after committing these verified changes, the caller integrates
locally while preserving the owner's active checkout.

**Review focus:** room replacement/merge/split identity, rejected state entry,
full saved room and unreachable-route fallback, failed pickup/full hand timing,
snapshot/hash participation and persistence through ordinary ClearJob paths.
