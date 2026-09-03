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
  all 12 editor KWD files.

Observed behaviour: each KWD file begins with a little-endian int32 record-type id.

Hypothesis: remainder of header (bytes 4..0x1B) is size/count/timestamp metadata
per the dk2-tools template — to be confirmed while implementing the parser.

Confidence: CONFIRMED (record-type id) / HYPOTHESIS (rest of header)

Implementation: `DK2.Data.Model.KwdRecordType`

Tests: `DK2.Data.Tests.KwdSmokeTests`

Known exceptions: none yet.

Open questions: KLD relationship to KWD (same container, level-local data?).

## Update 2026-08-29 (parser implemented)

Header layout CONFIRMED (KwdReader): 5 x u32 fixed header (sig, byteSize=4,
chunkEndOffset, checkOne=sig+1, headerBlockSize), then headerBlockSize bytes of
type-specific header (general: u32 itemCount, u32 unk, 2 x 10-byte timestamp),
then u32 checkTwo (sig+2 or sig+3), u32 contentSize, then records. Chunks
repeat until EOF (GlobalVariables.kwd = VARIABLES + THINGS chunks).
Timestamps are 10 bytes: u16 year, u8 month, u8 day, 2 unk, u8 h, u8 m, u8 s, pad.
Full corpus parses: 13 editor KWDs, 71 map KWDs, 330 KLDs.

## Update 2026-09-04 (every record type's size, and two that have none)

The pick-up list said to sweep the unparsed record layouts against OpenKeeper
before M6 needs them. This is the result. **Method:** read the field *order and
widths* out of OpenKeeper's reader, sum them (arithmetic over a field list is
fact, not code — `docs/research/external-resources.md` holds the licence
boundary), and check the total against `contentSize / itemCount` from the
shipped files. Two independent routes to one number, and the number is either
the same or it is not.

**Eleven record types have a fixed stride, and it is now pinned by a test**
(`DK2.Data.Tests.KwdRecordLayoutTests`, install-gated):

| type | file | items | stride | field list agrees | read by us |
|---|---|---|---|---|---|
| TERRAIN | `Terrain.kwd` | 41 | **552** | ✓ | parsed |
| ROOMS | `Rooms.kwd` | 26 | **1055** | ✓ | parsed |
| CREATURES | `Creatures.kwd` | 46 | **5449** | **✗ — see below** | parsed |
| KEEPER_SPELLS | `KeeperSpells.kwd` | 13 | **406** | ✓ | two fields |
| TRAPS | `Traps.kwd` | 16 | **579** | ✓ | one field (Q59) |
| DOORS | `Doors.kwd` | 8 | **616** | ✓ | — M6 |
| OBJECTS | `Objects.kwd` | 145 | **894** | ✓ | — M6 |
| SHOTS | `Shots.kwd` | 65 | **239** | ✓ | — M6 |
| PLAYERS | `<level>Players.kld` | 4 | **205** | ✓ | — M6 |
| CREATURE_SPELLS | `CreatureSpells.kwd` | 41 | **266** | ✓ | — M7 |
| EFFECTS | `Effects.kwd` | 370 | **246** | ✓ | — M7 |
| EFFECT_ELEMENTS | `EffectElements.kwd` | 596 | **182** | ✓ | — M7 |

**Eight of those sizes are new** (TRAPS, DOORS, OBJECTS, SHOTS, PLAYERS,
CREATURE_SPELLS, EFFECTS, EFFECT_ELEMENTS) and every one matched to the byte on
the first attempt. That is stronger than it sounds: a field list is a hundred-odd
widths in sequence, and a sum that lands exactly on an independently measured
stride cannot have a wrong field in it that another wrong field cancels out —
not eight times running. **The layouts are right, not just the sizes.**

MAP is the exception that needs no stride: it is `width × height` tiles of 4
bytes, already CONFIRMED, and its `itemCount` is meaningless (see ADR-0031 for
what the third of those bytes does).

### VARIABLES is a tagged union in a fixed cell

`Variables` is 16 bytes: `u32 id` and 12 bytes whose meaning the id picks
(creature pool, availability, sacrifice, plain value…). Summing the field list
gives **100**, being every branch of the union added together, and the install
gives **16**. Worth recording as the one place the method returns nonsense
rather than a near miss — the cross-check is not optional, and the direction of
authority is always install first.

### THINGS and TRIGGERS are self-describing

Both are `u32 tag; u32 payloadSize; u8[payloadSize]` per record. **The chunk can
therefore be split into records without knowing a single payload layout**, which
is the opposite of every other type here.

Validated the way everything else is — structurally, over the whole corpus.
Across all 133 tagged chunks the install ships (two per campaign level plus one
in `GlobalVariables.kwd`), walking `itemCount` records consumes each chunk **to
the byte**: 6,318 THINGS records and 9,161 TRIGGERS records, none left over and
none short. Implemented as `DK2.Data.Kwd.KwdTaggedRecords`, which deliberately
stops at the tag and the payload bytes; decoding a payload's fields is M6 work.

And the payloads are **fixed-size per tag**, campaign-wide — so THINGS is really
eleven fixed-size record types multiplexed, which is precisely why a stride check
could never have found it:

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
peculiar 555 — a number nothing but a correct field list produces. Only the dead
body is unconfirmed, because **no shipped file places one**; its 16 bytes rest on
OpenKeeper's field list alone and are marked as such in the code.

TRIGGERS carries two tags, `Generic` (213) and `Action` (214), and both are 16
bytes everywhere. A generic trigger's payload is a tagged union of its own inside
those 16 bytes, so the size does not describe it — that is M6's problem.

**Two traps found the hard way, both worth keeping:**

- **A whole stride does not mean a fixed-stride type.** `GlobalVariables.kwd`'s
  THINGS chunk is 39 cameras at 92 bytes each, so `contentSize / itemCount`
  divides perfectly and means nothing.
- **A sweep is only as wide as its glob.** The first pass swept
  `maps/*Things.kld` and concluded tags 202 and 205 appear nowhere. Widening it
  to the whole editor corpus turned up 39 cameras immediately. The test now
  sweeps every `.kwd` and `.kld`, and says why in a comment.

### The one disagreement: CREATURES

OpenKeeper's CREATURES field list totals **5377**; the shipped record is
**5449**. Their reader consumes 5377 bytes and its own `checkOffset` skips the
remaining 72 with a warning — so **their layout is not a complete description of
1.70 data**, and it is the one record here where our own scan-derived offsets go
further than theirs.

Two things make this worth writing down rather than shrugging at:

- **It costs us nothing today.** `KwdCreatures` reads nothing past `0x1081`, and
  every offset it does read was cross-checked against OpenKeeper's cumulative
  order in this same sweep — attractions `0xDAC`, spells `0xDC4`, resistances
  `0xE0C`, the three job-preference blocks at `0xE14`/`0xE38`/`0xE50`, hate jobs
  `0xE74`, height `0xEA4`, creature id `0xF4B`, torture HP `0x107F`. **Every one
  agreed.** The disagreement is confined to the tail, past `0x1501`.
- **It is a live hazard for whoever extends that parser.** The 72 bytes between
  `0x1501` and the record end are described by nobody. OpenKeeper has a
  conditional 88-byte tail it reads when `itemSize > 5536`, which our data does
  not trigger; that region is *near* the gap and is not the same shape as it.
  Recorded as **Q70**.

The general lesson is the one this project keeps relearning from a new angle:
these sources locate fields, and only the install says whether the description is
complete. Eight sizes confirmed in an afternoon, and the ninth was wrong — and
the ninth would have been believed without the cross-check.

Confidence: **CONFIRMED** (all twelve strides, the tagged-record shape, eleven of
twelve THINGS payload sizes) / **HYPOTHESIS** (dead-body payload 16, unobserved).

Implementation: `DK2.Data.Kwd.KwdTaggedRecords`, `dk2tools kwd chunks` (now
prints the stride) and `dk2tools kwd tagged`.

Tests: `DK2.Data.Tests.KwdRecordLayoutTests`.
