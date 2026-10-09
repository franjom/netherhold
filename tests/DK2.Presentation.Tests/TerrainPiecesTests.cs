// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>DK2's plain terrain builder <c>0x0043E860</c> (floor-and-terrain-pieces.md §5, Q71), on
/// patches carved into the shipped Level 1. Sides are N, E, S, W; quadrants NW, NE, SE, SW.</summary>
public class TerrainPiecesTests
{
    private const int N = 0, E = 1, S = 2, W = 3;
    private const int X0 = 10, Y0 = 10;
    private const byte Keeper = 3;

    private sealed record Patch(GameDatabase Database, WorldState World, WorldView View)
    {
        public void Set(int x, int y, string terrain, byte owner = 0)
        {
            ref TileState tile = ref World.Tile(x, y);
            tile.TerrainId = Database.Terrain.Values.Single(t => t.Name == terrain).TerrainId;
            tile.OwnerPlayerId = owner;
        }

        public TerrainPieces At(int x, int y) => View.TileAt(World, x, y).Pieces;
    }

    /// <summary>A 7×7 block of <paramref name="fill"/> with its top-left at (X0, Y0).</summary>
    private static Patch Block(string fill = "Rock")
    {
        string dir = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")!;
        var patch = new Patch(db, WorldState.FromMap(db, map, [0, 1, 2, Keeper]), new WorldView(db));
        for (int y = Y0; y < Y0 + 7; y++)
            for (int x = X0; x < X0 + 7; x++)
                patch.Set(x, y, fill);
        return patch;
    }

    [InstallFact]
    public void RockDrawsAFaceOnlyOnItsOpenSideAndATop()
    {
        Patch patch = Block();
        patch.Set(X0 + 4, Y0 + 3, "Dirt Path");

        TerrainPieces rock = patch.At(X0 + 3, Y0 + 3);
        Assert.True(rock.HasFace(E));
        Assert.False(rock.HasFace(N));
        Assert.False(rock.HasFace(S));
        Assert.False(rock.HasFace(W));
        Assert.True(rock.HasTop);
        Assert.False(rock.TopTagged);
        Assert.Equal(QuarterKind.None, rock.Quarters);
    }

    [InstallFact]
    public void DirtPathDrawsOneWholeFloor()
    {
        Patch patch = Block();
        patch.Set(X0 + 3, Y0 + 3, "Dirt Path");

        TerrainPieces dirt = patch.At(X0 + 3, Y0 + 3);
        Assert.True(dirt.HasFloor);
        Assert.False(dirt.HasTop);
        Assert.Equal(QuarterKind.None, dirt.Quarters);
    }

    /// <summary>A HYPOTHESIS from the art's name: the builder does not read art 3, so who draws
    /// the tagged top is unread.</summary>
    [InstallFact]
    public void TaggedRockDrawsTheTaggedTopByItsNameOnly()
    {
        Patch patch = Block();
        patch.World.Tile(X0 + 3, Y0 + 3).TaggedBy = PlayerMask.None.With(PlayerIds.Good);

        Assert.True(patch.At(X0 + 3, Y0 + 3).TopTagged);
    }

    /// <summary>Table <c>0x0066DA08</c>: a claimed tile with no claimed neighbour is four outer
    /// corners (piece 1), from its owner's set; set = player id − 1 rests on the sets' texture
    /// names (STRONGLY INFERRED).</summary>
    [InstallFact]
    public void AnIsolatedClaimedTileIsFourOuterCornersFromTheOwnersSetByName()
    {
        Patch patch = Block("Dirt Path");
        patch.Set(X0 + 3, Y0 + 3, "Claimed Path", Keeper);

        TerrainPieces claimed = patch.At(X0 + 3, Y0 + 3);
        Assert.Equal(QuarterKind.Floor, claimed.Quarters);
        Assert.Equal(Keeper - 1, claimed.QuarterSet);
        for (int q = 0; q < 4; q++) Assert.Equal(1, claimed.QuarterPiece(q));
    }

    [InstallFact]
    public void ClaimedFloorInsideClaimedFloorIsFourInteriorQuarters()
    {
        Patch patch = Block("Claimed Path");

        TerrainPieces claimed = patch.At(X0 + 3, Y0 + 3);
        for (int q = 0; q < 4; q++) Assert.Equal(3, claimed.QuarterPiece(q));
    }

    /// <summary>An east-west corridor's middle tile: each quadrant has one edge side (N or S) and
    /// its diagonal, giving the table's 3 (piece 0) on NW and SE and 6 (piece 4) on NE and SW.</summary>
    [InstallFact]
    public void AnEastWestClaimedCorridorAlternatesItsTwoStraightEdges()
    {
        Patch patch = Block("Dirt Path");
        for (int x = X0 + 2; x <= X0 + 4; x++) patch.Set(x, Y0 + 3, "Claimed Path", Keeper);

        TerrainPieces claimed = patch.At(X0 + 3, Y0 + 3);
        Assert.Equal([0, 4, 0, 4], Enumerable.Range(0, 4).Select(claimed.QuarterPiece));
        Assert.Equal([2, 3, 0, 1], Enumerable.Range(0, 4).Select(TerrainPieces.QuarterRotation));
    }

    /// <summary>A room that draws its own walls takes the face of an AllowRoomWalls tile beside it;
    /// Rock has no AllowRoomWalls, so it shows its own face to the room.</summary>
    [InstallFact]
    public void AReinforcedWallLeavesItsFaceToALairButRockDoesNot()
    {
        Patch patch = Block();
        string lair = patch.Database.Room(RoomTypeId.Lair).Name;
        patch.Set(X0 + 3, Y0 + 3, "Reinforced Wall", Keeper);
        patch.Set(X0 + 4, Y0 + 3, lair, Keeper);
        patch.Set(X0 + 5, Y0 + 2, lair, Keeper);

        TerrainPieces reinforced = patch.At(X0 + 3, Y0 + 3);
        Assert.False(reinforced.HasFace(E));
        Assert.Equal(QuarterKind.Top, reinforced.Quarters);
        Assert.False(reinforced.HasTop);
        Assert.True(patch.At(X0 + 5, Y0 + 3).HasFace(N));
    }

    /// <summary>The Heart (5×5 rotated) does not draw its own walls, so a Reinforced Wall beside it
    /// keeps its face; the Lair test above shows a room that does take it.</summary>
    [InstallFact]
    public void AReinforcedWallKeepsItsFaceToTheHeart()
    {
        Patch patch = Block("Reinforced Wall");
        patch.Set(X0 + 4, Y0 + 3, patch.Database.Room(RoomTypeId.DungeonHeart).Name, Keeper);

        Assert.True(patch.At(X0 + 3, Y0 + 3).HasFace(E));
    }

    /// <summary>A face toward the edge of the map is drawn (0x0043F1F6..0x0043F224).</summary>
    [InstallFact]
    public void AFaceTowardTheEdgeOfTheMapIsDrawn()
    {
        Patch patch = Block();
        patch.Set(0, Y0, "Rock");

        Assert.True(patch.At(0, Y0).HasFace(W));
    }

    [InstallFact]
    public void RoomsAndWaterDrawNoPlainPieces()
    {
        Patch patch = Block();
        patch.Set(X0 + 3, Y0 + 3, patch.Database.Room(RoomTypeId.Lair).Name, Keeper);
        patch.Set(X0 + 1, Y0 + 1, "Water");

        Assert.True(patch.At(X0 + 3, Y0 + 3).IsEmpty);
        Assert.True(patch.At(X0 + 1, Y0 + 1).IsEmpty);
    }
}
