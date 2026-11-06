# Shared .NET skills

Codex reads the canonical skills in this directory. Claude's entry points in
`.claude/skills/` direct it here; supporting paths resolve from the canonical
skill directory, never from the Claude entry point.

## Source and updates

Selected from [dotnet/skills](https://github.com/dotnet/skills) at
`26a06585630870595c4fba50e313621989f58fc7` (reviewed 2026-11-06).
`dotnet-skills.lock.json` records the source paths and upstream file hashes.
The upstream MIT notice is in `dotnet-skills.LICENSE`.
Each imported `SKILL.md` adds a link to this project guidance after its front
matter. The benchmarking authoring reference also fixes one relative link;
the lock records that replacement. Other upstream content retains its bytes.
Review updates against this pinned revision, retain complete reference folders,
and refresh both the lock and Claude metadata when a skill changes.

## Selected skills

| Skill | Project use |
|---|---|
| csharp-refactoring | Preserve behavior across binding-aware structural edits. |
| dotnet-breaking-changes | Check assembly consumers, conditional code and Unity compatibility. |
| msbuild | Diagnose imports, incremental generation and build performance. |
| dotnet-pinvoke | Check native signatures, layout, ownership and callbacks. |
| vectorization | Verify measured SIMD changes against scalar behavior. |
| analyzing-dotnet-performance | Inspect hot paths for avoidable allocations and API costs. |
| microbenchmarking | Design and run focused BenchmarkDotNet comparisons. |
| run-tests | Use the repository's runner and the smallest relevant selection. |
| platform-detection | Resolve test framework, runner and imported settings. |
| filter-syntax | Reference helper for runner-specific test filters. |
| test-value-evidence | Check that regression tests detect the relevant defect. |

The general C# expert is primarily a marketplace router, so it is omitted.
Web, database, MAUI, WinForms, AI integration, publishing, templates and framework
migrations are outside current project needs. Standalone MSBuild specialists
overlap the selected `msbuild` skill's bundled references. Test-writing and
suite-audit collections overlap existing workflows; the focused execution and
regression-evidence skills above supply the useful additions.

## Project guidance takes precedence

Read `AGENTS.md` and this section before applying an imported skill. These are
engineering references, never evidence of original DK2 behavior. Use the
project's binary/data/experiment source order and confidence scale for mechanics.

- Preserve integer arithmetic, overflow, truncation, RNG draw order and state
  layout. A faster framework API or SIMD reduction must preserve that contract.
- The SDK pin is in `global.json`; Unity-bound assemblies use `netstandard2.1`
  through `src/Shared/UnityBound.props`. Check the actual target and Unity runtime
  before using modern APIs, intrinsics, source generators or `LibraryImport`.
  CoreCLR benchmark results do not establish Unity performance.
- Native interop requires the actual ABI. Do not apply blanket type-table advice:
  `LibraryImport` is not universally required for NativeAOT; `DllImport` support
  depends on the signature/marshalling. Win32 handles have API-specific ownership;
  an `HWND` is not automatically an owning `SafeHandle`. Older targets may need
  explicit 32/64-bit declarations instead of `CLong`.
- Check library and tool APIs through Context7 as `AGENTS.md` requires. Use the
  existing Roslyn LSP, including its workspace warm-up, for binding references.
  No additional LSP, plugin, MCP server or agent role is installed by these skills.
- Scope performance findings to measured hot paths. Counts and pattern matches
  are candidates, not measured speedups or permission for codebase-wide edits.
  Preserve dependency pins and reuse existing benchmark projects and packages.
- Follow the repository's test commands and install-dependent skip policy.
  A zero exit code without executed tests is not verification. Do not migrate
  runners, add wrapper frameworks, or upgrade packages as a side effect.
- Upstream routing and stop phrases classify work; they do not cancel authorized
  feature or bug-fix work. Continue under the applicable project workflow when
  a requested operation is outside a skill's scope. Check actual shipped consumers
  before adding compatibility shims to internal project code.
- Upstream cleanup recipes do not authorize removal here. Follow `AGENTS.md`'s
  path inventory and approval rule, including for temporary copies and outputs.
  Keep irreplaceable native evidence at `F:\DK2Evidence` under its add-only rule.
  Preserve other sessions' files and use the project's reviewer/documenter order.
