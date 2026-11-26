// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>What the front-end menu reads from the install: the shared <see cref="InstallFrontEndSources"/> with Godot's
/// PNG decoder, plus the replacement TrueType face.</summary>
public sealed class ClassicFrontEndMenuAssets : IFrontEndMenuSources, IDisposable
{
    private readonly InstallFrontEndSources _sources;
    private FontFile? _replacementFont;
    private bool _replacementSearched;

    public ClassicFrontEndMenuAssets(string install) => _sources = new InstallFrontEndSources(install, DecodePng);

    /// <summary>Bullfrog's DungeonKeeperII TrueType face, or null when this machine has none. It is not ours to ship, so
    /// it is never committed or imported: it is read from an absolute path, in order DK2_REPLACEMENT_FONT, <c>--menu-font</c>,
    /// the launcher's menu font (<see cref="MenuFontSearch"/>), then <c>Fonts/DungeonKeeperII.ttf</c> in this project
    /// (gitignored). The subtitles use it too (<see cref="LoadReplacementFont"/>).</summary>
    public FontFile? ReplacementFont
    {
        get
        {
            if (_replacementSearched) return _replacementFont;
            _replacementSearched = true;
            return _replacementFont = LoadReplacementFont();
        }
    }

    /// <summary>The <see cref="ReplacementFont"/> search and load, for a scene without the menu's assets (the in-game
    /// subtitles); the caller owns the face.</summary>
    internal static FontFile? LoadReplacementFont()
    {
        string? path = FindReplacementFont();
        if (path == null) return null;
        var font = new FontFile();
        if (font.LoadDynamicFont(path) != Error.Ok) { font.Dispose(); GD.PushWarning("DK2.Godot: cannot read font " + path); return null; }
        GD.Print("DK2.Godot: replacement font ", path);
        return font;
    }

    private static string? FindReplacementFont()
    {
        string[] args = OS.GetCmdlineUserArgs();
        foreach ((string source, string path) in MenuFontSearch.Named(
                     System.Environment.GetEnvironmentVariable(MenuFontSearch.EnvironmentVariable),
                     LaunchArguments.Value(args, MenuFontSearch.Argument), LauncherSettingsStore.Current(args).MenuFont))
        {
            if (File.Exists(path)) return path;
            GD.PushWarning($"DK2.Godot: {source} names a missing file: {path}");
        }
        string project = ProjectSettings.GlobalizePath("res://");
        if (string.IsNullOrEmpty(project)) return null;
        string local = System.IO.Path.Combine(project, "Fonts", "DungeonKeeperII.ttf");
        return File.Exists(local) ? local : null;
    }

    public string Text(uint id) => _sources.Text(id);

    public BitmapFont Font(FrontEndFont font) => _sources.Font(font);
    internal BitmapFont LegibleFont(int height) => _sources.Hud.Font(InterfaceFonts.LegibleFileName(height));

    public CampaignBriefing Briefing(CampaignSlot slot) => _sources.Briefing(slot);

    /// <summary>A front-end image's pixels by name; the menu's <see cref="HudCanvas"/> reads its art through this.</summary>
    public RgbaImage Load(string name) => _sources.Load(name);

    public (int Width, int Height)? ImageSize(string name) => _sources.ImageSize(name);

    /// <summary>A DK2 PNG's stored bytes as RGBA, its colour-space chunks dropped (Godot's decoder would apply them).</summary>
    internal static RgbaImage DecodePng(byte[] bytes)
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
