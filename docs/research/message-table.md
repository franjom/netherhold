# The notification type table at 0x006A0B00 (all 100 rows)

- `DKII.EXE` v1.70 (GOG). Read 2026-10-26 from the file's own bytes (PE section map, every row and every variant list), and the first 1000 bytes were cross-read through Ghidra at the same address.
- Resolver: [Message_Post](functions/004C4600.md). Mechanism, encoding and the taunt pool: [mentor-speech-and-notifications.md](mentor-speech-and-notifications.md) §3-4. Tab art: [message-tabs.md](message-tabs.md) §3.
- Raw dump and scripts: `D:\DK2Evidence\message-table\` (`msgtable.json`, `type_sites.json`).

## Result in one sentence

All 100 rows are read; the table matches OpenKeeper's issue #566 on every field we can compare (all 100 rows), and the **raiser census finds 25 types that no `call` to the poster pushes, exactly OpenKeeper's 25 "none found" rows**. Every other type has a poster. Three things are new here: the full per-type raiser list (section 3), the `+6` field resolved as the tab graphic, and a wrong line in our own docs found on the way (section 5).

## 1. Row layout - CONFIRMED (bytes), 10 bytes at `0x006A0B00 + 10 * type`

| Field | Width | Meaning |
|---|---|---|
| `+0` | u16 | flags. Bits 0-12: on-screen seconds. `0x2000` NeedsTab (set on **every** row, 0 and 6 included). `0x4000` duplicates allowed. `0x8000` field `+2` is a variant list |
| `+2` | i32 | speech reference, stored `-(10000 + zero-based Speech.str id)` (Message_Post itself subtracts 9999, one more); a 0 or 1 means the table holds no speech (types 14, 17, 18, 20 and 21 build theirs at run time in Message_Post's own switch); with `0x8000`, a pointer to the variant list |
| `+6` | u32 | **the tab graphic (0-6)**, read as in [message-tabs.md](message-tabs.md) §3 (0 is read as 1). Observed values: 0, 1, 2, 3, 5, 6 (4, mt-objective, is never in the table) |

Variant list: 8-byte records `{u16 key, u16 seconds, i32 speech}`, ended by a record with key 0 whose speech is the default (0 = none). A list's seconds are per key; the row's own low 13 bits are 0.

## 2. Diff against OpenKeeper's table (issue #566, "checked with flame")

Compared per row: tab graphic, seconds (a key range counted as matching when every key's seconds fall in it), dedupe flag, variant-list flag, speech id, key count and default-or-none. **100 of 100 rows agree.** A planted-error run (3 mutated rows) was flagged, so the comparison is not vacuous.

What OpenKeeper adds, and what this census says of it:

| Their claim | Here |
|---|---|
| Type `0x01` is raised by `CPlayer::initAsNoPlayer` | The only post of type 1 is `Player_ModifyHeartHealth` `0x004DA323` (the heart's health change; matches our reading in [win-and-defeat.md](win-and-defeat.md)). Their function name is another label or wrong. |
| Type `0x2D` is raised by `CPlayer::fun_4E2500` | Agrees: `Library_FindForCreature` pushes it at `0x004E27FD` and jumps to the shared ecx setup at `0x004E2867`, then the call at `0x004E286D`. |
| Rows 26 and 34 take "20/30" seconds | Every key of both lists is 30 s. The 20 is only the seconds field of the list's terminator record (used, as the default, on row 34 only). Their other slash and range columns match the keys exactly. |
| 25 types have "none found" | **Confirmed, and no further type lacks a poster** (section 4). |
| "go-to" kinds, text strings | Not in the table (code and `Speech.str`). Not checked here. |
| A named producer per type | For the 67 types where they name one by address, one of our posters lies within `0x1800` bytes after it in every case. That is a plausibility check, not an identity: function sizes vary and the rest of their names are labels only. |

## 3. The 100 rows

`†` marks a raiser reached through a `push imm; jmp` arm into a shared call (found by pattern, STRONGLY INFERRED); unmarked raisers push the type immediately before the call. "Built" is the `PlayerMessageKind` that posts it.

| type | hex | flags | s | tab | reference | raisers (function, call site) | built |
|---:|---|---|---:|---:|---|---|---|
| 0 | `0x00` | `0x2000` | 0 | 0 | speech none (ref 1) | **none** |  |
| 1 | `0x01` | `0x2078` | 120 | 3 | speech 345 | Player_ModifyHeartHealth`4DA323` | HeartAttacked |
| 2 | `0x02` | `0x2032` | 50 | 1 | speech 1290 | Heart_OnDestroyed`4DA5D0` |  |
| 3 | `0x03` | `0x2014` | 20 | 1 | speech 1161 | Heart_OnDestroyed`4DA776` |  |
| 4 | `0x04` | `0xA000` | 0 | 1 | list `0x0069ff60`: 4 keys, 20 s, no default | **none** |  |
| 5 | `0x05` | `0xA000` | 0 | 1 | list `0x0069ff88`: 4 keys, 20 s, no default | HeroParty_Spawn`51A2B0` |  |
| 6 | `0x06` | `0x2000` | 0 | 0 | speech none (ref 1) | Combat_EngagingState`45EBFA` | CombatStarted |
| 7 | `0x07` | `0x2032` | 50 | 1 | speech 31 | **none** |  |
| 8 | `0x08` | `0x2032` | 50 | 1 | speech 333 | **none** |  |
| 9 | `0x09` | `0x6032` | 50 | 1 | speech 353 | fn 0x004d7a60`4D7CCD` |  |
| 10 | `0x0A` | `0x2032` | 50 | 1 | speech 356 | **none** |  |
| 11 | `0x0B` | `0x2032` | 50 | 1 | speech 1148 | **none** |  |
| 12 | `0x0C` | `0x2032` | 50 | 1 | speech 1149 | Tile_ApplyWork`45313E`; CreatureAction_BreachCheck`498C3B` | WallBreached |
| 13 | `0x0D` | `0x6032` | 50 | 2 | speech 376 | Portal_CreateArrival`4DCB56`; fn 0x004dcf50`4DD052` | CreatureArrived |
| 14 | `0x0E` | `0x6032` | 50 | 1 | speech built at run time by Message_Post (ref 0) | Portal_CreateArrival`4DCB25` | CreatureIntroduction |
| 15 | `0x0F` | `0x21A4` | 420 | 1 | speech 374 | Portal_PickCreatureType`4DC46F` |  |
| 16 | `0x10` | `0x21A4` | 420 | 1 | speech 375 | Portal_PickCreatureType`4DC46F`† |  |
| 17 | `0x11` | `0x2032` | 50 | 2 | speech built at run time by Message_Post (ref 0) | Anger_AddToBucket`478BE1`; Anger_ScaleBucket`478C91` | CreatureUnhappy |
| 18 | `0x12` | `0x2032` | 50 | 2 | speech built at run time by Message_Post (ref 0) | Anger_AddToBucket`478C12`; Anger_ScaleBucket`478CC2` | CreatureAngry |
| 19 | `0x13` | `0x6032` | 50 | 2 | speech 506 | State271_DefectCountdown`475156` |  |
| 20 | `0x14` | `0x2032` | 50 | 2 | speech built at run time by Message_Post (ref 0) | Leaving_FinalArrival`45A177` |  |
| 21 | `0x15` | `0x6032` | 50 | 1 | speech built at run time by Message_Post (ref 0) | TriggerPlayer_DispatchAction`4C86A8` | ScriptedSpeech |
| 22 | `0x16` | `0x200A` | 10 | 1 | speech 391 | Library_GrantSpell`4E1032` |  |
| 23 | `0x17` | `0xA000` | 0 | 1 | list `0x0069ffb0`: 13 keys, 20 s, no default | Library_GrantSpell`4E0F60` |  |
| 24 | `0x18` | `0x601E` | 30 | 1 | speech 395 | Research_ChooseTarget`4C30CB` |  |
| 25 | `0x19` | `0x212C` | 300 | 1 | speech 394 | Research_ShelveItem`4E0A2E`; fn 0x004e11a0`4E128B` |  |
| 26 | `0x1A` | `0xA000` | 0 | 1 | list `0x006a0838`: 17 keys, 30 s, no default | fn 0x004f5240`4F540E` |  |
| 27 | `0x1B` | `0xA000` | 0 | 1 | list `0x006a0020`: 7 keys, 20 s, default 1304 | **none** |  |
| 28 | `0x1C` | `0x2032` | 50 | 1 | speech 1323 | Job_Manufacture`4700FC` |  |
| 29 | `0x1D` | `0x2032` | 50 | 1 | speech 1291 | **none** |  |
| 30 | `0x1E` | `0xA000` | 0 | 1 | list `0x006a0ab8`: 3 keys, 20 s, no default | **none** |  |
| 31 | `0x1F` | `0x2032` | 50 | 1 | speech 1150 | **none** |  |
| 32 | `0x20` | `0xA000` | 0 | 1 | list `0x006a0060`: 18 keys, 20 s, default 77 | Tile_SetTerrain`44FF49`; Tile_SetTerrain`44FFE6`; Room_TransferOwnership`4E5931` | RoomCaptured |
| 33 | `0x21` | `0xA000` | 0 | 1 | list `0x006a00f8`: 18 keys, 20 s, default 155 | Room_TransferOwnership`4E5931`† | NeutralRoomCaptured |
| 34 | `0x22` | `0xA000` | 0 | 1 | list `0x006a0190`: 17 keys, 30 s, default 171 | Worker_BeginRoomAttack`466134`; CreatureAction_AttackRoom`497F29` | RoomAttacked |
| 35 | `0x23` | `0xE000` | 0 | 1 | list `0x006a0220`: 18 keys, 20 s, default 139 | Tile_SetTerrain`450004`; Room_TransferOwnership`4E58F4` | RoomLost |
| 36 | `0x24` | `0xA000` | 0 | 1 | list `0x006a02b8`: 17 keys, 20 s, default 108 | CreatureAction_AttackRoom`497FA5`† |  |
| 37 | `0x25` | `0xA000` | 0 | 1 | list `0x006a0348`: 17 keys, 20 s, default 123 | CreatureAction_AttackRoom`497FA5` |  |
| 38 | `0x26` | `0x2032` | 50 | 1 | speech 1344 | **none** |  |
| 39 | `0x27` | `0x2032` | 50 | 1 | speech 346 | DungeonHeart_Update`4D9AFD`† |  |
| 40 | `0x28` | `0x2032` | 50 | 1 | speech 344 | DungeonHeart_Update`4D9AFD` |  |
| 41 | `0x29` | `0xA000` | 0 | 1 | list `0x006a03d8`: 13 keys, 180/300/420/600 s, no default | Imp_ChooseGoldStore`46461E`; Worker_SelectCorpseOnTile`46D311`; Worker_SelectCorpse`46D760`; Worker_SelectRescue`46DAFE`†; Worker_SelectRescue`46DD2D`†; Worker_SelectRescue`46E0BD`†; Player_ChangeGold`4BA4AC`; Player_ChangeGold`4BA5AF`; Casino_FindForGambler`4D7A07`†; CombatPit_FindForCreature`4D8BB5`†; Hatchery_FindForCreature`4DEF1C`†; Lair_FindForCreature`4E06ED`†; Library_FindForCreature`4E286D`†; Temple_FindForCreature`4F1CFF`†; Torture_FindForCreature`4F2E74`†; Training_FindForCreature`4F383F`†; Workshop_FindForCreature`4F650B`† | RoomNeeded |
| 42 | `0x2A` | `0xA000` | 0 | 1 | list `0x006a04b8`: 13 keys, 90/240/300 s, default 212 | Imp_ChooseGoldStore`4645B2`; Worker_SelectCorpseOnTile`46D33A`; Worker_SelectCorpse`46D789`; Worker_SelectRescue`46DAFE`; Worker_SelectRescue`46DD2D`; Worker_SelectRescue`46E0BD`; Lair_SeekBedSlot`46F670`; Player_ChangeGold`4BA5AF`†; Casino_FindForGambler`4D7A07`†; CombatPit_FindForSpectator`4D884D`†; CombatPit_FindForCreature`4D8BB5`†; Graveyard_HasRoom`4DDD5F`; Hatchery_FindForCreature`4DEF1C`; Lair_FindForCreature`4E06ED`; Library_FindForCreature`4E286D`†; Room_ClaimPlace`4E5C72`; Room_ClaimPlace`4E5D82`; Temple_FindForCreature`4F1CFF`†; Torture_FindForCreature`4F2E74`†; Training_FindForCreature`4F383F`†; Workshop_FindForCreature`4F650B`† | RoomFull |
| 43 | `0x2B` | `0xA000` | 0 | 1 | list `0x006a0528`: 14 keys, 300/360 s, no default | Imp_ChooseGoldStore`4645E4`; Worker_SelectRescue`46DAFE`†; Worker_SelectRescue`46DD2D`†; Worker_SelectRescue`46E0BD`†; Job_Sleep`46F4D9`; Payday_FindMoney`472673`; Job_StartSulk`482F69`; Casino_FindForGambler`4D7A07`†; CombatPit_FindForSpectator`4D884D`†; CombatPit_FindForCreature`4D8BB5`†; Hatchery_FindForCreature`4DEF1C`†; Lair_FindForCreature`4E06ED`†; Library_FindForCreature`4E286D`†; Temple_FindForCreature`4F1CFF`†; Torture_FindHoldingVictim`4F2B62`; Torture_FindForCreature`4F2E74`†; Training_FindForCreature`4F383F`†; Workshop_FindForCreature`4F650B`† | RoomUnreachable |
| 44 | `0x2C` | `0xA000` | 0 | 1 | list `0x006a08e0`: 13 keys, 120/240 s, default 196 | Casino_FindForGambler`4D7A07`; CombatPit_FindForSpectator`4D884D`; CombatPit_FindForCreature`4D8BB5`; Library_FindForCreature`4E286D`; Temple_FindForCreature`4F1CFF`; Torture_FindForCreature`4F2E74`; Training_FindForCreature`4F383F`; Workshop_FindForCreature`4F650B` | RoomUnequipped |
| 45 | `0x2D` | `0xA000` | 0 | 1 | list `0x006a08c8`: 2 keys, 120 s, no default | Library_FindForCreature`4E286D`† | RoomStorageFull |
| 46 | `0x2E` | `0x2032` | 50 | 1 | speech 1152 | Payday_Tick`4BE1E3` |  |
| 47 | `0x2F` | `0x2032` | 50 | 1 | speech 1345 | ShotProcess12_Alarm`4AE145` | AlarmRaised |
| 48 | `0x30` | `0x200A` | 10 | 1 | speech 486 | Tile_SetTerrain`450078` |  |
| 49 | `0x31` | `0xE000` | 0 | 1 | list `0x006a05a0`: 13 keys, 20 s, default 488 | Special_Activate`4A4083`; Special_Activate`4A414C`; Special_IncreaseLevel`4A43A6`; Special_MakeSafe`4A45AF`; Special_DestroyWalls`4A46D5`; Special_IncreaseGold`4A482A`; Special_StunImps`4A4942`; Special_ReceiveImps`4A4B8F`; Special_MakeHappy`4A4C4C`; Special_MakeUnhappy`4A4D43`; Special_KillCreature`4A4DF4`; Special_HealAll`4A4F34`; Special_LocateHiddenLand`4A5016` |  |
| 50 | `0x32` | `0x2032` | 50 | 1 | speech 1159 | Player_EndLevelStatistics`4BEDBB` |  |
| 51 | `0x33` | `0x2032` | 50 | 1 | speech 1160 | Player_EndLevelStatistics`4BEB85` |  |
| 52 | `0x34` | `0xA000` | 0 | 1 | list `0x006a0610`: 4 keys, 20 s, no default | Mana_UpdatePlayer`4C162F` | LowManaWarning |
| 53 | `0x35` | `0xA000` | 0 | 1 | list `0x006a0638`: 5 keys, 20 s, default 448 | Mana_UpdatePlayer`4C13DE`; Player_TrapManaWarning`4C1A5E`; fn 0x004c1bc0`4C1D9F`; fn 0x004c1bc0`4C1DE5` | ManaShortfall |
| 54 | `0x36` | `0xA000` | 0 | 1 | list `0x006a0668`: 4 keys, 20 s, no default | **none** |  |
| 55 | `0x37` | `0xA000` | 0 | 1 | list `0x006a0690`: 5 keys, 20/240 s, default 445 | Payday_FindMoney`472673`†; Training_EnterHook`47B25E`; TrainingCategory_UpdateHook`47B3AE`; Job_StartTrain`4852A5`; fn 0x004c33b0`4C365F`; fn 0x004c3860`4C3A01`; Player_BuildRoomRectangle`4C7083` | TrainingUnpaid |
| 56 | `0x38` | `0x20F0` | 240 | 1 | speech 381 | (no function, 0x49938C)`49938C` |  |
| 57 | `0x39` | `0x20F0` | 240 | 1 | speech 382 | fn 0x004f2800`4F2827` |  |
| 58 | `0x3A` | `0x601E` | 30 | 1 | speech 380 | fn 0x004f2800`4F2883` |  |
| 59 | `0x3B` | `0x201E` | 30 | 1 | speech 378 | Torture_RewardInformation`4F27C5` |  |
| 60 | `0x3C` | `0x20F0` | 240 | 1 | speech 383 | fn 0x00473130`4731D5` |  |
| 61 | `0x3D` | `0x201E` | 30 | 1 | speech 388 | **none** |  |
| 62 | `0x3E` | `0x603C` | 60 | 1 | speech 385 | Prison_ToggleDoor`4E3705` |  |
| 63 | `0x3F` | `0x603C` | 60 | 1 | speech 384 | Prison_ToggleDoor`4E375A` |  |
| 64 | `0x40` | `0x20F0` | 240 | 1 | speech 1346 | Prison_SeekFood`473711` |  |
| 65 | `0x41` | `0x203C` | 60 | 1 | speech 387 | Prison_MakeSkeleton`4E3205` |  |
| 66 | `0x42` | `0x203C` | 60 | 1 | speech 389 | Prison_ReleasePrisoners`4E2F93`; Prison_ReleasePrisoners`4E30CC` |  |
| 67 | `0x43` | `0x6032` | 50 | 1 | speech 58 | **none** |  |
| 68 | `0x44` | `0x6032` | 50 | 1 | speech 59 | **none** |  |
| 69 | `0x45` | `0x6032` | 50 | 1 | speech 56 | (no function, 0x50FCF7)`50FCF7` |  |
| 70 | `0x46` | `0x6032` | 50 | 1 | speech 57 | **none** |  |
| 71 | `0x47` | `0xE000` | 0 | 1 | list `0x006a06c0`: 46 keys, 5 s, no default | State101_TempleEnter`472A97`; Object_Process4RoomHandler`49F71F`; Temple_GiveReward`4F1918`; (no function, 0x51346C)`51346C` |  |
| 72 | `0x48` | `0x6032` | 50 | 1 | speech 373 | Graveyard_CreateVampire`4DD99E` |  |
| 73 | `0x49` | `0x2032` | 50 | 1 | speech 365 | Casino_Jackpot`4D6BEA` | CasinoJackpot |
| 74 | `0x4A` | `0x2032` | 50 | 1 | speech 372 | **none** |  |
| 75 | `0x4B` | `0x2032` | 50 | 1 | speech 368 | State146_CasinoAlcove`456CD4` | CasinoDrinking |
| 76 | `0x4C` | `0x2032` | 50 | 1 | speech 371 | fn 0x004d6cb0`4D6D4C` |  |
| 77 | `0x4D` | `0x2032` | 50 | 1 | speech 372 | Creature_CasinoWinner155`457465`; Creature_CasinoWinner157`4575A3` | JackpotUnpaid |
| 78 | `0x4E` | `0x2032` | 50 | 1 | speech 504 | **none** |  |
| 79 | `0x4F` | `0x2032` | 50 | 1 | speech 505 | **none** |  |
| 80 | `0x50` | `0x2032` | 50 | 1 | speech 503 | **none** |  |
| 81 | `0x51` | `0x2032` | 50 | 1 | speech 1348 | **none** |  |
| 82 | `0x52` | `0xE000` | 0 | 5 | list `0x006a0950`: 7 keys, 30 s, no default | (no function, 0x513F2F)`513F2F`; (no function, 0x513FC5)`513FC5`; (no function, 0x513FDD)`513FDD`; (no function, 0x514072)`514072`; (no function, 0x51408A)`51408A`; GameAction_GoldToHeart`51417A`; GameAction_GoldToHeart`514192`; fn 0x005141b0`51420A`; fn 0x005141b0`514222` |  |
| 83 | `0x53` | `0x6032` | 50 | 5 | speech 1515 | (no function, 0x5140DB)`5140DB` |  |
| 84 | `0x54` | `0x2032` | 50 | 1 | speech 1322 | fn 0x00455da0`455E3A`; Creature_IdleChooser89`477A10` | CallToArmsUnreachable |
| 85 | `0x55` | `0x6032` | 50 | 6 | speech 1513 | fn 0x00510e90`510F18` |  |
| 86 | `0x56` | `0x6032` | 50 | 6 | speech 1514 | **none** |  |
| 87 | `0x57` | `0xE000` | 0 | 3 | list `0x006a0ad8`: 4 keys, 20 s, no default | Heart_OnDestroyed`4DA64A` |  |
| 88 | `0x58` | `0xE000` | 0 | 1 | list `0x006a0990`: 8 keys, 20 s, no default | fn 0x004b9740`4B9E9D` |  |
| 89 | `0x59` | `0xE000` | 0 | 1 | list `0x006a09d8`: 27 keys, 20 s, no default | fn 0x00407380`4074EA`; fn 0x00407380`40750D`; State146_CasinoAlcove`456CD4`†; Shot_ProcessCreatureCreation`4AD9E9`; fn 0x004b9740`4B9D2F`; fn 0x004b9740`4B9D9E`; fn 0x004b9740`4B9DF7`; Portal_CreateArrival`4DCCC8`; World_HostClockEvents`50A92A`; World_HostClockEvents`50A957`; World_HostClockEvents`50A982`; World_HostClockEvents`50A9AE`; World_HostClockEvents`50A9DA`; World_HostClockEvents`50AA06`; World_HostClockEvents`50AA33` | CasinoRowdy |
| 90 | `0x5A` | `0x2032` | 50 | 1 | speech 1158 | fn 0x004b9740`4B9CFC` |  |
| 91 | `0x5B` | `0x2032` | 50 | 1 | speech 1109 | Casino_JackpotMissed`4D6A96` | JackpotMissed |
| 92 | `0x5C` | `0x201E` | 30 | 1 | speech 19 | **none** |  |
| 93 | `0x5D` | `0x201E` | 30 | 1 | speech 27 | **none** |  |
| 94 | `0x5E` | `0x201E` | 30 | 1 | speech 28 | **none** |  |
| 95 | `0x5F` | `0x2032` | 50 | 1 | speech 1349 | fn 0x004b9740`4B9C6B` |  |
| 96 | `0x60` | `0x201E` | 30 | 1 | speech 377 | Torture_RewardInformation`4F27C5`† |  |
| 97 | `0x61` | `0x201E` | 30 | 1 | speech 379 | Torture_RewardInformation`4F2779` |  |
| 98 | `0x62` | `0x20F0` | 240 | 1 | speech 370 | World_HostClockEvents`50A8CA` |  |
| 99 | `0x63` | `0xA000` | 0 | 1 | list `0x006a0448`: 13 keys, 180/300/420/600 s, no default | Torture_FindForCreature`4F2D4A` | RoomWanted |

## 4. How the raisers were found

Every `call 0x004C4600` in the image: **151 sites**, and Ghidra's cross-reference list gives the same 151 addresses. The call is thiscall with the type as the last-pushed argument, so each site's type is the immediate pushed just before the `ecx` setup (`add`/`lea ecx` of `+0x2E54`, `mov ecx,edi`, `mov ecx,[esi+0xC]`). 148 resolve to the last immediate before the call and 3 were read by hand (`0x00472673` = `0x2B`, `0x004BE1E3` = `0x2E`, `0x004C13DE` = `0x35`). Sites that choose between several types do so by `push imm; jmp` into the ecx setup in front of one shared call. Those arms are found by looking up to 700 bytes back for a `push imm8/imm32` followed by a short or near `jmp` that lands within 14 bytes before the call; the extra types found that way are the `†` rows (the last push itself is unmarked). Two earlier passes were too narrow and were corrected: an 80-byte window missed type `0x2D` (its arm is 112 bytes before the call), and an 8-byte landing bound missed arms landing 9 bytes before it (`Worker_SelectRescue` types `0x29`/`0x2B`, `Payday_FindMoney` type `0x37`, `State146_CasinoAlcove` type `0x59`). A separate independent census by the verifier reproduces every (type, site) pair here. No raw dword reference to `0x004C4600` exists in the file, so no call goes through a pointer. No site pushes a type that is not an immediate.

Types with no poster: 0 (`0x00`), 4 (`0x04`), 7 (`0x07`), 8 (`0x08`), 10 (`0x0A`), 11 (`0x0B`), 27 (`0x1B`), 29 (`0x1D`), 30 (`0x1E`), 31 (`0x1F`), 38 (`0x26`), 54 (`0x36`), 61 (`0x3D`), 67 (`0x43`), 68 (`0x44`), 70 (`0x46`), 74 (`0x4A`), 78 (`0x4E`), 79 (`0x4F`), 80 (`0x50`), 81 (`0x51`), 86 (`0x56`), 92 (`0x5C`), 93 (`0x5D`), 94 (`0x5E`).

Not checked: any other writer into the player's message queue (the save and load code at `0x004C43B0`, or code that links records directly). "No post" here means no `call 0x004C4600` pushes the type. An arm that reaches the call by falling through, or a type built at run time, would not match the pattern. That the unposted set equals OpenKeeper's independently derived "none found" set is the evidence against missing arms; the claim is still "no post found", not "provably unreachable".

## 5. Findings for our own docs

- `mentor-speech-and-notifications.md` §3 says field `+6` is a "role unread"; it is the tab graphic ([message-tabs.md](message-tabs.md) §3 already says so).
- Only 6 of the 100 rows were listed there; this file is now the full list.
- `mentor-speech-and-notifications.md` §3 and the `004C4600` record give Message_Post "86 callers"; there are **151 call sites**, in 107 distinct named functions by Ghidra's names (7 sites sit in no named function).
- Types with no poster (so nothing to build, and nothing a keeper can ever see) and the other types we do not post yet are both visible in the `built` column: a row with raisers and an empty `built` cell is a message DK2 posts that this project does not.
