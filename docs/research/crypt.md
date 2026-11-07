---
systems: [rooms, creatures, combat, art-assets]
---
# Crypt: surviving cut-room content

Read 2026-11-07 from the reference install's DKII.EXE and shipped data.
Findings are STRONGLY INFERRED from binary/data, not observed in native play.
The Crypt is room 26, terrain 41; it is distinct from the Graveyard's object
75, also called a crypt. Shipped-map reachability is recorded in
[guard-room section 7](guard-room.md#7-the-rest-of-category-11-the-crypts-states-274277).

## Assets present

Read the records by their own id bytes, not their position in the file.
Rooms.kwd record 26 uses the same `Crypt` mesh resource and
`gui\rooms\graveyard` icon as Graveyard record 14. Its other icon resource
is `RoomIcons#13`. Its cost is 2000, flags 0x102C (no Buildable bit),
recommended size 5x5, and torch resource `Torch1`. Name/tooltip string ids,
eight effect words at file +0x382, and eight furniture ids at +0x39A are zero.

| Objects.kwd id | Name | Main art resource |
|---|---|---|
| 141 | Crypt Pillar | `Graveyard_Pillar` |
| 142 | Crypt Bell | `Hatchery Coup` |
| 143 | Crypt Dancer | `Ske-Dance` |
| 144 | Crypt Special Dancer | `Ske-Dance` |

These four object records have empty sound categories and zero effect words
at +0x354/+0x356/+0x358. The Bell therefore references a chicken-coop model;
its name does not establish that a dedicated bell model or bell sound survives.
Both dancers reference the same animated Skeleton resource.

Meshes.WAD contains `Crypt0.kmf` through `Crypt9.kmf`,
`Crypt_Sarcophagus.kmf`, `Graveyard_Pillar.kmf`, `Hatchery Coup.kmf` and
`Ske-Dance.kmf`. The sarcophagus is object 75's art, not the Bell's art.
Crypt0's material strings reference `Crypt Floor`, `Crypt_Floor2` and
`Brick floor_DIRTY`; Crypt8 references `Catacomb Skulls`, `Crypt_Alcove`,
`Crypt_Alcove_DECAY` and `Crypt_Cobwebs`. The texture-cache directory
`DK2TextureCache/EngineTextures.dir` contains the Crypt and Catacomb texture
names with mip suffixes, plus sarcophagus/lid and headstone textures; its
paired EngineTextures.dat is present. This establishes surviving named
texture entries and mesh references, not a visual acceptance check of each image.
Much of this art remains used by the shipped Graveyard.

## Rules present

The room's bell placement is [0x004D8CB0](functions/004D8CB0.md).
The working creature states are documented in guard-room section 7 above.
They register one occupant, route it beside the bell and start action 80.
There is no evidence here that using the room consumes or sacrifices that creature.

The full creature-to-periodic-effect table, timer gates and instruction
provenance are in [action 80's record](functions/00499CA0.md). Effects include
owner-wide healing and anger reduction, gold deposits, chickens, rats and
the existing Tremor system. A type taking the default periodic branch can
still supply an occupant-count bonus.

Occupant groups are maintained by [0x004D8E60](functions/004D8E60.md).
Already-traced consumers include Dark Angel/Unique Dark Angel melee bonuses
and Monk damage reduction ([combat damage](combat-damage.md), sections 4-5),
and Vampire/Unique Vampire threat modifiers and Maiden fear reduction
([combat fear](combat-fear.md), section 2). These are player-wide effects,
not merely improvements to the creature standing in the Crypt. Other group
consumers have not been exhaustively audited in this investigation.

Royal and Knight occupants also request Skeleton dancers. Their exact
counts and the additive room counter are recorded in
[state 275](functions/00483870.md) and
[the dancer-spawn helper](functions/004D9410.md). The latter creates object
143 in process state 57; this investigation does not establish the whole
lifetime of that process or a creation path for object 144.

## Recovery boundary

Enough survives to reconstruct substantial room behavior without inventing
its core periodic effects. Finished Crypt-specific artwork, complete UI and
the intended balance are not established. No custom-map native experiment
was performed; apparent placeholders and the shipped reachability restriction
do not prove the remaining paths work end to end. Re-enabling it would be
restoration of cut content, separate from shipped-campaign fidelity.
