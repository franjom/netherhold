---
systems: [creatures, rooms, simulation-core]
---
# Bed heading draw

## Cause and correction

The [452 home-handover control](lair-home-handover.md) reaches 1617 with
matching Warlock 437 state/facing, but native draws Creature.cpp:2790 before
the first chicken roam. At hash 452, the modern runtime bed factory created
and linked the bed without sampling its heading. The apparent ordering mismatch is a missing
draw, not evidence that the creature/object sweeps need reordering.

STRONGLY INFERRED from whole rereads of [bed creation](functions/0048AD50.md)
and [registration](functions/004E03F0.md): successful object creation precedes
the heading draw; the creation effect follows it. The bound and native stream
are established in [furniture placement section 7](furniture-placement.md#7-beds-and-treasury-gold).
The [thing sweep](functions/004B7830.md) still walks complete creature updates
before the object list. No world-phase ordering changes.

CONFIRMED in the native Level 5 seed 1 capture: at completed turn 1617, 437's
bed handle is 1456 and that bed's heading is 2005. Preseed 1515096229 produces
that heading and advances to 1962582464. The runtime context factory now takes
that draw after successful object creation and stores it before requesting
the effect. The explicit world/database setup helper keeps its synthetic
placement adapter. No new saved fields; hash/save 454, ADR-0722.

## Updated-master control

Master commit 1a5be6f0 supplies ordinary
Imp action clocks at 453 ([clock rule](imp-claim-action-clock.md)). The copied
captured-order diagnostic host needs the same new claim Poll/Advance calls as
production. Its prior copy froze the newly restored animation clock.

The captured 1400 Claim 454 input already names action 4 and its phase/rate/flags.
A further captured-state input now restores the newly modeled action block:
independent target (18,10), slot 13, phase 3352, rate 1862, flags 5, count 0,
handler-done false and animation-complete true. These are the capture's
current fields, not a completion tick from a later frame. Whole rereads of
[the runner](functions/00496AA0.md) and [point wrapper](functions/00496980.md)
confirm the completion-bit mapping and independent point offsets.

Two diagnostic clauses preserve the old pre-1400 claim bootstrap; the strict
unknown-room/order guard remains. With the extra captured input and repaired
host, master 453 matches the same 3587 ordered sites/preseeds through 1616.
All prior 26 input values are retained; the new clock input makes 27.

## Verification and remaining boundary

The final controlled hash-454 replay matches 3816 ordered sites/preseeds through 1630,
versus 3587 through 1616 on the repaired 453 baseline. Next is 1631 draw 1:
native ObjectState.cpp:2851 versus modern Treasure.cpp:335, seed 2996395860,
modern bound 2048, Imp 455/state 29. This is the next lead, not a rule built here.

The four regression cases cover two creature beds with the captured heading
and exactly one seed advance, failed creation without a draw, and synthetic
setup without a runtime draw. With 454 metadata retained, old behavior fails
the two runtime cases and passes both controls; the correction passes all four.
The nearby selection passes 287 cases. The full Release suite passes 8636,
skips 30 and has zero failures across ten projects (`full454-v2`).
Pin: `0xE47C63A5DCD4F64F`.
The diagnostic has 22 bootstrap clauses: the prior 19, two for master's claim
bootstrap, and one for the new heading draw. Production has none of those gates.

Study: `F:/DK2Evidence/studies/q712-master453--2026-11-08--001`.
`controlled453-v4/` is the repaired baseline; `controlled454-trial/` retains
the initial correction. `controlled454/` is the final control.
`counterfactual454/result.json`, `source-input-proof454.json`,
`native-proof454.json` and `verification-summary454.json` bind the result.
The source proof compares against frozen 452, so its 23 changed paths include
the integrated master action clocks and diagnostic host changes. Stripping
the 22 bootstrap clauses leaves current production with two inherited
read-only Simulation observers; all prior 26 input values are unchanged.

Warlock 437 matches 124/124 state/XY/heading samples; creatures 438, 439 and 441
each match 231/231 in those components. Other component differences retain
the limits in `native-proof454.json`. Controlled draw agreement does not
establish full-state, unconditional replay or Heart-outcome parity.

Frozen source, executable payload and SHA-256 manifest:
`F:/DK2Evidence/builds/q712-bed-heading1617--2026-11-08--001`.
The archive retains the exact executed source; `post-documentation-comment454.json`
binds the approved Presentation comment-only correction in `delivered-change.zip`.
