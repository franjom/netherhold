# Creature overview

CONFIRMED from DKII.EXE 1.70 and shipped Creatures.kwd; adapter layout still needs an original-game visual comparison.

## Roster and counters

`00419180` reserves slot1 for the first Evil+Worker definition. `004185E0`
then adds nonunique owned creature types in display order. `0050E330` resolves
that order from compact-definition+6B4; `0050E3A0` retrieves a definition by id.
The shipped record supplies display order at CF6 and portrait resource at D40
(64 ASCII bytes), with id at F4B in a5449-byte record.

The roster includes a type while its owned count is positive, even when every
member is held. Visible total subtracts state193 and held/local-pending members.
`00478AE0` classifies workers: idle flag or last-task6 means Idle5; otherwise
actual state category, resolving one movement resume, yields Fighting4 for
Combat22 and Busy6 otherwise. Native other modes use activity categories and
mood buckets. The adapter exposes all four modes.

## Input

`00418360` cycles primary pickup in native ownership-list order using a saved
per-type target and wraparound, applying pickup eligibility. `00418080` selects
highest/lowest level for modifier bindings35/36, preserving the first eligible
member on ties. `00417F60` dispatches secondary focus through `00415BC0` for
all members or `00415D30` for a filtered row. Focus excludes state193 and
held/pending members but does not apply pickup permission. The adapter returns
typed Pickup/Focus actions for the host to execute before its simulation worker;
local pending previews update counts immediately without a renderer world read.

`00418A90` supplies the row filters. Jobs use5/20/21/23, combat4/22/24,
and moods27/26/25. Filter6 excludes activity4/5; filter23 excludes5/20/21;
filter24 excludes4/22. Filter21 also accepts CombatPitFighter, although the
training counter counts activity21 alone. Mood counters read the retained mood
class: content0, unhappy1, angry2. The activity mapping already implemented by
CreatureActivityRules matches `00478AE0` for these views; its canonical category
table remains in [computer keeper management](computer-keeper-management.md#8-slapping--0x00502f00).

## Retained default-mode layout

Coordinates here are original1920-high virtual units; multiply by height/1920.
`00416FD0`/`00418BC0` place sidebar6 at category origin(472,1600), width432,
and menu7 at(904,1616). Count columns are local(20+136*i,0,108,244).
`00417850` draws menu7 counts at widget-top+93; other counters use top+12.
`00418DD0` updates visibility. `004201B0` draws portrait headers, separate
from those widgets: worker(544,1608,128,264), ordinary(912+136*i,1612,128,264).
The native capacity calculation divides category width minus432 by116 although columns
advance136; the adapter preserves that calculation pending visual verification.

The worker sidebar is one strip, catalogue157 `GUI/Tabs/t-cp-imp` (32×128;
widget `0x00690488`, menu6 local(4,8,64,264)). Catalogue65/56/50/53 are the art
fields of the mode-selector widgets at `0x006902F8`, not the sidebar; `00417850`
draws only counts and highlight162 over it (corrected 2026-10-19 after the owner's
comparison). Count rows use filters0/5/6/4.
Both page strips are always drawn, page_forward (catalogue10, widget `0x0068FFD8`)
above page_back (11, `0x00690028`). `0x00417730` adds a delta and clamps; the widgets' `+0x16` words read +1 (forward) and -1 (back), so top pages forward (LIKELY; the caller is indirect). The page callback `0x00410A60` writes visibility state3 when the roster fits one page; what state3 draws is unread, and the adapter draws both strips normally.
Hover highlight is catalogue162 GUI/Tabs/t-cp-hilight tinted 0x00FFC9, red in the low byte: RGB (201,255,0).
The byte order is settled by the selected-mode tints (00417B40), which the owner saw as green, red and orange.
Menu6's worker row icons are records 0x00690078 + 160·r, local (0, 4/68/132/196, 80, 72/68/72/72), text ids
2308..2314; its count widgets 0x006900C8 + 160·r, local (72, 12+64r, 120, 64), highlight on hover. The mode
selectors carry text ids 2316..2322; hovering one draws t-creature_panel-i over the strip (00417B40).
Numeric family selection follows [HUD resources](hud-resources.md).

Static menu6 selectors at `0x006902F8` plus80*i pass mode0..3 to `004177B0`.
Their local hit rectangles are(220,16+64*i,44,52). `004177D0` selects catalogue
158..161 for the64x264 frame at local(368,12). `00416FD0` lays out jobs in four
64-unit rows and combat/moods in three84-unit rows; totals use one244-unit row.
`00417B40` uses catalogue238..241 as hover previews, not as the small buttons.
The static decorative widget at `0x006904D8` draws catalogue163,
GUI/Tabs/t-creature_panel, at local(208,8,64,264); this is the four-button strip.

## Remaining presentation boundaries

Mode-specific counts and selection filters are connected. The adapter consumes
host press edges, matching type0's once-per-press callback, without reproducing
the GUI manager's full held-channel arbitration. Portrait tooltips remain open;
worker-row and mode tooltips, count highlights and mode hover art are built (2026-10-20). A native capture must settle header overlap and
pixel positioning at unusual aspect ratios.
