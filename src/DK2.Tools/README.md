# DK2.Tools

The `dk2tools` command line (an `Exe` on `net10.0`) for reading original data,
converting assets and checking the repository.

    dotnet run --project src/DK2.Tools -- <command> [args]

Commands are registered in `Program.cs` (`Registry`); running it with no arguments prints
the usage. Families: `kwd` (read, round-trip and rewrite KWD/KLD files), `wad`, `sdt`,
`str` (list archive and string contents), `convert` and `where` (asset conversion and
lookup, via `DK2.Convert`), `capture-diff`, `trigger-diff`, `trigger-run`, `replay`
and `decide` (comparing simulation runs with recorded captures), `coverage`, `kb`
(the function knowledge base), `docs` (doc budgets), `status`, `sgmap` and
`verification-ledger`. Install-reading commands need `DK2_DIR`.

Depends on `DK2.Assets`, `DK2.Convert`, `DK2.Core`, `DK2.Data`, `DK2.Data.Model` and
`DK2.Simulation`.

Test: `dotnet test tests/DK2.Tools.Tests`.
