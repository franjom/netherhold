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
chunks back to back. Every `KwdRecordType` id has a typed parsing surface, and each
typed record keeps its exact source image, so `dk2tools kwd corpus` byte-round-trips
every record it reads on the reference install. GLOBALS(0) is the one type no shipped
file holds, so its round trip is synthetic and that zero stays visible in the audit
(Q220). This is ADR-0091, and it establishes record *consumption*, not
parameter semantics — for TRIGGERS, ADR-0092 replaces several widths and names that
consumption had not disproved. `KwdCorpusAudit` is shared by the tool and the
install-gated test so the status claim and the assertion cannot drift apart.

| Reader | Records |
|---|---|
| `KwdMap` | MAP(100) — `u32 width`, `u32 height`, then exactly `width × height × 4` bytes, one tile per cell in row-major order |
| `KwdTerrainRooms` | TERRAIN(110) at 552 bytes, ROOMS(120) at 1055 — both self-validating, because summing the documented field widths reproduces the stride exactly |
| `KwdTraps`, `KwdDoors` | TRAPS(130) at 579, DOORS(140) at 616 — below |
| `KwdKeeperSpells` | KEEPERSPELLS(150) — 13 records of 406 bytes: name, cast cost, upkeep, cooldown, target and location selectors, flags, and the shot and bonus-shot fields DK2's loader `0x004F7CE0` copies (Q53, Q55; `research-tree.md` §5) |
| `KwdCreatures` | CREATURES(170) — 5449 bytes standalone, 5537 when embedded in a globals file (88 extra trailing bytes). Gameplay fields, plus each art-resource slot's integer animation timing; the art resources themselves are skipped |
| `KwdPlayers` | PLAYERS(180) — below |
| `KwdTaggedRecords` | THINGS(190) and TRIGGERS(210), the two types that are not a fixed stride: both are self-describing as `u32 tag; u32 payloadSize; u8[payloadSize]` |
| `KwdThings` | one typed class per THINGS payload tag, 194 through 205. `DK2.Simulation`'s `ThingProjection` places what it can (ADR-0101) |
| `KwdTriggers` | TRIGGERS' common tail, the eight-byte union and the type-aware parameter views (ADR-0092) |
| `KwdVariables` | VARIABLES(230) — 16 bytes each, `u32` id plus a 12-byte payload whose shape depends on the id |
| `KwdObjects` | OBJECTS(240) — 145 records of 894 bytes: the gameplay tail (the last 82) and the art slots' integer animation timing |
| `KwdLevel` | LEVEL(220), including the ticks-per-second field — one source field, not a second timing parser |
| `KwdAdditionalRecords` | the remaining definition tables: CREATURE_SPELLS(160) at 266 bytes, EFFECT_ELEMENTS(250) at 182, SHOTS(260) and EFFECTS(270) |

`KwdTriggerCorpusAudit` enumerates the trigger types actually present in the shipped
levels, which is what `dk2tools kwd trigger-types` prints.

## Writers

`KwdWriter.Write`, `WriteFile` and `ToBytes` reproduce parsed containers,
including unknown header words, timestamps and LEVEL's understated size.
`CreateFromTemplate` requires a parsed chunk for header provenance (ADR-0346).
`KwdRecordRoundTrip.Join` validates counts, strides and tagged lengths.

Every typed reader has an overlay writer. `KwdTypedRecords.Read`, `Write` and
`Rewrite` provide shared dispatch; individual APIs remain available on the
reader classes. Writers require explicit source images and preserve unknown
bytes and unchanged string padding. Typed `with` copies retain their source:
`ToBytes()` returns that original image, while overlay writers encode edits.

VARIABLES edits use individual cells, preserving duplicate ids and order.
PLAYERS preserve noncanonical booleans and unused high bytes; raw AI block
edits are refused. CREATURES preserve filtered source slots, conditional
payloads and the raw 88-byte embedded tail; filtered list-count changes are
refused. Both base-creature aliases share one value (Q528).
THINGS cover every tag, including all sixteen hero-party slots; the unshipped
dead-body layout remains a hypothesis (Q221). TRIGGERS share decoded parameter
layouts with their reader, reject conflicting alias edits and require an
independently validated template for type changes.
LEVEL preserves both metadata boundaries, all messages, paths and tail words.
`KwdLevel.WritePathRecord` supports GLOBALS only as its known LEVEL path
discriminator; standalone GLOBALS chunks remain refused (Q220).
`KwdRewriteAudit.Run(input, outdir)` applies every typed overlay and writes KWD/KLD
files with their relative directory structure. It reports exact identity or the
first differing byte, chunk, record index and decoded field; opaque bytes use
their raw offset. Output must be outside an input directory and cannot replace
the source file. Linked input/output paths and existing destination files are
refused before writing; existing plain output directories remain usable.
The command and install-gated test share this audit:

    dk2tools kwd rewrite <file|dir> <outdir>

Indices are zero-based; differing byte values are hexadecimal. Exit codes are
0 for identical files, 1 for differences and 2 for refused input or an I/O error.
The editor UI is still pending.

## Editing

`DK2.Data.Editing.KwdLevelFileSet.Load(levelFile, contentRoot)` loads the LEVEL
manifest and its referenced KWD/KLD files. Paths stay relative to the explicit
content root; extensionless references use `.kwd`, with case-insensitive lookup.
`Create` accepts explicit chunk templates for hand-built sets.
`ReadRecord<T>` and `EditRecord<T>` use a `KwdRecordLocation`; snapshots and input
images are isolated. A failed edit leaves the set unchanged; callbacks receive
an independent projection and nested mutations are refused.
`ToBytes` and `Save(outputRoot)` preserve file paths and serialize owned images.
Generic field edits freeze trigger identifiers, links and roots, plus the LEVEL
file manifest. `InsertThing` requires an explicit tagged image; `RemoveThing`
retains the remaining images and order. Both update count, content size and end
offset. New nonzero trigger roots must resolve uniquely in a complete level set.
`InsertTrigger` accepts an explicit template with a unique, unreferenced nonzero
id and no tree links. `RemoveTrigger` refuses nodes referenced by next/child
links or by LEVEL, PLAYERS, THINGS and hero-party member roots. Both require a
complete LEVEL file set and update the separate generic/action counts.
Existing trees are retained; graph rewiring remains outside this API.
`ReadMap` and `RepaintMap` use `KwdChunkLocation` and `TilePos`.
`ResizeMap` takes `KwdMapDimensions` and an explicit four-byte fill image.
It retains overlapping coordinates, fills new cells from that image, and rewrites
width, height and container lengths. Resizing does not relocate placed entities.
The API does not validate level playability.

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

`Dk2StringTable` reads the `BFST` files in `Data/Text/Default` and resolves their
table-local string ids through the sibling `MBToUni.dat` character map (`dk2tools str
list` prints one). Definition records refer to these local ids directly.

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

## BF4 bitmap fonts

`Bf4FontReader` reads the `F4FB` fonts in `Data/Text/Default` (`FONT_INGAME7`-`72`,
`FONT_FRONTEND10`-`36`; resource type 4) into a `BitmapFont`. All 22 shipped files
decode, every glyph to its full `w * h` (`Bf4FontReaderTests`). The class comment
gives the byte layout. The facts come from the loader `0x006336D0`, the font
object's methods `0x006335B0`-`0x006336A0`, and the blitter `0x006337A0`
(CONFIRMED, read 2026-10-17):
- the header holds the line height (byte `+5`) and the glyph count (u16 `+6`). An
  offset table follows, one record per glyph;
- a record holds its UTF-16 character, the stored and unpacked sizes, the flags,
  the width and height, signed x and y offsets from the pen, and the advance;
- pixels are 4-bit, 0 clear to 15 full, high nibble first. They run on across rows
  with no row padding. This is STRONGLY INFERRED from the data: every unpacked size
  is `ceil(w * h / 2)`, and odd widths draw sheared if padded. The five blitters
  behind `0x006337A0` are unread;
- flag bit 0 is a nibble RLE: a non-zero nibble is one pixel, and `0 n v` is `v`
  repeated `n` times. `0 0` ends the glyph. 177 of the 2,250 RLE glyphs stop one
  nibble short. The missing nibble is the low half of a byte whose high half the
  writer `0x00634040` stored as a whole byte, so it is 0 in DK2 and in ours;
- flag bit 1 is one bit a pixel. No shipped glyph uses it;
- a space draws nothing and only advances.

Header byte `+4` (1 in every file) and record `+2` are not read by those methods;
their roles are **UNKNOWN**.

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
205-byte records, one per player slot; every shipped file divides exactly. Slots with
the AI flag include the good and neutral rows every level carries, which the game's
player tick skips; the computer keepers are the rest. Each
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
