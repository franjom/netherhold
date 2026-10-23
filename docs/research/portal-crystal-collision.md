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
`Portal_Gem` primitives. Their horizontal bounds, relative to their tile centres,
are rounded outward to16.16 units:

| Piece | Min X,Y | Max X,Y |
|---|---|---|
| 0 | 18160,−2851 | 53288,34250 |
| 3 | 18140,3892 | 55234,32768 |
| 5 | −44421,−47743 | −16384,−10367 |
| 8 | −43412,−47947 | −22642,−19831 |

`PortalCrystalBounds` uses the same orthogonal-neighbour piece classifier as
presentation, including narrow fragments and split ownership. Bounds expand
by the mover's footprint radius. Every integrator alternative tests its full
segment, so high speed, arrival and stuck escape cannot bypass the guard.
An already overlapping body may move toward its nearest exit face. Targets
inside bounds move to the nearest face; approach distance breaks ties.
Predictive contacts are added only when the proposed step would cross an expanded
crystal rectangle, including arrival snapping and stuck escape. The former
circumscribed circles repelled clear paths yet missed some blocked steps;
three near-wall routes stalled permanently. They are no longer emitted.
This conservative horizontal projection also covers leaning crystal tops;
it is not a reconstruction of native collision geometry.

The cache is derived from room tiles, rebuilt before target projection and
after room edits/restores. Navigation bytes retain the existing hole rules.
No new saved field; hash/save294 includes the repaired movement behavior.

## Verification

`PortalCrystalMovementTests` covers all four footprints, open gaps, swept
crossings, radius, targets, existing overlap, first queries, room edits,
ownership fragments, Imp/Goblin movement, escape mode and diagonal approaches.
`PortalCrystalArrivalTests` uses installed Goblin data and its actual entrance
offset to check departure in all four directions. A zero-offset synthetic
creature standing in the hole is not the released Goblin's initial position.
The isolated Unity `PortalCrystalMovementProbe` records an original-art Goblin
walking past the portal and checks every segment; the captured run finished
after105 ticks. Native route parity is not claimed.
`PortalCrystalSteeringRegressionTests` checks90 admitted near-wall routes,
requiring completion and testing crystal clearance and solid tiles at every
step. The three reproduced failures complete with predictive contacts; four
focused cases cover clear side motion, early contacts, escape and arrival.
The installed Level11A keeper scenario still bridges, tunnels and engages an
attacked enemy Heart; changed movement yields two valid Call To Arms casts
instead of one. The test validates every cast's spell, ordering, attacked target
and resulting Mistress engagement rather than pinning the incidental count.
