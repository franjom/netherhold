// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The navigation cache is filled by a faster path than the one it replaced: a table per
/// terrain id instead of a lookup per tile, and only the DoubleQuad rooms flood-filled instead of
/// every room. These pin that the bytes, and so every mesh and route, are the same as the old
/// path's on shipped levels, at the start and after a stretch of play.</summary>
public class NavigationBytesOnRealDataTests
{
    private static readonly string[] LevelNames = ["Level1", "Level4", "Level8", "Level10", "Level11A", "Level17", "Level18"];

    public static TheoryData<string> Levels => new(LevelNames);

    /// <summary>One pass over every level, so the coverage the comparison relies on can be
    /// asserted across them: a level without a DoubleQuad room, a portal or a closed door would
    /// make that part of the comparison pass by vacuity.</summary>
    [InstallFact]
    public void TheCacheIsTheOldPathsByteForByte()
    {
        int doubleQuadRooms = 0, portalHoles = 0, closedDoors = 0;
        foreach (string level in LevelNames)
        {
            GameSimulation simulation = Campaign(level);
            for (int stretch = 0; stretch < 3; stretch++)
            {
                byte[] fast = WorldSteering.CaptureNavigationBytes(simulation.Database, simulation.World);
                byte[] reference = OldPath(simulation.Database, simulation.World);
                Assert.True(fast.AsSpan().SequenceEqual(reference),
                    $"{level} at tick {simulation.CurrentTick.Value}: first difference at tile {FirstDifference(fast, reference)}");

                doubleQuadRooms += RoomIndex.Build(simulation.World, simulation.Database)
                    .Count(r => r.Definition.Construction == TileConstruction.DoubleQuad);
                portalHoles += fast.Count(b => b == NavBytes.PortalHole);
                closedDoors += simulation.World.Doors.Count(d => !d.UnderConstruction && !d.Open);
                simulation.Run(150);
            }
        }

        Assert.True(doubleQuadRooms > 0, "no level held a DoubleQuad room");
        Assert.True(portalHoles > 0, "no level held a portal hole");
        Assert.True(closedDoors > 0, $"no level held a closed door (DoubleQuad rooms {doubleQuadRooms}, portal holes {portalHoles})");
    }

    [InstallTheory]
    [MemberData(nameof(Levels))]
    public void AFilteredSweepIsTheFullSweepFiltered(string level)
    {
        GameSimulation simulation = Campaign(level);
        Predicate<RoomDef> wanted = room => room.Construction == TileConstruction.DoubleQuad || room.TypeId == RoomTypeId.Treasury;
        string Describe(IEnumerable<RoomInstance> rooms) =>
            string.Join(" | ", rooms.Select(r => $"{r.Definition.TypeId}:{r.OwnerPlayerId}:{string.Join(",", r.TileIndices)}"));

        Assert.Equal(
            Describe(RoomIndex.Build(simulation.World, simulation.Database).Where(r => wanted(r.Definition))),
            Describe(RoomIndex.Build(simulation.World, simulation.Database, wanted)));
    }

    /// <summary>Unoptimized per-tile/full-room fill with the current navigation writers.</summary>
    private static byte[] OldPath(GameDatabase database, WorldState world)
    {
        var bytes = new byte[world.TileCount];
        StructureBlocks structures = StructureBlocks.For(world, database);
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                int index = (y * world.Width) + x;
                bytes[index] = WorldSteering.ByteOf(database.TerrainById(world.Tile(x, y).TerrainId), structures.IsPortalCentre(index));
            }

        foreach (RoomInstance room in RoomIndex.Build(world, database))
            if (room.Definition.Construction == TileConstruction.DoubleQuad)
                foreach (int tile in AutoFurnitureRules.FloorSlotSet(world, room)) bytes[tile] = NavBytes.PortalHole;

        foreach (NavStamp stamp in world.NavStamps)
            if (world.TileAtIndex(stamp.Tile).TerrainId == stamp.TerrainId)
                bytes[stamp.Tile] = stamp.Byte;

        foreach (DoorState door in world.Doors)
        {
            if (door.UnderConstruction || door.Open || !world.Contains(door.X, door.Y)) continue;
            bool barricade = database.Doors.TryGetValue(door.DoorId, out DoorDef? def) && def.Flags.HasFlag(DoorFlags.Barricade);
            bytes[(door.Y * world.Width) + door.X] =
                NavBytes.Door(door.PlayerId, door.LockMode != 0 || barricade, world.Tile(door.X, door.Y).IsTaggedByAnyone);
        }
        return bytes;
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return i;
        return -1;
    }

    private static GameSimulation Campaign(string level)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        return CampaignSimulationFactory.Create(db,
            GameDatabaseLoader.LoadCampaignMap(install, level)!,
            GameDatabaseLoader.LoadCampaignLevel(install, level)!,
            GameDatabaseLoader.LoadCampaignPlayers(install, level)!,
            GameDatabaseLoader.LoadCampaignThings(install, level)!,
            GameDatabaseLoader.LoadCampaignTriggers(install, level)!, 1);
    }
}
