---
systems: [keeper-spells, art-assets, presentation-godot, camera-lighting]
---
# Possession entry stars (Q749)

## Observation and native source

CONFIRMED owner play, 2026-11-18: yellow stars rise from the possessed creature's
feet while the keeper camera flies into it. The earlier remake staging showed none.
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

## Remake fixes and staging

`RoomConstructionParticles` now sign-extends both height words before the
origin conversion, in the generated-effect and generated-element paths. The
previous unsigned conversion placed the stars hundreds of tiles above the target.
Godot now supplies the same existing frame clock used by its other effects and
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
## Capture verification
The inspected navigation-validated `black-knight-entry-nav` (1600x1200)
and `black-knight-entry-wide-nav` (1920x1080) recordings are under
`F:\DK2Evidence\captures\possession-flyin-20261118-001`. Each has 200 frames at 30 fps,
exit 0, no runtime/shader errors, and stable pre/post DLL SHA-256
`0849eb6ee672f4c3c54e8f3de6197cf0f24fe0009113a5b2f19552340e97e48a`.
Adjacent metadata records exact commands, isolated user files and hashes.
Frames 10 and 25 show the camera above the selected creature, a yellow star at its
feet followed by a rising bright dense column, and no visor. Frame 40 is closer
behind the body, with the bright column ended and no visor. Frame 70 is settled
first person with the visor behind the HUD. The reviewer independently inspected
the 4:3 sequence. These establish qualitative remake visibility, not native density,
brightness, exact timing or camera-trajectory parity. The fly-in move itself is
unchanged and remains under the owner's separate binary recheck.
The earlier `black-knight-entry` run had no runtime errors but used a stale
navigation fixture and is excluded from effect verification. Its target tile was
rendered non-solid terrain 8 but NAV 0x08 (solid), so the correct creation gate rejected
it. The replacement was rebuilt from the authentic post-opening save, refreshing
navigation after painting and asserting NAV 0x00 before saving at tick 130. No
production visibility gate was bypassed; earlier evidence remains untouched.
