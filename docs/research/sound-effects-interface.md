---
systems: [audio]
---
# Sound effects: the interface and global requests of phase W8

Read 2026-11-03 for the last phase of [the world-sounds plan](../plans/2026-10-31-world-sounds.md). These are the requests the
slot scan of the sound manager (`0x0075B468`, [sound-effects.md](sound-effects.md)) found outside any thing's code: widgets, the
front end, the player's per-turn update. Nothing here is decompiler text (ADR-0046). Request shape as in
[sound-effects.md section 6](sound-effects.md#6-the-hand-and-its-cursor): slot `+0x24` is `(handle, category, event)` without a position, slot
`+0x20` adds one. Every claim carries its label.

## 1. The GUI sound table `0x006AC8C4`

STRONGLY INFERRED (disassembly `0x0052BA68` to `0x0052BAAE`, the loader inside [Gui_LoadMenuTree](../research/functions/0052B8F0.md); consumers
[0x0052CAD0](../research/functions/0052CAD0.md), [0x00412260](../research/functions/00412260.md)). The table is 0x30-byte records starting at
`0x006AC8A0`: the record's own number at `+0`, its category name at `+4` (32 bytes), the category handle the loader stores at `+0x24`, the press
sound at `+0x28` and the release sound at `+0x2C`. The loader runs once (record 0's handle word is its "not loaded" flag), takes the records
whose number is below 27 and asks the manager's category lookup (slot `+0x1C`) for each name under `GLOBAL\`. Every reader indexes the handle
at `0x006AC8C4 + slot * 0x30`, so a widget's sound slot is the record number. The record number matches the slot for all of 0 to 36 (read
from the data), which settles the pairing: earlier notes paired a slot's ids with the name 0x30 bytes later (one record too far). Independent
check: the front end's own filler at the tail of [0x00535A30](../research/functions/00535A30.md) (`0x00535B92` to `0x00535BD2`) walks the same records and, for
numbers `0x1B` to `0x24` (27 to 36), stores the front-end category handle `+0x5AB9` (FRONT_END) in record `0x20` (32) and the parchment's `+0x5AC1` in the
others, which is the table below. STRONGLY INFERRED.

| Slot | Category | Press / release | Used by |
|---|---|---|---|
| 1, 2, 3, 4 | `gui_button_tab_rooms`, `_spells`, `_workshop`, `_creature` | `0x104` / `0x136` | tabs; the tab flash's chime (section 2) |
| 5 | `gui_button_icon` | same | 128-unit category slots |
| 6 | `gui_button_icon_large` | same | 264-unit category slots |
| 7 | `gui_button_size` | `0x104` / none | the collapse button; the cheat entry's three sounds (section 3) |
| 8 to 12 | `gui_button_info`, `gui_sell`, `gui_button_options`, `gui_button_zoom`, `gui_minimap` | same | |
| 13, 15 | `gui_message_bar`, `gui_button_tab_new_combat` | none | their own requests (`0x320` to `0x322`; the battle fly-in's `0x32B`, `0x32C`, section 10) |
| 14 | `gui_button_default` | `0x104` / `0x136` | |
| 16 to 26 | `options` | `0x304`/`0x305` (16), `0x306` to `0x30B` (17 to 22), `0x30D`, `0x30E` (23, 24), `0x341` (25), `0x33C` (26); release none but 16 | in-game: Save and Load (25, 26, section 6); slots 16 to 24 have users unread |
| 27 to 30 | `object_fe_parchment` | `0x304`/`0x305` (27), `0x306`, `0x307`, `0x308` | 27: every front-end slider and scroll bar but speech (section 4); 28 to 30: no widget of ours |
| 31 | `object_fe_parchment` | `0x309` | no widget of ours; unmapped in `FrontEndMenuController` |
| 32 | `front_end` | `0x30A` | main menu labels |
| 33 to 36 | `object_fe_parchment` | `0x30B` to `0x30E` | page buttons and back icons |

STRONGLY INFERRED against the shipped maps: `options` is the only category with `0x33C` and `0x341`; `front_end` has `0x30A`, `object_fe_parchment` has
`0x30B` to `0x30E`. Corrected in our code (2026-11-03): the category slots' press and release were asked in `gui_button_icon_large` whatever the
grid; a 128-unit grid gives its slots sound slot 5, `gui_button_icon`, whose `0x104` is a different sample from the large icon's (the shipped
maps: archive 1 entry 38 of 408 ms against entry 39 of 733 ms, one-based as the maps store them). The press now follows the panel's grid, as the chime does. The front end's main-menu click
is asked in `front_end` (same sample as the parchment's `0x30A`: both resolve to the zero-based runtime entry 7 of `Global\Gui`, the one-based entry 8 of the maps).

## 2. The flash chime `0x33A`

STRONGLY INFERRED (disassembly `0x00412579` to `0x004125C3` and `0x00413A70` to `0x00413AA8`; [tab-alerts.md section 2](tab-alerts.md)). Sound `0x33A`,
nonpositional, handle 0, in the category of the flashing widget's sound slot (section 1). Both the tab flash and the slot flash test and then
write the interface's latch `+0x4F0D`: with the half-second phase (real clock divided by 500, bit 0; 1 is the dark half) zero, the latch
non-zero and the game not suspended (`[[interface+4]+0x284]` zero) they ask the sound; every flashing draw then stores its phase in the latch.
So one chime sounds at the start of each bright half-second after a dark draw, whichever flashing widget draws first, and not at all while
Options pauses the game. Nothing else writes the latch (store scan), so a flash that starts in a bright half is silent for that half.
STRONGLY INFERRED: the latch starts at zero (no store at construction was found; the interface is allocated zeroed, unread); that tabs draw before
slot panels, so a tab flash and a slot flash drawn in one bright frame use the tab's category.

All six categories have `0x33A` with one sample (entry 49 of 740 ms, the same in each), so the slot makes no audible difference. Built:
`FlashChime` (latch), `ClassicBottomPanel` (tab loop, `DrawPanel`), paused while Options is open. Tests: `InterfaceSoundTests`,
`InterfaceSoundMapsTests`.

## 3. Cheat entry `0x133`, `0x134`, `0x135`

STRONGLY INFERRED (disassembly; [cheats.md](cheats.md), [0x0040A600](../research/functions/0040A600.md), [0x0040A680](../research/functions/0040A680.md)),
all nonpositional, handle 0, `gui_button_size` (slot 7, which has all three groups):

| Sound | Site | When |
|---|---|---|
| `0x134` | `0x0040A66D` | Ctrl+Alt+C opens entry (all eight phrases armed) |
| `0x135` | `0x0040A707` | a typed character leaves no phrase armed: entry closes |
| `0x133` | `0x0040A736` | a phrase completes, before it fires |

Waits: the typed entry of ADR-0589 (accepted, not built). The remake has no cheat entry to attach them to. When it is built, the three requests
belong to its open, mismatch-cancel and accept.

## 4. Slider test sounds `0x345`, `0x346`, `0x31D`

STRONGLY INFERRED (disassembly [0x0042B950](../research/functions/0042B950.md), [0x0042B9F0](../research/functions/0042B9F0.md),
[0x0042BA90](../research/functions/0042BA90.md), [0x0054D180](../research/functions/0054D180.md), [0x0054D240](../research/functions/0054D240.md),
[0x00551F20](../research/functions/00551F20.md)). Each is the tail of a slider's change callback. When the widget's state word `+0x41` is 3, the widget's flag read by virtual `+0x50` is clear and the page's handle for the sound is zero, it asks the sound with handle 0, keeps the
returned handle and sets the flag through virtual `+0x4C`; when the state is no longer 3 and the flag is set it clears the flag and sends the stop command to the kept
handle (slot `+0x38`; `0x00551FE0` names its command object `0x0066C77C`, the world's stop). The flag's meaning (STRONGLY INFERRED: "held") is not read.

State 3 is a **thumb grab**, not any press. In the slider's per-update input function (`0x00529510`, the horizontal sibling of the vertical scrollbar update
[0x00528B20](../research/functions/00528B20.md), whose record says "Thumb: state 3") the state is set to 3 only at `0x0052974A` to `0x00529764`: the pointer is inside the thumb
rectangle (widget `+0x90` to `+0x9C`), the button is down and the previous state is 0. A press on the track outside the thumb gives states 4 then 5 (a one-step
page move) and the arrows states 1 and 2; all are silent for the test sound. Release clears the state, which stops the sound. STRONGLY INFERRED for the function
(disassembly); STRONGLY INFERRED that the callbacks above belong to widgets of this class: the callbacks call the common slider helper `0x005501F0`, and
`0x005507E0` is the hover helper both classes share. So the sound starts at the grab and is cut at the release.

The same grab block asks the widget's own table press through [0x0052CAD0](../research/functions/0052CAD0.md) with part 0 (`0x00529787`), and the release asks the release
(`0x005298C1`). The front-end sliders carry slot 27 (every one but Speech, widget 141, which has slot 0 and so no table sound): press `0x304`, release `0x305` in
`OBJECT_FE_PARCHMENT`, and both groups have no items there (data), so the table press is silent and nothing is built for it. The scroll bars carrying slot 27 are
silent the same way.

| Slider | Front end | In game | Sound |
|---|---|---|---|
| Speech volume (`0x00563490`) | `0x0054D180` | `0x0042B950` | `0x345` in `OPTIONS_SPEECH` (10 samples) |
| Music volume (`0x005635B0`) | `0x0054D240` | `0x0042B9F0` | `0x346` in `OPTIONS_MUSIC` (16 samples) |
| Effects volume (`0x00563520`) | `0x00551F20` | `0x0042BA90` | `0x31D` in `OBJECT_FE_PARCHMENT` (3 samples) |
| Master volume, gamma, game speed, scroll speed, sensitivity, shadows | `0x00551F20` (the pairing of callbacks and widget ids is STRONGLY INFERRED from the settings each callback stores) | none | `0x31D` in `OBJECT_FE_PARCHMENT` |

The categories are loaded by name at the page's construction (front end `0x00535A67`, `0x00535AC0`, `0x00535B7D`; in game `0x00402FE0`,
`0x00403016`, `0x0040302E`). The front end's other users of `0x00551F20` are multiplayer pages (not built); `0x00551FE0`, the same logic for a type 8
widget, is called by two multiplayer callbacks. `options` itself has `0x31D` with no items, so a lookup there would be silent: the parchment is the
category. Built in `OptionsPagesController.BeginSliderDrag` and `EndSliderDrag`. The callers begin a drag only for a press on the thumb: the front-end menu tests the
pointer against `SliderThumb.Rect` (the drawn thumb), the in-game panel registers the begin on its thumb element only, not on the track or arrows. Both drain `Sounds` through the shared player. UNKNOWN: which volume class (speech, music, effects) the manager gives `OPTIONS_SPEECH` and
`OPTIONS_MUSIC`; ours plays all three at the effects volume, so a speech test is silent with the effects switch off (the manager's request slot
refuses while the SFX switch is off, [0x00567810](../research/functions/00567810.md)).

## 5. The campaign map's land sound `0x32A`

STRONGLY INFERRED (disassembly [0x00538D00](../research/functions/00538D00.md) at `0x00538FA1` and `0x005390AE`, [0x004A35E0](../research/functions/004A35E0.md)).
Nonpositional, handle 0, `OBJECT_FE_MAP` (one sample, entry 66 of 797 ms). The per-frame map pick asks it for a hovered land whose
arming word (land `+0x184`) is 1 and whose height is still at most its creation height plus `0x1040`, then clears the word and raises the land. A
hovered arrow does the same for the land it belongs to. The land's own per-tick update ([0x004A35E0](../research/functions/004A35E0.md)) sets the word to 1
and lowers the land eight units whenever no hover holds it (land tags 1 to 30 except 25). So the sound plays on the first frame of each hover
that follows at least one unhovered update, and not again while the pointer stays. STRONGLY INFERRED: the height test never fails once the word is
set (the same update lowers the land), and the front end runs the land update each frame (its rate is unread). Built in `CampaignMapController.Hover`
and its frame update (`Sounds`); the Unity front end drains it after the pointer update.

## 6. In-game Save and Load: `0x33C`, `0x341`

STRONGLY INFERRED (disassembly). Both are nonpositional, handle 0, in `options` (slots 26 and 25). `0x33C` is the end of
[Options_SubmitSaveName](../research/functions/0042A920.md) (`0x0042A9D8`), after the file is written and the list refreshed. `0x341` follows a load request:
[Options_SelectLoadFile](../research/functions/0042A7D0.md) stores the chosen path and sets the interface word `+0x3990`; the interface's per-frame
input function ([0x004036E0](../research/functions/004036E0.md), site `0x00403EA6`) hands the path to the session object (virtual `+0x2C` of the object
at interface `+0x53`) and asks `0x341`. The in-game slots 16 to 24 (`options` `0x304` to `0x30E`) have users that are unread. Waits: the in-game Save and Load pages (menus 23 and 24) are not built
([options-menu.md](options-menu.md)); `InGameOptionsPage` has none of them.

## 7. The speech line and its duck

STRONGLY INFERRED (disassembly [0x00568490](../research/functions/00568490.md)). The site `0x00568531` is the speech queue's line start: slot `+0x24` with the
queue's stored handle, the line's category and the absolute value of the line's event. That is the request the Mentor queue
(`SpeechQueue`, `ClassicSoundPlayer.UpdateSpeech`) already makes, built in phase C4; the throttle mode around it is in
[sound-effects.md section 4](sound-effects.md#4-slow-frame-throttle-and-unverified-playback-budgets).

Not built before: the line also ducks the other volumes. Before the request it sends two commands (slot `+0x38`, command objects `0x0066F964` and
`0x0066F96C`) carrying the music setting (`0x0075AD17`) and the effects setting (`0x0075AD13`) times four over six, in whole percent, unsigned; when
the line's handle is dead ([0x005681D0](../research/functions/005681D0.md), `0x00568238`) it sends them again with the full settings. The commands
(`0x00608580`, `0x00608520`) set a volume class to the value times 0.01. STRONGLY INFERRED pairing: the music setter (`0x005635B0`) and the effects setter (`0x00563520`) call manager slot `+0x54` with classes 1 and 2; the slot's jump table
(`0x00567D5C`, index bytes `0x00567D80`) builds exactly the command objects `0x0066F964` and `0x0066F96C` the duck sends, and `0x0075AD17` is the music
setting, `0x0075AD13` the effects setting. STRONGLY INFERRED only: ambience and terrain beds sit in the effects class. Built as `SpeechDuck.Percent` and
`ClassicSoundPlayer`: while the Mentor line plays, music, ambience and effects run at two thirds of their settings, restored at its end.
DK2 restores only when the line's handle is non-zero and dead, so a refused request (no handle) leaves the volumes ducked; not mirrored, harmless
(ours restores when the Mentor source stops). The party music's duck of W7 is a different request and stays open.

## 8. Mana `0xF`, Horny `0x1`, party pop `0x344`

- **Mana `0xF`** (`0x004C1226`, [Mana_UpdatePlayer](../research/functions/004C10E0.md)), STRONGLY INFERRED site: in the once-a-second payment, when the player's mana
  reaches zero and the turn is past the stamp `+0xAE`, it asks `0xF`, nonpositional, handle 0, in the category of room definition 5 (the Dungeon Heart;
  definition `+0x449`), then moves the stamp on by the variable at `0x006F28ED` seconds. It has no local-player test, so every keeper that runs dry asks.
  Silent: no category of the Global maps has group `0xF` (data), so DK2 plays nothing there either. The warning the player does see is the
  message of type 52 (already built as `LowManaWarning`).
- **Horny `0x1`** (`0x004B9BD6`, [Player_UpdateFlagNotifications](../research/functions/004B9740.md)), STRONGLY INFERRED site: the world's category at `+0xA3B7`
  (loaded first, `0x0050A25A`) is `SPEECH_HORNY`, not an effects category, and `0x1` is its group 1 (one sample). It is asked once (latch: player flag
  `+0x76` bit `0x10000`) when, with no result on the player, world option bit 4 set (`+0x72A1`), the player's table of 0x11-byte records holds key 5 with
  value 3 (STRONGLY INFERRED: the availability of spell 5, the Horned Reaper) and the mana
  affordability helper ([0x004C1740](../research/functions/004C1740.md)) passes for the spell's cost. No local-player test either. Waits: that function's body
  is not mirrored (the Mentor hint posts of types 88 to 95 are the same function), and the flag's other readers and writers are unread, so a
  presentation-side detector would fake the trigger.
- **Party pop `0x344`** (`0x004BA0A8`): already built in W3 (`PartyPopSound`, [sound-effects.md section 9](sound-effects.md#9-workshop-casino-prison-and-portal)).

## 9. The front end's atmosphere and music request: `0x2EA`, `0x155`

STRONGLY INFERRED (disassembly [0x00537290](../research/functions/00537290.md), called from the front-end branch of
[GameSession_Run](../research/functions/00525370.md) at `0x005257CC`, once per run of that branch, STRONGLY INFERRED). If the one-shot-atmosphere handle is zero it asks `0x2EA` in `ONE_SHOT_ATMOS`; if the
music handle is zero it asks `0x155` in `MUSIC`; each is nonpositional, handle stored, never stopped by the front end. `0x2EA` is group 746 of music.md
(19 samples, volume 21, delays of 8 to 20 s). `MUSIC` has groups `0x157` and `0x159` only, so the front end's `0x155` finds nothing: silent (data;
the in-game ambience is `0x155` in `AMBIENCE`). So the front end plays no music through this site. Waits: the one-shot atmosphere is a
delay-chained player (flags `0x0204`, constructor `0x00615EE0`, vtable `0x006740B0`, [0x0060DA70](../research/functions/0060DA70.md)) whose body is unread:
what the delay is measured from, whether it repeats and what the volume 21 scales are UNKNOWN, so no player exists to start. The in-game manager
starts the same group (music.md section 3, HYPOTHESIS); both are the same missing player.

## 10. The battle fly-in `0x32B`, `0x32C`

STRONGLY INFERRED site and gates ([battle-tab.md section 5](battle-tab.md), [0x0042C0D0](../research/functions/0042C0D0.md), [0x0042C390](../research/functions/0042C390.md)):
`0x32B` at `0x0042C102` (graphics detail at least 3, no fly-in running) and `0x32C` at `0x0042C66E` (the overlay reports done, the camera is not letterboxed),
nonpositional, handle 0, slot 15 (`gui_button_tab_new_combat`; the data has both groups there, `0x32B` with two samples). They were listed as "tab
scroll"; they are the notice that flies in when a fight starts. Waits: the Battle tab and its fly-in overlay (`0x0042F040`) are not built; only
the camera cycle (`BattleNavigation`) is. The latch (`+0x4D75`) outlives the sound, so building the start without the end would change when a
second fly-in may play.

## 11. Status of the plan's items

| Item | Status |
|---|---|
| cheat entry `0x133` (and `0x134`, `0x135`) | waits: the typed entry (ADR-0589, not built) |
| icon pages `0x33A` (`0x004125A7`, `0x00413AA3`) | built now (`FlashChime`); the press category of the slots corrected |
| tab scroll `0x32B`, `0x32C` | waits: the Battle tab fly-in overlay |
| front-end map `0x32A` | built now |
| options `0x33C`, `0x341` | waits: in-game Save and Load pages |
| speech and music tests `0x345`, `0x346`, `0x31D` | built now (front end and in game), on a thumb grab; the slot-27 table press is silent |
| mana `0xF` | silent: no group in any Global map; nothing to build |
| `0x1` (`0x004B9BD6`) | waits: `Player_UpdateFlagNotifications`, Horny's category |
| effects `0x344` (`0x004BA0A8`) | already built (W3) |
| one-shot atmosphere `0x2EA` (`0x005372B3`) | waits: the delay-chained player, unread |
| speech-path site `0x00568531` | already built (C4); the duck built now |

## 12. Unknowns

- Which volume class `OPTIONS_SPEECH` and `OPTIONS_MUSIC` use (section 4).
- Whether ambience and terrain beds sit in the effects class the duck scales (section 7).
- The delay-chained player of the one-shot atmosphere (section 9).
- The latch `+0x4F0D` before the first flash, and the draw order of tabs and slots (section 2).
- The land update's rate in the front end (section 5).
- Whether the spell announcement (section 13) is requested on the slot's press or release.

## 13. Selecting, arming and cancelling a keeper spell

Read 2026-11-05 for the owner's report that DK2 plays a sound on selecting a spell that the remake lacks. Disassembly and decompile of
[0x00411140](../research/functions/00411140.md), [0x00424F50](../research/functions/00424F50.md), [0x004075E0](../research/functions/004075E0.md)
(tool mode 8 arm), [0x0040B7F0](../research/functions/0040B7F0.md), [0x0040E5E0](../research/functions/0040E5E0.md),
[0x004094C0](../research/functions/004094C0.md), [0x00408EE0](../research/functions/00408EE0.md); the shipped maps and `KeeperSpells.kwd`.

**The one request (STRONGLY INFERRED).** At `0x00411342` to `0x00411356` of [SpellPanel_ClickSlot](../research/functions/00411140.md), after the spell is selected
(`+0x13DB` = 8, `+0x13E8` = spell id), it asks slot `+0x24` with the interface's own handle `+0x2E` (stored back), the category at the runtime
spell's `+0x71` (the file record's `0x174`, `GUI_BUTTON_<SPELL>`, section 1 of [sound-effects-combat.md](sound-effects-combat.md)) and event `0x104`,
nonpositional. It runs for every spell of the panel except the two the list skips (ids 5 and 14, the reaper pair) and for every click that
reaches the spell arm (also while the spell is already armed), before the GUI-button post (`0x00422600`). Nothing else reads `+0x71`
of a spell record (store and read scans of `+0x71`).

The shipped samples are the spoken spell announcement, 3.2 to 4.4 s, one per category (group `0x104`, priority word `0xF9F`, against `0x32` of the
slot's own `gui_button_icon` click): Sight of Evil (id 1) 3214 ms, Possession (2) 3229, Call to Arms (3) 4277, Create Imp (4) 3375,
Thunderbolt (6) 4389, Turncoat (7) 3176, Tremor (8) 3336, Inferno (9) 4213, Heal (10) 3457, Chicken (11) 4001, Create Gold (13) 3873 (priority 1).
Summon Horny (5) and OneShotReaper (14) name `GUI_BUTTON_SUMMON_REAPER` (4144 ms) but never reach this arm: the Reaper button's
selector [Hud_SelectReaperSpell](../research/functions/00424F50.md) (mode 8, spell 5 or 14) asks no sound, and the Library specials
([0x0040A280](../research/functions/0040A280.md)) ask none either. `gui_button_reaper_talisman` (`0x104`, 1718 ms) has no reader (no string in the
executable or the KWDs). The handle is never stopped (the cancel path sends no stop command), so the announcement plays out; the reset at
`0x00403608` only forgets the handle. A live handle retains an equal or lower priority request (sound-playback.md), so a second spell click inside the
announcement is dropped, except one with a higher priority word (any spell after Create Gold).

**Silent, by reading (STRONGLY INFERRED).** The armed tool's per-frame feedback [0x0040B7F0](../research/functions/0040B7F0.md) and the dispatcher
`0x0040BBE0` ask nothing; the cursor request `0xC` (Idle.png, at `0x0040C1AA` on selection and in the callbacks) has the plain callback. The cast click (mode 8
arm of [Hand_Click](../research/functions/004075E0.md)) posts action `0x36`, `0x37`, `0x38` or `0x87` and requests state 13
(SpellCast.png, 12 frames, 30 a second) except for spell 2; its callback [0x0040E5E0](../research/functions/0040E5E0.md) (event 0 adopt, event 2 return to `0xC`) asks no
sound; the cast's `0xEF` is the simulation's own request ([sound-effects-combat.md](sound-effects-combat.md) section 1, built). A cast without enough
mana, or a refused target, returns without sound (`0x004C1740` is a pure compare). State 14 (SpellPossess.png) has no literal request among
the call sites of `0x0040ABC0` (scan of the last pushed literal) (variable-argument sites are the pickup arms); its callback is the plain one. Cancel (right press,
`0x00408EE0` then [Hud_CancelSelectedTool](../research/functions/004094C0.md)) and choosing another tool ask no sound and stop none. No Mentor line is asked by
any of these; action `0x72` only fires trigger event 53 for level scripts. The hover sound over one's own creature
([0x0040C290](../research/functions/0040C290.md), section 6 of sound-effects.md) runs in every tool mode, mode 8 included, as built.

**Ours.** `BottomPanelView.PressChoice` and `ReleaseChoice` play the slot's `gui_button_icon` `0x104`/`0x136`: DK2's widget table sound, which stays (no `0x136`
group exists in `gui_button_icon`, so the release is silent in both). The spell announcement is built (2026-11-05):
`KeeperSpellDef.GuiButtonSoundCategory` reads file `0x174`, and `BottomPanelView.Select` asks `0x104` in it on one
voice (`SpellAnnouncementVoice`, the handle `+0x2E`) for every spell but 5 and 14. Waits: where in the press/release
the handler runs (the widget's callback, record `+5` of the widget at `0x0068FDF8`, fires from its click path, unread); ours selects on release.
