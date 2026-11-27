// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Data;

namespace DK2.Presentation;

/// <summary>What stopped the game before or while it ran, each with its own message screen.</summary>
public enum GameFailureKind
{
    /// <summary>No Dungeon Keeper 2 install was found, so nothing of the game can be read.</summary>
    NoInstall,
    /// <summary>A level, a loaded checkpoint or a restart failed while it loaded.</summary>
    LevelDidNotStart,
    /// <summary>A simulation turn threw; the game stops on the last frame drawn.</summary>
    TurnFailed,
    /// <summary>An original DK2 save could not be read or imported.</summary>
    SaveImportFailed,
}

/// <summary>What a failure screen's choice does.</summary>
public enum GameFailureAction
{
    /// <summary>Shows the launcher, where the install folder is chosen.</summary>
    OpenLauncher,
    /// <summary>Ends the session and opens the front end's main page.</summary>
    ReturnToMenu,
    /// <summary>Quits with <see cref="GameFailureCopy.ExitCode"/>.</summary>
    Quit,
}

/// <summary>The keys a failure screen answers: Enter takes the first choice, Esc quits, as on the launcher.</summary>
public enum GameFailureKey
{
    Confirm,
    Cancel,
}

/// <summary>One failure: its kind, the error's own message, and what the screen names (the level, the turn, the file
/// or folder given).</summary>
public readonly record struct GameFailure(GameFailureKind Kind, string? Message = null, string? Level = null,
    long? Turn = null, string? Path = null);

/// <summary>A choice on the screen: what it does, its label and the key that takes it.</summary>
public readonly record struct GameFailureChoice(GameFailureAction Action, string Label, string Key);

/// <summary>The screen's words, top to bottom, and its choices; the first choice is the default.</summary>
public sealed record GameFailureScreen(string Title, string Summary, string? Detail, string LogLine, string? LogFile,
    IReadOnlyList<GameFailureChoice> Choices);

/// <summary>The failure screens' words and choices, shared by every adapter. Plain words, sentence case, as the launcher's
/// (<see cref="LauncherCopy"/>); the screen uses no DK2 art or lettering, since the failure may be a missing install.</summary>
public static class GameFailureCopy
{
    /// <summary>The process exit code of a Quit from a failure screen, and of a headless run that fails.</summary>
    public const int ExitCode = 1;

    public const string ChooseFolder = "Choose the folder";
    public const string BackToMenu = "Back to the menu";
    public const string Quit = "Quit";
    public const string EnterKey = "Enter";
    public const string EscapeKey = "Esc";

    private static readonly GameFailureChoice[] LauncherChoices =
    [
        new(GameFailureAction.OpenLauncher, ChooseFolder, EnterKey), new(GameFailureAction.Quit, Quit, EscapeKey),
    ];

    private static readonly GameFailureChoice[] MenuChoices =
    [
        new(GameFailureAction.ReturnToMenu, BackToMenu, EnterKey), new(GameFailureAction.Quit, Quit, EscapeKey),
    ];

    /// <summary>The screen for <paramref name="failure"/>. <paramref name="logFile"/> is where this run's log is written
    /// (<see cref="LogFile"/>), null when nothing is.</summary>
    public static GameFailureScreen For(in GameFailure failure, string? logFile)
    {
        string log = logFile == null
            ? "The details went to the console; file logging is off for this run."
            : "The details are in the log file:";
        string? message = string.IsNullOrWhiteSpace(failure.Message) ? null : Sentence(failure.Message!.Trim());
        string level = string.IsNullOrEmpty(failure.Level) ? "The level" : failure.Level!;
        switch (failure.Kind)
        {
            case GameFailureKind.NoInstall:
                string? folder = string.IsNullOrWhiteSpace(failure.Path) ? null : failure.Path!.Trim();
                return new("Dungeon Keeper 2 was not found",
                    "Netherhold plays from the files of your own copy of Dungeon Keeper 2, and none was found.",
                    folder == null
                        ? "Choose the folder that holds DKII.EXE and the Data folder in the launcher, or set DK2_DIR."
                        : "The folder given was " + folder + ". " + LauncherCopy.Install(Dk2Install.Check(folder)).Text,
                    log, logFile, LauncherChoices);
            case GameFailureKind.LevelDidNotStart:
                return new("The level could not start", level + " failed while it loaded, so there is nothing to play.",
                    message, log, logFile, MenuChoices);
            case GameFailureKind.TurnFailed:
                string turn = failure.Turn is long t ? "Turn " + t.ToString(CultureInfo.InvariantCulture) : "A turn";
                string of = string.IsNullOrEmpty(failure.Level) ? "" : " of " + failure.Level;
                return new("The game stopped",
                    turn + of + " failed, so the game has stopped. Behind this message is the last frame drawn.",
                    message, log, logFile, MenuChoices);
            case GameFailureKind.SaveImportFailed:
                string file = string.IsNullOrWhiteSpace(failure.Path) ? "The file" : failure.Path!.Trim();
                return new("The DK2 save could not be imported",
                    file + " could not be read as a Dungeon Keeper 2 save.", message, log, logFile, MenuChoices);
            default:
                throw new ArgumentOutOfRangeException(nameof(failure), failure.Kind, "Unknown failure kind.");
        }
    }

    /// <summary>An error message as a sentence: its first letter capitalised.</summary>
    private static string Sentence(string text)
    {
        return char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text.Substring(1) : text;
    }

    /// <summary>Whether a failure screen waits for a choice: only with a display. A run without one (<c>--headless</c>) logs
    /// the failure and quits with <see cref="ExitCode"/>, so a script sees it.</summary>
    public static bool WaitsForChoice(bool hasDisplay)
    {
        return hasDisplay;
    }

    /// <summary>The choice <paramref name="key"/> takes: Enter the first, Esc the Quit; null when the screen has none.</summary>
    public static GameFailureAction? ForKey(GameFailureScreen screen, GameFailureKey key)
    {
        if (screen is null)
        {
            throw new ArgumentNullException(nameof(screen));
        }

        if (key == GameFailureKey.Confirm)
        {
            return screen.Choices.Count > 0 ? screen.Choices[0].Action : null;
        }

        foreach (GameFailureChoice choice in screen.Choices)
        {
            if (choice.Action == GameFailureAction.Quit)
            {
                return choice.Action;
            }
        }

        return null;
    }

    /// <summary>The file this run logs to: the engine's <c>--log-file &lt;path&gt;</c> when given (the last one wins),
    /// else <paramref name="defaultLogFile"/> when file logging is on, else null. Godot's own rule
    /// (<c>main.cpp</c>'s <c>--log-file</c> over <c>debug/file_logging/log_path</c>).</summary>
    public static string? LogFile(IReadOnlyList<string> engineArgs, bool fileLogging, string? defaultLogFile)
    {
        if (engineArgs is null)
        {
            throw new ArgumentNullException(nameof(engineArgs));
        }

        string? named = null;
        for (int i = 0; i + 1 < engineArgs.Count; i++)
        {
            if (engineArgs[i] == "--log-file" && !string.IsNullOrWhiteSpace(engineArgs[i + 1]))
            {
                named = engineArgs[i + 1];
            }
        }

        if (named != null)
        {
            return named;
        }

        return fileLogging && !string.IsNullOrWhiteSpace(defaultLogFile) ? defaultLogFile : null;
    }
}
