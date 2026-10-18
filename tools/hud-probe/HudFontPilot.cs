// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Linq;
using DK2.Data.Model;
using UnityEngine;

namespace DK2.Unity.HudProbe
{
    public static class HudFontPilot
    {
        public static Texture2D Raster(BitmapFont font, string text)
        {
            BitmapGlyph[] glyphs = text.Select(c => font.Find(c)
                ?? throw new InvalidOperationException("Missing glyph: " + c)).ToArray();
            int minY = Math.Min(0, glyphs.Min(g => g.OffsetY));
            int height = Math.Max(font.LineHeight, glyphs.Max(g => g.OffsetY + g.Height)) - minY + 16;
            int width = glyphs.Sum(g => g.Advance) + Math.Max(0, glyphs.Max(g => g.OffsetX + g.Width - g.Advance)) + 16;
            var pixels = new Color32[width * height];
            int pen = 8;
            foreach (BitmapGlyph glyph in glyphs)
            {
                for (int y = 0; y < glyph.Height; y++)
                    for (int x = 0; x < glyph.Width; x++)
                    {
                        int px = pen + glyph.OffsetX + x, py = 8 + glyph.OffsetY - minY + y;
                        if (px >= 0 && px < width && py >= 0 && py < height)
                            pixels[(height - 1 - py) * width + px] = new Color32(255, 255, 255,
                                (byte)(glyph.Pixels[y * glyph.Width + x] * 17));
                    }
                pen += glyph.Advance;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels); texture.Apply(false, false);
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        // Bounded pilot conversion, not the production font atlas implementation.
        public static Texture2D DistanceField(Texture2D raster)
        {
            const int spread = 8;
            int width = raster.width, height = raster.height;
            Color32[] source = raster.GetPixels32(), result = new Color32[source.Length];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    bool inside = source[y * width + x].a >= 128;
                    int nearest = spread * spread;
                    for (int dy = -spread; dy <= spread; dy++)
                        for (int dx = -spread; dx <= spread; dx++)
                        {
                            int square = dx * dx + dy * dy;
                            if (square >= nearest) continue;
                            int sx = x + dx, sy = y + dy;
                            bool otherInside = sx >= 0 && sx < width && sy >= 0 && sy < height
                                && source[sy * width + sx].a >= 128;
                            if (inside != otherInside) nearest = square;
                        }
                    float distance = (float)Math.Sqrt(nearest) - .5f;
                    byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(.5f + (inside ? distance : -distance) / (2 * spread)) * 255);
                    result[y * width + x] = new Color32(255, 255, 255, value);
                }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(result); texture.Apply(false, false);
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }
    }
}
