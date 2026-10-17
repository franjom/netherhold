// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>Which in-game <c>.bf4</c> font DK2 loads for a text style at a screen width.</summary>
public static class InterfaceFonts
{
    /// <summary>
    /// The base size of the style the path subtitle uses. <c>0x005534D0</c> loads base 10
    /// into style <c>0x00741320</c> (which <c>0x0040A3E0</c> copies), 14 into <c>0x00741370</c>,
    /// and 18 into the third.
    /// </summary>
    public const int SubtitleBaseSize = 10;

    /// <summary>The screen widths DK2's font rule adjusts for, smallest first.</summary>
    public static readonly int[] NativeWidths = [400, 512, 640, 800, 1024];

    /// <summary>
    /// <c>0x00553690</c>: adjusts the base size for the screen width. It subtracts 2 at 512,
    /// and 2 (below 11) or 4 at 400. It adds 2 at 800 and 4 at 1024. Any other width, 640
    /// included, keeps the base size. The size then picks <c>FONT_INGAME&lt;n&gt;</c>: 6 picks
    /// 7, and any size without its own case picks 12.
    /// </summary>
    public static int IngamePointSize(int baseSize, int screenWidth)
    {
        int size = screenWidth switch
        {
            512 => baseSize - 2,
            400 => baseSize < 11 ? baseSize - 2 : baseSize - 4,
            800 => baseSize + 2,
            1024 => baseSize + 4,
            _ => baseSize,
        };
        return size switch
        {
            6 => 7,
            8 or 10 or 14 or 16 or 18 or 20 or 22 or 24 => size,
            _ => 12,
        };
    }

    public static string IngameFileName(int baseSize, int screenWidth) =>
        $"FONT_INGAME{IngamePointSize(baseSize, screenWidth)}.bf4";

    /// <summary>
    /// The DK2 width a modern screen stands for: the largest of <see cref="NativeWidths"/>
    /// that fits, or 400 if none does. DK2 offered only its own widths. A wider screen
    /// draws the text at this width's size, scaled up by <c>screen / reference</c>, so the
    /// text keeps its share of the screen. This is our choice, not DK2's.
    /// </summary>
    public static int ReferenceWidth(int screenWidth)
    {
        int chosen = NativeWidths[0];
        foreach (int width in NativeWidths)
            if (width <= screenWidth) chosen = width;
        return chosen;
    }
}

/// <summary>One laid-out line: its text and its width in font pixels.</summary>
public readonly record struct BitmapTextLine(string Text, int Width);

/// <summary>
/// Lays text out in a <see cref="BitmapFont"/>. A glyph's width on the line is its advance.
/// A character the font lacks takes no space. That rule, and finding a glyph by its record's
/// character, are INFERRED: DK2's font methods take a glyph index, and the mapping from
/// character to index lives in the unread layout <c>0x0062CAE0</c>.
/// </summary>
public static class BitmapTextLayout
{
    public static int Measure(BitmapFont font, string text)
    {
        int width = 0;
        foreach (char c in text) width += font.Find(c)?.Advance ?? 0;
        return width;
    }

    /// <summary>
    /// Word-wraps <paramref name="text"/> to <paramref name="maxWidth"/>, breaking at
    /// spaces and inside a word only when the word alone is too wide. That DK2's layout
    /// <c>0x0062CAE0</c> wraps this way is INFERRED. It is unread.
    /// </summary>
    public static IReadOnlyList<BitmapTextLine> Wrap(BitmapFont font, string text, int maxWidth)
    {
        var lines = new List<BitmapTextLine>();
        foreach (string paragraph in text.Replace("\r", "").Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (Measure(font, candidate) <= maxWidth)
                {
                    line = candidate;
                    continue;
                }
                if (line.Length > 0) lines.Add(new BitmapTextLine(line, Measure(font, line)));
                line = word;
                while (line.Length > 1 && Measure(font, line) > maxWidth)
                {
                    int fit = 1;
                    while (fit < line.Length && Measure(font, line[..(fit + 1)]) <= maxWidth) fit++;
                    lines.Add(new BitmapTextLine(line[..fit], Measure(font, line[..fit])));
                    line = line[fit..];
                }
            }
            lines.Add(new BitmapTextLine(line, Measure(font, line)));
        }
        return lines;
    }
}
