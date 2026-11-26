// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The colour-space chunk filter Godot decodes DK2's PNGs through (stored bytes, as Unity reads them).</summary>
public sealed class PngChunksTests
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static byte[] Chunk(string type, byte[] data)
    {
        byte[] chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type, 0, 4, chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>A 2x1 RGB image, laid out as MapColours.png is: IHDR, gAMA (0.22727), cHRM, IDAT, IEND.</summary>
    private static (byte[] Png, byte[] Ihdr, byte[] Idat, byte[] Iend) Strip(bool colourSpace = true)
    {
        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 2);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8; header[9] = 2;
        var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true)) z.Write([0, 231, 24, 24, 1, 2, 3]);
        byte[] ihdr = Chunk("IHDR", header), idat = Chunk("IDAT", raw.ToArray()), iend = Chunk("IEND", []);
        var png = new List<byte>(Signature);
        png.AddRange(ihdr);
        if (colourSpace)
        {
            png.AddRange(Chunk("gAMA", [0, 0, 0x58, 0xC7]));
            png.AddRange(Chunk("cHRM", new byte[32]));
        }
        png.AddRange(idat); png.AddRange(iend);
        return (png.ToArray(), ihdr, idat, iend);
    }

    private static List<string> Types(byte[] png)
    {
        var types = new List<string>();
        for (int at = 8; at < png.Length; at += 12 + (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at)))
            types.Add(Encoding.ASCII.GetString(png, at + 4, 4));
        return types;
    }

    [Fact]
    public void TheColourSpaceChunksGoAndTheOthersStayByteForByte()
    {
        (byte[] png, byte[] ihdr, byte[] idat, byte[] iend) = Strip();
        byte[] kept = PngChunks.WithoutColourSpace(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], Types(kept));
        Assert.Equal([.. Signature, .. ihdr, .. idat, .. iend], kept);   // CRCs included: the pixels are untouched
    }

    [Fact]
    public void SrgbAndIccpGoToo()
    {
        (byte[] png, byte[] ihdr, byte[] idat, byte[] iend) = Strip(colourSpace: false);
        byte[] tagged = [.. Signature, .. ihdr, .. Chunk("sRGB", [0]), .. Chunk("iCCP", [(byte)'p', 0, 0, 1]), .. idat, .. iend];
        Assert.Equal([.. Signature, .. ihdr, .. idat, .. iend], PngChunks.WithoutColourSpace(tagged));
    }

    [Fact]
    public void APngWithoutThemComesBackAsItWas()
    {
        byte[] png = Strip(colourSpace: false).Png;
        Assert.Same(png, PngChunks.WithoutColourSpace(png));
    }

    [Fact]
    public void ChunksAfterIendAreDropped()
    {
        byte[] png = [.. Strip().Png, .. Chunk("tEXt", [1])];
        Assert.Equal(["IHDR", "IDAT", "IEND"], Types(PngChunks.WithoutColourSpace(png)));
    }

    [Fact]
    public void AMissingSignatureALengthOverflowAShortChunkOrNoIendIsRefused()
    {
        byte[] png = Strip().Png;
        Assert.Throws<InvalidDataException>(() => PngChunks.WithoutColourSpace(png[1..]));
        Assert.Throws<InvalidDataException>(() => PngChunks.WithoutColourSpace([]));
        byte[] huge = (byte[])png.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(8), 0xFFFFFFF0);   // IHDR's length past int.MaxValue
        Assert.Throws<InvalidDataException>(() => PngChunks.WithoutColourSpace(huge));
        byte[] past = (byte[])png.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(past.AsSpan(8), 0x7FFFFFFF);   // fits an int, runs past the end
        Assert.Throws<InvalidDataException>(() => PngChunks.WithoutColourSpace(past));
        Assert.Throws<InvalidDataException>(() => PngChunks.WithoutColourSpace(png[..^12]));
    }

    [InstallFact]
    public void TheShippedPaletteStripLosesOnlyItsGammaAndChromaticity()
    {
        byte[] png = InstallMinimapAssets.PalettePngOf(Dk2InstallGate.Path);
        Assert.Equal(["IHDR", "gAMA", "cHRM", "IDAT", "IEND"], Types(png));
        byte[] kept = PngChunks.WithoutColourSpace(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], Types(kept));
        Assert.Equal(png.Length - (12 + 4) - (12 + 32), kept.Length);
    }
}
