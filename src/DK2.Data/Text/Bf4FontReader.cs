// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Text;

/// <summary>
/// Reads a DK2 <c>.bf4</c> bitmap font (resource type 4), as the loader <c>0x006336D0</c>,
/// the font methods <c>0x006335B0</c>-<c>0x006336A0</c> and the blitter <c>0x006337A0</c> do.
/// The byte layout is in <c>src/DK2.Data/README.md</c> §BF4. A record holds its character
/// (+0), stored size (+4), flags (+0xC), width and height (+0x10), x and y offsets (+0x14),
/// advance (+0x16) and pixels (+0x18).
/// </summary>
public static class Bf4FontReader
{
    private const uint Magic = 0x42463446; // "F4FB"

    public static BitmapFont Open(string path) => Read(File.ReadAllBytes(path), path);

    public static BitmapFont Read(ReadOnlySpan<byte> data, string sourceName = "<memory>")
    {
        if (data.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            throw Invalid(sourceName, "expected the F4FB magic");
        int lineHeight = data[5];
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        if (data.Length < 8 + 4 * count) throw Invalid(sourceName, "offset table is truncated");

        var glyphs = new BitmapGlyph[count];
        for (int i = 0; i < count; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data[(8 + 4 * i)..]);
            if (offset > (uint)data.Length - 0x18 || data.Length < 0x18) throw Invalid(sourceName, $"glyph {i} lies outside the file");
            ReadOnlySpan<byte> record = data[(int)offset..];
            uint stored = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(record[0x0C..]);
            int width = BinaryPrimitives.ReadUInt16LittleEndian(record[0x10..]);
            int height = BinaryPrimitives.ReadUInt16LittleEndian(record[0x12..]);
            if ((long)width * height > 1 << 20) throw Invalid(sourceName, $"glyph {i} is implausibly large");
            if (stored > (uint)(record.Length - 0x18)) throw Invalid(sourceName, $"glyph {i}'s data runs past the end");
            glyphs[i] = new BitmapGlyph(
                (char)BinaryPrimitives.ReadUInt16LittleEndian(record),
                width, height,
                OffsetX: (sbyte)record[0x14], OffsetY: (sbyte)record[0x15],
                Advance: BinaryPrimitives.ReadUInt16LittleEndian(record[0x16..]),
                Pixels: Unpack(record.Slice(0x18, (int)stored).ToArray(), flags, width * height, sourceName, i));
        }
        return new BitmapFont(lineHeight, glyphs);
    }

    private static byte[] Unpack(byte[] source, uint flags, int pixels, string sourceName, int glyph)
    {
        var output = new byte[pixels];
        int written = 0;
        bool padded = false;
        if ((flags & 1) != 0)
        {
            // 0x006337A0's RLE: a non-zero nibble is one pixel, a zero nibble is followed by a count
            // nibble (0 ends the glyph) and a value nibble repeated that many times. The stream
            // unpacks to the glyph's whole bytes, 2 * ceil(w * h / 2) nibbles. 177 of the 2,250
            // shipped RLE glyphs stop one nibble short: a byte's low nibble, which DK2's writer
            // 0x00634040 zeroed when it stored the high one, so the pixel is clear. An odd glyph's
            // last, padding nibble is dropped.
            int nibble = 0;
            int Next()
            {
                if (nibble >> 1 >= source.Length) throw Invalid(sourceName, $"glyph {glyph}'s RLE runs past its data");
                int value = (nibble & 1) == 0 ? source[nibble >> 1] >> 4 : source[nibble >> 1] & 0xF;
                nibble++;
                return value;
            }
            while (true)
            {
                int value = Next();
                if (value != 0)
                {
                    Put(value);
                    continue;
                }
                int run = Next();
                if (run == 0) break;
                int repeated = Next();
                for (; run > 0; run--) Put(repeated);
            }
        }
        else if ((flags & 2) != 0)
        {
            // One bit a pixel, high bit first; a set bit is a full pixel.
            for (int i = 0; i < pixels && (i >> 3) < source.Length; i++)
                Put((source[i >> 3] & (0x80 >> (i & 7))) != 0 ? 15 : 0);
        }
        else
        {
            for (int i = 0; i < pixels && (i >> 1) < source.Length; i++)
                Put((i & 1) == 0 ? source[i >> 1] >> 4 : source[i >> 1] & 0xF);
        }
        if ((flags & 1) != 0 && written == pixels - 1) written = pixels;
        if (written != pixels) throw Invalid(sourceName, $"glyph {glyph} unpacked to {written} of {pixels} pixels");
        return output;

        void Put(int value)
        {
            if (written == output.Length && (pixels & 1) == 1 && !padded)
            {
                padded = true;
                return;
            }
            if (written >= output.Length) throw Invalid(sourceName, $"glyph {glyph} unpacks past its size");
            output[written++] = (byte)value;
        }
    }

    private static InvalidDataException Invalid(string source, string reason) =>
        new($"{source}: not a valid BF4 font: {reason}.");
}
