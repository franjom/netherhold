# Q712 atomic input controls

## Captured boundary

A new owned Level 5 seed 2 replay retains 501 untorn completed-turn frames 1400
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
1569; position first differs by (3,4) fixed-point units on 1570. On 1588 native retains
Imp 442 as target while ours drops to idle. The born Warlock's position first differs
by one unit on 1464. These remain partial controls, not whole-world parity.

Evidence: `q712-atomic-budget-clock-control--2026-11-02--003/` under the study
root above, with production/observer four-file equality and five successful
runs. Attempt 001 stopped on a provenance-key guard with no usable runs;
attempt 002's original type-word extraction is superseded by its separately
archived byte-type correction. No production furniture or work rule changed.

Earlier controls use hash 370; the new control uses frozen 378. Diagnostic
inputs do not resolve Q719 or close Q712/Q713/Q714.


## Portal relocation scan reference (Q731)

| Binary address | Existing remake mismatch after hash 378 |
|---|---|
| 00459D13-00459D27 in 00459C20 | Copies the post-Entrance XYZ into previous position after the ordinary update stamp. StillJoining changes XY but leaves LastFightScanTileIndex on the portal tile. |
| 0045E9F0 / getter 004B5D40 | The next update compares against the relocated reference. The remake interprets the relocation as a tile crossing and forces a scan/reset. |

The matched Warlock 437 control shows the boundary: at completed turn 1464,
native and modern previous XY already name tile (16,21), but the modern scan
reference still names (16,20). On 1465 the native counter advances from 7 to 8;
modern resets from 7 to 0. The one-unit Y motion difference on 1464 is separate.

Two address-backed scratch tests fail with old source; three controls pass.
The proposed scratch write passes all five. A guarded reference-only replay
intervention reproduces all four prior main files in its off-control and all
1173 actor rows before 1464. At 1464 only the supplied scan tile changes, from
1036 to 1087; the next counter matches native 8 instead of control 0.
Independent review verifies the 45 study and 27 build file hashes.
Evidence: `F:/DK2Evidence/studies/q712-portal-relocation-scan--2026-11-02--001/`.
No production Portal rule changes yet. This bounded control does not settle
ordinary route arrivals or the separate rounding and Q719 input limits.
