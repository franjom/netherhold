// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The pixels behind a <see cref="HudLabel"/> and <see cref="HudImageVariant.Glow"/>: the BF4 label bitmap
/// and the orange glow frames the front end puts under a hovered label or scroll bar part. Renderers call it, so
/// every engine gets the same pixels.</summary>
public static class HudLabelPixels
{
    /// <summary>The label's ink box rasterized white with each glyph's 4-bit coverage as alpha, <paramref name="padding"/>
    /// transparent pixels on every side; top-down RGBA. Ink boxes overlap (a j reaches back under the letter before
    /// it), so each pixel keeps the stronger ink.</summary>
    public static RgbaImage Render(HudLabel label, int padding = 0)
    {
        FrontEndLabelGeometry geometry = label.Geometry;
        int width = Math.Max(1, (int)geometry.InkBounds.Width) + 2 * padding;
        int height = Math.Max(1, (int)geometry.InkBounds.Height) + 2 * padding;
        byte[] pixels = new byte[width * height * 4];
        foreach (FrontEndGlyph item in geometry.Glyphs)
        {
            BitmapGlyph glyph = label.Font.Find(item.Character)!;
            for (int y = 0; y < item.InkBounds.Height; y++)
                for (int x = 0; x < item.InkBounds.Width; x++)
                {
                    int targetX = (int)(item.InkBounds.X - geometry.InkBounds.X) + x + padding;
                    int targetY = (int)(item.InkBounds.Y - geometry.InkBounds.Y) + y + padding;
                    byte alpha = (byte)(glyph.Pixels[((int)item.SourceInkBounds.Y + y) * glyph.Width + (int)item.SourceInkBounds.X + x] * 17);
                    int at = (targetY * width + targetX) * 4;
                    if (alpha <= pixels[at + 3]) continue;
                    pixels[at] = 255; pixels[at + 1] = 255; pixels[at + 2] = 255; pixels[at + 3] = alpha;
                }
        }
        return new RgbaImage(width, height, pixels);
    }

    /// <summary>The image with <paramref name="padding"/> transparent pixels added on every side.</summary>
    public static RgbaImage Pad(RgbaImage image, int padding)
    {
        int width = image.Width + 2 * padding, height = image.Height + 2 * padding;
        byte[] padded = new byte[width * height * 4];
        for (int y = 0; y < image.Height; y++)
            Buffer.BlockCopy(image.Pixels, y * image.Width * 4, padded, ((y + padding) * width + padding) * 4, image.Width * 4);
        return new RgbaImage(width, height, padded);
    }

    /// <summary>The glow frame at <paramref name="quarters"/> (1 to 4) of full strength over an already padded image:
    /// the native dilation and blur (<see cref="FrontEndLabelGlow"/>, 0x005524F0), its alpha scaled by quarters / 4,
    /// as 0x0054FD20 builds frames 0-3.</summary>
    public static RgbaImage GlowFrame(RgbaImage padded, int quarters) => Quarter(Glow(padded), quarters);

    /// <summary>The full-strength glow over an already padded image. It is the costly part, the same for every
    /// quarter, so a renderer computes it once per label and takes the quarters from it.</summary>
    public static RgbaImage Glow(RgbaImage padded) => FrontEndLabelGlow.Create(padded);

    /// <summary>A copy of a full-strength glow with its alpha scaled by <paramref name="quarters"/> / 4.</summary>
    public static RgbaImage Quarter(RgbaImage full, int quarters)
    {
        byte[] pixels = (byte[])full.Pixels.Clone();
        for (int at = 3; at < pixels.Length; at += 4) pixels[at] = (byte)(pixels[at] * quarters / 4);
        return new RgbaImage(full.Width, full.Height, pixels);
    }
}
