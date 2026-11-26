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

## Godot Engine and .NET

- https://godotengine.org, the version `godot/godot-version.json` pins — MIT License, Copyright (c) 2014-present
  Godot Engine contributors, Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur. Its bundled components' notices
  are in Godot's `COPYRIGHT.txt`.
- The tester package (`tools/tester-package`) ships Godot's Windows export template, GodotSharp and the .NET runtime
  (https://github.com/dotnet/runtime, MIT License, Copyright (c) .NET Foundation and Contributors).

## Launcher fonts

- UnifrakturMaguntia (Copyright (c) 2010 j. 'mach' wust, Copyright (c) 2009 Peter Wiegel) and Alegreya Sans
  (Copyright 2013 The Alegreya Sans Project Authors), SIL Open Font License 1.1, unmodified from
  https://github.com/google/fonts (`ofl/unifrakturmaguntia`, `ofl/alegreyasans`).
- Used only by the Godot launcher, `godot/DK2.Godot/Launcher/`, beside their licences
  (`UnifrakturMaguntia-OFL.txt`, `AlegreyaSans-OFL.txt`).

## Research references

These projects were consulted for file formats, structure and behaviour.
The project's rule is facts only (ADR-0003). The exceptions below, taken
earlier, are being rewritten from the binary and leave this list when done.

- [OpenKeeper](https://github.com/tonihele/OpenKeeper) - GNU GPL version 3
  or later, Copyright (C) 2014-2016 OpenKeeper, Toni Helenius and contributors.
  Material derived from it that is still in this tree:
  - `src/DK2.Data/Kwd/KwdAdditionalRecords.cs`: field order, widths and names
    of creature spells, shots, effects and effect elements, from `KwdFile`.
  - `ObjectFlags` (`src/DK2.Data.Model/ObjectDef.cs`) and `CreatureStatId`:
    member names and order, from `GameObject.ObjectFlag` and
    `Variable.CreatureStats.StatType`.
  - `src/DK2.Data/Text/Dk2StringTable.cs`: the string-table reader's structure
    is close to `str.StrFile`.
  - `PortalRules.IsUnlocked` and `PoolLimit`: rules from
    `game.logic.CreatureSpawnSystem` (ADR-0042).
- [ufdada/dk2-tools](https://github.com/ufdada/dk2-tools) - binary-format
  research reference.
- [DiaLight/Flame](https://github.com/DiaLight/Flame) - structure and symbol
  research reference. Unlicensed; no code or identifiers reused in source,
  tests or the Godot adapter (ADR-0080).

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
  The shared decoder uses its dependency-free netstandard2.0 assembly. See ADR-0583.

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
