---
systems: [tooling-process, campaign-triggers, creatures, rooms, imps-tile-work, keeper-spells, audio, camera-lighting]
---
# OpenKeeper PR relevance: 1000 through 1110

Reviewed 2026-11-11 against local HEAD `49a861a7` (records checked again at `ea32b114`). GitHub's all-state list
had 81 PRs above the previous marker 999: 70 open, 10 merged, 1 closed. This
is a relevance audit of their descriptions and production patches against our
code, tests and canonical research, done in the same form as
[the 938-999 audit](openkeeper-pr-audit-938-999.md). No gameplay fix was made,
no test suite was run and no native experiment was played. "Covered" means the
code and tests exist, not that they were run for this audit. No OpenKeeper
code was copied.

PR links are provenance for leads, not proof of DK2 behaviour. Where a row
says **fresh read**, the claim rests on a Ghidra read made for this audit;
the function records named in that row carry the evidence. Other rows rest on
our existing records, or on a comparison of code alone where the row says so.

## What merits work

Ordered by shipped use and ordinary gameplay first.

1. **ALTER_SPEED, creature action 33 ([#1102](https://github.com/tonihele/OpenKeeper/pull/1102)).**
   72 shipped records (Level4, Level9, Level19, TrapTime). Arm `0x0049AF3D`
   calls [0x004887D0](functions/004887D0.md) with walk (0) or run (non-zero) from
   the operand's low byte. Built (ADR-0739): the mode only, not the speed-set
   mark ([speed-set turn](speed-set-turn.md) "Deviations").
2. **Creature event 8, tortured ([#1011](https://github.com/tonihele/OpenKeeper/pull/1011)).**
   88 shipped records. Fresh read:
   [0x00484110](functions/00484110.md) fires it at `0x004842BE`, once per strap
   and again on each re-strap, also for a keeper's own creature. Built
   (ADR-0739) in `TortureRules.EnterTheDevice`, `Restrap` and `TryStrapSelf`.
3. **Torture conversion messages and event 9 ([#1001](https://github.com/tonihele/OpenKeeper/pull/1001), [#1017](https://github.com/tonihele/OpenKeeper/pull/1017)).**
   Fresh read of [0x004F2800](functions/004F2800.md): message 0x39 to the old owner,
   owner change, anger clear, message 0x3A to the new owner, creature event 9.
   Built (ADR-0740) in `TortureSystem`'s conversion block. Event 9 has no
   shipped records; the messages matter more.
4. **REMOVE_FROM_MAP, creature action 35 ([#1036](https://github.com/tonihele/OpenKeeper/pull/1036)).**
   27 records in five campaign levels. Fresh read:
   [0x00487C70](functions/00487C70.md) skips the killed event, the count and the
   gold spill when `+0x3E6` bit 0x40 is set, which our `DeathUncounted` mirrors.
   The PR's claim that the killed event fires is wrong. Built (ADR-0744): byte 0
   sets the flag and `CreatureRemovalSystem` removes the creature through `Die`;
   byte 1 (the death fall) has no shipped record and stays unperformed.
5. **Imp body-carry takeover ([#1023](https://github.com/tonihele/OpenKeeper/pull/1023)).**
   Fresh read of [0x0046D810](functions/0046D810.md): a kind-13 body task has a
   holder, and a strictly nearer imp kicks a live holder to state 10 and takes
   it; [0x0046D3C0](functions/0046D3C0.md) does the same for kind-12 corpses.
   Built (ADR-0742).
6. **CREATE_PORTAL_GEM, world action 2 ([#1034](https://github.com/tonihele/OpenKeeper/pull/1034)).**
   Five records in four campaign levels; answers the action-2 half of Q755
   (level-placed gems are still unread). Fresh read: arm
   `0x00518E92` uses the tile as stored (zero-based), centre plus floor height
   from `0x004545C0`, then `Object_Create`.
7. **LEVEL_PAY_DAY, player event 46 ([#1032](https://github.com/tonihele/OpenKeeper/pull/1032)).**
   Fresh read of [0x004BE1A0](functions/004BE1A0.md): fired after the wage loop.
   One record, the level 1 tutorial pay-day speech, which never plays now.
8. **Timer speech and countdown narration, world action 51 ([#1092](https://github.com/tonihele/OpenKeeper/pull/1092)).**
   On by default (world init `0x005099A0` sets world `+0x72A1` bits 0-2), so
   it should be heard in timed levels (86 action-23 records) unless one of
   the 13 explicit action-51 records turns it off; which of those clear it
   depends on their byte 0, not checked. Fresh read of `0x004B9740`: equality
   tests at 1800 to 60 s and 10 to 1 s, speech ids 1 to 18.
9. **CHANGE_ROOM_OWNER, world action 49 ([#1090](https://github.com/tonihele/OpenKeeper/pull/1090)).**
   Level17's three handovers. Fresh read: the arm calls
   [0x004E5650](functions/004E5650.md) directly, so no health reset. Do not
   copy the PR's health restore.
10. **Hand events: slap 5 / P60 ([#1003](https://github.com/tonihele/OpenKeeper/pull/1003)), pick-up P58 / 65 ([#1018](https://github.com/tonihele/OpenKeeper/pull/1018)).**
    Sites and recipients pinned by fresh reads of
    [0x0048B8A0](functions/0048B8A0.md) and [0x004BFBD0](functions/004BFBD0.md).
    One shipped record each.
11. **Creature action 43 (found while checking [#1029](https://github.com/tonihele/OpenKeeper/pull/1029)).**
    Fresh read of [0x0049AC80](functions/0049AC80.md): arm 0x2B queues world
    action 4 with the creature's tile, with no ownership test. Unlike action 26
    it puts the creature's id, not a player id, in the player word, and the
    handler is unread, so "camera glide to the creature for any tree" is a
    HYPOTHESIS. Seven shipped records; ours treats it as a no-op.
12. **Torture reward 11 room reveal ([#1044](https://github.com/tonihele/OpenKeeper/pull/1044)).**
    Built (ADR-0740) in `TortureInformationRules.RevealTo` and
    `FogOfWarSystem.TortureSightPass`, from
    [torture-information.md](torture-information.md) §4 and
    [fog-reveal-sources.md §3.7](fog-reveal-sources.md).
13. **Held object as a fetch target ([#1075](https://github.com/tonihele/OpenKeeper/pull/1075)).**
    Fresh read: `Hand_PickUp` ([0x004BC500](functions/004BC500.md)) unlinks the
    object from its tile ([0x004B4F60](functions/004B4F60.md)), and the object scan
    [0x0046B9C0](functions/0046B9C0.md) walks only the tile list, so DK2 never meets
    a held object. Built (ADR-0744): `ObjectCarryRules.IsLoose` and `ObjectOn`
    ignore an object in the hand.
14. **Presentation:** trough effects 274/279 once a game second
    ([#1041](https://github.com/tonihele/OpenKeeper/pull/1041), fresh read of
    [0x00499300](functions/00499300.md); the effects are not behind the
    `0x00759ADC` sound gate), built (ADR-0740) in `TortureSystem.TurnTheWheel`; shipped 279 has no art and shows nothing. Still open: Torture Chamber and Casino torch phase
    ([#1040](https://github.com/tonihele/OpenKeeper/pull/1040),
    [wall torches §2](wall-torches.md)); Call to Arms HUD widget and the
    every-third-call effect 330 ([#1054](https://github.com/tonihele/OpenKeeper/pull/1054)).
15. **Sound distances ([#1098](https://github.com/tonihele/OpenKeeper/pull/1098)), Q678.**
    Fresh read: 2 mixer units per tile, minimum one tile, maximum from the
    sound group floored at 2.1 units, and a third member that is the constant
    0.5. What QMixer does with the third member is not established, so do not
    read it as the PR's rolloff without a test.
16. **No shipped use:** SET_FIGHT_FLAG ([#1104](https://github.com/tonihele/OpenKeeper/pull/1104)),
    TOGGLE_EFFECT_GENERATOR ([#1094](https://github.com/tonihele/OpenKeeper/pull/1094)).
    One-line arms; do them when convenient.
17. **Tests only:** tree-level tests for events 15, 13, 63 and 61; condition 64
    in a member tree naming another party; the four-per-tick imp cap and its
    dropped-imp bypass; a strapped-victim snapshot round trip.

## All 81 PRs

Source files are relative to `src/DK2.Simulation/` unless qualified.

### Script events

| PR | Finding for this project | Evidence / next action |
|---|---|---|
| [1001](https://github.com/tonihele/OpenKeeper/pull/1001) | Built: creature event 9 on conversion (ADR-0740) | Fresh read of [0x004F2800](functions/004F2800.md): receiver is the converted creature, after the owner change. Fired in `Systems/TortureSystem`'s conversion block. No shipped records. Neutral recruitment fires event 10 instead (also unwired). |
| [1003](https://github.com/tonihele/OpenKeeper/pull/1003) | Gap: slap events 5 and P60 | Fresh read of [0x0048B8A0](functions/0048B8A0.md): sites `0x0048BCB2` (main path) and `0x0048BD08` (states 209/255), P60 to the slapped creature's owner; the damage-only arm fires nothing. Fire after `CreatureHealthRules.Apply` in `HandSystem.Slap`. |
| [1005](https://github.com/tonihele/OpenKeeper/pull/1005) | Covered: stunned event 15 | `Combat/StatusEffectRules.Stun/StunInto`; [0x00491830](functions/00491830.md). No tree-level test. |
| [1008](https://github.com/tonihele/OpenKeeper/pull/1008) | Covered: imprisoned event 7, known divergence | `CategoryHooks` Prison enter; `PrisonImprisonedEventTests`. Q504 rim/full-prison divergence stands. |
| [1011](https://github.com/tonihele/OpenKeeper/pull/1011) | Gap: tortured event 8 | Fresh read of [0x00484110](functions/00484110.md), fire site `0x004842BE`. 88 shipped records. Built (ADR-0739) in `TortureRules.EnterTheDevice/Restrap/TryStrapSelf`. |
| [1013](https://github.com/tonihele/OpenKeeper/pull/1013) | Deliberate OpenKeeper deviation | Fresh reads of [0x00484110](functions/00484110.md) and [0x004F2180](functions/004F2180.md): the strap needs the device on the creature's own cell within one tile. OpenKeeper's move to a device elsewhere is not native. Ours matches. Room-drop state 76 (`0x004E8DA0`) not read. |
| [1015](https://github.com/tonihele/OpenKeeper/pull/1015) | Covered: attacked event 6, two known deviations | `CombatTargetRules.Engage`; `TriggerEventTests`; [0x0045A550](functions/0045A550.md). Ours fires only on a new engagement and not on a failed registration. |
| [1018](https://github.com/tonihele/OpenKeeper/pull/1018) | Gap: pick-up events P58 and 65 | [0x004BFBD0](functions/004BFBD0.md) fires both before any capacity test; fresh xref shows 17 call sites in 10 functions. Fire at the prepare step in `HandSystem.PickUp`, not after insertion. |
| [1020](https://github.com/tonihele/OpenKeeper/pull/1020) | Covered: sacked events 63 / P61 | `LeavingRules.PortalDrop` and `Sack`; [0x00459D50](functions/00459D50.md), [0x0045A0D0](functions/0045A0D0.md). No test reads the events. |
| [1029](https://github.com/tonihele/OpenKeeper/pull/1029) | Deliberate OpenKeeper deviation; exposed creature action 43 gap | Fresh read: hero and neutral trees dispatch to their own owner, and [0x004C4600](functions/004C4600.md) enqueues speech only for the local player. Ours already behaves so. See item 11 above for action 43. |
| [1030](https://github.com/tonihele/OpenKeeper/pull/1030) | Covered: condition 64 by the named party | `TriggerExecutor.PartyMembersMissing`; [dispatch tables](trigger-dispatch-tables.md). Code-only; no test for a member tree naming another party. |
| [1031](https://github.com/tonihele/OpenKeeper/pull/1031) | Covered: area condition 70 | `TriggerAreaConditions.PossessedCreatureExists`; Q280. |
| [1106](https://github.com/tonihele/OpenKeeper/pull/1106) | Covered for the reachable site | `StealGoldRules.AtTheGold` fires event 13 first. Fresh xref: four native sites; only state 165 is reachable with shipped data ([steal-gold.md](steal-gold.md) §7-8). No test. |
| [1110](https://github.com/tonihele/OpenKeeper/pull/1110) | Covered: P61 to the owner | `LeavingRules`, `TriggerExecutor.FirePlayerEvent`; [0x0045A0D0](functions/0045A0D0.md). |

### Script actions

| PR | Finding for this project | Evidence / next action |
|---|---|---|
| [1032](https://github.com/tonihele/OpenKeeper/pull/1032) | Gap: P46 producer | Fresh read of [0x004BE1A0](functions/004BE1A0.md): event at `0x004BE250` after the wage loop. Add `FirePlayerEvent` in `Systems/PaydaySystem.Clock`. |
| [1033](https://github.com/tonihele/OpenKeeper/pull/1033) | Covered: hero gate collapse | `TriggerExecutor.CollapseGate`, `HeroGatesOnRealDataTests`; [0x004E9130](functions/004E9130.md). Heart arm declined (Q743). |
| [1034](https://github.com/tonihele/OpenKeeper/pull/1034) | Gap: world action 2 | Fresh read of arm `0x00518E92`; Q755. Spawn at the zero-based tile centre with floor height. |
| [1035](https://github.com/tonihele/OpenKeeper/pull/1035) | Deliberate OpenKeeper deviation | Fresh read of arm `0x00518F41`: stores to local player `+0x19E`/`+0x1BE`; no reader draws it. Do not add UI. |
| [1036](https://github.com/tonihele/OpenKeeper/pull/1036) | Gap: creature action 35 | [Action 35](trigger-action-arms.md); fresh read of [0x00487C70](functions/00487C70.md) killed-event gate. Built (ADR-0744): byte 0 sets `DeathUncounted` and the creature is removed. |
| [1037](https://github.com/tonihele/OpenKeeper/pull/1037) | Covered: system messages toggle | `TriggerState.SetNotificationsEnabled`, `PlayerMessages`; [0x004C4600](functions/004C4600.md). |
| [1047](https://github.com/tonihele/OpenKeeper/pull/1047) | Covered: next room type | `TriggerState.SpecialInfoNextRoomTypeId`, `HudIndicators.NextRoom`, `HudIndicatorsTests`. |
| [1048](https://github.com/tonihele/OpenKeeper/pull/1048) | Covered; PR's selection rules are its own | `FinaleRules`, `FinaleSystem`, `ReaperFinaleOnRealDataTests`; [campaign-win-sequence.md](campaign-win-sequence.md). |
| [1052](https://github.com/tonihele/OpenKeeper/pull/1052) | OpenKeeper-only fix | Our `ActionPoints.FirstOrDefault` uses are null-checked. Code-only. |
| [1059](https://github.com/tonihele/OpenKeeper/pull/1059) | Gap in an unbuilt mode (My Pet Dungeon) | Partial fresh read of `0x00514320` agrees with the PR. Revisit when the MPD session model lands. |
| [1090](https://github.com/tonihele/OpenKeeper/pull/1090) | Gap: world action 49 | Fresh read of arm `0x005197F7`: one-based tile, direct [0x004E5650](functions/004E5650.md), no health reset. `RoomHealthRules.TransferOwnership` exists. |
| [1092](https://github.com/tonihele/OpenKeeper/pull/1092) | Gap: world action 51 and the countdown | Fresh read of arm `0x0051986C` and `0x004B9740`. `TriggerState` world bit 0x04 has no setter or reader. |
| [1094](https://github.com/tonihele/OpenKeeper/pull/1094) | Partial: emission covered, toggle missing | `EffectGeneratorSystem` (ADR-0648). Fresh read of arm `0x005197C8`: sets `+0x3C` from byte 1 == 1. No shipped use. SET_MUSIC_LEVEL already a success no-op in ours and native. |
| [1100](https://github.com/tonihele/OpenKeeper/pull/1100) | Covered: creature moods switch | `SetCreatureAngerEnabled`, `CreatureAngerRules.Apply`; [creature-anger.md](creature-anger.md). |
| [1102](https://github.com/tonihele/OpenKeeper/pull/1102) | Gap: creature action 33 | Fresh read of arm `0x0049AF3D`. 72 shipped records. Built (ADR-0739), mode only. |
| [1104](https://github.com/tonihele/OpenKeeper/pull/1104) | Gap: creature action 36, no shipped use | Fresh read of arm `0x0049AFD0`: only operand 1 enables `MayLookForAFight`. |

### Rooms, torture and prison

| PR | Finding for this project | Evidence / next action |
|---|---|---|
| [1017](https://github.com/tonihele/OpenKeeper/pull/1017) | Covered: devices, conversion messages and events 8 and 9 | `TortureRules`, `TortureSystem`, `TortureTests`. Messages 57/58 and event 9 built (ADR-0740, item 3 above); event 8 built (ADR-0739). Chair sparks and full clip unbuilt ([object-drawing.md §9](object-drawing.md)). OpenKeeper's torture sounds sit behind `0x00759ADC` natively (Q635). |
| [1040](https://github.com/tonihele/OpenKeeper/pull/1040) | Covered for wall furniture; gap in torch phase | `AutoFurnitureRules.WallSlots`; [auto-placed-furniture.md §4](auto-placed-furniture.md). `DK2.Presentation/TorchLayout` uses plain parity for rooms 12 and 15 ([wall-torches.md §2](wall-torches.md)). Lead: does the native Torture capacity count include the companion wheel? |
| [1041](https://github.com/tonihele/OpenKeeper/pull/1041) | Built: wall wheels and trough effects (ADR-0740) | Fresh read of [0x00499300](functions/00499300.md): effect 274 (coals) / 279 (water) once a game second while victim `+0x68` is in [0x3A0, 0x460) (meaning unread). `TortureSystem.TurnTheWheel`; the roll is `CreatureState.TortureRoll`. |
| [1042](https://github.com/tonihele/OpenKeeper/pull/1042) | OpenKeeper-only fix | Device occupancy is derived from creature state; `WorldSnapshot` saves the torture fields. No round-trip test. |
| [1043](https://github.com/tonihele/OpenKeeper/pull/1043) | Covered: own victims' mood | `TortureSystem.Act`, `TortureTests`; [state-category-hooks.md §3.3](state-category-hooks.md). |
| [1044](https://github.com/tonihele/OpenKeeper/pull/1044) | Built: information choice and reward 11 reveal (ADR-0740) | `TortureInformationRules`, `TortureInformationTests`. `RevealTo` and `FogOfWarSystem.TortureSightPass`. |
| [1045](https://github.com/tonihele/OpenKeeper/pull/1045) | Covered, more faithful than the PR | `JailBreakRules`, `JailBreakTests`; [jail-break.md](jail-break.md). OpenKeeper freeing all allies' prisoners is its own. |
| [1056](https://github.com/tonihele/OpenKeeper/pull/1056) | Deliberate OpenKeeper deviation | Fresh read: room 22 in `Rooms.kwd` has art `Torch1` with zero light and height; the def constructor zeroes `+0x456..+0x45C`. [wall-torches.md](wall-torches.md). |
| [1058](https://github.com/tonihele/OpenKeeper/pull/1058) | Covered: Mistresses torture themselves | `TorturerStep`, `TortureRules.TryStrapSelf`, `TorturerTests`; Q419, Q426. No long-run hunger/pay test. |
| [1060](https://github.com/tonihele/OpenKeeper/pull/1060) | Covered: Casino lever | `CasinoRules`, `Commands/CasinoCommands`, `CasinoTests`; Q423, ADR-0188. |
| [1067](https://github.com/tonihele/OpenKeeper/pull/1067) | Covered: room border flames | `Systems/RoomBorderFlameSystem`, `RoomBorderFlameTests`; [room-border-effects.md](room-border-effects.md), ADR-0524. |
| [1068](https://github.com/tonihele/OpenKeeper/pull/1068) | Covered: claim smoke | `RoomHealthRules.TransferOwnership` effect 8; `ClaimAndBedEffectTests`; [room-construction-effects.md §6](room-construction-effects.md). |
| [1070](https://github.com/tonihele/OpenKeeper/pull/1070) | Covered: capture speech | `RoomHealthRules.PostTransfer`, `MessageTabsView`, `RoomOwnershipMessageTests`. |
| [1071](https://github.com/tonihele/OpenKeeper/pull/1071) | Covered: SPECIAL_TILES floor pass | `AutoFurnitureRules`; [auto-placed-furniture.md §2](auto-placed-furniture.md). |
| [1108](https://github.com/tonihele/OpenKeeper/pull/1108) | OpenKeeper-only fix | jME deformation and ceiling mesh. Pillar rules already match [furniture-placement.md §5](furniture-placement.md). |

### Creatures, imps, spells and sound

| PR | Finding for this project | Evidence / next action |
|---|---|---|
| [1021](https://github.com/tonihele/OpenKeeper/pull/1021) | Unchanged since [its own review](openkeeper-1021-review.md) | Head is still `5367c0bf`. No dedicated test for the four-per-tick cap. |
| [1022](https://github.com/tonihele/OpenKeeper/pull/1022) | Covered: dropped imp skips the cap | `ImpTaskSystem.Work`, `JustDropped`; [0x00463DF0](functions/00463DF0.md). Ours scans rings 1-3 (ADR-0292), not the PR's 2 tiles. |
| [1023](https://github.com/tonihele/OpenKeeper/pull/1023) | Covered: nearer imp takes a body carry | ADR-0742; `ImpTaskSystem.Carrying.TryTakeBody`; [0x0046D810](functions/0046D810.md), [0x0046D3C0](functions/0046D3C0.md); kind-12 and kind-13 rows in [imp-task-stealing.md](imp-task-stealing.md) §2. |
| [1026](https://github.com/tonihele/OpenKeeper/pull/1026) | OpenKeeper-only fix | jME entity-set queue. Code-only. |
| [1046](https://github.com/tonihele/OpenKeeper/pull/1046) | Covered, ours goes further | `ResearchRules.SlapBonus`, `ResearchOnRealDataTests`; [library-and-research.md](library-and-research.md) §3-4, Q216. |
| [1054](https://github.com/tonihele/OpenKeeper/pull/1054) | Simulation covered; HUD and effect missing | `CallToArmsRules`, `CallToArmsSystem`, `CallToArmsTests`; [call-to-arms.md](call-to-arms.md). Fresh reads: cancel is game action 0x81 (`0x005142E0`), widget `0x00426920`, effect 330 every third call in `0x0049FCB0`. |
| [1057](https://github.com/tonihele/OpenKeeper/pull/1057) | Covered, ours more faithful | `JobRules.ChargesNoWorkAnger`; [0x004780E0](functions/004780E0.md). |
| [1062](https://github.com/tonihele/OpenKeeper/pull/1062) | Covered | `HandCursorFeedback`, `WorldView.PreviewTags`; [sound-effects.md](sound-effects.md), [tag-highlight.md](tag-highlight.md). |
| [1066](https://github.com/tonihele/OpenKeeper/pull/1066) | OpenKeeper debug extension | [cheats.md](cheats.md), ADR-0589. |
| [1075](https://github.com/tonihele/OpenKeeper/pull/1075) | Gap of ours | Built (ADR-0744): a held object is off DK2's tile list (`0x004B4F60`), which `0x0046B9C0` walks; `IsLoose` and `ObjectOn` ignore `InHand`. |
| [1095](https://github.com/tonihele/OpenKeeper/pull/1095) | Covered: manufacture rate | `ManufactureRules.PerOriginalTick`, `ManufactureTests`; [workshop-states.md](workshop-states.md). |
| [1098](https://github.com/tonihele/OpenKeeper/pull/1098) | Gap, already Q678 | `ClassicSoundPlayer` uses Unity's default rolloff. Fresh read of the QMixer mapping ([0x0060CDB0](functions/0060CDB0.md)); third member is the constant 0.5, its use unread. |

### Engine, rendering and tooling

[1012](https://github.com/tonihele/OpenKeeper/pull/1012),
[1024](https://github.com/tonihele/OpenKeeper/pull/1024),
[1025](https://github.com/tonihele/OpenKeeper/pull/1025),
[1027](https://github.com/tonihele/OpenKeeper/pull/1027),
[1028](https://github.com/tonihele/OpenKeeper/pull/1028),
[1050](https://github.com/tonihele/OpenKeeper/pull/1050),
[1053](https://github.com/tonihele/OpenKeeper/pull/1053),
[1065](https://github.com/tonihele/OpenKeeper/pull/1065),
[1074](https://github.com/tonihele/OpenKeeper/pull/1074),
[1076](https://github.com/tonihele/OpenKeeper/pull/1076)-[1086](https://github.com/tonihele/OpenKeeper/pull/1086),
[1087](https://github.com/tonihele/OpenKeeper/pull/1087) (closed),
[1088](https://github.com/tonihele/OpenKeeper/pull/1088),
[1096](https://github.com/tonihele/OpenKeeper/pull/1096) and
[1109](https://github.com/tonihele/OpenKeeper/pull/1109) are jME rendering,
lighting, ceiling-mesh, minimap, Nifty UI, CI, compile and networking
changes. None cites a DKII.EXE address or states a DK2 rule; no action.

## Limits

As in the previous audit, OpenKeeper's event patches decrement finite repeat
counts and deliver through a queue. The native event filter neither decrements
nor queues ([trigger-walkers.md](trigger-walkers.md)), and ours follows it.
Agreement on an event's name does not establish the same execution rules.

Shipped-record counts come from `dk2tools kwd trigger-types` on the install.
The new native facts above are recorded in the linked function records with
their confidence. No replay outcome, simulation hash or gameplay acceptance
changed.
