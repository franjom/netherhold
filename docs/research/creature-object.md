# The creature object, named by DK2 (Q264)

**Scope:** the field names DK2 prints for a creature, transcribed from its debug
dumper at `0x00490272`. Method, caveats and the other nine dumpers:
`debug-dumpers.md`. **A name is not a mechanism** — it says a field exists and
roughly what it holds, never its unit, writer or rule.

| offset | size | name |
|---|---|---|
| `+236` | word | RoomNode (%d, %d) |
| `+23A` | word | PlayerTorturedCreatureTypeListNode (%d, %d) |
| `+23E` | word | BattleNode (%d, %d) |
| `+24A` | word | FreeNode (%d,%d) |
| `+36F` | byte | TypeId %d |
| `+37A` | dword | IsInBattleFlag %d |
| `+37E` | dword | CanLeaveBattleAt %d |
| `+3AF` | dword | WorkModifier %d |
| `+3B3` | dword | WorkEnhancement %d |
| `+3B7` | word | LairId %d |
| `+3B9` | word | RoomId %d |
| `+3BB` | dword | MovementMode %d |
| `+3BF` | dword | ReloadSpeed %d |
| `+3C3` | word | CarryingId %d |
| `+3C5` | word | DraggingId %d |
| `+3C7` | dword | BeingDragged %d |
| `+3CB` | word | PrisonOwner %d |
| `+3CD` | dword | SkipScaleSetting %d |
| `+3D1` | dword | Idle %d |
| `+3D5` | word | ReportingTo %d |
| `+3D7` | dword | FearlessUntil %d |
| `+3DB` | dword | MovementEnd %d |
| `+3DF` | word | MyLeadersId %d |
| `+3E1` | byte | PositionInParty %d |
| `+3E2` | dword | GetPaid %d |
| `+3E6` | dword | FightProcessCounter %d |
| `+3E6` | dword | SniperMode %d |
| `+3EE` | dword | NextPossessionInfluenceTick %d |
| `+3F2` | word | CasinoBigWinnerGameTickRemaining %d |
| `+3F4` | word | CasinoBigWinnerWinnings %d |
| `+3F6` | byte | CasinoBigWinnerSlappedCount %d |
| `+3F7` | word | HighPriorityBody %d |
| `+3F9` | word | AttackingId %d |
| `+3FB` | dword | GoldHeld %d |
| `+3FF` | dword | ExperiencePoints %d |
| `+403` | byte | Level %d |
| `+404` | word | CreatedAtMapX %d |
| `+406` | word | CreatedAtMapY %d |
| `+408` | byte | KillsInCombatPit %d |
| `+409` | dword | GoodJob %d |
| `+40D` | word | GoodAttackPlayerId %d |
| `+40F` | dword | GoodJobData %d |
| `+413` | dword | GoodTimeDelay %d |
| `+417` | byte | PartyId %d |
| `+418` | byte | WanderRadius %d |
| `+419` | word | TriggerRoot %d |
| `+427` | dword | TimeLastUsedMelee %d |
| `+42B` | dword | RebelDelay %d |
| `+42F` | word | RebelTag %d |
| `+431` | dword | RebelPos %d |
| `+435` | dword | NumberOfFollowers %d |
| `+439` | word | AlarmPosition (%d, %d) |
| `+43D` | word | TrapTagId %d |
| `+43F` | dword | DurationUntilAttemptingAnotherTrapDisarm %d |
| `+443` | word | ArmourTimer %d |
| `+445` | word | InvulnerableTimer %d |
| `+447` | word | FreezeTimer %d |
| `+449` | word | ConcealedTimer %d |
| `+44B` | word | ChickenTimer %d |
| `+44D` | word | ScapegoatTimer %d |
| `+44F` | word | InvisibleTimer %d |
| `+451` | word | ObedientTimer %d |
| `+453` | word | TurncoatTimer %d |
| `+455` | word | TurncoatPlayer %d |
| `+457` | word | AngrySpecialTimer %d |
| `+459` | word | SpeedChangeTimer %d |
| `+45B` | word | CreatureAlertTimer %d |
| `+45D` | word | PoisonedTimer %d |
| `+45F` | word | PoisonedSeverity %d |
| `+461` | word | PoisonedByPlayerId %d |
| `+463` | word | FearedTimer %d |
| `+465` | word | FearedSeverity %d |
| `+467` | word | FearedByCreatureId %d |
| `+469` | word | OnFireCounter %d |
| `+46B` | word | OnFirePlayerId %d |
| `+46D` | word | LastDroppedCounter %d |
| `+46F` | byte | Flags %d |
| `+470` | dword | PaymentNeeded %d |
| `+474` | byte | PaymentAttempts %d |
| `+478` | byte | NBloodFootprints %d |
| `+479` | byte | NWaterFootprints %d |
| `+47A` | word | FlyingHeightModifier %d |
| `+47C` | word | MoveFromWorkCounter %d |
| `+47E` | dword | FollowingPossessedDelay %d |
| `+482` | word | PossessedCreatureFollowing %d |
| `+484` | dword | FollowingPossessedPos %d |
| `+488` | dword | PossessedNumberOfFollowers %d |
| `+48C` | byte | PossessedMovementFlags %d |
| `+48D` | word | ThrownDirection %d |
| `+48F` | dword | DamageFromWallHit %d |
| `+493` | dword | WallHitFlipIt %d |
| `+497` | dword | OldForceVelocity (%d, %d, %d) |
| `+4A3` | word | SneakingAs %d |
| `+4A5` | word | SkeletonCreationTime %d |
| `+4A7` | word | HypnotisedCreature %d |
| `+4A9` | word | HypnotisedPrevOwner %d |
| `+4B3` | dword | CountDownStartTick %d |
| `+4BB` | dword | LastSlapTick %d |
| `+4BF` | dword | PickupTick %d |
| `+4C3` | dword | LastReactionTime %d |
| `+4C7` | word | LastReactionTarget %d |
| `+4C9` | dword | HornyTick %d |
| `+4CD` | byte | CurrentElectrocuteArtDescriptorIndex %d |
| `+4CE` | word | DrunkenTimer %d |
| `+4D0` | word | PossessedTargetTag %d |
| `+4D2` | dword | PossessedTargetAttackTime %d |
| `+4D6` | byte | SuckedByInferno %d |
| `+4D7` | word | WorkFasterTimer %d |
| `+4D9` | word | AttackCreatureHits %d |
| `+4DB` | word | NHypnotisedAttacks %d |
| `+4DD` | word | SpeedBlurTimer %d |
| `+4DF` | dword | Scale %d |
| `+4E3` | word | LightningTimer %d |
| `+4E5` | dword | LastHelloTime %d |
| `+4E9` | word | ReaperTimer %d |
| `+4EB` | word | PortalGeneratedFromId %d |
| `+4ED` | word | NameIndex %d |
| `+4EF` | byte | BloodGroupIndex %d |
| `+4F0` | dword | CasinoBigWinGameTick %d |
| `+4F4` | word | CasinoGamblingDuration %d |
| `+4F6` | word | TorturedDuration %d |
| `+4F8` | byte | NotUsed %d |
| `+4F9` | byte | TriggerRootRepeatCount %d |
| `+4FA` | dword | GameTickInitialised %d |
| `+4FE` | dword | AttackRangeSquared %d |
| `+4` | word | Attacking %d |
| `+26` | word | TempFear %d |
| `+28` | word | CurrentFear %d |
| `+2A` | byte | OverThreatCounter %d |
| `+2B` | byte | AttackType %d |
| `+2B` | byte | SpellNo %d |
| `+2C` | word | OldFightPos (%d, %d) |
| `+30` | dword | OldFightTick %d |
| `+34` | byte | BackOffTimer %d |
| `+35` | word | ThreatThing %d |
| `+37` | word | ThreatThingThreshold %d |
| `+39` | word | AvoidCreature %d |
| `+3B` | word | AvoidCounter %d |
| `+3D` | word | RetreatingCreature %d |
| `+3F` | byte | CreatureRetreatingCounter %d |
| `+40` | byte | AvoidRetreatersCounter %d |

## Bitfields

Each group below is one dword, shifted and masked before the push.

**`+3E6`** — `InHand` bit 0, `ContributesToPortalLimit` bit 1, `AttackAbnormalTargets` bit 2, `RebelActive` bit 3, `RebelLeader` bit 4, `DeadBodyOnDestroy` bit 5, `LeaveNoTrace` bit 6, `Possessed` bit 7, `HeldByPossessed` bit 8, `ThrownByPossessed` bit 9, `BeingElectrocuted` bit 10, `VampireIsBat` bit 11, `Creeping` bit 12, `Praying` bit 13, `Leaving` bit 14, `CombatPitFighter` bit 15, `CannotLeaveHealthLoss` bit 16, `IgnoringDoors` bit 17, `IgnoringLockedRegion` bit 18, `CheckingAllDoors` bit 19, `CallToArmsOn` bit 20, `CreatureDying` bit 21, `WasLastDroppedOnARoom` bit 26, `ShouldDelayGetUp` bit 27, `FlyingCreatureMovingUp` bit 28, `LeavingLava` bit 29, `SkeletonFromSkeletonArmy` bit 30

**`+3EA`** — `IsHypnotised` bit 0, `AnimBackwards` bit 1, `CantBeResurrected` bit 2, `InfluencedByPossessed` bit 3, `HasBeenConverted` bit 4, `SkipNextPayment` bit 5, `IsAttackable` bit 6, `DestroysRooms` bit 7, `IsTool` bit 8, `IsMercenary` bit 9, `HasBeenKnockedOut` bit 10, `DieInstantly` bit 11, `PlayInstanceAnimation` bit 12, `CasinoGamblingFearless` bit 13, `TortureVoluntary` bit 14, `IsHasted` bit 15, `IsSlowed` bit 16, `IsOneShotReaper` bit 18, `ReaperComplete` bit 19, `Fleeing` bit 20, `InWater` bit 21
