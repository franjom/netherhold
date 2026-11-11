---
systems: [audio]
---
# Sound effects: spells, shots and combat

Sources: DKII.EXE v1.70, read through Ghidra on 2026-11-02; shipped `Data/Sound/Sfx/Global/*SFX.map` and
`Data/editor/{KeeperSpells,Shots,CreatureSpells}.kwd`. Plan Phase W6 of
[world-sounds](../plans/2026-10-31-world-sounds.md). Conventions as in
[sound-effects.md section 6](sound-effects.md#6-the-hand-and-its-cursor): a request is one slot `+0x20` call of the
sound manager (handle, category, event, position); a row is silent when no `SFX.map` holds the group with samples.
Sections 1 and 2 are the keeper spells and the Tremor, 3 the shots, 4 a creature's spell, 5 the swing, death and
corpses, 6 the torture devices.

## 1. Keeper spells and the categories

**Where each category lives (STRONGLY INFERRED).** A keeper spell's category is its runtime record's `+0x6D`: the loader
[0x004F7CE0](functions/004F7CE0.md) resolves the file record's 32 bytes at `0xF1` (ours `KeeperSpellDef.SoundCategory`)
through the manager's name lookup (slot `+0x1C`). The 32 bytes at `0x174` become `+0x71` and name the GUI button's category
(`GUI_BUTTON_*`). A shot's category is its definition's `+0x72`, loaded by `0x004ACDF0` from the file record's `0xC9` (ours
`ShotDef.SoundCategory`, already read); a shot thing's `+0x125` is its definition pointer, so the plan's "shot `+0x125`" and
"keeper spell `+0x72`" name the pointer and the shot's own field. The shipped keeper-spell names are `KEEPER_SPELL_<SPELL>`;
the Summon Horny and OneShotReaper spells share `KEEPER_SPELL_SUMMON_REAPER`.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0xEF` (handle 0, spell category) | [0x004BBD30](functions/004BBD30.md) at `0x004BBEBD`: after the shot is made, only when the shot definition's `+0x72` is zero; at the cast position argument, the pointer the shot's aim also gets | STRONGLY INFERRED | `KeeperSpellRules.CastAtPlace`, and `ImpSupplySystem.Cast` for Create Imp, bought and free (shot 2 has no category, so it asks `0xEF` in `KEEPER_SPELL_CREATE_IMP`, 5 samples) |
| `0xEF` (handle 0, spell category) | [0x004BBF90](functions/004BBF90.md) at `0x004BC26F`: after payment and both clocks, with no category test; at the target's own `+0x1A`, not the lifted point | STRONGLY INFERRED | `KeeperSpellRules.CastOnCreature` |
| `0xEF` | [0x004BC290](functions/004BC290.md) at `0x004BC4DB`: the third cast path, the handler of action 56 `0x00513370` | STRONGLY INFERRED site | waits: unmirrored; the computer keeper never reaches it (Q537) |

`0xEF` has samples in `KEEPER_SPELL_CALL_TO_ARMS` (1), `_CHICKEN` (5), `_CREATE_GOLD` (2), `_CREATE_IMP` (5), `_HEAL`, `_INFERNO`,
`_POSSESSION`, `_SIGHT_OF_EVIL`, `_SUMMON_REAPER`, `_THUNDERBOLT` and `_TURNCOAT` (1 each); none in `_TREMOR`. A shipped shot
with an empty category (2 Create Creature, 4, 5, 10, 21, 31, 32 and others) makes the cast ask in the spell's; the shots of
Inferno, Tremor, Sight of Evil and the Reaper name their spell's category, so they ask in it through the shot's own
construction (section 3). Deviation: the place command carries no height, so the position's `Z` is the shot's view height
(`0x1000`), not the pointer's. Create Imp's cast knows its z: the free cast (`0x004BB4C7`) passes 0, the bought one (action 54, `0x005132FB`) the terrain height from world vtable `+0x204` (unread, taken as the floor's), and the constructor's sounds sit on the floor.

## 2. The Tremor

All three requests are in the Tremor spell's category (the world's spell-8 record `+0x6D`) and keep the slot's handle, `+0x10` of
the 20-byte slot: ours is a voice per slot (`SoundSourceKind.Tremor`, the slot index).

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0xFB`, handle `slot+0x10` | [0x00510290](functions/00510290.md) at `0x00510355`: the end of the start, at the call's `(x, y)` with height `0x2000` | STRONGLY INFERRED | `TremorRules.Start` |
| `0x31F`, handle `slot+0x10` | [0x00510440](functions/00510440.md) at `0x005104C6`: the update that finds a slot's age at its duration, after the radius is cleared, at the slot's `(x, y)` and `0x2000` | STRONGLY INFERRED | `TremorRules.Update` |
| `0x2EC`, handle 0 | the same at `0x005105C3`: an active slot whose floor chooser `0x00510370` found a floor, after the view effect and before the tile pass, at that tile's centre with height 0 | STRONGLY INFERRED | `TremorRules.Update` |

`_TREMOR` holds `0xFB` (5 samples) and `0x2EC` (1); `0x31F` has samples only in `SPELL_RAISE_DEAD`, so the end is silent. An active slot
with a live handle also replays it at its place every update (`0x00510502`, slot `+0x30`): the replay moves a voice that exists and needs nothing built ([sound-effects-continuous.md section 3](sound-effects-continuous.md#3-looping-thing-and-room-sounds)).

## 3. Shots

Every request is in the shot definition's category (section 1), at the shot's position `+0x1A` when it runs. A shot has two
handles, `+0x2E` and `+0x32` (ours `SoundSourceKind.Shot` and `ShotSecond`, both by the shot's id).

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0xEF` (`+0x2E`), `0xF2` (`+0x32`) | [0x004AAC90](functions/004AAC90.md) at `0x004AAF1E`, `0x004AAF40`: the end of the constructor, after the floor raise; `0xEF` only when the last argument of `Shot_Create` is nonzero: fourteen of its fifteen callers push 1, and the fifteenth, `0x004B6CB0` in the saved-game loader (`0x004B65E0`), passes 0, so it asks `0xF2` alone (a simulation never loads that way) | STRONGLY INFERRED | `ShotRules.RequestTrail`, the construction hook of every spawner but Create Imp, which calls `ShotRules.RequestConstructionSounds` itself (it requests no trail) |
| `0x1BC` (`+0x2E`) | [0x004AB180](functions/004AB180.md) at `0x004AB49B`: physics reports the world stopped the shot, after the hit-solid effect and shot, before event 8; for every stop, whether or not the shot dies; a standing mass-0 shot stops every update (physics `0x004B2030` returns 0 at `0x004B20FB` on zero displacement, and gravity acts only where the shot has mass, vtable `+0x88` / definition `+0x49`, above the floor: only shot 18 has mass) | STRONGLY INFERRED | `ShotSystem.Move`, at both stops (a standing mass-0 shot's included, as its hit-solid effect is) |
| `0xF4` (`+0x2E`) | `0x004AB509`: the flying death, health at zero after the countdown, before the death effect; not for the shot dead at tick entry, nor killed by a process, nor one the world stopped with bit 0 | STRONGLY INFERRED | `ShotSystem.Die` (`showDeathEffect`) |
| `0xF4` (`+0x32`) | [0x004AAF70](functions/004AAF70.md) at `0x004AAF9F`: the teardown on removal, first | STRONGLY INFERRED | `ShotSystem.Die`, every death |
| `0xE8` (`+0x2E`) | [0x004AF0E0](functions/004AF0E0.md) at `0x004AF1BF`: the Sight of Evil's process, every call after its first, when its health is 20 and its owner is the viewer | STRONGLY INFERRED | `ShotRules.Process` (process 25, the pre-move event), `ViewerOnlyFor` the owner |
| `0xDB` (`+0x2E`), in the struck creature's category | [0x004ABD60](functions/004ABD60.md) at `0x004AC099`: a shot that hits a creature, after the contact damage, when definition flags bit 8 is set (also turns the creature away, unbuilt), at the creature's `+0x1A` | STRONGLY INFERRED | `ShotSystem.Update`, `ShotRules.TurnsCreature` |
| `0x31A` (`+0x2E`) | the same at `0x004AC00C`: the struck thing's class `+0x12` is 6, a body | STRONGLY INFERRED site | waits: our shots never find a body |
| `0x12A` | [0x004AD7A0](functions/004AD7A0.md) at `0x004ADA49` (process 1) and [0x004AFE50](functions/004AFE50.md) at `0x004AFF6B` (process 36): a non-worker creature made, the caster's type `0x17`, with the new creature's handle | STRONGLY INFERRED sites | waits: the non-worker arms of both processes are unbuilt |
| `0x154`, handle 0 | [0x004B5020](functions/004B5020.md) at `0x004B52FC`: the floor step bounces a moving shot with flags bit 3 (the Grenade), at its `+0x1A` | STRONGLY INFERRED | waits: no gravity or floor step is built, so no shot bounces (Q398) |
| `0x336`, creature category | [0x004AF8E0](functions/004AF8E0.md) at `0x004AFA7D`: gas process 31, shot damage type 0 only, a worker, half the draws | STRONGLY INFERRED site | unreachable: every shipped gas shot has damage type 2 and ours does nothing there |

The plan's `0x1BC` row is the generic world stop, not Raise Dead; its `0xEF`/`0xF2`/`0xF4` rows are the constructor, the lifetime
death and the teardown. Samples: `0xEF` in `SPELL_ARMOUR`, `_DRAIN`, `_FIREBOMB` (2), `_FREEZE`, `_GUIDED_BOLT`, `_HAIL_STORM`,
`_HAST_CREATURE`, `_HEAL_CREATURE`, `_INVISIBLE`, `_LIGHTNING`, `_RAISE_DEAD`, `_SLOW`, `_TOUCH_OF_DEATH` (1 each), `_ARROW` (8), `_KNIVES`
(4), `_GAS_CLOUD` and `_GAS_MISSILE` (5); `0xF2` in `_FIREBALL`, `_FIREBOMB` (1) and `_RAISE_DEAD` (4); `0xF4` in `_FIREBALL`,
`_FREEZE`, `_RAISE_DEAD` (1), `_FIREBOMB`, `_GRENADE` (2) and `_KNIVES` (5); `0x1BC`, `0x31A` in `_RAISE_DEAD` (1 each, and `0x1BC` in
the `WEAPON_*` categories); `0x12A` in `_SKELETON_ARMY`, `0x154` in `_GRENADE`; `0xE8` in `KEEPER_SPELL_SIGHT_OF_EVIL`. The shipped shot
categories (`Shots.kwd`): 1, 47, 49, 52, 57 and 62 `SPELL_FIREBALL`; 18 `_GRENADE`; 54 `_SKELETON_ARMY`; 15, 24, 33 and 46 their keeper
spell's; 2, 4, 5, 10, 21, 31, 32, 60 and 64 none. A deviation, marked: the shot's position is the observation's `ViewPosition`
(its height approximates the native one, Q398), not a stored `+0x1A`.

## 4. A creature's spell

[0x00498870](functions/00498870.md) at `0x00498A8D` (action 16) and [0x004984D0](functions/004984D0.md) at `0x0049868C` (action 15) ask,
right after the shot is made, the creature spell record's `+0x3F` (ours `CreatureSpellDef.SoundEvent`, file `0xDA`, as
combat-spells.md lists) in the shot's category, with the caster's own handle `+0x2E`, at the caster's `+0x1A` (not the origin the shot
leaves from). STRONGLY INFERRED; built in `FightRules.RequestCastSound`. A third caller of `Shot_Create`, action 14's handler `0x004982B0`
at `0x00498459`, asks the same; nothing begins action 14 (creature-action-table.md section 7). The shipped words are `0x115` to `0x12A` (`0x115` for the fireballs, spit and web), and zero
for the Wizard's first Firebomb and the Imp's Teleport and Haste; only `0x115` (5 samples in `SPELL_FIREBALL`) and `0x12A` (1 in
`SPELL_SKELETON_ARMY`) have samples in any shot category, so every other cast is silent.

## 5. The swing, death and corpses

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0xDB`, the attacker's category and handle | [0x00498E30](functions/00498E30.md) at `0x004992EB`: the last act of a swing that reached its tail (every class arm, a blow of zero included; not for a target that is gone or invulnerable, nor the Horned Reaper's same-owner kill), at the attacker's `+0x1A` | STRONGLY INFERRED | `FightRules.LandBlow` |
| weapon impact, by the target's surface byte | [0x004998A0](functions/004998A0.md) at `0x00499A15`: the attacker's attack type and the struck thing's surface byte, on each class arm before the blow-of-zero test, in the weapon's category with the attacker's handle at its `+0x1A`; see below | STRONGLY INFERRED | `WeaponImpactRules`, from `FightRules.Land` and `PossessionRules.Act` |
| `0xC9` or `0xCC` | `0x00499A40`, the handler stored for creature action 18 by `0x004954B0` (by its slot address, STRONGLY INFERRED): the viewer's possessed creature only, `0xCC` or `0xC9` by an elapsed-time test, with the creature's handle at its `+0x1A` | STRONGLY INFERRED site | waits: first person, an unmirrored handler (W7.4) |
| `0xDA` | [0x00487C70](functions/00487C70.md) at `0x00487E4A`: the head of the death bookkeeping, unless the creature's `+0x3E6` bit `0x40` is set (a removal that counts nothing; `DeathUncounted` is set by creature action 35's byte 0, ADR-0744, so a creature a script removes makes no cry) or world `+0x7239` bit `0x80` is set (a saved game being restored, INFERRED); the whole function is skipped while the world's level-leave probe (vtable `+0x4C`, `0x00487CB8`) is nonzero (the same probe gates what follows the sound in `Shot_Teardown`, `0x004AAFAD`, and `Corpse_Destroy`, `0x004B10B1`) | STRONGLY INFERRED | `CreatureHealthSystem.Die`; a simulation has no leave or restore |
| `0xF4`, `OBJECT_FLIES`, handle `+0x12E` | [0x004B1070](functions/004B1070.md) at `0x004B10A0`: the destruction of a corpse, first | STRONGLY INFERRED | `CreatureHealthSystem` (a body that rots or is buried) |
| `0x152`, `OBJECT_FLIES` | [0x004B12D0](functions/004B12D0.md) at `0x004B1373`: every update of a body, only while the viewer possesses a creature; handle 0 the first time, a slot `+0x30` replay after | STRONGLY INFERRED site | waits: a first-person loop (W7.3, W7.4) |

The death bookkeeping's one caller is the return-slot wrapper `0x004B7A20`, so DK2 runs it for every creature thing freed. Ours asks it
in `CreatureHealthSystem.Die`, the mirror of that bookkeeping, and not where the other removal paths free a creature (leaving the
dungeon, the Temple's pool, the hand's throw out of the map): a marked deviation, unread whether each sounds in DK2. A corpse is a
creature in our list, so its own removal asks `0xF4` and not `0xDA` again. `0xDA` has samples in all 27 creature categories (the
Imp's 3, the Firefly's 1); `0xDB` in 26 (the Imp's 6, none for the Firefly); `0xC9` in 25 (not the Imp or the Firefly); `0xF4` and
`0x152` in `OBJECT_FLIES` (1 and 4).

**The weapon impact (STRONGLY INFERRED, built).** `0x004998A0` is called with the attacker as `this` by the swing for each class arm (`0x00499072`
creature, after the invulnerable return; `0x00499168` door, `0x00499194` trap, `0x004991C0` object, each before the blow-of-zero test, so
a blow of zero sounds) and by the possessed act (`0x00514C8B`, the Anvil, Steel Bucket and Work Bench); the earlier record read it as a
pure lookup for presentation. It looks the attacker's attack type (definition `+0x77A`, ours `CreatureDef.MeleeAttackType`) up in the table at
`0x0069DA38` (four 37-byte records, types 1, 2, 3 and `0xF`: `WEAPON_METAL`, `_WOOD`, `_FLESH` and `_SCYTHE`, whose category dword at `+1` the
level start fills from the name pointer at `+5`), picks one of seven `(sound, effect)` word pairs at `+9` by the struck thing's surface byte
(1 to 7), asks effect `0xED` and the pair's effect (`0x139`, in the Metal's and the Scythe's arms 4 and 5 only), then the pair's sound in the
weapon's category with the attacker's handle `+0x2E` at the attacker's `+0x1A` (`0x004999F9`). The sounds by surface are `0x114`, `0x1BC`,
`0x113`, `0x1BB`, `0x112`, `0x112`, `0x1BD` (the jump table `0x00499A24` sends surface 6 to the pair at `+0x21` and 7 to `+0x1D`; `0x1BD` and `0x112` sit there in every record). A type outside the table or a surface outside 1 to 7 asks nothing. Samples by bank
(`METAL`, `WOOD`, `FLESH`, `SCYTHE`): `0x114` 3, 3, 4, 3; `0x113` and `0x1BB` 3 each; `0x1BC` 2, 3, 2, none; `0x112` only in `OBJECT_FORGE` and
`0x1BD` nowhere, so surfaces 1, 3 and 4 sound in all four banks, 2 in three and 5 to 7 are silent. The surface byte is the struck
definition's: creature `+0x685` (loader `0x00492320`, intermediate `+0x1366`, file `0xF76`), door `+0xB3` (file `0x226`), trap `+0xBF` (loader
`0x004AA440`, intermediate `+0x2D7`, file `0x22F`), object `+0x11C` (loader `0x0049DD60`, intermediate `+0x3C0`, file `0x340`); ours
`ImpactKind` on `CreatureDef`, `DoorDef`, `TrapDef` and `ObjectDef`. Each loader copies the byte only when a presence dword is nonzero (staging `+0x21C` for the creature, `+0x94` for the trap, `+0x78` for the object); our parsers read it unconditionally, which is harmless for the shipped records. The table's arm order is STRONGLY INFERRED from the jump table and the record bytes (not a whole read of each arm). Shipped creatures are 1 (flesh and most), 4 (the knights, the King, the
princes, the guards), 2 (the Stone Knight) and 3 (the Jack in the Box); traps 4, 2 (the Boulder, the Jack) and 3 (the Trigger); objects 1 to
7. The effects are presentation and are not asked; a shot or a body struck by the swing has no arm and no lookup. Marked: the
heart arm uses the Arch Pillar's (object 94) surface byte, as an object arm.

## 6. The torture devices

`0x31C`, `0xCF` and `0x156` of the action handler at `0x00499300` ([record](functions/00499300.md)) are, with the device's `0xFB` and `0x31B`
(sound-effects-objects.md section 1), each behind `CMP [0x00759ADC],1` (the reads at `0x00499605`, `0x00499663` and `0x0049973B` cover
the three). `0x31C` is the victim's, at the shared tail of both devices; `0xCF` is the victim's after the wheel's `0x0049EF60(0x1E)`
and `0x156` the victim's at the chair's arm, after the device's `0xFB`, each in the creature's category (`+0x6E3`); `0x31C` has one
sample in each of 25 creature categories (two in the Reaper's and the King's), `0xCF` 2 to 7 and `0x156` 1 or 2. The global has no store in the
uninitialised data, so it is 0 unless an indirect write exists (HYPOTHESIS; Q635). **Decision: they wait, with the device sounds**: the
handler's sound arms have no mirror (its trough effects do, ADR-0740), and if the flag is 0 in vanilla, as the missing store suggests, DK2 is silent there and building them would add a
sound the original lacks.
