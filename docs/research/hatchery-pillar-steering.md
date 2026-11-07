---
systems: [rooms, creatures, simulation-core]
---
# Hatchery pillar steering

## Native boundary

CONFIRMED from the Level5 seed1 capture: Warlock438 has matching entity and
body motion through completed1439. At1440 native reverses sideways while
production440 continues forward. Native pillar1353 (type78) stands at
50176,107520; the completed1439 creature stands at50679,106994.
All four Hatchery pillars are already present at1400. The diagnostic440
collector returns the Coop35 but omits these virtual presentation pillars.

STRONGLY INFERRED from fresh whole reads: [pillar placement](functions/004EF1F0.md)
creates objects at qualifying3x3 corners, using inward offsets and a physical
Pillar-flag suppression gate ([finder](functions/00453760.md));
[type selection](functions/004E4360.md) maps Hatchery to78.
[Obstacle enumeration](functions/004D12B0.md) forwards eligible object
positions and half-widths in tile-chain order, retaining its solid flag toggle.
[Contact](functions/004D3020.md) contributes a shove or slide when centres
are noncoincident and within combined radii; [repel](functions/004D31D0.md)
has the existing arrival and escape exemptions.
The shipped78 has flags0x44000 and width1634: half-width13072 in16.16.
The relevant query forwards Coop35 solid, then pillar78 soft.

## Controls and implementation boundary

Evidence is add-only under
`F:/DK2Evidence/studies/q712-pillar-repel--2026-11-07--001`.
`frozen440-obstacles` is unchanged production440 with observation-only telemetry.
It retains the preceding1510-draw prefix through1500; see
[counted reading clock](counted-reading-clock.md).

`physical-pillars440` supplies the four actual1400 pillars through a declared
23rd diagnostic input, preserving all22 prior inputs. This moves438's first
state/XY difference from1440 to1492. A missing site-map entry at1505 is the
existing variant2 stand-point draw, not a behavior difference: the fresh
[chooser](functions/00475A20.md) read identifies random2 at ResearchState.cpp:89.
The mapped physical control matches1600 ordered sites/preseeds through1506.
At1507 draw4 native next calls Treasure.cpp:132 while modern calls
PortalChoiceRules.Pick (bound409600, preseed876633829). The fresh [Portal selector](functions/004DBF90.md) read and source-path bytes
identify that call as Entrance.cpp:684, so the sites really differ. Eligibility
and the cause were the next investigation; the [room-manager order control](room-manager-order.md)
reproduces the ordering boundary without changing Portal timing.

Hash/save441 (ADR-0709) adds virtual Hatchery78 geometry to the ordinary steering query,
including half-width, Obstacle/SolidObstacle flags, corner support and active
physical-pillar suppression. Virtual entries share the row-major flag stream
with stored objects. No physical objects or world draws are created.
Only Hatchery pillars are in this slice. Other room pillars and native movement
gate bits remain separate.

Exact relative order against a non-Pillar stored obstacle on the same corner
tile is not retained by virtual furniture. The candidate treats it as older
than stored occupants; this is an adapter assumption, not a newly read native
rule. General mixed rebuild history needs physical object/list restoration.
`delivered441-pillars-v2-verification.json` verifies1600 ordered sites/preseeds
through completed1506, plus the first3 draws at1507. It inherits all22 input
key/value pairs unchanged. `delivered441-pillars-v2-components-through1506.json`
verifies107/107 state/XY samples each for439,441,447,449 and455;438 matches
92/107, with its first remaining XY difference at1492. The physical and virtual
controls agree on mapped sites/preseeds and measured components; inserted
objects change diagnostic modern bird IDs, so raw logs are not byte-identical.

Source/payload are frozen under
`F:/DK2Evidence/builds/q712-pillar-repel--2026-11-07--001` with `source-v2.zip`,
`payload-v2/`, `manifest-v2.json` and `frozen440-source.zip`. The study's
`source-input-proof-v2.json` records production-overlay identity and
records the2 inherited observation-only runtime differences. Scripts are in
`final-scripts-v2/`. `native438-boundary.json` retains native body fields and
1439 query tile chains. Physical pillar creation/destruction and lifetime
remain outside this virtual geometry correction.

## Regression evidence

Twelve focused cases fail6/pass6 before the change: four inward corner poses
and held/carried replacement cases detect missing geometry; data gates,
physical replacement and interior controls already pass. The reviewed collector passes13 new cases and14 existing steering cases;
the extra case explicitly pins the HYPOTHESIS same-tile ordering adapter.
The final ten-project suite passes 8531 cases with 30 skipped and zero failures.
TRX files are named `pillar-steering-full-final.trx` under each project's TestResults.
Unity compiles with zero errors and the two existing obsolete-API warnings.
Final test summaries and receipts are in the external study's `verification/` directory.

The separate before/after meal runs preserve hunger719, both first meals720,
375 healing and the egg-clock assertions. Only incidental selected birds
change from7/5 to21/15 (creatures1/2); scenario expectations pin the measured
new IDs. These are remake regression outcomes, not native measurements.
Receipts are `baseline440-meal/` and `candidate441-meal/`.

Draw agreement is bounded continuation, not full-state or Heart-outcome parity.
