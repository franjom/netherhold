# Open research questions

Living list. Every entry needs an experiment or RE dig before implementation
may treat it as fact.

| # | Question | Status | Blocking |
|---|---|---|---|
| Q1 | Simulation ticks-per-second of DK2 v1.70? | UNKNOWN — Flame `limit_tps` proves fixed tick; value unmeasured. **Negative result (2026-08-29):** the VARIABLES id space has an id 74 named `GameTicks` in the community template, but no shipped file carries it — not GlobalVariables.kwd, not any of the 71 map KWDs. Q1 cannot be answered from KWD data; it needs measurement against the running original | All timing fidelity (M5) |
| Q2 | Creature job-selection priority order? OpenKeeper uses lair→sleep→eat→work — theirs or DK2's? | **NARROWED TWICE** — (a) the candidate jobs and their percentage weights are DATA (happy/unhappy/angry tables per creature, see kwd-creatures.md); (b) **this does not cover workers at all**: the Imp's only job-table entry is `Explore` at 100%, so digging, claiming, carrying and the rest are engine behaviour attached to the `IsWorker` flag, not table entries (see imp-tasks.md). Still UNKNOWN for non-workers: re-evaluation timing, how mood picks the table, interaction with availability/reachability. For workers, see Q24 | M4 |
| Q3 | What RNG does DK2 use, and does its sequence matter observably? | UNKNOWN | Possibly nothing; revisit at M5 |
| Q4 | `CMapEntry` (11 bytes) — full per-tile runtime state or cache? Field meanings? | UNKNOWN | M3 terrain runtime |
| Q5 | `Follow_Path` (36 bytes) contents; pathfinding algorithm | UNKNOWN — the big one | M9 pathfinding fidelity |
| Q6 | `f3AF_workModifier` vs `f3B3_workEnhancement` semantics | UNKNOWN | Training/work rates |
| Q7 | Creature mood/anger model (nobody has solved this) | UNKNOWN | M8 |
| Q8 | Combat resolution formula (damage, armour, hit timing) | UNKNOWN | M7 |
| Q9 | Payday procedure & timing (`f3E2_getPaid`, `SkipNextPayment`) | PARTIAL | M4 economy |
| Q10 | GOG exe = v1.70 byte-for-byte? MD5 `2f3bbc21eae8c3aa468136baa303036d` (DKII.EXE, 2,987,568 bytes) recorded; verify against Flame's expectations before memory-probing | STRONGLY INFERRED (GOG ships 1.7) | M5 oracle work |

## New questions raised by the creature parser (2026-08-29)

| # | Question | Status |
|---|---|---|
| Q11 | Units/meaning of `moodChange` and `manaChange` on JobPreference records | UNKNOWN |
| Q12 | Is 32767 a general "never" sentinel for hungerRate/timeAwake, or Imp-specific? | HYPOTHESIS (sentinel) |
| Q13 | `expForNextLevel` is uniform 3000 (2800 uniques) — does per-level progression actually come from VARIABLES CreatureStats rows (StatType 13)? | STRONGLY INFERRED |
| Q14 | The 88 extra bytes in the embedded 5537-byte CREATURES record variant | UNKNOWN |
| Q15 | Purpose of the THINGS(190) chunk inside GlobalVariables.kwd (39 items) | UNKNOWN |

## Questions from terrain/rooms/map parsers (2026-08-29)

| # | Question | Status |
|---|---|---|
| Q16 | MAP tile bytes 3 (`flags`, bridge-related) and 4 — meaning? | UNKNOWN |
| Q17 | MAP `playerId` numbering: which id is neutral? (level1: p1=32, p2=1048, p3=60) | UNKNOWN |
| Q18 | `maxHealthTypeTerrainId` on Claimed Path points at Rock — does not fit the "upgrade at full health" reading. Second meaning for path tiles? | **ANSWERED** (2026-08-29) — no second meaning. The field is the destination of whichever *upward job* that terrain admits, and the conversion is triggered by the work rather than by the health level. Claimed Path's upward job is fill-in, whose result is rock. Evidence: the `FillInable` flag falls on exactly the five floor types, and six terrain types ship at full health pointing elsewhere, which refutes the level-triggered reading outright. See `tile-work.md` |
| Q19 | How often are the VARIABLES health deltas applied (per tick? per second?) — needed to turn dig/claim rates into real timings | UNKNOWN (tied to Q1). Now load-bearing: the M3 tile mechanics ship behind `SimulationTiming.WorkApplicationPeriodTicks` |

## Questions from the tile mechanics (2026-08-29)

| # | Question | Status |
|---|---|---|
| Q20 | `DigRockHealth` is −5000 against Rock's 165 health, so one application destroys it — digging earth would be instantaneous, which it is not in the original. Every other work rate is sane under a per-second reading. Is an application much shorter than a second, does a worker apply only a fraction of the delta, or does `DigRockHealth` not apply to terrain 2 at all? | **UNKNOWN — blocking real dig timings.** Implemented faithfully to the data; see `tile-work.md` |
| Q21 | Does a gold seam release its 3000 gold in proportion to the health removed, or in a lump when it collapses? | HYPOTHESIS (proportional) |
| Q22 | `FillInHealth` is 5, but every fill-inable tile is already at maximum health, so one application converts it. Does fill-in restart the health accounting? | UNKNOWN |
| Q23 | Is a dig marker per player or global? Assumed per player. | STRONGLY INFERRED |

## Questions from the first worker (2026-08-29)

See `docs/research/imp-tasks.md`. Q5 (pathfinding) is unchanged and still the
biggest of these; what is new is that it is now load-bearing rather than
theoretical.

| # | Question | Status |
|---|---|---|
| Q24 | **Worker job priority.** An imp digs what is marked, claims unclaimed floor, reinforces walls, carries gold to a treasury, drags bodies, and flees a fight. In what order does it prefer them, and what makes it start an unprompted one? Marked work is the only job whose trigger is not in doubt, so it is the only one implemented | **UNKNOWN — blocking the rest of imp behaviour.** Distinct from Q2: worker jobs are not in the creature job tables at all |
| Q25 | Of several marked tiles, which does an imp take? Implemented as fewest steps away, which is also what the search naturally yields. Could as easily be straight-line distance, or the order the tiles were marked in | HYPOTHESIS (nearest by path) |
| Q26 | Does anything reserve a job, so that imps spread across several marked tiles instead of converging on one? Implemented as no reservation, which is what the original visibly does | HYPOTHESIS (no reservation) |
| Q27 | Can a tile be worked from a diagonal neighbour, or only an orthogonal one? Implemented as orthogonal | HYPOTHESIS (orthogonal) |
| Q28 | Is DK2's creature movement four- or eight-connected — specifically, can a creature pass diagonally between two solid tiles? Implemented as four-connected, the conservative choice. Part of Q5 but separable and cheaply testable against the original | UNKNOWN |
| Q29 | Is a creature's `speed` in tiles per second? Three variables on the identical fixed-point scale name that unit in their own names (`BoulderSpeedTilesPerSecond` and friends), and the resulting imp/dwarf ratio matches the original | STRONGLY INFERRED |
| Q30 | Does terrain weight a path (water visibly slower), or only the mover's speed? Implemented as uniform step cost | UNKNOWN |
