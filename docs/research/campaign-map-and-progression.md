# Campaign map, progression and what the profile keeps between levels

**Scope:** the single-player campaign's level select (the "world map"), the unlock and branch
rules, the player profile DK2 keeps between levels (registry key
`HKCU\Software\Bullfrog Productions Ltd\Dungeon Keeper II\Configuration\Player`), the
Transfer Creature carry-over, the cutscene after a win, and My Pet Dungeon (MPD) progress.
The menu state machine and the asset inventory are covered elsewhere; this file names
hand-off addresses only. How to edit the registry for captures: `campaign-progress-registry.md`.

Every row below is a DKII.EXE read (v1.70) unless it says otherwise, so the ceiling is
**STRONGLY INFERRED**; nothing here was observed in play. Function records:
`docs/research/functions/<ADDRESS>.md`.

## 1. The profile object

The profile is one global object at `0x00759B2C`. It is filled from the registry by
`0x0055F6D0` (called from `0x0055EE50`); `0x0055F040` writes defaults when `Version Number`
is missing or not 1. Each setter writes the field and immediately re-saves that one
registry value, so the registry is current after every write (there is no "save profile
at exit" step for these values).

| Profile offset | Absolute | Registry value | Size | Meaning | Default | Provenance | Confidence |
|---|---|---|---|---|---|---|---|
| `+0x0C` | `0x00759B38` | `Level Number` | byte (DWORD in registry) | Last launched campaign map index (1-24 campaign, 26-30 secret) | 1 | load `0x0055F820`; set `0x00560E30` from launch `0x0053A0A1` | STRONGLY INFERRED |
| `+0x0D` | `0x00759B39` | `MPD Level Number` | byte | Last launched MPD level (1-7) | 1 | load `0x0055F89A`; set `0x00560EC0` from `0x0053A194` | STRONGLY INFERRED |
| `+0x0E` | `0x00759B3A` | `Transfer Creature ID` | byte | Creature type waiting for the next level (0 none) | 0 | `0x00560820`; read `0x0050C683` | STRONGLY INFERRED |
| `+0x0F` | `0x00759B3B` | `Transfer Creature Level` | byte | Its experience level | 0 | `0x005608B0`; read `0x0050C787` | STRONGLY INFERRED |
| `+0x10` | | `Secret Levels` | 16 dwords | Secret level n-1 unlocked (index 4 is overridden by the moon, §2.4) | zeros | `0x00561420`, getter `0x005613F0`, clear `0x005614B0` | STRONGLY INFERRED |
| `+0x51` | | `Secret Levels Completed` | 16 dwords | Secret level won | zeros | set `0x005615F0`, clear `0x00561540` | STRONGLY INFERRED |
| `+0x91` | | `Special Levels Completed` | 16 dwords | Won a level flagged "special" (§2.4) | zeros | set `0x00561750` | STRONGLY INFERRED |
| `+0xD1` | | `Pet Dungeon Levels Completed` | 7 dwords | MPD level i (0-based) open | `[0]=1`, rest 0 | load `0x005600CB`; set `0x00561890`; count `0x00561920` | STRONGLY INFERRED |
| `+0xED`..`+0x109` | | `Invert Mouse`, `Alternative Scroll`, `Mouse Sensitivity`, `ScrollSpeed`, `Message Tabs Enabled`, `World Tooltips Enabled`, `Use Blood`, `Language` (word, `+0x109`) | dwords | Options (sibling scope) | 0,0,2,10,1,1,1,9 | `0x0055F6D0` | STRONGLY INFERRED |
| `+0x11F` | `0x00759C4B` | none | dword | The last MPD score (Total Evil Rating "slot 0", outside the saved array) | 0 | `0x004BF2F1`; zeroed at MPD launch `0x0053A010` | STRONGLY INFERRED |
| `+0x123` | | `Total Evil Rating` | 40 dwords, 1-based slot n at `+0x11F + 4n` | Score of the last finished attempt of map slot n | zeros | set `0x00561BF0`, get `0x00561CC0` | STRONGLY INFERRED |
| `+0x1C3` | | `Level Attempts` | 40 dwords, 1-based slot n at `+0x1BF + 4n` | Failed attempts of map slot n | zeros | inc `0x00561940`, clear `0x00561A20`, get `0x00561AF0` | STRONGLY INFERRED |
| `+0x263` | | `Key Table` | 0x200 | Key bindings (sibling scope) | | `0x0055F6D0` | STRONGLY INFERRED |
| `+0x863` | | `User Cameras` | 0x1E | Saved camera bookmarks (sibling scope) | | `0x0055F6D0` | STRONGLY INFERRED |
| `+0x881` | | `Player Level Status` | 40 dwords, 1-based slot n at `+0x87D + 4n` | Map slot state, §2.1 | absent | set `0x00560D60`, set-unless-done `0x00560E00`, get `0x00560D40` | STRONGLY INFERRED |
| `+0x8DD` | | none | 8 x 0x44 | Per-player copy of the 17-dword level-end score block, for the statistics screen | | `0x00561E00` | STRONGLY INFERRED |
| `+0xAF9` | | none | 8 dwords | Per-player "row valid" flag for that block | | `0x00561E00` | STRONGLY INFERRED |
| `+0xB15` | | none | 8 dwords | Per-player value from `0x004C0280` (unread) | | `0x00561E90` | STRONGLY INFERRED |
| `+0xB35` | `0x0075A661` | `Secret Level Number` | dword | 1-5 while a secret level is being played, else 0 | 0 | set `0x00561ED0` from `0x005393F8` | STRONGLY INFERRED |
| `+0xB39` | `0x0075A665` | none | dword | 1 when the MPD level is a user-named file, 0 for `mpd<n>` | 0 | `0x0053A010` | STRONGLY INFERRED |
| `+0xB3D`, `+0xB7D` | | `Multiplayer Name`, `Multiplayer Game Name` | 0x40 each | sibling scope | | `0x0055F6D0` | STRONGLY INFERRED |

**Slot numbering** (the index every 40-dword array uses; the string table `0x006B83EC` maps
slot to level file):

| Slots | Maps |
|---|---|
| 1-24 | level1, 2, 3, 4, 5, 6a, 6b, 7, 8, 9, 10, 11a, 11b, 11c, 12, 13, 14, 15a, 15b, 16, 17, 18, 19, 20 |
| 25 | `null` (never launched: `0x0053A010` refuses it) |
| 26-30 | secret1 ... secret5 (Secret Level Number + 25) |
| 31-37 | MPD 1-7, attempts only (MPD Level Number + 30, `0x004BEB4A`) |

`Player Level Status` uses slots 1-24 only (the getter accepts 1-39). Total Evil Rating uses
1-24 and 26-30. This corrects the old reading that index 0 is level 1: slot 1 is level 1 and
lands at byte 0 of the registry value, so the byte layout in `campaign-progress-registry.md`
is unchanged.

Two more values exist: `Version Number` (must be 1, else defaults) and the hi-score table,
which is a file, not the registry: `Data\Settings\HiScores.dat`, ten 0x84-byte rows
(`0x0053E610`).

## 2. Unlock rules

### 2.1 Status values

| Value | Meaning | Map shows | Selectable | Provenance |
|---|---|---|---|---|
| 0 | locked | hidden marker | no | `0x005395A0`, `0x005393B0` |
| 1 | won | land piece tinted (`+0x74/+0x78` = `0x55A`) | **yes, replayable** | `0x005393B0` accepts 1 or 2 |
| 2 | open, not won | marker visible, blinks for the first 10 s | yes | `0x00538D00` |
| 3 | the branch sibling not taken | land piece tinted | **no** | `0x005393B0` accepts only 1 or 2 |

Confidence: STRONGLY INFERRED for the values and selectability; the visual meaning of
`0x55A` / `0xFFF` (written to thing `+0x74`/`+0x78`) is HYPOTHESIS (likely a light/tint
level).

### 2.2 What a win and a loss write (`Player_EndLevelStatistics` `0x004BE630`)

The status, attempts, Transfer-ID, secret/special and finale rows run only for the local
player (`0x004BEAC2`, `0x004BEBCD`) and in game mode 1 (`[0x0075AD4B]`), except the MPD
rows, which are mode 4. The last two rows sit on the shared tail from `0x004BEEEE` and have
no player gate; the routine runs once per player (guard `[player+0x19A]`, `0x004BE650`).

| Event | Writes | Address |
|---|---|---|
| Loss, campaign map | status[slot] = 2 unless already 1/3; Level Attempts[slot] += 1; **Transfer Creature ID = 0** | `0x004BEAE4`-`0x004BEB22` |
| Loss, secret level | Level Attempts[25 + secret] += 1 | `0x004BEADE` |
| Loss, MPD (mode 4, not a user file) | Level Attempts[30 + mpd] += 1 | `0x004BEB36` |
| Win, campaign map | status[slot] = 1 unless 3, then the branch table below | `0x004BEBF1`-`0x004BED5F` |
| Win, secret level | Secret Levels Completed[secret - 1] = 1 | `0x004BED61` |
| Win, MPD | Pet Dungeon Levels Completed[mpd] = 1 (opens MPD mpd+1) | `0x004BED7E` |
| Win, level flagged secret (LEVEL flags `0x20`) | Secret Levels Completed[world `+0x722E` - 1] = 1, then `0x004BF9E0` | `0x004BEDE5` |
| Win, level flagged special (LEVEL flags `0x40`) | Special Levels Completed[world `+0x722E` - 1] = 1, then `0x004BF9E0` | `0x004BEE11` |
| Win, other campaign level | `Player_FinaleSequence` `0x004BF410` | `0x004BEE38` |
| Either, any player who reaches the routine, modes 1 and 4 | Total Evil Rating[slot or 25+secret] = that player's weighted score (overwritten every attempt, win or loss; the last caller wins); MPD to `+0x11F`. Whether an AI keeper ever reaches it in a campaign is unread | `0x004BF2F9`-`0x004BF329` |
| Either, any player | per-player score block copied to profile `+0x8DD` for the statistics screen | `0x004BEF0F` |

All are STRONGLY INFERRED. "Win sets 1, loss sets 2" follows from the two arms' message
ids (`+0x19E` = `0x2C0` / `0x2BF`) and the attempts increment sitting in the loss arm only.
The global `0x006F2957` bits 3 and 4 are LEVEL flags (`+0x64C0`) bits 5 and 6, copied by
`0x0051E6D0`; the names "secret" and "special" come from the registry values they write.
The source of world `+0x722E` is unread.

### 2.3 Branch table (win of slot s; every "= 2" and "= 3" is set-unless-already-1-or-3)

| Won | Opens (= 2) | Closes (= 3) | Provenance |
|---|---|---|---|
| L5 (5) | 6a (6), 6b (7) | | jump table `0x004BF348`, arm `0x004BEC25` |
| L6a (6) | L7 (8) | 6b | `0x004BEC4B` |
| L6b (7) | L7 (8) | 6a | `0x004BEC57` |
| L10 (11) | 11a, 11b, 11c (12-14) | | `0x004BEC7C` |
| L11a (12) | L12 (15) | 11b, 11c | `0x004BEC97` |
| L11b (13) | L12 (15) | 11a, 11c | `0x004BECCE` |
| L11c (14) | L12 (15) | 11a, 11b | `0x004BECE8` |
| L14 (17) | 15a, 15b (18, 19) | | `0x004BED02` |
| L15a (18) | L16 (20) | 15b | `0x004BED0B` |
| L15b (19) | L16 (20) | 15a | `0x004BED2E` |
| any other s | s + 1 | | `0x004BED50` |

**The branch is the player's choice on the map**: both (or all three) maps open together,
and whichever is won first closes its siblings. Nothing else decides it. STRONGLY INFERRED.

The front end repeats the same table when it comes back from a won campaign level
(`0x00534F70`, from `0x0052F140`), so a status lost between the level and the menu is
repaired. On entering the front end `0x0052F140` also finds the first slot that is 0 or 2
and, if it is 0, sets it to 2 (a guard, so at least one map is always open).

A new campaign (`0x00538B90` with 1, and `0x0052EFE0` when `0x0055DDF0()` returns 1): status
1-25 = 0, Total Evil Rating 1-25 = 0, Level Attempts 1-40 = 0, status[1] = 2, Secret Levels
and Secret Levels Completed cleared, Level Number = 1, MPD Level Number = 1, `+0xB39` = 0.
Special Levels Completed and Pet Dungeon Levels Completed survive it.

### 2.4 Secret levels

| Secret | Unlocked by | Provenance | Confidence |
|---|---|---|---|
| 1-4 | Picking up a **Locate Hidden Land** special (object 129) on Level Number 5, 8, 11, 16 (slots = L5, L7, L10, L13), which writes Secret Levels[0..3] = 1 | `0x004A4F70` (specials.md) | STRONGLY INFERRED |
| 5 | The moon: Secret Levels index 4 is never read from the registry; `0x005613F0` returns `0x00559740()`, true when the float at `+0xF4D` of the system object at `0x00756EE8` (filled once at start-up by `0x00557CB0`; the getter loads ECX = `0x00756EE8`) is outside (-0.95, 0.95). The phase is computed once at start-up, so the game must be started near the moon phase | `0x00559740`, phase from `0x00557230` at `0x00557EA9` | STRONGLY INFERRED |

The phase is computed with the Numerical Recipes `flmoon` routine (`0x00556EF0`, Julian base
2415020) between consecutive **new** moons, scaled to -1..1, so by the arithmetic the gate
opens within about 2.5% of a lunation (~0.7 day) of a new moon. Community lore says "full
moon"; **owner question**. The map builder `0x004DEF30` uses the same test only for the
slot-30 (secret 5) piece: its `+0xF3` bit 0 is set only when `0x00559740` returns 1, as in
`0x005395A0`.

On the map secret markers 26-30 are visible only when unlocked (`0x005395A0`); selecting
one sets Secret Level Number to 1-5 (`0x005393B0`) and launches `secret<n>`. A secret
level never changes `Player Level Status`.

## 3. The campaign map

| Fact | Provenance | Confidence |
|---|---|---|
| It is a 3D scene drawn by the engine: the front end has its own world, and the map's pieces are things in it. The meshes are the `3dmap_*` and `MPD_map*` resources `0x004DFAA0` requests. | `0x004DFAA0`, `0x00538D00` picks things through the renderer (`[world+0x31182]` slot `+0x4C`) | STRONGLY INFERRED |
| Each map slot has a **land piece** (the `3dmap_level` mesh, object type `0x66`; array `0x00713682`, 25 slots, then five secret pieces at `0x007136B4`; meshes from resource `+0x9F24 + 0x1B i` per `0x004DEF30`) tinted `0x55A` for status 1/3, and a marker (`0x00713652`, 24) shown only at status 2; secret pieces are hidden until unlocked | `0x005395A0`, `0x004DEF30` | STRONGLY INFERRED |
| Picking: objects whose type byte `+0x12C` is `0x66` (land pieces) or `0x73` (creator unread) carry their slot in byte `+0x173` (1-30, or 151-174 = slot + 150 for a second piece). Hover over a selectable slot lights a piece through front-end world `+0x5F78 + 2n` (`0xFFF`; that this is the `0x00713682` array is unchecked), raises the marker with sound `0x32A`; leaving it restores both | `0x00538D00` | STRONGLY INFERRED |
| For the first 10 s after entering, open (status 2) markers blink every 200 ms and the route pieces from the last-played map to the newly opened one blink (`0x00539710`, cases for Level Number 6-16, 21, 22) | `0x00538B90` sets the timer, `0x00538D00`, `0x00539710` | STRONGLY INFERRED |
| Click on a selectable slot: Secret Level Number set, camera path 253 (`0xFD`) and menu `0x25` started, then `0x0053A010` launches the level | `0x005393B0`, `0x0053A010` | STRONGLY INFERRED |
| Entering the map plays front-end camera path 251 (`0xFB`) (`0x00538000` case 13) | `0x00538000` | STRONGLY INFERRED |
| A 2D fallback exists: when `[0x0075AEAB]` is set the map is a backdrop image and the slot comes from a byte index bitmap (front-end world `+0x310B`, stride `+0x3117`) | `0x00538D00` | STRONGLY INFERRED |
| New Campaign on an open, never-attempted level 1 skips the reset dialog, resets the profile (command `0x6D` = `0x00538B90(1)`) and flies to the map (path 251); nothing starts level 1 until a map piece is clicked | `0x00538000` case 1 (`0x005380E5`-`0x0053810A`), `0x005306F0` | STRONGLY INFERRED |
| Per-level text on the map (name, briefing) | not found in the map code; the launch loads two `Obj Shots\<level> <n>` images (`0x0053AC60`) for the next screen | UNKNOWN; next `0x005391F0`(1, 7) menu 7 and `0x005306F0` command `0x2B` |

## 4. Transfer Creature

| Fact | Provenance | Confidence |
|---|---|---|
| Set only by the **Transfer Creature** special (object 140), in game mode 1, on a creature the keeper owns: its type (def `+0x770`) and level (`+0x403`) go to the registry, then the creature leaves the level | `0x004A3FC0` (specials.md) | STRONGLY INFERRED |
| Losing a campaign level clears the ID (the level byte stays) | `0x004BEB1C` | STRONGLY INFERRED |
| At the next level's start (not a restored save, world `+0x7239` bit `0x40` clear), if the ID is non-zero: a creature of that type is created for the local player at the Dungeon Heart's tile centre, **two tiles toward lower Y**, facing a random quarter-turn, scale `0x1000`, state 1; it is raised by `Creature_LevelUp` (level - 1) times; then both values are written 0 | `Level_InitTriggersAndStart` `0x0050C67D`-`0x0050C7BF` | STRONGLY INFERRED |
| Kept: type and level only. Experience, name, health, equipment-like state are not stored; health is whatever `Creature_LevelUp` rescales to | same | STRONGLY INFERRED |
| The level start does not test the game mode, so a stored ID would also appear in a skirmish/MPD start; only mode 1 ever stores one | same | STRONGLY INFERRED |

## 5. Level start and level end

| Step | What happens | Provenance | Confidence |
|---|---|---|---|
| Launch | `0x0053A010`: Level Number = slot, game mode 1, file name from `0x006B83EC`, objective shots loaded, menu 7 shown, command `0x2B` issued. No movie plays before a level | `0x0053A010` | STRONGLY INFERRED |
| Entrance camera | From the level's own script (trigger action 31, paths 201-222), not from the front end | `camera-paths.md` §2 | STRONGLY INFERRED |
| Transfer creature | §4 | | |
| End (in game) | Win/loss writes §2.2; result screen and Escape/Space handling are in `game-actions.md` §3 (`0x0042BE90`, action 134, action 75) | `0x004BE630` | STRONGLY INFERRED |
| Campaign win finale | `Player_FinaleSequence` `0x004BF410`: finds an object with definition id 28 (`+0x12C`) in a world list, tries up to 500 random offsets of -8..7 tiles from it (more than ~5 tiles away for the first 128 tries) for a tile that is reachable, not in a room of types 3/5/11/13/16/18-23 and free, takes the keeper's Horned Reaper (def `+0x686 & 0x4000`) or creates one there, puts it in state 228, and queues actions 15, 4 (camera to that point) and 6 (`0x5800`, 1000), then action `0x74` | `0x004BF410` | STRONGLY INFERRED (what object 28 is: UNKNOWN) |
| Secret/special/MPD win | `0x004BF9E0`: camera to the heart (action 4), a deadline of 6 (14 with `+0x134`) turns at player `+0x111D`, action `0x74`, and every creature not in a room of type 12 is reset and starts a celebration (action `0x39`, state 270) | `0x004BF9E0` | STRONGLY INFERRED |
| Paths 100/101 | No code starts them: `0x0044A370` is called with constants only by the front end (250-274) and in game only through action `0x73`, which the shipped scripts never issue for 100/101 | `camera-paths.md` §2, xrefs of `0x0044A370` | STRONGLY INFERRED (likely unused) |
| Statistics screen | Front end, one row per player (`0x0053C830`): "won"/"lost" title (front-end strings `0x15`/`0x16`), time from `+0x3D` as mm:ss or hh:mm, the score block fields 0-3, 5-10 and 11 (20.12 fixed point shown as `a.b`), and attempts = Level Attempts[slot] (+1 when won). Data: profile `+0x8DD` copies | `0x0053C830`, `0x00561E00` | STRONGLY INFERRED |
| Hi-score | If the slot's Total Evil Rating qualifies, a name-entry row is offered and saved to `HiScores.dat` (`0x0053E320`, `0x0053E610`); its label is the level number with branch letters | `0x0053E320` | STRONGLY INFERRED |
| Cutscene | On the return to the front end after a **won** campaign map (status 1, not secret, `[0x0075AD4F]` = 1), `0x0053B980` plays `CutSceneLevel<N>.tgq`, N = level number (6a/6b share 6, 11a-c 11, 15a/b 15; `0x0053C320`). Before it, a text screen with string id from the 9-byte table `0x006B0DD0` and speech id + 1. Level 20 plays `Outro.tgq` then `BullfrogOutro.tgq`. Level 13's movie is skipped when Language (`+0x109`) is 7 or 0x12 | `0x00534F70`, `0x0053B980` | STRONGLY INFERRED |
| Back to the map | `0x00534F70` re-applies the branch table and sends the front end to menu 3 (from state `0x1A`), `0x16`, `0x1B` or `0x17` by the state the game left | `0x00534F70` | STRONGLY INFERRED (menu ids: sibling scope) |

Movie gallery: `0x0053B5F0` sets the "seen" flag (`+5`) of each of the 22 `0x006B0DD0`
rows from the map statuses (row 0 always; a branched level counts if either branch is
won). Hand-off to the menu sibling.

## 6. My Pet Dungeon

| Fact | Provenance | Confidence |
|---|---|---|
| Seven levels `mpd1`..`mpd7`; MPD 1 is open from the first run (`Pet Dungeon Levels Completed[0]` = 1 written when the value is created) | `0x0055F6D0` at `0x00560121`-`0x00560173` | STRONGLY INFERRED |
| Winning MPD n sets `[n]` = 1, which opens MPD n + 1 | `0x004BED99` | STRONGLY INFERRED |
| A user-named MPD file can also be launched (`+0xB39` = 1); it records no progress or attempts | `0x0053A010`, `0x004BEB2E` | STRONGLY INFERRED |
| The MPD table's look grows with progress: the 2D backdrop variant and a front-end creature placed on the table (`0x004DFD10`, type by count 2-7: 14, 30, 13, 29, 31, 21) both use the open count `0x00561920` | `0x005391F0`, `0x004DFD10` | STRONGLY INFERRED |
| Which MPD buttons are enabled on the MPD table | UNKNOWN; next `0x005384F0` (calls `0x0053A010` at `0x00538890`/`0x005388A2`) | |

## 7. Save games carry the campaign

`0x0055B980` (from the save loader `0x0050ADE0`) reads a 0x351-byte block: seven per-player
score blocks into profile `+0x8DD`, options, Level Number, MPD Level Number and, in mode 1,
status 1-25, Secret Levels, Secret Levels Completed, Secret Level Number and Special Levels
Completed, each through its setter, so **loading a save overwrites the registry's campaign
progress**. STRONGLY INFERRED.

## Unread / next

| Item | Next address |
|---|---|
| Who reads Secret/Special Levels Completed (map trophies?) | xrefs of profile `+0x51`/`+0x91`; getter `0x005616A0`, `0x00561540` |
| What `0x0055DDF0` tests before the reset in `0x0052EFE0` | `0x0055DDF0` |
| Source of world `+0x722E` (secret/special index) | LEVEL loader; candidate KWD `SpecialLevelIndex` (file 25545) |
| Briefing screen text and what menu 7 shows | `0x005391F0`, `0x005306F0` command `0x2B`, `0x00535C30` |
| Text/speech ids in `0x006B0DD0` (0x589 ...): which string table | `0x00553AF0` |
| Thing id 28 in the finale; state 228 | `0x004BF410`, `0x00473F80` |
| `0x004C0280` (per-player value at `+0xB15`) | `0x004C0280` |
| MPD table input | `0x005384F0` |
| `0x0053C3C0` cases 1-4 fully (restart / next after the result) | `0x0053C3C0`, `0x0053C520` |

Estimated unread in this scope: about **14 functions** (the nine rows above plus
`0x00536FA0`, `0x0055B480`, `0x005392E0`, `0x00539490`, `0x0053CAF0`).

## Questions only the owner can answer

1. Does secret level 5 appear at the **full** moon or the **new** moon? The arithmetic says new.
2. After winning a campaign level, does the Horned Reaper appear and walk somewhere before the
   result screen?
3. After choosing 6a, is 6b still shown on the map (dimmed) and can it never be played?
4. Is a text screen with a Mentor line shown before each `CutSceneLevel` movie?
