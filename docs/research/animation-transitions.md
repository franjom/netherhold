# Animation replacement and render clock

Primary source: retail v1.70 `DKII.EXE`, Ghidra decompilation and instruction
checks, 2026-10-11. Presentation clock only; Q582's combat lifetime is untouched.

## 1. Replacement — CONFIRMED

`0x004B37D0` installs immediately when requested or when the current resource
is absent/completed. Otherwise it stores one pending resource, scalar and art
slot at animation-object `+0x2C`, `+0x8B`, `+0x8F`; a later request replaces
that pending request. It sets `+0xAD` bit 3, preventing the current main loop
from wrapping. Immediate installation clears the queue and resets both phases.

`0x004B3C10` plays the current resource's End segment, when present, before
installing the pending resource at its terminal boundary. The new resource
starts at phase zero; it receives no additional advance in that call. Its
Start segment and rate are initialized normally. No overshoot transfers
between different resources. Segment carries within one resource remain as
[kmf-animation-playback.md](kmf-animation-playback.md) describes.

`0x0048BE50` normally passes immediate installation for ordinary movement
and active actions. Its state-76 landing arm selects slot 20 immediately for
an evil worker (`IsWorker` and `IsEvil`), or slot 24 deferred otherwise, and
clears the loop bit for slot 24. These are catalogue indices 19 and 23.
Death is a separate chooser arm; landing does not authorize deferred death.
The chooser compares the pending resource/slot when a queue exists, avoiding
repeated identical requests. A new action with identical art must use its
action entry stamp when the action starter reinstalls it.

Built: `RenderAnimationClock.Replace` and the Unity player's pending resource
set retain the outgoing mesh until replacement, then switch topology/materials
with the installed clock. `ResourceRevision` identifies that installation.

## 2. Rate exceptions — CONFIRMED

Resource flag `0x80` sets the stop-loop gate during installation even if
looping was requested. Frozen timer `creature+0x447` bypasses advancement in
`0x0048BE50` and makes previous/current phases equal. The presentation clock
supports both gates; holding consumes render ticks, so thawing does not catch
up the elapsed frozen turns.

For slots 1/2, zero current speed makes the chooser reuse its stored scalar
once and then store scalar 1. Subsequent stationary updates therefore use 1,
rather than explicitly setting the advance rate to zero. Nonzero movement
uses the approximation documented in kmf-animation-playback.md §6.

The alternate-rate mode (`animation+0x4C` bit `0x20`) adds `+0xA4` instead of
the nominal `+0x98`, including when the nominal rate is zero. A scalar update
at or below the stored `+0x9C` leaves that override active; a higher scalar
clears it before recalculating. `Advance` accepts a normalized fixed-point
override. Full-section direct/embedded-field scans (`+0xA4`/`+0x148`) found
only reset-to-zero for this field. Flag scans (`+0x4C`/`+0x90`) found consumers
and clearing gates, but no activation. A computed setter or equivalent layout
remains UNKNOWN; no supported simulation state supplies an override. This
negative scan does not prove that the binary never activates it.

Possessed movement replaces body speed with the squared XY distance between
`creature+0x1A` and the position returned by virtual slot `+0x2C`
(`0x0048C2ED–0x0048C307`, `0x0043C260`). Creature vtable `0x0066D524`
slot `+0x2C` resolves to `0x004B5D40`, returning the previous position at
thing `+0xF6`. The scalar is the sum of each squared XY displacement
shifted right 12, rather than the creature definition's speed.
Status helpers `0x004B4580`, `0x004B45B0`, `0x004B4610` change drawing offsets
and flags, rather than setting the phase advance rate; see their records.

## 3. Render factor producer — CONFIRMED

`0x00526590` passes its record at session `+0x278` to the renderer's virtual
slot `+0x2C` (`0x00598880`). Record word 0 is the turn stamp; word 1 is
`floor(elapsedMilliseconds * 256 / floor(1000 / turnsPerSecond))`.
At four turns per second the divisor is 250 ms. The renderer divides word 1
by 256 before `0x00575A00` stores the factor used by the mesh drawer.
The producer is therefore quantized to 1/256, and can exceed 1 when late.
With no turn driver it writes zero; with session `+0x284` nonzero it retains
the previous factor instead of recalculating.

`0x00521C80` computes unsigned elapsed time from driver `+0xC`, capped at
`tickMilliseconds + 999`. `0x00522760` calls `0x00521CC0` after a due turn.
The latter advances the ideal driver stamp `+8` by one tick and updates
`+0xC` to `predicted + trunc((now - predicted) / 2)`, where predicted is
the previous `+0xC` plus one tick. This is a smoothed real-time baseline,
not simply the wall-clock time when the turn finished. The real-time getter
`0x005B5E90` delegates to `0x005B5E00`, which uses the performance counter
with `timeGetTime` as fallback.

Unity's fixed-step host supplies its own bounded interpolation fraction.
Reproducing DK2's late-frame pacing and smoothing belongs to host timing;
it must not change simulation ticks. Q442's producer trace is closed.
