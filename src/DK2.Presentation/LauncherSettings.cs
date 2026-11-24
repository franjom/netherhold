// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Text;

namespace DK2.Presentation;

/// <summary>How the game's window is shown: a window of a chosen size, exclusive full screen, or a borderless window over
/// the whole screen.</summary>
public enum LauncherScreenMode
{
    Window,
    FullScreen,
    FullScreenWindow,
}

/// <summary>A window's client size in screen pixels.</summary>
public readonly record struct LauncherWindowSize(int Width, int Height)
{
    public const int MinimumSide = 320, MaximumSide = 16384;

    /// <summary>Exactly 16:9.</summary>
    public bool IsWide => Width * 9 == Height * 16;

    /// <summary>The dropdown's text, e.g. "1600 × 900".</summary>
    public override string ToString() =>
        Width.ToString(CultureInfo.InvariantCulture) + " × " + Height.ToString(CultureInfo.InvariantCulture);
}

/// <summary>What the launcher saves (the tester's install, the menu font, the screen and whether to start straight into
/// the game), and its text form: <c>key=value</c> lines, <c>#</c> comments, unknown keys and malformed values ignored
/// so an old or hand-edited file still loads.</summary>
public sealed record LauncherSettings
{
    public const string FileName = "launcher.cfg";

    /// <summary>The Dungeon Keeper 2 folder the tester chose, or null.</summary>
    public string? InstallFolder { get; init; }
    /// <summary>A TrueType file for the front end's labels, or null for the built-in behaviour.</summary>
    public string? MenuFont { get; init; }
    public LauncherScreenMode Screen { get; init; } = LauncherScreenMode.Window;
    /// <summary>The window's size in <see cref="LauncherScreenMode.Window"/>; null picks one for the screen
    /// (<see cref="LauncherWindowSizes.Default"/>).</summary>
    public LauncherWindowSize? WindowSize { get; init; }
    public bool StartDirectly { get; init; }

    public static LauncherSettings Default { get; } = new();

    public static LauncherSettings Parse(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        LauncherSettings settings = Default;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            int equals = line.IndexOf('=');
            if (line.Length == 0 || line[0] == '#' || equals <= 0) continue;
            string key = line.Substring(0, equals).Trim().ToLowerInvariant();
            string value = line.Substring(equals + 1).Trim();
            settings = key switch
            {
                "install" => settings with { InstallFolder = Blank(value) },
                "menu_font" => settings with { MenuFont = Blank(value) },
                "screen" => ParseScreen(value) is LauncherScreenMode mode ? settings with { Screen = mode } : settings,
                "window" => ParseSize(value) is LauncherWindowSize size ? settings with { WindowSize = size } : settings,
                "start_directly" => ParseBool(value) is bool on ? settings with { StartDirectly = on } : settings,
                _ => settings,
            };
        }
        return settings;
    }

    public string Format()
    {
        var text = new StringBuilder();
        text.Append("# Netherhold launcher settings. Hold Shift while starting to see the launcher again.\n");
        text.Append("install=").Append(InstallFolder ?? "").Append('\n');
        text.Append("menu_font=").Append(MenuFont ?? "").Append('\n');
        text.Append("screen=").Append(ScreenName(Screen)).Append('\n');
        if (WindowSize is LauncherWindowSize size)
            text.Append("window=").Append(size.Width.ToString(CultureInfo.InvariantCulture)).Append('x')
                .Append(size.Height.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("start_directly=").Append(StartDirectly ? "true" : "false").Append('\n');
        return text.ToString();
    }

    private static string? Blank(string value) => value.Length == 0 ? null : value;

    private static string ScreenName(LauncherScreenMode mode) => mode switch
    {
        LauncherScreenMode.FullScreen => "full_screen",
        LauncherScreenMode.FullScreenWindow => "full_screen_window",
        _ => "window",
    };

    private static LauncherScreenMode? ParseScreen(string value) => value.ToLowerInvariant() switch
    {
        "window" => LauncherScreenMode.Window,
        "full_screen" => LauncherScreenMode.FullScreen,
        "full_screen_window" => LauncherScreenMode.FullScreenWindow,
        _ => null,
    };

    private static bool? ParseBool(string value) => value.ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private static LauncherWindowSize? ParseSize(string value)
    {
        int x = value.IndexOf('x');
        if (x <= 0) return null;
        return Side(value.Substring(0, x)) is int width && Side(value.Substring(x + 1)) is int height
            ? new LauncherWindowSize(width, height) : null;
    }

    private static int? Side(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int side)
        && side >= LauncherWindowSize.MinimumSide && side <= LauncherWindowSize.MaximumSide ? side : null;
}

/// <summary>Where the launcher's settings live and how they are read and written: <c>launcher.cfg</c> in the engine's
/// user folder, or the file a <c>--launcher-config</c> argument names, so a scripted run never touches the tester's.</summary>
public static class LauncherSettingsFile
{
    public const string Argument = "--launcher-config";

    public static string PathFor(IReadOnlyList<string> args, string userRoot)
    {
        if (userRoot is null) throw new ArgumentNullException(nameof(userRoot));
        string? named = LaunchArguments.Value(args, Argument);
        return string.IsNullOrWhiteSpace(named) ? Path.Combine(userRoot, LauncherSettings.FileName) : named!;
    }

    /// <summary>The saved settings; a missing file is <see cref="LauncherSettings.Default"/>. An unreadable one throws
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> for the caller to report.</summary>
    public static LauncherSettings Load(string path) =>
        File.Exists(path) ? LauncherSettings.Parse(File.ReadAllText(path, Encoding.UTF8)) : LauncherSettings.Default;

    public static void Save(string path, LauncherSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(path, settings.Format(), new UTF8Encoding(false));
    }
}
