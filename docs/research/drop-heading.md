---
systems: [keeper-spells, creatures, combat]
---
# A drop heads the creature by the packet's angle

## Observation (CONFIRMED, Level 5 seed-1 packets and capture)

- The drop packets of turns 1688 to 1693 carry `0x03E80000` in their third
  word and those of 1806 to 1884 carry `0x00860000`: angles 1000 and 134.
- The four Warlocks of the first group read heading (`+0xF4`) 659 in the drop
  turn's frame and keep it through 255 and 257. A Warlock of the second group
  reads 0. A Warlock turns 341 a turn: 1000 less 341 is 659, and 134 reaches 0.
- On its engagement turn each of the four goes from 659 to 0, and the next
  turn faces its target and starts the cast.

## Reading (STRONGLY INFERRED, binary 2026-11-10)

- [004C0EE0](functions/004C0EE0.md) passes the upper word of keeper `+0x196`
  masked to 11 bits; [004BC710](functions/004BC710.md) writes it to the
  dropped thing's `+0xF4` and `+0x102` at its tail (`0x004BCA08`), creature or
  object, after stopping a creature's body at its own point. The packet's third
  word reaches `+0x196` through [004C09F0](functions/004C09F0.md). `+0x102` is
  `LastAngle`, copied from `+0xF4` on every update, so that copy is transient
  ([death-fall-heading.md](death-fall-heading.md)).
- The turn toward 0 on the drop's turn is the stopped route of
  [combat-stop-route.md](combat-stop-route.md), read link by link. It holds for
  non-Imps and is not CONFIRMED: the game was not run.
  1. Hand_Drop stops the body at its own point (`0x004BC7FB`) before the tail
     writes the angle, so no turn happens inside it. It runs as hand state 4's
     enter callback inside the packet dispatch (`0x0050A527`), before the
     entity sweep (`0x0050A7D7`).
  2. State 76 steers: its table row has flag byte `0x05`, and bit 0 is the
     steering bit the update tests at `0x0048A23B..0x0048A248`
     ([0047BBE0](functions/0047BBE0.md)). The gate order at
     `0x0048A209..0x0048A248` is the prisoner look latch `0x004739C0`,
     possession, type id 1 (the Imp, whose steering is skipped), the flag bit;
     then `0x004D5AF0`, `0x004D59A0`, Body_Integrate `0x004D5A40`,
     `0x004B35C0` and the commit `0x004B5560`
     ([00488930](functions/00488930.md)).
  3. [Follow Path](functions/004D3C40.md), creature arm: with no route or the
     finished bit already set it brakes and never turns. On the last waypoint
     it sets the finished bit in that call and still calls the turn helper
     toward the path target, a self-route's own position. So the turn happens
     once, on the first steering call after a fresh stop; later calls brake
     without turning, which is why 255 and 257 keep the heading.
  4. [Math_Atan2](functions/0065FB54.md) of (0, 0) is 0, so a self-point has
     bearing 0.
  5. [The turn helper](functions/004B2AE0.md) turns by min(rate, remaining).
     The Warlock's file `TurnRate` 1365 over variable 19 (4) is 341; the
     Wizard's is the same. 1000 to 659 and 134 to 0 follow, and from 1000 the
     shortest way to 0 is decreasing (1000 is below 1024).
- The engagement turn's 659 to 0 is two rate-limited turns in one update
  (STRONGLY INFERRED). The scan `0x0045E9F0` (call at `0x00489AC7`) runs before
  the state dispatcher (`0x0048A030`) and steering after it. Entering 199 runs
  [CombatCategory_Enter](functions/0047A810.md), whose first act is
  `Creature_StopMoving` (`0x0047A817`): a fresh unfinished self-route. 199's
  handler tail-calls [0045F030](functions/0045F030.md), which in reach turns
  toward the target (`0x0045FB3E`) and returns 1 while an angle remains: turn 1.
  Steering then turns toward the self-point, bearing 0: turn 2. The result is 0
  only if turn 1 leaves the heading at or below 341, so the target's bearing
  must be at or below 341 for all four Warlocks.

## Built (ADR-0733, ADR-0736, hash/save 463)

`DropCommand.Heading` carries the angle; `PacketLog` fills it from the packet.
`HandSystem.Drop` sets the heading and, for a creature that is not an Imp,
turns it once toward 0 by the creature's rate, standing in for the fall turn's
path step, which a falling creature does not take in ours. An Imp keeps the
angle. A command without an angle keeps the heading: the view's.

`DropCommand.ByComputerKeeper` gives the computer keeper's tile drops the
tile's centre plus `0x100` on each axis and angle 0; six sites in `src/DK2.AI`
use it. Ten call sites of [004BC710](functions/004BC710.md) were read to pass
that pair: `0x004F95F4` ([004F9520](functions/004F9520.md)), `0x004F96EA`,
`0x004F98D0`, `0x004F9A2E`, `0x004FAAFE`, `0x004FE280`, `0x00500BFF`
([005006D0](functions/005006D0.md)), `0x005037D4`, `0x004FF589`
([004FF260](functions/004FF260.md)) and `0x004FFA1A`
([004FF5C0](functions/004FF5C0.md)). The base value at `0x004FE8AD` was not
read. `0x004F9B5F` drops at the other creature's own position, and ours'
drop-on-a-creature form is unchanged. `KeeperThreatResponder`'s matching DK2
site is not identified (candidates `0x004FAAFE`, `0x004FE280`).

Deviations of that build from the reading:

- DK2's computer keeper passes height 0. Ours gives its drops the hand's
  one-turn fall.
- The turn's bearing is 0 only while the creature's x and y still equal the
  stop point. State 76's own arms can move it before the steering (the Torture
  snap, the Combat Pit scatter, the Heart push:
  [hand-drop-landing.md](hand-drop-landing.md)). Ours assumes 0 always, which
  matters only on those room tiles.
- Body_Integrate is skipped when the body's maximum speed (`+0xE2`) is 0
  ([004D5A40](functions/004D5A40.md)); ours does not check it.
- DK2 turns inside the creature's own update on the drop turn, ours in the
  command: the same unless something reads the heading between the two.
- Not checked: whether an action at pick-up can turn an Imp on the drop turn.

## Unknown

- The engagement turn's first-turn bearing: that the target's bearing is at or
  below 341 for all four Warlocks is a premise nobody has read. The target
  positions are not in these notes; the capture has them.
- The flag byte of states 199 and 200 (3, from creature-state-table.md): the
  value stored at `0x0047F0F2` was not read from the listing.

## Control

`F:/DK2Evidence/studies/q712-drop-heading--2026-11-10--001`,
`controlled462-v2`: the four Fireballs leave on DK2's turns (1715, 1717 three
times), the Wizard collapses on 1720 with its fall-heading draws in place, and
the ordered draw prefix grows from 5360 through 1719 to 5471 through 1722.
