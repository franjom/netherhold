// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using DK2.Art.Textures;

namespace DK2.Convert.Textures;

internal static class PngWriter
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] Write(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int rowBytes = checked(image.Width * 4);
        if (image.Width <= 0 || image.Height <= 0 || image.Pixels.Length != checked(rowBytes * image.Height))
            throw new ArgumentException("RGBA image dimensions do not match its pixels.", nameof(image));

        using var output = new MemoryStream();
        output.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)image.Height);
        header[8] = 8; header[9] = 6; // 8-bit RGBA
        WriteChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            for (int y = 0; y < image.Height; y++)
            {
                zlib.WriteByte(0); // PNG filter: None
                zlib.Write(image.Pixels, y * rowBytes, rowBytes);
            }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        uint crc = Crc32(typeBytes, data);
        BinaryPrimitives.WriteUInt32BigEndian(length, crc);
        output.Write(length);
    }

    private static uint Crc32(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        uint crc = uint.MaxValue;
        Update(first); Update(second);
        return ~crc;
        void Update(ReadOnlySpan<byte> bytes)
        {
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
            }
        }
    }
}

/// <summary>PNG encoding for a decoded texture, kept beside the writer so the reader assembly
/// stays free of it.</summary>
public static class RgbaImagePng
{
    public static byte[] ToPng(this RgbaImage image) => PngWriter.Write(image);
}
