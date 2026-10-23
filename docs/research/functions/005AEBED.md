address: 0x005AEBED
name: Movie_TqiMacroblock16
role: One TQI macroblock to a 16-bit surface (byte 7 bit 1 set, no width doubling): decodes the six blocks (0x005AE220) into a stack buffer, then converts 16 rows with 0x005AF7E0, passing chroma row r/2 for luma row r.
confidence: CONFIRMED
status: READ
evidence: docs/research/movie-format.md

Hand-written assembly; loads the bit reader base/position from the decoder (+0x608/+0x60C) into EBP/EDI around 0x005AE220.

## History
- 2026-10-23 claude: Read (disassembly) in the movie decoder pass; record created.
