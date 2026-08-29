# Open research questions

Living list. Every entry needs an experiment or RE dig before implementation
may treat it as fact.

**Many of these can be answered by playing the game.**
`observation-checklist.md` collects the ones a person in front of DK2 can settle
by looking, as concrete experiments, and says which ones playing cannot settle.
Check it before writing anything up as UNKNOWN.

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
| Q12 | Is 32767 a general "never" sentinel for hungerRate/timeAwake, or Imp-specific? | **ANSWERED for the Imp** (2026-08-29, owner observation): imps never eat, as a gameplay fact. The Imp's 32767 therefore does mean "never". Whether 32767 is a *general* sentinel wherever it appears on other creatures is still open, and cheap to settle the same way |
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
| Q20 | `DigRockHealth` is −5000 against Rock's 165 health, so one application destroys it — digging earth would be instantaneous, which it is not in the original. | **MEASURED, NOT RESOLVED** (2026-08-29, owner observation): one imp takes **2–3 hits and 1–2 seconds** for a rock tile, and **4–5 hits and 1.5–3 seconds** to mine out a gold tile. That puts a hit at roughly 0.4–0.75 s, i.e. about **two applications a second**, and at half the tabled delta a gold seam is 660/150 ≈ 4.4 hits — which lands exactly on the observed 4–5. The same reading leaves rock's −5000 wrong by about a factor of a hundred, so the anomaly is now *isolated to `DigRockHealth` alone* rather than casting doubt on the model. See `tile-work.md`; the half-delta/twice-a-second reading is an inference from these timings, not an observation, and is not implemented |
| Q21 | Does a gold seam release its 3000 gold in proportion to the health removed, or in a lump when it collapses? | HYPOTHESIS (proportional) |
| Q22 | `FillInHealth` is 5, but every fill-inable tile is already at maximum health, so one application converts it. Does fill-in restart the health accounting? | UNKNOWN |
| Q23 | Is a dig marker per player or global? Assumed per player. | STRONGLY INFERRED |

## Questions from the first worker (2026-08-29)

See `docs/research/imp-tasks.md`. Q24 is answered and implemented (ADR-0018);
Q27 is answered and needed no change. Q5 (pathfinding) is unchanged and still the
biggest of these; what is new is that it is now load-bearing rather than
theoretical.

| # | Question | Status |
|---|---|---|
| Q24 | **Worker job priority.** In what order does an imp prefer its jobs, and what makes it start an unprompted one? | **ANSWERED (2026-08-29, owner observation): marked excavation first — rock or gold alike — then claiming tiles, then reinforcing dungeon walls.** After that an imp mostly runs around the dungeon and interacts with the environment and with creatures. Implemented as a strict ordering (ADR-0018); the fourth behaviour is not (Q41). Carrying gold is not in the list because it is the tail of a mining job, not a job an imp chooses (Q31) |
| Q25 | Of several marked tiles, which does an imp take? Implemented as fewest steps away, which is also what the search naturally yields. Could as easily be straight-line distance, or the order the tiles were marked in | HYPOTHESIS (nearest by path) |
| Q26 | Does anything reserve a job, so that imps spread across several marked tiles instead of converging on one? | **PARTLY ANSWERED** (2026-08-29, owner observation): there is a hard cap — **three imps per face of a block, twelve on a block in all**. Whether a fourth imp goes elsewhere or waits its turn is the part still open, and it is what Q43 tracks. Not implemented; imps still converge without limit |
| Q27 | Can a tile be worked from a diagonal neighbour, or only an orthogonal one? | **ANSWERED — orthogonal** (2026-08-29, owner observation). Twelve imps on a block at three a face is four faces, and a tile has four orthogonal neighbours and eight if diagonals count. Implemented as orthogonal already; no change needed |
| Q28 | Is DK2's creature movement four- or eight-connected — specifically, can a creature pass diagonally between two solid tiles? Implemented as four-connected, the conservative choice. Part of Q5 but separable and cheaply testable against the original | UNKNOWN |
| Q29 | Is a creature's `speed` in tiles per second? Three variables on the identical fixed-point scale name that unit in their own names (`BoulderSpeedTilesPerSecond` and friends), and the resulting imp/dwarf ratio matches the original | STRONGLY INFERRED |
| Q30 | Does terrain weight a path (water visibly slower), or only the mover's speed? Implemented as uniform step cost | UNKNOWN |

## Questions from rooms and the treasury (2026-08-29)

See `docs/research/rooms-and-gold.md`. Q31 is the one that matters: it is the
other half of the gold loop, and it is the same UNKNOWN as Q24 wearing a
different hat.

| # | Question | Status |
|---|---|---|
| Q31 | **Where does mined gold go?** | **ANSWERED (2026-08-29, owner observation).** If the keeper has free capacity in a treasury or dungeon heart, the mining imp **takes the gold into its own hands up to its carrying capacity** (`maxGoldHeld`), runs to the drop spot and adds it to the store. If there is nowhere to put it, the imp keeps mining and **a lump accumulates on the floor in front of it**, up to a cap — candidate `MaxGoldPileOutsideTreasury` (2000), unconfirmed. So a pile is the *fallback*, not the first step, and carrying is the tail of a mining job rather than a job an imp chooses. Which tile of a multi-tile treasury the imp fills is still Q35 |
| Q32 | Is `RoomDef.Cost` per tile or per room? Implemented per tile, which is how the original charges as a keeper paints. Cheap to settle: build a two-tile treasury in the original and read the gold drop | STRONGLY INFERRED (per tile) |
| Q33 | Must a new room tile touch land the keeper already holds? | **ANSWERED — yes** (2026-08-29, owner observation): a room tile must start adjacent to a tile the keeper owns. That closes the bridge gap, where our rules previously let a keeper start a span anywhere on open water |
| Q34 | **Selling a room refunds 100% under both candidate numbers** — `RoomDef.ReturnPercentage` (100 for all 26 rooms) and `RoomSellValuePercentageOfCost` (100). | **ANSWERED — the refund is 50% of the cost, per tile** (2026-08-29, owner observation). Neither variable is the refund rate, so both are misnamed or mean something else, and **the number is not in the data at all**. Selling is still not implemented; when it is, 50% comes from the observation with the discrepancy recorded, not from either variable |
| Q35 | Which tile of a multi-tile treasury does a deposit land in, and which does a withdrawal drain? Ours is tile-index order. It is behaviour — it decides which gold survives a treasury being partly destroyed — and the original plausibly fills whichever tile the carrier reached | UNKNOWN |
| Q36 | Do a room's `RecommendedSizeX/Y` and `TileConstruction` constrain what a keeper may build, or only how it is drawn? | **ANSWERED — they do not constrain building** (2026-08-29, owner observation): a 1x1 temple is buildable in the original too. Our permissiveness is correct and unchanged. What the sizes *do* relate to is usefulness — some rooms need a minimum layout before they function, the owner believes 5x5 for a temple — which is a different question and is now Q45 |

## Questions from mana (2026-08-29)

See `docs/research/mana.md`. Q37 was the one with teeth and it is now answered,
by the owner rather than by the data: the ceiling is a flat 200000 from the first
second and never moves. Q37a is closed with it. What is left in this group is one
hypothesis (Q38), one unmeasurable (Q39) and one known rule nobody has
implemented (Q40).

| # | Question | Status |
|---|---|---|
| Q37 | **Does a keeper have a computed mana ceiling?** Three variables looked like contributions to one — `DungeonHeartMaxManaIncrease` (32000), `DungeonHeartManaGenerationIncreasePerSecond` (30) and `TerrainDef.MaxManaGain` — which would have made `MaximumManaThreshold` (200000) an absolute limit on top of a smaller per-keeper one. **ANSWERED (2026-08-29, owner observation of the original): 200000 is the ceiling every keeper has, and it does not grow with the dungeon.** The implementation was already flat and is unchanged. The same observation dissolved the heart's per-heart-or-per-tile ambiguity — a keeper has at most one heart, so its 30 a second is now implemented. Do not reopen without new evidence | **ANSWERED — implemented as observed** |
| Q37a | Residual: what are `DungeonHeartMaxManaIncrease` (32000) and `TerrainDef.MaxManaGain` (100, on Claimed Vault alone) *for*, given the ceiling is flat? **CLOSED (2026-08-29, owner observation): the ceiling is 200000 from the first second and never moves, so neither variable contributes to it and the growing-ceiling candidate is refuted.** What remains is a curiosity with no candidate attached — legacy data, editor-only data, or something outside mana entirely. Both are read by nothing and nothing depends on them. **Do not use either to justify a mana rule** | **CLOSED — not a ceiling contribution** |
| Q38 | Is `MaximumManaGainPerSecond` (500) a per-keeper cap or one shared across the level? Implemented per keeper: a shared cap would make one keeper's expansion throttle another's, which is not how the original plays | HYPOTHESIS (per keeper) |
| Q39 | Does mana accrue smoothly or in whole seconds? Ours is once a game second (ADR-0017), which is invisible in any total and visible only in the shape of the counter. Unmeasurable until Q1 is | UNKNOWN — nothing depends on it |
| Q40 | **Losing your dungeon heart is defeat.** What does defeat do? | **ANSWERED (2026-08-29, owner observation): the defeated keeper's creatures leave the dungeon through the nearest portal, and rival keepers claim their rooms and their gold.** **NOT IMPLEMENTED** and not implementable yet: it needs portals (an M6 record), creature pathing to one, and a rule for which rival takes what. Scheduled with objectives and win conditions at M6. Today a keeper whose heart falls simply stops earning its 30 a second and plays on, and every scenario with a heart on the map says so |
| Q41 | What does an imp do when it has no excavation, nothing to claim and nothing to reinforce? In the original it runs around the dungeon and interacts with the environment and with creatures (CONFIRMED). Here it stands still. Not implemented because wandering has no simulation consequence we model, and inventing a walk would put imps where DK2 would not | **CONFIRMED but NOT IMPLEMENTED** — harmless today; revisit when creature interaction exists |
| Q42 | Are claiming and reinforcing taken from ground the keeper already holds, or from any reachable floor? Implemented as the former — an imp reaches out from its own territory — which is what makes a dungeon grow outwards rather than in islands, and which Q33's answer supports for rooms. Not directly observed for imp jobs | STRONGLY INFERRED |
| Q43 | Three imps a face and twelve a block is a hard cap (CONFIRMED, Q26). Is it enforced by *reservation* — a fourth imp picks a different job — or by *crowding*, a fourth imp walking over and waiting for a slot? The observable difference is what the fourth imp does. Not implemented either way; imps still converge without limit | UNKNOWN — the visible half of Q26 |
| Q44 | When an imp claims a floor tile, does it stand **on** that tile or on the one next to it? Everything here works a tile from an orthogonal neighbour, which is necessarily right for walls and merely assumed for floor. The simulation outcome is nearly identical; only the imp's position differs | UNKNOWN — cosmetic today, matters once anything reads a worker's position |
| Q45 | Which rooms need a minimum size to *function*, and what is each minimum? A 1x1 temple can be built (Q36) but is not useful; the owner believes a temple needs 5x5. Nothing here implements room function at all, so nothing depends on it yet — but it is the shape of the answer for every room's behaviour | UNKNOWN — needs deep research, per the owner |
