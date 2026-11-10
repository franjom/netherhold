// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// A journal is read back by tools and, under ADR-0724 R6, kept by the relay. Overwriting any word
/// after the header with a huge or negative value must either read (as a truncated journal) or
/// throw <see cref="InvalidDataException"/>, without allocating far beyond the file's size.
/// </summary>
public class InputJournalHostileInputTests
{
    private const long MaxBytesPerRead = 1024 * 1024;

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(0x40000000)]
    public void AnyWordOverwritten_ReadsOrFailsInAControlledWay(int value)
    {
        (byte[] journal, int header) = Journal();
        for (int at = header; at + 4 <= journal.Length; at++)
        {
            byte[] input = (byte[])journal.Clone();
            BitConverter.TryWriteBytes(input.AsSpan(at), value);
            AssertControlled(input, at);
        }
    }

    private static void AssertControlled(byte[] input, int at)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            InputJournal.Read(new MemoryStream(input));
        }
        catch (InvalidDataException)
        {
        }
        catch (Exception e)
        {
            Assert.Fail($"{e.GetType().Name} with the word at {at} overwritten: {e.Message}");
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < MaxBytesPerRead, $"the word at {at} overwritten allocated {allocated} bytes");
    }

    private static (byte[] Journal, int Header) Journal()
    {
        var keeper = new PlayerId(PlayerIds.FirstKeeper);
        using var stream = new MemoryStream();
        int header;
        using (var writer = new InputJournalWriter(stream, new Dictionary<string, string> { ["seed"] = "7" }))
        {
            header = (int)stream.Length;
            writer.WriteFrame(0, InputFrame.Empty);
            writer.WriteFrame(3, InputFrame.Of(
                new TagTilesCommand(keeper, [new TilePos(4, 5), new TilePos(5, 5)], true),
                new SlapCreatureCommand(keeper, 12)));
            writer.WriteHash(3, 0x0123456789ABCDEF);
            writer.WriteEnd(4, "done");
        }
        return (stream.ToArray(), header);
    }
}
