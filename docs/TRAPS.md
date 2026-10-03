# Traps

Tooling, encoding and process traps that have cost a session time, one line each, for every
agent (Claude's private memory and Codex's are not shared; this file is). Add a line in the change
that hits a new one; delete a line whose cause is fixed. Ghidra-specific traps live in
`.agents/skills/ghidra-queries/SKILL.md`, DK2 facts in `docs/research/`.

## Shell and files

- Path.GetFullPath is lexical and does not detect junction or symlink aliases; output guards must inspect filesystem links.
- Unlink temporary Windows junctions before recursively deleting their targets; a dangling junction can make .NET cleanup fail with access denied.

- A long heredoc fails in the Bash tool, and any heredoc collapses backslash pairs: write a patch
  script to a file and run it. A heredoc also ends an `&&` chain; what follows runs regardless.
- Rewriting a file through `utf-8-sig` in and `utf-8` out drops its BOM; keep the BOM and the
  line endings you found (most docs and sources here are CRLF with a BOM).
- After a merge, autocrlf leaves CRLF working copies: a patch matching `"\n"`-joined text finds
  nothing. Normalise to `\n` before matching and restore the endings on write.
- A file restored with `mv` keeps its old mtime, and the next build may skip it.
- `apply_patch` rejects a delete and add of the same path in one patch; use an update or separate patches.
- In a sibling worktree, `apply_patch` cannot create parent directories; create them with an approved native filesystem command first.
- Windows Python defaults to a legacy code page here; use `encoding='utf-8-sig'` when reading repository Markdown.

## Builds, tests and dk2tools

- Unity-loadable DK2.Data targets netstandard2.1; newer Path helpers such as TrimEndingDirectorySeparator are unavailable there.
- InvalidDataException is not an IOException; command error handling must catch both to report format refusals.

- Typed KWD properties can be wider than their file fields: derive overlay test values and range checks from the reader's width, not the model's CLR type.
- Test discovery can intermittently undercount even in an isolated `status` run; compare the suite totals and repeat discovery before accepting a changed count.
- A one-project build leaves the harness's copies of dependency DLLs stale; build `DK2.slnx`
  before any `--no-build` run.
- `dk2tools status` lists tests from a Release build (`--list-tests -c Release --no-build`): run
  `dotnet build DK2.slnx -c Release` and the tests first, or it writes a short count into the plan
  that `status --check` then confirms.
- The plan's status paragraph and the activity and test counts in `ARCHITECTURE.md` and
  `GOAL_ALIGNMENT.md` are written by `dk2tools status`; run it instead of hand-editing them.
- `--artifacts-path` dodges a locked `bin/`, but then the tests that find the repo root by walking
  up from their binaries fail (14 in `DK2.Core.Tests` and `DK2.Behavior.Tests`), and those are the
  ones that read edited source. Rerun those two projects in-tree, one per `dotnet test`.
- Capture files are gitignored and live only in the main tree, so a second worktree skips the
  capture-comparison tests: compare skip counts, not only failures, before merging.
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
- A branch can hold uncommitted work (an agent that ran out of tokens): check `git status` in its
  worktree, not only `git log master..branch`, and run the gates on what it left.
- At most two subagents at once: more hit the account's rate limit and die mid-work. Give each
  whole units of work so an interruption leaves only complete ones.

## Editor and plugins

- A context-mode upgrade can arrive without `node_modules`, and FTS5 search then silently returns
  only old content; `ctx_doctor` names it. Install `better-sqlite3@^12` out of tree with
  `npm install --prefix <scratch>` and copy `node_modules` into the new plugin version's folder.
- The C# `LSP` tool runs the Roslyn server inside the VS Code C# extension's versioned folder; after
  VS Code updates that extension, every call "crashed with exit code 1". Update the version in both
  copies of the local `roslyn-lsp` plugin.json and start a new session.

## Captures

- A full-state capture (`*.dk2cap`, `reverse-engineering/dk2capture.py`) holds every room record
  as well as creatures and tiles; decode a field with `Record.u(offset)`. `things("room")` also
  matches stale slots, so filter by the room slot range (1100 up in the Level 1 and 2 runs).
- Before blaming a mechanism for a rig divergence, check whether DK2's own capture of the same
  packet ever reaches that situation (`dk2runs/level2-live2/seed01` is the `l2live2` packet).
- `dk2capture.py` records objects from slots 1150-1719 only; an object made after the window
  fills never appears in `object-map.csv`. "DK2 never made X" needs another witness.
- A capture's turn N shows the state before tick N's trigger pass: capture row N is our row N-1.
  Prefer a tick the game stores (a timer stamp) to one the probe samples.
- Campaign levels unlock through `Player Level Status` under `HKCU\Software\Bullfrog Productions
  Ltd\...`, 160 bytes indexed by map, not level (`campaign-progress-registry.md`). Close DK2 first;
  it rewrites the key on exit.
- DK2 runs elevated: start the probe from an elevated shell before the game, and the owner clicks
  two UAC prompts. A packet recording needs `-LEVEL` on both `-PSAVE` and `-PLOAD`, or the front
  end eats the seed and the replay desyncs on turn 1.
- A doc's "all N records" counts one record type: "every hero has `DestroysRooms`" was true of
  trigger-created creatures and false of party members (825 of 4,545).
