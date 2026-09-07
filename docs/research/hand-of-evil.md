# The Hand of Evil: picking up, dropping, slapping

Milestone M8. The keeper's only direct contact with a creature, and the oldest
verb in the series.

Implemented by `DK2.Simulation.HandRules`, `EfficiencyRules`, `HandSystem` and
the three commands in `Commands/HandCommands.cs`. Decisions: ADR-0062.

Read out of the original on **2026-09-05**, with the owner's observations of the
same day as the behavioural anchor. Addresses below are `DKII.EXE` v1.70 and are
**facts about the binary in our own words** — no decompiler output enters this
repository (ADR-0046).

---

## Summary of confidence

| Claim | Status |
|---|---|
| The hand is an array on the keeper, capacity `MaxNumberOfThingsInHand` = 64 | **STRONGLY INFERRED** — read from the pick-up handler and from DK2's own debug dump, which prints `NumberOfThingsInHand` and `MaxNumberOfThingsInHand` side by side |
| **Dropping is last-in-first-out** | **CONFIRMED** (owner, 2026-09-06), on top of the drop handler reading back the slot it is about to free. It holds **across kinds**: DK2 keeps one array of things, so a creature and a chicken share the slots |
| **The hand holds objects too** — a chicken, a pile of gold, a special | **CONFIRMED** (owner, 2026-09-06): *"I can pick up a chicken and drop it in the prison, or anywhere in the dungeon, on my owned tiles"*, with a screenshot of four held at once. `CanBePickedUp` is 26 of the 145 OBJECTS records and excludes spell books, crates and beds. ADR-0075 |
| **An object is dropped on the keeper's own ground** | **CONFIRMED** (owner, same day). Where the rule lives is **UNREAD**: the drop handler has no ownership test, so the gate is in the input layer. It is **not** `CanBeDroppedOnAnyLand`, which is set on one record and read by nothing (Q196) |
| **A creature is dropped on the keeper's own ground — except an imp, which may also go down on Dirt Path** (unclaimed floor) | **CONFIRMED** (owner, 2026-09-06): *"imps are the only creature that can be dropped onto dirt path tiles, those are tiles that are unclaimed by any keeper"*. **BUILT** the same day — `HandRules.CanDropCreatureOn`, ADR-0077. The edges of "unclaimed" are still UNKNOWN and are refused rather than guessed (Q198) |
| A thing already in a hand cannot be picked up again | **STRONGLY INFERRED** — an explicit scan of the array before anything else |
| A slap raises Efficiency by ten points, and does not stack | **CONFIRMED** (owner, 2026-09-05) — *"it raises from 273 to 310 after the first slap and stays there no matter how many times i slap that imp"* |
| **Being dropped next to work the keeper marked** raises it by ten points too, and dropping on empty ground is worth nothing | **CONFIRMED** (owner, 2026-09-05) — both halves watched |
| **There is no cooldown on a slap** | **STRONGLY INFERRED** — the `LastSlapTick` window guards the knock-back impulse and nothing else. See §4 |
| A slap costs `SlapDamage` health — 100 of an Imp's 500 | **CONFIRMED** by the shipped data; **UNVERIFIED** that the original applies it on the same path |
| `AngerInHand` applies **once a game second, from 20 game seconds after the pick-up** | **STRONGLY INFERRED** — read from the creature update |
| The slap's work bonus lasts **25 game seconds** | **CONFIRMED** (owner, 2026-09-06) — *"timeout for efficiency to return back to 80% after slap is 25s, not 60s"*. Q159 closed, against the owner's own earlier *"after a minute"* |
| **The aimed bonus is a task kind, not a flag** — and it lasts as long as that kind of work does | **STRONGLY INFERRED** (2026-09-06; two writers in the whole binary) — see section 6 and ADR-0069 |
| **The drop looks three tiles** | **CONFIRMED** (owner, 2026-09-06) — and DK2 reaches three from a nominal two by an off-by-one in its own loop. Q162 closed |
| Creature definition flag `0x4000` is **Horny**, and slapping him runs a different routine | **STRONGLY INFERRED** — Q158 |

---

## 1. DK2 names its own fields, and that is the find of the day

The executable carries its debug dumps, and they are `printf` calls with the
field name in the format string and the field itself as the argument. Reading
the creature dump gives a **name for 162 creature fields**, keyed by offset,
straight from the developers. The ones this mechanic needed:

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
strings — it is the UI's name for `WorkModifier + WorkEnhancement`. The dumps
name the machinery; the status bar names the number.

---

## 2. The hand itself

Pick-up (`FUN_004bc500`) and drop (`FUN_004bc710`), both on the keeper.

**The array is on the player**: `u16` thing ids at `+0xb2`, the count at
`+0x1bf`, the capacity at `+0x1c0` — which `MaxNumberOfThingsInHand` (64) seeds.

**Pick-up**, in the original's order:

1. Scan the held ids; if this thing is already in the hand, refuse.
2. Refuse if the thing does not exist, or the hand is full.
3. Ask the thing whether it can be picked up (a virtual call).
4. For a **creature**: refuse an enemy creature under one condition, refuse one
   state flag, refuse two specific creature states, and refuse if it cannot
   enter the held state.
5. Set `InHand`, stamp `PickupTick`, clear three fields.
6. Append the id and increment the count.

**Drop** takes a **position and no id** — because the hand chooses:

```
count = NumberOfThingsInHand
if count == 0: refuse
thing = hand[count - 1]          # the last one picked up
NumberOfThingsInHand = count - 1
```

For a creature it clears `InHand`, **stamps `PickupTick` again** (so the field's
name is narrower than the field), enters a landing state, writes the position,
and resets the velocity.

**A just-dropped creature cannot be slapped.** The landing state is one of the
three the slap handler returns on immediately — a small, real interaction
between two verbs that nobody would have guessed.

---

## 3. What refuses a pick-up

**CORRECTED 2026-09-05 (ADR-0064), and the correction is the interesting part.**

The original's four creature-specific refusals are one ownership test, one state
flag and two creature states:

| Original | Here |
|---|---|
| already in a hand | same |
| hand full | same |
| record lacks `CanBePickedUp` | same |
| an ownership test — **unread**, see below | the keeper's own creatures, **plus any creature whose `PrisonOwner` is the keeper** |
| state flag `0x200000` = **`CreatureDying`** | **same** — a creature that is down is refused |
| creature states `0x33` and `0x34` | **absent** — unidentified, and the states do not exist here |

**The `CreatureDying` row used to be recorded the other way round**, and it was
wrong for a day. The first draft of this section read the handler, wrote the
refusal down as *"two creature states and one state flag, none of which this
project has yet"* — **without naming the flag** — and then concluded that DK2
lets the hand take a collapsed creature and that ours refusing one was a
narrowing invented to excuse a missing mechanic. Bit `0x200000` is bit 21 of the
first state-flag word, and the creature dump's own bit list gives bit 21 as
`CreatureDying`. The rule was in the binary the whole time.

The owner settled it independently the same day: *"a corpse can not be picked up
by hand of evil … also an injured creature, lying on the floor bleeding out,
can't be picked up by hand of evil"* (Q161). **The hand never touches a body.
Carrying one is an imp's job and only an imp's.**

**The ownership test is genuinely unread.** The handler's test is
`if (SneakingAs != 0 && owner != me) refuse`, and `SneakingAs` (`0x4a3`) is the
creature type a creature is disguised as — so read literally it refuses an enemy
only while it is disguised, which cannot be the whole rule. The real gate is
most likely the virtual call the handler makes before any of this
(`vtable + 0x28`). Recorded as unread rather than guessed at. What this project
does instead is the owner's description: **own creatures, plus prisoners in the
keeper's own prison**, which is the one exception there is
(`docs/research/prison.md` §2).

---

## 4. The slap, and a cooldown that is not one

`FUN_0048b8a0`. In order:

1. **If the record carries flag `0x4000`, run something else entirely and
   return.** That bit is `IsHorny` (Q158) — one of two unnamed bits in this
   project's `CreatureFlags` until this handler named it.
2. Return on three specific states, including the one a just-dropped creature
   is in.
3. Two other states take a short path of their own.
4. `if (StateFlags1 & 0x8000) || now < LastSlapTick + var19` → **skip the
   knock-back impulse.** Then, unconditionally:
5. `LastSlapTick = now`
6. anger: `AngerSlap` from the record, on one branch of a test
7. a force applied in the slap's direction
8. **if `SpeedChangeTimer` is zero**, raise the creature's speed to 150% and set
   `SpeedChangeTimer` (see §5)
9. **`WorkFasterTimer = 100`**, unconditionally — so a second slap *rewrites*
   the timer rather than extending it, which is exactly the not-stacking the
   owner measured.

**Step 4 is the correction worth writing down.** A first draft of ADR-0062 read
that test as a one-second cooldown on the whole slap, and built it that way; the
test for it failed, the handler was re-read, and the window turned out to guard
**only the impulse**. So:

> **A keeper who can click fast angers a creature as fast as they can click.**
> The six countable slaps that pinned the anger ceiling (Q114) were six because
> a human hand is slow, not because the game rationed them.

This project has no physics for a creature to be thrown by, so
`HandRules.KnockBackWindowSeconds` is recorded and read by nothing.

---

## 5. How long the bonus lasts, and the one thing that does not add up

`WorkFasterTimer` is set to a **hardcoded 100** and decremented by one in the
creature update, every tick. Variable 19 is ticks per game second and is **4**
in every shipped level, so:

> **100 ticks = 25 game seconds**, and `SlapWorkFasterDurationSeconds` is
> **25**.

The hardcoded constant and the named variable agree exactly, which is a second
independent confirmation that variable 19 is what this project has taken it for.

**But the owner watched a slapped imp revert *"after a minute"***, and the table
carries an `IncreasedWorkRateDurationFromSlappingSeconds` of **60** that nothing
in the binary has been found to read. One of the two is wrong and they cannot
both be about this timer. **Q159**, and the test is now cheap and needs no
stopwatch precision at all: slap an imp, select it, and watch the status bar's
percentage fall back to 80 — 25 seconds and 60 seconds are not close.

> **RESOLVED 2026-09-07, and NEITHER of the two was wrong** (ADR-0090,
> `library-and-research.md` §4). The premise was: they cannot both be about this
> timer. Correct — and they are not. **The 60 is read**, by the keeper's
> **research** adder and nowhere else, as the window on a research bonus worth
> +200%. So one slap starts two clocks from the same `LastSlapTick` stamp: the
> work-rate bonus for **25** game seconds and the research bonus for **60**.
>
> The owner's *"after a minute"* was a real minute-long slap effect. The status
> bar reads Efficiency, so the 25 is what a slapped **imp** shows and Q159's
> answer stands; the minute belongs to a mechanism the status bar does not
> print. **The careful phrasing above — "has been found to read" rather than
> "does not read" — is why this correction is an addition and not a
> retraction.** `tile-work.md` used the unhedged form and needed one.

A second loose end in the same handler: `SpeedChangeTimer` is set to
`var19 * 100` = 400 ticks = **100 game seconds**, where
`SlapSpeedUpDurationSeconds` is 25. Recorded, not resolved; nothing in this
project moves a creature faster for being slapped.

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

Three things to take from it:

- **The two bonuses are independent and equal**, ten points each, which is why
  100% is reachable and 110% is not.
- **The non-worker branch is no longer a shrug.** It reads `DroppedToDoJobId`,
  the generic creature state's *"the job I was dropped to do"* — the same
  sentence as `ImpJobData.DroppedTask` one struct up, in a different struct
  because imps keep their job bookkeeping separately. Only workers do tile work
  in this project, so only the worker's branch is built.
- **A third term exists** — 0.05 per unit of a byte counter held on an object
  the creature points at. `IncreasedWorkRatePersentageOfPlayersTorturedCreatureTypes`
  is the obvious candidate and is **UNVERIFIED**; it needs a Torture Chamber to
  be non-zero and this project has none.

**And the offsets are now pinned rather than fitted.** The creature's whole state
object sits at **creature + 0x24E**, which is not guesswork: the creature debug
dump hands `creature + 0x24E` to the state dump, and two independent reads in the
update — `CurrentStateId` at `+0x25E` and `UtilityNewState` at `+0x32D` — land
exactly where that base puts them. From there, `+0x2EB` is `DroppedTask` and
`+0x2E7` is `JustDropped`, four bytes apart, which is how the two came to be read
as one thing for a day.

### Why this project counts in percents and not in 4096ths

DK2's constants are approximations. `0xccc` is 0.7998, not 0.8, and
`0xccc + 0x199` is 0.8997. Run the seam arithmetic through them and 90% gives
**67.47** health a swing, which rounds to 67 by every rule there is — and 67
would have read **304** gold on the owner's counter. It read **309**, which is
68.

So the **displayed percentages are the exact quantity** and the fixed-point
constants are the game's approximation of them. `EfficiencyRules` works in whole
percents with halves rounded away from zero, which reproduces all three measured
numbers exactly (ADR-0002: behaviour, not implementation).

### The aimed bonus: two fields, one look, and one clear

Read out on 2026-09-06, in `ImpState.cpp` and `ImpUtils.cpp`, after the owner
watched a dropped imp keep 90% all the way to the wall. **This section replaced a
shorter one that was wrong in three ways**, and the shape of the error is worth
as much as the answer: a plausible field name four bytes from the right one, and
nothing checking.

**The drop.** Putting a worker down sets `JustDropped = 1`, zeroes
`LastTaskType`, and moves it to the job-evaluation state. That is all it does —
it decides nothing about the bonus.

**The look.** The job chooser (state 10's handler) runs on the next update. Two
things happen because `JustDropped` is set: the *"don't re-evaluate for 0x28
ticks"* throttle is skipped, so a dropped worker thinks on the spot; and after
the ordinary priority chain is skipped — it is skipped, because `LastTaskType` is
zero — it **clears `JustDropped` unconditionally** and runs a spiral scan around
where the worker landed. The predicate is a composite of nine task kinds and its
order is *not* the priority chain's:

> excavation (1/2) → claim (3) → crate for a trap or door (9/10) → carry an
> unconscious creature (13) → reinforce or fix (6/16) → floor gold (7) → loose
> object (8) → corpse (12) → one unidentified

So **a worker dropped beside a marked wall digs it** rather than putting a book
away, even though the book is second in the ordinary chain (ADR-0065). And
**kind 14, claim-the-just-dug-tile, is not in the list at all**: a dropped worker
takes the ordinary claim.

**The bonus.** If and only if the scan takes something, `DroppedTask` is set to
that task's kind. If the scan finds nothing, the worker falls through to the
whole-map chain and finds work anyway — with `DroppedTask` still zero and no
bonus. That is the owner's *"drop it on a random tile and it remains at 80%."*

**The three tiles, and why they are an off-by-one.** The scan is
`ImpUtils.cpp`'s ring walk. It tests the tile underfoot, refuses to look further
if the radius is under `0x1000`, converts the radius to whole tiles, and then
walks rings — but it tests its bound **after** scanning a ring, with a strict
greater-than, and its ring counter starts at 1. So with the `0x2000` it is
handed — nominally **two** tiles at `0x1000` a tile — it scans ring 1, ring 2 and
**ring 3** before stopping. The owner counted exactly that on screen: three tiles
pay, the fourth does not (Q162). Reproduced rather than fixed, per ADR-0002.

The bound is measured to **the tile the work is on**, not to the tile the worker
would stand on: a marked wall four tiles away is reachable from a floor tile
three away and still does not pay. DK2 agrees in shape — its scan's own
pre-filter asks whether there is a dig task *on the scanned tile* before it calls
any predicate.

**The clear — and there is only one.** `DroppedTask` has exactly **two writers in
the entire binary**, both inside the job chooser: the set above, and

```
if DroppedTask == LastTaskType:
    if TakeJob(LastTaskType, radius = 0x7fffffff): return   # bonus survives
    DroppedTask = 0                                         # the only clear
```

which runs at the *top* of a later evaluation. So the bonus survives the walk, it
survives the digging, and it survives finishing the tile: a worker that finishes
one marked wall and finds another anywhere on the map **keeps its 90%**. It is
lost only when a worker that still calls itself an excavator can find nothing
left to excavate. Note also that the whole thing is guarded on
`DroppedTask == LastTaskType`: a worker that has drifted onto some other kind of
work does not lose the bonus, because the comparison fails and DK2 falls straight
through.

The owner's 2026-09-06 report is this mechanism end to end:

> *"it stays at 90% until it gets to that marked tile and starts excavating, it
> doesn't revert to 80%, however … I unmark it before an imp gets to it, it gets
> to that tile, "sees" it is unmarked and it drops back to 80%."*

The unmark case is the clear, arrived at from the other side: the job is
invalidated, the worker re-evaluates, it is still an excavator, and there is
nothing left marked.

**What this replaced.** *"The flag is cleared by `ClearJob`, so the bonus is
worth one job"* — which fitted an older owner report (a dropped imp mining its
first seam at 90% and the next at 80%) and is consistent with it only if that
seam was the last marked work on the map. Q177 is the observation that would
separate them and it is one glance: mark two seams far apart, drop an imp on one,
and watch the percentage after the first block falls.

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
seconds. Which is why moving a creature across the dungeon costs a keeper
nothing at all, and why parking one in the hand is a way to *calm* it: a
Goblin's `AngerInHand` is **−250** and a Warlock's −500. Jack is the exception in
this as in everything — **+500**, and slapping it calms it (−5000).

`TimeInHandBeforeCreaturesBecomeAngrySeconds` is **1000** and is **not** this
number, which is hardcoded at 20. A raw address scan of its cached global in
`DKII.EXE` v1.70 finds exactly one occurrence: the loader store. There is no
reader anywhere in `.text`, so variable 193 is **CUT**, not a duration in ticks
and not a disabled mechanism (Q160, binary, 2026-09-07).

---

## 8. What is not built

- **Carrying anything but a creature.** The hand holds 64 things and DK2 puts
  gold, chickens, and unconscious bodies in it. This is the next mechanic and it
  is what the hand is really for.
- **The knock-back**, and every other physical consequence of a slap.
- **The speed boost** (§5).
- **Dropping onto a room**, which the original tracks with a flag of its own
  (`WasLastDroppedOnARoom`) and which is how a creature is put into a Library or
  a Training Room.
- **Dropping onto enemy ground**, which the original's drop handler handles by
  changing the creature's owner under a condition. ~~Note what that proves: a
  creature drop is *not* restricted to owned ground, or the branch would be
  unreachable. The owned-ground rule the owner described is the **object**
  drop's.~~ **Corrected 2026-09-06 (Q198).** The owner: *"imps are the only
  creature that can be dropped onto dirt path tiles, those are tiles that are
  unclaimed by any keeper"* — every other creature only onto the keeper's own
  ground. So the owned-ground rule is the creature drop's too, with **one
  exception for workers and one kind of tile**. The handler's branch proves only
  that the *handler* does not refuse, which was already known (Q196): the gate
  is the input layer's, and it is that layer that lets an imp go down on
  unclaimed floor and nothing else. Whatever reaches the owner-changing branch
  is not the ordinary drop. **Built the same day (ADR-0077):**
  `HandRules.CanDropCreatureOn` — the dropping keeper's own tile for everyone,
  plus *unclaimed floor* for anything carrying `IsWorker`. `IsUnclaimedFloor` is
  structural rather than by id: not `Solid`, not `Ownable`, not `Room`, and
  neither `Water` nor `Lava`, which in the shipped table of 41 is **Dirt Path
  and the Mana Vault** — the identical flag word `0x10008000`, and each with an
  `Ownable` claimed counterpart. The edges of "unclaimed" (water, a bridge, a
  rival's *unclaimed* room floor) are still UNKNOWN and are **refused**, which
  is the narrower reading; item 5 of the observation checklist is what would
  change it.
- **A chicken dropped into somebody else's Hatchery becomes theirs** — object id
  9, a tile owned by another keeper, a room of type 4. Read, not built.
- **A chicken or a pile of gold dropped ONTO a creature is earmarked for it**,
  by the creature's id written into a field on the object. That is a
  reservation, and it is the one case where a bird is not up for grabs. The
  prison search does not consult it (ADR-0074); whether anything else does is
  unread.
- **The cursor's target search** (`FUN_0044dec0`), which is a rule rather than a
  ray-cast: a 3x3 sweep, an **octagonal** distance (`max + min/2`), a quarter
  tile for an object against half a tile for a creature, the Dungeon Heart
  admitted by id, and **a creature preferred over an object** whenever both are
  in reach. Presentation, so not built — but worth having written down before
  somebody invents a worse one.
- **Possession**, the other thing a keeper does to one creature at a time.
