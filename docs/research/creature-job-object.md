# A creature’s job and needs, named by DK2 (Q264)

**Scope:** the field names DK2 prints for a creature's job, needs and imp task state, transcribed from its debug
dumper at `0x0047920D`. Method, caveats and the other nine dumpers:
`debug-dumpers.md`. **A name is not a mechanism** — it says a field exists and
roughly what it holds, never its unit, writer or rule.

| offset | size | name |
|---|---|---|
| `+4` | dword | Opened %d |
| `+10` | dword | CurrentStateId %d |
| `+14` | dword | GameTickLastUpdated %d |
| `+18` | dword | StartedAt %d |
| `+1C` | dword | Age %d |
| `+40` | dword | StateBeforeUtilityState %d |
| `+44` | dword | SuspendTime %d |
| `+48` | dword | LastAteTime %d |
| `+4C` | dword | HungerNeed %d |
| `+50` | dword | LastAttemptedHunger %d |
| `+54` | dword | LastSleptTime %d |
| `+58` | dword | SleepNeeded %d |
| `+5C` | dword | LastAttemptedSleep %d |
| `+60` | dword | PayCheckedTime %d |
| `+64` | dword | CurrentlyDoingJobId %d |
| `+68` | dword | ReturnToJobId %d |
| `+6C` | word | ReturnRoomTagId %d |
| `+6E` | dword | DroppedToDoJobId %d |
| `+72` | dword | InitiatedEndOfLevelGems %d |
| `+76` | dword | Data.StartedAt %d |
| `+7A` | dword | Data.CarriedGold %d |
| `+7E` | dword | Data.TimerTick %d |
| `+82` | dword | Data.LastGameTickInterrupted %d |
| `+86` | dword | Data.IdleCounter %d |
| `+8A` | word | ImpJobData.ImpTaskID %d |
| `+8C` | dword | ImpJobData.ImpTaskPositionX %d |
| `+90` | dword | ImpJobData.ImpTaskPositionY %d |
| `+94` | word | ImpJobData.ImpTaskSurface %d |
| `+96` | word | ImpJobData.ImpTaskSlot %d |
| `+98` | byte | ImpJobData.LastTaskType %d |
| `+99` | dword | ImpJobData.JustDropped %d |
| `+9D` | byte | ImpJobData.DroppedTask %d |
| `+9E` | dword | ImpJobData.LastEvaluate %d |
| `+A2` | byte | ImpJobData.ScanningJob %d |
| `+A3` | dword | ImpJobData.ScanningData %d |
| `+DF` | dword | UtilityNewState %d |
| `+E3` | dword | WaitFor %d |
| `+E3` | dword | MoveTo (%d, %d) |
| `+E3` | dword | MoveWithinRangeOfThing (%d, %d) |
| `+EB` | dword | FailedMoveState %d |
| `+EF` | word | ThingId %d |
| `+F1` | dword | RangeSquared %d |
| `+F5` | dword | ReCalcNavCounter %d |
| `+E3` | dword | MoveToForNTurns (%d, %d) |
| `+EF` | dword | TurnCounter %d |
| `+E3` | word | RoomId %d |
| `+E5` | byte | RoomTypeId %d |
| `+E6` | dword | MoveToRoom (%d, %d) |
| `+EE` | dword | FailedMoveState %d |
| `+F9` | dword | LastGameTickSuccessfullyChoseUnhappyJob %d |
| `+FD` | dword | LastGameTickSuccessfullyChoseHappyJob %d |
| `+101` | dword | LastGameTickSuccessfullyChoseAngryJob %d |
| `+105` | dword | LairCalculated %d |
| `+109` | dword | LairAvailability %d |
| `+10D` | word | Lair %d |
| `+10F` | dword | PrisonCalculated %d |
| `+113` | dword | PrisonAvailability %d |
| `+117` | word | Prison %d |
| `+119` | dword | TorturingTargetPositionX %d |
| `+11D` | dword | TorturingTargetPositionY %d |
