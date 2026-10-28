# Imp task outcome audit, 2026-10-17

## Scope and reproducibility

The initial audit changed no simulation, Unity, hash or tests. DKII.EXE was checked
through pyghidra-mcp. Function verdicts live in the linked function records.

Window: simulation turns 1–1100, original capture frames 2–1101 (ADR-0119).
Read `Capture.frames`, not repeated `at` calls. All four captures supplied
1100 frames and none were torn. Counts below are living keeper-owned Imps,
one observed state per turn; they are not inferred from event durations.

Original runs: `reverse-engineering/dk2runs/{level1-live1,level2-live2,
level3-live1,level4-live1}/seed01/levelN.dk2cap`.
This audit uses historical P2, now preserved under
`D:/DK2Evidence/rig-2026-10-24/sets/P2/levelN/seed01–20`.
P2's exact build commit is not established. Its saved rates and the latest
hash305 measurement are in [the current experiment](../experiments/2026-10-25-packet-replay-hash305/README.md).
The state census below remains historical P2.

Two fresh, serial 1100-turn runs used the existing Release tool on checkout
`1b7a4cde`, seed 5, the original packets and P2's entity maps. Level 4 matched
all 3072 P2 events through 1100; Level 1 matched all 1944. No build, test sweep,
profiler or full campaign sweep ran. This verifies those two short runs;
the other seeds remain historical measurements, not a fresh HEAD sweep.

Local, ignored artifacts in `reverse-engineering/`:

- `imp-audit-20261017.py`: state census and event selections.
- `imp-audit-extra-20261017.py`: per-Imp sequences and spatial summaries.
- `imp-audit-20261017/per-imp.csv`: per-Imp state counts, job sequences,
  switches, longest assignment streak and spatial proxies.
- `imp-audit-20261017/{events.tsv,ours-creatures.csv,replay.csv}`: fresh Level 4.
- The same directory's `l1-*` files: fresh Level 1 seed 5.

Level 1's historical exports have events but no creature-state CSV; its state
comparison below uses only fresh seed 5. Levels 2–4 use all 20 saved seeds.
Each level has one original comparator here. In particular, Level 4 has no
20-seed original distribution: a difference from this capture does not by
itself prove a wrong selection rule or a statistically unusual outcome.

## State time and task allocation

Imp-turns; each entry is original / ours. Ours is the median over 20 seeds
except Level 1. Separate column medians need not sum to a seed's total.

| Observed state | Level 1, ours seed 5 | Level 2 | Level 3 | Level 4 |
|---|---:|---:|---:|---:|
| Dig, 13/14 | 842 / 854 | 928 / 1026.5 | 1945 / 1917 | 1098 / 1164.5 |
| Mine, 25/26 | 278 / 245 | 417 / 374 | 241 / 203 | 803 / 595 |
| Claim, 15/16 | 513 / 513 | 756 / 756 | 1191 / 1259 | 776 / 768.5 |
| Reinforce, 21/22 | 738 / 623 | 240 / 346 | 0 / 215 | 112 / 293 |
| Walk, 6 | 1721 / 1880 | 1684 / 1553.5 | 3017 / 2810 | 2102 / 2124.5 |
| Selection/rest/wander, 10/11/12/87 | 231 / 253 | 323 / 307 | 674 / 647.5 | 685 / 617.5 |
| Store arrival/deposit, 28/29 | 44 / 0 | 20 / 0 | 18 / 0 | 34 / 0 |

**CONFIRMED measurements:** Level 4's state 22 alone is 107 original versus
287 median ours, range 55–648. This window does not reproduce the prompt's
102; the figures must not be substituted without matching the window.
Level 3's corresponding counts are 0 versus 210.5, range 0–617.
The clearest allocation difference is extra reinforcement, not extra walking
on every level. Mining also occupies less time; its complete cause is UNKNOWN.

For Level 4 seed 5, the two cast counterparts are particularly different:

| State group, turns 1–1100 | Original 443 | Our 13 |
|---|---:|---:|
| Dig | 49 | 0 |
| Mine | 0 | 0 |
| Claim | 308 | 0 |
| Reinforce, including entry | 27 | 656 |
| Walk | 270 | 39 |
| Selection/rest/wander | 52 | 13 |

Our 13 makes 13 reinforcing takes from turn 394 through 1061, with no
`aim_spent` event by 1100. It works at (29,52), (30,52), (31,52), (32,53),
(32,54), then further walls. Original 443 takes kind 3 at aligned turn 395,
claims continuously until a kind-1 take at 955, returns to claim at 1052,
and first takes reinforcement at 1065. Long same-kind streaks occur in DK2 too.

## Sequence, distance and concentration

Observed original takes are reconstructed from changes in task kind/target
and selection-to-task walks. Our takes are explicit `job_taken` events.
Transient original takes within one turn can be missed. These counts are
diagnostic summaries, not proof that every selection was observed.

| Metric through 1100 | Level 2 original / ours median (range) | Level 3 | Level 4 |
|---|---:|---:|---:|
| Observed takes | 288 / 272.5 (246–310) | 514 / 492.5 (476–526) | 326 / 309.5 (271–353) |
| Changes of job kind | 46 / 39.5 (24–52) | 17 / 28 (16–38) | 65 / 55 (36–65) |
| Longest assignment streak, turns | 383 / 628.5 (364–890) | 959 / 861 (713–985) | 560 / 596.5 (361–762) |
| Median target Manhattan displacement, tiles | 2 / 2 | 2 / 2 | 2 / 2 |
| 90th percentile target displacement | 6 / 6 (3–10) | 5 / 8 (3–10) | 8 / 8 (6–9) |
| Tile-motion proxy during consecutive state-6 turns | 700.95 / 624.00 | 1239.45 / 1139.65 | 864.62 / 841.57 |
| Mean largest group in a fixed 5×5 tile bin | 2.53 / 2.40 | 3.09 / 2.99 | 3.08 / 2.86 |

Assignment streaks include walks and gaps between takes, and are censored at
1100. They are not uninterrupted work durations. A single claim work bout
(15/16) has median 9 turns in the original on Levels 2–4; our median longest
claim bout is also 9 on all three. There is no evidence here for a generic
claim-action duration error.

Spatial measures use tile coordinates on both sides. Target displacement is
not route length. Tile motion sums Euclidean differences of observed tile
positions only while both consecutive observations are state 6; it omits
within-tile movement, boundary turns and sub-turn detours. `steps=` is our
route-point count, not a directly comparable DK2 route-distance measure.
Exact continuous walk distances remain UNMEASURED with these exports.
The fixed bins are a concentration proxy, sensitive to their origin.

## Ranked findings and proposed changes

### 1. Wrong input to the cast Imp's drop scan: high outcome relevance

**CONFIRMED chain in seed 5:** our Imp 1 takes a dig at (20,54) on turn 370,
then interrupts that walk with the freshly-dug claim at (30,54) on 371.
The floor changes from terrain 3 to owned terrain 8 at 379. Original floor
ownership changes at capture 404, aligned 403: ours is 24 turns earlier.
Our cast Imp's scan at 394 therefore cannot take that floor. It takes the
wall at (29,52). Original 443's scan takes (30,54) at aligned 395.

Our initiating line is `ImpTaskSystem.Selection.cs:664`, reached through
the gates at lines 637–659 and the fresh-claim checks at 926–931. The resulting
drop selection and aim are at lines 163–167 and 78–84. Binary counterparts:
[on-the-way gate](functions/00464170.md),
[fresh-claim handler](functions/00469600.md),
[chooser](functions/00463DF0.md).
Their rereads did not establish a mismatching gate in this particular take.

**UNKNOWN root cause:** why the preceding task/route/pool history places our
Imp on that floor at this time. The original counterpart need not follow the
same jobs under a different random stream. The task pool, previous mining
departures and route choices are dependencies, not proven bugs.

Proposed next change: none to selection yet. First compare the complete
kind-14 task lifetime and exact on-the-way inputs around this take in both
engines. Preserve any matching on-the-way rule. If the upstream mismatch is
fixed, the expected local outcome is a floor still available to the cast Imp
and a claim assignment instead of reinforcement. Effect on Portal timing,
Imp deaths and wins is unmeasured, and could vary by seed.

### 2. Store states are collapsed: confirmed fidelity defect, plausible upstream influence

Before ADR-0502, `ImpTaskSystem.Gold.cs:309–320` deposited immediately once the route was
gone, then enters 28, 29 and 10 within that same update. Thus zero sampled
turns in 28/29 does **not** mean those states are never entered.
The original records 10/10, 9/9 and 17/17 turns in those states on Levels
2/3/4 respectively. Level 1 records 22/22; fresh seed 5 records zero/zero.

The [state-28 listing](functions/00466A40.md) turns toward the tile
centre, holds while an angle remains and otherwise sets 29; it does not deposit.
The [state-29 handler](functions/00466AC0.md) deposits and sets 10.
Both were checked against DKII.EXE in this investigation.

The ADR-0512 integration takes the researched ADR-0502 correction: dispatch
arrival, turn-to-centre wait and deposit on their actual state updates,
depositing only in 29. Store arrival uses state 6's exact progress predicate and
native body stop before resuming 28, so the turn and deposit remain stationary;
the shared `StopMoving` helper is unchanged. For an unblocked trip this should
restore two observed turns between arrival and the next selection compared
with the initial fold. Turning may add more. Across the observed original
Level 4 visits this accounts for 34 Imp-turns; it does not by itself explain
the extra reinforcement or the entire mining deficit. It will move gold
availability, subsequent task choices and RNG draw order. Direction and size
of campaign outcome changes require measurement; the follow-up below records
the paired test, without claiming a win-rate improvement.

### 3. Create Imp skips the arrival path: confirmed difference, low local explanatory power

Our `ImpSupplySystem.cs:199–219` spawns at a tile centre, arms the drop scan
and starts with ordinary state 1. Fresh seed 5 observes 1 at 392, 10 at 393,
and the first job walk at 394. Original 443 is observed in 214 at capture
392–393, 11 at 394, 10 at 395 and the walk at 396.
Its initial position is approximately (29.871,54.009), not the tile centre.

[State 214](functions/00481750.md) was reread: it waits on its height test
then calls the worker landing path. The geometric interpretation remains
STRONGLY INFERRED as recorded there. The captured state sequence and our
skipped states are CONFIRMED. The later [position trace](imp-create-position.md)
closes the sub-tile source; [clock calibration](imp-observation-clocks.md)
withdraws the earlier one-sided turn translation.

Proposed fix: reproduce the researched worker arrival sequence and then
measure its first scan, rather than adding a guessed wait. Expected local
effect: first selection is two post-turn observations early in this example.
This cannot recover (30,54), which ours already claimed at 379. Preserve the
researched cast position independently of implementing the landing conditions.

### 4. Aimed reinforcing persistence is faithful: do not add a timeout

The reread [chooser](functions/00463DF0.md) sets the aim only on drop-scan
success. When last kind equals aim it searches that kind at radius
`0x7FFFFFFF` before the ordinary tiers; success returns with the aim intact,
and failure clears it. The reread [dispatcher](functions/00468520.md) sends
kind 6 to the reinforcing ring scan. No duration or completed-wall limit
appears on this path. Our corresponding code is
`ImpTaskSystem.Selection.cs:38–49`, `:199` and `:401–408`.

**CONFIRMED rule; conditional outcome:** DK2 can keep an aimed reinforcer
working across the map as long as successive searches succeed. This does
not prove the exact 600-turn run under our changed map state would occur
in DK2. A timeout or priority reorder would be an invented deviation.

## Remaining uncertainty and stopping point

The selection order itself was not shown wrong. Two concrete timing defects
were identified; the large cast-Imp allocation difference was traced to an
earlier on-the-way claim, but not to a proven incorrect rule causing that
claim. The mining-state reread confirms an action-running hold before task
and hands checks ([record](functions/00465870.md)); the capture also has
many departures on the last-gold-increase turn. A blanket post-strike delay
is therefore not justified by this audit.

The initial audit applied no fix. Its first follow-up implemented only the store
state timing (ADR-0502); ADR-0512 integrates that correction and the later
worker-work visibility correction. Exact walk lengths, the upstream early-claim cause and the
counterfactual effect on win rate remain open; this report does not claim
the full original-versus-modern discrepancy is solved.

## ADR-0502 follow-up: paired measurement

Base: `647c43e8`, including ADR-0500's creature separation and hash 265.
Fixed: the store state change alone, hash/save 266. Both were built in
separate worktrees. All replays below ran serially for 1100 turns with the
same packet and seed-specific P2 entity maps; all produced the complete
turn sequence. These are short probes, not full entity-remapping runs.
Artifacts: `reverse-engineering/imp-store-compare-20261017/results.json`
and the per-side, per-seed event and creature exports beside it.

| Seed | State 22, base → fixed | Portal conversion, base → fixed | (30,54) claimed, base → fixed |
|---|---:|---:|---:|
| 1 | 0 → 0 | 991 → 1098 | 665 → 651 |
| 5 | 183 → 185 | 839 → after 1100 | 399 → 367 |
| 8 | 180 → 330 | 754 → 753 | 403 → 371 |
| 11 | 73 → 0 | 1010 → 1041 | 244 → 244 |
| 14 | 135 → 119 | 714 → 1053 | 403 → 398 |
| 16 | 431 → 13 | 639 → 620 | 403 → 372 |
| 18 | 120 → 626 | after 1100 → after 1100 | 277 → 277 |

States 28/29 now each appear for 13–17 Imp-turns per run, versus zero in
the base. This restores the observable boundary. Portal timing improves in
two seeds, worsens in four (including one newly censored), and remains
censored in one: no consistent early outcome gain. Seed 8's cast now takes
reinforcement where the base took claim. The store correction does not
establish a fix for the early-claim cause.

The old P2 lead is not the current baseline: for example seed 5 already
casts into claim on hash 265. Comparing only the fixed branch to P2 would
misattribute effects from intervening creature separation changes.

Verification: solution and Unity compile pass; 3570 simulation tests pass,
including five new cases for separate state updates, turning, an invalid
store and snapshots in either intermediate state. The focused suite's
three initial regression cases failed before the implementation. Independent
review found no blocking issue.

### Store-only campaign check (hash 266)

The fixed branch ran Levels 1–4 over seeds 1–20, serially, through the original
rig's iterative entity remapping. All 80 runs contain every requested turn;
Levels 2–4 reach stable maps, with no placed-type mismatches. Artifacts:
`reverse-engineering/imp-store-full-20261017/results.json` and its level/seed
directories. Remaining placeholders are reported by the existing rig and
are not treated as exact original-versus-modern creature equivalence.

| Level | Valid runs | Wins | Keeper Imp deaths, median | Total deaths | Range |
|---|---:|---:|---:|---:|---:|
| 1 | 20/20 | 19/20 | 0 | 0 | 0–0 |
| 2 | 20/20 | 9/20 | 0 | 24 | 0–6 |
| 3 | 20/20 | 19/20 | 0 | 0 | 0–0 |
| 4 | 20/20 | 1/20 | 18 | 448 | 2–61 |

Deaths count `creature_died` with `type=Imp player=3`. Level 4's winning seed
is 7, at turn 6756. These are fixed-branch totals, not a paired 20-seed
comparison against hash 265. The older P2 totals cannot establish this
change's contribution to the win or death differences.

**Confirmed store-only regression:** Level 1 seed 18 wins at 1816 on the matching
`647c43e8` baseline and does not win within 2044 on the fixed branch.
The fixed replay refuses the Hatchery build at 466 (`drag_cost=0`);
the baseline has no `build_refused` event. The causal trace follows below.
Baseline artifacts are under the full set's
`baseline-level1/seed18/` directory.

Three full Level 4 seeds were paired on baseline, store-only (266) and
per-worker work visibility (267), with stable entity maps and no placed-type
mismatches. All run 6804 turns.
Baseline artifacts: `reverse-engineering/imp-store-full-baseline-20261017/`.
Hash-267 artifacts: `reverse-engineering/imp-work-visibility-runs-20261017/`.

| Seed | Keeper Imp deaths, base → store → visibility | Win turn, base → store → visibility |
|---|---:|---:|
| 1 | 25 → 35 → 5 | none → none → none |
| 5 | 5 → 8 → 13 | 6768 → none → 6788 |
| 8 | 2 → 5 → 15 | none → none → none |

Store-only worsens deaths in all three and loses seed 5's baseline win.
Work visibility restores that win and reduces seed 1's deaths, but worsens
deaths in seeds 5 and 8. These are paired results, unlike the older P2 set.

These paired runs measure effects of the store correction, not disagreement
with the original mechanism. ADR-0512 integrates the verified store states;
the outcome changes remain diagnostics for the larger Imp discrepancy.

### Level 1 seed 18: why the Hatchery build fails

**CONFIRMED in the paired replays:** the turn-466 command builds one tile,
`(13,34)`. Baseline claims it at 420; the candidate at 821. All nine tiles
in the surrounding excavated 3-by-3 area have identical excavation turns
in both runs, but baseline claims all nine by 446 and candidate none by 465.
The refusal follows from the missing ownership at the requested tile.

The first relevant task divergence is earlier, at `(14,24)`:

| Event | Baseline turn | Candidate turn |
|---|---:|---:|
| Imp 1 deposits its first load | 191 | 193 |
| Imp 1 returns to mining `(14,24)` | 192 | 194 |
| The seam becomes floor | 211 | 213 |
| Imp 1 finishes its mining action | 212 | 214 |
| Another Imp removes the obsolete mining task | 213 | 214 |
| Fresh-claim task raised at `(14,24)` | 212 | never |
| Imp 1 claims there on its later store walk | 255 | never |

At candidate turn 214, Imp 3 searches for excavation before Imp 1 finishes
its action. Its search removes the obsolete mining task. Imp 1 then finds
no surviving task and raises no fresh claim. Baseline has already raised
the claim before another Imp removes the obsolete task. Imp 1's later
store walk takes that claim, leading into the southern claims and the
Hatchery tile. Candidate Imp 1 continues mining; another Imp first claims
`(14,24)` at 375 and works north instead.

**CONFIRMED binary rules:** the mining state's action-running check comes
before its task lookup; a missing task exits before raising kind 14. The
dig/mine search removes obsolete tasks without waiting for their old
workers. See [mining state](functions/00465870.md) and
[excavation search](functions/00469AF0.md), rechecked for this trace.
The existing `ImpClaimsWhatItDugTests` cover both the delayed raise and a
search removing the task first; all 11 tests pass.

A native Level 1 seed-1 capture provides a positive control: `(14,24)`
becomes Dirt Path at capture frame 159 and has kind 14 at frames 163–204.
Another dug tile, `(10,28)`, becomes Dirt Path at frame 43 and has no kind-14
entry through frame 300. These are capture frames, not replay turns, and
there is no matching original seed-18 capture. Missing fresh-claim tasks
are therefore observable natively; this does not prove identical race
timing for seed 18.

**CONFIRMED ordering difference:** original creature updates run the action before the state
dispatcher ([update record](functions/00488930.md)); our pending work is
applied after all Imp decisions. This known ordering difference is recorded
in [mining cadence §7](mining-cadence.md#7-how-long-a-digger-stays-after-its-wall-falls-2026-10-01).

### Work-visibility probe

A throwaway change allocated one `TileWorkSystem` in `ImpTaskSystem.Execute`
and called it immediately after each eligible Imp's `Update`, before the
next Imp. The normal final work pass remained. This tests visibility between
workers, not DK2's action-before-state ordering within one creature.
The diagnostic code was removed after measurement; hash 266 is unchanged.

**CONFIRMED counterfactual, Level 1 seed 18:**

| Event | Store candidate | Flush after each Imp |
|---|---:|---:|
| `(14,24)` becomes floor | 213 | 167 |
| Fresh claim raised there | never | 168 |
| `(13,34)` claimed | 821 | 381 |
| Turn-466 Hatchery build | refused | succeeds |
| Win within 2044 turns | none | 1796 |

The first event-content difference is turn 47: Imp 3 sees `(10,28)` fall
inside the same pass, abandons its obsolete work and chooses again at 48.
It drops the old task before Imp 4's action ends at 49, preventing a fresh
claim. That missing claim agrees with the native seed-1 observation above;
the native capture does not establish exact seed-18 timing. Earlier job
changes, not just the turn-214 race, produce the restored Hatchery build.

A control applied the same flush only at turns 200–214. It changed neither
the event multiset nor any replay CSV row over 900 turns, and the build
still failed. All three diagnostic runs had consecutive requested turns:
900 for the full-time probe and control, then 2044 for the full-time probe.
The two full-time runs agree through turn 900. Artifacts and runner:
`reverse-engineering/imp-work-order-probe-20261017{,-window,-full}/` and
`reverse-engineering/imp-work-order-probe-20261017.py`.

The archived ADR-0503 candidate implements work visibility, with three regression
cases covering both update orders and same-turn task cleanup. Hash/save 267
has no new fields. All 3573 simulation tests, solution and Unity compilation
pass; independent review found no remaining issue. The four targeted replays
are valid; Level 1 seed 18 again wins at 1796. The Level 4 table above records
mixed results. ADR-0512 integrates the inter-worker visibility correction while
retaining the final work pass; it does not implement the whole native scheduler.
[Broader measurements](imp-candidate-acceptance.md) remain campaign diagnostics,
not a veto on this verified boundary.
The [first additional Level 4 seed-8 death](imp-survival-trace.md) has a separate causal trace.
