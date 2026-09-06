# DK2.Data

Runtime readers for original Dungeon Keeper 2 data. The assembly targets
`netstandard2.1` and contains no Unity dependency.

## STR localised string tables

`Dk2StringTable` reads the 92 `BFST` files in `Data/Text/Default` and resolves
their table-local string ids through the sibling `MBToUni.dat` character map. The
shipped English files contain 7,024 entries and occupy 353,104 bytes in total.
`Text.str` has file id 1,091 and 3,086 entries, covering string ids 0 through
3,085. Definition records refer to these local ids directly.

The 12-byte header is `BFST`, a little-endian file/resource id, and a
little-endian entry count. Its offset table contains one little-endian value per
entry. These values
are relative to byte 12, not absolute file offsets: `Text.str`'s first stored
value is 12,344, which resolves to absolute byte 12,356, exactly after its
3,086-entry table. Entries contain four-byte-aligned typed chunks: character
data encoded as one- or two-byte indexes into `MBToUni.dat`, substitution
parameters, and an end marker.

The exact role of the file/resource id, the meanings of header bytes `+5`
through `+7` in `MBToUni.dat` beyond the verified
single-byte/multibyte threshold, non-English offset ordering, and any chunk
types absent from the shipped English files remain **UNKNOWN**. Decoded game
text is read only from a user's installation and is never stored in this
repository.

The creature-state-table hypothesis was also tested without adding a dependency
on that internal table. **Settled 2026-09-06** (ADR-0072): the table has **281**
records, not the 239 counted here, and the `u16` is at record `+0x14`. Every one
of the 281 values is a valid `Text.str` id — they resolve to **70** distinct
labels, which are the activity lines the game prints on the flower above a
creature's head. They name state *families* rather than uniquely naming all 281
internal states, so no implementation depends on them. The full table is in
`docs/research/creature-state-table.md`.

## Door and trap definitions

`KwdDoors` reads the 8 fixed-size, 616-byte records in `Doors.kwd`; `KwdTraps`
reads the 16 fixed-size, 579-byte records in `Traps.kwd`. Both expose the
32-byte editor name, contiguous byte id, health, gold price, and workshop
manufacture requirement. Traps additionally expose their shot-record id and
the already measured standing mana upkeep.

The gold columns are the strongest semantic anchors: `Doors.kwd +0x22E`
matches all six keeper-door prices printed in the original manual, and
`Traps.kwd +0x1F2` matches all eleven trap/guard-post prices there. The ids are
permutations of 1..8 and 1..16. Trap shot ids at `+0x20D` independently resolve
to matching `Shots.kwd` names (Gas Trap to Gas Trap Cloud, Boulder Trap to
Boulder, Alarm Trap to Alarm, and so on). OpenKeeper's proposed field order was
used to locate candidates, then checked against these shipped-byte invariants;
its code was not copied.

The following remain **UNKNOWN** and are deliberately not properties of the
typed definitions:

- Trap `+0x20A` is proposed externally as the number of shots when armed. It is
  not established as a finite charge count: the shipped column includes 48 for
  both Alarm rows, 4 for most traps, and 1 for Boulder and Jack In The Box.
- Trap `+0x1DC` contains small values from 1 through 9 and is a plausible
  trigger range, but neither the shipped bytes nor the manual pin that meaning
  or its unit.
- The remaining art resources, timing and height values, flags, materials,
  effect ids, string ids, sound categories, trigger modes, editor ordering,
  shot offsets, per-shot mana field, and unexplained bytes in both records are
  not read. Their external labels are hypotheses until independently anchored.

## Player slots

`KwdPlayers` reads the PLAYERS(180) record in each level's `<name>Players.kld`:
205-byte records, one per player slot, 66 files that all divide exactly (274
slots, 121 with the AI flag, of which 23 are computer keepers on 10 levels; the
other 98 are the good and neutral rows every level carries and the game's player
tick skips). Each slot exposes the id, name, starting gold, trigger id, camera,
the AI flag, and the decoded 158-byte AI tuning block as `KeeperAIParameters`.
`GameDatabaseLoader.LoadCampaignPlayers` loads one level's file.

The block's field names are the retail executable's own debug labels; the field
boundaries are the ones its loader applies, so three fields the editor stored as
16-bit values are read as the single byte the game consumes. The map, the
consumers of every field at runtime, the shipped ranges and the parameters the
game never reads are in `docs/KEEPER_AI.md` (STRONGLY INFERRED from the
binary; the record stride is CONFIRMED from data).
OpenKeeper's parser agrees on every field boundary and was used as a cross-check
only; its identifiers were not adopted.
