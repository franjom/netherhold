# Initial Imp walk: movement order causes the first position mismatch

2026-10-18, Level4 `l4live`, initial keeper workers. The movement-order correction
is included in the hash/save-280 integration branch under ADR-0512. Idle arrival
has its own evidence
([imp-idle-arrival.md](imp-idle-arrival.md)).
Source correction: `d10d19aa`.

## Compared boundary

**CONFIRMED observations:** native capture frames 5–7 align with modern turns
4–6. No frame is torn. Native slots 455–452 map to modern IDs 1–4. The modern
probe uses normal campaign initialization, xoshiro seed 1 and six ticks; the
recorded player actions have not begun in this interval.

At turn 4, all four workers agree in entity position, state, heading and the
23 exported body fields after the caller's position/velocity precision
conversion. The fields include axes, route target/window/segments, counts,
progress/history, flags and body limits. This matches compared body inputs,
not the entire native world, update boundary, neighbours or obstacle inputs.

At turn 5, all four still agree in entity position and full body velocity, but
their local axes differ. This is earlier than the known turn-6 raw position
difference (Q610).

| Worker | Shared velocity, 16.16 | Native X axis | Modern X axis |
|---|---|---|---|
| 1 | `(4276,29941)` | `(9356,65511)` | `(9298,65096)` |
| 2 | `(21421,21421)` | `(46869,46869)` | `(46572,46572)` |
| 3 | `(-25965,15578)` | `(-56813,34085)` | `(-56450,33868)` |
| 4 | `(-9640,28908)` | `(-21093,63251)` | `(-20958,62850)` |

## Binary and computation check

**STRONGLY INFERRED mechanism:** the axis assignment in
[Steering_Integrate](functions/004D4760.md) differs from
`SteeringIntegrator.Step`. The corrected native rule is in
[steering-behaviours.md §4](steering-behaviours.md#4-the-integrator-slide-step-and-wall-clamp).
The complete function was reread, including the shared axis-writing tail at
`0x004D4E4B`; its input is the body velocity, not the force or rotated step.

**CONFIRMED computations:** executing
[the original normalization helper](functions/004D1F10.md) on each of the
12 captured velocities reproduces every captured X axis exactly. The
normalization arithmetic itself is not the mismatch in these cases.

## Counterfactual and limit

The local probe replaces the axes after each tick with those derived from
velocity. All four workers' turn-5 axes then match native. Worker 2's turn-6
position remains identical to the unmodified modern result and still differs
from native. Worker 3 also retains a turn-6 axis difference associated with its
different full velocity. No gameplay source was edited for this control.

The axis-source mismatch is established; it does not explain Q610's first
position discrepancy. The movement-order control below resolves that cause.
The axis correction was integrated in hash 274 and remains separate from the
two corrections investigated here: movement order and idle arrival.

## Cause of Q610: movement order and crowd input

**CONFIRMED capture and control:** the native creature list places the four
workers in slot order 452, 453, 454, 455, corresponding to modern IDs 4, 3, 2, 1.
The [linker](functions/004B4A10.md) inserts at the head and the
[world sweep](functions/004B7830.md) follows that list. The captured head also
contains eight heroes before these workers. Their complete creature updates,
including an Imp's state-6 integration, run newest first.

Hash 269's `CreatureMovementSystem.Execute` walks `context.World.Creatures` in
creation order, oldest first. `ImpTaskSystem.Execute` already walks newest first.
This is an ordering disagreement between simulation passes, not a priority rule.

On turn 6, worker 4 moves from tile `(26,52)` to `(26,53)` before worker 2 in
DK2. The latter's current waypoint is `(26,54)`. The
[crowd query](functions/004D15E0.md) covers tile columns 25–27 and rows 53–55,
so that earlier worker contributes its captured radius word, 4576. In the
modern forward movement pass worker 4 has not moved, so this sum is zero.
The other three steering forces are zero at this boundary in the controlled
modern reconstruction; the changed crowd sum alters the follow-path force.

| Movement order | Crowd sum | Follow force per axis | Weighted force per axis | Velocity before speed clamp | Velocity after speed clamp |
|---|---:|---:|---:|---:|---:|
| Oldest first | 0 | 118925 | 24363 | 45771 | 21471 |
| Newest first | 4576 | 113556 | 24378 | 45786 | 21478 |

**CONFIRMED computations:** original Follow_Path and vector helpers reproduce
both numeric chains. That Follow_Path experiment controls its external turn
result and crowd callback; it does not emulate a complete native world.
The quantized length/clamp arithmetic explains the small increase in resulting
speed despite the lower raw follow force; do not smooth or compensate for it.

The independent six-tick campaign control reverses the creature list only while
the existing movement system executes, then restores it before other systems.
It leaves the axis calculation and every gameplay source file unchanged.
All four workers' entity positions, full velocities, headings and states then
match all 12 native observations over aligned turns 4–6. The original turn-6
position discrepancy disappears. Reversing only the axis calculation did not.
This closes the cause of Q610; it does not establish full-walk or campaign parity.

The isolated implementation and its acceptance measurements follow below.

Local ignored artifacts are under
`build/imp-acceptance/reverse-engineering/imp-first-integration-20261018/`:
`export.py`, `native.json`, `Probe.csproj`, `Program.cs`, `modern.json`,
`velocity-axes.json`, `verify.py` and `normalise-oracle.txt`. Further controls:
`reverse-movement.json`, `forces.json`, `reverse-forces.json`, `follow_oracle.py`,
`follow-oracle.txt`, `force_chain.py` and `force-chain.json`. The verifier checks
the turn-4 compared inputs, all turn-5 pose/velocity/axis assertions, all twelve
native normalization results, the axis-only counterfactual and the movement-order
control's exact pose/velocity/state/heading agreement.

## Movement-order implementation and acceptance

2026-10-18: `fixes/creature-movement-order`, ADR-0507, candidate hash/save 271.
Only the advancing loop walks newest first; body preparation and creature
storage order are preserved. There is no sorting or new per-tick allocation.
Axes, idle arrival, task choice and Heart routing/core policy are unchanged.
No dedicated performance benchmark was run.

`CreatureMovementOrderTests` translates the captured two-walker crowd boundary
onto the synthetic map. Both creation-order cases failed with the opposite
velocity result before the fix, then passed. They assert the tile crossing,
native arithmetic's velocity/position results and unchanged creature storage.

**CONFIRMED compiled-candidate observation:** without the order wrapper, all
four workers' entity positions, full velocities, headings and states match
native over aligned turns 4–17: 56 observations. The first subsequent difference
is worker 2 on turn 18: position still agrees, but native resumes idle and stops
while ours remains in state 6 with velocity. The known axis-source discrepancy
also remains in that candidate. The idle-arrival correction addresses the next
verified boundary separately.

**CONFIRMED replay outcomes:** reuse the stable hash-269 baseline and rerun
the same Level4 packet, xoshiro seed 1, 6804 turns and 707 actions on 647 turns.

| Version | Win turn | Keeper Imp deaths | Unresolved mappings | Mapping passes |
|---|---:|---:|---:|---:|
| 269 | 6732 | 3 | 9 | 5 |
| 271, movement order only | none | 23 | 6 | 4 |

The candidate map stabilizes. Its replay SHA256 is
`3ef87c0a192a7ac8fbc757d997688bc26aec55b74820c5f92b55368c29b067b5`.
The baseline is reused, not freshly rerun. Different unresolved mappings and
unmatched native/modern RNG limit interpretation; this is one replay, not a
seed spread or native campaign-equivalence result. The short native correction
is established. The failed campaign outcome is diagnostic, not a veto on that
correction; ADR-0512 includes it in the integration branch. Do not tune movement
to recover the sampled win.

Release build passes with no warnings; all 3572 simulation tests pass. The full
suite has three known-baseline failures: `LicenseHeaderTests.EverySourceFile_StartsWithTheNotice`,
`LevelVariablesOnRealDataTests.LevelOneOverridesReplacesAddsAndKeeps` and
scenario `creatures/a_goblin_eats_a_chicken.yaml`. The formerly failing Level17
and Drako behavior tests pass here. No scenario expectation was changed.

Candidate artifacts are ignored under
`build/imp-acceptance/reverse-engineering/imp-movement-order-20261018/`:
`replay.py`, `results.json`, `candidate/`, `solution-tests.log`, `Probe.csproj`,
`Program.cs`, `export.py`, `native.json`, `modern.json`, `build-info.json` and
`verify.py`. The verifier checks the compiled hash version, the native prefix,
remaining differences, stable replay result and full-suite failure identities.
