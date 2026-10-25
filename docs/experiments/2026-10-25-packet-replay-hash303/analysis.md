# Why Level 1 stops at 18 wins and Level 4 at 3

Analysis of the completed [hash-303 measurement](README.md), using its saved
events and creature snapshots. Simulation commit
`e96639742fa12c87eeb1407cdc13418f1b7931fe`, HashVersion 303.
No simulation changes, additional replay, repeat sweep, test suite or profiler.
These findings describe ours against recorded native inputs and outcomes;
they make no new claim about DKII.EXE.

## Evidence and method

Inputs are the existing, sealed files under
`D:/DK2Evidence/runs/packet-replay-hash303/level{1,4}/seed{01..20}/`:
`events.tsv`, `ours-creatures.csv`, `replay.csv`, and the set's `results.json`.
The measurement README documents map validity, placeholders and input hashes.
No sealed file was changed. Creature snapshots are sparse change records;
the last row is the last observation, not necessarily the replay's final tick.

Read-only analysis joined events by turn, creature id and tile, compared all
20 seeds, and followed script flags/timers in `replay.csv`. The shipped Level 1
trigger graph was inspected with the archived tool:

```powershell
$env:DK2_DIR = 'E:/GogGames/Dungeon Keeper 2'
& D:/DK2Evidence/tools/hash303-e9663974-2026-10-25/DK2.Tools.exe kwd triggers level1Triggers.kld
```

Our `TriggerExecutor`, `TileTagSystem`, `RoomBuildSystem` and combat state
constants were read to interpret these logs. CONFIRMED below means directly
observed in these saved runs or read in our implementation/shipped data.
STRONGLY INFERRED identifies a causal explanation joining those observations.
No counterfactual fix was tested.

The subsequent [native decision audit](../../research/decision-parity-audit.md)
checks bounded chooser/RNG and combat branches and establishes that our drop
relocation changes recorded coordinates. The frontier conclusions below explain
our adapted replay outcomes; they are not native drop eligibility requirements.

## Level 1: the hatchery command misses its construction window

**CONFIRMED:** the only losing seeds, **3 and 7**, never build a Hatchery.
At turn **466**, both emit `RoomBuild/build_refused` with
`player=3 room=Hatchery drag_cost=0`. Their nine intended Hatchery tiles,
`x=13..15, y=32..34`, become claimed only later. Every winning seed builds
all nine Hatchery tiles; there is no later successful Hatchery build in either
loss.

| Observation | Winner seed 1 | Losing seed 3 | Losing seed 7 |
|---|---:|---:|---:|
| First/last Hatchery tile claimed | 330 / 400 | 730 / 808 | 577 / 683 |
| Hatchery tiles built | 9, turns 466–470 | 0 | 0 |
| Timer 9 ever advances | yes | no | no |
| Flag 19 first set | 1217 | never | never |
| Creature objective action 65450 | 1218 | never | never |
| Dig tag `(14,17)` at turn 1433 | accepted | rejected | rejected |
| Final combat drops, turns 1700–1703 | `(15,14)` | `(15,18)` | `(15,18)` |
| Lord's last health | 0 | 6000 | 6000 |

The rooms are late for different immediate reasons: seed 3 digs this footprint
by turn 282 but does not start claiming it until 730; seed 7 does not dig its
first footprint tile until 475, already after the construction input. This
rules out treating both losses simply as slow excavation. The first logged
Claim jobs on this footprint are selected at 719 (seed 3) and 568 (seed 7),
compared with 300 in seed 1; neither loss has a selected footprint Claim job
waiting to complete when the Hatchery input arrives.

**CONFIRMED script chain:** condition 79 requires at least three non-worker
creatures; its nested room conditions 225 and 226 require Lair and Hatchery
tiles. Only then does action 65298 start Timer 9. After its 20-second condition,
action 65297 sets Flag 19, followed by terrain actions 65470, 65370 and 65367
which soften the three passage tiles `(15,17)`, `(14,17)`, `(16,17)` (the
shipped trigger coordinates are one-based). Creature condition 88 watches
Flag 19 and performs objective action 65450. Neither loss reaches that chain.
Both do build the Lair and acquire more than three Goblins.

**STRONGLY INFERRED:** the missed, unretried Hatchery construction is the
blocking progression failure. It prevents the passage-softening/hero-release
sequence, leaving the later recorded excavation and army placement inputs
unable to reproduce the winning encounter. The rejected tag is a consequence
of the unsoftened passage, rather than evidence that an Imp refused an eligible
dig job. In our tag implementation, rejecting this valid coordinate requires
discovered terrain that is not taggable.

Both losses finish with all three initial heroes alive at full health:
the two Dwarves at 1000 each and the Lord at 6000. Their recorded creature
pickups and drops resolve successfully; the army is dropped short of the
heroes. This is a progression/input-timing failure before the final fight,
not evidence of insufficient damage against an engaged Lord.

**UNKNOWN:** why worker scheduling delivers this footprint late in precisely
seeds 3 and 7. These traces identify the missed prerequisite, not a proven
defect in claiming, movement, job priority or RNG consumption. A successful
Hatchery retry would remove this observed gate; it has not been shown to
produce 20/20. Altering the packet would also change the measurement.

## Level 4: getting workers through to the northern Guard Room

**CONFIRMED:** all 17 losses leave Lord id 5 at its full **7500 health**;
none of its saved health observations falls below 7500. All three winners
convert the Guard Room at `(18,5)` before the final army placement window,
turns **6674–6685**. Those drops land at the intended `(20,5)` only in the
winners. The Lord then takes damage and dies within the existing run limit.

The table reports the first completed work event at each frontier stage.
“Wall opened” includes completion of the actual Dig to floor, rather than
merely stripping reinforcement. A dash means no completion by turn 6804.
The final-drop column groups successful stunned creature drops in that window;
counts vary with the per-seed army/map and do not include failed inputs.

| Seed | `(15,5)` claimed | Wall `(16,5)` opened | `(17,5)` claimed | Guard Room converted | Final drop tile (count) | Imp deaths | Win tick |
|---:|---:|---:|---:|---:|---|---:|---:|
| 1 | 6347 | — | — | — | `(15,5)` (12) | 19 | — |
| 2 | — | — | — | — | `(15,15)` (10) | 36 | — |
| 3 | 6418 | — | — | — | `(15,5)` (16) | 0 | — |
| 4 | 6379 | — | — | — | `(15,5)` (13) | 1 | — |
| 5 | 6350 | — | — | — | `(15,5)` (10) | 6 | — |
| 6 | 6298 | 6492 | 6544 | 6664 | `(20,5)` (17) | 6 | 6752 |
| 7 | 6303 | — | — | — | `(15,5)` (14) | 8 | — |
| 8 | — | — | — | — | `(24,21)` (10) | 28 | — |
| 9 | 6280 | — | — | — | `(15,5)` (10) | 4 | — |
| 10 | — | — | — | — | `(24,28)` (3) | 58 | — |
| 11 | — | — | — | — | `(24,21)` (8) | 17 | — |
| 12 | 6802 | — | — | — | `(15,11)` (13) | 22 | — |
| 13 | 6582 | 6472 | — | — | `(15,5)` (13) | 22 | — |
| 14 | 6450 | 6458 | 6513 | — | `(17,5)` (10) | 19 | — |
| 15 | — | — | — | — | `(24,22)` (5) | 38 | — |
| 16 | 6376 | 6442 | 6495 | 6615 | `(20,5)` (18) | 3 | 6736 |
| 17 | 6422 | — | — | — | `(15,5)` (15) | 1 | — |
| 18 | 6373 | — | — | — | `(15,5)` (11) | 7 | — |
| 19 | 6341 | 6488 | 6540 | 6660 | `(20,5)` (18) | 0 | 6752 |
| 20 | 6388 | 6442 | 6495 | — | `(17,5)` (15) | 9 | — |

### Eight losses stall at the wall despite taking excavation jobs

Seeds **1, 3, 4, 5, 7, 9, 17, 18** claim `(15,5)` before the final drops
but never open `(16,5)`. Every seed accepts its dig tag at turn 6436.
These are not rejected tags or a complete absence of dig-job selection:
their ImpTask logs repeatedly take and transfer Dig/DigOwnWall jobs.

For each of these eight seeds, selecting the shortest logged approach to
`(16,5)` before turn 6500 finds a worker that subsequently enters combat,
then state 203 (Fleeing), then walks away before reaching the wall area.
This is an example from each seed, not an assertion about every assigned Imp:

| Seed | Imp | Dig job taken | Combat engagement | Engaged hero |
|---:|---:|---:|---:|---:|
| 1 | 59 | 6448 | 6459 | 73 |
| 3 | 59 | 6465 | 6479 | 68 |
| 4 | 51 | 6463 | 6473 | 65 |
| 5 | 37 | 6442 | 6453 | 65 |
| 7 | 14 | 6436 | 6452 | 69 |
| 9 | 24 | 6449 | 6456 | 70 |
| 17 | 54 | 6450 | 6456 | 66 |
| 18 | 55 | 6447 | 6453 | 67 |

For example, seed 3 Imp 59 engages at `(15,13)` on turn 6479, flees on 6480,
and is already back at `(34,55)` in its last observation by 6614. It survives.
Thus zero Imp deaths does not imply effective worker delivery: seed 3 loses
with zero deaths while seed 19 wins with zero. Reinforcement is also not a
sufficient explanation: winners 6 and 19 successfully dig their reinforced wall.

### Two losses open the wall but lose the room conversion window

Seeds **14 and 20** both claim `(17,5)` in time and start converting `(18,5)`.
Their first nearby conversion workers are interrupted by heroes:

| Seed | Imp | ConvertRoom job | Working state 18 begins | Engaging / fleeing | Room completes |
|---:|---:|---:|---:|---:|---|
| 14 | 57 | 6516 | 6519 | 6602 / 6603 | never |
| 20 | 56 | 6498 | 6501 | 6608 / 6609 | never |

Both workers still have 1250 health when they flee. Other Imps repeatedly
take the conversion job afterwards, but the room remains enemy-owned through
the run. The final army drops therefore fall back to `(17,5)`.
By comparison, winners 6, 16 and 19 complete the conversion at 6664, 6615
and 6660 respectively; their converting workers hold working state 18 through
completion. Their Lord first takes damage at 6702, 6701 and 6726.

### Remaining losses and limits

Seeds **2, 8, 10, 11, 15** never claim `(15,5)`; seed **12** reaches it only
at 6802, after the final drops. Seed **13** digs the wall at 6472 but does not
complete the next claims, so its drops also stop at `(15,5)`. These are earlier
frontier delays, not additional examples of the specific room interruption.

**STRONGLY INFERRED:** timely, sustained worker access under hero pressure is
the immediate bottleneck for more wins. The observed chain is unfinished
excavation/claim/conversion, fallback army drops, no damage to the Lord.
Combat withdrawal can break that chain without killing the worker, so reducing
Imp deaths alone is not a sufficient target. The three successful runs show
that the existing army can kill the Lord within the recorded horizon once the
final drop reaches the intended frontier.

**UNKNOWN:** which differences in hero timing, combat detection/fear, worker
selection, routing or earlier army delivery account for each native-versus-ours
divergence. Historical map criteria allow disclosed placeholders, so army
composition/input availability is another limitation; this analysis does not
isolate it. Nor does it prove that changing flee behavior, protecting one Imp
or opening one tile would increase the rate. Those are separate hypotheses
requiring native evidence and controlled verification, not changes made here.

## Verification

A read-only independent review recomputed the Level 1 gates and Level 4 tables
and worker transitions from the saved files and checked the shipped trigger
chain; no blockers. The existing tool's `docs` and `kb check` gates and
`git diff --check` pass. No simulation source, tests or recording inputs changed.
