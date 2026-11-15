// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Data.Model;
using DK2.Data.Campaign;
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

    public sealed class InstallClassicFrontEndMenuAssets : IClassicFrontEndMenuAssets
    {
        private readonly InstallHudSources _text;
        private readonly ClassicFrontEndAssets _images;
        private readonly WadDirectoryIndex _imageIndex;
        private readonly Dictionary<string, RgbaImage> _pixels = new Dictionary<string, RgbaImage>();
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        public InstallClassicFrontEndMenuAssets(string install)
        {
            _text = new InstallHudSources(install);
            _images = new ClassicFrontEndAssets(install);
            _imageIndex = new WadDirectoryIndex(((WadImageSource)_images.Images).Entries);
            ReplacementFont = Resources.Load<Font>("Fonts/DungeonKeeperII");
        }
        public Font ReplacementFont { get; }
        public string Text(uint id) => _text.Strings.TryResolve(id, out string text) ? text : string.Empty;
        public BitmapFont Font(FrontEndFont font) => _text.Font(font.FileName);
        public CampaignBriefing Briefing(CampaignSlot slot) => CampaignBriefingLoader.Load(_text.Install, slot);
        public RgbaImage Load(string name) => LoadPixels(name + ".png");
        public (int Width, int Height)? ImageSize(string name)
        {
            try { RgbaImage image = Load(name); return (image.Width, image.Height); }
            catch (System.IO.FileNotFoundException) { return null; }
        }
        public Texture2D Image(string name) => LoadImage(name + ".png");
        private RgbaImage LoadPixels(string path)
        {
            if (!_pixels.TryGetValue(path, out RgbaImage pixels))
            {
                pixels = _images.Load(_imageIndex.Find(path));
                _pixels.Add(path, pixels);
            }
            return pixels;
        }
        private Texture2D LoadImage(string path)
        {
            if (!_textures.TryGetValue(path, out Texture2D texture))
            {
                texture = MinimapFrameVisual.Decode(LoadPixels(path));
                _textures.Add(path, texture);
            }
            return texture;
        }
        public void Dispose()
        {
            foreach (Texture2D texture in _textures.Values) UnityEngine.Object.Destroy(texture);
            _images.Dispose(); _text.Dispose();
        }
    }
}
