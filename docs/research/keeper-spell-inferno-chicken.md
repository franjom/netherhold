# Keeper Inferno and Chicken

Sources: DKII.EXE v1.70 (GOG), read with Ghidra 12.1.4 through pyghidra-mcp and the
headless runner, 2026-10-01; the reference install's `KeeperSpells.kwd`, `Shots.kwd`
and `Creatures.kwd`, read byte by byte. No original cast of either spell was
captured. Binary claims are **STRONGLY INFERRED** unless marked otherwise; values
read from the shipped files are **CONFIRMED**. Function records under
`functions/` carry the per-function detail; this note is the mechanism.

What this settles: the two offensive spells the battle step chooses but the build
does not cast (`computer-keeper-management.md` §4). **Chicken** turns one creature
into a chicken for 60 s: it stops looking for fights and walks to its own keeper's
Hatchery, but it can still be attacked. **Inferno** is a fire zone that does not
move. For 30 ticks it hits **every creature of any owner** within one tile of where
its target stood, the caster's own creatures included, for 100 per tick.

## 1. Shipped data — CONFIRMED

`KeeperSpells.kwd` record fields at the offsets `KwdKeeperSpells` reads:

| Spell | id | shot | data1 / data2 | upgraded | cost | cooldown raw | loc · target | flags |
|---|---|---|---|---|---|---|---|---|
| Inferno | 9 | 15 | 100 / 1 | 15, 125 / 1 | 50000 | `0x3C000` | 1 · 5 | `0x4` |
| Chicken | 11 | 10 | 60 / 0 | 10, 90 / 0 | 10000 | `0x3C000` | 1 · 3 | `0x4` |

Neither has upkeep. The cooldown loads as `(raw × 4) >> 12` = **240 ticks, 60 s**
(`keeper-spell-thunderbolt.md` §1). Flag `0x4` is the offensive arm's bit.

`Shots.kwd` (offsets of `shots.md` §2):

| Shot | process | process flags | flags | health | speed | collide | damage type |
|---|---|---|---|---|---|---|---|
| 10 Chicken | 15 | `0x4` (on a hit) | `0x6` | 16 | 2048 | 2 (enemies) | 0 |
| 15 Inferno | 30 | `0x2` (before each move) | `0x104` | 30 | 2048 | 0 (none) | 0 |

All chained shots are zero. `Flags` bit 1 (dies on a thing) and bit 2 (health
counts down a tick) are set on Chicken. Inferno has bit 2 and bit 8, and bit 8
turns a struck creature only on a collision, which collide type 0 never makes.

`Creatures.kwd` flags at `0xEFC`: `IsImmuneToChicken` (bit 17) is set on Prince
Tristran, King Reginald, Prince Felix, Horny, Stone Knight, Lord of the Land, Prince
Balder, Unique Firefly, Unique Rogue and Jack. `CanWalkOnLava` (bit 5) is set on
Salamander, Firefly, Giant, Fairy, Horny, Unique Firefly and Unique Salamander, and
of those, Firefly, Fairy and Unique Firefly also have `CanFly` (bit 13).

## 2. From the battle step to the cast

`0x00503060`'s offensive arm walks ids upward: Thunderbolt 6, then Inferno 9, then
Chicken 11. Tremor 8 is excluded by its flag `0x10`. With `q = cost / 2499` each takes the
roll `random(q + 2) >= 4 − SpellPreference`: q is 2 for Thunderbolt, 20 for
Inferno and 4 for Chicken. The target is a creature of the fighter's target owner
strictly within five tiles (`computer-keeper-management.md` §4).

- **Chicken** (target selector 3, entry byte `0x11` = 1): it is cast at a creature.
  The permission `0x004BBA40` is checked, then `0x004BBF90(11, tag, 0)` is called
  and its return is ignored (`0x0050361D`, `0x0050362D`). A passed permission
  spends one allowance.
- **Inferno** (target selector 5, entry byte 0): it is cast at a place. The battle
  step copies the target's x, y and z (`+0x1A/+0x1E/+0x22`) into a local, then calls
  `0x004BB8A0(9, &pos, 0)` and `0x004BBD30(9, &pos, 0)`. It spends the allowance
  only when **both** return nonzero (`0x0050365F..0x0050367B`).

**Permissions.** Both helpers check the cost against the mana (cost ≤ mana) and the
cooldown. A first cast is always ready, because its stamp is zero. Both then run
the location validator **1** (`0x00517B80`) at the position: own or allied land,
not neutral, not navigation-solid, and none of the room exclusions (Portal centre,
the Heart's inner 3×3, the interiors of Prison, Temple and Combat Pit). The full
rule is in `keeper-spell-thunderbolt.md` §2. **So both spells work only on an enemy
standing on the caster's or an ally's land.** The target validators differ:

- Selector 3 (`0x00517990`) accepts a live tag whose class `+0x12` is 0 (a creature)
  and whose owner `+0x28` is not the neutral player. Own and allied creatures pass.
- Selector 5 (`0x00517F60`) always returns 1.

Neither validator tests range, fog, health or immunity. An immune creature costs
the mana and is not changed (§3).

**No other caster.** Of the nine `0x004BBD30` sites, the others push spell 4, 13,
1 or Call to Arms' id held in a register, or are the packet handler's
(`0x00513322`). The other `0x004BBF90` site is the packet handler's too. Inferno and Chicken are therefore reached only by the battle step
and by a human's packets.

## 3. Chicken

**The shot.** `0x004BBF90` creates shot 10 at the target's position (the height
bump is Thunderbolt's alone), writes data1 and data2 (`+0x13D/+0x141`) and the
target tag `+0x147`, and for selectors 1, 2, 3 and 6 sets the forced-hit word
`+0x14D` (`0x004BC1D1..0x004BC1E1`). It is not launched, so its velocity is zero.

**The hit.** On its first update the collision routine `0x004ABD60` hits the forced
target if the tag is live and the thing's layer `+0x16` is not 2 (off the map). It
clears the word either way. On a hit it raises event 0x4. Damage type 0 deals no
contact damage. `Flags` bit 1 then kills the shot. If the target was lifted or died
first, the shot stays where it was made for its 16-tick life. It chickens the first
creature that overlaps it and is hostile to the caster and not dying (collide
type 2, `shots.md` §4a). That hit overwrites `+0x147` with the creature struck
(`0x004ABE7D`). Shot 10's radius is 819, about a fifth of a tile.

**Process 15 (`0x004AE2A0`).** The table at `0x0069E84F` gives entry n's callback at
`+1` (`0x004AD730` indexes it). The process:

1. Calls `0x004B32A0(owner, def+0x52, 2)`. A radius of 2 makes its bound
   `2 × (2 >> 12)` zero, so the call does nothing (§5).
2. For a target that is live, a creature, and whose kind lacks
   `IsImmuneToChicken`: sets `ChickenTimer` `+0x44B` = data1 × variable 19, which
   is 240 ticks (360 upgraded).
3. Then calls `0x0048AD30(96)`, which is `SetState(96, 0)` on that creature.

The timer is a store, not a maximum, so a second Chicken restarts it. It is
written **before** the state request, so a creature whose transition is refused
still carries it. Every reader below tests the timer, not the state, so such a
creature is still kept out of fights, fear, Possession and the Pit for the full
240 ticks.

**Entering state 96.** State 96 has group 3 and category 7, and category 7's
entry mask is `0xFFFFFFFF` (`state-categories.md` §7). From any other group the
move is allowed. From another group-3 state it is allowed only if that state is in
category 7: 75–78, 209, 210, 255–257, 263, 264 and 269–273, so a stunned or
hand-landing creature turns. Dying (216, 217), frozen (218), 71, 207, 208, 219 and
246 refuse it (`creature-state-table.md` §2.2). The process is the only literal
request for 96 (`SetStateLits` over both setters).

**State 96 (`0x00463BE0`), each time it runs:**

1. `0x004C6750` on the creature's **own** player (ECX is the dword at
   `creature+0x2A`) walks
   that player's room list, newest first, for type **4** (Hatchery). It keeps the
   room whose `NavigationPos` (`+0x2B/+0x2C`) tile centre is strictly nearest by
   squared distance, among those `Route_IsPointReachable` (`0x00471630`, options
   `0x80`) accepts for the creature.
2. If one is found, it draws `0x004E4D90(room, &pt, 1, 0)`, a random room tile:
   one draw on the world seed, whose result is not used. It then calls
   `Travel_RouteToRoom(room, 96, 0x80)`. If that succeeds, the state returns 1.
   Inside the room this is `SetState(1)` then `SetState(96)`. Outside, it walks there
   through state 7 and resumes 96 (`functions/00471A70.md`).
3. Otherwise it wanders. It draws `random(4)` for a cardinal direction and rotates
   through the four until a neighbour's terrain (`+0x17B`) lacks `Solid`, `Water`
   and `Lava`. If none qualifies it stays on its own tile. It then draws `random(9)`
   for a sub-tile point on a 3×3 grid at 1/6, 1/2 and 5/6 of the tile (the table at
   `0x00698EC8`), and calls `Travel_RouteToPoint(pt, 96, 0x80)`. It returns 1.

So **a chicken walks to the nearest reachable Hatchery of its own keeper and then
keeps re-entering 96 there. With no Hatchery it wanders one tile at a time.** Every
run draws from the world seed. The owner remembers a chicken running a few tiles
around (2026-10-01). That is the wander branch, which every hero takes because the
hero player owns no Hatchery, and which a keeper's creature takes when its Hatchery
is unreachable. The Hatchery branch is unobserved (Q498).

**The end.** The timed-effects routine `0x0048E160` decrements the word once per
tick. When it reaches 0 it calls `SetState(1, 0)` and clears byte `+0xF2`, a mover
field of unknown meaning (Q416). It does both whatever state the creature is in,
and clears `+0xF2` even when the request is refused. The end belongs to the
timer, not to state 96. That routine is skipped for an `IsHorny` kind,
and Horny is immune anyway.

**Every reader of `+0x44B`.** Writers: the constructor, the countdown and process
15. The two dword stores at that offset are a creature definition and a key-binding
table. The overlapping accesses were found but not typed:

- dword reads at `+0x449`: `0x004A3806`, `0x004A38DE`, `0x004C1219`, `0x004D671C`,
  `0x004DA2D0`, `0x004DD01A`, `0x004E3716`, `0x004E376B` and `0x004E56FF`, all
  through `[thing+0x19]`, which looks like a player pointer;
- dword stores at `+0x449`: `0x004EAC18` and `0x004EBDAE`;
- word reads at `+0x44A`: `0x00548C1A` (base unknown) and `0x004EBDD0`.

None reads as a creature access, but none was proven not to be one, and block
copies were not checked.

Readers:

- The fight gate `0x0045A6D0` refuses a chicken, so **it never looks for a fight**
  (`combat-acquisition.md` §2).
- The fear accumulator `0x0045E4F0` returns at once for a chicken, so it neither
  fears nor spreads threat (`combat-fear.md` §2).
- The attackability recompute `0x0045A8B0` does **not** read it, so **a chicken can
  still be attacked**.
- The Combat Pit finder `0x004D8100` gives a chicken no opponent and skips a chicken
  as one.
- Target selector 6 (`0x00517AB0`, Possession) refuses a chicken.
- The animation chooser `0x0048BE50` asks world slot `+0x188` (`0x0050DB20`, a
  table looked up by id) for record 9. Object 9 is the Chicken, so the creature
  most likely takes the Chicken object's art. This is presentation, and the
  identification is STRONGLY INFERRED.
- A `Possessed` creature's update posts message `0x15` to its owner (`0x00489FA6`).
- The dumper (`ChickenTimer`).

No speed, damage or health routine reads it. That is a statement about this field
census only.

## 4. Inferno

**The shot.** `0x004BBD30` checks permission and mana. When the row's `Upgraded`
byte is set it takes the upgraded shot byte as well as the upgraded data. The
creature path keeps the normal shot byte, but Inferno's two shot bytes are both 15,
so the difference does not arise here. It creates shot 15 at the given position with heading 0, writes data1
and data2, and sets the aim point `+0x131` to the same position (`0x004AB830`,
which also moves the attached effect). It then pays as `keeper-spell-casting.md`
§2 says. Nothing launches the shot, collide type 0 has no finder, and so **it never
moves**.

**Its life.** Process event 0x2 runs before collision on every tick while health
is above 0. Whichever way physics returns, the countdown is reached. Flying, it
goes straight there. Stopped, which is what `shots.md` §6 gives a shot slower than
0x80, it takes the solid-hit path: the hit-solid effect and sound, then event 0x8,
whose clear bit returns 1 (`0x004AD795`). Without `Flags` bit 0 the routine then
**falls through to the countdown** (`0x004AB4B6..0x004AB4DF`). Either way health 30
drops by one every tick. **Process 30
runs at ages 1 to 30**, exactly 30 times, and at age 30 the death path runs.
(`shots.md` §4 step 7 said the tick ends after a solid hit; it ends only when
`Flags` bit 0 is set.)

**Process 30 (`0x004AF660`), each run:**

1. `R = data2` (shipped 1). The bound is `((R << 12)²) >> 12`, which is R² tiles² in
   4096ths.
2. Calls `0x004B32A0(owner, def+0x52, R)` with the raw data2. With R = 1 its bound
   is zero, so this does nothing (§5).
3. Only then, on the shot's first tick (age `+0x12D` = 1), halves the bound
   (rounding toward zero), to a radius of about 0.71 tile.
4. Walks tiles from `y−R` to `y+R` and, within each row, `x−R` to `x+R` around the
   shot's tile. The lower bounds are clamped to 0 and the upper bounds to the map
   width and height through world slots `+0x1CC`/`+0x1D0`. The upper clamp is
   inclusive, so x = width reads tile (0, y+1), and y = height reads past the array.
   Shipped maps are bordered by rock, so this never triggers. A build should clamp to
   width − 1 and height − 1.
5. Skips a tile whose navigation-solid bit is set. That is world slot `+0x208`
   (`0x00509200`, navigation grid bit 3 at a tile), not the `+0x20C` the location
   validator uses.
6. Walks each tile's whole thing list (head at tile `+0`, next at thing `+0xC`). Each
   **creature** (class `+0x12` = 0) passes **unless its kind has `CanWalkOnLava` and
   lacks `CanFly`**.
7. A passing creature is hit if `dx²>>12 + dy²>>12` is below the bound (an unsigned
   compare on x and y only, with **no height test**). A hit:
   - sets burning (`0x0048EBD0`: `+0x469` = 1 tick, `+0x46B` = the owner tag),
   - then calls `Thing_ModifyHealth(−data1, owner, 0)`.
8. Posts bridge command 10 with the owner's tag (`0x00517480`). Its handler
   `0x004483A0` adds 1 to a counter at `CBridge+0x132B` when the tag is the bridge's
   own player. That is presentation, STRONGLY INFERRED.
9. Returns 1, so the tick goes on.

**No owner test.** The health routine refuses a dying, `IsHorny`,
`OnlyAttackableByHorny` or held creature and applies its multipliers: stunned
double, frozen ×5/4, armour or possession half, and the owner byte `+0x1095` at
−8 per point (`creature-status-effects.md` §3). On every call it also stamps the
source at `+0x36` (the caster's player word), and `+0x38/+0x3C`.
It tests no owner either. So **an Inferno burns the caster's own creatures, its
allies' creatures and neutrals alike**. **CONFIRMED** by the owner's own cast
(2026-10-01): it hurt their own Goblins and Fairies and Fireflies, and left
Salamanders unharmed; a second cast is refused for a while after the first, the
cooldown of §1 (its 60 s not timed). A creature that stays within one tile takes
up to 30 × 100 = 3000 before multipliers (3750 upgraded), or 29 hits if it stands
between about 0.71 and 1 tile away on the first tick. Infernos stack: only the
cooldown keeps casts from overlapping. Not read: whether a shot made at a flyer's
height can end early by falling to the ground (the physics `+0x84`/`+0x20` branch).
The hit test ignores z, so only the shot's lifetime is at risk.

Immune: Salamander, Giant, Horny and Unique Salamander (Horny is already refused by
the health routine). Firefly, Fairy and Unique Firefly burn despite `CanWalkOnLava`,
because they can fly. Burning is set even on a creature the health routine then
refuses. Its countdown is display only (`creature-status-effects.md` §1).
**Not involved:** the creature byte `+0x4D6` that the dumper names `SuckedByInferno`.
`Creature_Update` decrements it and the animation chooser reads it, but none of the
six stores the field scan found writes a nonzero value into a creature.

## 5. Corrections and the open edge

- `0x004B32A0`'s last argument is a **radius in 4096ths**, not a mode. Its bound is
  `radius × (radius >> 12)`.
  - Small radii make it zero: the literal 1 for a clear event bit; 2 at nine sites,
    among them the collision routine, Freeze, Lightning and Chicken; 3 at
    `0x004A34C0`, whose amount comes from `[ESI+0x152]`; and Inferno's data2 of 1.
  - **It is live elsewhere.** `0x004A84E0` pushes the literal `0x5000` (5 tiles) on
    one branch and `([ECX+0x70]+1) << 12` on the other; both reach one call at
    `0x004A8D09`.
  - The other register radii come from `0x004A0E30` (`[0x006F2934] << 12` on one
    branch), `0x004B0050` (raw data2) and Inferno's `0x004AF660`.

  Those callers are unread (Q397).
- `shots.md` §4 step 7 is corrected above.
- **UNKNOWN:** when the shot's first update runs relative to the cast. This is the
  same gap as Heal's (`keeper-spell-casting.md` §4).
- **UNKNOWN:** `+0xF2`.
- **UNKNOWN:** the original run (Q498).

**Built (ADR-0302):** `KeeperSpellRules` casts both (selectors 1·3 and 1·5);
`InfernoRules` is process 30; `ChickenRules` is process 15, state 96's handler and
the timer's end, with `CreatureState.ChickenTicks` read by the fight gate, the fear
accumulator and the Pit finder; `ChickenSystem` runs state 96; the battle step casts
both from captured permissions. Approximations: the Hatchery is measured from its
centroid tile rather than `NavigationPos`, and the tile box clamps to the last
column. Not modelled: burning, the source stamp, bridge command 10, the chicken art
and `+0xF2`. Level 18's Nemesis casts both unaided
(`scenarios/keepers/level18_nemesis_inferno_chicken.yaml`). Inferno needs a stationary 30-tick
shot with process 30. The battle step's predicted permission for Chicken is
Thunderbolt's location test with target 3, and for Inferno the location test alone,
at the target's position.
