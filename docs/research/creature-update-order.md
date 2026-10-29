# The creature update's order, and ours (ADR-0554)

**Scope:** the order in which one turn runs each creature's fight, needs, choosers, state handler
and steering, and how ours mirrors it. What each step does is in its own note: combat in
`combat-acquisition.md`, the needs in `creature-needs-routine.md`, pay in `payday.md`, the Imp's
walk in `imp-task-objects-and-walks.md`, and the sweep in [004B7830](functions/004B7830.md).

## 1. DK2's order

| Step | Where | Confidence |
|---|---|---|
| Payday clock, once per player | `0x004BE1A0` from `0x0050A450` at `0x0050A7BE`, before the sweep | CONFIRMED |
| Creature sweep, newest first, one whole update at a time | `0x004B7830` at `0x0050A7D7` | CONFIRMED |
| Fight scan | `0x0045E9F0` at `0x00489AC7` | CONFIRMED |
| Dispatcher: category hook, need-anger `0x00478D00`, needs routine `0x00477C20`, unhappy or angry chooser, then the state handler | `0x00476FC0` at `0x0048A030` | CONFIRMED |
| Physical commit | `0x004B2030` at `0x0048A204` | CONFIRMED |
| Steering step; its gate skips the Imp (`+0x36F == 1`) | `0x0048A266`, gate `0x0048A210`-`0x0048A248` | CONFIRMED |
| Imp's step: inside state 6's handler, then the arrival test and the resume, in one call | `0x00470C80` | CONFIRMED |

In the dispatcher, a success in the needs routine still runs the handler, on the new state. A
success in a mood chooser skips the handler for that turn.

## 2. Ours

`CreatureUpdateSystem` runs its steps one creature at a time, newest first by id. The steps, in
declared order:
1. `CombatSystem`: the action runner, the scan and the fear radiation;
2. `CreatureEatSystem`, `CreatureSleepSystem`, `PaydaySystem`;
3. `CreatureAngerSystem`;
4. `CombatSystem.States` (the combat states, then the trap reveal), `TremorCreature`, `Reaper`,
   `Finale`, `CombatPit`, `JackInTheBox`, `CasinoJackpot`, `Hero`, `CreatureJob` (ADR-0555);
5. `MovementDust`, `CreatureMovement`;
6. `ImpTask`.

`PaydaySystem.BeginTurn` is the clock. `ManufactureDecaySystem` is in the player frame.

### Where ours still differs

- Ours charges the need-anger after the needs routine, not before it, and before the health
  system. So a creature that claims a bed this tick is not charged `AngerNoLair` that second, and
  a creature that collapses this tick can still be charged.
- The angry chooser runs only on the last tick of a game second.
- A chooser's success does not skip the state handler.
- The Imp hold keys on the state at the start of the update, where DK2 keys on the state at the
  handler call. The two differ when the fight scan moves an Imp out of state 6 and back within one
  update. States 7 and 265 are not held, and their handlers are unread.
- These still run as passes over the whole list: status countdowns (`0x0048E160`), footprints,
  Call to Arms' heeding states, state 96, gold underfoot, health, prison and experience.
- The clock's collect decision keeps our mask test and busy test. `0x00472320` makes neither.
- The fireworks run after the last creature's update, not in the player frame.

## 3. Measurements

### One-tick A/B before the change

Source: `D:/DK2Evidence/runs/order-ab-jobs-2026-10-27`. Level 4, share of sampled ticks whose
state hash changed:
- moving the job systems to their slot in the creature update: 90%;
- also interleaving per creature: 28%.

### Walk-start timing

Source: `D:/DK2Evidence/runs/walk-start-timing-2026-10-27`, Level 2 and Level 4, seed 1. Each cell
counts walks that stepped on the tick they started, out of all walk starts.

| | native | before | after |
|---|---|---|---|
| L2 Imp | 0 / 871 | 190 / 932 | 5 / 622 |
| L4 Imp | 22 / 3020 | 132 / 2447 | 5 / 2257 |
| L2 other | 897 / 2215 (40%) | 20 / 2005 (1%) | 841 / 2496 (34%) |
| L4 other | 1190 / 4216 (28%) | 63 / 3067 (2%) | 841 / 3235 (26%) |

- Imp withdrawals from 203 no longer step on their first tick: 0 of 142 (native 0 of 24).
- Imp job walks from state 10 still never step on their first tick.

### The combat states in the dispatcher (ADR-0555)

Source: `D:/DK2Evidence/runs/q665-imp-flee-2026-10-27`, same rig. After ADR-0554, 151 of the Level 4
Imps' entries into 199 came from a walk, and 142 of their 173 entries into 199 fled one tick
later. Native Imps reach 203 straight from a walk. Running the fight state in the scan's update:

| Imp walk starts | native | ADR-0554 | ADR-0555 |
|---|---|---|---|
| Level 2, all | 871 | 622 | 852 |
| Level 2, from 87 | 124 | 5 | 122 |
| Level 4, all | 3020 | 2257 | 2165 |
| Level 4, from 87 | 809 | 167 | 41 |
| Level 4, from 203 | 14 | 100 | 149 |

### What remains on Level 4 (Q665)

Scripts and outputs: `D:/DK2Evidence/runs/q665-imp-flee-2026-10-27/analysis/`. Level 4, seed 1, against
native, three measured gaps (a replay resynced from DK2's tick 2340 matches native's job mix to 3100:
`resync-replay.md`). Seed 1 only: over 8 native seeds DK2 loses 0-64 Imps itself and flights are alike
([level4-outcomes.md](level4-outcomes.md)).

- **Imp deaths.** Ours loses 32 Imps over the run (15 in ticks 4000-4750) against 0 native; at hash
  318 it loses 45, with or without ADR-0559 (the resyncs into the window: `resync-replay.md`).
  - 184 of our 236 Imp entries into 201-203 fall in 4000-5999, against 41 native over the whole run
    (25 into 203 alone).
  - Resynced at 3520 with every creature copied, 4 are lost, three to party 5's Guard (ours'
    Thief stood nearer and drew the Elves); resynced at 3790 one was lost in the first run and none at hash 322 (ADR-0560,
    `resync-replay.md`, "The Guard fight"). The full replay's deaths look like drift (one seed, not
    tested over the same window); who kills them there is not traced.
- **Mining over reinforcing.** In ticks 3000-4000, with as many Imps as native and before the deaths,
  ours starts 210 walks to mine (state 25) and 25 to reinforce (21); native starts 121 and 75
  (`walk_starts_resume.py`, the hash-316 trace 7a28fcad).
  - Resynced at 3000 or 3400, ours reinforces at DK2's rate, so the unsynced shortfall is drift
    ([resync-replay.md](resync-replay.md), "Mining against reinforcing").
  - Our reinforcing falls behind and is still running after tick 6000, when native Imps idle in state
    12 for 27% of Imp-ticks and ours for 0.3% (`imp_states.py`, `state_ticks.py`).
  - 389 of native's 809 Imp walks from 87, which follow idling, come after 6000, when ours has 13 Imps.
- **Where the drift starts** (one seed: Level 4 seed 1, hash 318, ticks 1-3520). Evidence:
  `D:/DK2Evidence/runs/q665-job-choice-2026-10-28/README.md`, "Where the seed-1 drift starts".
  - Imp levels are not the source: native 34 Imp level-ups to 3520, ours 35, spread alike. Pairing
    Imps one to one is arbitrary, so "six of ours above DK2's level" was a pairing artefact for Imps;
    other species' levels were not compared.
  - Ours has 15 Imps at 3520 against 18 because three Create Imp casts are refused.
    - 1374 for mana by a 79-mana margin (7421 against 7500; native 8492).
    - 3231 and 3471 for location: (19,33) was not yet dug, (12,33) untouched.
  - The mana gap (458 at 800, about 1100 from 1000) comes with fewer claimed-path tiles (55 against
    41 at 800, 71 against 51 at 1000). STRONGLY INFERRED: only net mana is compared.
  - The first difference is at the level's first dig, (28,54), marked at 131: first strike native
    138, ours 155 (`imp_jobs_seq-1-400.out`). DK2's Imp 455 stood two ticks from the wall. That lag
    closes by about 350, and ours is ahead in claimed tiles at 400-500.
  - The claim deficit opens at about 550, where the marked digs are taken in a different order: ours
    digs (24,46) 17 ticks after its mark (557 against native 700). DK2 digs (18,53) at 551 and
    (17,52) at 571; ours digs them at 628 and 656.
  - That order comes from the north seam crew's size, which is fixed at about 374 (README, "Why the
    marks at ~550 are taken in another order"). The seam (31,54) falls at 305 in DK2 and at 324 in
    ours, with different Imps at its face. The walk `0x00469AF0` ranks walls from the Imp and seams
    from the last seam's centre, the same as ours, so:
    - DK2's Imp at the north face, (31.16,53.83), takes the seam (30,55): wall d2 2.90 against
      the seam's 2.0.
    - Our Imp 1 stands at the same point and takes the seam. Our Imp 3, at (30.83,54.83) on the
      west face (resume trace), takes the wall (31,55): d2 0.89. So (30,55) falls last.
    - (30,55) falls with no marked wall beside it, so its kind-14 claim can be taken on the way,
      and Imp 2 takes it and claims to about 568 instead of mining. In DK2 (30,55) is not
      claimable on the way until (31,55) falls at 350; whether it was raised is not read.
    - Two Imps mine the north seams in ours, three in DK2, so ours is still beside (24,46) when it
      is marked at 540.
    STRONGLY INFERRED (one seed, no counterfactual run): every step is the faithful rule from each
    side's state. Other Imps stood at the face when (31,54) fell because of the first-dig lag, which is
    RNG (next bullet).
  - The first dig and RNG, STRONGLY INFERRED (README, "The first dig from DK2's state"):
    - DK2's own first state 14 moves between games: 138 in seed 1, 151 in seed 2, on the same
      inputs.
    - Over 30 seeds of ours it falls at 136-157. DK2's 138 is inside that range, at about the 10th
      percentile (3 of 30 at or before it; one-sided p about 0.1).
    - From DK2's points (resync at 130), take to strike is 3 ticks beside the wall on both sides;
      from x 21 it is 16-17 ticks in DK2 and 15 in ours.
    - State-12 spells take the same four values (1, 20, 25, 26 ticks) on both sides; the means are
      not distinguishable at n 17.
    - The wander rule `0x004808E0`/`0x00480A00` matches ours. Ours stands terrain solidity in for
      nav bit 3, which is the same on this map, and the room-branch helper is unread. DK2's seed 2
      ends 6 of its 23 walks over ticks 1-200 on (24,51), the tile seed 1's Imps never chose.
    - So the lag is where the idle cycle leaves the Imps at 131. The exact draw is not replayed.
- **Native spread.** Native seed 2, on the same inputs, made 349 entries into 203 alone (472 into
  201-203). Its Imp deaths count 8 by slot disappearance, not the 19 that imp-fidelity-integration.md
  gives.

Non-Imp 8 → 199 is 524 against 291, mostly after an Imp target or a lost one. It falls in the same
window as the deaths; whether it is the same fight is not tested.

Walks from 173 still take their first step on their first tick slightly less often: 14% against 19%.
