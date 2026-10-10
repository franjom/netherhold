# Torture wheel attachment and collision

Scope: fix Snapdragon's standing victims, ignored wheel obstacle and missing doors/traps.

Completed and reviewed: 363aba56 (victims/device steering), e3c67c87 (doors/traps).
Release solution tests and Unity sync/compile pass. All 60 Level 1–3 replay outcomes,
win ticks and event files match version 215. Limits: ADR-0420 and ADR-0421.

1. Pin the shipped victim resource and binary device placement with failing tests.
2. Draw the victim's torture pose with the wheel's placement, tilt and rotation;
   keep full animation playback in classic look step 4. Attach simulation positions
   to wall devices without adding saved fields.
3. Supply wheel/chair continuous obstacles to existing steering, preserving the
   original soft repulsion and arrival exceptions. Other furniture remains deferred.
4. Add door/trap art references and scene rendering using existing simulation state;
   check door facing, open/closed meshes and structure visibility against the binary.
5. Record binary findings, add ADRs and hash/save version, run solution tests,
   Level 1–3 replays, Unity sync/compile, review, and commit the fixes separately.

Creature-to-creature overlap is checked against existing soft separation; do not
replace DK2's movement with rigid mesh collisions.
