// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The type-2 scrollbar a type-7 text widget owns (0x0052A0D0), as menu40's text uses it
/// (docs/research/minimap-controls.md, "Menu40 scrollbar"). Lengths along the bar are virtual
/// units; the position and range are screen pixels, as DK2 keeps them.</summary>
public sealed class TextScrollBar
{
    /// <summary>The bar's width, and the side of each arrow square.</summary>
    public const int Width = 64, Arrow = 64, MinimumThumb = 16;

    /// <summary>Menu-local rectangle of menu40's bar: right of the (20, 20) 1096x288 text widget.</summary>
    public const int MenuX = 1116, MenuY = 20, Height = 288;

    /// <summary>0x0052A330 measures the text this much narrower than its widget, the bar's width.</summary>
    public const int TextWidget = 1096, MeasuredWidth = TextWidget - Width;

    /// <summary>Enabled when the text overflowed at the last <see cref="Reset"/>.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Max − min: text height − box height, in pixels.</summary>
    public int Range { get; private set; }

    public int Position { get; private set; }

    /// <summary>0x0052A330: every set puts the position back to the top; the bar is enabled only
    /// when the text is taller than its box.</summary>
    public void Reset(int textPixels, int boxPixels)
    {
        Enabled = textPixels > boxPixels;
        Range = Enabled ? textPixels - boxPixels : 0;
        Position = 0;
    }

    /// <summary>0x00528F60 with min 0, the given max and position 0, as 0x0053D810 arms the statistics
    /// list's bar after a game.</summary>
    public void Arm(int range)
    {
        Enabled = true;
        Range = Math.Max(0, range);
        Position = 0;
    }

    /// <summary>An arrow held one GUI update, or one press on the track (0x00528FB0): ±1 pixel, clamped.</summary>
    public void Step(int direction)
    {
        if (!Enabled) return;
        Position = Math.Clamp(Position + Math.Sign(direction), 0, Range);
    }

    /// <summary>The track between the arrows (<c>h − 128</c>).</summary>
    public static int Track(int height) => height - 2 * Arrow;

    /// <summary>0x00529160 / 0x0052A1F0: the thumb's top and length along the bar, relative to the
    /// bar's top: <c>len = max(16, T / (R + 1))</c>, <c>top = 64 + pos·(T − len) / R</c>.</summary>
    public (int Top, int Length) Thumb(int height)
    {
        int track = Track(height);
        int length = Math.Max(MinimumThumb, track / (Range + 1));
        int top = Arrow + (Range == 0 ? 0 : (int)((long)Position * (track - length) / Range));
        return (top, length);
    }

    /// <summary>0x005290F0: a thumb drag to <paramref name="pointer"/> (virtual units from the bar's top)
    /// sets <c>pos = (pointer − 64)·(R + 1) / T</c>, clamped.</summary>
    public void DragTo(int pointer, int height)
    {
        if (!Enabled) return;
        int track = Track(height);
        Position = Math.Clamp((int)((long)(pointer - Arrow) * (Range + 1) / track), 0, Range);
    }
}
