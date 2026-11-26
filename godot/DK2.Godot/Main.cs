// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Scene root: shows the classic front-end menu over a black screen, after the <see cref="Launcher"/> when the
/// process's first start asks for it (<see cref="LauncherSettingsStore.Decide"/>; scripted runs never see it). The install
/// is <see cref="LauncherSettingsStore.FindInstall"/>'s; its files are read in place and never copied into the project. The
/// campaign profile and hi-score table are the player's files under <see cref="UserFolders"/>. The first front end of the
/// process runs the title screen and the intro movies, unless <c>--skip-startup</c> is given or there is no display
/// (<c>--headless</c>). <c>--play-movie &lt;path&gt;</c> plays one movie, relative to the install, and quits, as Unity's
/// <c>DK2/Movies</c> menu does; <c>--movie-skip-frame &lt;n,...&gt;</c> sends an Esc press at each frame n (counted from this scene's start) to test
/// skipping. <c>--level &lt;name&gt;</c> starts <c>InGameHud.tscn</c> instead, as naming that scene does, since an exported game
/// cannot name a scene on its command line.</summary>
public partial class Main : Node
{
    /// <summary>The page a level that ended hands this scene (Unity's <c>GameSessionRequest.FrontEnd(returnPage)</c>);
    /// taken and cleared by <see cref="_Ready"/>.</summary>
    internal static FrontEndPage s_returnPage;
    /// <summary>Whether this process has made its start decision; a later entry (a level that ended) goes straight on.</summary>
    private static bool s_started;

    private const string InGameHudScene = "res://InGameHud.tscn";

    private readonly HashSet<int> _skipFrames = new();
    private int _frame, _quitFrame = -1;

    public override void _Ready()
    {
        FrontEndPage returnPage = s_returnPage;
        s_returnPage = FrontEndPage.None;
        RenderingServer.SetDefaultClearColor(Colors.Black);
        string[] args = OS.GetCmdlineUserArgs();
        if (!s_started)
        {
            s_started = true;
            switch (LauncherSettingsStore.Decide(args))
            {
                case LauncherStart.ShowLauncher:
                    var launcher = new Launcher { Name = "Launcher" };
                    launcher.Open(args);
                    launcher.PlayRequested += settings =>
                    {
                        launcher.QueueFree();
                        LauncherDisplay.Apply(GetTree().Root, settings);
                        Start(args, returnPage);
                    };
                    AddChild(launcher);
                    return;
                case LauncherStart.StartWithSavedScreen:
                    LauncherDisplay.Apply(GetTree().Root, LauncherSettingsStore.Current(args));
                    break;
            }
            // A level that ended hands this scene the front end; only the process's first start goes to --level's level.
            if (Array.IndexOf(args, "--level") >= 0 && !InGameHud.LevelStarted)
            {
                // InGameHud reads the same user arguments (--level, --seed, ...).
                Callable.From(() => GetTree().ChangeSceneToFile(InGameHudScene)).CallDeferred();
                return;
            }
        }
        Start(args, returnPage);
    }

    private void Start(string[] args, FrontEndPage returnPage)
    {
        string? install = LauncherSettingsStore.FindInstall(args);
        if (install is null)
        {
            GD.PushError("DK2.Godot: no Dungeon Keeper 2 install found; choose it in the launcher (--launcher) or set DK2_DIR.");
            return;
        }
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
