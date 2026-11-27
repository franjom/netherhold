// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Data.Wad;

namespace DK2.Presentation;

/// <summary>What the front-end menu reads from an install, for every engine adapter: <c>Text.str</c>, the BF4 fonts,
/// the campaign briefings and FrontEnd.WAD's images through the replaceable texture source (<see cref="WadImageSource"/>),
/// each decoded once and kept in memory. Only the PNG decoder is the engine's, passed in.</summary>
public sealed class InstallFrontEndSources : IFrontEndMenuSources, IDisposable
{
    private readonly WadImageSource _images;
    private readonly WadDirectoryIndex _imageIndex;
    private readonly Dictionary<string, RgbaImage> _pixels = new(StringComparer.Ordinal);

    /// <param name="decodePng">Decodes a FrontEnd.WAD PNG to top-down RGBA rows.</param>
    public InstallFrontEndSources(string install, Func<byte[], RgbaImage> decodePng)
    {
        if (install == null)
        {
            throw new ArgumentNullException(nameof(install));
        }

        if (decodePng == null)
        {
            throw new ArgumentNullException(nameof(decodePng));
        }

        Hud = new InstallHudSources(install);
        _images = new WadImageSource(WadArchive.Open(Path.Combine(install, "Data", "FrontEnd.WAD")), decodePng);
        _imageIndex = new WadDirectoryIndex(_images.Entries);
    }

    /// <summary>The shared text and font reader the menu's strings and fonts come from.</summary>
    public InstallHudSources Hud { get; }

    public string Text(uint id)
    {
        return Hud.Text(id);
    }

    public BitmapFont Font(FrontEndFont font)
    {
        return Hud.Font(font.FileName);
    }

    public CampaignBriefing Briefing(CampaignSlot slot)
    {
        return CampaignBriefingLoader.Load(Hud.Install, slot);
    }

    /// <summary>A front-end image's pixels by name (no extension), read once.</summary>
    public RgbaImage Load(string name)
    {
        string path = name + ".png";
        if (!_pixels.TryGetValue(path, out RgbaImage? pixels))
        {
            pixels = _images.Read(_imageIndex.Find(path));
            _pixels.Add(path, pixels);
        }
        return pixels;
    }

    public (int Width, int Height)? ImageSize(string name)
    {
        try { RgbaImage image = Load(name); return (image.Width, image.Height); }
        catch (FileNotFoundException) { return null; }
    }

    public void Dispose()
    {
        _images.Dispose();
        Hud.Dispose();
    }
}
