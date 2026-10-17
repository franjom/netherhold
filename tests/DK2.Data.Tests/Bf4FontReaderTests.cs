// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class Bf4FontReaderTests
{
    [Fact]
    public void RawPixelsRunOnAcrossRowsHighNibbleFirst()
    {
        // 3x3 = 9 pixels in 5 bytes, no row padding.
        BitmapGlyph glyph = Single(Glyph('A', 3, 3, flags: 0, 0x12, 0x34, 0x56, 0x78, 0x90));

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, glyph.Pixels);
    }

    [Fact]
    public void RleIsALiteralNibbleOrAZeroCountValueRunAndZeroZeroEnds()
    {
        // 5, then run of 3 x 0xF, then 2, then end (0 0).
        BitmapGlyph glyph = Single(Glyph('B', 5, 1, flags: 1, 0x50, 0x3F, 0x20, 0x00));

        Assert.Equal(new byte[] { 5, 15, 15, 15, 2 }, glyph.Pixels);
    }

    [Fact]
    public void AnRleGlyphOneNibbleShortEndsClearAsDk2sWholeByteStoreLeavesIt()
    {
        // 2x2 even: 1 2 3, then end; the fourth pixel is the low nibble 0x00634040 zeroed.
        BitmapGlyph glyph = Single(Glyph('E', 2, 2, flags: 1, 0x12, 0x30, 0x00));

        Assert.Equal(new byte[] { 1, 2, 3, 0 }, glyph.Pixels);
    }

    [Fact]
    public void AnOddRleGlyphDropsItsPaddingNibble()
    {
        BitmapGlyph glyph = Single(Glyph('F', 3, 1, flags: 1, 0x12, 0x34, 0x00));

        Assert.Equal(new byte[] { 1, 2, 3 }, glyph.Pixels);
    }

    [Fact]
    public void OneBitPixelsAreHighBitFirstAndFull()
    {
        BitmapGlyph glyph = Single(Glyph('G', 4, 2, flags: 2, 0b1010_0001));

        Assert.Equal(new byte[] { 15, 0, 15, 0, 0, 0, 0, 15 }, glyph.Pixels);
    }

    [Fact]
    public void TheRecordCarriesOffsetsAdvanceAndTheHeaderTheLineHeight()
    {
        byte[] file = Glyph('C', 1, 1, flags: 0, 0xF0);
        BitmapFont font = Bf4FontReader.Read(file);
        BitmapGlyph glyph = font.Find('C')!;

        Assert.Equal(18, font.LineHeight);
        Assert.Equal((-1, 4, 7), (glyph.OffsetX, glyph.OffsetY, glyph.Advance));
        Assert.Null(font.Find('D'));
    }

    [InstallFact]
    public void EveryShippedFontUnpacksEveryGlyphToItsSize()
    {
        string folder = Path.Combine(Dk2InstallGate.Path, "Data", "Text", "Default");
        string[] files = Directory.GetFiles(folder, "*.bf4");
        Assert.Equal(22, files.Length);
        foreach (string file in files)
        {
            BitmapFont font = Bf4FontReader.Open(file);
            Assert.All(font.Glyphs, g => Assert.Equal(g.Width * g.Height, g.Pixels.Length));
            Assert.All(font.Glyphs, g => Assert.All(g.Pixels, p => Assert.InRange(p, 0, 15)));
            Assert.NotNull(font.Find('A'));
        }
    }

    [InstallFact]
    public void TheTenPointInGameFontHasAnEighteenPixelLine()
    {
        BitmapFont font = Bf4FontReader.Open(Path.Combine(
            Dk2InstallGate.Path, "Data", "Text", "Default", "FONT_INGAME10.bf4"));

        Assert.Equal(18, font.LineHeight);
        Assert.Equal(251, font.Glyphs.Count);
        BitmapGlyph one = font.Find('1')!;
        Assert.Equal((4, 9, -1, 4, 4), (one.Width, one.Height, one.OffsetX, one.OffsetY, one.Advance));
    }

    private static BitmapGlyph Single(byte[] file) => Assert.Single(Bf4FontReader.Read(file).Glyphs);

    private static byte[] Glyph(char c, int width, int height, uint flags, params byte[] data)
    {
        var file = new byte[8 + 4 + 0x18 + data.Length];
        "F4FB"u8.CopyTo(file);
        file[4] = 1;
        file[5] = 18;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), 12);
        Span<byte> record = file.AsSpan(12);
        BinaryPrimitives.WriteUInt16LittleEndian(record, c);
        BinaryPrimitives.WriteUInt32LittleEndian(record[4..], (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(record[8..], (uint)((width * height + 1) / 2));
        BinaryPrimitives.WriteUInt32LittleEndian(record[0x0C..], flags);
        BinaryPrimitives.WriteUInt16LittleEndian(record[0x10..], (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(record[0x12..], (ushort)height);
        record[0x14] = unchecked((byte)-1);
        record[0x15] = 4;
        BinaryPrimitives.WriteUInt16LittleEndian(record[0x16..], 7);
        data.CopyTo(record[0x18..]);
        return file;
    }
}
