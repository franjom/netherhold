// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Godot's <see cref="GameFolders"/>: rooted at <c>user://</c> (%APPDATA%/Godot/app_userdata/DK2.Godot), never
/// Unity's persistentDataPath. The user arguments <c>--profile</c>, <c>--settings</c>, <c>--saves</c> and
/// <c>--hiscores</c> name a file or directory instead, so a scripted run need not touch the player's own files.</summary>
public static class UserFolders
{
    public static GameFolders FromArguments(IReadOnlyList<string> args) => new(ProjectSettings.GlobalizePath("user://"))
    {
        ProfileFile = LaunchArguments.Value(args, "--profile"), SettingsFile = LaunchArguments.Value(args, "--settings"),
        SavesDirectory = LaunchArguments.Value(args, "--saves"), HiScoresFile = LaunchArguments.Value(args, "--hiscores"),
    };
}
