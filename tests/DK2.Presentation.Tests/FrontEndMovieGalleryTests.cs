// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Wad;
using DK2.Art.Textures;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class FrontEndMovieGalleryTests
{
    [InstallFact]
    public void EveryLockedAndUnlockedThumbnailResolvesInTheShippedArchive()
    {
        using var archive = WadArchive.Open(Path.Combine(Dk2InstallGate.Path, "Data", "FrontEnd.WAD"));
        var index = new WadDirectoryIndex(archive.Entries);
        var won = Enumerable.Repeat(CampaignLevelStatus.Won, 40).ToArray();
        foreach (var profile in new[] { new CampaignProfile(), new CampaignProfile { PlayerLevelStatus = won } })
        {
            var gallery = new FrontEndMovieGallery(profile, _ => true);
            foreach (var row in gallery.Entries)
                Assert.InRange(index.Find(row.Thumbnail + ".png"), 0, archive.Entries.Count - 1);
        }
        Assert.InRange(index.Find("Mov_Shots/M-Hilight.png"), 0, archive.Entries.Count - 1);
    }

    [Fact]
    public void ResetCampaignRefreshRemovesPreviouslyUnlockedMovies()
    {
        var status = new CampaignLevelStatus[40]; status[0] = CampaignLevelStatus.Won;
        var gallery = new FrontEndMovieGallery(new CampaignProfile { PlayerLevelStatus = status }, _ => true);
        Assert.True(gallery.Entries[1].Available);
        gallery.Refresh(new CampaignProfile());
        Assert.False(gallery.Entries[1].Available);
        Assert.True(gallery.Entries[0].Available);
    }

    [Fact]
    public void IntroIsAvailableAndUnwonMoviesUseTheUnavailableThumbnail()
    {
        var gallery = new FrontEndMovieGallery(new CampaignProfile(), _ => true);
        Assert.True(gallery.Entries[0].Available);
        Assert.Equal("Data/Movies/Intro.tgq", gallery.Entries[0].Path);
        Assert.False(gallery.Entries[1].Available);
        Assert.Equal("Mov_Shots/M-Unavailable-0", gallery.Entries[1].Thumbnail);
    }
    [Fact]
    public void AnyWonBranchUnlocksItsMovieButAMissingFileStaysDisabled()
    {
        var status = new CampaignLevelStatus[40]; status[6] = CampaignLevelStatus.Won;
        var profile = new CampaignProfile { PlayerLevelStatus = status };
        var gallery = new FrontEndMovieGallery(profile, _ => true);
        Assert.True(gallery.Entries[6].Available);
        Assert.Equal("Mov_Shots/M-005-0", gallery.Entries[6].Thumbnail);
        Assert.False(new FrontEndMovieGallery(profile, _ => false).Entries[6].Available);
        gallery.ScrollTo(99);
        Assert.Equal(17, gallery.FirstRow);
        Assert.Equal(5, gallery.Visible.Count);
    }
}
