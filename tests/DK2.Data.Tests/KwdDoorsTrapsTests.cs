// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdDoorsTrapsTests
{
    [Fact]
    public void TrapThreatIsAnUnsignedWordIndependentOfNeighbouringFields()
    {
        byte[] record = new byte[579];
        record[0x1E9] = 0xFF;
        record[0x1EA] = 0xCD;
        record[0x1EB] = 0xAB;
        record[0x1EC] = 0xFF;
        var chunk = new KwdChunk
        {
            Type = KwdRecordType.Traps, ItemCount = 1, Content = record, HeaderBlock = [],
        };
        Assert.Equal((ushort)0xABCD, Assert.Single(KwdTraps.Parse([chunk])).Threat);
    }

    [Fact]
    public void RuntimeColumnsKeepSignedWordsDwordsAndHighFlagBits()
    {
        byte[] bytes = new byte[579];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x1EC), 0xF1234567);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x1C8), -4097);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x1D4), -250);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x1E0), int.MinValue);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x231), -2048);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x1EA), 65530);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x241), -32760);
        TrapDef trap = Assert.Single(KwdTraps.Parse([new KwdChunk
        {
            Type = KwdRecordType.Traps, ItemCount = 1, Content = bytes, HeaderBlock = [],
        }]));
        Assert.Equal(0xF1234567u, trap.Flags);
        Assert.Equal(-4097, trap.RechargeSecondsRaw);
        Assert.Equal(-250, trap.ManaCostToFire);
        Assert.Equal(int.MinValue, trap.ShotData1);
        Assert.Equal(-2048, trap.ShotOffsetX);
        Assert.Equal((ushort)65530, trap.Threat);
        Assert.Equal((short)-32760, trap.HealthGain);

        byte[] doorBytes = new byte[616];
        BinaryPrimitives.WriteInt16LittleEndian(doorBytes.AsSpan(0x21C), -32760);
        DoorDef door = Assert.Single(KwdDoors.Parse([new KwdChunk
        {
            Type = KwdRecordType.Doors, ItemCount = 1, Content = doorBytes, HeaderBlock = [],
        }]));
        Assert.Equal((short)-32760, door.HealthGain);
    }

    private static string Editor()
    {
        string editor = Path.Combine(Dk2InstallGate.Path, "Data", "editor");
        Assert.True(Directory.Exists(editor), $"the install has no {editor}");
        return editor;
    }

    [InstallFact]
    public void DoorsHaveEightReadableRecordsWithContiguousIds()
    {
        string editor = Editor();

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

    [InstallFact]
    public void TrapsHaveSixteenReadableRecordsWithContiguousIds()
    {
        string editor = Editor();

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
