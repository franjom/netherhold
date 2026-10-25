// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;

namespace DK2.Art.Textures;

/// <summary>Uncompressed 24-bit BMP thumbnails, returned as top-down RGBA rows.</summary>
public static class BmpImageReader
{
    public static RgbaImage Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 54 || data[0] != 'B' || data[1] != 'M')
            throw new InvalidDataException("Not a BMP image.");
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        if (headerSize < 40 || headerSize > data.Length - 14 || offset < 14L + headerSize
            || width <= 0 || signedHeight == 0 || signedHeight == int.MinValue
            || BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) != 24
            || BinaryPrimitives.ReadUInt32LittleEndian(data[30..]) != 0)
            throw new InvalidDataException("Expected an uncompressed 24-bit BMP.");
        int height = Math.Abs(signedHeight);
        long stride = ((long)width * 3 + 3) & ~3L;
        if ((long)width * height > 16_777_216 || offset + stride * height > data.Length)
            throw new InvalidDataException("Invalid BMP dimensions or truncated pixels.");
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            int sourceY = signedHeight > 0 ? height - 1 - y : y;
            ReadOnlySpan<byte> source = data.Slice(checked((int)(offset + stride * sourceY)), width * 3);
            for (int x = 0; x < width; x++)
            {
                int p = (y * width + x) * 4;
                pixels[p] = source[x * 3 + 2]; pixels[p + 1] = source[x * 3 + 1];
                pixels[p + 2] = source[x * 3]; pixels[p + 3] = 255;
            }
        }
        return new RgbaImage(width, height, pixels);
    }
}
