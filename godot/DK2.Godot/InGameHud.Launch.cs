// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Carries the campaign map's launch request into the new level scene.</summary>
public partial class InGameHud
{
    /// <summary>The campaign map's launch the next instance of this scene starts; taken and cleared by its start.</summary>
    private static GameSessionRequest? s_launch;

    /// <summary>Set once a failure screen shows (<see cref="Fail"/>); the scene then does nothing more.</summary>
    private bool _failed;

    /// <summary>Whether this process has started a level in this scene; <see cref="Main"/> then no longer sends
    /// <c>--level</c> back here when a level hands it the front end.</summary>
    internal static bool LevelStarted { get; private set; }

    /// <summary>Queues <paramref name="request"/> for the next instance of this scene (the front end then changes to it);
    /// null withdraws a request whose scene change failed.</summary>
    internal static void QueueSession(GameSessionRequest? request) => s_launch = request;

    /// <summary>The session this instance starts, taking the queued launch: its level, mode and return page, from which the
    /// level end's front-end launch rule follows (<see cref="SessionContext.LaunchedFromFrontEnd"/>).</summary>
    private SessionContext? StartingSession(GameSessionRequest? resume, string? levelArgument, string[] args, string install)
    {
        GameSessionRequest? launched = s_launch;
        s_launch = null;
        if (resume == null && launched == null && Array.IndexOf(args, "--dk2-save") >= 0)
        {
            try
            {
                string? path = LaunchArguments.Value(args, "--dk2-save");
                if (path == null || path.StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("--dk2-save requires a path.");
                launched = GameSessionRequest.LoadDk2Save(install, path);
            }
            catch (Exception ex) when (IsLegacyImportError(ex))
            {
                ReportLegacyImportError(ex, LaunchArguments.Value(args, "--dk2-save"));
                return null;
            }
        }
        LevelStarted = true;
        return SessionContext.Starting(resume, launched, levelArgument, Array.IndexOf(args, "--from-map") >= 0);
    }

    internal static bool IsLegacyImportError(Exception ex) =>
        ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or KeyNotFoundException;

    /// <summary>Shows the import's failure screen, naming <paramref name="path"/> (the <c>--dk2-save</c> argument, null for a
    /// front-end import).</summary>
    private void ReportLegacyImportError(Exception error, string? path) =>
        Fail(new GameFailure(GameFailureKind.SaveImportFailed, error.Message, Path: path), overFrame: false, error);

    /// <summary>Stops the scene on <paramref name="failure"/> and shows its screen (<see cref="FailureScreen"/>), over the last
    /// frame drawn when <paramref name="overFrame"/>: no more turns, sound or input, the pointer given back. Without a display
    /// nobody can choose, so the run quits with <see cref="GameFailureCopy.ExitCode"/> once the failure is logged.</summary>
    private void Fail(in GameFailure failure, bool overFrame, Exception? error = null)
    {
        if (_failed) return;
        _failed = true;
        // Main must not send a command-line level back here.
        LevelStarted = true;
        if (error != null) GD.PushError("DK2.Godot: ", error.ToString());
        _handCursor?.Update(HandCursorKind.Default, false, (long)(_elapsed * 1000));
        CapturePointer(false);
        if (_soundOutput != null) _soundOutput.ProcessMode = ProcessModeEnum.Disabled;
        _mixer?.Dispose();
        FailureScreen screen = FailureScreen.Show(this, failure, overFrame);
        if (!GameFailureCopy.WaitsForChoice(hasDisplay: DisplayServer.GetName() != "headless"))
        {
            GetTree().Quit(GameFailureCopy.ExitCode);
            return;
        }
        screen.Chosen += FailureChosen;
    }

    /// <summary>The failure screen's choice, as <see cref="GameFailureAction"/> says: the launcher and the menu are
    /// <c>Main.tscn</c>'s.</summary>
    private void FailureChosen(GameFailureAction action)
    {
        if (action == GameFailureAction.Quit)
        {
            GetTree().Quit(GameFailureCopy.ExitCode);
            return;
        }
        Main.s_openLauncher = action == GameFailureAction.OpenLauncher;
        LeaveForFrontEnd(FrontEndPage.None, "the failure screen asked for " + (Main.s_openLauncher ? "the launcher" : "the menu"));
    }
}
