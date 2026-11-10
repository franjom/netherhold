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
  word reaches `+0x196` through [004C09F0](functions/004C09F0.md). The `+0x102`
  copy is not modelled; its readers are unread.
- HYPOTHESIS (fits the frames, not read here): the turn toward 0 on the drop's
  turn is the stopped route of [combat-stop-route.md](combat-stop-route.md), a
  newly finished self-point having bearing 0 for Follow Path. The engagement
  turn's 659 to 0 would then be two steps of 341: the fight handler's turn
  toward the target, whose bearing is near 0 for all four, then the stop's.

## Built (ADR-0733, hash/save 462)

`DropCommand.Heading` carries the angle; `PacketLog` fills it from the packet.
`HandSystem.Drop` sets the heading and turns it once toward 0 by the
creature's rate, standing in for the fall turn's path step, which a falling
creature does not take in ours. A command without an angle keeps the heading:
the computer keeper's drops (their angle is unread) and the view's.

## Control

`F:/DK2Evidence/studies/q712-drop-heading--2026-11-10--001`,
`controlled462-v2`: the four Fireballs leave on DK2's turns (1715, 1717 three
times), the Wizard collapses on 1720 with its fall-heading draws in place, and
the ordered draw prefix grows from 5360 through 1719 to 5471 through 1722.
