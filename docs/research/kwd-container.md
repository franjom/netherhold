# Mechanic: KWD/KLD container format (top-level record header)

Source(s):
- OpenKeeper `tools/convert/map/MapDataTypeEnum.java` (record ids 0..270)
- ufdada/dk2-tools `Formats/Level/*.bt` (independent derivation)
- Direct inspection of this project's reference install (GOG, 2026-08-29)

Original version: DK2 v1.70 (GOG release)

Evidence:
- `Data/editor/Creatures.kwd` bytes 0-3 = `AA 00 00 00` (0xAA = 170 = CREATURES)
- `Data/editor/GlobalVariables.kwd` bytes 0-3 = `E6 00 00 00` (0xE6 = 230 = VARIABLES)
- ASCII `"Imp"` at offset 0x38 of Creatures.kwd — first creature record's name,
  consistent with Flame's `CCreature_types` listing Imp as creature id 1.
- Test `KwdSmokeTests.EditorDatabaseFiles_StartWithExpectedRecordType` verifies
  the record-type id of all 12 editor KWD files.

Observed behaviour: each KWD file begins with a little-endian int32 record-type id.

Confidence: CONFIRMED

Implementation: `DK2.Data.Model.KwdRecordType` (18 ids), `DK2.Data.Kwd.KwdReader`

Tests: `DK2.Data.Tests.KwdSmokeTests`, `DK2.Data.Tests.KwdReaderTests`

Known exceptions: none.

Open questions: Q70 (the CREATURES tail), Q221 (the dead-body payload).

## Update 2026-08-29 (parser implemented)

Header layout CONFIRMED (`KwdReader`): 5 × u32 fixed header (sig, byteSize=4,
chunkEndOffset, checkOne=sig+1, headerBlockSize), then headerBlockSize bytes of
type-specific header (general: u32 itemCount, u32 unk, 2 × 10-byte timestamp),
then u32 checkTwo (sig+2 or sig+3), u32 contentSize, then records. Chunks
repeat until EOF (`GlobalVariables.kwd` = VARIABLES + THINGS chunks).
Timestamps are 10 bytes: u16 year, u8 month, u8 day, 2 unk, u8 h, u8 m, u8 s, pad.

KLD is the same container, level-scoped: 12 editor KWDs, 66 map KWDs and 330
KLDs, 408 files. `dk2tools kwd corpus` reproduces the file and record counts and
an install-gated regression test asserts them.

## Writer header evidence

CONFIRMED by a 2026-10-03 little-endian header dump of all 408 reference files
(409 chunks). Every chunkEndOffset equals the complete chunk length, relative
to that chunk's start. All 66 LEVEL chunks use checkTwo = sig+3; the other
343 use sig+2. General headers are 28 bytes and their unknown u32 is always 1;
MAP has an 8-byte dimensions header. TRIGGERS has a 32-byte header with two
counts and an unknown u32 of 1 at +8. LEVEL has a 28-byte header, a u16 count
and u16 value at +0/+2, and 10649 at +4. All LEVEL files understate contentSize;
their remaining bytes belong to that chunk. Created timestamps contain varied
opaque bytes; GlobalVariables' VARIABLES chunk has an entirely zero created
timestamp. Q529 tracks
new-header encoding; ADR-0346 requires a template.

## Update 2026-09-04 (every record type's size, and two that have none)

**Method:** read the field *order and widths* out of OpenKeeper's reader, sum
them (arithmetic over a field list is fact, not code — `external-resources.md`
holds the licence boundary), and check the total against `contentSize /
itemCount` from the shipped files. Two independent routes to one number.

`KwdRecordRoundTrip.Strides` holds fourteen fixed strides; MAP's 4-byte tile and
VARIABLES' 16-byte cell are two of them and are discussed separately below.
The twelve record-shaped strides, pinned by `DK2.Data.Tests.KwdRecordLayoutTests`
(install-gated):

| type | file | items | stride | field list agrees |
|---|---|---|---|---|
| TERRAIN | `Terrain.kwd` | 41 | **552** | ✓ |
| ROOMS | `Rooms.kwd` | 26 | **1055** | ✓ |
| CREATURES | `Creatures.kwd` | 46 | **5449** | **✗ — see below** |
| KEEPER_SPELLS | `KeeperSpells.kwd` | 13 | **406** | ✓ |
| TRAPS | `Traps.kwd` | 16 | **579** | ✓ |
| DOORS | `Doors.kwd` | 8 | **616** | ✓ |
| OBJECTS | `Objects.kwd` | 145 | **894** | ✓ |
| SHOTS | `Shots.kwd` | 65 | **239** | ✓ |
| PLAYERS | `<level>Players.kld` | 3–7 per level, 274 total | **205** | ✓ |
| CREATURE_SPELLS | `CreatureSpells.kwd` | 41 | **266** | ✓ |
| EFFECTS | `Effects.kwd` | 370 | **246** | ✓ |
| EFFECT_ELEMENTS | `EffectElements.kwd` | 596 | **182** | ✓ |

Eight of those sizes were new in this sweep (TRAPS, DOORS, OBJECTS, SHOTS,
PLAYERS, CREATURE_SPELLS, EFFECTS, EFFECT_ELEMENTS) and each matched to the byte
on the first attempt. A field list is a hundred-odd widths in sequence, and a sum
that lands exactly on an independently measured stride cannot hide a wrong field
that another wrong field cancels — not eight times running. The layouts are
right, not just the sizes.

All 18 record types now have typed readers and **every record image round-trips
byte for byte** across the corpus (ADR-0091; `KwdCorpusAudit`, `kwd corpus`).
The typed projection also round-trips for CREATURE_SPELLS, SHOTS, EFFECTS,
EFFECT_ELEMENTS, THINGS, TRIGGERS and LEVEL; for the rest the byte promise rests
on the record image and the typed reader exposes gameplay fields only.

MAP needs no per-record stride derivation: it is `width × height` tiles of 4
bytes, CONFIRMED, and its `itemCount` is meaningless (ADR-0031 for what the
third of those bytes does).

### VARIABLES is a tagged union in a fixed cell

`Variables` is 16 bytes: `u32 id` and 12 bytes whose meaning the id picks
(creature pool, availability, sacrifice, plain value…). Summing the field list
gives **100**, every branch of the union added together; the install gives
**16**. The one place the method returns nonsense rather than a near miss, so
the cross-check is not optional and the direction of authority is install first.

### THINGS and TRIGGERS are self-describing

Both are `u32 tag; u32 payloadSize; u8[payloadSize]` per record, so a chunk
splits into records without knowing a single payload layout — the opposite of
every other type here.

Validated structurally over the whole corpus. Across all 133 tagged chunks the
install ships (two per campaign level plus one in `GlobalVariables.kwd`),
walking `itemCount` records consumes each chunk **to the byte**: 6,318 THINGS
records and 9,161 TRIGGERS records, none left over and none short. The
structural split is `DK2.Data.Kwd.KwdTaggedRecords`; `KwdThings` and
`KwdTriggers` layer over it to decode payload fields while retaining every byte.

Payloads are **fixed-size per tag**, campaign-wide — THINGS is eleven fixed-size
record types multiplexed, which is why a stride check could never have found it.
Counts reproduce with `dk2tools kwd things` (66 map files, 6,279 records) plus
the 39 cameras in `GlobalVariables.kwd`:

| tag | OpenKeeper name | records | payload | field list agrees |
|---|---|---|---|---|
| 194 | Object | 788 | 24 | ✓ |
| 195 | Trap | 959 | 16 | ✓ |
| 196 | Door | 940 | 20 | ✓ |
| 197 | ActionPoint | 1152 | 56 | ✓ |
| 198 | NeutralCreature | 263 | 24 | ✓ |
| 199 | GoodCreature | 1025 | 32 | ✓ |
| 200 | KeeperCreature | 656 | 28 | ✓ |
| 201 | HeroParty | 220 | **555** | ✓ |
| 203 | EffectGenerator | 1 | 36 | ✓ |
| 204 | Room | 275 | 20 | ✓ |
| 205 | Camera | 39 | 84 | ✓ |
| 202 | DeadBody | **0** | 16 | field list only |

Eleven of twelve confirmed from both directions, including the Hero Party's
peculiar 555 — a number nothing but a correct field list produces. The dead body
is unconfirmed because **no shipped file places one**; its 16 bytes rest on
OpenKeeper's field list alone, marked as such in `KwdTaggedRecords`
(`InShippedData = false`) and open as Q221.

TRIGGERS carries two tags, `Generic` (213) and `Action` (214), both 16 bytes
everywhere. Both expose the eight-byte type-dependent body, the common
ids/type/repeat fields and type-aware parameter views. Those **parameter widths
and names follow DKII.EXE's own reads, not the editor labels** — several
transcribed names were false and many named bytes have no reader in the
executable (ADR-0092, `trigger-semantics.md`). Reproduce with
`dk2tools kwd trigger-types`, `kwd trigger-params` and `kwd triggers`.

**Two traps, both worth keeping:**

- **A whole stride does not mean a fixed-stride type.** `GlobalVariables.kwd`'s
  THINGS chunk is 39 cameras at 92 bytes each (84 payload + 8 header), so
  `contentSize / itemCount` divides perfectly and means nothing.
- **A sweep is only as wide as its glob.** The first pass swept
  `maps/*Things.kld` and concluded tags 202 and 205 appear nowhere. Widening it
  to the whole editor corpus turned up 39 cameras immediately. The test now
  sweeps every `.kwd` and `.kld`, and says why in a comment.

### The one disagreement: CREATURES

OpenKeeper's CREATURES field list totals **5377**; the shipped record is
**5449**. Their reader consumes 5377 bytes and its own `checkOffset` skips the
remaining 72 with a warning — so their layout is not a complete description of
1.70 data, and it is the one record here where our own scan-derived offsets go
further than theirs.

- **It costs nothing today.** `KwdCreatures` reads nothing past `0x1081`, and
  every offset it does read was cross-checked against OpenKeeper's cumulative
  order in this same sweep — attractions `0xDAC`, spells `0xDC4`, resistances
  `0xE0C`, the three job-preference blocks at `0xE14`/`0xE38`/`0xE50`, hate jobs
  `0xE74`, height `0xEA4`, creature id `0xF4B`, torture HP `0x107F`. Every one
  agreed. The disagreement is confined to the tail past `0x1501`.
- **It is a hazard for whoever extends that parser.** The 72 bytes between
  `0x1501` and the record end are described by nobody. OpenKeeper has a
  conditional 88-byte tail it reads when `itemSize > 5536`, which our data does
  not trigger; that region is *near* the gap and is not the same shape as it.
  Recorded as **Q70**.

These sources locate fields; only the install says whether the description is
complete.

Confidence: **CONFIRMED** (all twelve strides, the tagged-record shape, eleven of
twelve THINGS payload sizes) / **HYPOTHESIS** (dead-body payload 16, unobserved).

Implementation: `DK2.Data.Kwd.KwdTaggedRecords`, `KwdThings`, `KwdTriggers`,
`KwdAdditionalRecords`, `KwdLevel`, `KwdRecordRoundTrip`, `KwdCorpusAudit`;
`dk2tools kwd chunks`, `kwd tagged`, `kwd records`, `kwd things`, `kwd triggers`
and `kwd corpus`. Full fields: `kwd-record-fields.md`.

Tests: `DK2.Data.Tests.KwdRecordLayoutTests`, `KwdAdditionalRecordTests`.
