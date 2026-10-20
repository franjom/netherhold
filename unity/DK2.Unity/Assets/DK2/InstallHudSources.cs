// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using DK2.Art;
using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Data.Text;

namespace DK2.Unity
{
    /// <summary>The install files the HUD reads, opened once and shared by its panels: engine textures
    /// (through the art provider's cache when there is one), the default <c>Text.str</c>, the
    /// interface fonts and the KWD art records. Each used to open its own copies, about 30 ms apiece.</summary>
    public sealed class InstallHudSources : IDisposable
    {
        private readonly InstallArtProvider _art;
        private readonly Dictionary<string, BitmapFont> _fonts = new Dictionary<string, BitmapFont>();
        private EngineTextureArchive _opened;
        private Dk2ArtCatalogue _catalogue;
        private Dk2StringTable _strings;

        /// <summary>Over the install; with <paramref name="art"/>, its textures and art records are the
        /// provider's. Without it the engine-texture archive is opened on the first texture.</summary>
        public InstallHudSources(string install, InstallArtProvider art = null)
        {
            Install = install ?? throw new ArgumentNullException(nameof(install));
            TextDirectory = Path.Combine(install, "Data", "Text", "Default");
            _art = art;
            _catalogue = art?.Catalogue;
        }

        public string Install { get; }
        public string TextDirectory { get; }

        /// <summary>The default <c>Text.str</c>, read on first use.</summary>
        public Dk2StringTable Strings => _strings ??= Dk2StringTable.Open(Path.Combine(TextDirectory, "Text.str"));

        /// <summary>The install's KWD art records, the art provider's when it read them.</summary>
        public Dk2ArtCatalogue Catalogue => _catalogue ??= Dk2ArtCatalogue.Load(Install);

        public RgbaImage Texture(string name) =>
            _art != null ? _art.LoadTexture(name) : (_opened ??= EngineTextureArchive.OpenInstall(Install)).Decode(name);

        /// <summary>A BF4 font of the default text directory, read once.</summary>
        public BitmapFont Font(string file)
        {
            if (!_fonts.TryGetValue(file, out BitmapFont font))
            {
                font = Bf4FontReader.Open(Path.Combine(TextDirectory, file));
                _fonts.Add(file, font);
            }
            return font;
        }

        public void Dispose()
        {
            _opened?.Dispose();
            _opened = null;
        }
    }
}
