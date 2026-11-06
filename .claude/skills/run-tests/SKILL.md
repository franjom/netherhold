---
name: run-tests
description: >-
  ALWAYS USE before running .NET tests or answering with a test command or
  flags. Trigger on "run the tests", "exact dotnet test command", one
  test/class/category/trait/target framework, combined filters,
  `--filter-query`, `--no-build`, `--diag`, diagnostic logs, TRX, coverage
  collection, crash/hang dumps, filter errors, or unrecognized options. Chooses
  repository-compatible classic, VSTest, bridged MTP, or native MTP syntax for
  MSTest/xUnit/NUnit/TUnit. DO NOT USE for platform identification alone
  (platform-detection), writing or debugging test code, interpreting an
  existing coverage report, CI investigation, migration, or a persistent hot
  reload/watch loop.
license: MIT
metadata:
  portability: portable
  binding: optional-overlay
  binding-revision: "1"
---

Read `.agents/skills/README.md` for the project guidance, then read and follow
`.agents/skills/run-tests/SKILL.md`.
Resolve every supporting path from that canonical skill directory.
