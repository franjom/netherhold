# State 89's fallback and state 3: what an idle creature does with nothing to do

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25.
- **Built:** ADR-0228, `CreatureJobSystem` (its `Idle` part).
- Related: `job-selection.md`, `combat-pit-spectators.md`, `imp-idle-and-wandering.md`.

## Result in one sentence

When 89's happy chooser takes nothing, because it failed or because its five-second gate is
shut, 89 tries **Train, then watching a pit fight, then Drink**, each only when the level makes
that room available. With none, it gives the creature **12 turns in state 3**, which wanders in
87 or plays an idle action.

## 1. State 89, `0x004778E0` — CONFIRMED

In order:
1. A dying creature stops (`0x00459560`).
2. It clears `+0x3E6` bits 20 and 3.
3. The casino winner's countdown `+0x3F2`: 1, then 155.
4. A Call To Arms (player `+0x76` bit 2): a walk to the call's point resuming 191, or message
   `0x54`.
5. With no lair (`+0x3B7` = 0), the lair job `0x0046F390`.
6. `DroppedToDoJobId` (`+0x6E`), then `ReturnToJobId` (`+0x68`).
7. **The happy chooser `0x004780E0`.** It returns 0 untried when the current category is
   Combat, and inside its gate (`+0xFD`, `now − last < var19 × 5`). On a failed pass it charges
   `0x00478B80(3, def+0x6C7, 3)` and returns 0.
8. **`0x00478680`** (§2).
9. `+0x7E` = now + 12, and `SetState(3)`.

The unhappy and angry choosers are the dispatcher's (`0x00476FC0`), not 89's.

## 2. `0x00478680` — CONFIRMED

1. **Train** (job 4): unless the definition has `NotTrainWhenIdle` (`+0x686` bit 21), when
   `0x004BAB20(7)` is 3.
2. **Job 26**, `CombatPitSpectate`, when `0x004BAB20(16)` is 3.
3. **Drink** (job 9), when `0x004BAB20(15)` is 3.
4. Otherwise a random Heart of the keeper's (`0x004C6460(5)`). It aims at its `+0x27` tile ±2 on
   each axis (`random(2)` each, x first) and walks with `0x00471890(point, 1, 0x80)`.
   **It returns 0 either way.**

`0x004BAB20(t)` is the keeper's `RoomInfo` availability for room type `t`, 1 when the level has no
row (`player-object.md`). 3 is "available": buildable, not built. Each job's own handler then
looks for a room.

**The Heart walk has no effect.** 89 then calls `SetState(3)`. From the walk's state 6 that is
admitted: category 0's entry mask is 0. So the walk is replaced in the same call, and state 3
runs no movement.

## 3. State 3, `0x00477680` — CONFIRMED

- An action running: return 1.
- `now ≥ +0x7E`: return 0, idle.
- Otherwise **`0x004776C0`**:
  - With `+0xA7` set (a wander has run), `random(100)`:
    - under 25: wander;
    - 25–74: action `0x2B` (43), if the creature has it;
    - 75–99: action `0x2C` (44), if it has it.

    An action played clears `+0xA7` and returns 1. For a creature whose `+0x36F` is 3, a
    further `random(100)` under 10 makes shot `0x35`, the Heal Instant Shot, at its position
    (`0x004B7CD0`; corrected 2026-10-07 from "sound", `refused-states.md` §1).
  - Wander: `SetState(87)` and `+0xA7` = now + `0x84` (132), return 1. Refused, 0.

State 4 has the same handler. 87 (`0x004808E0`) is the imp's wander state and one handler for
every creature (`imp-idle-and-wandering.md`). Its walk resumes 1, so the arrival goes back
through 89.

## 4. What it changes

- **An idle creature wanders.** It no longer stands at the last place it stopped.
- **A creature with nothing in its table trains, watches or gambles**, if the level offers the
  room. The Goblin's Train is its happy table anyway; the fallback matters for the others.
- **`AngerNoWork` is far slower.** The happy chooser adds the loaded value, the file's figure
  over four (`creature-anger.md`), once a failed pass. Ours charged 50 once a second, which was
  right only while ours retried every tick. Passes are about eight turns apart, because the 87
  walk back to 1 cuts 3's twelve turns short.
- The Combat Pit's 139 adds the same loaded value every turn (`combat-pit.md` §2). Ours added the
  file's figure.

## 5. Ours

- The newly entered raw89 path runs the dispatcher mood prelude before `Choose`,
  then the happy chooser for every mood and the fallback to3. See
  [its function record](functions/004778E0.md). The existing raw1 path still combines
  selection with idle, uses the unhappy table for unhappy creatures, and leaves
  angry selection to `CreatureAngerSystem`.
- **The refusal log** now reports every refusal, since passes are rare.
- **Guessed:** that every creature has actions 43 and 44, and their length
  (`ImpTaskSystem.IdleActionGuessTicks` = 8).
- **Not built:**
  - the Heart walk, since it has no effect;
  - the idle sound;
  - `ReturnToJobId`;
  - the casino arm. The Call To Arms arm is built (`call-to-arms.md`, ADR-0279).

## 6. Two more `AngerNoWork` sites (Q438) — CONFIRMED, not built

Read 2026-09-26 after the verification pass (`verification-2026-09-26.md` §5). The reader search
covered every `def+0x6C0`–`+0x6CB` displacement and the indexed forms, and found four readers of
`def+0x6C7`: the happy chooser, pit state 139, and the two below.

1. **States 59 and 109.** Their handlers, `0x004762D0` and `0x004859E0`, both begin by calling
   `0x00478F00`. That routine's first act is `0x00478B80(3, def+0x6C7, 3)`, the same charge the
   happy chooser makes, before any of its tests. So a creature in 59 (Library) or 109 (Training
   Room) is charged every turn it is there.
2. **The disturbed room, `0x004E88A0(intruder)`.** `0x004E8660` calls it for a creature on a
   Library tile whose state category is not 4, or on a Training Room tile whose category is
   not 13. Once a game second (`turn % var19 == 0`), for each creature attached to the room
   (`+0xA`, then `+0x238`):
   - it is skipped if it is a worker (`def+0x686` bit 0), if it stands 3 tiles or more from the
     intruder (`dx² + dy² ≥ 0x9000` in 1/4096 units), or if it is not in state 56 (Library) or
     112 (Training Room);
   - if its bucket 5 is above a tenth of `def+0x76A`, it goes to state 59 or 109
     (`0x0048AD30`);
   - otherwise it is charged `0x00478B80(3, def+0x6C7, 3)`.

   `def+0x76A` is `UnhappyThreshold × 0x7FFF / 100` (the loader at `0x00494249`, from file
   `0xF4F`), which is `CreatureAngerRules.UnhappyAt`: the bucket-5 bar is 1638 for every shipped
   creature. The loader stores it only when `[EBP+0x198]` is set, and the record reader
   `0x00569041`–`0x005692D0` sets that field to 1 for every record read from file.

**Built:** the Library's half, with states 53–59 (ADR-0397, `library-states.md` §6). The Training
Room's (112 to 109) waits for the Training states 108–113.
