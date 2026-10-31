# Campaign win sequence: the Horned Reaper finale, the celebration and the win screen

DKII.EXE v1.70 (GOG), pyghidra-mcp reads and disassembly on 2026-10-26, plus the shipped
`Data/editor/maps/*.kwd`, `Effects.kwd` and `Paths.WAD`. No decompiler output is quoted
(ADR-0046). Turn counts assume the shipped `TicksPerSecond` of 4 (`0x006F2590`, every
shipped LEVEL record). What the profile records on a win is in
[campaign-map-and-progression.md](campaign-map-and-progression.md) §2.2; this document is
what happens in the level after that.

## 1. Which win runs which sequence

`Player_EndLevelStatistics` `0x004BE630` writes Status 2 (won) at its start, then reaches
the win tail at `0x004BED9E`: LevelWon `+0x172` = 1 and `Message_Post` `0x004C4600`
(type `0x32`, the player). From `0x004BEDC0` (STRONGLY INFERRED, disassembly read whole):

| Game mode | Player | LEVEL flags | Profile write | Sequence |
|---|---|---|---|---|
| 1 (campaign) | local only (`+4` = world slot `+0x264`) | `0x20` (global `0x006F2957` bit 3) | Secret Levels Completed[world `+0x722E` - 1] = 1 (`0x005615F0`) | celebration `0x004BF9E0` |
| 1 | local | `0x40` (bit 4) | Special Levels Completed[`+0x722E` - 1] = 1 (`0x00561750`) | celebration |
| 1 | local | neither | none here | **finale `0x004BF410`** |
| 4 (My Pet Dungeon) | any | any | Pet Dungeon Levels Completed[MPD] = 1 unless a user file (`0x0075A665`) | celebration (also for a user file) |
| 2, 3 | | | | none |

`0x0051E6D0` copies the runtime LEVEL record (file offset + `0x134`) into the world:
`+0x722E` is file byte 25545 and `+0x722F` byte 25546, the two bytes our reader exposes as
`LevelDef.SpecialLevelIndex` (CONFIRMED by the copy at `0x0051E6D0`). The same routine
fills the reward bytes of §5: world `+0x9B80..+0x9B83` from `PreviousRewards`,
`+0x9B86..+0x9B89` from `NextRewards`, and `+0x9B84`/`+0x9B8A` from extended-metadata bytes
25605/25606 (`LevelDef.Unknown_6403[2]`/`[3]`).

**The shipped data (CONFIRMED, dumped 2026-10-26):**
- Flag `0x20` is on Secret1-Secret5 and on the multiplayer map Islands (mode 2/3, so
  inert). The low byte of `SpecialLevelIndex` is 1-5 on Secret1-5 and 0 on every campaign
  map; its high byte (`+0x722F`) is 0x92-0x94 on several maps and its reader is unread.
- Flag `0x40` is on **no** shipped level, so the special arm is dead in v1.70.
- Every campaign map (Level1-Level20, 6a/6b, 11A-C, 15a/b) carries neither flag and so
  runs the finale. The secrets and the seven MPDs run the celebration.

## 2. The finale `Player_FinaleSequence` `0x004BF410`

Read whole 2026-10-26 (STRONGLY INFERRED; the sub-steps' confidence is noted). It returns
at once in game mode 3.

1. **Default point.** Player `+0xB29/+0xB2D` = (Heart tile x - 2, y - 2) shifted to
   4096ths, `+0xB31` = 0. This is overwritten whenever a gem is found.
2. **Find the gem.** It walks the world's player list (head world `+0x7A`, next `+0x3A`)
   and in each player's object list (`+0x0A`, next `+0x10`) takes the first object whose
   type byte `+0x12C` is 28, the Portal Gem ([objects.md](objects.md) §8). Its tile goes
   to `+0xB35/+0xB39` and its thing id to `+0xB3D`. **No gem: steps 3-6 are skipped**
   and only step 7 runs.
3. **Find a landing tile.** Up to 500 tries. Each try draws dx then dy, each
   `Random_Next(16)` - 8 (so -8..7), on the simulation stream (world `+0x15A0`): two draws
   per try, whether or not the try is rejected. A try is rejected when:
   - it is one of the first 128 and dx² + dy² <= 24 (the landing must be at least
     five tiles away early on; after 128 tries any offset is allowed);
   - the tile is outside the map (`World_ContainsTile` `0x00509090`);
   - the tile is unexplored (`World_IsTileExplored` `0x00510000`, tile word `+8` bit `0x2000`,
     a world flag with no player argument; this test would be dropped after 1024 tries,
     which 500 never reaches);
   - the navigation solid bit is set (`World_TileNavigationSolid` `0x00509200`);
   - the tile belongs to a room of type 3 Portal, 5 Dungeon Heart, 11 Prison, 13 Temple,
     16 Combat Pit, or 18-23 (the hero gates, Hero Stone Bridge) (`Tile_Room`
     `0x004559B0`, room `+0x18`);
   - any thing on the tile (chain `+0xC`) has class `+0x12` 0 creature, 2 object, 3 trap
     or 4 door;
   - `Route_CanReachForPlayer_Thunk` `0x00452970` (from the gem's position to the tile
     centre, the player's tag, all options 0) answers no.
   The first accepted tile's centre goes to `+0xB29/+0xB2D/+0xB31`. If all 500 fail the
   point is the gem tile's corner (tile << 12, no half-tile).
4. **The Reaper.** The first creature in the player's list (`+0x06`) whose definition has
   `+0x686 & 0x4000` (IsHorny) is used. Otherwise one is made: `World_GetReaperType`
   (world slot `+0x2E8`) and `CreatureManager_CreateAtFreeSlot` `0x004B78D0` at the point,
   owned by the player, at the constructor's level 1 (no level-up, unlike the spell's 10);
   creation failure returns without step 7. A new Reaper has its draw bit (`+0xF3` bit 0)
   cleared, gets a fog update, and the player gets `+0x76 |= 0x800`. An existing one also
   gets a fog update (`0x0048CC90`), `+0x76 &= ~0x800`, timer `+0x4E9` = 0 and `+0x3EA` bit `0x40000` (one-shot) cleared,
   so it can no longer expire; it is kicked to state 1, its motion words `+0x104..+0x118`
   are zeroed, and it is **not moved** here.
5. Both: `+0x46F` bits 0 and 3 cleared (no combat), heading `+0xF4` = `0x400` (half a
   turn), then `Creature_KickToState(228)`.
6. Only if state 228 is accepted, three view actions for the player (§4): **15** (camera
   mode 3), **4** with the gem tile (glide the focus there), **6** (`0x5800`, 1000: zoom to
   `0x5800` over 1000 ms). Player `+0x76 |= 0x40` is set either way (`0x004BF886` jumps to
   the `OR` at `0x004BF94A`).
7. Always (with or without a gem): action **`0x74`** with 1 (raise the letterbox).

Which shipped campaign maps have a gem (CONFIRMED, triggers and things corpus):
creature action 20 hands one to a creature in 14 levels (1-5, 6a, 6b, 7, 8, 9, 14, 15a,
15b, 16), trigger action 2 creates object 28 in 15a, 18, 19, 20, and 18, 19, 20 also place
one. **Levels 10, 11A-C, 12, 13 and 17 have none**, so their finale is the letterbox alone
and (§6) the win screen never starts (STRONGLY INFERRED from the corpus scan and the
code; not observed).

## 3. The Reaper's states 228-234

State-table rows 228-234 (`creature-state-table.md`). Age = state `+0x1C`, incremented
by the dispatcher before the handler; `TPS` = 4. All read in disassembly (only 228 is a
Ghidra function). STRONGLY INFERRED unless noted.

| State | Handler | What it does | Leaves for |
|---|---|---|---|
| 228 | `0x00473F80` | Sets state `+0xA7` = 1. If the player's `+0x76 & 0x800` is clear (an existing Reaper): age 1 ends any running action, makes effect 122 *Horny Smoke* at it, alpha `+0x8F` = 255 and `+0x9F` bit 0; age `TPS` makes effect 30 *Horny Birth Trail* with its heading; later ages drop alpha by 64 (to 0). From age `2·TPS` (both kinds): draw bit cleared, view action `0x73` with path **102** at the gem tile | 229 |
| 229 | `0x00474100` | Holds (age kept) while the camera mode `+0xDA8` is `0x12` (a path playing); from age 4, view action 4 with the landing tile (`+0xB29/+0xB2D` >> 12) | 230 |
| 230 | `0x004741D0` | Holds while the camera's timed move (`+0xDFA`) runs. Age 1: clears `+0x3EA` bit `0x80000`; `Thing_SetPositionThunk` to the landing point; effect 126 *Horny Grand Entrance Effect*; sound `0x32D` from the definition's bank (`+0x6E3`); view action `0x73` path **100** at its tile; view action **17** with its id (the camera follows it); creature action 59 once; draw bit on, alpha 0. Later ages: alpha += 16 (cap 255); when the action ends, alpha 255 and `+0x9F` bit 0 off | 231 |
| 231 | `0x004743C0` | Age 1: creature action 46 once (the arrival roar, art 35, [keeper-spell-reaper.md](keeper-spell-reaper.md) §1) and sound `0x12C`. Waits for the action to end | 232 |
| 232 | `0x00474430` | If thing `+0xB3D` is gone or no longer class 2, returns 0 and stays (what the dispatcher does with 0 is unread). When the position test or the route request
fails it returns 1 with no change, so the Reaper retries every tick. If the gem's position passes `Thing_PositionAllowsCreation` `0x004B35C0` and `Creature_RequestRoute` `0x004D0F60` (options `0x26`) succeeds: state 234 with next state 233 (`+0xDF`) and target (`+0xE3/+0xE7`) | 234 |
| 234 | `0x00474F80` | The walk: on its own tile and the four beside it (the loop skips a tile whose x and y offsets are both non-zero, `0x00474FAA`), a trap without `+0x10C` bit `0x20` is destroyed (slot `+0x20`); a door with `+0x109` = 0 is unlocked (`Door_SetLockMode(0)`) when it is the Reaper's owner's and locked, or else has its lock-pick timer started (`0x004A6DF0`, `DoorPickedTimeSeconds` · TPS). Then the en-route step `State06_EnRoute` `0x00470C80` (LIKELY: it resumes the saved 233 on arrival) and `Tile_SetExploredAndRefresh` on its tile | 233 |
| 233 | `0x004744F0` | Age 1: heading `0x400`; view action 17 with 0 (stop following); view action `0x73` path **101** at its tile; view action `0x84` (view `+0x1221 |= 0x10`, unread); creature action 47 once; `+0xF1` bit 3; the gem is removed through its slot `+0x20` (LIKELY destruction, as `0x004A5290` uses it); sound `0x32E`, handle kept at `+0x2E`. Then the age is held. When the game turn exceeds action start `+0x3A5` + 13·TPS (13 s): `+0x3EA |= 0x80000`, and if the renderer gives its mesh point 8000 (bridge slot `+0x10C`, `0x00441AA0`, forwarding to the render sink; point id LIKELY) effect 258 *Horny End of level* there. One turn later, once: **interface slot `+0x40`** `0x0042BE40`, the win screen (§5) | stays in 233 |

The Reaper stays in 233 until the player continues (§6), when `Player_ContinueAfterResult`
`0x004C0120` dismisses it (`Creature_DismissReaper` `0x00491B70`, state 242 fade).
The only call of interface slot `+0x40` in the binary is the one in state 233
(slot-`0x40` scan of all 31 indirect call sites, 2026-10-26; a second reading could not reproduce the scan exhaustively, so STRONGLY INFERRED), so **only a Reaper that
reaches the gem opens the win screen** (a Reaper whose route never succeeds stays in 232).
`Camera_StartPath` `0x0044A370` refuses while the camera is in mode `0x12` (a path playing),
10, 2 or `0xB`, or while view `+0xDD8` is set: path 101 is started once, at state 233's
first turn, so it does not play if path 100 (350 frames, about 11.7 s) is still running then.
Whether 230..233 outlasts path 100 is unmeasured.

Effect names are `Effects.kwd`'s; the creature actions are the Horny's own (46 and 47 are
read in keeper-spell-reaper.md; 59 is unread).

## 4. The view actions it issues

Game actions 4, 6, 15, 17, `0x73`, `0x74` and `0x84` are not in the world's table
(`game-actions.md` §4). `World_DispatchAction` `0x0050E5C0` first offers every record to
the bridge `[[world+4]+0x10]` (vtable `0x0066CC94`: slot `+0x20` `0x0043B150`, slot `+0x24`
`0x0043B180`, slot `+0xD0` `0x0043A980` returns the camera at bridge `+0x3E5`). The bridge
handlers come from the (id, handler) pairs at `0x0066CE30`, 37 pairs (CONFIRMED bytes):

| Id | Handler | Effect (camera = bridge `+0x3E5`) |
|---|---|---|
| 4 | `0x00442650` | glide the focus to the tile centre (≤ 500 ms) |
| 6 | `0x00442290` | `Camera_SetZoom(rec[0], rec[1])`: zoom, timed when rec[1] ≠ 0 |
| 15 | `0x00442780` | if the camera mode is 7, `Camera_SetYaw(0)`; then `Camera_RequestMode(rec[0], 0)` |
| 17 | `0x004427D0` | creature id to camera `+0xE02` (followed creature), yaw request |
| `0x73` | `0x00442540` | `Camera_StartPath(rec[0] & 0x7FFFFFFF, x, y, rec[0] >> 31)` |
| `0x74` | `0x004426F0` | letterbox: `0x0044D320(rec[0])`, so 1 raises and 0 lowers; 2 first sets camera `+0xE48` = 1 (the request count) and then lowers, resetting the count; [viewport guards](camera-possession-guards.md) still apply |
| `0x84` | `0x00443030` | bridge `+0x1221 |= 0x10` (meaning unread) |

The other pairs: 2, 3, 5, 7-14, 16, 21, 22, 25, 40-42, 44, 47, `0x66`, `0x68`, `0x6F`,
`0x70`, `0x7E`, `0x7F`, `0x80`, `0x83`, `0x85`, 31.

**Path 102 does not ship.** `Paths.WAD` has `EnginePath100.KCS` (350 frames, 11.7 s) and
`EnginePath101.KCS` (770 frames, 25.7 s) but no 102, and there is no loose file.
`Camera_StartPath` `0x0044A370` does nothing more when the load fails (it has already set
`+0xE3C` bit `0x20`), so state 228's path is a no-op and 229 only waits its four turns
(STRONGLY INFERRED; the failing load is not observed). Paths 100 and 101 are the
designers' "1st and 2nd cut of the end-of-level sequence" (camera-paths.md §1).

## 5. The win screen (interface `0x006CEA10`, vtable `0x0066C4A4`)

- **Start, slot `+0x40` `0x0042BE40`.** Clears the speech queue (`0x00568140` with
  `0x32` on `0x0075B8E8`), sets `+0x4CE2` = 1 (win), `+0x4CDE` = 0, `+0x4CE6` = 1,
  `+0x4CEE` = 0 (step) and `+0x4CEA` = 0, then shows step 0; when there is nothing to show,
  `+0x4CEA` = 1 (done). Its loss twin, slot `+0x3C` `0x0042BC20`, sets `+0x4CDE` instead
  and steps through `0x0042BC70` (unread).
- **One step, `0x0042BE90(i)`, i < 5.** The byte is world `+0x9B86 + i` through world slot
  `+0x314` (`0x00508D70`): the level's **NextRewards**, the binary's "Current Reward Id",
  with the fifth from byte 25606. Zero ends the screen. Otherwise it restarts the item's
  tablet (below) with the texture fraction `(b << 12) / 52` and queues the
  item's speech. The item table is `0x0066C780`, 52 rows of 16 bytes: id, UTF-16 name
  (Alarm, Barricade, ..., Workshop, Reaper_Talisman01-04), an icon word and a speech id
  (played as id + 1). Level 1's NextRewards are 12 Dark Library and 42 Training; Level 2's
  are Treasury, Workshop, Sentry, Wood Door. So the win screen announces what the next
  level unlocks, one item per speech (STRONGLY INFERRED from the shipped NextRewards).
  The row is: dword id (0-51, equal to the row index), dword pointer to the UTF-16 name,
  word icon, word speech id, dword flag (meaning unread; 0 on the spells, the Guard Post and
  the unused rows). The speech id is a Speech.str text id: 1369 reads "Alarm Traps announce
  enemy intrusion", 1350 "Devise new spells in the Library" (CONFIRMED, bytes dumped
  2026-10-26; transcribed in `WinScreen.Items`).
- **The tablet, interface `+0x4C6E`** (read 2026-10-27: set-up `0x0042BE90`, draw
  `0x0042F040`, step `0x0042EE80`, loads `0x0042F4D0`/`0x0042F5E0` from `0x0042BB30`).
  The reward is not a flat icon. It is the static mesh **Tablet.kmf**, 0.1 × 0.1 × 0.019
  units, whose front material lists 51 GUI icons (`gui\Traps\Alarm` … `reaper_talisman-01d`;
  the back is `Tablet_backf`, the sides `Tabside`). The draw's frame rule
  (`0x00579730`: round(51 × fraction + 1/1024 − 0.49999)) makes item n show icon n − 1.
  It is drawn orthographically in screen space at (1280, 800) of 2560 × 1920, so half the
  width and 5/12 of the height (`0x0052C5A0`), at scale × 4096 pixels per unit.
  Each drawn frame: while the frame counter is below 32, both turns (about axes 0 and 2,
  2048 units a turn) are frame × 64, and the scale closes 1/(32 − frame) of its gap
  from 0 to 0.75. That makes one full turn while the tablet grows to 0.75 × 4096 × 0.1 ≈ 307 px wide.
  Frame 32 sets the turns to 0 and shrinks the scale by 5 %, and frame 33 restores 0.75.
  While growing, two sparkles leave each of the mesh's control points (the CTRL table,
  `0x00577DC0`; Tablet.kmf's 16, ids 11000–11015, ring its edge), every second point
  below graphics detail 5. They move (rand − 16384)/8192 px a frame per axis and live
  rand/8192 + 8 frames (MSVC `rand`, `0x006359F0`). Each is drawn with the sprite
  TabletSparkle, its frames left as its size (LIKELY). Past frame 34, once no sparkle is
  left, a 2 s hold is armed (`+0x57` = 2) and when it passes the tablet is done (`+0x4B`).
  The frame counter counts drawn frames, not game turns, so the growth takes 32 drawn
  frames at whatever rate DK2 drew. CONFIRMED arithmetic; the order and sense of the two
  turns and the sparkles' blend are unread.
- **Update, `0x0042C390`** (per frame). Returns at once in camera modes 2, 7 or `0x12` (so
  path 101 holds the screen back) or while the game object's `+0x284` is set. While the
  screen is not done it draws the tablet. It advances a step only when the tablet is done
  (`+0x4CB9`) and the previous speech has finished (`0x00568570`). Once the screen is done
  (`+0x4CEA`) and it is a win (`+0x4CE2`), it arms a deadline 500 ms ahead (wall clock);
  when it passes, the local player's `+0x76` bit `0x100000` issues action 134 (continue),
  bit `0x200000` issues action 1 (leave); each sets `+0x13DA` and player `+0x109E`, and
  clears its bit. With neither bit the deadline re-arms every 500 ms. Who sets `0x200000`
  is UNKNOWN.

## 6. What ends the level, and Space and Escape

No timer ends a won campaign level; the player does (STRONGLY INFERRED):

| Moment | Space | Escape |
|---|---|---|
| A camera path is playing (mode `0x12`) | skips the path (`Camera_EndPath` `0x0044D8F0`, speech cleared); nothing else | same |
| A camera glide is running (`+0xDFA`) | finishes it in 1 ms | same |
| Status 2, mode 1, win screen not started or still stepping, `+0x109E` = 0 | action 1: leave the level (world `+0x723B`, session loop ends) | `+0x109E` = 1, `+0x76 |= 0x100000`, action 75 (reveal the map); the 134 follows from §5's deadline once the screen is done, and never if the screen never starts (no gem, or a Reaper stuck in 232) |
| Same, `+0x109E` set | nothing | nothing |
| Status 2, mode 1, win screen done | action 1 | action 134, `+0x13DA` = 1, action 75 |

Read from `Input_HandleGameKey` `0x00405010`: the skip at `0x00405362` precedes the
bindings, and the result arm at `0x004054F0` tests Status `+0x19A` = 2, mode 1, then
`+0x4CE2`/`+0x4CEA` and `+0x109E`. Escape needs global `0x0075A661` = 0. Action 134 runs
`Player_ContinueAfterResult`: play continues with the map revealed and the Reaper sent away.
Action 1 ends the session; the front end then shows statistics and the cutscene
(campaign-map-and-progression.md §5).

In other modes than 1 and 3 (My Pet Dungeon), the arm at `0x004055E3` tests no win screen:
Space posts action 1, and Escape (with `0x0075A661` = 0) posts 134 at once, sets `+0x13DA`
and posts 75 (`0x00405633`–`0x0040569C`).

So the "fade after pickup" the owner recalls is not a level-end fade found in the
simulation: after the pickup come path 101 (25.7 s if it starts, skippable), effect 258 at 13 s, and
the reward announcements; the level ends when the player presses Space. `0x0042F040`, once
listed here as a possible fade, is the reward tablet's draw (§5); where a fade is drawn is
unread.

## 7. The celebration `0x004BF9E0` (secret levels and MPD)

Read whole 2026-10-26. Returns in game mode 3. Sets player `+0x76 |= 0x40`; view action
4 to the Heart tile (`Player_DungeonHeartPosition`); `+0x111D` (FireworksTime) = turn + 6,
or + 14 when `+0x134` is non-zero; action `0x74` with 1; interface slot `+0x38` (hide the
GUI, hud-visibility.md). Each creature in the player's list, unless the room at its
`+0x3B9` is type 12 (Torture Chamber), is kicked to state 1, then creature action `0x39`
starts with `Random_Next(24)` cycles (simulation stream) and, if it started, state 270.
`Player_Tick` `0x004B9EC0` fires once at that turn: effect 320 *Fireworks* at the Heart
tile's corner, z `0x1000`, and world sound `0x344` (bank world `+0xA3BB`). Nothing calls
the win screen. On a secret level (mode 1, `0x0075A661` non-zero) Escape does nothing and
the player leaves with Space; in My Pet Dungeon Escape continues at once (§6).

## 8. Object 140 (Transfer Creature) in the shipped levels

Not placed (`kwd things`, all 66 Things files: no object 140; object 28 only in 18, 19,
20); trigger action 2 (`0x00518F32`, type from the record's byte 0) creates only object
28 (5 records, Level15a, 18, 19, 20), and no other trigger parameter names an object id;
`World_PlaceGeneratedSpecial` `0x0050F8C0` picks only 117-128 and never runs in v1.70.
Of `Object_Create`'s 71 call sites only those were checked; the shot, Temple-reward and
creature-drop callers were not traced to their type. So **no shipped level places or
scripts object 140** (STRONGLY INFERRED).

## 9. Build list (built in F5, ADR-0549)

State 270's update handler is `Stub_ReturnTrue` `0x004B4C20` ([state-categories.md](state-categories.md) §7.1): nothing to read.

1. **Win dispatch** (§1): after `RecordResult`, mode 1 local player: LEVEL flag `0x20` →
   Secret Levels Completed[SpecialLevelIndex low byte - 1] and the celebration; `0x40`
   → Special Levels Completed and the celebration; else the finale. MPD → celebration.
2. **Finale setup** (§2) as one deterministic simulation step: gem search order, the
   500-try landing search on the simulation stream (two draws per try), Reaper reuse or
   level-1 creation, flag writes, state 228. Hash and save the new player fields
   (`+0xB29..+0xB3D`, `+0x76` bits `0x40`/`0x800`).
3. **States 228-234** (§3) with their holds: 229 and 230 hold on the *view's* camera
   state, so the simulation needs a camera-busy input per player (path playing, glide
   running) that the view reports; in a headless run it is never busy. Path 102 is a no-op.
4. **View requests** (§4): actions 15, 4, 6, `0x74`, `0x73` (100, 101), 17 (follow on
   and off), `0x84` as scripted-view requests, the existing ADR-0496 channel.
5. **Win screen** (§5): presentation over `NextRewards` + byte 25606, the item table,
   speech; then the 500 ms continue/leave rule.
6. **Keys** (§6) and action 134's continuation, including dismissing the Reaper.
7. **Celebration** (§7): heart camera, GUI hide, creature celebration state 270 with
   action `0x39`, fireworks at turn + 6/14.

## Unread / open

| Item | Next |
|---|---|
| Creature action 59 | creature-action table |
| What the state dispatcher does when 232 returns 0 | the state dispatcher |
| Bridge `+0x1221` bit `0x10` (action `0x84`); render point 8000 | `0x00441AA0`'s sink slot `+0x10C` |
| Player `+0x76` bit `0x200000` setter; `0x0042F040`; `0x0042BC70` | slot scans |
| World `+0x722F` (high byte of SpecialLevelIndex) reader | field scan |
| Owner play check: on Level 10 or 12 (no gem), does a Reaper appear after the win? | owner |
