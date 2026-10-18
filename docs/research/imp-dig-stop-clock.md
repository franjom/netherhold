# Exhausted Imp dig action and slot availability

The native dates in the clock probes below are **execution-clock timestamps**.
Modern packet replay event dates are post-turn labels. See
[clock calibration](imp-observation-clocks.md); do not compare the two without
converting their domains.

## Evidence and local cause

**STRONGLY INFERRED (2026-10-18):** starting from the completed native turn-165
[strike image](imp-strike-choice-boundary.md), successful calls to the original
animation advance `0x004B3C10` and action poll `0x00496AA0` finish these actions:

| Native worker | State-14 entry | Phase after strike | Completion poll | Main hash-274 completion |
|---|---:|---:|---:|---:|
| 452 | 150 | 2726 | 168 | 168 |
| 454 | 158 | 4094 | 167 | 170 |
| 455 | 137 | 4087 | 167 | 167 |

Every emulated call returned successfully. No action or animation function was
stubbed. Art slot 5 has 15 Main frames and 8 End frames at 20 FPS. Its integer
rates are 1365 and 2560; the Main-to-End transition preserves truncated phase
carry. The action poll observes completion after the advance that finishes End.
The [continuous full-update capture](imp-dig-sequence.md#native-sequence--confirmed)
directly confirms these action-clear dates. **CONFIRMED:** the native captured state timeline independently shows 454/455 in
state 10 at snapshot header 168, and 452 at header 169. Main's `WorkCadence.ActionEnded` instead uses a six-turn
modulo from work entry, holding 454 three turns too long.

**CONFIRMED within a bounded modern control:** restore the native post-strike
tiles, workers and task occupancy at 165; run actual worker updates in order
452, 453, 454, 455 through 171. Freeze movement and skip worker 453 until its
captured pose/state and pre-choice RNG are restored at 171. With hash 274,
454 releases on 170 and takes mining only after 453 chooses on 171. Worker 453
takes Mine `(29,54)`, ending RNG 4284192478. Forcing only 454's completion on 167
makes it take mining on 168; all three slots are occupied before 453 chooses.
It takes Claim `(28,54)`, ending RNG 1843400869, matching the native boundary.
The isolated phase-based candidate produces that same choice without forcing
the worker's state-entry time.

**STRONGLY INFERRED:** the stale action hold is one upstream source of apparent
priority differences. This control does not prove a complete six-turn replay:
movement and chooser history are supplied, not simulated, and native task-id
reuse is not restored. In the capture, exhausted task id 1 becomes kind 14 on
167; the modern allocation control does not reproduce that identity lifecycle.
The broader reinforcement discrepancy remains Q612.

## Candidate and acceptance

The isolated hash/save-276 candidate changes only literal `TileWorkKind.Dig`
in state 14 after
the tile ceases to be diggable. It calculates the authored stop deadline,
persists it, and stamps it when queued work collapses the tile. Descending
worker order determines whether stop is requested on that turn or the next.
Unmarking a still-diggable wall retains the existing action boundary. Unsupported
animation metadata retains the old fallback. Mining, strike cadence and chooser
preferences are unchanged by this candidate.

Review identified an excluded edge: queued `DigOwnWall`/`DigEnemyWall` orders
can re-resolve through Rock to floor in one flush while keeping their original
kind. These state-14 actions still use the old modulo. This diagnostic candidate
does not correct them; rejection below prevents gameplay merge regardless.
The reconstructed clock assumes uninterrupted art-5 history from work entry;
animation interruptions have not been validated.

All 3,584 simulation tests pass, including captured completion dates,
collapse ordering, pre-action collapse, unmarking and resumed save/hash behavior.
The captured 158-to-167 case fails on the old implementation and passes here.
The tools build has no warnings or errors. Paired packet replays use the same
existing entity maps and seeds; baseline results are reused from hash 274.
The Level4 seed-1 entity map is also reused for seed 5, a comparison limitation.

| Level / seed | Hash-274 win / Imp deaths | Candidate win / Imp deaths |
|---|---|---|
| 1 / 1 | 1804 / 0 | 1824 / 0 |
| 1 / 5 | 1804 / 0 | 1788 / 0 |
| 4 / 1 | 6732 / 3 | none by 6804 / 54 |
| 4 / 5 | none by 6804 / 35 | none by 6804 / 11 |

**Rejected for gameplay merge.** Local native agreement does not establish
campaign compatibility. Preserve the candidate separately. Next locate the first
divergence in the failing Level4 seed-1 trajectory and distinguish native
task-object reuse/work visibility from the remaining hit/arrival timing before
combining corrections. Do not adjust job priorities from this result.

The seed-1 event streams are identical through 594. Their first differing event
is on 596: candidate Imp 14 abandons exhausted Dig `(28,53)` and raises a claim;
baseline releases it on 599. Both had changed Reinforced Wall to Rock on 592
and Rock to floor on 593. Imp 14 takes the next marked wall `(28,55)` on 597
instead of 600. This is the first candidate-induced event
change, not a demonstrated cause of the later deaths or a native mismatch.
Inspect this boundary alongside the earlier Portal visibility control.

The corresponding native worker is 442. Its snapshot timeline and the corrected
wall comparison are in [imp-create-position.md](imp-create-position.md).
The work-entry clock predicts its captured phase and release too, but the
packet replay's wall falls earlier. The cause of the failed campaign remains open.

## Reproduction

Ignored artifacts: `build/imp-acceptance/reverse-engineering/imp-dig-stop-20261018/`.
`native_clock.py` consumes the earlier completed live image and writes
`native-clock.json`; `export_stop.py` exports `input.json`. `Program.cs` runs
the bounded worker update: `modern-fixed-stop.json` is the retained hash-274
baseline, `CONTROL_STOP=1` gives `modern-native-stop.json`, and hash 276 writes
`modern-phase-stop.json`. `paired_dig.py` reuses the retained baseline logs and
runs four candidate replays; `verify.py` checks deadlines, choices and outcomes.
