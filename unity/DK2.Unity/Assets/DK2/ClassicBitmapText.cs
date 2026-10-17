// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Data.Model;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>
    /// Draws text in one of DK2's <c>.bf4</c> bitmap fonts through IMGUI. The glyphs are
    /// packed once into a point-filtered texture: white, with the glyph's 4-bit value as
    /// alpha. The style colour <c>0x005534D0</c> gives the subtitle style is
    /// FF FF FF FF.
    /// </summary>
    public sealed class ClassicBitmapText : System.IDisposable
    {
        private readonly BitmapFont _font;
        private readonly Texture2D _atlas;
        private readonly Dictionary<char, Rect> _uv = new Dictionary<char, Rect>();

        public ClassicBitmapText(BitmapFont font)
        {
            _font = font;
            const int atlasWidth = 512;
            int x = 0, y = 0, shelf = 0;
            var places = new List<(BitmapGlyph Glyph, int X, int Y)>();
            foreach (BitmapGlyph glyph in font.Glyphs)
            {
                if (glyph.Width == 0 || glyph.Height == 0 || _uv.ContainsKey(glyph.Character)) continue;
                if (x + glyph.Width + 1 > atlasWidth)
                {
                    x = 0;
                    y += shelf + 1;
                    shelf = 0;
                }
                places.Add((glyph, x, y));
                _uv[glyph.Character] = default;
                x += glyph.Width + 1;
                shelf = Mathf.Max(shelf, glyph.Height);
            }
            int atlasHeight = Mathf.NextPowerOfTwo(Mathf.Max(1, y + shelf + 1));
            var pixels = new Color32[atlasWidth * atlasHeight];
            foreach ((BitmapGlyph glyph, int gx, int gy) in places)
            {
                for (int row = 0; row < glyph.Height; row++)
                for (int column = 0; column < glyph.Width; column++)
                {
                    // Texture rows run bottom-up.
                    int index = (atlasHeight - 1 - (gy + row)) * atlasWidth + gx + column;
                    pixels[index] = new Color32(255, 255, 255, (byte)(glyph.Pixels[row * glyph.Width + column] * 17));
                }
                _uv[glyph.Character] = new Rect(
                    gx / (float)atlasWidth, (atlasHeight - gy - glyph.Height) / (float)atlasHeight,
                    glyph.Width / (float)atlasWidth, glyph.Height / (float)atlasHeight);
            }
            _atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "DK2 bitmap font",
            };
            _atlas.SetPixels32(pixels);
            _atlas.Apply(false, true);
        }

        public void Dispose() => Object.Destroy(_atlas);

        /// <summary>Draws <paramref name="text"/> wrapped to <paramref name="box"/>, each line
        /// centred and the block centred: the subtitle's flags 0x44 (horizontal and vertical
        /// mode 2 of 0x0062CA70/0x0062CA00; that 2 is centre is INFERRED).
        /// <paramref name="scale"/> magnifies the font's pixels.</summary>
        public void DrawCentred(Rect box, string text, float scale)
        {
            IReadOnlyList<BitmapTextLine> lines = BitmapTextLayout.Wrap(_font, text, Mathf.FloorToInt(box.width / scale));
            float lineHeight = _font.LineHeight * scale;
            float top = box.y + (box.height - lines.Count * lineHeight) / 2f;
            for (int i = 0; i < lines.Count; i++)
            {
                float pen = box.x + (box.width - lines[i].Width * scale) / 2f;
                float y = top + i * lineHeight;
                foreach (char c in lines[i].Text)
                {
                    BitmapGlyph glyph = _font.Find(c);
                    if (glyph == null) continue;
                    if (_uv.TryGetValue(c, out Rect uv) && glyph.Width > 0)
                        GUI.DrawTextureWithTexCoords(new Rect(
                            Mathf.Round(pen + glyph.OffsetX * scale), Mathf.Round(y + glyph.OffsetY * scale),
                            glyph.Width * scale, glyph.Height * scale), _atlas, uv);
                    pen += glyph.Advance * scale;
                }
            }
        }
    }
}
