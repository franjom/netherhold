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
        private IReadOnlyList<BitmapTextLine> _wrapped;
        private string _wrappedText;
        private int _wrapWidth;

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
        /// centred and the block centred (<see cref="BitmapTextPlacement"/>).
        /// <paramref name="scale"/> magnifies the font's pixels. The wrap is kept while the text
        /// and the wrap width stay the same, so a repaint does not wrap again.</summary>
        public void DrawCentred(Rect box, string text, float scale)
        {
            int wrapWidth = BitmapTextPlacement.WrapWidth(box.width, scale);
            if (_wrapped == null || wrapWidth != _wrapWidth || !string.Equals(text, _wrappedText, System.StringComparison.Ordinal))
            {
                _wrapped = BitmapTextLayout.Wrap(_font, text, wrapWidth);
                _wrappedText = text;
                _wrapWidth = wrapWidth;
            }
            DrawLines(box, _wrapped, scale);
        }

        /// <summary>Blits the glyphs <see cref="BitmapTextPlacement.Place"/> places for retained layout.</summary>
        public void DrawLines(Rect box, IReadOnlyList<BitmapTextLine> lines, float scale, bool centred = true)
        {
            foreach (PlacedBitmapGlyph placed in BitmapTextPlacement.Place(
                _font, new HudRect(box.x, box.y, box.width, box.height), lines, scale, centred))
            {
                if (_uv.TryGetValue(placed.Glyph.Character, out Rect uv))
                    GUI.DrawTextureWithTexCoords(new Rect(placed.Rect.X, placed.Rect.Y, placed.Rect.Width, placed.Rect.Height), _atlas, uv);
            }
        }
    }
}
