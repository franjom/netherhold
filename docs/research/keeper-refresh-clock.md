---
systems: [simulation-core]
---
# Keeper-frame refresh clock

## Binary rule

STRONGLY INFERRED from fresh primary reads of
[004BB2A0](functions/004BB2A0.md) and [004B8640](functions/004B8640.md),
with instructions004BB64D-004BB6A9 checked: the keeper frame tail compares
native now unsigned against player+0x96. It runs when now is at least that
deadline or byte+0x10FB is nonzero. It stamps+0x92 with now, draws50 on the
world stream at Player.cpp:2254, clears the request byte and stores
now+100+draw into+0x96 with32-bit wrap.

The outer frame gate is no killer at+0x132 or flags+0x76 bit22. Bit19 only
gates the free-Imp block earlier in the frame; it does not suppress this tail.
The call comes after mana, timed actions, construction waves and research.
The initializer writes+0x92=0, +0x96=native now-1 and request=0.
The current ordinary startup clock is1, so its initialized deadline is0.

Existing field queries find initializer and keeper-tail direct writes to
these player offsets; the request producer and other consumers are not closed
by that scan. No broader keeper service is inferred from the timer alone.

## Captured boundary

CONFIRMED in the Level5 seed1 capture: keeper3 has last stamp1308,
deadline1438 and request0 at completed1400 and1437. At completed1438 the
stamp is1438, deadline1550 and request0. The1438 draw must therefore return12.
The437 controlled replay reaches this site's absence after516 matching
complete-turn draws through1437 and14 matching draws in1438; see
[Training Target continuation](training-target-rebuild.md).

Production437 has no corresponding timer fields. The bounded438 continuation
restores their actual captured1400 values, including the then-current1438
deadline. No value from a later capture or fitted deadline feeds the run.

## Built scope and controlled comparison at438

KeeperRefreshClockRules and the existing active-keeper tail retain the clock
and bit22 frame gate. All four values are saved and hashed. Ordinary startup
initializes its clock at0; a restore keeps its saved values. PlayerStatus is
not the frame gate. Other bit22 consumers and the existing batching of player
services remain partial; the request producer and broader service are unclosed.

The diagnostic adapter adds one declared input key, Q712_KEEPER_REFRESH_INPUT,
containing keeper3's actual1400 clock state and bit22. The other20 input keys
are unchanged. The state-only control has438 storage/reader but the437 tail;
it and the frozen437 reference retain516 ordered draws through1437 plus14
matching draws in1438. Fullproduction438 runs its clock from startup, restores
the captured1400 state and matches590 ordered sites/preseeds through1442.
Its next difference is1443 draw1, native Object.cpp:63 versus Chicken roaming
at ObjectState.cpp:2851. The state/reader alone does not extend the prefix.
These controls establish draw continuation, not full-state or Heart parity.

External evidence is add-only under
`F:/DK2Evidence/{studies,builds}/q712-keeper-refresh--2026-11-07--001`.
The initial runs are frozen437, clock-state-only and production438. The
clock input and native-clock-boundary.json distinguish current1400 state from
later validation frames; no later frame is used as an input.

## Verification at 438

The final focused run passes 43 cases; the counterfactual fails two cases
and passes its positive control. The ten-suite full run passes 8496 cases
with 30 skipped and zero failures. The Release solution build has zero
warnings/errors; Unity compiles with the two existing warnings.
Final logs and TRX files are in the external study's `verification/` directory.
The final `delivered438-verification.json` receipt verifies the 590-draw prefix and
next boundary, plus all 51 Imp455 samples. The frozen437 and clock-state-only
controls preserve the earlier prefix with the same 21 declared input keys.
Only keeper3's actual 1400 clock state is added; later frames validate the
result and never supply inputs. The final comment-only overlay refresh is
verified to preserve runtime bytes and the same control result.

Archive provenance is base `e050b26d`. The study archive layout is
`delivered-change-v2.zip`, `verification/full-tests/*.trx`,
`verification/keeper-refresh-final-focused.trx` and `verification/summary.json`;
the build archive contains `source.zip`. Each archive's SHA-256 inventory path
is `manifest-final.json`. Replay receipts are at the study root; test logs
and TRX files are under `verification/`. Local integration is recorded in
`integration-receipt.json`.
