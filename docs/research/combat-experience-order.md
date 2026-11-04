---
systems: [combat, creatures]
---
# Combat category experience order (Q734)

## Pre-edit mismatches (2026-11-03)

| Binary address | Hash 390 | Binary order |
|---|---|---|
| 004770E0 / 004772AA in 00476FC0 | Global experience sweep follows state handlers | Category update precedes the handler, so leaving 199/200 still earns that turn and arriving from 8 earns only on the next update |
| 0047A870 / 00457880 | Promotion is invisible to the current handler | The category's adder can promote before that handler reads the level |
| 004B7830 / 00476FC0 | Combat awards run after every creature and the room/player passes | One creature's category award completes before the next creature's update |

The fight scan at 00489AC7 precedes the dispatcher; acquisition into 199 can
therefore earn on the same update. Existing target, worker/Good, level-rate,
possession and inactive-creature gates are retained. Native Wait suspension
remains the separate Q499 boundary; no new suspend-field implementation is
claimed here.

## Captured control

The [partial input scope](q712-atomic-inputs.md#actual-hash-390-prefix-and-xp-lead-q719q734)
retains the older untorn native trajectory and actual 390 partial input restore.
Hero 436 matches state, target and entity XY over 1571-1589, with equal XP
through 1588. On 1589 both leave 199 for 1: native XP becomes 703, modern stays 666.
The independent binary read and paired raw rows establish this exit-award
ordering gap, not its aggregate Q712 contribution.

Evidence: `F:/DK2Evidence/studies/q712-combat-xp-boundary-h390--2026-11-03--001/`.

## Corrected control at hash 391

The independently reviewed actual-391 partial-input control retains 57 raw
frames. Hero 436 matches native state, target and XP on completed 1571-1626,
and entity XY on 1571-1625. Both exit 199 for 1 at 1589 with 703 XP, repairing
the missing exit-turn award. On 1627 native stays in 6 with target 0 and XP 703;
modern is in 199 targeting Imp 445 with XP 740. That later XP difference
accompanies unequal decisions/inputs; it does not establish another award error.

Evidence under `F:/DK2Evidence/studies/`:
`q712-integrated391-worker-resume--2026-11-03--001/` and
`q712-combat-xp-boundary-h391--2026-11-03--001/`; matching control build
`F:/DK2Evidence/builds/q712-integrated391-worker-control--2026-11-03--001/`.
Independent review verifies the 40/28/3-file control manifests and source
identities. The [modeled persistent-route intervention](q712-atomic-inputs.md#actual-hash-391-partial-inputs-q719)
remains a separate partial-input control; its shorter hero prefix is not
whole-world or full native-body parity.

## Bounded correction

ADR-0640, hash/save 391, uses the existing experience system as a per-creature
step after the scan and before travel/needs/handlers. Its default global-last registration is removed.
Standalone execution and all current award predicates are retained; custom hosts
remain responsible for declaring their steps in native order. Work-hit awards
and the adder's arithmetic are unchanged. No saved field is added.

## Verification

Independent source/test/proof review approves the bounded correction. All
17 focused cases pass, and full suites pass Simulation 4398 and Tools 111.
The eight new runtime-order cases give six failures and two passing controls
against unchanged archived hash-390 source; all 750 source files remain unchanged.
Solution and Unity builds finish with zero warnings/errors.

Hash/save 391 has pin `0xDD9891C0E400A8EA`. Evidence under
`F:/DK2Evidence/builds/`: `q712-combat-xp-old390-proof--2026-11-03--001/`
and `q712-combat-xp-verification-h391--2026-11-03--001/`.
Final independent review approves source/proof, controlled results and
outcome provenance. Historical 390 controls retain their own prefixes.
Q734 is closed for the bounded ordering rule; Q712/Q719 remain open.

## Outcomes at hash 391

All 115 stored-pickup resyncs (75 Level 5, 40 Level 4) exit 0; four observer
main files match production. Independent review verifies the 701/374-file
study roots, 784-file measurement build and 26-file verification root, with
752 frozen source/config, 45 external and 25 installed input hashes unchanged.

390 is the immediate baseline. [Historical values and cohort definitions](lair-departure.md#outcomes-at-hash-390)
retain native/362 references. The historical 1401-3400 capture-header window
is separate from atomic completed turns and partial-input controls. Scores
check the binary-backed correction; no constants were tuned to them.

Strict Heart dispatch radius about (17,26), native seeds 2/3/7: three native
runs versus fifteen state/generator pairs per remake version. Means per run:

| Hero-ticks inside | DK2 | 362 historical | 390 baseline | 391 |
|---|---:|---:|---:|---:|
| All non-collapsed | 4327.00 | 2720.93 | 2830.73 | 2669.47 |
| Targeting Heart pillar | 3298.67 | 1607.00 | 1603.00 | 1432.00 |
| Targeting Imp | 790.00 | 790.60 | 903.07 | 912.13 |

| State-254 transition, mean/run | DK2 | 362 historical | 390 baseline | 391 |
|---|---:|---:|---:|---:|
| Entries | 15.00 | 12.87 | 14.73 | 17.27 |
| Exit to 199 | 3.67 | 0.60 | 1.13 | 1.13 |
| Exit to 203 | 5.00 | 2.60 | 2.00 | 2.20 |
| Exit to 1 | 3.00 | 4.67 | 4.47 | 5.53 |
| Exit to 6 | 2.00 | 4.40 | 6.53 | 7.47 |
| Exit to 8 | 1.00 | 0.53 | 0.60 | 0.87 |
| Fight plus flee (199+203) | 8.67 | 3.20 | 3.13 | 3.33 |

| Native seed | DK2 entries | 391 entries, five generators in retained order |
|---|---:|---|
| 2 | 15 | 47, 12, 17, 30, 21 |
| 3 | 17 | 8, 10, 5, 26, 17 |
| 7 | 13 | 27, 15, 8, 10, 6 |

The remake range is 5-47; entry parity is not established. Fight/flee exits
remain far below native, and pillar exposure decreases against 390.

Death means use the original scorer and complete-state historical cohorts:

| Window / group, mean/run | DK2 | 362 historical | 390 baseline | 391 |
|---|---:|---:|---:|---:|
| L5 1401-3400 / keeper | 13.38 | 9.90 | 9.40 | 8.525 |
| L5 1401-3400 / keeper Imp | 11.12 | 7.35 | 6.50 | 5.525 |
| L5 1401-3400 / hero | 3.75 | 5.10 | 5.20 | 5.45 |
| L5 3401-5000 / keeper | 8.60 | 9.16 | 11.52 | 11.96 |
| L5 3401-5000 / keeper Imp | 5.80 | 7.20 | 9.60 | 10.04 |
| L5 3401-5000 / hero | 5.00 | 4.44 | 3.48 | 3.36 |
| L4 5001-6000 / keeper Imp, exact | 6.500 | 11.925 | 8.375 | 7.775 |

Q714 Heart/terminal restoration is unchanged. The outcome-selected 32-pair
intersection gives exact Level 4 means 10.03125/8.78125/7.96875 for
362/390/391; pre-terminal pair counts are 8/0/0. Against 390 deaths fall 0.60
all-40 and 0.8125 in that subset. These descriptive subsets establish neither
causation nor per-death attribution to Q714. Q734's bounded order correction
is complete; Q712/Q719 remain open.

Evidence: `F:/DK2Evidence/studies/heart-defence-q712-h391-l5--2026-11-03--001/`
and its `l4` sibling; measurement build
`F:/DK2Evidence/builds/q712-combat-xp-measurement-h391--2026-11-03--001/`.
The source/proof verification bundle is linked above. The correction does not
resolve partial route/body inputs or establish another acquisition rule.
