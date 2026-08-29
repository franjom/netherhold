# Mechanic: WAD archive format ("DWFB" v2) + entry compression

Source(s):
- ufdada/dk2-tools `Formats/Archives/wad_struct.bt` (container layout facts)
- GLKeeper `src/DK2WADArchive.cpp` (MIT — decompression algorithm, adapted with
  attribution; see THIRD-PARTY-NOTICES.md)
- Direct inspection + exhaustive extraction test on the reference install

Original version: DK2 v1.70 (GOG)

Evidence:
- All five archives (`EngineTextures`, `FrontEnd`, `Meshes`, `Paths`, `Sprite`)
  parse and every entry extracts to exactly its declared uncompressed size —
  `WadArchiveTests.EveryEntry_Extracts_ToDeclaredSize` runs the full corpus.
- Extracted `.kmf` entries begin with 4CC `KMSH`.
- Entry stride is **40 bytes** (9 dwords). The 010 template's field list is
  correct; note `unk2[3]` = 12 trailing bytes. Verified by anchor: entry N's
  nameOffset always equals namesOffset + sum of previous nameSizes.
- Name offsets are absolute file offsets into the name block; names are
  NUL-terminated ASCII, often containing backslash paths.
- Compression type field: 0 = stored, 4 = compressed. No other value occurs in
  the corpus.

Observed behaviour (compression): LZ77-family token stream.
- Prelude: 1 flags byte (bit0 ⇒ skip 3 bytes), 1 skipped byte, then 3-byte
  **big-endian** uncompressed size (self-check against directory).
- Tokens by flag byte high bits: three back-reference classes (2/3/4-byte
  tokens with 0–3 leading literals) + literal-run class; run token with
  count > 0x70 terminates the stream carrying `flag & 3` final literals.
- Exact loop semantics preserved from GLKeeper (do-while copies count+1 bytes).

Confidence: CONFIRMED (empirically, full corpus)

Implementation: `DK2.Data.Wad.WadArchive`, `DK2.Data.Wad.WadDecompressor`

Tests: `DK2.Data.Tests.WadArchiveTests` (12 tests incl. exhaustive extraction)

Known exceptions: none observed.

Open questions: meaning of per-entry dword 0 ("unk1") and trailing unk2[3];
harmless for reading.
