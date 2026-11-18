# Torture captive simulation gaps

Original implementation: `fix/torture-captive`, based on `feature/bot-defence`
at `dfb5209e`, committed as `d72b4fc7`. Integration uses `integrate/torture-captive`
in `.claude/worktrees/torture-captive`, based on master `3f4c4ac4`, with the
isolated fix cherry-picked and no bot ancestry imported.

Status: original implementation, measurements, tests, reviewer and documenter
passes are complete, including the retained Heal hand guard. Integration
preserves master's Heart-defense behavior and passes both test suites,
reviewer and documentation checks.
Campaign measurements remain historical and have not been rerun on master.
Findings and campaign results:
[canonical note](../research/torture-captive-health.md).

1. Read the existing function records, then query DKII.EXE for Heal's collision,
   health and interruption paths, action 15, and the need-anger/mood gates.
   Verify all state-table bit-1 stores. Keep listings in the scratchpad.
2. Measure unchanged Level 6a seeds 1–3, player 3, through 57600 ticks.
3. Write failing focused tests for captive healing/conversion, objective refusal,
   and state-flag anger/chooser suppression. Implement the binary's guards.
4. Record findings in their canonical notes and function records, add the ADR,
   bump the simulation/save hash version and update affected scenario pins.
5. Run simulation and behavior suites, campaign measurements and docs/KB gates.
6. Run the reviewer, resolve objections, run the documenter, read its changes,
   then commit only named files. The owner subsequently authorized master
   integration; worktree removal awaits inventory-based cleanup approval.
