# In-game Options pages

Read 2026-10-29 from DKII.EXE static menu descriptors and callbacks.
The existing pause/open/close contract remains in [minimap-controls.md](minimap-controls.md#options-toggle-confirmed-control-flow-menu-identity-strongly-inferred).

## Row destinations

CONFIRMED static bindings: menu22 at `0x00696BE8`, widgets at `0x006945C8`,
80 bytes per record. A destination changes the visible page without clearing
interface `+0x2722`; therefore child pages retain the Options pause.

| Text.str | Row | Destination / primary callback |
|---|---|---|
| 537 | Level Objective | menu33 |
| 1265 | Game Options | menu25 |
| 143 | Load | menu23; `0x0042A760` refreshes the file list |
| 201 | Save | menu24; same refresh |
| 1266 | End Game | menu34 |
| 1269 | Restart Level | menu35; `0x00420570` seeds action2 and title1269 |
| 142 | Continue Game | `0x00428710` closes the family |

Menu33's widgets at `0x00696698` draw the existing objective panel. While
Options is open its navigation is Continue (Text.str142, closes Options) and
Back (20, target22); the Review Objective close button is hidden by
`0x004289D0`. Closing Options restores an objective opened before Options.
Only game modes1/4 draw the objective row (`0x00428C30`).

## End Game and Restart

Menu34 widgets at `0x00696828` offer action1 (Text.str12; dynamic variants
2913..2915 in `0x00428C60`), action3 (Text.str13), Continue and Back.
Both actions first enter confirmation menu35. Its widgets at `0x00696A08`
show question15, accept21 and cancel20; `0x00420570` supplies the title and
action to both buttons. Accept is `0x004205B0`, cancel `0x00420770`.
Cancel action2 returns to menu22; cancel actions1/3 returns to menu34.

CONFIRMED: accept action2 submits game action102 and closes Options;
action3 submits125; action1 submits1 and selects the front-end return page.
For campaign mode1, action1 selects page37 while the viewing player has no
result flag `0x400000`, page26 with it. Abandoning therefore returns to the
campaign map, not the statistics page. The callback makes no profile write;
the attempts increment is in the loss arm of the existing verdict routine
([campaign progress](campaign-map-and-progression.md#2-what-a-win-or-loss-writes)).
The downstream action102 and125 consumers remain unread here.

## Save/load browser

CONFIRMED: these are named-file browsers, not a fixed number of save slots.
`0x0042A760` refreshes a `*.sav` list; Load selection `0x0042A7D0` resolves a
listed file and sets the pending-load path/flag. Save selection `0x0042A850`
puts the selected basename in the name editor. Submit `0x0042A920` creates
`<directory>/<entered name>.SAV`, saves, refreshes the list, and plays sound.
That submit has no explicit overwrite-confirmation branch. The file helper's
own overwrite policy is unread. Save and Load are disabled in game mode3
or when `0x0044D870` returns1 (`0x0042A360`); the latter predicate's meaning
is UNKNOWN. Native Game Options has its own pages25..32, not front-end
pages15..17; it leads from25 to26,27,29,31.

## Game Options children

CONFIRMED: the static descriptors for menus25..32 begin at `0x00696CC0`
with 72-byte menu records; menu25 widgets begin at `0x00694C08` with
80-byte widgets and a `0xFF00` terminator. Root25 links Graphics26,
Sound27, Control29 and User Cameras31. Control29 links Keyboard30;
Keyboard Back returns29. Graphics/Sound/Control Back returns25; Root Back
returns22. These children keep the Options pause. Advanced Sound28 has only
Back to27; User Cameras31 links editor32, whose Store/Back returns31.
The static layout is represented in `ClassicGameOptionsLayout`; its rectangles
map the native virtual coordinates to the existing Options reference as
`x * 0.4, (y + 180) * 0.4, width * 0.4, height * 0.4`.

These pages apply edits live rather than using the front-end Confirm/Cancel
transaction. `0x00429CD0` applies and persists Gamma immediately, using
`0x00566220` and renderer slot `0x78`; slider15..60 scales by4096/30.
`0x00428F00` applies Speech Volume through `0x00563490`.
`0x00429C30`, `0x00428E90` and `0x0042AE40` initialize graphics, sound
and control sliders respectively. Control callbacks `0x0042AE90`,
`0x0042AF20` and `0x0042AF50` apply Game Speed, Scroll Speed and Sensitivity
through `0x0055D590`, `0x00560BC0` and `0x00560B30`; Game Speed also
requests action46. `0x0042B750` starts key capture at interface `+0x111`
and records the selected row at `+0x10D`.

`0x00414750` draws the slider through `0x00414850`, then its end labels.
The latter uses runtime GUI slots `+0x144`, `+0x15F`, `+0x17A`, `+0x195`;
resource table `0x006A8120` identifies the shipped horizontal left/right
arrows, background and thumb. `0x0042A9F0` draws clipped action/modifier/key
columns at native offsets0/200/300 with renderer font line height.
Its keyboard list has Continue and Back, rather than the front-end icons.
User-camera editing and the extra Message Tabs, Alternative Scroll and World
Tooltips settings are not implemented by this change and remain dimmed.


The in-game Sensitivity widget's declared limits are5..20, like Scroll Speed.
Initialization `0x0042AE40` passes global `0x00759C21` to its value setter;
`0x0042AF50` and front-end `0x0054BAF0` pass the slider value unchanged to
`0x00560B30`. This establishes no conversion in those callbacks. The interaction
with the registry fallback value and the eventual native input consumer remains
unresolved; these UI limits do not establish a native default or input multiplier.
