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

Reference install fingerprint (recorded 2026-08-29):
- GOG release, game id 1207658959
- DKII.EXE: 2,987,568 bytes, MD5 2f3bbc21eae8c3aa468136baa303036d
- DKII-DX.exe: 2,987,568 bytes, MD5 b9709c6c9084d3060e4ac7ac9764c70b
- Data/editor/ present (13 standalone game-database KWDs + 71 map KWDs)
