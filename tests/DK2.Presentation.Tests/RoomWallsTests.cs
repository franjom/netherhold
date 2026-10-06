// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>DK2's room wall chooser <c>0x00444F10</c> (wall-rendering.md, Q71), on a room carved
/// into the shipped Level 1: sides are N, E, S, W.</summary>
public class RoomWallsTests
{
    private const int N = 0, E = 1, S = 2, W = 3;
    private const int X0 = 10, Y0 = 10;

    private sealed record Room(GameDatabase Database, WorldState World, WorldView View);

    /// <summary>A Treasury three wide and four deep inside a ring of Reinforced Wall, its top-left
    /// tile at (X0 + 1, Y0 + 1).</summary>
    private static Room Treasury(string ring = "Reinforced Wall") => Carve(RoomTypeId.Treasury, ring);

    private static Room Carve(RoomTypeId type, string ring = "Reinforced Wall")
    {
        string dir = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")!;
        WorldState world = WorldState.FromMap(db, map, [0, 1]);
        byte wall = db.Terrain.Values.Single(t => t.Name == ring).TerrainId;
        byte floor = db.Room(type).TerrainId;
        for (int y = Y0; y <= Y0 + 5; y++)
            for (int x = X0; x <= X0 + 4; x++)
                world.Tile(x, y).TerrainId = x == X0 || x == X0 + 4 || y == Y0 || y == Y0 + 5 ? wall : floor;
        return new Room(db, world, new WorldView(db));
    }

    /// <summary>The middle of a straight wall is one whole piece, 7 (no furniture phase is kept),
    /// and a side against more room has no wall.</summary>
    [InstallFact]
    public void TheMiddleOfAStraightWallIsOneWholePiece()
    {
        Room room = Treasury();
        RoomWalls walls = room.View.TileAt(room.World, X0 + 2, Y0 + 1).Walls;

        Assert.True(walls.IsWhole(N));
        Assert.Equal(7, walls.WholePiece(N));
        Assert.False(walls.HasWall(E));
        Assert.False(walls.HasWall(S));
        Assert.False(walls.HasWall(W));
    }

    /// <summary>A corner tile's two walls are halves: an end piece (4 on the A half, 5 on the B
    /// half) where the wall stops, and 6 where it runs on.</summary>
    [InstallFact]
    public void ACornerDrawsAnEndHalfAndARunningHalfOnEachWall()
    {
        Room room = Treasury();
        RoomWalls walls = room.View.TileAt(room.World, X0 + 1, Y0 + 1).Walls;

        // N: its A half faces W, where the room stops; its B half runs on east.
        Assert.False(walls.IsWhole(N));
        Assert.Equal((4, 6), (walls.HalfAPiece(N), walls.HalfBPiece(N)));
        // W: its A half faces S, where the room runs on; its B half faces N, where it stops.
        Assert.Equal((6, 5), (walls.HalfAPiece(W), walls.HalfBPiece(W)));
    }

    /// <summary>A one-tile room is all ends.</summary>
    [InstallFact]
    public void AOneTileRoomIsAllEnds()
    {
        Room room = Treasury();
        byte wall = room.World.Tile(X0, Y0).TerrainId;
        for (int y = Y0 + 1; y <= Y0 + 4; y++)
            for (int x = X0 + 1; x <= X0 + 3; x++)
                if (x != X0 + 2 || y != Y0 + 2) room.World.Tile(x, y).TerrainId = wall;

        RoomWalls walls = room.View.TileAt(room.World, X0 + 2, Y0 + 2).Walls;
        for (int side = 0; side < 4; side++)
            Assert.Equal((4, 5), (walls.HalfAPiece(side), walls.HalfBPiece(side)));
    }

    /// <summary>A DoubleQuad room (the Temple) takes the same general chooser.</summary>
    [InstallFact]
    public void ADoubleQuadRoomTakesTheSameWalls()
    {
        Room room = Carve(RoomTypeId.Temple);
        Assert.Equal(TileConstruction.DoubleQuad, room.Database.Room(RoomTypeId.Temple).Construction);

        Assert.Equal(7, room.View.TileAt(room.World, X0 + 2, Y0 + 1).Walls.WholePiece(N));
        RoomWalls corner = room.View.TileAt(room.World, X0 + 3, Y0 + 4).Walls;
        // The bottom-right corner: E's A half faces N, where the room runs on; S's faces E, where it stops.
        Assert.Equal((6, 5), (corner.HalfAPiece(E), corner.HalfBPiece(E)));
        Assert.Equal((4, 6), (corner.HalfAPiece(S), corner.HalfBPiece(S)));
    }

    /// <summary>Only an AllowRoomWalls tile takes a room wall: against Rock the rock shows its own
    /// face (wall-rendering.md).</summary>
    [InstallFact]
    public void RockTakesNoRoomWall()
    {
        Room room = Treasury(ring: "Rock");
        Assert.True(room.View.TileAt(room.World, X0 + 2, Y0 + 1).Walls.IsEmpty);
    }

    /// <summary>A Portal (3×3 construction) does not use the general chooser.</summary>
    [InstallFact]
    public void APortalDrawsNoRoomWalls()
    {
        Room room = Treasury();
        room.World.Tile(X0 + 2, Y0 + 1).TerrainId = room.Database.Room(RoomTypeId.Portal).TerrainId;
        Assert.True(room.View.TileAt(room.World, X0 + 2, Y0 + 1).Walls.IsEmpty);
    }
}
