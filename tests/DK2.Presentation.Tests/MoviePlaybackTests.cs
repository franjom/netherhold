// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Media;
using DK2.Presentation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class MoviePlaybackTests
{
    private const int Rate = 22050;

    [Fact]
    public void ASilentMovieShowsItsFirstFrameAfterTheStartDelayAndTheRestAtDk2sRate()
    {
        using var movie = new MoviePlayback(EaMovie.Read(Silent(3)), "silent");

        Assert.False(movie.Update(0.05, 0));
        Assert.False(movie.HasFrame);
        Assert.True(movie.Update(MoviePlayback.StartDelaySeconds, 0));
        Assert.Equal(0, movie.ShownFrame);
        Assert.Equal(new byte[] { 115, 113, 115, 255 }, movie.Pixels.Slice(0, 4).ToArray());
        Assert.Equal(2f * 16 / 16, movie.DisplayAspect);

        Assert.True(movie.Update(MoviePlayback.StartDelaySeconds + 1.01 / EaMovieTimeline.SilentFramesPerSecond, 0));
        Assert.Equal(1, movie.ShownFrame);
        Assert.False(movie.Finished);
        Assert.True(movie.Update(10, 0));
        Assert.True(movie.Finished);
        Assert.Equal(0, movie.FramesDropped);
    }

    [Fact]
    public void TheAudioClockPacesTheFramesAfterTheDelaysSilence()
    {
        // Frame 0, a 100-frame block, frame 1, a 100-frame block: frame 1 is due when the second block starts.
        using var movie = new MoviePlayback(EaMovie.Read(Interleaved()), "audio");
        int delay = (int)Math.Round(MoviePlayback.StartDelaySeconds * Rate);
        float[] samples = new float[2 * (delay + 150)];

        movie.ReadAudio(samples);

        Assert.All(samples.Take(2 * delay), s => Assert.Equal(0f, s));
        Assert.Equal(1000, (int)Math.Round(samples[2 * delay] * 32768));
        Assert.Equal(2000, (int)Math.Round(samples[2 * (delay + 100)] * 32768));
        Assert.False(movie.Update(0, delay - 1));
        Assert.True(movie.Update(0, delay));
        Assert.Equal(0, movie.ShownFrame);
        Assert.False(movie.Update(0, delay + 99));
        Assert.True(movie.Update(0, delay + 100));
        Assert.Equal(1, movie.ShownFrame);
        Assert.False(movie.Finished);
        Assert.False(movie.Update(0, delay + 200));
        Assert.True(movie.Finished);
        Assert.Equal(0, movie.LagDriftMs);
    }

    [Fact]
    public void FramesThreeBehindArePassedOverUndecoded()
    {
        using var movie = new MoviePlayback(EaMovie.Read(Silent(8)), "silent");

        Assert.True(movie.Update(MoviePlayback.StartDelaySeconds, 0));
        Assert.True(movie.Update(MoviePlayback.StartDelaySeconds + 6.01 / EaMovieTimeline.SilentFramesPerSecond, 0));

        Assert.Equal(6, movie.ShownFrame);
        Assert.Equal(5, movie.FramesDropped);
    }

    [Theory]
    [InlineData(MovieInput.Escape, true)]
    [InlineData(MovieInput.Enter, true)]
    [InlineData(MovieInput.KeypadEnter, true)]
    [InlineData(MovieInput.Space, true)]
    [InlineData(MovieInput.LeftButton, true)]
    [InlineData(MovieInput.RightButton, true)]
    [InlineData(MovieInput.MiddleButton, true)]
    [InlineData(MovieInput.Other, false)]
    public void EscEnterSpaceOrAMouseButtonSkips(MovieInput input, bool skips)
    {
        using var movie = new MoviePlayback(EaMovie.Read(Silent(3)), "silent");

        Assert.Equal(skips, movie.Press(input));
        Assert.Equal(skips, movie.Finished);
        Assert.Equal(skips, movie.Skipped);
        if (skips)
        {
            Assert.False(movie.Update(10, 0));
        }
    }

    [Fact]
    public void SteadyPlaybackAllocatesNothingOnTheFrameThread()
    {
        using var movie = new MoviePlayback(EaMovie.Read(Silent(40)), "silent");
        float[] samples = new float[2 * 512];
        movie.Update(MoviePlayback.StartDelaySeconds, 0);
        movie.ReadAudio(samples);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 1; frame < 40; frame++)
        {
            movie.Update(MoviePlayback.StartDelaySeconds + (frame + .01) / EaMovieTimeline.SilentFramesPerSecond, 0);
            movie.ReadAudio(samples);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(39, movie.ShownFrame);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void AMissingMovieReportsWhyAndReturnsNull()
    {
        Assert.Null(MoviePlayback.TryOpen(Path.GetTempPath(), "Data/Movies/none-such.tgq", out string? error));
        Assert.Contains("none-such.tgq", error);
    }

    [InstallFact]
    public void BullfrogIntroPlaysToItsEndByTheAudioClockAndShowsTheDecodersFrames()
    {
        using MoviePlayback movie = MoviePlayback.TryOpen(Dk2InstallGate.Path, "Data/Movies/BullfrogIntro.tgq", out _)!;
        float[] samples = new float[2 * 735];
        var decoder = new TqiDecoder();
        var file = EaMovie.Open(Path.Combine(Dk2InstallGate.Path, "Data", "Movies", "BullfrogIntro.tgq"));
        EaMovieChunk[] frames = [.. file.Chunks.Where(c => c.Kind == EaMovieChunkKind.Video)];
        byte[] expected = new byte[movie.Width * movie.Height * 4];
        long played = 0;

        while (!movie.Finished && played < 60L * Rate)
        {
            movie.ReadAudio(samples);
            played += samples.Length / 2;
            if (movie.Update(0, played) && movie.ShownFrame % 47 == 0)
            {
                decoder.Decode(file.Payload(frames[movie.ShownFrame]), expected);
                Assert.True(movie.Pixels.SequenceEqual(expected), $"frame {movie.ShownFrame}");
            }
        }

        Assert.True(movie.Finished);
        Assert.Equal(frames.Length - 1, movie.ShownFrame);
        Assert.Equal((640f / 480f), movie.DisplayAspect);
    }

    // SCHl (mono, 22050 Hz, raw PCM), then the grey frame n times.
    private static byte[] Silent(int frames)
    {
        var file = new List<byte>();
        for (int i = 0; i < frames; i++)
        {
            Chunk(file, "pIQT", GreyFrame());
        }

        Chunk(file, "SCEl", []);
        return [.. file];
    }

    private static byte[] Interleaved()
    {
        var file = new List<byte>();
        Chunk(file, "SCHl", Encoding.ASCII.GetBytes("PT\0\0"));
        Chunk(file, "pIQT", GreyFrame());
        Chunk(file, "SCDl", Pcm(100, 1000));
        Chunk(file, "pIQT", GreyFrame());
        Chunk(file, "SCDl", Pcm(100, 2000));
        Chunk(file, "SCEl", []);
        return [.. file];
    }

    // A 16x16 DC-only frame at mid chroma: every pixel decodes to (115, 113, 115) (EaMovieTests).
    private static byte[] GreyFrame()
    {
        var bits = new StringBuilder();
        bits.Append("1111110").Append("10000000").Append("10");
        for (int i = 0; i < 3; i++)
        {
            bits.Append("100").Append("10");
        }

        for (int i = 0; i < 2; i++)
        {
            bits.Append("11111110").Append("10000000").Append("10");
        }

        string all = bits.ToString().PadRight((bits.Length + 31) / 32 * 32, '0');
        byte[] frame = new byte[8 + all.Length / 8];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, 16);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), 16);
        frame[4] = 100;
        frame[7] = 3;
        for (int w = 0; w < all.Length / 32; w++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8 + w * 4), Convert.ToUInt32(all.Substring(w * 32, 32), 2));
        }

        return frame;
    }

    private static byte[] Pcm(int count, short value)
    {
        byte[] payload = new byte[4 + 2 * count];
        BinaryPrimitives.WriteInt32LittleEndian(payload, count);
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(4 + 2 * i), value);
        }

        return payload;
    }

    private static void Chunk(List<byte> file, string tag, byte[] payload)
    {
        file.AddRange(Encoding.ASCII.GetBytes(tag));
        byte[] size = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, payload.Length + 8);
        file.AddRange(size);
        file.AddRange(payload);
    }
}
