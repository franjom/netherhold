// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Tools.Tests;

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection;

[Collection("Console")]
public class DispatcherTests
{
    private static (int Code, string Out) Run(params string[] args)
    {
        TextWriter original = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            return (Program.Dispatch(args), captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Theory]
    [InlineData]
    [InlineData("no-such-command")]
    [InlineData("--help")]
    [InlineData("kwd")]
    [InlineData("kwd", "no-such-subcommand")]
    [InlineData("kwd", "corpus", "extra")]
    [InlineData("capture-diff")]
    [InlineData("journal-report")]
    [InlineData("snapshot-diff")]
    [InlineData("wad", "list")]
    public void UnknownOrIncompleteCommandPrintsUsageAndExitsOne(params string[] args)
    {
        (int code, string output) = Run(args);
        Assert.Equal(1, code);
        Assert.StartsWith("usage: dk2tools <command>", output);
    }

    [Fact]
    public void RegistryNamesEveryTopLevelCommand()
    {
        string[] expected =
        [
            "capture-diff", "convert", "coverage", "decide", "docs", "journal-report", "kb", "kwd", "replay", "sdt", "sgmap", "simmatrix",
            "snapshot-diff", "status", "str", "trigger-diff", "trigger-run", "verification-ledger", "wad", "where", "wiki",
        ];
        Assert.Equal(expected, Program.Registry.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("terminal")]
    [InlineData("heart")]
    public void IncompleteResyncSectionsReturnAnInputErrorBeforeOpeningThePacket(string section)
    {
        string path = Path.GetTempFileName();
        TextWriter original = Console.Error;
        using var captured = new StringWriter();
        try
        {
            File.WriteAllText(path, $$$"""
                {"tick":5000,"width":1,"height":1,"tiles":[0,0,0],
                 "players":[{"id":3,"money":0,"mana":0,"{{{section}}}":{}}],"creatures":[]}
                """);
            Console.SetError(captured);
            int code = Program.Dispatch(["replay", "level4", "-", path + ".csv", "--packet", path + ".missing",
                "--packet-offset", "69", "--resync", path, "--resync-at", "5000"]);
            Assert.Equal(2, code);
            Assert.Contains(path, captured.ToString());
            Assert.Contains("capture", captured.ToString());
        }
        finally
        {
            Console.SetError(original);
            File.Delete(path);
        }
    }
}
