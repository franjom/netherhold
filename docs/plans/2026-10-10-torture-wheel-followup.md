# Classic scene follow-up

Scope: Snapdragon's victims, ordinary obstacles, water speed, campaign parties, torch lighting,
locked-door chains, portal translucency, imp alarm retreat and hatchery laying.

Earlier reviewed fixes: 363aba56 (victims/device steering), e3c67c87 (doors/traps).
The repeated wheel report required the ordinary obstacle query's retained flags,
not rigid mesh collision. Current decisions: ADR-0422–0428; torch setup: unity/README.md.

1. Pin the shipped victim resource and binary device placement with failing tests.
2. Draw the victim's torture pose with the wheel's placement, tilt and rotation;
   keep full animation playback in classic look step 4. Attach simulation positions
   to wall devices without adding saved fields.
3. Supply the complete ordinary object query in tile-head order, preserving retained
   flags and arrival/escape exceptions; verify a complete walk around the floor wheel.
4. Add door/trap art references and scene rendering using existing simulation state;
   check door facing, open/closed meshes and structure visibility against the binary.
5. Start campaigns through the player/party trigger tree; verify Snapdragon's northern party.
6. Apply the confirmed grounded water speed modifier; preserve the original routing bias.
7. Configure Forward+ so terrain chunks can receive local torch lights; compile the adapter.
8. Draw the shipped lock attachment from the existing closed/locked door state.
9. Sort translucent terrain triangles across chunks before drawing portal pillars.
10. Restore the binary's nearby-room imp alarm branch and Call to Arms gate; retain
    the north-bridge/Morgana scenario with a calibrated seed after the movement change.
11. Correct normal egg laying from the binary's pillar exclusion and sub-tile draws;
    keep those coordinates through hatching and presentation. Research roaming separately.
12. Record findings, run solution tests and Level 1–3 replays, sync Unity assemblies,
   review and commit. Live camera A/B remains an owner check.

Creature-to-creature overlap is checked against existing soft separation; do not
replace DK2's movement with rigid mesh collisions.

Verified: Release solution tests, Unity sync/compile, code/binary review and all 60
Level 1–3 replay validations pass. Outcomes and remaining visual checks: docs/HANDOFF.md.
