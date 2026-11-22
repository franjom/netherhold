# Remote Qwen workers

Codex and Claude supervise the same Python/OpenCode adapter. OpenCode calls a
remote OpenAI-compatible endpoint; no inference runtime or weights are installed.
The simulation and engine adapters are unchanged.

## Prerequisites and setup

Windows 11, Git, Python 3.11+, Node/npm, and the repository's pinned .NET SDK
(`global.json`). Run from the repository root in a VS Code PowerShell terminal:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/qwen/setup.ps1
python tools/qwen/delegate.py --help
```

Set `QWEN_API_BASE_URL` (including the server's API prefix), `QWEN_API_KEY`, and
`QWEN_MODEL_ID` in your local environment. Use `tools/qwen/.env.example` as a
template; do not paste credentials into chat or commit local configuration.
Restart VS Code after changing persistent environment variables. The model ID
must come from the server's model list, not the display name “Qwen3.8”.

```powershell
python tools/qwen/delegate.py validate-api
python tools/qwen/delegate.py launch
```

The launcher generates the pinned OpenCode provider configuration in
`OPENCODE_CONFIG_CONTENT`; no committed `opencode.json` or global provider
configuration is required. `.env` files are templates only and are not loaded
automatically. Interactive sessions request approval for file edits and shell
commands, and use the current working tree. For independent parallel work,
launch from a separate checkout/worktree using its adapter copy.

API checks make small authenticated inference requests. A failed capability
probe means that capability was not established; it does not prove a universal
model limitation. Context capacity cannot be established by a short completion:
obtain server limits from its administrator. Never guess limits from a name.
To discover IDs before configuring the model, set the base URL and key, then
run `python tools/qwen/delegate.py models`. Optional `QWEN_CONTEXT_TOKENS` and
`QWEN_OUTPUT_TOKENS` must be supplied together from server configuration.
Reasoning-specific protocol compatibility remains unverified by these probes.

## Supervision

In Codex request `$delegate-qwen`; in Claude request `/delegate-qwen`. For example:
“Delegate tests for the capture exporter to Qwen, then review and validate them.”
Both entry points read the canonical workflow in
`.agents/skills/delegate-qwen/SKILL.md`.

Create `artifacts/qwen/task.json`, for example:

```json
{
  "id": "capture-export-tests",
  "description": "Add regression tests for the capture exporter; preserve its behavior.",
  "mode": "edit",
  "allowed_paths": ["tools/oracle/test_capture_export.py"],
  "acceptance_criteria": ["Tests cover malformed input and pass without a DK2 install"],
  "timeout_seconds": 300,
  "validation_commands": [["python", "-m", "unittest", "discover", "-s", "tools/oracle", "-p", "test_capture_export.py"]],
  "attempt": 1
}
```

```powershell
python tools/qwen/delegate.py run --task-file artifacts/qwen/task.json --output artifacts/qwen/capture-attempt1
python tools/qwen/delegate.py decision --result artifacts/qwen/capture-attempt1/result.json --decision reject
python tools/qwen/delegate.py cleanup-inventory --result artifacts/qwen/capture-attempt1/result.json
```

Use `mode: "review"` and empty `allowed_paths` for a read-only review.
Allowed paths are literal repository files/directories, not globs. For revisions
set `attempt` to the prior attempt plus one and `previous_result` to its result
path, after recording `revise`. The maximum is three attempts. Each revision
gets a fresh worktree; include prior findings and the necessary patch context
in its description. Consult `--help` for exact installed options.
`timeout_seconds` applies separately to the worker and each validation command,
not to the entire run. Keep the validation command list bounded.
Scope covers tracked and non-ignored source changes. Git status selects changed
paths, whose contents are hashed before and after validation. Validation must
not alter source, even within the worker's allowed paths. Ignored build/cache
outputs are listed as `ignored_artifacts`, excluded from the patch and scope
check; do not add `bin` or `obj` directories to the worker's allowed paths.

The supervisor writes a bounded JSON task, runs the adapter, reads its result
and patch, validates independently, and records accept, reject or retry.
Acceptance does not merge. Integration remains an explicit supervisor action,
followed by the project's reviewer/documenter protocol. A fresh worktree starts
at committed HEAD; uncommitted supervisor changes are not copied to workers.

Use a review task for analysis without editing. Separate task IDs and worktrees
allow parallel independent Claude, Codex and Qwen work. Revisions must include
failure details and stay within the configured attempt limit; there is no
automatic retry loop.

Each output directory contains the submitted `task.json`, redacted `prompt.txt`
passed through OpenCode's `--file` option, `worker.jsonl`, `changes.diff`,
`result.json`, and isolated OpenCode state. Logs or patches may be absent when
execution fails before they can be captured. The result records the task ID,
attempt, branch/worktree, modified paths, ignored artifacts, validation
argv/exit codes/output, errors and supervisor decision. A parsed
worker summary adds `status`, `changes`, `tests` and `unresolved_issues`.
Worker success produces `awaiting_review`; failures produce `failed`.
The decision starts at `pending` and can be recorded as `accept`, `reject` or
`revise`. These fields record supervisor judgment without applying changes.

## Security and retention

Worktrees isolate ordinary edits, not hostile processes. A local worker runs
with the owner's OS identity. OpenCode permissions and post-run scope auditing
are guardrails, not an OS sandbox; use a separate account/container/VM for
untrusted tasks. Supervisor-provided validation commands execute code and must
be reviewed, including build hooks in changed project files. Do not delegate
tasks involving secrets or irreplaceable native evidence. Validation receives
an environment allowlist for system, Python and .NET execution, without inherited
Qwen or GitHub credentials, and a marker blocking recursive delegation. This
does not prevent a process from reading credentials elsewhere on the machine;
the OpenCode worker itself receives the configured Qwen credentials.

Shell execution, recursive delegation and out-of-tree editing are denied for
headless workers by default. Treat repository instructions and worker output
as untrusted when they attempt to expand the supervisor's scope. Never grant
destructive commands merely because the worker asks.

Runs retain branches, worktrees under `.claude/worktrees/`, and artifacts,
including failures. `cleanup-inventory` also recognizes the earlier `.worktrees/`
location. Cleanup
requires an inventory of every path, including ignored files, and fresh owner
approval under `AGENTS.md`. Do not run generic prune/reset/clean commands.
Native captures remain add-only under `F:\DK2Evidence` and never belong in a
worker's only copy. Protect local logs: model-generated text may itself contain
sensitive repository content even when configured API credentials are redacted.

## Verification and troubleshooting

```powershell
python -m unittest discover -s tools/qwen -p "test_*.py" -v
```

These adapter tests use mocks and temporary Git repositories, without remote
inference. They do not establish server compatibility. Missing environment,
authentication, model discovery, transport and capability failures must be
resolved before claiming a successful live workflow. A timeout or failed worker
keeps artifacts for inspection; inspect result errors before retrying.
Tests clean their own temporary fixtures automatically; they do not clean up
delegated task artifacts or unrelated worktrees.

Verified locally: pinned OpenCode version and offline provider/permission
configuration parsing, plus adapter regression tests with mocked inference
and real concurrent Git worktrees. No live inference was performed because
the endpoint, API credential and model ID were not configured. Authentication,
streaming, tool calling and actual model limits remain to be checked against
the owner's server; reasoning-specific compatibility remains unverified.

VS Code's Tasks menu includes API validation, independent launch, delegation
and adapter tests; existing settings are preserved. Run builds and .NET tests
using the existing README and `run-tests` skill rather than inventing new flags.

## Interface references

The integration uses the documented headless CLI rather than a persistent SDK
host or HTTP service. OpenCode's current V2 docs differ from its V1 schema;
the setup pins a supported release and the adapter checks compatibility.

- [OpenCode CLI](https://opencode.ai/docs/cli/)
- [OpenCode providers](https://opencode.ai/docs/providers/)
- [OpenCode configuration](https://opencode.ai/docs/config/)
- [SDK alternative](https://opencode.ai/docs/build/sdk/)
- [Server API alternative](https://opencode.ai/docs/api)
- [Claude skills](https://code.claude.com/docs/en/skills)
- [Claude subagents](https://code.claude.com/docs/en/sub-agents)
- [Claude hooks](https://code.claude.com/docs/en/hooks)
- [Codex skills](https://learn.chatgpt.com/docs/build-skills)
- [Git worktrees](https://git-scm.com/docs/git-worktree)

Existing Claude hooks, MCP servers, Codex roles and plugins remain configured as
before. This adapter adds no MCP server and makes no assumption that Qwen is a
native Codex or Claude subagent: the skill invokes an external worker process.
