// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
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

    /// <summary>A tag changes no piece: DK2 tints the pieces the tile already draws (tag-highlight.md),
    /// so tagged rock keeps its plain top (art 3, "TOP TAGGED", is not what draws it).</summary>
    [InstallFact]
    public void ATagChangesNoPiece()
    {
        Patch patch = Block();
        TerrainPieces before = patch.At(X0 + 3, Y0 + 3);
        patch.World.Tile(X0 + 3, Y0 + 3).TaggedBy = PlayerMask.None.With(PlayerIds.Good);

        Assert.Equal(before, patch.At(X0 + 3, Y0 + 3));
    }

    /// <summary>A Reinforced Wall is Taggable, and a tagged one shows as tagged like rock
    /// (tag-highlight.md: the tint goes on any Taggable terrain).</summary>
    [InstallFact]
    public void ATaggedReinforcedWallShowsAsTagged()
    {
        Patch patch = Block("Reinforced Wall");
        patch.World.Tile(X0 + 3, Y0 + 3).OwnerPlayerId = PlayerIds.Good;
        patch.World.Tile(X0 + 3, Y0 + 3).TaggedBy = PlayerMask.None.With(PlayerIds.Good);

        Assert.True(patch.View.TileAt(patch.World, X0 + 3, Y0 + 3).Tagged);
    }

    /// <summary>While a tag drag is held, every Taggable tile inside its rectangle shows as tagged
    /// and floor does not (<c>0x0043E860</c>); with the drag over, none does. Both corners are taken
    /// as inclusive: what fills DK2's rectangle is unread (tag-highlight.md §4).</summary>
    [InstallFact]
    public void ATagDragShowsItsTaggableTilesAsTaggedTakingBothCornersAsInclusive()
    {
        Patch patch = Block();
        patch.Set(X0 + 2, Y0 + 2, "Dirt Path");
        patch.View.PreviewTags(new TilePos(X0 + 3, Y0 + 3), new TilePos(X0 + 1, Y0 + 1));

        Assert.True(patch.View.TileAt(patch.World, X0 + 1, Y0 + 3).Tagged);
        Assert.False(patch.View.TileAt(patch.World, X0 + 2, Y0 + 2).Tagged);
        Assert.False(patch.View.TileAt(patch.World, X0 + 4, Y0 + 3).Tagged);

        patch.View.PreviewTags(null, null);
        Assert.False(patch.View.TileAt(patch.World, X0 + 1, Y0 + 3).Tagged);
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

    /// <summary>A hero-owned Reinforced Wall draws the hero wall pieces instead of its plain face
    /// (0x0043E860, 0x0043F3A0..0x0043FC21), carried in <see cref="TileView.Walls"/>. A run of
    /// five along a floor: the middle one runs on both ways and draws one whole piece, 7 at odd x on
    /// a south face; the west end stops against rock, so it draws two halves, the A half running on
    /// (6) and the B half not (the room scheme's 5, which the hero set draws as its 4). A keeper's
    /// wall keeps its plain face.</summary>
    [InstallFact]
    public void AHeroWallDrawsHeroPiecesWholeInARunAndHalvesAtItsEnd()
    {
        Patch patch = Block();
        for (int x = X0 + 1; x <= X0 + 5; x++)
        {
            patch.Set(x, Y0 + 2, "Reinforced Wall", PlayerIds.Good);
            patch.Set(x, Y0 + 3, "Dirt Path");
        }

        TileView middle = patch.View.TileAt(patch.World, X0 + 3, Y0 + 2);
        Assert.False(middle.Pieces.HasFace(S));
        Assert.Equal(QuarterKind.Top, middle.Pieces.Quarters);
        Assert.True(middle.Walls.IsWhole(S));
        Assert.Equal(7, middle.Walls.WholePiece(S));
        Assert.False(middle.Walls.HasWall(N));

        RoomWalls end = patch.View.TileAt(patch.World, X0 + 1, Y0 + 2).Walls;
        Assert.True(end.HasWall(S));
        Assert.False(end.IsWhole(S));
        Assert.Equal(6, end.HalfAPiece(S));
        Assert.Equal(5, end.HalfBPiece(S));

        patch.Set(X0 + 3, Y0 + 2, "Reinforced Wall", Keeper);
        TileView keeper = patch.View.TileAt(patch.World, X0 + 3, Y0 + 2);
        Assert.True(keeper.Pieces.HasFace(S));
        Assert.True(keeper.Walls.IsEmpty);
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
