# Traps

- OpenCode 1.18.35 can report misleading `EEXIST` for a sandbox-denied user config directory; set isolated `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `XDG_CACHE_HOME` and `XDG_STATE_HOME` under the task artifacts.

- Ghidra rejects a project path containing a dot-prefixed directory such as `.claude`; put query project clones under an ordinary path and pass output/cache paths explicitly.

- PowerShell uses `[uint64]`; C#'s `[ulong]` is not a PowerShell type alias.
- Windows PowerShell's `>` writes UTF-16LE; decode redirected native-command logs explicitly before parsing them.

Tooling, encoding and process traps that have cost a session time, one line each, for every
agent (Claude's private memory and Codex's are not shared; this file is). Add a line in the change
that hits a new one; delete a line whose cause is fixed. Ghidra-specific traps live in
`.agents/skills/ghidra-queries/SKILL.md`, DK2 facts in `docs/research/`.

- Python in Windows `ctx_execute` can print through cp1252; call `sys.stdout.reconfigure(encoding='utf-8')` before printing repository text containing Unicode.

- Godot AI 4.4.1's dock shows Claude Code red ("verification still reads error") for a working `local`-scope entry; check with `claude mcp get godot-ai` from the repo root, and do not run its suggested `remove --scope user`.
- Godot 4.8 beta 3 on this machine (project `window/size/mode=3`) ignores `--windowed` and `--resolution`; the window takes the desktop size and a Movie Maker PNG is a 1920x1080 cut of it. Use a temporary `godot/DK2.Godot/override.cfg` (`window/size/mode=0`) for captures.
- Godot 4.8 Movie Maker fixes its frame size at the window size it starts with and scales a later `Window.Size` change into it, so a launcher capture (it resizes its window) runs a copy of the project with an `override.cfg` of the launcher's size (`godot/README.md`, *The launcher*).
- Godot 4.8 beta 3 on Windows gives a decorated window 4 px less client height than `Window.Size` asks (1100x680 became 1100x676, 1280x720 1280x716); a deferred second set did not help.
- Godot 4.8's `Viewport.GetMousePosition` (which the adapter polls, as Unity polls `Input.mousePosition`) reads the OS cursor, so `Input.ParseInputEvent` motion does not move it; a hover probe that must `warp_mouse` moves the owner's real cursor while it runs. Drive a capture's pointer with `--pointer-path` instead (`godot/README.md`).
- Godot 4.8's `Camera3D.ProjectRayNormal` assumes a symmetric frustum and drops `frustum_offset`, so under the classic camera's off-centre `PROJECTION_FRUSTUM` it casts the wrong ray (`projection.cpp` at c51ff06e8). Build the pick ray from the placed pose with `MinimapCameraView.RayDirection` (`WorldScene.TryPickRay`).
- Godot 4.8 sorts a perspective camera's transparents by the straight-line distance from the camera to each instance's AABB centre (unless `InstanceSetPivotData(instance, offset, false)` puts the pivot at the origin), minus `sorting_offset`, not by depth along the view (`_fill_render_list`, render_forward_clustered.cpp at c51ff06e8; checked with two quads, 3q). A world-sized custom AABB therefore keys a thing by the world's centre: give a drawable the box it really has, or centre its box where it should sort (`WorldScene.RunSlot`).
- Godot's world is the renderers' frame with z negated (`RightHandedFrame`: the map runs from z 0 to +height), so a custom AABB written in the renderers' coordinates lies mirrored off the map, and Godot culls what draws by it as soon as the far plane is short (an opening path's, 24 on Level6a). The blended run slots' world box did until the Workshop's grate and alcove drew black in Level6a's opening; build such a box through `RightHandedFrame.Point` (`WorldScene.WorldBox`).
- Godot 4.8 adds a tangent to a surface built with normals and no tangents (format bit 2), so the vertex stream's normal block is 8 bytes a vertex, not the 4 that `MeshSurfaceGetFormatNormalTangentStride` gives for the format asked for; read the built surface's `format` before writing normals in place (`WorldScene.RunSlot.Grow`). The blended runs wrote 4-byte normals until 3q, and the liquids until audit item 9, which waves them in `WorldLiquid.gdshader` with no normals uploaded.
- A Godot 4.8 surface's vertex buffer is not interleaved when it has normals: all positions first (12 bytes each), then each vertex's octahedral normal and derived tangent from byte `vertex_count * 12`; UV and CUSTOM go to the separate attribute buffer. Bytes handed to `RenderingServer.MeshAddSurface` must match `_surface_set_data` (rendering_server.cpp at c51ff06e8) to the bit, and the format must be the one Godot gives the surface, version bits included: `WorldScene.ChunkUpload.cs` reads it from a surface Godot built and checks the strides (audit item 12). Work added to `TerrainChunkScheduler`'s build task lengthens it, and a budgeted flush that finds the task unfinished uploads a frame later; Movie Maker frames are slow enough that captures stayed identical, an uncapped run is not.
- A Godot 4.8 opaque `cull_back` spatial shader that neither writes `VERTEX` nor discards is drawn in the depth prepass with Godot's default material (`uses_shared_shadow_material`, scene_shader_forward_clustered.h at c51ff06e8), and the opaque pass then tests `COMPARE_OP_EQUAL`; on this AMD Vulkan driver the two programs' depths differ for transformed models, so whole triangles show the clear colour. Keep a `VERTEX` write or a `discard` in such a shader (`World.gdshaderinc`, pinned by `EveryWorldShaderKeepsItsOwnDepthPrepass`); `skip_vertex_transform` is not needed (audit item 16).
- Under MSAA, Godot 4.8 resolves the multisampled colour into the internal texture again after the transparent pass, so a post-opaque compositor effect's write to the resolved colour is lost, and that texture is not a storage image there; draw into `GetColorLayer(0, true)` instead (`WorldOcclusionEffect`).
- `godot/perf.ps1` names reports `<label>-<level>-<size>-r<n>.json` by repeat index, so a second invocation with the same label and `-Out` overwrites the first; interleave with distinct `-Out` folders or rename between rounds.
- After verifying a manually prepared `perf<level>.dk2g`, create its adjacent `perf<level>.dk2g.ok` marker so `godot/perf.ps1` reuses the save.
- Godot 4.8 beta 3's `CanvasItemAddTriangleArray` errors (span overload) or crashes the process silently (index array longer than `count`); pass exact-size arrays and count -1 (`HudCanvas.SectorBuffers`).
- Godot 4.8's canvas fragment stage reads a rect's region as half floats (`canvas.glsl` at c51ff06e8 packs `src_rect` with `packHalf2x16`), so `CanvasItemAddTextureRectRegion`'s `clip_uv` clamp can move a region far from an atlas's origin by a texel; draw one-texel-per-pixel atlas pieces with `clip_uv` false (`HudCanvas.RecordText`).
- `ClassicOptionsPanel` hovers from `Viewport.GetMousePosition`, the OS cursor, not `--pointer-path`, so an in-game options-menu capture lights whichever entry the real cursor is over and two runs can differ; keep the real cursor off the window or compare such frames by hand.
- `godot/editor.ps1` cannot pass a bare `--` to Godot, so the user arguments after it (`--level`, `--seed`, `--query-first-creature`, `--possess-first-creature`, `--stage-battle`, `--open-options [entry]`, `--saves <dir>`, `--settings <file>`, `--open-full-map` for `InGameHud.tscn`, `--profile <file>`, `--hiscores <file>` for either scene) never arrive (PowerShell sees `--profile`-style names as an ambiguous parameter); run the pinned console executable directly (`godot/README.md`).
- Godot's PNG decoder applies a PNG's gAMA chunk (231,24,24 decoded as 209,2,2), and nearly every DK2 PNG declares one (FrontEnd.WAD 356 of 371, EngineTextures.WAD all 102) while DKII.EXE draws the stored bytes ([0x005B6030](research/functions/005B6030.md)). Decode through `PngChunks.WithoutColourSpace`, as `ClassicMinimap.DecodePalette` and `ClassicFrontEndMenuAssets.DecodePng` do.
- Native creature positions are 12.12 but steering-body positions are 16.16; boundary pose controls must shift by four when updating both.
- The Godot export builds every referenced project as `ExportRelease`, a configuration the SDK does not optimise: `DK2.Simulation` and `DK2.Presentation` shipped with `Optimize` off and `DebuggableAttribute` flags 0x0107 (DisableOptimizations), so the JIT never optimised them and a Level6a tick took 10 ms against 1.7 (`Directory.Build.props` now sets it). After changing a configuration or the export, read an exported DLL's `DebuggableAttribute`: 0x0002 is optimised.
- The release export's runtime tiering (`DK2.Godot.runtimeconfig.json`) left a 4 Hz tick's methods in tier 0 for seconds: each first-use method jitted anywhere restarts the 100 ms call-counting delay, and dynamic PGO adds an instrumented tier. `DK2.Godot.csproj` sets `TieredPGO` off and `CallCountingDelayMs` 0; benchmark ticks in a cold process (`DOTNET_TieredPGO=0 DOTNET_TC_CallCountingDelayMs=0` reproduce it), since a warmed one hides the cost.
- A tile's terrain, owner, ruin bit and Secret Door bit count their changes (`TileState.KeyWriteCount`) and the room sweep, the structure mask and the navigation check trust the count. Code that changes those without the setters (a tile array copied over another, a whole `TileState` assigned from another tile, `Unsafe`) must call `TileState.NoteBulkKeyWrite()`, or those caches answer from stale tiles (`TileKeyCacheTests`).
- `TerrainChunkScheduler` uploads chunks within a 4 ms wall-clock budget, so a faster build splits a multi-chunk rebuild across frames differently (Level7 frame 122 differed from HEAD with nothing rendered differently). Compare Movie Maker captures across builds with `FlushBudgetMilliseconds = 0` in both trees.

## Shell and files
- Two sessions share one index: `git rm` stages at once, and another session's plain `git commit` takes it (a probe commit took 1a's Unity deletions and was redone). Delete with `rm` and commit by path (`git commit -- <paths>`).
- Codex MCP calls can time out after 300 seconds despite a larger `ctx_execute` budget; its `background: true` discards stdout, so the wrapper must write its own log and exit receipt.
- Normalize hash-manifest relative keys to one path separator before updating entries; mixed slashes can retain duplicate paths and invalidate setup provenance.
- Build a hash manifest's inventory before opening its output file; otherwise a recursive scan can include the manifest's own empty-file hash.
- `git archive` can export CRLF while `git show` returns an LF blob; compare blobs with blobs and archived files with their own manifests.
- A Godot `git archive` baseline omits the ignored `addons/godot_ai` dependency; copy that existing add-on into the scratch project before import, or its autoload fails.
- Godot 4.8's headless `--export-release` of a C# project exits 0 when its .NET part fails ("no solution file was found" without `DK2.Godot.sln` beside the project) and still writes `DK2.exe` and a pack with no assemblies; check the log for `ERROR:` and the `data_*` folder, as `tools/tester-package/build.ps1` does.
- Godot's release export templates refuse a scene on the command line (exit 1, "compiled without support for path overrides"); an exported run starts a level with `-- --level <name>` on the main scene.
- An isolated native DK2 runtime needs `DK2TextureCache` as well as `Data`; omitting it crashed startup with `EngineTestLight` missing.
- During a solution test run, an app's `bin` can retain old dependency copies until that project builds; wait for its build before checking schema compatibility.
- For `Unknown journal record 0` on an old v1 journal, check `CommandCodec` history: hash 462 added the Drop heading marker without changing the envelope version.
- In DK2.Tools, qualify `System.Convert`: the sibling `DK2.Convert` namespace shadows an unqualified `Convert`.
- On Windows, pass an absolute executable path to Python `subprocess.run`; `cwd=` alone can still launch the main checkout's relative executable.

- DK2.Simulation targets netstandard2.1 without System.Text.Json; scratch steering observers use plain CSV, while JSON belongs in Tools.

- After backing up your own edited path for a fast-forward, use git restore; manually writing HEAD's blob can still make merge report local changes.

- When production adds action Poll/Advance hooks, update captured-order diagnostic wrappers too; an old copied sweep can freeze the new animation clock.

- PowerShell passes wildcard path arguments literally to rg; use a directory argument with `-g` for filename patterns.

- Replacing a frozen diagnostic source file drops its inherited bootstrap clauses; reapply them and strip-check every clause against production bytes.

- Evidence commands need explicit absent-target guards at the call site as well as exclusive creation in helpers; avoid `write_text` for new evidence or helper files.
- Wait for a counterfactual's restoration receipt before copying source overlays; its working file temporarily contains the pre-fix implementation.

- With DOTALL enabled, bound merge-marker labels with `[^\n]*`; `^>>>>>>> .*` also consumes the file tail.
- Restore Git blobs as bytes; Windows text newline translation can double CRLF already present in a blob.
- Splice diagnostic source as bytes or explicit UTF-8; mixing UTF-8 Git text with Python's Windows locale encoding can corrupt only the inserted comments.

- Capture `record.u(offset)` defaults to a dword; supply the recorded field width explicitly so padding cannot enter restore inputs.
- Seed packed action completion/count fields as bytes: a dword at creature +0x3AD also overwrites the work rate at +0x3AF.

- The `dk2live.py seed` default map filter is 40x50; Level 5 is 51x55. Read the selected capture dimensions before launching a seeded native control.

- Codex's sandbox helper can fail before process startup with `setup refresh had errors`; an approved `require_escalated` command can still run.
- Concurrent commits in one checkout share both the index and COMMIT_EDITMSG; serialize staging and commits across sessions, including while hooks run.
- Committed unmerged branches can already allocate ADR/hash numbers absent from master; inspect their version and ADR headers before allocating a new number.
- Do not rebuild a project while its tests run: Windows testhost locks copied assemblies and MSBuild exhausts its copy retries.

- KLD content offsets depend on each chunk's headerBlockSize; validate record tags and the final boundary before treating a trigger file as empty (Frosty's Lair TRIGGERS starts at60, THINGS at56).

- Replay CSV labels its clock `tick`, but replay events TSV labels it `turn`; inspect the header before sharing a parser.
- `docs/experiments/` is ignored and untracked (`ecf22a30`): an experiment write-up goes to `F:\DK2Evidence\studies\<study>\payload\` (AGENTS.md, evidence rule), and nothing under `docs/experiments/` is staged, not even with `git add -f`.

- `dotnet run -m:1 -- ...` forwards the unrecognized MSBuild switch to the app; use `dotnet build -m:1`, then run the built DLL.

- If Codex's Windows shell fails before startup with `setup refresh had errors`, an approved command outside the sandbox can still run.

- `git add` can stage deletions before rejecting an ignored path; inspect the index before retrying, since already-staged deletions no longer match a pathspec.

- PowerShell 5 with ErrorActionPreference=Stop treats native stderr as NativeCommandError; capture child stdout/stderr directly when expecting test failures.


- Inspect original BMP captures in small batches; nine 1600×1200 images exceeded code-mode's 64MiB IPC frame limit.

- Warm isolated terrain GPU probes before recording pixel baselines; the first presentation can differ from subsequent terrain flushes.
- Gameplay GPU probes that rebuild terrain require isolated Play Mode; edit-mode Object.Destroy can retain old chunks and log errors despite passing pixel/model checks.

- Unity 6000.6 returned the material override tag value `True` as `true`; use lowercase marker tokens when comparing `Material.GetTag` results.
- Sandboxed Unity batch startup can stall at `Licensing is not yet initialized`; an isolated probe may need execution outside the sandbox to use the installed license.

- A managed worker with a different Windows SID may need per-command `git -c safe.directory=<absolute repository>`; do not change the owner's global Git trust configuration.

- Independent UI Toolkit PanelSettings cannot be assigned to a UIDocument nested under another UIDocument; give isolated probe panels separate root GameObjects.

- UI Toolkit rounded overflow masks need a stencil-capable RenderTexture target; a depth-zero probe can draw the mask itself as white.

- Windows PowerShell 5 needs both System.IO.Compression and System.IO.Compression.FileSystem loaded before ZipArchiveMode resolves.
- New function records require `evidence:` before the first blank line; `kb index` rejects a split header.
- Read a function record's complete History before relying on its summary: a later correction can be missing from the body, and truncating the record can hide it.

- `Capture.frames(start, stop)` includes the stop header; use `birth + 3` for four snapshots. Its snapshots share live dictionaries: freeze record fields before advancing or previous/current both read the current frame.

- Keep capture snapshot headers separate from in-tick action stamps; subtracting one only from native rows misaligns the two packet birth anchors (research/imp-observation-clocks.md).

- A native emulated strike can change a tile before faulting in presentation; require a successful return before treating any partial writes as an oracle result.

- If CS0009 names a zero-filled generated reference DLL, rebuild with `--no-incremental` before treating downstream missing types as source defects.

- Cast raw PowerShell Get-Content results to [string] before ConvertTo-Json; otherwise extended properties can turn the value into an object.
- Unity's C# 9 compiler rejects struct `with` expressions accepted by the adapter's newer compiler; construct replacement values explicitly in Unity/probe source.

- Runtime-selected material variants can disappear only in standalone builds; see [the verified card regression](research/classic-lighting-verification.md#windowed-player-and-counter-calibration).

- Missing Unity mesh UV2/UV3 shader inputs arrived as ones on D3D12, routing static terrain into animated lighting; publish explicit zero channels instead of assuming absent streams are zero.

- Unity 6000.6 Mono's per-thread allocation meter reports 0 for a live 64KiB allocation; calibrate before claiming zero, and do not treat GC.Alloc's TimeNanoseconds recorder as bytes.

- Reset mesh UV channels with explicit zero arrays after GPU fixtures; clearing them to empty can retain stale stream values after another UV channel changes.

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
- Windows PowerShell redirects native stdout as UTF-16; detect the BOM when aggregating logs in Python, or regex matches can silently return zero; write JSON artifacts as UTF-8 in the producer.
- Windows PowerShell Get-Content defaults to ANSI; pass -Encoding UTF8 when deriving patch text from repository files.
- Windows PowerShell `Set-Content -Encoding UTF8` adds a BOM even to commit-message files; use `UTF8Encoding(false)` when writing them.
- Run `dk2tools status` with the same `DK2_DIR` as the verified suite; data-dependent discovery otherwise silently reports fewer cases.

- A linked worktree may lack ignored replay drivers and captures; Claude scratch scripts can be recovered from its project JSONL Write calls, while captures remain under the main clone's reverse-engineering/dk2runs.

- Quarantined research .txt files may contain decompiler output; never preview their prefixes. Extract only facts or explicitly identified assembly sections.

- BitReaders.py takes an offset without `0x` (for example `3ea`); a prefixed offset silently searches for `+0x0x3ea` and returns no matches.

- Path.GetFullPath is lexical and does not detect junction or symlink aliases; output guards must inspect filesystem links.
- Copy `reverse-engineering/` evidence off the clone before any worktree cleanup: the gitignored native captures in `dk2runs/` were emptied during the 2026-10-18 worktree removal (cause unknown) and had no backup; AGENTS.md now keeps evidence in F:\DK2Evidence.
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
- Windows Python defaults to a legacy code page here; read Markdown with `encoding='utf-8-sig'` and write with `encoding='utf-8'` (or set `PYTHONUTF8=1`), otherwise existing Unicode can be corrupted.
- PowerShell here-strings piped into Python can replace non-ASCII source literals with `?`; use ASCII script literals or `apply_patch` for Unicode edits.
- `WadArchive.TryGetEntry` returns the later of two same-named entries; FrontEnd.WAD has 31 duplicate names (loading screens, `2dLevelS*`, Cancel/Confirm), so look those up by entry index ([front-end-assets.md](research/front-end-assets.md) §1.3).

- Decode Ghidra read_bytes hex as ASCII before naming a source-file draw site; reading near-spellings by eye can mislabel the verifier.

## Builds, tests and dk2tools
- Function records on disk need one `evidence:` line per path; comma-separated evidence is `kb_upsert` input syntax and becomes an invalid anchor in a record file.

- `SaveLoadingBoundsTests.DuplicateSecretDoorKeysAreWrappedWithPosition` uses the remake snapshot tile stride; update it when tile fields change, or its corruption lands in another section.

- Record HEAD with the build command; a later provenance snapshot can name a newer commit while the measured binaries remain unchanged.

- `setup-dotnet` installs the exact SDK from `global.json`, ignoring its roll-forward policy; use a published SDK version, not the runtime version `10.0.0`.

- Unicorn can retain translated code across oracle requests: use a fixed stub reading variable data, or a fresh process, when varying controlled returns; rewriting an immediate can silently reuse the first value.

- Snapshot mutable diagnostic objects when recording each row; serializing retained references later repeats their final state across the trace.

- Scratch SDK projects include nested `.cs` snapshots by default; keep copied Unity probe sources outside their compile glob.

- An authored camera update can overwrite a batch probe's Heart focus; reapply the focus after `DungeonRunner.Update` before judging Heart clearance from captures.

- A sandbox restore can fail NU1900 when NuGet audit is unreachable; use cached packages with a per-command `-p:NuGetAudit=false`, without changing repository audit settings.

- LIKELY and GUESS are retired (ADR-0642): `kb check` rejects them. A branch cut before 2026-11-03 runs `python tools/kb/rescale_confidence.py` after merging master.

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
- Linked worktrees share Git configuration. If `core.hooksPath` points into another worktree, invoke `git -c core.hooksPath=.githooks commit` to run this worktree's hooks.
- Renaming a research-doc heading breaks every function record whose `evidence:` anchors to it;
  `kb check` fails the commit. Repoint the anchors and give each record a dated History line.
- A dk2tools command can behave differently under `dotnet run` and under the hook's
  `dotnet <dll>`: the console code page differs. Decode a child process's output explicitly
  (git writes UTF-8; `Repo.RunGit` does since 2026-10-03).
- Replay rigs and the scenario harness run the built binaries for minutes: do not build the same
  worktree while they run.
- A hung dk2tools with no CPU was a full stderr pipe; drain stderr concurrently.
- A 57600-tick player run's `events.log` is about 80 MB and exceeds the 64 MiB output budget, so the run ends as incomplete evidence even when won; ladder runs need `--player-output-mib` (512 was enough for Level 4; use 1024 for Level 5).
- Unicorn's `uc_emu_start` kills a .NET process with 0xC0000409 (no dump, no event) while the
  same DLL works from CPython; the oracle runs it in `tools/oracle/dk2oracle.py` for that reason.
- A nonzero `uc_emu_start` timeout costs ~15 ms a call (a timer thread each time): 95k calls took
  23 minutes. Pass 0 and bound the run by instruction count.

## Worktrees, numbers and merges

- The dk2kb MCP (`kb_get`, `kb_search`) reads the main checkout. In a worktree, an empty `kb_get` does not mean the record is missing: check `docs/research/functions/<ADDRESS>.md` on disk and merge into it, keeping its History. `kb_upsert` also writes the main checkout, not the worktree: edit the record file in the worktree instead, or copy the result across and restore the main file.

- ADR, question and `HashVersion` numbers are read from committed files in every worktree, and
  master moves under a long task: re-check all of them immediately before each commit, and expect
  to renumber after merging master.
- After merging master the rig baseline moves: measure a detached worktree at the new master and
  the branch side by side before writing numbers into an ADR.
- Never `git add -A`: another session may be editing; stage named paths.
- `DungeonKeeperII.ttf` is gitignored, so a fresh worktree's Unity project lacks it and all its text falls back to DK2's bitmap font or Unity's default: copy `Assets/Resources/Fonts/DungeonKeeperII.ttf` from the main checkout (`unity/README.md`).
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

- Decode imported Python source with `utf-8-sig` before `compile`; Windows default `Path.read_text` can corrupt UTF-8 BOM source and evidence-scorer text.

## Captures
- State-copy comparisons can pass when a missing field stays at its default; regenerate `StateCopy.g.cs` and exercise non-default values for every added field.
- When carrying delta CSV rows forward each tick, retain persistent death/ghost exclusions; copying the row map alone reintroduces excluded actors.

- Windows text writes can translate LF to CRLF; hash the written test file for raw proof identity, rather than the pre-write string.

- A full-state capture (`*.dk2cap`, `reverse-engineering/dk2capture.py`) holds every room record
  as well as creatures and tiles; decode a field with `Record.u(offset)`. `things("room")` also
  matches stale slots, so filter by the room slot range (1100 up in the Level 1 and 2 runs).
- Before blaming a mechanism for a rig divergence, check whether DK2's own capture of the same
  packet ever reaches that situation (`dk2runs/level2-live2/seed01` is the `l2live2` packet).
- `dk2capture.py` records objects from slots 1150-1719 only; an object made after the window
  fills never appears in `object-map.csv`. "DK2 never made X" needs another witness.
- A recording that ends `"game stopped answering"` early is usually DK2 crashing, not the probe: the Windows Application log has 14 access violations at `DKII-DX.exe+0x1FCB80` (`0x005FCB80`, in the renderer range) since 2026-09-05, one of them the Level 4 dump run at tick 1176 (2026-10-15), well after its tick-500 dump. Check the log before blaming the recorder or a dump.
- Campaign levels unlock through `Player Level Status` under `HKCU\Software\Bullfrog Productions
  Ltd\...`, 160 bytes indexed by map, not level (`campaign-progress-registry.md`). Close DK2 first;
  it rewrites the key on exit.
- DK2 runs elevated: start the probe from an elevated shell before the game, and the owner clicks
  two UAC prompts. A packet recording needs `-LEVEL` on both `-PSAVE` and `-PLOAD`, or the front
  end eats the seed and the replay desyncs on turn 1.
- Launch DKII-DX.exe, not DKII.EXE: `DKII.EXE -LEVEL` exited before its level loaded (2026-10-28).
  The two share every code byte (1,126 differing bytes: 3 in the header, 5 in .rdata, the rest in
  data appended after the last section; .data identical), so DKII.EXE's addresses hold in DKII-DX.
- Idle `-LEVEL` runs of Levels 3 and 12 logged no raid or fight in about 4,700 ticks each; replay a
  recording (`-PLOAD <name> 0`) to exercise them (music.md §9, *Live capture*).
- A doc's "all N records" counts one record type: "every hero has `DestroysRooms`" was true of
  trigger-created creatures and false of party members (825 of 4,545).
- A clean unstaged diff checks no committed changes; run `git diff --check <base> HEAD` and `--cached` before committing new files.
- Snapshot tests need `Xoshiro256StarStar` or `Dk2Lcg`; scripted `IRandomAlgorithm` test doubles intentionally have no save format (ADR-0275, ADR-0564).
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
- Matched-state probes must restore constructor-owned fields before binding RNG; otherwise fixture setup can consume draws before the decision (research/decision-parity-audit.md).
- A hidden standalone Unity window can skip all screen rendering despite active CPU loops; require camera render callbacks and a nonblank PNG before treating its timings as rendered performance. `game-unity-probe -Windowed` ran hidden until 2026-11-14 (0 GPU frames, black screenshot).
- Unity can redirect `%LOCALAPPDATA%/Unity/Editor/Editor.log` to `<project>/Logs/Editor.log`; follow its redirect before diagnosing startup or compiler errors.
- The URP renderer asset has `postProcessData` unset, so URP runs no post-processing and a Volume (`ColorAdjustments.postExposure`) does nothing in a player; a screenshot at Gamma 20/30/45 showed the same mean brightness. Draw a pass from `endCameraRendering` instead (`ClassicSettingsRendering`).
- `game-unity-probe` runs before 2026-11-14 built a default scene (stock camera, shadow-casting light, fog of war off, wrong ambient and camera preset), so their lighting, GPU and frame numbers are not comparable with later runs, which build `Dungeon.unity`.
- PowerShell variable names are case-insensitive: a `$r` loop variable overwrote an earlier `$R`. Use distinct names.
- Mono Stopwatch and Unity frame timestamps can have different epochs despite equal frequencies; align Windows captures with QueryPerformanceCounter.
- ShaderUtil reports an uninitialized SRP compatibility result before a shader has actually drawn; check after a GPU render.
- Start PresentMon before creating the graphics context, retain its process Handle for ExitCode, reject lost ETW events, and stop only the probe's own named session on failure.
- A background (concurrent) GC charges a running thread's unused allocation-context tail to `GC.GetAllocatedBytesForCurrentThread` (1-8 KB it never allocated), so under CPU load a random zero-allocation test failed; `tests/Directory.Build.props` turns concurrent GC off for test hosts (blocking GCs leave the counter exact). Keep it, and check the test host still runs `GCLatencyMode.Batch` before blaming product code or loosening an assertion.
- Lightning zero-allocation tests reported 624 B in the worktree and 216 B on a clean HEAD archive. The full suite passed with `DOTNET_TC_QuickJitForLoops=0` set only for the test process. Use that control before attributing a failure to the change.
- `ManualResetEventSlim.Wait` allocates its lock object the first time it has to block (24 bytes, only when the other side is late), so a zero-allocation test around a worker hand-off failed about 1 run in 15 under CPU load; `MoviePlayback` waits on a `ManualResetEvent` for that reason.
- Runtime level switching must release the old scene before activating its replacement: deferred OnDestroy can clear or dispose shader globals already published by the new scene.
- Legacy Unity Font atlases can have black RGB with valid glyph alpha; UI Toolkit textured meshes need an in-memory white-RGB copy of that coverage.
- Restoring source with `Copy-Item` can preserve a timestamp older than the compiled DLL; touch the restored file or rebuild before trusting an incremental regression retest.
- Windows PowerShell Set-Content defaults to a legacy code page; pass -Encoding UTF8 for Python scripts containing non-ASCII prose.
- An effect's GenerateIds are effect ids, not element ids, when the effect has flag `0x40` (types 4 and 18); a dump that resolved them as elements made the Inferno look like one spark (shot-effects.md §5).
- Codex local exec can fail before process creation with "setup refresh had errors"; sandbox escalation worked here.
- Windows PowerShell piping non-ASCII script text to python - can turn symbols into question marks; keep pipe input ASCII and use Unicode escapes.
- Windows PowerShell can strip nested quotes from Python `-c` arguments; use a script file for replacements containing quoted prose.
- Unity `Transform.InverseTransformPoint` can differ in low bits from `worldToLocalMatrix.MultiplyPoint3x4`; preserve native point conversions when extracting bit-exact camera-space geometry.
- Disk-full compilation can leave fresh-timestamp, truncated `DK2.Simulation.dll` files in Unity verification `obj` and `bin`; incremental sync can skip them and report CS0009 ("Image is too small"), so verify the generated DLLs against a successful build before diagnosing source errors.
- Keep isolated Unity project paths short: PackageCache importer paths can hit MAX_PATH (260 chars) and fail even when the project copy succeeded.
- URP `Volume.sharedProfile` is a public field, so reflection must use `GetField`; `GetProperty` compiled but failed during the Options runtime probe.

- UI Toolkit pointer event positions and worldBound are panel units; multiplying them by physical screen-height scaling truncates scrollbar travel above the reference resolution.
- EditorApplication.update callbacks can outpace rendered play frames; UI capture probes must wait on Time.frameCount before checking page images.
- Procedural class-grid UVs and Graphics.Blit image UVs can have opposite Y origins; check logical tile colours against UI overlay positions on the active graphics backend.
- `WatchdogTests.WithTwoJobsTwoRunsOverlap` is wall-clock: rerun it alone after a failure in the full solution run or beside Unity imports; helper-process startup contention can make parallel wall time exceed the serial comparison.
- Startup probes inherit the scene's Front End Return Page; reset it to None before expecting title/intros instead of diagnosing a missing repaint from a skipped stage.
- UI Toolkit can log generateVisualContent exceptions while model updates continue; runtime probes must reject product log exceptions as well as failed assertions.
- Codex CLI 0.164 rejects an env-only project MCP entry as "invalid transport" before merging user config; repeat that server's command and args in the project entry.
- PowerShell does not expand wildcard path arguments for rg (`src/Foo*.cs`); search the directory with `-g 'Foo*.cs'` instead.
- StoreAt.py misses writes through computed bases; follow LEA-based field addresses (Camera_Reset's full-viewport slots) before declaring a writer census complete.
- A `replay --resync` with an `--entity-map` from another whole-game run names creatures born after the resync by that run's ids; pickups lift the wrong creature or none. Pass `--births <native creature-map.csv>` ([level4-outcomes.md](research/level4-outcomes.md#hand-drops-after-the-resync)).
- Windows PowerShell `New-Item` has no `-LiteralPath`; use a controlled `-Path` or `Directory.CreateDirectory`.
- Assign `ConvertFrom-Json` array results directly; wrapping the pipeline in `@()` can retain a nested array and yield `Object[]` member values.

- A scratch master Behavior test baseline needs committed scenarios, tools and Unity input source roots as well as src/tests; missing-root failures are harness setup failures.
- Use explicit UTF-8 for Markdown reads and writes on Windows; default cp1252 reads cause mojibake, and writes can truncate a file before encoding fails.
- Direct rule fixtures must refresh WorldSteering before querying NavByte; an uninitialized navigation cache reports solid tiles.
- Native thing-table player records contain only a 0x40-byte head; read full fields through Snapshot.player(PlayerId - 1) in REGION_PLAYERS, not record(tag).
- A destination-only replay probe must reuse simulation.Navigation; constructing WorldSteering rebuilds the persistent mesh and also changes the route inputs.

- Build evidence manifests before opening their output file; otherwise recursive enumeration can record the manifest itself with an empty-file digest.

- dk2oracle.py prints EAX and EDX before requested memory words; validate route endpoints before treating its output as a corridor.
- On Windows, resolve a replay executable to an absolute path before Python subprocess launch; a relative forward-slash .exe path can fail despite the file existing.
- Legacy native creature-map.csv is a changes-only log with integer tile coordinates; use raw .dk2cap frames for per-turn fixed-point geometry, and seed type identity from earlier events.
- Binary-backed helper tests do not establish caller or update-phase parity; add runtime entry/exit/order controls before marking the complete path built.
- A levelgen test level starts the keeper on its base level's own gold (Small: 3000). `SetStartingGold` above the Heart's 16000 needs Treasury tiles painted in the map (3000 each), or DK2 drops the rest (player-object.md).
- The dk2kb MCP server (`kb_upsert`) writes into the main checkout, not into the worktree you are working in, and validates against the main checkout's docs. In a worktree, write function records as files and run `kb check` there.
- A test that steps the room-construction effect engine (`RoomConstructionParticles`) must call `AdvanceTo` one 50 ms update at a time: over many quanta at once the particles move only once, so a rise or an offset measured after one big step is wrong.
- A level installed into the DK2 install's `Data/editor/maps` breaks the shipped-level counting tests in `DK2.Data.Tests` and `DK2.Behavior.Tests`, which expect 66 levels. Levelgen builds into a stage directory: remove an installed test level (an `MPT21Dungeon` once) before running the suite.
- `IClassicSoundAssets.Clip` decodes an uncached clip synchronously (190-240 ms for a music segment). Frame-thread code asks `SoundClipDecoder.TryGet` instead, which decodes on a worker and answers false until it is ready (ADR-0686).

- Replay probes using `CapturedCreatureOrder` bypass `CreatureUpdateSystem.Sweep`; mirror new lifecycle hooks before comparing their clocks.
- Serialize `GameTick.Value` in scratch JSON telemetry; serializing the struct follows its `Next` property recursively and fails at the depth limit.
- Validate diagnostic site labels against binary source-line constants: RoomWalkRules.ArrivalTile is Room.cpp:1509 (0x5E5), not the different picker at1563.

- If Docker tools are absent from chat, the local gateway still works via `docker mcp tools --gateway-arg=--profile=local call search_nodes query=topic`; CLI scalar tool arguments use key=value, not a raw JSON argument; nested objects require an MCP stdio JSON call.

- Python on this Windows host may default to cp1252; pass encoding="utf-8-sig" when reading BOM Markdown before rewriting, or its header becomes mojibake.
- The dk2kb MCP server (`kb_upsert`) writes records into the main clone, not the linked worktree you are in: edit the record file in the worktree by hand there, and restore anything it wrote to the main clone.
- The first `GameSimulation.Tick` executes tick 0; raw placed-state age fixtures need an entry stamp of -1, not 0.
- A tool attached to DK2 can change a replay: a Level 6a `record_native.py` replay went out of sync at GT 76 while a plain `-PLOAD` did not (2026-11-16; the cause is a hypothesis). The `-Replay` of the same recording (nothing stopping the game) logged `Gone Out Of Sync (Checksum) GT 631` and an out-of-sync line every turn after; whether the game diverged is not known. Check `MESSAGE.LOG` for `Gone Out Of Sync` after every replay capture, and do not touch the game during a replay (it takes live input; a focus change or a right click is the suspected cause, a hypothesis); `play-record.ps1 -Verify` is the control with nothing attached. See `tools/oracle/play-record.md`.

- Redirecting the Ghidra runner log/CACHE does not redirect InsGrep's insdb cache; instruction scans still write under the checkout's reverse-engineering/.cache/.

- Pinned Godot Movie Maker can log Vulkan surface-capability error -13 on every frame; `--rendering-driver d3d12` produced clean captures here without changing the project renderer.
- Painted capture fixtures must refresh navigation before saving: stale solid NAV can permanently reject first-frame gated effects despite visible non-solid terrain.
- Godot Movie Maker can leave empty or truncated PNGs when the output disk fills without failing the process; reserve output space and validate the PNGs before comparing captures.
- Detached Windows `Start-Process` children can survive a `ctx_execute` background-stop notice; inspect the task PIDs before relaunching to avoid duplicate runners.
- A Godot preview sharing `World3D` does not inherit another camera's `Environment` override; assign the main gamma/brightness environment to the preview camera too.
- Godot's `--profile`, `--settings`, `--saves` and `--hiscores` isolate game data, but not the engine log; captures must also pass `--log-file <temp>/godot.log` before `--` to avoid the real user log folder.
- Godot 4.8 reports an `AudioStreamGenerator` (or WAV) stream and its playback as leaked ObjectDB instances when the process quits while, or the same frame after, it plays; the audio server lets go only on a later mix. `Main.tscn --play-movie` quits ten frames after the movie stops; an `InGameHud.tscn` run quit by `--quit-after` still prints the warning.
- Context-mode runs as the owner while sandboxed `exec_command` uses a separate account here; verify the child token before native launches, because an isolated runtime copy does not isolate the owner's HKCU game profile.
- A native DirectDraw fixture may have a larger auxiliary render window than its input window; preserve its owned foreground window, request only the COM input HWND, and use the verified matching client DC if PrintWindow returns black.
- Godot's default `physics_jitter_fix=0.5` bends `Process(delta)` toward the 60 Hz physics clock (`main_timer_sync.cpp`): in real time uneven frames get near-even deltas (a draw clock stepped 4.75 ms over 3.3/6.5 ms frames), and fixed-FPS Movie Maker steps differ in the last bit; `InGameHud` sets it to 0 except under Movie Maker, whose captures were made with it.
- Godot's headless .NET export leaves MSBuild nodes and the compiler server holding the console wrapper's output handles, so `Godot_*_console.exe` does not exit until they do; `tools/tester-package/build.ps1` sets `MSBUILDDISABLENODEREUSE=1` and `UseSharedCompilation=false` (`dotnet build-server shutdown` frees a stuck one).
- A windowed Godot run on a display that is off or asleep (the monitor's power timeout, a screensaver) logs `VkResult error -13` every frame, draws nothing and still records frame times; check a capture's log first (`summarise_capture.py` now rates it invalid).
- Before simplifying minimap shader floating-point math, read `godot/DK2.Godot/Shaders/MinimapMath.gdshaderinc`; its operation barriers and compensated arithmetic preserve CPU-oracle bytes across Vulkan and D3D12.
- Godot SubViewport targets have a 2x2 minimum; a one-pixel minimap fixture replicates that pixel in the 2x2 target (see MinimapParity.cs).
- Pinned Godot's delayed viewport timestamps lose sparse minimap samples when draw_viewports clears timestamp_vp_map; harness render CPU/GPU timings cover main/preview only, not minimap or torch-shadow viewports.
- Godot C# `CompositorEffect.GetRenderSceneData` allocates 400 B inside the callback (`GetRenderSceneBuffers`: 0 B); the owner retains C#. Whole-frame cost is higher ([measurement](research/godot-torch-light-artifacts.md#scene-acceptance-and-performance)); the outside-body allocation is not independently attributed.
- `powershell -File script.ps1 -Args '--headless','--import'` hands a `[string[]]` parameter the one string `--headless,--import`, which Godot ignores, so a scripted "import" started the game windowed; call such a script with `&` from PowerShell, and wrap `@(...) + $extra` in parentheses when it is a command argument.
- A Godot `--headless --fixed-fps` run advances its clock 1/30 s a frame as fast as frames run, so a worker decode (`SoundClipDecoder`) that takes 20 ms of real time can show as seconds of game clock, or never land; time a sound check in real time (`--max-fps 60`) or decode before the first frame.
