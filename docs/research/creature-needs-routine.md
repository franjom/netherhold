# Mechanic: the needs routine — how hunger, sleep and pay take a creature off its work

Source: `DKII.EXE` v1.70 (GOG), `0x00477C20` and its caller, the per-creature
dispatcher `0x00476FC0`, read in full with Ghidra 11.0.1 on 2026-09-24. Facts in
our own words (ADR-0046). Confidence is **STRONGLY INFERRED**: binary evidence,
not a running-game observation. The eat, bed and pay attempts it calls have their
own notes (`eating.md`, `lair-acquisition.md`, `payday.md`); this note owns the
routine that decides when they are tried.

## 1. Where it runs

The dispatcher, once a turn for each creature, after its suspend countdown
(`+0x44`) and the category update hook:

1. A creature whose definition has flag bit 0 (`IsWorker`) skips steps 2–3.
2. A creature of neither of two special players (globals `0x006F254C`,
   `0x006F2548`; the second runs `0x004790A0` instead) runs the need-anger routine
   `0x00478D00`, then **the needs routine**. If that returns 1, the state handler
   runs at once, and the mood chooser is not asked this turn.
3. Otherwise, under a list of gates, the unhappy or angry chooser. The happy one is
   state 1's own (`job-selection.md`).
4. The current state's handler; a 0 is `SetState(1)`.

So a need is checked **every turn, whatever the creature is doing**, and the only
things that stop it are the routine's own tests.

## 2. The tests

The routine returns 0 at once for a state of **group 7**, the needs' own states
(eating, the lair, sleep, payday). Otherwise each need asks the transition rule's
differing-tags test about **its own category**: Eating 8, Sleeping 9, Payday 20.
It uses the creature's category, taken through a walk to the state the walk
resumes (`+0xDF`). The move is admitted when the categories are equal, when the
target's mask is zero, or when the mask names the creature's category's bit.

All three masks are **`0x800A3C3B`**. They admit categories 0 (the torturer's
127–129), 1 idle, 3 worker jobs, 4 Research, 5 Manufacture, 10 Sulking, 11 Guard
Post, 12 Temple, 13 Training, 17 Casino, 19 and 31 Spectating. They do **not**
admit 2 movement (a walk is judged by its resume), 6 Lair, 7, 14 Prison,
15 Torture, 16 Combat Pit, 22 Combat and the rest.

Hunger additionally refuses category 8, and sleep category 9. Payday has no such
test; only the group-7 skip keeps a creature being paid from being asked again.

Every attempt is also refused by the **busy test** `0x00477B40` (through
`0x0048F380`), which the pay branch checks too. It refuses a state of **group 1**
(captives, the torture devices, leaving, the pit fighter), a creature held in a
prison (`+0x3CB`), one whose category through a walk is 22 (combat), and a set of
creature fields and flags: `+0x482`, `+0x45B`, `+0x3F2`, `+0x3E6` bits 3, 14 and 15,
bit 26 unless `+0x46D` has reached 5 seconds, and two calls, `0x0048F350` and
`0x004888C0`. Named by the debug dumper and read on 2026-09-24: `+0x482` is
`PossessedCreatureFollowing`, `+0x45B` `CreatureAlertTimer` (set by `0x0048CC10`, counted
down by `0x0048E160`), `+0x3F2` `CasinoBigWinnerGameTickRemaining`, bit 3 `RebelActive`,
bit 14 `Leaving`, bit 26 `WasLastDroppedOnARoom`. State 76's handler writes bit 26 from
the landing tile (whether its word `+2` names a room, `0x0046EF1E`), and the creature
update holds `LastDroppedCounter` (`+0x46D`) at 0 while in 76 and counts it up each turn
after (`0x00489A4D`). `0x0048F350` is the stunned states 209, 255, 256 and 257;
`0x004888C0` is a Call To Arms: player `+0x76` bit 2, `CallToArmsOn` (bit 20), or states
190–192 through a walk.

## 3. The three needs, in order

**Hunger.** The level-scaled `HungerFill` (definition `+0x66D`, level row at
`+0x171`), when non-zero:

- **The clock.** Once `HungerRate` (definition `+0x5CE`) has passed since `+0x48`,
  and the state's flag byte lacks bit 3, `+0x48` is set to now and `HungerFill` is
  added to `+0x4C`. With bit 3 set the stamp is not moved, so the time still
  passes: a helping overdue on leaving the state lands at once.
- **The attempt.** When `+0x4C` has reached `HungerFill`, the mask admits it, and at
  least 4 turns have passed since `+0x50`: `+0x50` is set to now, and unless
  the busy test refuses, `0x004633D0` tries to eat. Success returns 1.

**Sleep**, when the definition's `+0x5D6` is non-zero:

- Once `+0x5D2` has passed since `+0x54`, and the flag byte lacks bit 4, `+0x58` is
  set to `+0x5D6` and `+0x54` to now.
- While `+0x58` is non-zero and the category is not 9, each turn takes
  `CannotSleepModifyCreatureHealthPerSecond` ÷ variable 19 of health through
  `0x004B2CE0`, unless creature `+0x3E6` has both bits 21 and 15.
- The attempt: the mask, the category not 9, 4 turns since `+0x5C`, the busy test,
  then `0x0046F440`, the bed. Success returns 1.

**Pay**: the mask, and `+0x60` reached. `+0x60` is set to 4 turns on, then
`0x00472320` runs (`payday.md` §4). This branch **always returns 1**.

**Last**, the wounded: `0x00488840` compares health × 100 ÷ the level-scaled
maximum (definition `+0x692`) with `CreatureSleepsWhenBelowPercentHealth` (50,
`0x006F28CE`). Below it, a bed attempt with the Sleeping mask, the category test and
the busy test, but no 4-turn gate.

## 4. Which states stop the clocks

Bit 3 (`SuppressHungerClock`) and bit 4 (`SuppressTirednessClock`) are set together
only on the Casino family, 145–159. A gambler's hunger and tiredness wait until it
leaves.

## 5. Implementation (ADR-0198)

- `NeedRules.Admits` is §2: the group-7 skip, the busy test's group 1, the resolved
  category, the per-need self test, and the rule's three arms. The busy test's
  creature fields are our activity filters (held, imprisoned, down, fighting,
  leaving).
- **Hunger, sleep and pay interrupt a job.** A creature at a job, or walking to one,
  is asked whenever its state admits the need. Hunger sets 91 and pay sets 187
  directly from the job's state, as `0x004633D0` and the payday check do. The job
  ends.
- The hunger clock stops in bit-3 states. Tiredness is not felt in bit-4 states:
  ours asks whether it is tired at the moment of the decision, where DK2 latches
  `+0x58` when the period passes, so a creature tired before it sat down at a table
  would still be sent to bed by DK2.
- The order of our systems is DK2's: eat, then sleep, then pay.
- **The cannot-sleep drain** (ADR-0199), `NeedRules.CannotSleepHealthPerSecond`: variable
  82 ÷ variable 19 a turn, truncated as `IDIV` does (`0x00477E74`), so −5 is −1 a turn
  and −3 nothing. Keepers' non-workers only, not in group 7, not Sleeping through a walk,
  and with or without a bed. Applied once a second by `CreatureHealthSystem`. Ours reads
  the latch `+0x58` as `NeedRules.IsTired`, and the drain skips prisoners, whose health
  `PrisonSystem` owns. Zero in every shipped level.

- **Pay every fourth turn, and the drop grace** (ADR-0202). `+0x60` is
  `CreatureState.NextPayTryTick`, stamped four turns on before the decision whether or
  not anything is owed, then the busy test. `NeedRules.IsBusy` is the drop grace: a
  creature dropped on a room tile is not asked for five seconds; the eat, sleep and pay
  attempts test it after their own stamps.

**Not built:** the alert timer, possession, rebels and the big winner in
the busy test (the Call To Arms is read, ADR-0279) (each is one of our activities or unbuilt); the wounded attempt's missing
4-turn gate (ours shares the tired path). Creatures still
on our own activities (combat, leaving, fleeing) are asked by none of the three, as
before; they are not on state ids yet.
