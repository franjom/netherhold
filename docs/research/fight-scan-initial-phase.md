# Initial fight-scan phase (Q718, Q712)

Source: retail DKII.EXE v1.70, ghidra-queries, 2026-11-01. Binary readings are
STRONGLY INFERRED; observed capture/replay values are CONFIRMED.

## Binary rule and allocation lifecycle

Constructor 0048755D-0048757A zero-extends the word tag at creature+4, divides
by 10, and stores the remainder in +3E6 bits 22-25. No random draw supplies it.
ProcessFight 0045E9F0 consumes that nibble using the existing stationary/moved
tile rule; recovery 269/210/263/264 suppresses ordinary scanning and preserves it.

The tag belongs to a preconstructed creature pool, rather than a creation
ordinal. Fresh level load 00513870 resets the global thing cursor, creates
seven players at 00513A4B, then the creature pool at 00513A5C. Thing_TakeSlot
00507850 assigns consecutive tags; the 448 creature records have tags 8..455.
ThingPool_Construct 004B5A10 prepends them in storage order, so free head 455
is first. The ordinary wrapper 004B78D0 passes that head to 004B7900. The
allocator removes the named slot from its doubly linked free list, then calls
the creature constructor. The serialized-manager path 004B65E0 may name a
different explicit slot; native save loading is outside this implementation.

ReturnSlot 004B7A20 calls death bookkeeping 00487C70, then prepends that tag to
the free head and decrements the live count. A corpse is a separate native
thing: retaining a CreatureState as our corpse must not retain this phase slot.
The two allocator callers and two return callers were enumerated over all
executable blocks. The other return caller is the front-end MPD table path.

## Paired diagnostic before any production change

Evidence: `F:/DK2Evidence/studies/q712-scan-phase--2026-11-01--001/` and
`F:/DK2Evidence/builds/q712-scan-phase-h366--g8e707689--2026-11-01--001/`.
All simulation source hashes equal integrated 366. The scratch CLI changes
only a newly born state 269 hero's initial phase after its native birth pairing.
The intervention uses captured +3E6, rather than a fitted constant. Existing
resync creatures are untouched. The observation-only control has identical
creature/event/replay/job outputs to the production CLI (four SHA-256 checks).

Each native seed 2/3/7 has hero 439 born 1514 in 269, age 0, phase 9. Each remake
generator 101 control starts phase 0. All six paired runs use the archived stored
pickup inputs. Seed 2 with phase 9 acquires native target 455 at 1536, matching
DK2; phase 0 acquires it at 1538 after movement. Seeds 3/7 retain identical
creature exposure and 254 outcomes under this intervention. This establishes a
local cause, not the aggregate Q712 cause or a production id-modulo shortcut.

| Generator 101, ticks 1401-3400 | Seed 2 baseline / mapped | Seed 3 baseline / mapped | Seed 7 baseline / mapped |
|---|---|---|---|
| Hero-ticks near Heart | 2948 / 3865 | 2916 / 2916 | 3397 / 3397 |
| Pillar-target ticks | 1988 / 2576 | 1649 / 1649 | 2222 / 2222 |
| Imp-target ticks | 747 / 1050 | 985 / 985 | 979 / 979 |
| Entries 254 | 9 / 7 | 3 / 3 | 8 / 8 |
| Exits 199+203 | 2 / 3 | 0 / 0 | 2 / 2 |

These are three paired controls, not the preceding 15-run outcome cohort.
Phase input JSON, commands, exact hero traces and visible transition counts
are retained. The original exposure functions are reused with source SHA-256.

## Mismatches listed before production changes

| Address | Integrated 366 / required behavior |
|---|---|
| 0048755D-0048757A | Every new creature starts counter 0. Initialize from its native pool tag modulo 10. |
| 00513A4B/00513A5C, 004B5A10 | Only monotonic simulation ids exist. Keep a separate phase-slot identity and the native fresh free-list order; do not replace simulation ids or use them modulo 10. |
| 004B78D0/004B7900 | Ordinary creation must take the free-list head; subsequent creation reuses the most recently returned slot. |
| 004B7A20/00487C70 | Release the phase slot at creature death bookkeeping even when our CreatureState continues as a corpse; later corpse removal must not return it twice. |
| WorldSnapshot / StateHash | The allocated slot and free-list order affect future scans, so both must survive snapshots and hashing. |

## Implementation boundary (ADR-0623)

CreatureSlotPool keeps a separate phase identity; permanent simulation ids
remain the references used by rules and events. Initial phase follows the
pool tag, not an id remap or a new random draw. Native saved-manager imports
remain outside our snapshot format. WorldResync still keeps our pool history;
its captured creature pairings do not restore the native free-list order.

The shared TrySpawnCreature factory returns no creature when the 448 live slots
are occupied; gameplay producers handle refusal. Checked projection/scenario
construction requires capacity. Hero parties count occupied slots rather than
retained corpse records. Snapshot loading checks the full allocated/free
partition, including duplicate ownership and missing slots.

Capacity review also read two failure branches: Jack 004B03C9 retains damage
but skips trap destruction and Jack setup; Temple 004F180C consumes the matched
history but skips the reward, effect and speech. Their KB records hold the
instruction details. These are covered as allocation-refusal controls.

## Verification

Hash/save 367 has pin `0xAEF95C92552DC089`. All 15 focused cases pass;
11 behavior cases fail against the separate version-366 source archive,
and four new snapshot-format cases pass. The reviewed full suite passes
4,285 Simulation and 1,413 Presentation cases. Behavior passes 510, skips 5
and retains the known Level 18 failure (`phase-all-tests-reviewed.log`).

## Matched outcomes (366 through 369)

All 460 frozen resyncs across 366/367/368/369 exit 0: each version runs 75 Level 5 and 40 Level 4
processes, with five generators per native state and the archived stored-pickup
inputs. Baseline 366 already includes the integrated Heart/supply changes;
362 is a historical reference, not the isolating control for ADR-0623.

Evidence roots:
`F:/DK2Evidence/studies/heart-defence-q712-h367-l5--2026-11-01--001/` and
`F:/DK2Evidence/studies/heart-defence-q712-h367-l4--2026-11-01--001/`;
the corresponding `h366`, `h368` and `h369` siblings hold the other versions. Frozen-build provenance:
`F:/DK2Evidence/builds/q712-fight-phase-h367--g8e707689-wip--2026-11-01--001/`.
The observation-only tracer's production control matches all four original
creature/event/replay/job outputs by SHA-256 (`production-control.json`).

### Hero exposure and targets

Ticks 1401-3400, native seeds 2/3/7: three native runs and 15 remake runs per
version. The strict radius uses exact positions around room tile (17, 26).
Means follow `exposure-summary.json`; historical 362 values are from
[the earlier exposure study](party-arrival-lifecycle.md#hero-presence-and-targets).

| Hero-ticks inside, mean/run | DK2 | 362 reference | 366 | 367 | 368 | 369 |
|---|---:|---:|---:|---:|---:|---:|
| All non-collapsed | 4327.00 | 2720.93 | 3102.33 | 2904.07 | 3213.87 | 3088.73 |
| Qualified intruders | 4262.33 | 2683.73 | 3055.27 | 2857.27 | 3176.33 | 3055.40 |
| Targeting Heart pillar | 3298.67 | 1607.00 | 1890.80 | 1718.20 | 1997.60 | 1895.00 |
| Targeting Imp | 790.00 | 790.60 | 901.53 | 851.27 | 933.93 | 918.60 |
| Other target | 66.67 | 90.60 | 56.13 | 85.53 | 56.20 | 52.27 |
| No target | 171.67 | 232.73 | 253.87 | 249.07 | 226.13 | 222.87 |

### Reporting entries and exits

Same cohort; visible transitions rather than every within-turn state call.
`state-transitions.json` records the source CSV hashes and per-run counts.

| State-254 transition, mean/run | DK2 | 362 reference | 366 | 367 | 368 | 369 |
|---|---:|---:|---:|---:|---:|---:|
| Entries | 15.00 | 12.87 | 10.93 | 9.13 | 8.60 | 11.20 |
| Exit to 199 | 3.67 | 0.60 | 0.73 | 0.60 | 0.60 | 0.87 |
| Exit to 203 | 5.00 | 2.60 | 1.47 | 1.13 | 1.47 | 2.20 |
| Exit to 1 | 3.00 | 4.67 | 3.93 | 3.67 | 3.53 | 4.27 |
| Exit to 6 | 2.00 | 4.40 | 3.93 | 2.47 | 2.60 | 3.20 |
| Exit to 8 | 1.00 | 0.53 | 0.87 | 1.20 | 0.33 | 0.60 |
| Fight plus flee (199+203) | 8.67 | 3.20 | 2.20 | 1.73 | 2.07 | 3.07 |

Both hero exposure and fight/flee exits remain below native in this cohort.
The phase and ordinary cast-hold rules are built, but these outcomes do not close Q712. Production restore
retains the unmatched resync allocator history described in Q719; the earlier
mapped-phase diagnostic's 1536 acquisition is not a production-alignment claim.

### Death scores and Level 4 comparison limit

Original `score.py` definitions and cohorts are retained. Level 5 at 1400
scores eight complete native states (5/8 excluded because their captures end
early); at 3400 it scores states 1/4/6/9/10. Level 4 uses all eight states.
Values use the scorer's printed rounding, from `score-r1400.out`,
`score-r3400.out` and `score-r5000.out` in each version's study.

| Window / group, mean/run | DK2 | 362 reference | 366 | 367 | 368 | 369 |
|---|---:|---:|---:|---:|---:|---:|
| L5 1401-3400 / keeper | 13.38 | 9.90 | 11.15 | 10.32 | 10.40 | 9.72 |
| L5 1401-3400 / keeper Imp | 11.12 | 7.35 | 8.50 | 7.73 | 7.65 | 7.08 |
| L5 1401-3400 / hero | 3.75 | 5.10 | 4.67 | 4.75 | 4.67 | 4.78 |
| L5 3401-5000 / keeper | 8.60 | 9.16 | 9.08 | 9.48 | 7.24 | 7.40 |
| L5 3401-5000 / keeper Imp | 5.80 | 7.20 | 7.16 | 7.64 | 5.36 | 5.40 |
| L5 3401-5000 / hero | 5.00 | 4.44 | 4.64 | 4.36 | 4.64 | 4.60 |
| L4 5001-6000 / keeper Imp | 6.50 | 11.93 | 10.10 | 10.10 | 11.80 | 11.82 |

Level 4 is identical between 366 and 367 in all 80 creature/event outputs.
The exact all-40 means for 362/366/367/368/369 are
11.925/10.1/10.1/11.8/11.825; the 32-pair intersection means are
10.03125/10.4375/10.4375/11.96875/11.84375. All 80 files differ between
367/368; 38 of 80 remain identical between 368/369. The room-position change
barely moves the all-40 death count. The excluded eight pairs are 362
generator-404 histories already terminal at 4966; later versions have no
detected pre-restore terminal case in this analysis. This outcome-selected
intersection is descriptive, not an unbiased regression test. Q714 Heart/terminal
restoration and Q719 allocator/clocks remain partial. Sources:
`resync-contamination-analysis.json` and `output-comparison.json`.

## Matched input control and remaining divergence

The first five diagnostic modes are valid: observe, pool, pool plus levels,
pool plus levels/scans, and the latter with a captured phase pulse at 1535.
Observe preserves all four production outputs. Restoring the captured pool
alone gives hero 439 native slot 439/phase 9 and acquisition at 1536. These
interventions are separate from the unmodified 367 outcome cohort above.

Adding level/XP/health and scan inputs at 1400 does not align Imp 455's later
scan clock. Adding the captured phase pulse at 1535 does: both actors match native
state, target and position through 1547, and the Imp's escape positions match
through the observed endpoint at 1555. Hero 439 drops target 455 at 1548,
while native drops it at 1553 and retains current action 16 through 1552.
The valid control therefore narrows the residual beyond the earlier escape
mismatch; it does not establish a new fear or selector rule.

### Next matched divergence (Q720, pre-edit)

CONFIRMED binary/source mismatch, independently reviewed before any cast fix:
ordinary targeted action 16 remains busy after shot-handler completion. The
shot return ends handler work; the shared counted mode-3 runner retains the
action until animation completion. The existing records own the binary
readings: [00498870](functions/00498870.md), [00496AA0](functions/00496AA0.md),
[004950C0](functions/004950C0.md), [004969D0](functions/004969D0.md),
[00496D00](functions/00496D00.md) and [0045F030](functions/0045F030.md).

| Boundary | Pre-edit 367 mismatch |
|---|---|
| Shot release versus action completion | `FightRules.FireSpell` clears `SwingInProgress` at release, losing the later busy interval. |
| Shared fight busy predicate | `MeleeActionIsRunning` omits the ordinary targeted-cast post-release hold. |

The native extract `cast-hold-native439.json` records handler flags 0 through
1547 and 1 at 1548-1552; the animation advance rate is 271 during the cast
through 1551, then 0 at 1552. Action 16 clears at 1553. This separates shot
completion at 1548 from the later action clear. Hash 367 and its measurements
above remain the pre-edit control. One captured cast does not
establish the aggregate Q712 cause or universal target retention.

Evidence: `input-probe-001/matched-control.json` under
`F:/DK2Evidence/studies/q712-resync-clock-audit--2026-11-01--001/`, with the
root `README.md`. Its 210 compared records cover the five valid modes.
`input-probe-001/results-validity.json` excludes the sixth mode,
`pool_levels_scans_pulse_seed`: an InvalidCastException from Xoshiro256StarStar
to Dk2Lcg invalidates its partial outputs. No successful RNG pulse is claimed.

### Ordinary cast hold built (ADR-0624, hash 368)

Ordinary `StartCast` now stores the counted art 6 deadline in the existing
`ActionUntilTick`, including the native art fallback. `ActionIsRunning` checks
that deadline at the existing fight busy gate after target validation and fear;
shot-handler completion no longer releases the remaining animation hold.
Travel cancellation and replacement `StartSwing`/`BeginPossessedAction` discard
the old deadline. No field was added; action 15 and the possessed casting model
are unchanged. The fresh-animation timing surrogate remains a boundary.

Hash/save 368 has pin `0x253230543C1C4CFE`. Eight focused cases pass. Against
archived 367 source, five fail and three pass: two failures expose early release;
three replacement cases cannot reach their assertions because the old hold is
absent. This is not five independent cancellation regressions. The full build
is clean; Simulation 4293 and Presentation 1413 pass. Behavior has 509 passes,
five skips, the known Level 18 failure and an overlapping watchdog timing
failure. The watchdog passes isolated on current source and master 8e707689;
that master still fails Level 18. Verification logs: `cast-final-build`,
`cast-final-tests`, `cast-watchdog-isolated`, `cast-master-controls` and
`cast-old-src/proof.json` supplied with this change.

The matched captured-input control agrees exactly for actors 439/455 in state,
target, position and health at 1535-1555; hero 439 remains exact through 1588.
The extended comparison first differs for Imp 455 movement at 1560, after
withdrawal destinations already differ at 1544: native Library (5,29), ours
(15,30). This identifies a separate destination input, not another hero-selector error. See `native-extended.json`, `extended-comparison.json` and
`native-withdraw-destination.json` under
`F:/DK2Evidence/studies/q712-cast-control--2026-11-01--001/`.
The diagnostic restores captured inputs and is separate from production outcome
cohorts. Q720 is closed for the bounded mechanic after independent review of
the source, controls and five evidence manifests (zero mismatches; 744 frozen
source hashes unchanged). Q712 and Q719 remain open; no aggregate causal
closure follows.

### Room position mismatch (Q721, pre-edit)

The confirmed position mismatch, completed instruction read, native 14-tile
case and pre-edit source inventory now live in [ordinary room positions](room-position-parity.md).
Hash 368 is the pre-edit control; the bounded hash-369 implementation closes
Q721, with no aggregate cause proved.
Study 001's `*_destination` intervention also rebuilt the mesh
and is excluded from destination-only causal claims. Corrected study 002
reuses `simulation.Navigation`. Supplying only the captured goal removes our
1590 re-encounter and extends hero 439's exact compared-field prefix through
1610. Imp 455 movement matches through 1565, then differs at 1566; native
arrival at 202 is 1585 versus ours 1587. Earlier path differences prevent an
arrival-rule verdict. This is a within-remake destination control, not matched
RNG/room-list inputs. See study 002 `README.md`/`comparison.json`.
The next hero difference at 1611 is not established as a selector error.
The hash-368 diagnostic is historical; current outcomes and controls are recorded separately.

## Resync inputs (Q719)

The existing restore keeps our phase-slot assignments and free-list order.
At 1400, native seeds 2/3/7 have free head 439 and a complete 432-free/16-live
partition. The frozen 367 seed 2/generator 101 run instead gives hero 439 slot 438,
phase 8, despite correctly implementing the fresh-level/reuse rule. Report
pairings do not currently replace that allocator history. Native room-list order
is another input: the capture has seven rooms, while our order is derived.
Room selection must be controlled separately from the position mismatch Q721.

Levels are also partial: the restore reports Imp 455 at level 3 against native 2
and leaves it there. At 1535 native 455 has phase 1 and health 625; ours has phase 3
and health 750. Native experience is the dword+3FF (00457880); the current
restore leaves that bank ours. The matched input control above isolates these
inputs diagnostically; production restoration remains partial under Q719.

Add-only input audit:
`F:/DK2Evidence/studies/q712-resync-clock-audit--2026-11-01--001/`.
The original `native-imp455.json` mislabels a raw word at +0x528 as the counter.
The corrected `probe-input-seed02-v2.json` extracts its byte, matching the
[reviewed width](functions/0045EC80.md). Experience is separately a dword at
+0x3FF and level a byte at +0x403 ([record](functions/00457880.md)).
These are distinct restore inputs; a level mismatch does not establish a
regeneration-rule difference.

### Captured-input and encounter controls (369)

The add-only study `F:/DK2Evidence/studies/q712-room-resync-input-control--2026-11-01--001/`
contains 45 successful runs: seeds 2/3/7, five generators each, restoring only
at 1400. These are cumulative partial-state interventions, not production
scores or an individual-field attribution. No later phase pulse or RNG restore
is applied. `measurements.json` retains per-run results against the same 15
production pairs above.

| Inputs additionally restored | Entries | 199+203 | Hero-ticks |
|---|---:|---:|---:|
| Pool assignments/free order | 11.00 | 3.00 | 3008.40 |
| Also level/XP/health bundle | 10.00 | 2.73 | 2601.47 |
| Also phase/prior scan-tile input | 10.00 | 2.73 | 2601.47 |
| Also native resident update order | 11.20 | 3.27 | 2620.13 |

These inputs do not close the aggregate gap. The room-goal control separately
verifies Training's owned centre (16,30) and reproduces all four observed main
files. Supplying only the captured Library goal still matches hero 439 through
1610 and victim movement through 1565, with earlier path differences before
arrival. Source: `q712-room-withdraw-destination--2026-11-01--001/`.

`reporting-position-summary.json` and `reporting-opportunities.json` compare
completed-frame responder positions against the NonFighter near-Heart region
about (16,25), distinct from dispatch about (17,26):

| Mean per run | Native | 369 |
|---|---:|---:|
| Reporting entries inside that region | 12.00 | 6.07 |
| Reporting ticks inside | 75.67 | 79.20 |
| Those ticks with an attackable, non-collapsed hero within one tile | 11.67 | 3.00 |

The distance bucket is geometric: it establishes neither LOS/route eligibility
nor inputs at the within-turn scan. The exit grouping uses the last visible
254 frame. It supports tracing encounter timing/position; it does not prove
another acquisition error. Native extraction checks all 6003 frames including
1400 for tearing and tick agreement; classified transition totals match the
original transition counter for every compared remake run.

A separate captured chain audit finds resident creature update order differs
in nine of 15 restores: native Imp 440 precedes Warlock 441, while their
permanent-ID order is reversed. The other 14 residents retain order. Seed 2,
generator 101 is one of the six aligned controls. `creature-order-at1400.json`
uses native class-list links and the actual descending-ID scheduler; this
additional Q719 input is tested in `q712-resident-order-control--2026-11-01--001/`: 17 successful runs, two off-controls and all six aligned active controls reproduce four baseline files. The intervention changes aggregate outcomes but does not close Q712. The next matched divergence and guarded route-window/map-input controls are in [q712-route-window.md](q712-route-window.md).
