# Portal crystal clipping

## Evidence boundary

The owner supplied a remake screenshot of a Goblin intersecting a crystal
(2026-10-23) and requested prevention for creatures generally. The remake's
navigation blocked only the hole; two crossing regressions failed before the fix.

CONFIRMED fresh binary reads: `FUN_004DCD20` deletes portal auto-objects,
marks the centre navigation byte4 and adds a light. `FUN_004DBB90` and
`FUN_004DBBC0` stamp/reset the portal clock, not crystal obstacles.
`FUN_004D12B0` enumerates flagged object obstacles and has a special Heart
tail; no portal-specific crystal geometry is forwarded by that routine.
These paths do not establish how every original-game collision behaves.

CONFIRMED shipped data: object99, Portal Crystal, has Obstacle and width2048,
but Portal/Hero Portal furniture ids are all zero and none of66 shipped THINGS
files places object99. Its native runtime producer, if any, remains UNKNOWN.
No native experiment establishes whether DK2 can exhibit this clipping.

## Explicit remake correction (ADR-0527)

Collision follows the visible crystal primitives rather than inventing a native
object99 producer. The asset provider's `PORTAL0/3/5/8` meshes contain the four
`Portal_Gem` primitives (KMF z points down; render height is −z). Portal floor
is at render height 1.0 with an interior sunk to 0.8, and the gems' bases sit
at 0.72–1.0. Each footprint is the convex hull of its gem below render height
1.85, the floor plus 0.85 tile of body (Mistress, Guard, Knight; most creatures
are shorter), vertices rounded outward to 16.16 units relative to the tile
centre. `PortalCrystalShape` holds them; `PortalCrystalMeshTests` re-derives
each hull from the installed meshes.

| Piece | Former box (tiles) | Hull extent | Hull area / box area | Box corner to hull |
|---|---|---|---|---|
| 0 | 0.536 × 0.566 | 0.536 × 0.566 | 0.212 / 0.303 (70%) | 0.156 |
| 3 | 0.566 × 0.441 | 0.520 × 0.403 | 0.105 / 0.249 (42%) | 0.237 |
| 5 | 0.428 × 0.570 | 0.410 × 0.570 | 0.147 / 0.244 (60%) | 0.197 |
| 8 | 0.317 × 0.429 | 0.317 × 0.429 | 0.094 / 0.136 (69%) | 0.139 |

The leaning tops were not what widened the detour: below body height the gems
already reach all but 0.05 tile of their full-height extents (pieces 3 and 5;
0 and 8 not at all). The former axis-aligned box was: around these diagonal,
irregular gems it stood up to 0.24 tile clear of the crystal, and padding it by
the radius squared its corners. Departures from the hole bowed 0.18–0.50 tile
off the straight line; they now stay within 0.07.

`PortalCrystalBounds` uses the same orthogonal-neighbour piece classifier as
presentation, including narrow fragments and split ownership. A hull is a set
of supporting lines (each edge, a bevel at each vertex, the four axis sides)
with integer unit normals; the mover's radius pushes every line out, giving a
bevelled Minkowski sum within 1.2 radii of the hull. The radius is the body's
crowd radius, half the KWD `0xEA0` footprint (Goblin 0.08 tile, Bile Demon 0.21).
Every integrator alternative tests its full segment (an integer Cyrus–Beck clip),
so high speed, arrival and stuck escape cannot bypass the guard.
An already overlapping body may move along or away from its nearest line. Targets
inside a footprint move out along the nearest line; approach distance breaks ties.
Predictive contacts are added only when the proposed step would cross an expanded
footprint, including arrival snapping and stuck escape. The former
circumscribed circles repelled clear paths yet missed some blocked steps;
three near-wall routes stalled permanently. They are no longer emitted.
This is not a reconstruction of native collision geometry.

The cache is derived from room tiles, rebuilt before target projection and
after room edits/restores. Navigation bytes retain the existing hole rules.
No new saved field; hash 300 (FormatVersion 297) carries the hull footprints.

## Verification

`PortalCrystalMovementTests` covers all four footprints, open gaps, swept
crossings, radius, targets, existing overlap, first queries, room edits,
ownership fragments, Imp/Goblin movement, escape mode and diagonal approaches.
`PortalCrystalArrivalTests` uses installed Goblin data and its actual entrance
offset to check departure in all four directions. A zero-offset synthetic
creature standing in the hole is not the released Goblin's initial position.
The isolated Unity `PortalCrystalMovementProbe` records an original-art Goblin
walking past the portal and checks every segment; the captured run finished
after105 ticks with the former boxes and has not been rerun with the hulls.
Native route parity is not claimed.
`PortalCrystalFootprintTests` checks that a zero-radius segment is blocked exactly when
it enters a hull, that the padding covers the radius and stays within 1.2 radii,
and that five departures from the hole run straight without entering a footprint.
Routes ending 0.05 tile from the hole's edge can hover short of an exact-spot
target with no crystal contact, before and after this change; that is not crystal behaviour.
`PortalCrystalSteeringRegressionTests` checks90 admitted near-wall routes,
requiring completion and testing crystal clearance and solid tiles at every
step. The three reproduced failures complete with predictive contacts; four
focused cases cover clear side motion, early contacts, escape and arrival.
The installed Level11A keeper scenario still bridges, tunnels and engages an
attacked enemy Heart; changed movement yields two valid Call To Arms casts
instead of one. The test validates every cast's spell, ordering, attacked target
and resulting Mistress engagement rather than pinning the incidental count.
