// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// Chunk-level reader for KWD/KLD files.
///
/// Layout (CONFIRMED against the reference install; docs/research/kwd-container.md):
///   u32 signature (KwdRecordType)     u32 byteSize (== 4)
///   u32 chunkEndOffset                u32 checkOne (== signature + 1)
///   u32 headerBlockSize
///   u8[headerBlockSize] type-specific header:
///       general:  u32 itemCount, u32 unk, timestamp[10] created, timestamp[10] modified
///       TRIGGERS: u32 itemCount0, u32 itemCount1, ... (count = count0 + count1)
///       MAP:      u32 width, u32 height
///       LEVEL:    u16 itemCount, u16 height, ...
///   u32 checkTwo (== signature + 2 or + 3)
///   u32 contentSize
///   u8[contentSize] records
/// Chunks repeat until end of file.
/// </summary>
public static class KwdReader
{
    public static List<KwdChunk> ReadFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs, path);
    }

    public static List<KwdChunk> Read(Stream stream, string sourceName = "<stream>")
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
            _ = U32(); // chunk end offset (unreliable across variants; not needed)
            uint checkOne = U32();
            uint headerBlockSize = U32();

            if (byteSize != 4)
                throw new InvalidDataException($"{sourceName}: unexpected byteSize {byteSize}.");
            if (checkOne != signature + 1)
                throw new InvalidDataException(
                    $"{sourceName}: header check failed (sig={signature}, checkOne={checkOne}).");

            byte[] headerBlock = new byte[headerBlockSize];
            stream.ReadExactly(headerBlock);

            uint checkTwo = U32();
            if (checkTwo != signature + 2 && checkTwo != signature + 3)
                throw new InvalidDataException(
                    $"{sourceName}: trailer check failed (sig={signature}, checkTwo={checkTwo}).");

            uint contentSize = U32();
            byte[] content = new byte[contentSize];
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
            int itemCount = type switch
            {
                KwdRecordType.Map => 0, // MAP is width*height tiles, not itemCount records
                KwdRecordType.Level => BinaryPrimitives.ReadUInt16LittleEndian(headerBlock),
                KwdRecordType.Triggers =>
                    (int)(BinaryPrimitives.ReadUInt32LittleEndian(headerBlock)
                        + BinaryPrimitives.ReadUInt32LittleEndian(headerBlock.AsSpan(4))),
                _ => (int)BinaryPrimitives.ReadUInt32LittleEndian(headerBlock),
            };

            chunks.Add(new KwdChunk
            {
                Type = type,
                ItemCount = itemCount,
                Content = content,
                HeaderBlock = headerBlock,
            });
        }

        return chunks;
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
