// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>A rectangle in native virtual HUD units: the screen is 1920 high and as wide as its aspect makes it.</summary>
public readonly record struct HudRect(float X, float Y, float Width, float Height)
{
    /// <summary>Inclusive of the top-left edge, exclusive of the bottom-right (UI Toolkit's and Godot's rule).</summary>
    public bool Contains(float x, float y) => x >= X && x < X + Width && y >= Y && y < Y + Height;

    public static implicit operator HudRect(BottomPanelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}

/// <summary>The HUD's surface in the adapter's local pixels: virtual unit v sits at v * <see cref="Scale"/>.
/// Panels that DK2 lays out in whole screen pixels use it to reproduce that rounding.</summary>
public readonly record struct HudScreen(int Width, int Height)
{
    public float Scale => Height / 1920f;
    public float VirtualWidth => Width / Scale;
    /// <summary>Virtual units for a length in local pixels.</summary>
    public float Virtual(float pixels) => pixels / Scale;
}

/// <summary>An 8-bit RGBA tint multiplied into the image.</summary>
public readonly record struct HudColor(byte R, byte G, byte B, byte A = 255)
{
    public static HudColor White => new(255, 255, 255);
    public static HudColor Grey(byte level) => new(level, level, level);
}

/// <summary>How a draw combines with what is under it. DK2's render flag 8 is read as additive.</summary>
public enum HudBlend : byte { Alpha, Additive }

/// <summary>A pixel transform of a catalogue image that is part of what DK2 shows, applied once when loaded.</summary>
public enum HudImageVariant : byte
{
    None,
    /// <summary>The Reaper talisman cut to its first <see cref="HudImage.Pieces"/> quarters.</summary>
    TalismanArc,
    /// <summary>Only the last of those quarters (the one-shot blink).</summary>
    TalismanQuarter,
    /// <summary>A gauge ring filled clockwise from 12 o'clock to <see cref="HudImage.Gauge"/>, the rest black.
    /// Live: a renderer keeps one texture per path and refills it when the fraction changes.</summary>
    Gauge,
}

/// <summary>Text set in a bitmap font, white with the glyphs' coverage as alpha; a tint colours it.
/// <paramref name="WrapWidth"/> is in the font's pixels. Fonts compare by reference.</summary>
public sealed record HudText(BitmapFont Font, string Text, int WrapWidth);

/// <summary>An image by catalogue name plus a variant, or set text. The default value is "no image":
/// nothing is drawn.</summary>
public readonly record struct HudImage(string? Path, HudImageVariant Variant = HudImageVariant.None, byte Pieces = 0,
    HudGaugeFraction Gauge = default, HudText? Text = null)
{
    public bool IsNone => Path is null && Text is null;
    /// <summary>The variant's pixels change over time; see <see cref="HudImageVariant.Gauge"/>.</summary>
    public bool IsLive => Variant == HudImageVariant.Gauge;
    /// <summary>This image with its live parameter cleared: the identity a renderer keeps one texture for.</summary>
    public HudImage Slot => IsLive ? new HudImage(Path, Variant, Pieces) : this;
    public static implicit operator HudImage(string? path) => new(path);
    public static HudImage Of(HudText text) => new(null, Text: text);
}

public enum HudDrawKind : byte
{
    /// <summary>The image's <see cref="HudDraw.Uv"/> part (all of it by default) stretched over <see cref="HudDraw.Rect"/>.</summary>
    Quad,
    /// <summary>A clockwise wedge from 12 o'clock over <see cref="HudDraw.Percent"/>; see <see cref="HudSector"/>.</summary>
    Sector,
    /// <summary>The rect filled with the tint; no image.</summary>
    Fill,
}

/// <param name="Uv">The part of the image drawn, in image fractions from its top-left; null for all of it.</param>
/// <param name="Clip">A window the quad shows through (an overflow-hidden box), or null. The renderer places
/// the quad and the window on its pixel grid first and then cuts with <see cref="HudQuads.Cut"/>, so a text
/// image keeps one texel per pixel as it scrolls.</param>
public readonly record struct HudDraw(HudDrawKind Kind, HudImage Image, HudRect Rect, HudColor Tint,
    HudBlend Blend = HudBlend.Alpha, int Percent = 0, HudRect? Uv = null, HudRect? Clip = null);

/// <summary>Quad helpers shared by the panels and renderers: clipping to a window and tiling an area.</summary>
public static class HudQuads
{
    /// <summary>Adds <paramref name="rect"/> seen through <paramref name="clip"/>; nothing when they do not overlap.</summary>
    public static void Clipped(HudFrame frame, HudImage image, HudRect rect, HudRect clip, HudColor tint)
    {
        if (!Cut(rect, new HudRect(0, 0, 1, 1), clip, out _, out _)) return;
        frame.Add(new HudDraw(HudDrawKind.Quad, image, rect, tint, Clip: clip));
    }

    /// <summary>The part of a quad (<paramref name="rect"/> showing <paramref name="uv"/> of its image) inside
    /// <paramref name="clip"/>, with the matching part of the image; false when nothing is left. Renderers
    /// call it with coordinates already on their pixel grid.</summary>
    public static bool Cut(HudRect rect, HudRect uv, HudRect clip, out HudRect drawn, out HudRect drawnUv)
    {
        float left = Math.Max(rect.X, clip.X), top = Math.Max(rect.Y, clip.Y);
        float right = Math.Min(rect.X + rect.Width, clip.X + clip.Width), bottom = Math.Min(rect.Y + rect.Height, clip.Y + clip.Height);
        drawn = drawnUv = default;
        if (right <= left || bottom <= top || rect.Width <= 0 || rect.Height <= 0) return false;
        drawn = new HudRect(left, top, right - left, bottom - top);
        drawnUv = new HudRect(uv.X + (left - rect.X) / rect.Width * uv.Width, uv.Y + (top - rect.Y) / rect.Height * uv.Height,
            (right - left) / rect.Width * uv.Width, (bottom - top) / rect.Height * uv.Height);
        return true;
    }

    /// <summary>Covers <paramref name="area"/> with the image repeated at <paramref name="tile"/> square from
    /// its top-left, the last row and column cropped (CSS background-repeat).</summary>
    public static void Tiled(HudFrame frame, HudImage image, HudRect area, float tile, HudColor tint)
    {
        if (tile <= 0 || area.Width <= 0 || area.Height <= 0) return;
        for (float y = 0; y < area.Height; y += tile)
        for (float x = 0; x < area.Width; x += tile)
        {
            float w = Math.Min(tile, area.Width - x), h = Math.Min(tile, area.Height - y);
            frame.Add(new HudDraw(HudDrawKind.Quad, image, new HudRect(area.X + x, area.Y + y, w, h), tint,
                Uv: new HudRect(0, 0, w / tile, h / tile)));
        }
    }
}

/// <summary>One frame of HUD draws in back-to-front order. Reused between frames: <see cref="Clear"/>
/// keeps its storage, so building a frame allocates nothing once it has grown to size.</summary>
public sealed class HudFrame
{
    private HudDraw[] _draws = new HudDraw[256];
    private int _count;

    public int Count => _count;
    public ReadOnlySpan<HudDraw> Draws => new(_draws, 0, _count);
    public void Clear() => _count = 0;

    public void Add(in HudDraw draw)
    {
        if (_count == _draws.Length) Array.Resize(ref _draws, _count * 2);
        _draws[_count++] = draw;
    }

    /// <summary>True when both frames would draw the same thing, so a renderer can skip rebuilding.</summary>
    public bool SameAs(HudFrame other) => Draws.SequenceEqual(other.Draws);

    public void CopyFrom(HudFrame other)
    {
        if (_draws.Length < other._count) _draws = new HudDraw[other._draws.Length];
        Array.Copy(other._draws, _draws, other._count);
        _count = other._count;
    }
}

/// <summary>A vertex of a HUD triangle fan: position in virtual units, UV with V = 0 at the image's top.</summary>
public readonly record struct HudVertex(float X, float Y, float U, float V);

/// <summary>Hud_DrawTexturedRadialSector <c>0x0041EF70</c> as the slot draw uses it: the image cut to the
/// wedge from 12 o'clock clockwise over p% (angle 3π − 2π·p/100 to 3π), reaching the square's corners.
/// Like DK2, the width is the diameter on both axes.</summary>
public static class HudSector
{
    /// <summary>The largest fan: centre, top, four corners and the wedge's end.</summary>
    public const int MaxVertices = 7;

    /// <summary>Writes a fan (centre first, then the rim clockwise on screen) and returns its vertex
    /// count; 0 when nothing is drawn. Triangles are (0, k, k + 1) for k in 1 .. count - 2.</summary>
    public static int Fan(HudRect rect, int percent, Span<HudVertex> into)
    {
        if (percent <= 0 || percent >= 100 || rect.Width <= 0) return 0;
        const float Pi = 3.14159274f;
        float sweep = 2 * Pi * percent / 100f;
        int corners = 0;
        while (corners < 4 && Pi / 4 + corners * Pi / 2 < sweep) corners++;
        int n = 0;
        into[n++] = At(rect, 0, 0);
        into[n++] = At(rect, 0, -1);
        // Corners in sweep order: top-right, bottom-right, bottom-left, top-left (screen y down).
        for (int k = 0; k < corners; k++) into[n++] = At(rect, k < 2 ? 1 : -1, k == 0 || k == 3 ? -1 : 1);
        float x = (float)Math.Sin(sweep), y = -(float)Math.Cos(sweep), reach = Math.Max(Math.Abs(x), Math.Abs(y));
        into[n++] = At(rect, x / reach, y / reach);
        return n;
    }

    private static HudVertex At(HudRect rect, float x, float y)
    {
        float u = (x + 1) / 2, v = (y + 1) / 2;
        return new HudVertex(rect.X + u * rect.Width, rect.Y + v * rect.Width, u, v);
    }
}
