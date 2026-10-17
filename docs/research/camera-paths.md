# Camera paths (`.KCS`) and their playback

DKII.EXE v1.70, Ghidra primary reads and the shipped `Data/Paths.WAD`, 2026-10-17.
The file format and the loader `0x0044CFB0` are **CONFIRMED** against all 80
shipped files. The playback in `Camera_Update` mode `0x12` and the render branch of
`0x00598880` are read; their arithmetic is **CONFIRMED** from disassembly. Nothing is
built yet beyond the file reader `DK2.Data.Camera.KcsCameraPath`.

This is step 1 of the campaign intro scenes: the camera half. The subtitles come
from the speech queue (`mentor-speech-and-notifications.md`); step 3 reads how they are drawn.

## 1. The files

`Paths.WAD` holds 80 `.KCS` files and the designers' own index,
`Engine Path Info.txt.txt` ("Camera Path Assignments", KD, 27/05/99). The index names
these groups:

| Paths | Use (the index's words) |
|---|---|
| 0-3 | "Camera for level start....Plug Explosion" |
| 50-66 | creature intros (50-55 generic, 56 Dark Elf ... 66 Troll) |
| 100, 101 | the 1st and 2nd cut of the end-of-level sequence |
| 150 | "Camera for heart explosion" |
| 201-222 | "Cam enterance for level N" (215 is 15a, 221/222 are 11b/11c) |
| 230-232 | "Generic rotate" 5, 10 and 20 seconds |
| 250-274 | the 3D front end (table, entry, credits, MPD table) |

Files outside the index: 233-236, 244, `1st_horny_path1`, `2nd_horny_path1`.

Layout, from `0x0044CFB0` and every shipped file:
- A 16-byte header. Dword 0 is the frame count; the other three are 0 in all 80 files.
- Then `count` frames of 56 bytes, 14 little-endian floats:
  - 0-2: position x, y, z, in tiles from the path's origin;
  - 3-11: a 3x3 rotation;
  - 12: the field of view, in radians;
  - 13: the view distance.

The loader keeps the position and the two tail values unchanged. It stores the rotation
as `S * Mt * S` with `S = diag(-1, 1, 1)`, so it transposes the file's matrix and
negates every element whose row or column, but not both, is 0.

The shipped data, checked over all 34,346 frames:
- every rotation has determinant 1 and is orthonormal;
- z runs from -16.7 to -0.02 (see §3 for the sign);
- the field of view runs from 0.35 to 2.52 rad;
- the view distance runs from 1500 to 12000.

The index's "Generic rotate (5/10/20 secs)" paths have 150, 300 and 600 frames,
which matches the 30 frames a second of §2.

## 2. Starting a path: `0x0044A370`

`0x0044A370(pathId, x, y, plain)` is a camera method:
1. It refuses while the camera's mode `+0xDA8` is 0x12, 10, 2 or 0xB, or while a timed
   move `+0xDD8` is in flight.
2. It sets camera flag `+0xE3C` bit 0x20 to `plain == 0`.
3. It formats `EnginePath%d.kcs` and loads that file through `0x0044CFB0`.
4. The origin `+0xEB2/+0xEB6` becomes `(x + 0.5, y + 0.5)`, the centre of tile (x, y).
   The origin's z `+0xEBA` is 0.
5. It enters mode 0x12 through `0x0044A5D0`, which saves the previous mode in `+0xED2`.
6. The first frame `+0xEC6` is 7 for paths 50-99 (the creature intros) and 0 for all
   others. The frame remainder `+0xECA` starts at 0.5. `+0xECE` is set to the time
   from `Clock_GetMilliseconds`; each mode-0x12 update then overwrites it, so it holds
   the time of the last update.
7. If flag 0x8 is clear, `0x0044D320(1)` starts the letterbox (§4).
8. It saves a renderer setting (`[[camera]+4]+8`) in `+0xEC2` and replaces it with a
   global clamped to 2..16. What that setting does is unread.

Callers:
- **In game:** only `0x00442540`, which has no direct call; it is reached through a
  handler table. It unpacks a record `{pathId | bit31, x, y}` and passes bit 31 as
  `plain`. That is the record trigger action 31 queues as game action 0x73
  (`level-time-limit.md` §7). The arm sets bit 31 when `Available` is non-zero, so
  the record matches; that 0x73 is the action dispatched to this slot is STRONGLY INFERRED.
- **Front end:** `0x00538000`, `0x005384F0`, `0x005392E0`, `0x005393B0` and `0x00539490`
  start paths 0xFA-0x112 (250-274). They use origin (3, 12) and `plain` 1, and each
  sets flag 0x8 just before the call, so the camera stays where the path ends. Each
  does this only while global `0x0075AEAB` is 0. A start that is refused leaves 0x8 set.

The shipped levels issue action 31 60 times (tag 214; the 9 type-31 records under
tag 213 are another record kind, with no PathId):
- one entrance path for each campaign level, 201-222;
- creature intros 56-66;
- generic rotations 230-234 and 236;
- path 1, once.

45 of the 60 records have `Available` 1, so `plain` is set and flag 0x20 clear.

## 3. Playing it: `Camera_Update` mode 0x12 and `0x00598880`

**Time.** Each camera update, mode 0x12:
- takes the real milliseconds since `+0xECE`;
- multiplies them by 0.03 (`0x0066D1A0`);
- adds the whole frames to `+0xEC6` and keeps the fraction in `+0xECA`, carrying 1 when
  it reaches 1.0.

When `+0xEC6 >= count - 1`, `0x0044D8F0` ends the path (§4). So a path plays at **30
frames a second of wall-clock time**, independent of the game's tick rate. A path
starting at frame 0 lasts about `(count - 1.5) / 30` seconds. The creature intros
start at frame 7, so they last `(count - 8.5) / 30`.

| Path | Duration |
|---|---|
| Heart plug paths 0-3 | 13.3 s |
| Level 1's entrance (201) | 16.6 s |
| Level 11b and 11c (221, 222) | 46.6 s |

**Interpolation.** In mode 0x12, the renderer's camera setup `0x00598880` reads frame
`i = +0xEC6`, frame `i + 1` and `t = +0xECA`:
- **Position:** each frame's offset is added to the origin (`0x0044E7B0`). The two
  points are lerped (`0x00452AB0`: `a + (b - a) t`). The eye goes to the renderer as
  `(x, y, z - 1.0)` in the engine frame.
- **Rotation:** the two native matrices are lerped element by element (`0x0059D5F0`
  with `0x00594ED0`: `A + (B - A) t`). The result is **not** re-normalised and becomes
  the view matrix as it is.
- **Field of view:** lerped. The focal length is
  `screenWidth / (2 tan(fov / 2))` pixels (`0x00756EEC`, the constant 0.5 at `0x0067050C`).
- **View distance:** lerped, then multiplied by 0.002 (`0x00670518`). That gives tiles:
  12000 is 24 tiles and 3600 is 7.2. It goes to frame parameter `0x13`, the global
  `0x00760B04`. That is the `viewDistance` of the view volume in
  `classic-light-visibility.md` §1, capped at 32 when renderer flag bit 0 is set.
  OpenKeeper's reading of this value as a near plane does not match the binary.
- **Renderer flags:** flag bit 1 comes from `0x0044AB60`. In mode 0x12 that returns
  camera flag 0x20 (set when `plain == 0`). In other modes it returns 1 only for the
  possession modes 2, 10 and 0xB. What renderer flag bit 1 changes is unread.

**No offset.** The ordinary camera adds an optical-axis offset to its focus
(`classic-camera.md` §2). The path branch does not: the path's point is the eye.

**The sign of z.** The engine frame's z is `-gameZ` (`classic-camera.md` §2). So a
frame at file z stands `1 - z` tiles up. The end-of-path snap (§4) computes the same
height in fixed point, `(1.0 - z) * 4096`. Every shipped frame has z < 0, so the
camera is always more than one tile up.

## 4. Ending: `0x0044D8F0`, and the letterbox `0x0044D320`

`0x0044D8F0` restores the mode saved in `+0xED2`, then branches:
- **Flag 0x8 set** (the front end):
  - the camera stays on the last frame;
  - its focus becomes `(x * 4096, y * 4096, (1 - z) * 4096)`;
  - zoom `+0xD80` becomes 0;
  - the rotation is copied in;
  - `+0xD8E` gets `width / (2 tan(fov / 2)) / 320` in 12.12 fixed point (`0x00441990`
    divides by 0x140000), the ordinary camera's lens unit, from the last field of view;
  - view distance `+0xD8A` gets the last value `* 500` (`0x0066D1C0`), capped at 8
    tiles, which is 8 tiles for every shipped path;
  - flag 0x8 is cleared.
- **Flag 0x10 set:** unless global `0x0075AEAF` is set, it zeroes the letterbox state
  (`+0xE4A` and the rects `+0xE52..+0xE7E`).
- **Neither:** `0x0044D320(0)` ends the letterbox.

It then resets `+0xEC6`, restores the renderer setting saved in `+0xEC2`, and applies
any mode request that `0x0044A5D0` deferred into `+0xDB0/+0xDB4` during the path.
The ordinary camera's own position fields are never written during an in-game path,
so the player's view returns to where it was before.

`0x0044D320` is not a camera save. Its rects are CONFIRMED; that they make a letterbox
is our reading of their values (LIKELY):
- With argument 1 it raises a count `+0xE48`. It animates the viewport rect
  `+0xE62..+0xE6E` over 2000 ms (`+0xE4A` start, `+0xE4E` duration) to
  `(0, h/8, w, h - h/8)`, which is black bars of an eighth of the screen height top
  and bottom.
- With argument 0 it lowers the count. At zero it animates back to the full viewport
  `+0xE82`, or `+0xE92` in modes 3-5 and 7.
- `Camera_Update` steps the rect.

## 5. What the intro needs next

- **Step 2:** the skip. Condition 72 and the cancel handler `0x00405362` already
  test mode 0x12 (`trigger-walkers.md`). Not yet read: what a cancel does to a path in
  flight, and how the trigger script waits on condition 72.
- **Step 3:** the subtitle drawing.
- **Step 4:** playback in Unity:
  - the native matrix and engine-frame position map through the conversion
    `ClassicCamera` already uses;
  - the frame advance runs on the presentation's real clock, not the simulation tick;
  - the letterbox is a 2 s viewport animation.
