# Legal and Clean Room

How the project uses the original game and other people's work without
redistributing either. The rules are in four ADRs: ADR-0003, ADR-0041, ADR-0046
and ADR-0080. The licence of every source is tabulated in
[`external-resources.md`](https://github.com/franjom/netherhold/blob/master/docs/research/external-resources.md).

*This page describes the project's rules. It is not legal advice.*

## The project's licence

The source is **GPL-3.0-or-later**
([`LICENSE`](https://github.com/franjom/netherhold/blob/master/LICENSE),
ADR-0041). Every source file opens with a three-line notice, and a test fails
the build if a file lacks it:

```
// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
```

Third-party code and what was taken from it are listed in
[`THIRD-PARTY-NOTICES.md`](https://github.com/franjom/netherhold/blob/master/THIRD-PARTY-NOTICES.md).

## The original game

- **No original files in the repository.** Nothing from a DK2 installation is
  committed: no data, art, sound or text. The game reads everything at run time
  from your own installation (`DK2_DIR`), through the asset layer.
- **Original assets may be used for development and reference**, but they are
  never assumed to be redistributable.
- Dungeon Keeper is a trademark of its owner. This project is not affiliated
  with or endorsed by Electronic Arts or Bullfrog Productions.

## The binary: facts may cross, code may not (ADR-0046)

`DKII.EXE` may be decompiled. It is the primary source for how the game
behaves. The line is drawn between **fact** and **expression**:

1. **Facts may cross**, in our own words, into `docs/research/`: formulas,
   thresholds, comparisons, orders of operation, descriptions of control flow,
   addresses. Each one names the binary and the address it came from.
2. **Decompiler output never crosses**: not into source, documents, commit
   messages or test fixtures, and not renamed, reformatted or tidied. It stays in
   the git-ignored `reverse-engineering/` folder on the researcher's machine.
3. **A read of the binary is STRONGLY INFERRED, never CONFIRMED.** CONFIRMED is
   reserved for the running game (see [[Glossary]]).

The reason: decompiler output is code derived from the original. A behaviour
re-expressed in our own words is not. Nothing derived from the binary is
distributed, which is the same stance the project takes on the game's assets.

The method is in
[`reverse-engineering/README.md`](https://github.com/franjom/netherhold/blob/master/reverse-engineering/README.md).

## Other projects

| Source | Licence | What we may take |
|---|---|---|
| **OpenKeeper** | GPLv3 | Facts, and implementation where it answers what the shipped data cannot (ADR-0003, as amended 2026-09-04). Every borrowed piece is listed in `THIRD-PARTY-NOTICES.md`, naming the upstream class. A rule taken from it is never CONFIRMED: it is itself a reimplementation and can be wrong. |
| **Flame** (a v1.70 symbol map) | none | Facts only. A Flame identifier never appears in `src/`, `tests/` or the Unity project, not even in a comment; `FlameIdentifierTests` checks it. A document may cite a Flame name as Flame's (ADR-0080). |
| **The Dungeon Keeper wiki on Fandom** | CC BY-SA | Facts, in our own words, with a citation. Its prose is never committed: not as a page, a dump or a long quote. CC BY-SA text cannot be redistributed under the GPL. A short attributed quotation is fine where the exact wording is the evidence ([`wiki-as-a-source.md`](https://github.com/franjom/netherhold/blob/master/docs/research/wiki-as-a-source.md)). |
| Other DK2 reverse-engineering documents | varies | Facts with citations, as for the wiki. |

The order these sources are consulted in, binary first, is in
[`AGENTS.md`](https://github.com/franjom/netherhold/blob/master/AGENTS.md#existing-research).
