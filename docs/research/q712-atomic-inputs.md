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
Warlock 440 enters that rectangle on 1513; the same modern pose sequence is
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

Runtime/config source remains hash370. Diagnostic inputs do not establish
whole-world parity, resolve Q719, or close Q712/Q713/Q714.
