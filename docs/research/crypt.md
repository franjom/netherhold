---
systems: [rooms, creatures, combat, art-assets, audio, hud-front-end]
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

## Text and sound

The English reference install has 92 STR tables under Data/Text/Default.
A decoded search for crypt, sarcoph, mausol, catacomb, tolling and the word
bell finds matches only in Text.str: id 2920 and ids 3049-3065. The latter
block contains capture/loss/attack notices, capacity/access warnings,
selection/zoom labels, a cost tooltip and an occupant tooltip. In particular:

- 3056: "Your Crypt is still not functional!  You must enlarge it."
- 3063: "Crypt.  Cost: %21 Gold."
- 3065: "Crypt.  Health: %37%.  Tolling the Bell is %90 the %30."

These strings exist despite the zero name/tooltip references in Rooms.kwd.
They are interface text, not evidence of recorded Mentor dialogue.
Speech.str has no match for the search terms. The mentor bank's sample 373,
`grave001.mp2`, corresponds to the Graveyard's Vampire-arrival line; it is
not a Crypt introduction ([speech mapping](mentor-speech-and-notifications.md#1-one-line-is-one-id-in-three-places)).

A header/name scan of all 142 SDT banks (8959 entries) under Data/Sound
finds no sample name containing crypt, sarcoph, mausol or catacomb, and no
bank/map filename containing those terms or bell. Three bell-named samples
do survive, all in Data/Sound/Sfx/Global/GraveyardHD.sdt:

| Zero-based entry | Name | Nonempty payload, bytes |
|---|---|---|
| 1 | `bell1_pd6r.mp2` | 40961 |
| 4 | `bell_v2.mp2` | 39009 |
| 5 | `bell_v4.mp2` | 39289 |

The bank also has two wisp samples. These are candidate shared sounds for
restoration; a Crypt-specific use is not established. No audio was auditioned
or exhaustively transcribed, so these searches establish no identifiable
Crypt Mentor line, not the absence of one under an unrelated name.

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
