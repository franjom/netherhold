# Creature overview

CONFIRMED from DKII.EXE 1.70 and shipped Creatures.kwd unless a section says otherwise; adapter layout still needs an original-game visual comparison.

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
above page_back (11, `0x00690028`). `0x00417730` adds a delta and clamps; the widgets' `+0x16` words read +1 (forward) and -1 (back), so top pages forward (LIKELY; the caller is indirect). The page callback `0x00410A60` writes visibility state3 when `0x00416980`'s count is at most the per-mode dword HUD`+0x7D+mode*4` (unread; LIKELY the visible column capacity), else state1. `0x004129E0` draws state3 multiplied by RGB (100,100,100) (`0x00412EA5..0x00412EBC`). The owner sees both strips darkened in DK2 when the roster leaves room (2026-10-20); the adapter darkens them when its own roster fits one page.
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
the GUI manager's full held-channel arbitration. Worker-row and mode tooltips, count highlights, mode hover art and the portrait and count hover
text (status strip, below) are built (2026-10-20, 2026-10-23). A native capture must settle header overlap and
pixel positioning at unusual aspect ratios.

## Portrait and count hover text

Read 2026-10-23 from DKII.EXE 1.70, the shipped Text.str and Creatures.kwd.

**No portrait widget exists — STRONGLY INFERRED** (a negative read from the one draw callback). `0x004201B0` is the draw callback of
the first row menu of each mode (menus 7, 8, 12, 15; static menu records near
`0x006935CE`) and only draws pictures. A portrait, and the numbers on it, lie over
that column's count widgets, whose update callback is `0x00418DD0` (draw
`0x00417850`). The Imp portrait lies over menu6's four worker count widgets
`0x006900C8 + 160·r`, absolute (544, 1612+64r, 120, 64). An ordinary column's widgets
are (924+136i, 1616+row·h, 108, h) by `0x00418BC0`, with h = 244, 64, 84, 84 for Total,
Jobs, Combat, Moods. Neither the portrait nor a count number has its own text. The
portrait is drawn about 128×264 from a small offset, wider and (in Total mode) taller than
the 108-wide widgets, so its right and bottom edges are LIKELY outside every widget and give
no text; ours hit-tests the widget rectangles only.

**Trigger — CONFIRMED.** While a visible count widget has the pointer (`+0x49`),
`0x00418DD0` writes interface `+0x4658 = 4` and `+0x465C` = the column's creature type.
The widget is hidden, and gives no text, when the column is past the roster or page
capacity or the definition has `+0x686` bit `0x8000000` (kwd `0xEFC` bit 27, our
`CreatureFlags.IsUnique`; `CreatureOverview` never makes a column for one, so ours matches). The value is the same for
every row and every mode: one type per column.

**Status strip text — CONFIRMED.** The strip draw `0x0042E140` calls `0x0042D820`,
whose case 4 calls `0x00494740` on the definition. That formats the definition's
`+0x6AC` string, copied by `0x00492320` from Creatures.kwd byte `0xF38` (Imp 2347,
Firefly 2349), with two substitutions, spelled %27 and %28 in the strings:

| Spelling | Source | Values |
|---|---|---|
| %27 | job class `+0x67D` (kwd `0xF02`) | 0 or other: 2418 Worker; 1: 2419 Thinker; 2: 2417 Fighter; 3: 2420 Scout |
| %28 | fight style `+0x67E` (kwd `0xF03`) | 0 or other: 2427 Coward; 1: 2424 Blitzer; 2: 2423 Blocker; 3: 2425 Flanker; 4: 2426 Support |

Firefly reads "Firefly.  Scout.  Fighting Style: Flanker."; the Imp's 2347 has no token.
No name, level, count or mood is substituted. The strip has no delay: it is written
every frame the widget is hovered and scrolls when too long, as for other widgets.

**Near-cursor popup — STRONGLY INFERRED: none.** Case 4 also queues the type name
`+0x6AA` (kwd `0xF24`: Imp 2771 "Imp", Firefly 2348) with the hovered widget `+0x116A`
for `0x0042E590`. The strip callback ends by writing `+0x4658 = 6`. It is widget
`0x00693318` of HUD menu `0x00693EB0`, drawn by `0x0052BBD0` inside `0x00419DF0`
before `0x0042E590` runs. So `0x0042E590` compares the queued widget with `+0x116E`,
which `0x0052BC50` sets only to a hovered widget with a nonzero text id. Count widgets
have text id 0, so the queue is cleared every frame and the 250 ms delay never expires.
The same frame order would make `0x0042E590`'s category 0..3 branch to `0x0042EA70`
unreachable, which is the main doubt. Ours shows no popup (`WorldHoverTextTests` names the doubt).
Open for the owner, from play:
- does hovering a creature portrait show a box near the pointer, or only the top-bar text?
- does hovering a room, spell, trap or door button show a box (the binary says that branch is dead)?
- where does the portrait's hover end at its edge (ours: the widget rectangles only)?
- does the Moods row's "Happy" flicker?

**Mood row anomaly — UNKNOWN effect.** Of the eleven count-widget templates
(`0x00690578`..`0x00690BB8`, stride 160), only `0x00690A78` (Moods row 0, filter 27)
has a text id: 2340 "Happy". Its status is still the creature description; on the
reading above, the popup alternates between 2340 and the type name each frame and is
never shown. Text ids 2324..2345 have description pairs in `0x0042DB60` but no other
count-widget template carries them (other writers of a runtime text id were not searched).
