# First dig: continuous work and release observations

2026-10-18, research only, modern gameplay hash/save 274. This extends the
[reservation controls](imp-hatchery-work.md#reservation-and-position-controls)
without changing priorities or accepting a gameplay candidate.

## Native sequence — CONFIRMED

A session-owned Level4 packet playback supplied 36 complete hardware
entry/return pairs of [the creature update](functions/00488930.md): workers
452, 454 and 455, in that order, on execution turns 159–170. All 72 records
share one process/thread. Return addresses are the world thing sweep's
`0x004B7854`, with the expected stack increment. One pre-159 memory image
contains 313651200 selected bytes and no short reads. Executable MD5 is
`b9709c6c9084d3060e4ac7ac9764c70b`. Only hardware debug registers were changed;
the original game continued between boundaries and was closed afterwards.

All dates here are execution timestamps, not snapshot headers or ordinary
modern post-turn labels; see [clock calibration](imp-observation-clocks.md).
The trace starts with all three diggers in state 14 and `(28,54)` still an
owned wall, health 354. It covers the remaining strikes, collapse, action
completion, task removal/creation and each digger's first mining choice.
It does not cover their earlier arrival or worker 453's later claim choice.

The action-clear dates previously predicted by the
[stop-clock experiment](imp-dig-stop-clock.md#evidence-and-local-cause)
are directly observed in these full updates. Native task entry 1 changes
from kind 1 to kind 14 during worker 454's update on 167; worker 455 then
releases its stale kind-1 id in the same turn, and 452 releases on the next.
A fresh [state-14 read](functions/004655D0.md) retains the action-running gate
before the id-and-kind task check. The native pool identity lifecycle is
observed here, not reconstructed by our tile flag.

## Bounded modern continuation — CONFIRMED results

Restore captured tiles, ordered marks and these three workers once, before
execution 159. Supply the previously observed work-entry stamps and remaining
hit timers 1/2/1. Those timers translate each next observed hit into our timer model;
they do not restore native action/animation blocks. Run the existing worker
updates in native order and the real tile-work resolver for fourteen ticks.
No later pose, RNG or state reset is applied. Movement, worker 453, other
creatures and general room/object simulation are omitted. Native RNG also
changes inside worker 454's first captured update; RNG-stream equality is
not established by this continuation.

| Control | Compared tile results | First worker-state mismatch |
|---|---|---|
| Hash 274, ordinary queue resolution | End-turn health first differs on 163: 42 versus native 68 | 454 on 167 |
| Extend only 454's first-hit gap from three to four | All 12 end-turn tile tuples match | Same |
| Same gap, resolve work after each worker | All 36 after-worker tile tuples match | Same |

Baseline opens the floor on 164; native and both gap controls open it on 165.
The immediate visibility difference starts on the first sampled worker update:
native health is 328 after worker 452's strike on 159, while the ordinary
modern queue still exposes 354 until the final work pass.

After matching the tile history at every sampled worker boundary, worker 454
still remains in state 14 on 167 while native enters 10. Modern releases it
on 170 and takes mining on 171, versus native 167/168. The other two diggers'
release and first-mine dates agree in this bounded continuation. The same
release mismatch persists with either queue schedule, so matching tile results
does not supply the missing action-completion timing.

The implementation boundaries are `WorkCadence.cs:28` (first gap),
`ImpTaskSystem.Work.cs:154` and `TileWorkSystem.cs:30` (deferred applications),
and `ImpTaskSystem.cs:281` / `WorkCadence.cs:53` (six-turn completion gate).
This joins the known cadence, visibility and release differences in one
controlled work sequence. It establishes neither a complete engine replay nor
a new fix: the earlier [action-stop candidate](imp-dig-stop-clock.md#candidate-and-acceptance)
fails campaign acceptance. Keep it isolated and leave the chooser unchanged.

## Reproduction and exclusions

Ignored artifacts under
`build/imp-acceptance/reverse-engineering/imp-continuous-dig-20261018/`:
`capture_live.py`, `live-elevated/boundaries.jsonl` and `start.mem.z` retain
the original update pairs and initial image. `export_input.py` imports the
existing decoder to write `input.json`; `Probe.csproj`/`Program.cs` use the
hash-274 assemblies and existing fixture loader. `verify.py` checks pair
coverage/returns, executable identity, tile comparisons and release/take dates.

The sandboxed launch faulted before capturing a pair. The completed capture
uses an unsandboxed session-owned child. Attempts to emulate full creature
updates and then a reduced poll/dispatch/advance sequence both faulted on an
unmapped fetch; neither sequence supplies evidence. No stubs or further retries
were added. No campaign run, gameplay source change or performance claim.
