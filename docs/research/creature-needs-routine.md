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
- **When the clock starts** (Q102, read 2026-10-04, CONFIRMED). At spawn.
  `CreatureState_Construct` `0x00476B40` is called only from `Creature_Construct`
  (`0x004873C5`). It stamps `+0x48` with the current turn rounded down to a multiple of 8,
  ORed with the creature's entity id & 7, so creatures are staggered across 8 turns.
  `+0x4C` starts at 0, and the same stamp goes into `+0x50`, `+0x54`, `+0x5C` and `+0x60`.
  Two later writes move the stamp: the clock above (`0x00477D2D`) and eating a chicken
  (`0x00463760`, at `0x004639B8`). No other store to the state object's `+0x48` was found in
  `.text`, so a pick-up or drop does not reset it (cseg was not scanned). Ours starts the
  clock at spawn with no stagger.
- **The attempt.** When `+0x4C` has reached `HungerFill`, the mask admits it, and at
  least 4 turns have passed since `+0x50`: `+0x50` is set to now, and unless
  the busy test refuses, `0x004633D0` tries to eat. Success returns 1.

**Sleep**, when the definition's `+0x5D6` is non-zero:

- Once `+0x5D2` has passed since `+0x54`, and the flag byte lacks bit 4, `+0x58` is
  set to `+0x5D6` and `+0x54` to now.
- **Latch writers, rechecked 2026-10-29 (CONFIRMED binary).** The constructor
  initializes `+0x58` to zero; the needs routine loads it at `0x00477E10`;
  `0x00477B20` subtracts with a zero floor. State 83's category hook calls the
  subtractor with 1 each turn, while its handler refreshes `+0x54` to now
  (`0x0046FAB5`, `0x0046FB26`). Entering sleep does not reload the latch. A
  full-health creature manually directed to an ordinary bed with zero debt
  can therefore finish on its first state-83 handler call, without spending
  `TimeSleep` there. A wounded one remains until fully healed. A partial sleep
  retains its unpaid debt. The slap's other subtractor call is unreachable
  after its state move; see [slap-states.md](slap-states.md) section 1.
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
the busy test, but no 4-turn gate (`0x00477F94`–`0x00478023`). The attempt is the same
`0x0046F440`, and nothing on the path writes `+0x58` (re-read 2026-10-26, CONFIRMED):

- **With a bed** it routes only while `+0x58` is non-zero, so a wound alone does not send
  a bedded creature to bed; it only drops the tired branch's 4-turn gate.
- **Without a bed** it calls the lair attempt `0x0046F390`, which does not read `+0x58`. State 80 places
  the bed and then asks the same wound test (`0x0046F785`): hurt is 81, so a bedless
  wounded creature does sleep untired.

## 4. Which states stop the clocks

Bit 3 (`SuppressHungerClock`) and bit 4 (`SuppressTirednessClock`) are set together
only on the Casino family, 145–159. They suppress the overdue update, without
advancing its timestamp; the update can happen immediately after leaving.

The earlier **group-7 return** (`0x00477C5F`–`0x00477C66`) also skips both
updates, including eating 91–95, active lair/bed states 79–84 and payday
187–189. Their elapsed time still passes. A travel state has group 2, so the
same skip does not automatically cover a walk resuming one of these states.
Captivity is different: the usual prisoner states are group 1 with neither
clock flag. The busy test blocks their attempts, but their overdue hunger and
sleep-latch updates still run. These distinctions are CONFIRMED from the whole
routine and state table, rechecked 2026-10-29; a blanket captivity clock pause
is not supported.

The tired need's retry is **4 native turns**, independently of the idle lair
search (`0x00477EF5`–`0x00477F0C`). State 89 tries a bedless lair search before
jobs with no such retry gate; its later idle fallback has a 12-turn deadline
([idle-fallback.md](idle-fallback.md) section 1). Neither is a universal
one-second throttle for autonomous lair attempts.

## 5. Implementation (ADR-0198)

- `NeedRules.Admits` is §2: the group-7 skip, the busy test's group 1, the resolved
  category, the per-need self test, and the rule's three arms. The busy test's
  creature fields are our activity filters (held, imprisoned, down, fighting,
  leaving).
- **Hunger, sleep and pay interrupt a job.** A creature at a job, or walking to one,
  is asked whenever its state admits the need. Hunger sets 91 and pay sets 187
  directly from the job's state, as `0x004633D0` and the payday check do. The job
  ends.
- **Persistent sleep debt (ADR-0579, hash/save 330):** `LastSleptTick`,
  `SleepDebtTicks` and `NextSleepTryTick` replace the awake counter. Debt is
  loaded when overdue, preserved across entry, interruption, capture and rescue,
  and reduced only in state 83. That handler refreshes the last-sleep stamp;
  waking requires zero debt and full health. `SleepTicks` is only a session counter.
- Hunger is checked every tick. Raw group 7 and the respective clock flags
  defer overdue hunger/debt updates without stopping elapsed time. Captivity
  permits clock updates but blocks bed attempts. Tired attempts stamp the
  four-turn retry before checking busy; the wounded branch can bypass the gate.
- **Remaining clock gaps:** constructor stamps still use the spawn tick rather
  than the native entity-id eight-turn stagger (Q102). Hunger updates still run
  in the health pass after creature handlers, rather than ahead of sleep/pay
  decisions in the native needs routine (Q674). Native capture timing has not
  been compared for this change.
- Need attempts run eat, then sleep, then pay, as steps of `CreatureUpdateSystem`
  inside each creature's update (ADR-0554); the payday clock's own call skips the stamp and
  runs before the creatures ([payday.md](payday.md) §2).
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
the busy test (the Call To Arms is built, ADR-0279) (each is one of our activities or unbuilt); a bedless
wounded creature's lair attempt from a job (ours tries only from idle, Q150). Creatures still
on our own activities (combat, leaving, fleeing) are asked by none of the three, as
before.
