# Message tabs (menu39) and the message panel (menu40)

Read 2026-10-21 from `DKII.EXE` v1.70: the static menu/widget records, the
menu39 updater and every callback the records name. The message queue itself
(record layout, insertion, expiry) is in
[computer-keeper-messages.md](computer-keeper-messages.md); the type table and
its speech references are in
[mentor-speech-and-notifications.md](mentor-speech-and-notifications.md). Each
function's reading is in its record under [functions/](functions/INDEX.md).
Nothing here was checked against a live capture; no owner observation was used.

## 1. Widgets (CONFIRMED static data)

Menu39 (`0x00693AC0`, virtual (1112,1460) 948x156) has eighteen 88x128
widgets at `0x006918D8` (80-byte records; the loader is
[0x00528300](functions/00528300.md)):

| record | local x | art | text | left / right callback | update |
|---|---:|---:|---:|---|---|
| `0x006918D8` Review Objective | 0 | 139 | 2163 | [0x004288C0](functions/004288C0.md) / none | none |
| `0x00691928` Ally | 88 | 153 | 262 "Ally" | [0x004247F0](functions/004247F0.md) / none | [0x00424810](functions/00424810.md) |
| `0x00691978` + 80*i, i = 0..15 | 176 + 88i (overwritten) | 127 | 0 | [0x00423F50](functions/00423F50.md) / [0x00424220](functions/00424220.md) | none |

Earlier notes said six slots; there are sixteen slot widgets, of which at most
eight are ever used (section 2). Source `+0x16` is the slot index the click
callbacks receive (runtime `+0x67`). All draw through
[0x004129E0](functions/004129E0.md) with the art id the updater writes.
The Ally button shows only in game mode 3 (`0x0075AD4B`), and toggles menu42
through [0x00425070](functions/00425070.md).

## 2. Which messages get a tab (CONFIRMED)

[0x00423BD0](functions/00423BD0.md), called every frame by
[Hud_UpdateControls](functions/00407260.md), walks the local player's messages in
queue order. New records are appended at the tail ([0x004C4600](functions/004C4600.md)),
so the walk is **oldest first**. It examines only the first **eight** of the
player's records (the count is capped before filtering), and gives a tab to each
with NeedsTab (`+0x27`) equal to 1 and dismissed bit `+0x2A & 4` clear. Every
one of the 100 type rows has flag `0x2000`, so every message starts with a tab;
NeedsTab clears at age 200 ticks (50 s at 4 ticks a second). The handled bit
`+0x28` is not tested. Unused slot widgets are hidden. There is no scrolling: a
ninth record is not shown until an older one leaves. The pass runs only while
`0x00759C29` is non-zero; [0x0042B410](functions/0042B410.md) hides all slots
([0x00423F30](functions/00423F30.md)) when it is zero (writer unread).

Slot n sits at local x = (n + 1) * 88, or (n + 2) * 88 in mode 3 (after Ally).

Record `+0x2A`, previously "purpose UNKNOWN", holds two interface bits: `4`
dismissed, `8` opened. They are written directly by the interface (no packet),
and the queue save `0x004C43B0` keeps them with the record.

## 3. Art, flash and slide-in (CONFIRMED arithmetic)

The record's graphic `+0x13` (from type-table field `+6`; 0 is read as 1)
selects a three-frame family from the GUI catalogue `0x006A7DF0`
(`gui\tabs\messages\`):

| graphic | frames | name | types (table `0x006A0B00`) |
|---:|---|---|---|
| 1 | 127-129 | mt-info | all others, including 0 and 6 (graphic 0) |
| 2 | 130-132 | mt-creature | 13, 17, 18, 19, 20 |
| 3 | 136-138 | mt-fight | 1, 87 |
| 4 | 139-141 | mt-objective | none in the table (producer unread) |
| 5 | 274-276 | mt-allymsg | 82, 83 |
| 6 | 271-273 | mt-playerexit | 85, 86 |

Frame `-01` while hovered (widget `+0x49 == 1`); otherwise frame `-02` on odd
game turns ([World_GetGameTurn](functions/00517490.md)) while the opened bit is
clear, else `-00`. The flash therefore alternates every turn (250 ms on, 250 ms
off, STRONGLY INFERRED from 4 turns a second) until the message is opened.
Other catalogue entries: 133-135 mt-goto, 142 mt-exit, 153 mt-ally, 167 mt-tick.

Slide-in uses the record's system-time stamp `+0x2C` (milliseconds at
insertion): age = (now - stamp) / 10. While age < 64 (640 ms), x is the slot x
plus 1020 - 16 * age, so the tab slides in from the right. Sounds go through
slot `+0x24` of the service at `0x0075B468` with argument `[0x006ACB34]`, one
handle per slot (interface `+0x0E + 4n`, state byte `+0x32 + n`): 0x320 on the
first sliding frame, 0x321 on each later sliding frame, 0x322 when it lands.
Which bank and samples these ids name is unread.

## 4. Hover text (CONFIRMED)

A hovered slot sets interface `+0x4658 = 5`, `+0x465C = slot`. The status
formatter [0x0042D820](functions/0042D820.md) case 5 finds that message and
writes its text ([0x004C51D0](functions/004C51D0.md)) to the status line through
`0x00553B90`, if [0x004C53B0](functions/004C53B0.md) says it has text. The
tooltip is the message text itself. Review Objective's own text ids are 2163
"Review Objective" / 2164 "Displays your overall objectives and goals in this
realm." (LIKELY through the ordinary widget-status path, as for the tabs).

Message text ([0x004C51D0](functions/004C51D0.md)): types 82, 83, 85, 86 and 87
substitute player names (player `+0xEEA`, from Data1 and/or Data2) into the
string `-10000 - id` (the formatter's table is LIKELY Speech.str); otherwise
the record's string id `+0x0F` resolves as: below -10000 Speech.str
(`-10000 - id`), -128..-1 the level table (`-1 - id`), 1..9999 Text.str.

## 5. Clicks (left/right STRONGLY INFERRED; bodies CONFIRMED)

Input channel 0xF0 reaches the hand's primary click and 0xF1 its secondary
([0x00406530](functions/00406530.md)); the widget manager sends 0xF0 to source
`+6` and 0xF1 to source `+0xA` ([0x0052C0A0](functions/0052C0A0.md),
[0x00528610](functions/00528610.md)).

**Left click** ([0x00423F50](functions/00423F50.md)), refused while Options,
the objective panel, the Ally panel or `+0x272A` is open: copies the n-th tabbed
record (same filter, no cap) to interface `+0x16A4`, sets opened bit 8, shows
menu40, and loads the message text into its text widget
([0x0052A330](functions/0052A330.md), scrollbar when it overflows). It then
sizes menu40: text measured in a 1096x288 box converted by `0x0052C5A0` (unread, LIKELY a
quarter-unit scale); if it fits strictly, height = 4 * text height + 172, else 440; y = panel top
(`+0x12D2`) - height - 168 when `+0x10C2 == 1`, else - 40 (`0x00424163..0x00424174`; overflow
y - 608 or - 480). `+0x10C2` is set while any indicator widget 0x2D..0x32 shows
([0x00427BA0](functions/00427BA0.md)); their row at y 1332 is one 128-unit step, so the panel
clears it (LIKELY). **No speech is replayed, no camera moves, no sound plays, no
packet is sent.**

**Right click** ([0x00424220](functions/00424220.md)): sets dismissed bit 4 on
the n-th tabbed record (its tab disappears next frame), hides menu40 if it is
showing that message, and sets `+0x4658 = 6`. No sound, no packet.

## 6. The message panel (menu40, `0x00693B08`)

Menu40 is shared with the creature Query (`+0x4B1D == 1`). In message mode it
shows ([widgets](functions/004246B0.md) at `0x00691EC8`, 88x88, y 20):

| x | art | callback | effect |
|---:|---|---|---|
| 88 | 142 mt-exit | [0x00424790](functions/00424790.md) | dismiss (bit 4) and close menu40 |
| 264 | 167 mt-tick | none | close menu40 (record `+0x12` close flag) |
| 440 | 133 mt-goto | [0x00424720](functions/00424720.md) | camera to the message location |

The goto button shows only when [0x004C4BE0](functions/004C4BE0.md) gives the
record a valid location ([0x00424660](functions/00424660.md)): per type, tile
(Data1, Data2), a live thing named by Data1 or Data2, a room's centre, or the
Dungeon Heart. Its click converts tile to world (`tile * 4096 + 2048`,
[0x004C3DC0](functions/004C3DC0.md)) and submits it through
[0x00415B30](functions/00415B30.md). Text is drawn by
[0x004242F0](functions/004242F0.md).

## 7. Review Objective (CONFIRMED)

[0x004288C0](functions/004288C0.md) toggles menu33 (full screen, (0,180)
2560x1280), unless Options is open. Opening saves and hides menu40; closing
restores it. It also closes the Ally panel and `+0x272A`. Menu33's text
([0x004207E0](functions/004207E0.md)) is Text.str 1408 "Main Objective", the
level briefing string 2, Text.str 1409 "Sub Objective", briefing strings 3, 4
and 5, scrolled past 250 units. The briefing table is `0x00741558`, loaded from
resource table `0x006BC100` (entry 1 = 1000 = LEVEL1_BRIEFING.str). No speech
replays. A menu33 button reuses 0x004288C0 to close it.

## 8. Built (ADR-0525, 2026-10-21)

`MessageTypeTable` holds the 100 rows' string references and graphics and the 28 variant lists,
copied from the bytes at `0x006A0B00` and `0x006A0000..`. `MessageTabsView` captures the tabs as
section 2-3 read them, keeping the opened and dismissed bits and the slide stamps out of the
simulation (first seen by the view stands in for the record's `+0x2C` stamp). `ClassicMessageTabs`
draws Review Objective and the slots, puts the hovered message's text on the status line, opens
the message panel on a left click and dismisses on a right click; the panel has exit and tick, and
Review Objective opens the briefing. The panel takes the text height in virtual units as four
quarter units (so `< 288`, `+ 172`). One owner DK2 shot (2026-10-22, no indicator
widgets up) shows menu40 40 above the strip, about x 1012..2228, its text at the top and exit/tick
under it; the box, offsets and colours are fitted to that shot (HYPOTHESIS for other states). The
indicator widgets are not built, so ours always takes 40. Not built:
types 17, 18 and 20, whose string `0x004C4600` computes from the creature's definition
(`def+0x6CD`/`+0x6D1`), so an unhappy or angry creature's tab has no text yet; the slide sounds, the Ally button, the goto button
(`0x004C4BE0`'s per-type locations), player-name substitution for 82..87, menu40's own frame art
(the panel is a dark box) and its sharing with the creature Query. Only the kinds our queue posts
can tab (computer-keeper-messages.md).

## 9. Open

- Sound bank of 0x320-0x322 and the argument `[0x006ACB34]`.
- Writer of `0x00759C29`; producer of graphic 4.
- The status-line placement of hover text versus a popup, as for other widgets.
