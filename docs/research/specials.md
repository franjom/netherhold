# Special objects

Source(s):
- Retail v1.70 `DKII.EXE`, read in Ghidra (pyghidra-mcp), 2026-09-28 and 2026-10-09
- The shipped `Data/editor/Objects.kwd` (`dk2tools kwd objects`)
- The owner's play, 2026-10-09
- The owner's Level 1 capture (Increase Level, §4)

Original version: DK2 v1.70 (GOG)

Built: Increase Level only (`SpecialSystem`, `ActivateSpecialCommand`). Everything else here is
read and not built.

## 1. What a player sees — CONFIRMED (owner, 2026-10-09)

A special is a magic box on the floor.

- When an imp claims the tile it stands on, the mentor says *"Ahhh, I see you have discovered a
  magical item"*.
- If the keeper has a Library with room to store it, an imp drags the box there. Its effect then
  appears in the spells menu as a single-cast item (answers Q495).
- With no Library, or no room, the box stays on its tile, and the keeper can click it with the
  Hand of Evil to cast its effect.
- A box on an enemy tile or on an unclaimed dirt path cannot be clicked.

## 2. The objects

Objects 117–129 and 140 carry `TypeSpecial` (definition `+0xF4` bit 2) in `Objects.kwd`, and so
does object 7, *Special [ <not used> ]*. No shipped use of 7 has been found; the dispatcher has no
arm for it and the hand's table rejects it, though action 88 would destroy it. The dispatcher
switches on the object's definition id (`+0x12C`).

| Id | Name | Arm |
|---|---|---|
| 117 | Increase Level | `0x004A42C0` |
| 118 | Reveal Map | inline in `0x004A3FC0` |
| 119 | Make Safe | `0x004A43E0` |
| 120 | Destroy Walls | `0x004A45F0` |
| 121 | Increase Gold | `0x004A4710` |
| 122 | Mana Boost | inline in `0x004A3FC0` |
| 123 | Stun Imps | `0x004A4870` |
| 124 | Receive Imps | `0x004A4980` |
| 125 | Make Happy | `0x004A4BD0` |
| 126 | Make Unhappy | `0x004A4C90` |
| 127 | Kill Creatures | `0x004A4D80` |
| 128 | Heal All | `0x004A4E50` |
| 129 | Locate Hidden Land | `0x004A4F70` |
| 140 | Transfer Creature | inline in `0x004A3FC0` |

## 3. Activation — STRONGLY INFERRED (binary)

Three inputs post action 88 with the object's tag and the player.

1. **The Hand of Evil** (`0x004075E0`, the object arm at `0x00407AD5`). For a special, the tile
   under the object must be owned by the clicking player (`+0x8 & 0xFFF`). The object's
   predicate at vtable `+0x74` must return 0 (§7). Then a jump table on the definition id posts
   action 88 for 117–126, 128 and 129. For 127 and 140 it enters a targeting mode instead
   (`+0x13DB` = 5, the subtype at `+0x13EA`, the object at `+0x13E8`). The ownership test is
   §1's rule: it lives in the input path, not in the action.
2. **The spells panel.**
   - `0x00411140` counts the keeper's spells. It skips any spell for which `0x004BAFA0` (not
     read) returns 1, and spells 5 and 14. The specials stored in the keeper's Libraries follow
     them: each Library (room type 6) is walked through its object chain (`+0xC`, next
     `+0x126`).
   - A click past the spells, while the panel word `+0x2722` is zero, selects the nth stored
     special (`0x00411790`).
   - `0x0040A280` then posts action 88, or enters the same targeting mode for 127 and 140.
3. **The computer keeper** (`0x00500520`), `computer-keeper-management.md` §3. It keeps the last
   qualifying object of its scan, and never activates 127 or 140.

All three switch tables also list id 8, the Torture Wheel. It has no `TypeSpecial` flag, so the
action handler refuses it.

**The action** (`0x00513760`) requires a live object of thing class 2 whose definition carries
`TypeSpecial`. It checks no ownership, distance or reach. It decrements the world byte `+0x729F`
when the object's byte `+0x188` is set. It calls `0x004A3FC0` with the player and the action's
word parameter (the target's tag for 127 and 140). It then destroys the object **whatever the
arm returned**, so a refused Kill Creatures still uses the box up.

**The dispatcher** (`0x004A3FC0`) increments the player's byte `+0x183` before it switches.
Every arm except 127's and 140's returns 1.

## 4. What each special does — STRONGLY INFERRED (binary)

"The keeper's creatures" means the player's creature list (`+0x6`, next `+0x10`). "The other
players" means every player in the player list (`+0x10FD`, next `+0x3A`) other than the
activator. That includes allies, heroes and the neutral player.

- **117 Increase Level.** Each of the keeper's creatures gains one level (`0x0048B120`, which
  stops at 10). Then it gains its new level-scaled maximum health through the health routine,
  which in effect heals it fully. CONFIRMED by capture: the original Level 1 packet activates
  slot 1128 at turn 1708. The level's only placed special is subtype 117 at (12,13). All seven
  keeper Goblins read 1875 health on turn 1708, and one had 890 on turn 1707. That shows the full
  heal and the 1500 → 1875 level-2 maximum. Mapping slot 1128 to our object 1 is STRONGLY
  INFERRED (`build/verification/level1-observed-entity-map.csv`, local; Q444).
- **118 Reveal Map.** Only when the activator is the local player (world vtable `+0x264`), it
  calls `0x00451EF0`. That sets the explored bit (`+9` bit `0x20`) on every tile, refreshes
  each tile and posts a redraw. The reveal is permanent and per viewer, as discovery is
  (`fog-reveal-sources.md`). This answers Q194's Reveal Map half.
- **119 Make Safe.** For each tile the keeper owns that is not solid to navigation (world vtable
  `+0x208`, `0x00509200`, bit 3 of the route-map byte; `bridge-placement.md`), it visits the
  eight neighbours (offset table `0x006BC370`). A neighbour qualifies if it is a diggable wall
  (`0x00453D10`), its owner is the keeper or the neutral player, and its health differs from the
  terrain's `MaxHealth` (`+0x1AD`). It then receives tile work of `MaxHealth` less its health, in
  the keeper's name (`0x00452F60`). Tile work converts a tile at `MaxHealth` to its
  `MaxHealthType` (`tile-work.md`), so every wall bordering the keeper's open land becomes its
  reinforced form.
- **120 Destroy Walls.** It scans every tile on the map. A tile qualifies if it is not the
  keeper's, is a diggable wall, its owner is an enemy by the alliance test (`0x004BF390`), and
  its terrain is `PlayerColouredWall` (flag `0x800`). It then receives work of minus its health,
  so tile work converts it at zero (`tile-work.md`).
- **121 Increase Gold.** It spends `SpecialIncreaseGoldAmount` (27; 10000) over the keeper's
  Treasuries and Hearts in room-list order. A room's capacity is tiles × `MaxGoldPerTreasuryTile`
  (24) for a Treasury and `MaxGoldPerDungeonHeartTile` (178) × 16 for a Heart. It fills each up to
  that capacity through `Room_DepositGold` (`0x004F3C60`). Any gold left is put down at the
  special's position (`0x004510C0`; `gold-on-the-floor.md`).
- **122 Mana Boost.** It adds `SpecialIncreaseManaAmount` (28; 50000) through `Player_AddMana`
  (`0x004C19C0`, clamped at the ceiling `+0x10EA`). It also adds the same amount to the player
  dword `+0x10EE`, whose meaning is UNKNOWN.
- **123 Stun Imps.** Every worker (`IsWorker`, definition `+0x686` bit 0) of every other player
  is kicked to state 209, *stunned*, after a state-move test (`0x00477310`, whose rule is only
  outlined). How long it lasts is state 209's business and is unread here.
- **124 Receive Imps.** It takes the first creature type with `IsWorker`, in the world's
  creature-type order: the Imp in shipped data. It creates `SpecialReceiveImpsAmount` (188; 10)
  of them for the keeper (`0x004B78D0`). They are spaced evenly on a circle of radius two tiles
  around the Dungeon Heart's centre (16.16 sine and cosine tables at `0x006C1488` and
  `0x006C1C88`, angle step 2048 / 10). Each one that is created gains **four levels** (level 5),
  faces a random direction drawn from the world stream, and is kicked to state 214.
  The owner confirms ten level-5 Imps (2026-10-12), and the Level 4 capture shows ten
  created on the turn the recording activates it (`2026-10-12-level4-packet-replay`). Not built.
- **125 Make Happy.** Each of the keeper's creatures gets `0x00486E20`. While the world anger gate
  (`+0x72A1` bit 0, trigger action 44) is set, that zeroes the six anger counters. The mood is
  recomputed either way (`creature-anger.md`).
- **126 Make Unhappy.** It applies to every creature of every other player. Unless the
  definition is `NeverAngered` (`+0x68A` bit 2), `0x0048B870` calls `0x00486ED0`. Under the same
  gate, that raises all six counters to their 65533 saturation, so the mood is angry. It also
  fires creature event 11. The creature's word `+0x457` is set to 100 regardless; its meaning is
  UNKNOWN.
- **127 Kill Creatures.** It is targeted: it takes one creature, the action's parameter. If that
  creature exists and is not the keeper's, it plays its effect at the target and deals minus the
  target's health through the health routine. Otherwise it returns 0, and the box is still
  consumed (§3).
- **128 Heal All.** Each of the keeper's creatures gains its level-scaled maximum less its
  current health. Whether a creature knocked out (`creature-death.md`) gets up is not read.
- **129 Locate Hidden Land.** It switches on the global `0x00759B38`. Values 5, 8, 11 and 16
  write 1 into slots 0–3 of the profile's `Secret Levels` registry value (`0x00561420`,
  `campaign-progress-registry.md`); any other value writes nothing. Either way it increments the
  byte `0x00759B7C`. That `0x00759B38` holds the current campaign level number is a HYPOTHESIS
  that fits the four values.
- **140 Transfer Creature.** It acts only in game mode 1 (`0x0075AD4B`, single player), on a
  creature the keeper owns. It writes the creature's type (definition `+0x770`) and level
  (`+0x403`) to the profile's `Transfer Creature ID` and `Transfer Creature Level` registry
  values (`0x00560820`, `0x005608B0`). It then clears the creature's flag `+0x3E6` bit 5 and
  removes it (vtable `+0x20`). The creature leaves the level for the next one. Whether
  `Objects.kwd` places 140 on any shipped level is unchecked.

## 5. What every arm also does

Every arm plays an effect (world vtable `+0x38C`) at the object, or at the target for 127. It
posts message 49 to the activator with a variant per subtype, and plays a sound (`0x0075B468`
vtable `+0x20`) through the object's sound handle.

| Id | Effect | Message 49 variant | Sound |
|---|---|---|---|
| 117 | `0xBA` | 2 | `0x80` |
| 118 | `0xBC` | 6 | `0x84` |
| 119 | `0xBD` | 7 | `0x85` |
| 120 | `0xBE` | 8 | `0x86` |
| 121 | `0xBF` | 10 | `0x88` |
| 122 | `0xC0` | 11 | `0x89` |
| 123 | `0xC1` | 12 | `0x8A` |
| 124 | `0xC2` | 13 | `0x8B` |
| 125 | `0xC3` | 14 | `0x8C` |
| 126 | `0xC4` | 15 | `0x8D` |
| 127 | `0xC5` | 16 | `0x8E` |
| 128 | `0xC7` | 18 | `0x310` |
| 129 | `0xC6` | 17 | `0x8F` |

Message 49 presumably carries the mentor's line for the special; its consumer is not traced.

## 6. Discovery and storage

- **Discovery.** When a tile changes owner (`Tile_SetTerrain`, `0x0044FC40`: old owner against new
  at `0x0045002E`, loop at `0x00450043`), each special lying on it posts message 48 to the new
  owner with the object's tag. A terrain change with the same owner posts nothing. Claiming a
  dirt path is such a change, since unclaimed land is the neutral player's; the path taken when
  the old owner is 0 (`0x0044FCDA`) is not traced. This is the likely source of §1's mentor
  line; its consumer is not traced.
- **The carry.** The imp objects handler (`0x0046B9C0`, `imp-task-objects-and-walks.md` §1) sends
  a neutral `TypeSpecial` object to a Library with space. A Library treats bit 2 as a stored
  item (`library-and-research.md`).
- **Counts.** At level start (`Level_InitTriggersAndStart`, `0x0050B5B0`, `0x0050C3E1`–
  `0x0050C45D`), per player: the world dword `+0xA3B3` is zeroed, then each special in the
  player's object list (each first passed to `0x0049BEE0`) adds 1 to it and to the player byte
  `+0x184`, and a Locate Hidden Land (129, `BL` = `0x81`) also to `+0x185`. Since the world dword
  is zeroed per player, it ends holding the last player's count. Their readers are not traced;
  they may feed the end-of-level statistics (`SpecialsFound`, `SpecialsMissed` in
  `debug-dumpers.md`).

## 7. UNKNOWN

- The object predicate at vtable `+0x74`: `0x004B7250` returns bit 1 of the object dword
  `+0x146`, as in `computer-keeper-management.md` §3.
- The meaning of player `+0x183`, `+0x10EE`, world `+0x729F` and object `+0x188`.
- How long state 209 holds a stunned imp.
- Whether Heal All revives a knocked-out creature.
- Who consumes messages 48 and 49.
