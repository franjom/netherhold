# Mechanic: training, and the four rates that share its shape

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-07
- The reference install's `Creatures.kwd` and `GlobalVariables.kwd`, via `DK2.Data`
- `docs/research/creature-state-table.md` for the 281 states and their categories

Original version: DK2 v1.70 (GOG)

Everything below is read out of the executable. Facts only, per ADR-0046 — no
decompiler output is reproduced and none is in the repository.

Confidence: **CONFIRMED** throughout except where a clause says otherwise.
Questions: Q205, Q79, Q127 (both readings corrected), Q124 (one clause
confirmed), Q186, Q203.

Trap: the Imp's `ExpPerSecond` of **1200** against a balance-table
`ImpExperienceGainPerSecond` of **20** is 20 × 60, which had been recorded as
evidence that the record's rates are **per minute**. They are not — the 1200 is
a coincidence between two unrelated mechanics (§5.2).

## 2. Where the payment lives — CONFIRMED

Not in any of the six training states. `FUN_00485310`, `FUN_004859e0`,
`FUN_00485580`, `FUN_004855b0`, `FUN_004857f0` and `FUN_00485a60` are states
108–113 and **none of them awards anything**. They walk to a training machine,
face it, and animate. State 109 charges `AngerNoWork` (Q438).

The payment hangs off the state's **category**. Every state record carries a
category byte at `+0x17`; there are **32 category records of `0x40` bytes at
`0x00699688`**; and the per-tick state dispatcher `FUN_00476FC0` calls the
current category's **update hook** immediately before the state's own handler.

| Category slot | Role | Base |
|---|---|---|
| `+0x00` | the category's own bit, `1 << n` | — |
| `+0x04` | **enter hook** | `0x0069968C` |
| `+0x14` | **update hook**, per tick | `0x0069969C` |
| `+0x24` | **leave hook** | `0x006996AC` |
| `+0x34` | an allowed-category bitmask | — |
| `+0x38` | the category's own index, `0`–`31` | — |
| `+0x3c` | a small integer, unread | — |

`+0x08/0x0c`, `+0x18/0x1c` and `+0x28/0x2c` are **zero in all 32 records**, so
each hook is a 16-byte slot of which only the first word is used — the shape of
an MSVC pointer-to-member-function. The same shape appears beside each *state*
record's handler and is unused there too (Q184).

**Category 13 is training.** Its hooks are:

| Hook | Routine | What it does |
|---|---|---|
| enter `+0x04` | `0x0047B230` | tests the keeper's gold, then initialises two counters on the creature |
| update `+0x14` | `0x0047B2D0` | **the payment**, every tick |
| leave `+0x24` | `0x0047B920` | shared with categories 4, 11, 12, 14, 16 and 31 |

States 108–112 carry category 13; state 113 carries category 0. So **five of the
six training states pay identically and the payment was written once**. The same
trick gave the prison its drain (category 14, `FUN_0047B7C0`, Q173).

## 3. What the update hook does, in order — CONFIRMED

1. If the creature's level is at or above `TrainingRoomMaxExperienceLevel`, the
   hook returns failure.
2. The award is `def.expTrainPerTick` at level 1; at any other level it is
   `def.expTrainPerTick * statPercent[16][level] / 100`, clamped to 0 to
   `0xFFFE`.
3. The award is added through the clamped experience adder — **every tick**.
4. If the creature's gold countdown is non-zero, it is decremented and the hook
   returns success.
5. Otherwise the countdown is reloaded from `variable19` — so the charge falls
   every 5th turn, not every 4th (Q424).
6. If the keeper's gold is below `trainingCostPerSecond`, message `0x37` is
   raised and the hook returns failure.
7. `trainingCostPerSecond` is spent at the creature's tile.
8. The creature's charge count goes up by one; when it reaches 4, a floating
   "-20" pops up and the count goes back to 0.

Four things fall out of the order, and each is behaviour:

- **Experience is per tick and gold is per second.** The countdown is reloaded
  from variable 19, which is four.
- **A creature is paid for the tick it trained even if that is the tick the
  money ran out on.** The award precedes the charge.
- **Returning failure from a category update hook makes the dispatcher call
  `SetState(1)`** — idle. So a keeper who cannot pay *loses the creature's job*.
  Training is the only job in DK2 with that property.
- **The "-20" is cosmetic and the accounting is not.** DK2 spends five a second
  and shows the player twenty every fourth second.

## 4. The rate is per second, and the loader is what says so — CONFIRMED

The creature loader `FUN_00492320` stores five consecutive frame slots into five
runtime fields, and **four of the five are divided as they are stored**:

| Frame | Record | Runtime | Stored as |
|---|---|---|---|
| `EBP+0x12F0` | `0x0F00` `ExpForNextLevel` | `def+0x6B5` | **raw** |
| `EBP+0x12F4` | `0x0F04` `ExpPerSecond` | `def+0x6B7` | `/ variable 19`, floored at 1 |
| `EBP+0x12F6` | `0x0F06` `ExpPerSecondTraining` | `def+0x6B9` | `/ variable 19`, floored at 1 |
| `EBP+0x12F8` | `0x0F08` `ResearchPerSecond` | `def+0x6BB` | `/ variable 19`, floored at 1 |
| `EBP+0x12FA` | `0x0F0A` `ManufacturePerSecond` | `def+0x6BD` | `/ variable 19`, floored at 1 |

The frame-to-record delta is the established `0x3F0` (`payday.md` §6), and the
mapping is airtight in both directions: the file record skips `0x0F02` (two
`u8`s, `JobClass` and `FightStyle`) and the loader's run skips that frame slot.
**`FightStyle` is loaded all the same**, separately to `def+0x67E`, where it
picks one of five target selectors (`combat-entry.md` §10).

**The divisor is `[0x006E4154 + 0x40]`, which is variable 19** — the same
globals block and the same slot payday already used for its percentage table.
Four ticks a game second, from the level header, 4 in all 66 shipped levels.

So a field named "per second" is stored as **per tick**, and a hook that adds
one such unit per tick delivers the tabled figure a second. **The name is
right.** What is *not* right is what survives the divide.

### 4.1 The truncation is reachable and is reproduced

`30 / 4 = 7`, so a creature carrying 30 trains at **28 a game second, not 30**.
**Twenty-six of the forty-six shipped creatures lose something to it** — more
than half the table, and the shipped column takes only five distinct values:

| `ExpPerSecondTraining` | Per tick | Per second | How many | Who |
|---|---|---|---|---|
| 40 | 10 | **40** — exact | 9 | Goblin, Skeleton, Salamander, Firefly, Dwarf and four uniques |
| 30 | 7 | **28** — loses 2 | 18 | Warlock, Vampire, Troll, Dark Elf, Rogue, Giant, Wizard, Elven Archer, Thief, Monk, Fairy, Guard, five uniques — and the Imp |
| 25 | 6 | **24** — loses 1 | 1 | Maiden Of The Nest, the only odd value in the column |
| 20 | 5 | **20** — exact | 10 | Bile Demon, Mistress, Knight, Black Knight, Royal Guard, Dark Angel, Jack and three uniques |
| 15 | 3 | **12** — loses 3 | 7 | **Horny**, Prince Tristran, King Reginald, Prince Felix, Prince Balder, Stone Knight, Lord Of The Land |

The 15s lose a **fifth** of their rate, and they are the heroes' lords and the
Horned Reaper.

### 4.2 And so is the floor, in the shipped data

The loader writes **1** whenever the file value was non-zero and the quotient was
zero. That is not rounding hygiene, and the research column proves it: the
**Skeleton's `ResearchPerSecond` is 1** and the **Dark Angel's is 3**. Both
divide to zero and are floored to one, so each researches at **4 a game
second** — four times the Skeleton's shipped number and a third more than the
Dark Angel's. It **rounds up as far as it needs to**.

## 5. What the sibling rates are for — CONFIRMED

Each of the four is read by exactly one category update hook, and each hook
scales it by one column of the 32 × 9 per-level stat table at `DAT_006E4154`:

| Record field | Hook | Category | Stat column | Stat id | Pays |
|---|---|---|---|---|---|
| `ExpPerSecond` | `0x0047A870` | **22** — battle, fleeing, threat-response | `+0x1DD` | **15** | the creature |
| `ExpPerSecondTraining` | `0x0047B2D0` | **13** — training | `+0x1EF` | **16** | the creature |
| `ResearchPerSecond` | `0x0047B000` | **4** | `+0x201` | **17** | the **player** |
| `ManufacturePerSecond` | `0x0047B190` | **5** | `+0x213` | **18** | the **player** |

**`ExpPerSecond` is combat experience.** Category 22 is the category the job
choosers and the job dispatcher both refuse on, and its states are battle,
fleeing, reporting danger and celebrating (Q185). That is the owner's own *"to
get to level 10 they need to fight actual fights"* (Q124), in the code.

### 5.1 The stat ids are pinned from both ends

The columns are 9 `s16` entries each (levels 2–10, level 1 implicit at 100%), so
the stride is `0x12` and the four offsets `0x1DD`, `0x1EF`, `0x201`, `0x213` are
consecutive ids **15, 16, 17, 18**. OpenKeeper names 17 `ResearchPerSecond` and
18 `ManufacturePerSecond`, and the binary's hooks for categories 4 and 5 read
exactly the record fields those names predict, so 15 and 16 fall out by position
with a confirmed anchor on each side. **The shipped table agrees from a third
direction:** the four columns are two matched pairs — 15 and 16 are **100% at
every level**, while 17 and 18 carry the *same* curve as each other (125% at
level 2 to 700% at 10).

### 5.2 One relationship nobody was looking for

**In 41 of the 46 records, `ExpPerSecond` is exactly five times
`ExpPerSecondTraining`** — 200/40, 150/30, 100/20, 75/15. The two fields were
maintained as one decision, which is a small argument that they are the same
kind of quantity in the same unit, arrived at without reading any code.

**The five exceptions say something too:**

| Creature | combat | training | ratio |
|---|---|---|---|
| **Imp** | 1200 | 30 | **40** |
| Unique Mistress | 150 | 20 | 7.5 |
| Unique Bile Demon | 150 | 20 | 7.5 |
| Unique Troll | 200 | 30 | 6.67 |
| Maiden Of The Nest | 100 | 25 | 4 |

Three of the four non-Imp exceptions are **uniques whose ordinary form obeys the
rule** — the plain Mistress is 100/20, the Bile Demon 100/20, the Troll 150/30 —
so their *combat* rate was raised and nobody re-derived the training column. The
Maiden Of The Nest is the only creature in the game carrying 25.

**The Imp is the real outlier at forty times**, and it is the one creature that
never trains and never fights: its 1200 is unreachable, because nothing puts a
worker into a category-22 state. **Unexplained, and recorded as such.**

## 6. Q79: imp experience is per tick of work, not per job — CONFIRMED

The owner (2026-09-07): *"imps are fast little buggers, but I am inclined to say
per job done, job being excavating tiles, mining gold, claiming tiles,
reinforcing walls, and anything else imps might do."* **The activities are
exactly right and the granularity is finer.**

`ImpExperienceGainPerSecond` (global `0x006F295E`, value 20) is read at **nine**
sites, every one of them in the imp's task unit between `0x00496E00` and
`0x00498166`, and every one has the identical shape:

```
target = <the creature's task tile>
if (!<a predicate on that tile>) bail            -- no experience
AddExperience(creature, 20)                       -- the RAW 20, undivided
<apply one tick's worth of work to the tile>
```

The three distinct predicates are all **tile properties**, read out of the
terrain definition table `DAT_006DA4A8`:

| Guard | What it tests |
|---|---|
| `FUN_00453FC0` | tile is unowned, terrain flag `+0x1CB & 1` set, `+0x17B & 1` clear — **claimable** |
| `FUN_00453D10` | terrain `+0x17B & 1` set and `& 2` clear — **a diggable wall** |
| `FUN_00453D80` | tile unowned, terrain solid, `+0x185` (`GoldValue`) `!= 0` — **a gold seam** (imp-task-priority.md, imp-state-exits.md §2) |

**None of them means "the job just finished."** They mean *"is this still a
valid target for the work I am about to do."* The work applied immediately after
is the accrual `tile-work.md` establishes runs **once an original tick**, so the
experience shares that cadence. The handlers are entries in the general 81-record **creature
action table** at `0x006E4198` (creature-action-table.md), stride `0x1D`, filled by a second static initialiser
`FUN_004950C0` — not creature state handlers: no state in the 281-record table
points into `0x00496`–`0x00498`.

### 6.1 So the global's name is wrong, and in the opposite direction

It is **the one rate in the family that is not divided by variable 19**. Its four
siblings on the creature record are converted to per-tick by the loader; this
one is a global, is read raw, and is added raw. **20 a tick is 80 a game
second**, so an imp reaches level 2 in **37.5 seconds** of continuous work
(3000 / 80), not the 150 asserted here for three weeks.

### 6.2 The cadence — CONFIRMED, and the chain is closed

| Link | Routine |
|---|---|
| the task records | `0x006E4198`, stride `0x1D`, filled by `FUN_004950C0` |
| indexed by the creature's current task kind, `creature+0x3A9` | `FUN_00496DC0` returns `base + kind * 0x1D` |
| the dispatcher that calls the handler at `+0x00` | **`FUN_00496AA0`** |
| its **only** caller | **`FUN_00488930`** (`+0x1138`) |
| which also calls the creature **state** dispatcher | **`FUN_00476FC0`** (`+0x1700`) |

And `FUN_00476FC0` runs **every tick**, from three independent facts established
elsewhere here: the prison drain's 16-tick countdown (Q173), the slap timer's
100 decrements over 25 game seconds (Q159, CONFIRMED by the owner off the status
bar), and the ticks-in-state counter it increments once per call.

So `FUN_00488930` is the **per-tick creature update**, the task handler runs once
a tick, and **20 a tick is 80 a game second**.

**One correction falls out.** `FUN_00485880`, the `EPLossSecondWhileNotTraining`
hook of §8, is **per tick** rather than per second: it is called from
`FUN_00488930` at `+0xC54`. The name says "Second", the same misnaming
`ImpExperienceGainPerSecond` carries. If that variable were ever non-zero it
would drain **four times** its named rate.

## 7. The adder, and two readings of ours that were wrong — CONFIRMED

`FUN_00457880(creature, delta)` is the only thing in the game that changes a
creature's experience:

1. If the creature's flags carry `0x8000` (`CombatPitFighter`) and its level is
   at or above `CombatPitMaxExperienceLevel`, it returns — a pit fighter at the
   pit cap (`combat-pit.md` §7, built ADR-0213).
2. `needed` is `def.expForNextLevel` at level 1; at any other level it is
   `def.expForNextLevel * statPercent[13][level] / 100`, clamped to 0 to
   `0xFFFE`.
3. If `needed` is zero it returns — it refuses **everything**, losses too.
4. For a negative `delta`: the loss is applied only if its magnitude is no more
   than the creature's current experience, and either way it returns — a loss
   that does not fit is a **no-op**.
5. Otherwise `delta` is added to the creature's experience.
6. If experience is still below `needed`, it returns.
7. Otherwise the creature levels up and its experience is set to 0 — the
   overshoot is **discarded**.

**Q127 asked which of two readings DK2 uses, and both halves chosen here were
wrong:**

| We had | DK2 |
|---|---|
| the threshold is *spent*, remainder carried | the balance is **zeroed** |
| a promotion **loops**, so a big award buys two levels | **one level per call**, however large the award |

The argument for the second was *"a single-step version would silently drop the
second level the day combat pays out in lumps."* It does drop it. That is the
game.

**And the discard is what makes the cadence load-bearing.** Four awards of seven
and one award of twenty-eight leave different balances behind on any second that
crosses a threshold, which is why training and imp work are both paid on the
original's tick here rather than once a game second.

- **`ExpForNextLevel` is not merely the price of a level, it is the switch.** A
  creature carrying zero can never gain *or lose* experience.
- **A loss floors at zero by refusing the subtraction**, not by clamping. On ten
  points, minus fifty leaves ten.

### 7.1 One clause of Q124 is confirmed rather than chosen

`ExperienceForNextLevel` is indexed by the creature's **current** level, and the
raw record value is used at level 1. That was a reading this project picked from
two possibilities; the adder makes it a fact.

### 7.2 The training path's extra clamp does nothing

Category 13's hook does not call the adder directly — it goes through
`FUN_004858B0`, which recomputes the same threshold and truncates the delta so
that `exp + delta <= needed`. Since the adder then promotes on `>=` and zeroes,
the outcome is identical either way. **Redundant, and reported as redundant**
rather than reproduced as if it mattered. The combat hook (category 22) calls
the adder directly and skips it.

## 8. One more inert balance variable, of a new kind (Q203)

| Variable | Value | Status |
|---|---|---|
| `MotionlessWhileTrainingSeconds` (40) | 5 | **DEAD** — one reference, `MOV [0x006F25ED],EAX`, its own stub. ADR-0082 listed it with the rest of the `MotionlessWhile*Seconds` family (`job-selection.md` §4); a dig into training is where its reader would have been if it had one |
| `EPLossSecondWhileNotTraining` (132) | **0** | **A DIFFERENT KIND OF INERT.** It has a real reader: `FUN_00485880`, called per tick, which calls the experience adder with the *negation* of this variable whenever the creature's current state category is **not** 13. DK2 was built to make experience **decay unless you kept your creatures training**, and shipped with the rate set to zero |

`EPLossSecondWhileNotTraining` is the **first inert variable here found inert by
*value* rather than by having no reader**. A variable with no reader is a feature
that was **cut**; a variable with a reader and a zero is one that was **turned
off**, its mechanism intact and a mod that set the rate would work. The audit
this project runs (scan for readers, count zero, call it dead) would have
**missed** it: twelve variables have been retired that way and none was checked
for a shipped zero on a live mechanism.

`FUN_00478050`, the routine that hook uses, returns the current state's category
byte, and **if that category is 2 it reads the category of the state saved at
`creature+0xDF` instead** — category 2 being the "utility state" that saves the
caller's state (`eating.md` §2.1), so a creature interrupted mid-training is
still counted as training.

## 9. Also read; built only where a row says so

| Fact | Where |
|---|---|
| Training refuses at the cap in the job handler and four of the six state handlers (108, 110, 111, 112). The category update hook does not refuse: at the cap it returns 1 with no experience, no charge and no countdown step (re-read 2026-10-01; this row said six places, the hook among them) | `0x0047B2E6` |
| The category **enter** hook is the one place that does *not* test the cap; it tests only the keeper's gold | `0x0047B230`; built, ADR-0313 |
| A creature at or above `TrainingRoomMaxExperienceLevel` but below `CombatPitMaxExperienceLevel` makes the job handler call **the combat pit's finder** for its messages, discard its result and refuse (`combat-pit.md` §1) | `FUN_00485240` tail, `FUN_004D8870` |
| State 108 kicks a capped creature to state `0x6E` via a `FUN_00496700(0x2C, …)` test | `FUN_00485310` |
| The gold spend goes through `FUN_004BA420(-cost, 0, tileX, tileY)`, i.e. it is charged **at the creature's tile** rather than to an abstract balance | `0x0047B3CA` |
| Message id `0x37` is what a keeper is shown when training cannot be paid for | `FUN_004C4600`; built as message 55, ADR-0313 |

## 10. Implementation

`TrainingRules` and the `Train` branch of `CreatureJobSystem.Working`
(ADR-0083):

- **Experience on the original's tick** (`SimulationTiming.OnOriginalTick`),
  `TrainingRules.ExperiencePerOriginalTick` —
  `SimulationTiming.LoaderRatePerTick(ExpPerSecondTraining)` scaled by stat 16 —
  through `CreatureLevelRules.Grant`. Our rate and DK2's are both four a game
  second (Q1, ADR-0089), so `OriginalTickPeriodTicks` is 1 today; the *four* is
  DK2's and must not be folded into our step rate.
- **The cap**, in `TrainingRules.LevelCap` / `BelowCap` and again in
  `JobRules.Refused`.
- **The bill**: `TrainingRules.GoldPerSecond` (five) and `CanAfford`, tested
  before the job is taken and before every charge, and the job lost when it
  cannot be paid. **The order**: award, then charge.
- `TrainingRules.ExperienceLostPerSecondWhileNotTraining` exposes §8's shipped
  zero rather than pretending the variable does not exist.

**Ours, and said so at the call sites:** a capped creature keeps the job for up
to one five-second gate instead of being ejected by a state handler, because
this project has one working state where DK2 has six; the gold comes out of the
keeper's tiles through `GoldStore.TryWithdraw` rather than the tile underfoot;
and there is no floating "-20", no message `0x37` and no combat-pit fallback.

## Tests

`TrainingTests` and `CreatureLevelTests` (`tests/DK2.Simulation.Tests/`);
`ResearchOnRealDataTests` pins §4.2's floor. Scenario
`scenarios/creatures/two_keepers_find_work_for_a_goblin.yaml`.

## Known exceptions

§4.1's truncation and §4.2's floor are both reproduced; §7.2's redundant clamp
is reported rather than reproduced; the unmapped `0x8000` flag in the adder's
first line is not reproduced.

## Open questions

The category record's `+0x3c` small integer (§2); the Imp's unreachable 1200
(§5.2); the adder's `creature.flags & 0x8000` (§7).
