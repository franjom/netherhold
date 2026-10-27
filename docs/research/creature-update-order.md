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
native, three measured gaps:

- **Imp deaths.** Ours loses 32 Imps over the run (15 in ticks 4000-4750) against 0 native.
  - 184 of our 236 Imp entries into 201-203 fall in 4000-5999, against 41 native over the whole run
    (25 into 203 alone).
  - Who kills them is not traced.
- **Mining over reinforcing.** In ticks 3000-4000, with as many Imps as native and before the deaths,
  ours starts 210 walks to mine (state 25) and 25 to reinforce (21); native starts 121 and 75
  (`walk_starts_resume.py`).
  - Our reinforcing falls behind and is still running after tick 6000, when native Imps idle in state
    12 for 27% of Imp-ticks and ours for 0.3% (`imp_states.py`, `state_ticks.py`).
  - 389 of native's 809 Imp walks from 87, which follow idling, come after 6000, when ours has 13 Imps.
- **Native spread.** Native seed 2, on the same inputs, made 349 entries into 203 alone (472 into
  201-203). Its Imp deaths count 8 by slot disappearance, not the 19 that imp-fidelity-integration.md
  gives.

Non-Imp 8 → 199 is 524 against 291, mostly after an Imp target or a lost one. It falls in the same
window as the deaths; whether it is the same fight is not tested.

Walks from 173 still take their first step on their first tick slightly less often: 14% against 19%.
