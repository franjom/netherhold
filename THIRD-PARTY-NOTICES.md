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
- **Creature field ordering (facts only):** `KwdFile.java` at
  `fb70fbc404361f271e5ff43eb5e0ec29ae5a4d5d` corroborated Q526's adjacent fields;
  the correction is grounded in DKII.EXE. No code copied.
- **Disarming review (facts only):** `Creature`, `Trap`, `Settings` and
  `PossessionCameraState` at `fb70fbc404361f271e5ff43eb5e0ec29ae5a4d5d` were
  consulted for the independent cross-check in `docs/research/traps.md` §11.
  No code copied; reachability conclusions come from DKII.EXE.
- **KMF format research (facts only, no code copied):** chunk hierarchy,
  material strings and numeric fields, static and animated vertex layouts,
  detail-level triangle lists, packed animation geometry, UV scaling, and the
  `ITAB`/`VGEO` per-frame lookup — including its indexing of one base table per
  block of 128 frames, which reaches every frame rather than only frame zero —
  were consulted in OpenKeeper's
  `toniarts.openkeeper.tools.convert.kmf.KmfFile` and `KmfModelLoader`, then
  reimplemented in `src/DK2.Convert` in this project's own structure and idiom.
- **Engine texture format research (facts only, no code copied):** the `TCHC`
  directory and data-entry layouts, mipmap naming, coefficient decode tables,
  8×8 block transform, luma/chroma conversion, alpha handling, and the two
  shipped Goblin texture-name corrections were consulted in OpenKeeper's
  `EngineTexturesFile`, `Dk2TextureDecoder`, `EngineTextureDecoder`, and
  `KmfModelLoader`, then reimplemented in `src/DK2.Convert` in this project's
  own structure and idiom.
- **SDT format research (facts only, no code copied):** the count and offset
  table, 40-byte entry layout, fixed name field, sample metadata, sound-type
  values, and proposed type names were consulted in OpenKeeper's `SdtFile` and
  `SdtFileEntry`, checked against every shipped SDT and its MPEG frame headers,
  then independently implemented in `src/DK2.Convert`.
- **STR format research (facts only, no code copied):** the 12-byte header,
  header-relative offset origin, typed entry chunks, parameter numbering, and
  `MBToUni.dat` variable-width character indexing were consulted in
  OpenKeeper's `StrFile` and `MbToUniFile`, checked against all shipped English
  string tables, then independently implemented in `src/DK2.Data`.
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
- `src/DK2.Data/Kwd/KwdDoors.cs` and `KwdTraps.cs`, and the `DoorDef` and
  `TrapDef` field names, used the **field order and widths** of OpenKeeper's
  `toniarts.openkeeper.tools.convert.map.KwdFile.readDoors`/`readTraps` and the
  `Door`/`Trap` classes to generate candidate offsets — including reading
  `trapId` and `shotTypeId` as adjacent unsigned bytes. Every candidate was then
  confirmed against the shipped bytes and, for all seventeen prices, against the
  original manual; where the two disagreed the shipped bytes won. No code was
  copied. Recorded because GPLv3 attribution is an obligation rather than a
  courtesy — see ADR-0003 (second amendment).

## Format research (facts only, no code copied)
- ufdada/dk2-tools (010 Editor binary templates) — WAD/KWD container layouts.
- DiaLight/Flame — DKII.EXE v1.70 structure and symbol research. Used on
  2026-09-06 to locate the saved-game code: which vtable slot the save GUI
  dispatches through, and the addresses and owning classes of the world's
  serialiser and its per-subsystem writers. **Addresses and structural roles
  only.** The write order and the field layout in
  `docs/research/sav-format.md` were read from the executable itself, and
  every offset was then confirmed against two real save files. No Flame code
  appears anywhere in this repository, and no Flame identifier appears in the
  source tree, its tests or the Unity adapter — the project holds no reuse
  rights to it at all (AGENTS.md). Planning and research documents **cite**
  Flame's names for a structure where that is the clearest way to point at the
  source; a name is not a copyrightable work and a citation is not a copy
  (ADR-0080). `FlameIdentifierTests` enforces the source-tree half.

## YamlDotNet
- https://github.com/aaubry/YamlDotNet — MIT License, Copyright (c) Antoine Aubry
  and contributors
- Used by `DK2.Harness` to read scenario files. Tooling only — no simulation
  assembly references it.
