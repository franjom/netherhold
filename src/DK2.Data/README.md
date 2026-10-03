# DK2.Data

Runtime readers for original Dungeon Keeper 2 data. The assembly targets
`netstandard2.1` and contains no Unity dependency. Nothing decoded from a user's
installation is ever stored in this repository.

Entry points: `Dk2Install.Find`/`Require` resolves the install (`DK2_DIR` first);
`GameDatabaseLoader.Load` builds the game database from `Data/editor`, and its
`LoadCampaign*` methods read one level's `MAP`, `VARIABLES`, `PLAYERS`, `THINGS`,
`TRIGGERS` and `LEVEL` records.

## KWD/KLD records

`KwdReader` is the chunk-level reader; the container layout is CONFIRMED against the
reference install (`docs/research/kwd-container.md`) and a file may carry several
chunks back to back. All **18** `KwdRecordType` ids have a typed parsing surface, and
each typed record keeps its exact source image, so `dk2tools kwd corpus` byte-round-
trips every record it reads — 408 files on the reference install, and every image of
the 17 types the shipped corpus contains. GLOBALS(0) is the eighteenth: no shipped
file holds one, so its round trip is synthetic and that zero stays visible in the
audit (Q220). This is ADR-0091, and it establishes record *consumption*, not
parameter semantics — for TRIGGERS, ADR-0092 replaces several widths and names that
consumption had not disproved. `KwdCorpusAudit` is shared by the tool and the
install-gated test so the status claim and the assertion cannot drift apart.

| Reader | Records |
|---|---|
| `KwdMap` | MAP(100) — `u32 width`, `u32 height`, then exactly `width × height × 4` bytes, one tile per cell in row-major order |
| `KwdTerrainRooms` | TERRAIN(110) at 552 bytes, ROOMS(120) at 1055 — both self-validating, because summing the documented field widths reproduces the stride exactly |
| `KwdTraps`, `KwdDoors` | TRAPS(130) at 579, DOORS(140) at 616 — below |
| `KwdKeeperSpells` | KEEPERSPELLS(150) — 13 records of 406 bytes, of which three bytes are read: Create Imp's cast cost and an imp's standing upkeep (Q53, Q55) |
| `KwdCreatures` | CREATURES(170) — 5449 bytes standalone, 5537 when embedded in a globals file (88 extra trailing bytes). Gameplay fields only; the ~45 art-resource blocks are skipped |
| `KwdPlayers` | PLAYERS(180) — below |
| `KwdTaggedRecords` | THINGS(190) and TRIGGERS(210), the two types that are not a fixed stride: both are self-describing as `u32 tag; u32 payloadSize; u8[payloadSize]` |
| `KwdThings` | one typed class per THINGS payload tag, 194 through 205. `DK2.Simulation`'s `ThingProjection` places what it can (ADR-0101) |
| `KwdTriggers` | TRIGGERS' common tail, the eight-byte union and the type-aware parameter views (ADR-0092) |
| `KwdVariables` | VARIABLES(230) — 16 bytes each, `u32` id plus a 12-byte payload whose shape depends on the id |
| `KwdObjects` | OBJECTS(240) — 145 records of 894 bytes, of which only the last 82 are gameplay |
| `KwdLevel` | LEVEL(220), including the ticks-per-second field — one source field, not a second timing parser |
| `KwdAdditionalRecords` | the remaining definition tables: CREATURE_SPELLS(160) at 266 bytes, EFFECT_ELEMENTS(250) at 182, SHOTS(260) and EFFECTS(270) |

`KwdTriggerCorpusAudit` enumerates the trigger types actually present in the shipped
levels, which is what `dk2tools kwd trigger-types` prints.

## Writers

`KwdWriter.Write`, `WriteFile` and `ToBytes` rewrite parsed containers, preserving
every header byte and LEVEL's understated content size. `CreateFromTemplate`
requires a parsed chunk as the explicit source for unknown header fields and
timestamps (ADR-0343); template and input payload arrays are copied.
`KwdRecordRoundTrip.Join` is the validated inverse of `Split`: it requires the
template's count, types and strides, including both CREATURES forms, and verifies
tagged payload lengths. GLOBALS has no established standalone layout and remains
a LEVEL path discriminator with synthetic coverage (Q220).
`WriteRecord` on `KwdDoors`, `KwdKeeperSpells`, `KwdObjects` and `KwdTraps`, and
`KwdTerrainRooms.WriteTerrainRecord` / `WriteRoomRecord`, overlay every decoded field over an explicit
record image, using reader offsets.
`MapGrid.WriteTile` overlays all four tile bytes; map resizing is still pending.
`KwdVariables.ParseRecord` / `WriteRecord` operate on individual typed cells,
preserving repeated ids and source order that the aggregate table folds.
Changing between structured payload shapes requires a matching template.
`KwdPlayers.WriteRecord` overlays player and AI fields. Unchanged boolean encodings
and unconsumed high bytes survive; `RawBlock` edits are refused. Exhaustive field
edit tests guard the AI reader/writer layout.
Unchanged names retain their original padding; unrepresentable edits are refused.
Other typed overlays, shape editing and the editor UI are still pending.
`KwdCreatures.WriteRecord` overlays both strides, preserving filtered list slots,
inactive Angry payloads, SizeClass's low bits and the raw 88-byte embedded tail.
List-count changes and edits invisible to the reader are refused. Q526's two
base-creature names share one value. Exhaustive edits guard the reader/writer layout.

## WAD archives and saved games

`WadArchive` reads DK2's "DWFB" version 2 archives — CONFIRMED against all five WADs
of the reference install, with the field table in `docs/research/wad-format.md`.
`WadDecompressor` implements the type-4 entry decompression, adapted from GLKeeper
(MIT — see `THIRD-PARTY-NOTICES.md`).

`Dk2SaveReader` reads the tile grid out of a `.SAV` or `.QSAV` into the same
`LevelMap` the KWD loader produces. STRONGLY INFERRED from DK2's own save routine and
cross-checked against every save in the reference install
(`docs/research/sav-format.md`). The entity records after the grid are undecoded
(Q182), so a loaded save has terrain and ownership and no creatures or objects.

## STR localised string tables

`Dk2StringTable` reads the 92 `BFST` files in `Data/Text/Default` and resolves their
table-local string ids through the sibling `MBToUni.dat` character map. The shipped
English files contain 7,024 entries and occupy 353,104 bytes in total. `Text.str` has
file id 1,091 and 3,086 entries, covering string ids 0 through 3,085. Definition
records refer to these local ids directly.

The 12-byte header is `BFST`, a little-endian file/resource id, and a little-endian
entry count. Its offset table contains one little-endian value per entry. These
values are relative to byte 12, not absolute file offsets: `Text.str`'s first stored
value is 12,344, which resolves to absolute byte 12,356, exactly after its
3,086-entry table. Entries contain four-byte-aligned typed chunks: character data
encoded as one- or two-byte indexes into `MBToUni.dat`, substitution parameters, and
an end marker.

The exact role of the file/resource id, the meanings of header bytes `+5` through
`+7` in `MBToUni.dat` beyond the verified single-byte/multibyte threshold,
non-English offset ordering, and any chunk types absent from the shipped English
files remain **UNKNOWN**.

The creature state table is a separate internal table, not a string table, and this
reader does not depend on it: it holds **281** records with a `u16` string id at
record `+0x14`, every one of which is a valid `Text.str` id resolving to one of
**70** distinct labels — the activity lines the game prints on the flower above a
creature's head. They name state *families* rather than uniquely naming all 281
internal states, so no implementation depends on them (ADR-0072,
`docs/research/creature-state-table.md`).

## Door and trap definitions

`KwdDoors` reads the 8 fixed-size, 616-byte records in `Doors.kwd`; `KwdTraps` reads
the 16 fixed-size, 579-byte records in `Traps.kwd`. Both expose the 32-byte editor
name, contiguous byte id, health, gold price, and workshop manufacture requirement.
Traps additionally expose their shot-record id and the already measured standing mana
upkeep.

The gold columns are the strongest semantic anchors: `Doors.kwd +0x22E` matches all
six keeper-door prices printed in the original manual, and `Traps.kwd +0x1F2`
matches all eleven trap/guard-post prices there. The ids are permutations of 1..8 and
1..16. Trap shot ids at `+0x20D` independently resolve to matching `Shots.kwd` names
(Gas Trap to Gas Trap Cloud, Boulder Trap to Boulder, Alarm Trap to Alarm, and so
on). OpenKeeper's proposed field order was used to locate candidates, then checked
against these shipped-byte invariants; its code was not copied.

The following remain **UNKNOWN** and are deliberately not properties of the typed
definitions:

- Trap `+0x20A` is proposed externally as the number of shots when armed. It is not
  established as a finite charge count: the shipped column includes 48 for both Alarm
  rows, 4 for most traps, and 1 for Boulder and Jack In The Box.
- Trap `+0x1DC` contains small values from 1 through 9 and is a plausible trigger
  range, but neither the shipped bytes nor the manual pin that meaning or its unit.
- The remaining art resources, timing and height values, flags, materials, effect
  ids, string ids, sound categories, trigger modes, editor ordering, shot offsets,
  per-shot mana field, and unexplained bytes in both records are not read. Their
  external labels are hypotheses until independently anchored.

## Player slots

`KwdPlayers` reads the PLAYERS(180) record in each level's `<name>Players.kld`:
205-byte records, one per player slot, 66 files that all divide exactly (274 slots,
121 with the AI flag, of which 23 are computer keepers on 10 levels; the other 98 are
the good and neutral rows every level carries and the game's player tick skips). Each
slot exposes the id, name, starting gold, trigger id, camera, the AI flag, and the
decoded 158-byte AI tuning block as `KeeperAIParameters`.
`GameDatabaseLoader.LoadCampaignPlayers` loads one level's file.

The block's field names are the retail executable's own debug labels; the field
boundaries are the ones its loader applies, so three fields the editor stored as
16-bit values are read as the single byte the game consumes. The map, the consumers
of every field at runtime, the shipped ranges and the parameters the game never reads
are in `docs/KEEPER_AI.md` §2.2 (STRONGLY INFERRED from the binary; the record stride
is CONFIRMED from data). OpenKeeper's parser agrees on every field boundary and was
used as a cross-check only; its identifiers were not adopted.
