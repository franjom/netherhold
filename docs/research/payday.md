# Payday

**Status:** read from the binary and **BUILT**, 2026-09-06 (ADR-0078); the anger
charge the same day (ADR-0079). Every address below was decompiled; nothing here
is inferred from a variable's name.

**Implementation:** `PaydayRules`, `Systems.PaydaySystem`,
`CreatureActivity.SeekingWages`, and `CreatureAngerRules` for the anger charge.
**Tests:** `PaydayTests`. **Scenario:**
`scenarios/creatures/a_keeper_meets_its_wage_bill.yaml`.
**Questions:** Q9 (answered), Q201.

A clock on the player, a wage on the creature, and three creature states that
walk it to the money.

---

## 1. What it is, in one paragraph

Every `PayDayFrequencySeconds` a keeper's payday falls due. Every creature it
owns is **credited** a wage — its `Pay`, scaled by its level — and then sent to
**walk to a treasury and take the gold off the floor itself**. A creature that
cannot find one, or arrives to find the tile empty, gives up after two attempts
and stays owed. Wages **accumulate**, so a creature that misses a payday is owed
two.

The failure mode has a consumer: `AngerNoPay`, charged on the outstanding debt
alone (§6).

---

## 2. The clock — `FUN_004be1a0`, called per player

Nothing happens unless the next-payday stamp at `player+0xB53` is non-zero and
`now` has reached it. When it has:

1. Message `0x2E` — "It's payday" — is raised for the player.
2. The player's creature chain is walked (it steps through `+0x10`). Each
   creature that is payable and not exempt is handled one of two ways:
   - if `creature+0x3E2` is zero, its wage is accrued (`FUN_0048b6b0`), its
     failed-attempt counter `creature+0x474` is reset to zero, and it tries to
     collect (`FUN_00472320`);
   - otherwise `creature+0x3E2` is set back to zero — the `GetPaid` flag is
     consumed — and nothing else is done for it.
3. Message `0x2E` is ended.
4. The next payday is scheduled: `player+0xB53 = now + PayDayFrequencySeconds *
   ticksPerSecond`.
5. `player+0xB4F` is set to `now`, recording when this payday began.

**`creature[0x3e2]` is DK2's `GetPaid`**, as its own creature dump prints it (the
dump's `SkipNextPayment` is a different field, `+0x3EA` bit 5, ADR-0217). Q9 guessed
it was *"a mood input rather than an accounting flag"*. It is neither: it is a
**one-shot ticket that suppresses exactly one payday and is consumed whether or
not it was needed**. That is precisely the manual's account of a treat
(§7.6.2.2, `manual-1999.md`): *"wages on the next Payday"* — **a treat is a
prepayment**, and this is where the prepayment is spent.

Note the branch: a creature with the flag set is **not** credited a wage at all.
It is not "paid early and skipped"; the payday simply does not happen for it.

### 2.1 The three variables are all live

Following the standing rule — *a named balance variable is not evidence the
engine consults it* — each was scanned for readers:

| Variable | Global | Hits | Reader |
|---|---|---:|---|
| `PayDayFrequencySeconds` = 600 | `0x006F25D1` | 2 | `FUN_004be1a0`, the clock above |
| `TimeBeforeFirstPayDaySeconds` = 600 | `0x006F2611` | 2 | `FUN_004be330`, the initialiser (below) |
| `PayDayCutOffTimeSeconds` = 120 | `0x006F28A3` | 3 | `FUN_00414bc0` and `FUN_004872a0` |

(One hit in each is the setter stub.)

**The first payday is armed by the keeper's first non-worker creature**, read
2026-09-30. `0x004BE330` runs on the owning player and sets `player+0xB53 = now +
TimeBeforeFirstPayDaySeconds × variable 19`, `player+0xB4F = now`, and
`player+0x76` bit `0x200`. Both callers skip it once that bit is set and for a
creature whose definition flags (`def+0x686`) have bit 0, `IsWorker`: the
creature constructor (`0x0048795E`) and a second site that hands a creature to a
new owner (`0x0048D24E`). So a keeper with only imps has no payday, and the first
one falls 600 s after its first other creature arrives, not after level start.
**Built** with the `GetPaid` rule below (ADR-0295). Unread: whether a level's own
creatures are built on turn 0, where an unarmed (zero) deadline would pass the
unsigned test; ours never sets `GetPaid` from a zero deadline.

All three are real — the negative of the
dead-field pattern this project has caught elsewhere, and read rather than
assumed.

**`PayDayCutOffTimeSeconds` is how close to payday a new creature joins too late
to be paid** (read 2026-09-30). Both readers compare it with the turns left until
payday (`0x004BE180`: `player+0xB53` minus now), in **raw turns**: the setter stub
`0x0051CF9F` stores the level value unconverted, so 120 is 30 game seconds.
`Creature_Construct` (`0x0048750C`) sets `GetPaid` (`+0x3E2`) to 1 when fewer turns
than that remain, so that creature's first payday is skipped, and to 0 otherwise.
The gold counter draw `0x00414BC0` flashes the wage bill inside the same window
(100 turns if the variable is 0).

---

## 3. The wage — `FUN_0048b6b0`

The level is read from `creature+0x403` and the base pay from the unsigned
16-bit field at `creatureDef + 0x69a`. At level 1 the wage is the base pay;
at any other level it is `levelPercent[level] * basePay / 100`. The wage is
added to the amount owed at `creature+0x470`, which is clamped at zero, and the
wage is returned.

**`creatureDef + 0x69a` is `Pay`**, and this was pinned rather than guessed. The
loader (`FUN_00492320`) stores it at `0x00492467` from frame offset
`EBP + 0x130c`; the frame-to-record delta is `0x3F0`, anchored on the already
established `EBP+0x131a ↔ record 0xF2A` (`angerNoFood`). That gives record
offset **`0x0F1C`**, which `kwd-creatures.md` lists as `u16 pay`. Exact hit.

`levelPercent` is a table of `s16` percentages at `DAT_006e4154 + 0x129`,
indexed by level — the same globals block that holds `V19` at `+0x40`.

Two consequences:

- **Wages accumulate** (`+=`, clamped at zero). Being owed persists across
  paydays, so a poor keeper's debt compounds.
- **A level-10 creature costs more than a level-1 one**, by a percentage the
  table gives (Q119).

**Implemented:** `PaydayRules.Wage` scales the record's `Pay` by the level
table's `CreatureStatId.Pay` row; `PaydayRules.MaxFailedAttempts` = 2;
`PaydayRules.IsPayingRoomOf` accepts a Treasury or Dungeon Heart of the
creature's own keeper; `PaydayRules.GoldIn` skips a room whose total is zero and
`PaydayRules.TakeFromRoom` lifts it from the room underfoot, starting at the creature's tile (Q435, ADR-0220).

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

Re-checks that the tile under the creature is a room of type 1 or 5 owned by its
keeper (`FUN_004539b0`, as §4.4) and bails if not — so a treasury sold out from
under a creature during the walk aborts the trip. On arrival: `SetState(0xbd)` = **189**, but only when the region test
`0x004B2AE0` on the tile centre returns a zero word; otherwise 188 returns 1 and
waits. That test is the turn toward a point, so 188 waits while it turns to face the tile (read 2026-09-25, `tunnelling.md` §3). Ours does neither check.

### 4.4 State 189 `FUN_00472740` — take it

Only when something is owed (`creature+0x470` non-zero):

1. `takeGoldFromTile` is asked for the amount owed, at the creature's tile X and
   Y; the result is `taken`.
2. If `taken` is zero — the creature came up empty — the failed-attempt counter
   `creature+0x474` goes up by one and the state returns 0.
3. Otherwise effect `0xC3`, a floating number and a sound are played.
4. `FUN_0048ae70` is called with `taken / 2`.
5. `0x0048E810` is called with `-taken`: the take comes off the owed wage.
6. If the amount owed at `creature+0x470` is now zero — fully paid —
   `FUN_00486f80` is called with 4.

**The gold comes off the room the creature is standing in, starting at its tile.** Corrected
2026-09-25 (Q435, ADR-0220). This section used to say "off the tile", and that was wrong.
`takeGoldFromTile` is `0x004F4510`, called on the tile's room (`MOV ECX,EAX` at `0x004727B2`).
It takes one pile a tile and goes round the room's tile ring until the wage is met
(`steal-gold.md` §4). Before it, 189 needs the tile to be a Treasury or Heart of the creature's
own keeper (`0x004539B0`, types 1 then 5). Otherwise it returns 0 and counts no attempt. The
confirmation of ADR-0015 stands: the withdrawal is from tiles, never from a player counter.

**`FUN_0048ae70(taken / 2)` puts half the wage in the creature's purse — CONFIRMED
(2026-09-24).** Its receiver is the creature (`MOV ECX,[ESI+0x8]` at the call). It adds
the argument to `creature+0x3FB`, the `GoldHeld` field DK2's own dumper names, then caps
it at the definition's `MaxGoldHeld` (`+0x69C`), scaled at levels 2–10 by the stat row
at `0x006E4154 + 0x13B` (to 120 %); what does not fit is lost. The other half is simply
gone. The Casino pays into the same purse and the Drink job wants more than 8 in it
(`state-category-hooks.md` §3). `InitialGoldHeld` (`+0x6B2`) is only copied by the
loader, never read, so a purse starts empty. **Built** (ADR-0187).

**How 189 ends — read 2026-09-24.** Every path returns 0, which is idle, with one
exception. After a successful take it walks the creature's job list (count at
`creature+0x370 → +0x78B`, entries by `0x004944D0(i)`); if any entry's job byte is
**9, Drink**, it calls the Drink job's starter `0x004560F0` and returns what that
returns, a room walk in 7 resuming in 145 (`travel-state-call-sites.md`). So **a
creature whose happy table holds Drink goes to the Casino straight from being paid**. The
list is the happy table: def `+0x628`, six-byte entries with the job at `+5`, its count at
`+0x78B`, filled by the loader from the three happy entries and rolled by the happy
chooser `0x004780E0`. Shipped, only heroes have Drink there.
No keeper's rooms or gold are tested here; the starter's own gates are in
`job-selection.md`. **Built** (Q421, ADR-0189). The empty take, the missing room and nothing
owed all return 0 as well.

**`FUN_00486f80(4)` wipes the pay-anger bucket — CONFIRMED (2026-09-24).** Its
receiver is the same `int[8]` the anger wrapper `0x00478B80` hands the adder
(`LEA ECX,[ESI+0x20]` in both). It adds the counter's own negation, so the
counter becomes 0, clamps as the adder does and recomputes the maximum. The
world byte `+0x72A1` bit 0 gates it, the same switch that gates every anger add
(`creature-anger.md`). So **a creature paid in full forgets its pay grievance at
once**, rather than cooling. State 80 makes the same call with bucket 2 as it
places a bed (`lair-acquisition.md` §9.1). **Built** (ADR-0179).

---

## 5. What this gives the implementation

| Piece | Status |
|---|---|
| Payday interval, first payday | **read**, and the variables are live |
| Wage = `Pay` scaled by level, accumulating | **read**, field pinned to record `0x0F1C` |
| `GetPaid` (`+0x3E2`) as a consumed one-shot | **read** |
| Walk to the nearest treasury/heart **that has gold** | **read** |
| Take gold from the room underfoot, starting at the creature's tile | **read** (corrected, Q435), and it confirms ADR-0015 |
| Two failed attempts per payday, then stop | **read** |
| Two distinct "cannot pay" messages | **read** |
| `AngerNoPay` firing on an unpaid creature | **READ AND BUILT** the same day (ADR-0079) — see §6 |
| `PayDayCutOffTimeSeconds`: a creature built within it of payday gets `GetPaid` (§2.1) | **READ AND BUILT** (ADR-0295) |
| First payday armed by the keeper's first non-worker (§2.1) | **READ AND BUILT** (ADR-0295) |
| `FUN_0048ae70(taken/2)`: half the wage into the purse, capped | **READ AND BUILT** (ADR-0187) |
| `FUN_00486f80(4)`: zero the pay-anger bucket when fully paid | **READ AND BUILT** (ADR-0179) |

**Built on 2026-09-06 (ADR-0078):** `PaydayRules`, `PaydaySystem`,
`CreatureActivity.SeekingWages`, three fields on the creature and one on the
player. It cost one bump of `GameSimulation.HashVersion` and the snapshot
`FormatVersion` with it; ADR-0078 records which.

Nothing in the payday path is left unread: `0x0048E810(-taken)` in state 189 subtracts the take from the owed wage `+0x470`, floored at zero (`Payday_AdjustOwed`).
The owner's account of the consequence stands as the observation (Q9: unhappy,
then angry, then leaves — or rebels, if converted).

---

## 6. Where the anger comes from — `FUN_00478d00` (2026-09-06, ADR-0079)

Reached from the creature update **immediately before the needs routine**, and
it charges all three standing sources one after another, **independently**:

| Source | Field | Condition |
|---|---|---|
| `AngerNoLair` | `def+0x6c1` | no bed **and** the room type is available |
| `AngerNoFood` | `def+0x6c3` | `HungerNeed` >= level-scaled `HungerFill` **and** a hatchery is available |
| **`AngerNoPay`** | **`def+0x6c5`** | **`WageOwed != 0`, and level-scaled `Pay` != 0** |

### 6.1 The pay clause has no treasury test

Both siblings test that the **room** exists. The pay charge tests **nothing but
the debt**: no treasury clause, no "did it try and fail" clause. A keeper with no
vault at all and a keeper whose creature has not walked over yet are charged
identically, from the moment the clock runs until the money is in hand — **you
cannot excuse yourself by having no treasury.** Assuming the sibling rule is the
trap here.

**Implemented:** `PaydayRules.IsOwedWages` is that test — outstanding wage and
level-scaled `Pay` both non-zero, nothing else — and `CreatureAngerRules` charges
`AngerNoPay` off it (ADR-0079).

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

### 6.4 A bed does nothing about an unpaid wage

`AngerNoPay` fills anger bucket 4 and `ModifyAngerOfCreatureInLairPerSecond`
(−550) drains bucket 5, and DK2 bands the **largest** bucket rather than a pooled
total (ADR-0081, `creature-anger.md`, "Anger is six counters, not one"). So the
two never meet, and sleeping off an unpaid wage is not a mechanism.

**Trap:** the two are shipped numbers with a plausible ratio between them — 60
against 550 reads as "a bed outpaces the debt nine to one" — and the arithmetic
is meaningless because the engine never puts them in one place. Check that it
does before comparing two shipped values.

### 6.5 Guards still unmodelled

`creature[0x482]`, `creature[0x3cb]`, three bits of `creature[0x3e6]`,
`def[0x686] & 0x4000`, and state **category 22** (fleeing, battle,
threat-response, celebrating). Each is a narrowing that would only ever charge
*less*, so leaving them out is the wider reading and is safe in that direction.

