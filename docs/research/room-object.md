# The room object, named by DK2 (Q264)

**Scope:** the field names DK2 prints for a room instance, transcribed from its debug
dumper at `0x004E70B6`. Method, caveats and the other nine dumpers:
`debug-dumpers.md`. **A name is not a mechanism** — it says a field exists and
roughly what it holds, never its unit, writer or rule.

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
