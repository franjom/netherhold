# Traps

Tooling, encoding and process traps that have cost a session time, one line each, for every
agent (Claude's private memory and Codex's are not shared; this file is). Add a line in the change
that hits a new one; delete a line whose cause is fixed. Ghidra-specific traps live in
`.agents/skills/ghidra-queries/SKILL.md`, DK2 facts in `docs/research/`.

## Shell and files

- A long heredoc fails in the Bash tool, and any heredoc collapses backslash pairs: write a patch
  script to a file and run it. A heredoc also ends an `&&` chain; what follows runs regardless.
- Rewriting a file through `utf-8-sig` in and `utf-8` out drops its BOM; keep the BOM and the
  line endings you found (most docs and sources here are CRLF with a BOM).
- After a merge, autocrlf leaves CRLF working copies: a patch matching `"\n"`-joined text finds
  nothing. Normalise to `\n` before matching and restore the endings on write.
- A file restored with `mv` keeps its old mtime, and the next build may skip it.

## Builds, tests and dk2tools

- A one-project build leaves the harness's copies of dependency DLLs stale; build `DK2.slnx`
  before any `--no-build` run.
- `dk2tools status` lists tests from a Release build; run it after the tests, not after a bare
  build, or it writes a short count into the plan.
- `verification-ledger --check` goes stale on any rule, ADR or test change, and the commit hook
  does not run it.
- `docs --check` covers unstaged files too: everything changed since HEAD.
- A dk2tools command can behave differently under `dotnet run` and under the hook's
  `dotnet <dll>`: the console code page differs. Decode a child process's output explicitly
  (git writes UTF-8; `Repo.RunGit` does since 2026-10-03).
- Replay rigs and the scenario harness run the built binaries for minutes: do not build the same
  worktree while they run.
- A hung dk2tools with no CPU was a full stderr pipe; drain stderr concurrently.

## Worktrees, numbers and merges

- ADR, question and `HashVersion` numbers are read from committed files in every worktree, and
  master moves under a long task: re-check all of them immediately before each commit, and expect
  to renumber after merging master.
- After merging master the rig baseline moves: measure a detached worktree at the new master and
  the branch side by side before writing numbers into an ADR.
- Never `git add -A`: another session may be editing; stage named paths.
- `kb_upsert` (the dk2kb MCP) writes the main tree, not your worktree.

## Captures

- A full-state capture (`*.dk2cap`, `reverse-engineering/dk2capture.py`) holds every room record
  as well as creatures and tiles; decode a field with `Record.u(offset)`. `things("room")` also
  matches stale slots, so filter by the room slot range (1100 up in the Level 1 and 2 runs).
- Before blaming a mechanism for a rig divergence, check whether DK2's own capture of the same
  packet ever reaches that situation (`dk2runs/level2-live2/seed01` is the `l2live2` packet).
- A doc's "all N records" counts one record type: "every hero has `DestroysRooms`" was true of
  trigger-created creatures and false of party members (825 of 4,545).
