# The Workshop's states: 63–67

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-10-07.
- **Built:** ADR-0404, `WorkshopStep`. The credit is the category's update hook `0x0047B190`
  (`workshop-crates.md`), which runs under 63–67. The crate made, 68 and 69, is `workshop-crates.md`.
- State `+0xA9`/`+0xAD` is the machine's position (the target), `+0xB1`/`+0xB5` where the creature
  was last seen, `+0xB9` (creature `+0x307`) how it works the machine: 1 at it, 2 beside it.
  `Age` is state `+0x1C`.
- Handlers from the state table (`creature-state-table.md`): 63 `0x00470110`, 64 `0x00470470`,
  65 `0x00470860`, 66 `0x00470560`, 67 `0x00470C00`. All five are category 5 (Manufacture).

## 1. How each is entered — CONFIRMED

A scan of every call to `SetState`, `0x0048AD30`, `0x00491830` and the two walks for `0x3F`–`0x45`:

| state | entered by |
|---|---|
| 63 | the job's room walk (`0x004700C0`), the slap reaction (`0x0046F2C3`, the handler of 77 and of 67), 64 (`0x004704E8`), 65 (`0x00470B09`), 66 (`0x00470811`, `0x0047084D`), 69 (`0x00470BE9`), 70 (`0x00470C5C`, handler `0x00470C10`); the room table gives the Workshop 63 |
| 64 | 63's walks (`0x004703BB`, `0x0047040C`) |
| 65 | 64 (`0x0047054D`) |
| 66 | nothing |
| 67 | the slap (`0x0048B8A0`): a creature holding a place in its slapper's Workshop, through the shared tail `0x0048B9DB` → `0x0048BA00` |

## 2. 66 is unreachable — STRONGLY INFERRED; 67 is the slap's

No setter, wrapper or walk carries `0x42`, by the call scan and by a raw byte scan of `.text`
for `6A 42` and `B8+r 42`; the room table's `+0x19`/`+0x1D` give the Workshop 63
(`portals-room-handler-table.md`), and no `+0xDF` store does (`travel-states.md` §2.4; the stores'
immediates are `0x105`, 1, `0xE9`, 1, 1). 66 is a variant of 65 that asks the finder again while it
works (read to `0x00470797`). Not covered, as for Q415: a computed call and a state restored from
a save.

**67** is entered. The slap `0x0048B8A0` ends a running action and pushes `0x43` at `0x0048B9DB`
for a creature whose held room (`+0x3B9`) is its slapper's Workshop (type 10), then jumps to the
shared `SetState` tail at `0x0048BA00`; the call scan missed it because no call follows the push
(`docs/TRAPS.md`). 67's handler `0x00470C00` is a thunk to the slap reaction `0x0046F1F0` (77's,
and 120's): action `0x13`, then 63 for a Workshop place. The slap's state moves (67, 77, 120)
are built (`SlapRules`, ADR-0401, `slap-states.md`; Q569).

## 3. The finder `0x004F5BE0(room, creature)` — CONFIRMED

Over the room's objects of type 15 (*Anvil*), 16 or 46, two passes, each the nearest by squared
distance (strict `<`, the second continuing from the first's best):

1. **At the machine.** The nearest that no place-holder of the room (the seeker included) targets
   with mode 1 (`+0x307` = 1, a valid target point equal to the object's position). Found: it
   returns 1 with the object.
2. **Beside it.** Otherwise the nearest that no place-holder targets with mode 2. Its stand point:
   on a tile that is no wall slot, the first of N, E, S, W (`0x006BC350`) whose neighbour tile has
   no creature on it (`0x00453670`), at angle `(side & 3) << 9`; on a wall slot, the angle
   `((wall side + 2) & 3) × 0x200 + 0x180`. The point is the object's position plus
   `(sin·r) >> 16`, `−(cos·r) >> 16` with `r = (objdef Width / 2 + footprint / 2) × 100 / 100`; with
   no free side, the object's position. It returns 0 with the object and the point.
3. Neither: it clears the seeker's mode and searches again, without end. A Workshop holds a place
   only while it has furniture (ADR-0205), so a holder never meets an empty one.

## 4. 63, the chooser `0x00470110` — CONFIRMED

1. At `Age` 1 it stops. With no manufacture order (player `+0xA1C` zero): at `Age` 1 action 43,
   and it holds while that plays, then returns 0.
2. Not on a tile of its own keeper's Workshop: return 1, and it stays.
3. The finder on the held room. Pass 1's object: mode 1, and the stand point is the object's
   position plus `r` at an angle: on a wall slot `((wall side + 2) & 3) × 0x200 + 0x780` (masked to
   `0x7FF`), otherwise `random(0x800)` from the coordinate-seeded selector on the object's tile
   (`0x2286` with y, `0x16F2` with x, the furniture choice's; line `0x56`). Pass 2's object: mode 2,
   the finder's point. A walk there resuming 64 (`0x00471890`, options `0x80`); taken, the target
   is the object's position. Either way it returns 1.

## 5. 64 and 65, the work — CONFIRMED

**64** (`0x00470470`): on the target's tile an object of type 46, else 15, else 16
(`0x004537E0`). None: `SetState(63)`; refused, return 0; taken, it goes on below anyway. Found:
it turns toward it (`0x004B2AE0`) and returns 1 until it faces it. Then action 32 with count
`random(7) + 6` (simulation stream, line `0xBC`; `0x004969D0`) and, if it starts, `SetState(65)`.
Return 1. Action 32 (record `0x006E4538`): handler `0x00498D20` (an anvil sound, presentation),
art 22.

**65** (`0x00470860`): it turns toward the target. The machine's process and effects are
presentation (STRONGLY INFERRED: type 46 takes object state `0x1A` through `0x0049EF60` and every
type the `+0x146` bit `0x20`, which `0x004A1A00` clears each turn; who reads state `0x1A` was not
traced). After `Age` 1, a creature that moved while the action runs ends it (`0x00496D20`).
It remembers its position. While the action runs, 1; ended, `SetState(63)` and its result.

## 6. Ours

- `WorkshopStep` runs 63, 64 and 65 under the Manufacture job, after the credit hook, as the
  Library's step does. A zero credit fails the hook and idles the creature before the step runs
  (`job_lost reason=cannot_manufacture`, ADR-0573).
- **The work's length** is `AnimationTimingRules.CountedHoldTicks` for art 22 and the count, LIKELY
  (the runner's end after its cycles is the renderer's flags, not read); "the action runs" is
  `now < ActionUntilTick`. 64 goes to 65 whether or not the action starts; DK2 goes only when
  `0x004969D0` takes it.
- The finder walks the objects on the room's tiles; DK2 walks the room's object list (`+0x0E`,
  link `+0x12A`). They differ only for an object on a room tile that the list does not hold.
- 63 without a held place stays; DK2 would pass a null room to the finder (not reached: the enter
  hook claims the place).
- The machine's process states, its effects, sounds and object flags are presentation and not kept
  (STRONGLY INFERRED: who reads object state `0x1A` and the `+0x146` bit `0x20` was not traced, §5).
- The finder's endless search (§3, step 3) is ours returning none, which leaves 63 standing.
- Ties go to the first object in `world.Objects` order; DK2 walks the room's object list.
- 63's pass-2 test does not count a corpse on the side tile (`dying-states.md` §2), as the Temple's.
