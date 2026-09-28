// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Tools;

namespace DK2.Tools.Tests;

public sealed class VerificationLedgerTests
{
    // The names are invented: the ledger lists a test file as a candidate for any rule
    // whose name it mentions.
    [Theory]
    [InlineData("public static class SampleTiming\n{", "SampleTiming")]
    [InlineData("public sealed record SampleShot(int Id);", "SampleShot")]
    [InlineData("public static int SampleRate(int fileValue)", "SampleRate")]
    [InlineData("public static List<int> SampleOrder<T>(T room)", "SampleOrder")]
    [InlineData("public int SampleCount { get; }", "SampleCount")]
    [InlineData("public int SampleCount => _count;", "SampleCount")]
    // A field is named, not its initialiser: the ledger once recorded "4", "0xCCC" and,
    // for a field initialised from another, that other field's name.
    [InlineData("public const int SampleTicks = 4;", "SampleTicks")]
    [InlineData("public const int SampleModifier = 0xCCC;", "SampleModifier")]
    [InlineData("public const int SampleApplications = SampleTicks;", "SampleApplications")]
    [InlineData("private static readonly int[] SampleDx = [0, 1, 0, -1];", "SampleDx")]
    [InlineData("[Obsolete]\n    public const int SampleHealth = 32000;", "SampleHealth")]
    public void TheSymbolIsTheDeclaredName(string declaration, string expected)
    {
        const string attribute = "[Dk2Behavior(Confidence.Confirmed, \"docs/x.md\")]\n    ";
        Assert.Equal(expected, VerificationLedger.SymbolAt(attribute + declaration, attribute.Length));
    }
}
