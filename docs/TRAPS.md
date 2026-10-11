# Traps

Tooling, encoding and process traps that have cost a session time, one line each, for every
agent (Claude's private memory and Codex's are not shared; this file is). Add a line in the change
that hits a new one; delete a line whose cause is fixed. Ghidra-specific traps live in
`.agents/skills/ghidra-queries/SKILL.md`, DK2 facts in `docs/research/`.

## Shell and files

- Unity Package Manager rejects a UTF-8 BOM in Packages/manifest.json; write that JSON as UTF-8 without a BOM.
- PowerShell can return early from a GUI executable invoked with `&`; use `Start-Process -PassThru` and wait on that process for Unity builds and player captures.

- PowerShell treats curly apostrophes as quote delimiters; avoid them inside single-quoted command arguments.
- Windows PowerShell Get-Content defaults to ANSI; pass -Encoding UTF8 when deriving patch text from repository files.

- A linked worktree may lack ignored replay drivers and captures; Claude scratch scripts can be recovered from its project JSONL Write calls, while captures remain under the main clone's reverse-engineering/dk2runs.

- Quarantined research .txt files may contain decompiler output; never preview their prefixes. Extract only facts or explicitly identified assembly sections.

- BitReaders.py takes an offset without `0x` (for example `3ea`); a prefixed offset silently searches for `+0x0x3ea` and returns no matches.

- Path.GetFullPath is lexical and does not detect junction or symlink aliases; output guards must inspect filesystem links.
- Unlink temporary Windows junctions before recursively deleting their targets; a dangling junction can make .NET cleanup fail with access denied.

- A long heredoc fails in the Bash tool, and any heredoc collapses backslash pairs: write a patch
  script to a file and run it. A heredoc also ends an `&&` chain; what follows runs regardless.
- Rewriting a file through `utf-8-sig` in and `utf-8` out drops its BOM; keep the BOM and the
  line endings you found (most docs and sources here are CRLF with a BOM).
- After a merge, autocrlf leaves CRLF working copies: a patch matching `"\n"`-joined text finds
  nothing, and PowerShell multiline `$` anchors need `\r?$`. Normalise to `\n` before matching and restore the endings on write.
- A file restored with `mv` keeps its old mtime, and the next build may skip it.
- Git Bash `sed -i` rewrites a CRLF file as LF, and `grep -c $'\r$'` there is no CRLF check (it reported 0 on a CRLF file): count `\r\n` bytes instead.
- `apply_patch` rejects a delete and add of the same path in one patch; use an update or separate patches.
- `apply_patch` Add File can overwrite an existing path; check existence and update existing function records so their history survives.
- A fixed TRX LogFileName with one results directory overwrites each solution project's results; use the logger's default distinct names.
- KB evidence fragments match Markdown headings, not HTML anchors; changing an evidence header also requires a dated History line.
- In a sibling worktree, `apply_patch` cannot create parent directories; create them with an approved native filesystem command first.
- Windows Python defaults to a legacy code page here; use `encoding='utf-8-sig'` when reading repository Markdown.
- PowerShell here-strings piped into Python can replace non-ASCII source literals with `?`; use ASCII script literals or `apply_patch` for Unicode edits.

## Builds, tests and dk2tools

- Unity-loadable DK2.Data targets netstandard2.1; newer Path helpers such as TrimEndingDirectorySeparator are unavailable there.
- A pixelated Unity Game view can be editor zoom: check its Scale slider at 1x before changing rendering; camera zoom does not reset it (saved under UserSettings/Layouts/).
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
- Renaming a research-doc heading breaks every function record whose `evidence:` anchors to it;
  `kb check` fails the commit. Repoint the anchors and give each record a dated History line.
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

- Host Python has Pillow but no NumPy; Blender's bundled Python supplies NumPy for mesh analysis.
- Direct `.ps1` execution is blocked in the Codex host; use a process-scoped `powershell -NoProfile -ExecutionPolicy Bypass -File` invocation.
- Workbench final renders do not reliably show `Object.show_wire`; label these clay views, not wireframe evidence.
- Context-mode subprocess writes are discarded with its sandbox; use native tools for persistent build logs and artifacts.

- A context-mode upgrade can arrive without `node_modules`, and FTS5 search then silently returns
  only old content; `ctx_doctor` names it. Install the bundle's externals out of tree with
  `npm install --prefix <scratch>` at the versions in its `bun.lock` and copy them into the new plugin
  version's `node_modules`: `better-sqlite3`, and `turndown`, `turndown-plugin-gfm` and
  `@mixmark-io/domino`, without which `ctx_fetch_and_index` fails ("Cannot find module 'turndown'")
  while `ctx_doctor` still passes. Claude (`~/.claude/plugins/cache/context-mode`) and Codex
  (`~/.codex/plugins/cache/context-mode`) each have their own copy to fix.
- The C# `LSP` tool runs the Roslyn server inside the VS Code C# extension's versioned folder; after
  VS Code updates that extension, every call "crashed with exit code 1". Update the version in both
  copies of the local `roslyn-lsp` plugin.json and start a new session.
- Headless Blender resolves relative paths (render output, image loads) against its own idea of the
  working directory, not the shell's (renders landed in `C:\build`); `os.path.abspath` every path argument.
- `codex mcp add` rewrites all of `~/.codex/config.toml`, dropping its comments and reordering
  tables; back it up and append the `[mcp_servers.<name>]` block by hand.
- `codex exec` never prompts, so it denies every MCP call that needs approval; approve a tool for
  one run with `-c 'mcp_servers.<server>.tools.<tool>.approval_mode="approve"'`.
- A Python CLI that prints non-ASCII to a cp1252 console crashes after its work is done
  (`mcp-for-blender install-addon` copied the add-on, then raised); set `PYTHONIOENCODING=utf-8`.
- Windows PowerShell with `$ErrorActionPreference = 'Stop'` aborts on a native command's first
  stderr line (Blender prints Python warnings there); set `Continue` around it and judge `$LASTEXITCODE`.

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
- A clean unstaged diff checks no committed changes; run `git diff --check <base> HEAD` and `--cached` before committing new files.
- Snapshot tests need the supported xoshiro RNG; scripted `IRandomAlgorithm` test doubles intentionally have no save format (ADR-0275).
- PowerShell can report failure when a native command writes diagnostics to stderr; finish redirected native runs with `exit $LASTEXITCODE` to preserve the program's exit code.
- Ghidra can omit callback arguments from a decompiler signature; check caller pushes and callee stack cleanup before assigning constructor versus cleanup roles.
- After a failed multi-file apply_patch, inspect each target before retrying: earlier files may already contain the change.
- Handoff pending-work checks require a date on the same physical line; a date on the preceding wrapped line does not satisfy them.
- A decompiler offset on a global typed `int*` is a dword index: `DAT_006ed484 + 0x568` is world byte `+0x15A0`, the simulation stream. Four docs called it a second RNG stream until the disassembly (`ADD EAX,0x15A0`) was read (2026-10-05).
- A `__fastcall` whose argument is a creature's state object (`+0x24E`) reads state-object offsets: `0x00478A20`'s `+0x98` is creature `+0x2E6` (the task kind), not creature `+0x98`; check `+0x10` against the state at `+0x25E` before reading a capture.
- A scan for a state id as a `SetState` argument that looks for pushes before each `CALL 0x00476D30` misses a push followed by a jump to a shared `SetState` tail: the slap pushes 67 at `0x0048B9DB` and jumps to `0x0048BA00`. Before calling a state unreachable, also scan `.text` for the raw `6A xx` push and read each hit (2026-10-07).
- Blender object copies inherit `hide_select`; clear selection locks and assert selection before GLB export with `use_selection=True`, which can otherwise succeed with no meshes.
- Ghidra `ForceFunc.py` can mistake an earlier stack reservation for the function prologue; after confirming the exact entry in disassembly, use `ForceAt.py` instead.
