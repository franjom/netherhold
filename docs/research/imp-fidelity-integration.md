# Imp fidelity integration

## Correction audit

ADR-0512 replaces the use of unmatched-seed campaign wins as a merge veto.
Ten distinct behavioral changes were held across the experimental branches;
combined experiments are counted once. Eight supported corrections are
integrated at hash/save 280. Two animation-clock implementations remain
provisional because their inferred phase scope is incompletely validated.

| Change | Source commit | Disposition and canonical evidence |
|---|---|---|
| Store arrival/facing 28, deposit 29, then selection | `d7972800` | Integrated; [task audit](imp-task-audit.md), [state exits](imp-state-exits.md) |
| Apply each worker's work before the next worker | `de4aa9f1` | Integrated; [task audit](imp-task-audit.md) |
| Apply the miner's own strike before checking seam/hands | extracted from `20c969b4` | Integrated independently of the ending clock; [mining finish](mining-finish.md) |
| Newest-first movement advance | `d10d19aa` | Integrated; [movement order](imp-first-integration.md) |
| Exact idle-return progress and stopped body before resume | `372a7334` | Integrated; [idle arrival](imp-idle-arrival.md), [composition](imp-movement-arrival.md) |
| Four-turn first dig gap across all three dig kinds | `f62b06f7` | Integrated; [cadence](mining-cadence.md) |
| Preserve fractional human Create Imp coordinates | `7d4d87f1` | Integrated; [creation](imp-create-position.md) |
| Height-driven stationary Create Imp landing | `46dbd2f9` | Integrated within documented scope; [creation](imp-create-position.md) |
| Dig animation-ending clock | `7f5adaaf` | Provisional family model; [stop clock](imp-dig-stop-clock.md), [continuous work](imp-dig-sequence.md) |
| Mining animation-ending clock | `20c969b4` | Provisional; [mining finish](mining-finish.md) |

CONFIRMED: the bounded native mechanisms cited above. Neither this integration
nor those captures establish complete chooser, action-scheduler or campaign
equivalence. No job-priority, reinforcement timeout or forced RNG correction
is included. The two provisional models reconstruct phase from work entry;
interrupted action histories still require validation. Lost wins are not the
reason they remain isolated.

## Regression interpretation

The old last-seam two-miner test expected a same-turn departure on collapse.
It now checks that exactly one purse receives the final gold and neither miner
departs on the collapse tick. The delivery scenario now expects the actual
post-strike purse, 1091, instead of the old pre-payout event payload 818.
These changes follow the native action-before-hands order, not replay wins.

The prison feeding fixture now drops food centrally at `(5,4)` instead of
the edge `(7,4)`, retaining every feeding expectation. The edge case remains
an unresolved modern route/confinement symptom: both prisoners select two-step
food walks on tick 281, leave the cell at `(7,2)` on ticks 295/299 and never
eat. Central and near-center controls both pass; the central case eats on
281 for 375 health. This does not establish the native cause or justify a
gameplay compensation. Reproduce and compare the food walk against DK2 next.
Original and control scenarios, selected snapshots and logs are preserved in
`reverse-engineering/imp-integration-20261018/prison-audit/`.

## Verification

Baseline is master `9794e07b`; it builds with `NuGetAudit=false` because the
vulnerability service is unavailable. Four Behavior tests fail there: Level1
variable overrides, Level17 Harkan, the Goblin eating scenario and Level11A
Drako. Baseline logs: `build/imp-integration-20261018/` in the main checkout.

Integration solution and Unity adapter compile; the adapter has zero warnings
or errors. Final full-suite result: 5,765 passed, 25 skipped, two failed. All
3,607 simulation tests pass. The remaining failures are the baseline Level1
variable override and Goblin eating cases; Harkan and Drako now pass.
Store arrival's reviewed stop correction has a regression that fails both
route-finished variants before the fix and passes afterward. Knowledge-base,
documentation budgets, coverage and generated ledger checks pass.
Reproduction logs and TRX live under
`reverse-engineering/imp-integration-20261018/` in the retained research checkout.
