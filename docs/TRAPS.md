# Traps

Tooling, encoding and process traps that have cost a session time, one line each, for every
agent (Claude's private memory and Codex's are not shared; this file is). Add a line in the change
that hits a new one; delete a line whose cause is fixed. Ghidra-specific traps live in
`.agents/skills/ghidra-queries/SKILL.md`, DK2 facts in `docs/research/`.

- Native creature positions are 12.12 but steering-body positions are 16.16; boundary pose controls must shift by four when updating both.

## Shell and files

- If Codex's Windows shell fails before startup with `setup refresh had errors`, an approved command outside the sandbox can still run; Docker context-mode uses `/workspace`, not Windows drive paths.

- Docker MCP can list zero tools when a host mount is rejected; inspect `docker mcp gateway run --profile local --dry-run --verbose` and see `tools/context-mode/README.md` for the read-only allowlist.

- `git add` can stage deletions before rejecting an ignored path; inspect the index before retrying, since already-staged deletions no longer match a pathspec.

- PowerShell 5 with ErrorActionPreference=Stop treats native stderr as NativeCommandError; capture child stdout/stderr directly when expecting test failures.

- ClassicLightWindowTests.WarmGatherDoesNotAllocate once measured6440 bytes in a full run; isolated and full reruns passed. Investigate the allocation source before weakening its zero-allocation assertion.

- Inspect original BMP captures in small batches; nine 1600×1200 images exceeded code-mode's 64MiB IPC frame limit.

- Warm isolated terrain GPU probes before recording pixel baselines; the first presentation can differ from subsequent terrain flushes.

- Unity 6000.6 returned the material override tag value `True` as `true`; use lowercase marker tokens when comparing `Material.GetTag` results.
- Sandboxed Unity batch startup can stall at `Licensing is not yet initialized`; an isolated probe may need execution outside the sandbox to use the installed license.

- A managed worker with a different Windows SID may need per-command `git -c safe.directory=<absolute repository>`; do not change the owner's global Git trust configuration.

- Independent UI Toolkit PanelSettings cannot be assigned to a UIDocument nested under another UIDocument; give isolated probe panels separate root GameObjects.

- UI Toolkit rounded overflow masks need a stencil-capable RenderTexture target; a depth-zero probe can draw the mask itself as white.

- Windows PowerShell 5 needs both System.IO.Compression and System.IO.Compression.FileSystem loaded before ZipArchiveMode resolves.
- New function records require `evidence:` before the first blank line; `kb index` rejects a split header.

- `Capture.frames(start, stop)` includes the stop header; use `birth + 3` for four snapshots, and copy values before advancing its reused snapshot.

- Keep capture snapshot headers separate from in-tick action stamps; subtracting one only from native rows misaligns the two packet birth anchors (research/imp-observation-clocks.md).

- A native emulated strike can change a tile before faulting in presentation; require a successful return before treating any partial writes as an oracle result.

- If CS0009 names a zero-filled generated reference DLL, rebuild with `--no-incremental` before treating downstream missing types as source defects.

- Cast raw PowerShell Get-Content results to [string] before ConvertTo-Json; otherwise extended properties can turn the value into an object.
- Unity's C# 9 compiler rejects struct `with` expressions accepted by the adapter's newer compiler; construct replacement values explicitly in Unity/probe source.

- Runtime-selected material variants can disappear only in standalone builds; see [the verified card regression](research/classic-lighting-verification.md#windowed-player-and-counter-calibration).

- Missing Unity mesh UV2/UV3 shader inputs arrived as ones on D3D12, routing static terrain into animated lighting; publish explicit zero channels instead of assuming absent streams are zero.

- Unity 6000.6 Mono's per-thread allocation meter reports 0 for a live 64KiB allocation; calibrate before claiming zero, and do not treat GC.Alloc's TimeNanoseconds recorder as bytes.

- Reset mesh UV channels with explicit zero arrays after GPU fixtures; clearing them to empty can retain stale stream values after another UV channel changes.

- Context-mode 1.0.169 can fail with missing better-sqlite3; use bounded native reads until its dependency is repaired.

- D3D12 skips draws with an unbound declared StructuredBuffer even behind a runtime-disabled branch; use shader variants to omit inactive buffer resources.
- Recentring a probe's Mesh.vertices around a moved Transform needs RecalculateBounds after each replacement; stale local bounds can cull unchanged world geometry.
- Edit-mode world probes must explicitly prepare runtime transparent batches; an opaque capture can otherwise omit blended terrain entirely.

- Unity destroys GameObjects at frame end; a same-frame Transform.Find can return the replaced actor. Disable it immediately and sample the active replacement in probes.

- `dk2tools status` discovery counts can differ from full test-run totals; report verified counts from `dotnet test` summaries.

- Equal source/DLL timestamps can hide edits from incremental MSBuild here; use `dotnet build --no-incremental` when changed decoder tests appear stale.
- `dotnet test -t` means list-tests, not an MSBuild target; rebuild with `dotnet build --no-incremental`, then use `dotnet test --no-build`.

- Unity Package Manager rejects a UTF-8 BOM in Packages/manifest.json; write that JSON as UTF-8 without a BOM.
- PowerShell can return early from a GUI executable invoked with `&`; use `Start-Process -PassThru` and wait on that process for Unity builds and player captures.

- PowerShell treats curly apostrophes as quote delimiters; avoid them inside single-quoted command arguments.
- Windows PowerShell Get-Content defaults to ANSI; pass -Encoding UTF8 when deriving patch text from repository files.
- Windows PowerShell `Set-Content -Encoding UTF8` adds a BOM even to commit-message files; use `UTF8Encoding(false)` when writing them.
- Run `dk2tools status` with the same `DK2_DIR` as the verified suite; data-dependent discovery otherwise silently reports fewer cases.

- A linked worktree may lack ignored replay drivers and captures; Claude scratch scripts can be recovered from its project JSONL Write calls, while captures remain under the main clone's reverse-engineering/dk2runs.

- Quarantined research .txt files may contain decompiler output; never preview their prefixes. Extract only facts or explicitly identified assembly sections.

- BitReaders.py takes an offset without `0x` (for example `3ea`); a prefixed offset silently searches for `+0x0x3ea` and returns no matches.

- Path.GetFullPath is lexical and does not detect junction or symlink aliases; output guards must inspect filesystem links.
- Copy `reverse-engineering/` evidence off the clone before any worktree cleanup: the gitignored native captures in `dk2runs/` were emptied during the 2026-10-18 worktree removal (cause unknown) and had no backup; AGENTS.md now keeps evidence in D:\DK2Evidence.
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
- KB list fields use repeated `key: value` headers; indented YAML arrays are rejected by `kb check`.
- In a sibling worktree, `apply_patch` cannot create parent directories; create them with an approved native filesystem command first.
- Windows Python defaults to a legacy code page here; use `encoding='utf-8-sig'` when reading repository Markdown.
- PowerShell here-strings piped into Python can replace non-ASCII source literals with `?`; use ASCII script literals or `apply_patch` for Unicode edits.
- `WadArchive.TryGetEntry` returns the later of two same-named entries; FrontEnd.WAD has 31 duplicate names (loading screens, `2dLevelS*`, Cancel/Confirm), so look those up by entry index ([front-end-assets.md](research/front-end-assets.md) §1.3).

## Builds, tests and dk2tools

- Record HEAD with the build command; a later provenance snapshot can name a newer commit while the measured binaries remain unchanged.

- `setup-dotnet` installs the exact SDK from `global.json`, ignoring its roll-forward policy; use a published SDK version, not the runtime version `10.0.0`.

- Unicorn can retain translated code across oracle requests: use a fixed stub reading variable data, or a fresh process, when varying controlled returns; rewriting an immediate can silently reuse the first value.

- Snapshot mutable diagnostic objects when recording each row; serializing retained references later repeats their final state across the trace.

- Scratch SDK projects include nested `.cs` snapshots by default; keep copied Unity probe sources outside their compile glob.

- An authored camera update can overwrite a batch probe's Heart focus; reapply the focus after `DungeonRunner.Update` before judging Heart clearance from captures.

- A sandbox restore can fail NU1900 when NuGet audit is unreachable; use cached packages with a per-command `-p:NuGetAudit=false`, without changing repository audit settings.

- Function KB headers use CONFIRMED/LIKELY/GUESS/DISPUTED; STRONGLY INFERRED belongs in research prose, with LIKELY in the KB header.

- `kb_upsert` with `evidence` replaces the whole list, and `name`/`role` overwrite an existing record's; `kb_get` first and pass the union, or pass only `note`.

- Concurrent branches can reuse hash/save versions or ADR ids; check current master before merging and allocate a fresh combined version when layouts differ.

- Source notices require a blank line after the three license comments; the exact LicenseHeaderTests prefix includes two trailing newlines.

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
- Unicorn's `uc_emu_start` kills a .NET process with 0xC0000409 (no dump, no event) while the
  same DLL works from CPython; the oracle runs it in `tools/oracle/dk2oracle.py` for that reason.
- A nonzero `uc_emu_start` timeout costs ~15 ms a call (a timer thread each time): 95k calls took
  23 minutes. Pass 0 and bound the run by instruction count.

## Worktrees, numbers and merges

- The dk2kb MCP (`kb_get`, `kb_search`) reads the main checkout. In a worktree, an empty `kb_get` does not mean the record is missing: check `docs/research/functions/<ADDRESS>.md` on disk and merge into it, keeping its History.

- ADR, question and `HashVersion` numbers are read from committed files in every worktree, and
  master moves under a long task: re-check all of them immediately before each commit, and expect
  to renumber after merging master.
- After merging master the rig baseline moves: measure a detached worktree at the new master and
  the branch side by side before writing numbers into an ADR.
- Never `git add -A`: another session may be editing; stage named paths.
- `git stash push -- <path>` on a path with no changes saves nothing and says so only without `-q`; a `git stash pop` after it applies whatever older stash is on top (2026-10-14: someone's INDEX.md stash landed in the main tree). Check `git diff --quiet -- <path>` first, or pop by the entry's own message, never a bare pop.
- `kb_upsert` (the dk2kb MCP) writes the main tree, not your worktree, and regenerates the main tree's `INDEX.md` too: copy both files across and `git checkout --` them in the main tree.
- `kb_upsert` replaces a record's whole body and can drop its UTF-8 BOM: an agent that thinks a record is new
  overwrites an existing one (005956E0, 2026-10-12). Diff `name:` lines and first bytes against HEAD before committing.
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
- Ghidra scripts using plain `open(..., 'w')` can fail on non-ASCII decompiler text under Windows cp1252; set `PYTHONUTF8=1` for the runner (stdout encoding alone does not fix file writes).
- `unity/DK2.Unity.Compile` references Unity's module DLLs directly, so it passes code that the
  project itself rejects when the built-in module is not in `Packages/manifest.json` (the movie
  player's `AudioSource` compiled there and failed in the editor until `com.unity.modules.audio`
  was added). Using a new `UnityEngine.*Module` type means adding its `com.unity.modules.*` entry too.
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
- A recording that ends `"game stopped answering"` early is usually DK2 crashing, not the probe: the Windows Application log has 14 access violations at `DKII-DX.exe+0x1FCB80` (`0x005FCB80`, in the renderer range) since 2026-09-05, one of them the Level 4 dump run at tick 1176 (2026-10-15), well after its tick-500 dump. Check the log before blaming the recorder or a dump.
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
- On Windows, running replay `DK2.Tools` processes lock their simulation DLL; finish them before rebuilding the solution or the Tools copy fails with MSB3027.
- A fresh worktree's `dotnet test --no-restore` can exit successfully with no tests when its test SDK assets are absent; require an actual test summary and restore that project once.
- `git switch -c` in the shared main checkout moves every session's HEAD: another session's next three commits landed on the new branch, and one swept in a change staged there (b280ce8b, 2026-10-14). Make side branches in a worktree, and commit with an explicit path list (`git commit -- <paths>`).

- In a Unity batch editor without a rendered view, waiting for `currentPipeline` does not initialize URP; call public `RenderPipeline.SupportsRenderRequest` with a temporary camera before submitting a render request.
- Windows PowerShell `Set-Content -Encoding utf8` writes a BOM; use BOM-free UTF-8 for commit-message files or the subject starts with an invisible character.

- A centred one-tile GPU lighting fixture does not establish whole-scene parity: also capture production torches on/off, a nonzero grid tile, shifted view and repeated presentation before accepting the result.
- Sandbox escalation can still receive Windows error5 reading DK2; an explicit Windows `RunAs` launch succeeded. The owner must be present for UAC; sandbox approval does not change the process token.
- If context-mode file processing fails with missing `better-sqlite3`, use bounded native output for the task rather than repeatedly retrying the broken runtime.
- An offline restore can fail NU1900 under warnings-as-errors even with cached packages; use `-p:NuGetAudit=false` for that local verification run, leaving CI auditing enabled.
- A compile harness can reference an installed Unity module that the project's Package manifest has disabled; verify in Unity too (HUD pilot: `com.unity.modules.imageconversion`).
- A centred GPU minimap fixture hides row-origin errors; test an asymmetric fit and a rotated marker, and retain integer division for native projected boundaries rather than flooring an inexact floating-point quotient.
- pyghidra search_code defaults to full code even with preview_length; pass include_full_code=false for bounded search results.
- Ghidra's decompiler search omits undefined functions; a negative field-offset query needs a scan across both executable sections and a known positive control.

- PowerShell does not expand path globs for `rg`; search a directory with `-g 'WorldState*.cs'` rather than passing `WorldState*.cs` as a path.
- Native byte arguments can have unrelated upper stack bits; oracle stubs must mask to the callee's actual width before indexing or testing availability.
- `apply_patch` with `Add File` can replace an existing file; check that a knowledge-base record is absent before adding it, and preserve existing evidence/history when updating it.
- If the native oracle's Python helper exits because `py -3` has no registered interpreter, set `DK2_PYTHON` to the installed executable alongside `DK2_UNICORN_DLL`.
- Unity Mono rounds `TimeSpan.FromSeconds(1f/60f)` to170000ticks; construct elapsed time with `FromTicks(round(seconds*TicksPerSecond))` when sub-millisecond accumulation matters.
- A Unity editor callback can report another window's `Screen` dimensions; sample live Game-view projection in `RenderPipelineManager.endCameraRendering`, not immediately after assembly reload.
- The source license gate requires all three notice lines plus a blank line, including the LICENSE reference; a copyright and SPDX pair alone fails, including in Unity editor scripts.
- A sandbox account can differ from the checkout owner; use per-command `git -c safe.directory=D:/DungeonKeeperRemake` (or inherited `GIT_CONFIG_COUNT` entries for repo tools), without changing the owner's global Git config.
- `hand-cursor-rendering.md` contains legacy single-byte section signs among UTF-8 text; strict UTF-8 patching rejects it. Keep unrelated byte normalization out of a cursor fix.
