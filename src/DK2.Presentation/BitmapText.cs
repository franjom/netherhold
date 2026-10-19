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

    /// <summary>
    /// <c>0x00553690</c>: adjusts the base size for the screen width. It subtracts 2 at 512,
    /// and 2 (below 11) or 4 at 400. It adds 2 at 800 and 4 at 1024. Any other width, 640
    /// included, keeps the base size: a DK2 capture at 1600 x 1200 draws Level 1's subtitle
    /// in <c>FONT_INGAME10</c>, unscaled (owner, 2026-10-17). The size then picks
    /// <c>FONT_INGAME&lt;n&gt;</c>: 6 picks 7, and any size without its own case picks 12.
    /// This is DK2's rule. Our Unity view does not use it: the owner found the result
    /// unreadable at high resolutions (see <see cref="LegibleFileName"/>).
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

    /// <summary>The shipped in-game sizes, smallest first.</summary>
    public static readonly int[] IngameSizes = [7, 8, 9, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    /// <summary>
    /// Our choice, not DK2's: the largest shipped in-game size no bigger than a fortieth of
    /// the screen height, drawn unscaled so it stays crisp. That is 10 at 400 high, 26 at
    /// 1080 and 36 at 1440. DK2 itself keeps size 10 at most widths (<see cref="IngamePointSize"/>),
    /// which the owner found unreadable at high resolutions (2026-10-17).
    /// </summary>
    public static string LegibleFileName(int screenHeight) => LargestWithin(screenHeight, 40);

    /// <summary>
    /// Our choice, not DK2's: the top status strip and the world hover popup use the largest
    /// shipped in-game size no bigger than a sixtieth of the screen height: 12 at 768, 18 at
    /// 1080 and 24 at 1440. The owner found the subtitle size too large for them (2026-10-19).
    /// </summary>
    public static string HudTextFileName(int screenHeight) => LargestWithin(screenHeight, 60);

    /// <summary>
    /// Our choice, not DK2's: hover tooltips are half the status strip's size, a 120th of the
    /// screen height (9 at 1080, 12 at 1440, the smallest shipped 7 below 960), at the owner's
    /// request (2026-10-19).
    /// </summary>
    public static string HudTooltipFileName(int screenHeight) => LargestWithin(screenHeight, 120);

    private static string LargestWithin(int screenHeight, int divisor)
    {
        int chosen = IngameSizes[0];
        foreach (int size in IngameSizes)
            if (size * divisor <= screenHeight) chosen = size;
        return $"FONT_INGAME{chosen}.bf4";
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
    /// spaces and inside a word only when the word alone is too wide. Spaces inside a line
    /// are kept, as DK2 keeps the double space after a full stop (the 1600 x 1200 capture).
    /// That DK2's layout <c>0x0062CAE0</c> wraps this way is INFERRED. It is unread.
    /// </summary>
    public static IReadOnlyList<BitmapTextLine> Wrap(BitmapFont font, string text, int maxWidth)
    {
        var lines = new List<BitmapTextLine>();
        foreach (string paragraph in text.Replace("\r", "").Split('\n'))
        {
            string line = "";
            bool started = false;
            int first = lines.Count;
            foreach (string word in paragraph.Split(' '))
            {
                if (!started && word.Length == 0) continue;
                string candidate = started ? line + " " + word : word;
                if (Measure(font, candidate) <= maxWidth)
                {
                    line = candidate;
                    started = true;
                    continue;
                }
                if (started)
                {
                    string done = line.TrimEnd(' ');
                    lines.Add(new BitmapTextLine(done, Measure(font, done)));
                }
                started = word.Length > 0;
                line = word;
                while (line.Length > 1 && Measure(font, line) > maxWidth)
                {
                    int fit = 1;
                    while (fit < line.Length && Measure(font, line[..(fit + 1)]) <= maxWidth) fit++;
                    lines.Add(new BitmapTextLine(line[..fit], Measure(font, line[..fit])));
                    line = line[fit..];
                }
            }
            // A paragraph that ended in an overflowing space has nothing left; an empty
            // paragraph still makes its blank line.
            if (!started && lines.Count > first) continue;
            line = line.TrimEnd(' ');
            lines.Add(new BitmapTextLine(line, Measure(font, line)));
        }
        return lines;
    }
}
