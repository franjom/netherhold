# Creature neighbour query

## Native query (CONFIRMED)

`0x004D2DF0` supplies 1.2 times the mover's radius as a span to `0x004D1170`.
The latter halves the span and scans a tile rectangle: lower edges floor from
16.16; upper edges use `(((position + halfSpan) >> 4) + 0xFFF) >> 12`, inclusive.
It does not reject candidate centres by radial distance. The callback receives
tile-chain entries whose thing class at `+0x12` is 0, creatures, rather than objects.
`0x004D2B80` subsequently checks approximate distance against the combined radii.

The low bounds clamp to 0; upper X clamps to width−1. Upper Y compares against
height but clamps to **width−1**, preserving a native non-square-map quirk.
Inner map-bound guards still reject invalid cells. Evidence: decompile and
assembly `0x004D1182..0x004D129F`, read 2026-10-17.

The query is broadphase, not rigid collision. Arrival suppression and the
overflowing soft-force arithmetic remain as documented in
[navigation and steering](navigation-and-steering.md#32-three-of-the-eight-do-nothing-and-separation-is-read-to-its-store).

## Reimplementation correction

`WorldSteering.CollectNeighbours` previously rejected centres outside the
supplied span by octile distance. For a Wizard's body radius 6880 in 16.16 units,
that discarded contacts between about 0.126 and 0.210 tiles before the summed-radius
check could run. The tile-rectangle query now supplies them (ADR-0500).
The simulation still walks its spawn-order creature list; force contributions
are added modulo 32 bits, so enumeration order does not change this sum.

Level8 seed 1 before-fix traces are `build/level8-movement-trace/ticks.csv`:
Wizard centres 28/31 reach about 0.074 tiles apart on tick 45. This is a modern
reproduction, not an original-game measurement. Some native overlap is legitimate;
this correction does not introduce a minimum centre distance.
