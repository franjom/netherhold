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
