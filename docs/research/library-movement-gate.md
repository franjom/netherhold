---
systems: [creatures, navigation, rooms, simulation-core]
---
# Library movement after a state handover

## Rule and missing coverage

STRONGLY INFERRED: the ordinary non-Imp steering call in
[Creature_Update](functions/00488930.md) reads the current state after the
handler. Its bit 0 must be set; a clear bit skips steering without clearing
the stored route or steering velocity. Fresh tail decompilation and
0048A204–0048A248 disassembly retain that reading. The branch has no neutral
ownership condition.

[State 62](functions/00476320.md) resumes the Library chooser, state 53, after
age 1 when still on its keeper's Library. Therefore the following mover tests
53's clear movement bit on that same turn, rather than 62's set bit.
The [shelf handler](functions/00475D20.md) later draws its existing random(7)
when facing finishes. No new research RNG rule follows.

Hash/save 445 extends the already built state/speed gate from neutral
non-Imps to the Library states recognized by `ResearchStep.IsResearchState`.
The current state's flag remains decisive: 53, 54 and 56 hold; 57, 59, 60 and 62
retain their movement-enabled behavior.
These caller families are an implementation boundary, not a native predicate.
Other mover adapters and the general owned-creature stop route remain partial.
See ADR-0713.

## Native boundary and controlled result

CONFIRMED for native Level 5 seed 1: Warlock 438 turns in state 62 at 1518–1519,
then resumes 53 at 1520 without moving from 18332,129047. The old replay made
an extra step to 18457,128872 during 53. Its later Library approach diverged:
native resumes 54 at 1549, faces at 1550, and begins action 29 in 56 at 1551;
the old replay remains travelling at 1551. The new comparison removes that
earlier extra step and restores the existing ResearchState.cpp:215 draw.

The final controlled replay matches 2527 ordered native draw sites and
preseeds through completed turn 1560, versus 2337 through 1550 before.
Actors 438, 439, 441, 444, 447, 449 and 455 each match 161/161 state/XY samples over
1400–1560. Heading matches all 161 for five; 438 first differs at 1425 and 444 at the
1400 restore frame. These are component checks, not full-body
or animation-clock parity.
This control stopped at 1561 draw 1, native RenderInfo.cpp:201 versus modern
Room.cpp:1563, preseed 2245879489; modern Imp 444/state 26 drew bound 25 for a
store. The [next control](possessed-excavation-clock.md) traced the missing
existing animation draw to possessed Imp excavation.

## Scope and evidence

Study: `F:/DK2Evidence/studies/q712-research1551--2026-11-07--001`.
The candidate `library-gate444/` isolates the additional Library scope;
`controlled445/` runs the final version. Both retain the preceding 24 input
keys and values, with no future state, heading, deadline or draw injection.
The same four diagnostic bootstrap clauses as
[the preceding comparison](imp-mine-arrival.md#scope-and-evidence) retain prior
behavior before 1400. Production applies the correction from startup.
This is a controlled post-restore comparison, not unconditional replay,
full-state or Heart-outcome parity.

## Verification at 445

`red-v2/` retains four failing regressions and four passing movement-enabled
controls against the prior implementation; all eight pass with the correction.
The state-62 handover case runs the actual Library return handler before the
mover and verifies that it uses the resulting state 53. `green/` passes 43
Library, neutral-route and determinism cases. The fixture pin is
`0x863B89D0EB9DFD9F`.

The full ten-project run passes 8552 cases, skips 30 and fails none; its raw
output is retained in `full/output.log`. `verification-summary.json` binds
the regression source identities and full totals. Final draw and component
receipts are `controlled445-draw-verification.json`, `native-proof.json` and
`heading-differences.json`.

Final build: `F:/DK2Evidence/builds/q712-research1551--2026-11-07--001`, with
`source.zip`, `payload/` and `manifest.json`. `source-input-proof.json` checks the
three Simulation changes from frozen 444, four bootstrap clauses and two
inherited Simulation observers. Tools extends only the two telemetry end
guards from 1550 to 1565. All 373 Simulation sources decode as UTF-8; the mover
overlay matches production bytes exactly after removing its existing clause.
