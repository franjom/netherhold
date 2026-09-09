# DK2 v1.70 global balance table (GlobalVariables.kwd)

DK2's own data — CONFIRMED by definition. Reproduce with `dk2tools kwd vars`,
which reads `Data/editor/GlobalVariables.kwd` from the reference install
directly; a disagreement between this page and the tool means the page is
stale. Layout: `kwd-variables.md`. Names from ufdada/dk2-tools
`variables_struct.bt`; ids with no published name print as `Unknown_N` and
carry their `unk1`/`unk2` payload slots (Q85).

**This is the global table only.** Every level ships a `<name>Variables.kld`
that overrides it, 26 ids are overridden by at least one level, and the merge
rules are ADR-0040 — see `kwd-variables.md` before quoting a number as *the*
value. Ids 66, 74 (`GameTicks`), 77, 241 (`LevelRating`) and 242 (`AverageTime`)
appear only in per-level files and so are absent below.

```
# Misc variables (197)
    3  EntranceGenerationSpeedSeconds                                           25
    4  ClaimTileHealth                                                         825
    5  AttackTileHealth                                                       -140
    6  RepairTileHealth                                                        125
    7  MineGoldHealth                                                         -300
    8  DigRockHealth                                                         -5000
    9  DigOwnWallHealth                                                       -130
   10  DigEnemyWallHealth                                                       -8
   11  FillInHealth                                                              5
   12  ReinforceWallHealth                                                      50
   13  RepairWallHealth                                                         22
   14  ConvertRoomHealth                                                     -4000
   15  AttackRoomHealth                                                      -1500
   16  RepairRoomHealth                                                      20000
   17  Unknown_17                                                                3   unk1=5 unk2=1000
   18  ChickenGenerationTimePerHatchery                                         16
   20  GoldMinedFromGems                                                        25
   21  TimeBetweenReattemptingHungerSeconds                                      4
   22  TimeBetweenReattemptingSleepSeconds                                       4
   23  DeadBodyDiesAfterSeconds                                                 60
   24  MaxGoldPerTreasuryTile                                                 3000
   25  DecomposeValueNeededForVampire                                         1000
   26  CreatureDyingStateDurationSeconds                                        60
   27  SpecialIncreaseGoldAmount                                             10000
   28  SpecialIncreaseManaAmount                                             50000
   30  ImpIdleDelayBeforeReevaluationSeconds                                     5
   31  DelayBeforePayReevaluationSeconds                                         4
   32  MaxGoldPileOutsideTreasury                                             2000
   33  PayDayFrequencySeconds                                                  600
   34  MotionlessWhileResearchingSeconds                                        70
   35  MotionlessWhileManufacturingSeconds                                       5
   36  StateCounterDelayBeforeReattemptEating                                    5
   37  StateCounterWhileGuarding                                                10
   38  MotionlessWhilePrayingSeconds                                            30
   39  MotionlessInPrisonSeconds                                                 8
   40  MotionlessWhileTrainingSeconds                                            5
   41  MotionlessInCasinoSeconds                                                13
   42  ExcessManaDecreaseRatePerSecond                                           5
   45  DefaultHorizontalTerrainWibble                                        24576
   46  DefaultVerticalTerrainWibble                                          12288
   47  RebelMaxFollowers                                                         2
   48  RebelLeavingPercentage                                                   70
   49  RebelBecomeGoodPercentage                                                15
   50  RebelDefectPercentage                                                    15
   51  TimeBeforeFirstPayDaySeconds                                            600
   52  DefaultTorchLightRed                                                    300
   53  DefaultTorchLightGreen                                                  200
   54  DefaultTorchLightBlue                                                   100
   55  PrisonModifyCreatureHealthPerSecond                                      -3
   61  Unknown_61                                                        305493641   unk1=-1413218304 unk2=0
   62  Unknown_62                                                       -377003726   unk1=1420541952 unk2=0
   63  Unknown_63                                                        305498471   unk1=-878624768 unk2=0
   67  DefaultCellingHeightTiles                                                 0
   68  PayDayCutOffTimeSeconds                                                 120
   70  CasinoModifyCreatureGoldPerSecond                                       -50
   71  CasinoBigWinKeeperLossPercentage                                          0
   73  CreaturesSupportedByFirstPortal                                          15
   75  ModifyHealthOfCreatureInLairPerSecond                                   100
   76  ModifyAngerOfCreatureInLairPerSecond                                   -550
   78  ModifyAngerInCompanyOfHatedCreaturesPerSecond                           150
   79  ForceAppliedToSlappedCreature                                          1024
   82  CannotSleepModifyCreatureHealthPerSecond                                  0
   83  CannotLeaveModifyCreatureHealthPerSecond                                  0
   84  PlayerRescanIntervalSeconds                                              -1
   85  PlayerRescanRange                                                        -1
   86  MaxFreeRangeChickensPerPlayer                                            12
   87  WoodBridgeLifeOnLavaSeconds                                              30
   88  CreatureSleepsWhenBelowPercentHealth                                     50
   93  DoorPickedTimeSeconds                                                     5
   94  MinimumImpThreshold                                                       4
   95  TimeBeforeFreeImpGeneratedSeconds                                         5
   96  CreatureStunnedTimeSeconds                                                2
   97  CreatureStunnedEffectDelaySeconds                                         1
   98  FirstPersonStatIncreasePercentage                                        12
  100  ManaCostInFirstPersonPerSecond                                            5
  101  HealthDecreaseWhenOnFirePerSecond                                      -150
  102  ModifyCreatureAngerWhilePrayingPerSecond                               -275
  104  RoomSellValuePercentageOfCost                                            50
  105  DoorSellValuePercentageOfCost                                            50
  106  TrapSellValuePercentageOfCost                                            50
  110  InsufficientManaImpEvaluationPeriodSeconds                               10
  111  DeathWhenPossessingManaReduction                                        100
  112  PercentageOfManaGainedBySacrificing                                       0
  113  PossessionFriendlyStatIncreasePercentage                                  6
  114  PossessionFriendlyHealthIncreasePercentage                                6
  115  ModifyCreatureHealthWhilePrayingPerSecond                                33
  116  HypnotismManaDrainPerSecond                                               0
  125  LowManaWarningThreshold                                                 100
  126  TrainingRoomMaxExperienceLevel                                            4
  127  StunnedDamageIncreasePercentage                                         100
  128  MaxNumberOfThingsInHand                                                  64
  129  RubberBandAreaLimit                                                       8
  130  GravityConstant                                                         240
  131  DungeonHeartHealthRegenerationPerSecond                                  25
  132  EPLossSecondWhileNotTraining                                              0
  133  ModifyAngerWhileSolitaryInPitPerSecond                                  275
  134  ModifyAngerOfCombatPitVictor                                         -16400
  135  ModifyAngerOfPitSpectatorPerSecond                                     -275
  136  ConvertNeutralRoomHealth                                             -20000
  138  ManufacturePointLossWhileNoWorkersPerSecond                               1
  139  CasinoBigWinnerIncreasedHappinessPersentage                             100
  140  DecreaseEntrancePersentageOfLastSackedCreatureType                       33
  141  IncreaseEntrancePersentageOfNewCreatureTypes                             33
  142  IncreasedWorkRatePersentageOfPlayersTorturedCreatureTypes                25
  143  IncreasedWorkRatePersentageWhenCasinoBigWinOccurs                        35
  144  ImpPopCountdownTimeSeconds                                               10
  145  NumberOfRandomWallDeteriorationsPerSecond                                 0
  146  WallDeteriorationDamage                                                   0
  147  WallDamageFromShot                                                      -10
  148  PercentageChanceOfKillingCreatureInPit                                   10
  149  ModifyPlayerColdWhileTrainingPerSecond                                   -5
  150  SkeletonArmyDurationSeconds                                              30
  151  OvercrowdingJailBreakPercentagePerExtraCreature                          50
  152  MaxTimeInGuardRoomBeforeWanderingToGuardPostSeconds                      60
  154  AmountOfGoldStolenInFirstPerson                                         100
  155  BoulderDeteriorationDamagePercentagePerSecond                         10240
  156  DrunkenDurationSeconds                                                   15
  157  BoulderInitialHealth                                                   4000
  158  BoulderSpeedTilesPerSecond                                             4915
  159  BoulderToCreatureDamagePercentage                                        50
  160  BoulderSpeedToHealthRatio                                                50
  161  BoulderFearAmount                                                       701
  162  BoulderFearRange                                                          4
  163  DrunkChancePercentagePerDrink                                             5
  164  IncreasedWorkRatePecentageFromSlapping                                   50
  165  IncreasedWorkRateDurationFromSlappingSeconds                             60
  166  DungeonHeartMaxManaIncrease                                           32000
  167  DungeonHeartManaGenerationIncreasePerSecond                              30
  168  TremorDamagePerTerrainTilePerSecond                                     436
  169  ExternalGuardingDurationSeconds                                          15
  170  MinimumJobDurationSeconds                                                15
  173  CreatureCriticalHealthPercentageOfMax                                    20
  174  DungeonHeartObjectHealth                                             100000
  175  ClaimNeutralManaVaultHealth                                               4
  176  ClaimEnemyManaVaultHealth                                                20
  177  CreaturesSupportedPerAdditionalPortal                                     5
  178  MaxGoldPerDungeonHeartTile                                             1000
  179  ImpExperienceGainPerSecond                                               20
  180  DungeonHeartClaimScanRadiusTiles                                         15
  181  StunTimePercentageIncreaseWhenHit                                        25
  182  CallToArmsFightDistanceTiles                                              5
  183  DungeonHeartReportingDistanceTiles                                        4
  184  DwarfDiggingMultiplier                                                    4
  185  TriggerTrapTriggerSpeedTilesPerSecond                                  4096
  187  FirstPersonAccuracyAngleDegrees                                          45
  188  SpecialReceiveImpsAmount                                                 10
  189  DungeonHeartCreatureGatheringDistanceTiles                                7
  190  GuardRoomReportingDistanceTiles                                           7
  191  GuardPostReportingDistanceTiles                                           7
  192  CombatPitMaxExperienceLevel                                               8
  193  TimeInHandBeforeCreaturesBecomeAngrySeconds                            1000
  194  SlapWorkFasterDurationSeconds                                            25
  195  SlapSpeedUpDurationSeconds                                               25
  196  TortureSpeedUpDurationSeconds                                            40
  197  CasinoWinSpeedUpDurationSeconds                                          60
  198  CriticalHealthPercentageForHeroLair                                      20
  199  GamblingModifyCreatureMoneySmilesPerSecond                                1
  200  GamblingModifyCreatureMoneyMoneyPerSecond                                -8
  201  GamblingModifyCreatureAngerSmilesPerSecond                            -2000
  202  GamblingModifyCreatureAngerMoneyPerSecond                               100
  203  GamblingDelayBeforeBecomingFearlessSeconds                              120
  204  GamblingFearlessDurationSeconds                                         120
  205  GamblingCreatureJackpotChanceSmilesPerSecond                           5000
  206  GamblingCreatureJackpotChanceMoneyPerSecond                           20000
  207  GamblingJackpotPayTimeLimit                                             240
  208  GamblingJackpotPayTimeLimitMissedAngerPercentageModifier                100
  209  GamblingJackpotBasicAmount                                             1000
  210  FirstPersonRechargeModifyPercentage                                     100
  211  BackOffMinimumDurationSeconds                                             0
  212  BackOffMaximumDurationSeconds                                            30
  213  GoodCreaturesChaseEnemyDurationSeconds                                    3
  214  EvilCreaturesChaseEnemyDurationSeconds                                    7
  215  MusicLevelTwoThreatThreshold                                            100
  216  MusicLevelThreeThreatThreshold                                          500
  217  MusicLevelFourThreatThreshold                                          1500
  218  TimeBeforeDungeonHeartConstructionBegins                                  0
  219  DefaultTorchLightIntensity                                              180
  220  DefaultTorchLightRadiusTiles                                           6144
  221  DefaultTorchLightHeightTiles                                           6144
  222  MaximumManaThreshold                                                 200000
  223  MaximumGroupNumberOfFollowers                                             8
  225  CombatPitMeleeDamageModifierPercentage                                  100
  226  UpgradedPossessionCostPercentage                                         70
  227  UpgradedCallToArmsCostPercentage                                         70
  228  MpdScoreHeroKilled                                                       50
  229  MpdScoreLandOwned                                                         5
  230  MpdScoreGoldSlabsMined                                                    1
  231  MpdScoreItemManufactured                                                 10
  232  MpdScoreCreatureEntered                                                  20
  233  Unknown_233                                                              40
  234  TortureChanceOfDyingWhenConverted                                         0
  235  MaximumManaGainPerSecond                                                500
  236  ClaimScanLightRed                                                       200
  237  ClaimScanLightGreen                                                      80
  238  ClaimScanLightBlue                                                       35
  239  PitPercentageDamageTakenOfNormalCombat                                   20
  240  BoulderSlapDamage                                                       400

# CreaturePool entries: 0
# Availability entries: 32
# Sacrifice recipes:    44
# Creature stat rows:   320
```

## The records the printed lines do not account for

The chunk holds **621 records**; the counts above sum to 593. The gap is not
missing data, and the arithmetic is worth writing down because two of the four
lines are not what they look like:

| kind | records | printed as |
|---|---:|---|
| plain values | 225 | **197 lines** — see below |
| `Availability` (id 2) | 32 | 32 |
| `Sacrifice` (id 43) | 44 | 44 |
| `CreatureStats` (id 65) | 288 | pooled into "Creature stat rows: 320" |
| `CreatureFirstPersonStats` (id 224) | 32 | the other 32 of that 320 |
| `CreaturePool` (id 1) | 0 | 0 — pools live only in per-level files |

**Id 17 is not a scalar.** It carries **29 records** in this one chunk — value 4
twelve times, 2 six times, 3 eleven times — and `KwdVariables` keeps plain
values in a by-id dictionary where the last occurrence wins, so the line above
shows one of the 29 and the other 28 are the whole of the 225 − 197 gap. Nothing
else in the global table repeats an id. Whatever id 17 selects, it is a **list
keyed by something the payload does not name**; the `unk1=5 unk2=1000` on the
surviving record is the last row's slots 2 and 3, not a global.

`CreatureStats` and `CreatureFirstPersonStats` are pooled into one
`GlobalVariables.CreatureStats` list distinguished by a `FirstPerson` flag,
which is why one "320" covers two ids. 288 = the level-scaled stat rows;
`creature-levels.md` reads them.

## Provenance

Implementation: `DK2.Data.Kwd.KwdVariables`, `DK2.Data.Model.GlobalVariables`,
`DK2.Data.Model.VariableTypeId`. Tests: `DK2.Data.Tests.GameDatabaseTests`,
`GlobalVariablesMergeTests`; on real data,
`DK2.Behavior.Tests.LevelVariablesOnRealDataTests`.

Which of these the executable actually reads is a separate question:
`balance-variables-in-the-binary.md` and `variables-corpus-sweep.md`. A zero
here does not mean a dead mechanic (ADR-0085).
