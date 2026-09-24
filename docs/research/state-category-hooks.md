# Mechanic: state-category hook bodies

Source: `DKII.EXE` v1.70 (GOG), all 32 functions below disassembled in full
with Ghidra 11.0.1 on 2026-09-23. `state-categories.md` owns the table and hook
addresses; this note owns what the previously unread bodies do. Confidence is
**STRONGLY INFERRED** unless a row says **UNKNOWN**: this is binary evidence,
not a running-game observation (ADR-0046).

## 1. Shared mechanisms

### Room position, admission and occupancy

Nine distinct enter functions, filling ten hook slots because Research and
GuardPost share one, use the same chain. The world virtual call at `+0x1D4`
looks up the map cell under the creature. `0x004559B0` resolves the cell's
low-12-bit thing id. `0x004E7F80` accepts the room when its occupant/type word
already names this creature or when the room type is one of the supported
work-room types. `0x004E5A70` then inserts the creature into the room's linked
occupant lists, writes the room id to creature `+0x3B9`, and increases the
room's used-capacity counters by the creature definition's `+0x672` byte.

The corresponding leave operation is `0x004E5E00(room, creature)`. It unlinks
the creature, zeroes creature `+0x3B9`, and subtracts the same capacity. Seven
category leaves then inspect the thing formerly named by `+0x3B9`; if it is
class 26 and its occupant word `+0x53` names this creature, they pass zero to
`0x004D8E60`. That routine reassigns the thing's occupant, maintaining the old
and new player's type-specific counts, and writes the new id to `+0x53`.

This confirms that the repeated enter/leave idioms **claim and release a
creature's room position**, not merely a boolean room slot. The room keeps
linked membership and capacity, the creature keeps the room id, and some room
furniture keeps a separate occupant id.

### Cadence fields

The hooks do not use one universal countdown:

| Category | Gate | Reload and cadence |
|---|---|---|
| Spectating (31) | state `+0xAB` | variable 19; once per game second |
| Prison (14) | state `+0xAB` | variable 19 x 4; once per four game seconds |
| Torture (15) | state `+0xAC` | variable 19; once per game second |
| Casino (17) | state `+0xB0` | variable 19; once per game second |
| Training (13) | state `+0xB3` | variable 19; once per game second |
| Lair (6), Sleeping (9), Sulking (10) | no countdown | game turn modulo variable 19 |

Combat Pit enter initializes `+0xAB`, but its update is the success stub and
does not decrement it. Temple enter initializes `+0xAF`; its already-read
update is outside this worklist. Within these 32 bodies, only the named update
hook decrements each countdown. No whole-program writer sweep was done, so
whether another subsystem also decrements one is **UNKNOWN**.

## 2. Non-room hooks

Each row states what the hook reads, calls and writes. A return of zero means
the category operation failed; a return of one means it remains valid.

| Address | Category / role | Body |
|---|---|---|
| `0x00455E70` | CallToArms enter | Calls the creature speed-mode setter `0x004887D0(1, 0)` and succeeds. |
| `0x00455E90` | CallToArms leave | Calls the same setter with `(0, 0)`, restoring the ordinary mode. |
| `0x00459130` | TrapDisarm leave | Resolves creature `+0x43D`; if it is a live class-3 thing, clears that thing's `+0x115` word, then always clears `+0x43D`. The words' meanings are **UNKNOWN**. |
| `0x004647D0` | WorkerJob leave | Ends two possible current-action words, normalizes task kinds 6/7 through state `+0xDF`, then dispatches on states 10-49. Depending on the state it removes or completes a player task with ids 1-14 and coordinates from state `+0x8A/+0x94/+0x96`, or clears one of two creature work flags. Exact task-id meanings are owned by `imp-task-priority.md`; several arms remain **UNKNOWN**. |
| `0x004752A0` | ReportingDanger leave | Clears creature `+0x3E6` bit 3, **`RebelActive`** (DK2's own dumper name, `rebellion.md` §4). Why leaving this category should cancel a rebellion is **UNKNOWN** (Q411). |
| `0x0047A780` | Refused leave | Calls `0x0048FD70` with a local `0x1000`, clears creature `+0x3CD`, zeroes the vectors at `+0x104` and `+0x110`, and clears `+0x3E6` bit 20. The combined role is **UNKNOWN**. |
| `0x0047A810` | Combat enter | Calls `0x0048ECE0`, ends a nonzero current action, and clears the combat words at `+0x3D5`, `+0x95`, `+0x97`, `+0x99`, `+0x64`, `+0x66` and `+0x68`. |
| `0x0047A870` | Combat update | Only states 199/200 can award combat experience. After definition-flag and target-class gates, it takes `ExpPerSecond` from definition `+0x6B7`, scales levels 2-10 by stat 15, clamps to `0..65534`, and calls the experience adder. It writes no field directly. |
| `0x0047A980` | Combat leave | Calls the fight-leave routine `0x0045ADC0`, restores speed mode `(0, 0)`, and clears creature `+0x3D5`. |
| `0x0047A9B0` | Unnamed28 update | Unless the owner's `Status` is Won (2), applies a health delta of -10 to the creature every tick. This does not distinguish what the 15 otherwise `idle` states represent, so category 28 remains unnamed. |

## 3. Room-category hooks

“Claims” and “releases” below mean the full shared mechanisms in section 1.

| Address | Category / role | Body |
|---|---|---|
| `0x0047A9E0` | Spectating enter | On room type 16, clears creature `+0x3E6` bit 23; initializes `+0xAB`; claims the room position. |
| `0x0047AAE0` | CombatPit enter | On room type 16 and an exact `0x400` position test, sets creature `+0x3E6` bit 23; initializes `+0xAB`; claims the room position. |
| `0x0047AB90` | Sleeping leave | If creature `+0x3B7` names a class-2 thing, toggles that thing's `+0xF3` bit 0; then toggles creature `+0xF3` bit 0. The precise bed/occupancy fields are **UNKNOWN**. |
| `0x0047AC00` | Sleeping update | State 83 first calls `0x00477B20(1)`. Once per game second by turn modulus, calls the shared lair effect `0x004DFF00`. |
| `0x0047AC40` | Lair update | Once per game second by turn modulus, calls `0x004DFF00`. |
| `0x0047AC70` | Sulking update | Does the same lair call, then returns whether `0x004871A0` finds id 5 in creature `+0x26E`. |
| `0x0047ACB0` | Casino enter | Resolves the room position and its type-15 station. Unless player `+0xF4A` is nonzero, refuses when creature money `+0x3FB` is at most `abs(GamblingModifyCreatureMoneyMoneyPerSecond)` (8). Initializes `+0xB0/+0xB3` and claims. |
| `0x0047AD70` | Casino update | Once per second, advances the fearless countdown at creature `+0x4F4`. Station mode byte `+0x4A` selects Smiles or Money. Smiles applies anger -2000; Money applies +100; both charge buckets 5, 0 and 3 with selector 2. It then adds 1 money in Smiles or subtracts 8 in Money. A roll uses jackpot chances 5000/20000 and calls the keeper jackpot routine on result 42. |
| `0x0047AF20` | Casino leave | Clears the fearless flag (`+0x3EA` bit 13), writes `now + variable19 x 120` to creature `+0x3D7`, and releases. |
| `0x0047AFB0` | Research / GuardPost enter | Claims the room position; no category-specific side effect. |
| `0x0047B000` | Research update | Reads level and definition `ResearchPerSecond` (`+0x6BB`), scales levels 2-10 by stat 17, clamps `0..65534`, and credits the owning keeper through `0x004C2DC0`. See `library-and-research.md` for the canonical rate analysis. |
| `0x0047B0A0` | Manufacture enter | Increments room/station `+0x8A`, clears state `+0xB9`, and claims. The counter's exact meaning is **UNKNOWN**. |
| `0x0047B120` | Manufacture leave | Decrements the same `+0x8A` and releases. |
| `0x0047B190` | Manufacture update | Reads definition `ManufacturePerSecond` (`+0x6BD`), scales levels 2-10 by stat 18, clamps `0..65534`, and credits the keeper through `0x004C3200`; zero returns failure. |
| `0x0047B230` | Training enter | Refuses and raises message 55 when keeper gold is below `-ModifyPlayerGoldWhileTrainingPerSecond` (5). Otherwise clears `+0xB4`, initializes `+0xB3`, and claims. |
| `0x0047B2D0` | Training update | Below the level cap, adds level-scaled `ExpPerSecondTraining` every tick. Once per second it checks the keeper can pay; after four successful checks it withdraws `5 x 4 = 20` gold at the creature's tile and resets `+0xB4`. Insufficient gold raises message 55 and returns failure. |
| `0x0047B430` | Torture enter | Claims, then initializes `+0xAC`. |
| `0x0047B4A0` | Torture leave | Clears two station occupancy bits, conditionally clears state `+0x6E`, clears creature `+0x3EA` bit 14, and releases. The station helpers' exact contracts are **UNKNOWN**. |
| `0x0047B590` | Torture update | Once per second, adds variable 19 to creature `+0x4F6`, applies definition `TortureHpChange` (`+0x79F`) as health, and, when the station's `+0x1D` names the creature, applies definition **`TortureMoodChange` (`+0x7A1`)** to anger bucket 5 with selector 8. Thus the anger amount is per creature, not a global. At the level-scaled `TortureTimeToConvert` threshold it either kills on the variable-234 roll (shipped zero) or calls the conversion helper. |
| `0x0047B740` | Prison enter | Claims, initializes `+0xAB`, and fires creature trigger event 7. |
| `0x0047B7C0` | Prison update | Every four game seconds, and only for a creature with nonzero level-scaled `HungerFill` and a room id, applies `4 x PrisonModifyCreatureHealthPerSecond` (-12) and **`ModifyAngerInCompanyOfHatedCreaturesPerSecond` (+150)** to bucket 0 with selector 9. |
| `0x0047B8C0` | Temple enter | Initializes `+0xAF` and claims. |

### 3.1 The Casino, re-read for building (2026-09-24)

- **The mode is the room's.** `0x004D6880` is `MOV AL,[ECX+0x4A]; RET`, called on the
  room the creature's `+0x3B9` names. Mode 0 ("Smiles") calms by variable 201 (−2000)
  and pays variable 199 (1) into the purse through `0x0048AE70`. Mode 1 ("Money")
  angers by variable 202 (+100) and takes `-(variable 200)` = 8 through `0x0048AF50`,
  which subtracts and floors at 0; the gold goes nowhere. Any other value does
  neither. `0x004D6890(mode, flag)` stores the byte and restyles the room's type-`0x5F`
  objects: object 95, the *Casino Pay Out Adjuster*, the lever. `0x004D69D0` flips 0
  and 1, and its one caller is the command handler `0x00513E50`, for a thing whose
  `+0x138` names the room. The owner confirms it is a click on the lever (Q423). Of the other byte writers at `+0x4A`, only
  `0x004D6250` sits among the Casino's methods. It has no direct caller (a vtable
  slot, not traced) and zeroes `+0x4A` together with `+0x53`, so a new Casino is in
  mode 0 (STRONGLY INFERRED). `0x004D7A30`/`0x004D7A60` are the Combat Pit's.
- **Update order:** first the countdown `+0xB0`. Then the fearless countdown
  `+0x4F4`, which the creature constructor `0x004872A0` seeds from variable 203 (120):
  at 0 it sets `+0x3EA` bit 13 and reloads. Then the purse test, which returns 0 when
  the purse is at most 8. Then the mode's anger and money. Then the jackpot roll
  `0x005558D0(chance)` == 42, with a chance of 5000 in mode 0 and 20000 in mode 1.
  That is 1 in 5000 a second on Smiley and 1 in 20000 on $, so the stingy setting
  pays out *less* often; `manual-1999.md`'s "higher on the stingy setting" misreads
  the range as odds.
- **The jackpot** `0x004D6AA0`, on the keeper, runs only if none of the keeper's
  creatures holds a `+0x3F2` timer. It gives every other creature in the room
  complaints 1 and `0x99`. It increments player `+0xF4A`, the count DK2's dumper
  names `DiscoInfernoCount`, which lifts the purse test in both the Drink job and
  this enter hook. It sets the winner's `+0x3F2` to variable 207 × variable 19
  ticks, and its `+0x3F4` to the larger of `0x004BE290()` (on the keeper) and
  variable 209 (1000). The payout path and variable 208's missed-deadline anger
  (applied to bucket 5 with selector 9 by `0x004D6A40`) are not traced further.
- **The lever's place.** The set-up `0x004D6ED0` walks the room's slab chain from
  `room+0x2D/+0x2E` and takes the first slab `0x00454110` accepts whose north-west,
  north and north-east neighbours it does not. The predicate wants a room slab whose
  byte `+0xC` is `0xFF` (interior), then switches on room type (`type − 3` through the
  byte table `0x0045421C` into `0x004541F4`). Types 9, 10 and 15 (Guard Room, Workshop,
  Casino) take the parity arm `(x + y + room+0x80) & 1 == 0`. So for a Casino it is a
  floor-furniture slot, and **a Casino smaller than 3×3 has no lever**, which the owner
  confirms (2026-09-24). Its screenshot shows the lever on the top interior row. It stores
  that tile at `+0x4B/+0x4F`, creates object `0x5F` there, and writes `+0x53` nonzero
  when the lever exists. **Enter** refuses a Casino whose `+0x53` is 0.
- **The settlement** is the room's own update, around `0x004D6783`. It runs only with a
  lever, and on turns divisible by variable 19 × 4, so every four game seconds. For
  each creature on the room's occupant list (`room+0xA`, then `+0x238`) it calls
  `0x004D7180(-4 × purse change, creature)`. Stingy, that is +32: added to the first
  gold pile (object flag `0x40`) on the lever's tile through `0x0049C760`, which has no
  cap, or else a new pile made there. Generous, it is −4: taken from that pile first,
  destroying it when empty, and the rest from the keeper through
  `0x004BA420(-min(rest, player+0x7E))`. The owner confirms that stingy takings lie
  beneath the lever (2026-09-24).
- **Built:** mode 0's calm and coin, the purse test at the Drink handler and every
  second, and the purse itself (ADR-0187). Also the lever, its pull, mode 1 and the
  settlement (ADR-0188). **Not built:** the jackpot and party, the fearless countdown
  and the leave hook's `+0x3D7`, the enter hook's lever test (every Casino here has a
  lever), and the chain's own order (ours is tile-index order).

### 3.2 The captive's states, 114–126 (read 2026-09-24)

Category 14 holds 114–121 and 123–125; 122 and 126 are in Refused.
- **In.** The imp's 44 puts an admitted captive in **114**. The generic drop state
  **120** (`0x0046F1F0`) sends a creature dropped in a Prison to 115, in a Workshop to
  63 and in a Torture Chamber to 130; a Horny-flagged one goes to 238. **126**
  (`0x00473920`), after its first tick, goes to 114 on a Prison tile.
- **114** (`0x00473130`): on a Prison slab that `0x00454110` accepts (for a Prison, any
  interior slab), sets 115 and stores the prison at creature `+0x3CB`; otherwise
  returns 0, which is idle.
- **115** (`0x004731F0`): hungry (the level-scaled `HungerFill` against `+0x4C`, and a
  gap of variable `0x006F2598` since `+0x50`) goes to 117. Otherwise it picks a slab
  within 4 through `0x004E50F0` and, when accepted, walks there through `0x00471890(tile,
  116, 0x80)`, which is state 6 resuming in 116; then it sets 116.
- **116** (`0x00473350`): every 10 ticks it looks for a bar object (type 2, model
  `0x31`–`0x33`, `+0x138` naming its prison) and pushes against it; otherwise it plays
  idle actions and a 20 % complaint. It always returns 1. How it gets back to 115 is Q422.
- **117** (`0x00473550`): the nearest food object in the prison's slab chain. With none,
  `AngerNoFood` goes to bucket 1 with selector 1 and message `0x40`; otherwise a walk to
  it resuming in 118. It then sets 116.
- **118** (`0x00473750`): on the food it wipes bucket 1 (`0x00486F80(1)`), eats, and goes
  to 119, or to 115 without food. **119** (`0x00473730`) waits for the action, then 115.
- **123–125** toggle the prison's `+0x5D` and walk; **121/122** are "freeing comrades".
  Neither is traced further.
- **Built** (ADR-0191): 114 and 120 into 115, the step walks in 6 into 116, 117 to 118,
  119 and 115, 117 to 116 with no food, and the drain on category 14. **Ours:** 114's move
  to 115 happens on the tick of admission. The roll for a step is our 20 % a tick, from
  116 through 115, which stands in for Q422's unread way back.

## 4. Answers and limits

- Casino's two anger globals are variables 201/202:
  `GamblingModifyCreatureAngerSmilesPerSecond` (-2000) and
  `GamblingModifyCreatureAngerMoneyPerSecond` (+100).
- Prison uses variable 78,
  `ModifyAngerInCompanyOfHatedCreaturesPerSecond` (+150).
- Torture uses the creature definition's `TortureMoodChange`, not a global.
- Category 28's drain is real but is not enough evidence for a durable name.
  Categories 7, 18, 21, 27 and 29 have no hooks, so this work names none of the
  six remaining `UnnamedNN` values.
- The built divergences are an owner decision under Q402. No behavior or hashed
  state changed in this research branch.
- **Built on the dispatcher's category (2026-09-24):** Lair, Sleeping and Sulking
  cooling (ADR-0185); Training, Research, Temple and **Spectating** at work
  (ADR-0186). Spectating's update `0x0047AA70` was re-read for it: countdown `+0xAB`
  reloaded from variable 19, then variable 135 to buckets 5, 0 and 3, selector 9,
  always returning 1. The **Casino** followed (ADR-0187, §3.1), then **Prison**
  (ADR-0191, §3.2). Not built: Manufacture (its adder `0x004C3200` is unread and
  nothing holds manufacture points), Torture, the enter and leave hooks, and the
  room-position claim.
- **Every countdown hook fires every v19 + 1 turns, from entry** (Q424). Casino
  `0x0047AD75`, Spectating `0x0047AA73`, Training `0x0047B368` and Torture `0x0047B596`
  each test the byte, decrement and return while it is nonzero, and at zero reload and
  act. The enter hooks seed it with variable 19 (four). So the effect lands on the fifth
  turn after entry and every fifth turn after that, and Prison's `4 × v19` reload makes it
  every seventeenth. `training.md`'s `--countdown` reading is wrong on this point. Ours
  runs all of them on the global second.
