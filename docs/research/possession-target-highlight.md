---
systems: [creatures, camera-lighting, presentation-godot]
---
# First-person target highlight

## Native evidence

CONFIRMED owner play, 2026-11-18, registered dataset
`F:\DK2Evidence\captures\visual\possession-black-knight--2026-11-18--001`:
the first-person highlight names one thing at a time. Melee highlights a Training
Dummy or an enemy door, and one enemy creature among several. Selecting the group
icon highlights a friendly creature, or nothing when no friendly target qualifies.
The mark has the same appearance as the Hand's hover.

## Client and draw chain

STRONGLY INFERRED from the 2026-11-18 binary reread:

- `0x0040BBE0`'s first-person arm reads the possessed creature, the camera focus
  plus offset, and yaw/pitch. Selection byte `+0x13D9` bit0 chooses world slot
  `+0x334` (`0x00515420`, melee); otherwise upper bits equal to `0x18` choose
  `+0x33C` (`0x00515D50`, follower). Other slots use the selected nonzero spell's
  definition and `+0x338`, passing the spell's friendly flag. This is a per-frame
  query, independent of a Fire press, attack recharge or recruitment.
- The one returned id is stored in interface `+0x10BC`. `0x0040ACD0` at
  `0x0040AD6A..0x0040AD8B` publishes it through `0x0044E130` and `0x0044E1C0`;
  both setters store the id at the presentation bridge's `+0x34`.
- `0x0043B220` at `0x0043BE40..0x0043BF39` reads that one id, refuses an
  undrawable thing and applies the existing hover art/red pass. Thus the target
  selection is possession-specific; the drawing mechanism is shared with the Hand.

The pick predicates, distances, aim cone and replacement order retain their homes
in [possessed actions](possession-arms-and-inputs.md) and
[pick heights](possessed-pick-heights.md). No nearest-pointer substitute is used.

## Implementation and limits

`PossessionRules.PreviewTarget` exposes the existing melee, follower and spell
pickers without attacking, stamping recharge or recruiting. `PossessionHighlight`
reads the settled session's current selection and camera aim and names one
`WorldHoverHit`. Godot feeds that hit to its existing `DrawHoverOverlay` before
committing the frame's hover materials.

The preview retains native candidate classes. The adapter can show creatures,
objects, doors and traps; shot, corpse and virtual Heart-pillar highlights remain
unbuilt because those classes have no ordinary hover model mapping. The inherited
picker's documented predicate and map-edge limits remain. The session's settled
camera gate is a remake presentation policy matching its existing possession
render gates; native behavior during the entry transition is not established here.

Tests cover a dummy in reach, an enemy door, exactly one of several enemies as aim
changes, a friendly group target and no qualifying group target. A hash-neutrality
test pins that preview does not attack, stamp or recruit. The inspected target and native comparison are recorded in
[adapter capture comparison](possession-foreground.md#adapter-capture-comparison).
Other drawable classes and transition-time behavior retain the limits above.

## Shared visibility policy

`PossessionDrawVisibility` owns per-entity mask, hidden-body and object-alpha
decisions; Godot uploads the existing tile mask and uses those decisions.
`WorldEffectVisibility` owns the explored/non-solid/owner-only request filter.
This moves existing decisions out of the adapter under ADR-0805 without claiming
new native behavior. Tests include hidden and visible entities, off-map mask
indices, disabled masks, zero alpha, solid effects and viewer-only effects.
