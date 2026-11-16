# Original-game capture tools: one tool per job

> Approved by the owner (2026-11-16) with the exact path list in "Removals". Steps 1-5 of "Order" are done in the
> working tree of branch `tools/capture-consolidation`, uncommitted. Each step: tests first, reviewer, documenter,
> commit named files, merge. Copies of every removed file: `F:/DK2Evidence/archive/oracle-retired--2026-11-16--001`
> (tracked) and `re-scripts--2026-11-16--001` to `--003` (local; originals, `dk2live.py`, `dk2capture.py`).

**Owner direction (2026-11-16):** "reduce or merge tools that are similar in use and intent, no need to have 10
different versions of a single tool... document them and make them robust". Sessions kept guessing which recorder to
use and how; on Level 6a the documented default (`record_native.py`) desynced a replay that the legacy recorder would
not have touched.

**Constraint:** `reverse-engineering/**` is never committed (`.gitignore`, ADR-0046): code holding offsets read from the
binary stays local, and its only copy must not live in a clone (AGENTS.md HARD RULE). A copy of every script there, as
of today, is `F:/DK2Evidence/archive/re-scripts--2026-11-16--001` (111 files, `sha256.txt`).

## Before the merge: 25 scripts, about nine jobs

| Job | Tracked (`tools/oracle/`) | Local (`reverse-engineering/`) |
|---|---|---|
| Record the owner's play | `play-record.ps1` (new, uncommitted) | - |
| Replay a recording undisturbed, every turn's state | - | `dk2record.py` (polls from outside; labelled legacy) |
| Exact memory at chosen turns of a replay | `record_native.py` + `native_memory.py` | - |
| Dump a running process's memory now | `process_dump.py` | (`dk2record.py --dump-ticks` calls it) |
| Stop at a function's entry/return | `decision_boundary.py`, `q737_boundary.py` (a Q737 special case) | - |
| Export a capture for C# or the emulator | `capture_state.py`, `capture_memory.py` | `dk2capture.py` CLI (legacy captures) |
| Read or poke a live game | - | `dk2watch.py` (plumbing), `dk2move.py`, `dk2seed.py`, `dk2reveal.py`, `dk2drawlog.py`, `dk2music.py` |
| Analyse a capture | `guard_room_on_capture.py` (pilot; hardcodes a dead path) | `dk2decisions.py`, `dk2steals.py`, `dk2idleruns.py`, `dk2idlestats.py`, `dk2drawsites.py`, `dk2check_l2.py` |
| Run DKII.EXE functions on an emulator | `dk2oracle.py`, `purity.py`, `texture_blocks.py` | - |
| One-off probes | `imp_claim_clock_probe.py` | many `*.py` binary-query helpers (not capture tools; out of scope) |

Out of scope (other jobs): `src/DK2.Tools` `replay`/`PacketLog` (replays a packet in our simulation),
`tools/tester-package/capture.ps1` (outside testers' timed runs), the Unity/Godot probes.

## Target: one tool per job

Tracked, `tools/oracle/`:
1. `play-record.ps1`: record the owner's play, decode packets, and drive the two replay captures below. Done.
2. `record_native.py`: exact memory at chosen turns. Kept; its README states the long-replay desync (Level 6a, GT 76).
3. `capture_export.py`: one CLI, `state` and `memory`, merging `capture_state.py` and `capture_memory.py` (same input,
   two outputs; `capture_state` already imports `capture_memory`). Callers and docs updated.
4. `decision_boundary.py`: absorbs `q737_boundary.py` as a named preset (its selection logic and tests move with it).
5. `process_dump.py`, `native_memory.py`: libraries with their CLIs, unchanged.
6. `dk2oracle.py`, `purity.py`, `texture_blocks.py`: the emulator, unchanged, documented.
7. `README.md`: the index, "which tool for which job", every tool's limits and traps with evidence.

Local, `reverse-engineering/` (stays uncommitted):
8. `dk2live.py`: one live-game tool with subcommands `record` (today's `dk2record.py`), `positions` (`dk2move.py`),
   `seed`, `reveal`, `drawlog`, `music`, `watch`; the process plumbing once (today copied across `dk2watch.py` and
   `dk2move.py`). Each subcommand keeps its options; a synthetic self-test covers argument parsing and the plumbing.
9. `dk2capture.py`: the legacy decoder and CLI, absorbing `decisions`, `steals`, `idle-runs`, `idle-stats`,
   `draw-sites` as subcommands.
10. `README.md` (the one committed file there): points at both and at the tracked index.

Robust means, for every kept tool: refuses an existing output (evidence is add-only); checks elevation and a running
game before starting; fails loudly with the reason; prints what it wrote; one README section with options, outputs,
limits and traps; tests that need no game (synthetic inputs), as `test_play_record.py` does.

## Removals (owner-approved list)

Merged into a kept tool (code moves, the file goes):
- tracked: `tools/oracle/capture_state.py`, `tools/oracle/capture_memory.py` (into `capture_export.py`),
  `tools/oracle/q737_boundary.py`, `tools/oracle/test_q737_boundary.py` (into `decision_boundary.py` and its tests)
- local: `reverse-engineering/dk2watch.py`, `dk2move.py`, `dk2record.py`, `dk2seed.py`, `dk2reveal.py`, `dk2drawlog.py`,
  `dk2music.py` (into `dk2live.py`); `dk2decisions.py`, `dk2steals.py`, `dk2idleruns.py`, `dk2idlestats.py`,
  `dk2drawsites.py` (into `dk2capture.py`)

Retired (no current use; kept in the F: copy):
- tracked: `tools/oracle/guard_room_on_capture.py` (a pilot that hardcodes `D:\DungeonKeeperRemake-oracle`),
  `tools/oracle/imp_claim_clock_probe.py` (a bounded probe whose study is done)
- local: `reverse-engineering/dk2check_l2.py` (one Level 2 prediction check)

Evidence studies under `F:/DK2Evidence` keep their own source copies, so their provenance is unaffected.

## Order

1. Done: `play-record.ps1` and the index `tools/oracle/README.md`.
2. Done: `capture_export.py` (`state`, `memory`), then `decision_boundary.py q737`.
3. Done: `dk2live.py` (local; `test_dk2live.py`). Not run against a live game since the merge.
4. Done: `dk2capture.py` subcommands (local; `test_dk2capture_cli.py`).
5. Done: `native-capture.md`, `reverse-engineering/README.md`, `docs/TRAPS.md`, the research notes and tool
   comments name the new tools. The Docker memory graph is not updated by this pass.

## Open

- Whether `dk2record.py`'s legacy capture (selected regions only, object slots 1150-1719, possibly torn frames) is
  enough for the jobs it would serve, or `record`'s regions should widen; first measure with the Level 6a `-Replay`.
