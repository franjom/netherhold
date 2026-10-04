# Keeper spell possession implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans to implement this plan task by task. Steps use checkbox syntax.

**Goal:** Implement the researched simulation side of Possession, preserving its original command and state boundaries.

**Architecture:** Keep casting in KeeperSpellRules/ShotRules and possession lifecycle in a focused PossessionRules. Engine-neutral commands carry the original action semantics; presentation remains outside this stage. Preserve state/job/claims on entry and use the existing state/category hooks on exit.

**Tech Stack:** C#, integer-only DK2 simulation, xUnit and private PyGhidra research.

**Spec:** User's seven-spell brief, Stage7; canonical evidence will live in `docs/research/keeper-spell-possession.md`.

## Global constraints

- Work only in this isolated worktree; no merge or push.
- Every binary function read has a dated record; raw decompiler output stays quarantined.
- Balance values come from GameDatabase; new state travels through hash and snapshot together.
- The owner must choose the upkeep policy before its implementation.
- Each independently reviewable part gets its full gate, commit and fresh verifier.

## Review focus

- Entering while already possessing consumes the ordinary cast but refuses the downstream effect.
- Entry preserves jobs, claims, state and current action; autonomous execution pauses.
- Exit can encounter frozen/held creatures and uses the original state-transition refusals.
- Last first-person stat row wins with its original signed-word width and fixed-point truncations.
- Death, aura deadlines and any new flags survive snapshots and keep their original ordering.

## Part7a: lifecycle and modifiers

**Files:** New `PossessionRules.cs`, `Commands/PossessionCommands.cs`, `Systems/PossessionSystem.cs` and `PossessionTests.cs`; update KeeperSpellRules, ShotRules, CreatureState, WorldSnapshot, autonomous system guards, CombatSystem, FightRules, SpellRules, MeleeDamageRules and GameDatabase. Record the evidence and retain explicitly unbuilt branches.

**Interfaces:** Process26 calls the lifecycle rule with the resolved creature. A leave command carries the keeper id through PlayerCommand. First-person stat lookup takes CreatureStatId and returns the original signed-word value. New possession/influence flags and the influence deadline belong to creature state.

- [x] Write cast tests through existing CastSpellOnCreatureCommand: accepted owned target, cost/clocks, every read target6 refusal, preserved job/state, duplicate possession downstream refusal and death gate.
- [x] Run focused tests and observe the existing unsupported-spell failure.
- [x] Implement process26 and target6 through existing casting; implement set/clear using the audited setter and existing category hooks.
- [x] Test and implement autonomous pause, continuing actions/status, influence line of sight and strict deadline, stat precedence/truncations and snapshot round trip.
- [x] Add upkeep (owner chose the binary rule, 2026-10-04); preserve LEVEL flag, creature/level scaling, upgraded percentage and strict insufficient-mana exit if approved.
- [x] Run focused tests, complete gate and all42 scenarios; explain any change against Stage0 and bump hash/save together.
- [x] Commit named paths and obtain a fresh independent Part7a verifier; fix each confirmed finding separately (nine corrections independently approved; upkeep still awaits the owner).

## Part7b: possessed actions

**Files:** Extend PossessionCommands/Rules/System and focused action tests; reuse researched action clocks, combat hits and shot casting.

**Interfaces:** Movement carries a pose in fixed-point units and heading. Attack/cast carry the researched aim/slot semantics, not an invented target-tag shortcut.

- [x] Close action25/26 target selection and action timing in the binary before implementation.
- [x] 7b1: movement (action25) tests, implementation, hash176 and snapshot; facts in keeper-spell-possession.md §Possessed actions.
- [x] 7b2: write failing melee miss/hit, spell-slot/refusal/cooldown, link-order and snapshot tests.
- [x] Implement the researched command paths (melee, spell scan, free aim, teleport, speed, gold pick-up, mixed-class link order, hash177); follower, dig/claim, chicken, type14 and object/shot/body attacks stay unbuilt (Q535).
- [x] Run focused tests, complete gate and all42 scenarios.
- [ ] Commit and obtain a fresh verifier.

### Action implementation contract

- Commands carry typed fixed-point pose/aim and spell slot; attack selection remains simulation-owned. Preserve action25's packed-word zero branch and masked heading.
- Track mixed-class tile insertion with a separate persisted link order, leaving the legacy trap-chain ordering alone; update at actual position/layer mutations. Reuse action/shot damage consumers, with separate possession entry. Melee stamps before searching; spells stamp after their branch even when action start refuses. Cooldown equality refuses.
- Model the requested pose height and free aim explicitly in state, hash and snapshot. Existing vertical physics/collision remains the documented M9/Q398 boundary; do not substitute an AI-target cast for free aim.
- Shipped action interruption bytes are all1; preserve replacement of represented actions. The generic engine refuses a zero interruption byte, which this simulation's data model does not author.
- Preserve the melee neighborhood order, strict cone ±0x155 and replacement conjunction. Preserve spell player/class list order and minimize the signed score `4096 − normalized ray dot`, starting from the authored angular threshold. Tests distinguish both from nearest-distance selection.
- Keep pickup, followers, Hero Lair controls and presentation explicitly unbuilt. Owner choice is still required for upkeep.

## Final integration

- [x] Continue Stage8 research upgrades and documentation after Part7 passes (no new build: grant and bonus data already generic; KeeperSpellUpgradeTests; no hash bump).
- [ ] Run the user's final verifier over the entire branch.
