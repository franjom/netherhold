# Original-game tools: which one for which job

Start here before recording, replaying or capturing the original game. Each line names the one tool for a job, its
limit, and the page with its options and traps. Do not guess a tool's arguments or a packet's fields: the pages
below hold them, with the evidence. The overlapping tools were merged on 2026-11-16
(`docs/plans/2026-11-16-capture-tools-consolidation.md`).

All native tools need an elevated shell (DK2 runs elevated) and no other DK2 process. Evidence goes under
`F:\DK2Evidence\`, add-only: every output path must be new.

| Job | Tool | Limit | Page |
|---|---|---|---|
| Record the owner playing a level | `play-record.ps1` | DK2's own packet recorder; inputs, seeds, checksums, camera, no state | [play-record.md](play-record.md) |
| Read a recording | `play-record.ps1 -Decode` | `thing` ids are the original process's, not types | [play-record.md](play-record.md) |
| Every turn's state from a replay, undisturbed | `play-record.ps1 -Replay -Map WxH` (runs `reverse-engineering/dk2live.py record`) | legacy capture: selected regions only, object slots 1150-1719, ticks can be missed or torn; not yet run on a long replay | [play-record.md](play-record.md), `reverse-engineering/README.md` |
| Exact full memory at chosen turns | `record_native.py` (or `play-record.ps1 -Capture`) | a Level 6a replay went out of sync at GT 76 under it while a plain replay did not (2026-11-16; cause unsettled, play-record.md) | [native-capture.md](native-capture.md) |
| A running process's memory, now | `process_dump.py` | no replay control, no provenance; prefer `record_native.py` | its docstring |
| Stop at a function's entry and return | `decision_boundary.py` (`decision_boundary.py q737` is the Q737 preset) | hardware breakpoints stop the game | [decision-boundary.md](decision-boundary.md), [q737-boundary.md](q737-boundary.md) |
| Export a capture | `capture_export.py state` (values for C#), `capture_export.py memory` (an image for the emulator) | refuses missing or torn turns and a changed frozen decoder | its docstring, [native-capture.md](native-capture.md) |
| Analyse a legacy capture (decisions, steals, idle runs and stats, draw sites) | `reverse-engineering/dk2capture.py <capture> <subcommand>` | local, uncommitted; `test_dk2capture_cli.py` | its docstring |
| Run DKII.EXE functions on an emulator | `dk2oracle.py`, `purity.py`, `texture_blocks.py` | functions it can run in isolation only | ADR-0481, `docs/ARCHITECTURE.md` |
| Poke or read a live game (seed, reveal, draw log, music, positions) | `reverse-engineering/dk2live.py` `seed`, `reveal`, `drawlog`, `music`, `positions`, `watch` (`--doc` prints a subcommand's notes) | local, uncommitted; `test_dk2live.py`; not run against a live game since the 2026-11-16 merge | `reverse-engineering/README.md` |

Not this folder: `dk2tools replay` (`src/DK2.Tools`, a packet replayed in our simulation; needs an entity map),
`tools/tester-package/capture.ps1` (a tester's timed run).

## Traps that apply to every native tool

- `-LEVEL` must be given with both `-PSAVE` and `-PLOAD`; launch `DKII-DX.exe`, not `DKII.EXE` (`docs/TRAPS.md`).
- A tool attached to the game can change the replay itself (suspected for `record_native.py`, play-record.md); check
  `MESSAGE.LOG` for `Gone Out Of Sync` after every replay capture, and copy the log before the next launch rewrites it.
- `reverse-engineering/` is never committed and its only copy must not live in a clone: copy a changed script to
  `F:\DK2Evidence\archive\` (the 2026-11-16 copy is `re-scripts--2026-11-16--001`).
- Do not run `reverse-engineering/` scripts with `python -I`: isolation drops their own folder from `sys.path`.
