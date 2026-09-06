// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

public class KwdDoorsTrapsTests
{
    private static string? Editor()
    {
        string? install = Dk2Install.Find();
        if (install is null) return null;
        string editor = Path.Combine(install, "Data", "editor");
        return Directory.Exists(editor) ? editor : null;
    }

    [Fact]
    public void DoorsHaveEightReadableRecordsWithContiguousIds()
    {
        string? editor = Editor();
        if (editor is null) return;

        List<DoorDef> doors = KwdDoors.Parse(
            KwdReader.ReadFile(Path.Combine(editor, "Doors.kwd")));

        Assert.Equal(8, doors.Count);
        Assert.Equal(
            Enumerable.Range(1, 8).Select(i => (byte)i),
            doors.Select(d => d.DoorId).OrderBy(id => id));
        Assert.Equal(["Wooden Door", "Steel Door", "Braced Door"],
            doors.Take(3).Select(d => d.Name));
        Assert.All(doors, door =>
        {
            Assert.False(string.IsNullOrWhiteSpace(door.Name));
            Assert.InRange(door.Health, (ushort)1, ushort.MaxValue);
            Assert.InRange(door.GoldCost, (ushort)1, ushort.MaxValue);
            Assert.InRange(door.ManufactureRequired, 1u, 100_000u);
        });
    }

    [Fact]
    public void TrapsHaveSixteenReadableRecordsWithContiguousIds()
    {
        string? editor = Editor();
        if (editor is null) return;

        List<TrapDef> traps = KwdTraps.Parse(
            KwdReader.ReadFile(Path.Combine(editor, "Traps.kwd")));

        Assert.Equal(16, traps.Count);
        Assert.Equal(
            Enumerable.Range(1, 16).Select(i => (byte)i),
            traps.Select(t => t.TrapId).OrderBy(id => id));
        Assert.Equal("Gas Trap", traps[0].Name);
        Assert.Equal("Jack In The Box", traps[^1].Name);
        Assert.All(traps, trap =>
        {
            Assert.False(string.IsNullOrWhiteSpace(trap.Name));
            Assert.InRange(trap.Health, (ushort)1, ushort.MaxValue);
            Assert.InRange(trap.GoldCost, (ushort)1, ushort.MaxValue);
            Assert.InRange(trap.ManufactureRequired, (ushort)1, ushort.MaxValue);
            Assert.InRange(trap.ShotTypeId, (byte)0, (byte)65);
        });
    }

    [Theory]
    [InlineData(KwdRecordType.Doors, 615)]
    [InlineData(KwdRecordType.Traps, 578)]
    public void ATruncatedRecordIsRejected(KwdRecordType type, int size)
    {
        var chunk = new KwdChunk
        {
            Type = type,
            ItemCount = 1,
            Content = new byte[size],
            HeaderBlock = [],
        };

        Assert.Throws<InvalidDataException>(() => type == KwdRecordType.Doors
            ? KwdDoors.Parse([chunk])
            : KwdTraps.Parse([chunk]));
    }
}
