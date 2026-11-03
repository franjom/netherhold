# Complete native captures

`record_native.py` is the default recorder for new analysis. It always saves
the normal decoded state **and every readable committed process-memory byte
at every selected completed turn**. There is no dump-enable, memory-category,
map-filter or size-limit flag to forget. The packet supplies the level name;
the initialized map is discovered and guarded. Seed is required and written
with readback at stopped world-turn 1 entry, after any earlier loader work.

```powershell
python -I tools/oracle/record_native.py `
  --exe 'E:/GogGames/Dungeon Keeper 2/DKII-DX.exe' `
  --packet 'E:/GogGames/Dungeon Keeper 2/PacketRecord/l5live.sav' `
  --seed 2 --first 1400 --last 3400 `
  --out 'F:/DK2Evidence/studies/my-new-native-capture'
```

The directory must be new. Packet must be the exact installed `.sav` the
native loader will open. No native files or settings are replaced. The tool
refuses an existing DK2 process, launches its own hidden child, uses hardware
breakpoints, and closes only that child. Compatibility setup is local to the
process environment. There is no external seeder or default 40x50 filter.

## Timeouts

`--timeout` is a native/debug-event wait budget, not a total wall-clock limit.
Intentional stopped `before`/`after` capture callbacks do not consume it;
the accounting excludes their duration even when a callback fails. A capture
failure still fails the run and produces no verified success receipt. The
recorder records `timeoutPolicy` and `nativeWaitBudgetSeconds` in `launch.json`.
The same default policy applies without another opt-in flag.

Capture, compression and serialization can therefore make total elapsed time
exceed the native wait budget. Keep measured pause durations with the run;
this does not change the timing and trajectory limitations below.

## Data and verification

- `level.dk2cap`: compatible structured frames. Their stamps mean completed
  executed turns; [legacy polling headers differ](../../docs/research/packet-playback-clock.md).
- `memory/`: full address-space indexes and compressed content-addressed
  blocks. Schema 2 packs new blocks per frame; unchanged bytes are shared.
  Original virtual-memory region boundaries survive oracle-image export.
- `boundaries.jsonl`: entry/return registers, full 716-byte WOW64 contexts
  for live threads, original debug registers, guards and measured read windows.
- `launch.json`, `seed.json`, packet copy and `sources/`: inputs and pinned
  producer/decoder identities. No proprietary capture or decoder enters git.
- First/last `.after.mem.z`: ready oracle images. Any saved turn can be
  exported later; choosing those two views does not limit retained data.

Early turns 1 and 2 always perform a full-memory and structured-frame
round-trip before waiting for the requested window. Every readable region
must be retained. Optional decoder markers for unused player pools are
separate from memory-coverage failures; required core/thing/active-pool data
must exist and match the full-memory image.

`run-status.json` records capture/shutdown, with validation still pending.
Only `verified.json` together with `sha256-final.json` establishes a completed
validated run. Consumers bind the actual capture, timeline/index chain and
decoder identities; missing or changed data fails closed. A failed capture
keeps its existing files and never gains a success receipt. Use a new directory
for another attempt; evidence is never renamed, overwritten or cleaned up.

## Analysis without extra capture flags

```powershell
python -I tools/oracle/capture_state.py --state-only `
  'F:/DK2Evidence/studies/my-new-native-capture/level.dk2cap' `
  'F:/DK2Evidence/studies/my-new-native-export' 1500

python -I tools/oracle/capture_memory.py `
  'F:/DK2Evidence/studies/my-new-native-capture/level.dk2cap' 1500 `
  'F:/DK2Evidence/studies/my-new-native-export/1500.mem.z'
```

Both discover the saved decoder and verified timeline automatically, including
when `--state-only` precedes the input. No `DK2_CAPTURE_DECODER` or `--dump`
is needed. State JSON remains a selected projection; its `memoryTimeline`
retains fields not yet decoded. This does not make C# resync a complete restore.
Missing exact turns, mismatched clocks/navigation widths and existing outputs
are rejected. Legacy captures remain explicitly partial; an explicit legacy
`--dump` must match the structured frame's clock. They cannot acquire data
which was never recorded.

## Coverage and limits

The scanner queries `[0, 2^32)` and records state/type/protection/allocation
metadata for every range. It captures mapped, private and image memory,
including read-only pages and regions over 64 MiB. Query errors, short query
metadata and short reads fail capture. Guard/no-access/uncommitted ranges are
identified rather than invented. The old `process_dump.py` and chooser
`decision_boundary.py` now use the same complete readable-region policy and
exclusive writes; use the turn recorder for full provenance and validation.

The debuggee's threads are stopped, but external writers to shared/system
pages are not frozen. Memory is collected over a measured read window, not a
simultaneous snapshot of the entire operating system. WOW64 context scope is
the 32-bit game, not kernel/host-native register state. Instrumentation pauses
can affect real-time presentation and camera-dependent trigger timing; these
pauses are not performance or old-trajectory parity evidence. The schema-2 study ending `--002` measured roughly
8.2 seconds on its first frame, then 2.8 and 1.6 seconds; keep the per-run times.

Coverage follows [VirtualQueryEx](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualqueryex)
and readable-page protections. Generated non-LAA/LAA WOW64 fixtures verify
the full query domain; allocation limits do not define the enumeration limit.

## Tests and retained controls

`python -m unittest discover -s tools/oracle -p 'test_*.py' -v` checks coverage,
exclusive writes, truncation/corruption, identity/receipt binding, decoder
selection, native-format contracts and generated-PE cleanup. All 45 tests pass.
The earlier completeness regressions fail eight cases on archived source,
with four passing controls. The timeout regressions give two errors on nine
unchanged archived source files, while twelve controls pass.

Native smoke captures and no-flag export controls are add-only studies under
`F:/DK2Evidence/studies/q712-complete-capture-*--2026-11-02--*/`. Each keeps its
own source and manifest; schema-1 prototype data remains readable. Historical
Q712 outcome data is separate from these short capture-tool controls.

The earlier long run
`F:/DK2Evidence/studies/q712-complete-native-level5-seed2--2026-11-02--001/`
was intentionally stopped after 63 complete pairs (turns 1, 2 and 1400-1460). Its owned child closed, the packet remained unchanged
and all files were retained, without a verified success receipt. Its mean
4.689-second pauses projected about 9383 seconds against the old 6920-second
wall budget; it is retained as incomplete evidence, not a successful long run.
The new timeout-policy smoke study is
`F:/DK2Evidence/studies/q712-capture-native-budget-smoke--2026-11-02--001/`.
It verifies turns 1-3, with its owned child closed and packet unchanged.
Independent review confirms the verified receipt, all 25 manifest entries
and six current producer source hashes with no mismatches. This bounded
smoke validates the timeout policy; the interrupted long run remains partial.

Verification bundle:
`F:/DK2Evidence/builds/q712-capture-native-budget-tools--g8bae7031-wip--2026-11-03--001/`.
