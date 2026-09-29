# Keeper management and battle selection audit

Independent Ghidra 12.1.4/PyGhidra read of the read-only `DKII.EXE` v1.70
project, 2026-09-28. Executable MD5:
`2f3bbc21eae8c3aa468136baa303036d`. All behavioural claims here are
**STRONGLY INFERRED**, without a matching live sequence. This note records
selection and allowance rules, not proof that every requested game action succeeds.

## 1. Prisoners — `0x005006D0`

Select one random owned Prison through `0x004C6460(11)`. An empty selected
Prison ends the routine; it does not search the other Prisons. Walk its occupant
list. The first non-enemy occupant is selected immediately, with no random
test. Each enemy occupant instead gets a `random(100) < 5` selection test;
stop at the first success. At most one occupant is selected per invocation.

Selecting an occupant consumes one allowance even if its later move is refused.
Non-enemies require a Lair. Enemies use `ImprisonedCreatureAction`: 0 Torture,
1 no move, 2 Combat Pit, 3 Temple. Mode 1 therefore still spends allowance when
an enemy is selected. There is no alternate destination when the requested
room is absent.

Every move checks the dropped-counter guard, the hand, state 76, the
209/255/256/257 exclusion helper and interaction permission. See
`computer-keeper-threat.md` for these common predicates. Lair, Pit and Temple
drops use the room's stored tile plus fractional offset `0x900`. Torture asks
`0x004E8210(1,1)` for a floor or wall furniture slot and refuses a tile
containing an active creature in state 136 or effective category 15
(`0x00485210`): a torturer or torture victim. Slot selection is documented in
`workshop-crates.md` §4.

## 2. Training moves — `0x004FF5C0`

Select one Training Room and one Combat Pit. A room matching the current
build's low seven id bits is excluded. Walk owned nonworkers while allowance
remains. Choose the Pit branch if experience is strictly above three quarters
of its level-scaled next-level requirement, or its level has reached the
Training Room cap while still below the keeper's attack target level.
Otherwise choose the Training branch. A missing Pit does not fall back to
Training for a creature already assigned to the Pit branch.

The Pit branch additionally requires health strictly above three quarters of
the level-scaled maximum; Training has no health comparison. Both apply the
dropped-counter guard, exclude an existing destination activity, combat category,
hand/state restrictions, and a nonzero creature dword `+0x2BC`. The latter's
meaning is **UNKNOWN** here. A selected room's position comes from
`0x004E8210(1,0)` (floor slots only) and is offset by `0x100` before dropping.

Neither branch attempts a move if its predicates fail. Once the move attempt
is reached, one allowance is spent even when position selection or hand
interaction fails. The outer tick has a separate 50% gate and calls readiness
first; readiness can itself grant levels (`computer-keeper-threat.md` §2).

## 3. Specials — `0x00500520`

Walk owned objects and retain the last object whose descriptor Special flag
is set, whose tile is owned by the keeper, and whose virtual predicate at
`+0x74` returns zero. Only **after** the complete scan, check its object type.
The accepted ids are 8, 117–126, 128 and 129; id 127 is excluded. Submit action
88 for that retained tag if accepted. An excluded last object suppresses an
earlier accepted one; this is not a last-activatable-object search. The tick
charges no allowance for this routine. RTTI resolves the object's primary vtable
to `0x0066D654`; slot `+0x74` calls `0x004B7250`, which returns bit 1 of the
object dword at `+0x146`. The gate is therefore a concrete bit test; its wider
state meaning remains **UNKNOWN**, and must not be replaced by guessed ownership
or pickup eligibility.

## 4. Battle spells — `0x00503060`

Re-read 2026-09-29 (Ghidra 12.1.4, Claude's read-only project) and **BUILT** as
`KeeperBattleStep` (ADR-0283). The tick (`0x00500C20`) reaches it at step 11
only with allowance left and the fight flag (bit 3) set; otherwise step 11
makes imps. The message walk sets the flag; only this routine clears it.

Clear the fight flag and walk own creatures in list order. Effective category
22 (`0x00478050`) sets it again. A candidate for emergency Call to Arms must
have an existing target (`+0x502` through the liveness table) with an owner,
no active CTA (bit 7) and a zero attack cooldown (`+0x4F3`). Strength is
`0x004FAC40` over that target owner's creatures within 12 tiles, without
traps; other enemy players are not summed, and the 12-tile square overflows
(`computer-keeper-threat.md` §2), so the whole map counts. It must strictly
exceed `BACallToArmsValue`, and the fighter's terrain must lack flag bit 8
(`Lava`, world slot `+0x68` → terrain `+0x17B`). The stored CTA position
(`+0x4E`) is written before the later checks. Require mana **strictly
greater** than `0x004BE560(3)`'s cost (the human's own cast passes on
equality), the world turn (`0x00517490`) divisible by 40, availability 3,
and `0x00452970`'s route from two tiles east of `DungeonHeartPosition` to
the fighter for the player with options 1,0,0,0. A cast `0x004BBD30`
accepts costs one allowance, sets bit 7 and `CallToArmsCounter ←
RemoveCallToArmsAfter`, and returns at once. Every fighter before the first
that passes the spell roll gets such an attempt.

Otherwise `random(4) >= UseKeeperSpells` permits a spell-selection attempt,
and that fighter ends the walk whatever comes of it; a fighter failing the
roll is passed over. `random(4) < AttackingVsDefensiveSpellPreference`
selects the supportive arm; otherwise offensive. Both enumerate spell ids 1
to the descriptor count (slot `+0x114`: 13 shipped, so id 14 is never
reached), require availability 3, no cooldown, the corresponding descriptor
flag word bit (`+0x54` ← file `0xDC`: 8 supportive, 4 offensive), and bit
16 clear. Shipped: Thunderbolt, Inferno and Chicken are offensive, Heal is
supportive, Tremor carries 4 and 16 (CONFIRMED from `KeeperSpells.kwd`).
With `q = manaCost / 2499`:

- Supportive: retain each eligible spell **before** testing
  `random(q+1) >= 4-SpellPreference`. Stop on success; if no roll succeeds,
  the last eligible spell remains selected.
- Offensive: retain the first eligible spell whose
  `random(q+2) >= 4-SpellPreference` succeeds. If all rolls fail, no spell
  is selected.

This is ordered selection with different fallback rules, not one weighted draw.
Supportive targets are own creatures strictly within five tiles, strictly
below half health and without the dying flag. Offensive targets are creatures
of the fighter's target owner strictly within five tiles; this loop adds no
health or dying filter, and neither scan skips a held or carried creature.
Every accepted target replaces the previous target; `random(100) > 75` stops
either scan. With a spell and a target, slot `+0x250` (`0x005177B0`: byte
`0x11` of the target-validator entry, set for selectors 1, 2, 3 and 6) picks
the thing cast: `0x004BBA40`'s permission, then `0x004BBF90` with its return
ignored; selectors 4 and 5 cast at the target's position through
`0x004BB8A0` and `0x004BBD30`, each of which must return nonzero. A refused
permission returns the allowance unspent; a cast spends one. At most one
spell is attempted per call.

Built: the whole walk, with Thunderbolt the only spell arm cast the
simulation executes; a chosen Heal, Inferno or Chicken is counted and not
issued (ADR-0283). Ours predicts the permission from the observation and
issues the cast a tick later; the original run is Q485.

## 5. CTA removal — `0x004FEFC0`

Called from step 7 while bit 7 is set and allowance remains; it spends none.
Remove when the timer is zero, or the strict-radius enemy count is below the
configured minimum, or the total own nonworker count is below its minimum.
Counts exclude map layer 2. The enemy walk takes every player in the manager
list that `0x004BF390` calls an enemy (another tag, alliance byte zero), heroes
and neutral included, and adds each such player's Heart room (`0x004C6460(5)`
with that player as receiver) if its stored centre lies within the radius of
the stored position's tile centre, `RemoveCallToArmsWithinRange`² × 4096
against the shifted squares. The own count has no distance requirement.
Clear bit 7, submit action 63 using the player's CTA tag, and set the attack
cooldown to `WaitBeforeAttackingAgain * 4`. Those AI state changes do not
depend on the command's return value. Built (ADR-0283); ours waits for the
observation to show the call before testing, since its cast lands a tick late.

## 6. Attack selection — `0x004FAEF0`

The outer gates are NeverAttack, nonzero attack cooldown, active CTA, readiness,
optional AllRoomsBuilt and optional absence of researchable spells. A required
researchable spell invokes the portal-slot eviction helper if a Library exists,
then declines the attack. The attacker-only mode consumes and clears the recorded
attacker before subsequent strength and room-selection failures.

The ordinary enemy scan skips player ids 1/2 and statuses 1/3, not winner status
2. Army strength sums the entire owned-creature list without the local-strength
helper's status filter. Equality suffices: own strength must be **at least** the
enemy's total multiplied by `IAThreatSuperiority / 100`.

A stored tunnel for a player with no tunnels left is resumed immediately during
the player scan, before nearest-heart selection and before that player's strength
comparison. Consuming the tunnel clears its start-X word. Ordinary mode writes
the attack cooldown, then clears it again if any stored tunnel for that player
remains. The attacker-only resume branch does not write this cooldown.

For a fresh ordinary-mode attack, the documented first-end and first-start
choices apply. The strongest-room choice starts its best score at zero and uses
a strict greater-than comparison, so all-zero room strengths leave no endpoint.
The attacked/first-attack bits are written before room selection can fail.
The attacker-only branch chooses the enemy non-bridge room nearest the own
Heart, then the own non-bridge room nearest that target. Receivers are the enemy
at `0x004FB9C1` and self at `0x004FBA2D`. The helper `0x004C6010` measures
from each room's first slab (`+0x2D/+0x2E`), excludes ids 8/17/22, and keeps
the first tie. These choices replace the ordinary first-attack choices.

A successful new tunnel decrements `TunnelsLeft` once here and stores player id
plus one in the target nibble. Task 7 later subtracts one from that encoded id
to index the tunnel array; it does **not** decrement `TunnelsLeft` again.
CTA staging begins at heart `(x+2,y+2)`; route-query effects are below.

## 7. Moving CTA front — `0x004FE9C0`, `0x004FECE0`

For each attacked player bit, choose an enemy Heart point with independently
random X/Y offsets of minus or plus two tiles. A creature route test must pass.
Nonzero CTA usage stores that destination directly. Usage zero, with an active
CTA, counts all own nonworkers and those strictly within ten tiles of the current
CTA position. Advance only when `total <= floor(nearby * 5 / 4)` and the route
helper `0x004D0E10` supplies a point. Its `0x2000` argument selects each kept
gate's midpoint through `0x004D0240`; it is not a movement distance. The keeper
copies the **first returned waypoint**, scaled back by 16. On a straight route
that can be the destination itself (`navigation-and-steering.md` §1). With no
active CTA, success leaves the staging point unchanged. The preceding creature
request `0x004D0E60` sets route-option bits 17–19 on that creature; querying
reachability is not entirely observational in DK2.

`0x004FECE0` removes an existing nonzero-usage CTA through the removal routine;
otherwise, with zero attack cooldown, it asks for a front and tries spell 3 when
availability is 3. A successful cast resets the timer and active flag. Usage 1
also queues half the entire army's threat for reinforcement when there is room
in the three-event queue and an owned drop point exists. Its drop search uses
the same asymmetric score as the room-threat variant (`computer-keeper-threat.md`
§1). Finding a front costs one allowance even if availability or casting fails.

## 8. Slapping — `0x00502F00`

The actual gates are accessor `0x00478A20 == 5`, health strictly above half
maximum, creature `u16 +0x459 == 0`, then `random(100) == 0`. Each submitted
action 62 spends one allowance. `+0x459` is `SpeedChangeTimer`, not
`LastSlapTick` or `WorkFasterTimer` (`hand-of-evil.md` §§4–5).

The accessor's 5 does **not** mean working. State-object `+0x3C` values 1/2
override it with 26/25. For nonworkers it reads the category's `+0x3C` default,
substituting the resume category during movement. Reconstructing the runtime
table from `0x004798D0..0x004805F0` confirms idle category 1 returns 5, whereas
research/manufacture return 20 and training returns 21. For workers, nonzero
creature `Idle +0x3D1` or state byte `+0x98 == 6` returns 5; otherwise effective
combat returns 4 and other categories return 6. The state byte's meaning is
**UNKNOWN**. The current AI's working-only test and 300-tick interval are both
unfaithful. Correcting them requires this observation and the slap's speed effect,
which `HandSystem` currently does not start.

## 9. Traps and doors — `0x005000A0`, `0x004FFD80`

The rectangle uses minima/maxima of the owned rooms' stored positions, not all
their slabs, with the random margin and map clamp described in `KEEPER_AI.md`
§5.6. The west-edge pass examines **only its first tile**: `0x0050021D` loads
the minimum Y; `0x0050025F..0x00500262` increments it and compares against that
same minimum. East, north and south have full inclusive loops. Corners can be
visited again. No original observation yet tests this asymmetry.

Selection scans type ids in ascending order and remembers each eligible candidate
before its random stop test. Thus the final eligible candidate is a fallback.
Door definitions with `+0x8B & 0x40` are skipped. Trap type 2 receives the boulder
branch only when type 2's availability is 3. With the long-corridor flag, four
further valid corridor tiles in any cardinal direction make that branch stop;
otherwise its stop test is `random(4) > preference`. Other trap types with
descriptor `+0xBD == 1` stop on `random(5) > preference`, the rest on
`random(5) < preference`. A chosen type still requires its world placement
predicate; after that passes, dispatch costs one allowance even if dispatch fails.

## 10. Room expansion — `0x004FBE00`

The slab-chain extrema are the smallest/largest `x+y`, not independent X/Y
bounds. The routine tests north, south, west, then east and retains **every**
accepted extension. It does not choose one winning strip. West/east use the
possibly extended north/south limits. Each scan excludes its high endpoint;
the final planned rectangle includes it. A one-tile extent therefore skips
that axis's validation loop. The gap line two tiles out allows at most one
open or already tagged sample per side. Bounds/terrain failures reject that
side, not earlier accepted sides. If any side survives, start task 2 over the
combined rectangle with positive X/Y stepping. These are binary predictions;
an irregular-room experiment would test the extrema and excluded endpoints.

## 11. Research slot and job placement — `0x004FA8D0`, `0x00503690`

The slot selector excludes workers, counts creatures whose job list contains
Research, and keeps a non-researcher with the smallest descriptor byte `+0x787`.
Equal values replace the candidate, so the **last** minimum wins. The byte's
meaning remains **UNKNOWN**. Fewer than two researchers and a candidate are
required. Prefer a Portal; fall back to a Hero Portal only if none exists.
`0x004DC760` counts both kinds for capacity; `0x004DCCE0` counts creature
state-flag bit 1. Their comparison admits eviction at or above capacity.
There is no allowance deduction inside the eviction helper. A selected candidate
that fails the drop/state/interaction gates does not cause a second selection.

Job placement first tries the hand's last item. A failed room/job match spends
allowance while retrying that same item, rather than advancing to earlier items.
Successful drop rereads the hand count. After this loop it draws `random(100)`
even with no allowance left; only a result below 10, remaining allowance and free
hand capacity permit the owned-creature pickup scan. That scan also requires
accessor 5 (§8), the dropped-counter guard, a zero dword at creature `+0x2BC`,
and its state exclusions. A successful pick costs one allowance; dropping that
creature is left to the hand-processing path on a later invocation.

The job table initializer `0x004F8180` confirms the four room mappings in
`KEEPER_AI.md` §2.3. The Research predicate `0x00502E90` is false without a
state-2 spell. With one, it calls eviction if a Library exists and returns true.
The Training predicate `0x00502EF0` negates readiness, including that routine's
direct level-up side effect. Thus predicate evaluation is not observational.

The caller `0x00500C20` draws the training roll and the research roll even with
zero remaining allowance. The trap roll is budget-gated. The job helper's own
10-percent roll is separate from these. An implementation must preserve these
draw sites if it intends to match the original command stream.

## 12. Gold deposit retention — `0x00501C10`

The four stored entries are sorted by squared distance to the nearest owned
room's stored position, but are **not always the four nearest deposits**.
The admission cutoff starts at zero (`0x00501C1D`), increases when an admitted
distance is larger (`0x00502165..0x00502172`), and never decreases after an
eviction. After four admissions, a new distance must be strictly below that
historical cutoff (`0x0050215A..0x00502163`). Insertion starts at slot 3 and
shifts only while the predecessor is strictly farther. Equal distances retain
encounter order; a later equal-to-third candidate can overwrite the fourth.

Discriminating prediction: encounter distances `1,4,9,100,16,25` leave
`1,4,9,25`, not `1,4,9,16`. This follows from the instructions, not a live test.
`GoldScanner` implements this retention rule with discriminating regression tests.

The instruction trace `0x00501D26..0x00501F15` checks terrain `u16 +0x185 != 0`,
without a solidity test, and visits cardinal neighbours through `0x006BC350`.
The seed contributes count 1 and its X/Y; each newly visited gold tile adds one
and its coordinates. Integer division gives the unweighted centroid. The stored
dword is the **sum of terrain gold values**, not the tile count: seed load at
`0x00501D83`, accumulation at `0x00501ECB..0x00501ED8`, store at `0x005021CC`.

If the centroid is out of bounds or has terrain flag bit 1, a square spiral
starts one tile east and accepts the first in-bounds tile without that bit.
It does not require the replacement tile to contain gold. The extent limit is
twice the larger map dimension (`0x00501F40..0x00501F59`); exhaustion sets X to
zero and preserves the low word of the accumulated Y sum, not the centroid Y
(`0x00501EFE..0x00501F15`, `0x005020AF`, `0x005021C2..0x005021C7`).
These details required instructions because the decompiler conflates
stack variables during this scan. Behaviour at gold on the map border remains
**UNKNOWN**: the neighbour walk has no explicit bounds call.

Implementation limits: room distance uses the bounding-box centre rather than
the original stored room position; the flood walk explicitly bounds neighbours.

## 13. Make-happy grievance selection — `0x004F9710`

The retained message identifies the creature through Data2, but its Data1
complaint selector does not choose the remedy. The routine returns success for
a missing creature or current mood zero. Otherwise `0x004F9772..0x004F9786`
passes creature `+0x26E` to `0x004871B0`, then dispatches on its result.
That helper scans six current anger counters, replacing the selected index only
for a strictly larger value; ties keep the lowest index.

The remedy mapping is therefore by current bucket: 0/4/5 pay gold, 1 give food,
2 move to Lair, 3 move to a job room. It must not use the sender's complaint
selector or a bucket cached when the message was inserted. The wrapper
`0x00478B80` sends its third argument as Data1; the bucket is its first argument.
The independently checked callers agree with the distinction in
`state-categories.md` section 7.2. A discriminating case retains a hunger message
while Lair anger becomes the largest counter: the keeper chooses the Lair.
