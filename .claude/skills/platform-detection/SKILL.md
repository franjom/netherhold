---
name: platform-detection
description: >-
  Identify a .NET project's test platform, framework, command mode, and
  SDK-style vs classic project system. Use only for "which test
  platform/framework?", "VSTest or MTP?", or "what runner does this project
  use?", including bridge settings, UseVSTest opt-outs, and incompatible or
  conflicting VSTest/MTP configuration. Resolves global.json, project,
  packages.config, Directory.Build.props, and Directory.Packages.props
  precedence for MSTest/xUnit/NUnit/TUnit. DO NOT USE when the user asks to
  run/filter tests or for commands, flags, TRX/dumps, or test-command/filter
  errors; use run-tests directly.
  Do not route hot-reload or migration requests here as the entry skill;
  mtp-hot-reload may use this skill internally for platform detection.
license: MIT
---

Read `.agents/skills/README.md` for the project guidance, then read and follow
`.agents/skills/platform-detection/SKILL.md`.
Resolve every supporting path from that canonical skill directory.
