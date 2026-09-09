# original-reference/

Local reference material. **Everything here except this file and `research/` is
gitignored and must never be committed.**

- The Dungeon Keeper 2 installation stays at its install location
  (`E:\GogGames\Dungeon Keeper 2`, resolved via `DK2_DIR` or `Dk2Install`). Do not
  copy it into the repo.
- `research/` holds pinned third-party research *data* (e.g. Flame's
  `DKII_EXE_v170` symbol maps) with recorded source commit hashes — data files
  describing DK2's binary layout, not source code. See ADR-0003 before adding
  anything here.
- **Reference checkouts live outside this repository.** Keeping the whole clone on
  another path removes the possibility of staging it by accident.

| What | Where | Pinned at | Licence |
|---|---|---|---|
| **Flame** | `D:\Flame` | `f6e2a3d385bf2e6e95dc70cab88aa387bb7659d9` (2026-03-01) | **none — no reuse rights whatsoever** |
| **OpenKeeper** | `D:\OpenKeeper` | tracks `origin/master` | GPLv3 |
| DK2 v1.70 install | `E:\GogGames\Dungeon Keeper 2` | GOG 1207658959 | EA proprietary |

**Flame's boundary is the strictest one here.** Unlicensed means default copyright:
nothing may be copied, and that is unaffected by this project being GPLv3. Consult
it, describe what you learn in this project's own words, and **never paste one of
its identifiers into the repository** — a field offset is a fact about DK2, but
`g_stateGroups` is DiaLight's choice of words, and the OpenKeeper precedent (names
used under ADR-0003's second amendment, attributed in `THIRD-PARTY-NOTICES.md`) does
not transfer, because it rests on a licence Flame does not have.
`docs/research/external-resources.md` is the full table.

Reference install fingerprint (recorded 2026-08-29):

- GOG release, game id 1207658959
- DKII.EXE: 2,987,568 bytes, MD5 2f3bbc21eae8c3aa468136baa303036d
- DKII-DX.exe: 2,987,568 bytes, MD5 b9709c6c9084d3060e4ac7ac9764c70b
- Data/editor/ present (13 standalone game-database KWDs + 71 map KWDs)

## Building the reference checkouts (2026-09-07)

**Flame builds.** Its readme asks for Visual Studio 2022; the 32-bit `v14.51`
toolset in Visual Studio 18 Community compiled all 287 objects unchanged, with nine
warnings and no errors, in under three minutes for configure, compile and install
together. From a `cmd` prompt (the readme is explicit that its steps are not for
PowerShell), with CMake and Ninja taken from Visual Studio's own
`Common7\IDE\CommonExtensions\Microsoft\CMake` tree:

```bat
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat"
cd /d D:\Flame
mkdir build & cd build
cmake -DCMAKE_BUILD_TYPE=Debug -GNinja -DCMAKE_INSTALL_PREFIX=../install ..
cmake --build .
cmake --install .
```

The configure step creates `D:\Flame\.venv` and installs `capstone` into it; the
install step writes `D:\Flame\install\PATCH.dll`, `flame\Flame.dll`, `flame\DKII.dll`
and their `.pdb`s. All of it is inside Flame's own gitignore, so the clone stays
clean. The Debug build is what compiled; Release was not tried.

**Two things deliberately not done.** `-DDK2_DIR=...` was not passed and nothing was
copied into `E:\GogGames\Dungeon Keeper 2`: Flame's install replaces the game's
`PATCH.dll`, which changes the oracle. Running DK2 under Flame is M5's first step and
should be taken on purpose, with the original `PATCH.dll` backed up first — the
running install is the oracle (`AGENTS.md`, source order item 3), and an instrumented
oracle is a different thing from the shipped one.

**Compiling Flame grants nothing.** The boundary above is unchanged by having a
`Flame.dll` on disk: it is DiaLight's code, built for our use as a research
instrument, and none of it, its identifiers included, enters this repository.

**OpenKeeper compiles.** `gradlew.bat compileJava` from `D:\OpenKeeper`, called by
full path from `cmd` (a bare `gradlew.bat` was not found from the batch file). The
wrapper fetches Gradle 9.6.1, and because `build.gradle` asks for a Java 25 toolchain
while only JDK 26 is installed, the foojay resolver in `settings.gradle` downloads
Eclipse Temurin 25 into `%USERPROFILE%\.gradle\jdks` (about 425 MB). `BUILD
SUCCESSFUL in 1m 52s`, 62 deprecation warnings, no errors, classes under
`D:\OpenKeeper\build`. Gradle logs a file-watching error on `D:\OpenKeeper` at
start-up and carries on without the watcher; it is noise. Only `compileJava` was run
— the full `build` with tests was not, and the application was not launched.
