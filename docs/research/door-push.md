---
systems: [doors-traps, creatures]
---
# Mechanic: a closed door pushes creatures off its tile

Source(s): DKII.EXE v1.70, pyghidra-mcp reads of `0x004A55F0`, `0x004B1F20`, `0x004B2030`, `0x004529A0`,
`0x0048EBF0`, `0x004B35C0`, the creature vtable `0x0066D524` and the loader `0x00492320` on 2026-11-05.
Original version: DK2 v1.70 (GOG)
Confidence: STRONGLY INFERRED unless a line says otherwise.
Implementation: `DoorPushRules`, `CreaturePhysicsRules`, `DoorRules.BlocksAt` (ADR-0679).
Tests: `DoorPushTests`.

Owner report (Level 6a): Imps standing on a barricade when its blueprint finishes stay trapped in it. In
DK2 they are pushed out and walk free.

## 1. The push: door update step 4

`0x004A55F0` after the owner test and before the barricade return, so **a barricade reaches it**. It runs
only when the open word `+0x10D` is 0 (closed) and the construction word `+0x109` is 0 (built). It does
not read the lock mode, the owner or health: a locked door, a door found dead this update and an enemy's
door all push. An open door and a blueprint do not.

1. For each of the four neighbours, in table `0x006BC350` order north, east, south, west: it is open when
   the navigation byte has no Solid bit (world slot `+0x208`) and `0x004529A0` at the neighbour's centre,
   height 0, answers no door body there. A neighbouring barricade or any closed door is therefore shut.
2. The push point starts at the door tile's centre. Each shut side moves it half a tile toward that side:
   north `y -= 0x800`, east `x += 0x800`, south `y += 0x800`, west `x -= 0x800` (a tile shut on both
   opposite sides nets zero on that axis).
3. For every thing on the door tile's chain with class 0 (a creature; no owner, state, flag or height
   test): `d = position - point`. With north and south both shut, `dy` is zeroed; with east and west both
   shut, `dx` is. `len = Math_Isqrt(dx² + dy²)`; if non-zero the vector is `(dx << 7) / len`, `(dy << 7) / len`
   (signed division, so length 0x80); if zero it is the creature's heading `+0xF4` through the sine tables,
   `x = (0x80 · sin) >> 16`, `y = -((0x80 · cos) >> 16)`, which the zeroing does not touch. The vertical part is 0.
4. `0x004B1F20` adds the vector to the creature's persistent velocity `+0x104/+0x108/+0x10C`. Nothing is
   moved here, and nothing is set on a state or a route.

It runs every update the door is closed, so the persistent velocity grows by about 0x80 a turn while the
creature stays. `0x004B1F20` has ten call sites: this one, the drop landing (state 76), the prisoner in a
cell (116), `0x004831D0` and `0x004833C0`, the slap reaction (two), the egg launch, the chicken flee (object
state 25).

## 2. The physical pass `0x004B2030`, persistent arm

The creature update calls it at `0x0048A204` for every creature, after the state handler and before the
steering gate. It sums the persistent and the temporary vectors (`+0x110`, cleared), and returns at once if
all three components are 0. Otherwise, for a creature:

- **Sub-steps.** `n = min(16, (len >> 14) + 1)` with `len` from `0x00555990`. Sub-step `i` is at
  `base + (i · v / n) / 16` per component, rounded toward zero.
- **Stop test per sub-step.** Blocked when the new tile is off the map (slot `+0x200`, `0x00509050`, an
  unsigned compare); or the creature's own tile byte is not 4 and the new tile's byte is 4 (a portal's
  centre hole); or terrain flag `+0x17B` bit 0 (Solid) of the new tile; or `0x004529A0` says the position is
  inside a door body and the creature was not already inside one. A creature that finds itself outside every
  door body stops counting as inside. A barricade's body never stops it here, whatever the terrain.
- **Reflect arm.** The creature's slot `+0x84` answers 0 and `+0x60` answers 1 (`0x00402AD0`, `0x00628BC0`).
  The pass tests the committed position moved along y alone by this sub-step's y: if `0x004B35C0` (on the map,
  not Solid) refuses it, or it is inside a door body and the creature was not, the y component of both the
  persistent and the working vector becomes `-(v / 2)`, else the x component does. The base becomes the
  last accepted point, the remaining count `n - i`, and the walk restarts at 1.
- **Commit.** With `m = |vx| + |vy|` of the persistent vector, it commits when `m` is 0 or above `0x7F`
  and the creature may step (`0x0048EBF0`, options 1 and 2 clear, the respect-doors option from its last
  route word) from its tile to its own tile with a zero cost, or to the final tile with a non-zero cost. A
  creature inside its own locked door cannot stay, so it commits. On commit the position is set
  (`0x004B5560`) and **persistent x and y are multiplied by `(0x1000 - word +0x11E) / 0x1000`**,
  truncated toward zero (the factor is 0x800 if the word exceeds 0x1000); z is kept.
  The body position becomes the entity's `<< 4`. Otherwise both horizontal components are zeroed.
- **`+0x11E`** is the creature definition's `+0x690` (constructor `0x00487349`), loaded at `0x004936F3` from
  file `+0xEAC` while its presence dword is non-zero. Every record of the shipped `Creatures.kwd` holds 327 (about 8 %).

## 3. What it gives

A creature 0x300 off the centre of a barricade across an east-west passage leaves the tile on the 24th
turn; one 0x10 off the centre leaves on the 33rd (the first push is added at the end of turn 1 and the pass
spends it from turn 2). At four turns a game second that is 6 to 8 game seconds. The owner remembers "a
couple of seconds", which is a recollection and not measured; the binary's numbers are as read.

## Built and not (ADR-0679)

Built: the push (§1), the persistent vector's horizontal arm (§2), `AirFriction` read from the file
(`CreatureDef.AirFriction`), state and snapshot fields `PhysicalVelocityX/Y`, the call position (the
creature's movement step, doors after the creatures).

Not built, and ours differs:
- The vertical component and the temporary vector are not in the pass: the Imp's creation fall and party
  arrival keep their own height code, and the flee step back its own slide (`FearRules.Slide`).
- Creature height is `PosZ` (zero floor), so a barricade body test `z < 0x1801` is true for any grounded
  creature and for none that is high; DK2's floor height is not read.
- Other callers of `0x004B1F20`, the lock mode 3 shove (`0x004A55F0` step 8, `+0x104/+0x108` written
  directly) and the Secret Door reveal. The pass's terrain tests (stop test, reflect arm, commit gate) are built.
- Whether the presence dword for file `+0xEAC` is set in every shipped record is not read; the value is
  taken from the file for all of them.

## 4. Deviation: a creature on the push point (ADR-0682)

Re-read for the report that an Imp finishing a barricade is pushed into a wall and stays pressed against it.
Step 4 and the pass match the binary (sections 1 and 2); no further terrain test exists in either, so DK2's
push can face a wall in one case only: the creature on the push point itself, where `len` is 0 and the vector
is the heading `+0xF4` (section 1, item 3), with the zeroing leaving nothing to say which way is open. The
reflect arm then turns the component back each turn and the persistent vector never empties: the creature
stays against the wall. In every other position the vector points away from the shut sides, so toward the
open ones.

Ours reached it from the Imp's arrival. The Imp walks to the tile's exact centre, and its steering turns to
the bearing of a zero vector (north) there, so an Imp finishing a barricade in an east-west passage was
pushed north into the wall (reproduced with the crate flow, 2026-11-05). Whether DK2's Imp arrives on the
exact centre, and with which heading, is UNKNOWN; its positions come from the same steering arithmetic.

Ours differs from the binary there (`DoorPushRules.TowardAnOpenSide`): when the heading mainly faces a shut
side and another side is open, the push goes to the open side nearest the heading (ties north, east, south,
west). The heading stands when it faces an open side or every side is shut. The owner's expectation that
DK2 always pushes a creature somewhere it can move is the reason; the binary does not say so.
Not changed: a push toward a neighbour that is open but not enterable (water, lava for this creature)
is DK2's, and the commit gate then keeps the creature on the tile.
