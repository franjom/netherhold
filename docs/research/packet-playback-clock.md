---
systems: [tooling-process]
---
# Packet playback clock

## Binary and native proof

CONFIRMED: playback consumes records sequentially. Loader 0055E7D0 reads the
68-byte file header. Reader 0055EA10 reads 17 count/header bytes, count*18 action
bytes and 12 trailer bytes, without gating on the stored timestamp. Session
00526752 reads before world call 00526AA7; increment 00526B9E follows the world
return. Initialization 00526254 sets the first executed clock to 1.
Producer 00522790 stores parent clock+1 at packet+1 (005227C3-005227CA),
which explains why ordinary recorded stamps lead the playback clock by one.

An independent reader consumes the complete Level 5 file: 6415 records, contiguous
stored stamps 2..6416, zero residue. In three atomic full images, the native
input at entry ESP+0x78 matches exactly one complete file header/action block.
The world clock, return address 526AAA and return ESP=entry ESP+8 are guarded.

| Completed native turn | Record ordinal | Stored stamp | Actions | Header byte offset |
|---:|---:|---:|---:|---:|
| 1400 | 1400 | 1401 | 0 | 42818 |
| 1615 | 1615 | 1616 | 1 | 51771 |
| 1900 | 1900 | 1901 | 0 | 61908 |

Evidence: `F:/DK2Evidence/studies/q712-packet-playback-clock--2026-11-02--001/`,
including the exclusive-write producer, input hashes and complete-block hashes.
The file stamp is retained evidence, not the native scheduling clock. Fixed
minus 1 is only equivalent for this contiguous file starting at stamp 2.

## Pre-edit mismatch inventory

| Binary address | Existing remake behavior at hash 370 |
|---|---|
| 0055EA10 / 00526752 | PacketLog.Read keys actions by the stored stamp; ReplayCommands.At(turn) executes them one turn late for this file. Native consumes the first body record on turn 1. |
| 0055EA10 | The remake infers action count from the next stamped header. Native reads the preceding count byte and can consume the final complete record without a following header. |

The built reader defines the selected header as playback origin and retains raw
stamps and offsets as provenance. Entity-map and partial-world restoration
are not proved correct by these three packet-buffer matches.

## Built scope (ADR-0633, hash 378)

PacketLog now consumes native count framing and keys actions by record ordinal.
The selected first header (normally byte 69) executes on playback turn 1, even
if its stored stamp differs. Raw stamp/byte-offset metadata includes empty
records. EntityReferences and At resolve on playback time; CSV rows are not
blanket-shifted. Explicit offsets rebase an origin, not an absolute-time slice.

Four binary-format regressions fail with unchanged archived master 377
source in scratch (744 source files hashed before/after), and pass with the fix.
The proof is `packet-clock-master377-old-source-proof.json`.
Additional fixtures cover empty-record provenance, non-scheduling raw stamps,
truncation and the existing action decoders. Native error recovery is outside
scope. Hash/save 378 has pin `0x2BA07ECF2D76C69E`. The reviewed full suites
pass Simulation 4321, Tools 111 and Presentation 1427 cases. Archived master 377
reproduces the same 15 Data and 11 Behavior failed-result names
(`packet-clock-master377-known-failures.json`); these are not new failures.
All 230 paired resyncs exit 0, 115 each at 377 and 378. Each version
reproduces its four observation-control main-file hashes. Independent final
review verifies all four study manifests and the verification bundle, 746
frozen source/config hashes, 45 external-input hashes and all 230 command
comparisons. Q730 is closed for the bounded reader rule; Q712 and partial
restoration Q714/Q719 remain open.

## Paired outcomes (377 and 378)

Each frozen version runs 75 Level 5 and 40 Level 4 resyncs with stored pickups,
the original inputs and five generators per native state. Version 377 is the
immediate isolation baseline; 362 is a historical reference spanning other
changes. The original `score.py` and cohort definitions are retained.

These tables retain historical 1401-3400 capture-header labels for comparison
with 362. They are distinct from the atomic completed-turn controls in
[q712-atomic-inputs.md](q712-atomic-inputs.md); the tables do not establish
whole-world or executed-turn alignment.

### Hero exposure

Strict dispatch radius about (17,26), native seeds 2/3/7: three native runs
against fifteen remake state/generator pairs per version. Means per run:

| Hero-ticks inside | DK2 | 362 historical | 377 | 378 |
|---|---:|---:|---:|---:|
| All non-collapsed | 4327.00 | 2720.93 | 2805.40 | 2753.87 |
| Targeting Heart pillar | 3298.67 | 1607.00 | 1671.93 | 1557.47 |
| Targeting Imp | 790.00 | 790.60 | 858.80 | 916.53 |

### Reporting entries and exits

Same fifteen-pair cohort; visible state transitions, not every within-turn call.

| State-254 transition, mean/run | DK2 | 362 historical | 377 | 378 |
|---|---:|---:|---:|---:|
| Entries | 15.00 | 12.87 | 11.00 | 18.40 |
| Exit to 199 | 3.67 | 0.60 | 0.53 | 1.07 |
| Exit to 203 | 5.00 | 2.60 | 1.33 | 2.27 |
| Exit to 1 | 3.00 | 4.67 | 3.40 | 3.33 |
| Exit to 6 | 2.00 | 4.40 | 5.20 | 10.47 |
| Exit to 8 | 1.00 | 0.53 | 0.53 | 1.20 |
| Fight plus flee (199+203) | 8.67 | 3.20 | 1.87 | 3.33 |

The clock correction increases fight/flee exits against 377, while increasing
walk exits and leaving pillar exposure far below native. It does not close
Q712 or establish another Imp-rule mismatch. No constants were tuned to scores.

### Death scores and Q714 limit

The 1400 death scorer uses eight complete native Level 5 states, excluding
5/8 because their captures end early; restore 3400 uses states 1/4/6/9/10.
Level 4 uses all eight states. Historical native/362 values come from
[the prior comparison](fight-scan-initial-phase.md#death-scores-and-level-4-comparison-limit).

| Window / group, mean/run | DK2 | 362 historical | 377 | 378 |
|---|---:|---:|---:|---:|
| L5 1401-3400 / keeper | 13.38 | 9.90 | 9.62 | 10.10 |
| L5 1401-3400 / keeper Imp | 11.12 | 7.35 | 6.75 | 7.33 |
| L5 1401-3400 / hero | 3.75 | 5.10 | 4.78 | 5.25 |
| L5 3401-5000 / keeper | 8.60 | 9.16 | 7.96 | 8.60 |
| L5 3401-5000 / keeper Imp | 5.80 | 7.20 | 5.88 | 6.96 |
| L5 3401-5000 / hero | 5.00 | 4.44 | 4.56 | 4.08 |
| L4 5001-6000 / keeper Imp, exact | 6.500 | 11.925 | 9.575 | 10.075 |

Q714 Heart/terminal restoration is unchanged. The outcome-selected 32-pair
intersection gives exact Level 4 means 10.03125/9.53125/9.90625 for 362/377/378.
Thus the apparent all-40 improvement from 362 to 378 (-1.85) narrows to -0.125
in that subset. The immediate 377-to-378 change worsens by 0.50 all-40 and
0.375 in the subset. These descriptive subsets neither establish causation
nor attribute individual deaths to Q714.

Evidence under `F:/DK2Evidence/studies/`:
`heart-defence-q712-h377-l5--2026-11-02--001/`, its `l4` sibling and the
corresponding `h378` studies. `state-transitions.json`, exposure outputs,
`score-r1400.out`, `score-r3400.out`, `score-r5000.out` and the 378 Level 4
`resync-contamination-analysis.json` retain the per-run inputs and results.
