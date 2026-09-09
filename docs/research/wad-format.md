# Format: WAD archive format ("DWFB" v2) + entry compression

Source(s):
- ufdada/dk2-tools `Formats/Archives/wad_struct.bt` (container layout facts)
- GLKeeper `src/DK2WADArchive.cpp` (MIT — decompression algorithm, adapted with
  attribution; see THIRD-PARTY-NOTICES.md)
- Direct inspection + exhaustive extraction test on the reference install

Original version: DK2 v1.70 (GOG)

## Container layout — CONFIRMED

```
0x00  char[4]  signature "DWFB"
0x04  u32      version (always 2)
0x08  u8[64]   zeros
0x48  u32      entry count
0x4C  u32      absolute offset of the name block
0x50  u32      name block size
0x54  u32      unknown
0x58  entries[count], 40 bytes (9 dwords) each:
        u32 unk1, u32 nameOffset (absolute), u32 nameSize,
        u32 dataOffset, u32 storedSize, u32 compressionType (0|4),
        u32 uncompressedSize, u32 unk2[3]
```

Evidence:
- All five archives (`EngineTextures`, `FrontEnd`, `Meshes`, `Paths`, `Sprite`)
  parse and every entry extracts to exactly its declared uncompressed size —
  `WadArchiveTests.EveryEntry_Extracts_ToDeclaredSize` runs the full corpus of
  roughly 2,600 entries, and the decompressor throws on any size mismatch.
- Extracted `.kmf` entries begin with 4CC `KMSH`.
- The **40-byte** entry stride is verified by anchor: entry N's `nameOffset`
  always equals `namesOffset` + the sum of the previous `nameSize`s. The 010
  template's field list is correct; note `unk2[3]` is 12 trailing bytes.
- Name offsets are absolute file offsets into the name block; names are
  NUL-terminated ASCII, often containing backslash paths, and every name in the
  corpus is printable ASCII.
- Compression type: 0 = stored, 4 = compressed. No other value occurs.

## Compression — CONFIRMED

An LZ77-family token stream.

- Prelude: 1 flags byte (bit0 ⇒ skip 3 bytes), 1 skipped byte, then 3-byte
  **big-endian** uncompressed size (self-check against the directory).
- Tokens by flag byte high bits: three back-reference classes (2/3/4-byte
  tokens with 0–3 leading literals) plus a literal-run class; a run token with
  count > 0x70 terminates the stream, carrying `flag & 3` final literals.
- Exact loop semantics preserved from GLKeeper (do-while copies count+1 bytes).

Confidence: CONFIRMED (empirically, full corpus)

Implementation: `DK2.Data.Wad.WadArchive`, `DK2.Data.Wad.WadDecompressor`;
`dk2tools wad list <file.WAD>` and `dk2tools wad extract <file.WAD> <entry>`

Tests: `DK2.Data.Tests.WadArchiveTests` — two install-gated theories over all
five archives (parse-and-sanity, exhaustive extraction) and one fact that
extracted `Meshes.WAD` entries are KMF.

Known exceptions: none observed.

Open questions: the meaning of per-entry dword 0 (`unk1`) and the trailing
`unk2[3]`; harmless for reading.
