---
systems: [combat, creatures, simulation-core]
---
# A shot probes ahead, a hit heads the creature, and the collapse is inside the hit

Source(s):
- `DKII.EXE` v1.70 (GOG), read 2026-11-11 for Q712 against the Level 5 seed-1 capture. Addresses
  are facts about the binary in our own words (ADR-0046).

Questions: Q712. Decision: ADR-0739. [shots.md](shots.md) section 4a holds the rest of the
collision step; [death-fall-heading.md](death-fall-heading.md) the collapse's picks and the
fall in state 216.

## 1. A hit heads the creature (STRONGLY INFERRED; CONFIRMED for launched Fireballs)

[Shot_Collide](functions/004ABD60.md), after the contact damage ([004AC420](functions/004AC420.md))
and the fear call (`0x004AC035` into [004B32A0](functions/004B32A0.md)): for a creature target
(class word `+0x12` is 0, tested at `0x004AC016` and `0x004AC03E`) whose shot definition has flags
dword `+0x43` mask `0x100` (`0x004AC049..0x004AC04F`), `0x004AC05B..0x004AC070` call
[005119F0](functions/005119F0.md) with the shot's `+0xF4` and the word `0x400` and store the
result in the creature's `+0xF4`. There is no alive, in-hand or health gate. The `0xDB` sound and
`0x0048FF70` (a sound request, `0x1BB`, or `0x114` when definition `+0x685` is 1; not built) follow
in the same gate.

Capture: Wizard 436 is hit on 1718, 1719 and 1720 by Fireballs heading 5, 29 and 131 and reads
`+0xF4` 1019, 995 and 893. The lethal hit's write comes after the collapse's picks (1041, 1036) and
overwrites the heading: this is the 893 that death-fall-heading.md recorded as UNKNOWN.

A chained child's heading word: a hit-thing child is made with 0 (`0x004ABF1A` zeroes the word
passed to [Shot_Create](functions/004B7CD0.md); [004AAC90](functions/004AAC90.md) stores it to
`+0xF4` and `+0x102` at `0x004AAD0B..0x004AAD15`); the hit-solid child passes the parent's `+0xF4`
(`0x004AB394`); the death-shot child's argument (`0x004AB4F9..`) is unread.

Not built: Guided Bolt's process 28 callback `0x004AF3A0` ([004AD1B0](functions/004AD1B0.md)),
whose tail rewrites the shot's `+0xF4` to the bearing to its target. It runs on the hit event
before this read (PLAUSIBLE; the wiring was not re-read).

## 2. The collapse is inside the hit (STRONGLY INFERRED)

[004B2CE0](functions/004B2CE0.md) calls [Creature_Collapse](functions/0048B770.md) at `0x004B2FB5`
inside the damage call, and contact damage ends with that call (`0x004AC589`). So the collapse's
picks and gold drop draw before the hit's heading write and before anything later in the shot's
tick. Damage from elsewhere still collapses in the `CollapseSystem` passes (ADR-0732).

## 3. The shot probes ahead (loop and rounding STRONGLY INFERRED; the creature case CONFIRMED)

With nothing struck, Shot_Collide takes the collide-table entry of definition `+0x6A`
(`0x004AC0DA`; types 0 and 5 up have no function and skip the step), halves each 1/16-unit
velocity word toward zero (`0x004AC138..0x004AC17F`) and loops twice:

- the same collision function as the start test; on a miss the accumulators add the half, and
  the position is set to start plus accumulated/16 when world vtable `+0x200`
  ([00509050](functions/00509050.md), a map-bounds test, not solidity) accepts it;
- on a hit (`0x004AC291`) the accumulated words are stored to `+0x104`, `+0x108`, `+0x10C` and
  the loop ends;
- both exits restore the start position (`0x004AC2AB`).

So a shot that finds a creature half a move ahead flies on at the distance covered so far and
strikes at the start of its next update.

Capture: on 1719 shots 1483 and 1484 go (-2352,-58928,2170) to (-1176,-29464,1085) and
(23068,-54275,2351) to (11534,-27137,1175), 933 and 210 units from the Wizard; on 1720 the newer,
1484, strikes and 1483 flies on at the halved velocity. The Thief's shot 1641 halves on 1863 and
strikes on 1864.

## Built (ADR-0739, hash/save 466)

- `ShotState.Heading` (saved, hashed, copied), set in `ShotRules.Launch` and `RequestTrail`;
  `ShotSystem` writes `Bearing.Distance(shot.Heading, 0x400)` to the creature after the damage.
  `ShotRules.Chain(onThing: true)` makes the hit-thing child with heading 0; the hit-solid and
  death-shot children keep the parent's.
- `CollapseSystem.Now`, called from `ShotSystem` right after `ShotRules.ContactDamage`.
- `ShotSystem.ProbeAhead` before `Move`: creature, trap, heart and door tests; `VelZ` is cut too.

Deviations: ours halves whole position units. The heart and door arms stand in for DK2's one
search predicate (`0x004B5800`) and have no capture. A hit in iteration 0 leaves velocity 0 and
`Move`'s standing arm then requests a hit-solid effect.

## Open: the order inside Shot_Update

DK2 runs Collide (`0x004AB320`), then the per-tick damage `0x004AC3C0` (`0x004AB329`), then the
mover `0x004B2030` (`0x004AB32E`). Ours runs `Move` before `AreaDamage` and `SquareDamage`, and
a shot that hits does not move in ours, while DK2 reaches the mover unless the shot was
destroyed. Not changed.
