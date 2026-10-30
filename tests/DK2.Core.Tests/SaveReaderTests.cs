// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Core.Tests;

public class SaveReaderTests
{
    [Theory]
    [InlineData(-1, 4, 10, SaveIncompatibleReason.OutOfRange)]
    [InlineData(11, 4, 10, SaveIncompatibleReason.OutOfRange)]
    [InlineData(3, 4, 10, SaveIncompatibleReason.Truncated)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue, SaveIncompatibleReason.Truncated)]
    public void CountsAreCheckedWithoutOverflow(int count, int minimumBytes, int maximum, SaveIncompatibleReason reason)
    {
        using var stream = new MemoryStream(new byte[8]);
        using var reader = new BinaryReader(stream);
        var error = Assert.Throws<SaveIncompatibleException>(() => SaveReader.CheckCount(reader, count, "entries", minimumBytes, maximum));
        Assert.Equal(reason, error.Reason);
        Assert.Equal(0L, error.Position);
    }

    [Theory]
    [InlineData(0, 0xef46db3751d8e999UL)]
    [InlineData(1, 0xe934a84adb052768UL)]
    [InlineData(3, 0xe5c7bb4533bc65ddUL)]
    [InlineData(4, 0xffced8604453cc1eUL)]
    [InlineData(8, 0x884a173614b81b8dUL)]
    [InlineData(31, 0xc346d2b59b4d8ee1UL)]
    [InlineData(32, 0xcbf59c5116ff32b4UL)]
    [InlineData(33, 0x0c535d1acafb8eadUL)]
    [InlineData(63, 0xe26aa9e2a95f8e4fUL)]
    [InlineData(64, 0xf7c67301db6713f0UL)]
    [InlineData(100, 0x6ac1e58032166597UL)]
    public void ChecksumMatchesReferenceXxHash64(int length, ulong expected)
    {
        // Independently evaluated by libxxhash.so.0, seed 0, byte sequence 0..length-1.
        byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        Assert.Equal(expected, PayloadChecksum.Compute(bytes));
    }
}
