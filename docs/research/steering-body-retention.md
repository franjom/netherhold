---
systems: [navigation]
---
# Steering body retention between mover calls (Q735)

Binary readings of DKII.EXE v1.70, 2026-11-03. The decision is ADR-0644 (hash/save 392).
Function-level facts are in the records [004D59A0](functions/004D59A0.md),
[004D5AC0](functions/004D5AC0.md), [004D2B80](functions/004D2B80.md),
[00488930](functions/00488930.md) and [004B2030](functions/004B2030.md).

## Rule (CONFIRMED in the binary and in native frames)

A creature's steering body (creature `+0x124`) keeps the full 16.16 position its
last step left. State 6 (`00470CC0-00470D39`) and the general creature update
(`0048A250-0048A2DA`) run `004D5AF0`, then `004D59A0` (the creature's own body
set to entity `+0x1A/+0x1E` shifted up four, velocity shifted down then up four),
`004D5A40` (integrate), `004D5AC0` (body position shifted down four) and
`004B5560`/`004B5020` (entity only). Nothing writes the body back.

- The second `004D59A0` at `0048A2C7` (entity position, zero velocity) runs only
  when `004B35C0` rejects the step. A door contact's `0048A422` calls `004D5B30`,
  which zeroes the body velocity.
- A census of body `+0x1A8`/`+0x84` writers finds no per-turn sweep. The others
  are the impulse commit `004B270B` (only with a non-zero impulse), the
  constructor `004D55B0`, the reset `004D5B30` and possession (`0040F023`/`77`).
- `004D2B80` (Separation's push) reads both bodies' `+0x84` (creature `+0x1A8`).
  Its size-share product is an unshifted, wrapping multiply, so the push
  direction depends on those low four bits.

## Native frames: Level 5 seed 2, completed turn 1417

Imp 445 integrates before Imp 454 in the restored native list order. At frame
1416 native has 454's body at (1037061,774047), not entity shifted up four
(1037056,774032). Our unchanged steering arithmetic with the mover at entity
shifted up four and that native neighbour body reproduces native 445's velocity
(-30207,1088) and position (1014337,775088) exactly; the quantized neighbour
gives the old modern (-30209,417). The follow-path force input comes from the
matched modern call, so the single-sample reproduction is STRONGLY INFERRED
(the mechanism above is CONFIRMED). Evidence:
`F:/DK2Evidence/studies/q712-active-route-call-h391--2026-11-03--001/`.

## What ours did, and now does

Ours re-synced every body to entity shifted up four at turn start, in `Sync` and
after every step. Now `Advance` syncs the mover's own body just before it
integrates, a committed step keeps the 16.16 position, and the solid reject and
the door-contact path re-sync. `BeginTurn` and `Sync` re-sync a body only when
its position shifted down four differs from the entity (`SyncMovedBody`). That is
an adapter, not native: native leaves bodies stale while states 56/217/250 move
the entity (captured for up to 198 frames) and zeroes one at a door contact
(Q736). The body position is hashed (`SteeringBody.HashInto`) and saved
(`WorldSnapshot`, FormatVersion 392); the loader no longer re-syncs.

## Gated control

Frozen-391 active-route restoration ON, the change enabled only after the 1400
resync; pre-1401 replay rows and 7115 events are byte-identical to the 391
control. First XY divergence from native, 391 then 392:

| Creature | 391 | 392 |
|---|---:|---:|
| Imp 445 | 1417 | 1467 |
| Imp 454 | 1423 | 1465 |
| Imp 442 | 1579 | 1598 |
| Hero 436 XY | 1613 | 1626 |
| Hero 436 target / state | 1613 / 1613 | 1633 / 1723 |
| Imp 444 | 1499 | 1499 |

Evidence: `F:/DK2Evidence/studies/q712-body-lowbits-control-h391--2026-11-03--002/`.
The ungated `--001` sibling changed the pre-resync trajectory and Imp 444's
unrestored speed status; it is superseded.

## Next boundary (Q719)

Diagnosed in `F:/DK2Evidence/studies/q712-mover-call-lowbits2-h391--2026-11-03--001/`.
Imp 454's 1465 step differs because native Imp 455 stands on tile (19,19),
inside 454's Follow_Path crowd square (0.95 tile around the current window
point), while modern 455 is elsewhere. 455 and 446 diverge from 1401 because the
resync reissues their mining as a walk: native 1400 has 455 in state 25 (resume
1) and 446 in state 26; modern goes 6, 25, 26 with a (5,8)-unit nudge. The
worker-resume restore covers only native state 22. 454's route also differs
(modern target (1212400,1758528) with 8 points, native (1059488,1780384) with 9).
This is a partial-input boundary, not a rule defect. `WorldResync` copies only
the entity position, not body `+0x1A8`, so captured low bits are lost at a
resync (a tooling limit for matched controls).

## Verification

Solution build 0 warnings/0 errors. Simulation 4602, Tools 111, Data 515 and
Presentation 1560 pass. Behavior 510 pass with two failures
(`Level17HarkanCastsTheCall...`, `creatures/a_goblin_eats_a_chicken.yaml`) that fail
identically on unchanged 391 source. Hash/save 392 has pin `0x679DAB6F572FB5EC`
(391: `0xDD9891C0E400A8EA`).

`SteeringBodyRetentionTests` against unchanged `db2fff43` source plus the final
test file: three fail (`ACommittedStepLeavesTheBodysLowBits`,
`TheAdapterKeepsABodyStillOnItsEntityAtTheNextTurn`,
`TheRetainedPositionIsSavedAndHashed`) and three pass as controls
(`TheAdapterResyncsABodyWhoseEntityMoved`, both `CapturedNeighbourLowBitsDecideTheStep`
rows). Evidence: `F:/DK2Evidence/builds/q712-body-lowbits-old391-proof--2026-11-03--002/`.

Two existing tests changed with the rule. `ALevelTwoImpWalksAtTheLevelOneSpeed`
walks each imp alone, since two co-located imps push each other order-dependently.
`AnOvercrowdedPrisonBreaksOutAndEmptiesItself` pins 4 releases and 2 jail breaks:
a released prisoner still on the floor at the next once-a-second roll is counted
by `PrisonerCount` and released again; Freed stays exactly 3.

## Outcomes at hash 392

115 bounded stored-pickup resyncs (75 Level 5, 40 Level 4) from the frozen 392
source, archived 367 commands with executable/output paths replaced, two workers,
all exit 0; production observer control 4/4. Historical 1401-3400 header cohort,
means per run (391 was measured on pre-sound `c2b4398c`; its pin did not change
across the sound merge):

| Measure | DK2 | 391 | 392 |
|---|---:|---:|---:|
| State-254 entries | 15.00 | 17.27 | 15.33 |
| Exits to 199 | 3.67 | 1.13 | 1.00 |
| Exits to 203 | 5.00 | 2.20 | 2.27 |
| Exits to 1 | 3.00 | 5.53 | 4.40 |
| Exits to 6 | 2.00 | 7.47 | 6.87 |
| Exits to 8 | 1.00 | 0.87 | 0.67 |
| Hero ticks near Heart, all | 4327 | 2669.47 | 2934.67 |
| Hero ticks near Heart, targeting pillar | 3298.67 | 1432 | 1679.53 |
| Hero ticks near Heart, targeting Imp | 790 | 912.13 | 909.60 |

Keeper Imp deaths, mean per run (the scorer and cohorts of
[combat-experience-order.md](combat-experience-order.md#outcomes-at-hash-391)):

| Window | DK2 | 391 | 392 |
|---|---:|---:|---:|
| L5 resync 1400 | 11.12 | 5.525 | 5.90 |
| L5 resync 3400 | 5.80 | 10.04 | 5.80 |
| L4, all 40 | 6.50 | 7.775 | 9.925 |

Level 4 gets worse. The selected-32 descriptive subset is 362: 10.03125,
391: 7.96875, 392: 10.65625. Entry parity and the fight/flee exits are not
established; the change is a binary-backed input correction and no constant was
tuned. After the measurement only comments and test names changed in source.
Q712 stays open.

Evidence: `F:/DK2Evidence/builds/q712-body-lowbits-measurement-h392--2026-11-03--001/`
and the studies `heart-defence-q712-h392-l5--2026-11-03--001/` and its `l4`
sibling (manifests of 699, 372 and 812 files).
