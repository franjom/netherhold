---
systems: [creatures, heroes, rooms]
---
# The Wait hold: who can be held by SuspendTime

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70, read 2026-11-13: the dispatcher `0x00476FC0`, `Job_Dispatch` `0x00478800` (13 call sites), `Job_EnterWait` `0x00486CC0`, chooser 89 `0x004778E0`, state 1 `0x004773D0`, state 114 `0x00473130`, `Room_DropCreature` `0x004E8DA0`, the Prison handler record at `0x006A2062`. `StoreAt` census of `+0x44`, `+0x6E` and `+0x2BC` over `.text` and `cseg` |
| Implementation | None. `HeroJobRules.Wait` has no hold, and ours has no Prison drop arm (`DropLandingRules.AfterGettingUp` idles). Q499, Q674(c) |
| Confidence | **STRONGLY INFERRED** for every step below, from the reads cited. The keeper path as a whole has not been seen in a native run |

## 1. The hold

The dispatcher `0x00476FC0` tests the state object's SuspendTime `+0x44` first. If it is
non-zero, it decrements it and returns 1 before the category hook, the needs routine and
the state handler (`0x00476FD4`, `0x0047700E`–`0x0047701E`). Four places write the field:
- the constructor `0x00476B7D` and `SetState` `0x00476F9B` set it to 0;
- the dispatcher counts it down;
- `Job_EnterWait` `0x00486CDC` sets it to the game turn + 1.

The other 798 stores to `+0x44` are other structures.

So only job 22, Wait, starts a hold, and a hold started at turn T lasts T + 1 turns.

## 2. Who dispatches job 22

`Job_Dispatch` has 13 call sites:
- **State 90** (`0x00477882`) and **state 226** (`0x004787FA`) pass the objective
  `+0x409`. State 1 enters 90 only for the hero player (`0x004773D0`), and that is the
  only literal `SetState(90)`. State 226 most likely belongs to party followers: two of
  its five entry sites (`0x004584E0`, `0x00458CD8`) are gated on the party-member flag,
  and the other three were not read.
- **The fallback `0x00478680`** passes only 4, 26 and 9.
- **The three mood choosers** pick from the definitions' job tables. No shipped
  `Creatures.kwd` record lists job 22. Per-level creature overrides were not scanned.
- **Chooser 89** passes `DroppedToDoJobId` (`+0x6E`, creature `+0x2BC`), then
  `ReturnToJobId` (`+0x68`). `+0x68` has only two writers, the constructor and a
  literal 6 at `0x00462919`, so it never holds 22. `+0x6E` is the keeper path.

## 3. How a keeper's creature gets `DroppedToDoJobId` 22

1. State 76 (`0x0046EAB0`) handles a hand drop. On a creature's own tile it skips the
   enemy-room block. For a non-worker (or a worker whose owner's `+0x10FC` is below 3)
   it asks `Room_DropCreature(1)`, then calls `Room_DropCreature(0)`. The Prison arm
   `0x004E8E82` takes the creature over when `Room_IsFloorSlot` `0x00454110` is true:
   a Prison cell whose `+0xC` byte is `0xFF`. Nothing tests whether the room's owner
   is the creature's owner.
2. The Prison record holds drop state `+0x19` = 114 and job `+0x25` = 22.
   `Room_DropCreature(0)` sets state 1, then 114, and stores 22. Four things can
   refuse it, and each returns before the store:
   - a non-zero casino countdown `+0x3F2`;
   - `SetState`'s own gates;
   - the Prison enter hook's claim, when the Prison is full;
   - the hated-jobs list `0x00494480` (keeper creatures only). No shipped definition
     hates job 22.
3. State 114 enters 115 and makes the Prison's owner the jailer, with no ally test.
   A keeper's own creature becomes its own keeper's prisoner.
4. The captive loop 115–117 never clears `+0x6E`. Only three places clear it:
   chooser 89 when the job is refused (`0x00477A57`), state 121 walking out
   (`0x004739E0`), and the Torture leave hook unless the creature is there voluntarily
   (`0x0047B532`).
5. The hand may lift its keeper's own prisoner. The pick-up predicate
   `Creature_PickupPermitted` `0x0048E6A0` lets a keeper lift its own creature when
   the creature holds no room (`+0x3B9`) or that room's owner is the keeper
   (`0x0048E794`). 114 and 115 are not among its refused states, and neither
   `0x004BC500` nor `0x004BFBD0` tests imprisonment. `Command_PickUpCreature`
   `0x00513480` zeroes `+0x68` and `+0x6C`, but not `+0x6E`.
6. Dropped on ordinary floor, the creature is stunned (`0xFF`). 257's tail calls
   `Room_DropCreature(0)` (`0x00481351`). With no room there it takes the 250 path
   (`0x004E9097`), which writes no `+0x2BC`, so the 22 stays.
7. 250 returns 0 after its deadline, and the dispatcher sets 1. State 1 sends a
   keeper's non-worker to 89, and 89 calls `Job_Dispatch(22)`, then `Job_EnterWait`.
   `SetState(176)` commits: state 176's category 18 has entry mask `0x3`, which
   admits category 1 (bit `0x2`), and category 18 has no enter hook. The hold is
   armed. A keeper under Call to Arms is refused at this step.
8. A successful dispatch does not clear `+0x6E`. State 176's handler `0x00486CF0`
   wanders on its own deadlines and touches nothing else. Every later return to 89
   dispatches Wait again and re-arms the hold at the later turn.

While held, the creature's needs routine does not run (Q674(c)), and its state age
`+0x1C` does not advance. While `+0x2BC` is non-zero, `Creature_Update` (`0x004895E4`)
also gives a non-worker the dropped-job work bonus (`+0x3B3`, `0x199`), so a trapped
creature keeps it.

## 4. Why it reads as an original defect (issue #2, DK2-020)

The only writer stores an absolute game turn, and the only consumer counts it down.
The same code family stores deadlines as absolute turns and compares them with the
turn (176's `+0x7E`), so this looks like a mix-up of units, not a style. The hold length
grows with elapsed level time. State 176's own 50–84-turn deadlines would serve no
purpose under an indefinite hold. Nothing in the binary proves the intent.

## 5. Not read

- Whether a fight breaks a hold. A hand pick-up does: `SetState` zeroes `+0x44` (Q499).
- What `+0x453` and `+0x4B3` hold for a captive (two pick-up refusals).
- Per-level creature overrides of the job tables; save/load of `+0x44`.
