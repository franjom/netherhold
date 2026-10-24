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
zero skips ordinary menus, resources, minimap, north and tooltips; its alternate
context branch remains separate. Result and special-player flags are classified below.

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
must also pass. FD encloses both GUI delivery and ordinary world-hand routing;
the latter also requires world `+0x284 == 0`. Thus hiding a panel is not sufficient
evidence that all world input should be enabled.

## Implemented subset

`HudVisibilityRules` mirrors both camera predicates. The Step 6 HUD uses the
director's active path mode and counted letterbox requests: hidden HUDs do
not draw or accept clicks, and blocked ordinary input cancels a pending hand
gesture. Scripted camera movement, path skipping, subtitles and animated bars
continue independently. Restoration follows the request count, not bar height.
Tests cover mode exceptions, nested requests and release after cancellation;
the adapter probe checks hidden/input-blocked navigation and restored zoom.

## Normal GUI toggle producers

**CONFIRMED subset:** key dispatcher `0x00405010` binding `0x26` toggles
interface `+0xF5`. In the ordinary dungeon arm, hiding expands the view to
virtual `(0,0,2560,1920)` through view slot `+0x34`; restoration uses the normal
viewport fields and graphics-mode offsets. The possession-like arm also
clears interface `+0x12B2` before toggling. Binding `0x18` invokes the Options
toggle and forces `+0xF5=1` if hidden. Binding-to-key defaults are documented
separately in controls.md; the dispatched numeric bindings are not scan codes.

`0x0040A9F0` is an additional `+0xF5` toggle registered by `0x00402D00` under
the command name `GUI`; it is not by itself proof of a keyboard binding.
The corner Options menu and full-screen map branches have separate contracts
in [minimap-controls.md](minimap-controls.md).

The `+0xF5` GUI toggle does not appear in the camera HUD predicate or ordinary
input predicate. It hides ordinary interface drawing without blocking the
ordinary world-hand path or suppressing its cursor by itself.

## Additional modal producers

`0x00426070` sets `+0x10DE/+0x10E2` while menu21's chat edit control is active.
The key dispatcher opens it through the broadcast/allies/recipient bindings;
`0x00426000` sends the edit text through `0x00409DA0` and closes the control.
This gate is chat input capture, separate from Options pause. The menu21
resource-panel interpretation was wrong; sender/recipient text is decisive.

The menu contracts were completed on 2026-10-24:

| Menu | Content and commands | Campaign / HUD contract |
|---|---|---|
| 38 (`0x00423360`, `+0x272A`) | Player-information table: eligible player rows, eleven statistic columns (`0x00423450`), close button. Binding `0x28` calls it without a multiplayer guard (`0x00405C85..0x00405CAC`); the default I binding is in controls.md. | Campaign-accessible. Toggles menu38, excludes Options/menu42, and saves/hides/restores detail menu40 through `0x00428990`. Neither F5 nor FD changes, so no whole-HUD hiding gate is needed. The statistics table remains outside this gate implementation. |
| 42 (`0x00425070`, `+0x1C1F`) | Other-player rows, alliance state/matrix and private-message button. `0x00425AB0` selects a player; `0x00425B50` queues alliance actions 118/122/123; `0x00425D40` closes the panel and selects a chat recipient. Other row callbacks have function records. | Explicitly mode 3 only. Excludes Options/menu38 and saves/restores menu40; neither F5 nor FD changes. Multiplayer commands/presentation are not built for Q611. |

Menu38 data is `0x00693A78` → widgets `0x00691478`; menu42 data is
`0x00693B98` → widgets `0x00692148`. The shared menu40 save is one field
(`+0x272E`), not a stack of previous modal visibility states.

The `+0x10C` byte is ordinary world gesture capture. Its set sites are in
`0x004075E0` (including build/sell drag arms); resets occur in the ordinary
pointer router, secondary handler, Options tool reset and runtime reset.
It suppresses GUI delivery while the world gesture owns the pointer, rather
than being a global simulation pause. The adapter's separate drag capture
implements that purpose without copying the raw numeric field.

## Result and special-player state (binary, 2026-10-24)

**CONFIRMED direct store contracts:** player flag `+0x76 & 0x80000` is set
by `0x004C0120` only when continuing old Status 1 (conquered). This store is
not multiplayer-only: the routine also serves campaign action 134 through
`0x00512950`. A second caller, `0x00428230` (reviewer, 2026-10-24), is a widget
callback held only in static widget record data at `0x0069318E`; it calls player
vtable `+0x260` with the interface's player id, then `0x004C0120`. Its neighbour
record's callback `0x004281D0` posts action record (0,0,0,1) through `0x004346E0`,
the same payload `0x004039A0` posts at `0x00403A90..0x00403AD5` when Status is neither
0 nor 2 outside game mode 3 and path mode. That pair is LIKELY a result menu's
continue and leave buttons; which menu owns the records and their labels is unread. Player initialization `0x004B8640` clears it in every mode.
The store/mask census found no other direct bit-19 setter/clearer on the player;
Reaper-state masks concern a different record, and `0x004B9740` changes only
notification bits 16/17. Bulk save restoration and computed aliases are outside
that direct-store census.

`0x00419DF0` suppresses normal menus, resources and minimap when this bit is
set, calling only menu21's callback when interface `+0x247B` permits it. It is
a post-conquest special view, separate from the ordinary GUI-toggle flag.

`0x00402B50` is the FD setter at interface table `0x0066C4A4 +0x1C`.
All-block address scans find only its table entry and no absolute reference to
that entry. None of the 89 listed constant-slot calls was resolved to the HUD
interface (resource, render, session/packet and other receivers). No extra
campaign modal producer was established; computed dispatch remains unproved.

The direct result updater `0x004039A0` establishes these campaign rules:

- Outside camera mode 18, clear FD/101 and enable 105 before checking results.
  A path camera leaves FD unchanged at this initial step.
- Nonzero player Status with continue flag `0x400000` clear sets FD/101,
  clears F5/105 and sets `+0x132E` bit 0 (tool/held-icon suppression).
- Local restore request `+0x13DA` plus continue flag `0x400000` clears
  FD/101, sets F5/105, clears tool-icon suppression and consumes the request.
  Status becoming zero alone is not the GUI restoration operation.
- A separate Status-2/game-mode-4 arm restores these fields, forces letterbox
  action `0x74` value 2 and shows menu51. Mode 4 is outside the campaign gate.

`ClassicHudControls.UpdateResult` implements the FD/F5 subset (CONFIRMED against
`0x004039A0`'s stores) and is tested, but **Unity does not feed it**. The key
dispatcher `0x00405010` tests Status and FD before the Options (0x18) and GUI (0x26)
bindings, so DK2 also refuses Options and G after a result; its way out is Space/Esc
(Status 2, not game mode 3: Space posts action 1, Esc posts action 134 then 0x4B and
sets `+0x13DA`) or the result menu above. The remake has neither, so suppressing input on
a terminal Status would strand the player. Wire it together with the end-of-level flow.
Our simulation keeps Status terminal and has no action-134 continue flag. No simulation change or
HashVersion bump was made. The special post-conquest view is likewise unbuilt.

## Scripted GUI restoration and nested letterboxes

**CONFIRMED setters:** interface `+0x34` is `0x00402B70` (F5=1), and `+0x38`
is `0x00402B80` (F5=0). Action 38 calls hide on enable and show on disable,
independently of its counted letterbox request. The non-mode-3 celebration
`0x004BF9E0` also calls hide. The script's show sets F5=1 unconditionally; it
does not remember an earlier manual GUI toggle or clear Options/menu state.

Path end/skip `0x0044D8F0` restores the saved camera mode and normally releases
one request, with no F5, FD or menu restoration. Its flag-0x10 arm clears
viewport rectangles without clearing the count; the front-end flag-0x8 arm is
separate. Count behavior is recorded in camera-paths.md §4/§6.

The director now sends the script's explicit GUI request to the HUD controls.
A path alone preserves a manually hidden GUI. With script and path requests
nested, either release order keeps the camera gate shut until the final
release; an open Options panel survives and resumes drawing when both F5 and
the camera predicate allow it. This implements the binary predicates;
visible original-game acceptance remains Q648.

## Still open

Q611's direct producer audit and campaign gate build are complete to the
boundaries above. Original-game restoration acceptance is Q648; campaign
post-conquest continuation is Q649. Implementing action 134's simulation
lifecycle requires a separate task. Computed virtual dispatch has not been
proved absent. Full-map hover symbols, creature squares and legend are a
separate Step6 item, outside Q611.
