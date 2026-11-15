# Combat Pit completion implementation plan

> Execute with the subagent-driven-development skill; the parent owns shared state, verification, final reviewer/documenter passes and the commit.

**Goal:** Close the remaining Combat Pit gaps identified in the implementation audit.
**Spec:** `docs/research/combat-pit.md`, `combat-pit-spectators.md`, and the owner's request to continue the remaining gaps.
**Architecture:** Keep DK2 behavior in the engine-neutral simulation. Preserve native owner-word semantics, room update order, animation actions, fixed-point arithmetic and RNG order; presentation consumes existing sound/effect requests.
**Tech stack:** C#, netstandard2.1 simulation/presentation, net10.0 xUnit tests, Ghidra 12.1.4 and shipped data.

## Constraints and coordination

- Native evidence before behavior; update each researched function record in the same change.
- Preserve the unrelated front-end/HUD work in the shared checkout.
- No cleanup or evidence removal. Native evidence stays outside the checkout.
- Root owns shared state/hash/snapshot/copy integration, simulation composition, combat targeting and shot integration.
- Workers own separate feature files and tests; request the root's test slot before builds, so test assemblies are never rebuilt concurrently.
- New state advances the current hash/save version once for the completed change; regenerate knowledge, copy code, index and ledger as required.
- Use meaningful domain types at new interfaces. An unresolved native behavior remains explicitly unresolved after a concrete binary query; never substitute a guessed rule.

## Stages

### 1. Native contract closure

- [x] Read current KB records and independently inspect each relevant native handler before implementation.
- [x] Resolve the room update's sound/effect calls and fight-over notification transition.
- [x] Resolve action 44 at the cap, state 140, and every native writer/reader found for the pit kill counter.
- [x] Trace the shared custody word and classify consumers by native binding versus actual Prison state/category.
- [x] Check dead forced-target fallback, shot ownership, neighbour navigation byte, reset/ownership behavior and spectator approximations.
- [x] Record exact contracts and remaining limits in their existing canonical homes; update this plan with implementation rulings where needed.

### 2. Room lifecycle, notifications and ambience

**Ownership:** worker; new `CombatPitRoomStep.cs` and `CombatPitRoomTests.cs`; room-function KB records. Root integrates persistent room state and step registration.

- [x] Write behavioral tests for the fight-over transition, non-transitions, countdown reloads and native requests; verify expected failures.
- [x] Implement the per-room handler using the researched native contract, preserving count and draw order.
- [x] Cover multiple rooms, ownership changes and snapshot continuation in integration tests.
- [x] Review native claims and implementation independently before integration is accepted.

### 3. Cap action, victory state and counter

**Ownership:** worker; `CombatPitSystem.cs`, a new completion-test file and relevant function records.

- [x] Verify failure of tests for cap action/wait, saved state 140 and its anger outcome.
- [x] Implement real action/animation behavior through existing action infrastructure; no new guessed duration.
- [x] Implement any researched counter transition and its observed readers; otherwise document the bounded negative result.
- [x] Retain the distinction between a load-restorable state and an observed gameplay entry.

### 4. Captive and worker drops

**Ownership:** worker; custody/drop rules and their tests. Root coordinates changes to the shared pit state handler and snapshot fields.

- [x] Write failing tests for own fighters retaining ordinary eligibility, enemy captives entering the pit, custody while held/stunned and knockout/rescue.
- [x] Separate native owner-word binding from actual imprisonment at the consumers that need that distinction.
- [x] Add the native pit binding writes and worker/low-player-number drop branches.
- [x] Check pickup, drop, idle, room reset, torture, Thunderbolt and trigger guards against their native predicates.
- [x] Keep state/header/API changes scoped to the binding contract; use the C# refactoring and compatibility skills if renaming an existing member becomes necessary.

### 5. Combat and geometry integration

**Ownership:** root; combat targeting, shot creation, pit neighbours and remaining lifecycle integration.

- [x] Add a failing dead-forced-target fallback regression, then reproduce the native fallback scans and commit.
- [x] Add a failing shot-owner regression, then implement the native pit shot owner without changing ordinary shots.
- [x] Verify and implement the remaining neighbour-byte, entry-point, reset/ownership and destruction-request branches.
- [x] Check spectator action availability, duration and room centre against native helpers and shipped animation data before changing approximations.

### 6. Verification and documentation

- [x] Exercise each changed behavior with a real failing-before/passing-after test; cover state/hash/copy/save continuation for new fields.
- [x] Run focused tests while integrating, then the repository's Release build/tests and adapter compile with durable logs.
- [x] Run the independent reviewer, fix findings and rerun affected checks.
- [x] Run the documenter with the final diff, native verdicts and measured verification results; inspect its edits.
- [x] Pass docs, KB, coverage and ledger gates, stage only named task files and commit.
- [x] Report completed contracts and remaining measured limits without claiming live-game acceptance from unit tests.

## Review focus

- A pit fighter with a custody word must not become a Prison inmate by accident.
- A carried or stunned former captive must retain the native keeper's pickup rights until the native clear path runs.
- Friendly pit fighters, hostile captives and dying losers must use the correct targeting, damage and rescue paths.
- Ambient countdowns and room scans must preserve the simulation RNG stream across save/load and room topology changes.
- Script events, kill statistics and mentor notifications are separate contracts; none proves another exists.

## Verification

Release project results: 9,777 passed, 30 skipped; the Core header and Tools documentation checks passed on rerun after formatting repairs. Unity adapter compilation passed with two existing obsolete-API warnings. Independent reviewer and documenter passes completed; docs, KB, coverage and verification-ledger gates passed. Native visual acceptance remains outstanding.
