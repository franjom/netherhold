// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;

namespace DK2.Art.Textures;

/// <summary>The .444 header followed by the engine texture codec; output is top-down RGBA.</summary>
public static class LoadingScreenImageReader
{
    public static RgbaImage Read(ReadOnlySpan<byte> data)
    {
        if (data.Length <= 8) throw new InvalidDataException("Truncated .444 image.");
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        byte flags = data[4];
        if (width == 0 || height == 0 || (long)width * height > 16_777_216)
            throw new InvalidDataException("Invalid .444 image dimensions.");
        if ((flags & 1) == 0)
            throw new NotSupportedException("Masked .444 images are not supported; shipped front-end images are unmasked.");
        int paddedWidth = (width + 7) & ~7, paddedHeight = (height + 7) & ~7;
        byte[] padded = Dk2TextureDecoder.DecodeLoadingScreen(data[8..], paddedWidth, paddedHeight, (flags & 0x80) != 0);
        if (width == paddedWidth && height == paddedHeight)
            return new RgbaImage(width, height, padded);
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(padded, y * paddedWidth * 4, pixels, y * width * 4, width * 4);
        return new RgbaImage(width, height, pixels);
    }
}
