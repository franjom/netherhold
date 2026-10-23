// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Media;

/// <summary>
/// When each video frame of an <see cref="EaMovie"/> may be shown, as DKII.EXE paces it
/// (<c>docs/research/movie-format.md</c> §5): there is no frame rate; every audio block remembers
/// how many video frames preceded it in the file, and frame n is shown once the audio block that
/// follows it starts playing. DK2 also subtracts its DirectSound buffer lead (L = 9 blocks for
/// BullfrogIntro, <c>0x005A8B20</c>); that lead belongs to its 32 KB looping buffer and is not
/// reproduced here. Engine-neutral: a host only supplies how far its audio output has played.
/// </summary>
public sealed class EaMovieTimeline
{
    /// <summary>The rate a movie without audio runs at: DK2's fake play cursor (22050 Hz,
    /// <c>0x005AC180</c>) over BullfrogIntro's 896-sample interleave.</summary>
    public const double SilentFramesPerSecond = 22050.0 / 896.0;

    /// <summary>The number of video frames DK2 is allowed to fall behind before it discards TQI
    /// frames undecoded (<c>0x005A8B20</c>, <c>+0x98</c>).</summary>
    public const int SkipThreshold = 3;

    private readonly long[] _blockStart;
    private readonly int[] _videosBefore;

    public EaMovieTimeline(EaMovie movie)
    {
        Movie = movie ?? throw new ArgumentNullException(nameof(movie));
        var video = new List<EaMovieChunk>();
        var audio = new List<EaMovieChunk>();
        var start = new List<long>();
        var before = new List<int>();
        long frames = 0;
        foreach (EaMovieChunk chunk in movie.Chunks)
        {
            if (chunk.Kind == EaMovieChunkKind.Video) video.Add(chunk);
            else if (chunk.Kind == EaMovieChunkKind.Audio)
            {
                int length = EaAudio.FrameCount(movie.Payload(chunk), movie.Channels, movie.Compression);
                if (length <= 0) continue; // never playing, so it never paces a frame
                audio.Add(chunk);
                start.Add(frames);
                before.Add(video.Count);
                frames += length;
            }
        }
        VideoFrames = video.ToArray();
        AudioBlocks = audio.ToArray();
        _blockStart = start.ToArray();
        _videosBefore = before.ToArray();
        TotalAudioFrames = frames;
    }

    public EaMovie Movie { get; }

    /// <summary>The <c>pIQT</c> chunks in file order.</summary>
    public IReadOnlyList<EaMovieChunk> VideoFrames { get; }

    /// <summary>The audio data chunks in file order that hold at least one sample frame.</summary>
    public IReadOnlyList<EaMovieChunk> AudioBlocks { get; }

    /// <summary>Stereo sample frames across every audio block.</summary>
    public long TotalAudioFrames { get; }

    public bool HasAudio => TotalAudioFrames > 0;

    /// <summary>The sample frame at which audio block <paramref name="block"/> starts.</summary>
    public long BlockStart(int block) => _blockStart[block];

    /// <summary>The audio block that holds sample frame <paramref name="frame"/>, or the block
    /// count when it lies past the end.</summary>
    public int BlockAt(long frame)
    {
        if (frame < 0) return 0;
        if (frame >= TotalAudioFrames) return _blockStart.Length;
        int index = Array.BinarySearch(_blockStart, frame);
        return index >= 0 ? index : ~index - 1;
    }

    /// <summary>How many video frames may be on screen once the audio has played
    /// <paramref name="audioFrame"/> sample frames: the video count of the block playing now, or
    /// every frame once the audio has ended.</summary>
    public int ShowableFrames(long audioFrame)
    {
        int block = BlockAt(audioFrame);
        return block >= _videosBefore.Length ? VideoFrames.Count : _videosBefore[block];
    }

    /// <summary><see cref="ShowableFrames(long)"/> for a movie without audio: frame n is due at
    /// n / <see cref="SilentFramesPerSecond"/> seconds.</summary>
    public int ShowableFramesWithoutAudio(double seconds) =>
        seconds < 0 ? 0 : (int)Math.Min(VideoFrames.Count, Math.Floor(seconds * SilentFramesPerSecond) + 1);

    /// <summary>The next frame to decode after <paramref name="shown"/> (-1 before the first), given
    /// <paramref name="showable"/> frames are due; -1 when none is due yet. Frames that leave the
    /// display <see cref="SkipThreshold"/> or more behind are passed over undecoded, as DK2 does
    /// for TQI frames.</summary>
    public static int NextFrameToDecode(int shown, int showable)
    {
        int target = showable - 1;
        int next = shown + 1;
        if (next > target) return -1;
        return target - next >= SkipThreshold ? target : next;
    }
}

/// <summary>
/// A movie's audio as one continuous stream of interleaved stereo floats, decoded block by block
/// on demand with <see cref="EaAudio"/>; nothing is pre-decoded or written anywhere. Not
/// thread-safe: a host calls it from one thread at a time (typically its audio callback).
/// </summary>
public sealed class EaAudioStream
{
    private readonly EaMovieTimeline _timeline;
    private short[] _block = new short[2 * 4096];
    private int _blockIndex = -1;
    private int _blockFrames;
    private long _position;

    public EaAudioStream(EaMovieTimeline timeline) =>
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));

    /// <summary>The next sample frame <see cref="Read"/> returns.</summary>
    public long Position => _position;

    public void Seek(long frame) => _position = Math.Max(0, Math.Min(frame, _timeline.TotalAudioFrames));

    /// <summary>Fills <paramref name="interleaved"/> (L, R, L, R ...) from the current position,
    /// silence past the end, and returns the number of stereo frames taken from the movie.</summary>
    public int Read(Span<float> interleaved)
    {
        int wanted = interleaved.Length / 2;
        int written = 0;
        while (written < wanted && _position < _timeline.TotalAudioFrames)
        {
            int block = _timeline.BlockAt(_position);
            if (block != _blockIndex) DecodeBlock(block);
            int offset = (int)(_position - _timeline.BlockStart(block));
            int take = Math.Min(_blockFrames - offset, wanted - written);
            if (take <= 0) break; // a block shorter than the timeline counted; never expected
            for (int i = 0; i < take * 2; i++)
                interleaved[written * 2 + i] = _block[offset * 2 + i] * (1f / 32768f);
            written += take;
            _position += take;
        }
        interleaved[(written * 2)..].Clear();
        return written;
    }

    private void DecodeBlock(int block)
    {
        EaMovie movie = _timeline.Movie;
        ReadOnlySpan<byte> payload = movie.Payload(_timeline.AudioBlocks[block]);
        int frames = EaAudio.FrameCount(payload, movie.Channels, movie.Compression);
        if (_block.Length < frames * 2) _block = new short[frames * 2];
        _blockFrames = EaAudio.DecodeBlock(payload, movie.Channels, movie.Compression, _block);
        _blockIndex = block;
    }
}
