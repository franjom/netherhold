# Mechanic: fear and flight — the accumulator, the counter, the back-off and the withdrawal

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-22. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046). Field names are DK2's own, from its creature dumper
  (creature-object.md).
- `Creatures.kwd` from the reference install for the `Fear` and `Threat` stats.

Original version: DK2 v1.70 (GOG)

Questions: answers Q367; opens Q372–Q375; corrects combat-fight-state.md §1 and
§2 step 6. `combat-fight-state.md` owns the fight state this hangs off;
`combat-acquisition.md` the acquirer whose own withdraw check §3.4 describes.

**What this settles:** fear is not a reaction to being hit. Every tick a fighter
**radiates** its level-scaled `Threat`, divided among everyone it fights, into
the intake of each opponent it can see; a creature's own fear is last tick's
intake scaled by how far it stands from its heart, nothing at all beside it. Three
ticks over its level-scaled `Fear` and it **backs off** — holds its attacker at
five quarters of reach for a timer between two balance variables — then **runs**
for its lair, its heart, a room or a nearby tile. A creature under a third of its
health and more than five tiles from its heart runs without the preamble.

---

## 1. The fields

The engagement record at `creature+0x4FE` (combat-entry.md §6) carries them; the
dumper prints each by name.

| offset | name | what |
|---|---|---|
| `+0x524` | `TempFear` | word; this tick's intake from everyone fighting me |
| `+0x526` | `CurrentFear` | word; last tick's intake, scaled by the heart distance |
| `+0x528` | `OverThreatCounter` | byte; ticks over the threshold, clamped |
| `+0x532` | `BackOffTimer` | byte; ticks of backing off left |
| `+0x533`, `+0x535` | `ThreatThing`, `ThreatThingThreshold` | the tag radiating most at me; read only by the creature update `0x00488930`, which zeroes both |
| `+0x52A`, `+0x52E` | `OldFightPos`, `OldFightTick` | stamped by the withdraw state; **read by state 1** (§8; "no reader" was wrong, corrected 2026-09-25) |
| `+0x3D7` | `FearlessUntil` | set only by the Casino leave hook, after 120 of the Casino update's acts (150 game seconds) have made a creature `CasinoGamblingFearless`: `now + var19 × GamblingDelayBeforeBecomingFearlessSeconds` (`leave-hook-bodies.md` §1, built ADR-0322) |

## 2. The accumulator (`0x0045E4F0`)

Called once a tick per creature from the creature update (`0x00488930`), after
`ProcessFight`. Returns at once for a creature in a stunned state (209, 255, 256,
257 — `0x0048F350`) or with a nonzero `ChickenTimer`. Then, in order:

1. **Handover.** `CurrentFear = TempFear; TempFear = 0`.
2. **The heart factor.** `d` = Chebyshev tiles from my tile to my owner's
   `DungeonHeartPosition` (`player+0xB43`, via `0x004C5F50`), capped at 10.
   `CurrentFear = CurrentFear × d / 10`, stored as a word. **At the heart a
   creature fears nothing; ten tiles out it fears in full.** A player with no
   heart keeps the field's initial value, **(1, 1)**: the player initialiser
   `0x004B8640` stores 1 in both words (`0x004B883E`, `0x004B8845`), and the
   setter `0x004C5F60` is the only other writer. (Corrected 2026-09-26: this
   said the origin, inferred from the setter being the only writer.)
3. **My threat.** The level-scaled `Threat` stat (`def+0x698`, file `0xF12`,
   table row 3 — level 1 unscaled). Divided by my **engagements**: the nonzero
   slots of both arrays plus one if `Attacking` (`+0x502`) is set. None → return.
4. **Two owner modifiers**, counts of the owner's **Crypts** (room type 26) by
   occupant: `0x004D8E60(room, occupant)` moves a Crypt's occupant (`room+0x53`)
   between 18 group bytes at `player+0x108A + group`, the group chosen by creature
   type (callers: the shared leave `0x0047B920`, `0x0047AF20`, `0x0047B120`,
   `0x0047B4A0`, Crypt states 274 and 276). `a` = `+0x108E`, Crypts holding a
   **Vampire** (8); the second, `+0x108F`, an **Unique Vampire** (42). The share
   is scaled by `(100 + 30·a)/100` and then, when the second is positive, by
   `(100 + 40·a)/100` — **with the first count in both**, a DK2 slip (read at
   `0x0045E638`, `0x0045E667`). This simulation holds no Crypt occupant, so the
   factor is 1 (Q375 for whether a shipped level fills a Crypt).
5. **Give.** For my target (if a live thing) and every nonzero slot: live, with
   `+0x12` clear, and a **line of sight** from my position to its
   (`0x004512F0(world+0x9DA, mine, theirs, 0, 1)`): the receiver's
   `TempFear += share × (10 − r)/10` where `r` is *its* owner's `+0x108D` count,
   Crypts holding the **Maiden of the Nest** (45), when positive (read at
   `0x0045E6F2`, `0x0045E7BF`, `0x0045E876`, and `0x0045C5D3` below); a share above its `ThreatThingThreshold` records my tag as its
   `ThreatThing`. A thing target takes the write into its own bytes.

**A second entry, `0x0045C5D0(creature, amount, source)`**, adds `amount` to
`TempFear` with the same `+0x108D` discount and the same `ThreatThing` rule. Two
callers: the timed-effects update `0x0048E160`, which each tick a countdown at
`+0x463` runs adds `+0x465` from `+0x467` — a fear-over-time effect a spell sets —
and `0x004B32A0`, which gives every creature of a player within a radius and in
sight of a point an amount at once. Both are spell effects, read and not built.

## 3. The checks in the fight state

`spFighting` step 6 (combat-fight-state.md §2) and, separately, the acquirer.

### 3.1 The escape test (`0x00460C50`) — corrected

Walks the eight neighbouring tiles. One on the map that I can route to
(`0x004D0E60`, word `0x80`) and that holds **no** hostile creature whose
`Attacking` is my tag is an **escape**, and the routine returns 0 at once. Only
when every reachable neighbour is held by an attacker of mine does it return 1.
**The fear checks run when an escape exists** and are skipped when cornered —
the opposite of what combat-fight-state.md §2 said before this reading.

### 3.2 Withdraw (`0x0045EDA0`)

Not a `CombatPitFighter`; the owner's `Number` (`player+0x10FC`) equal to 1 —
the **Good** player — counts as 6 tiles when `Flags +0x46F` bit 4 is set and 0
otherwise, anyone else measures Chebyshev tiles to `DungeonHeartPosition`; not
`IsFearless` (`def+0x686` bit 18); the turn past `FearlessUntil`; **more than 5
tiles**; and health under a third of the level-scaled `Hp` (`0x0048F250`). In
`spFighting` the state must also be a turn old; then the withdraw state (§5)
takes over and, succeeding, the handler returns 1. **Bit 4 is never set in the shipped
game** (read 2026-10-04): its one setter `0x0048E060` is called only by trigger action 1
(`0x00518C3F`) and the neutral, Good and keeper placed-creature loaders (`0x005203C2`,
`0x005207D7`, `0x00520B1E`), each copying bit 4 of the authored `Flags` byte; the
constructor writes `0x09` and hero-party creation does not copy the bit. No placed creature
or type-1 action sets it (`trigger-dispatch-tables.md`); party members' `Flags` do, uncopied.
So a hero never withdraws and never runs to its gate, as the owner saw and as ours does.

### 3.3 The counter (`0x0045EC80`)

`OverThreatCounter` over 5 → 0; over 3 → 3. `CombatPitFighter`, `IsFearless` or
inside `FearlessUntil` → 0, return 0. Else the level-scaled `Fear`
(`def+0x696`, file `0xF10`, table row 2): `CurrentFear > Fear` → `++`, else → 0.
A `NonFighter` (`def+0x67E` == 0) with any live creature that is **not** an Imp in
its melee or ranged slots (`0x0045EC10`: `+0x36F != 1`) → 3. **Return 1 at 3 or
more.** So an Imp attacked by a hero or a fighter flees at once, and an Imp
attacked only by Imps does not. (Corrected 2026-10-03: this said "with an Imp among
its attackers", and ours built it that way until ADR-0347.) In the flee state the counter then sits at 4 while afraid: clamped to 3,
then incremented.

### 3.4 The flee entry (`spFighting` after the counter)

`PossessedCreatureFollowing` is dropped if set. Then **`BackOffTimer`**:

```
threshold = level-scaled Fear
if threshold == 0:            timer = 0        (the "fear threshold of %d" assertion prints)
else: q = min(4, CurrentFear / threshold)
      timer = BackOffMinimumDurationSeconds
            + (BackOffMaximumDurationSeconds − BackOffMinimumDurationSeconds) × (4 − q)
```

Byte arithmetic throughout; variables 211 and 212 ship as 0 and 30, so the timer
is 120, 90, 60, 30 or 0 **ticks** (120 only when the counter was forced with no fear, q = 0) — named in seconds, counted once per call of a
handler that runs every tick. Then the timer is **zeroed** when there is no target, when I
am a `NonFighter`, or when my target is a live creature whose per-tick run speed
(`def+0x5B8`, the loader's `RunSpeed / var19` floored at 1) **exceeds mine** —
no point backing away from something faster. `SetState(203)`.

**The approach state's flee** (`0x00470E30`, state 8) runs the same counter and the
same timer formula but its own zeroing: only an `AlwaysFlee` definition (`def+0x686`
bit 3, the Imp's) gets 0. No target, a `NonFighter` and a faster target keep the
timer there. It also flees only while `PossessedCreatureFollowing` (`+0x482`) is 0
(read 2026-10-03, ADR-0347). The swing action,
if one is current, is not ended by this transition as read (Q373).

**The acquirer's own withdraw** (`SelectAVictim` at `0x0045DEE0`): after the
attack type is chosen and before the fight-starter, `0x0045EDA0` passing clears
the target and, unless `CreatureDying`, runs the withdraw state; success returns
1 with no fight, failure restores the target and fights. The `NonFighter` arm
(`0x0045BDF0`) does the same after its head-count.

## 4. State 203, fleeing (`0x004602E0`), in order

1. A dead target is dropped; two steering body bits cleared.
2. **`BackOffTimer == 0` → the run** (step 9). Else decrement it.
3. Room notice; target validity as in the fight state — invalid → unlink, clear.
4. Withdraw (§3.2) with the turn-old test; success → return 1, and the body's
   `+0x10F` bit 0 is set for a keeper's creature and cleared for a hero's.
5. The counter (§3.3). **Not afraid, or no target → `SetState(199)`**, back to
   the fight, unless `PossessedCreatureFollowing` is set.
6. The gate `0x0045A6D0(0)` refusing → return 0: **the state ends** and the
   dispatcher (`0x00476FC0`) sets state 1, idle, whose Combat leave hook clears
   the target (`combat-category-hooks.md` §2). A target no longer live → return 1.
   The handler calls no action routine, and `SetState(203)` from the fight runs no
   hook (same category), so a swing in progress still lands: the action runs in
   `CreatureAction_Run` `0x00496AA0`, called from `Creature_Update` (`0x00489A68`)
   whatever the state (STRONGLY INFERRED: earlier exits of the update unread).
7. **The nearest attacker** (`0x0045C690`): over my melee then ranged slots, the
   lowest distance — an eighth off across the alignments, the `Creeping`
   percentage applied — that is `IsAttackable`, not disguised as my effective combat side, seen within
   sight (Imp: a tile) or reached within hearing (Imp: nothing), and routable.
   A different one from my target: unlink, take it, register with my attack
   nibble; registration refused → clear the target, return 0.
8. **Stand off.** `range = max(0x4000, target.AttackRangeSquared if the target is
   in the combat group) × 5/4`. Farther than that: run speed, return 1. Nearer:
   **step back** (`0x00460DA0`) — a body impulse of `−(shufflePerTick × 24 & ~15)`
   along my heading (`+0xF4`, the sine and cosine tables at `0x006C1C88` and
   `0x006C1488`), allowed when a route to the point exists and the tile at
   `(x, y+vy)` or `(x+vx, y)` is not solid, else `±0x3000` along whichever axis
   routes; `0x004B1F60` adds it to the creature's `+0x110/+0x114/+0x118`
   accumulator, after the shuffle speed is set (`0x004887D0(2, 0)`). What spends
   the accumulator is unread (Q373). Impossible: cornered (§3.1) → `SetState(199)`, else
   `BackOffTimer = 0`.
9. **The run.** `0x00460B50`: a keeper's creature (not neutral, not Good) with a
   live target standing strictly inside one of its owner's rooms grown by
   `GuardRoomReportingDistanceTiles` (variable 190, ships 7) on every side counts
   heads with `0x004FAC40` over five tiles of its position — the summed
   level-scaled `Threat` of each side's standing creatures — and **ends the state
   when its own side at three quarters outweighs the enemy's**; otherwise a
   `Scout` (`JobClass` 3) with Call to Arms off and nobody already responding
   within five tiles goes to the nearest room to raise the alarm
   (`0x004752B0`, combat-alarm.md §2–§3, built with ADR-0166), unlinking and
   setting body bit `+0x10F`.0; anyone else ends the state.
   **Away from its rooms**, or with a dead target: unlink, clear, and the
   withdraw state — its result is the handler's.

## 5. The withdraw state (`0x004611D0`, `FightWithDrawState.cpp`)

A destination, then a run:

1. **Owner Good:** with `Flags` bit 4, the first hero gate (world `+0x12` list,
   `+0x32` bit 0) whose tile centre routes (`0x004D0E60`, word 0); without it, or
   none routing, **nothing** — the other steps are the else-branch.
2. **`LairId` (`+0x3B7`) set:** the lair object's position, **no route check**.
3. Else a random unruined room of type 5 (`0x004C6460`) — the heart — at its
   `+0x27/+0x29` tile **±2 on each axis**, each sign a `random(2)`; routable with
   word `0x80` for an Imp and 0 for anyone else (`(-(type != 1) & 0xFFFFFF80) + 0x80`;
   the earlier "0x80 (Imp: 0)" had it inverted, corrected 2026-10-02). **A 5×5
   heart's word is one tile past its centre** (STRONGLY INFERRED: Levels 1, 2 and 3
   read (15,29), (20,39), (27,37) for hearts at (12,26), (17,36), (24,34); dragged
   rooms read their floor centre — `RoomInstance.CentreFieldX`, Q515), so three of
   the four points are wall and the step fails: Level 3's imps landed at the heart
   6 times in 21 and at a room 15.
4. Else **three tries** of `0x004C63D0`: `random(NumberOfRooms)` walked over the
   room list **skipping bridges (types 8, 17, 22) without counting down**, so a
   draw past the last non-bridge room finds nothing; the room's `+0x27/+0x29`
   tile centre, routable as above.
5. Else a point away from the target — `random(4|dx|)` and `random(4|dy|)` tiles
   opposite the target's side, up to five tries, on the map and in sight — that
   the next step then **overwrites**: two tries (five when the target is dead or
   gone) of `myPos + (random(3) − 1, random(3) − 1)` tiles, accepted when on the
   map (`world` vslot `0x200`) and in my line of sight. So without a lair, heart
   or room a creature withdraws at most one tile, if at all.

Then: in the combat group, or `SetState(199)` succeeding; `0x00471890(&dest,
202, 0xE8)`, falling back to word `0x68` — **state 6, en route, arriving in
state 202** — sets the **run** speed (`0x004887D0(1)`), plays the definition's
`+0x6E3` sound, and stamps `OldFightPos`/`OldFightTick` unless an Imp. Return 1.

**State 202** (`0x00461850`, "fleeing") sets the **walk** speed and returns 0:
the dispatcher makes the creature idle where it arrived. The gate
`0x0045A6D0` refuses a creature en route to 202 or 251, so it will not look for
a fight on the way.

## 6. Speed modes — a correction

`0x004887D0(mode, speed)` is the speed-mode setter (steering-behaviours.md §7.5):
0 walk, 1 run, 2 shuffle. combat-fight-state.md §1 called its call at the fight
state's entry a "cue": **a creature entering the fight runs**, a withdrawing one
runs, a backing-off one shuffles as it steps and runs as it stands, and the
arrival state walks again.

## 7. Built and not built (ADR-0165)

**Built** (`FearRules`, `CombatSystem`): §2 steps 1–3 and 5 for creature
receivers, run after each creature's fight processing in id order; §3.1–3.4
including the acquirer's withdraw; §4 in order with the nearest-attacker rule,
the stand-off and the head-count; §5 steps 2–5 and the run, arriving idle;
`Running` as the body's speed cap (`RunSpeed` scaled by the Speed row, STRONGLY
INFERRED). `CreatureActivity.Fleeing` is state 203, `Withdrawing` state 6 toward
202, entered from 199 when not already in the combat group; both hold those ids since
ADR-0208.

**Approximated, named:** the step back is a route three quarters of a tile
straight away from the target, not a heading impulse (Q373); a hero's flag bit 4 is
absent, as in every shipped level (§3.2). The room
word `+0x27/+0x29` is `RoomInstance.CentreFieldX/Y` since ADR-0323 (§5 step 3).

**Not built:** hero gates; the stunned gate (a chicken is skipped, ADR-0302); the owner modifiers (Q375);
`ThreatThing`; the two spell sources of §2; the body bit `+0x10F`.0.

## Confidence

**CONFIRMED (binary):** every step of §2, §3, §4 and §5, the field names, the
two balance variables, the escape test's direction, the run-speed comparison's
direction, the dead overwrite in §5 step 5, the dispatcher's idle on a zero
return, §6.

**CONFIRMED (binary, 2026-09-26):** (1, 1) as a heartless player's heart position.

**STRONGLY INFERRED:** the
run speed scaled by the Speed row; `+0x12` clear meaning a live thing.

**CONFIRMED (binary, 2026-10-04):** bit 4's writers (§3.2); the Crypt counts (§2 step 4).

**UNKNOWN:** Q373 (the step back's accumulator), Q375 (a filled Crypt in play).

## 8. The walk back (read 2026-09-25, ADR-0225)

**The stamp.** The withdraw state's success tail (`0x004617B2`–`0x00461826`) sets the run speed,
makes effect `0xDC`, and then, unless the creature's `TypeId` (`+0x36F`, the dumper's name) is
1 (the Imp), stamps `OldFightPos` and `OldFightTick`. It reads `+0x1A`, the x coordinate, for
**both** words. **CONFIRMED.**

**The reader.** State 1 (`0x004773D0`) tests the stamp before its hero, neutral, worker and
default arms.
- **Stale:** with `now >= OldFightTick + 500` it clears all three.
- **Healthy:** with `(level-scaled Hp × 3) >> 2 < Health` it walks to the tile's centre,
  `0x00471890(point, 1, TypeId == 1 ? 0x80 : 0)`, and clears the stamp on success.
- **Otherwise** the stamp is kept, and the other arms run. **CONFIRMED.**
