# Remote Qwen delegation

User-authorized implementation plan; execute inline with isolated worker tasks.

Goal: Codex and Claude invoke the same supervised remote OpenCode worker.
Use Python standard-library tooling, matching existing tools, and a pinned
OpenCode CLI. No new MCP service or long-running orchestrator is needed.

- Inspect existing instructions, hooks, skills, CI and installed tools.
- Verify official CLI/provider/config interfaces and pin the supported release.
- Implement structured task/result files, isolated worktrees, scope auditing,
  bounded execution, API diagnostics and explicit supervisor decisions.
- Add a canonical Codex skill, Claude entry point, local installation and
  VS Code tasks. Keep credentials in environment variables.
- Test failure cases with mocks and temporary repositories. Attempt live
  inference only if endpoint credentials exist; report missing configuration.
- Run independent reviewer, address findings, then documenter and docs gates.

Safety: worker shell and recursive delegation are denied by default. Git
worktrees isolate edits but are not an OS sandbox. Do not automatically merge
or delete worktrees; project cleanup requires an inventory and owner approval.
Retain artifacts for review, including failures. Endpoint capabilities and
context/output limits must be measured or supplied, never inferred from a name.
