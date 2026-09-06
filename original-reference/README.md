# original-reference/

Local reference material. **Everything here except this file and `research/`
is gitignored and must never be committed.**

- The Dungeon Keeper 2 installation stays at its install location
  (`E:\GogGames\Dungeon Keeper 2`, resolved via `DK2_DIR` or `Dk2Install`).
  Do not copy it into the repo.
- `research/` holds pinned third-party research *data* (e.g. Flame's
  DKII_EXE_v170 symbol maps) with recorded source commit hashes. Data files
  describing DK2's binary layout — not source code. See ADR-0003 before adding
  anything here.

**Reference checkouts live OUTSIDE this repository, which is better than what
risk R15 originally proposed.** R15 suggested vendoring Flame's `mapping/` into
`research/`; keeping the whole clone on another path removes even the
possibility of staging it by accident, and costs nothing.

| What | Where | Pinned at | Licence |
|---|---|---|---|
| **Flame** | `D:\Flame` | `f6e2a3d385bf2e6e95dc70cab88aa387bb7659d9` (2026-03-01) | **none — no reuse rights whatsoever** |
| **OpenKeeper** | `D:\OpenKeeper` | tracks `origin/master` | GPLv3 |
| DK2 v1.70 install | `E:\GogGames\Dungeon Keeper 2` | GOG 1207658959 | EA proprietary |

**Flame's boundary, because it is the strictest one here.** Unlicensed means
default copyright: nothing may be copied, and that is unaffected by this project
being GPLv3. Consult it, describe what you learn in this project's own words,
and **never paste one of its identifiers into the repository** — a field offset
is a fact about DK2, but `g_stateGroups` is DiaLight's choice of words, and the
OpenKeeper precedent (names used under ADR-0003's second amendment, attributed
in `THIRD-PARTY-NOTICES.md`) does not transfer, because it rests on a licence
Flame does not have. `docs/research/external-resources.md` is the full table.

Reference install fingerprint (recorded 2026-08-29):
- GOG release, game id 1207658959
- DKII.EXE: 2,987,568 bytes, MD5 2f3bbc21eae8c3aa468136baa303036d
- DKII-DX.exe: 2,987,568 bytes, MD5 b9709c6c9084d3060e4ac7ac9764c70b
- Data/editor/ present (13 standalone game-database KWDs + 71 map KWDs)
