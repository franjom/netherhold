---
systems: [combat, creatures, simulation-core]
---
# Heights in the possessed picks, and a shot's height

## Cause

After the [possessed-melee control](possessed-melee-draw.md) the Level 5 seed-1
replay still differed from DK2 before its draw boundary: at 1647 the possessed
Imp 440 swung at the Wizard 436, where DK2's action target is thing 1463.

CONFIRMED in the capture: 1463 is the Wizard's Fireball. It is in frame 1646 at
(68358, 138924), height 5316, Health 59, and gone at 1647. The Imp's blow is 59,
so the swing destroys the shot: the Imp keeps 625 Health through 1660 and the
Wizard keeps 1500 until the second swing at 1653 takes it to 1441. Ours struck
the Wizard twice and let the shot hit the Imp.

Heights in the same frames: creatures standing on floor, traps and doors are at
4096; most objects are at 4096. The act packet's pose height is 5768 and the
move packets carry 1596, with the creature at 4096 after each.

## Binary

STRONGLY INFERRED from whole reads on 2026-11-10:

- [The melee pick](functions/00515420.md) hands each chain thing's position
  triple at `+0x1A` to the cone test and to
  [the replacement test](functions/00516670.md), which subtracts the pose from
  all three words of the candidate and of the held thing (`+0x22` is height).
- [The spell scan](functions/00515520.md) does the same for creatures, traps,
  doors and type-94 objects.
- [The launch](functions/004AB8A0.md) takes the bearing of (horizontal distance,
  shot height less aim height) and stores speed << 4 times its cosine, shifted
  16, as the third velocity word; a lobbed shot adds 0x400. The horizontal words
  are not scaled by the pitch.
- [The targeted cast](functions/00498870.md) aims at the target's position
  raised by half its level-scaled height; the lead is added to x and y only.
- [The follower pick](functions/00515D50.md) scores the same three-word
  difference from the pose.

With the shot read 4096 lower than DK2 has it, its direction from the pose
points steeply down, its aim dot is 0.28 of the Wizard's 0.88, and the nearer
shot does not replace the Wizard. At 5316 its dot is 0.87 and it does.

## Implementation (ADR-0728, hash/save 458)

- The melee, spell and follower picks take thing heights in the pose's frame: `FloorHeight` (4096) plus a
  creature's or shot's height above the floor, and the floor for traps, doors,
  objects and pillars.
- `ShotState.PosZ` and `VelZ` are hashed, saved and copied. A creature's
  targeted cast starts the shot at its height plus the slot's origin height and
  launches it with the aim height; each tick adds a sixteenth of the vertical
  velocity, truncated toward zero. Two captured shots stand at 5316 and 5373 a
  turn after launch; ours give the same. A third keeps a constant 226 a tick
  over five turns.
- A possessed move takes the packet's height less the floor and never below it.

Not built, and named in Q398: gravity (a shot with mass keeps its height), the
floor stopping a shot, the overlap test's height, aim heights for doors, traps,
hearts and free aim, the launch's table-quantised horizontal velocity, a floor
that is not 4096 (creatures in the frame stand at 2662 to 3096 on lower ground,
and the setter raises to the cell's floor), and object heights other than the
floor (15 objects stand at 3584; an airborne egg or flung pile is read at it).
Other shot makers leave the shot at floor height.

## Controlled result

With this change alone (`controlled458`, same 27 inputs and 27 diagnostic
clauses) the replay picks the shot at 1647, leaves the Imp at 625 and brings
the Wizard to 1441 at 1653; the Wizard's state, position and Health match DK2
through 1657, and the ordered-draw prefix stays at 4783 through 1685. The
Wizard's difference at 1658 is [the step back's speed mode](step-back-speed-mode.md),
which carries the prefix on.

Evidence: `F:/DK2Evidence/studies/q712-shot-height-pick--2026-11-10--001`.
