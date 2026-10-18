# Current-main Imp work near the Level4 Hatchery

2026-10-18, research only, main `bda4952e`, hash/save 274. The earlier
[task audit](imp-task-audit.md) measures an older build; its seed-5 cast-Imp
assignment must not be treated as a current-main result.

## Method and limits

Read native `level4-live1/seed01/level4.dk2cap` frames 2–1101 once, using
`Capture.frames`: 1100 untorn frames, aligned to simulation turns 1–1100.
Run modern xoshiro seeds 1 and 5 serially for the same 1100 turns with the
Level4 packet, verified offset 69 and the existing entity map. Each runs 123
actions on 116 turns. No gameplay, Unity, hash or test code changes.

The diagnostic records immutable post-turn raw entity positions and states,
plus the 25 tiles at columns 28–32, rows 52–56. Its seed-1 trigger CSV and
complete event sequence equal the verified hash-269 replay prefix exactly;
the merged axis-only change preserves those recorded gameplay outcomes.
The seed-5 map is reused, not independently reconciled for all entities.
Placed workers and the two cast workers retain their creation identities.
This is two modern samples against one native run, not a matched RNG stream
or a campaign/seed-distribution acceptance result.

State counts below measure observations, not event-derived durations. Walking
distance sums Euclidean displacement of consecutive post-turn raw positions
when either endpoint is state 6, divided by 4096. It omits the initial spawn
step and unobserved sub-turn curvature; it is sampled displacement, not exact
route length. Assignment traces use native observed kind/target changes and
modern `job_taken` events; native sub-turn retakes can be invisible.

## Measured work and distance

**CONFIRMED measurements**, in Imp-turns unless stated otherwise:

| Measure | DK2 | Main seed 1 | Main seed 5 |
|---|---:|---:|---:|
| Dig, 13/14 | 1098 | 1126 | 1027 |
| Mine, 25/26 | 803 | 581 | 601 |
| Claim, 15/16 | 776 | 832 | 724 |
| Reinforce, 21/22 | 112 | 399 | 398 |
| Walk, 6 | 2102 | 2001 | 2198 |
| Store, 28/29 | 34 | 0 | 0 |
| Sampled walking distance, tiles | 833.9 | 808.0 | 895.8 |
| First observed reinforcing turn | 1064 | 1018 | 1020 |

Per-worker triples below are **DK2 / seed 1 / seed 5**. Initial identities
are placed-creature order; cast identities are matched by their recorded casts.

| Native / modern worker | Claim turns | Reinforce turns | Sampled walking tiles |
|---|---|---|---|
| 455 / 1 | 54 / 0 / 0 | 0 / 72 / 64 | 163.9 / 145.9 / 196.5 |
| 454 / 2 | 153 / 189 / 0 | 18 / 60 / 70 | 159.9 / 181.0 / 128.8 |
| 453 / 3 | 261 / 117 / 81 | 9 / 80 / 72 | 174.7 / 161.4 / 162.3 |
| 452 / 4 | 0 / 387 / 153 | 21 / 64 / 60 | 170.9 / 164.2 / 192.3 |
| 443 / 13, first cast | 308 / 121 / 280 | 27 / 63 / 60 | 108.6 / 102.9 / 124.1 |
| 442 / 14, second cast | 0 / 18 / 210 | 37 / 60 / 72 | 55.9 / 52.6 / 91.8 |

Of the excess reinforcement, 206/287 turns in seed 1 and 221/286 in seed 5
occur before native's first reinforcement. All three samples contain 96
completed observed state-16 runs, each lasting six or eight turns. Their
respective six/eight counts are 44/52, 16/80 and 70/26. This sample does not
support inventing a uniform claim-action delay or reinforcement timeout.
The known store-state collapse is documented in the earlier audit.

## A controlled claim-choice boundary

**CONFIRMED observations:** seed 1's first cast claims `(30,54)` on turn 403,
the native turn. Seed 5 claims that tile on 358; its cast worker instead takes
Claim at `(29,53)`, not a long initial reinforcement assignment. Thus early
floor ownership remains seed-sensitive, while the old cast-to-reinforce
symptom is absent in these two current-main samples.

For seed 1, native 443 and modern 13 match in position `(124928,223232)`,
state, last/aimed kind 3 and target through turns 397–404. Both enter 15 on
397, work in 16 over 398–403 and enter 10 on 404. This matches those worker
fields, not the full world. On 405 native chooses `(30,53)`, modern `(29,55)`.
At 404, `(30,53)` is still native neutral floor but already modern owned
floor; `(29,55)` has the inverse ownership. Modern worker 4 claimed
`(30,53)` on 357; native first claims it on 414.

**CONFIRMED counterfactual:** immediately before modern turn 405, replace
only those two tiles' terrain, owner and health with their native frame-405
values: `(30,53)=(3,2,1)` and `(29,55)=(8,3,660)`. The chooser then takes
`(30,53)` on the native turn, without a chooser or RNG override. The trace
before the intervention is unchanged. This closes the immediate local
target difference, not the upstream cause of the earlier ownership.

Fresh whole-function reads of [the claim handler](functions/0046AB50.md) and
[side chooser](functions/00469340.md), with the latter's return checked in
instructions, preserve their existing verdicts. Owned tiles are refused
before task reservation; neighbour grades determine the claim start side.
Modern counterparts: `ImpTaskSystem.Selection.cs:678` (search), `:766`
(start side), `:949` (tile eligibility), and `TileWork.cs:353` (ownership gate).
No mismatching priority rule is established at this boundary.

## Earlier floor opening and decision window

**CONFIRMED observations:** the seed-1 worker-4 claim chain starts when its
gold-deposit walk enters `(28,54)` on turn 309. It interrupts that walk to
claim, carrying 1090 gold, and owns the tile on 318. Native already owns it
on 190. Native's worker 453 takes that claim from state 10 on 171, not from
a walking interruption. Its three other initial workers are then assigned
to mining `(29,54)`.

The first wall at `(28,54)` becomes neutral floor on native turn 165 and
modern turn 173. Native worker 453 reaches state 10 on 170 and takes claim
on 171. Modern worker 3 reaches 10 on 167, finds no job on 168 while the
wall is still solid, and remains in state 12 through 187. The dig arrival
and action histories already differ before the opening; these samples do
not isolate a work-rate, idle-duration or RNG defect.

**CONFIRMED counterfactual:** immediately before modern turn 176, set only
worker 3's state to 10 and its action deadline to zero. Raw worker rows
through 175 equal the baseline. It takes mining `(29,54)` on 176, loses
that slot through stealing on 178, then chooses claim `(28,54)` on 179 and
owns it on 196 instead of 318. This exposes an earlier claim opportunity
under altered decision timing; it does not reproduce the native world or
justify waking resting workers when new work appears.

A fresh read of [the walking interruption](functions/00464170.md) confirms
that gold is not rejected: the carried fields tested are object/body slots.
The distance gate refers to the last task tile, not the gold-store destination.
Modern `ImpTaskSystem.cs:256`, `ImpTaskSystem.Selection.cs:639` and the
existing [just-dug handler evidence](imp-claim-path.md#5-the-just-dug-claim-0x00464170-and-0x00469600)
do not establish a gate mismatch for the turn-309 take. The rest/selection
boundary is in `ImpTaskSystem.Wander.cs:77` and `:90`.

## Verdict and next boundary

Keep the chooser unchanged. The larger reinforcement excess remains an
allocation/timing discrepancy, not a proven preference for walls over eligible
floor. Its upstream cause remains **UNKNOWN**, tracked as Q611. The early
claim gap now traces to floor-opening and worker-decision timing before turn
176. The next useful comparison is the initial dig's arrival/action boundary,
including all three workers, against actual native update-boundary inputs.
Do not infer those inputs from a later whole-turn snapshot or patch priorities
to recover one replay. Existing unmerged work/store/arrival
candidates remain unaccepted. No campaign outcome improvement is claimed.

## Reproduction

Ignored artifacts in the retained worktree:
`build/imp-acceptance/reverse-engineering/imp-hatchery-work-20261018/`.
`Program.cs`/`Probe.csproj` run the normal factory and packet helpers with raw
observation; `CONTROL_HATCHERY=1` enables the two-tile control and
`CONTROL_RESELECT=1` the turn-176 worker control. `export_native.py`
exports the immutable native rows; `measure.py` writes `summary.json`,
`per-imp.csv`, `state-runs.csv`, `assignments.json` (job sequences/targets) and
`assignment-legs.csv` (observed assignment spans, sampled travel to arrival,
prior-sample straight-line distance, missing/overwritten/censored flags).
`verify.py` checks coverage, baseline agreement, metrics and the local control.
No full suite, profiler or broad campaign sweep was run for this research.
