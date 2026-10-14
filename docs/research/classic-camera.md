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
0x0044D550 supplies the visibility distance as the larger of twice authored
zoom and the active authored view distance, converted to world units.
An equivalent perspective camera has vertical FOV
`2*atan(drawableViewportHeight /(2*focalLengthPixels))`. Full screen width
and drawable viewport are distinct inputs when panels reduce the view.
At a full 640x480 viewport preset3's offset length is10 tiles and vertical
FOV is about41.112 degrees; at full1280x720 they are5 tiles and about58.716.
These values are conversion consequences, not settings invented for Unity.

0x0044D6D0 forms the view rotation from yaw, pitch and roll using `pi/1024`.
The pitch matrix's instruction range 0x0044D75C..0x0044D7D8 rotates about X.
The original's pitch-zero preset is a vertical view; converting pitch220 to
degrees below the horizontal yields51.328125. Frame orientation and the
transpose must be checked with asymmetric ground points, not a symmetric Heart.
Fresh reads of 0x00594CB0 and 0x00594DB0 confirm the existing row-vector math
records; 0x00594F30 supplies the transpose used for the optical-axis offset.

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

CONFIRMED shipped keeper3 player camera tiles: Level1(14,28), Level7(5,15),
Level8(75,9). 0x0051F0B0 copies the local player's camera coordinate pair to
world+0x7235, alongside its local-player id, when the startup override is clear.
The exact later startup sub-tile focus, intro transitions and camera pan input
timing remain unread. A tile-centre starting focus is explicitly a provisional
adapter placement, subject to the original-view acceptance gate.

Classic Step5 camera tests can establish numerical conversion and bounds;
they cannot establish matching rendered framing. The owner judged the Unity
camera view good (2026-10-14); a matched original-game comparison is still pending.

## 5. Owner-requested zoom extension

The convenience deviation is defined in ADR-0476; native bounds above remain
the original rule rather than the extended adapter setting.
