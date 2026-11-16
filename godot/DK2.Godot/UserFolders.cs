// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.ComponentModel;
using DK2.Data.Model;
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

    /// <summary>The player's profile into <paramref name="profiles"/>; one that cannot be read or created (corrupt,
    /// unwritable, a failed registry read) warns and leaves a fresh profile in memory only, with no file
    /// (<see cref="CampaignProfileStore.Adopt"/>), so the scene still runs. Unity lets the error stop it.</summary>
    public static CampaignProfile LoadProfile(CampaignProfileStore profiles, string path, GameSessionMode mode, FrontEndPage returnPage)
    {
        try { return profiles.Load(path, mode, returnPage); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Win32Exception or UnauthorizedAccessException)
        {
            GD.PushWarning($"DK2.Godot: cannot use the campaign profile {path} ({ex.Message}); using a fresh one that is not saved.");
            return profiles.Adopt(new CampaignProfile());
        }
    }
}
