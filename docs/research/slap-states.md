# The slap's states: 67, 77, 120

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-10-07.
- **Built:** ADR-0401, `SlapRules` (Q569, owner 2026-10-07: build the slap's state moves).
- The slap is `Hand_SlapCreature` `0x0048B8A0`; its arms before the move are ADR-0373
  (`HandSystem.OutcomeOf`). The reaction is `0x0046F1F0`, the handler of 77 and, through the thunk
  `0x00470C00`, of 67 and 120 (`creature-state-table.md`).

## 1. The move — CONFIRMED

After the early returns (a Horny to 242; states 76, 101 and 193 nothing; 209 and 255 the short
path), by the room the creature holds a place in (`+0x3B9`):

| place | move |
|---|---|
| none, in 67, 120, 132 or 197 | nothing: the slap returns |
| none, otherwise | 77 (`0x0048B9FE`) |
| a room its slapper does not own | nothing |
| its slapper's Workshop (type 10) | a running action ended (`0x00496D00`, `0x00496D20`), then 67 (`0x0048B9DB`) |
| its slapper's Prison (type 11) | a running action ended, then 120 |
| its slapper's Torture Chamber (type 12) | the damage only, and return |
| another of its slapper's rooms | 77 |

The move is `SetState` (`0x0048BA00`). **Refused, the slap returns**: no knock-back, slap turn,
anger, heading, damage, speed, sound, count or trigger (`0x0048BA0B`). Taken, the state-83 arm
(`0x0048BA1A`) tests the state just set, which is the move's, so it never matches after one of these
moves. Then the rest of the slap as ADR-0373 has it.

The transition table refuses 77 from 71, 207, 208, 216–219 and 246 (among them the dying 216 and
217 and the frozen 218), so a creature in one of those takes no slap at all.

## 2. The reaction `0x0046F1F0` — CONFIRMED

1. At `Age` below 2: a running action ended, action 19 (`0x13`) started (`CreatureAction_Begin`).
   Action 19's record plays art slot 4 (byte `+0x14` of record 19, `0x006E43D3`, written by the
   filler at `0x004956A0`; `creature-action-table.md`).
2. While an action runs: for each door (thing class 4, `traps.md` §11) on its tile that is not its
   own owner's or whose lock mode (`+0x111`, `doors-and-traps.md`) is set, its velocity is zeroed:
   the knock-back stops at such a door. Returns 1.
3. Ended: velocity zeroed. A Horny (`def+0x686` bit 14) to 238. A place in a Workshop to 63, a Prison
   to 115, a Torture Chamber to 130, each returning `SetState`'s answer. Otherwise a worker
   (`def+0x686` bit 0) to 10, 1 if taken; otherwise 0, idle.

The transition table refuses 10 from 77 (category 7 into WorkerJob), so a slapped worker in 77 also
idles, and 1 sends it to 10 on its next turn.

## 3. Ours

- `HandSystem.Slap` makes the move (`SlapRules.Move`) on the main path and treats a refusal as the
  slap ignored.
- `CreatureStatusSystem` runs the reaction for every creature in 67, 77 or 120 (activity `Slapped`),
  as it runs the get-up; the prisoner systems leave a captive in 120 to it.
- Action 19's length is `AnimationTimingRules.CountedHoldTicks` for art slot 4, count 0, LIKELY as for
  the other actions; it starts at the first turn after the slap (ours' `Age` 1).
- **Not modelled:** the knock-back's velocity (ADR-0373), so the door-tile stop in §2 step 2; a
  Horny is not slappable in ours (`HandRules.CanSlap`), so 242 and 238 are not reached.
- The move stops the creature's walk (`StopMoving`): DK2 moves only a walking state and the
  reaction zeroes the velocity, where ours' mover would follow a loaded route.
- A slapped Imp now pays the reaction and a fresh task choice before its faster swings: in the
  fixture it comes out ahead only on a wall 400 times as hard
  (`HandTests.ASlappedImpFlinchesThenDigsFaster_…`). The owner confirmed from play (2026-10-07) that
  a slapped Imp flinches, then digs on faster.
