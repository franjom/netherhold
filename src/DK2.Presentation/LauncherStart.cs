// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>What the first scene does at the start of a process.</summary>
public enum LauncherStart
{
    /// <summary>Show the launcher; Play continues into the game.</summary>
    ShowLauncher,
    /// <summary>Go straight into the game in the saved screen mode ("Start the game directly next time").</summary>
    StartWithSavedScreen,
    /// <summary>Go straight into the game and leave the window as the project opens it: a scripted or headless run.</summary>
    StartAsBefore,
}

/// <summary>What the start decision reads. <see cref="Automated"/> covers every run a script or test makes (no display,
/// Movie Maker, a scene named on the command line, a movie to play), so those behave as they did before the launcher.</summary>
public readonly record struct LauncherStartFacts(bool LauncherRequested, bool Automated, bool ShiftHeld, bool StartDirectly,
    bool InstallFound);

public static class LauncherStartRule
{
    /// <summary>The user argument that always shows the launcher.</summary>
    public const string LauncherArgument = "--launcher";

    /// <summary><c>--launcher</c> shows it whatever else holds; a scripted run starts as before; otherwise Shift held at
    /// start, no saved "start directly" or no install found shows it.</summary>
    public static LauncherStart Decide(LauncherStartFacts facts)
    {
        if (facts.LauncherRequested) return LauncherStart.ShowLauncher;
        if (facts.Automated) return LauncherStart.StartAsBefore;
        if (facts.ShiftHeld || !facts.StartDirectly || !facts.InstallFound) return LauncherStart.ShowLauncher;
        return LauncherStart.StartWithSavedScreen;
    }

    /// <summary>Whether a run is scripted: <paramref name="headless"/>, Movie Maker on, an engine argument naming a scene
    /// (<c>.tscn</c> or <c>.scn</c>), or a <c>--play-movie</c> or <c>--level</c> user argument (an exported game cannot
    /// name a scene, so its main scene starts a <c>--level</c> run in the level's scene).</summary>
    public static bool IsAutomated(IReadOnlyList<string> engineArgs, IReadOnlyList<string> userArgs, bool headless,
        bool movieMaker)
    {
        if (engineArgs is null) throw new ArgumentNullException(nameof(engineArgs));
        if (userArgs is null) throw new ArgumentNullException(nameof(userArgs));
        if (headless || movieMaker) return true;
        foreach (string arg in engineArgs)
            if (!arg.StartsWith("-", StringComparison.Ordinal)
                && (arg.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase) || arg.EndsWith(".scn", StringComparison.OrdinalIgnoreCase)))
                return true;
        foreach (string arg in userArgs)
            if (arg is "--play-movie" or "--level") return true;
        return false;
    }

    public static bool Requested(IReadOnlyList<string> userArgs)
    {
        if (userArgs is null) throw new ArgumentNullException(nameof(userArgs));
        foreach (string arg in userArgs)
            if (arg == LauncherArgument) return true;
        return false;
    }
}
