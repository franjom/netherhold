// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Art.Textures;
using DK2.Data.Wad;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public class FrontEndImageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadingScreenReadsAbsoluteRgbDcAndCropsThePaddedBlock(bool alpha)
    {
        string bits = "00011001" + "10" + "01100100" + "10" + "11001000" + "10";
        if (alpha) bits += "10000000" + "10";
        byte[] encoded = LoadingImage(bits, width: 5, height: 3, alpha);
        RgbaImage image = LoadingScreenImageReader.Read(encoded);
        Assert.Equal(5, image.Width); Assert.Equal(3, image.Height);
        Assert.Equal(Enumerable.Range(0, 15).SelectMany(_ => new byte[] { 25, 100, 200, alpha ? (byte)128 : (byte)255 }), image.Pixels);
    }

    [Theory]
    [InlineData("110", "0000010000000000000000000001")]
    [InlineData("111", "0000010000001000000011111111")]
    public void ExtendedEscapeCoefficientsMatchTheSameShortVlc(string shortAc, string extendedAc)
    {
        const string dc = "00100000";
        byte[] shortForm = LoadingImage(dc + shortAc + "10" + dc + "10" + dc + "10", 8, 8, false);
        byte[] escapeForm = LoadingImage(dc + extendedAc + "10" + dc + "10" + dc + "10", 8, 8, false);
        Assert.Equal(LoadingScreenImageReader.Read(shortForm).Pixels, LoadingScreenImageReader.Read(escapeForm).Pixels);
    }

    [Fact]
    public void FifthAcCoefficientIsNotDiscardedAsAConstantBlock()
    {
        const string dc = "01100100";
        byte[] encoded = LoadingImage(dc + "000001" + "010011" + "00010000" + "10" + dc + "10" + dc + "10", 8, 8, false);
        RgbaImage image = LoadingScreenImageReader.Read(encoded);
        Assert.True(image.Pixels.Where((_, index) => index % 4 == 0).Distinct().Count() > 1);
        Assert.All(image.Pixels.Where((_, index) => index % 4 == 1), green => Assert.Equal((byte)100, green));
    }

    [Fact]
    public void HeaderReplacementColorDoesNotEnableAlphaAndMaskedVariantIsRefused()
    {
        byte[] encoded = LoadingImage("000110011001100100101100100010", 8, 8, false);
        encoded[5] = 255; encoded[6] = 123; encoded[7] = 42;
        RgbaImage image = LoadingScreenImageReader.Read(encoded);
        Assert.All(image.Pixels.Where((_, index) => index % 4 == 3), alpha => Assert.Equal((byte)255, alpha));
        encoded[4] &= 0xfe;
        Assert.Throws<NotSupportedException>(() => LoadingScreenImageReader.Read(encoded));
    }

    private static byte[] LoadingImage(string bits, ushort width, ushort height, bool alpha)
    {
        bits = bits.PadRight(((bits.Length + 31) / 32 + 1) * 32, '0');
        byte[] encoded = new byte[8 + bits.Length / 8];
        BinaryPrimitives.WriteUInt16LittleEndian(encoded, width);
        BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(2), height);
        encoded[4] = alpha ? (byte)0xe5 : (byte)0x65;
        for (int word = 0; word < bits.Length / 32; word++)
            BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(8 + word * 4), System.Convert.ToUInt32(bits.Substring(word * 32, 32), 2));
        return encoded;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    public void BmpReadsBgrAndPaddedRowsInTopDownOrder(int height)
    {
        byte[] bmp = new byte[62];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(28), 24);
        new byte[] { 1, 2, 3, 99, 4, 5, 6, 99 }.CopyTo(bmp, 54);
        RgbaImage image = BmpImageReader.Read(bmp);
        Assert.Equal(1, image.Width); Assert.Equal(2, image.Height);
        Assert.Equal(height > 0
            ? new byte[] { 6, 5, 4, 255, 3, 2, 1, 255 }
            : new byte[] { 3, 2, 1, 255, 6, 5, 4, 255 }, image.Pixels);
        Assert.Throws<InvalidDataException>(() => BmpImageReader.Read(bmp.AsSpan(0, 61)));
        bmp[30] = 1;
        Assert.Throws<InvalidDataException>(() => BmpImageReader.Read(bmp));
    }

    [Fact]
    public void LoadingScreenWrapperRejectsMissingPayloadAndZeroDimensions()
    {
        Assert.Throws<InvalidDataException>(() => LoadingScreenImageReader.Read(new byte[7]));
        Assert.Throws<InvalidDataException>(() => LoadingScreenImageReader.Read(new byte[16]));
        byte[] image = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(image, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(2), 8);
        Assert.Throws<InvalidDataException>(() => LoadingScreenImageReader.Read(image));
    }

    [InstallFact]
    public void EveryShippedBmpAndLoadingScreenDecodesToItsHeaderSize()
    {
        using var wad = WadArchive.Open(Path.Combine(Dk2InstallGate.Path, "Data", "FrontEnd.WAD"));
        using var images = new WadImageSource(wad, _ => throw new Exception("PNG decoder called for another format"));
        int bmpCount = 0, loadingCount = 0;
        for (int i = 0; i < wad.Entries.Count; i++)
        {
            string name = wad.Entries[i].Name;
            bool bmp = name.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase);
            if (!bmp && !name.EndsWith(".444", StringComparison.OrdinalIgnoreCase)) continue;
            byte[] data = wad.Extract(i);
            RgbaImage image = images.Read(i);
            int width = bmp ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(18))
                : BinaryPrimitives.ReadUInt16LittleEndian(data);
            int height = bmp ? Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(22)))
                : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
            Assert.Equal(width, image.Width); Assert.Equal(height, image.Height);
            Assert.Equal(width * height * 4, image.Pixels.Length);
            Assert.True(image.Pixels.Distinct().Count() > 16, name);
            if (bmp) bmpCount++; else loadingCount++;
        }
        Assert.Equal(42, bmpCount); Assert.Equal(29, loadingCount);
    }

    [InstallFact]
    public void PngBytesReachTheInjectedRuntimeDecoder()
    {
        using var wad = WadArchive.Open(Path.Combine(Dk2InstallGate.Path, "Data", "FrontEnd.WAD"));
        byte[]? input = null;
        using var images = new WadImageSource(wad, bytes => { input = bytes; return new RgbaImage(1, 1, [1, 2, 3, 4]); });
        RgbaImage result = images.Read("Titlescreen.png");
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, input![..8]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result.Pixels);
        Assert.Throws<NotSupportedException>(() => images.Read("Titlescreen.psd"));
        Assert.Throws<FileNotFoundException>(() => images.Read("missing.png"));
    }
}
