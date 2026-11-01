# Dungeon Heart destruction

## Native trigger (CONFIRMED, 2026-11-01)

The destruction visual is an object death effect, independent of the Heart room's
opening and ruin clocks. `0x0049B1C0` enables object runtime `+0x146` bit 0.
In `0x0049BD20`, `0x0049BDA6..0x0049BDE5` requests definition word `+0x119`
when health is nonpositive, that bit is set and the effect id is nonzero. The
request carries the object's position, heading and owner. `0x0049DD60` copies
expanded-reader `+0x3D6` (file `+0x356`) to definition `+0x119`.

The shipped Heart definition (object 13) has death effect **76**. Its ordinary
floor resource remains `Dungeon centre`; no separate object art slot needs to
be selected. The builder `0x004DB700` creates the Heart at slab-chain index 12,
tile centre, requested Z0 and heading 0. Its centre entry in the position table
(`0x004EFBE0`) writes piece 0x40; `0x004545C0` at `0x00454626..0x0045462C`
returns floor 0 there. Linkage through `0x004B4E70` and `0x004B5020` therefore
preserves physical Z0, which the death request forwards unchanged. The ordinary
front-end prop floor 0x1000 does not establish the central Heart's height.
The effect floor callback must also retain this pit rather than lift the request.

The world thing sweep `0x004B7830` updates creatures and shots before objects,
then removes dead things in a second pass. The object's cleanup slot
`0x0049BEC0` delegates to `0x004B7B10`. Before destruction, `0x0049B5E0`
removes the Heart's three type 94 pillars and runs `0x004DA560`, which performs
keeper bookkeeping. The immediate cleanup
does not delete type 86 Arches, type 88 Steps or type 100 Obstacle.

The later room ruin is separate: `0x004D9A60` waits until after its opening
interval before noticing the missing Heart. `0x004E9130` case 5 sets the ruin
bit and refreshes tiles. Only its case 20 deletes room objects directly.
Downstream tile-event effects on surviving Heart decorations remain unverified.

## Authored effect graph (CONFIRMED shipped data)

Effect 76, `Heart Explosion Effect`, generates effects 74, 68 and 299. Effect 68
generates 75; effect 299 also starts next-effect 300. Their elements include sparks,
rock chips, fireballs, explosions and smoke. Effect 300 uses group resource
`Group_dharch` and element 512 for Heart mesh debris. The existing group-particle
renderer supplies each group's mesh, offset and motion for this authored chain.

## Level 7 and replenishment

The shipped map gives keeper 4 Gormagon a 1% Heart at (22,13), three nearby Giants
and no initial creatures. Keeper 3 starts with four Imps and a full Heart at
(5,15). Keeper 4 remains an active player. The supply mismatch and exact native
deadline are documented in [imps-and-mana.md](imps-and-mana.md); a weak-Heart or
Level 7-specific exclusion would be unsupported.

## Reference cross-check

OpenKeeper revision `f45c0219be5af51c6694a28a32e52568e17cfa0d` was consulted
for facts only. Its object reader corroborates the adjacent slap/death/idle
effect words. The inspected Heart construction code has commented decay-art
references; the native object-update trigger above establishes the implemented
destruction path independently. See THIRD-PARTY-NOTICES.md.

## Implementation and validation (ADR-0620, hash/save 363)

| Branch | Previous remake | Corrected remake |
|---|---|---|
| Object definition/update, `0x0049DD60` / `0x0049BD20` | Death word omitted; no Heart death request | Reader/writer preserve DeathEffectId; the lethal turn requests the authored effect once, including during the opening and with killer 0 |
| Physical floor, `0x004545C0` / `0x004EFBE0` | Effect floor callback lifted the Heart pit to ordinary height 4096 | Death forwards physical Z0; the callback preserves centre 0 and rim 4096 before and after ruin |
| Immediate cleanup, `0x0049B5E0` | Heart and pillars stayed until room ruin | Keeper Heart 13/pillars 94 leave the view at death; nonkeeper default health is not treated as a death observation |
| Supply initialization/opening, `0x004B8640` / `0x004D9ECD` | Zero deadline; opening write omitted | Disabled deadline and native opening arming; see [Imp supply](imps-and-mana.md) |
| Supply comparison/update, `0x004BB497` / `0x004BB57E` | Modern-tick/non-strict timing | Strict native-turn deadline with 32-bit wrap |
| Player walk, `0x004C2310` | Sorted player-slot order | Active-player order |

The effect consumes the existing presentation RNG stream. Healthy-Heart free-cast
placement still differs from the native ring (Q716); later decoration removal
remains Q715. No native pixel or destruction-timing comparison is established.

Regression coverage includes `DungeonHeartTests.ObjectUpdate0049BD20RequestsTheAuthoredHeartDeathEffectBeforeTheOpeningEnds`
and its nonlethal/initial-ruin controls, `WorldViewTests.HeartDeath0049B5E0RemovesTheHeartAndPillarsBeforeTheRoomRuins`,
the nonkeeper model-boundary control, `ClaimAndBedEffectTests.HeartPit004545C0KeepsTheEffectFloorAtZeroBeforeAndAfterRuin`,
and `EffectChainTests.HeartDeath0049BD20RunsTheShippedExplosionGraphAndArchDebrisToCompletion`.
`ImpSupplyTests` covers opening, strict deadline, active order, initial ruin and
`FreeCast004BB57EWrapsTheStoredNativeDeadlineToThirtyTwoBits`; the two actual-map
`HeartStartOnRealDataTests.LevelSevenGiantsDestroyTheRivalsHeartWithoutAnOpeningFreeImp`
and `LevelSevenLiveRivalsFreeSupplyArmsAfterTheHeartOpening` cover the doomed and
live-Heart controls.
The data overlay regression checks the newly decoded/written death word.

The broad functional run preceded final identifier allocation and the Z0 floor
correction. At provisional hash/save 362 it recorded 7,486 passed, 2 failed, 30 skipped
(7,518 total): Simulation 4,250/0, Presentation 1,407/0 and Data 502/0. Its failures
were the pre-existing Level 18 grant-count assertion (expected 39/13, actual 36/12,
identical on master) and a two-job watchdog timing assertion; the watchdog passed
isolated on both branch and master. The former Q520 data/behavior failures did
not reproduce against this install.

After the corrected Z0 change at hash/save 363, the Release build had zero
warnings/errors; all 4,250 Simulation tests and 11 HeartStart/Journal/Snapshot
Behavior tests passed, including the pin and actual Level 7 request height.
Two complete Presentation attempts each had 1,408 passed/1 allocation failure:
`ClassicObjectLightTests.WarmDynamicPublicationAndGatherAllocateNothing` first,
then `BottomPanelViewTests.CaptureIsReadOnlyAndAllocatesNothingAfterConstruction`.
The light control passed isolated on both branch and master; the BottomPanel
control failed with the same 88-byte allocation on both. Excluding only these
two performance controls, all 1,407 functional Presentation cases passed,
including the two new pit-floor cases.

The final corrected Z0 Play Mode probe uses the real Level 7 flyby: the rival
Heart dies at modern tick 13 during the path, requests effect 76 once at raw Z0
and receives zero free Imps. It records 20 original debris meshes, peak 336
explosion sprites, 7,265 changed pixels in the frozen geometry/control pair,
60 PNGs and expiry within 20 seconds after death. The control removes particle
geometry collectively; effect lights remain, so it does not isolate each
component's pixel contribution. The images show the explosion and pale
original debris. This checks remake integration, not native pixel/timing parity.

Artifact index: `F:/DK2Evidence/studies/level7-heart-destruction--2026-11-01--002/README.md`.
The earlier 001 artifact is a superseded Z1 diagnostic retained without overwriting.
The repeatable probe is documented in [the probe README](../../tools/classic-unity-probe/README.md#heart-destruction).
