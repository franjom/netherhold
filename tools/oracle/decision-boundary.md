# Imp selection boundary recorder

`decision_boundary.py` launches its own 32-bit game and records entry to and
return from the Imp task chooser. It refuses an existing DK2 process. It does
not attach to the owner's game, patch executable files or process code, inject
calls, or change game settings. Hardware execution breakpoints change thread
debug registers; cleanup restores those registers, resumes and detaches. If a
live thread's registers cannot be restored, cleanup terminates only the replay
it launched instead of detaching with a breakpoint still armed.
The launched replay remains running after a successful capture or timeout.

Run with 64-bit CPython on Windows, from this checkout:

```powershell
$env:__COMPAT_LAYER = 'RunAsInvoker'
python tools/oracle/decision_boundary.py `
  --exe 'E:/GogGames/Dungeon Keeper 2/DKII-DX.exe' `
  --out reverse-engineering/imp-selection-pair `
  --slot 452 --from-tick 133 --pairs 1 --timeout 90 `
  -- -LEVEL level4 -PLOAD l4live 0
```

The compatibility environment is for this process and its child, not a registry
change. It avoids the install's elevation requirement when permitted. A recorder
running elevated is the other supported way to launch an elevated game. Do not
start the recorder through UAC unless someone can answer that prompt. Packet
playback supplies the actions; nobody needs to play during the experiment.
The game is launched with a hidden startup window. If the requested pair cannot
be obtained, the recorder fails rather than treating an empty capture as success.

`--slot` selects a thing-table slot belonging to a living keeper-3 Imp; the
entry filter also checks its type and turn. `--from-tick` uses the game's
stored clock, without the whole-turn recorder's observation offset. Capture
breakpoints are per thread and have no software trap byte to undo. Each pair
stops the world at entry before any chooser instruction and at the caller's
return address before its next instruction. This records selection, not a
complete movement or animation cycle.

Only these executable MD5s are accepted:

- Retail: `2f3bbc21eae8c3aa468136baa303036d` (the oracle's pinned executable).
- Installed DX variant: `b9709c6c9084d3060e4ac7ac9764c70b`. The local comparison
  found its `.text` and `cseg` byte-identical to retail; five `.rdata` bytes differ.

The output must be a new directory beneath this checkout's ignored
`reverse-engineering/`. It contains proprietary runtime memory and must never
be committed or redistributed. `boundaries.jsonl` records pair id, phase, PID,
thread, executable identity, compatibility environment, CPU registers, Imp
summary and dump statistics. A pair is usable only with both phases for the
same process/thread/Imp and the same stored clock. A lone `before` row is
incomplete. Keep the executable identity with the images.

The `.mem.z` files use the existing oracle image format. While the outstanding
debug event holds all threads, the recorder reads the regions selected by
the strict `stopped_regions` counterpart of `process_dump.regions`: writable
image pages and private committed readable
regions, excluding guards, regions over 64 MiB and addresses above 0x7FFF0000.
Read-only executable pages remain supplied by the pinned file. Every selected
region must be read completely; a failed query or read leaves `.partial`, not a
completed image. This is an analysis run with pauses, not a wall-clock
performance run.

## Verification

```powershell
python -m unittest discover -s tools/oracle -p test_decision_boundary.py -v
```

The tests generate a tiny 32-bit PE containing two calls with distinct register
values. They check entry/return values, filtering, failure and timeout cleanup,
unchanged executable bytes, the saved return stack, incomplete-image handling,
restoration across threads and termination after a restoration failure.
They use no DK2 installation or third-party Python packages. The live debugger
tests skip outside 64-bit Windows. Evidence from the first DK2 pair and the
bounded modern reconstruction is in
[imp-boundary-route.md](../../docs/research/imp-boundary-route.md).

Win32 contracts: [debug events](https://learn.microsoft.com/en-us/windows/win32/debug/debugging-events),
[thread contexts](https://learn.microsoft.com/en-us/windows/win32/api/wow64apiset/nf-wow64apiset-wow64getthreadcontext),
[WOW64 layout](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-wow64_context),
[event continuation](https://learn.microsoft.com/en-us/windows/win32/api/debugapi/nf-debugapi-continuedebugevent).
