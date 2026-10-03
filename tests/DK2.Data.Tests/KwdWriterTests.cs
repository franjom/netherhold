// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdWriterTests
{
    [Fact]
    public void ParsedHeadersAndUndeclaredLevelTailSurviveWriting()
    {
        byte[] header = Enumerable.Range(0, 28).Select(i => (byte)(i + 31)).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(header, 1);
        byte[] first = Image(KwdRecordType.Variables, header, [1, 2, 3, 4], 2);
        byte[] second = Image(KwdRecordType.Level, header, [8, 7, 6, 5, 4], 3, 2);
        byte[] file = first.Concat(second).ToArray();
        List<KwdChunk> chunks = KwdReader.Read(new MemoryStream(file));
        Assert.Equal(file, KwdWriter.ToBytes(chunks));
        Assert.Equal(5, chunks[1].Content.Length);
    }

    [Fact]
    public void SameSizePayloadEditChangesOnlyThatByte()
    {
        byte[] file = Image(KwdRecordType.Variables, new byte[28], [2, 4, 8, 16], 2);
        List<KwdChunk> chunks = KwdReader.Read(new MemoryStream(file));
        chunks[0].Content[2] = 99;
        byte[] expected = (byte[])file.Clone();
        expected[^2] = 99;
        Assert.Equal(expected, KwdWriter.ToBytes(chunks));
    }

    [Fact]
    public void NewChunkRequiresExplicitHeaderTemplateAndCopiesItsUnknownBytes()
    {
        byte[] header = Enumerable.Range(0, 28).Select(i => (byte)i).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(header, 1);
        KwdChunk template = KwdReader.Read(new MemoryStream(
            Image(KwdRecordType.Variables, header, new byte[16], 2))).Single();
        KwdChunk created = KwdWriter.CreateFromTemplate(template, new byte[32], 2);
        byte[] bytes = KwdWriter.ToBytes([created]);
        KwdChunk reread = KwdReader.Read(new MemoryStream(bytes)).Single();
        Assert.Equal(2, reread.ItemCount);
        Assert.Equal(header.AsSpan(4).ToArray(), reread.HeaderBlock.AsSpan(4).ToArray());
        Assert.Equal(bytes.Length, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(32, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(52)));
        Assert.Equal(header, template.HeaderBlock);
    }

    [Fact]
    public void ChunkWithoutParsedHeaderOrExplicitTemplateIsRejected()
    {
        var chunk = new KwdChunk { Type = KwdRecordType.Variables, ItemCount = 0,
            HeaderBlock = new byte[28], Content = [] };
        Assert.Throws<InvalidDataException>(() => KwdWriter.ToBytes([chunk]));
    }

    [InstallFact]
    public void EveryShippedFileRewritesWithEveryHeaderByteIdentical()
    {
        string editor = Path.Combine(Dk2InstallGate.Path, "Data", "editor");
        string maps = Path.Combine(editor, "maps");
        string[] files = Directory.GetFiles(editor, "*.kwd")
            .Concat(Directory.GetFiles(maps, "*.kwd"))
            .Concat(Directory.GetFiles(maps, "*.kld")).ToArray();
        Assert.Equal(408, files.Length);
        int chunks = 0;
        foreach (string file in files)
        {
            List<KwdChunk> parsed = KwdReader.ReadFile(file);
            chunks += parsed.Count;
            Assert.True(File.ReadAllBytes(file).SequenceEqual(KwdWriter.ToBytes(parsed)), file);
        }
        Assert.Equal(409, chunks);
    }

    private static byte[] Image(KwdRecordType type, byte[] header, byte[] content,
        uint checkDelta, uint? declaredSize = null)
    {
        byte[] result = new byte[28 + header.Length + content.Length];
        uint[] words = [(uint)type, 4, (uint)result.Length, (uint)type + 1, (uint)header.Length];
        for (int i = 0; i < words.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i * 4), words[i]);
        header.CopyTo(result, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(20 + header.Length), (uint)type + checkDelta);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24 + header.Length), declaredSize ?? (uint)content.Length);
        content.CopyTo(result, 28 + header.Length);
        return result;
    }
}
