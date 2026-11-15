// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using DK2.Data.Model;
using DK2.Art.Textures;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    public interface IClassicFrontEndMenuAssets : IFrontEndMenuSources, IDisposable
    {
        Font ReplacementFont { get; }
        /// <summary>A front-end image's pixels by name; the menu's <see cref="HudCanvas"/> reads its art through this.</summary>
        RgbaImage Load(string name);
        Texture2D Image(string name);
        CampaignBriefing Briefing(CampaignSlot slot);
    }

    /// <summary>The shared <see cref="InstallFrontEndSources"/> with Unity's PNG decoder, textures and replacement font.</summary>
    public sealed class InstallClassicFrontEndMenuAssets : IClassicFrontEndMenuAssets
    {
        private readonly InstallFrontEndSources _sources;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        public InstallClassicFrontEndMenuAssets(string install)
        {
            _sources = new InstallFrontEndSources(install, DecodePng);
            ReplacementFont = Resources.Load<Font>("Fonts/DungeonKeeperII");
        }
        public Font ReplacementFont { get; }
        public string Text(uint id) => _sources.Text(id);
        public BitmapFont Font(FrontEndFont font) => _sources.Font(font);
        public CampaignBriefing Briefing(CampaignSlot slot) => _sources.Briefing(slot);
        public RgbaImage Load(string name) => _sources.Load(name);
        public (int Width, int Height)? ImageSize(string name) => _sources.ImageSize(name);
        public Texture2D Image(string name)
        {
            if (!_textures.TryGetValue(name, out Texture2D texture))
            {
                texture = MinimapFrameVisual.Decode(_sources.Load(name));
                _textures.Add(name, texture);
            }
            return texture;
        }

        /// <summary>A FrontEnd.WAD PNG as top-down RGBA rows (Unity's pixel rows are bottom-up).</summary>
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

        public void Dispose()
        {
            foreach (Texture2D texture in _textures.Values) UnityEngine.Object.Destroy(texture);
            _sources.Dispose();
        }
    }
}
