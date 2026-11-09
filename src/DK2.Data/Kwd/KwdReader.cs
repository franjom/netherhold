// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>Chunk-level reader for KWD/KLD files. Layout STRONGLY INFERRED against the
/// reference install (docs/research/kwd-container.md); chunks repeat to end of file.
/// <code>
/// u32 signature (KwdRecordType); u32 byteSize (== 4); u32 chunkEndOffset;
/// u32 checkOne (== signature + 1); u32 headerBlockSize;
/// u8[headerBlockSize] type-specific header:
///     general:  u32 itemCount, u32 unk, timestamp[10] created, timestamp[10] modified
///     TRIGGERS: u32 itemCount0, u32 itemCount1, ... (count = count0 + count1)
///     MAP: u32 width, u32 height;  LEVEL: u16 itemCount, u16 height, ...
/// u32 checkTwo (== signature + 2 or + 3); u32 contentSize; u8[contentSize] records
/// </code>
/// </summary>
public static class KwdReader
{
    public static List<KwdChunk> ReadFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs, path);
    }

    /// <summary>Reads every chunk. Any malformed or truncated input throws
    /// <see cref="InvalidDataException"/> (ADR-0724 R5); sizes are checked against the stream before
    /// anything is allocated from them. Needs a seekable stream.</summary>
    public static List<KwdChunk> Read(Stream stream, string sourceName = "<stream>")
    {
        try
        {
            return ReadChunks(stream, sourceName);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException($"{sourceName}: the file ends inside a chunk.", e);
        }
    }

    private static List<KwdChunk> ReadChunks(Stream stream, string sourceName)
    {
        var chunks = new List<KwdChunk>();
        byte[] dw = new byte[4];

        uint U32()
        {
            stream.ReadExactly(dw);
            return BinaryPrimitives.ReadUInt32LittleEndian(dw);
        }

        while (stream.Position < stream.Length)
        {
            uint signature = U32();
            uint byteSize = U32();
            uint chunkEndOffset = U32();
            uint checkOne = U32();
            uint headerBlockSize = U32();

            if (byteSize != 4)
                throw new InvalidDataException($"{sourceName}: unexpected byteSize {byteSize}.");
            if (checkOne != signature + 1)
                throw new InvalidDataException(
                    $"{sourceName}: header check failed (sig={signature}, checkOne={checkOne}).");

            byte[] headerBlock = new byte[Size(stream, headerBlockSize, sourceName)];
            stream.ReadExactly(headerBlock);

            uint checkTwo = U32();
            if (checkTwo != signature + 2 && checkTwo != signature + 3)
                throw new InvalidDataException(
                    $"{sourceName}: trailer check failed (sig={signature}, checkTwo={checkTwo}).");

            uint contentSize = U32();
            byte[] content = new byte[Size(stream, contentSize, sourceName)];
            stream.ReadExactly(content);

            // LEVEL chunks (and some legacy variants) understate contentSize;
            // the remainder of the file belongs to the same chunk. Only continue
            // the chunk loop if what follows actually looks like a chunk header.
            if (stream.Position < stream.Length && !NextLooksLikeChunkHeader(stream))
            {
                byte[] trailing = new byte[stream.Length - stream.Position];
                stream.ReadExactly(trailing);
                byte[] combined = new byte[content.Length + trailing.Length];
                content.CopyTo(combined, 0);
                trailing.CopyTo(combined, content.Length);
                content = combined;
            }

            var type = (KwdRecordType)signature;
            int countBytes = type switch
            {
                KwdRecordType.Map => 0,
                KwdRecordType.Level => 2,
                KwdRecordType.Triggers => 8,
                _ => 4,
            };
            if (headerBlock.Length < countBytes)
                throw new InvalidDataException(
                    $"{sourceName}: {type} header block is {headerBlock.Length} bytes, too short for its count.");
            long itemCount = type switch
            {
                KwdRecordType.Map => 0, // MAP is width*height tiles, not itemCount records
                KwdRecordType.Level => BinaryPrimitives.ReadUInt16LittleEndian(headerBlock),
                KwdRecordType.Triggers =>
                    (long)BinaryPrimitives.ReadUInt32LittleEndian(headerBlock)
                        + BinaryPrimitives.ReadUInt32LittleEndian(headerBlock.AsSpan(4)),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(headerBlock),
            };
            // Every record takes at least one content byte, so a larger count is corrupt; refusing
            // it here keeps every parser's count bounded by the bytes actually read (ADR-0724 R5).
            if (itemCount > content.Length)
                throw new InvalidDataException(
                    $"{sourceName}: {type} declares {itemCount} records in {content.Length} content bytes.");

            chunks.Add(new KwdChunk
            {
                Type = type,
                ItemCount = (int)itemCount,
                Content = content,
                HeaderBlock = headerBlock,
                ContainerHeader = new KwdContainerHeader(chunkEndOffset, checkTwo,
                    contentSize, content.Length),
            });
        }

        return chunks;
    }

    /// <summary>A declared size, refused when it runs past the end of the stream.</summary>
    private static int Size(Stream stream, uint declared, string sourceName)
    {
        if (declared > stream.Length - stream.Position)
            throw new InvalidDataException(
                $"{sourceName}: a declared size of {declared} bytes runs past the end of the file.");
        return (int)declared;
    }

    /// <summary>Peeks whether the stream is positioned at a plausible chunk header.</summary>
    private static bool NextLooksLikeChunkHeader(Stream stream)
    {
        long pos = stream.Position;
        if (stream.Length - pos < 28) return false;

        Span<byte> peek = stackalloc byte[16];
        stream.ReadExactly(peek);
        stream.Position = pos;

        uint signature = BinaryPrimitives.ReadUInt32LittleEndian(peek);
        uint byteSize = BinaryPrimitives.ReadUInt32LittleEndian(peek[4..]);
        uint checkOne = BinaryPrimitives.ReadUInt32LittleEndian(peek[12..]);

        return byteSize == 4
            && checkOne == signature + 1
            && Enum.IsDefined(typeof(KwdRecordType), (KwdRecordType)signature);
    }
}
