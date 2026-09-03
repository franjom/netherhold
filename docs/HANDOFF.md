# Handoff notes

Working knowledge that would otherwise live only in chat history, per
`AGENTS.md` ("Do not leave important knowledge only inside chat history").
Update this whenever a session ends or context is compacted.

---

## Current state (2026-09-03)

**M3 tile mechanics, the first worker, rooms + the treasury, mana, the gold
carrier and where imps come from: complete. M3 as a whole: partial.** M2 and the
five record types M1 needed are done. **354 tests green** with and without a DK2
install; ten scenarios run; clean tree. (Core 14, Simulation 228, Data 41, Behavior 71.)

> ### ⚠ Read this first: five sittings of owner answers, 2026-08-29 to 09-03
>
> From playing the original, the owner has supplied answers to **Q7, Q9, Q12,
> Q20, Q21, Q24, Q25, Q26, Q27, Q28, Q30, Q31, Q32, Q33, Q34, Q36, Q37, Q37a,
> Q37b, Q38, Q40, Q42, Q43, Q44, Q45, Q46, Q47, Q48, Q49, Q52, Q53, Q54, Q58,
> Q59, Q60, Q62, Q63** plus the one-dungeon-heart rule, the four-imp floor and the Create
> Imp price. `docs/research/open-questions.md` has every one;
> `docs/research/observation-checklist.md` is the file to hand back for more.
>
> **The measurements are worth more than the yes/no answers.** A number
> propagates where a yes does not, and these rewrote more than every earlier
> answer combined:
>
> | Measurement | What it settled |
> |---|---|
> | **273 gold a swing** | Q47 — an application is a fifth of the tabled delta, five a second (ADR-0020) |
> | **2 swings to dig a plain wall** | Q20 — `DigRockHealth`'s shipped −5000 cannot produce it, so it is corrected to −500 (ADR-0023) |
> | **13.38 s to reinforce a wall** | Q62 — an upward job costs the terrain's whole `MaxHealth`, not the gap (ADR-0024) |
> | **−28 then −35 mana, 4 then 5 imps** | Q53 — 7 a second per imp, and it is on the *spell* record, not the creature (ADR-0022) |
> | **5 traps, three distinct values** | Q59 — `Traps.kwd` 0x0202, pinned by a test, unimplemented until objects (M6) |
> | **Temple capacity 8 / 12 / 16** | Q60 — a temple's capacity is its rim, `2(w+h) − 4` (ADR-0027) |
> | **16000 gold in a heart** | Q52 — nine of a heart's 25 tiles are its platform. FIXED (ADR-0027) |
> | **Room costs, 5 of them** | Q32 — `Cost` is per tile, confirmed five times over |
> | **4 imps, 5s, 1500 mana** | The imp economy, all three named by DK2's own data (ADR-0021) |
>
> **Every known defect from the original list is now fixed.** The last one,
> **Q44/Q42**, went on 2026-09-01 (ADR-0028): an imp claims a floor tile by
> standing *on* it, and the ownership test moved to that tile's neighbour. It had
> been waiting on Q49 — whether repairing, attacking and filling in work the same
> way — and Q49 was settled **without an observation**, by deriving the stance
> from two physical clauses instead of listing it per work kind. See the fourth
> lesson below.
>
> **Five lessons from these sittings, in order of how much they cost to learn:**
>
> 1. **Ask for the quantity by name.** "How much gold per swing" got an answer
>    that "how fast does mining feel" could not have.
> 2. **Ask for values that DIFFER from each other.** Two traps both measured 5,
>    two offsets fitted, and neither could be ruled out. It was the three-valued
>    spread that identified the field. A second identical measurement cannot
>    separate a real column from a coincidence.
> 3. **Anchor a scan on what was measured, never on a community figure.** The imp
>    upkeep search failed for a whole session because it required an unverified
>    2000 to also be true; the negative it produced was therefore worthless, and
>    it hid the fact that the search was in the wrong file entirely.
> 4. **Check our own extracted dump before recording a number as absent.** Q34
>    was written up for four sessions as "the refund is in neither candidate
>    variable" on the strength of a mis-transcribed 100. `RoomSellValuePercentageOfCost`
>    is 50, `docs/research/game-database-dump.md` said so all along, and the
>    owner's observation matched the shipped value exactly. A number believed
>    absent sends you looking for an override you do not need.
> 5. **Some questions are cheaper to derive than to observe.** Q49 asked which of
>    four floor jobs are worked standing on the tile. Asking for four observations
>    was the obvious move; the right one was to notice that *filling a tile in
>    turns it to rock*, so a worker standing on it would be entombed — and that
>    two physical clauses then answer all ten work kinds with nothing left to
>    guess. Q48 fell the same way a day earlier (a diagonal is √2 tile-widths, so
>    octile is forced). **Before writing an observation request, check whether the
>    answer is already implied by something the data says.**

Three honest caveats:
- M3's definition of done asked for gold reaching a player and a Unity view.
  **Gold now does**: an imp mines into its own hands and carries the load to a
  treasury or dungeon-heart tile (Q31, ADR-0019). The Unity view does not exist.
  What is still missing from the gold loop is the **floor lump** an imp leaves
  when there is nowhere to unload — it keeps mining, which is what DK2 does, but
  what it cannot hold is lost rather than left on the floor, because a lump is an
  OBJECT (M6). A worker does marked excavation, then claims, then reinforces
  (Q24, ADR-0018); what it does when all three run out is stand still, where DK2
  would have it wander (Q41). **Imps now generate themselves** (ADR-0021): a
  keeper below `MinimumImpThreshold` gets one free every
  `TimeBeforeFreeImpGeneratedSeconds`, and can buy more with Create Imp at 1500
  mana rising by 1500 each time. And **a room is an instance now** (ADR-0027) but still
  mostly does nothing: a treasury and a dungeon heart hold gold because a
  variable says how much per tile, and a heart correctly withholds the nine
  tiles of its platform (Q52). Every other room is inert terrain its owner
  holds - though the machinery to change that finally exists, since a room can
  now be asked its size, its rim and whether it is big enough to function. **Mana is now spent AND drained** - Create Imp costs 1500 to cast and
  every imp charges its keeper 7 a second to keep (Q53, ADR-0022), which is the
  red figure under the original's mana orb. What is still missing from the
  outgoings: every other keeper spell (a summoned Horned Reaper is 2000 a second
  and Call To Arms 90, both parsed and read by nothing until M7), research, and
  what traps draw - measured, located at `Traps.kwd` 0x0202 and unimplemented
  because a trap is an object (Q59, M6). And **losing a dungeon heart is defeat in DK2 and
  defeat is not implemented**: a keeper whose heart is beaten down here stops
  earning its 30 a second and plays on (Q40, scheduled with objectives at M6).
- M1's stated definition of done was "all 18 record types round-trip". Five are
  parsed (VARIABLES, CREATURES, TERRAIN, ROOMS, MAP) plus the WAD and KWD
  containers. The rest (THINGS, TRIGGERS, PLAYERS, OBJECTS, SHOTS, …) are needed
  at M6 and scheduled there.
- The CI workflow is written and every step verified locally on Windows, but it
  has never run on GitHub. The Linux leg is what turns the pinned state hash
  into a real cross-platform claim; until it runs, that claim is untested.

What can be read from the original game:

| Capability | Entry point | Validation |
|---|---|---|
| WAD archives + decompression | `DK2.Data.Wad.WadArchive` | every entry of all 5 archives extracts to declared size |
| KWD/KLD containers | `DK2.Data.Kwd.KwdReader` | 13 editor KWDs + 71 map KWDs + 330 KLDs |
| Global balance table (197 vars) | `DK2.Data.Kwd.KwdVariables` | values spot-checked vs. community docs |
| 46 creature definitions | `DK2.Data.Kwd.KwdCreatures` | ids match Flame; happy-job % sum to 100 |
| 41 terrain + 26 room types | `DK2.Data.Kwd.KwdTerrainRooms` | cross-parser: room→terrain, room→creature |
| Level tile grids | `DK2.Data.Kwd.MapGrid` → `LevelMap` | every tile of every campaign map |
| **All of the above, as one object** | `DK2.Data.GameDatabaseLoader` → `GameDatabase` | loads the real install; every room's terrain resolves |

What the simulation can do:

| Capability | Entry point | Validation |
|---|---|---|
| Fixed-tick loop | `DK2.Simulation.GameSimulation.Tick()` | tick advances exactly one |
| Deterministic state hash | `GameSimulation.StateHash()` | identical over 1000 ticks; diverges on seed/input change; one pinned literal |
| Save/load | `DK2.Simulation.WorldSnapshot` | restored run continues identically for 500 ticks |
| World from real map data | `WorldState.FromMap` | every campaign level builds; tile health from `TerrainDef` |
| Headless scenarios | `DK2.Harness` + `scenarios/**.yaml` | runs on a real campaign map; reproducible hash series |
| CI gate | `.github/workflows/ci.yml` | Linux + Windows, no DK2 install |
| Dig / mine / claim / reinforce / repair / attack / fill in | `DK2.Simulation.TileWorkRules` | rules run against the real install; terrain graph terminates on a whole campaign level; **a swing is a fifth of the tabled delta and a shipped seam gives up 273 gold eleven times** (ADR-0020) |
| Dig markers, first real command | `TileTagSystem` + `TagTilesCommand` | per player; cleared only when the tile can no longer carry one |
| Workers that excavate, then claim, then reinforce | `ImpTaskSystem` + `CreatureMovementSystem` | the priority order is CONFIRMED (ADR-0018); imps dig a corridor on a synthetic map and on a shipped campaign level, then claim the floor they opened |
| Where a worker stands to do a job | `WorkStance` | a wall is worked from beside, floor from on top of it (Q44/Q49, ADR-0028); derived from two clauses rather than listed, and both clauses are pinned by a test that fails without them |
| Grid search behind an interface | `DK2.Pathfinding.GridPathfinder` | shortest, wall-respecting, reproducible; reused buffers proven inert |
| Rooms: where one may go, what it costs | `RoomRules` + `BuildRoomCommand` | placement flags partition the shipped rooms; ground rules run on a campaign level |
| Selling a room back | `RoomRules.RefundPerTile` + `RoomSellSystem` | half the cost per tile, every number from the data (ADR-0029); `ReturnPercentage` is 0 for exactly the rooms a keeper cannot sell, asserted both ways against the install |
| Gold, stored in tiles rather than on players | `GoldStore` | per-tile limits honoured; a first treasury is paid for out of the dungeon heart |
| Mana, earned by holding land and a heart | `ManaRules` + `ManaSystem` | the three paying terrains partition the install by exhaustion; income falls when claimed floor becomes a room; a 5x5 heart is worth 30/s, not 750 |
| Gold carried from the seam to the treasury | `CreatureState.GoldHeld` + `ImpTaskSystem` | an imp mines a shipped seam on level 1 and every coin of its 3000 ends in the shipped dungeon heart, 1000 to a tile, across as many trips as its hands need |
| Where imps come from, and the first mana spender | `ImpSupplyRules` + `ImpSupplySystem` | four free imps five seconds apart on a shipped level, then 1500 / 3000 / 4500 for the next three and a refusal for the fourth |

Extracted data artifacts (the specification the simulation is written against):
`docs/research/game-database-dump.md`, `creature-table-dump.md`,
`terrain-room-dump.md`. The last of these now carries the terrain table's
`maxHealth` column and full flag set, and the room table's full flag set
(`dk2tools kwd terrain` / `kwd terrainflags` / `kwd terrainmana` / `kwd rooms` /
`kwd roomflags`), which is what M3's rules are built on.

**Next: the Unity view, then what rooms actually do.** See "Immediate next
steps". Every known defect that list ever held is built, and so is selling - the
gold carrier, the work rate, the imp upkeep, the dig rate, the upward-work rule,
eight-connected movement, the crowding cap, the heart's 16000, the work stance
and the sell refund.

How this milestone stands against the twelve long-term goals in `AGENTS.md`, and
which of them have deadlines attached: `docs/GOAL_ALIGNMENT.md`. Re-run that
review at every milestone; the asset-abstraction deadline in it (goal 4 must
land *with* the first Unity view, not after) is the one that would be expensive
to miss.

---

## Where the M2 code lives

| File | What it is |
|---|---|
| `src/DK2.Core/Hash64.cs` | FNV-1a structural hash. The determinism gate's foundation. |
| `src/DK2.Simulation/GameSimulation.cs` | The loop, `StateHash()`, system registration. |
| `src/DK2.Simulation/WorldState.cs` | `TileState`, `PlayerState`, `FromMap`. All mutable state. |
| `src/DK2.Simulation/ISimulationSystem.cs` | `SimulationContext` — the only things a system may touch. |
| `src/DK2.Simulation/InputFrame.cs` | `PlayerCommand` base and the per-tick input frame. |
| `src/DK2.Simulation/WorldSnapshot.cs` | Binary save/load, RNG position included. |
| `src/DK2.Simulation/SimulationEvents.cs` | Write-only observation. |
| `src/DK2.Data.Model/GameDatabase.cs` | The data/simulation seam. Immutable, no IO. |
| `src/DK2.Data/GameDatabaseLoader.cs` | Builds one from an install; loads campaign maps. |
| `src/DK2.Harness/Scenario.cs`, `ScenarioRunner.cs` | YAML scenarios and the run report. |
| `tests/DK2.Simulation.Tests/TestWorld.cs` | Synthetic database + `ChaosSystem`. Test-only, no DK2 mechanics. |
| `docs/architecture/simulation-loop.md` | Why the gate is built the way it is. |

## Where the M3 code lives

| File | What it is |
|---|---|
| `src/DK2.Simulation/TileWork.cs` | `TileWorkKind`, `TileWorkOrder`, and `TileWorkRules` — the rules, pure and static. |
| `src/DK2.Simulation/Systems/TileWorkSystem.cs` | Drains the world's work queue each tick. |
| `src/DK2.Simulation/Systems/TileTagSystem.cs` | Applies dig markers. |
| `src/DK2.Simulation/Commands/TileCommands.cs` | `TagTilesCommand` — the first real player command. |
| `src/DK2.Simulation/SimulationTiming.cs` | **The only** place ticks relate to seconds. `TicksPerSecond` is still the Q1 placeholder; `WorkApplicationsPerSecond` is measured (ADR-0020). |
| `src/DK2.Simulation/CreatureState.cs` | The first mutable creature state. Position is fixed point; the route is state, not a cache. |
| `src/DK2.Simulation/Systems/ImpTaskSystem.cs` | Which marked tile a worker takes, and when it applies work. |
| `src/DK2.Simulation/WorkStance.cs` | **The one** rule for where a worker stands: on the tile for floor, beside it for a wall (ADR-0028). |
| `src/DK2.Simulation/Systems/CreatureMovementSystem.cs` | Walking, at the speed the creature record gives. |
| `src/DK2.Simulation/Systems/SimulationSystems.cs` | **The one** declaration of system order (ADR-0013). |
| `src/DK2.Simulation/WorldPathGrid.cs` | What "walkable" means, for one particular creature. |
| `src/DK2.Pathfinding/IPathfinder.cs`, `GridPathfinder.cs` | The Q5 placeholder, and its interface. |
| `src/DK2.Simulation/RoomRules.cs` | Where a room may go, what it costs, what it does to the tile. |
| `src/DK2.Simulation/GoldStore.cs` | **The only** way gold moves. Gold is in tiles, not on players (ADR-0015); `DepositInto` is the carrier's route, one tile at a time. |
| `tests/DK2.Simulation.Tests/GoldCarryingTests.cs` | The whole gold round trip: hands, trips, spillover, and what is lost when there is nowhere to unload. |
| `tests/DK2.Behavior.Tests/GoldOnRealDataTests.cs` | The same round trip on shipped level 1 — a real seam into a real dungeon heart. |
| `scenarios/imps/an_imp_carries_gold_home.yaml` | The trip end to end on a campaign map, and the event log that reads like DK2. |
| `src/DK2.Simulation/Commands/RoomCommands.cs` | `BuildRoomCommand` — the second real player command. |
| `src/DK2.Simulation/Systems/RoomBuildSystem.cs` | Applies it: a tile at a time, every refusal counted by reason. |
| `tests/DK2.Behavior.Tests/RoomsOnRealDataTests.cs` | The room fixture's drift guard, plus the flag-partition claim. |
| `scenarios/rooms/` | A keeper builds a treasury and cannot afford the last tile of it. |
| `docs/research/rooms-and-gold.md` | Rooms and the gold model: evidence, what falls out, what is absent. |
| `src/DK2.Simulation/ManaRules.cs` | The whole mana model: income from owned land, the cap and the ceiling. |
| `src/DK2.Simulation/Systems/ManaSystem.cs` | Pays it once a game second (ADR-0017). Runs last, after anything that changes ownership. |
| `tests/DK2.Behavior.Tests/ManaOnRealDataTests.cs` | The mana fixture's drift guard, plus the "only held ground pays" partition by exhaustion. |
| `scenarios/mana/` | Land pays a keeper; paving three tiles of it into a treasury lowers the rate. |
| `docs/research/mana.md` | Mana: evidence, why per second, and two numbers that mean nothing here (Q37a). |
| `tests/DK2.Simulation.Tests/Dk2TerrainFixture.cs` | DK2's real numbers, transcribed so the rules test in CI. |
| `tests/DK2.Behavior.Tests/TileWorkOnRealDataTests.cs` | The same rules against the real install — keeps the fixture from drifting. |
| `tests/DK2.Behavior.Tests/ImpsOnRealDataTests.cs` | Same, for the Imp record, plus imps digging a real level. |
| `scenarios/tiles/` | Dig/claim on a synthetic map; gold mining on a surveyed campaign map. |
| `scenarios/imps/` | Two imps digging a corridor from nothing but a tag command. |
| `docs/research/tile-work.md` | The tile rules: evidence, confidence tags, three anomalies. |
| `docs/research/imp-tasks.md` | The worker: evidence, what is inferred, what is deliberately absent. |
| `src/DK2.Simulation/ImpSupplyRules.cs` | Where imps come from and what they cost. Both numbers are lookups; the project has no hardcoded balance constants. |
| `src/DK2.Data/Kwd/KwdKeeperSpells.cs` | Two fields of a 406-byte record nobody has a template for - the cast cost (0x11C) and the per-second upkeep (0x00E2) - and a long note on why those two offsets are believable. |
| `src/DK2.Simulation/Systems/ImpSupplySystem.cs` | Free imps up to the floor, bought imps on command. The first system that spends mana. |
| `tests/DK2.Behavior.Tests/KeeperSpellsOnRealDataTests.cs` | Makes both unparsed-record offsets earn their place: 13 plausible costs, and an upkeep column that is non-zero for exactly the five spells whose result persists. |
| `scenarios/imps/a_keeper_never_runs_out_of_imps.yaml` | The imp economy end to end on a campaign map. |
| `docs/research/imps-and-mana.md` | Imps, the Create Imp price, and **the mana drain research** — the biggest open hole. |
| `docs/research/observation-checklist.md` | **Hand this to the owner.** Every open question a person playing DK2 can settle by looking, as experiments — and which ones playing cannot settle. |

## Running things

```
dotnet build DK2.slnx
dotnet test  DK2.slnx
DK2_DIR=/nonexistent dotnet test DK2.slnx      # what CI sees: install-gated tests skip
dotnet run --project src/DK2.Harness -- list
dotnet run --project src/DK2.Harness -- run scenarios
```

Harness output lands in `build/scenario-runs/<id>/` (gitignored).

---

## Immediate next steps (rest of M3)

**The order changed on 2026-08-29.** The owner's answers turned four research
questions into ordinary implementation work, and that work now comes before the
Unity view, because every one of them is a behaviour a renderer would display
incorrectly.

**Where to pick up, 2026-09-03 (sixth sitting).** **Every known defect from the
original list is fixed, and selling is built.** Q31 the gold carrier, Q47 the
work rate, Q53 the imp mana upkeep, Q20 the dig rate, Q62 the upward-work rule,
Q28/Q48 eight-connected movement, Q43 the crowding cap, Q52 the heart's 16000,
Q44/Q42 the work stance and Q34 selling are all built. What is left, in the order
it is worth doing:

1. **Unity presentation** — the simulation runs a dig scenario with moving
   workers and a keeper building a room, headlessly, which was the precondition.
   `CreatureState.PosX/PosY` are fixed point in tile units; a renderer converts,
   the simulation never does. **`docs/GOAL_ALIGNMENT.md` gap 3 applies here and
   is the expensive one to miss: the asset abstraction layer must land in the
   same milestone as the first Unity view, not after it.**

2. **What rooms actually do.** Room instances exist now (ADR-0027), so the
   machinery is finally there: `RoomInstance` gives a room its tile count, its
   bounding box, its rim and whether it is big enough to function. A lair, a
   hatchery, a library and the rest are still inert terrain, and each needs its
   own research note — none can be guessed from the ROOMS record, which carries
   art, size and cost and nothing about behaviour.

**What is banked and waiting for a milestone, not for research:** the trap mana
upkeep (Q59, `Traps.kwd` 0x0202, pinned by a test, needs objects at M6); the four
other spell upkeeps (Q55, parsed and read by nothing until spells at M7); payday
at 600 seconds (Q9, needs creatures that get paid at M4); creature attraction
(Q61, needs portals at M6); the floor gold lump (Q31 leftover, needs objects);
and defeat (Q40, needs portals and objectives).

**The cheapest unanswered observations**, if the owner is ever at the keyboard:
**a chain of ten claims timed end to end** would give the Q49 pause a number
(about 12 s with no pause against 17 s with one), and that is the only thing
blocking an implementation; fill-in now predicts **660 swings, about 132
seconds** for one tile (Q22) and that is untested and a long time; a **3x5
temple** is the only shape that can tell Q60's rim rule from a rule keyed on one
dimension, since all three measurements so far were squares; and a **5x5
graveyard's stated capacity** - 8 or 9 - would settle its grave rule in one look,
with a **4x4** as the fallback. **Q48 came off this list on 2026-09-03**: the
owner watched a creature cross open ground and it glides rather than
staircasing.

**Q47 came off this list on 2026-08-30 and is done.** It had said: get exact hit
counts before touching `SimulationTiming.WorkApplicationPeriodTicks`. The exact
count arrived - **273 gold a swing** at a gold seam - and it was worth the wait,
because the answer was five applications a second and not the two that the ranges
had suggested. See ADR-0020. The lesson is the same one ADR-0014 taught: the
half-delta reading was plausible, self-consistent, and wrong, and not having
implemented it made the correction free.

**Before touching worker behaviour, read `docs/research/imp-tasks.md`
§"What is not implemented, and is not guessed at".** The priority order is no
longer among the unknowns — it is CONFIRMED and built (ADR-0018) — and neither is
carrying gold (Q31, ADR-0019). What is still missing is missing on purpose:
wandering (Q41), the ~500 ms an imp lingers after claiming (Q49 - cooldown or
animation, and the two are indistinguishable from outside), dragging bodies,
fleeing, building rooms, picking up gold somebody else dropped, and the floor
lump. The crowding cap came off that list (ADR-0026) and so did the work stance
(ADR-0028).

**And before adding to `RoomRules`, read `docs/research/rooms-and-gold.md`
§"What is not implemented, and is not guessed at".** Rooms still have no
behaviour and no selling on purpose. Two of that file's open questions are now
answered and the file has not caught up with either: **a room tile must touch
land the keeper owns (Q33, not enforced — bridges can still start in open
water), and selling refunds 50% of cost per tile (Q34) — a number that is in
neither candidate variable, so the data cannot supply it.** The gold half of that
file *has* caught up: §"The round trip" is current as of ADR-0019.

**And before adding to `ManaRules`, read `docs/research/mana.md` §"What is not
implemented, and is not guessed at".** The mana ceiling is the cautionary tale
worth keeping: the data was *nearly* enough to build a computed per-keeper
ceiling out of `MaxManaGain` and `DungeonHeartMaxManaIncrease`, that reading was
tidier than the truth, and it was wrong. It stayed unimplemented only because it
required one more guess (per heart tile or per heart room?), and the owner then
settled it from the game in two sentences - the ceiling is 200000 from the first
second and never moves, which refutes the growing-ceiling variant as well.
`MaxManaGain` and `DungeonHeartMaxManaIncrease` are read by nothing, have no
candidate explanation left, and **may not be used to justify a mana rule**
(Q37a).

**And `docs/research/tile-work.md` §"What does not fit" still stands, but has
been reframed — read Q47 before Q20.** `DigRockHealth` destroying a rock tile in
one application is still a data anomaly and must not be "fixed" by changing the
number. What is new is that it is now the *only* anomaly: measured against the
original, every rate here runs about twice as fast, and an application landing
twice a second applying half the tabled delta reconciles mining exactly and
claiming approximately. That reading is self-consistent — half the delta twice a
second is the delta per second — so **the balance numbers are per-second and the
granularity is what is wrong.** Not implemented: the fit rests on ranges, and
exact hit counts are needed first. `tile-work.md` itself has not been rewritten
for this; Q47 in `open-questions.md` is the current statement.

---

## Decisions already made — do not relitigate

- **MIT licence** (owner decision). Consequence: **no code may be copied from
  OpenKeeper (GPLv3) or Flame (unlicensed)** — facts only, always with
  provenance. See ADR-0003 in `docs/DECISIONS.md`.
- GLKeeper is MIT, so its code *may* be adapted with attribution — this was done
  once, for the WAD decompressor (`THIRD-PARTY-NOTICES.md`).
- Integer/fixed-point for gameplay maths, `float` presentation-only (ADR-0001).
- Simulation assemblies target netstandard2.1 and must never reference Unity;
  enforced by `ArchitectureTests` (ADR-0002).
- `.slnx` solution format, .NET 10 (ADR-0005).
- Determinism gate design — RNG position in the hash, FNV-1a not `GetHashCode`,
  one pinned literal, test-only `ChaosSystem` (ADR-0006).
- Our own save format; DK2 save compatibility is not a goal (ADR-0007).
- YamlDotNet for scenarios, tooling only (ADR-0008).
- Tile transitions are triggered by the *work*, not by the health level
  (ADR-0009). This is not a style choice — the level-triggered reading is
  refuted by the data. See `tile-work.md`.
- Work orders are a FIFO queue on `WorldState`, filled by whoever is working
  (ADR-0010). Order is behaviour: never sort or deduplicate it.
- Ticks relate to seconds in `SimulationTiming` and nowhere else (ADR-0011).
- The pathfinder is an admitted placeholder behind `IPathfinder` (ADR-0012).
  Four-connected and uniform-cost by deliberate conservatism, not by belief.
- System order is declared once, in `SimulationSystems.Default()` (ADR-0013).
- **Imps come from a floor and a price** (ADR-0021). Below `MinimumImpThreshold`
  (4) a keeper gets one free every `TimeBeforeFreeImpGeneratedSeconds` (5);
  beyond it, Create Imp costs 1500 x (imps - 4 + 1), and the price FALLS BACK
  when imps die. Both numbers are read from `KeeperSpells.kwd` (ADR-0022); the
  hardcoded 1500 that ADR-0021 admitted lasted exactly one session.
- **Every imp costs its keeper 7 mana a second to keep** (ADR-0022, Q53),
  measured by the owner and read off DK2's own Create Imp record. Income and
  upkeep are capped separately and clamped to the ceiling together - an order
  fixed by a screenshot of a keeper pinned at exactly 200000 with +400 and -297.
- Imps take jobs in a strict CONFIRMED order - marked excavation, then claiming,
  then reinforcing (ADR-0018, superseding ADR-0014). Carrying gold is NOT in that
  list: it is the tail of a mining job, not a job an imp chooses.
- **Gold lives in tiles; a player has no gold counter** (ADR-0015). DK2 has no
  player-gold variable — it has four per-place limits. `GoldStore` is the only
  way gold moves.
- **Work lands five times a second, a fifth of the tabled delta at a time**
  (ADR-0020). Measured, not chosen: 273 gold a swing at a gold seam. The rate per
  second is unchanged; the swing *count* is now the measured quantity and is
  independent of Q1. `TileState.WorkRemainder` carries the fraction for the two
  deltas that do not divide by five.
- **A creature's hands are the fourth place gold can be** (ADR-0019).
  `CreatureState.GoldHeld`, bounded by the creature's own `maxGoldHeld`. A
  keeper's spendable total dips while their imps are walking home, and that is
  correct rather than an artefact. Deposits land in the tile the carrier reached,
  which is why spillover between treasury tiles needs no rule (Q35).
- Where a room may go is read from the room's own placeable-on flags, so nothing
  in the rules knows what a bridge is (ADR-0016).
- A `…PerSecond` rate from the balance table is applied **in whole, once every
  `TicksPerSecond` ticks** (ADR-0017), not smoothed across ticks. That keeps
  totals independent of the unmeasured Q1 constant, which is the point.
- **A keeper has at most one dungeon heart, ever, and losing it is defeat.**
  CONFIRMED by the owner from the original, and the ROOMS record agrees — the
  heart carries no `Buildable` flag. This is why the heart's mana is counted once
  per keeper rather than per heart tile, and it is worth knowing before designing
  anything that would otherwise need to count room instances.
- **The keeper mana ceiling is 200000 from the first second and never moves.**
  CONFIRMED by the owner from the original (Q37, Q37a). Do not reopen it with
  `DungeonHeartMaxManaIncrease` or `MaxManaGain`: they look like ceiling
  contributions, they are not, and a ceiling that never moves cannot have
  contributions at all.
- Reference install: `E:\GogGames\Dungeon Keeper 2` (GOG). Resolved by
  `DK2.Data.Dk2Install`. **`DK2_DIR` is authoritative when set** — if it is set
  and invalid, no fallback is tried, which is how CI runs with no install.
  Tests auto-skip when it resolves to nothing.

---

## Conventions that are easy to violate by accident

- **A balance constant hardcoded in simulation code is a bug** — read it from
  `GameDatabase`. `GameDatabase[VariableTypeId.X]` throws rather than returning
  zero for a missing value, on purpose. Consequence, and it bites every time: a
  system that reads a *new* variable breaks every synthetic database until that
  variable is added to it — `TestWorld.Variables()`,
  `Dk2TerrainFixture.Variables()` and `ScenarioTests.Variables()`. That is the
  design working; add the entry rather than defaulting the lookup.
- **Changing simulation state breaks the pinned hash.** That is the design.
  Bump `GameSimulation.HashVersion` and update the literal in
  `DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines` in the same
  commit. Never "fix" it by loosening the assertion.
- **Nothing in the simulation may read an event back.** Events are write-only
  observation; a system that reads one makes the observer part of the observed.
- **New state must be reachable from `WorldState`** or it is invisible to the
  hash and the snapshot — and therefore silently non-deterministic. When you add
  a field, add it to `HashInto` *and* to `WorldSnapshot` (bump its
  `FormatVersion`) in the same commit.
- **A gameplay rule keyed on a terrain id is a rule about the reference
  install, not about DK2.** Ask the terrain's flags. `TileWorkRules` does this
  throughout and `TileWorkOnRealDataTests` checks it still holds against the
  real database.
- **A number that does not match how the original plays is not licence to
  change it.** Reproduce the data, record the discrepancy as an open question,
  and let the scenario output show it (Q20 is the live example).
- **A decimal printed from an `Fp32` is lossy.** Transcribe the raw value.
  `dk2tools kwd creature <name>` prints a `raw:` line for exactly this reason;
  the fixture's first copy of the Imp's hunger rate was 32767 where the raw
  value is 134213632, and only the real-data guard caught it.
- **Read a creature's capability from its flags, never its id.** `IsWorker`
  picks out the Imp and the Dwarf; `CanWalkOnWater` / `CanWalkOnLava` decide
  what a mover may cross. Same rule as terrain flags, same reason.
- Confidence tags are mandatory in docs and code comments:
  **CONFIRMED / STRONGLY INFERRED / HYPOTHESIS / UNKNOWN**.
- Every researched mechanic gets a `docs/research/<name>.md` using the template
  in `DK2_REIMPLEMENTATION_PLAN.md` Appendix B.
- Never commit anything from `original-reference/` or any `.kwd`/`.wad`/`.kmf`
  (gitignored, but check before `git add -A`; a third-party template slipped in
  once and had to be removed in `4cdcd38`).

---

## Environment gotchas (each of these already cost time)

**Bash heredocs and C#.** Writing C# via `cat > file <<'EOF'` has bitten twice
by eating backslashes (`Replace('\\', '/')` → `Replace('\', '/')`), and once by
choking on a file containing backticks. Use the `Write`/`Edit` tools for any C#
containing backslash escapes or backticks; verify with `grep` otherwise.

**`cat > file` with no stdin hangs** the Bash tool until timeout. So does
`python`/`python3` — it is not installed and the Windows Store stub blocks.
Never write a bare `cat >` redirect, and do not reach for python.

**`ctx_batch_execute` sandbox quirks:**
- `for ... do ... done` loops fail to parse (the wrapper prefixes the command
  on one line). Use separate commands or `;`-chained statements.
- `/tmp` does not persist between commands in a batch — each gets a fresh
  workspace. Pipe directly, or `curl -o` and use the file in the *same* command.
- `python3` is not available; `curl`, `grep`, `sed`, `awk`, `perl`, `xxd` are.

**`dotnet build` takes one project, not several.** Pass `DK2.slnx` instead.

**Windows/git:** CRLF warnings on every commit are expected noise.

---

## Research method that has been working

Cheap-to-expensive, stop as soon as the question is answered:

0. **Ask the owner, or hand them `docs/research/observation-checklist.md`.**
   They have played DK2 and the original is the behavioural oracle. That file
   lists every open question a person in front of the game can settle by
   looking, as concrete experiments, and says which ones playing cannot settle.

   This is the cheapest source there is and it was under-used: Q37 (the mana
   ceiling) and the one-dungeon-heart rule were both settled in one sentence
   after being written up as unanswerable from the files. A question of the form
   "what does the player *see* happen" is almost always one of these. Record the
   answer as CONFIRMED with "owner observation" as the evidence, the same way a
   data extract is recorded — and record a "no" too, since it closes a question
   just as hard.
1. Is the answer just sitting in the KWD data? (Often yes — check first.)
2. ufdada/dk2-tools 010 Editor templates — independent format facts.
3. Flame's `mapping/DKII_EXE_v170.sgmap` — 868 structs, 10,548 globals.
4. OpenKeeper — consult as documentation only (GPLv3).
5. Only then: IDA/Ghidra on DKII.EXE, using Flame's IDA import scripts.

**Validate offsets structurally, never on trust.** The techniques that have
caught real errors so far:
- Record size must equal `contentSize / itemCount` exactly (caught the 40-byte
  WAD entry stride, which the template got wrong at 36).
- Sum the template's field widths and check it reproduces the record size
  (confirmed terrain 552 and rooms 1055 before writing any parser).
- Look for semantic invariants: happy-job chances summing to 100 across all 46
  creatures proved the JobPreference offsets were right.
- Cross-check between independent parsers: room→terrain→creature references all
  resolving is strong evidence all three are correct.
- Sanity-check values against known DK2 behaviour (Vampire 666 damage, Prison
  spawning Skeletons, Hero Lair healing).

---

## Live research questions

`docs/research/open-questions.md` holds all 68 (Q1-Q65; Q51 superseded). The ones that gate progress:

- **Q53 is ANSWERED and built** (2026-08-30): 7 mana a second per imp, measured
  by the owner and read off DK2's own Create Imp spell record (ADR-0022). Left in
  this list for the method, which is in `imps-and-mana.md` §*How this was
  established*: a scan anchored on a measured number produces a trustworthy
  negative, and a scan anchored partly on a forum post does not.
- **Q59 is ANSWERED** (2026-08-30): a trap is a standing mana charge, and the
  field is `Traps.kwd` offset 0x0202, confirmed against five owner measurements
  across three distinct values and pinned by `TrapsOnRealDataTests`. Not
  implemented, because a trap is an object and objects arrive at M6.
- **Q47 is ANSWERED and built** (2026-08-30): 273 gold a swing put an application
  at a fifth of the tabled delta, five a second (ADR-0020). Left in this list
  only to say that `DigRockHealth` is now the sole thing in the tile mechanics
  that does not fit, where it used to be one suspect among several.
- **Q40 — losing your dungeon heart is defeat.** Now fully described (the
  defeated keeper's creatures leave by the nearest portal; rivals claim their
  rooms and gold) and **not implementable yet**: it needs portals, which are an
  M6 record. Scheduled with objectives and win conditions.
- **Q7 — the creature mood model.** No longer a blank sheet. The four things that
  anger a creature are recorded and three of them map onto fields already parsed
  per creature (`AngerNoLair`, `AngerNoFood`, `AngerNoPay`). What is missing is
  the shape — accumulation, decay, thresholds. This was the area nobody had
  solved; it is now the area with a starting point.
- **Q1 — simulation tick rate.** Blocking any timing claim. Flame's `limit_tps`
- **Q1 — simulation tick rate.** Blocking any timing claim. Flame's `limit_tps`
  patch proves it is fixed; the value is unmeasured. It is one constant,
  `SimulationTiming.TicksPerSecond`, and it must stay that way. **Do not go
  looking in the KWD data again**: id 74 `GameTicks` exists in the format's id
  space but no shipped file carries it — checked across every editor KWD and all
  71 map KWDs. Q1 needs measurement against the running original.
- **Q19 — what the health deltas are *per*.** Tied to Q1. The variables table
  spells out `…PerSecond` / `…Seconds` on its other entries and not on these
  ten, which is the whole of the ambiguity.
- **Q20 — `DigRockHealth` is −5000 against Rock's 165 health.** One application
  destroys it, so digging earth is instantaneous, which is wrong. Every other
  work rate is sane. New at M3 and the first place the data and the original
  visibly disagree. Candidate explanations in `tile-work.md`.
- **Q18 is now answered** (2026-08-29): `maxHealthTypeTerrainId` is the
  destination of whichever upward job the terrain admits. Do not reopen it
  without new evidence.
- **Q5 — pathfinding.** The project's biggest risk (plan §9, R1). Flame names
  `Follow_Path` but has zero named fields for it. Nobody has solved this. Now
  load-bearing rather than theoretical: `GridPathfinder` routes every worker,
  and Q28 (four- or eight-connected?) and Q30 (does terrain weight a path?) are
  the two cheapest pieces of it to settle against the original.
- **Q2 — job selection.** Narrowed: candidates and weights are *data*. The
  remaining unknown is the procedure around them (re-evaluation timing, how
  mood picks which of the three tables applies, handling unavailable jobs).
- **Q7 — creature moods.** Genuinely unsolved; OpenKeeper's system is an empty
  stub after 12 years.
- **New at M2 — system update order.** DK2's own per-tick subsystem ordering is
  UNKNOWN. Ours is explicit and declared in exactly one place
  (`SimulationSystems.Default()`, ADR-0013). It starts to matter around M4;
  treat it as a research question then, not a convenience choice.

---

## Corrections carried forward

At the end of the previous session I told the owner that `AGENTS.md` references
a `docs/ai/CLAUDE.md` scoping the assistant to research and analysis, and
suggested it might need updating now that implementation has started. **That was
wrong.** No `CLAUDE.md` exists anywhere in the repository and `AGENTS.md` makes
no such reference. There is nothing to reconcile; do not go looking for it.

`AGENTS.md` itself is the operating instruction, and it does not restrict
implementation.

---

## Where the project stood at the 2026-09-03 compaction

Six sittings of owner measurements and six ADRs - one of which needed no
measurement at all, and one of which needed no new data either, only a look at
the dump we already had. The state:

**354 tests green** with and without a DK2 install (Core 14, Simulation 228,
Data 41, Behavior 71), ten scenarios running, clean tree.

**Every known defect is fixed, and the room economy now runs both ways.** In the
order they fell:

| | |
|---|---|
| ADR-0019 | Q31 — gold moves seam → imp's hands → treasury |
| ADR-0020 | Q47 — work lands five times a second, a fifth of the tabled delta |
| ADR-0021 | Imps come from a floor and an escalating price |
| ADR-0022 | Q53 — every imp costs 7 mana a second, off the *spell* record |
| ADR-0023 | Q20 — `DigRockHealth` corrected to −500, the only such override |
| ADR-0024 | Q62 — an upward job costs the terrain's whole `MaxHealth` |
| ADR-0025 | Q28/Q48 — eight-connected movement, a diagonal costs √2 |
| ADR-0026 | Q43 — three workers to a face, the fourth goes elsewhere |
| ADR-0027 | Q52/Q45/Q60 — a room is an instance, derived rather than stored |
| ADR-0028 | Q44/Q42/Q49 — floor is worked from on top of it, walls from beside |
| ADR-0029 | Q34 — selling returns half the cost, and the data said so all along |

**Nothing is left on that list**, which is why the pick-up list above now starts
with the Unity view.

**Two mechanisms were created for numbers that come from observation rather than
from the files, and both are meant to stay narrow:**

- `ObservedCorrections` — the file carries a value that *cannot produce* the
  observed behaviour. One member: `DigRockHealth`. Four rules for adding a
  second are written on the type.
- `ImpCrowding` — the file carries *no* value at all. One member: three workers
  to a face. **Q34 was expected to join it and does not** (ADR-0029): the shipped
  `RoomSellValuePercentageOfCost` really is 50 and this file had it wrong, so
  selling needed no override at all. The live candidate is now the **~500 ms
  pause after claiming** (Q49) - real, observed, and in no field we read, because
  every `...Seconds` variable in the balance table is a whole number and none is
  below 1. It needs a measurement first; see `observation-checklist.md` for the
  chain-of-ten method.

**The research method that produced all of this is written up in three places
and is worth reading before the next research push:** the call-out at the top of
this file (five lessons), `docs/research/imps-and-mana.md` §*How this was
established* (the failure and recovery in full), and
`docs/research/observation-checklist.md` (what is still worth asking, and what
playing cannot settle).
