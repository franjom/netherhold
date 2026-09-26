# Verification sweep of everything built

- **Status:** planned for the week of 2026-09-28. Not started.
- **Why:** the ADR-0232 audit found a built rule resting on a misread "only writer" claim: a
  heartless player's heart was taken to be the origin, and DK2 uses (1, 1). The earlier checks
  had been done one feature at a time. This sweep checks every built rule once, in a fixed order,
  with a record of what was checked.

## 1. Setup

1. **One Ghidra project per worker**, in its own folder under `D:\Ghidra\claude ghidra\`:
   `D:\Ghidra\claude ghidra\worker-1\`, `worker-2\`, and so on. Each is a copy of
   `D:\Ghidra\ghidra projects claude\DK2_RE`, opened `-readOnly`. The copies remove the lock that
   forces headless runs to be sequential.
   - The helper scripts take the project path as a parameter rather than hardcoding it.
   - Each copy has its own listing cache (`reverse-engineering/.cache` is keyed on the project
     directory, so this is automatic).
   - A positive control first in each copy: `ScanAll.py 006F2590` must report 226 hits.
2. **The ledger**, `docs/verification-ledger.md` or a generated CSV. It has one row per built rule:
   - the class and its `[Dk2Behavior]` confidence;
   - its research doc and ADR;
   - its tests;
   - every binary address cited in its comments;
   - one status column per check in §2, and the date.

   A `dk2tools` command generates the rows from the attributes, so nothing is typed by hand. The
   ledger is also the resume file.

## 2. The four checks, per rule

| # | Check | How | Catches |
|---|---|---|---|
| A | **Constants against the binary** | A script pairs each constant in a rule with the address cited beside it, disassembles there and looks for the immediate | misread or mistyped numbers |
| B | **Negative claims** | The ADR-0232 procedure over every doc: `CallScanAll.py`, `ScanAll.py`, and `InsGrep.py` with padded addresses (`0x%08x]`) and the one to three bytes below. Each query includes a known hit | "only", "never", "no reader" that are false |
| C | **Test strength** | Stryker.NET, scoped to one rule file at a time, over `DK2.Simulation.Tests` | behaviour no test pins; guesses a test hides |
| D | **Fresh reading** | A reviewer rereads the rule's routines in Ghidra against the doc and the code, without the earlier conclusions. Codex does a share of these in `D:\DungeonKeeperRemake-Codex` for a second opinion | reading errors like the (1, 1) one |

A and B are scripted and run first, over everything. C and D are per rule, in the order of §3.

## 3. Order

1. Rules marked `Hypothesis` (2 on 2026-09-26), then `StronglyInferred` (98). There are 65
   `Confirmed` rules, 165 attributed in all.
2. Rules whose doc rests on an "only", "never" or "no reader" claim.
3. Work from before 2026-09-07, when `cseg` was not scanned and scans were `.text` only.
4. The rest, by subsystem.

## 4. Rules for the sweep

- **At most four workers at once**, each writing its findings to a file as it goes.
- **Every finding is verified before anything is fixed.** A fix gets a negative control that fails
  its test, as in ADR-0232.
- **Behaviour the owner can observe** is batched into one list of questions, not asked piecemeal.
- **Commits** stage named paths only; never `.claude/`, `CLAUDE.md`,
  `docs/STRUCTURE_REFACTOR_PLAN.md` or `docs/HANDOFF.md`. Question numbers and `HashVersion` are
  checked against the Codex worktree before one is taken.

## 5. After the per-rule checks

**Compare against the game end to end.** The world-log dump (`0x0050EA70`) and DK2's own
`-PSAVE`/`-PLOAD` replay compare our simulation with DK2 over a whole session. That catches
wiring errors between rules that no per-rule check sees.

## 6. First steps, in order

1. Copy the Ghidra project into `D:\Ghidra\claude ghidra\worker-1\` … `worker-4\`, and run the
   positive control in each.
2. Write the ledger generator and produce the ledger.
3. Write check A's script and run it over every rule. Its count of mismatches sizes the rest of
   the sweep.
4. Run check B over all docs.
5. Then C and D in the order of §3. If this runs as a multi-agent workflow, it goes in waves of at
   most four workers.
