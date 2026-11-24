// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;

namespace DK2.Presentation;

/// <summary>How a launcher line reads: plain, ready (the colour of a found install) or a problem.</summary>
public enum LauncherTone
{
    Plain,
    Ready,
    Problem,
}

public readonly record struct LauncherMessage(string Text, LauncherTone Tone);

/// <summary>What a menu-font field names.</summary>
public enum LauncherFontCheck
{
    Empty,
    Missing,
    NotAFont,
    Found,
}

/// <summary>The launcher's words, and the checks behind its status lines. Sentence case throughout.</summary>
public static class LauncherCopy
{
    public const string Title = "Netherhold";
    public const string Intro = "A remake of Dungeon Keeper 2. It plays from the files of your own copy of the game.";
    public const string InstallHeading = "Where is Dungeon Keeper 2 installed?";
    public const string FontHeading = "Menu font (optional)";
    public const string FontPlaceholder = "Built-in font";
    public const string ScreenHeading = "Screen";
    public const string Window = "Window";
    public const string FullScreen = "Full screen";
    public const string FullScreenWindow = "Full screen window";
    public const string StartDirectly = "Start the game directly next time";
    public const string ShiftHint = "Hold Shift while starting to see this screen again.";
    public const string Browse = "Browse…";
    public const string Quit = "Quit";
    public const string Play = "Play";
    public const string ChooseInstall = "Choose the Dungeon Keeper 2 folder";
    public const string ChooseFont = "Choose a menu font";

    public static LauncherMessage Install(Dk2InstallCheck check) => check switch
    {
        Dk2InstallCheck.Found => new("Found Dungeon Keeper 2.", LauncherTone.Ready),
        Dk2InstallCheck.NoData => new("This folder has DKII.EXE but no Data folder. Choose the folder the game was installed to.",
            LauncherTone.Problem),
        Dk2InstallCheck.NoGame => new("This folder has no DKII.EXE. Choose the folder the game was installed to.",
            LauncherTone.Problem),
        _ => new("Choose the folder that holds DKII.EXE and the Data folder.", LauncherTone.Plain),
    };

    /// <summary>Without a face the front end draws its labels, and both scenes their subtitles, in DK2's own bitmap font.</summary>
    public static LauncherMessage Font(LauncherFontCheck check) => check switch
    {
        LauncherFontCheck.Found => new("The menu's labels and the subtitles will use this font.", LauncherTone.Ready),
        LauncherFontCheck.Missing => new("There is no file at this path.", LauncherTone.Problem),
        LauncherFontCheck.NotAFont => new("Choose a TrueType or OpenType font (.ttf or .otf).", LauncherTone.Problem),
        _ => new("Leave this empty and the menu and the subtitles use the game's own lettering.", LauncherTone.Plain),
    };

    public static LauncherFontCheck CheckFont(string? path, Func<string, bool> fileExists)
    {
        if (fileExists is null) throw new ArgumentNullException(nameof(fileExists));
        if (string.IsNullOrWhiteSpace(path)) return LauncherFontCheck.Empty;
        string extension = Path.GetExtension(path!.Trim()).ToLowerInvariant();
        if (extension != ".ttf" && extension != ".otf") return LauncherFontCheck.NotAFont;
        return fileExists(path.Trim()) ? LauncherFontCheck.Found : LauncherFontCheck.Missing;
    }

    /// <summary>A typed or pasted path without surrounding blanks and quotes ("Copy as path" quotes it); null when empty.</summary>
    public static string? CleanPath(string? text)
    {
        if (text is null) return null;
        string path = text.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"') path = path.Substring(1, path.Length - 2).Trim();
        return path.Length == 0 ? null : path;
    }
}

/// <summary>Where the front end's replacement TrueType face is looked for, before the adapter's own development copies:
/// the environment variable, then the command line, then the launcher's saved choice.</summary>
public static class MenuFontSearch
{
    public const string EnvironmentVariable = "DK2_REPLACEMENT_FONT";
    public const string Argument = "--menu-font";

    /// <summary>The named paths in order, each with what named it, for the adapter to try and report.</summary>
    public static IReadOnlyList<(string Source, string Path)> Named(string? environment, string? commandLine, string? saved)
    {
        var named = new List<(string, string)>(3);
        if (!string.IsNullOrWhiteSpace(environment)) named.Add((EnvironmentVariable, environment!));
        if (!string.IsNullOrWhiteSpace(commandLine)) named.Add((Argument, commandLine!));
        if (!string.IsNullOrWhiteSpace(saved)) named.Add(("the launcher's menu font", saved!));
        return named;
    }
}
