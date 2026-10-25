// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using DK2.Art;
using DK2.Art.Textures;
using DK2.Data.Wad;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>FrontEnd.WAD images through the replaceable texture-source boundary, entirely in memory.</summary>
    public sealed class ClassicFrontEndAssets : IDisposable
    {
        private readonly WadImageSource _source;
        public ITextureSource Images => _source;

        public ClassicFrontEndAssets(string installDirectory)
        {
            _source = new WadImageSource(WadArchive.Open(Path.Combine(installDirectory, "Data", "FrontEnd.WAD")), DecodePng);
        }

        public Texture2D LoadTexture(string name) => MinimapFrameVisual.Decode(_source.Read(name));
        public Texture2D LoadTexture(int entryIndex) => MinimapFrameVisual.Decode(_source.Read(entryIndex));

        private static RgbaImage DecodePng(byte[] bytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(bytes)) throw new InvalidDataException("Invalid front-end PNG.");
                Color32[] colors = texture.GetPixels32();
                byte[] pixels = new byte[checked(texture.width * texture.height * 4)];
                for (int y = 0; y < texture.height; y++)
                    for (int x = 0; x < texture.width; x++)
                    {
                        Color32 color = colors[(texture.height - 1 - y) * texture.width + x];
                        int index = (y * texture.width + x) * 4;
                        pixels[index] = color.r; pixels[index + 1] = color.g;
                        pixels[index + 2] = color.b; pixels[index + 3] = color.a;
                    }
                return new RgbaImage(texture.width, texture.height, pixels);
            }
            finally { UnityEngine.Object.Destroy(texture); }
        }

        public void Dispose() => _source.Dispose();
    }
}
