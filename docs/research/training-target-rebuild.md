---
systems: [creatures, rooms, simulation-core]
---
# Training Target rebuild after neighbor work

## Trigger and binary reading

CONFIRMED in the Level5 seed1 capture: between completed1435 and1436,
wall13,32 changes terrain2/health655 to terrain9/health900. Training room1118
remains type7, size16. Its four Dummies1408-1411 and Target1412 are replaced
by Dummies1425-1428 and Target1429 at the same positions. The Target is at
73472,124928. The modern436 control also completes reinforcement13,32 in
that turn through worker440; the work timing already agrees.

STRONGLY INFERRED from fresh whole primary reads of
[004ECDB0](functions/004ECDB0.md), [004E6BC0](functions/004E6BC0.md) and
[004EDFC0](functions/004EDFC0.md): neighbor work visits N/E/S/W, without
coalescing neighbors from the same room. A room with pillars or definition
SpecialWalls has all its registered auto-placed objects deleted, then its
furniture rebuilt. The wall Target arm draws4096 on the world stream at
RoomManager.cpp:2177, call004EECCB, writing the value to object+D4/+D8.
The draw occurs even when replacement furniture has the same slot and type.

The436 callback mirrors only Hatchery rebuilds; reconciliation keeps a
Training Room's existing objects. This omits both the replacement and draw.

## Bounded implementation scope

Neighbor-work Training Room rebuilds are built at hash/save 437 (ADR-0705). Startup,
room additions/removals and capture merges retain their known Target gaps.
The native object animation phase is not represented by this bounded rule;
the implementation mirrors the placement draw and furniture lifecycle.
No captured-turn condition or future seed/input is an implementation rule.

The prior436 baseline and its488-draw prefix through1435 are in
[library-capacity-draws.md](library-capacity-draws.md#built-at436-adr-0704).

## Controlled continuation at437

Baseline436 retains488 ordered draw sites and preseeds through1435. The
candidate with only the neighbor-work rule extends this to516 through1437,
then14 matching draws in1438. Its first different site is1438 draw15:
native Player.cpp:2254 versus modern ambient Player.cpp:2045. The
[keeper refresh clock at 438](keeper-refresh-clock.md) addresses this boundary. Inputs are the
same20 declared1400 environment keys; no new restore input or future fit.
At1436 draw1, seed785837652 with bound4096 returns1044, matching the new
native Target's+D4; the next draw uses seed2256700835 in both runs.
This establishes the draw continuation, not full-state or Heart parity.

The corrected verifier spells the deposit heading site Treasure.cpp:335,
verified by fresh primary [004F3C60](functions/004F3C60.md) and ASCII at
006A2FE0. Earlier external verifiers retain the Treasury.cpp typo. Their
488-draw prefix is unaffected; the correction permits comparison beyond it.

External evidence is add-only under
`F:/DK2Evidence/{studies,builds}/q712-target-neighbor--2026-11-06--001`.
The final paired controls are `baseline436` and `shipped437`; `candidate437` is retained. `native-boundary.json` records
the exact wall/furniture transition; corrected verification files distinguish
the gold-source label correction from any simulation change.

## Verification at 437

The original five-case counterfactual fails three cases and passes two positive
controls. The final focused run passes 31 cases, including a Training Room
without a Target. The ten-suite full run passes 8485 cases with 30 skipped
and zero failures. The Release solution build has zero warnings/errors; Unity
compiles with zero errors and two existing obsolete-API warnings. Logs and TRX files are archived in the external study's `verification/`
directory; the focused receipt is `target-rebuild-final-focused.trx`.
The four production files match the candidate control source byte for byte
(`overlay-sha256.json`). The final `shipped437` run verifies the same prefix
and next boundary. `native-target-draw-proof.json` ties the placement draw to
the captured Target's +D4; its later +D8 value is outside the placement check.
The add-only external archives have SHA-256 inventories in `manifest-final.json`.
