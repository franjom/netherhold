// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Commands;
using DK2.Tools;

namespace DK2.Tools.Tests;

public sealed class PacketPlaybackClockTests
{
    // 00522790 stores clock+1; 0055EA10 reads sequential count/header, actions,
    // trailer before world00526AA7. Native completed1615 consumes stamp1616.
    [Theory]
    [InlineData(2u)]
    [InlineData(4013u)]
    public void FirstRecordExecutesOnPlaybackTurnOneRegardlessOfStoredStamp(uint firstStamp)
    {
        WithPacket([firstStamp, firstStamp + 1], packet =>
        {
            PacketLog log = PacketLog.Read(packet, 1, 69);
            var map = new PacketEntityMap();
            map.Add(453, 1, 8, PacketEntityKind.Creature);
            Assert.Equal(new PickUpCreatureCommand(3, 8), Assert.Single(log.At(1, map).All));
            Assert.Equal(new[] { (1L, 453u, PacketEntityKind.Creature) }, log.EntityReferences);
            Assert.Empty(log.At(2, map).All);
        });
    }

    [Fact]
    public void EntityResolutionUsesThePlaybackTurnWhenASlotIsReused()
    {
        WithPacket([2, 3, 4], packet =>
        {
            PacketLog log = PacketLog.Read(packet, 2, 69);
            var map = new PacketEntityMap();
            map.Add(453, 1, 8, PacketEntityKind.Creature);
            map.Add(453, 2, 9, PacketEntityKind.Creature);
            Assert.Equal(new PickUpCreatureCommand(3, 8), Assert.Single(log.At(1, map).All));
            Assert.Equal(new PickUpCreatureCommand(3, 9), Assert.Single(log.At(2, map).All));
        });
    }

    [Fact]
    public void FinalCompleteRecordNeedsNoFollowingTimestampHeader()
    {
        WithPacket([1], packet =>
        {
            PacketLog log = PacketLog.Read(packet, 1, 69);
            Assert.Equal(1, log.ActionCount);
            Assert.Throws<InvalidDataException>(() => PacketLog.Read(packet, 2, 69));
        });
    }

    private static void WithPacket(uint[] stamps, Action<string> check)
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(new byte[68]);
                foreach (uint stamp in stamps)
                {
                    writer.Write((byte)1);
                    writer.Write(stamp);
                    byte[] restOfHeader = new byte[12];
                    restOfHeader[8] = restOfHeader[9] = 1;
                    writer.Write(restOfHeader);
                    writer.Write(453u);
                    writer.Write(0u);
                    writer.Write(0u);
                    writer.Write(57u);
                    writer.Write((ushort)3);
                    writer.Write(new byte[12]);
                }
            }
            check(path);
        }
        finally { File.Delete(path); }
    }
}
