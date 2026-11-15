// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Presentation;
namespace DK2.Unity
{
    public interface ICreatureOverviewAssets
    {
        RgbaImage Image(string path);
        string Portrait(CreatureOverviewType type);
        string Name(CreatureOverviewType type,string fallback);
        int Order(CreatureOverviewType type);
    }
    /// <summary>The reading is the shared <see cref="InstallCreatureOverviewArt"/>, which the Godot adapter uses too.</summary>
    public sealed class InstallCreatureOverviewAssets : ICreatureOverviewAssets
    {
        private readonly InstallCreatureOverviewArt _art;
        public InstallCreatureOverviewAssets(string install,GameDatabase database,IClassicBottomPanelAssets shared)
        {
            _art=new InstallCreatureOverviewArt(install,database,shared.Image,shared.Text);
        }
        public RgbaImage Image(string path)=>_art.Image(path);
        public string Portrait(CreatureOverviewType type)=>_art.Portrait(type);
        public string Name(CreatureOverviewType type,string fallback)=>_art.Name(type,fallback);
        public int Order(CreatureOverviewType type)=>_art.Order(type);
    }
}
