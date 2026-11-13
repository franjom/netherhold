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
`verification-ledger`, and `journal-report` (offline comparison of a journal with supplied
hashes). Install-reading commands need `DK2_DIR`.

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
parity. This report provides whole-state hash context; subsystem and entity
diffs remain planned in [the crash-tooling plan](../../docs/plans/2026-10-18-crash-and-debug-tooling.md#6-analysis-tools-with-a-gui).
