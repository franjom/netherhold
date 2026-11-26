// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Runtime.InteropServices;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Plays one of the install's movies full screen, with audio at the movie's authored sample rate.</summary>
public partial class ClassicMoviePlayer : Node
{
    /// <summary>Over the HUD, the placement cursor, the hand and the screen colours.</summary>
    public const int CanvasLayerIndex = 120;

    /// <summary>Frames pushed to the generator at a time.</summary>
    private const int AudioChunk = 512;

    private readonly Vector2[] _chunk = new Vector2[AudioChunk];
    private MoviePlayback? _movie;
    private CanvasLayer? _layer;
    private Rid _item;
    private Image? _image;
    private ImageTexture? _texture;
    private AudioStreamPlayer? _player;
    private AudioStreamGenerator? _generator;
    private AudioStreamGeneratorPlayback? _playback;
    private long _pushed;
    private int _capacity;
    private double _seconds;
    private Vector2 _drawnSize = -Vector2.One;
    private bool _drawnFrame, _done;
    private readonly bool _movieMaker = Engine.GetWriteMoviePath().Length > 0;
    // The adapter's own cost, for the summary: this node's process time and managed allocation after the first picture.
    private long _processTicks, _processTicksMax, _uploadTicks, _allocated;
    private int _processes, _uploads;

    /// <summary>Raised once when the movie ends, is skipped or cannot play, just before the node frees itself.</summary>
    public event Action? Finished;

    /// <summary>The movie playing, for the subtitles and anything else that follows it; null once finished.</summary>
    public MoviePlayback? Playback => _done ? null : _movie;

    /// <summary>True once the first picture is up; until then the caller keeps the screen covered.</summary>
    public bool HasFrame => _movie?.HasFrame ?? false;

    /// <summary>Starts <paramref name="relativePath"/> from <paramref name="install"/> on a new node under
    /// <paramref name="parent"/>; a movie that cannot be opened warns and finishes on its first frame.</summary>
    public static ClassicMoviePlayer Play(Node parent, string install, string relativePath)
    {
        var player = new ClassicMoviePlayer { Name = "Movie" };
        player._movie = MoviePlayback.TryOpen(install, relativePath, out string? error);
        if (error != null) GD.PushError("DK2.Godot movie: " + error);
        parent.AddChild(player);
        return player;
    }

    public override void _Ready()
    {
        _layer = new CanvasLayer { Name = "Layer", Layer = CanvasLayerIndex };
        AddChild(_layer);
        _item = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(_item, _layer.GetCanvas());
        // DK2 doubles each pixel; it does not filter.
        RenderingServer.CanvasItemSetDefaultTextureFilter(_item, RenderingServer.CanvasItemTextureFilter.Nearest);
        if (_movie == null) return;
        if (_movie.Width > 0)
        {
            _image = Image.CreateEmpty(_movie.Width, _movie.Height, false, Image.Format.Rgba8);
            _texture = ImageTexture.CreateFromImage(_image);
        }
        if (_movie.HasAudio) StartAudio(_movie);
    }

    private void StartAudio(MoviePlayback movie)
    {
        // The generator plays at the movie's own rate (22050 Hz) and Godot resamples.
        _generator = new AudioStreamGenerator
        {
            MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom,
            MixRate = movie.SampleRate,
            BufferLength = .25f,
        };
        _player = new AudioStreamPlayer { Name = "Audio", Stream = _generator };
        AddChild(_player);
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        // Empty, the ring buffer's free space is its capacity; what the mixer has taken is what was pushed less what waits.
        _capacity = _playback.GetFramesAvailable();
        PumpAudio(movie);
    }

    private void PumpAudio(MoviePlayback movie)
    {
        if (_playback == null) return;
        int room = _playback.GetFramesAvailable();
        while (room >= AudioChunk)
        {
            movie.ReadAudio(MemoryMarshal.Cast<Vector2, float>(_chunk.AsSpan()));
            _playback.PushBuffer(new ReadOnlySpan<Vector2>(_chunk));
            _pushed += AudioChunk;
            room -= AudioChunk;
        }
    }

    /// <summary>The frames of the movie's stream Godot's mixer has played: pushed, less what still waits in the buffer, plus the
    /// time since that mix (the mixer takes whole buffers, about 93 ms at a time under the dummy driver), as Godot's audio
    /// sync guide does; Movie Maker mixes once a frame, so a capture leaves the wall clock out and repeats.</summary>
    private long AudioPlayed(MoviePlayback movie)
    {
        if (_playback == null) return 0;
        long taken = _pushed - (_capacity - _playback.GetFramesAvailable());
        if (_movieMaker) return taken;
        return Math.Min(_pushed, taken + (long)(AudioServer.GetTimeSinceLastMix() * movie.SampleRate));
    }

    public override void _Input(InputEvent @event)
    {
        if (_done || _movie == null) return;
        MovieInput input = @event switch
        {
            InputEventKey { Pressed: true, Echo: false } key => key.Keycode switch
            {
                Key.Escape => MovieInput.Escape,
                Key.Enter => MovieInput.Enter,
                Key.KpEnter => MovieInput.KeypadEnter,
                Key.Space => MovieInput.Space,
                _ => MovieInput.Other,
            },
            InputEventMouseButton { Pressed: true } button => button.ButtonIndex switch
            {
                MouseButton.Left => MovieInput.LeftButton,
                MouseButton.Right => MovieInput.RightButton,
                MouseButton.Middle => MovieInput.MiddleButton,
                _ => MovieInput.Other,
            },
            _ => MovieInput.Other,
        };
        _movie.Press(input);
        // The menu under the movie is not running; none of its input reaches it.
        if (@event is InputEventKey or InputEventMouseButton) GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        MoviePlayback? movie = _movie;
        if (movie == null || movie.Finished) { Finish(); return; }
        long start = Stopwatch.GetTimestamp();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        bool counted = movie.HasFrame;
        _seconds += delta;
        PumpAudio(movie);
        if (movie.Update(_seconds, AudioPlayed(movie)) && _image != null && _texture != null)
        {
            long upload = Stopwatch.GetTimestamp();
            _image.SetData(movie.Width, movie.Height, false, Image.Format.Rgba8, movie.Pixels);
            _texture.Update(_image);
            if (counted) { _uploadTicks += Stopwatch.GetTimestamp() - upload; _uploads++; }
        }
        Redraw(movie);
        if (counted)
        {
            long ticks = Stopwatch.GetTimestamp() - start;
            _processTicks += ticks;
            _processTicksMax = Math.Max(_processTicksMax, ticks);
            _processes++;
            _allocated += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }
        if (movie.Finished) Finish();
    }

    /// <summary>Black over the whole screen, and the picture once there is one: its shape is <see cref="MoviePlayback.DisplayAspect"/>,
    /// centred and scaled uniformly to fit.</summary>
    private void Redraw(MoviePlayback movie)
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (size == _drawnSize && _drawnFrame == movie.HasFrame) return;
        _drawnSize = size;
        _drawnFrame = movie.HasFrame;
        RenderingServer.CanvasItemClear(_item);
        RenderingServer.CanvasItemAddRect(_item, new Rect2(Vector2.Zero, size), Colors.Black);
        if (!_drawnFrame || _texture == null) return;
        float aspect = movie.DisplayAspect;
        float w = size.X, h = size.Y;
        if (w / h > aspect) w = h * aspect; else h = w / aspect;
        RenderingServer.CanvasItemAddTextureRect(_item, new Rect2((size.X - w) * .5f, (size.Y - h) * .5f, w, h), _texture.GetRid());
    }

    private void Finish()
    {
        if (_done) return;
        _done = true;
        _player?.Stop();
        if (_movie != null)
        {
            if (_movie.Failure != null) GD.PushError("DK2.Godot movie: " + _movie.Failure);
            double toMs = 1000.0 / Stopwatch.Frequency;
            double upload = _uploads == 0 ? 0 : _uploadTicks * toMs / _uploads;
            string adapter = _processes == 0 ? "" : FormattableString.Invariant(
                $"; main thread {_processTicks * toMs / _processes:F2} ms per frame average, {_processTicksMax * toMs:F2} ms max, upload {upload:F2} ms per frame, {_allocated} managed bytes allocated over {_processes} frames; the mixer runs {AudioServer.GetOutputLatency() * 1000:F0} ms ahead of the speakers");
            GD.Print(_movie.Summary() + adapter);
        }
        Finished?.Invoke();
        QueueFree();
    }

    public override void _ExitTree()
    {
        _done = true;
        _player?.Stop();
        _movie?.Dispose();
        if (_item.IsValid) RenderingServer.FreeRid(_item);
        _item = default;
        // Released now, not when the collector gets to the wrappers: a quit right after the movie reports them leaked.
        _playback?.Dispose(); _playback = null;
        _generator?.Dispose(); _generator = null;
        _texture?.Dispose(); _texture = null;
        _image?.Dispose(); _image = null;
    }
}
