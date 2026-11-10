---
systems: [creatures, combat, simulation-core]
---
# A collapse picks its fall heading from the world stream

## Observation (CONFIRMED, Level 5 seed-1 draw log)

Turn 1720: after two Imp draws DK2 logs 29 draws at `Creature.cpp` line 6461,
before the turn's object draws. The hero Wizard (tag 436) is in state 216 in
that frame with health 240, the dying countdown. Decoded with the stream's
rotation, the values fall only in the sixteenths the rule below rejects until
draw 18 (1041) and draw 29 (1036); 1036 is the creature's `+0x102` in the
frame. Its `+0xF4` reads 893, not the first pick's 1041: the entry writes the
pick there (read), so something later in the turn moved it; the rotation read
below cannot. Ours stores 1041; that value is unverified.

## Reading (STRONGLY INFERRED, binary 2026-11-10)

- The health routine `0x004B2CE0` loads the countdown into health and calls
  [0048B770](functions/0048B770.md) inside the damage call (`0x004B2FB5`),
  which enters 216 through [004596D0](functions/004596D0.md). The other entry
  [00459560](functions/00459560.md) has five callers (the creature update's
  dying arm, state 76, two idle choosers, a trigger action).
- Both entries call [0048F410](functions/0048F410.md) twice, into `+0xF4` and
  `+0x102`, then set a position from `0x0048F690`, zero the velocities and drop
  the carried gold.
- The pick: mode 0 off a Temple or Combat Pit, else 2 on a floor-slot cell and
  1 otherwise. A neighbour blocks by its terrain's solid bit; inside a room in
  mode 2 the rim blocks and in mode 1 the interior. North, east, south and
  west all blocked: the heading stays, no draw. Otherwise `random(0x800)` is
  drawn until `((r + 0x20) & 0x7FF) >> 7` is even and its direction is open; a
  diagonal is blocked by its own tile or either orthogonal tile beside it.
- The rotation (`0x0048F5EA..0x0048F66E`): the result is `r & 0x7FF`; when the
  second argument, the flag, is 0 it is `(r - ((def+0x766 << 8) & 0x7FF)) &
  0x7FF`. Both entries push 0, so both picks are rotated. The definition's
  `+0x766` is one byte, stored unconditionally from file `0xF54`
  ([00492320](functions/00492320.md)), and is 0 in all 46 records of the shipped
  `Creatures.kwd`, Warlock and Wizard included; per-level records were not
  checked. The rotation is `(byte & 7) * 256`, so it never gives 148, the
  difference between 1041 and 893.
- [004596D0](functions/004596D0.md) after SetState(216): pick 1 into `+0xF4`,
  pick 2 into `+0x102`, the position from [0048F690](functions/0048F690.md),
  the previous-position copy, `+0xA7` 0, the velocities zeroed, the gold
  dropped and `+0xAB` stamped. [0048B770](functions/0048B770.md) read to its end:
  it ends the running action, begins action 62, sets `+0x3E6` bit 21 and raises
  creature event 0x10, with no heading write.
- State 216's handler ([00459850](functions/00459850.md)) writes no heading and
  its flag byte is 0, so the update's steering skips it. The decompile of the
  creature update has no `+0xF4` write and no call to the turn helper, and the
  turn helper's callers are fight, travel and state code.
- `0x0048F690` pushes a creature to exactly m from a blocked tile edge when it
  lies within m of it, with m = half the footprint radius plus 0x80, testing
  the neighbour and its diagonals; it is not a general clamp.
- `+0x102` is `LastAngle` ([004B27E0](functions/004B27E0.md)): the creature
  update copies `+0xF4` into it on every update (`0x00489FE6..0x0048A00E`),
  before the state dispatch. The collapse's second pick, and the drop's copy
  of its angle, are therefore overwritten at that creature's next update; ours
  dropping the word matters only for the draws, which it makes. Dead bodies
  copy it too; the thing checksum sums it with the position copy and the
  velocities (a net-sync style hash). That it feeds render interpolation is a
  HYPOTHESIS: the renderer was not read.

## Built (ADR-0732, hash/save 461)

- `DeathFallRules.Enter` makes the two picks; the first becomes the heading.
- `StatusEffectRules.Hurt` marks a creature it brings to zero health
  (`CreatureState.CollapseDue`, hashed and saved). `CollapseSystem` collapses
  the marked after the creature sweep, after the shots and after the traps, so
  the picks and the gold drop draw before the passes that follow. The health
  pass still collapses whatever reaches zero another way.

Deviations: DK2 collapses inside the damage call, so a blow struck during the
creature sweep draws there and ours after the sweep. Not built: the
definition's rotation (`+0x766`, 0 in every shipped record), the `+0x102` word
(drawn and dropped, transient), the position from `0x0048F690`. A tile off the
map blocks (ours).

## Unknown

The Wizard's `+0xF4` of 893 in the frame (the first pick is 1041). The word
`+0x102` still holds the second pick, 1036, though an update after the collapse
would have copied `+0xF4` over it (`0x0048A007`): so the Wizard did not update
after the collapse that turn, and yet something changed `+0xF4` by 148. A write
watch on `+0xF4` of tag 436 in turn 1720, or the capture's per-call heading
trace, would settle it, and whether the captured Wizard has a level-specific
`+0x766` (a value of 148 is impossible anyway).

## Control

`ThePicksReplayTheLoggedDrawsForALayoutFittedToTheLog`
replays the 29 logged draws from the logged seed, for a layout (only the south
open) fitted to the sixteenths the log rejects; the real tiles round the Wizard
were not read, so it pins the redraw loop and the two picks, not the blocking. At hash 461 the controlled replay still stopped at 1720 draw 3, the lethal
Fireball landing a turn late; with [the drop's heading](drop-heading.md)
(hash 462) the 29 draws match in place and the prefix runs through 1722.
