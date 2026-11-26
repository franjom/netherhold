// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>How this scene learns which session to start: a loaded save or a restart (<see cref="s_resume"/>), the campaign
/// map's launch (<see cref="QueueSession"/>, as Unity's runner hands its pending session to the runner that replaces it),
/// or the scripted arguments <c>--level</c> and <c>--from-map</c> (<see cref="SessionContext.Starting"/> decides).</summary>
public partial class InGameHud
{
    /// <summary>The campaign map's launch the next instance of this scene starts; taken and cleared by its start.</summary>
    private static GameSessionRequest? s_launch;

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
                ReportLegacyImportError(ex);
                return null;
            }
        }
        LevelStarted = true;
        return SessionContext.Starting(resume, launched, levelArgument, Array.IndexOf(args, "--from-map") >= 0);
    }

    private static bool IsLegacyImportError(Exception ex) =>
        ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or KeyNotFoundException;

    private void ReportLegacyImportError(Exception error)
    {
        GD.PushError("DK2.Godot could not import the DK2 save. " + error.Message);
        GetTree().Quit(1);
    }
}
