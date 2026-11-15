// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Data.Wad;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>What the front-end menu reads from the install, mirroring Unity's <c>InstallClassicFrontEndMenuAssets</c>:
/// the default Text.str, the BF4 fonts, and FrontEnd.WAD's images through the replaceable texture source
/// (<see cref="WadImageSource"/>), decoded once and kept in memory. Only the PNG decoder is Godot's.</summary>
public sealed class ClassicFrontEndMenuAssets : IFrontEndMenuSources, IDisposable
{
    private readonly string _textDirectory;
    private readonly WadImageSource _images;
    private readonly WadDirectoryIndex _imageIndex;
    private readonly Dictionary<string, RgbaImage> _pixels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BitmapFont> _fonts = new(StringComparer.Ordinal);
    private Dk2StringTable? _strings;
    private FontFile? _replacementFont;
    private bool _replacementSearched;

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

    public ClassicFrontEndMenuAssets(string install)
    {
        ArgumentNullException.ThrowIfNull(install);
        _textDirectory = System.IO.Path.Combine(install, "Data", "Text", "Default");
        _images = new WadImageSource(WadArchive.Open(System.IO.Path.Combine(install, "Data", "FrontEnd.WAD")), DecodePng);
        _imageIndex = new WadDirectoryIndex(_images.Entries);
    }

    private Dk2StringTable Strings => _strings ??= Dk2StringTable.Open(System.IO.Path.Combine(_textDirectory, "Text.str"));

    public string Text(uint id) => Strings.TryResolve(id, out string text) ? text : string.Empty;

    public BitmapFont Font(FrontEndFont font)
    {
        string file = font.FileName;
        if (!_fonts.TryGetValue(file, out BitmapFont? loaded))
        {
            loaded = Bf4FontReader.Open(System.IO.Path.Combine(_textDirectory, file));
            _fonts.Add(file, loaded);
        }
        return loaded;
    }

    /// <summary>A front-end image's pixels by name; the menu's <see cref="HudCanvas"/> reads its art through this.</summary>
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

    private static RgbaImage DecodePng(byte[] bytes)
    {
        using var image = new Image();
        if (image.LoadPngFromBuffer(bytes) != Error.Ok) throw new InvalidDataException("Invalid front-end PNG.");
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        return new RgbaImage(image.GetWidth(), image.GetHeight(), image.GetData());
    }

    public void Dispose()
    {
        _images.Dispose();
        _replacementFont?.Dispose(); _replacementFont = null;
    }
}
