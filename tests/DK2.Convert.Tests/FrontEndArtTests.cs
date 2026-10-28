// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Core;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public class FrontEndArtTests
{
    [Fact]
    public void FrontEndAnimationStartsAtItsSeededPhaseWithoutAnArtificialAdvance()
    {
        var timing = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 8, FramesPerSecond = 8 };
        var clock = new RenderAnimationClock(timing, true, initialPhase: new Fp32(2048));
        Assert.Equal(.5f, clock.Sample(0).Phase);
        Assert.Equal(.5f, clock.Sample(1).Phase);
        Assert.Equal(0, clock.CompletedLoops);
    }

    [InstallFact]
    public void FrontEndPiecesUseOneBasedMeshNamesAndEveryTableResourceResolves()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "FrontEnd3DLevel");
        using var provider = InstallArtProvider.Open(install, database, FrontEndArt.Entries(database));
        RoomDef room = database.Room(RoomTypeId.HeroGateFrontEnd);
        for (int i = 0; i < 16; i++)
        {
            Assert.True(provider.TryResolve(AssetCatalogue.RoomPiece(room.Name, i), out var resolved));
            Assert.Equal($"3DFrontend{i + 1}.kmf", resolved.InstallEntry, ignoreCase: true);
            Assert.True(provider.TryLoadModel(resolved, out _));
        }
        foreach (var entry in FrontEndArt.Entries(database))
        {
            Assert.True(provider.TryResolve(entry.Reference, out var resolved), entry.Reference.Key);
            Assert.True(provider.TryLoadModel(resolved, out _), resolved.InstallEntry);
        }
        Assert.True(provider.TryResolve(FrontEndAssets.CampaignPiece(FrontEndMapPiece.Level6b), out var sixth));
        Assert.Equal("3dmap_level6b.kmf", sixth.InstallEntry, ignoreCase: true);
    }
}
