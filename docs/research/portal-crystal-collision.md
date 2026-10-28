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

## Current implementation

ADR-0527 is withdrawn at the owner's request (2026-10-25). Crystal geometry no
longer participates in movement, target projection or steering contacts. The
portal meshes, hole navigation and existing Heart core protection are retained.
The six affected steering/interface files match `3f3811a4^` after normalizing
line endings. The former bounds and footprint calibration tests are removed;
their history is retained in `3305c504`. Hash/save 303 marks the behavior change.

## Verification and limits

`PortalCrystalRollbackTests` has four cases: both Portal kinds permit crossings
and unchanged targets while retaining the hole byte, and normal/escape movement
adds no pillar contacts or deflection. All four failed with the correction
present and pass after removal. The focused steering, Heart and determinism run
passes 156 tests; 14 snapshot cases and four installed-data Goblin departures
also pass.
Tools and Oracle-test builds have no warnings or errors. The updated hash pin
is `0x391985F81BB2BA42`.

The rollback does not preserve the former collision-specific matrix's completion
guarantee. Two of its near-hole routes remained unfinished: a wall at x=7 with
north-to-south travel at x=5, and a wall at y=7 with east-to-west travel at y=5.
These are observed limitations of the restored steering, not a native-behaviour
claim or a reason to invent another movement correction during this rollback.
Generic arrival/snap tests remain in `SteeringIntegratorTests`.

The hash-303 packet sweep is now [measured against hash 302](../experiments/2026-10-25-packet-replay-hash303/README.md).
No Unity visual probe was run for this rollback. Test logs are under
`D:\DK2Evidence\runs\portal-crystal-rollback-2026-10-25`. Before rebuilding, the
hash-302 Release tool was copied and SHA-256 verified (21 files) under
`D:\DK2Evidence\tools\hash302-before-portal-crystal-rollback-2026-10-25`.
