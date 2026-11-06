---
name: csharp-refactoring
description: "Safely refactors C#/.NET code without changing behavior. USE FOR any request to refactor, rename, move, extract, inline, merge, consolidate, deduplicate, split, or modernize C# code, even a small extract, inline, or rename, or merging near-identical types; green builds miss drift in #if branches, generated partials, and reflection/config names. When a changed member is visible outside the assembly (PublicAPI.*.txt, InternalsVisibleTo, shipped package), also invoke dotnet-breaking-changes; local/private renames do not need it. Also use when a request calls a feature, bug fix, package/framework upgrade, or public nullability change a refactor and the behavior-changing part must be separated or declined. DO NOT USE FOR ordinary feature or bug fixes not presented as refactoring; standalone upgrades after reclassification (use dotnet-upgrade); adding tests; or formatting-only work."
license: MIT
---

Read `.agents/skills/README.md` for the project guidance, then read and follow
`.agents/skills/csharp-refactoring/SKILL.md`.
Resolve every supporting path from that canonical skill directory.
