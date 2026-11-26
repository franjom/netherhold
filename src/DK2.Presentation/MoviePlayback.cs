// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Threading;
using DK2.Core;
using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>The input a playing movie reacts to; an adapter maps its keys and buttons here and passes the rest as
/// <see cref="Other"/>.</summary>
public enum MovieInput : byte { Other, Escape, Enter, KeypadEnter, Space, LeftButton, RightButton, MiddleButton }

/// <summary>
/// One of the install's EA movies (<c>Data\Movies\*.tgq</c>) as the front end plays it (<c>docs/research/movie-format.md</c>
/// §4.7, §5), with every playback decision Unity's <c>ClassicMoviePlayer</c> made: the start delay, which frame is due by
/// the audio clock (<see cref="EaMovieTimeline"/>), frames dropped three behind, the skip input and the end. The next due
/// frame is decoded ahead on one worker thread into a second buffer, so a due frame only needs uploading; steady playback
/// allocates nothing. The adapter shows <see cref="Pixels"/> (top-down RGBA, each pixel doubled horizontally on screen,
/// <see cref="DisplayAspect"/>) and plays <see cref="ReadAudio"/>; nothing derived is written to disk.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/movie-format.md")]
public sealed class MoviePlayback : IDisposable
{
    /// <summary>Silence before the first sample (and the first frame of a silent movie), a head start for the decoder.</summary>
    public const double StartDelaySeconds = 0.1;

    private readonly EaMovieTimeline _timeline;
    private readonly EaAudioStream? _audio;
    private readonly object _audioLock = new();
    private readonly long _delayFrames;
    private long _delivered;
    private readonly TqiDecoder _decoder = new();
    private byte[] _shownPixels = [], _aheadPixels = [];
    private readonly Thread? _worker;
    private readonly AutoResetEvent _request = new(false);
    // A kernel event, not ManualResetEventSlim: the slim one allocates its lock object the first time a Wait has to block,
    // which would put a one-off allocation on the frame thread at whatever frame the worker is first late.
    private readonly ManualResetEvent _decoded = new(true);
    private volatile bool _stopping;
    private int _requested = -1, _aheadIndex = -1;
    private long _decodeTicks;
    private Exception? _decodeError;
    private int _shown = -1;
    // One summary at the end, so a sync complaint can be measured rather than guessed.
    private int _framesShown, _framesDropped;
    private double _decodeMsSum, _decodeMsMax, _lagMsSum, _lagMsMax = double.MinValue, _lagMsMin = double.MaxValue,
        _lagMsFirst, _lagMsLast;

    public MoviePlayback(EaMovie movie, string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _timeline = new EaMovieTimeline(movie ?? throw new ArgumentNullException(nameof(movie)));
        if (_timeline.HasAudio)
        {
            _audio = new EaAudioStream(_timeline);
            _delayFrames = (long)Math.Round(StartDelaySeconds * SampleRate);
        }
        if (_timeline.VideoFrames.Count == 0)
        {
            return;
        }

        (Width, Height) = TqiDecoder.FrameSize(movie.Payload(_timeline.VideoFrames[0]));
        _shownPixels = new byte[Width * Height * 4];
        _aheadPixels = new byte[_shownPixels.Length];
        _worker = new Thread(DecodeLoop) { IsBackground = true, Name = "DK2 movie decode" };
        _worker.Start();
        DecodeAhead(0);
    }

    /// <summary>Opens <paramref name="relativePath"/> under <paramref name="install"/>, or returns null with the reason.</summary>
    public static MoviePlayback? TryOpen(string install, string relativePath, out string? error)
    {
        string path = Path.Combine(install, relativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            error = null;
            return new MoviePlayback(EaMovie.Open(path), relativePath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"could not open {path}: {ex.Message}";
            return null;
        }
    }

    /// <summary>The movie's path as it was asked for.</summary>
    public string Name { get; }

    /// <summary>The decoded frame's size in pixels; 0 for a movie without video.</summary>
    public int Width { get; }

    public int Height { get; }

    /// <summary>The picture's shape on screen, width over height: each pixel is doubled horizontally and lines are not
    /// (<c>0x005AABF0</c>), so 320x480 shows at 640x480 proportions, centred and scaled uniformly to fit.</summary>
    public float DisplayAspect => Height == 0 ? 4f / 3f : 2f * Width / Height;

    public bool HasAudio => _audio != null;

    /// <summary>The rate <see cref="ReadAudio"/> delivers at, stereo frames a second.</summary>
    public int SampleRate => _timeline.Movie.SampleRate;

    public int FrameCount => _timeline.VideoFrames.Count;

    /// <summary>The frame on screen, -1 before the first.</summary>
    public int ShownFrame => _shown;

    /// <summary>A picture is up; until then the adapter keeps the screen black.</summary>
    public bool HasFrame => _framesShown > 0;

    /// <summary>The movie ended, was skipped, or failed (<see cref="Failure"/>); the adapter stops it.</summary>
    public bool Finished { get; private set; }

    public bool Skipped { get; private set; }

    /// <summary>Why a frame could not be decoded, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>The shown frame, top-down RGBA, <see cref="Width"/> x <see cref="Height"/>.</summary>
    public ReadOnlySpan<byte> Pixels => _shownPixels;

    /// <summary>The sample frame of the movie's audio the speakers' clock is at, as last passed to <see cref="Update"/>.</summary>
    public long PlayedAudioFrame { get; private set; } = -1;

    /// <summary>Esc, Enter, Space or a mouse button ends a movie (<c>0x0053C270</c>, <c>0x0053C2A0</c>); Unity's player also
    /// took the keypad's Enter.</summary>
    public static bool Skips(MovieInput input) => input != MovieInput.Other;

    /// <summary>Ends the movie when <paramref name="input"/> skips it; true if it did.</summary>
    public bool Press(MovieInput input)
    {
        if (Finished || !Skips(input))
        {
            return false;
        }

        Skipped = true;
        Finished = true;
        return true;
    }

    /// <summary>Fills <paramref name="interleaved"/> (L, R, L, R ...) with the next stereo frames: <see cref="StartDelaySeconds"/>
    /// of silence, then the movie's audio, then silence. Safe from an audio thread.</summary>
    public void ReadAudio(Span<float> interleaved)
    {
        lock (_audioLock)
        {
            int frames = interleaved.Length / 2;
            int silent = (int)Math.Min(frames, Math.Max(0, _delayFrames - _delivered));
            interleaved.Slice(0, silent * 2).Clear();
            if (_audio != null)
            {
                _audio.Read(interleaved.Slice(silent * 2));
            }
            else
            {
                interleaved.Slice(silent * 2).Clear();
            }

            _delivered += frames;
        }
    }

    /// <summary>Advances the picture to what is due and ends the movie once both streams are done.</summary>
    /// <param name="seconds">Seconds since the movie started, the clock of a movie without audio.</param>
    /// <param name="audioFramesPlayed">How many frames of <see cref="ReadAudio"/>'s output the engine has played (its mixer's
    /// clock); unused without audio.</param>
    /// <returns>True when <see cref="Pixels"/> holds a new frame to upload.</returns>
    public bool Update(double seconds, long audioFramesPlayed)
    {
        if (Finished)
        {
            return false;
        }

        int showable;
        bool audioDone;
        long played = -1;
        if (HasAudio)
        {
            played = audioFramesPlayed - _delayFrames;
            PlayedAudioFrame = played;
            showable = played < 0 ? 0 : _timeline.ShowableFrames(played);
            audioDone = played >= _timeline.TotalAudioFrames;
        }
        else
        {
            showable = _timeline.ShowableFramesWithoutAudio(seconds - StartDelaySeconds);
            audioDone = true;
        }

        bool changed = false;
        int next = EaMovieTimeline.NextFrameToDecode(_shown, showable);
        if (next >= 0)
        {
            _framesDropped += next - _shown - 1;
            changed = ShowFrame(next);
            if (changed && HasAudio)
            {
                double lagMs = (played - _timeline.DueAudioFrame(next)) * 1000.0 / SampleRate;
                if (_framesShown == 1)
                {
                    _lagMsFirst = lagMs;
                }

                _lagMsLast = lagMs;
                _lagMsSum += lagMs;
                _lagMsMax = Math.Max(_lagMsMax, lagMs);
                _lagMsMin = Math.Min(_lagMsMin, lagMs);
            }
        }

        if (Failure != null || audioDone && _shown >= _timeline.VideoFrames.Count - 1)
        {
            Finished = true;
        }

        return changed;
    }

    /// <summary>One line for the log: frames shown and dropped, decode time, and how late each frame was by the audio clock
    /// (average, range, and the drift from the first frame to the last).</summary>
    public string Summary()
    {
        string head = $"DK2 movie {Name}: {_framesShown} of {FrameCount} frames shown, {_framesDropped} dropped";
        if (Skipped)
        {
            head += ", skipped";
        }

        if (_framesShown == 0)
        {
            return head;
        }

        head += FormattableString.Invariant($"; decode {_decodeMsSum / _framesShown:F2} ms average, {_decodeMsMax:F2} ms max");
        if (!HasAudio)
        {
            return head;
        }

        return head + FormattableString.Invariant($"; shown {_lagMsSum / _framesShown:F1} ms average after due by the audio clock ") +
               FormattableString.Invariant($"({_lagMsMin:F1} to {_lagMsMax:F1} ms), drift {_lagMsLast - _lagMsFirst:F1} ms first to last frame");
    }

    public double DecodeMsAverage => _framesShown == 0 ? 0 : _decodeMsSum / _framesShown;
    public double DecodeMsMax => _decodeMsMax;
    public int FramesShown => _framesShown;
    public int FramesDropped => _framesDropped;

    /// <summary>Lateness of the last shown frame minus the first's, in milliseconds of audio.</summary>
    public double LagDriftMs => _framesShown == 0 ? 0 : _lagMsLast - _lagMsFirst;

    // The worker owns _aheadPixels and the decoder while _decoded is reset; the frame thread waits for it before
    // touching either.
    private void DecodeAhead(int index)
    {
        _aheadIndex = -1;
        if (index >= _timeline.VideoFrames.Count || _decodeError != null)
        {
            return;
        }

        (int width, int height) = TqiDecoder.FrameSize(_timeline.Movie.Payload(_timeline.VideoFrames[index]));
        if (width != Width || height != Height)
        {
            return; // a size change is not expected in DK2's movies
        }

        _aheadIndex = index;
        _requested = index;
        _decoded.Reset();
        _request.Set();
    }

    private void DecodeLoop()
    {
        while (true)
        {
            _request.WaitOne();
            if (_stopping)
            {
                return;
            }

            long start = Stopwatch.GetTimestamp();
            try { _decoder.Decode(_timeline.Movie.Payload(_timeline.VideoFrames[_requested]), _aheadPixels); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException) { _decodeError = ex; }
            _decodeTicks = Stopwatch.GetTimestamp() - start;
            _decoded.Set();
        }
    }

    private bool ShowFrame(int index)
    {
        _shown = index;
        if (_aheadIndex < 0)
        {
            return false;
        }

        _decoded.WaitOne(); // waits only while the worker is still decoding
        if (_aheadIndex != index)
        {
            // Frames were dropped past the one decoded ahead: decode the due one now.
            DecodeAhead(index);
            if (_aheadIndex < 0)
            {
                return false;
            }

            _decoded.WaitOne();
        }
        if (_decodeError != null)
        {
            Failure = $"frame {index} of {Name} did not decode: {_decodeError.Message}";
            return false;
        }
        double decodeMs = _decodeTicks * 1000.0 / Stopwatch.Frequency;
        _decodeMsSum += decodeMs;
        _decodeMsMax = Math.Max(_decodeMsMax, decodeMs);
        _framesShown++;
        (_shownPixels, _aheadPixels) = (_aheadPixels, _shownPixels);
        DecodeAhead(index + 1);
        return true;
    }

    public void Dispose()
    {
        Finished = true;
        if (_worker != null && !_stopping)
        {
            _stopping = true;
            _request.Set();
            _worker.Join();
        }
        _request.Dispose();
        _decoded.Dispose();
    }
}
