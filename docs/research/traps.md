# Mechanic: the trap's update, its firing, its threat and the Trigger

Source(s): DKII.EXE v1.70, PyGhidra and pyghidra-mcp reads on 2026-09-28 and 2026-10-02; the
shipped `Traps.kwd`, `Shots.kwd` and THINGS files; `dk2tools sgmap` field names for `CTrap`.
Original version: DK2 v1.70 (GOG)
Confidence: STRONGLY INFERRED unless a line says otherwise; the shipped-data sections are
CONFIRMED from data, not from a run of the game.
Implementation: `TrapRules`, `TrapSystem`, `TrapRemovalSystem`, `TrapActivationRules`,
`FightRules.AnswerTrap`, `KwdTraps` (ADR-0251, ADR-0321).
Tests: `TrapFiringTests`, `TrapActivationTests`, `TrapRuntimeOnRealDataTests`,
`TrapsFireOnRealDataTests`.

Authored placement and construction are [doors-and-traps.md](doors-and-traps.md).

## 1. Definition loader

`0x004AA440` receives the file record behind a `0xA8` prefix of field-presence words; ESI is
the source and EBP the destination (`0x004AA457`–`0x004AA461`), so `CopyPairs.py` does not
harvest it. The pairs below were checked against its listing and body. File stride 579.

| File | Runtime definition | Meaning / conversion |
|---|---|---|
| `0x1C4` | `+0xA0` | idle-effect height, narrowed to a word |
| `0x1C8` | `+0x74` | recharge: low 32 bits of raw × ticks/second, shifted right 12 |
| `0x1CC` | `+0x78` | charge threshold, same conversion |
| `0x1D0` | `+0x8E` | threat duration, same conversion; firing narrows it to a byte |
| `0x1D4` | `+0x9A` | mana per shot, signed dword |
| `0x1D8` | `+0x84` | idle-effect interval, same conversion |
| `0x1DC` | `+0x70` | detection range in whole tiles |
| `0x1E0`, `0x1E4` | `+0x7C`, `+0x80` | shot data words |
| `0x1EA` | `+0x8C` | threat magnitude, unsigned word |
| `0x1EC` | `+0x88` | definition flags, dword |
| `0x1F0` | `+0xA2` | initial health, unsigned word |
| `0x1F4`, `0x1F6`, `0x1F8` | `+0xB7`, `+0xB5`, `+0xB9` | no-mana, idle and dying effects |
| `0x20B` | `+0xBD` | automatic trigger selector |
| `0x20C`, `0x20D` | `+0xB4`, `+0xBB` | trap and shot ids |
| `0x231`, `0x235`, `0x239` | `+0xC1`, `+0xC5`, `+0xC9` | shot origin offsets in 1/4096 tiles |
| `0x241` | `+0xA4` | signed health gain per recovery application |

Flags read: `0x2` reveal on fire, `0x20` initially hidden, `0x80` turn towards a target,
`0x100` mana gate, `0x200` retain the firing presentation, `0x800` Guard Post branch.
Steering's `0x004D12B0` also reads `0x10000` and `0x20000` to enumerate a completed trap as a
half-tile obstacle when its query enables traps.

## 2. The trap thing

Vtable `0x0066D754`: `+0x14` update `0x004A84E0`, `+0x20` `0x004B2AD0` (health := 0), `+0x24`
`0x004A96A0` (owner change), `+0x28` `0x004B5C30` (health ≤ 0), `+0x30` `0x004A93E0`
(→ `0x004B7C90`, which calls the cleanup `0x004A7B20`), `+0x58` `0x004B5C50` (turn rate
`0x100`). The factory `0x004B7C10` allocates, writes the id at `+0xF6` and constructs.

Constructor `0x004A7940` links class 3, seeds health `+0x40` from `+0xA2`, zeroes the
recharge `+0xFF`, charge `+0x10B`, disarmer `+0x115`, Guard Post room `+0x113`, threat clock
`+0x10E`, threat `+0x10F`, Trigger countdown `+0x10D`, Trigger creature `+0x117`, recovery
`+0x107` and sight cache `+0x119/+0x11D`; it clears `+0x10C`'s low nibble (the Trigger count, `0x004A79CD`), sets bit 5 (under
construction) and bit 4 (hidden) from flag `0x20`, and stamps `+0x103` (last idle) with the turn.
The zeroed sight-cache turn means the finder answers nobody on turn 0 (§5).
These names agree with `sgmap`'s `CTrap`: `fFF_rechargeAtTime`, `f107_nextHealthGainTick`,
`f10B_chargeTimeBeforeShot`, `f10C_triggerTrapsInRange_flags`, `f10D_triggeringTime`,
`f10E_threatDuration`, `f10F_threat`, `f117_triggeringCreature`. The heading `+0xF4` is zeroed
by the class constructor `0x004B28D0` (`0x004B28F5`), which also links the class (`0x004B4A10`). The loader
`0x00520E50` places the trap at the tile's centre, `(x<<12)+0x800`, `(y<<12)+0x800`, height 0.

## 3. The update `0x004A84E0`, in its order

1. Hidden presentation (`0x004A9F10`, a foreign local player's view); not built.
2. **Owner.** Tile word `+8 & 0xFFF` not the trap's owner: the disarmer's `+0x43D` is cleared,
   an unbuilt trap leaves its keeper's blueprint list (`0x005056E0`), health := 0. The update
   goes on.
3. `+0x10E` counts down when nonzero. Nothing else reads it (the dumper `0x004AA130` aside).
4. Under construction, being disarmed or dead: skip to step 8 (`0x004A8C6B`).
5. Guard Post (`0x800`): a room lookup `0x004C61C0(9)` every eighth turn and a creature
   dispatch to it; not built.
6. **Turn** (flag `0x80`, §6). The idle effect (`+0xB5`, `+0x84`, `+0x103`); presentation.
7. **Recharge gate:** at or after `+0xFF`, the selector (§5) answers a creature or nothing.
   Nothing: charge := 0. A creature: charge + 1 (a byte), a sound, and once charge ≥ the
   threshold `+0x78`, readiness (§4): 2 plays the no-mana effect and posts message `0x35` at
   most every 15 s (`0x004A8490` → `0x004C1A30`, player `+0xAA`); 4 fires (§7).
8. Animation and `0x004B3AD0` (frame advance); presentation.
9. **Threat** (§8), every update while `+0x10F` is nonzero.
10. **Trigger** (selector 3) with `+0x10D` nonzero: `+0x117` cleared unless a live creature,
    then the propagation (§9).
11. The dying effect; sound positions.
12. **Recovery:** alive and the turn strictly past `+0x107`, with `+0xA4` nonzero: gain
    `min(+0xA4, health max − health)` through `0x004B2CE0` when its low word is nonzero, and
    `+0x107 += [0x006F2590]` (one game second) either way. Returns 1.

## 4. Readiness and charge

`0x004A7D10` returns, in order: 1 under construction; 0 before the recharge; 2 when flag
`0x100` needs mana the owner lacks and the owner is not the hero player (`0x006F254C`);
3 with a disarmer; otherwise 4. `0x004C1740` allows a zero cost, else `cost <= mana`. A zero
threshold fires on the first detection; short of mana the charge is kept and climbs.

## 5. The selectors

Table `0x0066D430`: 1 `0x004A9290`, 2 `0x004A92C0`, 3 `0x004A9350`; 0 has none.

1. **Sight:** refuses while the Trigger nibble of `+0x10C` is nonzero, then `0x004A9400(0)`.
   The finder returns its cached answer on a second call in a turn. On the next turn the
   previous target is kept if live, in sight (`0x004512F0`), attackable (`+0x3EA` bit 6) and not
   `IsHorny` (`def+0x686 & 0x4000`), with an inclusive range; then every creature of each player
   `0x004BF390` calls hostile, attackable and not `IsHorny`, strictly nearer than the range and
   the best, and in sight, replaces it. Squared distance sums each axis squared and shifted
   right 12 (unsigned). The range is `+0x70` tiles, plus two when the turn asks.
2. **Tile:** refuses with a Trigger near. `0x00453AC0` takes the first thing in the tile's
   chain that is a creature, owned neither by the trap's owner nor neutral (`0x006F2548`), and no
   trap's disarmer (`+0x43D`). No alliance test. That creature is refused — and nobody else
   tried — if dying (`+0x3E6` bit 21), flying (vtable `+0x88`, `0x0048ED40`) or `IsHorny`; trap
   16 also wants `IsWorker`.
3. **Trigger:** the same first creature, with none of selector 2's tests.

**The tile chain** (world `+0x9EE`, eight slots a tile, slot at thing `+0x16`, links `+0xA` and
`+0xC`) is prepend: `0x004B4E70` and the position setter `0x004B5020` link at the head, and
`0x004B5020` relinks only when the tile changes. The first creature is the last to have
entered. `0x0048ED40`: a worker answers its `+0x3E6` bit 8; animations 12, 17, 19, 25, 30 answer
no; `0x00494700(0) == 3` answers `+0x3E6` bit 11; otherwise `CanFly`, except in states 217–218.
Ours: `CanFly` outside 217–218 for a non-worker; the other arms are not built.

## 6. The turn

With flag `0x80` the update calls the finder at range plus two first. A target: turn toward
its bearing (`0x004B2AE0`); none: toward the heading plus 10 (`0x004B2C10`); at most the rate
`0x100` either way (`0x005119F0`, `0x00511A30`, `Bearing`). The finder's answer is cached for
the turn, so a turning sight trap fires at range plus two (Q452). Sentry, Magic Door and Hero
Sentry turn.

## 7. The firing

`0x004A7FD0(target)`: no target, aim one tile along the heading
(`x + sin·0x1000>>16`, `y − cos·0x1000>>16`); a target, its position plus half its height, and
after creation a launch `0x004AB8A0` when the shot's `Flags & 0x20`; trap 16 then writes the
target into the shot's data2.

`0x004A80E0(aim, target)`: shot id 0 or a failed `0x004B7CD0` returns 0 with nothing after.
Origin: `x + (offX·cos>>16) − (offY·sin>>16)`, `y + (offY·cos>>16) + (offX·sin>>16)`, height
`+ offZ` (32-bit products, arithmetic shifts). Sine `0x006C1488` and cosine `0x006C1C88` are
one 2560-dword table a quarter turn apart, `trunc(65536·sin(2πi/2048))` for every entry
(CONFIRMED against the dump). A creature target is told (`0x0045AA20(trapTag, 0)`); then target
`+0x147`, aim (`0x004AB830`), data words from `+0x7C/+0x80`, and with a target the forced
collision `+0x14D` for every selector but 1 and 3. Then: the mana (flag `0x100`, not the hero
player) through `0x004C19C0` (adds, clamps at 0) and `player+0x10F2 += cost`; flag `0x2`
clears hidden; charge := 0; `+0x10E` := the duration's byte and `+0x10F` := `+0x8C` if the
duration is nonzero, else 0; `+0xFF` := turn + recharge. A Trigger keeps the creature in
`+0x117` and sets `+0x10D` = `Fixed_Multiply(ticks/s << 12, 0x1000) / [0x006F2966]`
(`TriggerTrapTriggerSpeedTilesPerSecond`, 4096 shipped): four.

**The target's answer** `0x0045AA20` has two arms. The first, a creature in the combat
category (22), runs only when the attacker is **not** class 3 (`0x0045AA66`). The second, a
creature outside it, runs only when the attacker **is** class 3 (`0x0045AD68`): if
`0x0045A6D0(0)` allows, it is not an Imp and `0x004D0E60` finds a route, `SetState(1)` and a walk
to the trap (`0x00471890`). So a creature fired at by a trap walks to it, and one struck by a
creature while not fighting does nothing. combat-fight-state.md §5 had this backwards.

The shot's caster word `+0x145` is not written, so "never the caster" excludes nobody. Its
immediate hit is `0x004ABD60`'s forced collision (shots.md §4a). Height, aim height and the
vertical origin are dropped with the rest of the flight's height (Q398).

## 8. The threat

`0x004B32A0(owner, threat, radius)`: count every creature of a hostile player strictly within
`radius·(radius>>12)` and in sight; with any, each one strictly within
`max(1, (radius>>12) − 1)` tiles and in sight gains `threat / count` through `0x0045C5D0`, which
adds it to `TempFear` (`+0x524`) after the owner's `+0x108D` discount and records the trap as
`ThreatThing` (`+0x533`) when it exceeds `+0x535`. The radius is `(+0x70 + 1) << 12` for
selector 1 and `0x5000` otherwise. **Nothing in the trap's code clears `+0x10F`**
(`0x004A7900`–`0x004AA800`: the constructor, the firing, the update's read and the dumper), so a
trap that has fired once with a nonzero duration shares its threat every update after.

## 9. The Trigger

**Counts.** Completion `0x004A96D0` (also the loader's for a completed trap) clears bit 5 and
registers the upkeep (`0x004C1B60`); a Trigger adds one to every own non-Trigger trap within a
tile (`0x004A9720`), any other trap recounts the own Triggers within a tile (`0x004A9A00`). The
cleanup of a completed trap, a Trigger, subtracts its one, never below 0 (`0x004A9890`). Each
looks at the first own trap (`0x00453700(3, owner)`) on each tile of the 3×3 square at squared
distance `< 0x1001`: the four sides. Order of loading does not change the counts.

**Propagation** `0x004A8E70`, each update while `+0x10D` runs: decrement it; reach
`r = 0x4000 − countdown·0x1000` (unsigned), squared limit `((r>>2)²)>>12`, half-width `r>>14`.
At 3, 2, 1 only the Trigger's tile is reached; at 0, the four sides. For each own trap within:
the creature live and the Trigger able to see it (`0x00509780` → `0x004512F0`), or the trap a
Trigger, fires at it through readiness and `0x004A7FD0`; trap 16 fires at it unseen. Otherwise
the trap fires through readiness one tile toward the Trigger, unlaunched (`0x004A80E0` with
target 0, aim based at the trap, `ECX = EDI`), and the owner's `+0xEE6` is incremented whatever
readiness answers (`0x004A9259`; a statistic, its readers unread, not built). A recharging
trap does nothing.

## 10. Death, removal and recovery

`World_UpdateThings` removes a dead trap the same turn (door-blocking.md §7). The cleanup
`0x004A7B20` updates its sounds, then — unless the world is closing (`[world]+0x4C`) and only
for a completed trap — releases its upkeep (`0x004C1B90`) and, for a Trigger, `0x004A9890`;
then unlinks it from its tile, owner and class lists (`0x004B4D40`, `0x004B4F60`, `0x004B4AB0`).
`0x004B2CE0` adds a signed delta to a trap's health directly.

## 11. Disarm and reveal (Q451, Q455)

States 204–206 are `0x00458DF0`, `0x00458EB0`, `0x00458F60`: travel to the trap at fractions
`(0x7FF, 0x2AA)`; face the centre and wait 50 + a draw below 200 ticks; then a roll below the
level-scaled `def+0x5E0` attempts a firing and enters 220 with a 50-tick retry, the other arm
destroys the trap. The attempt precedes clearing the disarmer, so readiness refuses it (Q451).
Exit hook `0x00459130` clears both references. `sgmap` names creature `+0x43D`
`f43D_trapTagId` and `+0x43F` `f43F_durationUntilAttemptingAnotherTrapDisarm`. Who starts it is
unread (Q455): the 13-block scan for `0x0000043D` (56 hits, positive control 226) does not
rule out indirect writes. The reveal: firing with flag `0x2`, `0x004AA110`, and a creature's
tile change (`0x00488930`) with `def` flag `0x04000000` scanning table `0x006BC3B0`; not built.

## 12. Shipped definitions and shot coverage

**CONFIRMED from shipped `Traps.kwd` and `Shots.kwd`:** zero charge everywhere but Spare Door 1
(two ticks). "Built" is `TrapRules.FiringBuilt`: the shot's damage type, process and chained
shots all have a handler.

| Trap | Sel. | Recharge | Mana | Shot | The shot | Fires here |
|---|---:|---:|---:|---:|---|---|
| Gas | 2 | 16 | 0 | 58 | process 31 unbuilt (Q395) | no |
| Boulder | 1 | 4 | 0 | 26 | process 22 unread | no |
| Sentry | 1 | 24 | 250 | 44 | Cannon Ball: launched, contact 7 | yes |
| Alarm | 1 | 4 | 0 | 13 | process 12, presentation arm unbuilt | no |
| Fireburst | 1 | 48 | 250 | 14 | stands, area damage 2 | yes |
| Lightning | 1 | 48 | 375 | 59 | speed 0, process 9 on its first tick | yes |
| Freeze | 2 | 24 | 250 | 45 | forced hit, process 11 | yes |
| Fear | 1 | 24 | 125 | 25 | process 21 unread | no |
| Trigger | 3 | 16 | 0 | 39 | health 1, no damage, collision or process | yes |
| Spike | 2 | 16 | 0 | 27 | damage type 1 unread | no |
| Guard Post | 2 | 4 | 0 | 0 | room and creature dispatch | no |
| Magic Door | 2 | 32 | 500 | 57 | Door Fireball: forced hit, contact 7 | yes |
| Spare Door 1 | 2 | 30 | 0 | 0 | none | no |
| Hero Sentry | 1 | 24 | 100 | 44 | Cannon Ball | yes |
| Hero Alarm | 1 | 4 | 50 | 13 | process 12 | no |
| Jack In The Box | 2 | 4 | 0 | 64 | process 39 unread | no |

The Magic Door's shot is 57, not the 59 this table once said. Only Cannon Ball and Door
Fireball carry the launch bit; the Fireburst and the Lightning shot stand where they are made.

## 13. Shipped placement

**CONFIRMED from data** (`TrapsFireOnRealDataTests`): 959 traps on 39 levels, all completed by
the loader; 649 fire here (Sentry 115, Hero Sentry 320, Lightning 80, Freeze 41, Fireburst 7,
Trigger 86); none is a Magic Door. 36 stand beside a Trigger. One is killed by the owner test
on its first update: **Level 20's hero Boulder at (12,5), on a Neutral-owned tile** — a prediction,
not an observation. Level 18's Freeze at (51,54) now freezes a hero standing on it on turn 0
(the Inferno and Chicken scenario moved off it).

## Built (ADR-0321) and left

Built: §2's fields; §3 steps 2, 3, 4, 6 (the turn), 7, 9, 10, 12; §4; §5 with the tile chain
noticed once a turn before the traps; §6; §7 for the traps that fire here, with the target's
answer's trap arm and the creature arm's correction; §8 without the owner discount and
`ThreatThing`, as `FearRules` holds neither (Q375); §9; §10's removal and Trigger counts.

Left: the Guard Post; disarming (Q455); the reveal; the upkeep (Q59); the no-mana message's
clock; effects, sounds and animations; the unread shots above; heights; `0x0048ED40`'s worker,
animation and `0x00494700` arms; a trap's damage from creatures and shots.

## Open questions

Q450 (what is left), Q451, Q452, Q455, Q398 (shot order and height), Q513 (observations).
