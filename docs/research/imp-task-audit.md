# Imp task outcome audit, 2026-10-17

## Scope and reproducibility

Report only: no simulation, Unity, hash or test changes. DKII.EXE was checked
through pyghidra-mcp. Function verdicts live in the linked function records.

Window: simulation turns 1–1100, original capture frames 2–1101 (ADR-0119).
Read `Capture.frames`, not repeated `at` calls. All four captures supplied
1100 frames and none were torn. Counts below are living keeper-owned Imps,
one observed state per turn; they are not inferred from event durations.

Original runs: `reverse-engineering/dk2runs/{level1-live1,level2-live2,
level3-live1,level4-live1}/seed01/levelN.dk2cap`.
Our distribution: the existing `sets/P2/levelN/seed01–20` under
`C:/Users/franj/AppData/Local/Temp/claude/d--DungeonKeeperRemake/`
`ec762c7a-940e-4ac0-b16e-928afe1df4fc/scratchpad/`.
Its recorded wins are 20/6/14/2. Its exact build commit is not established.

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

Our `ImpTaskSystem.Gold.cs:309–320` deposits immediately once the route is
gone, then enters 28, 29 and 10 within that same update. Thus zero sampled
turns in 28/29 does **not** mean those states are never entered.
The original records 10/10, 9/9 and 17/17 turns in those states on Levels
2/3/4 respectively. Level 1 records 22/22; fresh seed 5 records zero/zero.

The [state-28 listing](functions/00466A40.md) checks occupancy at the tile
centre, holds when occupied and otherwise sets 29; it does not deposit.
The [state-29 handler](functions/00466AC0.md) deposits and sets 10.
Both were checked against DKII.EXE in this investigation.

Proposed fix: dispatch arrival, occupancy wait and deposit on their actual
state updates, depositing only in 29. For an unblocked trip this should
restore two observed turns between arrival and the next selection compared
with the current fold. Occupancy may add more. Across the observed original
Level 4 visits this accounts for 34 Imp-turns; it does not by itself explain
the extra reinforcement or the entire mining deficit. It will move gold
availability, subsequent task choices and RNG draw order. Direction and size
of campaign outcome changes remain UNKNOWN; this is the first bounded
implementation candidate, not a promised win-rate improvement.

### 3. Create Imp skips the arrival path: confirmed difference, low local explanatory power

Our `ImpSupplySystem.cs:199–219` spawns at a tile centre, arms the drop scan
and starts with ordinary state 1. Fresh seed 5 observes 1 at 392, 10 at 393,
and the first job walk at 394. Original 443 is observed in 214 at capture
392–393, 11 at 394, 10 at 395 and the walk at 396 (aligned 395).
Its initial position is approximately (29.871,54.009), not the tile centre.

[State 214](functions/00481750.md) was reread: it waits on its height test
then calls the worker landing path. The geometric interpretation remains
STRONGLY INFERRED as recorded there. The captured state sequence and our
skipped states are CONFIRMED; the source of the original sub-tile spawn
position was not traced here.

Proposed fix: reproduce the researched worker arrival sequence and then
measure its first scan, rather than adding a guessed wait. Expected local
effect: the first selection moves about one aligned turn in this example.
This cannot recover (30,54), which ours already claimed at 379. The sub-tile
position requires its own binary trace before changing spawn placement.

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

No proposed fix was applied or tested for campaign outcomes. Investigation
stops before pursuing more mining, route and RNG dependencies without an
outcome test. Exact walk lengths, the upstream early-claim cause and the
counterfactual effect on win rate remain open; this report does not claim
the full original-versus-modern discrepancy is solved.
