# Imp comparison: snapshot labels and execution clocks

## Calibrated observations

**CONFIRMED (2026-10-18):** the raw Level4 capture header and modern post-turn
label agree at both recorded Create Imp births:

| Packet turn | Native worker / first active capture header | Modern worker / post-turn birth |
|---:|---|---|
| 392 | 443 / 392 | 13 / 392 |
| 552 | 442 / 552 | 14 / 552 |

Both native birth positions exactly equal their packet coordinates; see
[the position trace](imp-create-position.md). Earlier `export_native.py`
subtracted one from every header while modern raw rows and event labels used
completed-turn numbers. That mixes two clocks when comparing post-turn states.
The cast anchors do not support subtracting one only on the native side.

The recorder reads the clock at `*(*(world+4)+4)` without arithmetic and writes
the before/after readings into the frame. Its wait/settle loop and untorn flag
establish a stable clock, not proof that every record was sampled at an identical
point inside the world's update. Birth anchoring therefore supports these
packet/post-turn comparisons, not universal frame-by-frame state equivalence.

**Keep execution timestamps separate.** Modern `sim.Tick()` executes context
clock `t-1`, then increments `CurrentTick` to `t`; the probe labels the resulting
row `t`, and its event sink adds one to the event's execution clock. Native
animation/action stamps and live in-function clock readings are execution
timestamps. They must not be silently relabelled as snapshot headers. The live
165/171 boundary probes and their stored work-entry stamps retain their original
clock domain; their local binary-call results are unchanged by this correction.

In that Level 4 capture, the starting four workers first appear at header 2; modern starts with
them already present at post-turn 1. That initial-condition difference must be
measured separately rather than erased with a universal one-turn translation.
The [fresh Level 1 capture](decision-parity-audit.md#level-1-captured-construction-window-and-trigger-visibility)
has a different initial observation, reinforcing that limit on generalisation.

## Consequences and reproduction

Native snapshot dates in imp-hatchery-work.md now use raw headers. Its common
observation window is 2–1100, 1099 observations per stream and 5654 living
Imp-observations each. Counts and sampled walking were regenerated. Native-only
intervals, binary execution-clock calculations and modern campaign acceptance
results do not change. The previous broad claim of exact timed state agreement
around the first cast's claim is withdrawn; the state sequence has a one-turn
offset under birth anchoring.

Ignored artifacts: `build/imp-acceptance/reverse-engineering/imp-wall-boundary-20261018/`.
`verify.py` checks raw packet framing, native birth headers/positions, unchanged
modern baseline and bounded controls. `calibrated/export_native.py` preserves
headers; `calibrated/measure.py` compares the common window. Previous artifacts
remain retained with their original labels for reproducibility.

An independent reviewer redecoded both packet actions, read their raw native
records, and checked the recorder and modern tick/sink code. This is independent
verification of these anchors and labelling paths, not a whole-capture replay.
