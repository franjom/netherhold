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
2308..2314; its count widgets 0x006900C8 + 160·r draw at local x72, y12/72/136/200, size120×64. Their hit heights are60/64/64/64. The mode
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
text (status strip, below) are built (2026-10-20, 2026-10-23). Native hit coverage is now read below; unusual-aspect positioning remains a visual check. The binary audit closes the separate portrait-preview item without new drawing.

## Portrait and count hover text

Read 2026-10-23 and rechecked 2026-10-24 from DKII.EXE 1.70, shipped Text.str and Creatures.kwd.

**Portrait image — CONFIRMED within the audited draw path (2026-10-24).**
`0x004201B0`, draw callback of menus7/8/12/15, always installs compact
definition `+0x52B` at `0x00420394`/`0x00420495` and draws through renderer
slot `+0x5C`. Its complete body has no hover `+0x49` test, alternate portrait,
3D-preview call or sound call. Count hover in `0x00417850` draws only
catalogue162; mode hover art238..241 belongs to `0x00417B40`. Count records'
sound slots are0. No additional hover picture or sound was found.

`0x00492320` copies file `+0xD40` to the portrait descriptor; neighbours are
compact `+0x510` and `+0x546`. All 33 nonempty portraits among 46 shipped
definitions use type1 `-bg` art, flags0 or1, without animated/start/end paths.
The full 5,767-entry texture directory has no creature portrait `hilight`,
`-on` or `big` variant. Generic `GUI/Icons/Hilight-Creature` textures exist,
but their name occurs neither in the executable nor shipped editor KWDs.
Archive presence is not an active draw path; the name search alone does not
exclude dynamic construction elsewhere.

**Hit coverage — CONFIRMED for the retained overview layout.** Portraits are
separate draw submissions. `0x00418BC0` lays out ordinary count rectangles;
`0x00528300` preserves separate worker draw/hit geometry; `0x00528430` tests
half-open bounds. Menus4/5 have no widget arrays. Sidebar and row templates
have no extra widget covering the portrait's right/bottom edge.
Coordinates below are1920-high units; ordinary column i starts at0.

| Portrait | Draw bounds x / y | Count hit bounds x / y |
|---|---|---|
| Worker | [544,672) / [1608,1872) | [544,664) / [1612,1864) |
| Ordinary, Total | [912+136i,1040+136i) / [1612,1876) | [924+136i,1032+136i) / [1616,1860) |
| Ordinary, Jobs | same | same x / [1616,1872) |
| Ordinary, Fighting or Moods | same | same x / [1616,1868) |

The rightmost8 units have no creature hover target; bottom overhang varies
by mode. Worker row icons overlap its leftmost8 units and carry their own text.
The adapter retains a coarser hit union (worker256 high, ordinary244 in every
mode); exact row-boundary parity is not built by this no-preview audit.
Existing status text and count highlights are unchanged.

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

**Near-cursor popup — STRONGLY INFERRED: none in the ordinary initialized HUD.**
Case4 queues type name `+0x6AA` with hovered widget `+0x116A`. Menu53's strip
widget `0x00693318` calls `0x0042E140`, resetting `+0x4658` to6 at `0x0042E559`.
`0x0052B8F0` appends menus; `0x0052BBD0` draws backwards, so menu53 draws
first. `0x00419DF0` calls the draw at `0x00419F9A`, reads the hover category
at `0x00419F9F`, then calls `0x0042E590` at `0x0041A036`. No update occurs
between them. The branch skipping normal drawing also skips the popup.
Hiding the strip widget alone does not suppress its callback.

Category0..4 producers are update callbacks. Whole-image pointer and E8/E9
scans across both executable sections found room/spell/workshop producers only
in static widget `+0x34` update slots; the count updater is a positive control.
Later draw bodies, including `0x00412FC0`, do not republish the category.
There is no ordinary frame-order window restoring0..3 after reset; no legitimate
menu53-only suppression path was established. Absolute xrefs on `0x4658`/`0x465C`
resolve no symbol because these are field offsets. Direct-displacement scans
found status/popup formatting and frame-selection readers, not a preview reader.

After reset `0x0042E590` uses text-bearing widget `+0x116E`, which
`0x0052BC50` excludes for zero-text count widgets; their250ms delay cannot
complete. The category0..3 route to `0x0042EA70` is inactive in this ordinary
frame order. Popup id2238 is a separate route to that formatter, independent
of category: the complete cost formatter is not dead. Actual portrait and
cost-popup appearance remain play checks Q645/Q646; no new popup or preview
was built. The existing `WorldHoverTextTests` expectation is retained.

**Mood row anomaly — UNKNOWN effect (Q647).** Of the eleven count-widget templates
(`0x00690578`..`0x00690BB8`, stride 160), only `0x00690A78` (Moods row 0, filter 27)
has a text id: 2340 "Happy". Its status is still the creature description; on the
reading above, the popup alternates between 2340 and the type name each frame and is
never shown. Text ids 2324..2345 have description pairs in `0x0042DB60` but no other
count-widget template carries them (other writers of a runtime text id were not searched).
