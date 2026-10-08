# Mechanic: state-category hook bodies

Source: `DKII.EXE` v1.70 (GOG), all 32 functions below disassembled in full
with Ghidra 11.0.1 on 2026-09-23. `state-categories.md` owns the table and hook
addresses; this note owns what the previously unread bodies do. Confidence is
**STRONGLY INFERRED** unless a row says **UNKNOWN**: this is binary evidence,
not a running-game observation (ADR-0046).

## 1. Shared mechanisms

### Room position, admission and occupancy

Nine enter functions fill ten slots (Research and GuardPost share `0x0047AFB0`) and
use one chain, **CONFIRMED** (re-read 2026-10-01). The world call `+0x1D4` finds the
map cell under the creature, `0x004559B0` resolves its low-12-bit thing id, `0x004E7F80`
admits the room, and `0x004E5A70` claims a place in it (§3.5). The leave hooks release
the place with `0x004E5E00`. The room keeps a linked occupant list and `UsedCapacity`;
the creature keeps the room id at `+0x3B9`. The shared leave `0x0047B920` then, when
that room is a Crypt (type 26, room `+0x18`) whose `+0x53` names this creature, passes
zero to `0x004D8E60`, which reassigns the Crypt's occupant.

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
| `0x00459130` | TrapDisarm leave | Resolves creature `+0x43D`; if it is a live class-3 thing, clears that thing's `+0x115` word, then always clears `+0x43D`: the disarmed trap (`+0x43D`, `sgmap`'s `f43D_trapTagId`) and its disarmer. Reachability and save-restoration limits: `traps.md` §11, Q455. |
| `0x004647D0` | WorkerJob leave | Ends two possible current-action words; while in travel states 6/7, dispatches on resume `+0xDF`. Removes/completes player tasks using state `+0x8A/+0x94/+0x96`, or restores worker bits in the branches detailed in `creature-pickup.md`. That pickup subset is built (ADR-0263); full task cleanup remains incomplete. |
| `0x004752A0` | ReportingDanger leave | Clears creature `+0x3E6` bit 3, **`RebelActive`** (DK2's own dumper name, `rebellion.md` §4). Which creatures can carry the bit into the category, and the intent, are `rebellion.md` §4 (Q411). |
| `0x0047A780` | Refused leave | Calls `0x0048FD70` with a local `0x1000`, clears creature `+0x3CD`, zeroes the vectors at `+0x104` and `+0x110`, and clears `+0x3E6` bit 20. The combined role is **UNKNOWN**. |
| `0x0047A810` | Combat enter | Stops the creature (`0x0048ECE0`), clears `ReportingTo` `+0x3D5`, ends a running action, and zeroes the creature's (not the state object's) `+0x95`, `+0x97`, `+0x99`, `+0x64`, `+0x68`, `+0x66`; never refuses. Order, fields and what ours models: `combat-category-hooks.md` §1, §3 (re-read 2026-10-02, CONFIRMED). |
| `0x0047A870` | Combat update | Only states 199/200 can award combat experience. After definition-flag and target-class gates, it takes `ExpPerSecond` from definition `+0x6B7`, scales levels 2-10 by stat 15, clamps to `0..65534`, and calls the experience adder. It writes no field directly. The gates: definition `+0x686` bit 0 clear or bit 8 set, a live fight target of class 0. Built, ADR-0366. |
| `0x0047A980` | Combat leave | The fight-leave routine `0x0045ADC0` (the creature's forced target and engagement, then every attacker's, the victory nibble only at health 0 or below), the walk speed `0x004887D0(0, 0)`, then `+0x3D5` = 0. `combat-category-hooks.md` §2 (re-read 2026-10-02, CONFIRMED). |
| `0x0047A9B0` | Unnamed28 update | Unless the owner's `Status` is Won (2), applies a health delta of -10 to the creature every tick. This does not distinguish what the 15 otherwise `idle` states represent, so category 28 remains unnamed. |

## 3. Room-category hooks

“Claims” and “releases” below mean the full shared mechanisms in section 1.

| Address | Category / role | Body |
|---|---|---|
| `0x0047A9E0` | Spectating enter | On room type 16, clears creature `+0x3E6` bit 15, `CombatPitFighter` (`AND CH,0x7F`; corrected 2026-09-25, this row said bit 23); initializes `+0xAB`; claims the room position. |
| `0x0047AAE0` | CombatPit enter | On room type 16 and an exact `0x400` floor height, sets creature `+0x3E6` bit 15, `CombatPitFighter` (`OR 0x8000`; corrected 2026-09-25, this row said bit 23; `combat-pit.md`); initializes `+0xAB`; claims the room position. |
| `0x0047AB90` | Sleeping leave | Sets the draw bit (`+0xF3` bit 0, RenderInfo `+0xAF`) on the lair `+0x3B7` names and on the creature: believed presentation only, not built (`leave-hook-bodies.md` §2: the body CONFIRMED, the draw-bit reading inferred). |
| `0x0047AC00` | Sleeping update | State 83 first calls `0x00477B20(1)`. Once per game second by turn modulus, calls the shared lair effect `0x004DFF00`. |
| `0x0047AC40` | Lair update | Once per game second by turn modulus, calls `0x004DFF00`. |
| `0x0047AC70` | Sulking update | Does the same lair call, then returns whether `0x004871A0` finds id 5 in creature `+0x26E`. |
| `0x0047ACB0` | Casino enter | **CONFIRMED** (re-read 2026-10-01). `0x004539B0(15, owner, 0)` on the cell under the creature: only on its keeper's own Casino is room `+0x53` (the lever) tested, and 0 refuses. Otherwise the lever is not asked for, and the claim judges the cell. Then, unless player `+0xF4A` is nonzero, the purse: creature `+0x3FB` at most `abs(GamblingModifyCreatureMoneyMoneyPerSecond)` (8) refuses (signed `JLE`). Then `+0xB0` = variable 19 and `+0xB3` = 0, and the lookup and claim of §3.5, so a claim refusal leaves the seed written. Every refusal returns 0; none raises a message. Built: ADR-0313; the party, ADR-0396. |
| `0x0047AD70` | Casino update | Once per second, advances the fearless countdown at creature `+0x4F4`. Station mode byte `+0x4A` selects Smiles or Money. Smiles applies anger -2000; Money applies +100; both charge buckets 5, 0 and 3 with selector 2. It then adds 1 money in Smiles or subtracts 8 in Money. A roll uses jackpot chances 5000/20000 and calls the keeper jackpot routine on result 42. |
| `0x0047AF20` | Casino leave | Only when `CasinoGamblingFearless` (`+0x3EA` bit 13) is set: clears it and writes `FearlessUntil` (`+0x3D7`) = now + variable 19 × variable 203. Then releases. Built: ADR-0322 (`leave-hook-bodies.md` §1). |
| `0x0047AFB0` | Research / GuardPost enter | Claims the room position; no category-specific side effect. |
| `0x0047B000` | Research update | Reads level and definition `ResearchPerSecond` (`+0x6BB`), scales levels 2-10 by stat 17, clamps `0..65534`, and credits the owning keeper through `0x004C2DC0`. See `library-and-research.md` for the canonical rate analysis. |
| `0x0047B0A0` | Manufacture enter | Increments `NumberOfManufacturingCreatures`, **player** `+0x8A` of the tile's keeper; clears state `+0xB9` (unread); claims. Built: ADR-0322 (`leave-hook-bodies.md` §4). |
| `0x0047B120` | Manufacture leave | Decrements the same player `+0x8A`, with no floor, and releases. Built: ADR-0322. |
| `0x0047B190` | Manufacture update | Reads definition `ManufacturePerSecond` (`+0x6BD`), scales levels 2-10 by stat 18, clamps `0..65534`, and credits the keeper through `0x004C3200`; zero returns failure. |
| `0x0047B230` | Training enter | **CONFIRMED** (re-read 2026-10-01). First the gold: `-ModifyPlayerColdWhileTrainingPerSecond` (5) against player `+0x7E` (`Money`), `CMP`/`JBE`, so 5 ≤ Money passes. Failing, it raises message 55 to the creature's keeper on every refusal (`0x004C4600(0x37, owner, 5, 0, 1)`: Data1 5, subtable `0x006A0690`, 240 s) and returns 0, writing nothing. Passing, it clears `+0xB4`, sets `+0xB3` = variable 19, then the lookup and claim of §3.5, so a claim refusal leaves the seed written. The update hook raises the same message. Built: ADR-0313. |
| `0x0047B2D0` | Training update | At the level cap it returns 1 at once: no experience, no charge, no countdown step. Below it, adds level-scaled `ExpPerSecondTraining` every tick. Once per second (`+0xB3`) it checks the keeper can pay 5: if not, message 55 and failure; if so, it charges 5 at the creature's tile (`0x004BA420`), and every fourth charge calls `0x0050FB70` with 20, the floating "−20" (re-read 2026-10-01; this row said 20 gold every fourth check). |
| `0x0047B430` | Torture enter | Claims, then initializes `+0xAC`. |
| `0x0047B4A0` | Torture leave | Switches off an Electric Chair's sparks and shows the device again (presentation); unless `TortureVoluntary` (`+0x3EA` bit 14), zeroes `DroppedToDoJobId` (state `+0x6E`); clears bit 14; releases. Built but the presentation: ADR-0322 (`leave-hook-bodies.md` §3). |
| `0x0047B590` | Torture update | Once per second, adds variable 19 to creature `+0x4F6`, applies definition `TortureHpChange` (`+0x79F`) as health, and, when the station's `+0x1D` names the creature, applies definition **`TortureMoodChange` (`+0x7A1`)** to anger bucket 5 with selector 8. Thus the anger amount is per creature, not a global. At the level-scaled `TortureTimeToConvert` threshold it either kills on the variable-234 roll (shipped zero) or calls the conversion helper. |
| `0x0047B740` | Prison enter | **CONFIRMED** (re-read 2026-10-01): the lookup and claim of §3.5; any refusal returns 0 with nothing else done, so no seed and **no event 7**. Once the claim takes, it seeds `+0xAB` from variable 19, fires creature trigger event 7 on the captive itself (`ECX` = state `+0x8`, pushes `7, 0, 0`), and returns 1. The claim's overcrowding message `0x2A` is on its success path, so 7 fires there too. Built: ADR-0311. |
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
- **The jackpot** `0x004D6AA0`, the party and the winner's collection: `casino.md` §8–§10
  (ADR-0396).
- **The lever's place.** The set-up `0x004D6ED0` walks the room's slab chain from
  `room+0x2D/+0x2E` and takes the first slab `0x00454110` accepts whose north-west,
  north and north-east neighbours it does not. The predicate wants a room slab whose
  byte `+0xC` is `0xFF` (interior), then switches on room type (`type − 3` through the
  byte table `0x0045421C` into `0x004541F4`). Types 9, 10 and 15 (Guard Room, Workshop,
  Casino) take the parity arm `(x + y + room+0x80) & 1 == 0`. So for a Casino it is a
  floor-furniture slot, and **a Casino smaller than 3×3 has no lever**, which the owner
  confirms (2026-09-24). Its screenshot shows the lever on the top interior row. It stores
  that tile at `+0x4B/+0x4F`, creates object `0x5F` there, and writes `+0x53` nonzero
  when the lever exists. **Enter** refuses the keeper's own Casino whose `+0x53` is 0 (§3).
- **The settlement** is the room's own update, around `0x004D6783`. It runs only with a
  lever, and on turns divisible by variable 19 × 4, so every four game seconds. For
  each creature on the room's occupant list (`room+0xA`, then `+0x238`) it calls
  `0x004D7180(-4 × purse change, creature)`. Stingy, that is +32: added to the first
  gold pile (object flag `0x40`) on the lever's tile through `0x0049C760`, which has no
  cap, or else a new pile made there. Generous, it is −4: taken from that pile first,
  destroying it when empty, and the rest from the keeper through
  `0x004BA420(-min(rest, player+0x7E))`. The owner confirms that stingy takings lie
  beneath the lever (2026-09-24).
- **Built:** ADR-0187, ADR-0188, ADR-0322 (the fearless countdown); what the hooks lack is
  in `CategoryHooks.Slots`. The settlement walks the chain in tile-index order, not DK2's.

### 3.2 The captive's states, 114–126 (read 2026-09-24)

Category 14 holds 114–121 and 123–125; 122 and 126 are in Refused.
- **In.** The imp's 44 puts an admitted captive in **114**. **Corrected 2026-09-25
  (ADR-0226):** a hand drop enters **76**, which puts a captive dropped into a cell in 114
  through 1 (`hand-drop-landing.md`). **77** is the slap's reaction, entered only by the
  slap handler `0x0048B8A0`. Its handler `0x0046F1F0` sends a creature to 115 in a Prison,
  63 in a Workshop and 130 in a Torture Chamber; a Horny-flagged one goes to 238. 120 reuses that handler
  through the thunk `0x00470C00`, but it is a captive state: 130 is refused from it,
  since the two share group 1 and differ in category. **126** (`0x00473920`), after its
  first tick, goes to 114 on a Prison tile. 1 (category 1), 76, 77 (7) and 126 (0) are outside
  14, so entries from them run the Prison enter hook and, the claim taking, event 7. The slap
  sets 120 for a creature in its keeper's own Prison (`0x0048B9C3`), so 115 → 120 → 115 runs none.
- **114** (`0x00473130`): on a Prison slab that `0x00454110` accepts (for a Prison, any
  interior slab), sets 115 and stores the prison at creature `+0x3CB`; otherwise
  returns 0, which is idle.
- **115** (`0x004731F0`): hungry (the level-scaled `HungerFill` against `+0x4C`, and a
  gap of variable `0x006F2598` since `+0x50`) goes to 117. Otherwise it picks a slab
  within 4 through `0x004E50F0` and, when accepted, walks there through `0x00471890(tile,
  116, 0x80)`, which is state 6 resuming in 116; then it sets 116 whether or not the walk
  started. So a prisoner walks in 116, holding resume 116 and saved 115 (Q422).
- **116** (`0x00473350`): every 10 ticks it looks for a bar object (type 2, model
  `0x31`–`0x33`, `+0x138` naming its prison) and pushes against it; otherwise it plays
  idle actions. When none is playing it rolls 100: under 10, with action 9 available, an
  idle action 41; otherwise under 20 sets 115, the way back (Q422). It always returns 1.
- **117** (`0x00473550`): the nearest food object in the prison's slab chain. With none,
  `AngerNoFood` goes to bucket 1 with selector 1 and message `0x40`; otherwise a walk to
  it resuming in 118, returning 1. No food, or a walk that fails, sets 116.
- **118** (`0x00473750`): on the food it wipes bucket 1 (`0x00486F80(1)`), eats, and goes
  to 119 if filled, else 117 (re-read 2026-09-30). **119** (`0x00473730`) waits, then 115.
- **123–125** toggle the prison's `+0x5D` and walk; **121/122** are "freeing comrades".
  Neither is traced further.
- **76** (`0x0046EAB0`), a creature the map places with flag bit 6, goes by the room it
  stands in; in a Torture Chamber it zeroes `+0x4F6` and sets 130. 77 does not zero it.
- **Built** (ADR-0191, ADR-0194): 114 and 120 into 115, a step walked in 116, 117 to 118,
  119 and 115 (118 back to 117 when owing or birdless), 117 to 116, the drain on 14. **Ours:** 114's move
  to 115 happens on the tick of admission. The roll for a step is our 20 % a tick, from
  116 through 115, standing in for 116's roll and 115's slab pick.

### 3.3 Torture, 127–135 (read 2026-09-24)

- **The devices** are object types 8 (Torture Wheel) and 61 (Electric Chair).
  `0x004F2180` returns the nearest one in the room that no creature in 131 or 136 is
  standing on. **130** (`0x00484110`) needs that device on the creature's own tile, within
  a tile of it, then places it and sets **131**; otherwise it returns 0, which is idle.
  **131** (`0x00484590`) animates, and goes to 136 if the device is gone. 132 is a stub
  returning 0; 133 and 135 reset the animation and return 0.
- **The hook** `0x0047B590`, every fifth turn. It adds variable 19 to `+0x4F6`
  (`TorturedDuration`) and applies `TortureHpChange` through `0x004B2CE0`. When the
  chamber's owner is the creature's own it applies `TortureMoodChange` to bucket 5 with
  selector 8, so a captive's mood never moves. An enemy that is dead (vtable `+0x28`)
  gets `0x004F2370`, the manual's "valuable piece of information", and the hook returns
  before reloading. Otherwise it reloads. Past the threshold, the definition's
  `+0x76C` (`TortureTimeToConvert` × variable 19, stored by the loader at `0x0049391A`)
  scaled by stat row 24, it zeroes the duration. An enemy then dies on
  `rand(100) < TortureChanceOfDyingWhenConverted` (0) or is converted by `0x004F2800`:
  owner changed to the chamber's, trigger event 9, and the keeper's conversion count
  `+0x16A` goes up.
- **The torturer.** The job handler `0x00483AE0` walks in 7 resuming in **127**
  (`0x00483B90`). 127 looks for a creature in 129 on the chamber's slabs and walks
  beside it (6, resuming 128); failing that, it walks onto a free device resuming 130,
  setting `+0x3EA` bit 14. **134** (`0x00484950`) walks to the nearest category-15
  creature nearby. Owner (2026-09-24): only the Mistress tortures, going to the chamber to
  whip victims on the devices. Only the two Mistresses carry the job (50 %).
- **The whip** (read 2026-09-24). **128** (`0x00483E90`) finds, on the target tile
  `+0x119`, a creature in 136 or category 15; it turns to face it (`0x004B2AE0`), then
  sets 1 and 129 and starts action 63 on it. **129** (`0x00483FF0`) keeps facing it while
  the action plays, then sets 1 and 128; with no victim left, 128 returns 0, idle. Action
  63's handler `0x00499C20` waits 5 turns and spawns effect `0xED` at the victim (world
  method `+0x38C`); it does not touch its health. There is no torture-specific duration
  or boredom check in either handler. Before them each tick, dispatcher `0x00476FC0`
  runs the common needs routine `0x00477C20`—hunger, sleep, then payday—and can run the
  unhappy or angry job chooser. These can interrupt 127–129; otherwise the whip repeats
  until the victim is no longer valid. **134** waits half of variable `0x006F2590`; when
  a nearby category-15 creature on its tile is not in 133, it calls `0x0048AD30(133)` on
  that creature, then returns 0 so the Mistress goes idle. Otherwise it walks to the
  nearest one in the chamber, resuming 134. 127–129 are category 0: the torturer takes
  no hook.
- **The gate, through idle.** 127–129 are group 8, category 0 (the filler at
  `0x0047DDC0`). A whole-program operand scan finds 2,529 state-table filler stores and
  91 reads, with no later write to the 281 records. `0x00476D30` has no force argument;
  its second is an out flag. A direct 128 → 129 is refused (same group, target category
  0), so 128 and 129 go through idle:
  1 then the other, both allowed now that category 0's bit is read as 1 (ADR-0195), and
  each drops to idle when its handler fails. First read with the bit at 0, which refused
  every one of these and made the whip impossible; the owner's Mistress (Q426) did not
  fit. The apparent 134 → 133 dead end is not a transition of the Mistress: 134 invokes
  it on the victim. From victim state 131 it is same-group/category 15 → 15; from 136 it
  is different-group category 0 → 15, whose target mask is zero. Both are allowed.
- **Built** (ADR-0193): a captive dropped on a free device, 77 → 130 → 131; the hook's
  duration, hurt, own-keeper mood, threshold and conversion; the device-gone release.
  **Ours:** health at zero releases the victim to the health system (DK2 knocks it out
  and kills it on the device, revealing information); a convert is released rather than
  left on the device, which the owner confirms (Q425: it gets off, beds down, eats).
- **Built:** ADR-0196, its *Ours* list included; the leave hook's `+0x6E` is ADR-0322's.
  The information is ADR-0230 (`TortureInformationRules`); not built: its panel, reward 11's reveal, the speech.
- **Read 2026-09-24: 77, 136 and the others' way in.** 77 (`0x0046F1F0`), once the landing
  action ends, sends a Horned Reaper (`IsHorny`, definition flag bit 14) to **238** in any
  room; otherwise by the room at `+0x3B9`: Workshop 63, Prison 115, Torture Chamber 130. So
  any creature, a keeper's own included, is strapped on by a drop. **136** (`0x00484CE0`),
  in a Torture Chamber, takes `0x004F2180`'s nearest free device and needs its squared
  distance under `0x1800` (1.5 tiles²): from a tile's centre, that tile or a side
  neighbour. It moves onto it (sound `0x24` wheel, `0x25` chair; a chair turns away a
  creature without `CanBeElectrocuted`) and sets 130; otherwise 0, idle. 136 is entered
  from 131 when the device is gone (`0x00484920`), for every creature attached to a
  chamber by `0x004F1F10` (it then re-tests the room for devices, object type 8 or 61),
  by `0x004F1FB0` when the chamber is freed or taken, and by `0x004E44E0`, which moves the head
  place-holder (one a call) of a room whose definition has `SpecialWalls` (`+0x43E` bit 0) when a
  neighbouring tile's terrain changes: Library 62, Training Room 113, Workshop 70, Torture Chamber
  136, Casino 154 (`room-return-states.md` §1).
- **Built:** ADR-0200, its *Ours* list included; the chamber's and the neighbour's kicks to 136
  (ADR-0406). 238 is not.
- **Read 2026-09-24: who enters 134.** The creature update `0x00488930`, on a turn it has
  moved to a new tile (a local flag), for a creature on the ground, not dying and not
  attached to a room (`+0x3B9` zero), on a room tile, calls `0x004E8660(room, creature)`.
  Its Torture Chamber arm needs: a non-worker; the room's `UsedCapacity` (`+0x6B`)
  non-zero; creature `Flags` (`+0x46F`) bit 5; the room's owner not allied to the creature
  (`0x004BF3D0`); the creature's category through a walk not 15 or 22. It then walks the
  room's attached list (`+0x0A`, linked by `+0x238`) for a creature not dying and allied
  to it, and calls `SetState(134)` on **itself** (`MOV ECX,EDI` at `0x004E8857`) until one
  is taken. The Prison arm sets 122, "freeing comrades": the same tests with category 14
  for 15, the creature not already in 122, and the neutral player counting as an ally
  on both sides. `Flags` bit 5 comes only from a level's creature records (the loader
  copies bits 0 and 3–7 of a neutral or keeper record's `Flags`, all eight of a good one's,
  `0x00520391` on) and from triggers (`0x00518C09`, `0x0051A52B`); the constructor stores 9.
  616 of the 1,944 placed creatures carry it (104 neutral, 427 good, 85 keeper).
- **Read 2026-09-24: the information** `0x004F2370(chamber, victim)`. Nothing for a hero
  victim but the last choice. Otherwise, by the victim definition's `+0x67D` byte (0, 1 or
  2), the candidates are 3 and 7, 4 and 9, or 5 and 8, each only if `0x004C2BC0(k, victim's
  owner, chamber's owner)` says it is not yet given; then 10 on the same test; then always
  11. `rand(n)` picks one. 11 walks the victim's keeper's first room chain and calls
  `0x00517480` (an area of radius 3, kind 6) on each tile not flagged `0x20`, or failing
  that sets `+0x475 = 0x10` and `+0x476` to the chamber's owner on the first creature
  standing on such a tile; any other marks it given through `0x004C2B60`. Then speech
  `0x61`, and `0x60` for 3–10 or `0x3B` for 11. **Read 2026-09-25 (ADR-0230,
  `torture-information.md`):** 3–10 unlock rows of the enemy-information panel, presentation
  only; the candidates are keyed by `JobClass`.
- **Built:** ADR-0201 (the contact test and 134), its *Ours* list included; 122 is not.

### 3.4 The Workshop, category 5 (read 2026-09-24)

- **The credit.** The update `0x0047B190` takes `ManufacturePerSecond` (`+0x6BD`), which
  the loader divides by variable 19 with a floor of one (`0x004938B8`, beside
  `ResearchPerSecond` at `+0x6B9`), scales levels 2–10 by stat row 18, clamps to
  `0..0xFFFE` and passes it to `0x004C3200` every turn. The adder's other calls read the
  turn and discard it; a `turn % v19 == 0` call to `0x004F2300` is unread.
- **The points and the list.** The adder adds to `player+0x217` (a word). The list at
  `player+0x219` is a ring of 128 sixteen-byte entries: cost word, kind (0 trap, 1 door),
  type id; head `+0x801`, tail `+0x802`, count `+0x803` (`player+0xA1C`, which the
  Manufacture job handler `0x00470080` tests). `0x00506B50`: non-empty and head cost ≤
  points. Then `0x004C3B50` picks the keeper's Workshop nearest the creature with someone
  attached; `0x004F5240` sends everyone attached to it to state 68, makes the crate (the
  definition's crate object: trap file `0x20E`, door `0x246`) on the room's first chain
  tile, owned by the keeper when the room has space, and adds to `ItemsManufactured`
  (`player+0x162`). The points go to 0 and the order is popped. With no Workshop, the
  unread `0x004C6CF0`.
- **The pushes.** `0x005066E0(kind, type)`: refused past 128; counts the keeper's
  blueprints of the type (doors with `+0x109`, traps with `+0x10C` bit 5) against its
  crates of the type, and queues one order only while blueprints outnumber crates. Its
  callers are the level loader `0x00520F40`, `0x004C33B0` and `0x004C3860`; the removal
  `0x005069E0` is called by `0x004C3670` and `0x004C3A20`. The shipped levels place 3
  door blueprints, in one level, and no trap blueprints.
- **The decay.** The player update `0x004B9EC0` calls `0x004C3340`: with points, and
  `player+0x8A` (the hooks' count, `leave-hook-bodies.md` §4) zero, once a second the
  points fall by variable 138 (1), to no lower than 0. The count is stored (ADR-0322).
- **Built** (ADR-0203): all four. **Ours:** the nearest Workshop is judged by its first
  tile. ADR-0214 (`workshop-crates.md`) adds state 68, the crate left to nobody in a full
  Workshop, the Workshop's capacity extra `0x004F6140`, and the carry to a blueprint. The
  order kind is 0 for a door and 1 for a trap. Blueprints placed in play
  (`0x004C33B0`, `0x004C3860`) are built (ADR-0231); not built: the removals.

### 3.5 Capacity: the test, the claim and the finders (read 2026-09-25)

- **The test** `0x004E53B0(room, units)`: `UsedCapacity (+0x6B) + units ≤ TotalCapacity
  (+0x67)`, both words. Room type 6 (Library) must also pass `0x004E1AC0`, type 10
  (Workshop) `0x004F6140`, and type 9 (Guard Room) adds the owner's creatures whose
  `+0x2B6` is 6 and `+0x2BA` names the room. Independently checked 2026-09-28;
  the extra predicates are in `library-and-research.md` §7 and `workshop-crates.md` §3.
  `units` is the
  definition's `+0x672` byte (creature file `0xF4D`), 1 for all 46 shipped creatures.
  TotalCapacity is the furniture count `0x004E3ED0` (`auto-placed-furniture.md` §5).
- **The lookup** (`0x0047AFB0`, re-read 2026-10-01): no thing in the cell, or
  `0x004E7F80` refusing, returns 0 at once (state 1, no message). `0x004E7F80` admits the
  creature's own keeper's room (room `+0x1D` = creature `+0x28`) and any keeper's Prison,
  Torture Chamber, Temple, Combat Pit or Crypt (11, 12, 13, 16, 26).
- **The claim** `0x004E5A70` (re-read 2026-10-01). Prison (11) first needs the position
  tests `0x00453A20(11)` and `0x00454110`, failing with 0 and no message. A Combat Pit (16)
  creature with `+0x3E6` bit 15 is linked and given `+0x3B9` with **no test and no units**.
  Otherwise the test; on success it links the creature at the head of room `+0x0A`, writes
  `+0x3B9`, and adds `units` to `+0x6B` and `+0x6D`. The Torture Chamber (12) also links it
  into a per-owner list at `+0x4C`. A Prison past `+0x4B` raises message `0x2A` and may
  call `0x004E2E60` on a roll.
- **The release** `0x004E5E00`: unlinks, zeroes `+0x3B9`, and subtracts `units` only
  **while `+0x6B ≥ units`**, so a fighter's release takes a spectator's unit. Callers: the
  leave hooks; the room freed (`0x004E39C0`) and `0x004D9570`, each kicking every occupant
  to state 1 first; guards after a state reset (`0x004883F8`, `0x0048D1E2`). A sale frees
  the whole room and a merge the absorbed one (`room-slab-chain.md` §3–4): both evict.
  **A walk keeps the place** (re-read 2026-10-01): entering category 2 runs no hook, so a
  creature `0x00477C20` sends to bed from work holds its place until it arrives.
- **A failed claim** calls `0x004C6AE0`: the nearest other room of the type that passes
  the test and is reachable (`0x004D0E60`), preferring rooms none of whose occupants pass
  `0x0045C640`. Its answer is written to a local nothing reads. So it decides only
  whether message `0x2A` is raised (no other room) before `0x0048AD30(1)`, state 1. The
  Guard Room searches only when every attached creature's cell names it, and otherwise
  raises the message at once.
- **Callers of the test**, 23 in all: the Training finder `0x004F3560` and the Temple
  finder `0x004F1A20`, each asking for `units` at two sites; the
  generic nearest-room-with-space helpers `0x004C6510`, `0x004C6610` (7 callers) and
  `0x004C6860`; the crate maker `0x004F5240`; and the claim. The torture handler
  `0x00483AE0` has two finders. `0x004F29A0` (a chamber with an occupant, used while the
  keeper has victims) **asks no capacity**. `0x004F2B80` (any chamber) asks for
  **`2 × units`** at both its sites, and falls back on a timer at room `+0x9F` that is unread. The strap
  helper `0x00483B90` also asks for `2 × units`. The torturer's own states 127–129 are
  category 0, which claims nothing, so only the victim holds a place.
- **Built** (ADR-0205, ADR-0309): the test, every finder asking it, the lookup, and the
  claim and release as the hooks, with `0x004C6AE0`'s message choice; `UsedCapacity` sums
  `CreatureState.HeldPlace`. Not built: Q504's gaps; the Library and Guard Room extras
  and the torture fallback timer.

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
- Spectating's update `0x0047AA70` (re-read 2026-09-24): countdown `+0xAB` reloaded from
  variable 19, then variable 135 to buckets 5, 0 and 3, selector 9; it always returns 1.
- **What is built, per slot,** is `CategoryHooks.Slots` (ADR-0308); its divergences are the
  owner's decision under Q402.
- **Every countdown hook fires every v19 + 1 turns, from entry** (Q424). Casino
  `0x0047AD75`, Spectating `0x0047AA73`, Training `0x0047B368` and Torture `0x0047B596`
  each test the byte, decrement and return while it is nonzero, and at zero reload and
  act. The enter hooks seed it with variable 19 (four). So the effect lands on the fifth
  turn after entry and every fifth turn after that, and Prison's `4 × v19` reload makes it
  every seventeenth. The Temple's `+0xAF` (`0x0047B970`, seeded by `0x0047B8C0`) is the
  same. **Built** (ADR-0192): one countdown per creature, seeded when `SetState` runs an
  enter hook, so a walk inside one category keeps it.

## 5. How `SetState` runs the hooks — CONFIRMED (`0x00476D30`, re-read 2026-10-01)

Read in full with Ghidra 12.1.4; `travel-states.md` §3 owns the walk rows (a category-2 target runs no hook).

1. **Guards, before the rule:** it returns 0, writing nothing, if a state-object pointer at
   `+0x04`, `+0x08` (the creature) or `+0x0C` is null, or creature `+0x3E6` bit 0
   (`InHand`) is set. Then it clears the out flag.
2. **The rule** (`creature-state-table.md` §2.2). A refusal returns 0 and runs no hook.
3. **Same category** (the current one, or `+0x40`'s when leaving a walk): commit, no hook.
4. **Leave:** the old category's leave hook (`+0x40`'s when leaving a walk); its return is
   discarded. Then a raw `+0x10 = 1` and the out flag set to 1.
5. **Enter:** the target category's enter hook, which sees state 1. **A zero return returns
   0 at once:** the creature is left in state 1, already out of its old category, with
   `+0x18`, `+0x1C` and `+0x44` unwritten.
6. **Commit:** `+0x10` = target, `+0x18` = the turn, `+0x1C` = `+0x44` = 0, return 1.

A slot is a member pointer: the function at `+0`, a `this` adjustment at `+4` added to the
state object into `ECX`; a null slot is skipped. The 2→2 recursion's `SetState(1)` is
judged by the rule too, and no travel state with any resume is refused it (all 4 × 281).
**Ours** (`CategoryHooks`, ADR-0308) runs steps 2–6 in this order. The guards are not
modelled (Q503).
