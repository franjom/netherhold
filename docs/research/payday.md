# Payday

**Status:** read from the binary and **BUILT**, 2026-09-06 (ADR-0078).
Questions: Q9 (answered), Q201 (new).

Payday is the last unbuilt item of M4's original seven, and it turns out to be
one of the most completely readable mechanics in the executable: a clock on the
player, a wage on the creature, and three creature states that walk it to the
money. Every address below was decompiled; nothing here is inferred from a
variable's name.

---

## 1. What it is, in one paragraph

Every `PayDayFrequencySeconds` a keeper's payday falls due. Every creature it
owns is **credited** a wage — its `Pay`, scaled by its level — and then sent to
**walk to a treasury and take the gold off the floor itself**. A creature that
cannot find one, or arrives to find the tile empty, gives up after two attempts
and stays owed. Wages **accumulate**, so a creature that misses a payday is owed
two.

The last clause is what makes this worth building carefully: it is a real
economy with a real failure mode, and the failure mode already has a consumer —
`AngerNoPay`, which `creature-anger.md` records as *"No — there is no payday"*.

---

## 2. The clock — `FUN_004be1a0`, called per player

```c
if (player[0xb53] != 0 && now >= player[0xb53]) {     // next payday due
    message(0x2e, playerId, ...);                      // "It's payday"
    for (creature in player's creature chain) {         // chain steps via +0x10
        if (isPayable(creature) && !isExempt(creature)) {
            if (creature[0x3e2] == 0) {
                accrueWage(creature);                   // FUN_0048b6b0
                creature[0x474] = 0;                    // reset failed attempts
                tryToCollect(creature);                 // FUN_00472320
            } else {
                creature[0x3e2] = 0;                    // consume SkipNextPayment
            }
        }
    }
    endMessage(0x2e);
    player[0xb53] = now + PayDayFrequencySeconds * ticksPerSecond;
    player[0xb4f] = now;                                // when this payday began
}
```

**`creature[0x3e2]` is `SkipNextPayment`**, and this is the first time it has
been seen doing anything. Q9 had the name from Flame's symbol map and guessed it
was *"a mood input rather than an accounting flag"*. It is neither: it is a
**one-shot ticket that suppresses exactly one payday and is consumed whether or
not it was needed**. That is precisely the manual's account of a treat
(§7.6.2.2, `manual-1999.md`): *"wages on the next Payday"* — **a treat is a
prepayment**, and this is where the prepayment is spent.

Note the branch: a creature with the flag set is **not** credited a wage at all.
It is not "paid early and skipped"; the payday simply does not happen for it.

### 2.1 The three variables are all live, and that is worth stating

Following the standing rule — *a named balance variable is not evidence the
engine consults it* — each was scanned for readers:

| Variable | Global | Hits | Reader |
|---|---|---:|---|
| `PayDayFrequencySeconds` = 600 | `0x006F25D1` | 2 | `FUN_004be1a0`, the clock above |
| `TimeBeforeFirstPayDaySeconds` = 600 | `0x006F2611` | 2 | `FUN_004be330`, the initialiser |
| `PayDayCutOffTimeSeconds` = 120 | `0x006F28A3` | 3 | `FUN_00414bc0` and `FUN_004872a0` |

(One hit in each is the setter stub.) So all three are real, which is a
different result from the four dead fields this project has caught — and worth
recording as the *negative* of that pattern rather than assuming it.

**`PayDayCutOffTimeSeconds` is still not understood.** Q9 guessed *"how long a
creature has to reach the treasury before it counts as unpaid"*. Its two readers
are **not** in the payday states, which is evidence against that reading; one is
in the creature initialiser. Left UNKNOWN rather than assumed.

---

## 3. The wage — `FUN_0048b6b0`

```c
uint accrueWage(creature) {
    level   = creature[0x403];
    basePay = *(u16*)(creatureDef + 0x69a);

    wage = (level == 1) ? basePay
                        : (levelPercent[level] * basePay) / 100;

    creature[0x470] = max(0, creature[0x470] + wage);
    return wage;
}
```

**`creatureDef + 0x69a` is `Pay`**, and this was pinned rather than guessed. The
loader (`FUN_00492320`) stores it at `0x00492467` from frame offset
`EBP + 0x130c`; the frame-to-record delta is `0x3F0`, anchored on the already
established `EBP+0x131a ↔ record 0xF2A` (`angerNoFood`). That gives record
offset **`0x0F1C`**, which `kwd-creatures.md` lists as `u16 pay`. Exact hit.

`levelPercent` is a table of `s16` percentages at `DAT_006e4154 + 0x129`,
indexed by level — the same globals block that holds `V19` at `+0x40`.

**Two consequences worth keeping:**

- **Wages accumulate** (`+=`, clamped at zero). Being owed is a state that
  persists across paydays, so a poor keeper's debt compounds.
- **A level-10 creature costs more than a level-1 one**, by a percentage the
  table gives. This project has creature levels (Q119) and has never had
  anything that spends them.

---

## 4. Collecting it — a decision and three states

### 4.1 The decision, `FUN_00472320`

Called from the needs routine. Every gate:

| Gate | Meaning |
|---|---|
| `(creature[0x3e6] & 0x200000) == 0` | not `CreatureDying` — the same bit the pick-up handler refuses (ADR-0064) |
| `creature[0x3cb] == 0` | a guard shared with the flee routine |
| `(creature[0x3ea] & 0x20) == 0` | a per-payday bit; cleared on the else branch |
| **`creature[0x474] < 2`** | **at most two failed collections**, and the clock resets this counter each payday |
| `creature[0x470] != 0` | something is actually owed |
| `stateCategory != 0x14` | not already in a payday state — states 187-189 are category 20 |
| `state != 0xc1` | one specific state is excluded |

→ `SetState(0xbb)` = **187**.

### 4.2 State 187 `FUN_00472390` — find the money

Walks the **player's room chain** and keeps a room when:

- its type is **1 (Treasury)**, or **5 (Dungeon Heart)** with `(room[0x9a] & 1) == 0`; **and**
- `room[0x49] != 0` — **the room actually has gold in it**.

Candidates are ranked by **straight-line squared distance** in fixed point, the
identical ranking the prison's food search uses (`prison.md` §7). The winner is
then searched tile by tile for an object of kind 2 whose `+300` is 3 — a gold
pile — and the creature is issued `move(target, resume: 0xbc, flags: 0xe8)`.

**When there is nothing to walk to, the two failures are distinguished:**

| Case | Message |
|---|---|
| no treasury or heart with gold at all | `0x37` |
| there is one, but no route or no pile found | `0x2b` |

### 4.3 State 188 `FUN_00472690` — arriving

Re-checks that the player still owns a room of type 1 or 5 (`FUN_004539b0`) and
bails if not — so a treasury sold out from under a creature mid-walk aborts the
trip. On arrival: `SetState(0xbd)` = **189**.

### 4.4 State 189 `FUN_00472740` — take it

```c
if (creature[0x470] != 0) {
    taken = takeGoldFromTile(creature.tileX, creature.tileY, creature[0x470]);
    if (taken == 0) { creature[0x474]++; return 0; }     // came up empty

    ... effect 0xc3, floating number, sound ...
    FUN_0048ae70(taken / 2);
    FUN_0048e810(-taken);
    if (creature[0x470] == 0) FUN_00486f80(4);           // fully paid
}
```

**The gold comes off the tile the creature is standing on.** This is the
strongest independent confirmation yet of ADR-0015's finding that **DK2 has no
player gold counter** — payday, the one moment where a global balance would be
most convenient, is implemented as a withdrawal from a tile.

`FUN_0048ae70(taken / 2)` and `FUN_00486f80(4)` are **unread**. The halving is
conspicuous and has no candidate.

---

## 5. What this gives the implementation

| Piece | Status |
|---|---|
| Payday interval, first payday | **read**, and the variables are live |
| Wage = `Pay` scaled by level, accumulating | **read**, field pinned to record `0x0F1C` |
| `SkipNextPayment` as a consumed one-shot | **read** |
| Walk to the nearest treasury/heart **that has gold** | **read** |
| Take gold from the tile underfoot | **read**, and it confirms ADR-0015 |
| Two failed attempts per payday, then stop | **read** |
| Two distinct "cannot pay" messages | **read** |
| `AngerNoPay` firing on an unpaid creature | **READ AND BUILT** the same day (ADR-0079) — see §6 |
| `PayDayCutOffTimeSeconds` | **UNKNOWN**, and its readers are not in the payday path |
| `FUN_0048ae70(taken/2)`, `FUN_00486f80(4)` | **UNKNOWN** |

**Built on 2026-09-06 (ADR-0078):** `PaydayRules`, `PaydaySystem`,
`CreatureActivity.SeekingWages`, three fields on the creature and one on the
player, 13 tests and one scenario. Hash 30 -> 31.

**The anger gap closed the same day** — §6. What is left is `PayDayCutOffTimeSeconds` and the two unread calls in state 189.

~~The gap that matters is the first UNKNOWN: **where the anger comes from.**~~ The
owner has already described the consequence (Q9: unhappy, then angry, then
leaves — or rebels, if converted), and `AngerNoPay` is +60/s in the table, so
the mechanism exists at both ends and the middle has not been found. It is
probably charged from the needs routine rather than the payday states, in the
same shape as `AngerNoFood`. That is the next thing to read.

---

## 6. Where the anger comes from — `FUN_00478d00` (2026-09-06, ADR-0079)

Reached from the creature update **immediately before the needs routine**, and
it charges all three standing sources one after another, **independently**:

| Source | Field | Condition |
|---|---|---|
| `AngerNoLair` | `def+0x6c1` | no bed **and** the room type is available |
| `AngerNoFood` | `def+0x6c3` | `HungerNeed` >= level-scaled `HungerFill` **and** a hatchery is available |
| **`AngerNoPay`** | **`def+0x6c5`** | **`WageOwed != 0`, and level-scaled `Pay` != 0** |

### 6.1 The pay clause has no treasury test, and that is the finding

Both siblings test that the **room** exists. The pay charge tests **nothing but
the debt**: no treasury clause, no "did it try and fail" clause. A keeper with no
vault at all and a keeper whose creature has not walked over yet are charged
identically, from the moment the clock runs until the money is in hand.

Writing the sibling rule here would have been very natural and would have been
wrong. **You cannot excuse yourself by having no treasury.**

### 6.2 The fields were pinned, not guessed

The loader's anger block at `0x00493964` stores five fields from consecutive
frame offsets `0x1318`–`0x1320`; at the established `0x3F0` frame-to-record delta
those land exactly on `0x0F28`–`0x0F30`, which `kwd-creatures.md` lists as
`angerNoLair / angerNoFood / angerNoPay / angerNoWork / angerSlap`.

**And the first four are divided by ticks-per-second while `angerSlap` is not** —
the per-tick versus per-event distinction ADR-0074 *inferred* for the prison,
here visible directly in the instruction stream.

### 6.3 A captive is charged nothing at all, and it names a flag bit

The routine refuses outright when the current state carries **bit 1 of the state
record's flag byte**, and **all eleven "being held captive" states carry it**.

That advances **Q185**: bit 1 is *"do not charge need-anger in this state"*, and
its **43** states read as one idea — being held captive (11), stunned (6),
joining your dungeon (4), fleeing (4), idle (3), torturing a captive (3), freeing
comrades, being tortured, engaging in battle, responding to a threat, reporting
danger, celebrating (2 each). **A creature busy with something urgent, or not
properly yours yet, does not stop to be resentful.** Bits 3 and 4 were the Casino
(ADR-0074); this is the third bit named.

### 6.4 What a bed does to all of this

`ModifyAngerOfCreatureInLairPerSecond` is **−550** and a Goblin's `AngerNoPay` is
**60**, so a creature asleep in its own bed sleeps off unpaid resentment **nine
times faster than being owed accrues it**. Wages alone will never drive a
well-housed creature out of a dungeon; failing to pay has to be one of several
things going wrong, which is what the ladder is for.

This is why `a_keeper_meets_its_wage_bill.yaml` shows no mood change on either
side — the lairs it needs to make its Goblins idle enough to collect are the same
lairs that keep them calm. Pinned as arithmetic in
`PaydayTests.ABedOutpacesAnUnpaidWageNineToOne`.

### 6.5 Guards still unmodelled

`creature[0x482]`, `creature[0x3cb]`, three bits of `creature[0x3e6]`,
`def[0x686] & 0x4000`, and state **category 22** (fleeing, battle,
threat-response, celebrating). Each is a narrowing that would only ever charge
*less*, so leaving them out is the wider reading and is safe in that direction.

