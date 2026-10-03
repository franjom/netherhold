// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdVariablesWriterTests
{
    [Theory]
    [InlineData(1, "CreatureId:4:4 Value:8:4 PlayerId:C:4")]
    [InlineData(2, "Kind:4:2 PlayerId:6:2 TypeId:8:4 Value:C:4")]
    [InlineData(17, "Kind:4:4 TypeId:8:4 Cost:C:4")]
    [InlineData(43, "Type1:4:1 Id1:5:1 Type2:6:1 Id2:7:1 Type3:8:1 Id3:9:1 RewardType:A:1 SpeechId:B:1 RewardValue:C:4")]
    [InlineData(65, "StatId:4:4 Value:8:4 Level:C:4 FirstPerson:0:4")]
    [InlineData(224, "StatId:4:4 Value:8:4 Level:C:4 FirstPerson:0:4")]
    [InlineData(999, "Id:0:4 Value:4:4 Unk1:8:4 Unk2:C:4")]
    public void EveryFieldInEveryIdShapeRoundTripsAndChangesOnlyItsBytes(int id, string fieldList)
    {
        byte[] source = Cell(id);
        var image = new KwdRecordImage(KwdRecordType.Variables, source);
        var fields = fieldList.Split(' ').Select(f => f.Split(':')).ToDictionary(f => f[0],
            f => (Offset: Convert.ToInt32(f[1], 16), Width: int.Parse(f[2])));
        KwdVariableEntry original = KwdVariables.ParseRecord(source);
        var properties = original.GetType().GetProperties().Where(p => p.SetMethod is not null).ToArray();
        Assert.Equal(properties.Length, fields.Count);
        Assert.Equal(source, KwdVariables.WriteRecord(original, image));
        foreach (var property in properties)
        {
            KwdVariableEntry changed = KwdVariables.ParseRecord(source);
            object value = property.PropertyType == typeof(int) ? -12345
                : property.PropertyType == typeof(ushort) ? (ushort)1234
                : property.PropertyType == typeof(byte) ? (byte)13
                : property.PropertyType == typeof(bool) ? !(bool)property.GetValue(changed)!
                : property.PropertyType == typeof(VariableTypeId) ? (VariableTypeId)9998
                : throw new InvalidOperationException(property.Name);
            property.SetValue(changed, value);
            byte[] written = KwdVariables.WriteRecord(changed, image);
            Assert.Equal(value, property.GetValue(KwdVariables.ParseRecord(written)));
            Assert.False(source.SequenceEqual(written));
            var field = fields[property.Name];
            for (int i = 0; i < source.Length; i++)
                if (i < field.Offset || i >= field.Offset + field.Width) Assert.Equal(source[i], written[i]);
        }
        Assert.Equal(source, image.ToBytes());
    }

    [Fact]
    public void CellOverlaysPreserveDuplicateIdsAndSourceOrder()
    {
        byte[] first = Cell(3), second = Cell(3);
        second[4]++;
        var chunk = new KwdChunk { Type = KwdRecordType.Variables, ItemCount = 2,
            HeaderBlock = [], Content = first.Concat(second).ToArray() };
        var images = KwdRecordRoundTrip.Split(chunk);
        var rewritten = images.Select(i => new KwdRecordImage(KwdRecordType.Variables,
            KwdVariables.WriteRecord(KwdVariables.ParseRecord(i.ToBytes()), i))).ToArray();
        Assert.Equal(chunk.Content, KwdRecordRoundTrip.Join(chunk, rewritten));
        var changed = (MiscVariable)KwdVariables.ParseRecord(first) with { Value = 7 };
        rewritten[0] = new KwdRecordImage(KwdRecordType.Variables, KwdVariables.WriteRecord(changed, images[0]));
        byte[] content = KwdRecordRoundTrip.Join(chunk, rewritten);
        Assert.Equal(7, ((MiscVariable)KwdVariables.ParseRecord(content.AsSpan(0, 16))).Value);
        Assert.Equal(second, content[16..]);
        Assert.Equal(2, KwdRecordRoundTrip.Split(chunk).Count);
        Assert.Single(KwdVariables.Parse([chunk]).Misc);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(17)] [InlineData(43)] [InlineData(65)] [InlineData(224)]
    public void RefusesAnEntryThatDoesNotMatchTheTemplateShape(int id)
    {
        var image = new KwdRecordImage(KwdRecordType.Variables, Cell(id));
        Assert.Throws<InvalidDataException>(() => KwdVariables.WriteRecord(new MiscVariable((VariableTypeId)3, 1, 2, 3), image));
    }

    [Fact]
    public void RefusesSpecialIdsInPlainCellsAndWrongImages()
    {
        var misc = new MiscVariable(VariableTypeId.Availability, 1, 2, 3);
        Assert.Throws<InvalidDataException>(() => KwdVariables.WriteRecord(misc,
            new KwdRecordImage(KwdRecordType.Variables, Cell(3))));
        Assert.Throws<InvalidDataException>(() => KwdVariables.WriteRecord(misc,
            new KwdRecordImage(KwdRecordType.Objects, Cell(3))));
        Assert.Throws<InvalidDataException>(() => KwdVariables.WriteRecord(misc,
            new KwdRecordImage(KwdRecordType.Variables, new byte[15])));
        Assert.Throws<InvalidDataException>(() => KwdVariables.ParseRecord(new byte[15]));
    }

    [InstallFact]
    public void EveryShippedVariableCellTypedOverlayIsByteIdentical()
    {
        string editor = Path.Combine(Dk2InstallGate.Path, "Data", "editor");
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(editor, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).Equals(".kwd", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(f).Equals(".kld", StringComparison.OrdinalIgnoreCase)))
        foreach (KwdChunk chunk in KwdReader.ReadFile(file).Where(c => c.Type == KwdRecordType.Variables))
        foreach (KwdRecordImage image in KwdRecordRoundTrip.Split(chunk))
        {
            byte[] source = image.ToBytes();
            Assert.Equal(source, KwdVariables.WriteRecord(KwdVariables.ParseRecord(source), image));
            count++;
        }
        Assert.Equal(8246, count);
    }

    private static byte[] Cell(int id)
    {
        byte[] bytes = Enumerable.Repeat((byte)0x5A, 16).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes, id);
        return bytes;
    }
}
