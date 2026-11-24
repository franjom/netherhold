// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The front end's movies, as Unity's <c>DungeonRunner.Startup</c> and <c>DungeonRunner.Cutscene</c> run them: the
/// title screen and the two intros on the first front end of the process (<see cref="FrontEndStartup"/>), the attract movies
/// after three idle minutes (<see cref="FrontEndAttract"/>), the gallery's and the trailer's movies, and a won map's title
/// card and cutscene before the front end runs (<see cref="CampaignCutsceneRunner"/>). While any of them runs the menu does
/// not. The title card's text is not drawn yet (the subtitles' batch); the card is black.</summary>
public partial class ClassicFrontEndMenu : ICampaignCutsceneHost
{
    private static readonly FrontEndStartupGate StartupGate = new();
    private readonly FrontEndAttract _attract = new();
    private FrontEndStartup? _startup;
    private double _startupStarted, _frontEndStarted;
    private string? _install;
    private ClassicMoviePlayer? _movie, _cutscenePlayer;
    private CampaignCutsceneRunner? _cutscenes;
    private CanvasLayer? _coverLayer;
    private Rid _cover;
    private ImageTexture? _titleImage;
    private Vector2 _coverSize = -Vector2.One;
    private int _coverState = -1;
    private Vector2 _lastPointer;
    private bool _keyPressed;

    /// <summary>The movie on screen (a startup, attract, gallery or cutscene movie), or null: its playback state is what the
    /// subtitles will follow.</summary>
    public ClassicMoviePlayer? PlayingMovie => _movie ?? _cutscenePlayer;

    /// <summary>The won map's title card and cutscene runner, or null outside one.</summary>
    public CampaignCutsceneRunner? Cutscenes => _cutscenes;

    private void OpenMovies(string install, bool skipStartup, FrontEndPage returnPage)
    {
        _install = install;
        _coverLayer = new CanvasLayer { Name = "Cover", Layer = ClassicMoviePlayer.CanvasLayerIndex - 1 };
        AddChild(_coverLayer);
        _cover = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(_cover, _coverLayer.GetCanvas());
        _hud!.Model.MovieRequested += PlayFrontEndMovie;
        if (StartupGate.Begin(skipStartup, returnPage))
        {
            _startup = new FrontEndStartup();
            _startupStarted = _elapsed;
            try
            {
                RgbaImage title = _assets!.Load("TitleScreen/TitleScreen");
                using Image image = Image.CreateFromData(title.Width, title.Height, false, Image.Format.Rgba8, title.Pixels);
                _titleImage = ImageTexture.CreateFromImage(image);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
            {
                GD.PushWarning("DK2.Godot: no title screen image (" + ex.Message + "); the title stays black.");
            }
        }
        // 0x00534F70 plays a won map's title card and cutscene before the front end runs.
        if (returnPage == FrontEndPage.CampaignResults && _profiles?.Profile is CampaignProfile profile)
        {
            _cutscenes = new CampaignCutsceneRunner(this);
            _cutscenes.Begin(profile, install, id => CampaignCutsceneRunner.SpeechText(install, id), (float)_elapsed);
        }
    }

    /// <summary>Runs the startup, then the cutscene, then the attract check; true while one of them holds the screen.</summary>
    private bool UpdateMovies()
    {
        bool held = UpdateStartup() || UpdateCutscene() || UpdateAttract();
        _keyPressed = false;
        DrawCover();
        if (_canvas != null) _canvas.Visible = !held;
        return held;
    }

    private bool UpdateStartup()
    {
        if (_startup == null) return false;
        FrontEndStartupStage stage = _startup.Update(TimeSpan.FromSeconds(_elapsed - _startupStarted));
        if (stage == FrontEndStartupStage.Done)
        {
            _startup = null;
            _frontEndStarted = _elapsed;
            _session!.Update(TimeSpan.Zero);
            return false;
        }
        if (stage != FrontEndStartupStage.Title && _movie == null)
        {
            _movie = ClassicMoviePlayer.Play(this, _install!, FrontEndMoviePaths.Of(stage == FrontEndStartupStage.BullfrogIntro
                ? FrontEndMovie.BullfrogIntro : FrontEndMovie.Intro));
            _movie.Finished += () => { _movie = null; _startup?.MovieFinished(); };
        }
        return true;
    }

    private bool UpdateCutscene()
    {
        if (_cutscenes == null || !_cutscenes.Playing) return false;
        if (_cutscenes.Update((float)_elapsed)) return true;
        // The front end's clock starts when it first runs, as DK2's init returns only after the movie.
        _frontEndStarted = _elapsed;
        return false;
    }

    private bool UpdateAttract()
    {
        if (_movie != null) return true;
        Vector2 pointer = GetViewport().GetMousePosition();
        FrontEndMovie? movie = _attract.Update(FrontEndTime, _session!.Page, pointer != _lastPointer, _keyPressed);
        _lastPointer = pointer;
        if (!movie.HasValue) return false;
        PlayFrontEndMovie(FrontEndMoviePaths.Of(movie.Value));
        return true;
    }

    private TimeSpan FrontEndTime => TimeSpan.FromSeconds(Math.Max(0, _elapsed - _frontEndStarted));

    private void PlayFrontEndMovie(string path)
    {
        if (_movie != null || _install == null) return;
        _movie = ClassicMoviePlayer.Play(this, _install, path);
        _movie.Finished += () =>
        {
            _movie = null;
            _attract.Reset(FrontEndTime);
        };
    }

    /// <summary>Unity's <c>Input.anyKeyDown</c> for the attract timer: a key or a mouse button went down this frame.</summary>
    private void NoteKeyPress(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true }) _keyPressed = true;
    }

    /// <summary>Black under the startup's title screen (drawn at 640 x 480 fitted), and from the cutscene's card until a movie's
    /// first picture is up and between its movies, since nothing else hides the menu beneath.</summary>
    private void DrawCover()
    {
        if (!_cover.IsValid) return;
        int state = _startup?.Stage == FrontEndStartupStage.Title ? 2
            : _cutscenes != null && _cutscenes.Blackout(_cutscenePlayer != null && _cutscenePlayer.HasFrame) ? 1 : 0;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (state == _coverState && size == _coverSize) return;
        _coverState = state;
        _coverSize = size;
        RenderingServer.CanvasItemClear(_cover);
        if (state == 0) return;
        RenderingServer.CanvasItemAddRect(_cover, new Rect2(Vector2.Zero, size), Colors.Black);
        if (state != 2 || _titleImage == null) return;
        float scale = MathF.Min(size.X / 640f, size.Y / 480f);
        RenderingServer.CanvasItemAddTextureRect(_cover,
            new Rect2((size.X - 640 * scale) / 2, (size.Y - 480 * scale) / 2, 640 * scale, 480 * scale), _titleImage.GetRid());
    }

    private void CloseMovies()
    {
        if (_hud != null) _hud.Model.MovieRequested -= PlayFrontEndMovie;
        if (_cover.IsValid) RenderingServer.FreeRid(_cover);
        _cover = default;
        _titleImage = null;
    }

    bool ICampaignCutsceneHost.SpeechBusy => _mixer?.SpeechBusy ?? false;

    void ICampaignCutsceneHost.QueueSpeech(SoundId speech) =>
        _mixer?.QueueSpeech(SoundCategory.Mentor, speech, clearWaiting: true);

    bool ICampaignCutsceneHost.MoviePlaying => _cutscenePlayer != null;

    void ICampaignCutsceneHost.PlayMovie(string relativePath, string install)
    {
        _cutscenePlayer = ClassicMoviePlayer.Play(this, install, relativePath);
        _cutscenePlayer.Finished += () => _cutscenePlayer = null;
    }
}
