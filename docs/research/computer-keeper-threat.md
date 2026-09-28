# Computer keeper threat and retreat audit

Independent read of `DKII.EXE` v1.70 (MD5
`2f3bbc21eae8c3aa468136baa303036d`), Ghidra 12.1.4/PyGhidra, read-only project,
2026-09-28. Behaviour below is **STRONGLY INFERRED** from the binary; none of
these edge cases has an original-game observation. Addresses are virtual.
Message producers and lifetime: `computer-keeper-messages.md`.

## 1. Threat messages are not interchangeable

Both readers refuse a message when three event tasks are queued. The player-list
walk records the last enemy with nonzero local strength as the attacker.
`0x004BF390` tests a different player tag and a zero alliance byte, while
`0x004BF3D0` accepts self or a nonzero alliance byte. Neither excludes heroes or
neutral players by id.

| Rule | Position messages, `0x004F9B90` | Room message, `0x004FA000` |
|---|---|---|
| Location | Data1/Data2 tile coordinates | Room tag in Data2; room coordinates at `+0x27/+0x29` |
| Imprisonment reroll | Only after finding an owned Prison | No Prison check |
| Reroll scope | Every enemy-owned creature, including distant ones | Same |
| Enemy/ally radius | 10 / 4 tiles | 5 / 5 tiles |
| Include traps | Keeper's `TrapsStartFights` flag, for both sides | Always disabled |
| Drop candidates | Owned, in bounds, navigation-solid bit clear; inclusive ±5 square | Same candidate predicates and square |
| Candidate score | Squared tile distance | Erroneous expression described below |
| No reinforcement needed | Cancel the reserved event | Still enqueue an event with residual zero |

Each reroll compares `random(100) < ImprisonPercentage` and sets or clears the
keeper's bit in creature word `+0x3F7`. Its scope is not limited to the fight.
The per-player bit uses player id minus one, not the player's object tag.

The room reader's candidate score is `(targetX-x)^2 + y*targetY*(targetY-y)`.
At `0x004FA278` and `0x004FA27D`, instructions multiply the candidate Y by
target Y and then their difference; `0x004FA284` squares only the X difference.
The comparison is signed and strict, with initial best `0x19000`, despite these
being integer tile coordinates. For target `(20,20)`, candidate `(20,25)` scores
−2500 and beats `(20,20)`'s zero. This arithmetic example is a prediction,
not an observed drop. The position reader really does use squared distance.
Both scan Y then X in ascending order and retain the first equal-score tile.
A chosen zero X or Y is subsequently treated as no drop site.

Both compute integer `battleSuperiority * enemyStrength / 100 - allyStrength`.
With no drop site, removal enabled, nonnegative ally strength and positive
deficit, the event's signed 16-bit residual is the negative ally strength.
Otherwise the room reader still queues residual zero; the position reader
cancels the reservation. Do not suppress all zero-residual events: event 1
dispatches zero through its reinforcement arm.

## 2. Local strength and readiness

`0x004FAC40` walks the player's creature list, excluding thing status `+0x16 == 2`.
It applies a strict radius test, shifting each squared fixed-point axis delta
right by 12 separately before summing. The optional trap walk uses the player's
trap list, excludes the same thing status and trap byte `+0xF6 == 2`, then adds
descriptor threat `u16 +0x8C`. Describing this as an ammunition test is not
established by this routine.

Creature threat is descriptor `u16 +0x698` at level 1. Other levels multiply it
by the signed percentage at `[0x006E4154] +0x105 +2*level` and divide by 100
(`0x004FBDB0`, also inlined in the strength and event routines). It does not
scale threat by current health.

Readiness at `0x004FAB10` counts nonworkers by descriptor bit 0, requires strictly
more than `IAMinimumCreatures`, and then checks level. When the below-level
count is at most the target level and less than the total, it grants each
below-level nonworker one ordinary level-up before testing again. It does not
exclude low-health, captive or fighting creatures in this count, and it does
not send creatures to Training. The level-up can repeat on subsequent calls
until the test passes. The grant path calls `0x0048B120` directly.

## 3. Response event and withdrawal gate

`0x004FE0B0` uses a signed 16-bit residual, so narrowing and subsequent word
arithmetic matter. Its negative arm withdraws creatures in list order from a
strict five-tile radius; its nonnegative arm reinforces. Eligibility and the
movement search are detailed in `computer-keeper-handlers.md` §10.

Withdrawal excludes a creature when `LastDroppedCounter <= 300` **and the game
turn is greater than 300**. The instruction check is at
`0x004FE189..0x004FE1A1`; it does not exclude the first 300 turns. The field is
the dumper's `LastDroppedCounter`, not time since the last pickup.

The reinforcement branch subtracts the selected creature's threat once the
interaction predicate succeeds, even when pickup or drop fails. The subtraction
at `0x004FE90B` lies after the hand-success branch. More surprisingly, when the
payload tile is already owned, `0x004FE5AA` jumps to `0x004FE85C` with the move
flag still zero (`EDI` cleared at `0x004FE54E`). It skips pickup/drop but still
debits the threat. Only a replacement site found after ownership differs sets
the move flag. This original no-move branch must not be silently repaired.

## 4. Automatic retreat is separate from an event withdrawal

`0x004FF260` scans owned creatures while allowance remains. These are alternative
eligibility branches, not one combined health-and-level gate:

- A Combat Pit fighter (creature flag `+0x3E6 & 0x8000`) below half maximum
  health is selected regardless of the target skill level.
- A creature below `BARetreatHealth` percent in state category 22 is selected
  before the dropped-counter restriction.
- Other non-pit creatures below that percentage must pass the dropped-counter
  restriction, be outside categories 6/22, outside the hand and state 76, pass
  `0x0048F350 == 0`, and have no room tag at creature `+0x3B9`.
- A remaining Pit fighter is selected when its level is at least
  `IACreatureSkillLevel`, even if healthy.

After selection, an owned Heart room and interaction permission are required.
It attempts pickup and, if in hand, drops at a randomly selected own Lair or
the heart when no Lair exists, with fractional tile offset `0x900`. Finding
the Heart consumes one allowance even if the interaction fails. No Heart
means no allowance debit. The dumper calls `+0x3B9` RoomId
(`creature-object.md`). `0x0048F350` rejects states 209/255/256/257.

`0x00478050` is the effective state-category lookup: while travelling it reads
the saved resume state's category. `0x004888C0` rejects the owner flag at
player `+0x76 & 4`, creature flag `+0x3E6 & 0x100000`, or effective state
190/191/192. The hand eligibility routine `0x0048E6A0` also reads countdown,
Turncoat, room ownership and worker flags; those predicates need their existing
hand-rule mappings checked before reuse. `0x004BFBD0` prepares state 75,
retrying through idle on refusal; it is not simply a permission query.
