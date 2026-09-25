# Mechanic: guard duty — states 97, 98 and 99

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25: the handlers of states
  97 (`0x00462930`), 98 (`0x00462D00`) and 99 (`0x00463140`), the post-slot finder
  `0x00462BB0`, the Guard Post finder `0x00463190`, the tables at `0x00698540` and
  `0x00698588`, the jump table at `0x00463120`, and the variable loader's stores. Addresses
  are facts about the binary in our own words; no decompiler output enters this repository
  (ADR-0046)
- `Traps.kwd` from the reference install, for the Guard Post flag; OpenKeeper's
  `Trap.TrapFlag` for the flag's name, as a format source only

Original version: DK2 v1.70 (GOG)

Questions: advances ADR-0135. `creature-state-table.md` owns the rows (97–100, category 11,
labels "guarding" and "on patrol to a Guard Post"); `state-category-hooks.md` owns the
category's enter hook, which only claims the room position.

Confidence: **STRONGLY INFERRED (binary)** for §1–§4; **CONFIRMED (data)** for the Guard
Post flag.

---

## 1. State 97: to the nearest post

1. The creature must stand on a tile of its own keeper's Guard Room (`0x004539B0(9, owner,
   0)` on the tile under it). Otherwise the handler returns 0: idle.
2. **A square spiral** from the creature's tile finds the first tile, in map bounds (world
   vslot `+0x1FC`), whose slot (§2) is not 8. The order is (x, y), (x+1, y), (x+1, y+1),
   (x, y+1), (x−1, y+1), (x−1, y), (x−1, y−1), (x, y−1), (x+1, y−1), (x+2, y−1), …: legs of
   1, 2, 2, 3, 3, …, alternating vertical and horizontal, the first leg downwards. The
   search stops once a leg would reach twice the map's larger side (vslots `+0x1CC`,
   `+0x1D0`). Nothing found leaves the result at tile (0, 0).
3. The slot finder runs again on the result, writing the slot's **facing** to `state+0xAB`.
4. **The deadline** `state+0xB1` = the turn + 75 + `random(v152)`, where v152 is
   `MaxTimeInGuardRoomBeforeWanderingToGuardPostSeconds` (60), stored raw by the loader
   (`0x0051D2D8`), so these are turns.
5. The target is `(x << 12) | ox`, `(y << 12) | oy`, with `(ox, oy)` read from
   `0x0069858C + 8 × slot` and `0x00698590 + 8 × slot`: four bytes into the direction table
   of §3. So slots 0, 1, 4 and 5 take `(0x600, 0xA00)` and slots 2, 3 and 6 `(0xA00,
   0x600)`. **Slot 7's y reads `0x006985C8`, the first dword of the source-path string**
   (`0x445C3A44`), so its target is far off the map. Slot 8, the nothing-found case, reads
   the string for both.
6. `0x00471890(&target, 98, 0x80)`: state 6, resuming 98. A failed route returns 0: idle.

## 2. The post slot (`0x00462BB0(x, y)`)

The tile must hold a room of any kind (the tile's room word, `+0x2 & 0xFFF`), else 8. Its
four neighbours on the keeper's own Guard Room make a mask: west 1, east 2, north 4
(y − 1), south 8. The mask is looked up in nine `(mask, facing)` pairs at `0x00698540`:

| slot | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|---|
| mask | 0 | 1 | 2 | 4 | 8 | 5 | 9 | 6 | 10 |
| facing | 1 | 1 | 0 | 3 | 1 | 2 | 1 | 3 | 0 |

A hit writes the facing to `state+0xAB` and returns the slot; a miss returns 8. So a post
is meant to be a room tile with no more than two Guard Room neighbours, not in a straight
line. Edge and interior tiles, and the middle of a one-wide run (masks 3 and 12), are never
posts, and a room tile of another kind beside the Guard Room qualifies, since the tile itself
is not tested for type. Two quirks leave a rectangular room with **two working corners of
four**:

- **The north-west corner is no post.** Its mask, 10 (east and south), is the ninth pair,
  found at index 8, which is also the miss. Every caller compares with 8, so the spiral
  passes it over.
- **The south-west corner fails.** Mask 6 (east and north) is slot 7, whose target is off the
  map (§1.5), so a guard whose nearest post it is goes idle.

The south-east (slot 5, facing 2) and north-east (slot 6, facing 1) corners work.

## 3. State 98: round the room's edge

1. Each call clears `+0x3E6` bits 22 and 24 and sets 23 and 25 (unnamed; not modelled).
2. **Past the deadline**, `0x00463190` (§4). If it sends the creature to a Guard Post, the
   handler returns 1. Otherwise the deadline = the turn + 75 + `random(v30)`, v30 being
   `ImpIdleDelayBeforeReevaluationSeconds` (5), also raw (`0x0051CE59`).
3. Not on its own Guard Room tile: return 0, idle.
4. **One leg of a left-hand wall follow**, from the jump table at `0x00463120` on the facing
   `d` in `state+0xAB`. Four headings, each in two table entries (d and its inner-corner
   twin: 0 and 5, 1 and 6, 2 and 7, 3 and 4):

   | heading | step | wall | forward blocked | wall opens |
   |---|---|---|---|---|
   | 0, 5 | east | north | facing 1, stay | step, facing 4 |
   | 1, 6 | south | east | facing 2, stay | step, facing 5 |
   | 2, 7 | west | south | facing 3, stay | step, facing 6 |
   | 3, 4 | north | west | facing 0, stay | step, facing 7 |

   "Forward blocked" is the next tile not on the keeper's Guard Room; "wall opens" is the
   stepped-to tile's wall-side neighbour being on it. While neither holds, the creature steps
   and the leg goes on, so **one call is one straight run to the next turn**.
5. The new facing goes to `state+0xAB`; the target is `(x << 12) | ox`, `(y << 12) | oy`
   from the eight `(ox, oy)` pairs at `0x00698588` indexed by the facing: 0 and 4 `(0x600,
   0x600)`, 1 and 5 `(0xA00, 0x600)`, 2 and 6 `(0xA00, 0xA00)`, 3 and 7 `(0x600, 0xA00)`:
   the quarter of the tile against its wall.
6. `0x00471890(&target, 98, 0x80)`. On failure the definition's `+0x6E3` sound plays
   (`0xDD` through the sound object's vslot `+0x20`, its handle to `+0x2E`) and the state
   is set to 99. Either way the handler returns 1.

## 4. The Guard Post (`0x00463190`)

It walks the keeper's trap list (`creature+0x2A`, head at `+0xC`, next at thing `+0x10`)
and counts traps that are not blueprints (`+0x10C` bit 5 clear), whose definition has flag
`0x800` (`def+0x88`) and whose `+0x113` equals the creature's `RoomId` (`+0x3B9`). With at
least one, it draws `random(count)` and routes to that trap, `0x00471890(&trap position,
100, 0x80)`, returning 1 on success. **Flag `0x800` is the Guard Post**: in `Traps.kwd` the
flags dword at record offset `0x1EC` has bit 11 on the Guard Post (`0xC18`) and on no other
of the 16 records, and OpenKeeper names it `GUARD_POST`. State 100 (`0x004632A0`) sets a
deadline of the turn + 50 + `random(v152)` and routes back in 98; the rest of it is unread.

## 5. State 99

`Age` (`state+0x1C`), which `SetState` zeroes and the dispatcher increments before every
handler (`eating.md`), at 1 or less: return 1. Then on the keeper's Guard Room: `SetState(97)`,
return 1; otherwise return 0. So a patrol whose route failed waits one turn and starts again
from the nearest post.

## 6. Built and not built

**Built** (ADR-0212): §1–§3 and §5 as `GuardRules` and `GuardStep`, run by the Guard job
once a turn. **Not built:** §4, since traps are not built, so the deadline only ever resets
and state 100 is never entered; the `+0x3E6` bits; the sound. **Ours:** the room test is
our tile owner and room type; a target off the map is a failed route.

## Open questions

- Are the two corner quirks visible in play: does a guard never start from the north-west
  corner, and does one standing nearest the south-west corner go idle instead of patrolling?
- What bits 22–25 of `+0x3E6` mean.
