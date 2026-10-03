# Mechanic: job selection for creatures that are not workers

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra
- The reference install's `Creatures.kwd` and `GlobalVariables.kwd`, via `DK2.Data`
- `docs/research/creature-state-table.md` for the 281 states and their categories

Original version: DK2 v1.70 (GOG)

Facts only, per ADR-0046 — no decompiler output is reproduced and none is in the
repository.

Confidence: **CONFIRMED**. Questions: Q2 (closed), Q203, Q204, Q210.

---

## 1. Three choosers and one dispatcher

| Routine | Mood | Gate field |
|---|---|---|
| `FUN_004780e0` | happy | `creature+0xfd` |
| `FUN_004782c0` | unhappy | `creature+0xf9` |
| `FUN_004784c0` | angry | `creature+0x101` |

**Three timestamps side by side, not one.** So a creature that has just chosen
while content is free to choose again the moment it turns unhappy, and the gate
never silences a mood change.

Each chooser does the same five things:

1. Refuse outright if the current state's **category is 22** (fleeing, battle,
   threat-response, celebrating — the same category the job dispatcher refuses
   on).
2. Refuse if the creature-def flag `def+0x686 & 0x4000` is set; instead set
   state `0xEE` unless already in state `0xF2`. That flag is **`IsHorny`**, bit
   14 of the record's first flag dword, and **exactly one of the 46 shipped
   creatures carries it: Horny** (§6.2). So the Horned Reaper is refused a
   job outright.
3. **The five-second gate**: `now - last >= 5 × variable 19`, or `last == 0`.
   Hardcoded, in all three.
4. Build **cumulative ranges** from the mood's table — running total, record
   order, inclusive at both ends — then `rand(100)`. Identical to the arithmetic
   `creature-anger.md` documents for the angry chooser, including the sort that
   cannot reorder anything.
5. Try the job whose range holds the roll; **on failure try every job in the
   table in list order**. Stamp the gate only on success.

**Two differences between them, and both matter:**

- **The unhappy and angry choosers try for a lair first** (`creature+0x3b7 == 0`
  then `FUN_0046f390`); the happy one does not, because the idle state already
  did (Q150, ADR-0060).
- **Only the happy chooser charges `AngerNoWork`.** Its tail, when every job has
  failed, is `FUN_00478b80(3, def+0x6c7, 3)`. The unhappy chooser's identical
  tail is a bare `return 0`, and so is the angry one's. **A creature that is
  already unhappy is never made crosser by idleness.**

That asymmetry is the same shape as `AngerNoPay` having no treasury clause where
both its siblings test for the room (ADR-0079). Twice: a plausible symmetry the
game does not have.

---

## 2. The dispatcher, `FUN_00478800`

A 28-way switch from job id to that job's handler. It returns the handler's
result, and **on success writes the job id to the state object's `+0x64`** (creature `+0x2B2`) — DK2's own
"current job" field, written here and nowhere else.

**Every handler has the same three parts:**

1. A cheap test on the keeper or the creature. Some jobs have none.
2. A search for the nearest suitable room.
3. `FUN_00471a70(room, stateId, priority)` — entering the job's state, which can
   **still** fail, in which case the job has failed and the chooser moves on.

**That boolean is the whole of job availability.** The choosers never inspect a
room; they try jobs until one says yes. Which closes the clause Q2 had left
open — *what makes Research or Train fail so the list falls through*.

Two guards on the dispatcher itself:

- State category 22 refuses everything, as in the choosers.
- **`player+0x76 & 0x4` restricts the creature to jobs 10 (Leave) and 16
  (Rebel) and nothing else.** That bit is **`CallToArmsOn`** (§6.1). While a
  keeper has a Call To Arms up, none of its creatures will take a voluntary job:
  the guard at `0x00478849` jumps past the whole `0..0x1C` jump table and admits
  only those two ids, so every other job returns failure without being tried.

Job **20 (Tunnelling)** has a case that does nothing and returns zero, and job
21 has no case at all: both always fail.

---

## 3. The handlers, and what each one tests

| Job | Handler | Room | State | Its own test |
|---|---|---|---|---|
| 1 Sleep | `FUN_0046f440` | Lair | `0x51` | sleeps at all, **and has a bed**; bedless it kicks a lair attempt and **returns failure** |
| 2 Eat | `FUN_004633d0` | Hatchery | — | — |
| 3 Research | `FUN_004759d0` | Library | `0x35` | `player+0x213 != 0` |
| 4 Train | `FUN_00485240` | Training Room | `0x6c` | keeper's gold ≥ training cost, **and level < `TrainingRoomMaxExperienceLevel`**; above the cap it falls to a second call that is the combat pit's |
| 5 Manufacture | `FUN_00470080` | Workshop | `0x3f` | `player+0xa1c != 0` — **something queued** |
| 6 Guard | `FUN_00462850` | **room type 9** | `0x61` | reuses a remembered guard post if it is still valid |
| 8 Praying | `FUN_004728e0` | Temple | `0x65` | **none at all** — three lines, straight to the temple search |
| 9 Drink | `FUN_004560f0` | Casino | `0x91` | a jackpot party is on (player `+0xF4A`) **or** the purse `+0x3FB` holds more than the stake, 8; **then** a casino (corrected 2026-09-24, ADR-0187) |
| 15 Sulk | `FUN_00482ef0` | **Lair** | `0x55` | sleeps at all, **has a bed**, and that bed is in a room |

### 3.1 Sulking is going to your lair and moping in it

`FUN_00482ef0` is the Sleep handler with one digit changed — same three
preconditions, same room, state `0x55` where sleep uses `0x51`. **A creature
with no bed cannot sulk at all**, which matters because every unhappy table in
the shipped game is Sulk and Drink.

### 3.2 The shipped tables are small and sharp

| Creature | Happy | Unhappy |
|---|---|---|
| Goblin | Train 100% | Drink 100% |
| Warlock | Research 80%, Train 10%, Pray 10% | Sulk 10%, Drink 90% |
| Troll | Manufacture 100% | Sulk 10%, Drink 90% |
| Bile Demon | Train 40%, Manufacture 60% | Sulk 10%, Drink 90% |
| Dark Elf | Guard 80%, Train 20% | Sulk 10%, Drink 90% |
| Vampire | Research 80%, Pray 20% | Sulk 10%, Drink 90% |

**No shipped table contains Sleep or Eat**, so the question of whether the job
chooser or the needs routine owns those two never arises in practice.

The whole happy side needs five rooms — training room, library, workshop, temple
and guard room — and **a Goblin in a dungeon with no training room has an empty
table and takes `AngerNoWork`**. That is the mechanic. **Corrected 2026-09-25 (ADR-0228):**
the charge is the loaded `def+0x6C7` (the file's 50 over four, 12) once per failed pass, and a
pass comes only when state 3 ends (`idle-fallback.md`), so not 50 a second.

---

## 4. Seven dead balance variables, all in this family (Q203)

A raw `.text` scan for each global finds **only the loader stub writing it**:

| Variable | Value |
|---|---|
| `MotionlessWhileResearchingSeconds` | 70 |
| `MotionlessWhileManufacturingSeconds` | 5 |
| `MotionlessWhilePrayingSeconds` | 30 |
| `MotionlessWhileTrainingSeconds` | 5 |
| `StateCounterWhileGuarding` | 10 |
| `CasinoModifyCreatureGoldPerSecond` | −50 |
| `MinimumJobDurationSeconds` | 15 |

That takes the count of shipped-but-unread balance variables from five to
twelve, and this is the first time a whole *family* has gone at once.

**`MinimumJobDurationSeconds` is a cross-check rather than a curiosity.** All
three choosers gate on a hardcoded `5 × variable 19`, and the shipped variable
that looks like it should govern how long a creature stays on a job is read by
nothing. Two independent facts agreeing that the gate is five seconds, one of
them by being conspicuously unused — together, real evidence that **there is no
job timer at all**.

The two praying variables in the same block are emphatically *not* dead:
`ModifyCreatureAngerWhilePrayingPerSecond` has **32** read sites and
`ModifyCreatureHealthWhilePrayingPerSecond` two.

---

## 5. The Temple, `FUN_0047b7c0`

The heading's address is the prison's update hook; the Temple's (category 12) is
`0x0047B970` (state-categories.md §7.1). Once a game second, gated on a per-creature countdown reloaded from variable 19:

- **`ModifyCreatureAngerWhilePrayingPerSecond` (−275) applied six times**, once
  per anger bucket, in six consecutive calls.
- **`ModifyCreatureHealthWhilePrayingPerSecond` (+33)**, clamped to the missing
  health, against the level-scaled maximum.

Since ADR-0081 that first line is the Temple's whole reason to exist: **it is
the only source in DK2 that drains an anger bucket it did not fill.** Every
other calm in the game — a bed, the Hand of Evil, winning in the combat pit —
reaches bucket 5 and bucket 5 only. A month of unpaid wages sits in bucket 4 and
a Temple is the only way to take it back.

---

## 6. Implementation

`JobRules` + `CreatureJobSystem` (ADR-0082, extended by ADR-0083),
`CreatureActivity.GoingToJob` and `AtWork`, `CreatureState.CurrentJob`. Built:
the happy and unhappy choosers, the gate and its three timestamps
(`JobRules.ReselectionGateTicks`), the cumulative ranges and fall-through
(`JobRules.TableFor`), the room search, the walk, the per-job tests of §3,
`AngerNoWork` and its asymmetry (`JobRules.ChargesNoWorkAnger`, content only),
`JobRules.RoomFor` from §6, the Temple's per-second effect (`PrayingCalm`,
`PrayingHeal`), and training's award and bill (`training.md`).

**Ours, and stated as such at the call sites:**

- **The walk to the room.** Copied from the hatchery walk tile for tile —
  nearest room by squared distance to its centre, first reachable one wins,
  arrive at the centre or at the lowest tile index when the centre is not in the
  room. The original's job states each travel for themselves and none was read.
- **One state where DK2 has one per job**, composed the way `SeekingWages`
  composes payday's three.
- **How long a creature keeps a job** — until the room stops being its keeper's,
  or a need takes it away. §4 is the evidence that this is not a timer.
- Train's gold clause is modelled and the original's **order** is kept: gold is
  tested *before* the level cap, so a broke keeper's level-9 creature is refused
  for poverty rather than for being too good, and a keeper who runs out mid-job
  **loses the creature's job** — which no other job in DK2 does.

**Two jobs have an effect** (`JobRules.EffectIsBuilt`): Praying and Train.
Research and Manufacture are walked to and stood in; their category hooks pay
the *player*, and no keeper here has anything to research or make.

## 7. The room → job map — CONFIRMED (Q210)

`JobRules.RoomFor` was strongly inferred: only Guard was certain, because its
handler passes the literal `9` to its room search. **DK2's per-room-type handler
table settles eight of them at once.** Each of its 26 records ends with a **job
id** (`portals.md`, slot `+0x25`), and `FUN_004E8DA0` loads it, asks the
*creature's definition* whether that creature may do the job, and writes it into
`creature+0x2bc`.

| room | id | `JobType` |
|---|---|---|
| Hatchery | 2 | `Eat` |
| Library | 3 | `Research` |
| Training Room | 4 | `Train` |
| Workshop | 5 | `Manufacture` |
| Guard Room | 6 | `Guard` |
| Torture Chamber | 7 | `Torture` |
| Temple | 8 | `Praying` |
| Casino | 9 | `Drink` |
| **Prison** | **22** | **`Wait`** |

Every row matches the `JobType` numbering this project already had, **including
both non-sequential values** — `Drink` is 9 rather than 10, and `Wait` is 22.
Eight independent agreements, from a table opened while chasing a portal reset.

**Prison → `Wait` is inert**: no creature's job tables list `Wait` and nothing in
this project asks for it. It is in the map because the table states it, and a
missing row is harder to notice than an unused one.

**Two rows the table cannot speak to.** The **Lair** and **Combat Pit** records
carry *no* job id, so `Sleep`, `Sulk` and `CombatPitSpectate` are reached by some
other route and are neither confirmed nor contradicted.
**`CombatPitSpectate` answered (2026-09-25, ADR-0227):** its only caller is state 89's
fallback `0x00478680`, after the mood chooser fails (`combat-pit-spectators.md` §1). Sulk needing a Lair (§3.1)
rests on its handler. A room with no job slot is not a room with no purpose.

---

## Tests

`CreatureJobTests` (`tests/DK2.Simulation.Tests/`). Scenarios
`scenarios/creatures/two_keepers_find_work_for_a_goblin.yaml` and
`a_goblin_with_no_lair_walks_out.yaml`.

---

## 6. Q204's two flags, both answered 2026-09-11

### 6.1 `player+0x76` bit 2 is `CallToArmsOn` — CONFIRMED

Not by inference: **DK2 prints the name itself.** The player debug dumper
(`0x004BCF2F`, `player-object.md`) reaches `+0x76` fifteen times, once per bit,
and each push is a shift and a mask before its own format string. At
`0x004BD31B` the sequence is `MOV` the dword at `+0x76`, `SHR` by **2**, `AND 1`,
push, and the string is `CallToArmsOn %d`. The shift settles the bit; the order
of the names in `player-object.md`'s bitfield row is corroboration, not evidence.

The guard then reads plainly. `MOV ECX,[ECX+0x2A]` takes the creature's **owner**
— the same owner pointer Q262 established — so the flag is the keeper's,
not the creature's. Set, only job **10** and job **16** are reachable. Job 10's
handler is three lines and sets state **194**, whose shipped label is
`leaving Dungeon`; job 16's is the rebel path. Both names were ours already and
Flame's map agrees, cited under ADR-0080 and adopted nowhere.

**The rule, in one line:** a keeper's Call To Arms suspends voluntary job-taking
for that keeper's whole workforce, leaving only quitting and revolting.

**The standing hypothesis is refuted.** `HANDOFF.md` carried this flag as the
candidate for *a keeper whose dungeon heart has fallen*, and it is nothing of the
kind. Defeat needs a different candidate (Q93, Q207).

**Built** (ADR-0279): `CreatureJobSystem` admits only Leave and Rebel while the owner's
Call To Arms is on, as this gate does.

### 6.2 `def+0x686 & 0x4000` is `IsHorny` — CONFIRMED

The loader copies file offset `0xEFC` straight into runtime `+0x686`
(`record-field-sweep.md`), so bit order is preserved and `0x4000` is **bit 14**.
`CreatureEnums.cs` has named bit 14 `IsHorny` since it was written, from the
`creatures_struct.bt` bit order — and nothing in this repo had ever put the
two facts side by side. Checked the ADR-0086 way rather than trusted:
`dk2tools kwd creaturehex 0xEFC 4` sets byte `0xEFD` bit `0x40` on **exactly one
of the 46 shipped creatures, Horny**. One bit, one creature, an independent name.

State `0xEE` (238) is where it lands, and its handler is a small idle cycle: a
one-in-three roll either moves it to 239 or tries once more and settles on 242.
All three carry the generic `idle` string in category 28, so the shipped labels
name none of them. The roll draws through the world's **game seed** at `+0x15A0`,
the field `world-object.md` names `Seed` — worth knowing for determinism: a
creature's idle transitions are seeded, not free-running.

## Open questions

- **Q203**: the seven dead variables are STRONGLY INFERRED dead, from a `.text`
  scan per global.
