// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Art.Textures;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>Turns a decoded top-down image into a Unity texture. The minimap's frame, corners and north sprite
    /// are <see cref="DK2.Presentation.MinimapHud"/>'s now; the name stays for the panels that decode with it.</summary>
    public static class MinimapFrameVisual
    {
        public static Texture2D Decode(RgbaImage image)
        {
            int row = checked(image.Width * 4);
            var bottomUp = new byte[image.Pixels.Length];
            for (int y = 0; y < image.Height; y++)
                Buffer.BlockCopy(image.Pixels, y * row, bottomUp, (image.Height - 1 - y) * row, row);
            var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(bottomUp); texture.Apply();
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }
    }
}
