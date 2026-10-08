# Verification sweep of everything built

- **Status:** underway (checked 2026-10-08). The ledger exists and checks A, B and a
  D pilot have run as audits (§2); no rule has passed all four, and the ledger's A-D cells
  are still blank except one C entry.
- **Why:** the ADR-0232 audit found a built rule resting on a misread "only writer" claim: a
  heartless player's heart was taken to be the origin, and DK2 uses (1, 1). The earlier checks
  had been done one feature at a time. This sweep checks every built rule once, in a fixed order,
  with a record of what was checked.

## 1. Setup

1. **Ghidra needs no per-worker setup.** Every runner call and every pyghidra-mcp session works
   on its own clone of the one project `D:\Ghidra\DK2_RE`, deleted at exit, so workers never
   collide (`ghidra-queries` skill; `tools/ghidra/ghidra.py --agent <name>`,
   `tools/ghidra/mcp-serve.py`).
   - Use the MCP server for single-function reads and the runner for scripted bulk scans.
   - A positive control first in each run: `ScanAll.py 006F2590` must report 226 hits.
2. **The ledger**, the generated CSV `docs/verification-ledger.csv`. It has one row per built rule:
   - the class and its `[Dk2Behavior]` confidence;
   - its research doc and ADR;
   - its tests;
   - every binary address cited in its comments;
   - one status column per check in §2, and the date.

   A `dk2tools` command generates the rows from the attributes, so nothing is typed by hand. The
   ledger is also the resume file.

`dk2tools verification-ledger` writes `docs/verification-ledger.csv` and preserves A-D
and date cells on regeneration; `--check` detects a stale ledger. It holds 256 attributes
on 2026-10-08 (1 Hypothesis, 180 StronglyInferred, 75 Confirmed). `CandidateTests`
means a lexical source reference, not proof that a test pins the rule. ADRs are
collected from the source comments and the linked research note; absent citations
remain blank for review.

## 2. The four checks, per rule

| # | Check | How | Catches |
|---|---|---|---|
| A | **Constants against the binary** | A script pairs each constant in a rule with the address cited beside it, disassembles there and looks for the immediate | misread or mistyped numbers |
| B | **Negative claims** | The ADR-0232 procedure over every doc: `CallScanAll.py`, `ScanAll.py`, and `InsGrep.py` with padded addresses (`0x%08x]`) and the one to three bytes below. Each query includes a known hit | "only", "never", "no reader" that are false |
| C | **Test strength** | Stryker.NET, scoped to one rule file at a time, over `DK2.Simulation.Tests` | behaviour no test pins; guesses a test hides |
| D | **Fresh reading** | A reviewer rereads the rule's routines in Ghidra against the doc and the code, without the earlier conclusions. Codex may do a share for a second opinion | reading errors like the (1, 1) one |

A and B are scripted and run first, over everything. C and D are per rule, in the order of §3.

**Scripts** (`tools/verification/`): `constants.py` inventories literal constants in
attributed source files, `match_constants.py` pairs them with the disassembly window at
their cited addresses, `negative_claims.py` inventories "only / never / no reader" wording
across `docs/`, and `ScanBatch.py` and `InsBatch.py` run the raw and instruction scans over each claim's
nearby addresses (with the three preceding bytes), alongside `CallScanAll.py` in `tools/ghidra/scripts/`. No negative claim is cleared solely by
an address count: indexed accesses such as the ADR-0232 counterexample can use a
different base address.

**Audit of 2026-10-05** (commits `f2010e0b`, `1f1f13cb`; queues and findings in
`build/verification/2026-10-05/`, local): 32 cited constants (A), 81 "only / never"
claims behind built rules (B) and 19 suspect built function records (D), each finding
re-read in Ghidra by a reviewer. None changed built behaviour; the false or overstated
claims were corrected in the docs, and a few code items were listed for a follow-up
branch. Two imp function records were also read blind (`blind/imp-compare.md`).

**C so far:** Stryker.NET 5.0.0, Basic mutations in `ImpTaskSystem.cs` against
`DK2.Simulation.Tests`, scored 76.21% on 2026-09-27 (49 survived, 25 no coverage, 30
timeout). The vertical three-tile drop boundary survived; `HandTests` now covers both
axes. C stays Partial until the other survivors and timeouts are reviewed. Run Stryker
from `tests/DK2.Simulation.Tests/` with `--project DK2.Simulation.csproj` and
`--mutate Systems/ImpTaskSystem.cs`; launching from the solution root also analyzes
unrelated test projects.

## 3. Order

1. Rules marked `Hypothesis` (1 on 2026-10-08), then `StronglyInferred` (180). There are 75
   `Confirmed` rules, 256 attributed in all; the ledger is the current count.
2. Rules whose doc rests on an "only", "never" or "no reader" claim.
3. Work from before 2026-09-07, when `cseg` was not scanned and scans were `.text` only.
4. The rest, by subsystem.

## 4. Rules for the sweep

- **At most two subagents at once** (owner rule, 2026-09-30; waves of four hit the session limit), each given whole units, each writing its findings to a file as it goes.
- **Every finding is verified before anything is fixed.** A fix gets a negative control that fails
  its test, as in ADR-0232.
- **Behaviour the owner can observe** is batched into one list of questions, not asked piecemeal.
- **Commits** stage named paths only; never `.claude/` or `CLAUDE.md`. Question numbers and `HashVersion` are
  checked against every worktree (`git worktree list`) before one is taken.

## 5. After the per-rule checks

**Compare against the game end to end.** The world-log dump (`0x0050EA70`) and DK2's own
`-PSAVE`/`-PLOAD` replay compare our simulation with DK2 over a whole session. That catches
wiring errors between rules that no per-rule check sees.

**Check what the replay's checksum warning means.** On 2026-09-26 both level 2 recordings
(`q428.sav`, `q428_1.sav`, made with `-LEVEL level2 -PSAVE`) logged
`Gone Out Of Sync (Checksum) GT 6`. The seed matched, the checksum was off by 2, and turns 1–6
carry no player command. Even so, the replay followed the session: the owner watched it play to
the same win, and the log shows the Lord of the Land's party arriving as it did. So the checksum
may cover something local, like the camera or the interface, that does not steer the game.
The recordings of 2026-09-18 raised no warning.

**Answered 2026-09-28: the interface, through condition 72.** Two probed playbacks of one
recording (`l2win928`) went out of sync at GT 81 and GT 9. Their trigger timelines differ in one
entry only, timer 14's start, at 81 and 9. Root 194 starts timer 14 behind a type 72 test, which
reads modal interface state (Q242). The packet does not record that state, so a playback decides
it again. Every flag and the win matched. A replay is sound wherever a 72-gated branch only speaks
or waits; a branch that 72 gates into game state would need the interface modelled.

## 6. Next steps

Steps 1-4 of the original order are done: the positive control, the ledger generator, the
check A and B scripts and one audit batch (§2). What remains:

1. Finish A and B over the rules the 2026-10-05 audit did not reach, and record the result in
   the ledger's A and B cells.
2. Run C and D in the order of §3. If this runs as a multi-agent workflow, it goes in waves of
   at most two workers.

## 7. Function records: blind re-reading

Checks A–D cover built rules. The records in `docs/research/functions/` need their own pass: a
pilot on 2026-10-01 (26 records) found 3 wrong, 12 correct but incomplete, 9 matching, and 2 wrong
blind readings; the omissions were early returns, flags and mode parameters, not wording.

1. **Mechanical pass.** `python tools/ghidra/ghidra.py FuncFacts.py reverse-engineering/fn-facts.json
   reverse-engineering/fn-addrs.txt` dumps each function's operands, callees, `RET n` and callers
   (one Ghidra start, about 30 s); `python tools/verification/check_function_records.py` checks
   every address and offset a record cites against that dump and writes
   `build/verification/function-record-check.csv`. It triages: a cited sibling or caller address
   shows as `absent`, so SUSPECT means "read this first", not "wrong". "Ghidra has no function
   here" means the entry is undefined in the project.
2. **Blind reading.** A verifier gets only the addresses and the pyghidra-mcp tools; it may not
   read `docs/`, `src/`, `tests/` or the kb tools, and writes its own role, arguments, fields,
   callees and constants to `build/verification/<run>/<ADDRESS>.blind.md`, with SURE / PROBABLE /
   GUESS on each claim. The project's Ghidra names came from the records, so a meaning that
   rests on a name is not independent.
3. **Comparison.** A second agent classifies each pair AGREE, PARTIAL, CONTRADICT or UNDECIDED
   and settles every CONTRADICT, and every PARTIAL that could change behaviour, in Ghidra. The
   blind reader is often the one at fault (argument counts from the decompiler, a pass-through
   that is not one), so a contradiction is not a verdict until adjudicated.
4. **Record.** A confirmed error or omission goes into the function's record with a dated
   History line (AGENTS.md hard rule).
5. **Impact.** For every finding on a record with `status: BUILT` or an `implements:` line, and for
   every record whose address `src/` cites, read the implementing code and decide whether it
   relies on the wrong or missing fact. If it does, file **one row in
   `docs/research/open-questions.md`** naming the record, the code, what the code assumes and
   what the binary does, and say what test would pin the fix. A finding that changes a
   shipped rule that was built on the old reading also bumps the rule's `[Dk2Behavior]`
   confidence until fixed. If the code is unaffected, say so in the record's History line, so
   the check is not repeated.
6. **Order.** Records with `status: READ` and nothing downstream first, because no test or replay
   covers them; then records with no function in Ghidra; then LIKELY and GUESS; BUILT records
   last, since their outcomes are already judged against captures. A record marked CONFIRMED
   that cites only a binary read is STRONGLY INFERRED by `RESEARCH_STATUS.md`; list those for
   relabelling as the pass reaches them.
7. **Limits.** At most two agents at once, each given a whole unit (about 13 records); the
   comparison runs after both blind readings of a unit are written.
