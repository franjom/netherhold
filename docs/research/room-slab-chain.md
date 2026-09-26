# A room's slab chain: the order its tiles are in

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-26.
- Answers Q439. Owner observations 2026-09-26: two Prison screenshots (minimap north up), the
  wave's first tiles at the tile released, and a door that moved on a reload.
- Related: `room-list.md` and `room-object.md` (`FirstSlab`/`LastSlab`, the 16-byte tile record),
  `jail-break.md` §1 (the Prison door), `combat-pit-spectators.md` (the pit finder).

## Result in one sentence

A room's tiles form a singly linked chain, and a tile joins it **at the tail, in the order it was
built**. The chain therefore depends on:
- how the room was built: the drag's corners, or the order of single-tile clicks;
- the order rooms merged in;
- the replay a sale performs.

The chain does not depend on the camera.

## 1. The chain

- The room holds `FirstSlab` (`+0x2D`, `+0x2E`) and `LastSlab` (`+0x2F`, `+0x30`).
- Each tile's 16-byte record holds the next tile's coordinates at `+0x04`/`+0x05`. (0, 0) ends
  the chain.
- `+0xA7` is the count.
- Room tile walks follow these links, sometimes after advancing to a random start. §7 lists them.

## 2. Building: the action and its wave — CONFIRMED

**Action `0x2F` (build a room), `0x00512850`**, calls `0x004C6EF0(type, x1, y1, x2, y2)` on the
player. The corners are the packet's words at `+4`, `+6`, `+8` and `+0xA`, passed signed and
**not normalised**.

**`0x004C6EF0`** queues the build in the first free one of three slots at `player+0x1C2`, each
`0x19` bytes:

| Offset | Field |
|---|---|
| `+0x1C2` | room type |
| `+0x1C3` | x1 |
| `+0x1C7` | y1 |
| `+0x1CB` | x2 |
| `+0x1CF` | y2 |
| `+0x1D3` | the cost accumulated so far (word) |
| `+0x1D5` | a word, zeroed |
| `+0x1D7` | the turn (`0x00517490`) it was queued |

- The pre-check `0x004C5A20` runs over the normalised rectangle first.
- When it fails, the cost exceeds the player's gold (`player+0x7E`) and the rectangle's area is
  over 2, message `0x37` is posted.

**The wave, `0x004C7380`** (its thunk is `0x004C7330`), for each queued slot:
- `e` = turns elapsed since `+0x1D7`.
- The tiles built on that turn are those with `|x − x2| + |y − y2| = e`, clamped to the map and
  to the rectangle. **The wave starts at (x2, y2) and advances one diagonal per turn.**
- Within a diagonal, x runs from x2 toward x1. The x step is +1 if `x2 ≤ x1`, else −1; y steps
  the same way.
- Each tile gets the placement test `0x004C5A20`, then `buildRoom` `0x004C5C30`.
  - The two bridges and the hero bridge (types 8, 17, 22) test the tile against a per-rectangle
    map `0x004C57F0` instead.
- A refusal of −1 ends the slot at once.
- The slot empties when the wave reaches (x1, y1). `0x004F04F0` then clears the rectangle's
  highlight, and the accumulated cost is shown at the last tile (`0x0050FB70`).

**Single-tile building is the same action with x1 = x2, y1 = y2**, a one-tile wave. A room built
click by click is therefore built in click order.

**Which corner is which — CONFIRMED (owner, 2026-09-26).** The packet carries (press, release),
and the wave runs from the release corner back to the press corner. The owner saw a drag's first
tiles appear at the tile released, and the door (§5) agrees.

## 3. Adding a tile: join, create or merge — CONFIRMED

`buildRoom` calls `0x004ED1A0`, which calls `0x004ED320(x, y, type, owner, allowance)`.
- **`0x00450840`** asks whether any orthogonal neighbour is a room of that type whose tile owner
  (`+8`) is the builder.
- **None: a new room.** This needs the allowance, `0x60 − player+0x10F9` (at most 96 rooms a
  keeper). `0x004EC5A0` sets `FirstSlab = LastSlab =` the tile.
- **Otherwise it joins, in `0x004EC650`.** The neighbour table `0x006BC350` is **N (0, −1),
  E (1, 0), S (0, 1), W (−1, 0)**, and the first neighbour passing `0x0044F5D0` (same room type,
  same tile owner) names the room joined.
  - The tile is written into the old `LastSlab`'s link, and becomes `LastSlab`. **Append at the
    tail.**
- **Then it merges.** Each later neighbour in that order, belonging to a different room, is
  merged into the joined one by `0x004ECB70`:
  - the absorbed room's tiles are relabelled;
  - its chain is linked after the tail, and the tail becomes its `LastSlab`;
  - the counts add;
  - the absorbed room is freed (`0x004ECD00`).

  So a tile that bridges two rooms gives: the joined room's chain, then the tile, then the
  absorbed room's chain.
- **A forced join.** When `world+0x9D4` is set, the tile joins the room id at `world+0x9D8`
  with no merge (§6).

After every insert, `0x004ED1A0` runs the room's rebuild `0x004EDFC0`, which **recomputes the
Prison door** (`jail-break.md` §1). The door can therefore move while a room grows.

## 4. Selling a tile: the chain is replayed — CONFIRMED

**Action `0x31`, `0x005128F0`**, calls `0x004C70A0(5, x1, y1, x2, y2)`. That queues a sell in
three slots at `player+0xE97`, laid out as in §2.

**Its wave, `0x004C7160`**, is the same diagonal sweep from (x2, y2). It sells each tile with
`0x004C5DB0(x, y, 1)`, which calls `0x004ED440`, then **`0x004ED7B0`**:
1. It unlinks the tile, fixing `FirstSlab` or the predecessor's link and `LastSlab`.
2. It turns the tile back to ground.
3. It clears the room id on every remaining tile of the chain.
4. It frees the room.
5. It **re-inserts every remaining tile in its old chain order**, through the §3 insert. A tile
   with no re-inserted neighbour starts a new room.
6. It returns up to 32 resulting rooms. `0x004ED440` gives each a share of the old room's
   stored value, and rebuilds each.

So a sale that cuts a room in two leaves two rooms, each chain in its tiles' old relative order.
A sale can also reorder a surviving chain, where the replay merges pieces (§3).

## 5. Why the Prison's door follows the drag

`0x004E32C0` puts the door north of the **first** tile in the chain that meets both conditions:
- it is interior (`0x00454110` = 1, the cell's `+0xC` = `0xFF`; a Prison's rim fails);
- its N, NW and NE are not interior.

For a room dragged out as a rectangle:
- the chain runs in wave order from (x2, y2);
- the first interior tile in the top interior row is the one **at the x2 end of that row**;
- that is the east end when x2 > x1, and the west end when x2 < x1. The y corner never changes
  the column.

The owner's two screenshots show this: a 5×5 Prison in the same place, north up.

| Drag | Door |
|---|---|
| top-left to bottom-right | the east interior column (4th of 5) |
| top-right to bottom-left | the west interior column (2nd of 5) |

The earlier "default camera right, turned 180° left" was the same thing: a drag that looks the
same on screen runs the other way on the map when the camera is turned.

**A room built click by click, or grown, keeps its chain head.** The door is north of the
earliest-built tile in the top interior row. Because the rebuild runs on every insert, it can
also move when a later tile makes an earlier one interior.

## 6. Rooms that come with the map, and after a load

**The level file.** `0x0056D2D0` reads the KWD map chunk (tag 100, `0x65`/`0x66` headers):
- it goes **row by row, x inside y**;
- it calls the per-tile callback `0x0051EC60(x, y, record)`, registered by the KWD table
  `0x0051E0A0` at `0x0051E0F8`;
- for room terrain, the callback calls `buildRoom`.

A pre-placed room's chain is therefore its tiles in **row-major insert order**, with §3's merges
(a rectangle has none). Its Prison door is at the **west** end of the top interior row.

**A stream load**, `0x0044F4C0`:
- it reads the width and height;
- it reads each tile's 10 bytes in memory order (row-major) with `0x00452BD0`;
- for room terrain, `0x00452BD0` sets `world+0x9D4 = 1` and `world+0x9D8 =` the tile's saved room
  id, then calls `buildRoom`, a forced join (§3).

**It is the save-game load — CONFIRMED (owner, 2026-09-26).** A Prison dragged so its door was
in the east column was saved and reloaded, and its door moved to the west column. Every room's
chain comes back row-major. Rooms keep their saved ids, and the forced join makes no merge, so
each room's chain is its tiles in row-major order.

## 7. What else reads the chain

These readers traverse the slab links; most start at `FirstSlab`, while the
random-start readers enter the same chain at a chosen tile:

| Reader | Address | Doc |
|---|---|---|
| Prison door | `0x004E32C0` | `jail-break.md` §1 |
| Combat Pit finder's distance, to `FirstSlab` | `0x004D8510` | `combat-pit-spectators.md` |
| Pit route point: the first rim tile | `0x004E5000` | `verification-2026-09-26.md` §4 |
| A random room tile: `random(Size)` steps, then on while refused | `0x004E4D90` | `hero-party-walk.md` |
| Imp room point | `0x004E8430` | `imp-idle-and-wandering.md` |
| Wall-furniture phases (`+0x92`/`+0x96`): parity of the first eligible slab | `0x004ECEB0` | `auto-placed-furniture.md` |
| Hatchery: the first chicken per tile, distance-ranked | `0x00463500` | `eating.md` |
| Carrier's gold target: random chain tile, then onward and wrap to a tile with room | `0x00464400` | `rooms-and-gold.md` |
| Lair acquisition: local candidates, then random chain tile and onward | `0x004E0140` | `lair-acquisition.md` |
| Workshop crate free tiles: counted along the chain to place each crate | `0x004F5040` | `workshop-crates.md` |
| Prison food search (distance-ranked; order breaks ties only) | `0x00473550` | `prison.md` |
| Fog of a 3×3/5×5 room revealed along the chain | `0x004E8B80` | `fog-reveal-sources.md`; order cannot change the final bit set |

The eight migrated readers and the fog walk were re-decompiled for Q441 on
2026-09-26. They follow each tile's `+4/+5` link. Gold and lair start at a
random chain tile and wrap; the other readers start at `FirstSlab`. The Workshop's
*capacity* (`0x004F6140`) is a count of
objects and tiles, so chain order cannot change it. Gold capacity is likewise a
sum; only selection of the deposit tile is order-sensitive. Fog sets a discovery
bit on every tile, so its order cannot change the resulting fog state; event
ordering is outside the current fog model.

## 8. Ours

Before ADR-0240, ours kept no chain. `RoomIndex` rebuilds rooms from the map, and
`RoomInstance.TileIndices` is ascending tile index, which is row-major. That is exactly DK2's
order for a rectangular pre-placed room, and wrong for any room a player builds. `JailBreakRules`
walked each row from the right, which is DK2's result for one drag direction only.

ADR-0240 records the chain order. §9 says what was built.

## 9. Built (ADR-0240)

- `TileState.SlabOrder` and `WorldState.NextSlabOrder` record each room tile's place in its
  chain. `SlabChain.Order(world, room)` returns the tiles by (`SlabOrder`, tile index).
  - Tiles with order 0, the map's own, therefore come in row-major order, as §6 loads them.
- **The §3 insert:** `SlabChain.Added`, on the N, E, S, W table. It:
  1. takes the room of the first neighbour of the same terrain and owner;
  2. appends the new tile;
  3. appends each later neighbour's different room;
  4. restamps the whole chain.
- **The §4 replay:** `SlabChain.Removed`. It takes the old room's remaining tiles in chain order,
  re-inserts them one by one through the same rule, and restamps each resulting room.
- `BuildRoomCommand.Rectangle` and `SellRoomCommand.Rectangle` keep the corners and list the tiles
  in wave order from the second corner.
- ADR-0243 switches the order-sensitive §7 readers: pit route point, imp room
  point, wall-furniture phase, chicken and prison-food ties, gold deposit tile,
  lair search, and Workshop crate placement. Each has a dragged-room regression
  that fails when its reader is temporarily returned to index order.
- Fog and capacity counts require no order change (above).
- **A loaded game:** `WorldSnapshot.LoadGame` zeroes every order (`SlabChain.AsLoaded`), which is
  DK2's row-major re-chaining. `FromBytes` stays an exact checkpoint (ADR-0241).

## 10. Owner changes and room count — STRONGLY INFERRED

The room damage path `0x00497BF0` calls `0x004E5300`; when the room is taken,
`0x004E5650` relinks the *room* in the new player's list, retags its owner, and
walks the existing slab links to update tile ownership. It does not relink the
slabs. Keeper defeat `0x004DAA10` uses that same `0x004E5650` for rooms passed
to neutral. Ours keeps `SlabOrder` in both cases, as DK2 does.

`0x004ED320` refuses a new disconnected room when the keeper already has 96
rooms (`0x60 − player+0x10F9`). This is an observable construction limit and
should be modelled with a dedicated rejection test, but it is outside Q441's
reader migration. It does not affect joins to an existing room.
