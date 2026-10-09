// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Convert;
using DK2.Data;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public class ArtCatalogueTests
{
    [InstallFact]
    public void ShippedArtRecordsMatchGameplayDefinitionCounts()
    {
        string install = Dk2InstallGate.Path;

        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(install);
        var database = GameDatabaseLoader.Load(install);

        Assert.Equal(database.Terrain.Count, art.Terrains.Count);
        Assert.Equal(database.Rooms.Count, art.Rooms.Count);
        Assert.Equal(database.Creatures.Count, art.Creatures.Count);
        Assert.Equal(database.Objects.Count, art.Objects.Count);
        Assert.Equal("ROCK WALL", art.Terrains[2][1].Name);
        Assert.Equal("Imp_Idle1", art.Creatures[1][19].Name);
        Assert.Equal("Gold Pile", art.Objects[1][0].Name);
    }

    /// <summary>A KMF clip's rate and frame count are its descriptor's words at <c>+0x48</c> and
    /// <c>+0x44</c>, the same fields the
    /// gameplay reader takes for the knockdown clock (catalogue index 23 is slot 24, Q442).</summary>
    [InstallFact]
    public void AnAnimationResourceCarriesItsDescriptorsRate()
    {
        string install = Dk2InstallGate.Path;
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(install);
        var database = GameDatabaseLoader.Load(install);

        Dk2ArtResource knockdown = art.Creatures[1][23];
        Assert.Equal(6, knockdown.Type);
        Assert.True(knockdown.FramesPerSecond > 0);
        Assert.Equal(database.Creatures[1].KnockdownAnimation.FramesPerSecond, knockdown.FramesPerSecond);
        Assert.Equal(database.Creatures[1].KnockdownAnimation.FrameCount, knockdown.FrameCount);
    }
}
