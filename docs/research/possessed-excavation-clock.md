---
systems: [imps-tile-work, creatures, simulation-core]
---
# Possessed Imp excavation action clock

## Native action at 1561

CONFIRMED in the Level 5 seed 1 capture: possessed Imp 440 starts action 1
at 1561, targeting tile 29,51. Its slot-5 phase advances 1365,2730,4095;
the stopped clip enters End at 1564 with phase 2558/rate 2560, completes at 1565,
and the action clears at 1566. The handler-complete bit stays clear while the
collapsed wall's animation runs. Native draw 1 at 1561 is RenderInfo.cpp:201.
Modern 445 applied the dig through the possession queue without starting an
action or its clip, so its first draw was Imp 444's store chooser instead.

## Rule and implementation

STRONGLY INFERRED from the possession dispatcher
[00514830](functions/00514830.md), point wrapper
[00496980](functions/00496980.md), starter
[00496700](functions/00496700.md), runner
[00496AA0](functions/00496AA0.md) and excavation handlers
[00496E00](functions/00496E00.md)/[004971D0](functions/004971D0.md):
the press begins an ordinary action 1/2 at the faced tile's corner. The runner
strikes before the creature's fight/state work. A still-workable tile completes
the handler after one strike; otherwise it sets Stop and waits for the clip's
terminal flag. Animation completion is independent and sticky. The existing
[installer](functions/004B37D0.md) supplies the coin; no new random source is added.
Zero rate completes only the animation half; it does not replace the handler's
terminal-flag test.

Hash/save 446 adds the possessed Imp action kind, target tile, independent
animation completion and shared-clip ownership. It reuses the existing action
clock, polls in the common creature update and advances after the state/trigger
work. World copies retain its value state. Replacement cancels the action; a matching
unfinished requested slot is retained by the existing cache adapter. The new
action is reported by `CreatureCurrentAction` and survives snapshots independently of old AI job fields.
See ADR-0714.

## Scope and limits

Timed type 1 Imp dig/mine starts use this path. Other diggers, definitions without
timing, claiming and floor conversion retain their earlier adapters; the timing
fallback is not a native action-start refusal. Only fresh-install action sounds
are added; retained/wrapped sound requests remain partial. General action
resource-ID equality is not represented by the shared clock; see the starter
record. Generic action replacement/rendering and native restore completeness
remain partial.

Imp 440's old AI state is still restored as 22 where native holds 10 during
possession. This change does not inject a corrected state. Native idle/movement
art is represented as no shared action clip, not native slot 39.

## Controlled comparison

The final controlled replay matches 2774 ordered sites/preseeds through complete
turn 1575, versus 2527 through 1560 before. Newly exposed Hero 434's follow draw
at 1563 is the existing [00461F00](functions/00461F00.md) random 9 at
GoodState.cpp:253; mapping it is not a gameplay change.
Next: 1576 draw 1, native PlayerRooms.cpp:644 versus modern ObjectState.cpp:2851,
preseed 2605709186, modern Chicken 128 drawing 4. Identify the native room caller.

The preceding 24 input values are unchanged. The four inherited bootstrap
clauses plus one new before 1400 excavation fallback retain the earlier startup
history. Production has no tick gate. The native-order diagnostic wrapper now
invokes the same new common poll/advance hooks as production. Its earlier
`controlled446/` trial omitted those hooks and is excluded from mechanism claims.
No future state, facing, timer or draw is injected. This is a controlled
post-restore comparison, not unconditional production replay or Heart parity.

## Verification and evidence

The final 15 regression cases fail 9/pass 6 on the old behavior and pass 15 with
the fix, with identical test bytes and restored source hashes. The review-edge
controls separately fail 2/pass 1 before the copy/terminal-flag repairs.
The full Release suite passes 8567 with 30 skipped and zero failures across
10 projects. The remake fixture pin is `0x075852C6F00B405D`.

Seven actors each match 176/176 state/XY samples over 1400-1575. The
[two earlier heading exceptions](library-movement-gate.md#native-boundary-and-controlled-result)
remain unchanged. Imp 440's active action, point, slot,
phase, rate, flags, count and handler-complete bit match at 1561-1565; both
actions clear at 1566, within the art/state limits above.

Study: `F:/DK2Evidence/studies/q712-excavation1561--2026-11-07--001`.
`controlled446-v5/` and its draw receipt are final; `native-proof.json`,
`source-input-proof.json`, `verification-summary.json`, `counterfactual-v2/`
and `full-v3/` bind the comparison and regressions to their source.
`review-receipt.json` records the independent approval.
Frozen source/payload and SHA-256 manifest:
`F:/DK2Evidence/builds/q712-excavation1561--2026-11-07--001`.
