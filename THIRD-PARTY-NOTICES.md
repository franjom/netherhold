# Third-party notices

This project is licensed under the GNU GPL version 3 (see LICENSE, ADR-0041).
Everything listed below is GPL-compatible and keeps its own licence and
attribution; the MIT licence in particular permits inclusion in a GPLv3 work,
provided its notice is retained, which is what this file and the per-file
comments do.

## GLKeeper
- https://github.com/codenamecpp/GLKeeper — MIT License, Copyright (c) codenamecpp
- `src/DK2.Data/Wad/WadDecompressor.cs` adapts the WAD entry decompression
  algorithm from GLKeeper's `src/DK2WADArchive.cpp`.

## Format research (facts only, no code copied)
- ufdada/dk2-tools (010 Editor binary templates) — WAD/KWD container layouts.
- DiaLight/Flame — DKII.EXE v1.70 structure and symbol research.
- tonihele/OpenKeeper (GPLv3) — consulted as documentation only; no code
  copied or transliterated (see docs/DECISIONS.md ADR-0003).

## YamlDotNet
- https://github.com/aaubry/YamlDotNet — MIT License, Copyright (c) Antoine Aubry
  and contributors
- Used by `DK2.Harness` to read scenario files. Tooling only — no simulation
  assembly references it.
