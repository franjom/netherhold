# The computer keeper, named by DK2 (Q264)

**Scope:** the field names DK2 prints for the AI keeper's own state, transcribed from its debug
dumper at `0x00503F61`. Method, caveats and the other nine dumpers:
`debug-dumpers.md`. **A name is not a mechanism** — it says a field exists and
roughly what it holds, never its unit, writer or rule.

| offset | size | name |
|---|---|---|
| *(same push)* | | WallHug %d |
| *(same push)* | | WallHugLeft %d |
| *(same push)* | | FightGoingOn %d |
| *(same push)* | | NextRoomToBuildSet %d |
| *(same push)* | | AllRoomsBuilt %d |
| *(same push)* | | HaveBeenAttacked %d |
| *(same push)* | | CallToArmsInUse %d |
| *(same push)* | | gtDirection %d |
| *(same push)* | | DefendDirection %d |
| *(same push)* | | OpenDirection %d |
| `+0`, bits 14-17 | nibble | CurrentEventTask %d |
| `+0`, bits 18-21 | nibble | NumberOfEventTasks %d |
| `+0`, bits 22-25 | nibble | Task %d |
| `+0`, bits 26-29 | nibble | NextTask %d |
| `+8` | word | AttackingPlayerId %d |
| `+12` | dword | MaxBuildArea (%d, %d, %d, %d) |
| `+4E` | dword | CallToArmsPos (%d, %d, %d) |
| `+72` | word | DigRoomAt (%d, %d) |
| `+76` | word | DigRoomAtCurrent (%d, %d) |
| `+7A` | word | WallHugStart (%d, %d) |
| `+7E` | word | WallHugEnd (%d, %d) |
| `+82` | word | JoinPathStart (%d, %d) |
| `+86` | word | JoinPathEnd (%d, %d) |
| `+8A` | dword | WallHugDistance %d |
| `+8E` | byte | WallHugHeading %d |
| `+8F` | byte | DigDX %d |
| `+90` | byte | DigDY %d |
| `+91` | byte | InnerLoopXOrY %d |
| `+92` | byte | MaxX %d |
| `+93` | byte | MaxY %d |
| `+94` | byte | SquaresTarget %d |
| `+95` | byte | SquaresCurrent %d |
| `+96` | word | Flags %d |
| `+98` | byte | CurrentRoomToBuild %d |
| `+99` | byte | NextRoomToBuild %d |
| `+9A` | byte | CurrentGoldArea %d |
| `+9B` | word | PlayersAttackedFlags %d |
| `+9D` | word | FirstAttackFlag %d |
| `+4AF` | word | TendancyMoney %d |
| `+4B1` | byte | TendancyType %d |
| `+4B2` | dword | TilesLeftBetweenRooms %d |
| `+4B2` | dword | BuildNewRoomWhenMoreSpaceRequired %d |
| `+4B2` | dword | AggressiveVsDefensiveTrapPreference %d |
| `+4B2` | dword | TrapVsDoorPreference %d |
| `+4B2` | dword | DoorPreference %d |
| `+4B2` | dword | ProbabilityOfMovingCreatureForResearch %d |
| `+4B2` | dword | IACreatureSkillLevel %d |
| `+4B2` | dword | AttackingVsDefensiveSpellPreference %d |
| `+4B6` | dword | UseKeeperSpells %d |
| `+4B6` | dword | IAFirstStart %d |
| `+4B6` | dword | IAFirstEnd %d |
| `+4B6` | dword | CorridorStyle %d |
| `+4B6` | dword | BreachAtXPoints %d |
| `+4B6` | dword | UseSightOfEvil %d |
| `+4B6` | dword | SpellPreference %d |
| `+4B6` | dword | CallToArmsUsage %d |
| `+4B6` | dword | ImprisonedCreatureAction %d |
| `+4BA` | dword | BlibBlubBlob %d |
| `+4BE` | byte | MinimumRandomTimeBeforePlacingReseachedRoom %d |
| `+4BF` | byte | DefaultRoomSize %d |
| `+4C0` | byte | TrapAndDoorUsage %d |
| `+4C1` | byte | MaximumImps %d |
| `+4C2` | byte | TilesPerImpRatio %d |
| `+4C3` | byte | ImprisonPercentage %d |
| `+4C4` | byte | BARetreatHealth %d |
| `+4C5` | byte | SpecialSearchChance %d |
| `+4D5` | byte | SpecialFindChance %d |
| `+4D6` | byte | Flexibility %d |
| `+4D7` | byte | IAMinimumCreatures %d |
| `+4D8` | word | IAThreatSuperiority %d |
| `+4DA` | word | BAThreatSuperiority %d |
| `+4DC` | word | BACallToArmsValue %d |
| `+4DE` | byte | BuildLostRoomAfterMinimumTime %d |
| `+4DF` | byte | SpecialRange %d |
| `+4E0` | byte | NeutralRoomRangeFromHeart %d |
| `+4E1` | byte | NeutralRoomRangeFromClaimedArea %d |
| `+4E2` | byte | IAUseCreatures %d |
| `+4E3` | byte | TendancySpeed %d |
| `+4E4` | byte | Openness %d |
| `+4E5` | word | GoldValue %d |
| `+4E7` | word | ManaValue %d |
| `+4E9` | word | RemoveCallToArmsAfter %d |
| `+4EB` | byte | RemoveCallToArmsEnemyCreatures %d |
| `+4EC` | byte | RemoveCallToArmsWithinRange %d |
| `+4ED` | byte | RemoveCallToArmsOwnCreatures %d |
| `+4EE` | byte | WaitBeforeAttackingAgain %d |
| `+4EF` | dword | CallToArmsCounter %d |
| `+4F3` | dword | CannotAttackCounter %d |
| `+4F7` | word | UntagGoldCounter %d |
| *(same push)* | | TrapsStartFights %d |
| *(same push)* | | TimesOut %d |
| `+4` | dword | TaskTimeCounter %d |
| `+22` | dword | EventTask[%2d] = %d |
| `+2` | word | (RespondToAttack) %d, %d (%d, %d) |
| `+2` | dword | GoldDeposits[%2d] (%d, %d) %d |
| *(same push)* | | JobRooms[%2d] %d |
| `+3E` | byte | EnemyBreaches[ %2d].TunnelsLeft = %d |
| *(same push)* | | EnemyBreaches[ %2d].Tunnels[ %2d].Start.X = %d |
| *(same push)* | | EnemyBreaches[ %2d].Tunnels[ %2d].Start.Y = %d |
| `+2` | word | EnemyBreaches[ %2d].Tunnels[ %2d].End.X = %d |
| `+4` | word | EnemyBreaches[ %2d].Tunnels[ %2d].End.Y = %d |
| *(same push)* | | RoomList[%2d] = %d |

## Bitfields

Each group below is one dword, shifted and masked before the push.

**`+2`** — `Opened` bit 0

**`+4B2`** — `CreateEmptyRoomShapeAreasWhenIdle` bit 0, `AutomaticallyBuildBiggerLairWhenPortalTakenOver` bit 1, `PlaceRoomsNextToIdeal` bit 6, `IAAllRoomsPlaced` bit 23, `IAAllSpellsResearched` bit 24, `IAOnlyAttackAttackers` bit 25, `BoulderTrapsOnLongCorridors` bit 29, `BARemoveIfCantReinforce` bit 30

**`+4B6`** — `SellRecentlyTakenOverRoom` bit 7, `BoulderTrapOnBreachRoute` bit 10, `UnhappyCreaturesHappy` bit 15, `AngryCreaturesHappy` bit 16, `AngryCreaturesDisposeOf` bit 17, `AngryDisposalMethod` bit 18, `UnwantedCreatureDisposeOf` bit 19, `LightningInWater` bit 22, `NeverAttack` bit 30
