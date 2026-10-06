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

/// <summary>The wall torch rule <c>0x00439B90</c> on the shipped Level 1 (wall-torches.md, ADR-0391).</summary>
public class TorchLayoutTests
{
    private static readonly int[] Dx = [0, 1, 0, -1];
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
