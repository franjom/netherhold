# Camera paths (`.KCS`) and their playback

DKII.EXE v1.70, Ghidra primary reads and the shipped `Data/Paths.WAD`, 2026-10-17.
The file format and the loader `0x0044CFB0` are **CONFIRMED** against all 80
shipped files. The playback in `Camera_Update` mode `0x12` and the render branch of
`0x00598880` are read; their arithmetic is **CONFIRMED** from disassembly. The skip,
action 38's letterbox switch and the subtitle drawing are read (§5-§7). The fog
question is answered from the shipped scripts (§8). Built as ADR-0496 (§9).

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
- **In game:** only `0x00442540`, game action 0x73's view handler. The view's
  initialiser `0x0043AAD0` fills its handler array at view `+0xA2` from (id, handler)
  pairs at `0x0066CE30`, and the pair `(0x73, 0x00442540)` is there (CONFIRMED, read
  2026-10-17). How a queued action reaches that array is unread, so the link is
  **LIKELY**. `0x0050E5C0` first offers a record to the observer `[[world+4]+0x10]`
  (slots +0x20 and +0x24), then to the world table at `+0x1684`, whose filler
  `0x00511A80` registers no 0x73. It unpacks `{pathId | bit31, x, y}` and passes bit 31 as `plain`. Trigger
  action 31's arm `0x004C886E` queues that record for the local player only: the
  point's centre `((StartX + EndX) / 2, (StartY + EndY) / 2)` in integers, with bit 31
  set when `Available` (body byte 2) is non-zero.
- **Front end:** `0x00538000`, `0x005384F0`, `0x005392E0`, `0x005393B0` and `0x00539490`
  start paths 0xFA-0x112 (250-274). They use origin (3, 12) and `plain` 1, and each
  sets flag 0x8 just before the call, so the camera stays where the path ends. Each
  does this only while global `0x0075AEAB` is 0. A start that is refused leaves 0x8 set.

The shipped levels issue action 31 60 times (tag 214; the 9 type-31 records under
tag 213 are another record kind, with no PathId):
- one entrance path for each campaign level, from 201-222 (there is no 202 or 220);
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

## 5. The skip: `Input_HandleGameKey` `0x00405010`, branch `0x00405362`

On a key press (`[ESP+0x60]` non-zero), two keys reach `0x00405362`:
- scan code 0x01, Escape, while global `0x0075A661` is 0;
- scan code 0x39, Space. With player flag `+0x76 & 0x400000` set, Space still
  reaches the skip when `[ESP+0x14]` is 2 or `0x0052C520()` is non-zero, and
  otherwise issues action 1.

The branch then does one of four things:
- **Mode 0x12:** `0x00568140(0x28)` clears the speech queue, then `Camera_EndPath`
  `0x0044D8F0` runs. The path ends at once, letterbox and all.
- **A move to a point in flight (`+0xDFA`):** the queue is cleared (argument 0) and
  `0x0044A270(1)` re-times the move to end in 1 ms.
- **A yaw turn in flight (`+0xE20`):** the same, through `0x0044A550(1)`.
- **Otherwise:** the key goes on to the ordinary bindings.

`0x00568140` returns at once when the speech object's `+4` is zero. Otherwise, when a
line plays, it calls the sound object's slot +0x38 with one of two callback blocks:
`0x0066C77C` for argument 0, or `0x0066F99C` carrying the argument. What each does to
the playing line is unread. It then zeroes:
- the line queue's count `+0xC8`;
- the subtitle queue's count `+0x14C`;
- the last-subtitle time `+0x15C`.

A subtitle already on screen keeps its time.

The script waits through condition 72 (`trigger-walkers.md`), which is false while
the camera is on a path. Level 1 chains two paths with it (§8).

## 6. Action 38 is the script's letterbox

The player arm `0x004C8955` (jump table `0x004C8A60`, pointer 10) acts for the local
player only. It does two things:
- it queues game action 0x74 with `Available == 1`;
- it calls the interface object `[[world+4]+0xC]`: slot +0x38 when `Available` is 1,
  +0x34 otherwise.

The level-won sequence `0x004BF9E0` makes the same pair (0x74 with 1, then slot
+0x38). What the two slots change on screen is unread. That they hide and show the
control panel is INFERRED from that pairing.

Action 0x74's handler is `0x004426F0`; the pair `(0x74, 0x004426F0)` is in the same
table as 0x73's. It calls the letterbox `0x0044D320` with its argument. With 2 it
sets the count to 1 and lowers it, which forces the bars off.

So the letterbox is counted across two sources:
- a path start raises it and the path's end lowers it (§2, §4);
- action 38 raises and lowers it on its own.

Level 1 does both, so its bars stay until the path has ended **and** trigger 7's
`38 Available=0` has run.

## 7. Subtitles

`CSpeechSystem` (`0x0075B8E8`) keeps two queues:
- **Lines** (`0x00568390`): 16 slots of 12 bytes, count `+0xC8`. A duplicate
  (id, second value) is refused. `0x005681D0` starts the next line when none plays
  and 1000 ms have passed since the last one ended.
- **Subtitles**: 16 slots of 8 bytes at `+0xCC`, count `+0x14C`.
  - They are filled only when the enqueue's third argument is non-zero.
  - Each holds the line id - 1 and a display time of `strlen(text) * 0x60`, which is
    **96 ms a character**.
  - `0x005681D0` shows the next one (`+0x170` line, `+0x174` end time) when none
    shows and 1000 ms have passed since the last one cleared (`+0x15C`).

Which one is drawn depends on whether speech audio is on (`+4` and `+0x160`):
- `0x005687D0` (a subtitle to draw?) returns the playing line's flag `+0x16C` with
  audio on, else `+0x174 != 0`;
- `0x00568800` (which line?) returns the playing line's id - 1 with audio on, else
  `+0x170`.

So with sound, the subtitle lasts exactly as long as its line plays. The timed queue
is what shows with speech off.

Trigger action 24 reaches the queue through `Message_Post` `0x004C4600` type 0x15.
Its jump table at `0x004C4AC0` sends 0x15 to `0x004C49B4`, which passes:
- the action's `SpeechId` as the queue id;
- world `+0x7209` as the second value;
- **1** as the subtitle flag.

`Message_Post` returns before any of it when the recipient's `+0x19A` (Status) is
set.

`Message_Post` plays the line only for the local player (`0x004C4A4C`).

**The text is the level's own table, not `Speech.str`.** The enqueue and the
draw resolve it through `0x00553A90`, which reads only the table `0x00553780`
loaded at level start (`0x007415B4`; else an empty string). `0x00553780` takes a
byte at `+0x64EC` of `Level_ApplyRuntimeConfiguration_LIKELY`'s source, INFERRED
to be the KWD `TextTableId` (`LevelTextTableTests`, five levels). Its 60-entry
table `0x006BC010` (CONFIRMED) maps 1-34 to resources 0x420-0x441 (`LEVEL1.str`
... `DEMO3.str`), 35-40 to 0x439 (`MYPETDUNGEON.str`), and the rest to nothing.
Level 1's speech 3 is `LEVEL1.str` 2, *"Behold, your Dungeon Heart..."*; speech 2
is 1, *"Here walks the goodly Lord Antonius..."*.

**Drawing.**
- The overlay pass `0x00403FB0` draws a subtitle only when the camera is in mode
  0x12 and one is active, through `0x0040A3E0(line)`. It does this only on its
  no-result branch: the player's `+0x19A` is 0, or the game mode is 4.
- The text buffer is `FollowPathSubtitleTextBuffer`, made by `0x00402D00` at the full
  screen width and `h >> 3` high. It is drawn at `y = h - h/8`, which is the bottom
  letterbox bar.
- A line that fits is laid out once. One that overflows the box waits 8000 ms, then
  scrolls up a pixel every 200 ms until the overflow has passed.
- While the letterbox count is non-zero, the same pass also calls `0x0041A190`,
  which draws an animated resource (unread).

## 8. Fog of war during an intro, and the return to the Heart

The engine does not lift the fog for a path, as far as read (**STRONGLY
INFERRED**, a negative claim):
- `0x0043E320` draws unscanned ground as terrain 27, and scanned but unexplored
  ground as 38. Its only bypass, `0x0044DB70`, covers the possession modes 2, 10 and
  11 (`fog-reveal-sources.md` §1). Two parts of this are unread:
  - it passes view `+0x118D` to the helper, not the camera mode `+0xDA8`, and the
    writer of `+0x118D` is unread;
  - it also asks a view slot `+0xDC`, which is unread.
- `Camera_SetMode` `0x0044ACE0` only records mode 0x12 (its case 0x11). It
  invalidates the terrain cache only when entering or leaving possession.
- Starting or ending a path touches no explored bit.
- A path can set renderer flag bits 1 and 3:
  - bit 1 is camera flag 0x20, set only for a path started with `plain == 0`
    (`Available` 0: 15 of the 60 shipped records; Level 1's path 1, not its 201);
  - bit 3 (`0x0044AA80`) scales the view polygon (`0x00574820`).

  Bit 0 (`0x0044AA20`, possession only) is the one that changes terrain enumeration.

**The scripts do it.** Action 17 explores an action point's rectangle (`Available`
0) or un-explores it (non-zero), for the local player (`fog-reveal-sources.md` §3.6).
The campaign scripts pair them around their paths:
- Level 4's entrance runs `17 point 28 Available=0` with `31 path 204`. Its
  condition 72 (trigger 48) then runs `17 point 28 Available=1`, so the area shows
  for the intro and is hidden again when the camera stops.
- Level 2 reveals point 18 with path 232. Many levels reveal before their generic
  rotations (230-234).
- Level 1 chains its whole opening this way:
  1. trigger 1 (level time) runs actions 44 and 45, path 1 (the Heart plug, point 18,
     53 ticks), letterbox on (38) and speech 3. Its child is condition 73 (type 72);
  2. condition 73, once the plug path stops, **explores point 2**, queues speech 2,
     and flies **path 201** (point 23, 500 frames, 67 ticks). Its child is
     condition 7 (type 72);
  3. condition 7, once the fly-over stops, turns the letterbox off, **un-explores
     point 2**, sets flag 83 and starts timer 15, which the level's opening counts on.

  `LevelOneRevealsPointTwoForItsFlyOverAndHidesItAfterwards_AtNominalTickLengths` runs this chain on the
  shipped data: point 2 is explored from tick 53 to tick 119 and hidden at tick 120.

The owner confirmed this from play (2026-10-17): the hero gate shows during the
fly-over, and when control returns everything is behind the fog again.

So the owner's recollection is right in effect, and the mechanism is each script's
action 17, timed by condition 72. Before ADR-0496 our condition 72 answered "still"
at once, so the hiding action ran on the next turn and an intro's reveal never
lasted.

**The return.** The path's end restores the saved mode, and the ordinary camera's
focus, zoom and angles were never written during the path (§4). So the view comes
back to where the keeper camera was. At level start that is the startup focus on
the Heart's tile (`classic-camera.md` §4).

A script can also move the keeper camera itself, with action 26:
- The player arm `0x004C86CF` (pointer 4) acts for the local player only. It queues
  game action 4 with the point's centre, packed `x | y << 16`.
- Action 4's view handler `0x00442650` (pair `(4, 0x00442650)` in the table) works
  in three steps:
  - `0x0044A150` sets the move target `((x << 12) + 0x800, (y << 12) + 0x800)`,
    clamped to the camera bounds;
  - `0x0044A270(0)` times the move as a tenth of the summed squares of the
    quarter-unit offsets: 25.6 ms per square tile, at most 500 ms;
  - `0x0044A500(0)` and `0x0044A550(0)` leave the yaw where it is.
- So action 26 is a short glide of the focus, which condition 72 also waits for
  (`+0xDFA`).
- Level 11A's entrance condition (trigger 49) runs one at once, on point 7.
- Level 1 uses its 26s in later tutorial steps.
- The level-won sequence `0x004BF9E0` queues the same action 4 to a position from
  `0x004C5F50`.

The 18 action 27s, the timed camera turns, are in levels 6b, 11A and 20. None sits
under a condition 72: they hang off flags, timers, a creature count and one type-4
condition.

## 9. Built (ADR-0496)

**Simulation:**
- Each keeper has a `CameraPathEndTick`, saved and hashed (hash 262).
- Action 31 starts a path for its keeper. The start is refused while a path plays,
  while the keeper possesses, or when the file is missing. DK2 also refuses in camera
  mode 0xB and during a timed transition (`+0xDD8`), neither of which is modelled.
  Condition 72's `+0xDD8` test is not modelled either.
- A path lasts `(2 (frames - first) - 3) / 15` ticks, rounded up: DK2's wall-clock
  time at a nominal 250 ms a tick. Level 1's path 1 lasts 53 ticks. Q1 measured
  3.93-3.95 ticks a second live, so the real count is near 52; no capture pins it.
- Condition 72 answers false while a path plays.
- `SkipCameraPathCommand` ends the path before the turn's walk.
- Actions 24, 26, 31 and 38 emit `ScriptedViewRequest`s.

**Presentation:** `ScriptedViewDirector` drives three parts on the wall clock:
- `CameraPathPlayback` (§3);
- `CinematicLetterbox` (§4, §6);
- `SubtitleQueue` (§7), with the speech-off timing, since Unity plays no speech
  audio. Its skip leaves the queue alone, as `0x00568140` does when the speech
  object's `+4` is zero (that this is the speech-off state is INFERRED).

**Unity:**
- the camera follows the path;
- the drawn area shrinks for the letterbox, with black bars;
- the subtitle shows in the bottom bar, from the level's text table
  (`LevelTextTable`), over opaque bars;
- action 26 glides the keeper camera's focus;
- Escape and Space skip.

**Not built:**
- the moves a skip also finishes (`+0xDFA`, `+0xE20`), and action 27;
- condition 72 waiting out action 26's glide (at most 500 ms, two ticks);
- action 38's interface slots;
- the overflow scroll;
- `0x0041A190`;
- speech audio;
- DK2's bitmap font (`FONT_INGAME*.bf4`; no reader yet, and which one
  `0x0040A3E0` selects is unread). Unity's default font stands in;
- the message record action 24 posts.

**Deviations:**
- The simulation measures the path in game ticks, where DK2 measures wall clock.
  The two agree at normal speed.
- Each keeper has its own path state, where DK2 has one local camera. This equals
  DK2 whenever only the human's tree starts paths.
