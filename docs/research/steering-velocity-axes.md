# Steering axes follow the resulting velocity

2026-10-18, ADR-0509. The standalone change is based on main `506557a5`,
hash/save 269; the candidate is hash/save 274. It excludes the rejected
movement-order and idle-arrival candidates. No new state field is introduced.

## Binary and regressions

**CONFIRMED instructions:** in [0x004D4760](functions/004D4760.md),
`0x004D4B1F` binds the velocity address at body `+0x8C`; the shared tail
`0x004D4E4B` passes it to [0x004D1F10](functions/004D1F10.md), stores the
normalized vector at `+0x94`, and its perpendicular at `+0x9C`. Arrival and
wall clamps precede this tail. Our old `SteeringIntegrator.Step` used the
clamped force or the pre-turn displacement. Replace that source with the
resulting velocity and remove the unused clamp output.

**CONFIRMED computations:** original normalization reproduces the four initial
Level4 worker axes from their captured velocities. New coasting regressions
assert those literal native values. A nonzero-force arrival regression requires
zero axes after the snap. Existing partial-wall and refused-step cases now
assert axes as well. All seven new or strengthened cases fail before the fix.
The original helper also returns `(65791,0)` for velocity `(0x6666,0)`;
the speed-clamp test's old force-derived expectation is corrected accordingly.

## Controlled comparison on the composed candidate

An earlier local experiment applied this same correction over the rejected
movement/arrival composition, using diagnostic hash/save 273. With just the two
turn-22 idle rolls controlled to 75, all four workers match native over turns
4–26: 92 observations of entity position, full velocity, heading, state and both
local axes. This controls a decision input; it does not align the complete
native RNG stream or prove complete world equivalence.

Without that control, the known idle-roll difference remains. The same Level4
packet, xoshiro seed 1 and 6804 turns has the identical recorded replay CSV
SHA256 as the composition without corrected axes:
`1c1bcdcfe9c5aca316fdb93fcc81d19884c1ece144a90f8f939e8b156def8528`.
It still loses 39 keeper Imps and does not win. Reusing its last entity map
stabilizes immediately with ten unresolved mappings. This does not rehabilitate
the rejected gameplay candidates or establish original campaign parity.

## Standalone acceptance

Release build passes with zero warnings/errors; all 3575 simulation tests pass.
The complete solution has five known baseline failures: license-header spacing,
LevelVariables, Level17 Harkan, Level11A Drako and the goblin-eats-chicken scenario.
The earlier baseline runs at `2367fd35` reproduce those identities; main
`506557a5` has identical source/test/Unity trees. The prison-chicken scenario
passes here. No scenario expectation was changed.

**CONFIRMED replay outcome:** the same Level4 packet, xoshiro seed 1, 6804
consecutive turns and 707 actions on 647 turns reproduces the stored hash-269
baseline CSV exactly: win turn 6732, three keeper Imp deaths, nine unresolved
mappings, stable on the first pass when seeded with that baseline's entity map.
SHA256: `1e27ac7a748060ad5400177408edfd2e35a2cb3c1c4178baad4d9ea340521925`.
The baseline is reused, not rerun. Recorded gameplay agreement is not an assertion
of identical full state: corrected axes and the hash/save version differ.

All eight native axis observations over turns 4–5 match on the standalone build.
The separate movement-order boundary at turn 6 remains; its correction is not
part of this branch. The 92-observation controlled result above belongs only
to the composed diagnostic. No dedicated performance benchmark was run. The
change substitutes the normalization input without adding a normalization,
allocation or Heart scan. This is ready for review independently of the
unresolved worker task, arrival and campaign discrepancies.

## Evidence artifacts

Ignored directory in the retained worktree:
`build/imp-acceptance/reverse-engineering/imp-velocity-axes-20261018/`.
It holds the composed patch, immutable motion/axis JSONs, per-pass mappings,
replays/events, test/build logs, probes and `verify.py`. Native comparison inputs
come from the same worktree's `imp-movement-arrival-20261018/native.json`.
The earlier random-input control is preserved on `research/imp-rest-choice`.
No original assets or decompiler output are tracked.
