# Bridge placement, and the computer keeper's bridges

**Scope:** `DKII.EXE` v1.70, MD5 `2f3bbc21eae8c3aa468136baa303036d`; read 2026-10-01 with Ghidra
12.1.4 (Claude's clone), disassembly checked for receivers, argument order and `RET` sizes. Facts
are in our own words; no decompiler text is reproduced. A binary read is **STRONGLY INFERRED**
until a running game confirms it. What a bridge reverts to and how one burns: ADR-0031/0032.

## 1. The placement predicate, 0x0044F630

World vtable `0x0066E3EC` slot `+0x22C` is `0x00509280`, which adds `0x9DA` (the runtime map,
`runtime-map.md`) to the world pointer and forwards `(x, y, playerTag, roomType)` to
`0x0044F630` (`RET 0x10`). It returns 1 when the room may go on that tile:

1. The tile's terrain id (definition byte `+0x1C4`) must not be 31–34 or 37 — on shipped data
   Mana Vault, Claimed Vault and the three Hero Gates — and the tile's room word (`+0x02`, low
   twelve bits) must be zero: no room there already.
2. **Water** (terrain flags `+0x17B` bit `0x80`) when the room's flags (`+0x43D`) carry bit 0,
   PlaceableOnWater; **lava** (bit `0x100`) when they carry bit 1, PlaceableOnLava. Either needs
   `0x00453B30` (one of the four orthogonal neighbours is the player's own, Ownable, non-Solid
   tile) and no obstruction from `0x0044F800`.
3. Otherwise **land**: the tile's owner (`+0x08`, low twelve bits) is the player, world slot
   `+0x208` returns 0 for the tile, the room carries bit 2 (PlaceableOnLand), and no obstruction.

A water or lava tile that fails its arm — no room bit, no own floor beside it, or an obstruction —
falls to the land arm and fails on ownership, since nobody owns a hazard. Shipped data
agrees with the bit reading: both bridges carry `0x1083` (water, lava, buildable), Water and Lava
carry `0x80` and `0x100`, and both bridge terrains `0x34` (Ownable, not Solid), so an owned bridge
is own floor for the next span (`kwd roomflags`, `kwd terrainflags`).

**Obstruction, `0x0044F800`** (`RET 0xC`; its third argument is never read): it walks the tile's
thing list and refuses on any thing of class 3 or 4 (door and trap, HYPOTHESIS from the classes'
use elsewhere), or a creature (class 0) whose kind byte `+0x36F` is not 1 (the Imp), whose owner
`+0x28` is not the player and whose `+0x3E6` bit `0x200000` (dying, `payday.md`) is clear. Not
built (Q501).

Every room build reaches this predicate: `0x004C5C30` unless told to skip it, the drag pre-check
`0x004C5A20`, the span map `0x004C57F0`, keeper tasks 3 and 7, and `0x00442960` (instruction scan
for calls through `+0x22C`, both code blocks).

## 2. A dragged span, 0x004C5A20 and 0x004C57F0

For rooms 8, 17 and 22 the drag's pre-check clears the per-rectangle map at `0x00746EC0` and
fills it with `0x004C57F0`: first every rectangle tile that is water or lava the room may span
and that the predicate accepts; then, repeatedly until a pass marks nothing, every unmarked such
tile with an orthogonal neighbour inside the rectangle already marked. The wave (`0x004C7380`,
`room-slab-chain.md` §2) rebuilds the map each turn and calls the builder for a marked tile with
the predicate skipped. So a dragged span grows from whichever of its tiles touch own floor,
through connected hazard inside the rectangle, whatever corner the wave starts from; the
obstruction test is applied to the seed tiles only. For one tile the map is the plain predicate.

## 3. The builder and the keeper's wrapper, 0x004C5C30 and 0x004C5B40

`0x004C5C30(x, y, room, turn, bypass, skipTest, 8)` on the player: unless `bypass`, it refuses
when the room's per-tile price (`+0x43F`) exceeds `Money` (`+0x7E`); unless `skipTest`, it refuses
when the predicate does; it builds through `0x004ED1A0` with the allowance `0x60 − (+0x10F9)`
rooms (`room-slab-chain.md` §3); on success, unless `bypass`, it debits the price and adds it to
`+0x177`.

`0x004C5B40(x, y, room, a4, a5)` (`RET 0x14`) passes `bypass = 1` when one tile's price exceeds
`Money`, else `a4`, and `skipTest = a5`, bracketing the call with `0x004F0390`/`0x004F04F0` on
world `+0x9BE` (role UNKNOWN). Its only callers are tasks 3 (`0x004FD2B0`) and 7 (`0x004FDC50`)
(rel32 scan of every executable block, `.text` and `cseg`). **A keeper's tile it cannot afford
is laid free**; a human's is refused.

## 4. Task 7 on water and lava, 0x004FDC50

Each pass spends one allowance unit on the cursor's own tile, in this order: cursor at the target
ends the task; with the attack nibble set, enemy land in the 3×3 stores the tunnel
(`computer-keeper-management.md` §6); then the cursor's terrain (world slot `+0x6C`):

| terrain | bridge asked for |
|---|---|
| water (`0x80`, tested first) | room 8 when `0x004BAB20(8)` is 3; else room 17 when `0x004BAB20(17)` is 3; else the task ends |
| lava (`0x100`) | room 17 when its availability is 3; else the task ends. Never room 8, whose flags allow it |

Then the predicate (`ECX` the world, `(x, y, tag, room)`) and `0x004C5B40(x, y, room, 0, 0)`
(`ECX` the player) must both succeed, and the walker `0x00502230` steps **in the same pass**;
its zero return ends the task. A refusal by either retries the same tile on the next pass, each
spending allowance, so a refused span spends the rest of the turn. A refused Wooden Bridge is
retried as wood, never as stone. Every exit writes `Task = NextTask` and leaves `NextTask` at 4.
Gold is never checked here: the wrapper's bypass lays the span free when gold is short.

The walker returns 0 at the target without moving the cursor and on a hug that loops back;
otherwise it moves the cursor onto the next diggable wall, water or lava tile (straight walk
stops at terrain bits `0x81` or `0x100`), or along the hug (`computer-keeper-tasks.md` §5).

## 5. Stock maps

Merged VARIABLES availability for each map's computer keepers (`AvailabilitiesOf`, global then
level rows), 2026-10-01: Level 11A, 11B and 11C (624–631 lava tiles), Level 17 and TrapTime give
both bridges at 3. Levels 10, 12, 13 and 18 give both at 4, Level 7's Gormagon wood 4 and stone
none; 4 is not 3, so those keepers cannot bridge at the start. Level 11A's triggers change no
bridge availability (`kwd triggers`).

**Ours on Level 11A, not original evidence** (`level11a_drako_attack.yaml`, seed 7): Drako tags
(64,41) at tick 6, lays Stone Bridge at (64,40) on tick 62 and (64,39) on 63 (DK2 66, 70), tags
(64,38) beyond the lava on 64, casts Call to Arms at (64,34) beside Morgana's heart and its
creatures engage that heart. Before the bridge branch the same run stopped at the lava and cast no call.

## 6. Ours, and where it differs

`RoomRules.CanBuild` applies the predicate's terrain-id refusal and its water, lava and adjacency
arms to every `BuildRoomCommand`; a bridge order of several tiles is marked whole first
(`SpannableTiles`), as §2. `KeeperAI.DigPath` follows §4 through `BuildRoomCommand`; the walk after
a laid span follows at once, costing no allowance, as in DK2's pass. Differences, ADR-0304: the
span lands on the next tick, so at most one span a tick where DK2 may lay one a pass; a span the
keeper cannot pay for waits for gold rather than being laid free; the obstruction test is not
built; the land arm asks terrain solidity, not world slot `+0x208`; a hug that loops back
abandons the task where DK2 takes `NextTask`.

## 7. Open

- Q497: whether the original Drako crosses the lava and calls at Morgana's heart as ours does.
- Q501: the obstruction test, and whether classes 3 and 4 are door and trap.
- `0x004F0390`/`0x004F04F0` around the keeper's build; what `0x00442960` uses the predicate for.
