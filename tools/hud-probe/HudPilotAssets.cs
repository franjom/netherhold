// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Data.Wad;

namespace DK2.Unity.HudProbe
{
    public interface IHudPilotAssets
    {
        byte[] PalettePng { get; }
        byte[] BackdropPng { get; }
        BitmapFont ReferenceFont { get; }
        BitmapFont LargestFont { get; }
    }

    public sealed class InstallHudPilotAssets : IHudPilotAssets
    {
        public byte[] PalettePng { get; }
        public byte[] BackdropPng { get; }
        public BitmapFont ReferenceFont { get; }
        public BitmapFont LargestFont { get; }

        public InstallHudPilotAssets(string install)
        {
            if (string.IsNullOrWhiteSpace(install)) throw new ArgumentException("DK2 install required.");
            using (var archive = WadArchive.Open(Path.Combine(install, "Data", "EngineTextures.WAD")))
            {
                PalettePng = archive.Extract("GUI\\Map\\MapColours.png");
                BackdropPng = archive.Extract("Map-BG.PNG");
            }
            string text = Path.Combine(install, "Data", "Text", "Default");
            ReferenceFont = Bf4FontReader.Open(Path.Combine(text, "FONT_INGAME14.bf4"));
            LargestFont = Bf4FontReader.Open(Path.Combine(text, "FONT_INGAME72.bf4"));
        }
    }
}
