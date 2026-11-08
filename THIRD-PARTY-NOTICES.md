# Third-party notices

This project is licensed under the GNU GPL version 3 (see LICENSE, ADR-0041).
Included or adapted third-party software retains its licence and attribution.
Projects consulted for research are acknowledged separately below; detailed
research provenance belongs in `docs/research/`, not in this notice file.

## .NET agent skills

- Selected skills from https://github.com/dotnet/skills, MIT License,
  Copyright (c) .NET Foundation and Contributors.
- Source revision, selection and project adaptations:
  [.agents/skills/README.md](.agents/skills/README.md).
- Full upstream notice: [.agents/skills/dotnet-skills.LICENSE](.agents/skills/dotnet-skills.LICENSE).

## GLKeeper
- https://github.com/codenamecpp/GLKeeper — MIT License, Copyright (c) codenamecpp
- `src/DK2.Data/Wad/WadDecompressor.cs` adapts the WAD entry decompression
  algorithm from GLKeeper's `src/DK2WADArchive.cpp`.

## Research references

These projects were consulted for file formats, structure and behaviour.
No code was copied from them; implementations were written independently.

- [OpenKeeper](https://github.com/tonihele/OpenKeeper) - GNU GPL version 3
  or later, Copyright (C) 2014-2016 OpenKeeper, Toni Helenius and contributors.
  Also informed the creature-generation logic in `PortalRules.cs` and
  `Systems/PortalSystem.cs`, independently reimplemented from OpenKeeper's
  `toniarts.openkeeper.game.logic.CreatureSpawnSystem` (ADR-0003, ADR-0042).
- [ufdada/dk2-tools](https://github.com/ufdada/dk2-tools) - binary-format
  research reference.
- [DiaLight/Flame](https://github.com/DiaLight/Flame) - structure and symbol
  research reference. Unlicensed; no code or identifiers reused in source,
  tests or the Unity adapter (ADR-0080).

## YamlDotNet
- https://github.com/aaubry/YamlDotNet — MIT License, Copyright (c) Antoine Aubry
  and contributors
- Used by `DK2.Harness` to read scenario files and by `DK2.Tools` to read
  `docs/systems.yaml`. Tooling only — no simulation
  assembly references it.

## Roslyn (Microsoft.CodeAnalysis.CSharp)
- https://github.com/dotnet/roslyn, version 4.14.0 — MIT License, Copyright (c) .NET Foundation
  and Contributors
- Used by `DK2.Tools` (`dk2tools simmatrix`) to compile the simulation's source and read which
  systems read and write which state. Tooling only — no simulation assembly references it.

## NLayer

- https://github.com/naudio/NLayer, version 3.0.0, MIT licence, Copyright (c) 2018 Mark Heath, Andrew Ward & Contributors.
- Runtime MPEG Layer I/II audio decoder, used only behind `DK2.Data.Media.MpegAudioDecoder`.
  Unity loads its dependency-free netstandard2.0 assembly. See ADR-0583.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
