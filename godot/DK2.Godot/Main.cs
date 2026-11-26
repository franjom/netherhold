// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Starts the launcher when needed, then opens the front end over a black screen. The install is read in place and player files use <see cref="UserFolders"/>. Launch arguments can skip startup, play one movie, script Escape or open a level.</summary>
public partial class Main : Node, IDevOverlayHost
{
    /// <summary>The page the front end opens after a level ends:
    /// taken and cleared by <see cref="_Ready"/>.</summary>
    internal static FrontEndPage s_returnPage;
    /// <summary>Whether this process has made its start decision; a later entry (a level that ended) goes straight on.</summary>
    private static bool s_started;
    /// <summary>Set by a failure screen's "Choose the folder" in another scene: the next entry shows the launcher.</summary>
    internal static bool s_openLauncher;

    private const string InGameHudScene = "res://InGameHud.tscn";

    private readonly HashSet<int> _skipFrames = new();
    private ClassicFrontEndMenu? _menu;
    private int _frame, _quitFrame = -1;

    public override void _Ready()
    {
        FrontEndPage returnPage = s_returnPage;
        s_returnPage = FrontEndPage.None;
        RenderingServer.SetDefaultClearColor(Colors.Black);
        string[] args = OS.GetCmdlineUserArgs();
        if (s_openLauncher)
        {
            s_openLauncher = false;
            s_started = true;
            ShowLauncher(args, returnPage);
            return;
        }
        if (!s_started)
        {
            s_started = true;
            switch (LauncherSettingsStore.Decide(args))
            {
                case LauncherStart.ShowLauncher:
                    ShowLauncher(args, returnPage);
                    return;
                case LauncherStart.StartWithSavedScreen:
                    LauncherDisplay.Apply(GetTree().Root, LauncherSettingsStore.Current(args));
                    break;
            }
            // A level that ended hands this scene the front end; only the first process start can use a command-line level request.
            if (!InGameHud.LevelStarted && (Array.IndexOf(args, "--level") >= 0 || Array.IndexOf(args, "--dk2-save") >= 0))
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
            ShowNoInstall(args, returnPage);
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
        _menu = menu;
        DevOverlay.Attach(this, this, args);
    }

    private void ShowLauncher(string[] args, FrontEndPage returnPage)
    {
        var launcher = new Launcher { Name = "Launcher" };
        launcher.Open(args);
        launcher.PlayRequested += settings =>
        {
            launcher.QueueFree();
            LauncherDisplay.Apply(GetTree().Root, settings);
            Start(args, returnPage);
        };
        AddChild(launcher);
    }

    /// <summary>No install: the failure screen (<see cref="GameFailureKind.NoInstall"/>), whose "Choose the folder" shows
    /// the launcher here and whose Quit quits.</summary>
    private void ShowNoInstall(string[] args, FrontEndPage returnPage)
    {
        FailureScreen screen = FailureScreen.Show(this,
            new GameFailure(GameFailureKind.NoInstall, Path: LauncherSettingsStore.NamedInstall(args)), overFrame: false);
        if (!GameFailureCopy.WaitsForChoice(hasDisplay: DisplayServer.GetName() != "headless"))
        {
            GetTree().Quit(GameFailureCopy.ExitCode);
            return;
        }
        screen.Chosen += action =>
        {
            if (action == GameFailureAction.Quit)
            {
                GetTree().Quit(GameFailureCopy.ExitCode);
                return;
            }
            screen.QueueFree();
            ShowLauncher(args, returnPage);
        };
    }

    void IDevOverlayHost.FillDevOverlayFacts(ref DevOverlayFacts facts)
    {
        facts.Scene = DevOverlayScene.FrontEnd;
        facts.Page = _menu?.Page ?? FrontEndPage.None;
    }

    bool IDevOverlayHost.TryStateHash(out ulong hash, out DK2.Core.GameTick turn)
    {
        hash = 0;
        turn = default;
        return false;
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
