---
systems: [rooms, combat]
---
# The Combat Pit's spectators: job 26 and states 142, 143, 144

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25.
- **Built:** ADR-0227, `CombatPitSpectatorRules`.
- The pit itself: `combat-pit.md`.

## 1. The way in — STRONGLY INFERRED

- **138's rim arm never succeeds.** 142–144 are category 31, Spectating, whose entry mask
  `0x8002383B` lacks the Combat Pit's bit `0x10000`. `SetState(142)` from 138 is refused, and
  138 returns 0.
- **Job 26, `CombatPitSpectate`**, handler `0x00457780`, is the way in. The job dispatcher
  `0x00478800` sends job `0x1A` to it.
- **No shipped creature lists job 26** in its happy, unhappy or angry table. The only code
  that selects it is **`0x00478680`**. A level could give a placed hero job 26 as its standing
  job, but none of the 66 shipped levels does (`verification-2026-09-26.md` §6). `0x00478680` is the tail of state 89 (`0x004778E0`) after the mood chooser fails:
  1. Train, unless the definition has `NotTrainWhenIdle` (`+0x686` bit 21), when the keeper's
     Training Room availability (`0x004BAB20(7)`) is 3.
  2. Job 26, when the Combat Pit's availability (`0x004BAB20(16)`) is 3.
  3. Drink, when the Casino's (`0x004BAB20(15)`) is 3.
  4. Otherwise a walk resuming 1 to a point two tiles from the centre of the keeper's Heart
     (`0x004C6460(5)`, room `+0x27`, each axis ±2 by `random(2)`). It returns 0 either way, so
     89 then stamps `+0x7E` and sets 3.

  **Built in ours (ADR-0228, `idle-fallback.md`).**

## 2. Job 26, `0x00457780` — STRONGLY INFERRED

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
   `0x2A`–`0x2C` (built: see `combat-pit.md` section 9).
3. Found: `0x00471A70(room, 142, 0x80)`. Already in the room it sets 1 and then 142. Otherwise
   it walks in state 7 to a point from `0x004E4C10`: a random tile of the room, then the first
   round the chain that is **not interior** (`0x00454110` = 0, so on a pit the rim) and passes
   three more tests.

## 3. `0x004D7CF0`, "a fight is on" — STRONGLY INFERRED

This is a room method. It walks the room's tile chain; on each tile `0x00454110` accepts (the
floor), each thing on the ground (`+0x12` = 0) whose state category (`0x00478050`, which reads
the resume state `+0xDF` of a walk) is **22, Combat**. The first one found gives its position,
and **`room+0x53` takes now**. It returns 1; with none, 0.

## 4. The states — STRONGLY INFERRED

**142** (`0x00457D60`), choosing a spot.
- Another thing on the ground on its tile: `0x004E50F0(position, out, count, 1, 1)`, with
  `count` 8 on the floor and 4 on the rim, rim tiles only, nobody on them.
- Otherwise the same only when `random(100)` ≤ 35; above, its own position.
- None found: its own position.
- The point's low twelve bits on each axis become `random(0x555) + 0x555`, x first: the middle
  third of the tile.
- `0x00471890(point, 143, 0x80)` walks there. Refused: `SetState(1)`, then job 26 again, and 142
  returns what that returned. That call, at `0x00457F2A`, is the handler's one direct caller
  besides the job dispatcher's (raw call scan of both code blocks, 2026-09-26).

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

`0x00457780` stops keeping a creature in place at `var19 * 4` elapsed turns,
while 144 still picks a new spot at equality and leaves only above that limit.
The elapsed comparison is unsigned.

## 5. Ours
- `PitFightSeenTick` holds `+0x53` on the lowest tile. Runtime migration and reset
  follow the room-lifecycle rules in
  [combat-pit.md section 9](combat-pit.md#9-what-is-built-adr-0213-hashsave-523).
- `SpectatorPosX` and `SpectatorPosY` hold 144's remembered position; `CurrentJob`
  is `CombatPitSpectate` while spectating.
- Actions 69/70 use counted animation playback, creature art availability and the native
  conditional RNG draws. The fixed eight-turn cheer guess is removed. Shared animation
  resource identity and action-position payload limits remain.
- Facing uses the attached room's centroid field; the room route uses its first
  slab-chain point. Shared route geometry and ground-occupancy limits remain.
- The quiet interval uses variable 19 with the fallback precedence recorded in
  [combat-pit.md section 9](combat-pit.md#9-what-is-built-adr-0213-hashsave-523).
- The finder's messages are built (ADR-0413); job 26's fallback caller is built (ADR-0228).
- 138's rim arm remains refused by the category gate.
- Getting up on a pit takes drop state 137 through 1. On the rim, 137 goes to 138,
  whose refused spectator transition idles.
