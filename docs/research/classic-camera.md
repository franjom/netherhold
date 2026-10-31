# Classic dungeon camera

Source: retail v1.70 DKII.EXE, Ghidra 12.1.4, 2026-10-14;
shipped GlobalVariables.kwd and Level1/7/8 player records.

## 1. Authored settings

CONFIRMED data: the THINGS camera payload is 84 bytes. The existing CameraThing
reader exposes its fixed-point lens, zoom and bounds plus 2048-unit angles.
GlobalVariables.kwd has the camera records; ordinary dungeon preset id3 has
position Z4096, zoom40960, minimum16384, maximum53248, lens8192, pitch220,
yaw0, roll0 and flags0x26. Preset4 has zoom118784, bounds16384..212992,
lens40960 and pitch240. Preset5's pitch is zero.

STRONGLY INFERRED binary: the camera parser 0x00570A20 reads the 84-byte record
and calls 0x0056F900, which copies its fields into a 152-byte intermediate
record; its camera id uses the payload's low byte, not the retained word.
The level loader 0x005215F0 passes it to 0x00449A30; that installer
accepts ids1..18 and stores them at camera+0x28+id*0x98. Its lower graphics
quality arm reduces the maximum view distance; it does not change zoom bounds.
Mode selection 0x0044ACE0 obtains the corresponding intermediate preset.
The commit 0x0044BD70 transfers target position, zoom, angles, distance and lens
to the active view; unrelated possession flags are documented in terrain-lattice.md.

## 2. Projection and camera offset

STRONGLY INFERRED from whole decompiles and the instruction range
0x00598CD6..0x00598DE2: the ordinary renderer 0x00598880 uses the active camera
matrix and focus position, with an optical-axis offset of
`-(ZoomRaw /4096) *640 /fullScreenWidth`. It transposes the view matrix before
transforming that offset into world space. Focal length is the signed fixed
product of LensRaw and `320*4096`, converted back to float pixels.
For the shipped ordinary profile, focal length is therefore 640 pixels.
Before adding the engine offset it converts focus to `(gameX,gameY,-gameZ)`;
converting that engine frame to Unity `(x,-z,-y)` preserves positive focus height.

0x0058A6F0 scales projection by focal length divided by each viewport extent.
CONFIRMED (whole decompile and instructions rechecked 2026-10-16):
0x0044D550 supplies the visibility distance as the larger of twice active
zoom (+0xD80) and active view distance (+0xD8A), converted to world units.
`NativeViewDistance` shares this value with the camera far clip; using the
authored distance alone clipped visible terrain out of the light tables.
An equivalent perspective camera has vertical FOV
`2*atan(drawableViewportHeight /(2*focalLengthPixels))`. Full screen width
and drawable viewport are distinct inputs when panels reduce the view.
At a full 640x480 viewport preset3's offset length is10 tiles and vertical
FOV is about41.112 degrees; at full1280x720 they are5 tiles and about58.716.
These values are conversion consequences, not settings invented for Unity.
The Unity runner sets `ClassicCamera.ReferenceScreenWidth` to1600, matching the
owner's1600x1200 recording. Resolution/aspect adaptation is in section8.
STRONGLY INFERRED (read 2026-10-20; within a row of the owner's captures): DK2's view axis meets the screen at the
middle of the 3D viewport (that the projection is centred on the viewport is inferred), and [the panel height setter](functions/00417170.md) ends that viewport
0x98 below the bottom panel's top (at graphics detail 0x00759AEC below 4 the rect is instead
(0, 0x68, 0xA00, top + 0x14)). Expanded
(top 0x5B4) it ends at 1612 of 1920, so the axis is 806/1920 = 504 rows of 1200 from the top; collapsed
(top 0x6EC) the rect ends at 1924, past the bottom; that the view clamps it and the axis is then
the middle is inferred. The captures put the axis at x 811 against the rule's 800; that 11 px is
unexplained. The captures had measured it first:
- Level 1 from the start view (SHOT0084) and at maximum zoom-out (SHOT0085), no pan: eight floor
  markers move along lines that meet at (811, 501), each within 3-5 px, the slow centre marker 11 px.
  Zooming moves the camera along its view axis, so that point is where the axis meets the screen.
- A joint fit of both shots (pitch 51.33°, focal 640) puts the centre at y 505, rms 1.3 px. With the
  centre at 600 it is 10.6 px, even with pitch free. A centred projection with the camera tilted off
  the zoom direction needs focal 409 to fit.
- SHOT0057, at maximum zoom (owner): with the centre at 505, focal 640 and the rule's height there
  (4.06 tiles) fit with rms 0.7 px. The earlier "9% gap" came from assuming the middle.
- The projected marker-scale ratio between the two shots is about1.47. This does not measure
  the raw zoom ratio: perspective scale depends on each marker's depth relative to the focus.
  Elevated wall markers therefore cannot establish an initial zoom different from40960.

Every Camera_SetMode branch (0x0044B04F, 0x0044B2FF, 0x0044B4F0, 0x0044B807) sets the bound +0xD54
from the preset's MaxZoom (53248; no shipped level overrides preset 3). The renderer reads
`-(zoom/4096)·640/[0x00756EEC]` at 0x00598D10..0x00598D22, where 0x00756EEC is the screen width
(Camera_Update 0x0044C075 builds a rect from it and 0x00756EF0). The focal length is lens·320 = 640
(0x00598D26..0x00598DE2), and 0x0058A6F0 divides it by the viewport extents. The camera rect is the
whole screen (0x004498F0, called from 0x0043ABD8 and 0x00442F1B); the shortened viewport is the
renderer's own, set through the view's virtual +0x34 by 0x00417170. The rect (left, top, right, bottom) = (0, H/8, W, 7H/8)
that 0x0044D320 blends in over 2 s is a letterbox; its guards and mode-change retargeting are in
[camera-possession-guards.md](camera-possession-guards.md).
`ClassicCamera.ProjectionCentreFromTop(panelTop)` carries the rule, and the Unity runner builds an
off-centre frustum from it, switching when the panel collapses. The engine's viewport also stops the
3D image at that line; the runner instead renders full screen under the opaque panel. The light-visibility volume stays symmetric, as read. The native rule
gave a 97° view whose far edge is 100 tiles wide at 2560×1440; at 1024×768 the ground footprint's
near and far edges are 6.8 and 19.2 tiles wide (default zoom). Camera paths already fix the horizontal angle.

0x0044D6D0 forms the view rotation from yaw, pitch and roll using `pi/1024`.
The pitch matrix's instruction range 0x0044D75C..0x0044D7D8 rotates about X.
The original's pitch-zero preset is a vertical view; converting pitch220 to
degrees above the floor yields51.328125. Frame orientation and the
transpose must be checked with asymmetric ground points, not a symmetric Heart.
Fresh reads of 0x00594CB0 and 0x00594DB0 confirm the existing row-vector math
records; 0x00594F30 supplies the transpose used for the optical-axis offset.

CONFIRMED (2026-10-14): matrix constructor 0x0042F480 stores its nine
arguments at element indices0,3,6,1,4,7,2,5,8. Thus the yaw/roll arguments
in 0x0044D6D0 become rows `(c,-s,0; s,c,0; 0,0,1)` in our row-major
representation, requiring negative System.Numerics Z angles. Pitch is built
directly in scratch memory and keeps the negative X angle. Panning uses the
same corrected yaw; a separate startup offset would leave it inconsistent.

## 3. Bounds and wheel input

STRONGLY INFERRED: 0x00449CA0 refuses relative zoom with camera flag0x08 or
an active mode transition, then clamps the resulting raw zoom to
`[minimum, maximum-1]`. 0x00449C30 applies the same bounds for an immediate
absolute zoom; its nonzero-duration arm schedules a timed change instead.

The bridge handler at0x004422B0..0x004422E8 inspects input sign, computes a
target of `(zoom*270)>>8` for positive input or `(zoom*242)>>8` otherwise,
and sends the difference to relative zoom. Input magnitude is unused there.
Unity wheel polarity is an adapter binding; the native screen-width/lens
calculation and clamping remain the presentation model's responsibility.

## 4. Starting location and limits

CONFIRMED binary (2026-10-14): bridge initialization 0x0043AAD0 calls camera
reset 0x00449670. That reset clears the active view block and then sets yaw
at camera+0xD84 to256 (45 degrees). The rotation builder 0x0044D6D0 consumes
that field as yaw. Entering ordinary modes3/4/5 through 0x0044ACE0 preserves
the active yaw, rounds it to the nearest eight units and replaces pitch/roll
with the selected profile. Consequently a fresh dungeon view starts diagonally
despite preset3's authored yaw0. The adapter supplies this reset yaw separately
from the untouched preset. Its Unity-facing reported yaw is45 degrees.

CONFIRMED map/screenshot landmark: Level1's gold tile(14,24) is north of the
Heart start(14,28). The owner showed it on DK2's left and our right. With the
corrected matrix sign its projection lies left of the Heart, now pinned by
an install-backed regression. The reset value256 remains unchanged.

Ordinary pitch220 converts to51.328125 degrees above the floor. The matched
recording comparison in section6 supports this tilt and the existing lens.

CONFIRMED shipped keeper3 player camera tiles: Level1(14,28), Level7(5,15),
Level8(75,9). 0x0051F0B0 copies the local player's camera coordinate pair to
world+0x7235, alongside its local-player id, when the startup override is clear.
CONFIRMED (2026-10-15): startup command7, 0x00447D50 at0x00447E0B..0x00447E57,
reads that pair through world virtual+0x324 (0x00509830). It zero-extends both
tile words, shifts each left12 and adds0x800, fixes height at0x1000, forces
mode3 with duration0 through0x0044A9E0, then calls0x00449AC0. Thus the requested
focus is exactly `(tileX+0.5,tileY+0.5,1)`, not an inferred tile corner or an
authored camera Z. The setter copies it unless focus is locked or a mode
transition is active. Its follow-up0x00449B30 clamps each component to that
axis's active minimum through maximum-minus-one. Intro transitions and native
camera pan input timing remain outside this startup read.

Classic camera tests establish numerical conversion and bounds; section6
separately checks rendered framing against the original recording.

## 5. Owner-requested zoom extension

The convenience deviation is defined in ADR-0478; native bounds above remain
the original rule rather than the extended adapter setting.

## 6. Camera recording, 2026-10-23

CONFIRMED capture: the owner's `2026-10-23 15-43-11.mp4` is88.53seconds at30fps,
recorded with OBS Studio. The1600x1200 game is scaled to1080x810 inside a1440x810
video, with180pixel side bars. The owner confirms the initial zoom was untouched
after the Level1 intro/tutorial move. The original and SHA-256 manifest are in
`D:\DK2Evidence\camera-20261023-154311-20261023T1349154080`; the source remains in Videos.

`ClassicCameraReferenceProbe` renders Level1 at1600x1200, gold AP4 focus
(14.5,1,-24.5), reset yaw and the authored default/minimum/maximum zooms. It uses
the production camera model, projection shift and TerrainLattice. Omitting the
lattice flattens the wall markers and invalidates the comparison. The focus is
a comparison assumption, not a native camera-state measurement.

Measured remake-minus-original red wall-marker centres, expressed in native
1600x1200 pixels: eight markers at5s/default zoom40960 have RMS per axis1.34px,
mean offset(-1.68,-0.15)px; seven at10s/maximum zoom53247 have RMS1.18px and
mean(-1.53,-0.17)px. `tools/classic-unity-probe/compare-camera.py` reproduces the
colour-weighted connected-component measurements from the extracted frames.
At15s, six known wall markers fitted with fixed lens/pitch/focus and maximum
zoom give yaw-61.758degrees and RMS1.47px per axis. This rotation fit does not
measure input timing. These samples support the current lens, tilt and HUD
centre; lighting, fog, objects and HUD rendering differ. They do not establish
global pixel equivalence or99% motion fidelity.

## 7. Ordinary camera controls, 2026-10-23

CONFIRMED binary: 0x004066A0 polls ordinary input only on the first inner pass
and after 0x0044D590 permits it. It decays signed horizontal/vertical counters
at HUD+0x1094/+0x1098 one step toward zero, then calls0x00406DF0. That function
submits five-pixel edge directions first, followed by non-Ctrl arrow bindings.
0x00407070 emits one action126 per direction and adds signed2 to its counter,
clamping each counter to[-6,6]. Shift sets the keyboard packet's fast flag;
edge packets have that flag clear.

Action126 handler0x00442140 computes each screen-axis offset as
`ticksPerGameSecond * min(frameMilliseconds,1000) * direction * 512 / 1000`,
with signed integer truncation and an additional factor8 for fast packets.
The tick-rate global0x006F2590 comes from the level, not the old15Hz placeholder;
the shipped levels use4. The handler rotates through the native16.16 trig table,
then, in ordinary modes3/4/5, multiplies each rotated coordinate by raw zoom
and shifts right13. Products retain native32-bit wrapping and signed shifts.

After direction packets,0x00406DF0 submits action3 with screen offsets
`ScrollSpeed * counter * 64 / 10`. Handler0x00441FB0 applies the same rotation
and zoom arithmetic. This coasting packet has no elapsed-time factor: its
distance and five-frame release tail are frame-rate dependent in DK2. The
ScrollSpeed preference defaults to10 at profile+0xF9 in0x0055F040 and when its
registry value is absent in0x0055F6D0. It is distinct from the level tick rate.

Every packet reaches0x00449BD0, which adds its delta then calls0x00449B30 to
clamp focus immediately. Combining packets before clamping is observably wrong
at map edges. Focus lock bit0x40 or an active transition suppresses the update.
Ordinary presets fix height to raw4095 after maximum-exclusive clamping.

CONFIRMED binary (2026-10-27): the edge test in 0x00406DF0 is strict (x < 5, x > width - 5,
likewise y) and runs whether or not Ctrl or Shift is held. The registry option "Alternative
Scroll" (profile +0xF1, 0x00759C1D, default 0) swaps LEFT/RIGHT only: plain LEFT/RIGHT rotate
(action 8, +/-0x40) and Ctrl+LEFT/RIGHT pan with the Shift fast flag (0x004066A0). UP/DOWN
stay pan and zoom. The adapter implements the default; whether the Options screen exposes the
switch is unread.

Keyboard yaw packets carry elapsed clock milliseconds masked to26bits from
HUD+0x10B8; instruction0x00403E66 writes that clock at the end of an inner pass.
0x004422F0 masks the submitted angle to11bits;0x00449D40 adds it to yaw unless
flag bit1 locks yaw. Thus one input millisecond is one of2048 angle units,
with pan preceding yaw. Keyboard zoom submits signed1024 to action7, but
0x004422B0 consumes only its sign: the wheel's multiplicative rule still applies.

Pitch and roll (read 2026-10-30, CONFIRMED). Command 8 carries yaw in field0, roll in field1 and
pitch in field2. X held sends dx*-8 as yaw and dy*-8 as pitch; Pitch Up/Down (bindings 0x2F/0x30)
send pitch -8/+8, and 0x31/0x32 send roll. `0x00449D40` does nothing during a timed transition
(+0xDD8) and skips each angle whose lock bit in +0xD92 is set (0x01 yaw, 0x02 roll, 0x04 pitch).
Outside modes 1, 2, 10 and 11, pitch is clamped to 0..0x1CC (0 is straight down) and roll to ±0x200.
Camera_SetMode writes +0xD92 from the preset's Flags (THINGS camera payload +0x48 via `0x0056F900`).
In the shipped GlobalVariables.kwd, presets 3, 4 and 5 have flags 0x26, so pitch and roll are locked
in the ordinary views; the owner saw no tilt there (2026-10-30). The user cameras (modes 13-15, F4-F6)
use preset 9 with flags 0, so X with vertical mouse and the pitch keys tilt there. The lock is
data, not code. We build only the ordinary views and do not apply pitch or roll.

The adapter implements the shipped Home/End, Delete/PageDown and Ctrl-arrow
bindings through the rebindable key table; the WASD, Q/E and Unity-axis (joystick)
aliases were removed (2026-10-29), and the wheel stays. Pure-controller tests
cover held/released counters, fast/edge distinctions, fractional timing and
locks. The native executable oracle checks630 pan combinations and both
opposing-packet map-edge cases; only its virtual mode query is stubbed.
The isolated Unity probe passes at all three zooms. Its runtime timing check
confirms the adapter's tick construction retains sub-millisecond precision;
the Mono conversion trap is recorded in `docs/TRAPS.md`.

## 8. Projection widening and reference matching, 2026-10-23

CONFIRMED binary and executable oracle:0x0058A6F0 divides the supplied pixel
focal length by viewport width/height. With focal640, changing a1600x1200
viewport to1600x900 keeps horizontal normalized scale0.4 and changes vertical
scale from0.533333 to0.711111. Viewport cropping does not change the lens.
The ordinary renderer0x00598880 supplies the lens independently of the crop;
its conversion is in section2.

CONFIRMED remake defect: reference-resolution scaling used the cropped viewport
height. A live3840x2160 snapshot with ordinary camera and letterbox height0.75
had vertical FOV86.3048degrees and horizontal FOV131.54degrees. A full-screen
height correction removed the crop dependence but retained the earlier extra
widescreen coverage at118.07degrees. The owner judged that better but still
distorted, and requested strict reference matching instead of a comfort lens.
The 18 original live audit files are preserved with verified SHA-256 copies in
`D:\DK2Evidence\camera-projection-20261023T1649534901957`.

The final adaptation scales the pixel lens by full screen width/reference
width. With reference1600, full-width horizontal coverage is102.68degrees at
1080p and4K, matching the1600x1200 baseline. Wider displays crop vertically;
they no longer extend the peripheral angle. Camera distance, authored lens,
tilt and raw zoom stay unchanged. This replaces the2026-10-20 extra-side-view
policy; it is a modern resolution mapping, not evidence of native widescreen
support. Null reference width retains the raw native rule from section2.

Full/cropped C# regressions cover1080p and4K. The executable oracle covers the
native1600-pixel crop calculation. Unity checks pass at three zooms for
1080p/full,4K/letterbox and the1600x1200 baseline; the latter retains the section6
pixel measurements. Owner-session confirmation of the final mapping is pending.
Native HUD principal-point placement during a combined letterbox remains
unmeasured. Matching the
reference coverage does not remove perspective distortion inherent in DK2.
