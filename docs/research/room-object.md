# The room object, named by DK2 (Q264)

**Scope:** canonical runtime layout for a room instance. Most names come from
DK2's debug dumper at `0x004E70B6`; interpreted links below add binary traversal
evidence. Method, caveats and the other nine dumpers: `debug-dumpers.md`.
**A dumped name is not a mechanism** — it says a field exists and roughly what
it holds, never its unit, writer or rule.

## Established links and interpreted fields

- `+0x06/+0x08` are the previous/next nodes in the owning player's room list;
  `player+0x36` is its head. `room-list.md` owns the traversal evidence.
- `+0x19` points to the loaded `RoomDef`. Furniture placement follows it to the
  definition's `ObjectIds` (`auto-placed-furniture.md`).
- `+0x27/+0x29` (u16 each) are the room's position words (`CentrePos`), which the
  withdraw, the alarm, the threat list and a Vampire's reincarnation aim at. The
  writer is `Room_UpdateCentrePos` (`0x004E4590`). It picks a rule by room type
  through the index bytes at `0x004E4AD8` and the jump table at `0x004E4AC0`
  (binary, 2026-10-04, Q515). CONFIRMED:
  - Heart (5): `FirstSlab` +3 on each axis (`0x004E45D0`), and `NavigationPos` +1.
  - Portal and Hero Portal (3, 0x19): `FirstSlab` +1.
  - Types 0x12–0x18: `FirstSlab` itself.
  - Every other type: the centroid of the slab chain, or a spiral out from it to
    one of the room's own tiles.

  `Room_Construct` zeroes both words (`0x004E389A`). A dragged room therefore
  reads the centre of its box rounded down (Level 3 Treasury 32–36×34–38 →
  (34,36), Lair 25–29×42–46 → (27,44)). The three captured 5×5 Dungeon Hearts
  read their first slab +3, which is one tile past their centre ((24,34) →
  (27,37); Level 2 (17,36) → (20,39); Level 1 (12,26) → (15,29)), and Level 3's
  Portal reads its first slab +1, its true centre (26,29). `FirstSlab` is the
  head of the slab chain. That a placed heart's chain starts at its low corner is
  STRONGLY INFERRED from those captures; the loader's tile order is unread.
  `RoomInstance.CentreFieldX/Y`.
- `+0x2D/+0x2E` and `+0x2F/+0x30` are the head and tail coordinates of the
  room's slab list. Each 16-byte tile record stores the next coordinates at
  `+0x04/+0x05`; zero ends the list. Furniture and room-removal paths both walk
  it. A room therefore does know its tiles, indirectly.
- `+0x67` is `TotalCapacity`; `+0x6B` is the separate `UsedCapacity`.
  `room-conditions.md` and `auto-placed-furniture.md` own their consumers.
- `+0x92/+0x96` hold the two wall-furniture alignment phases. They start at
  minus one and take the coordinate parity of the first eligible slab in list
  order (`auto-placed-furniture.md`).
- `+0xA7` (word) is the slab count, confirmed against live rooms in `room-list.md`.
- **Every room starts from zero, and a sold tile makes new rooms** (binary,
  2026-10-04).
  - Every room is built by `Room_Construct` (`0x004E3800`), whose only caller
    is the allocator `0x004EC490`. It zeroes `+0x49..+0x66`, which is every
    type's overlay, before the type's own constructor slot runs.
  - Taking one tile out of a room (`Room_RemoveTile` `0x004ED440`, reached from
    a sale through `0x004C5DB0`) frees the room at `0x004ED9FA` and rebuilds
    what is left through `0x004ED320`. Each tile either joins a neighbouring
    piece (`0x004EC650`) or starts a new room (`0x004EC5A0` → `0x004EC490` →
    `Room_Construct`). CONFIRMED.
  - Only three types have overlay state copied across the rebuild: type 8
    (`+0x49`), the Prison (`+0x59/+0x5D`) and the Casino (`+0x53`). Every
    other type's overlay, for example the Graveyard's `DecomposeValue` or the
    Hatchery's `ChickensGenerated`, starts again from its constructor.
  - A merge (`Room_Absorb` `0x004ECB70`) writes no overlay field. The surviving
    room keeps its own overlay and the absorbed room's is dropped with it.

## Dumper fields

Duplicate offsets below are room-type-specific overlays printed by different
dumper branches, not simultaneous meanings of one common field.

| offset | size | name |
|---|---|---|
| `+4` | word | Room TagId %d |
| `+6` | word | PlayerRoomListNode (%d, %d) |
| `+A` | word | CreatureList %d |
| `+C` | word | ObjectList %d |
| `+E` | word | AutoPlacedObjectList %d |
| `+10` | word | DeadBodyList %d |
| `+18` | byte | TypeId %d |
| `+1D` | word | PlayerId %d |
| `+1F` | word | RoomManagerNode (%d, %d) |
| `+23` | word | ChangedAreaRoomListNode (%d, %d) |
| `+27` | word | CentrePos (%d, %d) |
| `+2B` | byte | NavigationPos (%d, %d) |
| `+2D` | byte | FirstSlab (%d, %d) |
| `+2F` | byte | LastSlab (%d, %d) |
| `+31` | dword | Closing %d |
| `+35` | dword | Health %d |
| `+41` | dword | DiscoModeActive %d |
| `+45` | dword | SData.DiscoModeStartedOnGameTick%d |
| `+67` | word | TotalCapacity %d |
| `+69` | word | TotalFighterCapacity %d |
| `+6B` | word | UsedCapacity %d |
| `+6D` | word | UsedCreatureCapacity %d |
| `+6F` | word | UsedObjectCapacity %d |
| `+71` | word | UsedDeadBodyCapacity %d |
| `+73` | dword | GameTickLastUpdated %d |
| `+77` | dword | GameTickStartFight %d |
| `+7B` | dword | GameTickFightDuration %d |
| `+7F` | byte | MinimumRoomFightDurationSeconds %d |
| `+80` | byte | SpecialTileOffset %d |
| `+81` | dword | SparkPosition %d |
| `+85` | word | SparkPositionUpdate %d |
| `+87` | byte | SparkSlab (%d, %d) |
| `+89` | byte | SparkEdge %d |
| `+8A` | dword | BeingDeleted %d |
| `+8E` | dword | GameTickLastFightEffect %d |
| `+92` | dword | RoomWallAlignmentMapX %d |
| `+96` | dword | RoomWallAlignmentMapY %d |
| `+9B` | dword | Orientation %d |
| `+9F` | dword | GameTickLastOnChangeSize %d |
| `+A3` | dword | Functional %d |
| `+A7` | word | Size %d |
| `+49` | dword | GameTickLastGenerated %d |
| `+4D` | dword | CreaturesGeneratedSinceLastClaimed %d |
| `+49` | dword | SteamEffectCounter %d |
| `+4D` | dword | SmokeEffectCounter %d |
| `+51` | dword | LavaBubblingEffectCounter %d |
| `+55` | dword | LavaSpillEffectCounter %d |
| `+59` | dword | MaxCrates %d |
| `+49` | dword | GoldTally %d |
| `+4D` | dword | Destroyed %d |
| `+51` | dword | SuckGold %d |
| `+55` | byte | EffectCounter %d |
| `+56` | byte | RegenerateHealthCounter %d |
| `+57` | dword | LastGameTickHealth %d |
| `+5B` | dword | GameTickStartedConstructionSequence %d |
| `+5F` | dword | GameTickLastGeneratedDamagedSFX %d |
| `+4D` | dword | GenerationSpeed %d |
| `+51` | dword | ChickensGenerated %d |
| `+49` | dword | DecomposeValue %d |
| `+4D` | byte | EffectCounter %d |
| `+49` | byte | DripEffectCounter %d |
| `+4A` | byte | ModeSwitch %d |
| `+4B` | dword | PayOutPlacementX %d |
| `+4F` | dword | PayOutPlacementY %d |
| `+53` | dword | Activation %d |
| `+4` | byte | Type %d, TypeId %d |
| `+5B` | byte | QueueHeadIndex %d |
| `+5C` | byte | QueueTailIndex %d |
| `+5D` | byte | QueueLength %d |
| `+5E` | byte | MistEffectCounter %d |
| `+5F` | byte | HandIdleEffectCounter %d |
| `+60` | byte | SplashEffectGameTickLastActivated %d |
| `+4A` | byte | FliesEffectCounter %d |
| `+4B` | dword | HatchTileX %d |
| `+4F` | dword | HatchTileY %d |
| `+53` | dword | GameTickLastFight %d |
| `+57` | word | PitLiftId %d |
| `+4B` | word | SafeCreatureCapacity %d |
| `+4D` | dword | PrisonJailBroken %d |
| `+51` | dword | DoorTileX %d |
| `+55` | dword | DoorTileY %d |
| `+59` | dword | Activation %d |
| `+5D` | dword | DoorOpen %d |
| `+61` | byte | DelayAutoCloseDoorCount %d |
| `+49` | dword | FlagToBurn %d |
| `+49` | dword | GemHolderSoundHandle %d |
| `+4D` | word | GemHolderTagId %d |
| `+4F` | byte | NoChains %d |
| `+50` | dword | ChainTagId %d: %d |
| `+60` | dword | FrontEndTick %d |
| `+49` | word | Left %d |
| `+4B` | word | Right %d |
| `+4D` | word | Top %d |
| `+4F` | word | Bottom %d |

## Bitfields

Each group below is one dword, shifted and masked before the push.

**`+9A`** — `Destroyed` bit 0
