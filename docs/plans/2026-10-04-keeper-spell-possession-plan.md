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
- [ ] Add upkeep only after the owner chooses; preserve LEVEL flag, creature/level scaling, upgraded percentage and strict insufficient-mana exit if approved.
- [x] Run focused tests, complete gate and all42 scenarios; explain any change against Stage0 and bump hash/save together.
- [ ] Commit named paths and obtain a fresh independent Part7a verifier; fix each confirmed finding separately.

## Part7b: possessed actions

**Files:** Extend PossessionCommands/Rules/System and focused action tests; reuse researched action clocks, combat hits and shot casting.

**Interfaces:** Movement carries a pose in fixed-point units and heading. Attack/cast carry the researched aim/slot semantics, not an invented target-tag shortcut.

- [ ] Close action25/26 target selection and action timing in the binary before implementation.
- [ ] Write failing movement, melee miss/hit, spell-slot/refusal/cooldown and snapshot tests.
- [ ] Implement the minimal researched command paths; keep pickup, follower UI and presentation branches explicitly outside the requested actions.
- [ ] Run focused tests, complete gate and all42 scenarios; commit and obtain a fresh verifier.

## Final integration

- [ ] Continue Stage8 research upgrades and documentation after Part7 passes.
- [ ] Run the user's final verifier over the entire branch.
