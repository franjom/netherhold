// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Media;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class EaMoviePlaybackTests
{
    // SCHl (defaults: mono, 22050 Hz, raw PCM), then audio 4 frames, video, audio, video, audio, end.
    private static EaMovieTimeline Interleaved()
    {
        var file = new List<byte>();
        Chunk(file, "SCHl", Encoding.ASCII.GetBytes("PT\0\0"));
        Chunk(file, "SCDl", Pcm(100, 200, 300, 400));
        Chunk(file, "pIQT", new byte[8]);
        Chunk(file, "SCDl", Pcm(500, 600, 700, 800));
        Chunk(file, "pIQT", new byte[8]);
        Chunk(file, "SCDl", Pcm(900, 1000, 1100, 1200));
        Chunk(file, "SCEl", []);
        return new EaMovieTimeline(EaMovie.Read(file.ToArray()));
    }

    [Theory]
    [InlineData(0, 0)]  // first block plays: no frame preceded it
    [InlineData(3, 0)]
    [InlineData(4, 1)]  // the block after frame 0 starts
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(12, 2)] // audio ended: every frame
    public void AFrameIsShownOnceTheAudioBlockThatFollowsItStartsPlaying(long audioFrame, int showable)
    {
        Assert.Equal(showable, Interleaved().ShowableFrames(audioFrame));
    }

    [Theory]
    [InlineData(-1, 0, -1)] // nothing due yet
    [InlineData(-1, 1, 0)]
    [InlineData(0, 4, 1)]   // two behind: decode the next one
    [InlineData(0, 5, 4)]   // three behind: discard undecoded and jump to the due frame
    [InlineData(4, 5, -1)]
    public void TheDisplayCatchesUpFrameByFrameUntilItIsThreeBehind(int shown, int showable, int next)
    {
        Assert.Equal(next, EaMovieTimeline.NextFrameToDecode(shown, showable));
    }

    [Fact]
    public void TheAudioStreamCrossesBlocksDuplicatesMonoAndPadsWithSilence()
    {
        var stream = new EaAudioStream(Interleaved());
        stream.Seek(3);
        var samples = new float[2 * 3];

        Assert.Equal(3, stream.Read(samples));
        Assert.Equal(new[] { 400, 400, 500, 500, 600, 600 }, samples.Select(s => (int)Math.Round(s * 32768)));

        stream.Seek(11);
        Assert.Equal(1, stream.Read(samples));
        Assert.Equal(new[] { 1200, 1200, 0, 0, 0, 0 }, samples.Select(s => (int)Math.Round(s * 32768)));
    }

    [InstallFact]
    public void BullfrogIntrosInterleaveShowsAFramePer896SamplesAndTheStreamReadsToTheEnd()
    {
        var timeline = new EaMovieTimeline(EaMovie.Open(Path.Combine(Dk2InstallGate.Path, "Data", "Movies", "BullfrogIntro.tgq")));

        Assert.Equal(timeline.VideoFrames.Count, timeline.ShowableFrames(timeline.TotalAudioFrames));
        long oneSecond = timeline.Movie.SampleRate;
        // Two frames lead the first audio block, then one per block: about 2 + 5 s * 22050 / 896.
        Assert.InRange(timeline.ShowableFrames(5 * oneSecond), 118, 132);

        var stream = new EaAudioStream(timeline);
        var buffer = new float[2 * 1000];
        long read = 0;
        int got;
        while ((got = stream.Read(buffer)) > 0) read += got;
        Assert.Equal(timeline.TotalAudioFrames, read);
    }

    private static byte[] Pcm(params short[] samples)
    {
        var payload = new byte[4 + 2 * samples.Length];
        BinaryPrimitives.WriteInt32LittleEndian(payload, samples.Length);
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(4 + 2 * i), samples[i]);
        return payload;
    }

    private static void Chunk(List<byte> file, string tag, byte[] payload)
    {
        file.AddRange(Encoding.ASCII.GetBytes(tag));
        var size = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, payload.Length + 8);
        file.AddRange(size);
        file.AddRange(payload);
    }
}
