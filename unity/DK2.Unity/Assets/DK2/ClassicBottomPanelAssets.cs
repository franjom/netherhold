// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Presentation;

namespace DK2.Unity
{
    /// <summary>The install's HUD art; <see cref="IBottomPanelArt"/> is the part the engine-free panel reads.</summary>
    public interface IClassicBottomPanelAssets : IBottomPanelArt
    {
        RgbaImage Image(string name);
        string Name(BottomPanelChoiceKey key, string fallback);
        int Order(BottomPanelChoiceKey key);
        BitmapFont Font(int screenHeight);
    }

    /// <summary>Native interface assets remain local to the install; the reading is the shared
    /// <see cref="InstallBottomPanelArt"/>, which the Godot adapter uses too.</summary>
    public sealed class InstallClassicBottomPanelAssets : IClassicBottomPanelAssets, IDisposable
    {
        private readonly InstallBottomPanelArt _art;

        /// <summary>From the install alone, owning the texture archive it opens.</summary>
        public InstallClassicBottomPanelAssets(string install, GameDatabase database)
        {
            _art = new InstallBottomPanelArt(install, database);
        }

        /// <summary>Over shared sources, which the caller disposes.</summary>
        public InstallClassicBottomPanelAssets(InstallHudSources sources, GameDatabase database)
        {
            _art = new InstallBottomPanelArt(sources, database);
        }

        public string Icon(BottomPanelChoiceKey key) => _art.Icon(key);
        public string UpgradedIcon(BottomPanelChoiceKey key) => _art.UpgradedIcon(key);
        public int Order(BottomPanelChoiceKey key) => _art.Order(key);
        public string Name(BottomPanelChoiceKey key, string fallback) => _art.Name(key, fallback);
        public string Text(uint id) => _art.Text(id);
        public RgbaImage Image(string name) => _art.Image(name);
        public BitmapFont Font(int screenHeight) => _art.Font(screenHeight);

        public void Dispose() => _art.Dispose();
    }
}
