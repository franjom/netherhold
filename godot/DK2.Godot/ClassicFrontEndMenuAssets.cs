// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>What the front-end menu reads from the install: the shared <see cref="InstallFrontEndSources"/> (as Unity's
/// <c>InstallClassicFrontEndMenuAssets</c> uses it) with Godot's PNG decoder, plus the replacement TrueType face.</summary>
public sealed class ClassicFrontEndMenuAssets : IFrontEndMenuSources, IDisposable
{
    private readonly InstallFrontEndSources _sources;
    private FontFile? _replacementFont;
    private bool _replacementSearched;

    public ClassicFrontEndMenuAssets(string install) => _sources = new InstallFrontEndSources(install, DecodePng);

    /// <summary>Bullfrog's DungeonKeeperII TrueType face, or null when this machine has none. It is not ours to ship, so
    /// it is never committed or imported: it is read from an absolute path, in order DK2_REPLACEMENT_FONT, then
    /// <c>Fonts/DungeonKeeperII.ttf</c> in this project (gitignored), then the Unity project's (gitignored) copy.</summary>
    public FontFile? ReplacementFont
    {
        get
        {
            if (_replacementSearched) return _replacementFont;
            _replacementSearched = true;
            string? path = FindReplacementFont();
            if (path == null) return null;
            var font = new FontFile();
            if (font.LoadDynamicFont(path) != Error.Ok) { font.Dispose(); GD.PushWarning("DK2.Godot: cannot read font " + path); return null; }
            GD.Print("DK2.Godot: replacement font ", path);
            return _replacementFont = font;
        }
    }

    private static string? FindReplacementFont()
    {
        string? configured = System.Environment.GetEnvironmentVariable("DK2_REPLACEMENT_FONT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured)) return configured;
            GD.PushWarning("DK2.Godot: DK2_REPLACEMENT_FONT names a missing file: " + configured);
        }
        string project = ProjectSettings.GlobalizePath("res://");
        if (string.IsNullOrEmpty(project)) return null;
        string local = System.IO.Path.Combine(project, "Fonts", "DungeonKeeperII.ttf");
        if (File.Exists(local)) return local;
        string unity = System.IO.Path.GetFullPath(System.IO.Path.Combine(project, "..", "..",
            "unity", "DK2.Unity", "Assets", "Resources", "Fonts", "DungeonKeeperII.ttf"));
        return File.Exists(unity) ? unity : null;
    }

    public string Text(uint id) => _sources.Text(id);

    public BitmapFont Font(FrontEndFont font) => _sources.Font(font);

    /// <summary>A front-end image's pixels by name; the menu's <see cref="HudCanvas"/> reads its art through this.</summary>
    public RgbaImage Load(string name) => _sources.Load(name);

    public (int Width, int Height)? ImageSize(string name) => _sources.ImageSize(name);

    private static RgbaImage DecodePng(byte[] bytes)
    {
        using var image = new Image();
        // Stored bytes, as Unity's LoadImage reads them: most FrontEnd.WAD PNGs declare a gamma Godot would correct.
        if (image.LoadPngFromBuffer(PngChunks.WithoutColourSpace(bytes)) != Error.Ok) throw new InvalidDataException("Invalid front-end PNG.");
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        return new RgbaImage(image.GetWidth(), image.GetHeight(), image.GetData());
    }

    public void Dispose()
    {
        _sources.Dispose();
        _replacementFont?.Dispose(); _replacementFont = null;
    }
}
