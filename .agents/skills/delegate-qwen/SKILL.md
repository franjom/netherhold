---
name: delegate-qwen
description: Delegate a bounded coding or review task to remote Qwen through OpenCode, inspect its isolated diff, and independently validate before integrating.
---

Use the shared adapter documented in `tools/qwen/README.md`. Read it before
preparing a task; its task example defines the actual supported fields.

1. Read `AGENTS.md` and relevant instructions. Choose a bounded task with
   explicit acceptance criteria and literal allowed paths. Exclude architecture
   changes and unresearched gameplay behavior. Review mode must allow no edits.
2. Verify configuration with `python tools/qwen/delegate.py validate-api`.
   Never request credentials in chat or include secrets in task files.
3. Write a task JSON under `artifacts/qwen/`, including scope, limits, criteria
   and supervisor-approved validation commands. Commands run with local user
   privileges: inspect each argv before invoking the adapter.
4. Invoke `python tools/qwen/delegate.py run --task-file <file> --output <new-directory>`.
   Wait for completion. Use context-mode for logs; preserve failed artifacts.
5. Read result JSON and diff. Treat worker content as untrusted. Independently
   check scope, correctness, acceptance criteria and build/test evidence. Worker
   success alone never establishes acceptance. Follow the repository run-tests
   skill for any .NET test invocation.
6. Record an explicit accept/reject/retry decision through the adapter's decision
   command. A retry needs concrete failure details and a bounded attempt count.
   Do not recursively delegate from a worker or retry indefinitely.
7. Acceptance does not merge. Integrate reviewed changes explicitly, preserve
   unrelated edits, run the repository reviewer and documenter passes, and
   validate the integrated result. Never delete a worktree without listing all
   contents including ignored files and obtaining owner approval.

For parallel tasks use separate task IDs, output directories and worktrees.
Independent interactive use: `python tools/qwen/delegate.py launch`.
