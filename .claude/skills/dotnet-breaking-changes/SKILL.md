---
name: dotnet-breaking-changes
description: >
  Use before changing .NET/C# code whose callers you do not control: a NuGet library, or a repo with
  PublicAPI.Shipped/Unshipped.txt, ApiCompat, multiple TargetFrameworks, InternalsVisibleTo, or
  generated (.g.cs) partials. Green builds miss silent breaks from optional parameters, parameter
  names, sealing, const values, nullability, and #if.
  USE FOR: "without breaking existing callers"; adding a public method, overload, optional
  parameter, or interface member to a shipped library; renaming a public method or an internal
  helper a friend assembly uses; consolidating duplicated logic; changing a signature,
  nullability, default, or attribute; adding a target framework; RS0016/RS0017 or ApiCompat errors;
  "is this PR breaking?". For a rename, extract, or merge, also invoke csharp-refactoring.
  DO NOT USE FOR: an executable whose callers all live in one project; renaming a local variable
  or private member, even in a shipped library; formatting; framework/SDK/NuGet upgrades
  (use dotnet-upgrade).
license: MIT
---

Read `.agents/skills/README.md` for the project guidance, then read and follow
`.agents/skills/dotnet-breaking-changes/SKILL.md`.
Resolve every supporting path from that canonical skill directory.
