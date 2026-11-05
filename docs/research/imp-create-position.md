---
systems: [imps-tile-work, keeper-spells]
---
# Create Imp position loss and the second Hatchery wall

## Position evidence

**CONFIRMED (2026-10-18):** two human Create Imp casts preserve fractional
12.12 X/Y in the original game. Hash-274 modern replay truncated them to a tile
and spawned at its centre:

| Packet turn | Packet X/Y and native birth X/Y | Hash-274 birth X/Y |
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
acceptance or proof of the global reinforcement cause.

## Exact-position correction and historical replay diagnostics

The isolated `fixes/imp-cast-position` candidate, hash/save 277, preserves human
cast X/Y through packet decoding, the command and spawning. Existing tile and
Heart callers retain their placement semantics. No state field is added; no
landing, action cadence or chooser rule changes. The presentation click now
casts at the pointer's ground point (tile centre when the pointer has none, nothing
without a pointer tile), so a played cast reaches state 214.

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

| Level / seed | Main win / Imp deaths | Position 277 win / deaths | Position + landing 278 win / deaths |
|---|---|---|---|
| 1 / 1 | 1804 / 0 | 1804 / 0 | 1804 / 0 |
| 1 / 5 | 1804 / 0 | 1804 / 0 | 1804 / 0 |
| 4 / 1 | 6732 / 3 | none by 6804 / 11 | none by 6804 / 13 |
| 4 / 5 | none by 6804 / 35 | none by 6804 / 15 | none by 6804 / 3 |

Exact birth-position agreement establishes this bounded correction. The hash-280
integration branch includes it under ADR-0512; the historical campaign outcomes
above diagnose remaining differences and do not override the native evidence.

## State 214 and height

**CONFIRMED observations:** both native cast births have the same four-snapshot
sequence. For worker 443 the headers are 392–395; for 442 they are 552–555:

| Snapshot relative to birth | State | Z (+0x22) | Floor (+0x26) |
|---:|---:|---:|---:|
| 0 | 214 | 6144 | 4096 |
| 1 | 214 | 4096 | 4096 |
| 2 | 11 | 4096 | 4096 |
| 3 | 10 | 4096 | 4096 |

The sampled physical velocity vectors are zero in all eight records, including
the vertical dword +0x10C; flying flag bit 8 is clear.
**STRONGLY INFERRED:** a fresh whole read of [state 214](functions/00481750.md)
retains its floor-below-Z hold, then its worker landing call; it has no duration
counter in that handler. The samples do not establish a universal two-turn
landing duration; do not replace this path with a fixed timer.
An independent reviewer reread all eight raw records and repeated the guard's
decompile and signed comparison instructions, with the same bounded conclusion.

**STRONGLY INFERRED, static height path:** the creature update's relevant branch
runs [physical movement](functions/004B2030.md) after state dispatch and before
steering. Its first gate is virtual slot +0x88: for a worker,
[the flying test](functions/0048ED40.md) reads flag bit 8, rather than the
object class's mass test. Above the floor and not flying, the pass applies
gravity. The creature's +0x64 response is [0x004B2740](functions/004B2740.md),
which clears vertical velocity when already at/below the floor.

With shipped gravity 240, a zero-velocity free step adds -61440 physical vertical
velocity, yielding -3840 position units after division by 16. A birth only
2048 above its floor can therefore reach it within one pass. The pass can
commit through [the position setter](functions/004B5020.md), whose floor clamp
also clears downward velocity for a creature. Collision and commit conditions
still apply; this arithmetic is not a completed native creation replay.

Two controlled calls of the original physical pass from an earlier live image
faulted at an unmapped instruction fetch. Neither returned; their partial writes
are excluded. The completed live boundary capture below supersedes those probes.

**CONFIRMED, completed native calls:** a session-owned original replay of the
same packet captures worker 442 at entry and return using hardware breakpoints:

| Function | Execution clock | Before | After |
|---|---:|---|---|
| Physical pass | 552 | state 214, Z 6144 | state 214, Z 4096 |
| State 214 handler | 553 | state 214, Z 4096 | state 11, Z 4096 |

Floor is 4096 throughout. Exact X/Y remains 111273/222521; persistent and
temporary velocity vectors are zero at all four boundaries. The correct worker
flying mask, 0x100, is clear, and the live gravity byte is 240. Both functions
return to their recorded caller addresses, with stack delta +4 and no clock
advance within either call. All four memory images have 333611008 bytes with no
short reads. An independent reviewer checked these fields directly in each dump,
including the state object's creature backlink and entry/return registers.

This establishes descent in the physical pass and landing dispatch on the next
execution turn for this cast. Intermediate gravity/collision writes remain a
static reading; the capture does not establish a universal landing duration or
the global reinforcement cause. Native execution clocks above remain distinct
from the snapshot headers in the preceding table.

The complete instruction read of [world slot +0x204](functions/00509010.md)
resolves it to the floor-height query. Action 54 uses that return as cast Z;
the creation callback adds 2048. The initial height above the stationary floor
is therefore 2048, rather than a duration inferred from snapshots.

## Height-driven candidate

The isolated `fixes/imp-creation-landing`, hash/save 278, added a floor-relative
creation fall only for exact point commands. `ImpCreationRules` uses the
simulation's zero-floor convention, retains 214 while above it, accumulates
the byte-sized gravity's vertical vector and clamps below-floor movement.
At floor it runs the landing's 1-to-11 transition; existing 11 then enters 10.
Every Create Imp cast now falls: bought, free, tile-only and heart-default casts
fire the spell's shot and `ImpCreationRules.FromShot` makes the imp from the
shot's death event (ADR-0630, ADR-0635). Receive Imps keeps its previous path. This is the stationary, non-flying worker subset, not general physics.
At floor, `ImpCreationRules` requests effect 29 (Imp Birth Trail) at the landing
point, as [0x00481750](functions/00481750.md) does; it is a Create Imp's only visual
and an observation event, not state.

Both seeds reproduce each cast's five snapshot observations: 214 above floor,
214 at floor, 11, 10, then first walk. X/Y remains exact. The raw prefix through
391 is unchanged under the common fields; no native RNG/whole-trajectory match
is claimed. Tests establish zero gravity holds for twelve updates, lower gravity
accumulates velocity, and a mid-fall save resumes identically. Presentation adds
creation elevation to its existing floor projection and keeps previous height.

The [landing handler](functions/0046EAB0.md) has more than this ordinary
empty-handed fallback. Existing omissions remain: adjacent-door work,
[Workshop blueprint takeover](functions/00464A80.md), Temple handling and
room-specific position adjustments. Heart-interior landing is normally excluded
by point-cast permissions. Do not substitute the general room-drop helper for
these worker branches. Interrupted creation resets its temporary elevation to
the existing grounded-state model; native physical flight outside 214 is not
implemented or claimed. Review caught the hand-interruption case; two failing
pickup regressions now cover it.

The final build has no warnings or errors. All 3589 simulation and 630
presentation tests pass, as do the other solution projects except four Behavior
tests. Each failure also reproduces using main's existing built tests: the
Level1 variable value, Level17 Call to Arms, Level11A bridge/attack scenario,
and the Goblin chicken-eating expectation. They are not corrected in this
candidate. The suite also skips 22 oracle and three capture-dependent cases.

The hash-280 integration branch includes both the exact-position and scoped
height-driven corrections under ADR-0512. These are two distinct corrections;
the duplicate position commit in the landing branch adds no third behavior.
Their native evidence establishes the corrected local behavior without proving
the complete trajectory. Existing landing omissions above remain explicit.

The historical paired replay table above diagnoses remaining simulation
differences. Its outcomes do not reject
the observed birth correction or justify priority changes to restore a win.
The reinforcement cause remains open; compare the earliest differing worker
decision and its actual task inputs.

## State 214 animation, scale and effect (read 2026-11-05)

**STRONGLY INFERRED, animation.** The chooser [0x0048BE50](functions/0048BE50.md) has its own
arm for a worker (definition flag bit 0) whose state is 214: art slot 32 (0x20), immediate
install, advance scalar 4096, the loop bit left set. It is not the state-76 landing arm, which
tests state 76 only. The 214 handler runs the landing and the state is already 11 when the
chooser runs later in the same update (the state write is immediate), so state 76's slot 20/24
choice never applies to a created Imp. The Imp's slot 32 is block 32 of Creatures.kwd (native
slot N is file block N; the art catalogue index is N-1, so 31): `imp-jump(iw)`, type 6, 20
frames at 20 per second in the descriptor (the KMF file itself holds 22 frames), flags 0. Its
mesh rises about 0.39 tile from standing at frame 11 and lands at frame 21. Slot 32 is also
the art of actions 54, 60 (the Imp teleport) and 61, 63 (from the table filler's stores);
none of their literal starters is on the 214 to 11 path.

**STRONGLY INFERRED, how long it shows.** Per the captured clocks the chooser sees state 214 on
one update (the first, whose physical pass drops the Imp) and state 11 on the next. At 20 frames
per second and four turns per second the clip advances about 5 of its 20 frames, then the
standing pose replaces it at once (zero speed, slot 39, which resolves to slot 1 for an Imp
because its slot 39 has no resource). The visible hop is the first quarter of the clip plus the
height change, not a whole jump.

**HYPOTHESIS, the rise.** Creature construction copies its creation position into the previous
position `+0xF6` (0x004872F0, [record](functions/004872A0.md)). The shot's worker arm creates the
creature at shot Z + 0x20, then moves it to Z + 0x800 with the position setter, which does not
touch `+0xF6`; the previous-position refresh runs at the start of the next update. If the draw
interpolates `+0xF6` to `+0x1A` as ours does, the Imp rises from floor + 0x20 to floor + 0x800
over its creation turn, then falls over the next: this would be the owner's "rises from the
floor, hops up, lands". The draw's use of `+0xF6` for a just-created creature, and what is drawn
before the first chooser run installs a clip, were not read.

**STRONGLY INFERRED, scale.** Nothing grows an Imp. Both creation arms (0x004AD7A0, 0x004AFE50)
call [0x0048FD70](functions/0048FD70.md) with 4096, and the handler repeats it at landing; every
chooser run does too while `+0x3CD` is zero (only a path at 0x004AE92C sets it to 1, and 0x004817B1
clears it). The call stores the scalar and writes definition `+0x626` scaled by level and scalar
into `+0x48/+0x4C`, so the Imp keeps its normal size throughout. Grow-in is state 245's.

**STRONGLY INFERRED, effect.** Effect 29 Imp Birth Trail (Effects.kwd): generation type 4, 2
elements per turn for 24 turns, one element, 18 Imp Birth Trail Spark: sprite Sparkle-02g, tint
(40, 255, 180), life 12 to 16 turns, scale 0.4 to 0.8, horizontal and vertical speed to 8192 either way,
friction 327, fading. It is made at landing only (the turn state 214 becomes 11), not during the
rise. Ours requests it (`ImpCreationRules.Step`), the effect model supports type 4 and the Unity
runner feeds it the simulation events; not seen running.

**Built (2026-11-05), presentation only.** `WorldView` gives a worker in state 214 the clip key
`creation_jump` (art catalogue 31, slot 32), played once from the state's entry tick and replaced
when the state leaves 214, as the chooser does. On the creation turn (both heights at the birth
height, no vertical velocity yet) it draws the previous height at floor + 0x20, the rise of the
HYPOTHESIS above. The simulation, its hash and saves are unchanged. A non-worker in 214 keeps the
plain fall. The owner saw the Imp hop up and land in the running game (2026-11-05). That fits this
reading, but nobody has yet compared the built result with the game frame by frame.

**Ours today.** State 214 maps to Activity Idle, so `WorldView.AnimationFor` returns the `idle`
key (slot 20, Imp_Idle1, 171 frames) with no route, and the same in 11 and 10. Height is
`PosZ` over the floor, interpolated from `LastPosZ`; `Begin` sets both to 0x800, so there is no
rise and the Imp is first drawn half a tile up.

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
height/state samples. Their historical campaign-gating wording is superseded
by ADR-0512; the measurements remain diagnostic.
`native_height.py` and the retained fault logs are excluded physical-pass trials.
`imp-birth-landing-20261018/capture_live.py`, `live-boundaries/boundaries.jsonl`
and `verify.py` retain the completed live pairs and raw-image validation. The
capture script's flying metadata mask was corrected from 8 to 0x100 after the
run; both masks yield false in these four images, and neither controls selection.

The independent reviewer separately decoded the packet, read native records,
and repeated the action/cast/creation binary reads. It did not run the modern
controls; their verifier was run by the implementing agent.

Hash-278 artifacts are in `imp-creation-landing-20261018/`: `measure.py` records
the two ordinary 1100-turn probes; `paired_dig.py` uses the same maps/packets,
baseline 274 and acceptance horizons as 277. `verify.py` checks the birth traces,
common prefixes, trigger CSV equality and all eight paired outcomes. Review
independently checked the floor-wrapper instructions, landing limits and code;
the implementing agent ran the modern tests and campaign probes.
