# Handoff notes

Working knowledge that would otherwise live only in chat history, per
`AGENTS.md` ("Do not leave important knowledge only inside chat history").
Update this whenever a session ends or context is compacted.

---

## Current state (2026-08-29)

**M2 deterministic skeleton: complete. M1 data foundation: complete for the
five record types the simulation needs.** 88 tests green with and without a DK2
install, clean tree.

Two honest caveats:
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

Extracted data artifacts (the specification the simulation is written against):
`docs/research/game-database-dump.md`, `creature-table-dump.md`,
`terrain-room-dump.md`.

**Next milestone: M3 — dungeon prototype.** Nothing started.

---

## Immediate next steps (M3)

The first real mechanics. Everything they need is already extracted — this is
implementation against a known specification, not research.

1. **Tile mechanics as systems** — dig, claim, reinforce, repair. The numbers
   are all in hand: `DigRockHealth=-5000`, `ClaimTileHealth=825`,
   `MineGoldHealth=-300`, `ReinforceWallHealth=50`, `RepairWallHealth=22`
   (`kwd-variables.md`), and the terrain transition graph
   (`kwd-terrain-rooms.md`). Read them from `GameDatabase`, never inline.
2. **The first real commands** — `TagTilesCommand`, `ClaimCommand`. `TileState`
   gains a tagged-for-digging flag at this point, not before.
3. **The open timing question surfaces immediately.** The health deltas are
   per *something* — Q1, the tick rate, is UNKNOWN. Implement the deltas behind
   a single named constant and flag it; do not silently pick 25 ticks/second.
4. **First Imp** — spawn, task selection, movement. This is where `IPathfinder`
   gets its honest A* placeholder (plan risk R1).
5. **Treasury and gold** — the first resource loop.
6. **Real dig/claim scenarios** in `scenarios/`, replacing the two smoke files.

Unity presentation also belongs to M3, but the simulation should be able to run
a dig-and-claim scenario headlessly first.

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
  hash and the snapshot — and therefore silently non-deterministic.
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

`docs/research/open-questions.md` holds all 19. The ones that gate progress:

- **Q1 — simulation tick rate.** Now blocking: M3's health deltas are per unit
  of time and we do not know the unit. Flame's `limit_tps` patch proves it is
  fixed; the value is unmeasured. Keep it one named constant until measured.
- **Q5 — pathfinding.** The project's biggest risk (plan §9, R1). Flame names
  `Follow_Path` but has zero named fields for it. Nobody has solved this.
- **Q2 — job selection.** Narrowed: candidates and weights are *data*. The
  remaining unknown is the procedure around them (re-evaluation timing, how
  mood picks which of the three tables applies, handling unavailable jobs).
- **Q7 — creature moods.** Genuinely unsolved; OpenKeeper's system is an empty
  stub after 12 years.
- **New at M2 — system update order.** DK2's own per-tick subsystem ordering is
  UNKNOWN. Ours is explicit and declared at registration. It starts to matter
  around M4; treat it as a research question then, not a convenience choice.
