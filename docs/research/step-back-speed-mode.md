---
systems: [combat, creatures, simulation-core]
---
# A step back's speed mode outlasts the step

## Cause

With [the shot picked at 1647](possessed-pick-heights.md) the Level 5 seed-1
replay follows DK2's Wizard 436 through 1657. At 1658 both begin an approach
from (65421, 141450): DK2's Wizard moves 419 units, ours 1098.

CONFIRMED in the capture: creature `+0x3BB` (MovementMode) and the speed word at
`+0x206` change together. Walking it is 0 and 9824; the fight's entry at 1571,
1605 and 1622 sets 1 and 17680; the step back at 1653 sets 2 and 6544. Mode 2
then holds through the approaches at 1658 and 1679 to 1693 and the stands
between, until the fight ends at 1703 and mode 0 returns. `+0x3BF`, which the
setter raises on every call, is 1 at 1653 and never again through 1702: no
speed call was made in those turns, the returns from an approach to state 199
at 1659 and 1694 included.

## Binary

STRONGLY INFERRED from reads on 2026-11-10:

- [The speed setter](functions/004887D0.md) is the only writer of the mode.
- [spFighting](functions/0045F030.md)'s stand-off arm: with no action running,
  a target within a quarter of both the attack range and the sight range and a
  step the test `0x00460DA0` allows, it calls the setter with (2, 0) at
  `0x0045FFC9` and then gives the impulse; outside either quarter (1, 0) at
  `0x0045FFF6`. A running action or a refused step makes no call.
- [The flee state](functions/004602E0.md) does the same: (2, 0) at
  `0x00460806` before its step back, (1, 0) at `0x00460880` beyond the
  stand-off distance; a refused step makes no call.
- [State 199's handler](functions/0045EB20.md) calls (1, 0) only when the
  state's entry stamp is the current turn. [Every accepted state change](functions/00476D30.md)
  stamps the turn, but a state set from inside another handler (the approach's
  return, the flee state's fight-on) is first run a turn later, when the stamp
  no longer matches.

The two (2, 0) calls were already recorded in combat-fear.md and
combat-spells.md; what was missing is that the mode persists.

## Implementation (ADR-0729, hash/save 458)

`CreatureState.SpeedMode` (walk, run, shuffle) replaces the running flag in the
hash, save and copy; `Running` remains as the run mode's accessor, and clearing
it is the walk. Both step backs set the shuffle. The run speed is set where
ours already mirrors the handler's gate for the battle message: in the fight
update, when the creature is in state 199 and entered it this turn. The
engagement and the flee state's fight-on no longer set it; a creature with no
state still takes it at the engagement. The body's cap takes the definition's
shuffle speed in mode 2.

Tests cover the cap, the accessor, hash/save/copy and the entry-turn gate. No
unit test stages either step back's write: those rest on the binary read and the
control below, which applies all the rules together and so cannot separate
them. The turn a handler sets the speed is not modified by water or possession: [speed-set-turn.md](speed-set-turn.md) (ADR-0736). The classic view shows the walk clip for the shuffle; DK2's animation
chooser reads the mode and picks art slot 0x26.

## Controlled result

`controlled458-v6` (same 27 inputs; the 27 earlier diagnostic clauses plus
four pre-1400 guards on the new speed rules; the fight-on's removed write is
unguarded) matches 5360 ordered
sites/preseeds through 1719, versus 4783 through 1685. Imp 440 matches 320/320
native position and Health samples from 1400; the Wizard matches 203 of 207,
first differing at 1715 in Health (DK2 1650, ours 1050). Next is 1720 draw 3:
native Creature.cpp:6461 where ours has the next chicken draw.

Evidence: `F:/DK2Evidence/studies/q712-shot-height-pick--2026-11-10--001`
(`controlled458-v6-draw-verification.json`).
