# The player object's fields, named by DK2 (Q264)

**Scope:** runtime offsets on DK2's player object. What a *trigger* does with
them is `trigger-semantics.md`; this is the map those readings point into.

`FUN_004BCF10` is a debug dumper that writes the whole player object to a log,
one `printf` per field with the field's name in the format string. It is the
strongest kind of evidence this project gets short of running the game: **the
names are the original's own**, not an inference from behaviour, an editor label
or a community document.

## Confidence

| Claim | Confidence | Basis |
|---|---|---|
| Each offset below carries the name beside it | **Known** | Direct instruction read: the value is loaded from that offset and pushed as the argument to the format string quoted |
| The bit-named entries are bits of the dword at `+0x76` | **Known** | Each is `SHR`/`AND 1` on `+0x76` before the push |
| The dump is complete for the fields it prints | **Strongly inferred** | Straight-line code, no branches skipped; but it prints no member of the score block at `+0x142`–`+0x16E` |

## The fields

| offset | name | note |
|---|---|---|
| `+0x04` | `TagId` | the player id the trigger walker pairs with a root |
| `+0x36` | `RoomList` | head of the owned-room list, walked through the object table at `0x006EE540` |
| `+0x3C` | `OwnedArea` | four values |
| `+0x76` | *(bitfield)* | `CanPopImps`, `BookHeldInPossession`, `CallToArmsOn`, `DungeonHeartIsInvulnerable`, `HeartPlugRemoved`, `GameWonSequenceActive`, `ReaperCreatedForEndSequence`, `ThirdPersonPossess`, `WarnAboutLowMana`, `FirstPayDaySet`, `HasPlayedHornyAvailable`, `HasPlayedToDismissReaper`, `PortalsOpen`, `Active`, `NotUsed` |
| `+0x7A` | `ImpProcessedCounter` | zeroed at the top of every player frame |
| `+0x7E` | **`Money`** | condition type 30 |
| `+0x82` | **`CurrentMana`** | condition type 32 |
| `+0x86` | `ManaIncreaseRate` | |
| `+0x8A`, `+0x8E` | `NumberOfManufacturingCreatures`, `NumberOfResearchingCreatures` | |
| `+0x92`–`+0xAE` | `LastScanTick`, `PlayerScanTick`, `ManaGainTick`, `LastLowManaWarningTick`, `ImpGenerateTick`, `ImpPopScanTick`, `LastTrapManaWarningTick`, `LastNoManaWarningTick` | |
| `+0x132` | `KilledByPlayerId` | read by condition type 47 |
| `+0x134`, `+0x136` | `CreaturePossessed`, `CreatureHeldInPossession` | `+0x134` gates condition type 73 |
| `+0x138`–`+0x13E` | `CallToArmsTag`, `CallToArmsPosition`, `CallToArmsManaTick` | |
| `+0x19A`, `+0x19E` | `Status`, `ObjectiveTextId` | |
| `+0x1BE`–`+0x1C1` | `ObjectiveActionPointId`, `NumberOfThingsInHand`, `MaxNumberOfThingsInHand`, `FreeRangeChickenCount` | |
| `+0x20D` | `CasinoBigWinnerGameTickRemaining` | |
| `+0x20F`, `+0x213` | `ResearchCompleted`, `ResearchPoint` | **contradicts a comment in `WorldState.PlayerState`** — Q265 |
| `+0x217` | `ManufactureCompleted` | |
| `+0xB35`–`+0xB3F` | `GemX`, `GemY`, `GemId`, `DungeonArea` | |
| `+0xB43`, `+0xB47` | `DungeonHeartPosition`, `EntranceLastSackedCreatureTypeId` | |
| `+0xB4F`, `+0xB53` | `LastPayDayTime`, `WorldPayDayTime` | |
| `+0xE93` | `PossessedCreatureInitialGold` | |
| `+0xEE2`, `+0xEE6` | `GameTickDied`, **`NumberOfSlaps`** | `+0xEE6` is condition type 71 |
| `+0xF4A` | `DiscoInfernoCount` | |
| `+0xFCE` | `CreatureUsed[]` | an array |
| `+0x10D2`–`+0x10F2` | `MaintainImpManaPerSecond`, `MaintainTrapManaPerSecond`, `MaxManaGainPerSecond`, `ManaGainPerSecond`, `BasicManaPerSecond`, `MaxMana`, `OneShotManaGain`, `OneShotManaLoss` | |
| `+0x10DA`, `+0x10DC` | **`TriggerRoot`, `TriggerRootRepeatCount`** | confirms `trigger-walkers.md` by name |
| `+0x10F6`–`+0x10FB` | `MaxCreatures`, `NumberOfCreatureTypes`, `NumberOfRooms`, `ComputerPlayerOn`, `ImpsWantARescan` | |
| `+0x111D` | `FireworksTime` | |

Keeper spells are a four-entry array at `+0x5A`, `+0x62`, `+0x6A`, `+0x72`,
stride 8, each printing `TypeId`, `Availability` and `Upgraded`; the last also
prints `TimeLastCast` and `TimeLastChecked`.

## The score block, and what it is made of

Twelve dwords at `+0x142`, `+0x146` … `+0x16E` are summed into a weighted total
in the function containing `0x004BF211`, each multiplied by one of twelve
hardcoded dwords at `0x0069F300`: **1000, 40, 50, 5, 500, 5, 1, 2, 5, 20, 40,
50** — `+0x152` takes the 500, out of order in the emitted code.

The player dumper prints none of them, but the **level-statistics** dumper
(`debug-dumpers.md`) prints a struct whose first twelve fields land on those
offsets exactly. `+0x142` is the struct's `+0x00`:

| offset | field | weight |
|---|---|---:|
| `+0x142` | `DungeonHeartsDestroyed` | 1000 |
| `+0x146` | `EnemyCreaturesKilled` | 40 |
| `+0x14A` | `HeroesDestroyed` | 50 |
| `+0x14E` | `RoomsTakenOver` | 5 |
| `+0x152` | `RoomExtraPoints` | 500 |
| `+0x156` | `LandOwned` | 5 |
| `+0x15A` | **`GoldMined`** | 1 |
| `+0x15E` | `ManaStored` | 2 |
| `+0x162` | `ItemsManufactured` | 5 |
| `+0x166` | `CreaturesCommanded` | 20 |
| `+0x16A` | `CreaturesConverted` | 40 |
| `+0x16E` | `CreatureTraining` | 50 |

So **condition type 31 tests how much gold this keeper has mined** (Q260). The
credit at `0x004973CF` adds to the owning player from a creature, which is what
mining is.

**There are two scoring systems and they are not the same one.** `Variables.kwd`
holds `MpdScore*` at ids 228–233 — 50, 5, 1, 10, 20, 40 — naming five of the
same concepts. Four agree with the table above (hero 50, land 5, gold 1,
creature 20) and **`MpdScoreItemManufactured` is 10 where the table has 5**, so
the hardcoded weights are not those variables read at load. Which system a
shipped level scores by is unknown (Q267).
