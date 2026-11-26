// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Text;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The glyph pieces and the gauge ranks a renderer draws from instead of a texture per text or per gauge
/// fraction must give the same pixels as the CPU images they replace.</summary>
public class HudTextAtlasTests
{
    [Fact]
    public void PiecesRebuildTheRenderedImageWhereInkOverlaps()
    {
        // 'j' reaches back two pixels into the letter before it; 'X' is taller than the line, so lines overlap too.
        var font = new BitmapFont(4, new[]
        {
            new BitmapGlyph('a', 3, 3, 0, 1, 3, new byte[] { 15, 4, 0, 0, 8, 1, 3, 3, 15 }),
            new BitmapGlyph('j', 3, 4, -2, 0, 2, new byte[] { 0, 9, 12, 7, 1, 2, 0, 15, 15, 2, 2, 2 }),
            new BitmapGlyph('X', 2, 7, 0, -1, 2, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }),
            new BitmapGlyph(' ', 0, 0, 0, 0, 2, Array.Empty<byte>()),
        });
        foreach (string text in new[] { "aj", "ajaj a", "X\nX", "jX aj\nXjaj", "", "  ", "j", "aXa\naXa\naXa" })
        {
            foreach (bool centred in new[] { false, true })
            {
                foreach (int wrap in new[] { 0, 5, 9 })
                {
                    AssertPiecesMatch(new HudText(font, text, wrap, centred));
                }
            }
        }
    }

    [InstallFact]
    public void PiecesRebuildEveryShippedFontsText()
    {
        string folder = Path.Combine(Dk2InstallGate.Path, "Data", "Text", "Default");
        const string sample = "The quick brown fox jumps over the lazy dog. Imp, Mistress, Dark Angel: 1,234 gold (75%)\n" +
            "fjord ajar Tj Yj Wj \"quoted\" {braces} [x] ~^`| AVAWAY LT Ty yjgjpjqj";
        foreach (string file in Directory.GetFiles(folder, "*.bf4"))
        {
            BitmapFont font = Bf4FontReader.Open(file);
            AssertPiecesMatch(new HudText(font, sample, 0));
            AssertPiecesMatch(new HudText(font, sample, 120, Centred: true));
            var every = new System.Text.StringBuilder();
            for (char a = '!'; a <= '~'; a++)
            {
                for (char b = '!'; b <= '~'; b++)
                {
                    every.Append(a).Append(b).Append(' ');
                }
            }

            AssertPiecesMatch(new HudText(font, every.ToString(), 1200));
        }
    }

    private static void AssertPiecesMatch(HudText text)
    {
        byte[] expected = HudTextPixels.Render(text, out int width, out int height);
        var pieces = new List<HudTextPiece>();
        HudTextPixels.Pieces(text, pieces, out int w, out int h);
        Assert.Equal((width, height), (w, h));
        byte[] drawn = new byte[width * height * 4];
        foreach (HudTextPiece piece in pieces)
        {
            Assert.InRange(piece.X, 0, width - piece.Width);
            Assert.InRange(piece.Y, 0, height - piece.Height);
            for (int y = piece.Y; y < piece.Y + piece.Height; y++)
            {
                for (int x = piece.X; x < piece.X + piece.Width; x++)
                {
                    byte alpha = piece.Alpha(x, y);
                    if (alpha == 0)
                    {
                        continue;
                    }

                    int at = (y * width + x) * 4;
                    if (drawn[at + 3] != 0)
                    {
                        Assert.Fail($"pixel {x},{y} of \"{text.Text}\" inked twice");
                    }

                    drawn[at] = drawn[at + 1] = drawn[at + 2] = 255; drawn[at + 3] = alpha;
                }
            }
        }
        Assert.Equal(expected, drawn);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 4)]
    [InlineData(1, 1)]
    [InlineData(33, 64)]
    public void GaugeRanksLightTheSamePixelsAsTheCpuMask(int width, int height)
    {
        var mask = new HudGaugeMask(width, height);
        byte[] source = new byte[width * height * 4];
        Array.Fill(source, (byte)200);
        byte[] into = new byte[source.Length];
        foreach (int maximum in new[] { 0, 1, 2, 3, 4, 7, 8, 12, 37, 100, 360, 1000 })
        {
            for (int value = -1; value <= maximum + 1; value++)
            {
                var fraction = new HudGaugeFraction(value, maximum);
                HudImagePixels.Fill(source, width, height, new HudImage("g", HudImageVariant.Gauge, Gauge: fraction), into);
                int lit = mask.LitCount(fraction);
                for (int i = 0; i < width * height; i++)
                {
                    Assert.Equal(into[i * 4] != 0, mask.Ranks[i] < lit);
                }
            }
        }
    }

    [Fact]
    public void GaugeOffsetMeasuresRowsAsTheMaskDoes()
    {
        Assert.Equal(new Vector2(-1.5f, -1.5f), HudImagePixels.GaugeOffset(0, 0, 4, 4));
        Assert.Equal(new Vector2(1.5f, 1.5f), HudImagePixels.GaugeOffset(3, 3, 4, 4));
    }
}
