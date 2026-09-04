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

## OpenKeeper
- https://github.com/tonihele/OpenKeeper — GNU GPL version 3 or later,
  Copyright (C) 2014–2016 OpenKeeper, Toni Helenius and contributors.
- **Format research (facts only, no code copied):** KWD/KLD record layouts,
  field offsets and enumerated value meanings, used as documentation and
  cross-checked against the shipped files.
- **Behavioural research, and one place where the debt is more than facts:**
  `src/DK2.Simulation/PortalRules.cs` and
  `src/DK2.Simulation/Systems/PortalSystem.cs` reimplement, in our own
  structure and idiom, the creature-generation logic of OpenKeeper's
  `toniarts.openkeeper.game.logic.CreatureSpawnSystem` — in particular the
  spawn-interval scaling `max(base, base × creatures ÷ 2)`, the reading of
  `CreaturePool` as a standing per-type limit, the requirement that a creature
  satisfy *every* attraction slot against a *single* room of the stated size,
  and the reading of the creature count once per tick. None of those is derived
  from the shipped data files; each was read out of OpenKeeper's source.
  Recorded here because GPLv3 attribution is an obligation rather than a
  courtesy. See docs/DECISIONS.md ADR-0003 (second amendment) and ADR-0042, and
  docs/research/portals.md for what each inference rests on.
- `PlayerIds` corroborates its four reserved ids against OpenKeeper's
  `toniarts.openkeeper.tools.convert.map.Player` constants.

## Format research (facts only, no code copied)
- ufdada/dk2-tools (010 Editor binary templates) — WAD/KWD container layouts.
- DiaLight/Flame — DKII.EXE v1.70 structure and symbol research.

## YamlDotNet
- https://github.com/aaubry/YamlDotNet — MIT License, Copyright (c) Antoine Aubry
  and contributors
- Used by `DK2.Harness` to read scenario files. Tooling only — no simulation
  assembly references it.
