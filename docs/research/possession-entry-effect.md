---
systems: [keeper-spells, art-assets, presentation-godot, camera-lighting]
---
# Possession entry stars (Q749)

## Observation and native source

CONFIRMED owner play, 2026-11-18, registered dataset
`possession-fly-in-skeleton--2026-11-18--001` under `F:\DK2Evidence\captures\visual`:
yellow stars form an expanding floor ring around the Skeleton's feet during entry
(SHOT0000–0005), then a yellow haze lights nearby walls as the camera approaches
(SHOT0010–0012). None remains after landing (SHOT0013–0014). The shots span more
than one cast and do not supply continuous timing.
This observation does not settle the fly-in trajectory; that is being rechecked
separately. This change leaves `PossessionFlight` and `PossessionSession` alone.

STRONGLY INFERRED from the focused DKII.EXE reread and shipped data:

- Keeper spell2 uses shot32 in both research levels. The creature cast
  [004BBF90](functions/004BBF90.md), at004BC160–004BC1E1, makes the shot at the
  target's body position and forces the creature collision. Only shot21 gets
  the additional0x2000 height; Possession does not.
- On the first shot update, [004AB180](functions/004AB180.md) at004AB1AA–004AB1D7
  requests definition+59 through world+38C, at the shot's point and heading.
  The request precedes event40 and the collision process. It does not attach
  this creation effect to the creature or shot. The wrapper duplicates the
  supplied point as the effect's two initial points; see [shot effects](shot-effects.md#1-how-the-simulation-asks-for-an-effect).
- Shot32's collision handler [004AEEA0](functions/004AEEA0.md) sets possession
  via [0048DEA0](functions/0048DEA0.md), installs the player pointer via
  [004BE090](functions/004BE090.md), then posts bridge command11 or12. Thus the
  creation request occurs before the client is told to enter possession,
  rather than being an event raised by the first-person session.
- Shipped creation effect102, Chosen Creature Effect, has generation type4,
  health20, sixteen elements per emission, no Next effect, and a gold light
  (RGB255,200,13; radius6144). It generates element243, Gsparkle, whose health
  is32, mass is−819, air friction655, scale819 and RGB120,80,6. Negative mass
  accelerates the particles upward. Parent20 means twenty view updates,
  with the [effect engine's50ms clock](room-construction-effects.md); surviving
  elements continue after emission stops.
- The raw lower/upper origin words are both65386. The definition loader
  [004358C0](functions/004358C0.md) retains those words; type4 generation
  [004386F0](functions/004386F0.md) reads runtime+39/+3B as signed shorts and
  multiplies by16. They mean−150, hence an origin offset−2400, not+1046176.
  [00435660](functions/00435660.md) adds the drawn height to the current parent
  point. The request stays at that point if the creature later moves.
- Effect102 has zero inner/outer origin radii. The ring comes from element243's
  horizontal launch speed7372, not an authored ring-shaped emission area.
  [00435F20](functions/00435F20.md) calls floor positioning at00435FDA before
  installing launch velocity at00436155 and authored HP afterward. The zero
  elasticity in [00446C70](functions/00446C70.md) therefore clamps the birth
  point to the floor without erasing that later launch. Ordinary movement
  contact still damps or kills according to the element flags.

## Remake fixes and staging

`RoomConstructionParticles` now sign-extends both height words before the
origin conversion, in the generated-effect and generated-element paths. The
previous unsigned conversion placed the stars hundreds of tiles above the target.
The prior remake birth contact ran after launch initialization: it erased the
zero-elasticity stars' horizontal motion and produced a vertical column. Birth
positioning now preserves the subsequent authored launch velocity and HP, including
for below-floor kill/stop flags; movement contact is unchanged. General group-part
and contact-child birth ordering remains outside this correction (Q620).
Godot supplies the same existing frame clock used by its other effects and
possession input; the default draw-list stopwatch must not expire a burst against
wall time while Movie Maker advances the fly-in on fixed frame time.

Real creature casts already submit the native first-tick creation request through
`ShotSystem`. The unpaid `--possess-first-creature` rig does not cast a spell or
make a shot, so it must explicitly project that creation request. The shared
`PossessionEntryEffect` follows spell id2's basic-shot definition and its creation
effect id; it does not hardcode102 or depend on an English spell name. It returns
nothing for absent data or effect0. Its absolute point is `(PosX,PosY,4096+PosZ)`
in the remake's above-floor creature convention, with the viewer as owner,
heading0, gated visibility and no attachments. Staging delivers it before setting
possession, after aligning and placing the ordinary camera; no session-entry
fallback invents a second burst on a real cast.

## Verification boundary

The height regression failed with1050272 instead of1696 at an ordinary floor point.
Shared tests now check signed origin, upward motion, a renamed spell and overridden
shot/effect ids, missing-data behavior, and the actual shipped102/243 star resource.
The installed-data test advances the supplied clock from0 to51/101ms, observes
sixteen then thirty-two stars, and moves the creature to establish no attachment.
The floor regression additionally checks a sixteen-star birth wave at floor height,
outward and upward motion on the next update, and eventual disappearance, plus
birth launch/HP retention for ordinary, floor-kill and floor-stop elements.
## Capture verification

Registered native comparison: `F:\DK2Evidence\captures\visual\`
`possession-fly-in-skeleton--2026-11-18--001`, fifteen ordered files from multiple
casts. SHOT0005 shows a broad floor ring; SHOT0010–0012 show wall haze. File order
alone does not establish one cast's elapsed timeline.

The inspected final `skeleton-entry-final-4x3` (1600x1200) and
`skeleton-entry-final-wide` (1920x1080) recordings are under
`F:\DK2Evidence\captures\possession-entry-review-20261118-001`. Each has 200 frames
at 30fps, exit0, no runtime/shader errors, and stable pre/post DLL SHA-256
`5cfda8d2fd29fea92f5e1fd9056f84ae689bc313dfd0036a613bbefb516b160c`.
Adjacent metadata records exact commands, temporary settings/profile/saves/hiscores
and hashes. The fixture is rebuilt from the authentic post-opening format533 save;
its painted corridor's navigation is refreshed and asserted non-solid before saving.

The parent inspected 4:3 frames10,20,40,70 and wide frames10,40,70; the reviewer
independently inspected frames10,20,40,70 in both formats. Frame10 shows small
feet-level gold stars from above and the ordinary HUD. Frame20's larger gold burst
is partly hidden by the foreground wall; it does not establish a complete visible
ring matching SHOT0005. Frame40 is beneath the roof with the red hovered Skeleton,
level badge, localized status name and ordinary HUD. At frame70 the controlled body
and entry gold are gone, and the possession HUD has appeared.

The supported birth-order correction is built, but native brightness, density,
phase, full ring shape and wall-haze timing remain unverified. The native ring is
broader and wavier than the inspected remake stars. This narrow staged corridor
also exposes the unchanged fly-in crossing the side wall/roof edge; no trajectory
parity is claimed and `PossessionFlight`/`PossessionSession` remain under the
owner's separate recheck. The earlier column and pre-review recordings are retained
as superseded evidence, not current validation.
