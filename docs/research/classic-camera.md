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
The Unity runner sets `ClassicCamera.ReferenceScreenWidth` to 1024 (ADR-0519's visual rule;
owner 2026-10-20): every screen gets 1024's camera distance and horizontal angle. The native rule
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

The owner's screenshots confirm the requested diagonal orientation but do not
establish a different pitch: viewport size, HUD and distance differ. Ordinary
pitch220 still converts to51.328125 degrees above the floor. A matched ground
projection comparison remains necessary before changing that tilt.

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

Classic Step5 camera tests can establish numerical conversion and bounds;
they cannot establish matching rendered framing. The owner judged the Unity
camera view good (2026-10-14); a matched original-game comparison is still pending.

## 5. Owner-requested zoom extension

The convenience deviation is defined in ADR-0478; native bounds above remain
the original rule rather than the extended adapter setting.
