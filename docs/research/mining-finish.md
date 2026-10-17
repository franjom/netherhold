# The mining action's ending phase

Sources: retail v1.70 binary through PyGhidra, shipped Imp art slot 5 and
the four Level 1–4 seed-1 full-state captures, checked 2026-10-17.
Ordinary keeper Imps only; full action scheduling is not claimed.

## Mechanism — STRONGLY INFERRED from the binary

[Action begin](functions/00496700.md) keeps the phase when its looping
resource and art slot already match. Restarting the action does not restart
that loop. [Action 2](functions/004971D0.md) strikes once while workable.
When the seam is gone it sets animation stop bit 8 and keeps returning busy
until the terminal completion flag appears. The
[mode-3 runner](functions/00496AA0.md) needs handler and animation completion
before the [mining state](functions/00465870.md) checks the task and hands.

The [type-6 clock](functions/004B3C10.md) suppresses looping after stop is
requested, carries the remaining main phase into the End segment and marks
completion. The next action poll sees it. Imp art 5 has 15 main frames,
8 End frames and 20 FPS, with no Start segment. Its integer advance rates
are 1365 and 2560; segment durations and carries retain intermediate truncation.

## Capture check — CONFIRMED observations

Across frames 2–1101 of the four native captures, the normal mining phase
equals `(frames since entry into state 26 × 1365) mod 4096` in all 1068
checked samples (172/261/154/481 by level). The first action starts on the
update after state 26 is entered. Starting the stop calculation uses the
phase before that update's animation advance.

From the first observed stop request to leaving state 26, the binary clock
predicts all 102 measured intervals: 11/25/14/52 by level. This check uses
the observed phase and segment flags after the request; it does not assume
all miners see a collapse in the same update. Observed seam-collapse-to-exit
delays span 1–5 frames, so a fixed added delay is not equivalent.

Example: Level 1's Imp 455 mines `(14,24)`. The seam becomes floor at
frame 159. The Imp retains action 2 through frame 162, and enters state 10
at 163. Its main phases at 158–160 are 1363, 2728, 4093; frame 161 carries
into End at 2554, and 162 marks completion. Full hands on a still-workable
seam can instead leave on a strike turn; this is a different branch.

## Implementation and verification

This section describes the unmerged `fixes/imp-store-timing` candidate.

`AnimationTimingRules.LoopingActionStopTicks` reconstructs the ordinary
loop phase from the work-state epoch, requests stop and counts to completion.
`CreatureState.MiningActionEndsAt` retains that deadline while the ending
phase runs. `HoldForMiningFinish` starts it only for an exhausted seam after
the first action has begun. The worker's own last strike is applied before
this state check. Only after completion does a surviving task allow the
full-hands store choice or fresh-claim raise; a removed task raises nothing.

Thirteen focused cases pass, including variable phases, the worker's own
last strike, no action yet, task loss, full hands with and without seam
exhaustion, hash sensitivity and a snapshot taken inside the End segment.
Eight initial regressions failed before the implementation; the no-action
control passed. All 3586 simulation tests pass. Read-only independent review
found no correctness issue in the bounded implementation. Campaign acceptance
fails: Level 1 wins in 14/20 seeds against 19/20 on baseline hash 265 and
16/20 on the preceding hash-267 candidate. Losing seeds are 3, 11, 13–16.
All 20 runs have consecutive turns and no placed-type mismatch, but these
replays do not establish native entity/RNG equivalence. The mechanism is
supported; the combined implementation is not approved for gameplay merge.
Results: `reverse-engineering/imp-mining-tail-level1-20261017/results.json`.

Captures are under `reverse-engineering/dk2runs/` at the paths in
[the task audit](imp-task-audit.md#scope-and-reproducibility), decoded with
`dk2capture.py`. Relevant creature offsets: state `+0x25E`, task kind
`+0x2E6`, task tile `+0x2DA/+0x2DE`, action `+0x3A9`, phase `+0xD8`, rate
`+0xDC`, animation flags `+0xF1`. State-controller offsets need their base.
