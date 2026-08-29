# Handoff notes

Working knowledge that would otherwise live only in chat history, per
`AGENTS.md` ("Do not leave important knowledge only inside chat history").
Update this whenever a session ends or context is compacted.

---

## Current state (2026-08-29)

**Phase 1 / milestone M1 data foundation: complete.** 7 commits, 44 tests green,
clean tree. Latest: `3cb6d41`.

What can be read from the original game today:

| Capability | Entry point | Validation |
|---|---|---|
| WAD archives + decompression | `DK2.Data.Wad.WadArchive` | every entry of all 5 archives extracts to declared size |
| KWD/KLD containers | `DK2.Data.Kwd.KwdReader` | 13 editor KWDs + 71 map KWDs + 330 KLDs |
| Global balance table (197 vars) | `DK2.Data.Kwd.KwdVariables` | values spot-checked vs. community docs |
| 46 creature definitions | `DK2.Data.Kwd.KwdCreatures` | ids match Flame; happy-job % sum to 100 |
| 41 terrain + 26 room types | `DK2.Data.Kwd.KwdTerrainRooms` | cross-parser: room→terrain, room→creature |
| Level tile grids | `DK2.Data.Kwd.MapGrid` | every tile of every campaign map |

Extracted data artifacts (the specification the simulation is written against):
`docs/research/game-database-dump.md`, `creature-table-dump.md`,
`terrain-room-dump.md`.

**Next milestone: M2 — deterministic simulation skeleton.** Nothing started.

---

## Immediate next steps (M2)

The first code that is genuinely ours rather than a reading of DK2's data.

1. **`GameDatabase`** in `DK2.Data.Model` — load all parsed records into one
   immutable object, built from a DK2 install path. This is the seam between
   the data layer (done) and the simulation layer (not started).
2. **`GameSimulation`** in `DK2.Simulation` — `Tick()` advancing one `GameTick`,
   holding `WorldState` + `SimRandom`. No gameplay yet; just the loop and state.
3. **`StateHash()`** — structural hash of all simulation state.
4. **CI determinism gate** — same seed + same inputs ⇒ identical hash at every
   tick. Must be green before any gameplay logic lands, or fidelity work later
   is meaningless. Also add the `DK2.Simulation` → no-Unity check to CI (the
   test exists; CI does not run yet).
5. **Scenario harness** — `DK2.Harness` reads a YAML scenario from `/scenarios`,
   runs N ticks headless, writes an event log. Format sketched in
   `DK2_REIMPLEMENTATION_PLAN.md` §6.2.

Only after that does M3 (grid, digging, claiming, first Imp) make sense — and
the dig/claim numbers are already extracted, see `kwd-terrain-rooms.md`.

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
- Reference install: `E:\GogGames\Dungeon Keeper 2` (GOG). Resolved via
  `DK2_DIR` env var or `Dk2Install`. Tests auto-skip when absent.

---

## Environment gotchas (each of these already cost time)

**Bash heredocs eat backslashes.** Writing C# via `cat > file <<'EOF'` silently
turns `Replace('\\', '/')` into `Replace('\', '/')` — a compile error. This bit
twice. Use the `Write`/`Edit` tools for any C# containing backslash escapes,
or verify with `grep` afterwards.

**`cat > file` with no stdin hangs** the Bash tool until timeout. Never write a
bare `cat >` redirect without a heredoc body.

**`ctx_batch_execute` sandbox quirks:**
- `for ... do ... done` loops fail to parse (the wrapper prefixes the command
  on one line). Use separate commands or `;`-chained statements.
- `/tmp` does not persist between commands in a batch — each gets a fresh
  workspace. Pipe directly, or `curl -o` and use the file in the *same* command.
- `python3` is not available; `curl`, `grep`, `sed`, `awk`, `perl`, `xxd` are.

**Windows/git:** CRLF warnings on every commit are expected noise, not a problem.

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

- **Q1 — simulation tick rate.** Blocks all timing fidelity. Flame's `limit_tps`
  patch proves it is fixed; the value is unmeasured. Needed at M5.
- **Q5 — pathfinding.** The project's biggest risk (plan §9, R1). Flame names
  `Follow_Path` but has zero named fields for it. Nobody has solved this.
- **Q2 — job selection.** Narrowed: candidates and weights are *data*. The
  remaining unknown is the procedure around them (re-evaluation timing, how
  mood picks which of the three tables applies, handling unavailable jobs).
- **Q7 — creature moods.** Genuinely unsolved; OpenKeeper's system is an empty
  stub after 12 years.

---

## Conventions

- Confidence tags are mandatory in docs and code comments:
  **CONFIRMED / STRONGLY INFERRED / HYPOTHESIS / UNKNOWN**.
- Every researched mechanic gets a `docs/research/<name>.md` using the template
  in `DK2_REIMPLEMENTATION_PLAN.md` Appendix B.
- A balance constant hardcoded in simulation code is a bug — read it from
  `GameDatabase`.
- Never commit anything from `original-reference/` or any `.kwd`/`.wad`/`.kmf`
  (gitignored, but check before `git add -A`; a third-party template slipped in
  once and had to be removed in `4cdcd38`).
