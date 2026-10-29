# Special-role idle: confirmed reads and engine boundary

Read 2026-10-04 from DKII.EXE using per-run Ghidra clones. Function facts
are canonical in the linked records below; raw output remains quarantined.

## Implementation boundary

The Tremor Fleeing idle integration mirrors role selection in
[004773D0](functions/004773D0.md): neutral state5 with deadline now+12,
Horny state238, then an end to that handler turn. These destinations have
explicit UnbuiltHandler activity, which prevents ordinary keeper selection.
They remain ResearchOnly. The neutral inner routine and Horny idle controllers
have been read but remain unbuilt. Arrival235/236/237 and departure242 are
separately built; see `keeper-spell-reaper.md` for that lifecycle boundary.

The known dependencies include actual action availability and animation
metadata, the unusual wander search and original room/slab linked order,
Chicken process fields, physical combat flags, and forced Pillar object
identity/combat. They are not evidence that the controller bodies are unread.
Transitive room-point/map predicate helpers, full forced-object combat, loaded
art metadata for the additional slots, and render/bulk field consumers were
outside this focused audit. Casino collection-position aliases in state156
also remain unchecked in assembly. The records distinguish these limits.

## Horny's targets, and his own creatures (read 2026-10-29)

The owner remembers that Horny cannot flee and that, finding no enemies, he
turns on his allies (Q673). Read so far, the binary agrees with the first and
does not yet show the second.

- **He never flees.** STRONGLY INFERRED: the shipped record carries `IsFearless`
  and fear's flee test exits on it (`combat-fear.md`); ours too (`FearRules`). Other
  ways into a flee state were not traced. ADR-0574's fleeing-Horny arm in
  `DispatchIdle` is not expected to run.
- **The target he chooses is never his own keeper's or an ally's.** CONFIRMED.
  State 238 ([004748E0](functions/004748E0.md)) and `SelectAVictim` with his forced
  bit (`+0x3E6` bit 2) clear (`0x0045CE8D`, `0x0045D078`) call
  [0045E1B0](functions/0045E1B0.md): pass 1 skips every player allied with his owner
  (a player is allied with itself, `0x004BF3D0`) and takes the nearest attackable,
  routable creature other than himself; pass 2, from 238 only and only when pass 1
  found none, takes an enemy Heart's Arch Pillar by its own metric (dx² + 2·dy, in
  tiles). With neither, 238 sets 242 and he departs. In `SelectAVictim` this call
  stands in for the gate `0x0045A6D0`; `ProcessFight` still applies that gate first
  (`0x0045EA18`) while the forced bit is clear.
- **With nothing found there, he falls into the ordinary scans.** CONFIRMED control
  flow (`0x0045D069`-`0x0045D08C`, no bail-out for Horny): scans B, C and D, the
  fight-type arm and F (`combat-acquisition.md` §6). Whether any of them can pick his
  own side was not read; this is the path a "no enemies" Horny takes.
- **Scan A takes any side, but is rarely reached.** Scan A (`0x0045CF0B`-`0x0045D067`)
  walks every player's creatures with no owner or alliance test (CONFIRMED), and a
  dead forced id falls into it (`0x0045CEBC`, `0x0045CF02`). But the fight handler
  clears a dead engaged target's forced id before `SelectAVictim`
  (`0x0045F0A7`-`0x0045F0C6`), death bookkeeping clears it too (`0x0045E4A0`), and his
  own kill sends him to 239 (`0x004992A1`-`0x004992C4`). A target killed by someone
  else before he engages it is the one window seen. HYPOTHESIS that scan A is what
  the owner remembers.
- **His blow on his own keeper's creature kills it outright.** CONFIRMED control
  flow in the melee swing ([00498E30](functions/00498E30.md), `0x00499077`-`0x004990C3`),
  for a creature target (`+0x12` = 0, `0x00498F2A`) with `+0x445` zero (`0x0049904B`):
  same owner (`+0x28`) as Horny → his forced target cleared, `SetState(242)` on him,
  the target's `DeadBodyOnDestroy` (`+0x3E6` bit 5) cleared and its health zeroed
  through slot `+0x20` ([004B2AD0](functions/004B2AD0.md)), before any damage. So it
  dies with no body; whether 242 is accepted from his fight state (and he departs)
  is PLAUSIBLE, unchecked. An ally's creature (another owner) takes the ordinary blow.
- After any blow, a target whose death test (slot `+0x28`) is true sends him to 239.
  The health routine `0x004B2CE0` refuses him negative deltas ([0047A9B0](functions/0047A9B0.md)).

None of this is built: ours has no Horny 238-241 controller, no forced-target
reselection and no same-owner branch in the blow.

## Census scope

Direct bit/offset searches covered initialized .text and cseg with the
catalogued BitReaders, ReadsAt and StoreAt queries. Known setters/resetters
served as controls. Offsets0x9F/0x5B also match unrelated receiver layouts;
the focused alpha writer reads classified several such false positives.
No independent direct gameplay consumer of render-block+0x5B bit0 was
established in this scope. Setter coupling with alpha supports a visual-fade
interpretation, not a claim of global harmlessness or absence. Bulk copies,
indirect renderer accesses and serialization were not exhaustively censused.
Raw census captures: `reverse-engineering/.cache/tremor9f1.txt`,
`tremor5b1.txt`, `tremor9f5b-reads.txt`, `tremor-fade-stores.txt`,
`tremor-alpha-reads.txt`, `tremor46f1.txt`, and `tremor46f8.txt`.

## Function records

- [004773D0](functions/004773D0.md): Creature_IdleHandler.
- [004778E0](functions/004778E0.md): Creature_IdleChooser89.
- [00477660](functions/00477660.md): Creature_IdleDeadline2.
- [00477680](functions/00477680.md): Creature_DoingNothingWrapper.
- [004776B0](functions/004776B0.md): Creature_NeutralIdle5.
- [004776C0](functions/004776C0.md): Creature_IdleInner.
- [004748E0](functions/004748E0.md): Creature_HornyStart238.
- [00474960](functions/00474960.md): Creature_HornyWander239.
- [00474AF0](functions/00474AF0.md): Creature_HornyChicken240.
- [004B4C20](functions/004B4C20.md): Stub_ReturnTrue.
- [00474CC0](functions/00474CC0.md): Creature_HornyFade242.
- [0047A9B0](functions/0047A9B0.md): Creature_HornyCategoryUpdate.
- [0048DF10](functions/0048DF10.md): Creature_SetFlag46FBit0.
- [0048DFA0](functions/0048DFA0.md): Creature_SetFlag46FBit3.
- [0045A6D0](functions/0045A6D0.md): Combat_MayLookForFight.
- [0045A8B0](functions/0045A8B0.md): Creature_UpdateAttackable.
- [0045CB00](functions/0045CB00.md): Combat_SelectAVictim.
- [0045F030](functions/0045F030.md): Combat_SpFighting.
- [0045E1B0](functions/0045E1B0.md): Combat_ReaperForcedTarget.
- [00453840](functions/00453840.md): Cell_FindNearbyGroundObject.
- [0049CE70](functions/0049CE70.md): Object_SetHornyInteraction.
- [004966C0](functions/004966C0.md): CreatureAction_IsAvailable.
- [004950C0](functions/004950C0.md): CreatureAction_FillTable.
- [00494880](functions/00494880.md): CreatureDef_ResolveArtSlot.
- [00496700](functions/00496700.md): CreatureAction_Begin.
- [00498260](functions/00498260.md): CreatureAction_KillObject45.
- [0049E600](functions/0049E600.md): ObjectProcessTable_Fill.
- [004A24A0](functions/004A24A0.md): Object_ChickenProcess8.
- [0049CEC0](functions/0049CEC0.md): Object_TryAvoidNearbyCreature.
- [00480A00](functions/00480A00.md): Creature_FindWanderPoint.
- [004B1DF0](functions/004B1DF0.md): Thing_InitMovementFields.
- [004B28D0](functions/004B28D0.md): Thing_InitClass.
- [004B4120](functions/004B4120.md): RenderBlock_ClassInitialize.
- [004B5E10](functions/004B5E10.md): Thing_ConstructBase.
- [004B5A10](functions/004B5A10.md): ThingPool_Construct.
- [004B5B80](functions/004B5B80.md): Thing_BaseCleanup.
- [00635EC0](functions/00635EC0.md): Array_ConstructWithCleanup.
- [00635E50](functions/00635E50.md): Array_CleanupReverse.
- [004B3610](functions/004B3610.md): RenderInfo_Reset.
- [004B37D0](functions/004B37D0.md): Animation_InstallResource.
- [0048E160](functions/0048E160.md): Creature_UpdateTimedEffects.
- [004B2AD0](functions/004B2AD0.md): Thing_ZeroHealth.
- [00473F80](functions/00473F80.md): Creature_State228Fade.
- [004D9A60](functions/004D9A60.md): DungeonHeart_UpdateEffects.
- [004DB700](functions/004DB700.md): DungeonHeart_Build.
- [004E6BC0](functions/004E6BC0.md): Room_DeleteAutoPlacedObjects.
- [004F8360](functions/004F8360.md): FieldInitializer_004F8360.
- [005012A0](functions/005012A0.md): SpatialRoutine_005012A0.
- [00461980](functions/00461980.md): Creature_StartFollow246.
- [00457010](functions/00457010.md): Creature_CasinoWinner155.
- [004574A0](functions/004574A0.md): Creature_CasinoWinner156.
- [00457560](functions/00457560.md): Creature_CasinoWinner157.

## Capture provenance

Record-specific capture references are local ignored filenames, not artifacts
required to build the engine. Main whole-body groups are
`tremor-neutral-horny.json`, `tremor-idle-dependencies.json`,
`tremor-horny-cycle.json`, `tremor-horny-helpers.json`,
`tremor-horny-action45.json`, `tremor-horny-remaining.json`,
`tremor-core-final.json` and `tremor-fade-writers-focused.json`.
Missing Ghidra function entries for240/242 were read using forced-entry output
`tremor-horny-missing.txt` and assembly `tremor-horny-asm.txt`.
Critical wander/Heart iteration assembly is in`tremor-wander-asm-final.json`;
forced Heart distance assembly is in`tremor-forced-distance.json`.
Chicken process8 assembly is in`tremor-chicken8-asm.txt`.
No raw listing or decompiled body is reproduced in tracked documentation.
