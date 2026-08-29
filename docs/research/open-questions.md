# Open research questions

Living list. Every entry needs an experiment or RE dig before implementation
may treat it as fact.

| # | Question | Status | Blocking |
|---|---|---|---|
| Q1 | Simulation ticks-per-second of DK2 v1.70? | UNKNOWN — Flame `limit_tps` proves fixed tick; value unmeasured | All timing fidelity (M5) |
| Q2 | Creature job-selection priority order? OpenKeeper uses lair→sleep→eat→work — theirs or DK2's? | HYPOTHESIS | M4 |
| Q3 | What RNG does DK2 use, and does its sequence matter observably? | UNKNOWN | Possibly nothing; revisit at M5 |
| Q4 | `CMapEntry` (11 bytes) — full per-tile runtime state or cache? Field meanings? | UNKNOWN | M3 terrain runtime |
| Q5 | `Follow_Path` (36 bytes) contents; pathfinding algorithm | UNKNOWN — the big one | M9 pathfinding fidelity |
| Q6 | `f3AF_workModifier` vs `f3B3_workEnhancement` semantics | UNKNOWN | Training/work rates |
| Q7 | Creature mood/anger model (nobody has solved this) | UNKNOWN | M8 |
| Q8 | Combat resolution formula (damage, armour, hit timing) | UNKNOWN | M7 |
| Q9 | Payday procedure & timing (`f3E2_getPaid`, `SkipNextPayment`) | PARTIAL | M4 economy |
| Q10 | GOG exe = v1.70 byte-for-byte? MD5 `2f3bbc21eae8c3aa468136baa303036d` (DKII.EXE, 2,987,568 bytes) recorded; verify against Flame's expectations before memory-probing | STRONGLY INFERRED (GOG ships 1.7) | M5 oracle work |
