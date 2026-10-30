# Level-end statistics: the score block, the scores and the statistics screen

**Scope:** the 0x44-byte statistics block at player `+0x142`..`+0x185`, who writes
each field and when, the two weighted scores and the keeper rank built from it,
and how the front end prints it. Read from `DKII.EXE` 2026-10-04 (weights) and
2026-10-26 (everything else). The field names are the original's, from the
level-statistics debug dumper ([debug-dumpers.md](debug-dumpers.md), *Level
statistics*); the player record around it is [player-object.md](player-object.md).

## 1. Life cycle

- **Zeroed** at player set-up (`Player_Initialise` `0x004B8640`, 17 dwords from
  `0x004B871D`) and again at level start by `0x004BE370`, whose one call site,
  `Level_InitTriggersAndStart` at `0x0050C02A`, sits in a loop over the player
  list (head `+0x7A`, next `+0x3A`, `0x0050C017`–`0x0050C03E`), so every player
  is zeroed; the loop is skipped for a level restored from a save (world `+0x7239`
  bit `0x40`, `0x0050C00E`). CONFIRMED.
- **Running counters** are bumped by game events through the level (§2).
- **Level end** is `Player_EndLevelStatistics` `0x004BE630`, entered once per
  player (it returns at once when status `+0x19A` is already set, `0x004BE650`).
  It writes the status, samples four fields from live state, adjusts the special
  bytes, counts creatures left dying, sets `LevelWon`, computes the keeper rank
  (§4), sets `LandOwned`, copies the block to profile `+0x8DD + 0x44 x Number`
  and sets that row's valid flag at `+0xAF9 + 4 x Number` (both `0x00561E00`, at
  `0x004BEF0F`) and, in modes 1 and 4, the score (§3). CONFIRMED.

## 2. The fields

Block offset = player offset − `0x142`. "End" means written only by `0x004BE630`.
The credited player is the record written; no field is gated by player number
except where stated.

| player | block | name | writers, event and amount | conf. |
|---|---|---|---|---|
| `+0x142` | `+0x00` | `DungeonHeartsDestroyed` | `Heart_OnDestroyed` `0x004DA560` at `0x004DA7B2`: +1 to the player named by its tag argument, which the heart's caller passes as the heart owner's `KilledByPlayerId` (`[heart+0x2A]+0x132`, `0x0049B7B2`–`0x0049B7CE`), when that id is a live thing slot (class byte `0x006ED540[id]` non-zero, `0x004DA725`–`0x004DA72D`) and not the heart's owner; the same player takes the owner's `CurrentMana` (`0x004DA783`–`0x004DA7A5`). Any game mode | CONFIRMED; that `+0x132` names the destroyer STRONGLY INFERRED (its writer not read) |
| `+0x146` | `+0x04` | `EnemyCreaturesKilled` | Death handler [0x00487C70](functions/00487C70.md) at `0x00488311`: +1 to the credited killer when the victim is not `IsGood` (definition `+0x686` bit 8) and the killer is not the victim's owner (`+0x28`). End pass `0x004BEA16`: +1 for each other player's creature left dying (`+0x3E6` bit `0x200000`) whose `+0x36` names this player and whose owner is not the hero player | CONFIRMED |
| `+0x14A` | `+0x08` | `HeroesDestroyed` | Same handler at `0x004882DC`: +1 for an `IsGood` victim, no owner test. End pass `0x004BE9E6`: a dying creature owned by the hero player (`[0x006F254C]`). Mode 4 also adds `MpdScoreHeroKilled` (§3) | CONFIRMED |
| `+0x14E` | `+0x0C` | `RoomsTakenOver` | `Room_TransferOwnership` `0x004E5650` at `0x004E5890`: +1 to the new owner (room `+0x1D`) for every transfer, except room types 8, 17 and 22 (the three bridges) | CONFIRMED |
| `+0x152` | `+0x10` | `RoomExtraPoints` | Same routine at `0x004E58D1`, every type: += slab count (room `+0xA7`) x per-tile price (definition `+0x43F`) / 100, truncated | CONFIRMED |
| `+0x156` | `+0x14` | `LandOwned` | End, `0x004BEF09`: copied from `DungeonArea` `+0xB3F`. That is a live count: `Tile_SetOwner` `0x004559D0` calls `0x004BE070` with −1 for a non-zero old owner (`0x00455A07`) and +1 for the new one (`0x00455AA5`) whenever a tile changes hands, any terrain. A first value stored at `0x004BE678` (`0x004BFE00`: owned tiles that are not navigation-solid and carry no room) is overwritten before the copy and never reaches the profile | CONFIRMED; that the map's initial tiles count STRONGLY INFERRED (loaded through `Tile_SetTerrain`, order against `Player_Initialise` not read) |
| `+0x15A` | `+0x18` | `GoldMined` | `Imp_MineGoldAction` [0x004971D0](functions/004971D0.md) at `0x004973CF`: += the gold one strike releases, to the miner's owner `[creature+0x2A]`, whenever that is non-zero. The amount is `trunc(old x G / H) − trunc(new x G / H)` (`0x00497347`–`0x00497385`; old = the slab's gold word before, new = after, `G` = terrain `+0x185`, `H` = terrain `+0x1AD`), or `GoldMinedFromGems` per strike on a gem face; all of it counts, whether it goes to the purse or the floor. The MPD gold event (`0x004973A7`) is separate and fires only when the slab's word reaches 0 | CONFIRMED |
| `+0x15E` | `+0x1C` | `ManaStored` | End, `0x004BE685`: `CurrentMana` `+0x82` | CONFIRMED |
| `+0x162` | `+0x20` | `ItemsManufactured` | The crate maker [0x004F5240](functions/004F5240.md) at `0x004F53D8`–`0x004F53E4`: +1 per crate, to the crate's own owner `[crate+0x2A]`. The crate is created for the neutral player (`Object_Create` with `[0x006F2548]`, `0x004F52B2`) and takes the Workshop owner (`+0x24`, `0x004F539C`) only when the room admits it; when both capacity tests fail (`0x004F537B`, `0x004F538F`) the neutral player is credited ([workshop-crates.md](workshop-crates.md) §3) | CONFIRMED |
| `+0x166` | `+0x24` | `CreaturesCommanded` | End, `0x004BE68E`: `TotalNumberOfOwnedThings[0]` `+0x16`, the creatures owned at the end, not a tally | CONFIRMED; Imps included STRONGLY INFERRED |
| `+0x16A` | `+0x28` | `CreaturesConverted` | `0x004F2800` at `0x004F28FC`: +1 to the creature's new owner, the torture room's owner. Its only caller is the torture update [0x0047B590](functions/0047B590.md) at `0x0047B71F`, after a `random(100) < TortureChanceOfDyingWhenConverted` roll did not kill the victim. The only writer: no other change of side counts | CONFIRMED |
| `+0x16E` | `+0x2C` | `CreatureTraining` | End, `0x004BE69B`: `0x004BFEA0`, the mean experience level (`+0x403`) of the creatures in list `+0x06`, as 20.12 (`Fixed_Divide`); 0 with none | CONFIRMED |
| `+0x172` | `+0x30` | `LevelWon` (byte) | End: 0 on status 3 (lost, `0x004BEB6C`), 1 on status 2 (won, `0x004BEDA2`); status 1 leaves the zero | CONFIRMED |
| `+0x173` | `+0x31` | `OwnCreaturesKilled` | Death handler `0x00488336`: +1 to the victim's owner (player-object.md, *kill tally*) | CONFIRMED |
| `+0x177` | `+0x35` | `MoneySpent` | += the price paid for a room tile (`Player_BuildRoomTile` `0x004C5D0A`, not when bypassed), a door blueprint ([0x004C33B0](functions/004C33B0.md), `0x004C34DE`) or a trap blueprint ([0x004C3860](functions/004C3860.md), `0x004C38E0`) | CONFIRMED |
| `+0x17B` | `+0x39` | `NumberOfThingsResearched` | `Research_AddPoints` `0x004C2DC0` at `0x004C2FDC`: +1 per completed entry | CONFIRMED |
| `+0x17F` | `+0x3D` | `TimeTaken` | End, `0x004BE6BD`: game turn (`0x00517490`) / `TicksPerSecond` (`0x006F2590`), whole seconds; 0 when that global is 0 | CONFIRMED |
| `+0x183` | `+0x41` | `SpecialsFound` (byte) | `Special_Activate` `0x004A3FE6`: +1 per activation. End `0x004BE94C`: += the things in the player's list `+0x0A` whose type `+0x12C` is 117–129 (specials held, not used) | CONFIRMED |
| `+0x184` | `+0x42` | `SpecialsMissed` (byte) | `World_PlaceGeneratedSpecial` `0x0050FA25`: +1 on every player per random special, which v1.70 never places. End `0x004BE954`: itself − `SpecialsFound`, floored at 0 (sign test) | arithmetic CONFIRMED; "therefore 0" LIKELY (rests on [0050F8C0](functions/0050F8C0.md)'s LIKELY "never placed") |
| `+0x185` | `+0x43` | `SecretLevelsMissed` (byte) | End only, `0x004BE966`: itself − the profile byte `0x00759B7C` (which `Special_LocateHiddenLand` `0x004A4F70` increments), floored at 0. Nothing raises it first, so 0 | CONFIRMED arithmetic; intent HYPOTHESIS |

Writers were found by searching every instruction for each displacement and
keeping the stores whose base is a player record; byte fields were checked at
each byte offset. Indexed and `LEA`-taken forms were included.

## 3. The two weighted scores

**Level end, modes 1 and 4** (`0x004BF211`–`0x004BF2EA`), table `0x0069F300`:

| field | 0 | 1 | 2 | 3 | 5 | 6 | 7 | 8 | 9 | 10 | 11 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| weight | 1000 | 40 | 50 | 5 | 5 | 1 | 2 | 5 | 20 | 40 | 50 |

Field 4 (`RoomExtraPoints`) is added unweighted. Field 11 contributes
`int x 50 + trunc(h x 50 / 100)`, where `h = trunc(frac x 100 / 4096)`. The
twelfth table entry, 500 at `0x0069F32C`, has no reader. Mode 4 stores the sum
at `0x00759C4B`; mode 1 writes it to Total Evil Rating for the campaign slot
(or 25 + secret index) through `0x00561BF0` at `0x004BF329`, on a win and a loss
alike, for any player that reaches it (last one wins). CONFIRMED; that mode 1 is
the campaign and mode 4 My Pet Dungeon is STRONGLY INFERRED.

**My Pet Dungeon also scores live** (Q267). `Variables.kwd` ids 228–233
(`MpdScore*`: 50, 5, 1, 10, 20, 40; globals `0x006F29D5`–`0x006F29E9`) are read
only at their events: hero killed `0x004882FB`/`0x004BEA05`, land owned
`0x00455A70`/`0x00455ACB` (− on loss, + on gain), gold slab mined `0x004973A7`,
item made `0x004F5439`, creature entered `0x004DCB81` (`Portal_CreateArrival`), creature converted
`0x004F28B9` (id 233). Each is gated by mode `[0x0075AD4B] == 4` and the
player's `Number >= 3` (the land pair: `== 3`, outside a restore), and adds the
value to `LevelScore` (world `+0x72A2`, floored at 0) through world slot
`+0x344` (`0x00511650`). CONFIRMED.

**The statistics page in other modes** (`0x0053CAF0`, from `0x0053CD5B`) prints
a total computed from profile block 3 with a second table at `0x006B14A0`:
fields 0, 1, 2, 3 x 40, 50, 5, 5; fields 5–10 x 1, 2, 5, 20, 40, 50; field 11
x 26 (same rounding); field 4 unweighted. Fields 0–3 take the weight the level-end
sum gives the next field, and `0x006B149C` holds 1000: the table reads like the
end table read one entry late. Arithmetic CONFIRMED; whether DK2 meant it is
UNKNOWN. In mode 1 the page prints the slot's Total Evil Rating (`0x00561CC0`)
and in mode 4 `0x00759C4B`.

## 4. The keeper rank (`0x004C0280`)

Computed at level end for statuses 2 and 3 only, before `LandOwned` is set, and
stored per player at profile `+0xB15 + 4 x Number` (`0x00561E90`). Not saved
(STRONGLY INFERRED): the profile's registry value names (`0x0066F444`–`0x0066F4A8`)
include none for it, its only accessors are `0x00561E90` and `0x00561EB0`, and the
save restore `0x0055B980` restores the score blocks through `0x00561E00` but not
the rank.
With `trunc` the float-to-integer call `0x00634F30` (MSVC `__ftol`, which truncates).
Every constant is an x87 double in the image (`0x0066D9A8`–`0x0066DA00`); the
x87 precision mode in play is UNKNOWN, so a quotient that lands on an integer
(such as 83 / 8.3) can truncate either way:

- `s = 40 + H + trunc(L x 10/9 − 10/9)` (the constant is 10/9; `L` loads by an
  arithmetic shift) where `L` = the integer part of
  `CreatureTraining`, and `H` = 0, 6, 8, 9, 10 for 0–4 hearts destroyed (0 above 4).
- `s += trunc(−83 / (CreaturesCommanded + 8.3)) − trunc(133 / (kills + 13.3))
  − trunc(33 / (RoomsTakenOver + 3.3)) − trunc(67 / (ItemsManufactured + 6.7))`,
  `kills` = fields 1 + 2.
- `s += 10` if `LevelWon`; `s += trunc(90 / (OwnCreaturesKilled + 9))`, the one
  field loaded signed (`FILD dword`); the others load zero-extended.
- Time: `T` = game turn x `TicksPerSecond`, `A` = `AverageTime` (variable 242)
  x 60. `T <= A/3` adds 10; else `T < 2A` adds `12 − trunc(6T / A)`.
- `s += 10 − trunc(33 / (CreaturesConverted + 3.3))`.
- Rank = `trunc(s x LevelRating / 4096)` (variable 241; read as 20.12 fixed point
  from the 2^-12 factor, an inference). `s` and the rating load as unsigned
  dwords and only the product's low dword is compared with 100 (unsigned), so a
  negative `s` gives a huge product whose low dword decides. Level 1 ships `LevelRating` 6501 (about 1.59)
  and `AverageTime` 8 (`dk2tools kwd vars .../maps/level1Variables.kld`).

Every term saturates at 10, so `s` tops out near 100 before `LevelRating`.
`TimeTaken` divides the turn by `TicksPerSecond`; this multiplies by it, so with
4 ticks a second (LIKELY, `animation-clock.md`) the time term compares 16x the
seconds with the level's average. The arithmetic is CONFIRMED; whether the
product is a DK2 slip is UNKNOWN.

The page `0x0053CAF0` reads block 3's rank (`0x00561EB0(3)`) and shows Text.str
2895 *Sniveller* (≤ 17), 2896 *Underling* (≤ 33), 2897 *Apprentice* (≤ 50), 2898
*Deviant* (≤ 67), 2899 *Master Keeper* (≤ 82) or 2900 *Immortal*, passing
0xB4F + band to [0x00533450](functions/00533450.md), a one-line wrapper of
`Text_GetString` `0x00553A60`; the six ids resolve to those strings in
`Text.str` (`dk2tools str get`). CONFIRMED.

## 5. The statistics rows (`0x0053C520`, `0x0053C830`)

`0x0053C520` sizes the table by the saved mode `0x0075AD4F`: 4 rows (blocks 3–6)
in mode 3, the set-up's player count in mode 2, else 1 row (block 3). A row is
`0x370` bytes of 0x2C-byte cells, each a label pointer then a 0x28-byte value
string; `0x0053C830` fills the values. Text.str ids are decimal below.

| cell | label | text | value | format |
|---|---|---|---|---|
| `+0x000` | 1516 | Level Won | `LevelWon`: 21 *Yes* or 22 *No* | string |
| `+0x02C` | 1410 | Time Taken | `TimeTaken`: hours and minutes when at least an hour, else minutes and seconds | `%02lu : %02lu` |
| `+0x058` | 1508 | Enemy Keepers Vanquished | field 0 | `%lu` |
| `+0x084` | 1413 | Enemy Creatures Killed | field 1 | `%lu` |
| `+0x0B0` | 1509 | Heroes Destroyed | field 2 | `%lu` |
| `+0x0DC` | 1510 | Rooms Captured | field 3 | `%lu` |
| `+0x108` | 1511 | Land Owned | field 5 | `%lu` |
| `+0x134` | 1411 | Gold Mined | field 6 | `%lu` |
| `+0x160` | 1512 | Mana Stored | field 7 | `%ld` |
| `+0x18C` | 1513 | Items Manufactured | field 8 | `%lu` |
| `+0x1B8` | 1514 | Creatures Commanded | field 9 | `%lu` |
| `+0x1E4` | 1429 | Creatures Converted | field 10 | `%lu` |
| `+0x210` | 1515 | Creature Training | field 11: integer part, then `trunc(frac x 100 / 4096)` | `%lu.%lu` (unpadded: 2.05 prints as 2.5) |
| `+0x23C` | 1636 | Attempts At Level | Level Attempts for the slot (mode 1) or 30 + MPD level (mode 4, 0 for a user map), +1 when won | `%lu` |

CONFIRMED (labels by `Text_GetString` `0x00553A60`, formats from the wide
strings at `0x006B1F98`–`0x006B1FB8`). `RoomExtraPoints` and the block's last
seven fields are never printed.

**Where page 26 draws them** (read 2026-10-27). Coordinates are the 640 × 480 front end's,
as (left, top, right, bottom). Colours are the front end's five-byte B, G, R entries set
by [0x00533E90](functions/00533E90.md).

- **The summary, widget 179, [0x0053CAF0](functions/0053CAF0.md).** It uses fixed screen
  boxes, not the widget's:

  | box | text | colour |
  |---|---|---|
  | (191, 110, 640, 480) | the rank title (2895–2900) | `+0x30332` (255, 84, 0) |
  | (191, 148, 459, 170) | 1641 *Total Evil Rating For Level* | none set: the style keeps the last restored colour, `+0x5FF4` by every user read (PLAUSIBLE) |
  | (460, 148, 640, 170) | the score (§3) | fixed (255, 0, 0) |
  | (191, 172, 459, 194) | 1637 *Overall Total Evil Rating* | `+0x3032D` (191, 191, 191) |
  | (460, 172, 640, 194) | the sum of `0x00561CC0` over slots 1–25 | `+0x3032D` |
  | (191, 194, 459, 216) | 1638 *Specials Found* | `+0x3032D` |
  | (460, 194, 640, 216) | `%d / %d`: block byte `+0x41`, then `+0x41` + `+0x42`, read signed | `+0x3032D` |

  The two Overall rows appear in mode 1 outside a secret level only.
- **The rows, widget 180 at (190, 242), 375 × 128, [0x0053D270](functions/0053D270.md).**
  - The list lays out seven cells from its scroll bar's first row and skips the row
    labelled 1508 *Enemy Keepers Vanquished*: the field is filled but never shown in a
    single-player game.
  - Each label is at the box's left in `+0x3031E`, the label colour. Its value is 250 px
    to the right in `+0x30323` (255, 168, 0), with alignment 1 (taken as right, INFERRED).
  - Lines are 25 px apart and clipped to the box, so about five show at once.
- **The list's scroll bar** is the type-2 child the widget creates
  ([0x0052A0D0](functions/0052A0D0.md)): x = list x + width + record byte 5 (90), so
  (2350, 968, 64, 512) in reference units, (587, 242, 16, 128) at 640 × 480 (0x0052C630 truncates).
  - Its position is the first row. After a game
    [0x0053D810](functions/0053D810.md) sets its range to 0–9 at 0 (0–8 after
    multiplayer or skirmish) through [0x00528F60](functions/00528F60.md), whatever the
    rows.
  - Input is the class's ([0x00528B20](functions/00528B20.md),
    [minimap-controls.md](minimap-controls.md#menu40-scrollbar-read-2026-10-23)): an
    arrow steps one row every GUI update it is held, a press on the track one row, and
    the thumb can be dragged while the pointer stays in the track column. The scroll-bar class reads
    only the pointer and the button, no wheel. DK2's front-end update rate is unread; ours steps once
    per Unity frame.
  - [0x00551BA0](functions/00551BA0.md) draws it 8 px left of and 6 px above its box:
    `1-V-Up_Arrow` at the top, `5-V-Scroll_Bar_BG` 10 px lower, `1-V-Down_Arrow` 8 px
    above the bottom.
  - The thumb is cut from row 8 of `0-V-Scroll_Bar`, as tall as the thumb (38 units, 9 px)
    and 16 px wide, which is wider than the 14 px art. Whether DK2's blit of it succeeds
    is unobserved.
  - The part under the pointer ([0x005507E0](functions/005507E0.md)) glows orange
    (255, 168, 0, [0x0054FD20](functions/0054FD20.md)). Its counter restarts at 8 each
    frame it is hovered and drops by 1 every frame, so the glow stays full for three frames
    after the pointer leaves, then fades through ¾, ½ and ¼.
- **The label colour `+0x3031E`** (and its copy `+0x5FF4`) is copied from the palette object
  `0x00756F34`. Its constructor `0x00559820` makes it white (LIKELY).
- **Page 26's briefing sections 2 and 3** pass mode 1 to [0x0053A580](functions/0053A580.md),
  which lays text out only in mode 0. On this page they show only images:
  - the two objective shots at (20, 110) and (20, 242);
  - the panel strips `BG_Tiles\BG-Objective-00` (456 × 32) at (184, 110) behind the rank,
    `-01` (456 × 75) at (184, 147) behind the summary, and `-03` (456 × 128) at (184, 242)
    behind the list.

  On a secret or special level (level flags 0x20 and 0x40) the second shot is not drawn.
  The briefing page (mode 0) draws `-00` and `-01` behind the main objective, and `-00`
  and `-02` at (184, 242) and (184, 279) behind the sub-objectives.

Page 18's colours, read from [0x0053DBD0](functions/0053DBD0.md):

- The header is (255, 168, 0).
- The entry row is fixed (255, 0, 0).
- The top row is the label colour.
- The other rows are `+0x3032D`.

## Statistics paragraph source

CONFIRMED read,2026-10-29: `0x0053CAF0` treats front-end `+0x3118E` as a
Text.str id. A nonzero id goes through `0x00553A60` and the existing scrolling
text setter `0x0052A330` once, guarded by `0x0074034C` bit0x80. Its producer
remains UNKNOWN (Q686). A direct-offset store search found none; initialization
or indirect writes have not been excluded. No paragraph is fabricated.
