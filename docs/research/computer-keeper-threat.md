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
until the test passes. The grant path calls `0x0048B120` directly. Built as
`KeeperReadiness` (ADR-0298): the grant is `GrantCreatureLevelCommand`, and the tick's
test reads the level it predicts.

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
the dumper's `LastDroppedCounter`, not time since the last pickup: the constructor
zeroes the word (`0x00487711`), the creature update holds it at zero in state 76
and increments it once a turn otherwise (`0x00489A4D`, `0x00489A5F`), so it wraps.
`HandRules.LastDroppedCounter` derives it from the spawn or last drop tick.

Both readers store their own drop site as the event's position whenever one
exists (`0x004F9FB6..0x004F9FCF`); only a reservation without a drop site carries
the attacked tile. Since a drop site is always an owned tile, the reinforcement
arm's move below is reached only when that tile changes hands before a later
attempt, and the spiral's bound is twice the larger map dimension.

The reinforcement branch subtracts the selected creature's threat once the
interaction predicate succeeds, even when pickup or drop fails. The subtraction
at `0x004FE90B` lies after the hand-success branch. More surprisingly, when the
payload tile is already owned, `0x004FE5AA` jumps to `0x004FE85C` with the move
flag still zero (`EDI` cleared at `0x004FE54E`). It skips pickup/drop but still
debits the threat. Only a replacement site found after ownership differs sets
the move flag. This original no-move branch must not be silently repaired.

## 4. Automatic retreat is separate from an event withdrawal

**STRONGLY INFERRED** and BUILT (`KeeperRetreatStep`, ADR-0284); reread in Ghidra
12.1.4 on 2026-09-29. The tick `0x00500C20` runs `0x004FF260` at step 7, after
attack management and the Call to Arms removal test, only while no event task is
pending and allowance remains, and the routine returns the allowance left. It walks
the keeper's owned list from its head, the newest creature, while allowance remains.
The branches are alternatives, tested in this order; the first that holds selects:

1. A Combat Pit fighter (`+0x3E6` bit 15) whose health `+0x40` is strictly below half
   its level-scaled maximum (an unsigned shift).
2. Any creature strictly below `BARetreatHealth` percent of that maximum (the byte
   times the unsigned maximum, divided by 100, compared signed) whose effective
   category (`0x00478050`, through a walk) is 22. No hand, counter or room test.
3. If `LastDroppedCounter` (`+0x46D`) is at most 300 **and** the game turn is above
   300: a Pit fighter goes on to branch 5; anyone else is skipped.
4. A non-Pit creature below the percentage whose effective category is neither 6 nor
   22, that this keeper's hand does not hold (`0x004BCAE0`), whose current state is
   not 76, that `0x0048F350` does not call stunned (209, 255 to 257) and whose
   `RoomId` (`+0x3B9`) is zero.
5. A Pit fighter whose level `+0x403` is at least `IACreatureSkillLevel` (the
   four-bit field at `+0x4B2` bits 19 to 22), whatever its health.

The maximum is `Hp` (`def+0x692`) at level 1, else the signed product `Hp x
table[+0xE1 + level x 2] / 100`, the scaling `CreatureLevelRules.MaxHealth` applies.

A selected creature needs an own Heart room, drawn by `0x004C6460(5)` with the
world's RNG among unflagged rooms of the type; without one nothing is attempted and
nothing is spent. With one, one allowance is spent whatever follows. The creature
predicate `0x0048E6A0(creature, keeper id)` (`creature-pickup.md`) decides whether
`0x004BFBD0` (state 75, before the hand's capacity test, `hand-of-evil.md` §2) and
the hand pick-up `0x004BC500` run at all; when
`0x004BCAE0` then finds the creature in the hand, `0x004BC710` drops it, with no tile
test of its own, at a random own Lair's `CentrePos` (room `+0x27`, `0x004C6460(2)`)
or, with no Lair, at `DungeonHeartPosition` (`player+0xB43`, `0x004C5F50`), each axis
`tile x 4096 + 0x900`. The walk continues with the next creature, so several may move
in one tick; a lifted creature sits in state 78 for the rest of the tick, outside
category 22, so step 11's battle step does not count it. The routine reads no random
draw, timer or flag of its own.

`RoomId` is written only by the constructor (zero), the load relink `0x004E5DA0`, the
claim `0x004E5A70` that the enter hooks of categories 4, 5, 11 to 17 and 31 call, and
the release `0x004E5E00` their leave hooks call (`state-categories.md`); the simulation
derives it from the state (`RoomMembershipRules`). Shipped values: `BARetreatHealth`
10, 20 or 30 and `IACreatureSkillLevel` 0 to 8 by profile (`KEEPER_AI.md`'s profile
table). The one-tick command delay, the keeper's own RNG and the Lair's bounding-box
middle for `CentrePos` are ADR-0284's deviations. No original run (Q486).

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

Campaign keepers receive PLAYERS activation order from the harness; the observation
stores its reversed active-list walk, casualty-priority masks, pickup
permissions, the keeper's Call to Arms and mana, each spell row's last cast and, per
creature, the DK2 state, the state through a walk, the fight gate, level-scaled
maximum health, `LastDroppedCounter`, whether this keeper's hand holds it, its rank
in the owner's list, its `0x004888C0` answer, its target's owner, the battle
step's captured permissions (ADR-0283), its Combat Pit mark and whether it holds a
room id (ADR-0284). Synthetic callers may supply activation
order explicitly; otherwise it uses this simulation world's ascending creation
order, reversed. For current simulation versions see `hash-versions.md`.

`KeeperResponseQueue` implements reservation, cancellation, FIFO retirement,
signed residual accounting and the event's allowance contract.
`KeeperThreatResponder` connects it (ADR-0278): `KeeperAI.Think` walks the
retained messages in queue order after the gold untag, evaluates kinds 1 and 6
through the position reader when a slot is free, acknowledges by command what the
reader accepts, marks every other kind handled and sets the fight flag; then it
dispatches the head event with the tick's allowance and keeps none of it. Each
attempt restarts the owner-list walk with the tests of `computer-keeper-handlers.md`
§10; a refused permission spends the attempt; a withdrawal emits pick-up and drop
on the heart tile; a reinforcement moves only from an unowned payload through the
spiral and debits threat either way. Commands land a tick later, so outcomes are
predicted from the captured permissions and a creature moved this tick leaves the
tick's candidates; `0x004888C0` is captured whole now that the call exists
(`call-to-arms.md`). The retreat of the wounded runs after the removal test
(§4, `KeeperRetreatStep`). Not represented: the spiral's `0x00454110` exclusion and
the room reader (no kind 34 producer).
`KeeperThreatResponseTests` distinguish the owned-payload no-move, the withdrawal
walk and its counter gate, refused permission, the full queue, the cancelled
reservation, the imprisonment reroll, the spiral's order and a live type 6 from
`CombatSystem` ending in a drop on the heart. No original response is observed (Q482).
