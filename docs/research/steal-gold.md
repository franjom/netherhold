# Stealing: job 13 and states 160–172, 226

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25.
- Built: ADR-0219, `StealGoldRules`.
- Neighbours: `hero-party-walk.md` §1 (the dispatcher and arm 13), `payday.md` (the same gold take), `travel-states.md` (the route helpers).

## Result in one sentence

A hero with job 13 walks to the first keeper's nearest Treasury and takes what its purse can hold from the whole room, of which it keeps half. It then walks back to the tile it was made on and vanishes with the gold. A party leader recruits the followers trailing it.

## 1. The arm, `0x00481FA0` — CONFIRMED

- **The player.** It comes from `0x00462660`: the job's player word `+0x40D` as a tag, or with none, the strongest player (`0x0050EA20`).
  - Unlike arms 18 and 19, the choice is **not written back**.
  - The player must differ from the creature's owner (`+0x4` tags, `0x00481FB4`).
- **The room.** `0x004C61C0(1, position, &room, 0)`, with `ECX` the target player (`0x00481FCE`), finds that player's Treasury nearest the creature.
  - Distance is to the room's first tile centre (`room+0x2D/+0x2E`), as `(dx² >> 12) + (dy² >> 12)`, unsigned and strict, so the first room wins a tie.
- **The route.** `0x00471740(room, 3)` tests it; then `SetState(163)`.

## 2. State 163, `0x00482010` — CONFIRMED

- **The raided player.** `ECX = [world+0x84] + 0x224A` is the third record of the player array. `0x004C1E20` builds 7 records of `0x1125` and writes id byte `+0x10FC` as index + 1, so this is **player 3, the first keeper**.
- **Its own creatures refuse.** A creature of that player's fails (`0x0048202E`).
- **Otherwise it walks.** Its nearest Treasury is found by the same finder, **on player 3, not the job's player**. The walk is `0x00471A70(room, 164, 0x69)`, and sound `0x100` plays on success.

## 3. State 164, `0x00482190` — CONFIRMED

- **Whose Treasury.** The player is the creature's owner, or for a hero (`+0x28 == [0x006F254C]`) the job word `+0x40D`. `0x004539B0(1, player, 0)` needs the tile underfoot to be a Treasury owned by that player.
  - So a hero whose job names another player, or none, fails here after 163 has walked it to player 3's gold. It loops through idle, 90 and the arm. **Reproduced.**
- **The gold.** The walk goes to the first gold object in the room's list (`+0x12C == 3`) that routes, `0x00471890(pos, 165, 0x68)`.
- **A leader recruits.** A leader (`+0x46F & 2`) then scans its owner's list for followers of its party (`+0x417`, bit 4) that are in 222 (`+0x25E`) or walking back to it (`+0x32D`). Each gets `0x0048BE20(13, leader's +0x40D, 0)` and `SetState(226)`.

## 4. State 165, `0x004824E0` — CONFIRMED

1. **The event.** Creature event 13 (`0x0049AA30(0xD, 0, 0)`).
2. **The gold underfoot.** The first thing list of the creature's cell is scanned (the loop runs once) for an object (`+0x12 == 2`) of kind 3.
3. **The take.** What the purse lacks is `MaxGoldHeld` (level-scaled, `0x006E4154 + 0x13B`) minus `+0x3FB`. That amount is taken by `0x004F4510` on the cell's room.
4. **Half is kept.** `0x0048AE70(taken >> 1)` credits half.
5. **The walk out.** It routes to the tile `+0x404/+0x406` with `0x00471890(centre, 169, 0)`. The gold is already gone if that route fails.
6. **No gold.** `SetState(164)`, then return 0, so the dispatcher sets 1.

**`0x004F4510` is a ring over the whole room.** It starts at the given tile. On each tile it takes from the first gold pile of the room (`+0x138 == room`) and moves to the cell's link (`cell+4`). A zero link wraps to `room+0x2D`. It stops back at the start or when the amount is met (`0x004F464D`–`0x004F4680`). Payday's state 189 calls the same routine (`0x004727B4`), so **payday does not take from one tile only** (Q435).

## 5. State 169, `0x00482DB0` — CONFIRMED

1. **The purse.** `0x0048AF50(GoldHeld)` empties it.
2. **The creature goes.** A carried thing (`+0x3C3`) is dropped and destroyed. `+0x3E6` bit 5 (`DeadBodyOnDestroy`) is cleared, and the creature is destroyed (vtable `+0x20`).
3. **The effect.** Unless its definition has `AvailableViaPortal` (`+0x686` bit 10), world vtable `+0x38C` makes effect 15 at its position.

**`+0x404/+0x406` is the origin tile.** Its only writer is the constructor `0x004872A0` (`0x004875DA`, `0x004875E7`), from the creation position `>> 12`.

## 6. State 226, `0x004787F0` — CONFIRMED

It is `0x00478800(+0x409)`: the standing job, run once through the dispatcher.

## 7. The other steals, read and not built

| states | arm | room | object | then |
|---|---|---|---|---|
| 160–162 | `0x00482690` → 160 | 6 (`0x004C6A00(6, 1, …)`) | `+0x12D` flags `& 8`, `0x004E1590`; or `& 0x10`, a crate out (`0x004F5860`) | carried (`0x0048B350`), walk to origin, 169 |
| 166–168 | `0x00482A60` → 166 | 10 (Workshop) | `& 0x10`, a crate out | carried, walk to origin, 169 |
| 170–172 | (not traced) | the owner's own Treasury, found by `0x004C6930` and `0x004BF390` | gold as 165 | walk to a room from `0x004C6930`, or to the heart, then 172 |

- **The same quirks.** 160 and 166 carry the same player-3 test as 163. Their leaders recruit with jobs 14 and 17.
- **State 172** (`0x00482EB0`) spends 75 % of the purse and drops what it carries.

## Differences in ours

- **The ring's order.** `TakeFromRoom` walks `TileIndices` in index order from the tile underfoot, where DK2 follows each cell's link.
- **Tile gold, not objects.** Treasury gold is tile gold (ADR-0015); a tile with gold stands for a gold object.
- **Room points.**
  - The feasibility point (`0x004E5000`, unread) is the room's first walkable tile.
  - The walking point (`0x004E4C10`) is a random tile, then the next walkable one; its four cell tests are unread.
- **Not built:** sound `0x100`, and destroying a carried thing, since no carried steal is built. Option bit 0's tunnelling is built (ADR-0222).
