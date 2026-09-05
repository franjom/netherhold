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
| **Dropping is last-in-first-out** | **STRONGLY INFERRED** — the drop handler reads back the slot it is about to free |
| A thing already in a hand cannot be picked up again | **STRONGLY INFERRED** — an explicit scan of the array before anything else |
| A slap raises Efficiency by ten points, and does not stack | **CONFIRMED** (owner, 2026-09-05) — *"it raises from 273 to 310 after the first slap and stays there no matter how many times i slap that imp"* |
| **Being dropped next to work the keeper marked** raises it by ten points too, and dropping on empty ground is worth nothing | **CONFIRMED** (owner, 2026-09-05) — both halves watched |
| **There is no cooldown on a slap** | **STRONGLY INFERRED** — the `LastSlapTick` window guards the knock-back impulse and nothing else. See §4 |
| A slap costs `SlapDamage` health — 100 of an Imp's 500 | **CONFIRMED** by the shipped data; **UNVERIFIED** that the original applies it on the same path |
| `AngerInHand` applies **once a game second, from 20 game seconds after the pick-up** | **STRONGLY INFERRED** — read from the creature update |
| The slap's work bonus lasts **25 game seconds** | **STRONGLY INFERRED and in conflict with the owner's stopwatch** — see Q159, §5 |
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
| `0x2eb` | **`ImpJobData.JustDropped`** | the aiming bonus |

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

A second loose end in the same handler: `SpeedChangeTimer` is set to
`var19 * 100` = 400 ticks = **100 game seconds**, where
`SlapSpeedUpDurationSeconds` is 25. Recorded, not resolved; nothing in this
project moves a creature faster for being slapped.

---

## 6. Efficiency, and the two flags that raise it

The creature update rebuilds `WorkEnhancement` from scratch each tick:

```
bonus = 0
if IsWorker:                        # definition flag bit 0
    if WorkFasterTimer != 0:  bonus += 0x199      # 409 = 0.0999
    if JustDropped != 0:      bonus += 0x199
else:
    if WorkFasterTimer != 0:  bonus += 0x199
    if <another field>  != 0: bonus += 0x199
if counter > 0:               bonus += counter * 0xcd     # 205 = 0.05 each
WorkEnhancement = bonus
```

Three things to take from it:

- **The two bonuses are independent and equal**, ten points each, which is why
  100% is reachable and 110% is not.
- **Workers and non-workers read different second flags.** Only workers do tile
  work in this project, so only the worker's branch is built; the other is
  recorded here rather than guessed.
- **A third term exists** — 0.05 per unit of a byte counter held on an object
  the creature points at. `IncreasedWorkRatePersentageOfPlayersTorturedCreatureTypes`
  is the obvious candidate and is **UNVERIFIED**; it needs a Torture Chamber to
  be non-zero and this project has none.

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

### `JustDropped`, and how long it lasts

Set to 1 when a dropped imp enters the dropped-work job, alongside a **local
scan** of radius `0x2000` — two tiles in the original's fixed point — for
something to do near where it landed. Cleared at a later job evaluation.

This project's `HandSystem` tests the tile underfoot and its four orthogonal
neighbours, which is what *"drop an imp adjacent to it"* describes; the
difference from DK2's two-tile radius shows only for a drop two tiles away
(Q162). The flag is cleared by `CreatureState.ClearJob`, so the bonus is worth
one job — which is exactly what the owner watched:

> *"an imp excavating its first seam has its efficiency at 90%, hits for 309-310
> per swing, once it excavates its first gold tile its rate drops to 273 per
> swing, 80% efficiency."*

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
number, which is hardcoded at 20. What variable 193 is for is **UNKNOWN**
(Q160).

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
  changing the creature's owner under a condition.
- **Possession**, the other thing a keeper does to one creature at a time.
