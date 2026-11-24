// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Scene root: shows the classic front-end menu over a black screen. The install is DK2_DIR, else the usual
/// install locations (<see cref="Dk2Install"/>); its files are read in place and never copied into the project. The
/// campaign profile and hi-score table are the player's files under <see cref="UserFolders"/>. The first front end of the
/// process runs the title screen and the intro movies, unless <c>--skip-startup</c> is given or there is no display
/// (<c>--headless</c>). <c>--play-movie &lt;path&gt;</c> plays one movie, relative to the install, and quits, as Unity's
/// <c>DK2/Movies</c> menu does; <c>--movie-skip-frame &lt;n,...&gt;</c> sends an Esc press at each frame n (counted from this scene's start) to test
/// skipping.</summary>
public partial class Main : Node
{
    /// <summary>The page a level that ended hands this scene (Unity's <c>GameSessionRequest.FrontEnd(returnPage)</c>);
    /// taken and cleared by <see cref="_Ready"/>.</summary>
    internal static FrontEndPage s_returnPage;

    private readonly HashSet<int> _skipFrames = new();
    private int _frame, _quitFrame = -1;

    public override void _Ready()
    {
        FrontEndPage returnPage = s_returnPage;
        s_returnPage = FrontEndPage.None;
        RenderingServer.SetDefaultClearColor(Colors.Black);
        string? install = Dk2Install.Find();
        if (install is null)
        {
            GD.PushError("DK2.Godot: no Dungeon Keeper 2 install found; set DK2_DIR.");
            return;
        }
        string[] args = OS.GetCmdlineUserArgs();
        int skipAt = Array.IndexOf(args, "--movie-skip-frame");
        if (skipAt >= 0 && skipAt + 1 < args.Length)
            foreach (string frame in args[skipAt + 1].Split(','))
                if (int.TryParse(frame, out int at)) _skipFrames.Add(at);
        int movieAt = Array.IndexOf(args, "--play-movie");
        if (movieAt >= 0 && movieAt + 1 < args.Length)
        {
            ClassicMoviePlayer movie = ClassicMoviePlayer.Play(this, install, args[movieAt + 1]);
            // A few frames later: the audio server lets go of the stopped stream only on a later mix.
            movie.Finished += () => _quitFrame = _frame + 10;
            return;
        }
        var menu = new ClassicFrontEndMenu { Name = "FrontEndMenu" };
        bool skipStartup = Array.IndexOf(args, "--skip-startup") >= 0 || DisplayServer.GetName() == "headless";
        menu.Open(install, UserFolders.FromArguments(args), returnPage, skipStartup);
        AddChild(menu);
    }

    public override void _Process(double delta)
    {
        if (_frame == _quitFrame) GetTree().Quit();
        if (!_skipFrames.Contains(_frame++)) return;
        // Through Godot's input like a real press; the OS cursor and keyboard are not touched.
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = false });
    }
}
