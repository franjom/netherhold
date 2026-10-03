# Mechanic: the Boulder — the trap's shot, the rolling object and what it strikes

Source(s): DKII.EXE v1.70, pyghidra-mcp listings and decompiles on 2026-10-02; the shipped
`Traps.kwd`, `Shots.kwd`, `Objects.kwd` and the global variables (`BoulderOnRealDataTests`).
Original version: DK2 v1.70 (GOG)
Confidence: STRONGLY INFERRED (binary, static) unless a line says otherwise; nothing here was
watched in the running game (Q520).
Implementation: `BoulderRules`, `BoulderSystem`, `StatusEffectRules.StunInto` (ADR-0333).
Tests: `BoulderTests`, `BoulderOnRealDataTests`, `TrapsFireOnRealDataTests`.

The trap that fires it is traps.md (§12: Boulder Trap 2, sight selector, range 3, recharge 4 ticks,
no mana, shot 26). Shot 26 has health 1, flags `0x24` (counts down, launched), process events `0x40`
(first tick only), collide type 3 and no damage type.

## 1. The launch: process 22 (`0x004AEBD0`)

On the shot's first tick, before it moves (shots.md §4 step 1):

1. The world's vtable `+0x200` must pass the shot's position (the same spot test a shot's flight
   uses); otherwise nothing.
2. `0x004B7A90(74, shot owner, &position, &out)` makes object 74 at the shot's position.
3. The first class-3 thing on that tile (`0x00453670(3, …)`) is destroyed through its vtable
   `+0x20`: the Boulder trap is spent.
4. Health: two calls to `Thing_ModifyHealth` (`0x004B2CE0`), minus the object's own and plus
   `BoulderInitialHealth` (variable 157, `0x006F292A`, 4000). Wear `+0x156` := 0.
5. Speed `+0x152` := `BoulderSpeedTilesPerSecond` (158, `0x006F292C`, 4915) over ticks a second
   (`0x006F2590`), unsigned: 1228.
6. Direction: `Math_Atan2` (`0x0065FB54`) of the **tile** difference from the shot to its target
   point `+0x131`, masked to 11 bits; `+0x14A` := `(sin·0x1000) >> 16`, `+0x14E` := `−((cos·0x1000) >> 16)`,
   the sine table of traps.md §7. So the direction is a whole tile long, and quantised to the tile.
7. `+0x15A` := the sign of `+0x14E`, `+0x15B` := the sign of `+0x14A`. A sound and a bridge
   command (presentation). Returns 0.

## 2. The object

`0x0049B1C0`, every object's initialisation, seeds health `+0x40` from definition `+0x104` (Hp)
and the process state `+0x13A` from `+0x11B` (StartState). `ObjectDef_Load` (`0x0049DD60`) reads
source = file + `0x80`: `+0xF4` is Flags (file `0x344`), `+0xF8` the Width as a word (`0x32C`),
`+0x104` Hp (`0x348`). Object 74: flags `0x238200` (`BoulderCanRollThrough`, `BoulderDestroys`,
`Damageable`, `CanBeSlapped`, `Bounce`), Hp 4000, width 4096, StartState **17**.

The object process table is filled at `0x0049E686`: 20-byte entries from `0x006E4B38`, the state
id at `+0`, the update at `+4`. Entry 17's update is **`0x004A1390`**, the boulder's (13
`0x0049FC50`, 14 `0x0049FCB0`, 15 `0x0049FD80`, 18 `0x004A13F0` …). The union (objects.md §7) is
`+0x14A` x direction, `+0x14E` y direction, `+0x152` speed, `+0x156` wear, `+0x15A`/`+0x15B` the
y and x signs: 18 bytes, the widest member.

## 3. The update, state 17 (`0x004A1390`)

1. The strike (`0x004A0930`, §4).
2. The roll (`0x004A0E30`, §5) — which destroys a boulder whose health is 0 and returns first.
3. The terrain wear (`0x004A10E0`, §7).
4. Health `≤` wear: destroyed (vtable `+0x20`), a sound. Returns 1.

The object update `0x0049BD20` runs it through the state runner `0x0049EED0`, then the stepper
`0x004B2030`, which moves only `+0x104`/`+0x110` (zero on a boulder); its decay needs definition bit 0
or 1, which `0x238200` lacks. So nothing breaks a boulder at a wall but §6's tenth a turn (Q520). A slap (`Object_ReactToSlap`
`0x0049C2A0`, its object-74 arm `0x0049C4ED`–`0x0049C56A`, read 2026-10-03) sets the roll to the slap's
angle (`+0x14A` := sin·`0x1000` >> 16, `+0x14E` := −cos·`0x1000` >> 16, the signs untouched), writes the
angle to the heading `+0xF4`/`+0x102`, and costs `BoulderSlapDamage` (240, `0x006F29F8`, 400) through the
health routine with the boulder's own owner: ten slaps break it, as the owner recalls from a level played as golf.
The angle is slap action 6's (`0x004C0FE0`), whose source is unread.
**A slanted roll can enter a solid tile** (read 2026-10-03). The handlers run only on a centre line and
test only the tile ahead on that axis; the step moves through `0x004B5560` → `0x004B5020`, which relinks
the tile chains and tests no terrain. A launch at 45° (`+0x14A`, `+0x14E` = ±2896) from a tile centre
whose two side tiles are free reaches neither centre line in its first tick at 3·1228 a tick, and crosses
both tile boundaries at once into the diagonal tile, solid or not. At that tile's centre the later axis's
handler runs: going north with rock ahead and the free tile to the west, it turns west, wearing a tenth.
So it clips the corner and rolls on; nothing breaks it there. Ours does the same (`BoulderTests`).

## 4. The strike (`0x004A0930`)

For each offset of table `0x006BC3B0` — N, NE, E, SE, S, SW, W, NW, then (0,0) — each thing in that
tile's chain, by class, where `half = def+0xF8 >> 1` (2048) and distance is traps.md §5's squared
sum (each axis squared, shifted right 12, unsigned):

- **Creature (0):** not in state 76 (the drop's landing) or 256, not dying (`+0x3E6` bit 21), and
  distance `< (half + def+0x63A/2)² >> 12` (`def+0x63A` the footprint). Damage: its level-scaled
  `Hp` (level 1 raw, else `table[level] × Hp / 100`, table `0x006E4154 + 0xE1`), halved, × 100,
  over `BoulderToCreatureDamagePercentage` (159, 50): **its whole `Hp`**. Capped by the boulder's
  health less wear (unsigned), through `Thing_ModifyHealth` with the boulder's owner, so the stun
  and freeze multipliers apply and the invulnerable word is not tested. Wear grows by what landed.
  Still not dying: unless its state's group tag (`0x006DA8C6 + state × 25`) is 1 or it is possessed,
  `0x00491830(0x100)` — `SetState(256)`, action 52, creature event 15. Dying: a sound.
- **Object (2):** not itself, within `(half + other def+0xF8/2)² >> 12`, its definition carrying
  `BoulderDestroys` (`+0xF4` bit `0x20000`): damage its definition's `Hp`, capped; below its health,
  through `Thing_ModifyHealth`, else destroyed (vtable `+0x20`) and its health is what landed.
- **Trap (3):** completed (`+0x10C` bit 5 clear), not a Trigger of the boulder's owner, within
  `half² >> 12`: `0x004A7D90(boulder)` — readiness, and a trap that is ready fires **at the boulder**
  (`0x004A7FD0`) — then damage a quarter of its definition's health (`def+0xA2 >> 2`), capped, as the
  object arm. No owner test: a keeper's own traps fire and suffer.
- **Door (4):** completed (`+0x109` clear), within `half² >> 12`: damage its definition's health
  (`def+0x91`), capped, as the object arm. No owner test.

Shipped `BoulderDestroys`: the beds, the Chicken, the Dungeon Heart (13), the Arch Pillar, the
Dungeon Heart Plug, the Arch Construction, the Jack-In-The-Box object and the Boulder itself.

## 5. The roll (`0x004A0E30`)

Health 0: destroyed, return. Otherwise, `s` the speed, `r` `BoulderSpeedToHealthRatio` (160, 50),
`h` health and `w` wear, the tick's speed is `((s·r)/100)·(h−w)/h + ((100−r)·s·5)/100` — the first
quotient unsigned, the second signed, the third unsigned: 3·s whole, 2.5·s near breaking. `+0xDC`
:= speed·4096 / 12868 (2π·2048), the spin; presentation.

At a tile centre on y (`y & 0xFFF == 0x800`) the north or south handler runs by the sign of `+0x14E`;
at a centre on x, the west or east one by `+0x14A` (§6). Then `dx = speed·+0x14A >> 12`, `dy` alike,
and while either is nonzero, the step `0x004A0050` (§6) moves the boulder; when it stops on a centre
line, that line's handler (jump table `0x004A10C8`: 0 N `0x004A05B0`, 1 E `0x004A03E0`, 2 S
`0x004A0770`, 3 W `0x004A0210`) runs, and a turn that leaves it whole (`w < h`) ends the move with
a sound. **A turn that breaks it keeps going** with what is left of the old step.

Then the fear: `0x004B32A0(owner, threat, BoulderFearRange << 12)` (162, 4 tiles; traps.md §8) from
the boulder, with `threat` = `BoulderFearAmount` (161, 701) × `h` / `def+0x104` when both are
nonzero, else 701. Then the heading `+0xF4`/`+0x102` := `Math_Atan2(+0x14A, +0x14E)`; nothing in the
update reads it.

## 6. The step and the four handlers

**The step** (`0x004A0050(&dx, &dy, &dir)`): the next centre line ahead on x is
`((x ± 0x800) & ~0xFFF) + 0x800` (`+` when `dx ≥ 0`); if `x` and `x + dx` lie on different sides of it
(by sign, zero a side of its own) and `x` is not on it, the move stops there, `y` follows
proportionally (`(c − x)·dy/dx`, signed), and `dir` is 1 (`dx ≥ 0`) or 3. The same on y with the
possibly shortened `y`, recomputing `x` from the original `dx`, `dir` 2 or 0: the later axis wins. What
was not travelled is left in `dx`, `dy`; the position is set through `0x004B5560`. Returns whether it
stopped.

**The handlers** test tiles of the boulder's own tile `(x, y)` (`pos >> 12`) through `0x00453C10`
(§8). Going north: `(x, y−1)` free, return 0. Blocked: `(x−1, y)` then `(x+1, y)`. Both blocked: `+0x14E`
:= `+0x1000` (back), x untouched. West blocked only: `+0x14A` := `+0x1000`; east blocked only:
`−0x1000`; neither: `+0x15B << 12`, or with no sign `random(2)·0x2000 − 0x1000` on the world stream
(`0x005558D0`, `world+0x15A0`, lines `0x5BC`/`0x5F9`/`0x542`/`0x57F`); then `+0x14E` := 0. South is the
mirror (ahead `(x, y+1)`, back `−0x1000`); west and east test ahead, then north `(x, y−1)` and south
`(x, y+1)`, and take `+0x15A`. Every turn adds `h/10` (signed) to the wear and posts a bridge command
(presentation); returns 1. The four were diffed against north instruction for instruction.

## 7. The terrain wear (`0x004A10E0`)

On the boulder's tile: terrain flag `0x100` (lava), wear `+= h/6`; else `0x80` (water) `+= h/10`; else
a room of type 3, 13 or 16 whose floor height at the boulder (`0x004545C0`) is below `0x1000`,
`+= h/3`; never past `h` (unsigned). Each with effects and sounds; lava also sets `+0x91` bit `0x20`, cleared
on entry, whose reader is unread. `0x004545C0` gives 0 on a Portal's
cell piece 4 (its hole), 0 on a Temple's interior cell (`+0xC == 0xFF`, `0x00454110`), `0x400` on a
Combat Pit's interior and `0x1000` or `0x1200` (the slopes) elsewhere on those rooms, so only those
cells wear it. The heart, Hatchery, Graveyard and Crypt floors are lowered too, but not tested.

## 8. What stops it (`0x00453C10`)

A tile blocks when its terrain has `+0x17B` bit 0 (`Solid`); when its room (`+2 & 0xFFF`) is type 11
(Prison) and its cell byte `+0xC` is `0xFF` (an interior cell), or type 20 (the 2×2 Hero Gate); or when
an object in its chain lacks `BoulderCanRollThrough` (`+0xF4` bit `0x10000`, 39 of 145 shipped).
Doors, creatures, traps and water do not block.

## 9. What reads nothing

`BoulderDeteriorationDamagePercentagePerSecond` (155, `0x006F2926`): a byte scan of every section,
`.text` and `cseg` included, finds its address once, the loader's store (`0x0051D2F4`); the same scan
finds the three readers of 161 and 160. An indexed read through a base was not ruled out.

## 10. Built and not built (ADR-0333)

**Built:** §1; §2's fields as `ObjectState` (`Health`, `PosX/PosY`, `RollX/RollY`, `RollSpeed`, `Wear`,
`RollSignX/RollSignY`); §3–§8, with state 256 run as 255 (`0x004810D0` jumps to `0x004810E0`). Ours
orders a tile's things as creatures newest-linked first, then objects newest first, the heart, the trap
and the door; boulders update oldest first after the shots and before the traps; the coin is our stream.
The heart object is the owner's heart health at its centre tile (shots.md §7, Q363's stand-in). A trap
fires at the boulder's position with no target thing.

**Not built:** a shot striking the boulder (its predicate admits object 74, shots.md §4a; contact and
area damage then quarter or halve), and so a trap's forced hit on it; the slap (ADR-0341: ours slaps creatures and
the Call to Arms flag only, and carries no angle); creatures steering round a rolling boulder (unread); the heading, spin,
effects and sounds.

## Open questions

Q520 (the predictions in play), Q513 (Level 20's hero Boulder), Q398 (heights).
