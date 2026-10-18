# Create Imp position loss and the second Hatchery wall

## Position evidence

**CONFIRMED (2026-10-18):** two human Create Imp casts preserve fractional
12.12 X/Y in the original game. Modern replay truncates them to a tile and
then spawns at its centre:

| Packet turn | Packet X/Y and native birth X/Y | Modern birth X/Y |
|---:|---|---|
| 392 | 122351 / 221220 | 120832 / 223232 |
| 552 | 111273 / 222521 | 112640 / 223232 |

Birth timing and snapshot labels are calibrated in
[imp-observation-clocks.md](imp-observation-clocks.md). Raw packet framing uses
offset 69, 18-byte actions, kind 54, spell 4 and player 3. The first active
native records are workers 443 and 442, both in state 214.

**STRONGLY INFERRED, binary chain:** action [54](functions/005132D0.md) preserves
the record's X/Y in its position vector; [casting](functions/004BBD30.md) passes
that position to the shot factory. The worker arm of
[creation process 1](functions/004AD7A0.md) explicitly restores the shot's X/Y
after creature allocation, enters 214 and places it at shot Z + 0x800. These
paths contain no tile-centre conversion. The independent reviewer reread the
position setter too; shot-initializer internals and the shipped spell/shot
mapping were not independently reread. The observed equality closes the two
tested casts without treating the entire unexamined chain as confirmed.

Modern loss occurs at `PacketLog.At`, case 54: both coordinates are shifted
right 12 into `CreateImpCommand.At`. That command exposes only `TilePos`;
`ImpSupplySystem.Spawn` calls the tile-centred creature factory. The native
position was previously an open question in imp-task-audit.md; it is supplied
by the recorded cast, not an observed random spawn displacement.

## Bounded causal controls

For the second cast, both games target wall `(28,53)`. Each reinforced-wall hit
removes 29 health. The modern three-turn first gap remains the separate
[cadence mismatch](mining-cadence.md#digging-reconciliation-2026-10-18).
The following dates use native raw capture headers and modern post-turn labels:

| Observation | Native | Main 274 | Exact birth X/Y only | X/Y plus diagnostic landing hold | Plus gap and completion controls |
|---|---:|---:|---:|---:|---:|
| First job walk | 556 | 554 | 554 | 556 | 556 |
| First state 14 | 560 | 557 | 558 | 560 | 560 |
| First hit | 562 | 559 | 560 | 562 | 562 |
| Second hit | 566 | 562 | 563 | 565 | 566 |
| Wall to Rock | 596 | 592 | 593 | 595 | 596 |
| Rock to floor | 597 | 593 | 594 | 596 | 597 |
| Release to state 10 | 599 | 599 | 600 | 602 | 599 |
| Next wall chosen | 600 | 600 | 601 | 603 | 600 |

The position-only control changes modern worker 14's birth X/Y and last X/Y,
leaving the chooser unchanged. It adds one walking turn. The landing control
sets its idle timer to 3 at birth, delaying first selection by two turns. This
is a diagnostic substitute, **not** a researched implementation of state 214.
The final column additionally changes this wall's first gap to four and forces
its completion on post-turn 599; it does not implement a general action clock.
Other workers, packet actions and RNG remain unconstrained.

The last control matches these selected local observations, not the native
trajectory. The first Portal still converts on modern 742; state-22 observations
through 1100 remain 389. Position alone leaves that Portal date unchanged and
reduces state-22 observations to 358. These are bounded computations, not campaign
acceptance or proof of the global reinforcement cause. No gameplay fix is merged.

Next preserve exact human cast coordinates through the command and spawn
boundary with a focused regression; keep the existing tile-only callers working.
Research state 214's actual landing conditions before implementing its duration.
Do not replace it with the diagnostic timer or change chooser priorities.

## Reproduction and review

Ignored artifacts are alongside those in imp-observation-clocks.md. `Program.cs`
uses hash-274 assemblies. `WALL_CONTROL=pose`, `pose-aligned-landing` and
`pose-aligned-full` select the three controls above. `none` matches the retained
baseline raw rows, tiles, event stream and trigger CSV byte for byte. Earlier
`arrival`/`full` controls used the old labels and are not native timing matches.
`verify.py` checks all nine retained runs and both packet/birth anchors.

The independent reviewer separately decoded the packet, read native records,
and repeated the action/cast/creation binary reads. It did not run the modern
controls; their verifier was run by the implementing agent.
