// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdRecordJoinTests
{
    [Theory]
    [InlineData(KwdRecordType.Terrain, 552)]
    [InlineData(KwdRecordType.Rooms, 1055)]
    [InlineData(KwdRecordType.Traps, 579)]
    [InlineData(KwdRecordType.Doors, 616)]
    [InlineData(KwdRecordType.KeeperSpells, 406)]
    [InlineData(KwdRecordType.CreatureSpells, 266)]
    [InlineData(KwdRecordType.Creatures, 5449)]
    [InlineData(KwdRecordType.Creatures, 5537)]
    [InlineData(KwdRecordType.Players, 205)]
    [InlineData(KwdRecordType.Variables, 16)]
    [InlineData(KwdRecordType.Objects, 894)]
    [InlineData(KwdRecordType.EffectElements, 182)]
    [InlineData(KwdRecordType.Shots, 239)]
    [InlineData(KwdRecordType.Effects, 246)]
    [InlineData(KwdRecordType.Map, 4)]
    public void FixedImagesJoinAtTheirMeasuredStrideAndKeepEveryUnknownByte(KwdRecordType type, int stride)
    {
        byte[] content = Enumerable.Range(0, stride * 2).Select(i => (byte)(i % 251)).ToArray();
        KwdChunk chunk = Chunk(type, content, type == KwdRecordType.Map ? 0 : 2);
        List<KwdRecordImage> records = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(stride, r.ConsumedLength));
        Assert.Equal(content, KwdRecordRoundTrip.Join(chunk, records));
    }

    [Theory]
    [InlineData(KwdRecordType.Things, 194, 24)]
    [InlineData(KwdRecordType.Triggers, 213, 16)]
    public void TaggedImagesKeepHeadersAndPayloadBytes(KwdRecordType type, int tag, int payloadSize)
    {
        byte[] content = new byte[8 + payloadSize];
        BinaryPrimitives.WriteInt32LittleEndian(content, tag);
        BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(4), payloadSize);
        content[^1] = 0xDA;
        KwdChunk chunk = Chunk(type, content, 1);
        List<KwdRecordImage> images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(content, KwdRecordRoundTrip.Join(chunk, images));
        byte[] damaged = images[0].ToBytes();
        damaged[4]++;
        Assert.Throws<InvalidDataException>(() => KwdRecordRoundTrip.Join(chunk,
            [new KwdRecordImage(type, damaged, tag)]));
        Assert.Throws<InvalidDataException>(() => KwdRecordRoundTrip.Join(chunk,
            [new KwdRecordImage(type, content, tag + 1)]));
    }

    [Fact]
    public void JoinRejectsWrongTypeCountAndStride()
    {
        KwdChunk chunk = Chunk(KwdRecordType.Doors, new byte[616], 1);
        Assert.Throws<InvalidDataException>(() => KwdRecordRoundTrip.Join(chunk, []));
        Assert.Throws<InvalidDataException>(() => KwdRecordRoundTrip.Join(chunk,
            [new KwdRecordImage(KwdRecordType.Traps, new byte[616])]));
        Assert.Throws<InvalidDataException>(() => KwdRecordRoundTrip.Join(chunk,
            [new KwdRecordImage(KwdRecordType.Doors, new byte[615])]));
        KwdChunk creature = Chunk(KwdRecordType.Creatures, new byte[5537], 1);
        Assert.Throws<InvalidDataException>(() => KwdRecordRoundTrip.Join(creature,
            [new KwdRecordImage(KwdRecordType.Creatures, new byte[5449])]));
    }

    [Fact]
    public void GlobalsSyntheticPathStaysInLevelAndStandaloneLayoutRemainsUnknown()
    {
        byte[] content = new byte[25603 + 8 + 72];
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(25603), 222);
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(25607), 72);
        content[25603 + 8 + 8] = (byte)'x';
        KwdChunk chunk = Chunk(KwdRecordType.Level, content, 1);
        LevelDef level = KwdLevel.Parse([chunk])!;
        Assert.Equal(KwdRecordType.Globals, level.Paths.Single().RecordType);
        Assert.Equal(content, KwdRecordRoundTrip.Join(chunk, KwdRecordRoundTrip.Split(chunk)));
        Assert.Throws<InvalidDataException>(() => KwdRecordRoundTrip.Split(
            Chunk(KwdRecordType.Globals, new byte[72], 1)));
    }

    [Fact]
    public void EmptyFixedAndTaggedChunksJoinWithoutInventingRecords()
    {
        foreach (KwdRecordType type in new[] { KwdRecordType.Creatures, KwdRecordType.Doors,
            KwdRecordType.Things, KwdRecordType.Triggers })
        {
            KwdChunk chunk = Chunk(type, [], 0);
            Assert.Empty(KwdRecordRoundTrip.Join(chunk, KwdRecordRoundTrip.Split(chunk)));
        }
    }

    [InstallFact]
    public void EveryShippedChunkSplitJoinIsIdentical()
    {
        string editor = Path.Combine(Dk2InstallGate.Path, "Data", "editor");
        string maps = Path.Combine(editor, "maps");
        string[] files = Directory.GetFiles(editor, "*.kwd")
            .Concat(Directory.GetFiles(maps, "*.kwd"))
            .Concat(Directory.GetFiles(maps, "*.kld")).ToArray();
        Assert.Equal(408, files.Length);
        int total = 0;
        foreach (string file in files)
        foreach (KwdChunk chunk in KwdReader.ReadFile(file))
        {
            Assert.Equal(chunk.Content, KwdRecordRoundTrip.Join(chunk, KwdRecordRoundTrip.Split(chunk)));
            total++;
        }
        Assert.Equal(409, total);
        Assert.Equal(0, KwdCorpusAudit.Run(Dk2InstallGate.Path).RecordCounts[KwdRecordType.Globals]);
    }

    private static KwdChunk Chunk(KwdRecordType type, byte[] content, int count)
    {
        byte[] header = new byte[28];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)count);
        return new KwdChunk { Type = type, Content = content, HeaderBlock = header, ItemCount = count };
    }
}
