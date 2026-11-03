# Sound effects: objects, doors, traps and the boulder

Sources: DKII.EXE v1.70, read through Ghidra on 2026-11-02; shipped `Data/Sound/Sfx/Global/*SFX.map` and
`Data/editor/{Objects,Doors,Traps}.kwd`. Plan Phase W5 of
[world-sounds](../plans/2026-10-31-world-sounds.md). Conventions as in
[sound-effects.md section 6](sound-effects.md#6-the-hand-and-its-cursor): a request is one slot `+0x20` call of the
sound manager (handle, category, event, position); a row is silent when no `SFX.map` holds the group with samples.
Sections 1 to 3 are objects, doors and traps, the boulder.

## 1. Objects and specials

An object's category is its definition's `+0x10E` (ours `ObjectDef.SoundCategory`); its handles are `+0x2E` and
`+0x32`, its position `+0x1A`. Samples are those in the category's `SFX.map`.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0xF1` (`+0x2E`), `0xF2` (`+0x32`) | [0x0049B1C0](functions/0049B1C0.md) at `0x0049B4C8`, `0x0049B4ED`: the last thing before the type switch, for every object, whoever makes it | CONFIRMED | `ObjectSoundRules.Made`, called by `WorldState.SpawnObject` |
| `0xF4` (`+0x2E`) | [0x0049B5E0](functions/0049B5E0.md) at `0x0049B691`: at the head of the deletion, only while world slot `+0x4C` (the leave flag) is zero and world `+0x7239` bit `0x80` (a saved game being restored) is clear | CONFIRMED | `ObjectSoundRules.Removing`, called by `WorldState.RemoveObject` |
| `0xF1` again | [0x0048AD50](functions/0048AD50.md) at `0x0048AE27`: a bed, after its creation effect was made, with the handle the first `0xF1` returned | CONFIRMED site | not asked: a live handle retains an equal-priority request (STRONGLY INFERRED, [sound-playback.md](sound-playback.md)) |
| `0xF6` | `0x0049F03E`, an object landing | CONFIRMED site; silent | nothing |
| `0x110` | [0x0049FA50](functions/0049FA50.md) at `0x0049FA95`: a chicken (definition `+0x113` = 9) on each call where its step count `+0x170` is 1 (test at `0x0049FA66`, before the turn and the decrement, so the last call of a step and every call while it turns), only when bit 0 of the global counter `0x006E4FD4` is set; the counter then counts up, asked or not. The counter has two references (the test at `0x0049FA70`, the increment at `0x0049FA9B`), feeds nothing else and is never reset | CONFIRMED | `ObjectMovementRules.StepMove` offers an `Alternating` request on every such call; `WorldSoundDirector` keeps the counter (test bit 0, then count), so the simulation holds no state |
| `0x111` | [0x00514830](functions/00514830.md) at `0x00514BEA`, after `0xCD` at `0x00514BC2` (the creature's handle and category): a possessed creature that owes food, or a Bile Demon, eating a chicken, after the heal and before the bird is deleted | CONFIRMED | `PossessionRules.EatChicken` |
| `0x112` | [0x00498D20](functions/00498D20.md) at `0x00498E08`: the Anvil (object 15) on the tile of a manufacturer, once Gate A has passed and its category is the Workshop's, on every call while action 32 runs | CONFIRMED | `WorkshopStep` (sound-effects-continuous.md section 2) |
| `0x311` | [0x004A13F0](functions/004A13F0.md) at `0x004A14F2`: age 1 of the struck state, after the effect and the art, for the Training Dummy (id 14) only | CONFIRMED | `ObjectReactionRules.Step` |
| `0xFB`, `0x31B` | the handler of actions 36 to 38 at `0x00499300` ([record](functions/00499300.md)): torture-device sounds at `0x00499404`, `0x004994D7`, `0x00499593`, `0x00499634`, each only while global `0x00759ADC` is 1 | CONFIRMED sites, handler read in part | waits: no mirror of the handler |
| `0x10E` | the Library's Spell Book placement `0x004E0A50` (the new book, object 4, category `LIBRARY_BOOK`) (site `0x004E0B36`) and three like requests in the same code, at `0x004E0CC6`, `0x004E0E56`, `0x004E1008` | CONFIRMED sites; silent | nothing |
| `0x80`, `0x8B`, `0x8F` | the arms for specials 117, 124 and 129: [0x004A42C0](functions/004A42C0.md), [0x004A4980](functions/004A4980.md), [0x004A4F70](functions/004A4F70.md), their last act before returning 1, with no gate | CONFIRMED | `SpecialSystem` |
| `0x84`, `0x85`, `0x86`, `0x88`, `0x89`, `0x8A`, `0x8C`, `0x8D`, `0x8E` | the arms of the other specials ([0x004A3FC0](functions/004A3FC0.md) holds 118 and 122 inline; the rest from [specials.md](specials.md#5-what-every-arm-also-does)) | CONFIRMED for 118 and 122, the rest read earlier | waits: those arms are unbuilt (`special_unimplemented`) |
| `0x310` | the Heal All arm | silent | nothing |
| `0x2EA`, `0x33F` | [0x004DF6E0](functions/004DF6E0.md) at `0x004DF7F0`, `0x004DF860`: the hero gate room's tick, `0x2EA` at its age 1 for its object of id 131, `0x33F` at one in 24 turns for each of eight slots | CONFIRMED sites; `0x33F` silent | waits: that tick handler is unbuilt |

`0xF1` has 7 samples in `OBJECT_CHICKEN`, one in `OBJECT_BED`; `0xF2` 6 in `OBJECT_BAT`, 2 in `OBJECT_RAT`; `0xF4` 9 in
`OBJECT_CHICKEN`, one in `TRAP_BOULDER` (object 74's category); `0x110` 10 and `0x111` 11 in `OBJECT_CHICKEN`; `0x112`
3 in `OBJECT_FORGE`; `0x311` 2 in `OBJECT_TRAINING_DUMMY`; `0xFB` one each in `OBJECT_WHEEL` and `OBJECT_ELECTRIC_CHAIR`,
`0x31B` one in the wheel; `0x80`, `0x8B`, `0x8F` one each in `OBJECT_SPECIAL`; `0x2EA` one in `OBJECT_FE_WINDOW`. No shipped
object names `OBJECT_TORCH`, so a wall torch is silent. No object category has `0xF6`, `0x10E`, `0x310` or `0x33F`, and
no door or trap category has the groups the rest of the plan's rows ask for (section 2).

The plan's `0x110` row was wrong in two of its three sites: `0x00474BA3` and `0x00514BEA` ask `0x111`. The simulation asks
`F1`, `F2` and `F4` for every object, as DK2 does, and leaves silence to playback: of the shipped object categories only
`OBJECT_BED` and `OBJECT_CHICKEN` hold `F1`, `OBJECT_BAT` and `OBJECT_RAT` `F2`, and `OBJECT_CHICKEN` and `TRAP_BOULDER` `F4`
(read 2026-11-02, the sound maps of every category).

Deviations. DK2 makes the creation request inside the constructor, with the position it was given; ours calls it when
`SpawnObject` returns, so a position a creator sets afterwards (a hatched chicken takes its egg's, a boulder its
shot's) is not yet set and the tile's centre stands in. Level load builds its objects before the simulation holds the world,
so they are silent, as are the hatchery initialiser's and a restored snapshot's; DK2's sound manager would hear them. A deleted object asks `0xF4` wherever ours deletes it, including
the chicken a creature eats, which asks `0x111` first (`Eat_EatChicken`). The possessed creature's `0xCD` is listed
under plan W7.4; it is built here because it is the first of the two requests of that one arm.

## 2. Doors and traps

A door's category is its definition's `+0xA9` (record offset `0x248`, [0x004A7100](functions/004A7100.md)), a trap's `+0xB0`
(record offset `0x20F`, [0x004AA440](functions/004AA440.md)); ours `DoorDef.SoundCategory` and `TrapDef.SoundCategory`, read
and written by the KWD readers (`DoorAndTrapSoundCategoriesAndTheDoorImpactByteAreTheShippedOnes`). The plan named the
thing's `+0xF7`, which is its definition pointer. Handles are `+0x2E` and `+0x32`, the position `+0x1A`.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| door `0xE7` open, `0xE8` shut | [0x004A55F0](functions/004A55F0.md) at `0x004A60B9`: the open state `+0x10D` changed this update; not for a door that is dead, under construction or a barricade | CONFIRMED | `DoorSystem.Execute` |
| door `0xE9` lock, `0xEA` unlock | [0x004A6800](functions/004A6800.md) at `0x004A6844`: a completed door's mode set, before it is stored; from the lock action `0x00513800`, the finale Reaper's walk ([0x00474F80](functions/00474F80.md)) and the level loader | CONFIRMED | `DoorSystem.Lock`, `FinaleRules.Walk`; the loader's is silent in ours |
| door `0xF2` (`+0x32`), `0xF4`, `0xF4` and `0xF5` of a sale | [0x004A5070](functions/004A5070.md) at `0x004A5275`, [0x004A5290](functions/004A5290.md) at `0x004A52D0`, [0x004C3670](functions/004C3670.md) | CONFIRMED sites; silent: no door category holds them | nothing |
| door `0xEB` | the Magic Door's own shot, `0x004A642B`, in the category of the trap definition the door names | CONFIRMED site | waits: ours does not fire a Magic Door's shot |
| trap `0xEB` | [0x004A80E0](functions/004A80E0.md) at `0x004A82C2`, `0x004A830E`: after the shot is made. Every shot but the Alarm's (id 13) asks it once; the Alarm's asks it for a creature target of the local player, and again when the trap is the local player's | CONFIRMED | `TrapRules.RequestFireSound` (the Alarm's two as `ViewerOnlyFor` the target's owner and the trap's) |
| trap `0xF4` | [0x004A7B20](functions/004A7B20.md) at `0x004A7B48`: first, for every removed trap, built or not | CONFIRMED | `TrapRules.Remove` |
| trap `0xF4`, `0xF5` of a sale | [0x004C3A20](functions/004C3A20.md) at `0x004C3A99`, `0x004C3B2A` | CONFIRMED sites | waits: ours has no trap sale; no trap category holds `0xF5` |
| trap `0xF2` | [0x004A7940](functions/004A7940.md) at `0x004A7AF1` | CONFIRMED site; silent | nothing |
| `0xED`, `0xF3` | `0x004A8C1F`, `0x004C360D`, `0x004C39C9` | silent: no `SFX.map` holds either | nothing |

`0xE7`, `0xE8`, `0xE9` and `0xEA` have one sample each in `DOOR_WOOD`, `DOOR_STEEL`, `DOOR_BRACED`, `DOOR_MAGIC`, `DOOR_SECRET` and
`DOOR_BARRICADE` (the Hero Barricade shares the last, the Hero Portcullis uses `DOOR_STEEL`); `0xEB` one to five in every trap
category but `TRAP_GUARD_POST`; `0xF4` one each in `TRAP_ALARM` and `TRAP_BOULDER`. The Boulder trap is destroyed when its
shot makes the boulder, so it sounds `0xEB` then `0xF4`; a Sentry and a Hero Sentry share `TRAP_SENTRY`, an Alarm and a Hero
Alarm `TRAP_ALARM`. A barricade never reaches the open decision, so it asks no open or shut sound.

Deviations. A door and a trap keep no stored position in ours: the request is at the middle of the tile at the floor's height.
Ours keeps a lock mode for a door still under construction (the lock command writes it), which DK2 refuses; the sound follows DK2
and is not asked then. The level loader's `Door_SetLockMode` (`0x00521130`), which sounds `0xE9` for a door that ships locked,
is not asked: level-load spawns are silent. A door DK2 finds dead skips the open decision after its own owner and support kills,
which ours run in `DoorHealthSystem` after `DoorSystem`; a door killed this tick may still sound this tick and goes the same turn. The Alarm's local-player tests are asked of whichever keeper's screen plays them, so a second keeper's view
would hear its own; DK2's world slot `+0x264` is the viewer.

## 3. The boulder

Object 74's category is `TRAP_BOULDER` ([boulder.md section 9a](boulder.md#9a-what-a-wall-hit-shows-and-plays-read-2026-10-31-q520)
had read 335, 329 and 244 in decimal: `0x14F`, `0x149`, `0xF4`). Every request carries the boulder's handle and its position
at the time. The launch asks none ([0x004AEBD0](functions/004AEBD0.md)).

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0x14F` | [0x004A0E30](functions/004A0E30.md): every update with health, before the speed and the move | CONFIRMED | `BoulderRules.Roll` |
| `0x149` | `0x004A101F`: the move loop ended on a turn that left wear below health; a turn that reaches health keeps the loop going and is silent | CONFIRMED | `BoulderRules.Roll` |
| `0x14D`, `0x14E` | [0x004A0930](functions/004A0930.md) at `0x004A0D87`, `0x004A0DBD`: a struck creature, `0x14E` when the blow left it dying, else (after the stun) `0x14D` | CONFIRMED | `BoulderRules.StrikeCreature` |
| `0x14A`, `0x15A` | same, `0x004A0AC3`, `0x004A0A96`: a struck completed door, `0x15A` when its definition's byte `+0xB3` is 4 or 5 | CONFIRMED | `BoulderRules.Strike` |
| `0x14B`, `0x14C` | [0x004A10E0](functions/004A10E0.md): wear on water, on lava; the sunken-floor arm is silent | CONFIRMED | `BoulderRules.Wear` |
| `0xF4` | [0x004A1390](functions/004A1390.md): after the destruction call; the deletion asks it again | CONFIRMED | `BoulderRules.Update`, and `ObjectSoundRules.Removing` |
| `0xC5` | [0x0049C2A0](functions/0049C2A0.md): a slap | silent: no object category holds it | nothing |

All have samples in `TRAP_BOULDER`: one each but `0x14E` and `0x14F` (three). The door's byte (record `0x226`, shipped 3, 4, 3, 5,
2, 2, 2, 2 for doors 1 to 8) is `DoorDef.ImpactKind`; the Steel and Magic doors give `0x15A`. A struck object or trap asks nothing. `Boulder_Update` runs the strike, the roll and the wear whatever the roll did, so a boulder
with no health (which the roll destroys, silently) still reaches the destruction block and asks `0xF4`; no shipped
`BoulderInitialHealth` is 0 and the slap that would set it is unbuilt, so play does not reach it.

The boulder's own roll sound is asked every update while it lives, as DK2 does; a live handle retains an equal-priority request, so
one sample plays to its end and the next follows (STRONGLY INFERRED from [sound-playback.md](sound-playback.md), not heard in play).
The turn's `0x149` and the break's `0xF4` share the handle and are retained too while a roll sample still plays.
