// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Tools;
using DK2.Tools.Commands;

// DK2.Tools — command-line utilities for inspecting original game data.

return Program.Dispatch(args);

internal partial class Program
{
    internal static readonly IReadOnlyDictionary<string, Func<string[], int>> Registry =
        new Dictionary<string, Func<string[], int>>(StringComparer.Ordinal)
        {
            ["capture-diff"] = args => args.Length is 3 or 5 ? CaptureCommands.CaptureDiff(args) : Usage(),
            ["trigger-diff"] = args => args.Length == 3 ? CaptureCommands.DiffTriggerCsv(args[1], args[2]) : Usage(),
            ["trigger-run"] = args => args.Length == 4
                ? ReplayCommands.RunTriggerCsv(
                    args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args[3])
                : Usage(),
            ["replay"] = args => args.Length >= 4 ? ReplayCommands.Replay(args) : Usage(),
            ["decide"] = args => args.Length == 4 ? ReplayCommands.Decide(args[1], args[2], args[3]) : Usage(),
            ["sgmap"] = args => SgmapCommand.Run(args[1..], Repo.FindRepoRoot()),
            ["verification-ledger"] = args => VerificationLedger.Run(args[1..], Repo.FindRepoRoot()),
            ["where"] = AssetCommands.RunWhere,
            ["status"] = StatusCommand.Run,
            ["kb"] = args => args.Length >= 2 ? FunctionKbCommand.Run(args.Skip(1).ToArray()) : Usage(),
            ["docs"] = DocsCommand.Run,
            ["coverage"] = args => args.Length == 2 && args[1] == "states"
                ? StateCoverageReport.Print(Console.Out)
                : CoverageCommand.Run(args),
            ["convert"] = AssetCommands.RunConvert,
            ["wad"] = args => args.Length >= 3 && args[1] == "list" ? AssetCommands.RunWadList(args)
                : args.Length >= 4 && args[1] == "extract" ? AssetCommands.RunWadExtract(args)
                : Usage(),
            ["sdt"] = args => args.Length == 3 && args[1] == "list" ? AssetCommands.RunSdtList(args)
                : args.Length is 3 or 5 && args[1] == "extract" ? AssetCommands.RunSdtExtract(args)
                : Usage(),
            ["str"] = args => args.Length == 3 && args[1] == "list" ? AssetCommands.RunStrList(args)
                : args.Length == 4 && args[1] == "get" ? AssetCommands.RunStrGet(args)
                : Usage(),
            ["kwd"] = KwdCommands.Run,
        };

    internal static int Dispatch(string[] args) =>
        args.Length > 0 && Registry.TryGetValue(args[0], out Func<string[], int>? run) ? run(args) : Usage();

    internal static int Usage()

    {

        Console.WriteLine("usage: dk2tools <command>");

        Console.WriteLine();

        Console.WriteLine("commands:");

        Console.WriteLine("  where                                  print resolved DK2 install path");

        Console.WriteLine("  convert --out converted-assets/        convert catalogue assets to GLB and PNG");

        Console.WriteLine("  wad list <file.WAD>                    list archive entries");

        Console.WriteLine("  coverage [--all] [--check]             which research is built: every type a research");

        Console.WriteLine("  verification-ledger [--check]          generate or validate the sweep ledger CSV");

        Console.WriteLine("                                         doc names must exist in src/; --all also lists");

        Console.WriteLine("                                         docs naming no implementation; --check exits 1");

        Console.WriteLine("                                         on a stale claim");

        Console.WriteLine("  coverage states                        the 281 creature states: built, unreachable or");

        Console.WriteLine("                                         unbuilt, per category, and the unbuilt list");

        Console.WriteLine("  docs [--since <ref>] [--all]           check AGENTS.md's recording budgets on what");

        Console.WriteLine("                                         changed; --all reports the whole backlog");

        Console.WriteLine("       [--message <file>]                also check a commit message body");

        Console.WriteLine("  kb check [--all|--staged|--since REF]  check the function knowledge base on what changed");

        Console.WriteLine("  kb index                               write docs/research/functions/INDEX.md from the records");

        Console.WriteLine("  kb apply-names [--dry-run]             write the records' names into the canonical Ghidra project");

        Console.WriteLine("  status [--fast] [--check]              regenerate the plan's status paragraph;");

        Console.WriteLine("                                         --check exits 1 if the plan is stale");

        Console.WriteLine("  trigger-run <level> <turns> <out.csv>  our per-turn trigger state, in the live probe's shape");

        Console.WriteLine("  replay <level> <tags.csv> <out.csv> [--turns N] [--lag L] [--origin T]");

        Console.WriteLine("                                     [--rooms rooms.csv]");

        Console.WriteLine("                                         the same, driven by a captured task-pool log");

        Console.WriteLine("  capture-diff <ours.csv> <capture.csv> [--anchor tN]");

        Console.WriteLine("                                         event set and stamp-aligned capture timing");

        Console.WriteLine("  decide <level> <decisions.jsonl> <out.tsv>");

        Console.WriteLine("                                         DK2's imp choices put to ours in DK2's world");

        Console.WriteLine("  trigger-diff <left.csv> <right.csv>   exact per-turn diff for two of our own runs");

        Console.WriteLine("  wad extract <file.WAD> <entry> [out]   extract one entry");

        Console.WriteLine("  sdt list <file.sdt>                    list sound-bank entries and formats");

        Console.WriteLine("  sdt extract <file.sdt> [--out converted-assets/audio]");

        Console.WriteLine("                                         extract PCM as WAV, compressed audio raw");

        Console.WriteLine("  str list <file.str>                    list decoded string ids and values");

        Console.WriteLine("  str get <file.str> <id>                resolve one table-local string id");

        Console.WriteLine("  sgmap [--sgmap <path>] struct <name>   show a structure's concrete field layout");

        Console.WriteLine("  sgmap [--sgmap <path>] find <text>     find structures, fields and globals by name");

        Console.WriteLine("  sgmap [--sgmap <path>] global <hex>    identify a global by address");

        Console.WriteLine("  sgmap [--sgmap <path>] at <name> <hex> identify a field by structure offset");

        Console.WriteLine("  kwd chunks <file.kwd|kld>              list chunks of a KWD/KLD file");

        Console.WriteLine("  kwd vars [file.kwd]                    dump the global balance table");

        Console.WriteLine("  kwd alterterrain [-v]                  every ALTER_TERRAIN_TYPE trigger in every shipped");

        Console.WriteLine("                                         map, with what each target tile starts as (Q74)");

        Console.WriteLine("  kwd portals                            every portal in every shipped map, its owner and");

        Console.WriteLine("                                         size, flagging levels with two claimable ones (Q86)");

        Console.WriteLine();

        Console.WriteLine("offset hunting in records we do not parse:");

        Console.WriteLine("  kwd recscan <file.kwd> <value> [rec]   offsets holding a measured value, with the column");

        Console.WriteLine("  kwd records <file.kwd|kld>            dump decoded ids/types and consumed lengths");

        Console.WriteLine("  kwd trigger-types                     dump shipped trigger type counts and levels");

        Console.WriteLine("  kwd trigger-params <tag> <type|*> [name]  value distribution of one trigger parameter");

        Console.WriteLine("  kwd triggers <file> [tag] [type]        one level's triggers, their links and parameters");

        Console.WriteLine("  kwd corpus                             parse and round-trip the shipped KWD/KLD corpus");

        Console.WriteLine("  kwd rewrite <file|dir> <outdir>          typed rewrite with per-file first-difference diagnostics");

        Console.WriteLine("  kwd things [level|file.kld]            placed entities of one level, or the corpus");

        Console.WriteLine("  kwd parties                            how shipped HERO_PARTY records map onto the 24 runtime slots");

        Console.WriteLine("  kwd roots                              every trigger root in the campaign, by the walker that owns it");

        Console.WriteLine("  kwd unreached [turns]                 records a run never walks, and why -- declined, creature, party, orphan, closed (default 14400 = an hour)");

        Console.WriteLine("  kwd oracle [turns]                     every level run headless, ranked by how much of its");

        Console.WriteLine("                                         script a two-sided capture could discriminate");

        Console.WriteLine("  kwd creaturescan <name> <value>        the same, anchored on one creature");

        Console.WriteLine("  kwd creaturehex <offsetHex> <count>    raw byte window across every creature");

        Console.WriteLine("  kwd objecthex <offsetHex> <count> [-w]  the same across every object; -w reads u16s");

        return 1;

    }
}
