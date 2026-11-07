---
systems: [imps-tile-work]
---
# Claim, enemy-floor and room-conversion action clocks

## Finding

STRONGLY INFERRED from fresh DKII.EXE reads and original-function execution,
2026-11-07: the ordinary adapters for actions 4, 5 and 10 omit retained
animation state. This produces both application-spacing and completion errors
in the current engine (hash/save 452). A constant first-gap correction cannot
reproduce all the tested inputs. No simulation behavior changed in this study.

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

| Case | Native applications | Native action ends | Modern applications | Modern work state exits |
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
Native action ending and the modern state exit are different boundaries:
the modern conversion fallback notices completed work on its following update.

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

The modern probe runs ImpTaskSystem and its real work applier on the existing
test map, with slot-13 timing supplied from shipped metadata. It begins at
the first application boundary, not a complete walking/arrival trajectory.
The work-state entry is -2 and the initial work countdown is 1: completed-world
tick 1 executes context tick 0, corresponding to native poll 1 after an action
start at relative 0. PDB identity and SHA-256 document checks match five
relevant current source files, including WorkCadence and both worker adapters.
This is bounded source/build evidence, not a complete revision build proof.

No native capture, campaign replay, Unity run or .NET test-suite run was made.
These results establish the supplied phase/cycle cases, not aggregate claiming
speed or campaign outcomes. The full-health fresh attack control supports the
PR's four-tick break-to-first-claim sequence through the already-read states;
the probe does not execute that complete state-dispatch sequence.

Source matching follows the [Portable PDB document checksum specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md)
and [PE/PDB identity and checksum specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PE-COFF.md).
The probe checks the full PDB identity, PDB content checksum and SHA-256
source checksums; a mismatch aborts the run.

## Implementation boundary

WorkCadence gives AttackTile and ConvertRoom a first gap of 1 and uses a
two-tick modulus for claim completion. WorkAnimation's stored loop applies
to digging/mining; reinforcement has its separate runner. Ordinary actions
4/5/10 need independent handler/animation completion, retained phase/cycle
state, their target points and polling before state validation. Preserve each
handler's distinct terminal return rule. Restored actions must use captured
clock inputs rather than a completion date read from later frames.

The next implementation should extend an evidenced live-action path for these
families and verify fresh, retained-phase, retained-wrap and terminal-hit cases.
Replacing the gap with 3 or fitting a fixed tile duration would retain known
errors in the controls above.
