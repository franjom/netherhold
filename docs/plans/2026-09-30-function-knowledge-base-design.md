# Function knowledge base: design (2026-09-30)

**Decision.** One record per DKII.EXE function, as text in git, is the shared
home for what a function is, how sure we are, why, and how that conclusion
changed. Both agents fill and correct it from any session; a gate keeps it
current. Ghidra shrinks to one canonical project plus clones that delete
themselves. Approved by the owner 2026-09-30 (option A of three).

**Why.** Nothing in either Ghidra project was ever renamed (4,923 named
symbols in each, all imports, CRT and RTTI, identical across the two), so the
projects are analysis caches and the knowledge lives only in docs/research,
where 225 functions are cited across ~50 files with no per-function verdict,
confidence or history. A second session, or the other agent, cannot ask
"what is FUN_0050EA70 and who decided" without reading those files.

## 1. The record

`docs/research/functions/<ADDRESS>.md`, address as eight upper-case hex digits.

```
address: 0x0050EA70
name: WorldLog_Dump
role: Prints every world field and creature state into MESSAGE.LOG.
confidence: CONFIRMED        # CONFIRMED | LIKELY | GUESS | DISPUTED
status: READ                 # READ | BUILT | SUPERSEDED
evidence: docs/research/debug-dumpers.md#world-log
implements: tools/worldlog.py

Reasoning in our own words. No decompiler text (ADR-0046); RTTI class
names stay local as before.

## History
- 2026-09-30 claude: created from debug-dumpers.md; CONFIRMED by the
  Cheat Engine call that appended a dump to MESSAGE.LOG.
```

Header lines are `key: value`, parsed without a YAML library; `evidence` and
`implements` may repeat. `implements` names our code that mirrors the
function's behaviour, not code that consumes its output. `INDEX.md` in the
same folder (address, name, role, confidence) is generated, never edited. A
sqlite index is deferred until a query needs more than INDEX.md and grep.

## 2. The HARD rule and its gate

AGENTS.md gains a HARD rule: **every new finding or correction about a
function goes into its record in the same change, with a History line.** A
document, ADR or source file that names a function without a record does not
pass. Because a claim nothing checks may already be false, `dk2tools kb check`
joins the existing `dk2tools docs` hook and fails on:

- a `FUN_00xxxxxx` in a changed line of `docs/` or `src/` with no record
  (`docs/plans/` is exempt: a plan is a process document, not a claim);
- a record whose header changed in the diff without a History line dated today;
- an `evidence` link whose file or anchor does not exist;
- a duplicate address, or one outside `.text`/`cseg`.

## 3. Tooling

- `dk2tools kb check | index | apply-names`, C# in DK2.Tools beside the docs
  gate, with tests. `index` writes INDEX.md and the sqlite; `apply-names`
  writes record names into the canonical Ghidra project through the runner
  (`--write`), so decompiles read `WorldLog_Dump` instead of `FUN_0050EA70`.
- `dk2kb`, a small Python MCP server (FastMCP, stdio, `uv run`), spawned per
  session by Claude (.mcp.json) and Codex (config.toml) like pyghidra-mcp:
  `kb_get`, `kb_search`, `kb_upsert`, `kb_history`. It writes the record file
  and runs `dk2tools kb check` before confirming. Direct file edits stay valid.

## 4. Ghidra: one project, self-deleting clones

- Canonical project `D:\Ghidra\DK2_RE` (today's `ghidra projects\DK2_RE`).
- `tools/ghidra/mcp-serve.py` clones it per session to
  `D:\Ghidra\mcp\<agent>-<pid>` (instant on ReFS) and deletes the clone on
  exit. The finished semantic index lives once in `D:\Ghidra\mcp\index` and is
  cloned beside the project.
- `tools/ghidra/ghidra.py` clones per run the same way; `--write` is reserved
  for `apply-names` on the canonical project.
- Removed: `backup_projects_11`, `ghidra_11.0.1_PUBLIC`, `claude ghidra`,
  `ghidra projects codex-thunder`, `ghidra projects claude`, old `mcp` copies
  (about 1.9 GB). `akddll`, `akdsh`, `certilia` move to `D:\Ghidra\other
  projects`. Deletion runs from a listed script the owner confirms.

## 5. Import and verification

Mechanical import creates a record for each of the 225 cited functions with
`confidence: GUESS`, `status: READ`, the role taken from the sentence of the
first citation and `evidence` pointing at that section. Agents then review the
records in waves of four, raising confidence to what the evidence supports.
Done means: `kb check` passes over the whole repo, `mcp-check.py` passes for
both agents, and one record was written through `dk2kb` from Codex and from
Claude in separate sessions.
