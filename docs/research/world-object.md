# The world object, named by DK2 (Q264)

**Scope:** the field names DK2 prints for the world, transcribed from its debug
dumper at `0x0050EAE9`. Method, caveats and the other nine dumpers:
`debug-dumpers.md`. **A name is not a mechanism** — it says a field exists and
roughly what it holds, never its unit, writer or rule.

| offset | size | name |
|---|---|---|
| `+3A` | word | Logging World [GT %d]... |
| `+15B0` | dword | World Log at GameTick %d |
| `+72E7` | dword | Checksums: Exe %x, Level %x, Data %x, TM %x, Map %x, RM %x, PM %x |
| *(same push)* | | Checksum %x |
| `+15A0` | dword | Seed     %x |
| *(same push)* | | ME Player TagId %d [%ls] |
| `+15B0` | dword | Terrain       %d |
| `+15C0` | dword | Room          %d |
| `+15D0` | dword | Creature      %d |
| `+15E0` | dword | Door          %d |
| `+15F0` | dword | Trap          %d |
| `+1600` | dword | Object        %d |
| `+1620` | dword | CreatureSpell %d |
| `+1610` | dword | KeeperSpell   %d |
| `+1630` | dword | Shot          %d |
| `+15A0` | dword | GameSeed %d |
| `+72A2` | dword | LevelScore %d |
| `+72A6` | dword | LevelScoreSet %d |
| `+72AA` | dword | LevelTimeLimit %d |
| `+72AE` | dword | NumberOfNonUniqueCreatureDescriptors %d |
| `+722F` | byte | UniqueCreatureTypeId %d |
| `+7230` | byte | ClonedCreatureTypeId %d |
| `+7231` | dword | UniqueCreatureCreated %d |
| *(same push)* | | NeutralPlayerId %d |
| *(same push)* | | GoodPlayerId %d |
| `+729B` | dword | TimeToPlaceNextGeneratedSpecial %d |
| `+729F` | byte | CurrentNoOfGeneratedSpecialsOnLevel %d |
| `+72A0` | byte | HornedReaperTypeId %d |
| `+72EB` | byte | LoadingLevelId %d |
| `+7310` | word | TriggerRoot %d |
| `+7312` | word | NumberOfTriggerWhens %d |
| `+7314` | word | NumberOfUsedTriggerWhens %d |
| `+7316` | word | NumberOfTriggerActions %d |
| `+7318` | word | NumberOfUsedTriggerActions %d |
| `+731A` | byte | TriggerRootRepeatCount %d |
| `+731B` | byte | NumberOfSacrificeCombinations %d |
| `+731C` | dword | PotentialManaGain %d |
| `+7320` | dword | PotentialMaxMana %d |
| `+9BAE` | byte | WaterTerrainTypeId %d |
| `+9BAF` | byte | LavaTerrainTypeId %d |
| `+A3B3` | dword | InitialSpecialsOnLevelCount %d |
| `+72B2` | dword | bContinualHeroInvasion %d |
| `+72B6` | word | SpecialInfoPlayerId %d |
| `+72B8` | dword | SpecialInfoMaximumSlaps %d |
| `+72BC` | byte | SpecialInfoNextRoomTypeId %d |
| `+2E70` | dword | TriggerFlag[ %d] %d |
| `+3FF0` | dword | TriggerTimer[ %d] %d |
| *(same push)* | | CreatureOnLevel[ %d] %d |
| `+9BB8` | dword | WorldX %d |
| *(same push)* | | WorldY %d |
| *(same push)* | | StartedAt %d |
| `+4` | word | Duration %d |
| `+6` | word | Radius %d |
| *(same push)* | | Invasion Party Count %d: %d |
| `+2` | byte | Level Objective %d: (%d, %d) |
| `+9B64` | dword | Level Text Id %d: %d |
| *(same push)* | | Previous Reward Id %d: %d |
| *(same push)* | | Current Reward Id %d: %d |
| `+9B94` | dword | Level Intro Override %d: (%d, %d) |

## Bitfields

Each group below is one dword, shifted and masked before the push.

**`+7239`** — `Opened` bit 0, `ResourceHeadersLoaded` bit 1, `ResourceTypesLoaded` bit 2, `ResourceLevelLoaded` bit 3, `ResourceDataValidated` bit 4, `ResourceDataValid` bit 5, `LoadingLevel` bit 6
