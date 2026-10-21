# Bottom panel controls

Read2026-10-18 from DKII.EXE static menu/widget records, callbacks and the
shipped KWD/interface resources. Confidence applies to each contract below.
Function-level readings and revisions live in their knowledge-base records.

## Geometry and category controls (CONFIRMED)

The virtual coordinate system is2560x1920; the presentation reference transform
is height/1920, preserving the original bottom and left anchors. Static menu18
at0x006938C8 provides the tab widgets at0x00690C58. Open-panel y comes from
[the height setter](functions/00417170.md), rather than the static initial y1360.

| visual order | localized label id | category | local tab x | art catalogue id |
|---|---:|---:|---:|---:|
| Creatures |2153|4|0|4|
| Rooms |2155|1|136|5|
| Keeper Spells |2157|2|272|6|
| Workshop Items |2159|3|408|7|

The tab strip starts(472,1460); tabs are128x128. The single-category grid starts
at(472,1600), with draw extent1800x300 and hit extent1800x432. Native layout
shares that width when several categories are locked open; see
[category placement](functions/00417310.md) and [tab selection](functions/00416C20.md).
The current adapter exposes one category at a time; category locking and collapsed
panel controls remain unimplemented.

[Grid layout](functions/00417470.md) puts264-unit cells at local(76,12), then
steps272 horizontally in the normal expanded layout: six columns, one row.
Page-back/forward artwork is catalogue11/10. The64x132 arrows occupy local
(0,12) and(0,144). A crowded multi-panel layout can use128-unit cells; the
current single-category adapter does not need that branch.

On wider screens the panel fill extends to the right anchored control block;
the grid and tab strip retain their reference positions. Added width is fill,
not additional category slots. Native bitmap64x64 Panel-BG is tiled through
the retained image pool. The last partially visible tile is stretched; exact
edge cropping remains a presentation limitation.

## Room state and action (CONFIRMED)

The visible resolver is [0x00410AE0](functions/00410AE0.md), the widget updater
is [0x00410990](functions/00410990.md), and selection is
[0x00410860](functions/00410860.md). They distinguish:

| availability | displayed state | selection |
|---:|---|---|
|1|absent|none|
|2|unknown-room icon|unknown widget state2|
|3|room icon|room-placement tool|
|4|only present if keeper owns that room type|clears active tool|

Only Buildable definitions participate. A researched room remains selectable
when unaffordable: the click callback does not test gold. The renderer
[0x00412FC0](functions/00412FC0.md) separately compares room cost with stored
gold and tints an unaffordable icon RGB100. Selected feedback uses native
selected-room artwork. The current adapter preserves an existing selected
room while its availability changes; simulation placement still rejects
invalid construction. Clicking a visible availability4 entry clears the tool.

The room name comes from RoomDef.NameStringId, cost from RoomDef.Cost and icon
from its first84-byte art descriptor. Page ordering is CONFIRMED: service
virtual+0xA8 is [0x0050D150](functions/0050D150.md), which finds the definition
whose runtime+0x466 byte matches the ordinal. The room loader copies that byte
from intermediate+0x496, file+0x3C2, the existing RoomDef.OrderInEditor field.
The model sorts by that field. Secondary room-slot navigation uses the separate
[callback](functions/004118B0.md); its adapter ordering limitation is below.

## Spells and workshop (partially CONFIRMED)

The tab strip's background is [0x00418EE0](functions/00418EE0.md): Bar-00 wood tiled from x0 (read;
the minimap draws over it, and the stone fill also spans the full width), then Bar-02/Bar-03 metal whose
width depends on the screen width; the adapter's last-480-units placement is a 4:3 reading (LIKELY).
[0x00412260](functions/00412260.md) draws the selected tab's alternate art (`+0x40`: catalogue
173/174/175/178, `gui\icons\selected-creature/room/spell/workshop`) over the icon and tints the
Workshop tab grey while no Workshop is owned. A hovered tab draws catalogue121 `gui\icons\hilight` over itself with render flag8 (read as additive; built as a lightening overlay). Tab hover follows the widget text id (`+0x30`,
2153+2i; status 2154+2i via [0x0042DB60](functions/0042DB60.md)).
The fifth strip widget `0x00690D98` (catalogue44 `GUI\Icons\i-mise`, local (544,0), 128x128,
text2161 "Resize Panel", callback [0x004167D0](functions/004167D0.md)) collapses and expands the panel
(BUILT 2026-10-20). Collapsing hides category menus1..4, the creature menus5..17 and menus41/44 (the
Reaper); [0x00417170](functions/00417170.md) moves the strip (menu18), menu39 and menu43 from y0x5B4
to y0x6EC, places menu40 above them, and ends the 3D viewport 0x98 below the strip, so the camera's
centre moves with it ([classic-camera.md](classic-camera.md) §2). Expanding restores the remembered
categories and menu41 and relays out ([0x00416FD0](functions/00416FD0.md)). Not built: the pointer
jump onto the moved button (legacy input cannot move the pointer), sounds 0x134/0x135, and menu40's
move (the query/entity panel). What a tab click does while collapsed is unread (0x00416C20); the
adapter changes the category with the grid hidden.
Menu39 at (1112,1460) 948x156 holds eight 88x128 buttons: Review Objective (text2163, art139,
`0x004288C0`), a second button (art153, `0x004247F0`), and six message slots (art127,
`0x00423F50`/`0x00424220`, slot 1..6), not built. Menu43 is at (2080,1472) 480x140.
Tab visibility is [0x00416E20](functions/00416E20.md) (CONFIRMED): Creatures and Rooms
always show; Spells and Workshop show only while one of their filtered choices has
availability other than1, and a hidden tab keeps its place.
The common renderer's spell branch excludes spell5 and14 from the normal grid.
It uses separate unknown icons, upgrade state, cooldown wedges and tutorial
indicators. Workshop enumeration places doors before traps and excludes
availability1. Trap selection also excludes definition flag0x2000
([trap slot resolver](functions/00411080.md)). The no-Workshop tint is RGB128, on the tab and on
every door and trap slot (a slot only while its widget +0x41/+0x45 are zero, meaning unread), over the unaffordable RGB100; without a Workshop the cursor marks every
tile invalid ([0x0040B3C0](functions/0040B3C0.md), [0x0040B4F0](functions/0040B4F0.md)) and the
hand places nothing ([Hand_Click](functions/004075E0.md); BUILT 2026-10-21 as tint and refusal). These slot facts are in
[the common renderer record](functions/00412FC0.md).

The adapter routes supported choice keys to the host's existing command
interfaces. Affordability is display feedback; simulation rules remain the
authority for cast/placement validation. Create Imp cost uses the existing
ImpSupplyRules.CreateImpCost, including the live additional-imp multiplier.
Cooldown wedges, research/manufacture progress, upgrade art and tutorial
flashing are not yet produced by this adapter. Default creature overview roster, counters and input use a separate retained helper;
see [creature overview](creature-overview.md) for confirmed producers and the
remaining alternate-mode presentation boundary.

The bottom-right menu41 is the Reaper talisman, not a creature portrait.
Its control contract lives in [keeper-spell-reaper.md](keeper-spell-reaper.md).
The adapter supports regular spell5 when availability5 is3, and campaign
spell14 when LevelDef.Flags bit0x10 is set and the player has not spent its
one-shot use. Selection refuses while an owned Horny creature remains.
Normal unlock is latched after availability3; noncampaign initialization also
sets it for any availability other than1, preserving the native interface state.
It draws catalogue122's background and123/124/125 native states in menu41's
264-square local(0,12) rectangle, anchored288 virtual units from the right.
Campaign LevelDef.TalismanPieces supplies the quarter count. Retained masked
textures render its native90-degree sectors and500ms unused-one-shot blink.
The active one-shot Reaper countdown uses its retained ReaperTicks divided by
OriginalTicksPerGameSecond and native Gui/spells/rt-0..9 sprites. The native
draw positions are local x128 for one digit,64/192 for two, y72, with128-square
sprite extents. Treating flag2's x as the horizontal centre is STRONGLY INFERRED
from the symmetric positions and screen-edge fit; owner comparison must verify
it. Values above99 remain undisplayed because the native resource-index behavior
outside the ten digit descriptors has not been established.

## Install asset metadata (CONFIRMED shipped data)

All original artwork remains in the install and is accessed through
IClassicBottomPanelAssets. Rooms use the existing art catalogue. Additional
records are read with KwdReader and their known record sizes; no bitmap is
written to the repository.

| table | size | id byte | localized name word | icon descriptor | order byte |
|---|---:|---:|---:|---:|---:|
|KeeperSpells|406|0xEE|0xE6|0x20|0xDB|
|Doors|616|0x244|0x23C|0x74|0x245|
|Traps|579|0x20C|0x208|0x74|0x230|

Native door and spell ordinal iterators are [0x0050D6F0](functions/0050D6F0.md)
and [0x0050DE00](functions/0050DE00.md). These match runtime display-order bytes,
rather than ids. The file field positions were corroborated against OpenKeeper
KwdFile.java at4f2ab401; see THIRD-PARTY-NOTICES.md. No source was copied.

The shipped Text.str resolves these to names such as Sight of Evil, Wooden Door
and Gas Trap; the corresponding descriptor names resolve directly through
EngineTextures.dir/.dat. GUI art catalogue0x006A7DF0,69-byte records, confirms
tab ids4..7, unknown icons118..120, selected-room/spell/door/trap ids174..177
and Panel-BG id71. The panel exposes a retained HoverTextSnapshot containing
the localized name and current price; the existing native status HUD draws it
with BF4 glyphs. It creates no additional text strip or typography.

## Input contract and verification

ClassicBottomPanel retains its widgets, caches decoded native textures and
captures the pointer from primary press to release/cancel. Release outside the
pressed control cancels activation. SetVisibility cancels when ordinary input
is disabled; the host must also call CancelPointerGesture on focus loss and
cancel any in-progress world gesture on a HUD press. ContainsPointer includes
capture outside the panel, so the host can block world commands and camera
input for the entire gesture. Wheel events page only while over the panel.
Room secondary presses use the same capture/cancel path and emit typed
NavigateRoom requests; the host cycles owned rooms using their retained
CentreFieldX/Y camera position. Native [0x00415F00](functions/00415F00.md) retains
one last-room id across all room types, tries following links of that room
while ownership/type match, and otherwise wraps to the owner's chain head.

The adapter retains one last room but cycles derived rooms in ascending minimum
tile order: the simulation does not store native room ids or owner-chain links.
This is an explicit ordering approximation. Identity uses the room's minimum
tile; a split/merge can therefore restart the cycle. Camera targets use the
existing CentreFieldX/Y and native glide rather than inventing new room centres.

BottomPanelViewTests cover localized names, live costs, unknown/absent entries,
selected tool cancellation, native grid coordinates, read-only capture and
steady-state allocations. Owner comparison at1024x768 and2560x1440, adapter
allocation profiling and original-game interaction comparison remain required.
