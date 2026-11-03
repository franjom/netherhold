# Q712 atomic input controls

## Captured boundary

The older owned Level 5 seed 2 atomic control retains 501 untorn completed-turn frames 1400
through 1900 plus full private memory at 1400, 1615 and 1900. Clock/return-stack,
map 51x55, seed and unchanged packet guards pass; the owned process closed.
Unlike legacy polling headers, these raw frame stamps name the completed
executed turn. [Legacy clock convention](trigger-timer-stamps.md#clock-and-observation-boundary)
still applies to the old cohorts. Do not combine their numbering silently.

This is a new trajectory: first hero 436, born 1513, is not old seed 2 hero 439.
The first party is action 65527/party 0 at point 22 (17,42), under condition 2,
type 39, point 2 rectangle (15,40)..(19,44), player 3 creature count >= 1. Native
Warlock 440 enters that rectangle on 1513; the pre-fix modern pose sequence is
one executed turn later. The [packet clock](packet-playback-clock.md) is a
confirmed input mismatch, not yet a claim that it explains every divergence.

Native Portal Warlock 437 appears 1443 and is absent in the modern control;
the modern due roll rejects the type on budget. Consequently the next hero's
allocated slot/phase differs even with native 1400 free order restored. Room
furniture, active bodies and worker-state/timer history remain partial inputs.
There are no native 254 entries in this bounded window; it is not a replacement
for the historical outcome cohort.

### Later complete-recorder prefix

A separate prefix retains whole completed structured chunks through 1465,
with guarded complete memory at 1466. It is explicitly partial and has no
overall-run success receipt. Comparing executed turns 1400-1465 against the
older atomic CSV gives 1211 older actor rows versus 1056 later rows, with 881
differing rows; the first difference is tag 438 absent in the later run at
1400. The cause is not established. These trajectories are not interchangeable;
keep their poses, navigation inputs and controls distinct.

Evidence: `F:/DK2Evidence/studies/q712-complete-prefix-trajectory-control--2026-11-03--001/`,
`comparison.json` and `sha256-final.json`. This prefix comparison does not
establish a completed full run. The later recorder was deliberately stopped
with 435 complete memory turns (1, 2 and 1400-1832) and 433 structured frames
(1400-1832). Independent review verifies its 1321 retained files, owned-child
closure and unchanged packet. The requested window is incomplete and no
`verified.json` was issued. Evidence:
`F:/DK2Evidence/studies/q712-complete-native-level5-seed2--2026-11-03--001/`.

## Diagnostic restore controls

Production and both diagnostic observers match all four main output hashes.
Controls restore native levels/scan phases/free order, then creature-list order,
then door open/lock values and the retained mesh/cache/free chains. These are
scratch overlays; no production rule is changed. The native mesh has 1718
triangles/862 points; free lists come from the captured image, with explicit
dead-slot/point-anchor canonicalization. Existing active body routes are still
reissued by resync and are not restored by importing the mesh afterwards.

The first navigation import is excluded: its equality guard found exactly
one byte difference, door 1164 at (39,48), native 0 versus modern 16. capture_state
exports open/lock values, but ResyncDoor drops them. Restoring that open door's
Boolean/lock fields makes all grid bytes agree, permitting the guarded import.
Native 004A55F0 uses dword open +10D and lock +111; 004A66F0 clears the open door's
owner nibble. This diagnoses a partial-restore input, not a new door-rule fix.

A legacy-seed 2 control applies the same snapshot at 1400 and 1399. Neither removes
the first hero's executed birth delay: native 1513 versus modern 1514. Earlier
same-header arrival-prefix matches remain historical header-aligned controls;
they are not proof of executed-turn parity. Moving the restore stamp aligns
one Imp phase but leaves active-route/pose differences.

## Evidence

All paths below are under `F:/DK2Evidence/studies/`:

- `q712-native-turn-boundary--2026-11-02--003/`: accepted atomic capture.
- `q712-native-turn-boundary--2026-11-02--001/` and `--002/`: excluded failed attempts, zero usable frames.
- `q712-atomic-snapshot-guard--2026-11-02--001/`: unused missing player pools are optional; all core records and nonzero active-player pools are required.
- `q712-atomic-nav-input-control--2026-11-02--001/`: scratch restore comparisons, including the rejected navigation guard.
- `q712-resync-turn-boundary-control--2026-11-02--001/`: legacy clock intervention and limits.

## Executed-turn clock and missing Portal birth (hash 378)

The corrected packet reader makes hero 436 appear on completed 1513, matching
native; the hash 370 control was one executed turn late. The remaining initial
slot difference is caused by the absent 1443 Portal Warlock, not the birth clock.

Native Imp 444 is already reinforcing in state 22 at 1400 and finishes wall (5,32)
on 1438. Resync reissues that same task through a route; modern state 21 on 1401
replaces native 22, and its reinforcement finishes 1443. The Portal pass precedes
that conversion and the next furniture reconciliation. Native room 1115 has two
Book Cases 64 plus wall Book 58, capacity 4; modern has only the two cases,
capacity 3, and rejects the Warlock on budget. The shared wall predicate 00454230,
rebuild 004EDFC0 and capacity 004E3ED0 readings establish no new rule mismatch.

A declared scratch intervention copies only the captured terrain 9/owner 3 at
(5,32) after completed 1438. It restores capacity 4 before the due Portal pass,
Warlock 437 on 1443, and hero 436's slot/phase 6 on 1513. With the other named partial
inputs, the hero agrees in state, target, position, health and scan phase through
1569; position first differs by (3,4) fixed-point units on 1570. The later target
contrast is bounded by the [action-clock alignment](F:/DK2Evidence/studies/q712-atomic-target-retention-boundary--2026-11-03--001/action-clock-alignment.json):
native action 16 spans completed 1572-1588 and clears at 1589; modern starts
on completed 1571 and its busy hold ends at completed 1588. Both holds span
17 turns. The raw modern `ActionUntilTick` remains 1587 on its internal clock;
no field clear is implied. The retention contrast therefore establishes no new hold-duration
or selector error. The earlier 1570 pose difference, Imp 442 work history and
cast-start offset remain unmatched under Q712/Q719. The born Warlock's position
first differs by one unit on 1464. These remain partial controls, not whole-world parity.

Evidence: `q712-atomic-budget-clock-control--2026-11-02--003/` under the study
root above, with production/observer four-file equality and five successful
runs. Attempt 001 stopped on a provenance-key guard with no usable runs;
attempt 002's original type-word extraction is superseded by its separately
archived byte-type correction. No production furniture or work rule changed.

Earlier controls use hash 370; the new control uses frozen 378. Diagnostic
inputs do not resolve Q719 or close Q712/Q713/Q714.


### Saved bodies and worker resume (Q719)

The [saved-body inspection](F:/DK2Evidence/studies/q712-atomic-body-boundary--2026-11-03--001/)
loads seven retained hash-378 snapshots without advancing simulation. Body
`Radius` and `Pos` are not serialized: the empty-system loader's radius 13107
is a default, not an observed live value. Definition-derived radii 6880 for
Wizard and 4576 for Imp match native. This control establishes no radius or
movement-rule defect; compare retained live and quantized mover inputs instead.

A [frozen-378 CLI-only worker control](F:/DK2Evidence/studies/q712-atomic-work-resume--2026-11-03--001/)
restores captured working state 22, age and heading for Imps 439/442/444.
It also supplies an explicit, future-measured one-tick next-work delay for each
worker, not a native countdown or proposed production rule. Its off-control
matches all five prior output hashes. With the other declared partial inputs,
this restores Library capacity 4 and Warlock 437's birth at 1443 without copying
a future wall tile. In this resume-only control, hero 436's first pose
difference remains at 1570.

Experience matches native through each final work application, but native exits
22 for 10 on that same final hit; the resumed remake exits one turn later:
439 at 1426/1427, 442 at 1444/1445 and 444 at 1438/1439 (native/remake).
The complete animation/action clock is not restored, so neither this terminal
lag nor the supplied delay proves a production work-cadence defect.
[Measured diagnostic inputs](F:/DK2Evidence/studies/q712-work-resume-inputs--2026-11-03--002/)
and the worker study retain the timings and verified manifests.

The subsequent [finish-order control](F:/DK2Evidence/studies/q712-atomic-work-finish-order--2026-11-03--001/)
adds one environment-gated scratch overlay to `ImpTaskSystem.cs`: after tile
work, eagerly recheck the finished reinforcement task through the existing
fallback action-end predicate. It retains the future-measured resume delays
above. Its off-control matches all five outputs; independent review verifies
40 study and 27 build manifest files. In this control, hero 436's first
state/XY/target difference moves to 1620 (previously pose 1570 and state 1588);
its first experience difference is 1589. Imp 442's first XY difference is 1579
and first state difference 1594. Frozen build:
`F:/DK2Evidence/builds/q712-atomic-work-finish-h378--2026-11-03--001/`.

This isolates an order boundary, not the complete art-12/mode-3 action clock.
Action 7 can remain busy after a final hit without cycle flags (`+F1 & 6`);
the three captured workers have flags 5, which satisfy that test. An
unconditional production recheck is therefore not justified. The later
[ordinary action clock](reinforcement-action-clock.md) follows the
complete reviewed rule (ADR-0638/hash 389); its [outcomes](reinforcement-action-clock.md#outcomes-at-hash-389) are verified.
These frozen-378 controls retain their historical overlays. Q719 remains
open; this prototype's extended prefix is not inherited by a later build.
The earlier prefixes and facing controls retain their own partial-input scopes.

### Withdrawal destinations and actual hash-389 inputs (Q719)

The [older frozen-378 withdrawal observer](F:/DK2Evidence/studies/q712-atomic-withdrawal-window--2026-11-03--001/)
matches all five captured-clock prototype output hashes. Imp 442 already has
different goals at 1570: native Heart room 1124's word (17,26), modern Hatchery
(11,25). Two initial waypoints agree, delaying visible pose divergence until
1579. The native completed-turn RNG advances 39 draws, but the within-call
seed is unproved. This establishes unequal destination inputs, not a new
steering or selector defect. The amended manifest verifies 52 files while
explicitly excluding the original invalid manifest self-entry; old bytes remain.
Frozen observer build:
`F:/DK2Evidence/builds/q712-atomic-withdraw-route-h378-observer--2026-11-03--001/`.

The [integrated-389 worker control](F:/DK2Evidence/studies/q712-integrated389-worker-resume--2026-11-03--001/)
uses unchanged frozen-389 simulation with CLI-only native pool/order, doors,
retained navigation and ordinary action-7 restoration. Its input contains no
future-measured work-delay fields and uses no wall pulse. Warlock 437's birth
at 1443 is restored, but the old prototype's 1619 prefix does not carry over:
Imp 442 first differs in pose at 1570 and state at 1572; hero 436 first differs
in state/target at 1588 and pose at 1590. Frozen control build:
`F:/DK2Evidence/builds/q712-integrated389-worker-control--2026-11-03--001/`.

The current lead is actual-389 withdrawal inputs at 1570 (004611D0), then
subsequent hero exposure. These partial controls neither prove a generic 1579
integrator defect nor close Q712/Q719.

### Reporting geometry at hash 389 (Q712)

The independently reviewed per-turn native geometry control uses raw fixed-point
captures for seeds 2/3/7, headers 1401-3400; each seed has 2001 untorn frames including
the 1400 predecessor. Means per run:

| Geometric observation | DK2 | 388 | 389 |
|---|---:|---:|---:|
| Reporting entries within strict D000 of the initial Heart object centre | 12.00 | 8.07 | 6.47 |
| Those entries also within strict one-tile distance of an observed attackable, non-down hero | 9.33 | 3.87 | 2.80 |
| Reporting entries outside that Heart circle | 3.00 | 5.80 | 8.27 |
| Pillar-target hero ticks in both dispatch and Heart circles | 3260.67 | 1584.27 | 1599.53 |

Heart geometry uses (16.5,25.5), while dispatch geometry uses (17.5,26.5).
The hero-tick row is their intersection, not every hero near a current live
Heart. These are geometric bounds, not complete selector eligibility or a
causal explanation: hostility, disguise, visibility, route, Heart lifetime
and within-turn order are not replayed. The sole native censored reporting
episode loses its qualifying Imp row; it is not a timeout.

Evidence under `F:/DK2Evidence/studies/`:
`q712-reporting-heart-geometry-h389--2026-11-03--002/` and
`q712-reporting-candidate-geometry-h389--2026-11-03--001/`.
The first geometry attempt mixed legacy tile/change-log coordinates with
fixed-point per-turn observations and is explicitly excluded. The type-map
history check changes zero target classifications in these 33 runs. The bounded [initial Lair departure](lair-departure.md) correction is built
at 390, with [outcomes verified](lair-departure.md#outcomes-at-hash-390); no new reporting-state rule is
established by these geometric bounds.

### Actual hash-390 prefix and XP lead (Q719/Q734)

The later actual 390 partial-input control uses the corrected initial Lair
departure without other new interventions. Hero 436 matches entity XY through
1625 and state/target through 1626, versus 389's first differences 1590/1588.
Imp 442 still has unequal withdrawal inputs before its 1579 pose difference;
this longer hero prefix is not whole-world parity. Evidence:
`F:/DK2Evidence/studies/q712-integrated390-worker-resume--2026-11-03--001/`.

A separate [route-445 control](F:/DK2Evidence/studies/q712-route445-resume-h390--2026-11-03--001/)
restores captured modeled persistent steering fields at completed 1400; its
five off-control output hashes match the actual-390 control. Imp 445 then
matches XY through 1416, state through 1496 and XP through 1547, versus first
differences at 1401/1427/1430 without that restoration. Hero 436's state/XY/target
first difference instead moves earlier to 1613, shortening the hero prefix. Independent review
verifies 38 study and 26 build entries; the build has the same basename under
`F:/DK2Evidence/builds/`. This is partial route-input restoration, not full
native-body parity or evidence of a generic steering defect.

One earlier deterministic difference is category experience (Q734): hero 436
matches state, target and XY on completed 1571-1589, with equal XP through 1588.
On 1589 both leave 199 for 1, but native gains 37 (703 total) and 390 gains 0 (666).
The category hook 0047A870 runs before the state handler in 00476FC0; the remake
instead awards through a global last promotion sweep, losing the exit award.
This ordering gap remains unimplemented in 390; aggregate Q712 impact is not
established. Raw native rows, paired observer rows and hashes:
`F:/DK2Evidence/studies/q712-combat-xp-boundary-h390--2026-11-03--001/`.

### Facing input boundary (Q719)

The [native-byte facing control](F:/DK2Evidence/studies/q712-native-cast-facing--2026-11-03--001/)
retains four declared previous/current target-point alternatives. At completed
1571 their predicted heading differs from the observed heading; one prior-point
alternative matches at 1572. The captured state-199 entry on 1571 has age 1,
consistent with the existing dispatcher reading. These controls leave the exact
within-turn inputs to 0045FB3E/004B2AE0 unmatched; they establish no new turning
rule or generic entry gate. Worker restoration must also preserve action timing
at 00466390, not just set state 22. `results.json`, `native-facing-inputs.json`
and `native-state-age.json` retain the declared inputs and scope. The original
helper also matches the managed facing result at the declared preceding
completed modern poses (`modern-input-comparison.json`); this checks arithmetic,
not the unmatched native within-turn call path.

## Portal relocation scan reference (Q731)

| Binary address | Pre-edit mismatch, through hash 387 |
|---|---|
| 00459D13-00459D27 in 00459C20 | Copies the post-Entrance XYZ into previous position after the ordinary update stamp. StillJoining changes XY but leaves LastFightScanTileIndex on the portal tile. |
| 0045E9F0 / getter 004B5D40 | The next update compares against the relocated reference. The remake interprets the relocation as a tile crossing and forces a scan/reset. |

The matched Warlock 437 control shows the boundary: at completed turn 1464,
native and modern previous XY already name tile (16,21), but the modern scan
reference still names (16,20). On 1465 the native counter advances from 7 to 8;
modern resets from 7 to 0. The one-unit Y motion difference on 1464 is separate.

The early scratch write is isolated by a guarded reference-only replay. That
intervention reproduces all four prior main files in its off-control and all
1173 actor rows before 1464. At 1464 only the supplied scan tile changes, from
1036 to 1087; the next counter matches native 8 instead of control 0.
Independent review verifies the 45 study and 27 build file hashes.
Evidence: `F:/DK2Evidence/studies/q712-portal-relocation-scan--2026-11-02--001/`.
### Built scope (ADR-0637, hash 388)

`PortalRules.StillJoining` now stamps `LastFightScanTileIndex` from the displaced
position, after its existing clamped XY step. The next ordinary fight update
compares against that relocated reference. The action wait, floor-height
adapter, previous-pose representation and ordinary route arrivals are unchanged.

Hash/save 388 has pin `0xEC99A6F22A85DDC3`. All five address-backed cases pass;
unchanged integrated-387 scratch source fails two and passes three controls.
All 748 archived source files remain unchanged. `proof-raw-bytes.json` records
the raw-byte check and corrects the earlier LF/CRLF hash-label comparison.
The full Simulation suite passes 4362 cases, with no failures; independent
source/proof review has no blocker.

### Paired outcomes (387 and 388)

Each frozen version completes 75 Level 5 and 40 Level 4 resyncs with stored
pickups, the original inputs and original `score.py`. All 230 commands exit 0;
each version matches all four observer-control main hashes. Independent review
verifies the four study manifests (694/371/701/374 files), both 781-file build
manifests, the 29-file verification bundle, 750 frozen source/config hashes,
45 external and 25 installed inputs. Full checks pass Simulation 4362 and Tools
111. Q731 is closed for the bounded relocation-reference write; Q712 remains open.

The historical 1401-3400 capture-header window is retained for comparison with
362 and 378; it is separate from the completed-turn atomic controls above.
387 is the immediate isolation baseline. Historical native/362/378 values are
from [the packet-clock comparison](packet-playback-clock.md#paired-outcomes-377-and-378).
These scores check a binary-backed fix; no constants were tuned to them.

Strict dispatch radius about (17,26), seeds 2/3/7: three native runs versus
fifteen state/generator pairs per remake version. Means per run:

| Hero-ticks inside | DK2 | 362 historical | 378 historical | 387 baseline | 388 fixed |
|---|---:|---:|---:|---:|---:|
| All non-collapsed | 4327.00 | 2720.93 | 2753.87 | 2721.47 | 2710.47 |
| Targeting Heart pillar | 3298.67 | 1607.00 | 1557.47 | 1664.80 | 1593.73 |
| Targeting Imp | 790.00 | 790.60 | 916.53 | 782.60 | 830.60 |

Same cohort; visible transitions, not every within-turn call:

| State-254 transition, mean/run | DK2 | 362 historical | 378 historical | 387 baseline | 388 fixed |
|---|---:|---:|---:|---:|---:|
| Entries | 15.00 | 12.87 | 18.40 | 12.33 | 13.87 |
| Exit to 199 | 3.67 | 0.60 | 1.07 | 1.00 | 1.47 |
| Exit to 203 | 5.00 | 2.60 | 2.27 | 1.53 | 1.80 |
| Exit to 1 | 3.00 | 4.67 | 3.33 | 4.13 | 4.20 |
| Exit to 6 | 2.00 | 4.40 | 10.47 | 4.73 | 4.93 |
| Exit to 8 | 1.00 | 0.53 | 1.20 | 0.87 | 1.40 |
| Fight plus flee (199+203) | 8.67 | 3.20 | 3.33 | 2.53 | 3.27 |

The 388 cohort also has one visible exit to 10 (mean 0.067); the source retains
all transitions. Fight/flee exits increase against 387, while pillar exposure
remains far below native. No further Imp-rule mismatch follows from these totals.

Death scoring uses eight complete native Level 5 states at restore 1400
(excluding 5/8), five at restore 3400 (1/4/6/9/10), and all eight Level 4 states.

| Window / group, mean/run | DK2 | 362 historical | 378 historical | 387 baseline | 388 fixed |
|---|---:|---:|---:|---:|---:|
| L5 1401-3400 / keeper | 13.38 | 9.90 | 10.10 | 10.75 | 8.55 |
| L5 1401-3400 / keeper Imp | 11.12 | 7.35 | 7.33 | 8.35 | 6.00 |
| L5 1401-3400 / hero | 3.75 | 5.10 | 5.25 | 4.92 | 5.38 |
| L5 3401-5000 / keeper | 8.60 | 9.16 | 8.60 | 9.56 | 10.52 |
| L5 3401-5000 / keeper Imp | 5.80 | 7.20 | 6.96 | 7.76 | 8.36 |
| L5 3401-5000 / hero | 5.00 | 4.44 | 4.08 | 3.92 | 4.24 |
| L4 5001-6000 / keeper Imp, exact | 6.500 | 11.925 | 10.075 | 10.200 | 10.325 |

Q714 Heart/terminal restoration is unchanged. The outcome-selected 32-pair
intersection gives exact Level 4 means 10.03125/9.90625/9.40625/9.59375 for
362/378/387/388. The immediate 387-to-388 change worsens by 0.125 all-40 and
0.1875 in that subset. Pre-terminal pairs number 8/0/0 for 362/387/388.
These descriptive subsets establish neither causation nor per-death attribution
to Q714; the apparent 362-to-388 improvement is not a restoration fix.

Evidence under `F:/DK2Evidence/studies/`:
`heart-defence-q712-h387-l5--2026-11-03--001/`, its `l4` sibling and the
corresponding `h388` studies. The 388 Level 5 study retains `state-transitions.json`,
`exposure-summary.json` and both original score outputs; its Level 4 sibling
retains `resync-contamination-analysis.json`. Source/proof and final verification:
`F:/DK2Evidence/builds/q712-portal-scan-verification-h388--2026-11-03--001/`.

Q712, Q714/Q719 restoration, the separate rounding difference and other
state-side relocations remain open. The next diagnostic boundary is the
[actual-389 withdrawal inputs](#withdrawal-destinations-and-actual-hash-389-inputs-q719),
then subsequent hero exposure. Earlier prefixes are
controls of their declared inputs, not whole-world parity.
