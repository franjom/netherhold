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
pick there, and the lethal Fireball's hit writes the creature's heading after
the collapse's picks, inside the same turn
([shot-probe-and-hit-heading.md](shot-probe-and-hit-heading.md) sections 1 and 2).
The rotation read below could not explain the 148. Ours stores the pick and then
the hit's write.

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
  its flag byte is 0, so the update's steering skips it (its hold and step:
  the next section). The decompile of the creature update has no `+0xF4` write
  and no call to the turn helper, and the turn helper's callers are fight,
  travel and state code.
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

## State 216 holds for the collapse's action, then steps back (STRONGLY INFERRED; the Wizard's displacement CONFIRMED)

Handler [00459850](functions/00459850.md), read 2026-11-11:

- It returns 1 while [00496D00](functions/00496D00.md) says an action runs (creature `+0x3A9`
  other than 0 and `0x15`). Otherwise it calls SetState(217) through
  [00476D30](functions/00476D30.md); a refusal returns 0 with no step and no stamp.
- The step: vy is vector 3's y of the definition ([00494650](functions/00494650.md)(3), definition
  `+0x6EC` + 36 + 4; file offset `0x1297`; the shipped Wizard 3223, Thief 3715, Goblin 2166).
  x' = x + (S[h] * -vy >> 16) and y' = y - (C[h] * -vy >> 16), the sine and cosine tables at
  `0x006C1488` and `0x006C1C88`, h the creature's heading `+0xF4`.
- It is committed through [004B5560](functions/004B5560.md) and copied to `+0xF6` only when the
  destination's terrain is not solid (terrain `+0x17B` bit 0) and its navigation byte (world
  `+0x9F2`) equals the creature's own tile's (`0x00459944..0x0045996B`). The step moves the
  entity only; the steering body (creature `+0x124`) stays where the creature collapsed
  ([steering-body-retention.md](steering-body-retention.md#state-217-leaves-its-body-where-it-fell-adr-0745-hashsave-471)).
- Then state `+0xAB` is set to the turn plus 10, step taken or not (`0x004599A5..0x004599B0`).
  217's handler [004599D0](functions/004599D0.md) compares it with the turn at `0x00459A34` to hold
  off its two-phase fall machine (state `+0xA7`, the z sink, effect `0x149`). None of that
  machine is built, so the stamp is not built either.

Capture: Wizard 436 is in 216 from 1720 and in 217 on 1726, moving (67803,135760) to
(66542,132794). Thief 434 is in 216 on 1864 and 217 on 1870 and does not move; its destination
tile x is 19 against its own 20, so the tile was solid or the navigation byte differed, and the
capture cannot say which. Health stays 240 through 216 and counts down from 217. Unexplained: the
Thief's z drops 4096 to 3924 on 1865, during 216.

## Built (ADR-0732, hash/save 461)

- `DeathFallRules.Enter` makes the two picks; the first becomes the heading.
- `CreatureHealthRules.Apply` marks a creature it brings to zero health or below
  (`CreatureState.CollapseDue`, hashed and saved; every damage path, ADR-0765,
  after `StatusEffectRules.Hurt` alone, ADR-0732). `CollapseSystem` collapses
  the marked after the creature sweep, after the shots and after the traps, so
  the picks and the gold drop draw before the passes that follow. The health
  pass's tail collapses what its own terrain and drain damage marked.

Deviations: DK2 collapses inside the damage call. Ours does for a shot's hit
(`CollapseSystem.Now`, ADR-0741) and a slap (ADR-0765); other damage that empties health collapses in
the passes above, so a blow struck during the creature sweep draws after the
sweep. Not built: the
definition's rotation (`+0x766`, 0 in every shipped record), the `+0x102` word
(drawn and dropped, transient), the position from `0x0048F690`. A tile off the
map blocks (ours).

## Built since (ADR-0741, hash/save 468)

- `CreatureDef.DeathFallStepY` (the `KwdCreatures` reader and writer), and
  `DeathFallRules.Fall` in `CreatureStatusSystem`'s per-creature step: it waits for
  `ActionUntilTick`, enters 217, then steps back as above. `CreatureHealthSystem.Dying` no
  longer forces 217, does not count the dying window in 216 (the countdown starts when 217 is
  entered) and calls `Fall` as an adapter for simulations assembled without the creature sweep.
  `WorldResync` enters 217 directly for a creature DK2 has in 217.
- Since ADR-0745 (hash/save 471) the step leaves the body: `SyncMovedBody` skips a creature in 217.
- Not built: the `+0xAB` stamp. `Fall` does not test `CarriedByCreatureId` or `InHand`, and
  `ActionUntilTick` is not cleared when 216 is left by a rescue.

## Hypothesis and unknown

`DeathFallRules.FallEndsAt`, the turn the handler finds the action over, is the collapse turn
plus 1 plus the hold of art slot 7 (HYPOTHESIS). Both captured creatures were felled by a shot
after the creature sweep and both have hold 5, so "+1 + hold" cannot be told from "+6", and a
creature felled before its own update is in no capture. The word `+0x102` still holds the
second pick, 1036, in the Wizard's frame: the Wizard did not update after the collapse that
turn, which fits the hit's write coming inside the same turn.

## Control

`ThePicksReplayTheLoggedDrawsForALayoutFittedToTheLog`
replays the 29 logged draws from the logged seed, for a layout (only the south
open) fitted to the sixteenths the log rejects; the real tiles round the Wizard
were not read, so it pins the redraw loop and the two picks, not the blocking. At hash 461 the controlled replay still stopped at 1720 draw 3, the lethal
Fireball landing a turn late; with [the drop's heading](drop-heading.md)
(hash 462) the 29 draws match in place and the prefix runs through 1722. The
current prefix is in Q712.
