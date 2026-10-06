# The Library's states: 53–59

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-10-06.
- **Built:** ADR-0397, `ResearchStep`. The credit and its hook: `library-and-research.md`.
- State `+0xA9`/`+0xAD` is the researcher's target point (creature `+0x2F7`/`+0x2FB`), `+0xB1`/
  `+0xB5` where a wait last saw it, `+0xA7` the creature 59 watches. `Age` is state `+0x1C`.

## 1. The walk — CONFIRMED

States 53–59 are category 4, so the Research hook runs under all of them and a walk between them
keeps it running.

| state | handler | does | next |
|---|---|---|---|
| 53 | `0x00475A20` | chooses a free book or bookcase, or a bare tile, and walks there | 54 |
| 54 | `0x00475D20` | faces the object, then reads (action 29) | 56 or 57 |
| 55, 58 | `0x00484940` | returns 0 | — |
| 56 | `0x00476060` | waits out a long read | 53, or idle |
| 57 | `0x00475E70` | waits out a short read, back to 53 when a spot frees | 53, or idle |
| 59 | `0x004762D0` | watches an intruder, then walks back | 53 |

Entries (`SetStateLits` over `0x00476D30`, `0x0048AD30` and `0x00491830`; `PreCall` over the
route helpers): 53 from job 3's walk (`0x00475A00`), 54 (`0x00475D86`), 56 and 57 (`0x00475FBD`,
`0x0047603F`, `0x004761DF`) and 59's walk (`0x004762E9`); 54 only from 53's two walks
(`0x00475C29`, `0x00475C6F`); 56 (`0x00475E5B`) and 57 (`0x00475E0F`) only from 54; 59 only from
the disturbed room (`0x004E89EB`, §6).

## 2. 55 and 58 are unreachable — STRONGLY INFERRED

No literal 55 (`0x37`) or 58 (`0x3A`) reaches either setter or the wrapper `0x00491830`, and
neither is a walk's resume. The room handler table's `+0x19`/`+0x1D` holds neither
(`portals-room-handler-table.md`), and no `+0xDF` store does (`travel-states.md` §2.4). Of the 42
instructions that push or store either constant, the only stores near the state code are the state
table's own id field (`0x0047CAA8`, `0x0047CC24`); the pushes near `0x0047B230` are message 55.
Not covered, as for Q415: a computed call that was not scanned, and a state restored from a save.

## 3. 53, the chooser `0x00475A20` — CONFIRMED

1. At `Age` 1 the creature stops. With no research target (player `+0x213` zero): at `Age` 1
   action 43, and it holds while that plays, then returns 0.
2. Not on a tile of its own keeper's Library: return 1, and it stays.
3. The finder `0x004E1ED0` (§4) on the held room. An object found: if its tile is a wall slot,
   the stand point is the tile's centre a quarter tile toward that side (`0x006BC350`); otherwise
   the object's position plus `r = (objdef Width/2 + footprint/2) × 100 / 100` at the object's
   angle, turned by `0x400` when its variant byte `+0x14A` is 2 and `random(2)` (simulation stream,
   line `0x59`) is nonzero, the offset `(sin·r) >> 16`, `−(cos·r) >> 16`. A walk resuming 54
   (options `0x80`); taken, the target is the object's position. No object: a walk to the
   finder's point, the target that point. Either way it returns 1.

## 4. The finder `0x004E1ED0` — CONFIRMED (the variant seed STRONGLY INFERRED)

Over the room's objects of type 58 (*Library Book*) or 64 (*Book Case*), the nearest by squared
distance (strict `<`) that is free: no place-holder of the room has a valid target point (world
slot `+0x200`, a bounds test) equal to its position, and, for a Book Case, no creature stands on
the tile of its stand point (`0x00453670`, kind 0), that point taken at its angle turned by `0x400`
when variant 2 and a coordinate-seeded `random(2)` says so (source lines `0x503`–`0x505`, a
local seed, not the simulation stream). Found, it returns 1.

None: over the held room's slab chain, the tiles that are no floor or wall slot and that no
place-holder's target point lies on are counted, `random(count)` (simulation stream, line `0x55A`)
picks one by its rank along the chain, and its centre is the point; with none counted the point
stays zero. Then `random(0x800)` (line `0x599`) moves it `(cos << 7) >> 16`, `(sin << 7) >> 16`,
and it returns 0.

**The furniture.** The Library lists a Book Case in slot 0 (floor) and a Library Book in slot 3
(wall; `LibraryOnRealDataTests`). The wall pass moves a book `0x700` toward its wall (arm `0x004EECEB`,
sides at `0x004EF1B0`), as the Casino's alcove (`casino.md` §3); 53 stands at its tile's centre a
quarter tile toward that wall. A Book Case's floor arm (`0x004EE6A5`) sets angle `(y & 1) << 10` and
variant `random(3)` from the coordinate-seeded selector the furniture choice uses
(`AutoFurnitureRules`). **STRONGLY INFERRED:** that the selector is seeded in the same order as ours
(evidence: the floor arm at `0x004EE6A5` calls the same selector the choice does; the seeding order
across a rebuild was not traced).

## 5. Reading: 54, 56, 57 — CONFIRMED

**54.** On the target point's tile a Book Case, else a Library Book. Neither: `SetState(53)`;
refused, return 0; taken, it goes on below anyway. Found: it turns toward the object and returns 1
until it faces it. Then on a floor or wall slot action 29 with count `random(7) + 7` and 56, on a
bare tile action 29 with count `random(3) + 3` and 57. Action 29 has no handler, mode 0, art 14
(`0x0049592E`, `0x00495927`).

**56 and 57.** It turns toward the target; a Book Case is flagged and a Library Book's process set
(presentation). After `Age` 1 a creature that moved ends its action. It remembers its position.
While the action runs, 57 asks the finder again and, given an object, sets 53 and ends the action;
56 just holds. When it ends: if a room-mate of another creature type is one it objects to
(`0x004E85F0`, `0x004946C0`: a good creature objects to an evil one, anyone else to a good one) and
`0x004C6AE0` finds a Library with a place other than this one, return 0 (idle, to choose again);
otherwise `SetState(53)`.

## 6. The disturbed Library and 59 — CONFIRMED

**The way in.** The contact test `0x004E8660`, run when a creature steps onto a room tile, calls
`0x004E88A0` for a creature on a Library tile whose resolved category is not 4 (the intruder). Once
a game second (`turn % v19 == 0`, DK2's turn being our tick + 1), for each place-holder that is no worker and stands within 3 tiles of it
(`dx² + dy² < 0x9000`) and is in 56: its Treatment bucket above a tenth of `def+0x76A` sends it to
59 (`0x0048AD30`); otherwise it is charged `AngerNoWork` on Work with complaint 3. `def+0x76A` is
`UnhappyThreshold × 0x7FFF / 100`: the loader stores it when `[EBP+0x198]` is set, and the record
reader `0x00569041`–`0x005692D0` sets that field to 1 for every record read from file.

**59** (`0x004762D0`). Every call it charges `AngerNoWork` (`0x00478F00`). At `Age` 1, on a room
tile, it takes the nearest creature on the room's tiles that holds no place or holds one in another
Library and is not researching (`0x004E8A30`), and starts action 40 at it (handler `0x00499800`:
turn toward the target; art 5). Later, while that creature lives: it turns toward it, and on its
second turn, facing it within `0x80`, calls `0x0048CAD0` on it with percent 2 × its current
per-tick speed (vtable `+0x90`, `0x0048D000`, the word `+0x11C`) and `2·v19` seconds. That sets its
speed to current × percent / 100, clamped between `def+0x5B4 / 4` and `v19 << 12`, and the store
`0x0048D010` clamps the word again to `0xFFF`, which is the bound that holds (`v19 << 12` is above
it), for `v19 × 2·v19` turns: it is hurried out. It holds while the action plays. Otherwise a walk to a random tile of its
held room (`0x004E4D90(pt, 1, 0)`), resuming 53.

## 7. Ours

- `ResearchStep` runs 53, 54, 56, 57 and 59; the disturbed Library is `RescueSystem`'s contact test.
- Not modelled, all presentation: the object flags and the Library Book's process, action 40's
  animation, 59's art test.
- `0x004C6AE0`'s distance is to the room's centre (`+0x27`); ours takes its bounding box's middle
  and routes to its first slab, as `RoomCapacityRules.AnotherHasRoom` does.
- The Training Room's arm of the disturbed room (112 to 109) waits for the Training states.
- The contact call's own gates (`0x0048A81C`–`0x0048A858`: on the ground, not dying, no place held)
  are applied to the Library arm; a creature in the hand never reaches the test (`Sidelined`). The rescue arms in the same system lack them; that gap predates
  this work.
- `0x004C6AE0`'s capacity test `0x004E53B0` counts the creature's own place, as ours does. It also
  asks a Library for a shelf (`0x004E1AC0`, `library-and-research.md` §7: one draw); ours does not,
  for any caller of `RoomCapacityRules.HasRoom`, so the Library's capacity and the stream differ when
  every shelf is full.
- Ties in the finder (equal squared distance) go to the first object in `world.Objects` order; DK2
  walks the room's object list, whose order ours does not keep. Ours takes any object of the two
  types on the room's tiles.
- 59's hurry rebuilds the intruder's speed word from its definition and level; DK2 reads the stored
  `+0x11C`. They differ only for a creature on a party speed, and the product clamps to `0xFFF`
  either way. The disturb walks `world.Creatures`, DK2 the room's place-holder list `room+10`; no
  draw is made, so only the order of the kicks differs.
- A speed effect stops at the word `0x0048D010` stores (`0xFFF`), in `StatusEffectRules.SpeedCap`,
  for Haste as well (ADR-0397).
