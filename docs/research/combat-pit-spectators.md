# The Combat Pit's spectators: job 26 and states 142, 143, 144

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25.
- **Built:** ADR-0227, `CombatPitSpectatorRules`.
- The pit itself: `combat-pit.md`.

## 1. The way in — CONFIRMED

- **138's rim arm never succeeds.** 142–144 are category 31, Spectating, whose entry mask
  `0x8002383B` lacks the Combat Pit's bit `0x10000`. `SetState(142)` from 138 is refused, and
  138 returns 0. **Corrected:** `combat-pit.md` §10 said, before ADR-0227, that 142 was refused in ours only because it was not built.
- **Job 26, `CombatPitSpectate`**, handler `0x00457780`, is the way in. The job dispatcher
  `0x00478800` sends job `0x1A` to it.
- **No shipped creature lists job 26** in its happy, unhappy or angry table. Its only caller
  is **`0x00478680`**, the tail of state 89 (`0x004778E0`) after the mood chooser fails:
  1. Train, unless the definition has `NotTrainWhenIdle` (`+0x686` bit 21), when the keeper's
     Training Room availability (`0x004BAB20(7)`) is 3.
  2. Job 26, when the Combat Pit's availability (`0x004BAB20(16)`) is 3.
  3. Drink, when the Casino's (`0x004BAB20(15)`) is 3.
  4. Otherwise a walk resuming 1 to a point two tiles from the centre of the keeper's Heart
     (`0x004C6460(5)`, room `+0x27`, each axis ±2 by `random(2)`). It returns 0 either way, so
     89 then stamps `+0x7E` and sets 3.

  **`0x00478680` is not built in ours.** Until it is, job 26 is unreachable with shipped data.

## 2. Job 26, `0x00457780` — CONFIRMED

1. On a tile of a Combat Pit owned by the creature's keeper or an ally
   (`0x004539B0(16, owner, 1)`), off the floor (`0x00454110` = 0): if a fight is on
   (`0x004D7CF0`) or the pit's stamp `+0x53` is less than `var19 × 4` turns old, `SetState(142)`.
   Taken, return 1.
2. Otherwise **`0x004D8510`**, on the keeper (`ECX` = creature `+0x2A`), over its own rooms
   (`player+0x36`). A Combat Pit is a candidate when:
   - a fight is on in it (`0x004D7CF0`, which stamps each pit it finds a fight in);
   - the creature can route to a point in it (`0x00471740(room, 0x80)`: `0x004E5000`, then
     `0x004D0E60`);
   - it has a place (`0x004E53B0`, the capacity test).

   It takes the nearest by `(dx² >> 12) + (dy² >> 12)` to the room's first tile. It prefers a
   pit none of whose occupants (`room+0x0A`) `0x0045C640` objects to. That test is called on the
   seeker (`MOV ECX,[EBP+0x10]`): a good creature objects to an evil one, anyone else to a good
   one. With a pit full, and the keeper able to build pits, it posts one of messages
   `0x2A`–`0x2C` (not built).
3. Found: `0x00471A70(room, 142, 0x80)`. Already in the room it sets 1 and then 142. Otherwise
   it walks in state 7 to a point from `0x004E4C10`: a random tile of the room, then the first
   round the chain that is **not interior** (`0x00454110` = 0, so on a pit the rim) and passes
   three more tests.

## 3. `0x004D7CF0`, "a fight is on" — CONFIRMED

This is a room method. It walks the room's tile chain; on each tile `0x00454110` accepts (the
floor), each thing on the ground (`+0x12` = 0) whose state category (`0x00478050`, which reads
the resume state `+0xDF` of a walk) is **22, Combat**. The first one found gives its position,
and **`room+0x53` takes now**. It returns 1; with none, 0.

## 4. The states — CONFIRMED

**142** (`0x00457D60`), choosing a spot.
- Another thing on the ground on its tile: `0x004E50F0(position, out, count, 1, 1)`, with
  `count` 8 on the floor and 4 on the rim, rim tiles only, nobody on them.
- Otherwise the same only when `random(100)` ≤ 35; above, its own position.
- None found: its own position.
- The point's low twelve bits on each axis become `random(0x555) + 0x555`, x first: the middle
  third of the tile.
- `0x00471890(point, 143, 0x80)` walks there. Refused: `SetState(1)`, then job 26 again, and 142
  returns what that returned.

**143** (`0x00457F40`), facing the pit.
- Another thing on its tile and `SetState(142)` taken: return 1.
- Otherwise it turns toward the centre of the tile at room `+0x27`, where the room is the one
  the creature is attached to (`+0x3B9`), through `0x004B2AE0`. While any turn is left it
  returns 1.
- Then, when the creature has animation 8 or `0x14` (`0x00494860`, `+0xB` bit 0), action 70,
  or 69 when `random(100)` > 30 with parameter `random(2) + 1`.
- Then `SetState(144)`.

**144** (`0x004580D0`), cheering.
- At `Age` 1, or standing where it stood when it last looked (state `+0xAC`, `+0xB0`), or with
  `SetState(142)` refused, it looks:
  - It remembers where it stands.
  - With an action running it returns 1.
  - Otherwise, on a room tile: a fight on (`0x004D7CF0`) passes; with none, when
    `now − room+0x53 > var19 × 4` it returns 0, idle.
  - Then `SetState(142)`.
- Moved, and 142 taken: a running action is ended (`0x00496D20`), return 1.

`0x00457780`'s test is `≥`, and 144's is `>`: 16 turns after the last fight, job 26 no longer
keeps a creature in place, while a watcher in 144 still picks a new spot.

## 5. Ours

- `PitFightSeenTick` holds `+0x53` on the pit's lowest tile, as `HatchTicks` does for a
  Hatchery. A pit whose lowest tile changes forgets it.
- `SpectatorPosX`, `SpectatorPosY` hold 144's position.
- `CurrentJob` is `CombatPitSpectate` while spectating.
- **UNKNOWN or guessed:**
  - The cheer's length (`CheerGuessTicks` = 8), since the animations are not read.
  - Whether a creature has animation 8 or `0x14`: ours assumes it does.
  - Room `+0x27`, the position tile, is taken to be the room's centre: 89's fallback aims ±2
    tiles from the Heart's. Ours uses the middle of the bounding box.
  - The route point `0x004E5000`: ours tests the room's first standable tile.
- **Not built:** the finder's messages, and `0x00478680`, which is the only caller of job 26.
- 138's rim arm is kept and refused by the gate, as DK2's is.
- **Getting up on a pit.** A stunned creature that gets up on a pit (`0x004E8DA0(0)`) takes the
  drop state 137 through 1. On the rim, 137 goes to 138, and 138 idles.

