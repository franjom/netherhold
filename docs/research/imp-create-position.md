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

Main hash-274 loss occurs at `PacketLog.At`, case 54: both coordinates are shifted
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

## Exact-position candidate and acceptance

The isolated `fixes/imp-cast-position` candidate, hash/save 277, preserves human
cast X/Y through packet decoding, the command and spawning. Existing tile and
Heart callers retain their placement semantics. No state field is added; no
landing, action cadence or chooser rule changes. The current presentation path
still issues its existing Heart cast; this change does not add point aiming to UI.

Two regressions failed before correction: same-tile points decoded to equal
commands, and exact casts spawned at the tile centre. All 79 tools tests and
3580 simulation tests pass, including signed/outside-map rejection, permissions,
mana and tile-centre compatibility. The whole solution builds without warnings
or errors. The reviewer found no implementation correctness blocker.

Both Level4 seeds reproduce both original birth positions. Seed 1's raw prefix
through 391 is unchanged; diagnostic trigger CSVs equal the first 1100 CLI rows.
This candidate preserves every recorded human cast, unlike the earlier control
that changes only worker 14's birth. Paired packet acceptance retains the same
entity maps and seeds; baseline results are reused from hash 274. Reusing the
Level4 seed-1 entity map for seed 5 remains a limitation.

| Level / seed | Main win / Imp deaths | Exact-position candidate win / deaths |
|---|---|---|
| 1 / 1 | 1804 / 0 | 1804 / 0 |
| 1 / 5 | 1804 / 0 | 1804 / 0 |
| 4 / 1 | 6732 / 3 | none by 6804 / 11 |
| 4 / 5 | none by 6804 / 35 | none by 6804 / 15 |

**Gameplay merge deferred.** Exact birth-position agreement establishes the
bounded correction; campaign acceptance still fails. No priority workaround or
guessed landing wait is included. Preserve this candidate separately.

## Next boundary: state 214 and height

**CONFIRMED observations:** both native cast births have the same four-snapshot
sequence. For worker 443 the headers are 392–395; for 442 they are 552–555:

| Snapshot relative to birth | State | Z (+0x22) | Floor (+0x26) |
|---:|---:|---:|---:|
| 0 | 214 | 6144 | 4096 |
| 1 | 214 | 4096 | 4096 |
| 2 | 11 | 4096 | 4096 |
| 3 | 10 | 4096 | 4096 |

The sampled vertical-velocity dword +0x10C is zero in all eight records.
**STRONGLY INFERRED:** a fresh whole read of [state 214](functions/00481750.md)
retains its floor-below-Z hold, then its worker landing call; it has no duration
counter in that handler. The samples do not establish the intermediate height
writer or a universal two-turn landing duration. Trace that writer and landing
dispatch before implementing this path; do not replace them with a fixed timer.
An independent reviewer reread all eight raw records and repeated the guard's
decompile and signed comparison instructions, with the same bounded conclusion.

## Reproduction and review

Ignored artifacts are alongside those in imp-observation-clocks.md. `Program.cs`
uses hash-274 assemblies. `WALL_CONTROL=pose`, `pose-aligned-landing` and
`pose-aligned-full` select the three controls above. `none` matches the retained
baseline raw rows, tiles, event stream and trigger CSV byte for byte. Earlier
`arrival`/`full` controls used the old labels and are not native timing matches.
`verify.py` checks all nine retained runs and both packet/birth anchors.

Candidate artifacts: `imp-cast-position-20261018/` in the same ignored parent.
`Program.cs`/`Probe.csproj` record ordinary hash-277 packet runs; `paired_dig.py`
reuses baseline results and runs four candidate replays. Its `verify.py` checks
all eight outcomes, exact birth positions, prefix agreement and the native
height/state samples. Neither verifier treats the mixed campaign outcomes as
acceptance.

The independent reviewer separately decoded the packet, read native records,
and repeated the action/cast/creation binary reads. It did not run the modern
controls; their verifier was run by the implementing agent.
