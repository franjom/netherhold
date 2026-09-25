# The Hand of Evil: picking up, dropping, slapping

Milestone M8. The keeper's only direct contact with a creature.

**Source(s).** `DKII.EXE` v1.70, read 2026-09-05 and 2026-09-06, with the owner's
observations through 2026-09-13 as the behavioural anchor. Addresses are **facts
about the binary in our own words**; no decompiler output enters this repository
(ADR-0046).

**Implementation.** `DK2.Simulation.HandRules`, `EfficiencyRules`,
`Systems.HandSystem`, and the four commands in `Commands/HandCommands.cs`
(`PickUpCreatureCommand`, `PickUpObjectCommand`, `DropCommand`,
`SlapCreatureCommand`). Decisions: ADR-0062, ADR-0064, ADR-0069, ADR-0075, ADR-0077.

**Tests.** `HandTests` (`tests/DK2.Simulation.Tests`);
`scenarios/creatures/a_keeper_drops_a_warlock_and_an_imp_on_open_ground.yaml`.

---

## Summary of confidence

| Claim | Status |
|---|---|
| The hand is an array on the keeper, capacity `MaxNumberOfThingsInHand` = 64 | **STRONGLY INFERRED** — read from the pick-up handler and from DK2's own debug dump, which prints `NumberOfThingsInHand` and `MaxNumberOfThingsInHand` side by side; a thing already in a hand is refused by an explicit scan of the array |
| **Dropping is last-in-first-out** | **CONFIRMED** (owner, 2026-09-06), on top of the drop handler reading back the slot it is about to free. It holds **across kinds**: DK2 keeps one array of things, so a creature and a chicken share the slots |
| **The hand holds objects too** — a chicken, a pile of gold, a special | **CONFIRMED, BUILT** (owner, 2026-09-06; ADR-0075): *"I can pick up a chicken and drop it in the prison, or anywhere in the dungeon, on my owned tiles"*, with a screenshot of four held at once. `CanBePickedUp` is 26 of the 145 OBJECTS records and excludes spell books, crates and beds. **Gold lifts only from the keeper's own claimed land** (owner, 2026-09-25): not from unclaimed dirt, not from a rival's floor (Q196) |
| **An object is dropped on the keeper's own ground** | **CONFIRMED** (owner, same day). Where the rule lives is **UNREAD**: the drop handler has no ownership test, so the gate is in the input layer. It is **not** `CanBeDroppedOnAnyLand`, which is set on one record, Call To Arms, and read by nothing (Q196) |
| **A creature is dropped on the keeper's own ground — except a worker, which may also go down on unclaimed floor** | **CONFIRMED, BUILT** (owner, 2026-09-06; `HandRules.CanDropCreatureOn`, ADR-0077). A prisoner lifted from the keeper's prison may likewise be placed on owned tiles, a torture rack or in a Combat Pit (owner, 2026-09-13); the latter room effects are not built. The edges of “unclaimed” remain UNKNOWN (Q198) |
| A slap raises Efficiency by ten points, and does not stack | **CONFIRMED** (owner, 2026-09-05) — *"it raises from 273 to 310 after the first slap and stays there no matter how many times i slap that imp"* |
| **Being dropped next to work the keeper marked** raises it by ten points too, and dropping on empty ground is worth nothing | **CONFIRMED** (owner, 2026-09-05) — both halves watched |
| **There is no cooldown on a slap** | **STRONGLY INFERRED** — the `LastSlapTick` window guards the knock-back impulse and nothing else. See §4 |
| A slap costs `SlapDamage` health — 100 of an Imp's 500 | **CONFIRMED** by the shipped data; **UNVERIFIED** that the original applies it on the same path |
| `AngerInHand` applies **once a game second, from 20 game seconds after the pick-up** | **STRONGLY INFERRED** — read from the creature update |
| The slap's work bonus lasts **25 game seconds** | **CONFIRMED** (owner, 2026-09-06) — *"timeout for efficiency to return back to 80% after slap is 25s, not 60s"*. Q159 closed |
| **The aimed bonus is a task kind, not a flag** — and it lasts as long as that kind of work does | **STRONGLY INFERRED** (2026-09-06; two writers in the whole binary) — §6, ADR-0069 |
| **The drop looks three tiles** | **CONFIRMED** (owner, 2026-09-06) — and DK2 reaches three from a nominal two by an off-by-one in its own loop. Q162 closed |
| Creature definition flag `0x4000` is **`IsHorny`**, and slapping him runs a different routine | **STRONGLY INFERRED** — Q158 |

---

## 1. DK2 names its own fields

The executable carries its debug dumps, and they are `printf` calls with the
field name in the format string and the field itself as the argument. Reading the
creature dump gives a **name for 162 creature fields**, keyed by offset, straight
from the developers. The ones this mechanic needed:

| Offset | DK2's name | What it is |
|---|---|---|
| `0x3af` | **`WorkModifier`** | the base work rate, `0xccc` = 0.8 at spawn |
| `0x3b3` | **`WorkEnhancement`** | the bonus, rebuilt from flags every update |
| `0x3e6` bit 0 | **`InHand`** | first bit of the first state-flag word |
| `0x3e6` bit 26 | `WasLastDroppedOnARoom` | unused here |
| `0x459` | `SpeedChangeTimer` | the slap's *speed* boost, see §5 |
| `0x46d` | `LastDroppedCounter` | unused here |
| `0x4bb` | **`LastSlapTick`** | when the last slap landed |
| `0x4bf` | **`PickupTick`** | written on pick-up **and on drop** |
| `0x4d7` | **`WorkFasterTimer`** | the slap's work bonus, counts down |
| `0x2e7` | `ImpJobData.JustDropped` | a one-shot ticket for the drop scan |
| `0x2eb` | **`ImpJobData.DroppedTask`** | **the aiming bonus** — the task kind, or 0 |
| `0x2bc` | `DroppedToDoJobId` | the same idea for a **non-worker** |

`Efficiency`, the word on the status bar, does not appear in the binary's
strings — it is the UI's name for `WorkModifier + WorkEnhancement`.

---

## 2. The hand itself

Pick-up (`FUN_004bc500`) and drop (`FUN_004bc710`), both on the keeper.

**The array is on the player**: `u16` thing ids at `+0xb2`, the count at `+0x1bf`,
the capacity at `+0x1c0` — which `MaxNumberOfThingsInHand` (64) seeds.

**Pick-up**, in the original's order:

1. Scan the held ids; if this thing is already in the hand, refuse.
2. Refuse if the thing does not exist, or the hand is full.
3. Ask the thing whether it can be picked up (a virtual call).
4. For a **creature**: refuse an enemy creature under one condition, refuse one
   state flag, refuse two specific creature states, and refuse if it cannot enter
   the held state.
5. Set `InHand`, stamp `PickupTick`, clear three fields; append the id and
   increment the count.

**Drop** takes a **position and no id** — because the hand chooses:

```
count = NumberOfThingsInHand
if count == 0: refuse
thing = hand[count - 1]          # the last one picked up
NumberOfThingsInHand = count - 1
```

For a creature it clears `InHand`, **stamps `PickupTick` again** (so the field's
name is narrower than the field), enters a landing state, writes the position, and
resets the velocity.

**A just-dropped creature cannot be slapped:** the landing state is one of the
three the slap handler returns on immediately. **A held worker lets go of what it
was carrying** — `HandSystem.DropLoad` puts down the object and the body a lifted
worker had in its arms, where it stood.

---

## 3. What refuses a pick-up

The original's four creature-specific refusals are one ownership test, one state
flag and two creature states:

| Original | Here |
|---|---|
| already in a hand | same |
| hand full | same |
| record lacks `CanBePickedUp` | same |
| an ownership test — **unread**, see below | the keeper's own creatures, **plus any creature whose `PrisonOwnerPlayerId` is the keeper** (ADR-0064) |
| state flag `0x200000` = **`CreatureDying`** | **same** — a creature that is down is refused |
| creature states `0x33` and `0x34` | **absent** — unidentified, and the states do not exist here |

**The hand never touches a body.** CONFIRMED by the owner (Q161): *"a corpse can
not be picked up by hand of evil … also an injured creature, lying on the floor
bleeding out, can't be picked up by hand of evil"*. Carrying one is an imp's job
and only an imp's. Bit `0x200000` is bit 21 of the first state-flag word, and the
creature dump's own bit list gives bit 21 as `CreatureDying`, so the binary agreed
all along. **Trap: name the flag when you read a handler** — written down as *"two
creature states and one state flag, none of which this project has"*, the unnamed
flag turned a rule DK2 had into a narrowing supposedly invented here.

**The ownership test is genuinely unread.** The handler's test is
`if (SneakingAs != 0 && owner != me) refuse`, and `SneakingAs` (`0x4a3`) is the
creature type a creature is disguised as — so read literally it refuses an enemy
only while it is disguised, which cannot be the whole rule. The real gate is most
likely the virtual call the handler makes before any of this (`vtable + 0x28`);
recorded as unread rather than guessed at. What this project does instead is the
owner's description: **own creatures, plus prisoners in the keeper's own prison**,
the one exception there is (`prison.md` §2). Once lifted, that prisoner may be
dropped on owned tiles, a torture rack or in a Combat Pit; here a non-prison drop frees it.

---

## 4. The slap, and a cooldown that is not one

`FUN_0048b8a0`. In order:

1. **If the record carries flag `0x4000`, run something else entirely and
   return.** That bit is `IsHorny` (Q158) — one of two unnamed bits in this
   project's `CreatureFlags` until this handler named it.
2. Return on three specific states, including the one a just-dropped creature is
   in.
3. Two other states take a short path of their own.
4. `if (StateFlags1 & 0x8000) || now < LastSlapTick + var19` → **skip the
   knock-back impulse.** Then, unconditionally:
5. `LastSlapTick = now`
6. anger: `AngerSlap` from the record, on one branch of a test
7. a force applied in the slap's direction
8. **if `SpeedChangeTimer` is zero**, raise the creature's speed to 150% and set
   `SpeedChangeTimer` (see §5)
9. **`WorkFasterTimer = 100`**, unconditionally — so a second slap *rewrites* the
   timer rather than extending it, which is exactly the not-stacking the owner
   measured.

**Step 4 is the trap.** That test guards **only the impulse**, not the slap: a
first reading took it for a one-second cooldown on the whole handler and built it
that way. So **a keeper who can click fast angers a creature as fast as they can
click** — the six countable slaps that pinned the anger ceiling (Q114) were six
because a human hand is slow, not because the game rationed them.

This project has no physics for a creature to be thrown by, so
`HandRules.KnockBackWindowSeconds` is recorded and read by nothing. **Nothing in
`DK2.Simulation` paces a slap**; the only slap interval in the repository is AI
pacing in `src/DK2.AI/KeeperAI.cs`, which is not a game rule.
`HandRules.CanSlap` also asks `CreatureFlags.CanBeSlapped` and refuses a held
creature, one already down, and a prisoner — the ownership exception §3 makes is
for lifting a captive out of a cell, not for hitting it.

---

## 5. How long the bonus lasts

`WorkFasterTimer` is set to a **hardcoded 100** and decremented by one in the
creature update, every tick. Variable 19 is ticks per game second and is **4** in
every shipped level, so **100 ticks = 25 game seconds**, and
`SlapWorkFasterDurationSeconds` is **25**. The hardcoded constant and the named
variable agree exactly, a second independent confirmation that variable 19 is what
this project has taken it for. Implemented as `EfficiencyRules.SlapBonusSeconds` /
`SlapBonusTicks`, counted down by `HandSystem.Tend`.

**The table's `IncreasedWorkRateDurationFromSlappingSeconds` of 60 is a different
effect, and both numbers are right** (Q159 closed; ADR-0090,
`library-and-research.md` §4). The 60 is read by the keeper's **research** adder
and nowhere else, as the window on a research bonus worth +200 %. So one slap
starts two clocks off the same `LastSlapTick` stamp: the work-rate bonus for **25**
game seconds and the research bonus for **60**. The status bar reads Efficiency, so
the 25 is what a slapped **imp** shows; the owner's *"after a minute"* was the other
clock, which the status bar does not print. **Trap: write "no reader has been
found", not "nothing reads it"** — the hedged form is why this became an addition
rather than a retraction, and `tile-work.md` used the unhedged form and needed one.

`SpeedChangeTimer` is a loose end in the same handler: set to `var19 * 100` = 400
ticks = **100 game seconds**, where `SlapSpeedUpDurationSeconds` is 25. Recorded,
not resolved; nothing here moves a creature faster for being slapped.

---

## 6. Efficiency, and the two things that raise it

The creature update rebuilds `WorkEnhancement` from scratch each tick:

```
bonus = 0
if IsWorker:                        # definition flag bit 0
    if WorkFasterTimer  != 0: bonus += 0x199      # 409 = 0.0999
    if DroppedTask      != 0: bonus += 0x199      # a task KIND, not a flag
else:
    if WorkFasterTimer  != 0: bonus += 0x199
    if DroppedToDoJobId != 0: bonus += 0x199
if counter > 0:               bonus += counter * 0xcd     # 205 = 0.05 each
WorkEnhancement = bonus
```

- **The two bonuses are independent and equal**, ten points each, which is why
  100% is reachable and 110% is not.
- **The non-worker branch reads `DroppedToDoJobId`**, the generic creature state's
  *"the job I was dropped to do"* — the same sentence as `ImpJobData.DroppedTask`
  one struct up, kept separately because imps keep their job bookkeeping
  separately. Only workers do tile work here, so only the worker's branch is built.
- **A third term exists** — 0.05 per unit of a byte counter held on an object the
  creature points at. `IncreasedWorkRatePersentageOfPlayersTorturedCreatureTypes`
  is the obvious candidate and is **UNVERIFIED**; it needs a Torture Chamber to be
  non-zero and this project has none.

**The offsets are pinned rather than fitted.** The creature's whole state object
sits at **creature + 0x24E**, which the creature debug dump hands to the state
dump, and two independent reads in the update — `CurrentStateId` at `+0x25E` and
`UtilityNewState` at `+0x32D` — land exactly where that base puts them. From there
`+0x2EB` is `DroppedTask` and `+0x2E7` is `JustDropped`, **four bytes apart**, which
is how the two came to be read as one thing for a day. **Trap: a plausible field
name four bytes from the right one, and nothing checking.**

### Why this project counts in percents and not in 4096ths

`EfficiencyRules` works in whole percents (`BasePercent` 80, `BonusPercent` 10)
with halves rounded away from zero, because the **displayed percentages are the
exact quantity** and DK2's `0xccc`/`0x199` are its approximation of them
(ADR-0002: behaviour, not implementation). The measurements that separate the two
readings are in `tile-work-efficiency.md`.

### The aimed bonus: two fields, one look, and one clear

Read out on 2026-09-06 from `ImpState.cpp` and `ImpUtils.cpp` — DK2's own source
file names, carried in its debug strings — after the owner watched a dropped imp
keep 90% all the way to the wall.

**The drop** sets `JustDropped = 1`, zeroes `LastTaskType`, and moves the worker to
the job-evaluation state. That is all it does; it decides nothing about the bonus.

**The look.** The job chooser (state 10's handler) runs on the next update. Two
things happen because `JustDropped` is set: the *"don't re-evaluate for 0x28
ticks"* throttle is skipped, so a dropped worker thinks on the spot; and after the
ordinary priority chain is skipped — it is skipped, because `LastTaskType` is
zero — it **clears `JustDropped` unconditionally** and runs a spiral scan around
where the worker landed. The predicate is a composite of nine task kinds and its
order is *not* the priority chain's:

> excavation (1/2) → claim (3) → crate for a trap or door (9/10) → carry an
> unconscious creature (13) → reinforce or fix (6/16) → floor gold (7) → loose
> object (8) → corpse (12) → one unidentified

So **a worker dropped beside a marked wall digs it** rather than putting a book
away, even though the book is second in the ordinary chain (ADR-0065). And **kind
14, claim-the-just-dug-tile, is not in the list at all**: a dropped worker takes
the ordinary claim.

**The bonus.** If and only if the scan takes something, `DroppedTask` is set to
that task's kind. If the scan finds nothing, the worker falls through to the
whole-map chain and finds work anyway — with `DroppedTask` still zero and no bonus.
That is the owner's *"drop it on a random tile and it remains at 80%."*

**The three tiles, and why they are an off-by-one.** The scan is `ImpUtils.cpp`'s
ring walk. It tests the tile underfoot, refuses to look further if the radius is
under `0x1000`, converts the radius to whole tiles, and then walks rings — but it
tests its bound **after** scanning a ring, with a strict greater-than, and its ring
counter starts at 1. So with the `0x2000` it is handed — nominally **two** tiles at
`0x1000` a tile — it scans ring 1, ring 2 and **ring 3** before stopping. The owner
counted exactly that on screen: three tiles pay, the fourth does not (Q162).
Reproduced rather than fixed, per ADR-0002. The bound is measured to **the tile the
work is on**, not to the tile the worker would stand on: a marked wall four tiles
away is reachable from a floor tile three away and still does not pay. DK2 agrees
in shape — its scan's own pre-filter asks whether there is a dig task *on the
scanned tile* before it calls any predicate.

**The clear — and there is only one.** `DroppedTask` has exactly **two writers in
the entire binary**, both inside the job chooser: the set above, and

```
if DroppedTask == LastTaskType:
    if TakeJob(LastTaskType, radius = 0x7fffffff): return   # bonus survives
    DroppedTask = 0                                         # the only clear
```

which runs at the *top* of a later evaluation. So the bonus survives the walk, the
digging, and finishing the tile: a worker that finishes one marked wall and finds
another anywhere on the map **keeps its 90%**. It is lost only when a worker that
still calls itself an excavator can find nothing left to excavate. The guard is
`DroppedTask == LastTaskType`, so a worker that has drifted onto some other kind
of work does not lose the bonus either — the comparison fails and DK2 falls
straight through.

The owner's 2026-09-06 report is this mechanism end to end:

> *"it stays at 90% until it gets to that marked tile and starts excavating, it
> doesn't revert to 80%, however … I unmark it before an imp gets to it, it gets
> to that tile, "sees" it is unmarked and it drops back to 80%."*

The unmark case is the clear from the other side: the job is invalidated, the
worker re-evaluates, it is still an excavator, and nothing is left marked.

**Still to watch (Q177):** whether the bonus really carries from one marked tile
to the next. The binary says yes and nobody has looked; one glance settles it —
mark two seams far apart, drop an imp on one, and watch the percentage after the
first block falls. The rival reading, *"the flag is cleared by `ClearJob`, so the
bonus is worth one job"*, fits an older owner report only if that seam was the last
marked work on the map.

---

## 7. The hand and the mood

The creature update, once per creature per tick:

```
if InHand
   && now > PickupTick + var19 * 20        # 20 game seconds
   && now % var19 == 0:                    # once a game second
       apply AngerInHand
```

So **`AngerInHand` is a rate, not a one-shot**, and it does not start for twenty
seconds. Which is why moving a creature across the dungeon costs a keeper nothing
at all, and why parking one in the hand is a way to *calm* it: a Goblin's
`AngerInHand` is **−250** and a Warlock's −500. Jack is the exception in this as
in everything — **+500**, and slapping it calms it (−5000).

`HandRules.AngerGraceSeconds` / `AngerGraceTicks` and `AngerDueThisTick` hold it,
riding the same once-a-game-second clock as every tabled rate (ADR-0017).

`TimeInHandBeforeCreaturesBecomeAngrySeconds` is **1000** and is **not** this
number, which is hardcoded at 20. A raw address scan of its cached global
(`0x006F2977`) in `DKII.EXE` v1.70 finds exactly one occurrence in all of `.text`,
the loader store at `0x0051D49F`, and no reader — so variable 193 is **CUT**, not a
duration in ticks and not a live mechanism disabled by its value (Q160, binary,
2026-09-07).

---

## 8. What is built beyond a creature, and what is not

**Built.** Objects and creatures share one LIFO array. A prisoner returned to the
dropper's prison is re-imprisoned; elsewhere it is freed. **Both** disposal tiles
exist as of 2026-09-15: ordinary and converted goodly creatures go through a
portal's hole with their gold, an Imp is refused there and goes through the
Heart's platform instead, and a captive is refused both (`DisposalRules`,
ADR-0148; `structure-blocks.md` §4.2). What is assumed inside that is Q312.

**Not built:**

- **The knock-back** and every other physical consequence of a slap, and **the
  speed boost** (§5).
- **Dropping onto a room**, which the original tracks with a flag of its own
  (`WasLastDroppedOnARoom`) and which is how a creature is put into a Library or a
  Training Room. The prison cell is the one room drop that is built.
- **Dropping onto enemy ground.** The original's drop handler changes the
  creature's owner under a condition, and that branch proves only that the
  *handler* does not refuse — the gate is the input layer's (Q196), so whatever
  reaches that branch is not the ordinary drop. The owned-ground rule is the
  creature drop's too, with one exception (Q198, ADR-0077):
  `HandRules.CanDropCreatureOn` allows the dropping keeper's own tile for everyone
  plus *unclaimed floor* for anything carrying `IsWorker`. `IsUnclaimedFloor` is
  structural rather than by id — not `Solid`, not `Ownable`, not `Room`, and
  neither `Water` nor `Lava` — which in the shipped table of 41 is **Dirt Path and
  the Mana Vault**, the identical flag word `0x10008000`, each with an `Ownable`
  claimed counterpart. The edges of "unclaimed" (water, a bridge, a rival's
  *unclaimed* room floor) are UNKNOWN and **refused**, the narrower reading; item 5
  of the observation checklist would change it. Keyed on `IsWorker`, which also
  admits the Dwarf where the owner said *imps* — deliberate, and unreachable today.
- **A chicken dropped into somebody else's Hatchery becomes theirs** — the handler
  tests object id 9 against another keeper's tile in a room of type 4. **Unreachable
  by the Hand**: nothing can be dropped on another keeper's tile (owner, 2026-09-25).
- **A chicken or a pile of gold dropped ONTO a creature is earmarked for it**, by
  the creature's id written into a field on the object, when that creature's
  level-scaled `HungerFill` or `Pay` is non-zero. **CONFIRMED by the owner,
  2026-09-25**, not built. The field, all accesses, direct food/wage effects and
  search negatives are in `hand-drop-earmark.md`.
- **The cursor's target search** (`FUN_0044dec0`), which is a rule rather than a
  ray-cast: a 3x3 sweep, an **octagonal** distance (`max + min/2`), a quarter tile
  for an object against half a tile for a creature, the Dungeon Heart admitted by
  id, and **a creature preferred over an object** whenever both are in reach.
  Presentation, so not built — but worth having written down before somebody
  invents a worse one.
- **Possession**, the other thing a keeper does to one creature at a time.
