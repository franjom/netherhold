# Handoff notes

Working knowledge that would otherwise live only in chat history, per
`AGENTS.md` ("Do not leave important knowledge only inside chat history").
Update this whenever a session ends or context is compacted.

---

## Current state (2026-08-29)

**M3 tile mechanics, the first worker, and rooms + the treasury: complete.
M3 as a whole: partial.** M2 and the five record types M1 needed are done.
198 tests green with and without a DK2 install, clean tree.

Three honest caveats:
- M3's definition of done also asks for gold reaching a player and a Unity view.
  Neither exists. There is a treasury now, but **nothing carries mined gold to
  it** (Q31), and **a worker only does work the keeper marked** — claiming,
  reinforcing, carrying gold and fleeing are real imp jobs that are not
  implemented because their trigger and priority order are UNKNOWN (Q24).
  **Nothing generates imps** either: scenario files and tests place them. Every
  scenario run that places a worker says all of that out loud. And **a room does
  nothing**: a treasury and a dungeon heart hold gold because a variable says
  how much per tile; every other room is inert terrain its owner holds.
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
| Dig / mine / claim / reinforce / repair / attack / fill in | `DK2.Simulation.TileWorkRules` | rules run against the real install; terrain graph terminates on a whole campaign level |
| Dig markers, first real command | `TileTagSystem` + `TagTilesCommand` | per player; cleared only when the tile can no longer carry one |
| Workers that find, walk to and do marked work | `ImpTaskSystem` + `CreatureMovementSystem` | imps dig a corridor on a synthetic map and on a shipped campaign level |
| Grid search behind an interface | `DK2.Pathfinding.GridPathfinder` | shortest, wall-respecting, reproducible; reused buffers proven inert |
| Rooms: where one may go, what it costs | `RoomRules` + `BuildRoomCommand` | placement flags partition the shipped rooms; ground rules run on a campaign level |
| Gold, stored in tiles rather than on players | `GoldStore` | per-tile limits honoured; a first treasury is paid for out of the dungeon heart |

Extracted data artifacts (the specification the simulation is written against):
`docs/research/game-database-dump.md`, `creature-table-dump.md`,
`terrain-room-dump.md`. The last of these now carries the terrain table's
`maxHealth` column and full flag set, and the room table's full flag set
(`dk2tools kwd terrain` / `kwd terrainflags` / `kwd rooms` / `kwd roomflags`),
which is what M3's rules are built on.

**Next: the rest of M3 — gold that reaches a keeper, and the Unity view.**

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
| `src/DK2.Simulation/SimulationTiming.cs` | **The only** place ticks relate to seconds. Q1 placeholder. |
| `src/DK2.Simulation/CreatureState.cs` | The first mutable creature state. Position is fixed point; the route is state, not a cache. |
| `src/DK2.Simulation/Systems/ImpTaskSystem.cs` | Which marked tile a worker takes, and when it applies work. |
| `src/DK2.Simulation/Systems/CreatureMovementSystem.cs` | Walking, at the speed the creature record gives. |
| `src/DK2.Simulation/Systems/SimulationSystems.cs` | **The one** declaration of system order (ADR-0013). |
| `src/DK2.Simulation/WorldPathGrid.cs` | What "walkable" means, for one particular creature. |
| `src/DK2.Pathfinding/IPathfinder.cs`, `GridPathfinder.cs` | The Q5 placeholder, and its interface. |
| `src/DK2.Simulation/RoomRules.cs` | Where a room may go, what it costs, what it does to the tile. |
| `src/DK2.Simulation/GoldStore.cs` | **The only** way gold moves. Gold is in tiles, not on players (ADR-0015). |
| `src/DK2.Simulation/Commands/RoomCommands.cs` | `BuildRoomCommand` — the second real player command. |
| `src/DK2.Simulation/Systems/RoomBuildSystem.cs` | Applies it: a tile at a time, every refusal counted by reason. |
| `tests/DK2.Behavior.Tests/RoomsOnRealDataTests.cs` | The room fixture's drift guard, plus the flag-partition claim. |
| `scenarios/rooms/` | A keeper builds a treasury and cannot afford the last tile of it. |
| `docs/research/rooms-and-gold.md` | Rooms and the gold model: evidence, what falls out, what is absent. |
| `tests/DK2.Simulation.Tests/Dk2TerrainFixture.cs` | DK2's real numbers, transcribed so the rules test in CI. |
| `tests/DK2.Behavior.Tests/TileWorkOnRealDataTests.cs` | The same rules against the real install — keeps the fixture from drifting. |
| `tests/DK2.Behavior.Tests/ImpsOnRealDataTests.cs` | Same, for the Imp record, plus imps digging a real level. |
| `scenarios/tiles/` | Dig/claim on a synthetic map; gold mining on a surveyed campaign map. |
| `scenarios/imps/` | Two imps digging a corridor from nothing but a tag command. |
| `docs/research/tile-work.md` | The tile rules: evidence, confidence tags, three anomalies. |
| `docs/research/imp-tasks.md` | The worker: evidence, what is inferred, what is deliberately absent. |

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

The tile rules, the first worker, rooms and the treasury are done. What is left
is the half of the gold loop that needs a carrier, and the view.

1. **Mana from claimed tiles** — the cheapest real thing left, and it needs
   nobody's permission. `ManaGain`/`MaxManaGain` are on every terrain (Claimed
   Path 1, Claimed Vault 100) and nothing reads them; `MaximumManaThreshold`,
   `MaximumManaGainPerSecond` and `ExcessManaDecreaseRatePerSecond` are all
   extracted. Mana is already a player-level pool (`PlayerState.Mana`), so
   unlike gold it needs no new state. It is also the precondition for the Create
   Imp spell, which is the precondition for anything generating workers.
2. **Unity presentation** — the simulation runs a dig scenario with moving
   workers and a keeper building a room, headlessly, which was the precondition.
   `CreatureState.PosX/PosY` are fixed point in tile units; a renderer converts,
   the simulation never does. **`docs/GOAL_ALIGNMENT.md` gap 3 applies here and
   is the expensive one to miss: the asset abstraction layer must land in the
   same milestone as the first Unity view, not after it.**
3. **Gold reaching a keeper (Q31)** — the remaining half of M3's definition of
   done, and the one that is blocked on research rather than on typing. The
   treasury and the spill rule exist; what is missing is the carrier. Three
   things have to be settled first, and none can be guessed: does the mining imp
   take the gold it freed or does it land on the floor as a pile (a pile is an
   OBJECT, so that branch needs M6); what decides when an imp stops mining and
   delivers (the Imp's `maxGoldHeld` is 1400 against ~1363 an application from a
   seam and 25 from a gem face — recorded in `rooms-and-gold.md`); and which
   treasury tile it fills (Q35). **This is also where Q24 has to be faced.**
4. **Claiming as an imp job** — `Claim` already works as a rule and is exercised
   by scenario-injected work. Making an imp start one unprompted is a Q24
   decision, not a coding one; do the research before the code.
5. **What rooms actually do** — a lair, a hatchery, a library and the rest are
   inert terrain. Each needs its own research note; none can be guessed from the
   ROOMS record, which carries art, size and cost and nothing about behaviour.

**Before touching worker behaviour, read `docs/research/imp-tasks.md`
§"What is not implemented, and is not guessed at".** The missing jobs are
missing on purpose, and adding one means answering Q24 rather than picking an
order that looks right.

**And before adding to `RoomRules`, read `docs/research/rooms-and-gold.md`
§"What is not implemented, and is not guessed at".** Same discipline: rooms have
no size rules, no selling and no behaviour on purpose.

**And `docs/research/tile-work.md` §"What does not fit" still stands.** Q20
(DigRockHealth destroys a rock tile in one application) is now visible in every
imp scenario: a marked wall falls one work period after an imp reaches it,
whatever the wall is. It is a data anomaly, not a bug in the rules. Do not "fix"
it by changing the number.

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
- Workers do marked work only, on purpose (ADR-0014). Adding a job means
  answering Q24, not writing code.
- **Gold lives in tiles; a player has no gold counter** (ADR-0015). DK2 has no
  player-gold variable — it has four per-place limits. `GoldStore` is the only
  way gold moves.
- Where a room may go is read from the room's own placeable-on flags, so nothing
  in the rules knows what a bridge is (ADR-0016).
- Reference install: `E:\GogGames\Dungeon Keeper 2` (GOG). Resolved by
  `DK2.Data.Dk2Install`. **`DK2_DIR` is authoritative when set** — if it is set
  and invalid, no fallback is tried, which is how CI runs with no install.
  Tests auto-skip when it resolves to nothing.

---

## Conventions that are easy to violate by accident

- **A balance constant hardcoded in simulation code is a bug** — read it from
  `GameDatabase`. `GameDatabase[VariableTypeId.X]` throws rather than returning
  zero for a missing value, on purpose.
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

`docs/research/open-questions.md` holds all 36 (one answered). The ones that
gate progress:

- **Q31 — where mined gold goes.** The other half of M3's gold loop. Needs the
  OBJECTS record for the gold-pile branch (M6) and measurement against the
  original for the threshold. See the "Immediate next steps" entry.
- **Q24 — worker job priority.** Still the one blocking the rest of M3.
  An imp digs what is marked, but also claims, reinforces, carries gold and
  flees; the order it prefers them in and what makes it start an unprompted one
  are UNKNOWN. This is *not* Q2 — worker jobs are not in the creature job tables
  at all (the Imp's only entry is `Explore` at 100%).
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
