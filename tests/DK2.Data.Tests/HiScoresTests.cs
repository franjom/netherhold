// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class HiScoresTests
{
    [Fact]
    public void ReadsTenNativeRowsIgnoringStaleBytesAfterTerminators()
    {
        byte[] data = new byte[1320];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 123456);
        Encoding.Unicode.GetBytes("Keeper\0stale").CopyTo(data, 4);
        Encoding.Unicode.GetBytes("6a\0old").CopyTo(data, 68);
        IReadOnlyList<HiScoreEntry> entries = HiScoresFile.Read(data);
        Assert.Equal(10, entries.Count);
        Assert.Equal(new HiScoreEntry(123456, "Keeper", "6a"), entries[0]);
        byte[] encoded = HiScoresFile.Write(entries);
        Assert.Equal((byte)0, encoded[18]); Assert.Equal((byte)0, encoded[76]);
        Assert.Equal(entries, HiScoresFile.Read(encoded));
        Assert.Throws<InvalidDataException>(() => HiScoresFile.Read(new byte[450]));
        Assert.Throws<InvalidDataException>(() => HiScoresFile.Read(data[..^1]));
        Assert.Throws<ArgumentException>(() => HiScoresFile.Write(entries.Take(9).ToArray()));
    }

    [Fact]
    public void WritesUnicodeWithATerminatorAndRejectsNamesThatDoNotFit()
    {
        var entries = Enumerable.Repeat(new HiScoreEntry(uint.MaxValue, "Keeper \u017e", "11c"), 10).ToArray();
        Assert.Equal(entries, HiScoresFile.Read(HiScoresFile.Write(entries)));
        entries[0] = entries[0] with { Name = new string('x', 32) };
        Assert.Throws<ArgumentException>(() => HiScoresFile.Write(entries));
        entries[0] = entries[0] with { Name = "bad\0name" };
        Assert.Throws<ArgumentException>(() => HiScoresFile.Write(entries));
    }

    [InstallFact]
    public void InstalledRuntimeTableRoundTripsWithoutWritingToTheInstall()
    {
        string path = Path.Combine(Dk2InstallGate.Path, "Data", "Settings", "HiScores.dat");
        byte[] before = File.ReadAllBytes(path);
        var scores = HiScoresFile.Read(before);
        Assert.Equal(10, scores.Count);
        Assert.Equal(scores, HiScoresFile.Read(HiScoresFile.Write(scores)));
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
