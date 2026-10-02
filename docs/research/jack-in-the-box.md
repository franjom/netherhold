# Mechanic: the Jack In The Box — the trap's shot, the Jack and its explosion

Source(s): DKII.EXE v1.70, pyghidra-mcp listings and decompiles on 2026-10-02; the shipped
`Traps.kwd`, `Shots.kwd` and `Creatures.kwd` (`JackInTheBoxOnRealDataTests`).
Original version: DK2 v1.70 (GOG)
Confidence: STRONGLY INFERRED (binary, static). No shipped level places the trap (traps.md §13),
so nothing here was seen in play (Q521).
Implementation: `JackInTheBoxRules`, `JackInTheBoxSystem`, `TrapRules` (trap 16's data word),
`ShotRules.AreaDamage` (ADR-0334).
Tests: `JackInTheBoxTests`, `JackInTheBoxOnRealDataTests`.

## 1. The trap and its shot

Trap 16: tile selector (wants a worker, traps.md §5), range 1, recharge 4 ticks, no mana, health
8000, shot 64 with data words 10000 and 2. Fired at a target, `0x004A7FD0` launches the shot (flags
`0x24`) and, for trap 16 alone, writes the **target's 16-bit thing tag** over its second data word
`+0x141`. Through the Trigger the trap fires at the Trigger's creature unseen (traps.md §9).

Shot 64: health 1, process events `0x40` (first tick), process 39, collide type 2, **damage type 2**
(area, shots.md §7) with `AttackCategory` 13. The first tick's process result is not looked at
(`0x004AB281`): the tick goes on to the collision and the area damage, whose radius is that second
word — the victim's tag, in tiles.

## 2. Process 39 (`0x004B02E0`)

1. The world's spot test (vtable `+0x200`) at the shot, else nothing.
2. `0x004B32A0(owner, +0x52, 2)`: a threat at radius 2, which counts nobody (shots.md §8).
3. With `+0x141` nonzero: the thing of that tag; if it has a player (`+0x2A`) and
   `Player_IsEnemy` (`0x004BF390`) of the shot's player says so, it takes `+0x13D` (data1, 10000)
   through `Thing_ModifyHealth` with the shot's owner, and its owner word `+0x28` is kept.
4. Only with that owner: a creature of type `0x2E` (the Jack) of the shot's owner at the shot
   (`0x004B78D0`); the first class-3 thing on the tile destroyed (the trap is spent);
   `0x0048DFA0(0)` and `0x0048DF10(0)` clear its `+0x46F` bits 3 and 0; `SetState(273)`
   (`0x0048AD30`); `+0x2F5` := the victim's owner. Returns 0.

`+0x2F5` is the state machine's `+0xA7` word (`sgmap`), the one the alarm keeps its threat in.

## 3. State 273 (`0x0046E820`)

Category 7, group 3, flags 68 (`creature-state-table.md`); category 7 has no enter, update or leave
hook (`state-categories.md`). Each tick:

1. `Thing_ModifyHealth(−1)` on itself, its owner the source.
2. The heart position of the player in `+0x2F5` (`0x004C5F50`, player `+0xB43`, which
   `0x004C5F60` sets from the heart object's tile: the heart's centre; `(1, 1)` before a heart).
3. On tile `(x+1, y+1)`: `Thing_ModifyHealth(−50)`, return 1.
4. Else `0x00471890(&point, 273, 0)` to `((x+1)·4096 + 0x800, (y+1)·4096 + 0x800)`: a walk in
   state 6 resuming 273, no door rule, no tunnel; a failed route leaves it in 273. Return 1.

The travel state's handler `0x00470C80` charges a creature of type `0x2E` one more point each tick
it walks. So a Jack loses a point a tick wherever it is and fifty at its target. Tile `(x+1, y+1)` is
a corner of the heart's raised 3×3, which ours does not route to; whether DK2's router reaches it
is not read, and the walk's real end is Q521's.

## 4. The explosion

`Thing_ModifyHealth` on a creature, when its death query (vtable `+0x28`) answers yes after the
change and its type `+0x36F` is `0x2E` (`0x004B3108`), makes shot `0x41` (65) at it, of its owner,
with its heading. Shot 65: health 5 (flags `0x104`, counting down, never launched), damage type 2,
data words 20000 and 3, `AttackCategory` 4: five ticks of 20000 falling off over three tiles to
every thing not of the Jack's owner (shots.md §7). The dying bit is not set for a creature without
a body (`0x0048B770`), so a dead Jack hit again before its removal explodes again unless the routine's
vtable `+0x74` gate refuses it (unread).

## 5. The area damage's arithmetic (`0x004AC6D0`)

`R = data2 << 12`, `R² >> 12` (32-bit, arithmetic shift), compared unsigned against the squared
distance; the damage `((data2 << 12) − isqrt(d² << 12)) · d / R`, the shift and product 32-bit and
the division signed; `0x0065FAE0` is an unsigned integer square root. From 12 tiles `R²` overflows:
the limit is then `(data2² mod 256) << 12` read signed, so a byte of 128 or more reaches everything in
the box and a smaller one a few tiles; 16 gives 0 and hurts nobody. For shot 64 the radius
is a thing tag, so its first strike's reach turns on the victim's slot.

## 6. Built and not built (ADR-0334)

**Built:** §1's data word (ours writes the target's creature id: we hold no thing slots, so the
first strike's reach differs from DK2's); §2 for a creature victim; §3; §4, once per Jack, at its
next update when something other than its own wear killed it; §5's 32-bit arithmetic for every area
shot, a wrapped negative damage skipped where DK2 would heal. **Not built:** a victim that is not a
creature (a fired-without-target shot names thing 2; ours strikes nobody); the `+0x46F` bits; effects and sounds.

## Open questions

Q521 (the predictions in play), Q450.
