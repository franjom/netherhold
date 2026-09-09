# The placed-object thing, named by DK2 (Q264)

**Scope:** the field names DK2 prints for a placed object, transcribed from its debug
dumper at `0x0049D336`. Method, caveats and the other nine dumpers:
`debug-dumpers.md`. **A name is not a mechanism** — it says a field exists and
roughly what it holds, never its unit, writer or rule.

| offset | size | name |
|---|---|---|
| `+124` | word | RoomNode (%d, %d) |
| `+128` | word | AutoPlacedObjectNode (%d, %d) |
| `+12C` | byte | TypeId %d |
| `+133` | dword | AutoPlaced %d |
| `+137` | byte | TriggerRootRepeatCount %d |
| `+138` | word | RoomId %d |
| `+13A` | dword | StateId %d |
| `+13E` | dword | StateAge %d |
| `+142` | word | WhoGetsThisFromADrop %d |
| `+144` | word | TriggerRoot %d |
| `+14A` | dword | Tick %d |
| `+14E` | byte | EffectCounter %d |
| `+14F` | byte | Destroyed %d |
| `+14A` | dword | GoldHeld %d |
| `+14E` | word | XRotation %d |
| `+14A` | dword | Direction %d |
| `+14A` | dword | FreeRange %d |
| `+14E` | dword | PeckingTimer %d |
| `+152` | dword | StayThere %d |
| `+156` | byte | ScatterGameTicks %d |
| `+14A` | dword | PeckingTimer %d |
| `+14E` | dword | StayThere %d |
| `+14A` | dword | SparkleEffectTimer %d |
| `+14A` | dword | Type %d |
| `+14E` | byte | Id %d |
| `+14F` | word | InRoom %d |
| `+151` | dword | SparkleEffectTimer %d |
| `+14A` | dword | GameTickStart %d |
| `+14E` | word | InitialRevolutionSpeed %d |
| `+150` | byte | Direction %d |
| `+14B` | byte | Speed %d |
| `+14C` | dword | Fire %d |
| `+14A` | word | CreatureId %d |
| `+14C` | dword | FlagDestroyWhenDropped %d |
| `+14E` | byte | TypeId %d |
| `+14A` | dword | VX %d |
| `+14E` | dword | VY %d |
| `+152` | dword | Speed %d |
| `+156` | dword | Damage %d |
| `+15A` | byte | PreferedVerticalDirection %d |
| `+15B` | byte | PreferedHorizontalDirection %d |
| `+14A` | dword | eo %d |
| `+14E` | dword | center_pos %d |
| `+152` | word | dodge_speed %d |
| `+14A` | word | FlareEffectCounter %d |
| `+14A` | word | ChildTagId (%d, %d) |
| `+14E` | byte | EffectTriggerDelayGameTicks %d |
| `+14A` | dword | EffectTriggerRepeatTimer %d |
| `+14A` | dword | Timer %d |
| `+14E` | dword | StartThreat %d |
| `+152` | dword | CurrentThreat %d |
| `+14A` | dword | InUse %d |
| `+15C` | dword | UtilityNewState %d |
| `+160` | dword | EndX %d |
| `+164` | dword | EndY %d |
| `+168` | dword | dX %d |
| `+16C` | dword | dY %d |
| `+170` | word | GameTicksToDestination %d |

## Bitfields

Each group below is one dword, shifted and masked before the push.

**`+146`** — `InHand` bit 4, `EnableDeathEffect ` bit 0, `BeingDragged` bit 1, `BeingCarried` bit 2, `InUse` bit 5, `Possessed` bit 3

**`+172`** — `Turning` bit 0, `Interruptable` bit 1
