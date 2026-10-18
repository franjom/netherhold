// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public sealed class CreatureNamePoolTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(64)]
    public void RejectsTruncatedExecutableHeaders(int length) =>
        Assert.Throws<InvalidDataException>(() => CreatureNamePool.Read(new byte[length]));

    [Fact]
    public void RejectsASectionTableOutsideTheFileWithADataError()
    {
        byte[] data = new byte[128];
        data[0] = (byte)'M'; data[1] = (byte)'Z'; data[0x3c] = 64;
        data[64] = (byte)'P'; data[65] = (byte)'E'; data[70] = 1;
        data[84] = 0xe0; data[88] = 0x0b; data[89] = 1; data[118] = 0x40;
        Assert.Throws<InvalidDataException>(() => CreatureNamePool.Read(data));
    }
    [InstallFact]
    public void ReadsTheOriginalIndexedNamesAndBloodGroupsFromTheInstalledBinary()
    {
        var pool = CreatureNamePool.Open(Path.Combine(Dk2InstallGate.Path, "DKII.EXE"));
        Assert.Equal("Delver", pool.Name(new CreatureNameIndex(0)));
        Assert.Equal(500, pool.Names.Count);
        Assert.Equal(7, pool.BloodGroups.Count);
        Assert.All(pool.Names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Contains("Rh", pool.BloodGroup(new CreatureBloodGroupIndex(0)));
        Assert.Equal("", pool.Name(new CreatureNameIndex(ushort.MaxValue)));
    }
}
