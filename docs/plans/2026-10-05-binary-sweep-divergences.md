# Divergences found by the 2026-10-05 binary open-questions sweep

The research branch `research/binary-open-questions` answered about 60 open questions from
`DKII.EXE` and recorded each place where our code does something the binary does not. This is the
work list for the follow-up branch. The evidence lives in the cited question row and its research
doc; this file only lists and orders the work. Every item was graded CONFIRMED or STRONGLY INFERRED
by an independent reviewer.

**Progress:** rows marked built are on branch `fix/binary-sweep-divergences`.

**Owner decision (2026-10-05):** one follow-up branch, after this research is committed. Each change
gets its ADR, tests (a negative control that fails without it) and a replay-rig run against the
current Level 1–3 baselines.

## Likely to move level outcomes

| Q | Ours | DK2 | Where |
|---|---|---|---|
| Q508 (**built, ADR-0358**) | Never lowers a portal's `EntranceCreaturesGenerated` | A portal creature's death decrements its portal's tally (`0x00487C70`) | `PortalRules`, `PortalSystem` |
| Q164 (**built, ADR-0359**) | Rot and rescue clocks run while a body is carried | Both pause while carried (`0x004B12D0`, `0x004599D0`, `0x004B2CE0`) | `CreatureHealthSystem` |
| Q545 (**built, ADR-0357**) | Mana above the ceiling drains at 5/s | Clamped to the ceiling in one step (`0x004C10E0`) | `ManaRules` |
| Q330 (**built, ADR-0361**) | Trigger roots: action points first, then creatures, oldest first | Creatures (list 0) before action points (list 5), each newest first; a creature's tree runs inside its update | `TriggerExecutor.Run` |
| Q129 (**built, ADR-0360**) | Heart room `InitialHealth` percentage ignored | Level 7's heart starts at 1000; Level 8's heart room is ruined at load with no heart object | `ThingProjection` |
| Q514 (**built, ADR-0362**) | Non-ownable tiles keep the map's owner (3,629 shipped tiles) or get owner 0 | Forced to the neutral player `[0x006F2548]` | `WorldState.FromMap`, `RoomRules.cs:281`, `DefeatRules.cs:89`, `TriggerExecutor.cs:1271` |
| Q301 (**built, ADR-0363**) | Trigger action 22 declines room tiles; owner 0 for non-ownable terrain; no effect 0x146 | Sells the old room tile without refund, builds the new one free and untested | `TriggerExecutor.AlterTerrain` |
| Q255/Q259 (**Q259 hash built; Q255 deferred: `+0x20C` unread and the cosmetic draws dwarf it**) | No effect generators, so no draws | Every level gets a map-wide effect-90 generator drawing from the simulation seed (RNG fidelity gap, `dk2-random.md`) | new; `EffectGeneratorState` hashes three fields DK2 discards |

## Combat, creatures, rooms

| Q | Ours | DK2 | Where |
|---|---|---|---|
| Q511 (**built, ADR-0364**) | Victory nibble at the collapse of a victim outside category 22 | No nibble at a collapse; only an outright death while still linked | `VictoryRules.TargetDied` |
| Q510 (**built, ADR-0365**) | Forced commit only for a door | A forced creature target also commits (no melee short-cut, spell always cast, route check) | `CombatTargetRules.Forced` |
| Q376 (**built, ADR-0368**) | Victory pause fixed at 8 ticks | One play of art slot 35 (or 8): 1–22 turns by creature | `VictoryRules.PauseTicks` |
| Q400 (**built, ADR-0369**) | Flanker point behind the target's velocity; a standing target gives its own position; celebrate within 0x400² | Behind the heading via the sine tables; turn to face the body, no distance test | `ApproachRules.Destination`, `VictoryRules.GoingToBody` |
| Q399 (**deferred: map our `Running` writes to the 22 callers first**) | Haste scales the speed cap every tick | Written once; the next `SetSpeedMode` restores definition speed | `StatusEffectRules.SpeedCap` |
| Q398 (**built, ADR-0367**) | Shots updated oldest first | Each thing list newest first | `ShotSystem` (its summary, and `shots.md`'s "its tick after the creatures have moved", are stale) |
| Q376 (**built, ADR-0368**) | Slot-35 presence from file flag bit 0 | Present when the type is non-zero, or flag bit 0 | `ReaperRules.cs:121` |
| Q318–Q321 | Vampire on the last burial tile; subtract 1000 keeping the excess; no capacity gate; tally survives a partial sale | Last Graveyard Crypt; zero the tally; capacity gate `0x004DDCA0`; any sale rebuilds the room at 0 | `GraveyardRules` |
| Q329 | No Vampire reincarnation (Q111) | Five guards; no Graveyard or no capacity makes the death permanent | `CreatureDeathRules` |
| Q269 | Trigger condition 20 declined (ADR-0113) | Mood class `+0x28A` is `Anger_Add`'s band, which `CreatureAngerRules` already builds | `TriggerConditionEvaluator` |

## Triggers and data

| Q | Ours | DK2 | Where |
|---|---|---|---|
| Q229 | Flag 128 with the literal bit clear reads `IndirectFlagId`; indirect 128 reads `SpecialValue`; no MpdScore adder | Compares LevelScore with Value; indirect 128 reads bank byte `+0x3F70+0x80`; slot `+0x344` adds | `TriggerConditionEvaluator` |
| Q278 | `HandSystem.Slap` never counts | `+0xEE6` credited to the slapped thing's owner (creature, trap, object) | `HandSystem`, `PlayerState.SlapCount` |
| Q285 | No object trigger roots, no event 18 on claiming a neutral object | Object `+0x144` roots, event filter `0x004A3F50` | trigger walkers, `kwd unreached` |
| Q11 | JobPreference mood/mana read as `U16`/`ushort` | Signed 16-bit | `KwdCreatures`, `CreatureDef` |
| Q14 | Throws on CREATURES record sizes other than 5449/5537 | Accepts any size, zero-fills to 5449 | `KwdCreatures` |
| Q227 | Fields named `Unknown_*`; action 24's second field read as a dword | Roles now known; DK2 reads only its low byte | `KwdAdditionalRecords` |

## Stale code comments (verification audit, 2026-10-05)

| Where | Fix |
|---|---|
| `VictoryRules.cs:109` (**fixed**) | `BodyStandSquared`'s comment calls `0x004B2AE0` a position test; it is a turn step (Q400) |
| `GateWalk.cs:40` (**fixed**) | The `0x1000` cap is pushed by `0x004D0320` (`0x004D0967`), not held in `0x004D0150` |
| `TriggerExecutor.cs` (~737) | Types 20 and 21 are declined, but their mood-class writer is known (Q269): build them |
| `CategoryHooks.cs:262` (**fixed**) | When the states 199/200 combat-experience hook is built, its gate is "not a worker, or good" (`0x0047A870`): the hero Dwarf earns `ExpPerSecond`, the Imp never |

## ImpTaskSystem (blind re-read, 2026-10-05)

From the imp unit's blind re-read and its comparison (`build/verification/2026-10-05/blind/imp-compare.md`),
each graded by the reviewer. The evidence is in the cited function record. "Draws" says whether the
fix changes how many world-stream draws a tick takes, and so moves every later draw.

| Function | Ours | DK2 | Where | Draws |
|---|---|---|---|---|
| `0x0046AEA0` | A free side whose walk fails is passed over; nothing is dropped | Deletes the wall's kind-6/16 entry (`0x00508040`), a pre-existing one included, then tries the next side | `TryReinforceAt`, `ImpTaskSystem.cs:1149-1166` | no |
| `0x00464400` (**built, ADR-0355**) | First chain tile with space (`SpaceIn > 0`, Heart rim at 1000); on failure the next room; walks to the tile | Tile with no class-2 thing or a kind-3 pile under the Treasury cap (3000, Heart too); back at the start returns 0; walks to `0x00698C20[rand(8)]` | `GoldStore.DepositTileAlongChain`, `GoldStore.cs:158-170`; `TryRouteToSomewhereItFits`, `ImpTaskSystem.cs:841-855` | **yes**: +1 rand(8) per store choice |
| `0x004F3C60` (**built, ADR-0355**) | A store tile's gold is tile state; no pile is created | rand(0x800) (line `0x14F`) after each store pile it creates | `GoldStore.FillAlongChainFrom`, `GoldStore.cs:191-205`; `StoreSweepSystem.cs:53` | **yes**: +1 per new store pile |
| `0x004651A0` | No cast on a walk | Casts a ready bit-`0x40` ability (Imp Haste) on every walk start, `noTeleport` or not | the imp's walk start (Q401) | no draw seen; speed |
| `0x00463DF0` (**built, ADR-0356**) | Failed aimed kind-3 retry: one claim search (`else if`) | Falls through to column 5: a second `SearchKind(3)` and a second rand(4) (`0x00468D54`) | `TryTakeJob`, `ImpTaskSystem.cs:885-901` | **yes**: +1 rand(4) per failed aimed claim retry |
| `0x00468D10` | Own tile filtered by `RingScanVisits` in Tier B and the drop scan | Own tile reaches the chain untested (`0x00468DA0`) | `TrySweep` ~`ImpTaskSystem.cs:1131`; `TryTakeDroppedJob` ~`1003` | no |
| `0x0046B9C0` | Shared latch tested before the crate branch; a blueprint take goes through the kind-8 route check and steal | Crate branch `0x00468910` first; its success returns at once | `TryObjectAt`, `ImpTaskSystem.cs:2031-2035` | no |
| `0x0048904E` | Any world object within the box; picks up before the claim on the way, which is gated on activity | Own tile's thing list only, first pile, break; the claim on the way first, on a tile change and category not 22 | `PickUpGoldInPassing`, `ImpTaskSystem.cs:345-359`; order `195` against `200-202` | no |
| `0x00466850` | Every pile on the tile, to the full limit | min(pile, room) per pile within ±`0x555`, stopping once `0x00464360(1)` reports full (limit less variable 74) | `TakeFromPilesOn`, `ImpTaskSystem.cs:662-663`, `697-712` | no |
| comment (**fixed**) | `ImpTaskSystem.cs:966`: DK2 zeroes the last kind after Tier B | The only zeroing is `0x00464010`, before the drop scan | `ImpTaskSystem.cs:966` | no |
| comment (**fixed**) | `ImpTaskSystem.cs:1455`: held-claim stealing not built | Built (`TryJobOn` → `HolderToTakeFrom`) | `ImpTaskSystem.cs:1455` | no |

## Asset pipeline (presentation only)

| Q | Ours | DK2 | Where |
|---|---|---|---|
| Q442 | Key byte discarded; each frame held at the previous key; STEP keys at an assumed 30 fps | Linear interpolation between key entries (`0x0057E5B0`); clip length frames/FPS from the art descriptor | `KmfReader.cs:138`, `GlbWriter.cs:16,201` |
| Q445/Q446 | No torches | Placed by command 4, recoloured by command 16 | Unity adapter, `RoomDef` |
| Q71 | One wall mesh per solid terrain | Piece chooser `0x00444F10` by TileConstruction | `AssetCatalogue` |
