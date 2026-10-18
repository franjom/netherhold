# HUD visibility

## Camera gate (CONFIRMED, binary)

Read 2026-10-18: complete `0x0044D620`, `0x0044D590`, `0x0044D870` and
`0x00419DF0`, plus the GUI-routing arm of `0x00406530` and the overlay shell
`0x00403FB0`. Camera mode is `+0xDA8`; the counted letterbox is `+0xE48`.

`0x0044D620` permits the normal HUD exactly when mode is not `0x12` and
either the letterbox count is zero or mode is 10, 2 or `0xB`. Therefore an
ordinary camera can hide the HUD without playing a path, and a path camera
still hides it with a zero letterbox count. Do not equate this gate with
`PathPlaying` or with the current animated bar height.

The gate is checked at the start of `0x00419DF0`, before ordinary menus,
minimap and heading decoration. Within that gate, view slot `+0xCC` returning
`0xB` draws a different element and returns; player flags `+0x76 & 0x80000`
also select a special callback instead of normal HUD. Interface field `+0xF5`
zero skips the normal-menu branch. These additional modes/flag producers are
not identified here and must not be assigned invented gameplay names.

Menu rendering itself (`0x0052BBD0`) additionally tests each menu's `+0x48`
before drawing it and its children. Individual menu visibility is separate
from the camera's whole normal-HUD gate.

## Restoration and remaining overlays

Camera path start/end and letterbox count updates are established in
[camera-paths.md sections 2 and 4](camera-paths.md#2-starting-a-path-0x0044a370).
The HUD predicate restores drawing as soon as the count is zero and mode is
allowed, even while the viewport's two-second restoration blend continues.
Ending a path alone does not restore the HUD if another counted letterbox
request remains active. Mode `0x12` independently keeps it hidden.

`0x00403FB0` draws path subtitles before calling the normal HUD dispatcher,
and tests letterbox count afterward to draw its separate element. Hiding the
normal HUD therefore does not hide subtitles or the letterbox. View slot
`+0xCC` returning 7 selects a distinct full-map branch instead of calling
`0x00419DF0`; its visibility/input contract remains outside this reading.

`0x0044D870` groups numeric modes: 1/2/10/11 return 1; 3/4/5/9/13/14/15 return
2; 7 returns 3; 18 returns 4; other values return 0. This is a mode classifier,
not itself a visibility test. Normal HUD drawing uses it to select a special
compact branch; panel semantics remain unread.

## Hidden HUD input (CONFIRMED, binary; incomplete modal semantics)

Pointer routing `0x00406530` first calls `0x0044D590`. This permits ordinary
input only when camera `+0xDD8` is zero, mode is neither `0xB` nor `0x12`, and
the counted-letterbox condition above passes. Failure returns before both GUI
and world-hand dispatch. A hidden intro HUD cannot keep its buttons active
through that ordinary path.

When initial input passes, GUI delivery additionally requires interface `+0xF5`
nonzero, `+0xFD` zero, mode not 7, the HUD gate true, mode not 2 unless `+0x2722`
is nonzero, and interface byte `+0x10C` zero. The outer `+0x10DE` modal condition
must also pass. If GUI handling declines an event, ordinary world-hand routing
has its own world `+0x284 == 0` condition. Thus hiding a panel is not sufficient
evidence that all world input should be enabled.

## Still open

Trace interface `+0xF5`, the special player flag and modal-field writers; identify
the numeric special modes and full-map branch as needed. Validate ordinary
intro, scripted letterbox without a path, nested requests and restoration in
the original game. Q610 owns the implementation/research acceptance boundary.
