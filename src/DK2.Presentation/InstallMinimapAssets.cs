// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Wad;

namespace DK2.Presentation;

public interface IMinimapAssets
{
    byte[] PalettePng { get; }
    byte[] BackdropPng { get; }
    RgbaImage Frame { get; }
    RgbaImage CameraFootprint { get; }
    RgbaImage North { get; }
    RgbaImage Corner(MinimapCorner corner, bool active);
}

public sealed class InstallMinimapAssets : IMinimapAssets
{
    public byte[] PalettePng { get; }
    public byte[] BackdropPng { get; }
    public RgbaImage Frame { get; }
    public RgbaImage CameraFootprint { get; }
    public RgbaImage North { get; }
    private readonly RgbaImage[] _corners = new RgbaImage[8];
    private const string PaletteEntry = "GUI\\Map\\MapColours.png";

    /// <summary>Only the palette strip's PNG (<see cref="PalettePng"/>), for a host that draws no minimap art.</summary>
    public static byte[] PalettePngOf(string install)
    {
        using var archive = WadArchive.Open(Path.Combine(install, "Data", "EngineTextures.WAD"));
        return archive.Extract(PaletteEntry);
    }

    /// <summary>The minimap's art from the install. <paramref name="loadTexture"/>, when given,
    /// loads the engine textures from an archive already open (the art provider's); without it
    /// the engine-texture archive is opened here, which reads a header per texture.</summary>
    public InstallMinimapAssets(string install, Func<string, RgbaImage>? loadTexture = null)
    {
        using (var archive = WadArchive.Open(Path.Combine(install, "Data", "EngineTextures.WAD")))
        {
            PalettePng = archive.Extract(PaletteEntry);
            BackdropPng = archive.Extract("Map-BG.PNG");
        }
        EngineTextureArchive? opened = loadTexture == null ? EngineTextureArchive.OpenInstall(install) : null;
        try
        {
            Func<string, RgbaImage> load = loadTexture ?? opened!.Decode;
            Frame = load(MinimapHud.FrameArt);
            CameraFootprint = load(MinimapHud.FootprintArt);
            North = load(MinimapHud.NorthArt);
            for (int i = 0; i < 4; i++)
                for (int active = 0; active < 2; active++)
                    _corners[i * 2 + active] = load(MinimapHud.CornerArt((MinimapCorner)i, active != 0));
        }
        finally
        {
            opened?.Dispose();
        }
    }
    public RgbaImage Corner(MinimapCorner corner, bool active)
    {
        if ((uint)corner > 3) throw new ArgumentOutOfRangeException(nameof(corner));
        return _corners[(int)corner * 2 + (active ? 1 : 0)];
    }
}
