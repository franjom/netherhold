// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;

namespace DK2.Presentation.Tests;

/// <summary>The off-frame clip decoder: a clip not yet decoded is reported as not ready and decoded once on a worker,
/// and a clip that fails to decode becomes silence and one reported failure, not an exception on the frame's thread.</summary>
public sealed class SoundClipDecoderTests
{
    private static readonly ResolvedSound Reference = new("Global/Test", new SoundEntryId(3));

    private sealed class Assets(Func<ResolvedSound, PcmClip?> clip) : IClassicSoundAssets
    {
        public int Calls;
        public SoundMaps Maps(SoundCategory category) => new(BankMap.Empty, SfxMap.Empty);
        public PcmClip? Clip(ResolvedSound reference)
        {
            Interlocked.Increment(ref Calls);
            return clip(reference);
        }
    }

    private static PcmClip? WaitFor(SoundClipDecoder decoder, ResolvedSound reference)
    {
        PcmClip? clip = null;
        Assert.True(SpinWait.SpinUntil(() => decoder.TryGet(reference, out clip), TimeSpan.FromSeconds(10)));
        return clip;
    }

    [Fact]
    public void AClipIsNotReadyUntilAWorkerHasDecodedItOnce()
    {
        using var release = new ManualResetEventSlim();
        var pcm = new PcmClip(22050, 1, new short[] { 1, 2, 3 });
        var assets = new Assets(_ => { release.Wait(); return pcm; });
        var decoder = new SoundClipDecoder(assets);

        Assert.False(decoder.TryGet(Reference, out _));
        Assert.False(decoder.TryGet(Reference, out _));
        release.Set();

        Assert.Same(pcm, WaitFor(decoder, Reference));
        Assert.Equal(1, assets.Calls);
        Assert.Equal(3, decoder.DecodedSamples);
    }

    [Fact]
    public void AnEntryWithNoAudioIsReadyAsSilence()
    {
        var decoder = new SoundClipDecoder(new Assets(_ => null));
        decoder.TryGet(Reference, out _);
        Assert.Null(WaitFor(decoder, Reference));
        Assert.False(decoder.TryTakeFailure(out _));
    }

    [Fact]
    public void AClipThatFailsIsSilenceAndOneReportedFailure()
    {
        var assets = new Assets(_ => throw new InvalidDataException("Not a valid MPEG file!"));
        var decoder = new SoundClipDecoder(assets);
        decoder.TryGet(Reference, out _);

        Assert.Null(WaitFor(decoder, Reference));
        Assert.True(decoder.TryTakeFailure(out string failure));
        Assert.Contains("Global/Test:3", failure);
        Assert.False(decoder.TryTakeFailure(out _));
        Assert.Equal(1, assets.Calls);
    }

    [Fact]
    public async Task APrefetchOfCategoriesWithNoSamplesCompletes()
    {
        var assets = new Assets(_ => null);
        var decoder = new SoundClipDecoder(assets);
        await decoder.Prefetch(new[] { new SoundCategory("object_training_target"), SoundCategory.None }, 2).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, assets.Calls);
    }
}
