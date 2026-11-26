// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The window sizes the launcher offers for a screen, the one it picks first, and the launcher's own scale.
/// Sizes are in screen pixels; a screen is its usable area (the desktop less the taskbar).</summary>
public static class LauncherWindowSizes
{
    /// <summary>Room left for a window's title bar and frame when judging whether a size fits.</summary>
    public const int FrameAllowance = 48;

    /// <summary>The launcher's own layout size before scaling.</summary>
    public static readonly LauncherWindowSize LauncherSize = new(1100, 680);

    /// <summary>The share of the screen the default window may take on each side.</summary>
    private const int DefaultPercent = 90;

    /// <summary>16:9 sizes and the common desktop sizes, widest first, then tallest.</summary>
    private static readonly LauncherWindowSize[] Candidates =
    [
        new(5120, 2880), new(3840, 2160), new(3440, 1440), new(3200, 1800), new(2560, 1600), new(2560, 1440),
        new(2560, 1080), new(1920, 1200), new(1920, 1080), new(1680, 1050), new(1600, 900), new(1440, 900),
        new(1366, 768), new(1280, 1024), new(1280, 800), new(1280, 720), new(1024, 768), new(1024, 576), new(960, 540),
    ];

    /// <summary>The sizes whose window fits the screen with its frame, widest first; never empty (the smallest size
    /// stands in on a screen too small for any).</summary>
    public static IReadOnlyList<LauncherWindowSize> Fitting(int screenWidth, int screenHeight)
    {
        var fitting = new List<LauncherWindowSize>();
        foreach (LauncherWindowSize size in Candidates)
        {
            if (Fits(size, screenWidth, screenHeight))
            {
                fitting.Add(size);
            }
        }

        if (fitting.Count == 0)
        {
            fitting.Add(Candidates[Candidates.Length - 1]);
        }

        return fitting;
    }

    public static bool Fits(LauncherWindowSize size, int screenWidth, int screenHeight) =>
        size.Width <= screenWidth && size.Height + FrameAllowance <= screenHeight;

    /// <summary>The largest 16:9 size within <see cref="DefaultPercent"/> of the screen on each side (1600 x 900 on a
    /// 1920 x 1080 screen), else the smallest 16:9 size.</summary>
    public static LauncherWindowSize Default(int screenWidth, int screenHeight)
    {
        foreach (LauncherWindowSize size in Candidates)
        {
            if (size.IsWide && size.Width * 100 <= screenWidth * DefaultPercent
                && size.Height * 100 <= screenHeight * DefaultPercent)
            {
                return size;
            }
        }

        return Candidates[Candidates.Length - 1];
    }

    /// <summary>The dropdown's sizes and the selected one: the fitting sizes, with a saved size that fits but is not
    /// among them (a hand-edited file) added in order; a saved size that no longer fits gives way to
    /// <see cref="Default"/>.</summary>
    public static (IReadOnlyList<LauncherWindowSize> Sizes, int Selected) Options(LauncherWindowSize? saved,
        int screenWidth, int screenHeight)
    {
        var sizes = new List<LauncherWindowSize>(Fitting(screenWidth, screenHeight));
        LauncherWindowSize chosen = saved is LauncherWindowSize s && Fits(s, screenWidth, screenHeight)
            ? s : Default(screenWidth, screenHeight);
        int index = sizes.IndexOf(chosen);
        if (index < 0)
        {
            index = 0;
            while (index < sizes.Count && Larger(sizes[index], chosen))
            {
                index++;
            }

            sizes.Insert(index, chosen);
        }
        return (sizes, index);
    }

    /// <summary>The launcher's scale for a screen's DPI: 96 DPI is 1, rounded to quarters, at least 1, and no larger than
    /// lets the launcher fit the screen with its frame.</summary>
    public static float LauncherScale(float dpi, int screenWidth, int screenHeight)
    {
        float scale = Math.Max(1f, (float)Math.Round(dpi / 96f * 4f) / 4f);
        while (scale > 1f && (LauncherSize.Width * scale > screenWidth
                              || LauncherSize.Height * scale + FrameAllowance > screenHeight))
        {
            scale -= .25f;
        }

        return scale;
    }

    private static bool Larger(LauncherWindowSize a, LauncherWindowSize b) =>
        a.Width > b.Width || (a.Width == b.Width && a.Height > b.Height);
}
