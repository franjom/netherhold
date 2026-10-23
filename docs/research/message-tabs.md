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
y - 608 or - 480). `+0x10C2` is set while any indicator menu 45..50 shows (section 9); their
row at y 1332 is 128 tall, so the panel clears it. **No speech is replayed, no camera moves, no sound plays, no
packet is sent.**

**Right click** ([0x00424220](functions/00424220.md)): sets dismissed bit 4 on
the n-th tabbed record (its tab disappears next frame), hides menu40 if it is
showing that message, and sets `+0x4658 = 6`. No sound, no packet.

## 6. The message panel (menu40, `0x00693B08`)

Menu40 is shared with the creature Query (`+0x4B1D == 1`). Its static record is (1020, 200)
1200x440; 0x00423F50 rewrites y and height (section 5), never x or width. In message mode it
shows ([widgets](functions/004246B0.md) at `0x00691EC8`, 88x88, local y 20 from the bottom:
anchor value 4, [0x00528570](functions/00528570.md), so their top is 108 above menu40's bottom):

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
[0x004242F0](functions/004242F0.md) in the text widget `0x00692008`, local (20,20) 1096x288
(its own inset inside that box is unread).

**Frame.** Menu40's draw callback (static record `+0x1E`) is
[0x00417EA0](functions/00417EA0.md), shared by menus 36, 37, 51 and 52. CONFIRMED: it converts the
menu rectangle to screen pixels, insets it by 10 pixels and calls
[0x00411D00](functions/00411D00.md) with flags 8, which draws the nine
`gui\windows\text_box-{tl,t,tr,l,m,r,bl,b,br}` pieces (catalogue 19..27, resource offsets
0x201..0x2D9): corners 16 pixels square outside the inset rectangle, edges stepped 16 pixels and
cut at the far end, the middle stepped 32 pixels and clipped to the rectangle. So the frame
overhangs the menu by 6 device pixels, not scaled with resolution. STRONGLY INFERRED, not read:
that flags 8 (the blit at view slot `+0x5C`) draws the art's alpha with no tint; that the pieces
are the 16x16 cache images `GUI\Windows\text_box-*MM0` (world-hover-text.md); and that the 32-pixel
middle step draws that 16x16 piece stretched to 32. The owner's fitted box (x 1012..2228 at
1604x1202) is the menu's 1020..2220 plus the overhang within the fit's error (8 units fitted
against about 9.6). The callback does not depend on the mode, so the Query uses the same frame;
Query mode's own widgets are not read here.

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
quarter units (so `< 288`, `+ 172`). Menu40 is the static (1020, y) 1200 wide with the
text_box frame of section 6 (`HudIndicatorLayout.MessagePanel` / `FrameInner`), exit, tick and goto
108 above its bottom; the device-pixel frame sizes are scaled by height / 1200 (our choice: the
owner's 1202-line reference, where it equals DK2). The text stays where it was fitted to the owner's
DK2 shot (2026-10-22, no indicators up): x 1054, 32 below the top (HYPOTHESIS: 0x004242F0's inset is
unread). The indicators of section 9 set the 168 offset. Types 17 and 18 take
Speech `base + complaint`, base the creature's unhappy or angry line (`def+0x6CD`/`+0x6CF`, file
`0xD05`/`0xD07`; `0x004C4600`), fixed when the view first sees the record. The goto button (art 133, x 440) shows when
`0x004C4BE0` gives a tile, a live creature or a room centre (the box middle, Q515), and moves our
focus there: `0x00415B30` submits game action 4 with the point's tile, a glide of at most
500 ms to its centre (`0x00442650`). Not mirrored: the thing-kind check and the Query-mode hide
(`+0x4B1D`). Type 42 shows no goto in either game: DK2's producers post it with Data2 0
(computer-keeper-messages.md's producer table; only 44 carries the room), which names no room (STRONGLY INFERRED: thing slot 0 is never a room).
Complaint 9 (cell, Temple, spectating; state-categories.md §7.2) reads one past a creature's
nine lines, so its 17/18 text names the next creature type's first complaint, in DK2 as in ours:
the Imp's 585 + 9 is Speech 594, "A Bile Demon has become unhappy because it has no Lair". Not built: type 20
(`def+0x6D1`, file `0xD09`, "has left your Dungeon"), which our queue never posts; the slide sounds, the Ally button, the goto
location of a kind-2 thing (22, 26, 48) or the Heart (85..87), none posted by us, player-name substitution for 82..87, and menu40's sharing with the creature Query. Only the kinds our queue posts
can tab (computer-keeper-messages.md).

## 9. Indicator menus 45..50 (CONFIRMED; built in part)

[0x00427BA0](functions/00427BA0.md) runs each frame. It tests six world values and shows a 128-tall
menu for each that passes; the first shown sits at (20, 1332), and each next is placed right after the
previous one's width at panel top - 128 (the same row while the panel is expanded). In camera class 1
(`0x0044D870`: modes 1/2/10/11) they stack upward from 1332 instead. Any shown sets `+0x10C2`, which
lifts menu40 to 168 above the strip (section 5).

| menu | label (Text.str) | shows while | art | draw |
|---:|---|---|---|---|
| 45 | 1671 Time Remaining | LevelTimeLimit `+0x72AA` != 0 | timer-bg | [0x00427230](functions/00427230.md): m:ss |
| 46 | 82 Score | LevelScoreSet `+0x72A6` != 0 | score-bg | [0x004273D0](functions/004273D0.md): LevelScore |
| 47 | 1673 Current Holder | SpecialInfoPlayerId `+0x72B6` != 0 | kingofhill-bg | [0x004274C0](functions/004274C0.md): player name |
| 48 | 1672 Heroes Killed | LEVEL flag 0x80 (global `0x006F2957` bit 5, [0x0051E6D0](functions/0051E6D0.md)) | kills-bg | [0x00427770](functions/00427770.md): player `+0x14A` |
| 49 | 1681 Slaps Remaining | MaximumSlaps `+0x72B8` - player slaps >= 0 | slaps-bg | [0x00427870](functions/00427870.md): Max - player `+0xEE6` |
| 50 | 2127 Next Room | SpecialInfoNextRoomTypeId `+0x72BC` in 1..25 | (art 0) | [0x00427980](functions/00427980.md): two icons |

Widths are 444 (256 art plus [0x00427010](functions/00427010.md)'s gold-bg-01 188 to its right),
256 for 49 and 50. Digits are [0x00415650](functions/00415650.md)'s, white, 40 below the menu top,
right-aligned to slots of 40. `+0x72B8` starts at -1 (0x0050A2A5), so Slaps shows only after action
50 sets a limit. In the shipped levels: action 23 (time limit) in Levels 6b, 9 and 10, the Secrets and
MPDs; LEVEL flag 0x80 in Level17, Secret2, Alcatraz, Spiral and the seven MPDs; action 47 (next room)
and action 50 (slaps) only in MPDs and Secret1; action 7 on id 128 (score) in 21 records, levels not
listed. Our simulation has no
slot `+0x344` adder ([World_AddLevelScore](functions/00511650.md), floored at 0, trigger-state.md §4), so where DK2's level
adds to the score through it, ours can show a different score.

**Built (2026-10-23).** `HudIndicatorSnapshot.Capture` makes the tests on what our simulation holds
(time limit, `SpecialValueWritten`, level flag 0x80, `SpecialInfoPlayerId`, `MaximumSlaps` minus the
player's slaps, next room). `HudIndicatorLayout.Place` packs them; `ClassicMessageTabs` draws all six
boxes: Time Remaining and Score digits, Current Holder's name (ping-pong scroll when it overflows; the
Players-record name, else "Good Player", "Neutral Player" or "Keeper N"), Heroes Killed's count, Slaps
Remaining's digits and Next Room's icons. Camera class 1 is not mapped to our camera, so ours always
packs in a row.

**The four other boxes (read 2026-10-23).**

- *Heroes Killed* draws the local player's dword `+0x14A` (HeroesDestroyed), 10 slots. Player_Initialise
  zeroes it ([0x004B8640](functions/004B8640.md)). It rises by one when an `IsGood` creature (definition
  `+0x686` bit 8) dies, credited to the player tagged in the victim's word `+0x36` (the last health-change
  source, stamped by `0x004B2CE0`), else to the owner of its first attacker (melee `+0x504[i]` before ranged
  `+0x514[i]`, i = 0..7); a victim with `+0x3E6` bit `0x40` counts nothing
  ([0x00487C70](functions/00487C70.md), CONFIRMED). No owner test: any keeper that kills a good
  creature counts it. At level end each player also counts the hero-owned creatures left dying whose `+0x36`
  names them ([0x004BE630](functions/004BE630.md)). In My Pet Dungeon mode each also adds
  MpdScoreHeroKilled to the score.
- *Slaps Remaining* draws MaximumSlaps `+0x72B8` minus the player's NumberOfSlaps `+0xEE6`, 6 slots (CONFIRMED).
  Player action 50 sets `+0x72B8` to its dword `+4` (`0x004C8A25`). Nothing but the HUD, the world dumper
  and `0x0050F3D0` reads `+0x72B8`, so the hand never refuses a slap; running out only hides the box
  (the level's own triggers can test condition 71).
- *Next Room* draws no background and no text: catalogue 263 `gui\icons\next_room`, then the GUI icon of
  room type `+0x72BC` (room definition `+0x00`, through [0x0050D0B0](functions/0050D0B0.md)), as 128x128
  squares side by side (art CONFIRMED, placement STRONGLY INFERRED). Its only writer is action 47.
- *Current Holder*: world action 46 (`0x00519755`) is the only caller of the setter
  [0x00509880](functions/00509880.md). Byte 00 zero clears the holder; otherwise the dword `+4` is a one-based
  tile (x low word, y high word) and the holder becomes that tile's owner (`+8 & 0xFFF`); a missing tile
  fails the action. CONFIRMED.

Built (ADR-0532): `PlayerState.HeroesDestroyed` credited in `CreatureHealthSystem.Die`/`CreditHeroKill`
from `CreatureState.HealthSourcePlayerId` (`+0x36`, stamped at each health-change call site), `DeathUncounted`
(`+0x3E6` bit 0x40, nothing sets it yet), `TriggerState.MaximumSlaps` (init -1) and `SpecialInfoPlayerId`
set by actions 50 and 46. Not built: the level-end pass of [0x004BE630](functions/004BE630.md) (its
`+0x146` and the `0x19E`/`0x173` tallies) and the My Pet Dungeon score for a hero kill.

## 10. Open

- Sound bank of 0x320-0x322 and the argument `[0x006ACB34]`.
- Writer of `0x00759C29`; producer of graphic 4.
- The status-line placement of hover text versus a popup, as for other widgets.
- For the owner, from play: does an opened message sit about 128 units higher while an indicator is up; do the Time Remaining digits read m:ss and does the last second hold at 0:01; does the frame keep its pixel thickness across resolutions (ours scales it, our choice); does Heroes Killed tick for heroes killed by traps, lava or the player's own creatures; does a long Current Holder name bounce, and keep its scroll offset across holders.
