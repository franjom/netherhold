// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class FrontEndSceneryTests
{
    [InstallFact]
    public void ShippedRoomUsesAllSixteenWholePiecesIncludingTheEntrance()
    {
        var (database, world) = Load();
        var view = new WorldView(database);
        var pieces = new List<int>();
        for (int y = 7; y < 12; y++)
            for (int x = 2; x <= 4; x++)
            {
                TileView tile = view.TileAt(world, x, y);
                Assert.True(tile.RoomFloor.HasWhole);
                Assert.Equal((x - 2) + 3 * (y - 7), tile.RoomFloor.WholePiece);
                Assert.Equal(0, tile.RoomFloor.WholeRotation);
                Assert.False(tile.RoomKeys!.Piece(tile.RoomFloor.WholePiece).IsEmpty);
                pieces.Add(tile.RoomFloor.WholePiece);
            }
        Assert.Equal(15, view.TileAt(world, 3, 12).RoomFloor.WholePiece);
        Assert.Equal(FrontEndAssets.EntranceCap(database.Room(RoomTypeId.HeroGateFrontEnd).Name),
            view.TileAt(world, 3, 12).ExtraRoomPiece);
        Assert.True(view.TileAt(world, 2, 11).ExtraRoomPiece.IsEmpty);
        Assert.Contains(view.TileAt(world, 3, 12).ExtraRoomPiece, FirstFrameArt.Keys(database, world, new PlayerId(1)));
        Assert.Equal(16, AssetCatalogue.RoomPieceCount(database.Room(RoomTypeId.HeroGateFrontEnd)));
    }

    [InstallFact]
    public void NativeSlabIndicesPlaceChainsBannersHolderAndBothTables()
    {
        var (database, world) = Load();
        var scenery = FrontEndScenery.Create(world, database, new CampaignProfile(), new SimRandom(1));
        Assert.Equal(8, scenery.Count(p => p.View.ObjectTypeId == 132));
        Assert.Equal(8, scenery.Count(p => p.View.ObjectTypeId is 89 or 134 or 135 or 136));
        var holder = Assert.Single(scenery, p => p.View.ObjectTypeId == 131);
        Assert.Equal(new TilePos(3, 7), holder.View.Tile);
        Assert.Equal(0x400, holder.View.Heading);
        var land = scenery.Where(p => p.MapPiece.HasValue).ToArray();
        Assert.Equal(30, land.Length);
        Assert.All(land, p => { Assert.Equal(new TilePos(3, 10), p.View.Tile); Assert.Equal(0x400, p.View.Heading); });
        Assert.Equal(FrontEndMapPiece.Level21, land[24].MapPiece);
        Assert.Equal(FrontEndMapPiece.Secret5, land[29].MapPiece);
        Assert.All(land.Skip(25), p => Assert.False(p.Visible));
        var mpd = scenery.Where(p => p.PetDungeonPiece.HasValue).ToArray();
        Assert.Equal(7, mpd.Length);
        Assert.All(mpd, p => { Assert.Equal(2 * 4096 + 0x19a, p.View.PosX); Assert.Equal(0x600, p.View.Heading); });
        Assert.Equal(54, scenery.Count);
        Assert.All(scenery, p => Assert.Equal(Fp32.One, p.View.Height));
        Assert.Equal(5, scenery.Count(p => p.View.Light is not null));
        Assert.NotNull(land[0].View.Light);
        Assert.All(land.Skip(1), p => Assert.Null(p.View.Light));
        Assert.All(scenery.Where(p => p.View.PlaysAnimation), p => Assert.Null(p.View.AnimationStartedAt));
        Assert.Equal(scenery.Count, scenery.Select(p => p.View.Id).Distinct().Count());
        Assert.Empty(world.Objects);
    }

    private static (GameDatabase, WorldState) Load()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "FrontEnd3DLevel");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "FrontEnd3DLevel")!;
        return (database, WorldState.FromMap(database, map, new byte[] { 1, 2, 3 }, fogOfWarEnabled: false));
    }
}
