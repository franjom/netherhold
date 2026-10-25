// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DK2.Data.Model;
using DK2.Art.Textures;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    public interface IClassicFrontEndMenuAssets : IDisposable
    {
        string Text(uint id);
        BitmapFont Font(FrontEndFont font);
        Font ReplacementFont { get; }
        Texture2D Image(string name);
    }

    public sealed class InstallClassicFrontEndMenuAssets : IClassicFrontEndMenuAssets
    {
        private readonly InstallHudSources _text;
        private readonly ClassicFrontEndAssets _images;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        public InstallClassicFrontEndMenuAssets(string install)
        {
            _text = new InstallHudSources(install);
            _images = new ClassicFrontEndAssets(install);
            ReplacementFont = Resources.Load<Font>("Fonts/DungeonKeeperII");
        }
        public Font ReplacementFont { get; }
        public string Text(uint id) => _text.Strings.TryResolve(id, out string text) ? text : string.Empty;
        public BitmapFont Font(FrontEndFont font) => _text.Font(font.FileName);
        public Texture2D Image(string name)
        {
            if (!_textures.TryGetValue(name, out Texture2D texture))
            {
                var source = (WadImageSource)_images.Images;
                // Root menu art precedes the first directory run; later bare names include Buttons' duplicates.
                int entry = Enumerable.Range(0, source.Entries.Count)
                    .TakeWhile(i => source.Entries[i].Name.IndexOf('\\') < 0 && source.Entries[i].Name.IndexOf('/') < 0)
                    .First(i => string.Equals(Path.GetFileNameWithoutExtension(source.Entries[i].Name), name,
                        StringComparison.OrdinalIgnoreCase));
                texture = _images.LoadTexture(entry);
                _textures.Add(name, texture);
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
