# Ordinary room positions (Q721)

Primary source: retail DKII.EXE v1.70, ghidra-queries, 2026-11-01. Read the
[function record](functions/004E4590.md) before the full decompile and ordinary
centre instruction window 004E4701-004E495A. Committed hash 368 is the
pre-edit control for ADR-0625/hash 369.

## Confirmed rule

Ordinary rooms sum their slab coordinates and divide each sum by the count.
The resulting tile is accepted only when it belongs to that exact room. A
miss starts an expanding square spiral: east 1, south 1, west 2, north 2, east 3,
south 3, and so on. The first owned slab supplies CentrePos. The separate
navigation-position search does not change that first centre selection.

The signed divides at 004E4759/004E4760 truncate positive coordinates down;
004E47A5-004E47BA tests the room tag before storing. The fallback starts east
at 004E484C, increments Y at 004E48B2, reverses into X at 004E48BB-004E48D2,
and expands the alternating legs through 004E493A-004E4954. It checks room
membership, rather than selecting the nearest Euclidean tile or a walkable
position. Routing is a later caller decision.

Special room types use first-slab rules instead; their existing simulation
adapters are outside this ordinary-room change. KeeperView already computes
an ordinary centroid but uses a nearest-tile fallback; that separate AI
approximation is recorded in the function record and is not this Level 5 case.

## Mismatches recorded before implementation

| Address | Pre-edit 368 simulation comparison |
|---|---|
| 004E4759-004E4760 | RoomInstance.CentreFieldX/Y use a bounding-box midpoint rather than the slab centroid. |
| 004E47A5-004E47BA | The getters accept that midpoint even when it is outside the room's own tile set. |
| 004E484C-004E4954 | The simulation getters have no expanding spiral fallback. |

The implementation slice is the ordinary position supplied by the
simulation spatial index. Constructor geometry uses typed `TilePos` values;
derived positions need no new saved field, but changed behavior requires
the paired hash/save bump. No room order, RNG draw, path, special-room adapter
or AI selection change is inferred from this field mismatch.

## Captured control

At seed 2 tick 1544, native Training room 1118 and the remake have the same 14
tiles. Their coordinate sums are 218/427; floor centroid (15,30) is a hole.
Native CentrePos is (16,30), the first owned fallback candidate. The pre-edit remake's
bounding-box getter returns (15,30), outside the room.

The verified source hashes, tile lists and values are retained in
`F:/DK2Evidence/studies/q712-cast-control--2026-11-01--001/native-room-centre-case.json`.
Its remake input is the observation-only mode of withdrawal study 001, which
reproduces all four prior main outputs. That study's destination intervention
also rebuilt a mesh and is excluded from destination-only conclusions.

The corrected goal control and its remaining path differences belong to
[the Q712 investigation](fight-scan-initial-phase.md#room-position-mismatch-q721-pre-edit).
They do not establish the aggregate exposure/254 gap's cause.

## Built scope and verification (hash 369)

`RoomIndex` derives the ordinary centroid and exact-component spiral fallback;
`RoomGeometry` passes typed bounds/position to `RoomInstance`, whose shared
field readers use the result. Special-room bounding-box adapters and the AI
nearest-tile fallback remain separate boundaries. No room order, RNG or routing
change is included. Geometry remains derived from the saved tile state.

Hash/save 369 has pin `0xDA38F477882C0B87`. All 11 focused cases pass. Archived
368 source fails seven behavior cases and passes four controls
(`room-old-src/proof.json`); the 730 production source files are unchanged in
that old-source proof. The pre-bump determinism check retained the 368 pin;
the version fold accounts for the new pin. The full build is clean; Simulation
4304 and Presentation 1413 pass. Behavior has 510 passes, five skips and only
the known Level 18 failure. Independent source review approved the freeze.

Q721 is closed for the bounded ordinary-room position rule: the matched
Training room centre is (16,30), owned by that room. Independent final review
verified all eight evidence manifests and 744 frozen source hashes, with no
blocker. [Production outcomes and captured-input controls](fight-scan-initial-phase.md#matched-outcomes-366-through-369)
leave Q712 and Q719 open. The next concrete control is the [resident update-order
input](fight-scan-initial-phase.md#captured-input-and-encounter-controls-369),
whose causal effect has not been tested. Special-room/AI adapters remain outside
this implementation.
