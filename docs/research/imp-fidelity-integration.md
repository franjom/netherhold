# Imp fidelity integration

## Correction audit

ADR-0512 replaces the use of unmatched-seed campaign wins as a merge veto.
Ten distinct behavioral changes were held across the experimental branches;
combined experiments are counted once. Eight supported corrections are
integrated at hash/save 280. The two animation-clock models were later
replaced by the stored clock of ADR-0514 (hash/save 282).

| Change | Source commit | Disposition and canonical evidence |
|---|---|---|
| Store arrival/facing 28, deposit 29, then selection | `d7972800` | Integrated; [task audit](imp-task-audit.md), [state exits](imp-state-exits.md) |
| Apply each worker's work before the next worker | `de4aa9f1` | Integrated; [task audit](imp-task-audit.md) |
| Apply the miner's own strike before checking seam/hands | extracted from `20c969b4` | Integrated independently of the ending clock; [mining finish](mining-finish.md) |
| Newest-first movement advance | `d10d19aa` | Integrated; [movement order](imp-first-integration.md) |
| Exact idle-return progress and stopped body before resume | `372a7334` | Integrated; [idle arrival](imp-idle-arrival.md), [composition](imp-movement-arrival.md) |
| Four-turn first dig gap across all three dig kinds | `f62b06f7` | Integrated; [cadence](mining-cadence.md) |
| Preserve fractional human Create Imp coordinates | `7d4d87f1` | Integrated; [creation](imp-create-position.md) |
| Height-driven stationary Create Imp landing | `46dbd2f9` | Integrated within documented scope; [creation](imp-create-position.md) |
| Dig animation-ending clock | `7f5adaaf` | Superseded by the stored clock, ADR-0514; [animation clock](animation-clock.md) |
| Mining animation-ending clock | `20c969b4` | Superseded by the stored clock, ADR-0514; [animation clock](animation-clock.md) |

CONFIRMED: the bounded native mechanisms cited above. Neither this integration
nor those captures establish complete chooser, action-scheduler or campaign
equivalence. No job-priority, reinforcement timeout or forced RNG correction
is included. The two animation-clock models are superseded by ADR-0514, which
stores the clip instead of reconstructing its phase.

## Regression interpretation

The old last-seam two-miner test expected a same-turn departure on collapse.
It now checks that exactly one purse receives the final gold and neither miner
departs on the collapse tick. The delivery scenario now expects the actual
post-strike purse, 1091, instead of the old pre-payout event payload 818.
These changes follow the native action-before-hands order, not replay wins.

The prison feeding fixture now drops food centrally at `(5,4)` instead of
the edge `(7,4)`, retaining every feeding expectation. The edge case remains
an unresolved modern route/confinement symptom: both prisoners select two-step
food walks on tick 281, leave the cell at `(7,2)` on ticks 295/299 and never
eat. Central and near-center controls both pass; the central case eats on
281 for 375 health. This does not establish the native cause or justify a
gameplay compensation. Reproduce and compare the food walk against DK2 next.
Original and control scenarios, selected snapshots and logs are preserved in
`reverse-engineering/imp-integration-20261018/prison-audit/`.

## Verification

Baseline is master `9794e07b`; it builds with `NuGetAudit=false` because the
vulnerability service is unavailable. Four Behavior tests fail there: Level1
variable overrides, Level17 Harkan, the Goblin eating scenario and Level11A
Drako. Baseline logs: `build/imp-integration-20261018/` in the main checkout.

Integration solution and Unity adapter compile; the adapter has zero warnings
or errors. Final full-suite result: 5,765 passed, 25 skipped, two failed. All
3,607 simulation tests pass. The remaining failures are the baseline Level1
variable override and Goblin eating cases; Harkan and Drako now pass.
Store arrival's reviewed stop correction has a regression that fails both
route-finished variants before the fix and passes afterward. Knowledge-base,
documentation budgets, coverage and generated ledger checks pass.
Reproduction logs and TRX live under
`reverse-engineering/imp-integration-20261018/` in the retained research checkout.

## Level 4 outcomes 2026-10-19

The lost native capture was re-recorded as a seed-1 replay of `l4live.sav`
(`D:\DK2Evidence\dk2runs\level4-live1`). It reproduces the original: all 92
initial-Imp records over turns 4–26 match except two per-process pointers, and the
1100-turn state counts below match the original exactly.

Imp-turns by state over the first 1100 turns, simulation seeds 1–5, one entity map.
Runs fall into two modes: native-like (reinforce 94–344) and reinforce-heavy (480–695).

| Version | Native-like runs | Median L1 to native | Median Imp deaths | Mean mining (native 803) | Wins by 6804 |
|---|---|---|---|---|---|
| Hash 280 | 3/5 | 474 | 37 | 599 | 0 |
| Hash 281, ADR-0513 | 0/5 | 1162 | 16 | 600 | 0 |
| Hash 282, ADR-0514 | 2/5 | 1300 | 9 | 669 | 0 |
| Hash 283, ADR-0515 | 2/5 (2 more at 395–407) | 690 | 32 | 722 | 0 |

With an entity map built for each seed, so the player's pickups land on the right
creatures, hash 283 gives median Imp deaths of 22 (other columns as above). Hash 284
(ADR-0516) raises (33,55)'s fresh claim in 4/5 seeds at turns 1195–1258 (native 1225),
where 283 never did. Reinforce-light runs rise to 4/5, the median distance falls to
404 and mining averages 676. Median Imp deaths are 30, ranging from 5 to 40.

Hash 285 (ADR-0517) sends alarms only to Guard Rooms. Imp deaths per seed fall to
2, 35, 1, 0 and 9 (median 2). Seed 2's start Guards are still woken by a Goblin refused
training. Imps still raise 10–65 alarms per run where DK2 raised one, so a gate in
`0x004752B0`'s callers or `0x004754D0` remains unmatched.

Hash 286 (ADR-0518) removes level from speed. Neighbouring claims now land mostly at 13
ticks (11/12/13 split 3-103 / 8-72 / 137-215 across seeds; native 86/62/133). The hero Guard
Room at (24,38) converts at 1998-2016 in four seeds (native 2029; 1962-1995 at 285) and the
Dwarves of party 3 arrive at 2959-2977 (native 2989; 2923 at 285); seed 5 is late (2413,
3374). Imps still flee far more than DK2's (53-371 flee entries against 25) and raise 11-132
alarms. Imp deaths per seed are 43, 12, 4, 1 and 60. The fleeing, not the alarm gate
(`0x004602E0` matches ours), is the open divergence.

The first Dwarf encounter (native from 2989, seed 4 from 2959): DK2's Dwarves meet Imps
mining the (18,32) gold from (18,33) at about relative turn 16 and fight at (16-18, 31-33),
outside the Sentry Traps' five-tile reach; their fear stays at 1-3 a tick (an Imp's Threat is
2). Ours walk one tile further east before their first Imp, into range of the traps at
(23,33) and (22,35); each fired Sentry Trap shares its Threat of 74 every update
(`traps.md` §8), so fear reads 74, 148 and 185 against a threshold of 62 and the Dwarf flees.
The fear counter (`0x0045EC80`) and accumulator (`0x0045E4F0`) match ours; the difference is
position (route order and mining stance), not a rule found so far. Flag 14 (a hero Guard
attacked) is set at 5277-5282 in seeds 2-4, as natively (5282); seed 5's late Dwarves draw a
returning Dark Elf to the Guards at 3837, and seed 1 never sets it.

The early (24,39) dig, re-measured at hash 286: seeds 2-4 mine it out at 1954-1958 (native
1951; seed 1 1978, seed 5 2087). Their room converts at 1998-2016 against 2029 because an Imp
already at the gold claims the tile 2-4 turns after the last strike; DK2's last miner left
with the gold and the claim waited 38 turns. Over the run, dug-to-claimed delays match
(native median 80 turns; ours 61-121), and DK2 also puts two or three Imps on one gold tile
(65 of 284 sampled turns; seed 3, 66 of 287). Claims within 15 turns of the dig are 7% natively
and 3-14% in ours; one capture cannot tell that from chance. Lead closed until a second
capture or a rule in the binary says otherwise.

Native: Idle 704, Walk 2102, Dig 1098, Mine 803, Claim 776, Reinforce 112,
Deposit 34, Other 31. Five seeds per version only bound the effect; RNG streams are
not matched to DK2's. Results and scripts: `D:\DK2Evidence\outcomes-2026-10-19-level4`.
