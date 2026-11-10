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
pick there (read), so something later in the turn moved it, or the unread
rotation applies. Ours stores 1041; that value is unverified.

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

## Built (ADR-0732, hash/save 461)

- `DeathFallRules.Enter` makes the two picks; the first becomes the heading.
- `StatusEffectRules.Hurt` marks a creature it brings to zero health
  (`CreatureState.CollapseDue`, hashed and saved). `CollapseSystem` collapses
  the marked after the creature sweep, after the shots and after the traps, so
  the picks and the gold drop draw before the passes that follow. The health
  pass still collapses whatever reaches zero another way.

Deviations: DK2 collapses inside the damage call, so a blow struck during the
creature sweep draws there and ours after the sweep. Not built: the
definition's rotation (`+0x766`, unread), the `+0x102` word (drawn and
dropped), the position from `0x0048F690`. A tile off the map blocks (ours).

## Control

`ThePicksReplayTheLoggedDrawsForALayoutFittedToTheLog`
replays the 29 logged draws from the logged seed, for a layout (only the south
open) fitted to the sixteenths the log rejects; the real tiles round the Wizard
were not read, so it pins the redraw loop and the two picks, not the blocking. The controlled replay
(`F:/DK2Evidence/studies/q712-death-fall-heading--2026-11-10--001`,
`controlled461-v3`) still stops at 1720 draw 3: ours lands the lethal Fireball
on 1721, a turn late, because the Warlock's cast starts a turn late (Q712).
