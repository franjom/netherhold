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

    [InstallFact]
    public void ShippedTriggerTypes_HavePinnedDistribution()
    {
        IReadOnlyList<KwdTriggerCorpusAudit.Entry> entries =
            KwdTriggerCorpusAudit.Run(Dk2InstallGate.Path);
        Dictionary<byte, int> generic = ParseCounts(
            "1:1375,2:303,3:22,4:37,6:158,8:88,10:4,12:2,13:4,16:30,19:205,20:2," +
            "22:26,23:1,24:24,25:85,27:3,28:7,29:80,30:11,31:9,32:1,34:325,36:40," +
            "39:183,40:92,41:69,42:89,44:13,46:1,47:72,52:3,53:159,56:1,57:1,58:1," +
            "60:1,62:32,64:37,65:1,68:5,69:5,70:6,71:2,72:137,73:1");
        Dictionary<byte, int> action = ParseCounts(
            "1:268,2:5,4:1,6:303,7:1417,8:291,9:78,10:40,11:47,14:259,15:565," +
            "16:109,17:291,18:27,20:16,21:2,22:160,23:86,24:827,26:100,27:18,28:10," +
            "30:15,31:60,32:67,33:72,34:2,35:27,37:22,38:34,42:59,43:7,44:2,45:18," +
            "47:84,49:3,50:3,51:13");

        Assert.Equal(84, entries.Count);
        Assert.Equal(9161, entries.Sum(e => e.Count));
        Assert.Equal(generic, entries.Where(e => e.Tag == 213).ToDictionary(e => e.Type, e => e.Count));
        Assert.Equal(action, entries.Where(e => e.Tag == 214).ToDictionary(e => e.Type, e => e.Count));
        Assert.Equal(["level1"], entries.Single(e => e.Tag == 213 && e.Type == 46).Levels);
        Assert.Equal(["Level19"], entries.Single(e => e.Tag == 214 && e.Type == 4).Levels);
    }

    [Fact]
    public void TriggerParameterViews_MatchBinaryReadWidthsAndOffsets()
    {
        TriggerRecord generic39 = Trigger(213, 39, [1, 2, 3, 4, 0xfc, 0xff, 0xff, 0xff]);
        Assert.Equal(2, generic39.Parameters["PlayerId"]);
        Assert.Equal(3, generic39.Parameters["TypeId"]);
        Assert.Equal(4, generic39.Parameters["ThingType"]);
        Assert.Equal(-4, generic39.Parameters["Value"]);

        TriggerRecord generic47 = Trigger(213, 47, [7, 1, 2, 3, 4, 5, 6, 7]);
        Assert.Equal(7, generic47.Parameters["PlayerId"]);
        Assert.DoesNotContain("CreatureId", generic47.Parameters.Keys);
        Assert.DoesNotContain("Value", generic47.Parameters.Keys);

        TriggerRecord generic52 = Trigger(213, 52, [1, 2, 3, 9, 4, 0, 0, 0]);
        Assert.Equal(9, generic52.Parameters["Level"]);
        Assert.DoesNotContain("PlayerId", generic52.Parameters.Keys);

        TriggerRecord generic64 = Trigger(213, 64, [5, 7, 2, 3, 4, 0, 0, 0]);
        Assert.Equal(7, generic64.Parameters["PartyId"]);
        Assert.DoesNotContain("PlayerId", generic64.Parameters.Keys);

        TriggerRecord generic70 = Trigger(213, 70, [0, 2, 3, 6, 4, 0, 0, 0]);
        Assert.Equal(2, generic70.Parameters["PlayerId"]);
        Assert.Equal(3, generic70.Parameters["TypeId"]);
        Assert.Equal(6, generic70.Parameters["ThingType"]);
        Assert.DoesNotContain("TargetId", generic70.Parameters.Keys);

        TriggerRecord action4 = Trigger(214, 4, [0x34, 0x12, 0x78, 0x56, 9, 1, 2, 3]);
        Assert.Equal(0x1234, action4.Parameters["ObjectiveId"]);
        Assert.Equal(9, action4.Parameters["ActionPointId"]);

        TriggerRecord action8 = Trigger(214, 8, [7, 1, 2, 3, 4, 5, 6, 7]);
        Assert.Equal(7, action8.Parameters["TimerId"]);
        Assert.DoesNotContain("Value", action8.Parameters.Keys);

        TriggerRecord action16 = Trigger(214, 16, [7, 1, 2, 3, 0xfe, 0xff, 6, 7]);
        Assert.Equal(-2, action16.Parameters["Value"]);

        TriggerRecord action26 = Trigger(214, 26, [7, 1, 2, 3, 4, 5, 6, 7]);
        Assert.Equal(7, action26.Parameters["ActionPointId"]);
        Assert.DoesNotContain("TargetId", action26.Parameters.Keys);

        // Q227: the roles DKII.EXE gives the fields once read as Unknown_* (ADR-0382).
        Assert.Equal(5, Trigger(213, 23, [1, 2, 5, 3, 4, 0, 0, 0]).Parameters["PlayerSlot"]);
        TriggerRecord action24 = Trigger(214, 24, [9, 0, 0, 0, 1, 0xff, 0xff, 0xff]);
        Assert.Equal(1, action24.Parameters["SpeakOnly"]);
        Assert.Equal(9, action24.Parameters["SpeechId"]);
        Assert.Equal(1, Trigger(214, 35, [1, 2, 3, 4, 5, 6, 7, 8]).Parameters["DeathKind"]);
        Assert.Equal(3, Trigger(214, 39, [3, 4, 0, 0, 1, 0, 2, 0]).Parameters["DoorId"]);
        Assert.Equal(6, Trigger(214, 40, [6, 4, 0, 0, 1, 0, 2, 0]).Parameters["TrapId"]);
        Assert.Equal(0x0102, Trigger(214, 41, [2, 1, 0, 0, 1, 0, 2, 0]).Parameters["EffectId"]);
    }

    [InstallFact]
    public void ShippedTriggerParameterViews_UseBinaryReadNames()
    {
        string maps = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "maps");
        TriggerRecord[] triggers = Directory.GetFiles(maps, "*Triggers.kld")
            .SelectMany(file => KwdTriggers.Parse(KwdReader.ReadFile(file))).ToArray();

        TriggerRecord generic47 = triggers.First(t => t.Tag == 213 && t.Type == 47);
        Assert.Contains("PlayerId", generic47.Parameters.Keys);
        Assert.DoesNotContain("CreatureId", generic47.Parameters.Keys);
        Assert.DoesNotContain("Value", generic47.Parameters.Keys);

        TriggerRecord generic52 = triggers.First(t => t.Tag == 213 && t.Type == 52);
        Assert.Contains("Level", generic52.Parameters.Keys);
        Assert.DoesNotContain("PlayerId", generic52.Parameters.Keys);

        TriggerRecord generic64 = triggers.First(t => t.Tag == 213 && t.Type == 64);
        Assert.Contains("PartyId", generic64.Parameters.Keys);
        Assert.DoesNotContain("PlayerId", generic64.Parameters.Keys);

        TriggerRecord action4 = triggers.Single(t => t.Tag == 214 && t.Type == 4);
        Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(action4.Body.Span),
            action4.Parameters["ObjectiveId"]);

        TriggerRecord action8 = triggers.First(t => t.Tag == 214 && t.Type == 8);
        Assert.DoesNotContain("Value", action8.Parameters.Keys);

        TriggerRecord action26 = triggers.First(t => t.Tag == 214 && t.Type == 26);
        Assert.Contains("ActionPointId", action26.Parameters.Keys);
        Assert.DoesNotContain("TargetId", action26.Parameters.Keys);
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

    private static TriggerRecord Trigger(int tag, byte type, byte[] body)
    {
        byte[] tagged = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(tagged, tag);
        BinaryPrimitives.WriteInt32LittleEndian(tagged.AsSpan(4), 16);
        body.CopyTo(tagged, 8);
        tagged[22] = type;
        return KwdTriggers.Parse([Chunk(KwdRecordType.Triggers, tagged)]).Single();
    }

    private static Dictionary<byte, int> ParseCounts(string specification) =>
        specification.Split(',').Select(pair => pair.Split(':')).ToDictionary(
            pair => byte.Parse(pair[0]), pair => int.Parse(pair[1]));
}
