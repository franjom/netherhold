// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

public static class KwdWriter
{
    public static byte[] ToBytes(IEnumerable<KwdChunk> chunks)
    {
        using var stream = new MemoryStream();
        Write(stream, chunks);
        return stream.ToArray();
    }

    public static void WriteFile(string path, IEnumerable<KwdChunk> chunks)
    {
        byte[] bytes = ToBytes(chunks);
        File.WriteAllBytes(path, bytes);
    }

    public static void Write(Stream stream, IEnumerable<KwdChunk> chunks)
    {
        Span<byte> word = stackalloc byte[4];
        foreach (KwdChunk chunk in chunks)
        {
            KwdContainerHeader header = chunk.ContainerHeader ?? throw new InvalidDataException(
                $"{chunk.Type}: writing requires a parsed header or an explicit header template.");
            long delta = (long)chunk.Content.Length - header.SourceContentLength;
            uint end = checked((uint)(header.ChunkEndOffset + delta));
            uint size = checked((uint)(header.DeclaredContentSize + delta));
            uint signature = (uint)chunk.Type;
            if (header.CheckTwo != signature + 2 && header.CheckTwo != signature + 3)
                throw new InvalidDataException($"{chunk.Type}: header template has an incompatible checkTwo.");
            U32(stream, word, signature);
            U32(stream, word, 4);
            U32(stream, word, end);
            U32(stream, word, signature + 1);
            U32(stream, word, checked((uint)chunk.HeaderBlock.Length));
            stream.Write(chunk.HeaderBlock, 0, chunk.HeaderBlock.Length);
            U32(stream, word, header.CheckTwo);
            U32(stream, word, size);
            stream.Write(chunk.Content, 0, chunk.Content.Length);
        }
    }

    public static KwdChunk CreateFromTemplate(KwdChunk template, byte[] content, int itemCount)
    {
        KwdContainerHeader original = template.ContainerHeader ?? throw new InvalidDataException(
            "The explicit template must have a parsed container header.");
        if (itemCount < 0) throw new ArgumentOutOfRangeException(nameof(itemCount));
        byte[] header = (byte[])template.HeaderBlock.Clone();
        switch (template.Type)
        {
            case KwdRecordType.Map:
                if (itemCount != 0) throw new InvalidDataException("MAP has no record count.");
                break;
            case KwdRecordType.Level:
                BinaryPrimitives.WriteUInt16LittleEndian(header, checked((ushort)itemCount));
                break;
            case KwdRecordType.Triggers:
                throw new InvalidDataException("TRIGGERS requires separate generic/action counts; use its editing surface.");
            default:
                BinaryPrimitives.WriteInt32LittleEndian(header, itemCount);
                break;
        }
        return template with
        {
            HeaderBlock = header, Content = (byte[])content.Clone(), ItemCount = itemCount,
            ContainerHeader = new KwdContainerHeader(
                checked((uint)(28 + header.Length + content.Length)), original.CheckTwo,
                checked((uint)content.Length), content.Length),
        };
    }

    private static void U32(Stream stream, Span<byte> word, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(word, value);
        stream.Write(word);
    }
}
