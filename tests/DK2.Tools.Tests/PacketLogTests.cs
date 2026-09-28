// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Simulation.Commands;
using DK2.Tools;

namespace DK2.Tools.Tests;

public sealed class PacketLogTests
{
    [Fact]
    public void ReadsRecordedTurnsAndRequiresObservedCreatureIds()
    {
        var bytes = new List<byte>(new byte[69]);
        Turn(bytes, 2, (Pack(12, 20), Pack(13, 21), 0, 40), (453, 0, 0, 57));
        Turn(bytes, 3, (453, 0, 0, 57), (7u << 12, 9u << 12, 1u << 24, 61));
        Turn(bytes, 4);
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [.. bytes]);
            PacketLog packet = PacketLog.Read(path, 3, 69);
            Assert.Equal(4, packet.ActionCount);

            var map = new PacketEntityMap();
            Assert.Throws<InvalidDataException>(() => packet.At(3, map));
            map.Add(453, 2, 8, PacketEntityKind.Creature);
            map.Add(453, 3, 9, PacketEntityKind.Creature);

            var early = packet.At(2, map).All;
            var tag = Assert.IsType<TagTilesCommand>(early[0]);
            Assert.Equal(4, tag.Tiles.Count);
            Assert.Equal(8, Assert.IsType<PickUpCreatureCommand>(early[1]).CreatureId);

            var hand = packet.At(3, map).All;
            Assert.Equal(9, Assert.IsType<PickUpCreatureCommand>(hand[0]).CreatureId);
            var drop = Assert.IsType<DropCommand>(hand[1]);
            Assert.Equal((7, 9), (drop.Tile.X, drop.Tile.Y));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SpecialActivationRequiresObservedObjectMapping()
    {
        var bytes = new List<byte>(new byte[69]);
        Turn(bytes, 2, (1128, 0, 0, 88));
        Turn(bytes, 3);
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [.. bytes]);
            PacketLog packet = PacketLog.Read(path, 2, 69);
            Assert.Contains(packet.EntityReferences, reference =>
                reference == (2L, 1128u, PacketEntityKind.Object));
            var map = new PacketEntityMap();
            Assert.Throws<InvalidDataException>(() => packet.At(2, map));
            map.Add(1128, 1, 1, PacketEntityKind.Creature);
            Assert.Throws<InvalidDataException>(() => packet.At(2, map));
            map.Add(1128, 2, 1, PacketEntityKind.Object);
            var command = Assert.IsType<ActivateSpecialCommand>(Assert.Single(packet.At(2, map).All));
            Assert.Equal((3, 1), (command.PlayerId, command.ObjectId));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecordingOriginIsExplicitAndFirstTurnComesFromTheRecord()
    {
        var bytes = new List<byte>(new byte[83]);
        for (uint turn = 100; turn <= 10100; turn++) Turn(bytes, turn);
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [.. bytes]);
            Assert.Equal(0, PacketLog.Read(path, 10099, 83).ActionCount);
            Assert.Throws<InvalidDataException>(() => PacketLog.Read(path, 10100, 83));
            Assert.Throws<InvalidDataException>(() => PacketLog.Read(path, 10099, 69));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CsvRequiresTypesAndUsesTheLatestSlotOccupant()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "original,from_turn,ours\n450,2,1\n");
            Assert.Throws<InvalidDataException>(() => PacketEntityMap.Read(path));
            File.WriteAllText(path, "original,from_turn,ours,kind\n450,2,1,Creature\n450,10,1,Object\n");
            PacketEntityMap map = PacketEntityMap.Read(path);
            Assert.True(map.TryResolve(450, 9, PacketEntityKind.Creature, out int creature));
            Assert.Equal(1, creature);
            Assert.False(map.TryResolve(450, 10, PacketEntityKind.Creature, out _));
            Assert.True(map.TryResolve(450, 10, PacketEntityKind.Object, out int item));
            Assert.Equal(1, item);
        }
        finally { File.Delete(path); }
    }

    private static uint Pack(uint x, uint y) => x | y << 16;

    private static void Turn(List<byte> bytes, uint turn,
        params (uint First, uint Second, uint Third, uint Kind)[] actions)
    {
        var header = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, turn);
        header[12] = 1;
        header[13] = 1;
        bytes.AddRange(header);
        foreach (var action in actions)
        {
            var body = new byte[18];
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0, 4), action.First);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4, 4), action.Second);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8, 4), action.Third);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(12, 4), action.Kind);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(16, 2), 3);
            bytes.AddRange(body);
        }
        bytes.AddRange(new byte[13]);
    }
}
