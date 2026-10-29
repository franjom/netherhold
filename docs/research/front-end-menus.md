# Front-end menus: start-up, station state machine, menu tree, options

**Scope:** how DKII.EXE gets from program start to the main menu, how the 3D front end is
driven (pages, camera-path stations, the per-frame state machine), every menu page and item,
the hover/click model, the hand-off to a level and back, and the options pages.
Out of scope, with hand-offs only: the campaign map, unlock rules and profile
(`campaign-map-and-progression.md`), and the asset files (`front-end-assets.md`; camera path
file format: `camera-paths.md`).

Every row is a DKII.EXE v1.70 read unless it says otherwise, so the ceiling is **STRONGLY
INFERRED** (LIKELY in the function records); owner recollections below are not controlled original-game experiments. Labels are
`Data/Text/Default/Text.str` ids (file id 1091) decoded with `dk2tools str list`. Function
records: `docs/research/functions/<ADDRESS>.md`. Path names are the designers' own index
in `Paths.WAD` (`Engine Path Info.txt.txt`, entries 250-274).

## 1. Components and the application loop

| Fact | Provenance | Confidence |
|---|---|---|
| The application main `0x005A5DA0` parses the command line (`0x005A63B0`), initialises, then runs a chain of static components: call slot +0x0C (init), slot +0x14 (run, returns the next component), slot +0x10 (shutdown), until run returns null. The chain starts at the game-session object `0x0070D138` (vtable `0x0066EC4C`, run = `GameSession_Run 0x00525370`). | `0x005A6327-0x005A633B` | STRONGLY INFERRED |
| `CFrontEndComponent` is the static object `0x0070D6D8` (vtable `0x0066EF3C`: dtor `0x0052EF50`, one-time setup `0x0052EFE0`, init `0x0052F140`, shutdown `0x0052F550`, run `0x0052F9E0`); constructed by `0x0052E490`. Its +4 is "exit requested" and +8 "next component" (`0x00552C60` exits with none, `0x00552C80` exits naming one). | RTTI `.?AVCFrontEndComponent@@`, `0x00552BE0` | STRONGLY INFERRED |
| `CFrontEndComponent3D` is the static object `0x0070D5F0` (vtable `0x0066EF5C`, one slot: its destructor `0x00537FD0`); its only other users are `0x00537FB0`/`0x00537FF0`. Its role is not established. | RTTI `.?AVCFrontEndComponent3D@@` | UNKNOWN |
| **The 3D front end is a game session.** With `0x0075AEAF` = 1 and game mode `0x0075AD4B` = 5, `GameSession_Run` calls the front-end init `0x0052F140`, loads level `FrontEnd3DLevel` through the normal KWD loader, attaches no HUD (`0x006CEA10` is attached only when `0x0075AEAF` is 0), runs world turns, and each frame runs the front end's GUI input (`0x005B2AE0` with handler `0x0073E5F4`), `Gui_UpdateMenusAndCapture 0x0052BC50`, the active page's frame callback, the tick `0x00533D30`, and the overlay draw `0x005334C0`. It leaves when the front end's exit flag `0x0070D6DC` is set, and calls the front-end shutdown `0x0052F550`. | `0x00525370` | STRONGLY INFERRED |
| **2D front end.** `0x0075AEAB` = 1 skips the view component, the level load and the world loop: only the menu pages run, on 2D backdrops (`BackdropMain`... and `2Dlevel*` art, `0x00534210`), and every camera path below is skipped (the state jumps straight to its end state). Set by `-FE`, or at front-end init from `0x0075AEB7`, which start-up sets when the CPU benchmark `0x00559790` is under 240 (3D card present) or under 290 (none). `-FE3D` forces the 3D front end. | `0x005A63B0`, `0x005A5DA0`, `0x0052F140` | STRONGLY INFERRED |
| `CFrontEndComponent` run `0x0052F9E0` (a 2D-only loop) is reached only when a session returns `0x0070D6D8` itself, which `GameSession_Run` does when byte `0x006BCE48` is `'e'`. What sets that byte is unread. | `0x00525D0D` | UNKNOWN |

## 2. Start-up sequence

| Step | Where | Confidence |
|---|---|---|
| Default start (no `-LEVEL`/`-Q`, no `-PD`): game mode 5, level name `FrontEnd3DLevel`, 3D front end on. `-LEVEL name`/`-Q` set mode 1 and go straight into a level with no front end; `-PD` sets mode 4. | `0x005A61F8-0x005A6300` | STRONGLY INFERRED |
| First front-end init of the run (`0x0074035C` = 0) shows `TitleScreen\TitleScreen` (`-Japanese` for language 0x11, `-<language>` when not English) and **busy-waits 10 s** with no input polling (not skippable). | `0x005340F0` | STRONGLY INFERRED |
| Then it stops CD audio (MCI `cdaudio` stop) and plays `<movies>\BullfrogIntro.tgq` (`0x0053C020`), then `<movies>\Intro.tgq` (`0x0053BFD0`). | `0x0052F140` | STRONGLY INFERRED |
| Movie playback `0x0053BCD0` runs only when movies are on (`0x0075B170`, cleared by `-NOMOVIES`). Its key handler `0x0053C270` sets the skip flag `0x0073F598` on Esc (scan 1), Enter (0x1C) or Space (0x39); the decoder (`0x005A8F70` open, `0x005A8FB0` step) is in `movie-format.md`. The second handler `0x0053C2A0` sets the same skip flag on a mouse button. | `0x0053BCD0` | STRONGLY INFERRED (skip keys, mouse skip) |
| Then the front-end resources (`0x00534210`), the page tree (`Gui_LoadMenuTree 0x0052B8F0` over the table `0x006AE4E0`), and the return router `0x00534F70`: in its default branch (return page `0x00759B30` not one of the routed values) it first calls `0x005321A0(page)` when the page is not 1, then sets `0x00759B30` = 1 and state 1, which plays path 250 "Intro to 3D Front End" and then shows the main menu (page 1). | `0x00534F70`, `0x005384F0` | STRONGLY INFERRED |
| **Attract mode:** if the mouse has not moved for strictly more than 180 s and the active page is not 10, 11, 0x20, 0x23, 0x27 or 0x2C (multiplayer pages), the tick replays `BullfrogIntro.tgq` the first time and `Intro.tgq` the next, alternately. Any key also resets the idle timer. | `0x00533D30`, `0x0052FAF0` | STRONGLY INFERRED |

## 3. Pages, items and stations

### 3.1 How menus are defined: hard-coded tables in the exe

Menus are **not data files and not KWD triggers**. They are static records in DKII.EXE, the
same GUI system the in-game HUD uses (`Gui_*` records):
- page table `0x006AE4E0`: 32 pointers to page records (0x48 bytes; id at +0, rects in a
  2560x1920 virtual screen at +0x0A..+0x18, frame callback at +0x22, widget array at +0x3E,
  2D-backdrop index at +0x42);
- each widget record is 0x50 bytes: type byte +0, id +1, left-click action +6, right-click
  action +0x0A, page to show +0x0E, hide-own-page flag +0x12, action arguments +0x16 and
  +0x1A, rect +0x20, hit rect +0x28, frame callback +0x34, draw callback +0x38, label text id
  +0x40, draw arguments +0x44/+0x48, sound slot +0x4C.

A click (`Gui_PollButtonPresses 0x00528610`) calls `action(dword +0x16, dword +0x1A, frontEnd)`,
then hides the own page if +0x12 is set and shows page +0x0E if set. In the 3D front end,
`Gui_LoadWidgetRecord 0x00528300` drops +0x0E from buttons that carry a command, so the camera
state machine (§3.3) shows the next page when the path ends.

Three action functions carry the menu logic (all STRONGLY INFERRED):
- `0x00538000` (station move): low word of +0x16 = current page, high = page to show;
  low word of +0x1A = command, high = item index (0xFF = no index check);
- `0x005394E0` -> `0x00539500` (same-station page swap): hides +0x16 low, shows +0x16 high,
  and runs dispatcher command +0x1A high first if non-zero;
- `0x005306F0` (the command dispatcher, ~95 cases): command = +0x1A low.

The two command spaces overlap in their numbers (8 is the Quit dialog in one and the
multiplayer page set-up in the other), so below every command is written **S n** (station
command: `0x00538000`'s switch, table `0x005384A8`, low word of widget +0x1A) or **D n**
(dispatcher `0x005306F0` case n).

`0x00538000` and `0x00539500` act only when the item's "text is under the pointer" flag
(frontEnd +0x5B09, slot from `0x005372F0`) is set, so a click must land on the label itself.

### 3.2 Menu tree (labels from Text.str)

| Page | Item (Text.str id: label) | Action -> effect | Provenance |
|---|---|---|---|
| 1 Main | 71 Single Player Game | swap to page 2 at the same station | `0x006AD8B0` |
| 1 | 72 Multiplayer Game | S2: path 271 "Entry to 1st right" -> state 5 -> page 3 + D8 (multiplayer page set-up) | `0x006AD900`, `0x0053817E` |
| 1 | 1675 My Pet Dungeon | S11: path 262 "Entry to 2nd left" -> state 0x10 -> page 0x26 + D0x5B | `0x006AD950` |
| 1 | 94 Options | S5: path 256 "Entry to 2nd right" -> state 8 -> page 4 | `0x006AD9A0`, `0x005382D6` |
| 1 | 73 Extras | S6: path 259 "Entry to 1st left" -> state 0x19 -> page 6 | `0x006AD9F0` |
| 1 | 28 Quit | S8: dialog "Quit" / 15 "Are you sure?"; yes = D0x6F (exit, no next component: the program ends), no = D0x70 (back to page 1) | `0x006ADA40`, `0x005381CE`, `0x00536BA0` |
| 2 Single player | 140 New Campaign | S1: when map 1 has status 2 and 0 attempts (`0x00560D40(1)` = 2, `0x00561AF0(1)` = 0; not literally "a fresh profile") goes straight to D0x6D; otherwise dialog 2847 "This will reset the current Campaign" first, yes = D0x6D. D0x6D resets the profile and flies path 251 "Entry to Table" -> state 2 -> campaign map (hand-off `0x00538D00`, `campaign-map-and-progression.md`) | `0x006ADB30`, `0x00537990` |
| 2 | 2059 Continue Campaign | S13: path 251 -> state 2 -> campaign map | `0x006ADB80` |
| 2 | 141 Skirmish | S10: path 271 -> state 0x1A -> 7 -> page 9 (lobby) + D0x15 | `0x006ADBD0`, `0x00538AC0` |
| 2 | 145 Load Game | S7: lists `*.sav`, path 271 -> state 6 -> page 8 | `0x006ADC20` |
| 2 | (back icon) | plain swap back to page 1 (no command) | `0x006ADC70` |
| 3 Multiplayer protocol | list widget 12; back D0x16 | back: path 272 "1st right to Entry" -> state 0x1C -> path 274 "Static Entry" -> page 1 | `0x006B7180` |
| 4 Options | 96 Graphics / 95 Sound / 98 Control Options | swap to page 15 / 16 / 17 running D0x1D / D0x1A / D0x3A first (snapshot of current values) | `0x006B5BF8` |
| 4 | (back icon) | S15: path 257 "2nd right to Entry" -> state 0x1C -> path 274 -> page 1 | `0x006B5CE8` |
| 6 Extras | 2802 Today's Top Keepers | swap to page 18, D0x3D | `0x006B20C8` |
| 6 | 93 View Movies | swap to page 5, D0x3B -> `0x0053B3F0(5)`: `Mov_Shots` thumbnails, 5 buttons `0x0053B840`; which movies are unlocked comes from the map statuses (`0x0053B5F0`, campaign sibling) | `0x006B2118` |
| 6 | 86 DK3 Trailer | D0x3E: plays `<movies>\Trailer.tgq` | `0x006B2168` |
| 6 | 372 Credits | D99: path 265 "1st left to credit view" -> state 0x11 -> page 40; D0x62 returns by path 266 | `0x006B21B8` |
| 6 | (back icon) | S16: path 260 "1st left to Entry" -> state 0x1C -> path 274 -> page 1 | `0x006B2208` |
| 7 Briefing | shown by `0x0053A010` with D0x2B; 1408 Main Objective, 1409 Sub Objective (titles); back icon D10; go icon D0xC (launch the level) | back (campaign): path 254 "1st right to Table" -> map; go: §5 | `0x006AF160`, `0x006AF1B0` |
| 8 Load | 83 Name, 2730 Date, 30 Time columns; OK (9) D7 (`0x00532090`), Cancel (10) D0x13 (path 272 -> page 2) | | `0x006BB3F0`, `0x006BB3A0` |
| 9 Lobby | 270 Kick Player, 1547 Add Computer Player, 1495 Change map, 1496 Random map, 2803 Game Settings, start `0x005475A0` | skirmish/MP setup | `0x006AD6A0` |
| 0x25 (37) | campaign map overlay, back D0x59 (path 252 "Table to Entry" -> page 2) | hand-off: `campaign-map-and-progression.md` | `0x006AD6E8` |
| 0x26 (38) | MPD level buttons (`0x00542070`), back D0x5D (path 263 -> page 1) | MPD table: path 269 -> `0x0053A010(1)` | `0x006B3370` |
| 10, 11, 0x27 | multiplayer session lists (Join, Create, Refresh, Address Book) | unread | |
| 0x20-0x23 | multiplayer/skirmish lobby, map and game settings (Creatures, Rooms, Spells, Traps, Doors, Gain Mana) | unread | |
| 18, 40, 0x2C | high scores, credits, generic yes/no dialog (`0x00536BA0`) | | |
| 26, 27, 30, 42, 45-48 | single-button or list pages, roles unread | | UNKNOWN |

Item ids, label ids, commands and path numbers are CONFIRMED bytes of the tables (decoded
by a scratch parser over the image); what each command does is STRONGLY INFERRED.

### 3.3 Stations and the state machine (`0x005384F0`)

The 3D level has stations reached by camera paths 250-274 (the index names Entry, Table,
1st/2nd left, 1st/2nd right, credit view, MPD Table). A command starts a path through the
camera (`0x0044A370(path, 3, 12, 1)` with flag 0x8 so the camera stays at the end) and writes
the next state to `0x0073EDA4`. Each frame `0x005384F0` waits while the camera is in path
mode 0x12, then acts on the state:

| State | Action | | State | Action |
|---|---|---|---|---|
| 1 | path 250 -> 0x0C | | 0x0C | show page 1 (main) |
| 2, 0x13 | campaign map frame `0x00538D00` | | 0x0D, 0x19 | show page 6 (Extras) |
| 3, 0x16, 0x17, 0x1B | post-game `0x0053C3C0(4/2/3/1)` | | 0x0E | show page 2 |
| 4, 0x14 | level prepare `0x0053A010(0/1)` | | 0x10 | show page 0x26 + D0x5B |
| 5 | show page 3 + D8 | | 0x11 | show page 40 (credits) |
| 6 | show page 8 (Load) | | 0x12 | `0x00538B90(0)` |
| 7 | show page 9 (lobby) + D0x15 | | 0x1A | -> 7 |
| 8, 0x0F | show page 4 (Options) | | 0x1C | path 274 -> 0x0C |
| 9 | show page 18 + D0x3D | | 0x1D | path 274 -> 0x0E |
| 0x0B | show page 5 + D0x3B | | 10 | D0x3E |

In the 2D front end paths are skipped and states 1 and 0x1C go straight to 0x0C.

### 3.4 The 3D props: spawned by the Hero Gate FrontEnd room, not bound to menu items

`FrontEnd3DLevel` places no objects (asset sibling). The props are spawned by the Hero Gate
FrontEnd room's builder `0x004DEF30` (source file `HeroGateFrontEnd.cpp`; it acts only on room
type 0x10 and is reached through a room vtable, no direct caller). It walks the room's tiles in
order and, by tile index:

| Tile index | Objects created (Objects.kwd id) | Kept in | Confidence |
|---|---|---|---|
| 0, 3, 6, 9 | 0x84 (132), turned 0x400 | room +0x50 list (up to 8) | STRONGLY INFERRED |
| 5, 8, 11, 14 | 0x84 (132), unturned | room +0x50 list | STRONGLY INFERRED |
| 1, 7 | 0x59 (89) and 0x86 (134) | | STRONGLY INFERRED |
| 4, 10 | 0x87 (135) and 0x88 (136) | | STRONGLY INFERRED |
| 1 | 0x83 (131) | room +0x4D | STRONGLY INFERRED |
| 3 | seven 0x85 (133), each given mesh resource world +0xA26A + i*0x1B (the `MPD_map` set, `0x004DFAA0`), slot byte +0x173 = i+1, state 0x2B | `0x007136BE[7]` | STRONGLY INFERRED |
| 10 | thirty land pieces, object 0x66 (102), mesh resource world +0x9F24 + i*0x1B (the `3dmap_level` set), slot byte +0x173 = i+1, state 0x2B; slot 30 shown only when `0x00559740` returns 1 | `0x00713682[25]` (campaign land pieces) followed by `0x007136B4[5]` (the five secret pieces); one 30-entry write fills both | STRONGLY INFERRED |

All are flagged auto-placed room objects. `0x005395A0` (after the level loads and on map entry)
sets the texture phase of the 25 campaign land pieces by map status (0x55A for status 1 or 3) and shows or hides the five
secret-level land pieces (`0x007136B4[5]`) and the arrows (`0x00713652[24]`, created by `0x005357F0`). `0x00535950`, before the level load, requests the `3dmaplevel1_arrows`... mesh set
(table `0x006B8E28`, stride 0x110) through the engine's resource slot +0x54.

**Menu items are not bound to 3D objects** (STRONGLY INFERRED; object hooks on 0x83, 0x59,
0x86, 0x87 and 0x88 were not searched for). The main, single-player, options and extras items
are 2D widgets drawn over a camera parked at a station; only the tables are picked in 3D.
The campaign map pick `0x00538D00` accepts things of type 0x66 or 0x73 by their slot byte
+0x173 (slots 1-30 and 0x97-0xAE); the 0x66 pieces are the ones `0x004DEF30` makes, the
0x73 arrows come from `0x005357F0`. The seven MPD pieces (0x85) go through `0x005392E0`, which
was not re-read for this.
F2 builds the room, decorative props and typed station flow (`FrontEndScenery`,
`FrontEndFlow`, `FrontEndSession`). Prop creation includes the factory's floor clamp,
authored animation phases and retained descriptor lights; see the function records
for `0x004DEF30` and `0x004B5020`. F3 adds the 32-page/402-widget catalogue and
interactive shell pages; F4 adds campaign picking, marker/texture-phase state and
briefing navigation (campaign-map sibling). Other consumers remain later phases. No matched-original visual acceptance is claimed.

## 4. Input, hover and click model

| Fact | Provenance | Confidence |
|---|---|---|
| Menu items are **2D widgets** in a 2560x1920 virtual screen over the 3D view, hit-tested by rectangle in `Gui_UpdateMenusAndCapture 0x0052BC50`; there is no 3D picking for menu items. (The tables do pick 3D things: the campaign map `0x00538D00`, types 0x66 or 0x73, slots 1-30 and 0x97-0xAE; MPD through `0x005392E0`. See §3.4.) | | STRONGLY INFERRED |
| A text item's draw callback `0x00532670` draws one of six pre-rendered label images per item (frontEnd +0x11B61, 6 x 0x28 per slot): image 4 normal, 5 highlighted, 0-3 a fade. When the pointer is inside the label's own rectangle it plays sound 0x30F once per entry, sets the highlight and the item's "clickable" byte, and restarts an 8-frame fade counter (`0x0070D688`) that steps the glow down after the pointer leaves. Alignment comes from the draw argument's low word (2 = centred). | `0x00532670` | STRONGLY INFERRED |
| Click and release sounds come from the widget's sound slot (+0x4C) through the table at `0x006AC8C4`: main-menu items use slot 32, back icons 36/35. | `0x0052BC50` | STRONGLY INFERRED |
| Keyboard: Ctrl+Alt-style modifier 3 with key 0x20 (D) loads a demo packet file and starts it (debug); Alt+Enter (0x1C with modifier 2) calls `0x00558BD0`; key 0xB7 sets frontEnd +0x6022 (screenshot, unread). | `0x0052FAF0` | STRONGLY INFERRED (keys); HYPOTHESIS (purposes) |

### F3 implementation boundary

`FrontEndMenuController` supports Main, Single Player, Options/Graphics/Sound/Controls
shells, Extras and Quit confirmation. Pointer hold suppression, hover/fade and sound
hooks are implemented; playback is F8. Deferred actions are dimmed as a temporary
capability gate, not as a reading of native campaign availability. Option values,
media and other generic-dialog uses remain later phases; F4 builds reset
confirmation and campaign map/briefing navigation (see the campaign-map sibling).

Font replacement follows ADR-0539. Native measurement uses the advance/line-height
canvas ([glyph measurement](functions/006335D0.md)); visible ink retains its bearing
inside that canvas. Main labels use the pre-rendered-label style, while options
and dialog text use the [common drawer](functions/005323B0.md) and its font thunks.
Titles use [their own preparation](functions/00536850.md). Glow construction is
recorded in [dilation](functions/00552770.md), [blur](functions/00552990.md) and
[alpha scaling](functions/00552620.md). These are binary readings, not a matched
original-game screenshot acceptance.

## 5. Transitions

| Direction | What happens | Provenance | Confidence |
|---|---|---|---|
| Front end -> level | The briefing's go (D0xC) or skirmish start (D0x5A) or Load (D7): sets `0x0075AEB3` = 1 (3D front end) or flags 0xC00 (2D), leaves game mode in `0x0075AD4B` (1 campaign, 2 skirmish, 3 multiplayer, 4 MPD), level name in `0x0075AD57` (from `0x006B83EC[slot]`, `mpd%u`, or the chosen map/save), records the return page in `0x00759B30` (0x1A campaign, 0x2D skirmish, 0x1B multiplayer, 0x2A MPD) and exits the front end naming the session `0x0070D138`. `GameSession_Run` then clears `0x0075AEAF` and runs the session again with the real level. | `0x005306F0` cases 7, 0xC, 0x5A; `0x00525C86` | STRONGLY INFERRED |
| Level -> front end | When a session ends with `0x0075AEAF` = 0 and `0x0075AEB3` = 1, `GameSession_Run` saves the mode in `0x0075AD4F`, sets mode 5, level `FrontEnd3DLevel`, `0x0075AEAF` = 1, and returns the session again. The return page `0x00759B30` is then routed in two places. **`0x00534F70`** (front-end init) handles 0x1A/0x1B/0x2A/0x2D, which are the statistics pages' own ids (26 campaign, 27 multiplayer, 42 MPD, 45 skirmish): the campaign branch table and cutscene first (`campaign-map-and-progression.md` §5), then it shows that page at once (`0x005321A0`) and sets post-game states 3/0x16/0x1B/0x17 -> `0x0053C3C0(4/2/1/3)`, which plays path 255 ("Spare"; path 264 for MPD) with station state 0, under the page, and fills it through `0x0053C520` -> `0x0053C830`; and the default (any other page: `0x005321A0(page)` when it is not 1, then `0x00759B30` = 1, state 1 -> path 250 -> main menu). For 0x25, 9, 0x0B, 0x0A, 0x27 and 0x26 it returns at once, and **`0x005384F0`'s first-frame block** (gated by `0x0073EDB4` = 0) routes them: 0x25 -> path 251, state 2 (map); 9 -> path 259, state 7 (lobby); 0x0B/0x0A/0x27 -> path 256, state 5 (multiplayer); 0x26 -> path 264, state 0x10 (MPD). | `0x00525C99-0x00525CFB`, `0x00534F70`, `0x005384F0` | STRONGLY INFERRED |
| Exit | Quit's yes (D0x6F) exits the front end with no next component; the session returns null and the application loop ends. | `0x005306F0` case 0x6F | STRONGLY INFERRED |

## 6. Options pages

Values are written to the registry by per-setting setters, each of which opens a key,
writes one value and closes it (`0x005621E0` closes). Key names are the setters' strings.

| Page | Item (Text.str) | Widget / handler | Setting written | Confidence |
|---|---|---|---|---|
| 15 Graphics | 1444 Water Translucency | toggle `0x0054BA30` | applied on OK by `0x0054BB80` (`SetTranslucentWater`) | STRONGLY INFERRED |
| 15 | 1450 Environment Mapping | toggle `0x0054BA70` | `EnvironmentMapping` (`0x00566420`) | STRONGLY INFERRED |
| 15 | 132 Hardware Acceleration | toggle `0x0054BA50`; card button `0x0054C540` (device 0x00757D51) | `D3D Device Guid` (`0x005660F0`), `Screen Mode Type` (`0x00566970`) | STRONGLY INFERRED |
| 15 | 134 Resolution | button `0x0054C2A0` -> `0x00566A70` | resolution (key unread) | HYPOTHESIS |
| 15 | 135 Gamma | slider 15..60, `0x0054B9B0` | `Gamma Level` (`0x00566220`) | STRONGLY INFERRED |
| 15 | 1445 Shadow Detail Level | slider 0..3, `0x0054B980` | `Shadow Level` (`0x005663A0`) | STRONGLY INFERRED |
| 15 | (buttons) | D0x1E applies all through `0x0054BB80` (also `Texture Reduction Level` `0x00566620`); D0x1F leaves | | STRONGLY INFERRED |
| 16 Sound | 1464 Sound Quality | button `0x0054C810` | `SoundQuality` (`0x005637E0`) | STRONGLY INFERRED |
| 16 | 1674 Speaker Configuration | button `0x0054C970` | `Headphones` (`0x00563870`) | STRONGLY INFERRED |
| 16 | 1460 Master / 1457 Voice / 1459 Music / 1458 Sfx Volume | sliders 0..100, `0x0054B940`/`0x0054B820`/`0x0054B860`/`0x0054B8A0`, live | `MasterVolume`, `SpeechVolume`, `MusicVolume`, `SoundEffectVolume` | STRONGLY INFERRED |
| 16 | on/off beside Voice, Music, Sfx | `0x0054C9F0` | `SpeechSwitch`, `MusicSwitch`, `SFXSwtich` (sic) | STRONGLY INFERRED |
| 16 | 1679 QSound, 1472 Environmental Effects | toggles `0x0054B910`, `0x0054B8E0` | `QSound`, `EnvironmentalEffects` | STRONGLY INFERRED |
| 16 | (buttons) | D0x1C re-applies the eight values captured on entry (D0x1A, `0x0054B4B0`): a revert; D0x1B keeps the live values | | STRONGLY INFERRED (behaviour); HYPOTHESIS (which icon is OK) |
| 17 Control | 1477 Keyboard Setup | key list `0x0054BD70`, capture `0x0054BD90` | key bindings via `InputBindings_CopyToConfiguration` / `RestoreFromConfiguration` | STRONGLY INFERRED |
| 17 | 1466 Mouse Sensitivity | slider 5..20, `0x0054BAF0` | `Mouse Sensitivity` (`0x00560B30`) | STRONGLY INFERRED |
| 17 | 1469 Game Speed | slider 1..16, `0x0054BAC0` | `GameSpeed` (`0x0055D590`) | STRONGLY INFERRED |
| 17 | 1468 Scroll Speed | slider 5..20, `0x0054BA90` | `ScrollSpeed` (`0x00560BC0`) | STRONGLY INFERRED |
| 17 | 2840 Invert Mouse (1st Person) | toggle `0x0054BB20` | `Invert Mouse` (`0x00560940`) | STRONGLY INFERRED |
| 17 | (buttons) | D0x38 restores bindings and sensitivity; D0x39 writes Invert Mouse, Game Speed, Scroll Speed (`0x0054BB40`) | | STRONGLY INFERRED (behaviour); HYPOTHESIS (which icon is OK) |

Slider ranges are the widget's +0x16 low word (minimum) and +0x1A low word (maximum); that
reading is STRONGLY INFERRED from the values (volumes 0..100, shadow 0..3).

## Unread / next

| Address | What | Why it matters |
|---|---|---|
| `0x0053C830`, `0x0053AC60`, `0x00535C30` | statistics rows and per-level briefing text | results screen contents |
| `0x005306F0` cases 0x20-0x55, 0x65-0x75 | multiplayer: session lists, lobby, settings | multiplayer menus |
| pages 10/11/0x20-0x27 widgets (`0x0054EDF0`, `0x0054EE10`, `0x005457A0`, `0x005415D0`, `0x005475A0`...) | multiplayer and skirmish lobby | skirmish setup items |
| `0x00537CA0`, `0x00537E10` | bottom strip draws | exact art per page |
| `0x006BCE48`, `0x0052F9E0` | when the 2D-only run loop is used | |
| `0x00537FB0`, `0x00537FF0` | `CFrontEndComponent3D` | its role |
| `0x0054D040`, widget 680 on page 15 | unlabelled graphics button | |

## Owner recollections (2026-10-25)

The owner recalls a Dungeon Keeper 2 title screen with logos and disclaimers before
the Bullfrog intro, movie skipping by keys/clicks, and idle intro replay. The exact
10-second/180-second timings remain binary readings. No multiplayer recollection is
available; retain the binary's return routing through path 256 without upgrading
its confidence. These answers do not establish title-screen skipping.

## Movie gallery

STRONGLY INFERRED from the complete callbacks `0x0053B3F0`, `0x0053B5F0`,
`0x0053B750`, `0x0053B480`, `0x0053B810` and `0x0053B840` (2026-10-29):
page5 is a five-row window over the22-row table at `0x006B0DD0`, not a directory
listing. Row0 is Intro and always unlocked; campaign wins unlock rows1..20,
with any won branch sufficient for6/11/15. Row13 is unavailable in languages7
and18; row21 is available only in7. This host has no language selection and
uses the English roster, retaining row21 as unavailable. Missing files also
remain disabled. Gallery row20 plays Outro alone; the post-win path separately
plays BullfrogOutro. Movie thumbnails are M-Intro, M-Outro, M-000..M-018,
or M-Unavailable, under Mov_Shots. Native locked-row presses show a dialog;
ours leaves the rows disabled. The five static buttons are53..57, names59..63,
scrollbar52 and return65; the existing catalogue owns their rectangles.

The native idle tick resets its timer after a blocked-page timeout too, and
after movie playback returns. It requires an active page. The title remains
unskippable; movie skip is handled by the existing player. `skipStartup` is a
development switch; returning from a level skips first-entry startup.

Credits stays disabled: dispatcher99 selects path265/state17, but its content
producer has not been established. Trailer uses the existing movie player.
