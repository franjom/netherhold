# Mechanic: shots — what a creature spell fires, how it flies, and how it lands

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-23. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046). Offsets were checked against Flame's structure map
  (ADR-0080: facts only; none of its names is used here). The executable's own
  strings name the file: `ShotProcess.cpp`.
- The reference install's `Shots.kwd` (65 records) and `CreatureSpells.kwd`,
  read with `DK2.Data`'s layouts — a roster, never copied here.

Original version: DK2 v1.70 (GOG)

Questions: answers Q392 for the damaging shots and the status shots' hooks; opens
Q395–Q397. `combat-spells.md` §7 is the caster's side — action 16 makes the shot
this page follows. Traps fire the same things (`doors-and-traps.md`, `0x004A80E0`).

**What this settles:** a shot is a thing of its own with a definition from
`Shots.kwd`. It flies in a straight line at the definition's speed in position
units a tick, under gravity only if it has mass, and each tick looks for a thing
of its collide class within its radius — never its caster. On a hit its damage
type decides: **type 7 deals `ShotData1` flat, unscaled by level**, through the
health routine with the shooter's owner; type 2 hurts every enemy within
`ShotData2` tiles each tick, falling off linearly. Its process type then runs at
the events its process flags name — that is where Freeze, Slow, Haste, Heal,
Armour, Invulnerable, Invisible, Lightning and Drain live, each a few stores to
creature fields whose creature-side meaning is the next read (Q396).

---

## 1. The shot thing

Class vtable `0x0066D86C`, 0x173 bytes, made by `0x004B7CD0(things, type,
owner, &origin, &heading, &out, flag)` when the definition exists: allocate,
construct (`0x004B7D60`), owner byte `+0x124`, then `0x004AAC90` initialises from
the definition `+0x125`: position and last position, heading `+0xF4` and last
angle `+0x102`, zero velocities, air friction `+0x11E`, **health `+0x40` = the
definition's health**, **`+0x13D`/`+0x141` = the definition's two data words**
(the spell's `ShotData1`/`ShotData2` overwrite them, `combat-spells.md` §7),
speed through the vtable, age `+0x12D` 0, max range `+0x155` 0. The caller sets
caster `+0x145`/`+0x16D`, target `+0x147`, target position `+0x131`. The
per-tick handler is vtable slot `+0x14`, `0x004AB180`; slot `+0x2C` returns the
last position, which is what `combat-spells.md` §7's lead reads.

## 2. The definition as the game holds it (`0x004ACDF0`)

| runtime | file | field (`ShotDef`) | note |
|---|---|---|---|
| `+0x37` dword | 152 | `Unknown_0098` | **data1's default** |
| `+0x3B` dword | 156 | `Unknown_009C` | **data2's default** |
| `+0x3F` dword | 160 | `ShotProcessFlags` | which events run the process type (§5) |
| `+0x43` dword | 168 | `Flags` | §5 |
| `+0x47` word | 188 | `Health` | lifetime or hits (§5) |
| `+0x49` word | 144 | `Mass` | gravity applies when nonzero (§6) |
| `+0x4B` word | 148 | `Speed` | **position units a tick**, unconverted |
| `+0x4D` word | 140 | `AirFriction` | |
| `+0x4F` word | 164 | `Radius` | slot `+0x3C` returns twice it |
| `+0x51` byte | 200 | `AttackCategory` | 4 fire, 6, 7 (§7) |
| `+0x58` byte | 190 | `ShotId` | |
| `+0x59`/`+0x5B`/`+0x5E`/`+0x61`/`+0x64`/`+0x67`/`+0x6D`/`+0x6F` words | 174/176/180/182/184/186/172/178 | creation, death, hit-solid, -lava, -water, -thing, general, timed effect | |
| `+0x5D`/`+0x60`/`+0x63`/`+0x66`/`+0x69` bytes | 191/193/194/195/196 | death, hit-solid, -lava, -water, -thing **shot** | chained (§4) |
| `+0x6A` byte | 198 | `CollideType` | 1 friends, 2 enemies, 3 both, 4 bodies (§4) |
| `+0x6B` byte | 197 | `DamageType` | 7 contact, 1–4 and 8 area, 0 none (§7) |
| `+0x6C` byte | 199 | `ProcessType` | the handler table (§8) |
| `+0x71` byte | 192 | `TimedDelay` | timed effect every N ticks |

No copy into the word at `+0x52` was found; §8's radius calls pass it (Q397).

## 3. Launch (`0x004AB8A0(shot, &aim)`)

Speed `s = Speed << 4`. Heading = atan2 to the aim in the horizontal plane, 2048
steps a turn, stored at `+0xF4` and `+0x102`. Pitch = atan2 of the aim's height
difference against `isqrt(horizontal d²) << 6`. Velocity `+0x104` = `s` turned by
pitch and heading through the sine table at `0x006C1488`/`0x006C1C88` (16.16),
plus `0x400` upward when the vtable's `+0x68` says so; `0x004ABA10` applies it.
Because the stepper adds a sixteenth of the velocity a tick (§6), **the shot
advances `Speed` position units a tick**: Fireball 3686 (0.9 tile, 3.6 tiles a
second), Arrow and Knives 4915, Firebomb and most others 2048, Grenade 1228.

## 4. The tick (`0x004AB180`), in order

1. `age++`. **First tick only:** the creation effect (`+0x59`) at my position and
   heading; with `Flags` bit 7 it is given my velocity × ticks-a-second / 20 as its
   carried velocity ([shot-effects.md](shot-effects.md) §2); `DamageType` 5 runs the area handler once (§7);
   **process event 0x40**; then `processedAtStart = 1`.
2. `health ≤ 0` → the tail (step 8).
3. Last position and angle := current. If `+0x155` (max range, position units)
   is set and my distance² from `+0x159` (start) exceeds it → destroyed
   (`vtable+0x20`, `0x004AB308`).
4. **Process event 0x2** (before movement); the handler returning 0 ends the
   tick here.
5. **Collision** (`0x004ABD60`, §4a) — with a thing, or a move along the
   velocity.
6. **Per-tick damage** by `DamageType` (`0x004AC3C0`, §7).
7. **Physics** (`0x004B2030`, §6). Returns 0 = stopped by the world: the
   hit-solid effect (`+0x5E`) and **hit-solid shot** (`+0x60`, inheriting target
   position, data words, caster, target and the possessed flag), sound, **process
   event 0x8** (returning 0 ends the tick), and with `Flags` bit 0 → destroyed
   (`vtable+0x20`), tick ends; **without bit 0 it falls through to the countdown
   below**, as a flying shot does (`0x004AB4B6`, re-read 2026-10-01 for Inferno,
   `keeper-spell-inferno-chicken.md` §4). Returns 1 = flying, and the fall-through:
   `Flags` bit 2 → `health−−`; health still > 0 → the tail; else **death**: sound,
   death effect (`+0x5B`), **death shot** (`+0x5D`, inheriting), **process event
   0x80**, and if that returns 0 the tick ends.
8. **Tail:** an attached effect follows me; the timed effect (`+0x6F`) every
   `TimedDelay` ticks; render update; sound position.

So `Health` is a lifetime in ticks when `Flags` bit 2 is set (Fireball 60,
Knives and Arrow 40, Freeze 50 — Fireball flies at most 15 s), or a count of
hits otherwise (Firebomb 128 with bit 1 set dies on its first thing).

### 4a. Collision (`0x004ABD60`)

**Who is hit.** A forced collision (`+0x14D`, set by the instant handler §8) hits
the target `+0x147` if it is alive. Otherwise the `CollideType` routine
(`0x006E4FF0`, filled by `0x004ABCA0`) calls the overlap finder
`0x00451570(index, shot, predicate, 0, &out)`, which walks the tiles the shot's
radius covers and returns the first thing that overlaps (`0x004B5590`) and
passes the predicate — **never the caster** (`+0x145`), unless caster and target
are one. Predicates: **1 friends** (`0x004B58E0`): a creature of my owner that is
not dying (body bit 21, `creature-death.md`), a closed door of any owner, an Arch Pillar
(object 94) of another owner or object 74; **2 enemies** (`0x004B5800`): a
creature whose owner is hostile to mine (`0x004BF390`) and not dying, or a
Turncoat; the same doors and objects; **any completed trap**, whoever owns it (re-read
2026-10-02); **3 both**; **4** a dead body (class 6).

**The overlap test (`0x004B5590`, read 2026-09-23)** is a box, not a circle: on each
axis the distance between the two things is strictly under **half the sum of their
radii**, and the height difference under the sum of their heights. The shot's radius
here is **twice** its `Radius` (`0x004B7E10`), 1638 for every creature shot; a
creature's is its footprint (STRONGLY INFERRED: the only radius it has); an object of a
hostile owner counts as 0x800 wide, and so does a trap of an enemy owner, any other trap 0
wide (its vtable `+0x3C` returns 0). A door with `+0x10D` or `+0x109` set never
overlaps; a thing never overlaps itself. Sampled once a tick, so a shot at 3686 units a
tick can pass a standing creature between two positions where a 2048 one cannot.

**On a hit:** the hit-thing effect (`+0x67`) — on a creature the effect's `+0x133`
set to the creature's tag, so it follows the creature ([shot-effects.md](shot-effects.md) §2); the **hit-thing shot** (`+0x69`) inherits my target
position, data words, caster, target and possessed flag and is aimed at my target
position; **process event 0x4**; `DamageType` 7 → **contact damage** (§7);
per-class sounds; on a creature with `Flags` bit 8 the creature is turned to
face away (`0x005119F0`, `0x0048FF70`) with its hit sound; `Flags` bit 1 → health
0 (the shot dies). No hit: the velocity is applied in **two half-steps**, each
checked against the world (`vtable+0x200`) and, for a possessed caster, the
pick ray; the position moves only through passable spots.

## 5. The flags, as read

`Flags` (`+0x43`): bit 0 dies on hitting solid; bit 1 dies on hitting a thing;
bit 2 health counts down each tick; bit 3 bounces off solid (slot `+0x60`, `0x004AB810`);
bit 4 is the lobbed launch (slot `+0x68`, `0x004B7E30`); bit 6 removes the trail at once on
teardown; bit 7 the creation effect gets my velocity; bit 8
turns a struck creature. Bit 5 launches a shot: a trap's fired at a target (`0x004A7FD0`) and
a door's, tested in the door code at `0x004A6390` (
`traps.md` §7); the Fireburst, Spike and gas clouds lack it.

`ShotProcessFlags` (`+0x3F`) are **events**: 0x40 first tick, 0x2 before each
move, 0x4 on hitting a thing, 0x8 on hitting solid, 0x80 at death. `0x004AD730
(event)`: with the bit set, the `ProcessType`'s routine runs and its result is
returned; with the bit clear, `0x004B32A0(shot, +0x52, owner, 1)` runs (Q397)
and 1 is returned. Bits 0x1 and 0x100 were not tested by any routine read (Q395).

## 6. Physics (`0x004B2030`, the moving-thing stepper), read for the shot's needs

Above the floor and not flying (`vtable+0x88`), gravity `GravityConstant`
(variable 130, 240) × 256 is added downward — the shot's hook applies it only
with `Mass` (Grenade 8192; every other creature shot 0, so **they fly flat**).
The drag hook `vtable+0x64`; force and transitive velocities summed; the step is
divided into `|v| >> 14 + 1` sub-steps, at most 16; each sub-step tests the next
spot (`vtable+0x200`) and lava/water; a blocked spot with slot `+0x84` set → health
0 and return 0 — slot `+0x84` is `0x00402AD0`, 0 for every shot — otherwise, without the
bounce slot `+0x60` (bit 3), return 0 (the solid hit of §4 step 7), and with it the
velocity is halved and reflected (a bounce, STRONGLY INFERRED from the sign flips; §10). A shot slower
than 0x80 a tick returns 0 too. Each sub-step's position is set through
`0x004B5560`; the last position is the previous tick's.

## 7. Damage

**Contact, `DamageType` 7 (`0x004AC420(shot, thing, owner)`):** `damage =
+0x13D` (data1), **doubled in sniper mode** (`+0x165`); the target's slot `+0x34`
must allow. A creature: refused while its `+0x445` (invulnerable ticks, §8) is
set; **`AttackCategory` 4 (fire) on a creature below a fifth of its level-scaled
`Hp`, not standing on water (terrain flag bit 7), sets it on fire**
(`0x0048EBD0(creature, 10, owner)`, Q396). An object of type 74: a quarter of
the damage for categories 4 and 6, nothing otherwise; any other object or a
door of the shooter's own owner: nothing. Then `0x004B2CE0(target, −damage,
owner, 0)` — the melee blow's routine (`combat-fight-state.md` §4), so a heart's
killer is stamped the same way. **Nothing scales with the caster's level.**

**Area, per tick (`0x004AC3C0` by `DamageType`; 1 `0x004AC5A0`, 2 `0x004AC6D0`,
3 `0x004AC940`, 4 `0x004ACAF0`, 8 `0x004ACCA0`; 0, 5, 6, 7 nothing).** Type 2
(`0x004AC6D0`, CONFIRMED from the listing 2026-10-02): over the tiles within
±`data2` of me, clamped to the map, rows outer, each tile's thing chain in order,
every thing **not of my owner** — a creature with `+0x445` clear, any object, a
**trap or a door** unless my `AttackCategory` is 7 (gas) — within `data2` tiles
takes `d × (R − dist) / R` through the health routine, `R` and `dist` in position
units, where `d` starts as `data1` and **is overwritten by each result**
(`0x004AC8BF`), so each later thing in the tick takes less. Object 74's quarter only
gates the hit; the damage uses `d`. Ours: objects are not hit. Type 1, the Spike's
(`0x004AC5A0`, read 2026-10-02): over the tiles from my tile less `data2` to **below** it
plus `data2` on each axis — half-open, so `data2` 1 is the 2×2 square to the north-west —
in the map, a creature not of my owner with `+0x445` clear takes `data1` flat and object 74
`data1 / 2`; no falloff, no trap or door. Types 3, 4 and 8 print *"Creature Damage, %d"* and
are unread (Q395).

**The Firebomb's explosion hurts nobody in v1.70.** Action 16 writes the spell's two
words over the definition's defaults with no zero test (`0x00498A9D`), the hit shot
copies its parent's words unconditionally (`0x004AB433`), and the area handler reads
the shot's own `+0x141` (`0x004AC6DA`); the Firebomb spell's `ShotData2` is 0, so the
explosion chained on the hit has a zero radius. The Firebomb is a 600-point contact
hit with a display (CONFIRMED binary 2026-09-23; the owner recalls no splash, Q398).

## 8. Process types read (table at `0x0069E84F`, filled by `0x004AD1B0`)

Each runs with the shot as `this`; the target is `+0x147`, `t/s` variable 19.

| type | who | what it does |
|---|---|---|
| 3 | Modify Health (shot 4, the Heals' death shot and the keeper Heal's own shot; shot 43 Disruption) — `0x004ADB30` | a live creature target that is not `IsHorny`; a fire-category shot on one under a fifth of its health, not on water, ignites it (display); **shot 43** sets health to five percent of the level-scaled `Hp` (`health := 5% — health` through the health routine) or destroys a target already under it; **any other** heals `min(data1, max — health)` through the health routine, after `0x0048AD30(1)` — **`SetState(1)`, idle** (`rebellion.md` §7.2), so the heal interrupts what the target was doing — unless frozen, held, a pit fighter or abnormal-targeting |
| 4 | Haste Creature (150, 15), Slow (20, 15), Imp Haste | `0x0048CAD0(creature, data1, data2)` — the speed modifier and its duration; then, faster than `def+0x5B4` → body bit 23 set, bit 16 clear, `+0x4DD` = data2; slower → bit 16 set, `+0x4DD` 0 |
| 5 | Cast Armour (80, 50) | creature `+0x443` = data1 |
| 9 | Lightning (300) | `0x004B32A0(shot, +0x52, owner, 2)`; the target alive, an object only if an Arch Pillar or untagged; a creature whose second flag word `def+0x68A` bit 0 is set is immune; **damage data1, doubled on water**, through the health routine; then `+0x4E3` = 20 and, unless held (`+0x3CB`), in state 23, `IsHorny` or `Possessed`, `0x00491830(creature, 0xFF)` |
| 10 | Invulnerable (5) | creature `+0x445` = data1 × t/s — the word the melee blow and §7 refuse |
| 11 | Mistress and Fairy Freeze (10, 15) | `0x004B32A0(shot, +0x52, owner, 2)`; not `IsHorny` → `0x0048CB60(creature, data1 × t/s)` |
| 12 | (keeper) | effect 0x2F at the tile; a UI call for the local player |
| 15 | Chicken (keeper spell 11) — `0x004AE2A0` | a live, non-`IsImmuneToChicken` creature target: `ChickenTimer` `+0x44B` = data1 × t/s, then `SetState(96)` (`keeper-spell-inferno-chicken.md` §3) |
| 20 | the Invisibles (15) | creature `+0x44F` = data1 × t/s; `0x0045EE60(creature)` drops every attacker's hold on it and its targetable bit clears while the ticks run (`creature-status-effects.md` §2) |
| 28 | the Guided Bolts | every eighth turn the velocity is re-aimed at the target with the shot's speed; wired to event 0x4 only in the shipped flags — how it homes in flight is UNKNOWN (Q395) |
| 30 | Inferno (keeper spell 9) — `0x004AF660` | each tick before the move: every creature of any owner on non-solid tiles within data2 tiles, unless `CanWalkOnLava` without `CanFly`, takes data1 and one tick of burning; first tick at half the radius² (`keeper-spell-inferno-chicken.md` §4) |
| 34 | the Heals, Hail Storm, Skeleton Army | **instant**: the shot is moved onto the target and `+0x14D` forced, so §4a hits it this tick; no target or a room → health 0 |
| 35 | Drain (25) | `d = min(target health, data1% of its level-scaled Hp)`; the target loses `d` and the caster gains `d`, both through the health routine; refused on a dying target |

Read since, in `creature-status-effects.md` §2: 29 Reaper Fireball (the stun), 38 Poison
Spit. Read for the traps (2026-10-02, the table's entries 17 bytes from `0x0069E84F`, the
handler at +1): **12** `0x004AE120` the Alarm, message 47 to my owner with my tile, plus a
UI call for the local player; **21** `0x00484940` the Fear, a stub returning 0; **22**
`0x004AEBD0` the Boulder, object 74 made at me and rolled toward my target point, the trap on
my tile destroyed (built, [boulder.md](boulder.md)); **31** `0x004AF8E0` the gas, a threat at radius 2 that counts nobody and,
for damage type 0 only, workers kicked to state 210 — every shipped process-31 shot has
damage type 2; **39** `0x004B02E0` the Jack In The Box, data1 to the worker named by data2,
then a creature of type 0x2E in state 273, the trap destroyed (built, [jack-in-the-box.md](jack-in-the-box.md),
which also has the area damage's 32-bit arithmetic). Since recorded: **1** `0x004AD7A0`
(creature creation) and **17** `0x004AE990` (area knockdown). **37** `0x004B0050` the
Web (2026-10-04): `0x004B32A0` fear at the shot, then over the tiles within `+0x141`
of it, each creature strictly inside that radius other than the thing tagged at `+0x16D`
gets `0x0048CAD0(−90, +0x13D)`, `+0x4DD` = 0, and body bit 15 cleared and bit 16 set.
`0x0048CAD0` divides unsigned, so −90 writes the top speed (variable 19 `<< 12`), not a
slow. Slow sets the same bit 16, which `creature-status-effects.md` §2 reads as display;
what, if anything, holds a webbed creature is unread (HYPOTHESIS).
Unread: **18** Raise Dead (`0x004AE880`, not a defined function in the project), the
buff table `0x006E4AC8` and area damage types 3, 4 and 8 (Q394, Q395).

**The events.** `0x004AD730`'s five direct callers raise 0x40 (`0x004AB281`), 0x2
(`0x004AB30F`), 0x8 (`0x004AB4A5`), 0x80 (`0x004AB662`) and 0x4 (`0x004ABFC1`): none raises
0x1 or 0x100 through it (other readers of those bits were not searched). Type 28's
handler `0x004AF3A0` is referenced only from its table slot (`0x0069EA2C`).

## 9. The shipped creature shots, compressed

| shot | speed | hp | flags | collide | damage | process | data1 (spell overrides) |
|---|---|---|---|---|---|---|---|
| 1 Fireball, 47 Spit, 52 Reaper Fireball | 3686 | 60/50/128 | 0x127/0x127/0x123 | 2 | 7 | 0/0/29 | 300/150/300 |
| 12 Arrow, 11 Knives | 4915 | 40 | 0x127 | 2 | 7 | 0 | 300–450 |
| 16 Firebomb → 34 explosion on hit | 2048 | 128 | 0x123 | 2 | 7 → 2 | 0 | 600; explosion 250 in 3 tiles |
| 18 Grenade → 19 explosion | 1228, mass 8192 | 16 | 0x13C | 0 | 0 → 2 | 0 | 60 in 2 tiles |
| 23 Gas Missile → 55 cloud | 2048 | 16 | 0x123 | 2 | 0 → 2 | 0 → 31 | 100, cloud 50 in 2 tiles |
| 3 Gas Cloud (Bile Demon) | 4096 | 5 | 0x4 | 2 | 2 | 31 | 60 in 1 tile |
| 8 Freeze, 38 Slow, 6 Haste, 9 Armour, 7 Invulnerable, 22 Invisible | 2048–4096 | 1–76 | 0x23–0x27 | 2 / 1 for buffs | 0 | 11/4/4/5/10/20 | see §8 |
| 30 Lightning | 0 | 8 | 0x104 | 0 | 0 | 9 | 300 |
| 35 Guided Bolt, 36 Chicken Arrow, 43 Disruption | 2867/1638/2048 | 60/96/128 | 0x127/0x127/0x23 | 2 | 7 | 28/0/0 | 300/300/990 |
| 53 Heal, 37 Hail Storm, 54 Skeleton Army | 0 | 1 | 0x2 | 2 / 1 | 0 | 34 | 3000/10/9 |
| 42 Drain, 61 Web, 62 Poison Spit, 41 Cyclone, 40 Raise Dead | 2048/1024 | 128/16/20/128/1 | 0x27/0x23/0x123/0x25/0x23 | 2/2/2/0/4 | 0 | 35/37/38/17/18 | 25/15/200/3/9 |

Every creature shot has radius 819 (a fifth of a tile).

## 10. Built and not built

**Built (ADR-0167):** `ShotState` is the thing, `ShotSystem` its tick after the
creatures have moved, `ShotRules` its rules: §3's launch at `Speed` a tick along the
line to the aim; §4's order — a hit, else two half-steps refused by solid
tiles, a bounce for flags bit 3, else a stop that kills a bit-0 shot; the lifetime
counting down; death chaining the death shot, none for a shot the world stopped;
§4a's collide classes over creatures by alliance and owner, never the caster; §7's
contact damage flat and the type-2 linear area; chained hit shots inheriting the data
words. Corrections from the build's last read: flags **bit 3 bounces** (slot `+0x60`,
`0x004AB810`), **bit 4 is the lobbed launch** (slot `+0x68`, the `+0x400` of §3), a
**zero mass flies** (slot `+0x88`, `0x004B7E40`), slot `+0x84` returns 0 for every shot,
and air friction scales the velocity by `(4096 — AirFriction) / 4096` a tick, no
lower than a half.

**Built since (ADR-0168):** §4's process events 0x40 (first tick) and 0x4 (the thing
struck, written into the target word first) for the nine types with a creature side
(`creature-status-effects.md`); §7's invulnerable refusal and the health routine's
multipliers on contact and area damage. **(ADR-0170):** the pre-move event 0x2 with the
instant handler 34 (the shot moved onto its target, dying with no live one) and the Modify
Health handler 3 (§8, the heal and the Disruption); the finder's overlap box (§4a).
**(ADR-0287):** §4a's forced collision as `ShotState.ForcedCollision`, set by the instant
handler and a keeper's cast at a thing, hitting the target tag if live and spent either way;
handler 3's `SetState(1)` with its four exclusions (`keeper-spell-casting.md` §4).

**Not built, named (Q398):** height — every shot flies flat and gravity, the lob and
the aim's half-height are dropped, the overlap box's height test with them; the finder
takes the nearest overlap where DK2 takes tile order;
objects and bodies as things hit (doors since ADR-0318, traps since ADR-0326) — a shot sent at a heart strikes within a
quarter tile of its tile (ours, a stand-in: Q363); process types 28 (guided), 34 (instant) and the unread
ones (Q394, Q395); a stopped or standing shot counts its lifetime down where DK2's physics returns 0 and does
not; the effects ([shot-effects.md](shot-effects.md) §7). **Thing order (2026-10-04):** `0x004B7830` updates list 0 (creatures)
before list 1 (shots, kind 1 from `0x004AAC90`), each newest first (`0x004B4A10` links at
the head, the next pointer read after each update), so a creature-fired shot should move
the turn it is made (STRONGLY INFERRED: the factory's fire path was not traced), as ours
does; ours walks `world.Shots` newest first too (ADR-0367).

## Confidence

**CONFIRMED (binary):** §1 creation and defaults; §2's mapping; §3's launch
arithmetic; §4's order and every branch; §4a's finder, predicates and hit
handling; §7 contact and type-2 area arithmetic; §8's ten handlers as tabled.

**CONFIRMED (data):** §9 and the radius.

**CONFIRMED (binary, 2026-09-23):** §4a's overlap test; §8's types 3 and 34.

**STRONGLY INFERRED:** the bounce in §6; class 6 as dead bodies. (Body bit 21 was
first read here as invisibility; it is the dying flag `0x0048B770` sets —
`creature-status-effects.md` §3, corrected 2026-09-23.)

**CONFIRMED (binary, 2026-10-04):** §8's Web body and the five raised events; §10's list
order and shot kind.

**UNKNOWN:** Q395 (how the Guided Bolt homes in flight, handler 18, body bit 16, area
types 3, 4, 8), Q397 (`0x004B32A0` with the never-loaded word
`+0x52`).
