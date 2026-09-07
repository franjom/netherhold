// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdAdditionalRecordTests
{
    [InstallFact]
    public void ShippedCorpus_ParsesAllEighteenTypes_WithPinnedCountsAndRoundTrips()
    {
        KwdCorpusAudit.Result audit = KwdCorpusAudit.Run(Dk2InstallGate.Path);
        Assert.Equal(408, audit.Files);
        Assert.Equal(18, audit.ParsedTypes.Count);

        var expected = new Dictionary<KwdRecordType, int>
        {
            [KwdRecordType.Globals] = 0,
            [KwdRecordType.Map] = 345694,
            [KwdRecordType.Terrain] = 41,
            [KwdRecordType.Rooms] = 26,
            [KwdRecordType.Traps] = 16,
            [KwdRecordType.Doors] = 8,
            [KwdRecordType.KeeperSpells] = 13,
            [KwdRecordType.CreatureSpells] = 41,
            [KwdRecordType.Creatures] = 46,
            [KwdRecordType.Players] = 274,
            [KwdRecordType.Things] = 6318,
            [KwdRecordType.Triggers] = 9161,
            [KwdRecordType.Level] = 66,
            [KwdRecordType.Variables] = 8246,
            [KwdRecordType.Objects] = 145,
            [KwdRecordType.EffectElements] = 596,
            [KwdRecordType.Shots] = 65,
            [KwdRecordType.Effects] = 370,
        };

        Assert.Equal(expected.Count, audit.RecordCounts.Count);
        foreach ((KwdRecordType type, int count) in expected)
            Assert.Equal(count, audit.RecordCounts[type]);
    }

    [Theory]
    [InlineData(KwdRecordType.CreatureSpells, KwdAdditionalRecords.CreatureSpellSize)]
    [InlineData(KwdRecordType.Shots, KwdAdditionalRecords.ShotSize)]
    [InlineData(KwdRecordType.Effects, KwdAdditionalRecords.EffectSize)]
    [InlineData(KwdRecordType.EffectElements, KwdAdditionalRecords.EffectElementSize)]
    public void FixedDefinitionRecord_RoundTripsEveryByte(KwdRecordType type, int stride)
    {
        byte[] bytes = Pattern(stride);
        KwdChunk chunk = Chunk(type, bytes);

        KwdRoundTripRecord record = type switch
        {
            KwdRecordType.CreatureSpells => KwdAdditionalRecords.ParseCreatureSpells([chunk]).Single(),
            KwdRecordType.Shots => KwdAdditionalRecords.ParseShots([chunk]).Single(),
            KwdRecordType.Effects => KwdAdditionalRecords.ParseEffects([chunk]).Single(),
            KwdRecordType.EffectElements => KwdAdditionalRecords.ParseEffectElements([chunk]).Single(),
            _ => throw new InvalidOperationException(),
        };

        Assert.Equal(stride, record.ConsumedLength);
        Assert.Equal(bytes, record.ToBytes());
    }

    [Theory]
    [MemberData(nameof(ThingLayouts))]
    public void EveryThingPayload_RoundTripsEveryByte(int tag, int payloadSize)
    {
        byte[] bytes = Pattern(payloadSize);
        ThingRecord record = KwdThings.Parse(tag, bytes);
        Assert.Equal(tag, record.Tag);
        Assert.Equal(payloadSize, record.ConsumedLength);
        Assert.Equal(bytes, record.ToBytes());
    }

    public static TheoryData<int, int> ThingLayouts()
    {
        var data = new TheoryData<int, int>();
        foreach ((int tag, (string _, int size, bool _)) in KwdTaggedRecords.ThingPayloadSizes)
            data.Add(tag, size);
        return data;
    }

    [Theory]
    [InlineData(213)]
    [InlineData(214)]
    public void EveryTriggerPayload_RoundTripsEveryByte(int tag)
    {
        byte[] bytes = Pattern(16);
        bytes[14] = 0; // an unknown type deliberately exposes both body dwords as Unknown_*
        byte[] tagged = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(tagged, tag);
        BinaryPrimitives.WriteInt32LittleEndian(tagged.AsSpan(4), 16);
        bytes.CopyTo(tagged, 8);

        TriggerRecord record = KwdTriggers.Parse([Chunk(KwdRecordType.Triggers, tagged)]).Single();
        Assert.Equal(16, record.ConsumedLength);
        Assert.Equal(bytes, record.ToBytes());
        Assert.Equal(8, record.Body.Length);
        Assert.Contains("Unknown_0000", record.Parameters.Keys);
        Assert.Contains("Unknown_0004", record.Parameters.Keys);
    }

    [Fact]
    public void LevelAndGlobalsReference_RoundTripEveryByte()
    {
        const int metadataSize = 25603;
        byte[] content = Pattern(metadataSize + 8 + 72);
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(metadataSize), 222);
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(metadataSize + 4), 72);
        BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(metadataSize + 8), (int)KwdRecordType.Globals);

        byte[] header = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(header, 1);
        LevelDef level = KwdLevel.Parse([new KwdChunk
        {
            Type = KwdRecordType.Level, ItemCount = 1, HeaderBlock = header, Content = content,
        }]) ?? throw new InvalidOperationException();

        Assert.Equal(content.Length, level.ConsumedLength);
        Assert.Equal(content, level.ToBytes());
        Assert.Equal(KwdRecordType.Globals, level.Paths.Single().RecordType);
    }

    [Fact]
    public void FixedDefinitionParserRejectsAPartialRecord()
    {
        KwdChunk chunk = Chunk(KwdRecordType.Shots, new byte[KwdAdditionalRecords.ShotSize - 1]);
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => KwdAdditionalRecords.ParseShots([chunk]));
        Assert.Contains("239", error.Message);
    }

    private static KwdChunk Chunk(KwdRecordType type, byte[] content) => new()
    {
        Type = type, ItemCount = 1, HeaderBlock = new byte[28], Content = content,
    };

    private static byte[] Pattern(int size) =>
        Enumerable.Range(0, size).Select(i => (byte)(i * 37 + 11)).ToArray();
}
