# Reinforcement action clock (Q712/Q719)

## Binary rule and pre-edit differences

Read with Ghidra on 2026-11-03. These ordinary action-7 rules are confirmed by
the binary; the captured controls and their input limits are in
[Q719's worker controls](q712-atomic-inputs.md#saved-bodies-and-worker-resume-q719).

| Address | Native rule | Through hash 388 |
|---|---|---|
| 004950C0, record 7 | Art 12, mode 3, handler 00497960. | Reinforcement excluded from the stored work-animation clock. |
| 00488930 at 00489A68 / 00496AA0 | Poll the current action before fight processing and state dispatch. | Reinforcement work queued after its state's task validation. |
| 00496980 / 00497960 | Work uses the action's stored point independently of the current task. | Work uses only TargetTileIndex. |
| 00497960 / 00453DC0 | Eligible work pays XP, applies work, then retests the complete neutral-wall/own-floor gate. Still eligible returns done; otherwise request Stop and return done only on a cycle flag. | Reinforcement cadence and completion use fallback tick constants. |
| 00496AA0 / 00466390 | Both handler and animation completion precede checking the task again; a completed final hit can release state 22 in that same update. | Final applied work is noticed by state 22 on its next update. |
| 00496700 / 00496D20 | Accepted action replacement and explicit ending clear the old action block. | No independent reinforcement action to cancel or save. |
| 0048904E / 00489A68 | Worker claim/pickup precedes the action poll. | The worker block runs in the later worker step. |

The ordinary state 21 arrival enters 22 after task/facing checks (004662E0).
State 22 returns while any action is running (00496D00). Otherwise it checks
the kind 6 task and neutral wall, starts action 7, or follows its existing
finished-wall/corner tail to 10. A final hit without cycle flags may remain
busy: an unconditional eager exit is not the rule.

## Implementation and scope

ADR-0638, hash/save 389, stores action 7's active flag, independent
tile point and animation-completion bit, alongside the existing handler-done,
count and clip fields. Its common poll runs before ProcessFight, and the
state checks follow that poll. Art 12 keeps or installs through the existing
animation clock; final advancement follows the worker handler.

The linked worker Prelude runs the existing claim/pickup block early only
for this action or its authored state 22, and deduplicates the later worker
block. Other worker families keep their existing pickup phase. The ordinary
host includes Prelude; a custom host must include it ahead of combat too.
Standalone ImpTaskSystem includes it itself. Missing timing retains the
existing fallback; this is not a complete generic native action layer.

Accepted action replacement and shared EndAction cancel the independent
work block, without resetting the retained animation object immediately.
The existing frozen-animation predicate pauses advancement, not the earlier
action-handler poll. No new reporting, fear or hero-selection rule is added.

## Verification

Independent source/proof/pin review approves the frozen implementation.
All 23 focused cases pass; full Simulation passes 4385 and Tools passes 111.
The exact core-test proof against unchanged archived hash-388 source gives ten
failures and two passing controls; all 748 source files remain unchanged.
The test SHA-256 is
`ed14761262c78b6f3bfe6e858290380efe2852af8afe5eac657a4c57b9ff1b3b`.

Hash/save 389 has pin `0x5863A5427336C18A`. Source and verification:
`F:/DK2Evidence/builds/q712-reinforce-action-verification-h389--2026-11-03--001/`.
Old-source proof:
`F:/DK2Evidence/builds/q712-reinforce-action-old388-proof--2026-11-03--003/`.
Final outcome/provenance review approves the completed measurements below.
Historical hash-378 diagnostic controls retain their declared overlays and are
not current outcome data. The actual-389 partial control is separately scoped
in [Q719](q712-atomic-inputs.md#withdrawal-destinations-and-actual-hash-389-inputs-q719).
Q712/Q719 remain open; no aggregate causal closure follows from this rule.

## Outcomes at hash 389

All 115 stored-pickup resyncs (75 Level 5, 40 Level 4) match the retained
367-template commands and exit 0. The observer matches all four main output
hashes. Independent review verifies the 701/374-file studies, 784-file
measurement build and 31-file verification bundle, with 752 frozen source/config,
45 external and 25 installed input hashes unchanged. The scoring wrapper
stopped only after producing death scores; its named amendment preserves those
successful hashes, and v2 completes the missing analyses without rerunning resyncs.

388 is the immediate baseline. Native/362/388 references and cohort definitions
are retained in [the prior comparison](q712-atomic-inputs.md#paired-outcomes-387-and-388).
Historical 1401-3400 capture-header labels remain distinct from atomic completed
turns and partial-input controls. Scores check the binary-backed rule; no tuning.

Strict Heart dispatch radius about (17,26), seeds 2/3/7: three native runs
versus fifteen state/generator pairs per remake version. Means per run:

| Hero-ticks inside | DK2 | 362 historical | 388 baseline | 389 |
|---|---:|---:|---:|---:|
| All non-collapsed | 4327.00 | 2720.93 | 2710.47 | 2830.73 |
| Targeting Heart pillar | 3298.67 | 1607.00 | 1593.73 | 1603.00 |
| Targeting Imp | 790.00 | 790.60 | 830.60 | 903.07 |

Same cohort; visible transitions, not every within-turn call:

| State-254 transition, mean/run | DK2 | 362 historical | 388 baseline | 389 |
|---|---:|---:|---:|---:|
| Entries | 15.00 | 12.87 | 13.87 | 14.73 |
| Exit to 199 | 3.67 | 0.60 | 1.47 | 1.13 |
| Exit to 203 | 5.00 | 2.60 | 1.80 | 2.00 |
| Exit to 1 | 3.00 | 4.67 | 4.20 | 4.47 |
| Exit to 6 | 2.00 | 4.40 | 4.93 | 6.53 |
| Exit to 8 | 1.00 | 0.53 | 1.40 | 0.60 |
| Fight plus flee (199+203) | 8.67 | 3.20 | 3.27 | 3.13 |

Death cohorts retain eight complete native Level 5 states at restore 1400,
five at 3400, and all eight Level 4 states:

| Window / group, mean/run | DK2 | 362 historical | 388 baseline | 389 |
|---|---:|---:|---:|---:|
| L5 1401-3400 / keeper | 13.38 | 9.90 | 8.55 | 9.15 |
| L5 1401-3400 / keeper Imp | 11.12 | 7.35 | 6.00 | 6.575 |
| L5 1401-3400 / hero | 3.75 | 5.10 | 5.38 | 5.25 |
| L5 3401-5000 / keeper | 8.60 | 9.16 | 10.52 | 10.92 |
| L5 3401-5000 / keeper Imp | 5.80 | 7.20 | 8.36 | 9.00 |
| L5 3401-5000 / hero | 5.00 | 4.44 | 4.24 | 3.72 |
| L4 5001-6000 / keeper Imp, exact | 6.500 | 11.925 | 10.325 | 10.350 |

Q714 Heart/terminal restoration is unchanged. The outcome-selected 32-pair
intersection gives exact Level 4 means 10.03125/9.59375/10.34375 for
362/388/389, versus native 6.50. Pre-terminal pairs number 8/0/0 respectively.
Against 388, deaths rise 0.025 all-40 and 0.75 in that selected subset. These
subsets establish neither causation nor per-death attribution to Q714.
There is no aggregate improvement or Q712 closure: pillar exposure remains
low, fight/flee exits remain far below native, and Q719 inputs remain partial.

Evidence: `F:/DK2Evidence/studies/heart-defence-q712-h389-l5--2026-11-03--001/`
and its `l4` sibling; measurement build
`F:/DK2Evidence/builds/q712-reinforce-action-measurement-h389--2026-11-03--001/`.
The source/proof verification bundle is linked above. The next possible
measurement is reporting Imps' near-Heart eligibility and candidate geometry,
keeping [actual-389 withdrawal inputs](q712-atomic-inputs.md#withdrawal-destinations-and-actual-hash-389-inputs-q719)
separate from an unproved selector or steering change.
