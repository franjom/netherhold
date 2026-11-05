// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.Presentation;
using DK2.TestSupport;

namespace DK2.Assets.Tests;

public class ClassicSoundAssetsTests
{
    [InstallFact]
    public void TheProviderReadsInstalledMapsAndRetainsDecodedClips()
    {
        IClassicSoundAssets assets = new InstallClassicSoundAssets(Dk2InstallGate.Path);
        Assert.Equal(4, assets.Maps(SoundCategory.FrontEnd).Effects.Groups.Count);
        var reference = new ResolvedSound(@"Global\FrontEnd", new SoundEntryId(0));
        PcmClip? clip = assets.Clip(reference);
        Assert.NotNull(clip);
        Assert.Equal(26112, clip.Samples.Length);
        Assert.Same(clip, assets.Clip(reference));
    }

    // A level's categories decode on workers while it loads; afterwards a clip is ready without a decode.
    [InstallFact]
    public async Task APrefetchedCategoryIsReadyWithoutADecode()
    {
        var decoder = new SoundClipDecoder(new InstallClassicSoundAssets(Dk2InstallGate.Path));
        await decoder.Prefetch(new[] { SoundCategory.FrontEnd }, 2).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(decoder.TryGet(new ResolvedSound(@"Global\FrontEnd", new SoundEntryId(0)), out PcmClip? clip));
        Assert.Equal(26112, clip!.Samples.Length);
        Assert.False(decoder.TryTakeFailure(out _));
    }

    // Object 29 Training Target names OBJECT_TRAINING_TARGET, which the install does not ship; its requests
    // resolve to nothing instead of throwing on every request.
    [InstallFact]
    public void ACategoryTheInstallDoesNotShipHasEmptyMaps()
    {
        IClassicSoundAssets assets = new InstallClassicSoundAssets(Dk2InstallGate.Path);
        SoundMaps maps = assets.Maps(new SoundCategory("object_training_target"));
        Assert.Empty(maps.Banks.Archives);
        Assert.Empty(maps.Effects.Groups);
    }

    [Fact]
    public void ArchiveAndCategoryNamesCannotEscapeTheInstall()
    {
        var assets = new InstallClassicSoundAssets(".");
        Assert.Throws<ArgumentException>(() => assets.Maps(new SoundCategory("../outside")));
        Assert.Throws<ArgumentException>(() => assets.Clip(new ResolvedSound("../outside", new SoundEntryId(0))));
    }
}
