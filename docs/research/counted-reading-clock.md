---
systems: [creatures, simulation-core]
---
# Counted reading clock

## Boundary and native evidence

Q712's production439 control matches 1041 ordered random sites and preseeds
through completed turn1474. At1475 modern Warlock439 draws7 for a new read
before native's first object draw; see [the preceding control](neutral-idle-heading.md).

CONFIRMED from the Level5 seed1 capture: Warlock439 begins action29, art14,
in state56 at1406 and reaches53 at1475. Its stored count is8. Start phase
advances by1462, main by529 and end by1396; completion appears at1473,
rate becomes0 at1474 and the action clears at1475. The shipped clip has
58 main frames,21 start frames,22 end frames and30fps, with both segments
flagged. The earlier calculator returned62 ticks; this fresh action lasts69.

STRONGLY INFERRED from fresh whole-body reads of
[the shelf state](functions/00475D20.md), [long read](functions/00476060.md),
[count initializer](functions/004969D0.md), [runner](functions/00496AA0.md)
and [animation advance](functions/004B3C10.md): the shelf draws7+7 on a
furnished slot; state56 waits for the action. Action29 is mode0 with no
handler, as recorded in [the action table](creature-action-table.md).
The stored byte count and cycle flags determine when Loop clears. Start/end
segments and integer phase carry contribute to the lifetime, and the final
completed phase needs another advance to clear the rate before polling ends it.
A nominal repeated-frame sum omits those effects.

## Implementation and limits

`CountedHoldTicks` now steps a fresh resolved base clip through the existing
integer advance and counted-runner rules. It supplies no captured completion
deadline, changes no world RNG and keeps the existing count0/1 path.
The shared advance extraction preserves the public live-clock entry point.
A thread-local scratch creature avoids allocating a large state record for
every duration query; every field the calculation uses is reset.

Hash/save440 changes timing without adding saved fields (ADR-0708).
This calculator does not restore a retained phase, choose an alternate art
resource or model handler work. Those require the live action clock. A shelf
state's failed action-start guard remains outside this timing correction.

## Controlled replay and verification

Evidence is add-only under
`F:/DK2Evidence/studies/q712-reading-clock--2026-11-07--001`.
`frozen439-reading` preserves the earlier62-tick result and telemetry.
The candidate inherits all22 restore input keys and values unchanged from the
heading control; no new input or later-capture deadline is supplied.
`delivered440-reading-v2-verification.json` verifies1510 ordered sites/preseeds
through completed1500, with the calculated69-tick hold. Next is1501 draw1:
native ObjectState.cpp:2851 versus modern Library.cpp:1068, preseed3043324754,
modern tag438/state1 searching Library capacity. Its movement already differs
at1440, so the next investigation starts before that draw.

`delivered440-reading-v2-components.json` verifies101/101 state/XY samples
(1400 through1500) each for Warlocks439 and441, neutrals447/449 and Imp455.
Creature438 has40/101 matching samples; its first XY difference is1440.
Draw-stream agreement therefore does not imply full-state agreement.

The install-independent regression has five meaningful pre-fix failures
(native69 and four final-rate-clear cases), with three unchanged-path controls
passing. Fixture-construction failures in earlier runs are excluded.
This remains bounded continuation, not full-state or Heart-outcome parity.

The final focused run passes 64 cases. The ten-project final suite passes
8518 cases with 30 skipped and zero failures; each project's TRX is named
`counted-reading-full-final.trx`. Unity compiles with zero errors and the two
existing obsolete-API warnings.

## Delivery and source scope

The final comment-refresh delivery repeats the prefix and component checks
above with all 22 input key/value pairs unchanged. Its four production
overlays match the executed source byte for byte. Source identity is recorded in
`source-input-proof-v2.json`; scripts are in `final-scripts-v2/`, test receipts
and summaries in `verification/`.

The matching build archive is
`F:/DK2Evidence/builds/q712-reading-clock--2026-11-07--001`, with
`source-v2.zip`, `payload-v2/`, `manifest-v2.json` and `frozen439-source.zip`.
The control inherits two observation-only differences from main:
ChickenMovementRules' Q712 marker/side event and ObjectMovementSystem's
marker wrapper. They are inherited probe instrumentation, not new gameplay
changes. Archive verification and integration are recorded in the external
integration receipt.
