// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The launcher's saved settings for this process (<see cref="LauncherSettingsFile"/>: <c>user://launcher.cfg</c>
/// or <c>--launcher-config</c>), read once, and what the scenes take from them: the install
/// (<see cref="Dk2Install.Find(string?, string?)"/> with <c>--dk2Install</c>) and the start decision.</summary>
public static class LauncherSettingsStore
{
    private static LauncherSettings? s_settings;

    public static string PathFor(IReadOnlyList<string> args) =>
        LauncherSettingsFile.PathFor(args, ProjectSettings.GlobalizePath("user://"));

    public static LauncherSettings Current(IReadOnlyList<string> args)
    {
        if (s_settings != null)
        {
            return s_settings;
        }

        string path = PathFor(args);
        try { s_settings = LauncherSettingsFile.Load(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"DK2.Godot: cannot read the launcher settings {path} ({ex.Message}); using the defaults.");
            s_settings = LauncherSettings.Default;
        }
        return s_settings;
    }

    /// <summary>Saves <paramref name="settings"/> and makes them this process's; false (warned) when the file cannot be written.</summary>
    public static bool Save(IReadOnlyList<string> args, LauncherSettings settings)
    {
        s_settings = settings;
        string path = PathFor(args);
        try { LauncherSettingsFile.Save(path, settings); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"DK2.Godot: cannot save the launcher settings {path} ({ex.Message}).");
            return false;
        }
    }

    /// <summary>The install: DK2_DIR, then <c>--dk2Install</c>, then the launcher's folder, then the usual locations.</summary>
    public static string? FindInstall(IReadOnlyList<string> args) =>
        Dk2Install.Find(LaunchArguments.Value(args, "--dk2Install"), Current(args).InstallFolder);

    /// <summary>The folder this run named when <see cref="FindInstall"/> found none: DK2_DIR, then <c>--dk2Install</c>,
    /// either of which decides alone; null when the run named neither.</summary>
    public static string? NamedInstall(IReadOnlyList<string> args)
    {
        string? environment = System.Environment.GetEnvironmentVariable("DK2_DIR");
        if (!string.IsNullOrWhiteSpace(environment))
        {
            return environment;
        }

        string? named = LaunchArguments.Value(args, "--dk2Install");
        return string.IsNullOrWhiteSpace(named) ? null : named;
    }

    /// <summary>What the first scene does (<see cref="LauncherStartRule"/>): scripted runs (no display, Movie Maker, a
    /// scene named, <c>--play-movie</c>) start as they always have.</summary>
    public static LauncherStart Decide(IReadOnlyList<string> args)
    {
        bool automated = LauncherStartRule.IsAutomated(OS.GetCmdlineArgs(), args, DisplayServer.GetName() == "headless",
            !string.IsNullOrEmpty(Engine.GetWriteMoviePath()));
        return LauncherStartRule.Decide(new LauncherStartFacts(LauncherStartRule.Requested(args), automated,
            !automated && LauncherSystem.ShiftHeld(), Current(args).StartDirectly, FindInstall(args) != null));
    }
}
