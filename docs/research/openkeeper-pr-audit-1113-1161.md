---
systems: [tooling-process, campaign-triggers, creatures, rooms, imps-tile-work, keeper-spells, camera-lighting]
---
# OpenKeeper PR relevance: 1113 through 1161

Reviewed 2026-11-13 against local HEAD `36db3f01`. GitHub's all-state list had 46 PRs above the
previous marker 1110 (1111, 1112 and 1158 are issues): 41 open, 5 merged (1119, 1122, 1125,
1127 and 1129). This is a relevance audit of their descriptions and production
patches against our code, tests and canonical research, done in the same form as
[the 1000-1110 audit](openkeeper-pr-audit-1000-1110.md). No gameplay fix was made, no test suite
was run and no native experiment was played. "Covered" means the code and tests exist, not that
they were run for this audit. No OpenKeeper code was copied.

PR links are provenance for leads, not proof of DK2 behaviour. Where a row says **fresh read**,
the claim rests on a Ghidra read made for this audit; the function records named in that row
carry the evidence. Other rows rest on our existing records, or on a comparison of code alone
where the row says so. Many of these PRs were written with an assistant and cite DK2 addresses
that match our records.

## What merits work

Ordered by ordinary gameplay first.

1. **A prisoner dropped on a Prison's rim ([#1153](https://github.com/tonihele/OpenKeeper/pull/1153)).**
   Fresh read of [0x004E8DA0](functions/004E8DA0.md): room type 11 routes to arm `0x004E8E82`,
   which takes the creature only when Room_IsFloorSlot `0x00454110` accepts the cell, so only
   interior tiles; the Prison claim `0x004E5A70` uses the same test. A rim drop is stunned and
   loose, its jailer word kept until 255 ends. Ours: `HandSystem.DropIntoACell` takes it on any
   tile of the keeper's Prison. Next: require the interior, and add a rim-drop test beside
   `PrisonTests.DroppingAPrisonerBackOnAPrisonTilePutsItStraightBack`. No shipped script involved.
2. **The walk-over chicken (found through [#1124](https://github.com/tonihele/OpenKeeper/pull/1124)).**
   Fresh read of [0x00488930](functions/00488930.md) `0x004892C5..0x0048932E`: a creature the
   worker arm does not take, standing on a loose neutral Chicken (object type 9), not in state 76
   and not on a Portal or Hatchery tile, destroys it and starts action 26 at its position. No
   hunger test or change, no health top-up. A Casino tile or `+0x3F2` skips the whole walk.
   Ours: `WalkOverGoldSystem` documents the arm as not built. Next: build it beside the gold arm
   with a test next to `WalkOverGoldTests`; Q494 stays the observable check.
3. **Tests only: needs and pay interrupt Workshop work too ([#1126](https://github.com/tonihele/OpenKeeper/pull/1126), [#1130](https://github.com/tonihele/OpenKeeper/pull/1130)).**
   The shared mask `0x800A3C3B` ([0x00476D30](functions/00476D30.md)) admits category 5,
   Manufacture; `NeedInterruptTests` pins only Training. Add states 63 and 64 to
   `TheThreeNeedsShareOneMask` and a hungry or owed-wages maker test.
4. **Slapping a Horny ([#1138](https://github.com/tonihele/OpenKeeper/pull/1138)).** Fresh
   read of [0x004918B0](functions/004918B0.md): the slap predicate refuses state 0x4C, and a
   Horny only while `+0x46F` bit 0 is clear. `HandRules.CanSlap` refuses a Horny outright (Q158).
   No shipped consequence known; low value.

## All 46 PRs

Source files are relative to `src/DK2.Simulation/` unless qualified.

### Script actions

| PR | Finding for this project | Evidence / next action |
|---|---|---|
| [1113](https://github.com/tonihele/OpenKeeper/pull/1113) | Deliberate OpenKeeper deviation: DK2 never enforces a slap limit | Fresh read of [0x00509880](functions/00509880.md): a whole-image scan finds only the Slaps Remaining HUD (`0x00427964`, `0x00427F9F`), the world dumpers and the init to -1 reading `MaximumSlaps` (world `+0x72B8`); the hand never refuses a slap. Ours stores it (`TriggerState.MaximumSlaps`, ADR-0532) and draws it (`HudIndicators`). In DK2 0 means "0 left". 3 shipped records (mpd1, Secret1; [0x004C8620](functions/004C8620.md)). Q631 stays open. |
| [1155](https://github.com/tonihele/OpenKeeper/pull/1155) | Covered: condition 39, AP_CONGREGATE_IN | `TriggerAreaConditions` case 39, [0x004B0620](functions/004B0620.md), [dispatch tables](trigger-dispatch-tables.md). Selectors 0, 3 and 6 ship (110, 70 and 3 of 183 records). Tests: `TriggerAreaConditionsTests.Type39*`. |
| [1156](https://github.com/tonihele/OpenKeeper/pull/1156) | Covered: condition 70, AP_POSESSED_CREATURE_ENTERS | `TriggerAreaConditions.PossessedCreatureExists`; object branch declined (Q280). 6 shipped records. Tests: `TypeSeventy*`. The PR's player-0 wildcard is its own. |
| [1157](https://github.com/tonihele/OpenKeeper/pull/1157) | Covered: condition 69, AP_TAG_ALL_OF | `TriggerAreaConditions` case 69; comparison and value unread by the arm (`0x004B0EA1`). 5 shipped records (Level1, Level2). Test: `Type69AsksWhetherEveryTaggableTileInTheRectangleIsTagged`. |
| [1159](https://github.com/tonihele/OpenKeeper/pull/1159) | Covered: CREATE_CREATURE's tile is zero-based | [dispatch tables](trigger-dispatch-tables.md) "Action type 1" (arm `0x00518B12`; 268 shipped records), [trigger-action-arms.md](trigger-action-arms.md). Tests: `TriggerExecutorTests`, `TriggerExecutorOnRealDataTests`. |

### Rooms, torture and prison

| PR | Finding for this project | Evidence / next action |
|---|---|---|
| [1148](https://github.com/tonihele/OpenKeeper/pull/1148) | Covered: Workshop capacity is furniture + 1, or 0 with none | [0x004E3ED0](functions/004E3ED0.md), [auto-placed-furniture.md](auto-placed-furniture.md) §5. `AutoFurnitureRules.TryTotalCapacity`, `RoomCapacityRules`; the third-of-crates turn-away ([0x004F6140](functions/004F6140.md)) is `CrateRules.WorkshopHasRoom`. Tests: `AutoFurnitureTests`. |
| [1149](https://github.com/tonihele/OpenKeeper/pull/1149) | Covered: Casino capacity is furniture + 1, or 0 with none | [0x004E53B0](functions/004E53B0.md), [0x004E5A70](functions/004E5A70.md). `AutoFurnitureTests`. |
| [1150](https://github.com/tonihele/OpenKeeper/pull/1150) | Covered: drop validity, including the rules the PR lists as unbuilt | `HandRules.MapAllowsCreature`, `CanDrop`, `IsSolidOrDoor`, `IsHeartInner`; [0x00450910](functions/00450910.md), [0x00450BE0](functions/00450BE0.md), [0x00450D40](functions/00450D40.md), [0x004DA150](functions/004DA150.md), [0x0040D8F0](functions/0040D8F0.md). Tests: `HandDropGateTests`. |
| [1152](https://github.com/tonihele/OpenKeeper/pull/1152) | OpenKeeper-only fix (stale room-instance owner) | Fresh read of [0x0046EAB0](functions/0046EAB0.md) `0x0046EF29..0x0046EF99`: the enemy-room arm compares the landing tile's owner, never a room object's. Our `RoomInstance` is derived from tiles and cannot go stale. |
| [1153](https://github.com/tonihele/OpenKeeper/pull/1153) | **Gap: a prisoner dropped on a Prison's rim is taken in** | Item 1 above; fresh read of [0x004E8DA0](functions/004E8DA0.md). |

### Creatures, imps, spells and sound

| PR | Finding for this project | Evidence / next action |
|---|---|---|
| [1122](https://github.com/tonihele/OpenKeeper/pull/1122) | Covered: a gem face takes no damage and pays on every blow | [0x004971D0](functions/004971D0.md); `TileWorkRules.Apply` (ADR-0470). Test: `TileWorkTests.AGemFace_IsNeverDamagedAndPaysTheGemsRateEveryStrike_ConfirmedByBinaryAndOwner`. |
| [1123](https://github.com/tonihele/OpenKeeper/pull/1123) | Covered: three workers per open side, twelve at most | [0x00507BB0](functions/00507BB0.md), [0x00469AF0](functions/00469AF0.md). `MiningSlots` (PerFace 3), `ImpTaskSystem.Selection`, `TaskStealingTests`; a side counts only when reachable (ADR-0472). |
| [1124](https://github.com/tonihele/OpenKeeper/pull/1124) | Covered, more faithful than the PR; it surfaced the walk-over chicken gap | [0x00477C20](functions/00477C20.md): the clock adds its fill once the rate has passed and stamps now; each chicken subtracts 1. `EatingRules.Tick`, `CreatureEatSystem.Clock`; tests `CreatureNeedsTests.TheHungerClockAccumulatesRatherThanSaturating`, `EachChickenPaysOffOneAndRestartsTheClock`. The PR's catch-up is an artefact of its idle-only polling. Item 2 above. |
| [1125](https://github.com/tonihele/OpenKeeper/pull/1125) | Covered | [0x00457880](functions/00457880.md) zeroes the balance on promotion (Q127, ADR-0083). `CreatureLevelRules`; test `CreatureLevelTests.TheHitThatReachesThreeThousandBuysLevelTwo`. |
| [1126](https://github.com/tonihele/OpenKeeper/pull/1126) | Covered, with a test gap | The need mask admits Manufacture ([0x00476D30](functions/00476D30.md), [creature-needs-routine.md](creature-needs-routine.md) §2); `NeedRules.Admits`. Item 3 above. |
| [1130](https://github.com/tonihele/OpenKeeper/pull/1130) | Covered: the walk to a Treasury or Heart for pay | [0x00472320](functions/00472320.md), [0x00472390](functions/00472390.md), [0x00472740](functions/00472740.md), [0x004F4510](functions/004F4510.md). `PaydaySystem`, `PaydayTests`. Ours follows the native walk and its 4-turn retry; the PR's random tile and delay are its own. |
| [1134](https://github.com/tonihele/OpenKeeper/pull/1134) | Covered, ours goes further | [0x004B2CE0](functions/004B2CE0.md), [0x0048F350](functions/0048F350.md). `StatusEffectRules.DamageMultiplier` (frozen, armour or possession, stunned states, the Pit). Tests: `CreatureStatusEffectTests`, `CombatPitTests`. |
| [1135](https://github.com/tonihele/OpenKeeper/pull/1135) | Covered | [0x004BC710](functions/004BC710.md), [0x0049EF90](functions/0049EF90.md), [0x0048AE70](functions/0048AE70.md), [0x00481610](functions/00481610.md). `DropDeliveryRules`, `HandTests.GoldDroppedOntoACreatureClearsItsWholeWageForHalfThePile`. |
| [1136](https://github.com/tonihele/OpenKeeper/pull/1136) | Covered, one known approximation | [0x004510C0](functions/004510C0.md) merges within 0x40 of the miner; ours per third-of-a-tile cell (`FloorGold.PutDown`), as the record states. Tests: `GoldCarryingTests`, `GoldLandingTests`. |
| [1138](https://github.com/tonihele/OpenKeeper/pull/1138) | Covered; a small difference for a Horny | Fresh read of [0x004918B0](functions/004918B0.md); [0x0048E6A0](functions/0048E6A0.md). `HandRules.HasPickupPermission`, `HandSystem.OutcomeOf`. Item 4 above. |
| [1147](https://github.com/tonihele/OpenKeeper/pull/1147) | OpenKeeper-only fix (entity-filter cost); behaviour covered | `CallToArmsRules.WithinFightDistance` reads the owner's single call. `CallToArmsTests`. |
| [1151](https://github.com/tonihele/OpenKeeper/pull/1151) | OpenKeeper-only fix (stale tasks in its queue) | Ours keeps no task queue: [imp-task-stealing.md](imp-task-stealing.md), `ImpTaskSystem`. |
| [1154](https://github.com/tonihele/OpenKeeper/pull/1154) | Covered for leaders and followers; its "theft-capable member" is its own | [hero-party-walk.md](hero-party-walk.md) §1, §5, [steal-gold.md](steal-gold.md) §3; `HeroSystem.StandingJob`, `StealGoldRules.InTheTreasury`, `HeroPartyRules.KnockedOut` (ADR-0218). The steal arm `0x00481FA0` has no capability test. |
| [1160](https://github.com/tonihele/OpenKeeper/pull/1160) | Covered: a slapped worker is interrupted | [0x0048B8A0](functions/0048B8A0.md), state 77 (`0x0046F1F0`); `SlapRules.ReactionState`. Test: `SlapStateTests.AWorkerSlappedIdlesAfterTheReaction_TheTableRefusing10From77`. |
| [1161](https://github.com/tonihele/OpenKeeper/pull/1161) | Covered: imps carry trap crates to blueprints | `ImpTaskKind.BuildTrap`, `ImpTaskSystem.Crates.cs`, `CrateRules.Build` (ADR-0214), [workshop-crates.md](workshop-crates.md) §6-8; tests `CrateTests`. The PR's "oldest blueprint first" and texts 2612/2613 are its own. |

### Engine, rendering and tooling

Every one is an OpenKeeper-only fix: a race between its jME threads, an entity-system cost, or
a rendering cache. Ours ticks one worker over a live world, the frame reads a presented copy and
events are queued and replayed (ADR-0693, ADR-0694), which removes the thread-race class.

| PR | Finding for this project | Evidence / next action |
|---|---|---|
| [1114](https://github.com/tonihele/OpenKeeper/pull/1114) | OpenKeeper-only fix (creature controller cache) | No such cache; one tick thread. |
| [1115](https://github.com/tonihele/OpenKeeper/pull/1115) | OpenKeeper-only fix (fog bit sets across threads) | Fog is simulation state (`FogOfWarSystem`, ADR-0076); the frame reads the presented copy. |
| [1116](https://github.com/tonihele/OpenKeeper/pull/1116) | OpenKeeper-only fix (events on the emitting thread) | `DeferredEventSink` (ADR-0694). |
| [1117](https://github.com/tonihele/OpenKeeper/pull/1117) | OpenKeeper-only fix; same bug class handled | A spell's target is resolved by id at tick time: `KeeperSpellRules.CanCast` returns `MissingCreature`; ids are never reused. |
| [1118](https://github.com/tonihele/OpenKeeper/pull/1118) | OpenKeeper-only fix (steering removing a fresh path) | No second writer of path state. |
| [1119](https://github.com/tonihele/OpenKeeper/pull/1119) | OpenKeeper-only fix (Call to Arms banner view) | Our recall effect is built (ADR-0771). |
| [1120](https://github.com/tonihele/OpenKeeper/pull/1120) | OpenKeeper-only fix (speech queue across threads) | Speech arrives through the deferred events (`DungeonRunner.Sound.cs`). |
| [1121](https://github.com/tonihele/OpenKeeper/pull/1121) | OpenKeeper-only fix (volatile camera flag) | Camera paths are simulation state ([camera-paths.md](camera-paths.md)). |
| [1127](https://github.com/tonihele/OpenKeeper/pull/1127) | OpenKeeper-only fix (flood-fill visit once) | No DK2 rule; ours follows [combat-acquisition.md](combat-acquisition.md). |
| [1128](https://github.com/tonihele/OpenKeeper/pull/1128) | OpenKeeper-only fix (entity filter in the eat task) | `ChickenRules.NearestReachableHatchery`. |
| [1129](https://github.com/tonihele/OpenKeeper/pull/1129) | OpenKeeper-only fix (enum array copies) | The state is an int (`CreatureState.Dk2StateId`). |
| [1131](https://github.com/tonihele/OpenKeeper/pull/1131) | OpenKeeper-only fix (material lighting setup) | Ours binds lighting once per mesh and caches tinted materials (`ClassicMeshes`). |
| [1132](https://github.com/tonihele/OpenKeeper/pull/1132) | OpenKeeper-only fix (KMF pose cache) | Ours samples a fractional phase each frame (`RenderAnimation.Sample`); a lead only: skip resampling a held pose, and cull off-screen creatures in `DungeonScene.DrawCreatureFrame`, if a crowded-level profile asks. |
| [1133](https://github.com/tonihele/OpenKeeper/pull/1133) | OpenKeeper-only fix (per-frame lighting setup) | As 1131. |
| [1137](https://github.com/tonihele/OpenKeeper/pull/1137) | OpenKeeper-only fix (shared position vectors) | Single-threaded simulation. |
| [1139](https://github.com/tonihele/OpenKeeper/pull/1139) | OpenKeeper-only fix (research lists across threads) | The HUD reads the presented copy. |
| [1140](https://github.com/tonihele/OpenKeeper/pull/1140) | OpenKeeper-only fix (shutdown order) | `ReleaseRuntimeResources` waits for the tick first. |
| [1141](https://github.com/tonihele/OpenKeeper/pull/1141) | OpenKeeper-only fix (room map across threads) | Rooms are tick-only state. |
| [1142](https://github.com/tonihele/OpenKeeper/pull/1142) | OpenKeeper-only fix (landing on the logic thread) | A drop lands inside the tick (`HandSystem.LandDue`, ADR-0226, [hand-drop-landing.md](hand-drop-landing.md)). |
| [1143](https://github.com/tonihele/OpenKeeper/pull/1143) | OpenKeeper-only fix (entity-set lookups) | No entity sets; `world.Creatures` scanned by owner. |
| [1144](https://github.com/tonihele/OpenKeeper/pull/1144) | OpenKeeper-only fix; a performance lead | DK2 pre-checks region ids before routing ([0x004C9400](functions/004C9400.md)); ours lets a failed search stand in (ADR-0157). Only if profiling asks. |
| [1145](https://github.com/tonihele/OpenKeeper/pull/1145) | OpenKeeper-only fix (position in entity sets) | Held creatures are explicit flags (`CreatureState.InHand`, `HandRules.IsHeld`). |
| [1146](https://github.com/tonihele/OpenKeeper/pull/1146) | OpenKeeper-only fix (evaluation order) | No DK2 content. |

## Limits

The new native facts above are recorded in the linked function records with their confidence.
[0x004E8DA0](functions/004E8DA0.md)'s Combat Pit arm reads in the opposite sense to the record's
older body text; that is flagged in the record and not reconciled (our `CombatPitRules` is
unchanged). The PRs' own measurements were not checked. No replay outcome, simulation hash or
gameplay acceptance changed.
