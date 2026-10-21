// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;
using System.Numerics;

namespace DK2.Presentation.Tests;

/// <summary>The wall torch rule <c>0x00439B90</c> on the shipped Level 1 (wall-torches.md, ADR-0391).</summary>
public class TorchLayoutTests
{
    [Theory]
    [InlineData(1536, 2048, 3584, 2048, 4095)]
    [InlineData(0, 512, 2048, 0, 2048)]
    [InlineData(512, 2048, 512, 2048, 0)]
    [InlineData(1024, 3584, 2048, 4095, 2048)]
    public void MeshMountReachesWallWhileLightStaysOffIt(int yaw, int lightX, int lightY, int meshX, int meshY)
    {
        var torch = new TorchView(0, default, lightX, lightY, 6144, yaw, 300, 200, 100, 180, 6144, default);
        Assert.Equal(meshX, torch.MeshPosX);
        Assert.Equal(meshY, torch.MeshPosY);
        Assert.Equal(lightX, torch.PosX);
        Assert.Equal(lightY, torch.PosY);
    }

    private static readonly int[] Dx = [0, 1, 0, -1];

    [InstallFact]
    public void TorchMountsFollowTheSameBentSurfaceAsTheirWall()
    {
        (GameDatabase db, WorldState world) = Load();
        var layout = new TorchLayout(db);
        layout.Update(world);
        var lattice = new TerrainLattice(db, world, 1);
        int moved = 0;
        for (int i = 0; i < world.TileCount; i++)
        {
            if (layout.At(i) is not TorchView torch) continue;
            Vector3 straight = TorchGeometry.Vertex(torch, Vector3.Zero, null);
            Vector3 wallPoint = lattice.Deform(new Vector3(straight.X, -straight.Z, -straight.Y), torch.Wall.X, torch.Wall.Y);
            Vector3 wallMount = new Vector3(wallPoint.X, -wallPoint.Z, -wallPoint.Y);
            if (Vector3.Distance(straight, wallMount) > 0.001f) moved++;
            Assert.True(Vector3.Distance(wallMount, TorchGeometry.Vertex(torch, Vector3.Zero, lattice)) < 0.00001f);
        }
        Assert.True(moved > 0);
    }

    [Theory]
    [InlineData(0, 1f, 0f)]
    [InlineData(512, 0f, -1f)]
    [InlineData(1024, -1f, 0f)]
    [InlineData(1536, 0f, 1f)]
    public void StraightTorchVerticesKeepTheirYawAndHeight(int yaw, float x, float z)
    {
        var torch = new TorchView(0, default, 512, 2048, 6144, yaw, 300, 200, 100, 180, 6144, default);
        Vector3 mount = TorchGeometry.Vertex(torch, Vector3.Zero, null);
        Vector3 point = TorchGeometry.Vertex(torch, new Vector3(1, 0.25f, 0), null);
        Assert.True(Vector3.Distance(point - mount, new Vector3(x, 0.25f, z)) < 0.00001f);
    }
    private static readonly int[] Dy = [-1, 0, 1, 0];

    private sealed record Level(GameDatabase Database, WorldState World);

    private static Level Load()
    {
        string dir = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")
            ?? throw new InvalidOperationException("the install has no Level1 map.");
        return new Level(database, WorldState.FromMap(database, map, [0, 1]));
    }

    /// <summary>Every torch hangs on a Torch wall, over a floor that is not a door, on a face
    /// whose parity passes; and every floor with such a face has one (the first wall takes it).</summary>
    [InstallFact]
    public void EveryEligibleFloorHasOneTorch_OnAWallThatQualifies()
    {
        (GameDatabase db, WorldState world) = Load();
        var layout = new TorchLayout(db);
        layout.Update(world);

        int torches = 0;
        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
            {
                int index = (y * world.Width) + x;
                bool eligible = !Solid(db, world, x, y) && world.DoorAt(x, y) is null
                    && Enumerable.Range(0, 4).Any(d => FaceOk(db, world, x, y, d));
                TorchView? torch = layout.At(index);
                Assert.Equal(eligible, torch.HasValue);
                if (torch is not TorchView t) continue;
                torches++;
                int face = Enumerable.Range(0, 4).Single(d => new TilePos(x - Dx[d], y - Dy[d]) == t.Wall);
                Assert.True(FaceOk(db, world, x, y, face));
            }
        }

        Assert.True(torches > 20, $"Level 1 lit {torches} torches");
    }

    /// <summary>Level 1's Lord's hall, against the owner's DK2 shots SHOT0065..0078: the level
    /// initialiser refreshes every Torch wall in row order once the map is complete (Q446), so a
    /// floor's N wall wins, then W, then E, then S. (17,10) takes the east wall (18,10) over the
    /// south wall (17,11), which the earlier setter-order layout gave it, and (15,11) has none.</summary>
    [InstallFact]
    public void TheLordsHallTorchesMatchDk2()
    {
        string dir = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(dir, "level1");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "level1")!;
        WorldState world = WorldState.FromMap(db, map,
            GameDatabaseLoader.LoadCampaignPlayers(dir, "level1")!.Select(p => p.PlayerId).ToArray());
        var layout = new TorchLayout(db);
        layout.Update(world);

        TilePos? WallOf(int x, int y) => layout.At((y * world.Width) + x)?.Wall;
        Assert.Equal(new TilePos(15, 5), WallOf(15, 6));
        Assert.Equal(new TilePos(17, 6), WallOf(16, 6));
        Assert.Equal(new TilePos(17, 7), WallOf(17, 8));
        Assert.Equal(new TilePos(13, 8), WallOf(14, 8));
        Assert.Equal(new TilePos(13, 10), WallOf(14, 10));
        Assert.Equal(new TilePos(18, 10), WallOf(17, 10));
        Assert.Null(WallOf(15, 11));
    }

    /// <summary>A torch over a room's floor takes the room's colour and art; over plain floor the
    /// balance defaults and the bridge's Torch1 (wall-torches.md §4).</summary>
    [InstallFact]
    public void ATorchTakesItsFloorsRoomOrTheDefaults()
    {
        (GameDatabase db, WorldState world) = Load();
        var layout = new TorchLayout(db);
        layout.Update(world);

        bool sawRoom = false, sawPlain = false;
        for (int index = 0; index < world.TileCount; index++)
        {
            if (layout.At(index) is not TorchView torch) continue;
            RoomTorch? room = db.RoomByTerrain(world.TileAtIndex(index).TerrainId)?.Torch;
            if (room is null)
            {
                sawPlain = true;
                Assert.Equal((300, 200, 100), (torch.Red, torch.Green, torch.Blue));
                Assert.Equal(AssetCatalogue.TorchMesh("Torch1"), torch.Mesh);
            }
            else
            {
                sawRoom = true;
                Assert.Equal((room.Red, room.Green, room.Blue), (torch.Red, torch.Green, torch.Blue));
                Assert.Equal(Math.Min(room.Radius, TorchLayout.MaxRadius), torch.Radius);
            }
        }

        Assert.True(sawRoom && sawPlain);
    }

    /// <summary>Digging out a torch's wall puts that torch out: the setter refreshes the dug tile,
    /// which no longer qualifies (<c>0x0044FC40</c>, <c>0x00439B90</c> step 2).</summary>
    [InstallFact]
    public void DiggingOutATorchsWallPutsItOut()
    {
        (GameDatabase db, WorldState world) = Load();
        var layout = new TorchLayout(db);
        layout.Update(world);
        int floor = Enumerable.Range(0, world.TileCount).First(i =>
            layout.At(i) is TorchView t && !db.TerrainById(world.Tile(t.Wall.X, t.Wall.Y).TerrainId).IsImpenetrable);
        TilePos wall = layout.At(floor)!.Value.Wall;

        world.Tile(wall.X, wall.Y).TerrainId = TerrainOf(db, "Dirt Path");
        IReadOnlyList<int> changed = layout.Update(world);

        Assert.Contains(floor, changed);
        Assert.True(layout.At(floor) is not TorchView t || t.Wall != wall);
    }

    /// <summary>A room built under a torch recolours it and keeps its mesh: the room-change closers
    /// post command 16 (<c>0x00448710</c>), which sets the colour alone (Q445). That nothing else
    /// changes is STRONGLY INFERRED: the closers' other refreshes find the floor already lit.</summary>
    [InstallFact]
    public void ARoomBuiltUnderATorchRecoloursIt_AndKeepsItsMesh_StronglyInferred()
    {
        (GameDatabase db, WorldState world) = Load();
        var layout = new TorchLayout(db);
        layout.Update(world);
        int floor = Enumerable.Range(0, world.TileCount).First(i =>
            layout.At(i) is not null && db.RoomByTerrain(world.TileAtIndex(i).TerrainId) is null);
        TorchView before = layout.At(floor)!.Value;
        RoomDef temple = db.Room(RoomTypeId.Temple);

        world.TileAtIndex(floor).TerrainId = temple.TerrainId;
        layout.Update(world);

        TorchView after = layout.At(floor)!.Value;
        Assert.Equal((temple.Torch!.Red, temple.Torch.Green, temple.Torch.Blue), (after.Red, after.Green, after.Blue));
        Assert.Equal(before.Mesh, after.Mesh);
        Assert.Equal(before.Intensity, after.Intensity);
    }

    private static bool FaceOk(GameDatabase db, WorldState world, int fx, int fy, int d)
    {
        int wx = fx - Dx[d], wy = fy - Dy[d];
        if (!world.Contains(wx, wy) || world.DoorAt(wx, wy) is not null) return false;
        if (!db.TerrainById(world.Tile(wx, wy).TerrainId).Flags.HasFlag(TerrainFlags.Torch)) return false;
        return d % 2 == 0 ? (fx & 1) == 1 : (fy & 1) == 0;
    }

    private static bool Solid(GameDatabase db, WorldState world, int x, int y) =>
        db.TerrainById(world.Tile(x, y).TerrainId).IsSolid;

    private static byte TerrainOf(GameDatabase db, string name) =>
        db.Terrain.Values.Single(t => t.Name == name).TerrainId;
}
