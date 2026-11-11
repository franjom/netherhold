---
systems: [imps-tile-work]
---
# Claim, enemy-floor and room-conversion action clocks

## Finding

STRONGLY INFERRED from fresh DKII.EXE reads and original-function execution,
2026-11-07: the ordinary adapters for actions 4, 5 and 10 omit retained
animation state in baseline hash/save 452. This produces both application-spacing
and completion errors. ADR-0721 supplies the ordinary live-action clocks at 453;
a constant first-gap correction cannot reproduce all the tested inputs.

The external lead was [OpenKeeper PR 1021](https://github.com/tonihele/OpenKeeper/pull/1021)
and its prerequisite 933; the [PR comparison](openkeeper-1021-review.md) owns
the rest of that review. Their reported game measurements were not rerun here.

## Native mechanism

The [initializer](functions/004950C0.md) assigns actions 4, 5 and 10 to
art slot 13, mode 3, with no fixed duration. The shipped Imp slot is
`Imp_ClaimFloor`: 11 frames at 20 FPS, no start/end segments. At four ticks
per second its normalized rate is 1862, from the integer duration/reciprocal.

The [starter](functions/00496700.md) retains matching looping resource/slot
state. The [runner](functions/00496AA0.md) polls before state dispatch;
animation advancement follows it. Handler completion and animation completion
are separate, sticky conditions. A retained wrap flag can satisfy the latter
on the first poll of a later action; it does not shorten the authored clip.

- [Claim action 4](functions/004974C0.md) keeps applying work while eligible.
  After conversion it requests stop and remains busy until completion bit 1.
- [Enemy-floor action 5](functions/00497670.md) returns handler-done after a
  nonterminal hit. A terminal hit requests stop and waits for completion bit 1.
- [Room-conversion action 10](functions/00497BF0.md) likewise finishes its
  handler after a nonterminal hit. At takeover its tail accepts either cycle
  bit (mask 6), unlike actions 4/5's bit-1 test.

Consequently a fresh first-to-second attack/conversion gap is three ticks,
but retained phase can make it two, and a retained wrap flag can make it one.
The prior inference that one application per tick meant a one-tick room
animation was wrong: the retained cycle state explains that cadence.

## Executed controls

Tick 0 starts an action; its first handler poll is tick 1. Three animation
inputs were tested for each family: phase 0/flags 1 (loop), phase 2000/flags 1,
and phase 0/flags 5 (loop plus retained wrap). These are constructed inputs,
not observations of how often the game reaches each one.

| Case | Native applications | Native action ends | Baseline 452 applications | Baseline 452 work state exits |
|---|---|---|---|---|
| Claim, phase 0 | 1–5 | 7 | 1–5 | 7 |
| Claim, phase 2000 | 1–5 | 6 | 1–5 | 7 |
| Attack, phase 0 | 1, 4–26 | 27 | 1–24 | 25 |
| Attack, phase 2000 | 1, 3–25 | 26 | 1–24 | 25 |
| Attack, retained wrap | 1–24 | 25 | 1–24 | 25 |
| Convert room, phase 0 | 1, 4–25 | 25 | 1–23 | 24 |
| Convert room, phase 2000 | 1, 3–24 | 24 | 1–23 | 24 |
| Convert room, retained wrap | 1–23 | 23 | 1–23 | 24 |

The remaining claim/wrap control matches the phase-0 claim row. Application
amounts are +164 for floor claiming, -28 for floor attacking and -4000 for
neutral-room conversion. The compared jobs need five, 24 and 23 applications.
Native action ending and the baseline modern state exit are different boundaries:
the baseline conversion fallback notices completed work on its following update.

## Method, evidence and limits

Evidence and SHA-256-verified copies (41 files):
`F:/DK2Evidence/studies/imp-claim-clock--2026-11-07--002/`.
`native.json` contains all nine traces and oracle requests; `modern.json`
contains all nine real worker-system results. `manifest.json` names inputs.
The external bundle also retains the modern probe source, binary, runtime
configuration, dependency assemblies and PDBs. The originals remain in place.

The repeatable native probe is `tools/oracle/imp_claim_clock_probe.py`, using
the existing oracle helper and pinned retail executable. It runs the native
table initializer, action starter, handlers, runner and type-6 advance.
Eligibility, health application, room lookup, XP and sound dependencies are
synthetic stubs; no terrain, takeover, merge, sound or XP correctness follows
from this native fixture. The target routines themselves are not replaced.
Initial installed animation phase/rate/resource are supplied; this is not a
test of resource loading or the animation installer.

The baseline 452 modern probe runs ImpTaskSystem and its real work applier on the existing
test map, with slot-13 timing supplied from shipped metadata. It begins at
the first application boundary, not a complete walking/arrival trajectory.
The work-state entry is -2 and the initial work countdown is 1: completed-world
tick 1 executes context tick 0, corresponding to native poll 1 after an action
start at relative 0. PDB identity and SHA-256 document checks match five
relevant baseline source files, including WorkCadence and both worker adapters.
This is bounded source/build evidence, not a complete revision build proof.

The 2026-11-07 investigation ran no native capture, campaign replay, Unity run
or .NET test suite.
These results establish the supplied phase/cycle cases, not aggregate claiming
speed or campaign outcomes. The full-health fresh attack control supports the
PR's four-tick break-to-first-claim sequence through the already-read states;
the probe does not execute that complete state-dispatch sequence.

Source matching follows the [Portable PDB document checksum specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md)
and [PE/PDB identity and checksum specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PE-COFF.md).
The probe checks the full PDB identity, PDB content checksum and SHA-256
source checksums; a mismatch aborts the run.

## Implementation and remaining scope

ADR-0721/hash453 implements ordinary actions4/5/10 in ClaimActionRules using
the stored animation clock. The action point survives changes to the task;
the early poll applies work before combat/state validation, and advancement
follows the handlers. Handler completion and animation completion remain
independent, including a zero-rate poll with a counted cycle. The slot13 clip is
kept only while the Imp is in the work state; the entry state drops it (below, ADR-0746).
Accepted action replacement cancels the
old action; the [worker category's leave hook](functions/004647D0.md) ends it
before releasing its task. A paused state handler in possession still polls
the running action, including actions4/5's existing recharge multiplier.

The new state is hashed, copied and saved. Restored actions use those saved
clock inputs, not a completion date read from later frames. The nine native
timing expectations above pass through the real ordinary worker system.

Definitions without playable art13 timing retain WorkCadence; new possession
claim/attack input still uses its existing adapters. Resource-identity reuse,
general queued art replacement and full captured-action restoration remain
outside this change. The existing shared sound-start phase approximation is
retained; this is not a complete native action or animation layer.

## An attacked floor's claim starts its own clip (ADR-0746, hash/save 472)

Capture, Level 5 seed 1, Imp slot 440 (state `+0x25E`, action `+0x3A9`, animation phase
`+0xD8`, rate `+0xDC`, flags `+0xF1`; tile (16,38) has health 1 through 1789, then 186, 371,
556, 660 on 1790-1793):

| Turn | State | Action | Phase | Rate | Flags |
|---:|---:|---:|---:|---:|---:|
| 1784 | 20 | 5 | 1866 | 1862 | 5 |
| 1785 | same | same | 3728 | same | 13 |
| 1786 | same | same | 4096 | same | 15 |
| 1787 | 15 | 0 | 0 | 0 | 3 |
| 1788 | 16 | 0 | 0 | 0 | 3 |
| 1789 | 16 | 4 | 1862 | 1862 | 1 |
| 1790 | same | same | 3724 | same | 1 |
| 1791 | same | same | 1490 | same | 5 |
| 1792 | same | same | 3352 | same | 5 |
| 1793 | same | same | 4096 | same | 15 |
| 1794 | 10 | 0 | 0 | 0 | 3 |

The finished attack clip is not carried
into the claim: the Imp stands for two turns and action 4 begins at phase 0. Before ADR-0746 ours
held phase 4096, rate 0, flags 15 from 1786 through 1792 and left state 16 on 1793.

Binary reading (STRONGLY INFERRED, reviewer): the [runner](functions/00496AA0.md) clears only
the action block (`+0x38D`-`+0x3AE`) at an action's end, not the animation at `creature+0x44`;
`Creature_SetState` (`0x00476D30`) touches neither. [State 20](functions/00465F20.md), with no
action running and the tile neutral, calls SetState(15) and starts no action. State 15's handler
(`0x00465BC0`) checks tile and task and goes to 16 (`0x00465C5C`-`0x00465CB4`), starting no action;
states 19 (`0x00465E60`) and 21 (`0x004662E0`) start none either. The
[chooser](functions/0048BE50.md), called from `0x00488930` at `0x0048A4B5` after the action poll and
the state dispatch, reads the action id `+0x3A9` (`0x0048C3B1`, `0x0048C369`). With an action running
it takes the action's art slot through `0x00496DE0` (`0x0048C369`-`0x0048C3A6`,
`0x0048C45B`-`0x0048C469`, `0x0048C51C`-`0x0048C535`); otherwise the standing or movement slot
chosen on `+0x3BB` (`0x0048C3B1`-`0x0048C405`, `0x0048C46E`-`0x0048C47C`). At
`0x0048C584`-`0x0048C5B7` it compares resource and slot with the installed clip, and a different
slot goes on to install. So a turn that ends with no action running gets the standing clip.
Between two actions of one work state the handler restarts the action in the same turn, before
the chooser, so the [starter](functions/00496700.md) finds the slot-13 clip and keeps it.
Not read: the chooser's install call after `0x0048C637`, the meaning of `+0x3BB`, states 17 and 18.

Controlled replay (`F:/DK2Evidence/studies/q712-claim-clip--2026-11-11--001`, `controlled471`,
the hash-468 diagnostic source plus ADR-0745 and this change): 6905 ordered draws through turn
1798 (6831 through 1793 before). The boundary is turn 1799 draw 1, tracked in Q712.

## Implementation verification (2026-11-08)

The final solution build and Unity compile pass; Unity reports two existing
CS0618 warnings at DungeonRunner.Hud.cs:83. The full solution has 8653 passed,
9 skipped and 0 failed tests; Simulation has 5073 passed, and the focused
action-clock set has 30 passed. On archived baseline f177790b, the nine core
cases produce six expected failures and three passing controls. The complete
attack-to-claim handover regression also fails there: baseline break tick 24
becomes 26, followed by current claim hits 30–34 and state 10 at 36 (34 until ADR-0746).

Implementation evidence is retained separately from the native study:
`F:/DK2Evidence/studies/imp-claim-actions--2026-11-08--001/`.
Its manifest verifies 58 files by SHA-256, including ten final TRX files,
baseline failure TRXs, the unchanged 520-source baseline archive, exact tests,
changed source and binaries. No native gameplay capture was made for this change.
