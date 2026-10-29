# In-game Options pages

Primary evidence: retail DKII.EXE static menu records and callback instructions,
plus installed `Data/Text/Default/Text.str`, read 2026-10-29. OpenKeeper #565 is
only the investigation lead. No slider ranges are taken from it.

## Row destinations (CONFIRMED)

The Options family is a contiguous set of 0x48-byte menu records, menu22 at
`0x00696BE8`, widgets at `0x006945C8`. Widget stride is 0x50; target menu +0x0E,
hide-own flag +0x12, callback +6, arguments +0x16/+0x1A, label +0x40.
The generic click contract is in [front-end-menus.md section 3.1](front-end-menus.md#31-how-menus-are-defined-hard-coded-tables-in-the-exe).

| Menu22 label | Text id | Destination / preparation |
|---|---:|---|
| Level Objective | 537 | menu33; objective row is restricted to modes1/4 by 0x00428C30 |
| Game Options | 1265 | menu25; its title is **97**, also Game Options |
| Load / Save | 143 / 201 | menu23 /24, both prepare through 0x0042A760 |
| End Game | 1266 | menu34 |
| Restart Level | 1269 | menu35; 0x00420570 arguments (2,1269) |
| Continue Game | 142 | 0x00428710 closes the Options family |

## Settings and camera (CONFIRMED)

Menu25 widgets `0x00694C08`: title97; Graphics96 -> menu26 (0x00429C30),
Sound95 -> menu27 (0x00428E90), Controls98 -> menu29 (0x0042AE40), and
**Define user cameras1485** -> menu31. Title1490 is not that fourth row:
it is **Adjust Camera**, the title of menu32 (`0x006964B8`).

Menu31 (`0x00696288`) offers User Camera1/2/3, ids1486/1487/1488.
Each invokes `0x0042B5D0` with index0/1/2, saves the prior camera mode at
interface+0x2732 and enters mode13/14/15 before showing menu32.
Menu32 displays instruction1686 (adjust tilt/zoom, then Store) and Store1489.
`0x0042B660` stores active zoom and angle words in the selected ten-byte
user-camera slot at `0x0075A38F + index*10`. Back `0x0042B6B0` requests the
saved mode. Store's widget returns to menu31; whether it restores that mode
as another callback side effect has not been established.

## Objective and quit (CONFIRMED, scoped)

Menu33's renderer `0x004207E0` draws Text.str1408 (Main Objective), briefing
entry2, Text.str1409 (Sub Objective), then briefing entries3/4/5. It is one
scrollable panel, not separate objective pages; rendering is canonical in
[message-tabs.md](message-tabs.md).

Menu34 (`0x00696828`) has Exit to Main Menu12 and Exit to Windows13, both
opening menu35 through `0x00420570`, arguments (1,12)/(3,13). Title14
(Exit to System) exists in Text.str but is not the shipped static row.
`0x00428C60` changes the first row to2913 (Exit to Debriefing Screen) when
player+0x76 bit0x400000 is set,2914 in mode2,2915 in mode3 (mode overrides bit).
Menu35 has confirmation text15 (Are you sure?), Yes21 and Back20.
`0x004205B0` dispatches Yes argument1 as action1,2 as action102 (restart),
and3 as action125 (exit OS). `0x00420770` returns Back to menu34 except
argument2, which returns to menu22. Profile/attempt effects and the save-list,
load/overwrite/name callbacks remain unread: completion plan A1/A5 must finish
them. A6 must use these in-game menus rather than assume front-end pages15–17.
