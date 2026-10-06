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
            "capture-diff", "convert", "coverage", "decide", "docs", "kb", "kwd", "replay", "sdt", "sgmap",
            "status", "str", "trigger-diff", "trigger-run", "verification-ledger", "wad", "where",
        ];
        Assert.Equal(expected, Program.Registry.Keys.Order(StringComparer.Ordinal));
    }
}
