# Level 7 Heart destruction and Imp supply implementation plan

> For agentic workers: use subagent-driven-development for the independent Imp supply task; root owns the Heart destruction task. Use checkbox steps for progress.

Goal: reproduce the native Level 7 doomed keeper's Heart destruction and free-Imp eligibility during the introductory flyby.

Architecture: preserve the engine-neutral simulation boundary. Recover the native death-event/art sequence and use existing effect/animation presentation machinery; correct the independent Imp rule at its native eligibility or scheduling branch. Do not tune level-specific constants or hide creatures to match an image.

Tech stack: C#, deterministic simulation and xUnit; existing Unity classic renderer; Ghidra queries against DKII.EXE.

Spec: owner's 2026-11-01 request in this conversation: research and implement the missing destroyed-Heart animation and explain/correct the two free Imps visible at the nearly dead AI Heart. OpenKeeper is supporting evidence only.

## Constraints

- Worktree: .claude/worktrees/l7-heart-death; branch fixes/l7-heart-destruction; base ea478084.
- Read each function record before querying and write findings with dated History. Native binary and shipped data take precedence.
- Two CPU workers total while busy; DOTNET_PROCESSOR_COUNT=2 and builds -m:2. Coordinate heavy jobs between root and worker.
- Evidence outside clones is add-only under F:/DK2Evidence. Do not remove worktrees, evidence or ignored files.
- Preserve owner's Dungeon.unity and path edits. Stage named paths; no merge/push or hook bypass.
- Simulation behavior changes require paired HashVersion/FormatVersion bump, pin and hash-version entry; recheck master identifiers before commit.

## Review focus

- AI versus local Heart death; already ruined-at-load Heart; deletion without combat.
- Death during introductory camera movement and while ordinary Heart room regeneration is suppressed.
- Exactly-once death effects and resource cleanup after expiry or level switching.
- Imp eligibility and deadline controls without level-specific suppression.
- Save/load and deterministic RNG boundaries; no undocumented art or constants.

## Task 1: native Imp replenishment parity (l7_imp_supply owns)

Files: ImpSupplySystem.cs, ImpSupplyRules.cs if needed, ImpSupplyTests.cs and focused real-data tests; imps-and-mana.md and Imp-only KB records.

- [x] Compare 004BB2A0, its player traversal/initialization and shipped Level 7 inputs to C#; enumerate mismatches before editing.
- [x] Write native-addressed failing regressions and verify failures.
- [x] Implement the minimum binary-backed correction; verify focused tests.
- [x] Report findings and exact file ownership to root for versioning and final review.

## Task 2: native Heart destruction sequence (root owns)

Files to inspect: WorldState Heart damage/deletion, DungeonHeartSystem and DefeatRules; presentation ScenePresenter/WorldView.Objects, effect requests and Unity RoomConstructionParticles/ClassicMeshes. Tests: Heart/Defeat, DungeonHeartVisualTests and relevant effect tests. Exact edit set follows the binary inventory.

- [x] Recover object death, Heart-specific destruction and room-ruin branches; inspect authored effect/mesh resources. Compare OpenKeeper for reference without copying implementation.
- [x] Reproduce Level 7 startup death and inventory each simulation/presentation mismatch with addresses.
- [x] Write failing regressions; implement the recovered timeline, resources and lifecycle through existing abstractions.
- [x] Verify the intro-flyby case in an isolated Unity probe plus engine-neutral tests.

## Integration

- [x] Run focused clean-base tests before production changes; classify existing failures.
- [x] Bump paired versions and pin if behavior changes, then run appropriate full suites and adapter checks.
- [x] Run actual reviewer; resolve objections; run actual documenter and the docs/KB gates.
- [x] Inspect documentation edits and recheck allocated identifiers. Git records the final named-path commit with its Assisted-by trailer; retain branch/worktree for review.
