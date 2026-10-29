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

    [Fact]
    public void ArchiveAndCategoryNamesCannotEscapeTheInstall()
    {
        var assets = new InstallClassicSoundAssets(".");
        Assert.Throws<ArgumentException>(() => assets.Maps(new SoundCategory("../outside")));
        Assert.Throws<ArgumentException>(() => assets.Clip(new ResolvedSound("../outside", new SoundEntryId(0))));
    }
}
