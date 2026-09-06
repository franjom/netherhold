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
on that internal table. Every one of its 239 values at record offset `+4` is a
valid `Text.str` id, but they resolve to only 63 distinct shared status labels.
They describe state families rather than uniquely naming all 239 internal
states, so no implementation depends on them.
