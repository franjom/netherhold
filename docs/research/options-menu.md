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
