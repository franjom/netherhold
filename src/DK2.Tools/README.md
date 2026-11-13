# DK2.Tools

The `dk2tools` command line (an `Exe` on `net10.0`) for reading original data,
converting assets and checking the repository.

    dotnet run --project src/DK2.Tools -- <command> [args]

Commands are registered in `Program.cs` (`Registry`); running it with no arguments prints
the usage. Families: `kwd` (read, round-trip and rewrite KWD/KLD files), `wad`, `sdt`,
`str` (list archive and string contents), `convert` and `where` (asset conversion and
lookup, via `DK2.Convert`), `capture-diff`, `trigger-diff`, `trigger-run`, `replay`
and `decide` (comparing simulation runs with recorded captures), `coverage`, `kb`
(the function knowledge base), `docs` (doc budgets and the system tags of ADR-0643),
`wiki` (the generated wiki pages, `Wiki/`), `simmatrix` (which simulation system reads and
writes which state, `SimMatrix/`; `wiki` writes it too), `status`, `sgmap` and
`verification-ledger`, `journal-report` (offline comparison of a journal with supplied
hashes), and `snapshot-diff` (decoded saved-snapshot field comparison). Install-reading commands need `DK2_DIR`.

Depends on `DK2.Assets`, `DK2.Convert`, `DK2.Core`, `DK2.Data`, `DK2.Data.Model` and
`DK2.Simulation`, on YamlDotNet for `docs/systems.yaml`, and on Microsoft.CodeAnalysis.CSharp
(Roslyn), which `simmatrix` uses to compile the source of `DK2.Core`, `DK2.Data.Model`,
`DK2.Pathfinding` and `DK2.Simulation` and follow the calls each system's rows make.

Test: `dotnet test tests/DK2.Tools.Tests`.

## Offline journal report

    dotnet run --project src/DK2.Tools -c Release -- journal-report input.journal hashes.tsv new-report.html

Reads an existing v1 input journal and a UTF-8 TSV of strictly increasing,
nonnegative ticks and hexadecimal state hashes (two columns, no header). It
writes a self-contained, escaped HTML report; the output path must not exist.
It needs no game install and does not run a replay. Supply hashes from an
existing harness replay separately.

Commands are decoded by this build's codec. Older journals can use incompatible
command payloads despite sharing the v1 envelope; those inputs are refused.
The command does not migrate journal encodings.

The report includes journal metadata, SHA-256 fingerprints of both inputs,
the first divergent hash with three hash ticks on either side, nearby commands
in journal order, and missing or truncated evidence. Hash tick T is the state
after T ticks; input tick T can first affect hash T+1. A surviving attempted
input from a crashed tick is labelled separately.

Exit codes: 0 means all supplied hashes match with complete coverage; 1 means
an observed mismatch (also used for CLI usage errors); 2 means invalid inputs
or an output error; 3 means incomplete comparison without an observed mismatch.
A cut final journal record is reported as truncated; malformed records are
rejected. The TSV carries no build or scenario metadata, so its provenance is
not verified. Matching records establish neither successful gameplay nor DK2
parity. This report provides whole-state hash context. For saved-snapshot field
changes, see [snapshot comparison](#snapshot-comparison); integrated replay diffs
remain planned in [the crash-tooling plan](../../docs/plans/2026-10-18-crash-and-debug-tooling.md#6-analysis-tools-with-a-gui).

## Snapshot comparison

    dotnet run --project src/DK2.Tools -c Release -- snapshot-diff before.dk2g after.dk2g new-report.json
    dotnet run --project src/DK2.Tools -c Release -- snapshot-diff before.snapshot after.snapshot new-report.json --level Level1

Accepts our DK2G saved sessions or raw DK2S snapshots. Raw snapshots require
`--level <campaign-level>`; saved-session levels must agree, and an explicit
level must match them. Decoding uses the current `WorldSnapshot.FromBytes`
format and original campaign data from the installed game (`DK2_DIR`). The
original database is absent from snapshots, so level-data provenance is not
verified. The command executes no ticks, changes no inputs, and requires a
new output path.

The schema-1 JSON report includes source-file and snapshot-payload SHA-256
fingerprints, format version, ticks, scope and limits. Sorted changes contain
`path`, `kind`, `before` and `after`, with invariant text values or JSON null.
It compares creature fields by creature ID, player fields by player ID, tiles
by coordinates, and snapshot tick, dimensions, fog, mode and RNG. Nested
collections and identity additions/removals are included. Computed getters
and world/delegate references are excluded.

Exit codes: 0 means identical snapshot payload bytes; 1 means projected field
changes (also used for CLI usage errors); 2 means an input, game-data or output
failure; 3 means different payload bytes without projected field changes.
Save-container headers and campaign profiles are excluded from payload equality.
Shots, objects, queues, navigation and other state outside the named projection
are not compared field by field. An empty change list does not establish whole-world
identity or DK2 parity; use `snapshotBytesEqual` to distinguish payload equality
from changes outside the projection.
