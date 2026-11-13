// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Tools.Tests;

[Collection("Console")]
public class JournalReportTests
{
    [Fact]
    public void FirstDivergenceIncludesTheInputThatProducedItAndEscapesMetadata()
    {
        var fixture = new Fixture();
        fixture.Journal();
        fixture.Hashes("0\t00000000000000a0\n1\t00000000000000a1\n2\t00000000000000ff\n3\t00000000000000ee\n");

        Assert.Equal(1, fixture.Run());
        string html = File.ReadAllText(fixture.Output);
        Assert.Contains("First divergent hash: tick 2", html);
        Assert.Contains("00000000000000a2", html);
        Assert.Contains("00000000000000ff", html);
        Assert.Contains("SlapCreatureCommand", html);
        Assert.Contains("CreatureId = 7", html);
        Assert.Contains("Input tick 1", html);
        Assert.Contains("&lt;script&gt;bad()&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("Provenance is not verified", html);
        Assert.DoesNotContain("src=\"http", html);
    }

    [Fact]
    public void IdenticalCompleteEvidenceReportsAllComparedHashes()
    {
        var fixture = new Fixture();
        fixture.Journal();
        fixture.Hashes("0\ta0\n1\ta1\n2\ta2\n3\ta3\n");

        Assert.Equal(0, fixture.Run());
        string html = File.ReadAllText(fixture.Output);
        Assert.Contains("All supplied hashes match", html);
        Assert.Contains("Compared hashes: 4", html);
        Assert.Contains("Hash version", html);
        Assert.Contains("completed", html);
    }

    [Theory]
    [InlineData("0\ta0\n1\ta1\n", "Missing replay hashes: 2")]
    [InlineData("0\ta0\n2\ta2\n3\ta3\n", "Missing replay hashes: 1")]
    [InlineData("", "Missing replay hashes: 4")]
    public void MissingReplayEvidenceCannotReportSuccess(string hashes, string expected)
    {
        var fixture = new Fixture();
        fixture.Journal();
        fixture.Hashes(hashes);

        Assert.Equal(3, fixture.Run());
        Assert.Contains(expected, File.ReadAllText(fixture.Output));
    }

    [Fact]
    public void ExtraReplayTicksAreReportedAsIncompleteComparison()
    {
        var fixture = new Fixture();
        fixture.Journal();
        fixture.Hashes("0\ta0\n1\ta1\n2\ta2\n3\ta3\n4\ta4\n");

        Assert.Equal(3, fixture.Run());
        Assert.Contains("Replay hashes without a recorded hash: 1", File.ReadAllText(fixture.Output));
    }

    [Fact]
    public void TruncatedJournalKeepsItsAvailablePrefixButCannotReportSuccess()
    {
        var fixture = new Fixture();
        fixture.Journal(end: false);
        fixture.Hashes("0\ta0\n1\ta1\n2\ta2\n3\ta3\n");

        Assert.Equal(3, fixture.Run());
        string html = File.ReadAllText(fixture.Output);
        Assert.Contains("Journal is truncated", html);
        Assert.Contains("Compared hashes: 4", html);
    }

    [Fact]
    public void PartialFinalRecordKeepsTheSurvivingEvidence()
    {
        var fixture = new Fixture();
        fixture.Journal(end: false);
        using (var stream = new FileStream(fixture.JournalPath, FileMode.Append))
            stream.Write([2, 0, 0]);
        fixture.Hashes("0\ta0\n1\ta1\n2\ta2\n3\ta3\n");

        Assert.Equal(3, fixture.Run());
        Assert.Contains("Compared hashes: 4", File.ReadAllText(fixture.Output));
    }

    [Fact]
    public void CommandContextIsLimitedToTheDivergenceWindow()
    {
        var fixture = new Fixture();
        using (var stream = File.Create(fixture.JournalPath))
        using (var writer = new InputJournalWriter(stream, new Dictionary<string, string>()))
        {
            for (int tick = 0; tick <= 100; tick++)
            {
                writer.WriteHash(tick, (ulong)tick);
                if (tick is 0 or 49 or 99)
                    writer.WriteFrame(tick, InputFrame.Of(new SlapCreatureCommand(new PlayerId(3), tick)));
            }
            writer.WriteEnd(100, "completed");
        }
        fixture.Hashes(string.Join('\n', Enumerable.Range(0, 101).Select(t => $"{t}\t{(t == 50 ? 0 : t):x16}")) + "\n");

        Assert.Equal(1, fixture.Run());
        string html = File.ReadAllText(fixture.Output);
        Assert.Contains("Input tick 49", html);
        Assert.DoesNotContain("Input tick 0 ", html);
        Assert.DoesNotContain("Input tick 99", html);
    }

    [Fact]
    public void JournalWithoutHashesCannotReportSuccess()
    {
        var fixture = new Fixture();
        fixture.Journal(hashes: false);
        fixture.Hashes("");

        Assert.Equal(3, fixture.Run());
        Assert.Contains("No recorded hashes", File.ReadAllText(fixture.Output));
    }

    [Fact]
    public void AGapInBothSourcesCannotBeMistakenForCompleteEvidence()
    {
        var fixture = new Fixture();
        fixture.Journal(omittedHash: 2);
        fixture.Hashes("0\ta0\n1\ta1\n3\ta3\n");

        Assert.Equal(3, fixture.Run());
        Assert.Contains("Missing recorded hashes: 1", File.ReadAllText(fixture.Output));
    }

    [Fact]
    public void AnUnrepresentableJournalTickIsRefused()
    {
        var fixture = new Fixture();
        using (var stream = File.Create(fixture.JournalPath))
        using (var writer = new InputJournalWriter(stream, new Dictionary<string, string>()))
            writer.WriteHash(long.MaxValue, 0xa0);
        fixture.Hashes($"{long.MaxValue}\ta0\n");

        Assert.Equal(2, fixture.Run());
        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public void InitialStateDivergenceIsReportedAtZero()
    {
        var fixture = new Fixture();
        fixture.Journal();
        fixture.Hashes("0\tff\n1\ta1\n2\ta2\n3\ta3\n");

        Assert.Equal(1, fixture.Run());
        Assert.Contains("First divergent hash: tick 0", File.ReadAllText(fixture.Output));
    }

    [Theory]
    [InlineData("duplicate hash")]
    [InlineData("duplicate frame")]
    [InlineData("trailing record")]
    [InlineData("future frame")]
    [InlineData("completed end frame")]
    public void ConflictingOrImpossibleJournalEvidenceIsRefused(string fault)
    {
        var fixture = new Fixture();
        using (var stream = File.Create(fixture.JournalPath))
        using (var writer = new InputJournalWriter(stream, new Dictionary<string, string>()))
        {
            writer.WriteHash(0, 0xa0);
            if (fault == "duplicate hash") writer.WriteHash(0, 0xa0);
            if (fault == "duplicate frame")
            {
                writer.WriteFrame(0, InputFrame.Of(new SlapCreatureCommand(new PlayerId(3), 7)));
                writer.WriteFrame(0, InputFrame.Of(new SlapCreatureCommand(new PlayerId(3), 8)));
            }
            if (fault is "future frame" or "completed end frame")
                writer.WriteFrame(fault == "future frame" ? 2 : 1, InputFrame.Of(new CreateImpCommand(new PlayerId(3))));
            writer.WriteHash(1, 0xa1);
            writer.WriteEnd(1, "completed");
            if (fault == "trailing record") writer.WriteHash(2, 0xa2);
        }
        fixture.Hashes("0\ta0\n1\ta1\n");

        Assert.Equal(2, fixture.Run());
        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public void AFailedTicksInputIsKeptAndLabeledAsAttempted()
    {
        var fixture = new Fixture();
        using (var stream = File.Create(fixture.JournalPath))
        using (var writer = new InputJournalWriter(stream, new Dictionary<string, string>()))
        {
            writer.WriteHash(0, 0xa0);
            writer.WriteFrame(0, InputFrame.Of(new CreateImpCommand(new PlayerId(3))));
            writer.WriteEnd(0, "InvalidOperationException: fixture failure");
        }
        fixture.Hashes("0\ta0\n");

        Assert.Equal(0, fixture.Run());
        string html = File.ReadAllText(fixture.Output);
        Assert.Contains("attempted; no completed tick", html);
        Assert.Contains("fixture failure", html);
        Assert.Contains("CreateImpCommand", html);
    }

    [Theory]
    [InlineData("0\ta0\n0\ta1\n")]
    [InlineData("-1\ta0\n")]
    [InlineData("tick\thash\n")]
    [InlineData("0\txyz\n")]
    [InlineData("0\ta0\textra\n")]
    [InlineData("1\ta1\n0\ta0\n")]
    public void InvalidHashRowsAreRefusedWithoutCreatingAReport(string hashes)
    {
        var fixture = new Fixture();
        fixture.Journal();
        fixture.Hashes(hashes);

        Assert.Equal(2, fixture.Run());
        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public void ExistingOutputAndInputFilesAreNeverOverwritten()
    {
        var fixture = new Fixture();
        fixture.Journal();
        fixture.Hashes("0\ta0\n");
        File.WriteAllText(fixture.Output, "keep me");
        byte[] journal = File.ReadAllBytes(fixture.JournalPath);
        byte[] hashes = File.ReadAllBytes(fixture.HashPath);

        Assert.Equal(2, fixture.Run());
        Assert.Equal("keep me", File.ReadAllText(fixture.Output));
        Assert.Equal(2, fixture.Run(fixture.JournalPath));
        Assert.Equal(journal, File.ReadAllBytes(fixture.JournalPath));
        Assert.Equal(hashes, File.ReadAllBytes(fixture.HashPath));
    }

    [Fact]
    public void BadJournalOrMissingInputIsAReadableInputError()
    {
        var fixture = new Fixture();
        File.WriteAllText(fixture.JournalPath, "not a journal");
        fixture.Hashes("0\ta0\n");
        Assert.Equal(2, fixture.Run());
        Assert.False(File.Exists(fixture.Output));

        Assert.Equal(2, Program.Dispatch(["journal-report", fixture.JournalPath + ".missing", fixture.HashPath, fixture.Output]));
    }

    private sealed class Fixture
    {
        private readonly string _directory = Path.Combine(Directory.GetCurrentDirectory(), "build", "journal-report-tests", Guid.NewGuid().ToString("N"));
        public string JournalPath => Path.Combine(_directory, "input.journal");
        public string HashPath => Path.Combine(_directory, "hashes.tsv");
        public string Output => Path.Combine(_directory, "report.html");

        public Fixture() => Directory.CreateDirectory(_directory);

        public void Journal(bool end = true, bool hashes = true, int? omittedHash = null)
        {
            using var stream = File.Create(JournalPath);
            using var writer = new InputJournalWriter(stream, new Dictionary<string, string>
            {
                ["scenario"] = "<script>bad()</script>",
                ["commit"] = "fixture-commit",
            });
            if (hashes) writer.WriteHash(0, 0xa0);
            writer.WriteFrame(1, InputFrame.Of(new SlapCreatureCommand(new PlayerId(3), 7)));
            if (hashes)
                for (int tick = 1; tick <= 3; tick++)
                    if (tick != omittedHash) writer.WriteHash(tick, 0xa0UL + (ulong)tick);
            if (end) writer.WriteEnd(3, "completed");
        }

        public void Hashes(string text) => File.WriteAllText(HashPath, text);

        public int Run(string? output = null) =>
            Program.Dispatch(["journal-report", JournalPath, HashPath, output ?? Output]);
    }
}
