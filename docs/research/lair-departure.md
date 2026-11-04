---
systems: [creatures, navigation]
---
# Lair departure point and arrival options

## Pre-edit mismatches (2026-11-03)

| Binary address | Hash 389 | Binary rule |
|---|---|---|
| 0046F418–0046F422 | LairSeeker routes with E0, body flags 12 | The lair attempt passes E8 to 00471A70, yielding body flags 13 |
| 00471AE8 / 004E4C10 | Final departure uses the centre-nearest LairRules.ArrivalTile without a draw | Outside the chosen room, draw one random slab-chain starting tile and use the existing room-point predicate/search |
| 00471AAA–00471AD2 | Routes to the centre before deciding whether a walk exists | Already inside the chosen room: state 1 then resume 80; no point draw, route or helper action cancellation; return the resume transition result |

These are STRONGLY INFERRED by independent binary reads. The bounded correction uses
the existing RoomWalkRules point picker and ExactSpot options. It preserves
the room finder's existing reachability, centre and tie approximations; it
does not establish parity for the entire lair search. State 80's local-candidate
retry is a separate E0 point route (0046F7C5-0046F7D3), already matched by
CreatureSleepSystem; it is retained.

## Captured input boundary

The actual 389 observation-only control retains five matching main output
hashes against its predecessor. Native Warlock 437 already has target
(11.5,31.5) and body flags 13 at completed 1464; the remake has (11.5,29.5)
and flags 12. The body window's final point and route distances differ before
the first visible entity-position difference on 1479. Earlier equal entity
poses therefore do not establish equal steering inputs. Native internal body
positions also retain finer precision than entity coordinates; this control
does not establish a generic integrator error.

Evidence under `F:/DK2Evidence/`:

- `studies/q712-integrated389-first-body--2026-11-03--001/`
- `builds/q712-integrated389-first-body--2026-11-03--001/`

This is the older atomic trajectory with named partial restore inputs, not
the later complete-recorder trajectory or the aggregate outcome cohort.
Q712 and Q719 remain open.

## Implementation and verification

ADR-0639, hash/save 390, uses the existing room-point picker and E8
for initial departure outside the chosen room. Inside it, idle-to-80 transitions
preserve the existing route, action and shared resume, and return the accepted
resume transition. The finder is deliberately unchanged; Q473's full effects
and finder approximations remain outside this bounded correction.

Five new address-backed cases pass and fail against unchanged archived hash-389
source (750 source files unchanged). Independent source/test/proof review approves
the correction. Full Simulation passes 4390 cases and Tools passes 111.
Hash/save 390 has pin `0xE03362E6067637E1`. Evidence under `F:/DK2Evidence/builds/`:
`q712-lair-departure-old389-proof--2026-11-03--001/` and
`q712-lair-departure-verification-h390--2026-11-03--001/`.

Final independent outcome/provenance review has no blocker. No aggregate
Q712 cause or full finder parity is established.

## Outcomes at hash 390

All 115 stored-pickup runs (75 Level 5, 40 Level 4) match the retained
367-template commands with two workers and exit 0. Four production-observer
main files match. Independent review verifies 701 Level 5, 374 Level 4,
784 measurement-build and 26 verification files; all 752 frozen source/config,
45 external and 25 installed input hashes remain unchanged. Solution and
Unity compile checks finish with zero warnings/errors.

389 is the immediate baseline. [Historical cohorts and values](reinforcement-action-clock.md#outcomes-at-hash-389)
retain native/362 comparisons. The historical 1401-3400 capture-header window
is separate from atomic completed-turn controls. These outcomes check the
binary-backed correction; no constants were tuned to scores.

All fifteen paired 389/390 transition rows and all fifteen exposure rows are
exactly equal. Strict dispatch radius about (17,26), seeds 2/3/7, means per run:

| Hero-ticks inside | DK2 | 362 historical | 389 baseline | 390 |
|---|---:|---:|---:|---:|
| All non-collapsed | 4327.00 | 2720.93 | 2830.73 | 2830.73 |
| Targeting Heart pillar | 3298.67 | 1607.00 | 1603.00 | 1603.00 |
| Targeting Imp | 790.00 | 790.60 | 903.07 | 903.07 |

| State-254 transition, mean/run | DK2 | 362 historical | 389 baseline | 390 |
|---|---:|---:|---:|---:|
| Entries | 15.00 | 12.87 | 14.73 | 14.73 |
| Exit to 199 | 3.67 | 0.60 | 1.13 | 1.13 |
| Exit to 203 | 5.00 | 2.60 | 2.00 | 2.00 |
| Exit to 1 | 3.00 | 4.67 | 4.47 | 4.47 |
| Exit to 6 | 2.00 | 4.40 | 6.53 | 6.53 |
| Exit to 8 | 1.00 | 0.53 | 0.60 | 0.60 |
| Fight plus flee (199+203) | 8.67 | 3.20 | 3.13 | 3.13 |

Per-seed entry controls, with the five generator runs in retained order:

| Native seed | DK2 entries | 389 and 390 entries |
|---|---:|---|
| 2 | 15 | 14, 30, 11, 30, 3 |
| 3 | 17 | 8, 7, 2, 25, 13 |
| 7 | 13 | 27, 15, 19, 10, 7 |

The remake range is 2-30 entries; its mean does not establish entry parity.
Fight/flee exits and pillar exposure retain the Q712 gap.

Death means use the original scorer and historical complete-state cohorts:

| Window / group, mean/run | DK2 | 362 historical | 389 baseline | 390 |
|---|---:|---:|---:|---:|
| L5 1401-3400 / keeper | 13.38 | 9.90 | 9.15 | 9.40 |
| L5 1401-3400 / keeper Imp | 11.12 | 7.35 | 6.575 | 6.50 |
| L5 1401-3400 / hero | 3.75 | 5.10 | 5.25 | 5.20 |
| L5 3401-5000 / keeper | 8.60 | 9.16 | 10.92 | 11.52 |
| L5 3401-5000 / keeper Imp | 5.80 | 7.20 | 9.00 | 9.60 |
| L5 3401-5000 / hero | 5.00 | 4.44 | 3.72 | 3.48 |
| L4 5001-6000 / keeper Imp, exact | 6.500 | 11.925 | 10.350 | 8.375 |

Q714 Heart/terminal restoration is unchanged. The outcome-selected 32-pair
intersection gives exact Level 4 means 10.03125/10.34375/8.78125 for
362/389/390; pre-terminal pair counts are 8/0/0. Against 389 the all-40 mean
falls by 1.975 and the selected mean by 1.5625. These descriptive subsets
establish neither causation nor per-death attribution to Q714. Q712/Q719
remain open, and Q734's confirmed XP-ordering gap is unimplemented in 390.

Evidence: `F:/DK2Evidence/studies/heart-defence-q712-h390-l5--2026-11-03--001/`
and its `l4` sibling; measurement build
`F:/DK2Evidence/builds/q712-lair-departure-measurement-h390--2026-11-03--001/`.
The verification bundle is linked above. The bounded Q157 departure correction
is complete; full finder parity remains Q473.
