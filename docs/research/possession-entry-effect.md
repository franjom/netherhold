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
  The request precedes event40 and the collision process. The wrapper duplicates
  the supplied point as both initial points; attachment needs the separate tag
  path established below, not merely the absence of an id argument.
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

## Creation attachment trace
STRONGLY INFERRED, fresh shot32-path read: [the constructor](functions/00434700.md)
explicitly clears attachment tag+133 and selector+135. The request follows
[00509580](functions/00509580.md), [0050E490](functions/0050E490.md),
[00440420](functions/00440420.md) and [00440350](functions/00440350.md) into
[the initializer](functions/004347E0.md); none installs that tag. The creation
handle in [Shot_Update](functions/004AB180.md) is local and receives only optional
carried velocity, not a creature tie. [The update](functions/00434E80.md) follows
things only for nonzero+133, with no effect102-specific automatic attachment.
This traced creation/update path therefore leaves102 at its copied point; it is
not a whole-image proof that no indirect writer could ever affect any effect.

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
sixteen then thirty-two stars, and moves the creature to verify the remake
retains its copied request point. The native premise is the traced tag path above.
The floor regression additionally checks a sixteen-star birth wave at floor height,
outward and upward motion on the next update, and eventual disappearance, plus
birth launch/HP retention for ordinary, floor-kill and floor-stop elements.
## Capture verification
Native comparison: registered dataset
`F:\DK2Evidence\captures\visual\possession-entry-and-leave-sequences--2026-11-19--001`.
The parent and reviewer compared SHOT0039's floor ring, SHOT0051's high stars and
SHOT0077's return. Earlier Skeleton entry captures remain registered as
`possession-fly-in-skeleton--2026-11-18--001`; file order across multiple casts
alone does not establish elapsed timing.
Current remake recordings are under
`F:\DK2Evidence\captures\possession-entry-leave-20261119-001`:
| Recording | Size | Inspected result |
|---|---|---|
| `aligned-final` | 1600x1200 | Frame20: bright rotated floor ring;40/50: diffuse high cloud;70: faint stars survive landing. |
| `side-final` | 1600x1200 | Frame20: bright floor ring;50: rotated high cloud. |
| `front-final` | 1600x1200 | Frame20: bright floor ring;40: high cloud;50: no stars in view after the camera swings behind. |
| `aligned-wide-final` | 1920x1080 | Frame40: bright ring/high stars, ordinary HUD and badge;99: ordinary HUD and roof;110: body and badge. |
Each recording has260 PNG frames at30fps, exit0 and no logged runtime/shader
errors. All have stable pre/post Godot DLL SHA-256
`da237ae3f876a4703c8b390a1107957b6df23f8a9d8db057b1df92474bdfe61f`.
Adjacent metadata records exact commands and temporary settings/profile/saves/hiscores.
The staged format533 saves use an open room and three creature headings; navigation
is refreshed and asserted non-solid before saving. These are remake fixtures,
not recreations of the owner's dungeon. The previous narrow-corridor recordings
are superseded for the strength comparison because a foreground wall obscured the ring.
The parent inspected the listed angle views; the reviewer also inspected aligned
frames20,40,50,70,99,100,110,150. The bright floor ring and later high cloud are
present, and rotation now produces tilted stars. Brightness and size are visually
plausible against the native files; they have not been quantitatively matched.
The native post-landing disappearance remains unmatched: aligned frame70 has
faint gold stars against the upper ceiling after the possession HUD appears.
No stars are visible in the inspected return frames.
The aligned input journal locates the first return draw at frame99, tick143
(Leave queued at92 and applied at98). It has the ordinary HUD and roof, but no
visible body or badge. The head appears at100, and body and badge at110. Shared
rules and the per-frame adapter restore model draw eligibility immediately; that
does not establish visibility when the camera is initially inside its mesh.
Exact first-frame body/badge parity therefore remains unverified. No forced
formerly-possessed badge or camera adjustment is added.
The owner-confirmed camera move is recorded in
[client completion](possession-client-completion.md#entry-and-return-observations).
## Later floating stars (F3)
CONFIRMED owner sequences, 2026-11-19, registered dataset
`possession-entry-and-leave-sequences--2026-11-19--001`: the bright foot ring
lights the floor (SHOT0038-0043,0060-0069); later stars rise high around the
creature (SHOT0050-0053,0070-0073). Nothing remains after landing or on leaving.
STRONGLY INFERRED from shipped definitions and the fresh function rereads:
102 has only generated id243, with zero next/death effects;243 likewise has no
next/death effect. Shot32 has only creation102, with all other effect ids zero,
and [the collision handler](functions/004AEEA0.md) introduces no second effect.
The high stars are therefore the same negative-mass243 particles; their flag8
RGB fade explains the darker older stars. Fixed scale819 with Gsparkle's4096
resource width yields a full billboard width0.2tile; there is no data-supported
brightness or size multiplier for a second phase.
The authored light descriptor has flags0, but [the native loader](functions/004358C0.md)
ORs in flag1 whenever RGB is nonzero. The [factory](functions/00439880.md) therefore
creates102's gold point light. The remake's nonzero-RGB fallback mirrors that
loader normalization; treating the raw flags as final runtime flags is incorrect.
Dynamic elements [constructed with mode1](functions/00435E70.md) go through
[the tile draw arm](functions/0043CC30.md), renderer+E8, and [00599C60](functions/00599C60.md).
It passes current heading+E2 twice through [0059B880](functions/0059B880.md)
and [0059C360](functions/0059C360.md). [0057F1E0](functions/0057F1E0.md)
therefore interpolates equal angles; [0057F3D0](functions/0057F3D0.md) rotates
the quad clockwise in the camera plane. The remake now applies that current
heading, restoring the tilted stars; roll and previous heading do not supply
this angle. A regression distinguishes the current heading from nonzero roll and
a different previous heading.
The bounded draw read found no possession-mode suppression in this dynamic
particle arm. The definition refresh [00436600](functions/00436600.md), definition
constructor [00436630](functions/00436630.md), and separate static sprite path
[00599960](functions/00599960.md)/[0059BB80](functions/0059BB80.md) do not settle
why native post-landing stars are absent.
The post-landing residual is recorded in [capture verification](#capture-verification).
The authored lifetimes permit survivors; a native cancellation or visibility cause
has not been traced. No unsupported landing-hide rule is added.
