// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>The input journal's file format (ADR-0552 Stage B): what is written reads back, and a
/// journal cut off at any byte still reads up to its last whole record.</summary>
public class InputJournalTests
{
    private static readonly PlayerId P = new(3);

    [Fact]
    public void AJournal_ReadsBackItsHeaderFramesHashesAndEnd()
    {
        InputJournal journal = InputJournal.Read(new MemoryStream(Written()));

        Assert.Equal(GameSimulation.CurrentHashVersion, journal.HashVersion);
        Assert.Equal("abc123", journal.Header["commit"]);
        Assert.Equal([1L, 3L], journal.FrameTicks);
        Assert.Equal(2, journal.FrameAt(3).All.Count);
        Assert.Empty(journal.FrameAt(2).All);
        Assert.Equal(0xA4UL, journal.Hashes[4]);
        Assert.Equal(4, journal.TicksRun);
        Assert.Equal("completed", journal.EndReason);
        Assert.False(journal.Truncated);
    }

    [Fact]
    public void AJournalCutAtAnyByte_ReadsItsWholeRecords()
    {
        byte[] full = Written();
        int headerEnd = HeaderLength();
        for (int cut = headerEnd; cut < full.Length; cut++)
        {
            InputJournal journal = InputJournal.Read(new MemoryStream(full[..cut]));

            Assert.True(journal.Truncated, $"cut at {cut} read as complete");
            // The two-command frame at tick 3 is either absent or whole, never half read.
            int atThree = journal.FrameAt(3).All.Count;
            Assert.True(atThree is 0 or 2, $"cut at {cut} read {atThree} commands at tick 3");
            Assert.True(journal.LastHashedTick <= 4);
        }
    }

    [Fact]
    public void AJournalCutInsideItsHeader_IsRefused()
    {
        byte[] full = Written();
        for (int cut = 0; cut < HeaderLength(); cut++)
            Assert.Throws<InvalidDataException>(() => InputJournal.Read(new MemoryStream(full[..cut])));
    }

    [Fact]
    public void ACommandWithoutATag_LeavesNoPartOfItsFrame()
    {
        var stream = new MemoryStream();
        using (var writer = new InputJournalWriter(stream, new Dictionary<string, string>()))
        {
            Assert.Throws<NotSupportedException>(() =>
                writer.WriteFrame(1, InputFrame.Of(new SlapCreatureCommand(P, 7), new UntaggedCommand(P))));
            writer.WriteEnd(1, "exception");
        }

        InputJournal journal = InputJournal.Read(new MemoryStream(stream.ToArray()));
        Assert.Empty(journal.FrameTicks);
        Assert.Equal("exception", journal.EndReason);
    }

    private sealed record UntaggedCommand(PlayerId PlayerId) : PlayerCommand(PlayerId);

    [Fact]
    public void AnotherFormatVersion_IsRefused()
    {
        byte[] bytes = Written();
        bytes[4] = 99;

        Assert.Throws<InvalidDataException>(() => InputJournal.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void AnEmptyFrame_IsNotWritten()
    {
        var stream = new MemoryStream();
        using (var writer = new InputJournalWriter(stream, new Dictionary<string, string>()))
            writer.WriteFrame(0, InputFrame.Empty);

        Assert.Empty(InputJournal.Read(new MemoryStream(stream.ToArray())).FrameTicks);
    }

    private static byte[] Written()
    {
        var stream = new MemoryStream();
        using (var writer = new InputJournalWriter(stream, new Dictionary<string, string> { ["commit"] = "abc123" }))
        {
            writer.WriteHash(0, 0xA0);
            writer.WriteFrame(1, InputFrame.Of(TagTilesCommand.Tag(P, new TilePos(1, 2))));
            for (long t = 1; t <= 4; t++)
            {
                if (t == 3)
                    writer.WriteFrame(3, InputFrame.Of(new SlapCreatureCommand(P, 7), new CreateImpCommand(P)));
                writer.WriteHash(t, 0xA0UL + (ulong)t);
            }
            writer.WriteEnd(4, "completed");
        }
        return stream.ToArray();
    }

    private static int HeaderLength()
    {
        var stream = new MemoryStream();
        using (new InputJournalWriter(stream, new Dictionary<string, string> { ["commit"] = "abc123" })) { }
        return (int)stream.Length;
    }
}
