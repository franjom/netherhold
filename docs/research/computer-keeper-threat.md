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

The active player list is reverse activation order, not ascending player id.
`0x004C1E20` allocates compact ids 1 through 7 on a free list; `0x004C2460`
moves an activated player to the head of the active list. `0x004B8640` stores
the manager pointer at player `+0x10FD`; threat readers start at that manager's
head and follow player `+0x3A`. For a fresh level, `0x0056DAD0` reads the
205-byte PLAYERS records sequentially; callbacks `0x0051EF80/0x0051F0B0`
activate each through `0x004C2340`. Thus the last nonzero enemy encountered
is the earliest activated such enemy. Level10Players ids are 1,2,3,4 and
Level17Players ids 1,2,3,4,5 (**CONFIRMED from data**), giving reversed walks.
Id 0 is unowned ground, not one of these player objects. Save-restored list
order and dynamic deactivation/reactivation are not established by this load trace.

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
The navigation query is world vtable `0x0066E3EC +0x208`, resolving to
`0x00509200`: it tests byte bit 3 in the navigation grid at world `+0x9F2`.
It does not reject water, lava, portal-hole or door bits. Ownership is checked
separately by each reader. `KeeperView.IsThreatDropCandidate` captures this
predicate using the simulation's shared navigation-byte builder.
Both scan Y then X in ascending order and retain the first equal-score tile.
A chosen zero X or Y is subsequently treated as no drop site.

Both compute integer `battleSuperiority * enemyStrength / 100 - allyStrength`.
With no drop site, removal enabled, nonnegative ally strength and positive
deficit, the event's signed 16-bit residual is the negative ally strength.
Otherwise the room reader still queues residual zero; the position reader
cancels the reservation. Do not suppress all zero-residual events: event 1
dispatches zero through its reinforcement arm.

## 2. Local strength and readiness

`0x004FAC40` walks the player's creature list, excluding map layer `+0x16 == 2`
(off-map, not low health; `combat-pit.md` section 6).
It applies a strict radius test, shifting each squared fixed-point axis delta
right by 12 separately before summing. The optional trap walk uses the player's
trap list, excludes the same map layer and trap definition id `+0xF6 == 2`,
then adds descriptor threat `u16 +0x8C`. This is a type exclusion, not runtime
state: `0x00520E50` passes the authored type to `0x004B7C10`, which stores it
at `+0xF6`; initializer `0x004A7940` uses it to select the descriptor. Rechecked
2026-09-28 with Ghidra 12.1.4/PyGhidra. Shipped `Traps.kwd` record 2 names
Boulder Trap (record offset `0x20C`); construction and recharge are not tested
by this strength helper.

The threat word comes from TRAPS record `+0x1EA`: `0x00570D10` copies it to
intermediate `+0x292`; `0x004AA440` copies that word to descriptor `+0x8C`.
`0x004AA330` initializes it to zero. No conversion or scaling occurs.
Shipped global `Traps.kwd` ids 1 through 16 carry, in order, 74, 0, 74, 0, 185, 74,
37, 600, 0, 74, 0, 100, 0, 74, 0, 0 (**CONFIRMED** from data). Zero is a
real zero contribution, not a missing-value fallback. The synthetic parser test
also distinguishes unsigned values above 32767 and adjacent fields.

The radius square is a 32-bit multiply followed by arithmetic shift 12
(`0x004FAC57`, `0x004FAC62`), then an unsigned distance comparison
(`0x004FACCB..0x004FACCD`). The 4/5/10-tile message radii behave geometrically.
A 12-tile radius overflows to a negative square and becomes `0xFFF90000`
after shifting; it can therefore admit a creature 100 tiles away. This is a
binary prediction, not an observed battle response. Preserve this quirk when
reusing the helper for Call to Arms; do not replace its radius square with 64-bit math.

Creature threat is descriptor `u16 +0x698` at level 1. Other levels multiply it
by the signed percentage at `[0x006E4154] +0x105 +2*level` and divide by 100
(`0x004FBDB0`, also inlined in the strength and event routines). It does not
scale threat by current health.

Pickup `0x004BC500` unlinks the held thing through `0x004B4F60`; carrier
pickup `0x0048B350` does the same for its payload. These correspond to our
`InHand` and `CarriedByCreatureId`. A dying creature still on the map is
counted. A corpse is a different original thing type (creature-death.md),
so our retained corpse-shaped creature record must not enter this list.
Authored trap placement `0x00520E50` uses tile centres: coordinate * 4096 + 2048.

Readiness at `0x004FAB10` counts nonworkers by descriptor bit 0, requires strictly
more than `IAMinimumCreatures`, and then checks level. When the below-level
count is at most the target level and less than the total, it grants each
below-level nonworker one ordinary level-up before testing again. It does not
exclude low-health, captive or fighting creatures in this count, and it does
not send creatures to Training. The level-up can repeat on subsequent calls
until the test passes. The grant path calls `0x0048B120` directly.

## 3. Response event and withdrawal gate

The three slots are FIFO: readers refuse before any imprisonment reroll when
count is three; a new reservation uses `(head + count) % 3`. A position reader
that cancels its reservation still returns success, allowing acknowledgment.
`0x004FA400` sets FightGoingOn even when the reader refuses a full queue.

`0x004FE0B0` uses a signed 16-bit residual, so narrowing and subsequent word
arithmetic matter. Its negative arm withdraws creatures in list order from a
strict five-tile radius; its nonnegative arm reinforces. Eligibility and the
movement search are detailed in `computer-keeper-handlers.md` §10.

Every dispatch returns **zero allowance**, including early completion or no
candidate. Rechecked in Ghidra 12.1.4 on 2026-09-28: retirement clears the
allowance at `0x004FE36E/0x004FE99B`; both exits clear AL at
`0x004FE37D/0x004FE9AC`. Caller `0x00500D99..0x00500DAA` consumes that return.
Thus only one event is dispatched per tick, and subsequent budgeted work is
skipped even if a single attempt completes it. Within that event, each attempt
restarts the creature-list search. Refused interaction spends an attempt but
does not change the residual. Word addition/subtraction precedes the signed
crossing-zero test. Retirement advances the head modulo three and decrements
count. Crossing zero clears the residual; no-candidate retirement leaves its
unused payload stale. Exhausted allowance without completion retains the event.
Zero residual enters the reinforcement arm and
still attempts selection. These are binary predictions, not live observations.

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
190/191/192. The hand eligibility routine `0x0048E6A0` is audited against the
current `HandRules` in `hand-of-evil.md` section 3; its missing gates must be
accounted for before reuse. `0x004BFBD0` prepares state 75,
retrying through idle on refusal; it is not simply a permission query.

### 4.1 Casualty-priority lifetime and reader

**STRONGLY INFERRED**, read-only PyGhidra recheck, 2026-09-28:
`0x004872A0` initializes the creature's word at `+0x3F7` to zero.
The writers `0x004F9B90` and `0x004FA000` set or clear the bit indexed
by compact player id minus one after their imprisonment-percentage roll.
`0x004F2800` clears only the converting keeper's bit after torture conversion.

`0x0046D810` traverses the same tile's thing list three times: marked foreign
dying creatures, own dying creatures, then other foreign dying creatures.
The tile must belong to the worker. Candidate reservation, room, capacity and
path checks still apply. This is priority within one tile, not a global
ordering of casualties by mark. Instruction sites `0x00513E22`/`0x00513E3E`
also write this word; their trigger integration is not implemented here.

The simulation command adapter and existing per-tile body chooser implement
the mask and these three ranks (ADR-0261). Existing route selection, stable
spawn-order ties and room-capacity approximations remain; this does not verify
the complete original rescue selector. No new original-game capture covers it.

## 5. Implementation boundary

`DK2.AI.ThreatEvaluator` implements the two readers' pure arithmetic: local
strength, message-specific radii and trap inclusion, last nonzero enemy in
player-list order, drop-site selection and signed 16-bit response payloads.
Rechecked against `0x004F9B90`, `0x004FA000`, `0x004FAC40`, `0x004FBDB0`,
`0x004BF390` and `0x004BF3D0` on 2026-09-28. Tests distinguish strict range,
per-axis truncation, signed scaling, scan ties, zero-coordinate rejection,
the room-score defect, zero residuals and narrowing overflow.

`ThreatEvaluator.Sources` and the observation overload of `Assess` now project
live captured creature and trap values. Held/carried creatures become off-map
sources, corpse records are omitted, and dying creatures retain their threat.
Trap construction and recharge do not filter this strength calculation. The
simulation has no additional limbo or trap off-map lifecycle to project yet.

Campaign keepers receive PLAYERS activation order from the harness; observation
schema 7 stores its reversed active-list walk, casualty-priority masks and pickup permissions. Synthetic callers may supply
activation order explicitly; otherwise it uses this simulation world's ascending
creation order, reversed. This changes observation semantics, not world tick order.
For current simulation versions see `hash-versions.md`. `KeeperResponseQueue` implements
reservation, cancellation, FIFO retirement, signed residual accounting and the
event's allowance contract. Its caller supplies creature selection and interaction
outcomes; it cannot itself move creatures. `KeeperAI.Think` still does not consume
these assessments. The priority command and represented pickup gates are built;
remaining eligibility lifecycles are in `creature-pickup.md`. Integration still
needs hand responses; acknowledging a message before those effects would lose work.
