# Imp candidate acceptance

Measured 2026-10-17 against hash 265 at `647c43e8`. Main at `9f7341bf`
has identical simulation, data, pathfinding and simulation-test sources.
The original packet commands are replayed with simulation seeds, not a
matching native RNG stream. Wins measure compatibility with those commands;
they do not prove behavioral equivalence.

## Paired campaign results

| Level / seeds | Baseline wins | Hash 267 wins | Baseline Imp deaths | Hash 267 Imp deaths |
|---|---:|---:|---:|---:|
| 1 / 1–20 | 19/20 | 16/20 | 0 | 0 |
| 2 / 1, 8, 18 | 2/3 | 3/3 | 12 | 7 |
| 3 / 1, 8, 18 | 2/3 | 2/3 | 0 | 1 |
| 4 / 1–10 | 3/10 | 3/10 | 126 | 120 |

All 72 runs have consecutive requested turns and no placed-type mismatch.
Entity-map placeholders remain, so these are not exact native entity matches.
The complete paired results live at
`reverse-engineering/imp-merge-acceptance-20261017/results.json`.
The later hash-268 Level 1 sweep and the mining evidence are in
[mining-finish.md](mining-finish.md).

## Verdict

The candidate solution and Unity adapter compile. All 3586 simulation tests
pass. The full solution test run has six failures: one carrying-gold scenario,
four other behavior cases, and a license-header check naming five existing
presentation files. This does not override the campaign acceptance failure.
TRX files: `reverse-engineering/imp-final-tests-20261017/`.
The four other behavior failures reproduce on unchanged main; the carrying-gold
scenario passes there. Baseline TRX: `reverse-engineering/imp-final-baseline-tests-20261017/`.

Do not merge the gameplay candidates. Focused tests and binary evidence
support their bounded mechanisms, but the combined task races regress the
Level 1 command sequence. Repeated reinforcement is observed in the original;
there is no evidence for a reinforcement timeout or priority workaround.
The additional death trace is in [imp-survival-trace.md](imp-survival-trace.md).

Preserve `fixes/imp-store-timing` for a future matched-state experiment.
The next useful experiment starts an original and modern worker at an actual
selection boundary; the existing checker reconstructs that state incompletely.
Its measured limits and required capture inputs are in
[imp-decision-checker.md](imp-decision-checker.md). More unmatched-seed sweeps
alone cannot identify the missing mechanic. The merged research preserves
gameplay and snapshot format.
