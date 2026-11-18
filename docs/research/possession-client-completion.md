---
systems: [camera-lighting, keeper-spells, presentation-godot]
---
# Remaining possession client boundaries

DKII.EXE v1.70, bounded reads on 2026-11-18. Binary findings below are
STRONGLY INFERRED; no new original-game observation was made.

## Virtual room-pillar roof
The parent inspected final format-532
`F:\DK2Evidence\captures\possession-finish-20261118-final-532-001\virtual-room-pillar-fit\f00000180.png`.
Four dark Treasury corner columns are visible, with the ceiling meeting the front
caps. Staging assertions
check that the four corners are non-solid, have virtual room decorations and closed
ceiling, with no pillar object states there. This verifies the virtual-pillar fixture,
not a solid-terrain substitute or an original-game visual match. Shared recording
parameters and provenance are in [HUD verification](possession-hud.md#verification-boundary).
## Object death view

`Camera_SetMode`'s non-class-6 fallback at `0x0044BA14`..`0x0044BA62` keeps
the camera's X/Y, targets the floor there plus `0x100`, keeps yaw masked to
11 bits, and targets pitch 400 and roll 200. It shares the 3000 ms death move,
the zero view-distance target and the red/black transitions with the body arm.

`PossessionObjectDeathView` implements that target; `PossessionSession.RequestObjectDeathView`
runs the existing death move, hold and return without a body. Two tests cover
the target and session transitions. Bridge delivery remains unbuilt: this
client still acquires creatures only, while native chicken/object possession
and its deletion notification are outside the current simulation interface.
The explicit request API does not fabricate an object death from an ordinary exit.

## Listener and lighting search

The complete `0x0044E2A0` was reread. Its ordinary listener point is the camera's
focus at `+0xD68` plus offset at `+0xD74`; modes 2, 10 and 11 take that common
path. Its special point substitution is for mode 18 or map mode 7. Possession
also bypasses the unchanged-point cache through the player's possession word.
The grid remains 11 by 11; its existing implementation is described in
[terrain ambience](sound-effects-continuous.md#4-terrain-ambience-grid-and-first-person).

The adapter has no sound player, so this listener is not connected to playback.
The separate first-person lighting-distance contract remains UNREAD after
checking the camera mode/update records, that listener routine and the existing
`ClassicLightPublisher`/Godot camera inputs. The fade and tile-visibility distances
are implemented independently; this read does not establish a new light cutoff.

## Mouse units and sensitivity

The forwarding chain `0x005B2BB0` → `0x005BC260` → `0x005DD6A0` drains the
axis accumulators, truncating them to integers and clearing their fractional
remainder. `0x005DDA90` forwards buffered axis integer values without scaling;
`0x005DD4A0` optionally doubles them at two thresholds, multiplies by the mouse
object's float `+0x30`, then accumulates both position and relative movement.
`0x005DD360` initializes that multiplier to 1 and the threshold-enable field to 0.

A writer scan of `+0x30` found initialization but no sensitivity setter in the
mouse class. Adjacent routines `0x005DD6D0`, `0x005DD6F0`, `0x005DD730`,
`0x005DD270` and `0x005DD450` do not connect the profile sensitivity value to
that multiplier. The profile-to-device link and calibration against Godot's
screen-relative pixels remain UNREAD. The existing host adaptation is retained;
its distinction from native sensitivity is recorded in
[controls](controls.md#pointer-delta-and-sensitivity). No guessed replacement was made.

## Other boundaries

The cast creation effect, corrected birth ordering and remaining visual differences are recorded
in [Q749](possession-entry-effect.md). The unpaid check projects that native request;
real casts already request it through the shot update. Native density, brightness,
timing and camera-trajectory parity remain unverified. Footstep requests still have
no Godot sound player. Third-person mode 10 remains out of scope.
