# The Temple's states: 101–107

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-10-06.
- **Built:** ADR-0398, `TempleStep`. The prayer itself is the category's update hook
  `0x0047B970` (`job-selection.md`, ADR-0186), which runs under all of these but 107.
- State `+0xB0`/`+0xB4` is where 105 last saw the creature. `Age` is state `+0x1C`.
- Handlers from the state table (`creature-state-table.md`): 101 `0x00472930`, 102 `0x00472AB0`,
  103 `0x00472AC0`, 104 `0x00472D30`, 105 `0x00472E90`, 106 `0x00473010`, 107 `0x004730E0`.
  101–106 are category 12 (Temple); 107 is category 0.

## 1. How each is entered — CONFIRMED

A scan of every call to `SetState` (`0x00476D30`), `0x0048AD30`, `0x00491830` and the two walks
(`0x00471890`, `0x00471A70`) for the constants `0x65`–`0x6B`:

| state | entered by |
|---|---|
| 101 | the job and the room drop (the Temple's room-table slots, `portals-room-handler-table.md`); 102, 105 and 107 (`0x00472AB4`, `0x00472EBF`, `0x00473118`); the walks to a Temple (`0x00472913`, `0x00472D0E`) |
| 102 | nothing |
| 103 | 101 (`0x0047299F`), 104 (`0x00472D72`), 105 (`0x00472FF5`) |
| 104 | 103's walks (`0x00472C98`, `0x00472CBE`) |
| 105 | 104 (`0x00472E7E`) |
| 106 | the sacrifice reward `0x004F1780`, on the creature it creates (`0x004F1835`) |
| 107 | the Temple's room-type slots `+0x05`, `+0x09` and `+0x0D`, all `0x004F15D0` (`0x004F15EA`) |

The other hits of `0x68` and `0x69` are option pushes before walks to other states.

## 2. 102 is unreachable — STRONGLY INFERRED

102 is `SetState(101)` and return 1. No setter, wrapper or walk carries `0x66`, the room table's
`+0x19`/`+0x1D` give the Temple 101 (`portals-room-handler-table.md`), and no `+0xDF` store does
(`travel-states.md` §2.4). Not covered, as for Q415: a computed call and a state restored from a save.

## 3. 101, the way in `0x00472930` — CONFIRMED

At `Age` 1 the creature stops (`0x0048ECE0`). On a Temple tile (`0x00453A20(13)`):
- if the floor height at its position is 0 (`0x004545C0`, which answers 0 for any Temple tile
  `0x00454110` calls a floor slot, whatever the position: the pool), it is the offering: the creature goes, the offering is recorded
  (`0x004F1600`), and the recipes are matched (`0x004F1660`, `0x004F1780`; `sacrifice.md`);
  no match posts message `0x47`. It returns 0.
- otherwise, if the tile is a Temple of its own keeper (`0x004539B0(13, owner, 0)`),
  `SetState(103)` and return 1.

Off a Temple, or on another keeper's, it returns 0.

## 4. 103, the spot `0x00472AC0` — CONFIRMED

1. `n` is 8 when its tile is a floor slot (`0x00454110`), else 4.
2. If another creature stands on its tile (the tile's thing list, kind 0, not itself), the
   neighbour search below; found, that is the target.
3. Otherwise `random(100)` (simulation stream, line `0x96`): above 35, its own tile; 35 or less,
   the neighbour search.
4. **The neighbour search** `0x004E50F0(room, pos, n, 1, 1)` on the held room (`+0x3B9`): `r =
   random(n)` (simulation stream, line `0x6B5`), then for `k = 0 … n−1` the neighbour
   `(r + k) % n` of the four (`0x006BC350`: N, E, S, W) or the eight (`0x006BC370`: N, NE, E, SE,
   S, SW, W, NW) that is a tile of the same room, not solid (terrain `+0x17B` bit 0), not a floor
   slot, of the same nav class as its own (`0x00455000`), and with no creature on it
   (`0x00453670`). The first is the target, its tile's centre.
5. **A target.** The bearing `a` from the room's centre tile (`+0x27`/`+0x29`) to the target tile,
   both at tile centres (`0x0065FB54`), and the stand point is the target tile's centre plus
   `(sin(a) << 10) >> 16`, `−(cos(a) << 10) >> 16`: a quarter tile further out from the centre.
   A walk there resuming 104 (`0x00471890`, options `0x80`).
6. **None** (the search failed): a walk to its own position resuming 104.
7. A walk refused: `SetState(1)`, then the Temple finder `0x004F1A20` and a walk to the Temple it
   names resuming 101 (`0x00471A70`); 1 if that walk is taken, else 0.

## 5. 104 and 105, the prayer — CONFIRMED

**104** (`0x00472D30`):
1. Another creature on its tile: `SetState(103)`; taken, return 1.
2. It turns toward the centre of the room's centre tile (`0x004B2AE0`) and returns 1 until it
   faces it.
3. If the art its action plays resolves present (`0x00494860(23)`, bit 0 of `+0xB`): action 27 with
   count `random(2) + 2` (simulation stream, line `0xFA`; `0x00496A30`), and sound `0xD4` when it
   starts. Action 27 (record `0x006E44A7`) has no handler, art 23, mode 0, duration 0, interrupt 1
   (`0x0049581D`–`0x004958ED`).
4. `SetState(105)`, return 1.

**105** (`0x00472E90`):
1. After `Age` 1, if it has moved since it was last seen (`+0xB0`/`+0xB4`): `SetState(101)`;
   taken, it ends a running action (`0x00496D20`) and returns 1.
2. It remembers its position. While the action runs, return 1.
3. The action over: the step `v = 0x00494650(def, 1)`, the definition's vector 1. The point is
   its position plus `(sin(h) × −v.y) >> 16`, `−((cos(h) × −v.y) >> 16)` at its heading `h`
   (`+0xF4`): `−v.y` forward. It is put there (`0x004B5560`, and `+0xF6` takes the point), then
   `SetState(103)`, return 1. A floor-slot test sits before the put (`0x00472F6F`–`0x00472FC4`),
   but both of its lookups are of the creature's own position (EDI, `+0x1A`), never the point, so
   it always passes: the step is always taken, into the pool if it reaches it.

**The step vector.** The definition's eight vectors at `def+0x6EC`: vector 0 is the portal step
(`+0x1284` of the reader's record, file `0xE94`); vectors 1–7 are file `0x127B + 12(i − 1)`, which
the record reader `0x00569B09`–`0x00569B7B` copies and marks present for all seven, and the loader
`0x00493AAE`–`0x00493AFF` copies into `def+0x6F8`. Vector 1's `y` is −1257 for the Warlock,
Goblin, Vampire, Wizard, Elven Archer, Thief and King Reginald, −1675 the Troll, −1445 the Giant,
−1249 the Monk, −843 the Rogue, 0 for the rest: the prayer's animation ends that far ahead of
where it began. Vector 2 is 257's get-up step (`hand-drop-landing.md` §3).

## 6. 106, the summoned creature — CONFIRMED

`0x004F1780`'s creature reward (case 1) creates the creature at the offering, raises it to the
offering's level, and kicks it to 106 (`0x0048AD30`). The kick enters the Temple category, whose
enter hook claims it a place.

106 (`0x00473010`): only at `Age` 1 and holding a place (`+0x3B9`). An effect from the room's
record (`+0x19` → `+0x423`, presentation), then the room point `0x004E8430` (a uniform draw
among the room's tiles in chain order that are neither a floor nor a wall slot) and a walk there
resuming 1. It returns 1 when the walk is taken, otherwise 0; any later turn returns 0.

## 7. 107, a Temple that changed — CONFIRMED

`0x004F15D0` kicks every place-holder of the room to 107 until its list is empty. It is the
Temple's room-type slot `+0x05` (the room is freed, `0x004E39C0`), `+0x09` (its tiles changed,
`0x004E4480`) and `+0x0D` (its owner changed, `0x004E5650`): table `0x006A1E9F + 13 × 41`, slots
at `0x006A20B9`, `0x006A20BD`, `0x006A20C1`. 107 is category 0, so the Temple's leave hook
releases each place as it goes, which is what empties the list.

107 (`0x004730E0`): at `Age` 1 or less, return 1. Then on a Temple tile of its own keeper,
`SetState(101)` and return 1; otherwise 0.

## 8. Ours

- `TempleStep` runs 101, 103–107 under the Praying job; `CreatureJobSystem` runs the prayer hook
  first, as for the Library.
- **101's pool** (§3, the offering) is not run from 101. Ours sacrifices a creature at the Hand's
  drop on any tile of the keeper's Temple (`TempleSacrificeRules`, ADR-0149), which predates this
  work; DK2 sacrifices only in the pool, after the landing. A walking creature in 101 on a floor
  slot (the floor-height test's own answer) goes idle instead. No walk ends there since the room
  walk's point is DK2's (§9); a prayer's step can, and then 103 takes it on.
- **107's trigger.** Ours sees a rebuilt Temple at the holder's next turn, by the room's size stamp
  (`SlabChain.SizeChangedTick`, the turn `0x004E4480` stamps) differing from the one it held when it
  claimed its place (`CreatureState.HeldPlaceStamp`, set by `RoomCapacityRules.Claim`), not inside
  the rebuild. A Temple that is freed or changes owner ends the job at the next game
  second (`CreatureJobSystem`'s room test), where DK2's 107 idles it a turn later.
- The centre tile `+0x27` is `RoomInstance.CentreField`, the bounding box's centre (Q515).
- 103's neighbour search does not test solid terrain or the nav class. A tile of the same room has
  the room's terrain, which is not solid, and the nav byte is written only by `Tile_SetTerrain`
  (record `00455000`), so it is class 0 like its neighbours.
- 103 and 104 count a creature on the tile, not a corpse: DK2's corpse is a `CDeadBody`, another
  kind of thing (`dying-states.md` §2). STRONGLY INFERRED: that kind is not 0.
- The job walk draws the room point (§9) for each candidate room before routing to it, where DK2's
  finder settles the room and then draws once; a candidate that fails to route costs a draw.
- 103's refused walk does not try another Temple (`0x004F1A20`): ours goes idle and the job
  chooser picks again. A walk to its own position is not refused in practice.
- 103's stand point a quarter tile out is reached only from another tile: ours ends a walk to a
  point on the creature's own tile at once, so a worshipper staying on its tile prays at its
  centre.
- Not modelled, all presentation: the sounds, the effect, `+0xF6`.

## 9. Where a walk to a room ends `0x004E4C10` — CONFIRMED

Ours walked every job to its room's centre tile, which in a Temple is the pool, so a worshipper
walked into it and 101 met the pool (§3). DK2's room walk `0x00471A70` (travel-states.md §2.2)
does not aim at the centre. Already in the room, it enters the state at once. Otherwise it takes
the point from `0x004E4C10(room)`:

1. `random(size)` (`+0xA7`, the slab count; simulation stream, line `0x5E5`) steps along the slab
   chain from its first slab.
2. From there, round the chain for at most `size + 1` tiles (wrapping to the first slab), the first
   tile that is no floor slot (`0x00454110`), no wall slot (`0x00454230`), of nav class 0
   (`0x00455000`), and either a Lair's tile (`0x00453A20(2)`) or holding no Obstacle object
   (`0x00453600`, `+0xF4` bit `0x4000`). Its centre is the point; with none, 0 and no walk.

So no job walk ends in a Temple's pool, on furniture, or against a wall's furniture.
`RoomWalkRules.ArrivalTile` is this; `CreatureJobSystem.TrySetOut` uses it for every job room
(ADR-0398). The Casino's floor point `0x004E4EA0` (`casino.md` §3) is the same walk without the wall
test or the Lair clause.

**Measured.** The change reaches every job, not just the Temple's. Against master `95daa5f6`:
Level 1 20/20 → 20/20; Level 2 2/20 → 5/20 (seeds 3, 5, 10, 13, 14); Level 3 seed 01 won at
3560 → 3608 (DK2 3683). The owner accepted the move past the one-seed bar (2026-10-07).
