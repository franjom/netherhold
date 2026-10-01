// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary>A Torture Chamber's places are its auto-placed devices (<c>0x004E3ED0</c>), and a
/// victim's claim needs one (ADR-0309). No shipped map places a device as a THINGS object, so
/// every pre-built chamber must get its devices from the room itself. Swept over every map.</summary>
public class TortureChamberPlacesOnRealDataTests(ITestOutputHelper output)
{
    private static IEnumerable<string> LevelNames(string install)
    {
        string maps = Path.Combine(install, "Data", "editor", "maps");
        if (!Directory.Exists(maps)) yield break;

        foreach (string file in Directory.EnumerateFiles(maps, "*Map.kld").OrderBy(f => f))
            yield return Path.GetFileName(file)[..^"Map.kld".Length];
    }

    [InstallFact]
    public void EveryPreBuiltTortureChamberHasAPlaceOnceFurnished()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        var empty = new List<string>();
        int chambers = 0;

        foreach (string level in LevelNames(install))
        {
            LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, level)!;
            WorldState world = WorldState.FromMap(db, map, [0, 1, 2, 3, 4, 5, 6, 7]);
            var rooms = RoomIndex.Build(world, db).Where(r => r.Definition.TypeId == RoomTypeId.TortureChamber).ToList();
            if (rooms.Count == 0) continue;

            new GameSimulation(db, world, 1, [new AutoFurnitureSystem()]).Tick();
            foreach (RoomInstance room in RoomIndex.Build(world, db).Where(r => r.Definition.TypeId == RoomTypeId.TortureChamber))
            {
                chambers++;
                AutoFurnitureRules.TryTotalCapacity(world, room, out int capacity);
                output.WriteLine($"{level} owner={room.OwnerPlayerId} {room.Width}x{room.Height} tiles={room.TileCount} places={capacity}");
                if (capacity == 0) empty.Add($"{level} owner={room.OwnerPlayerId} {room.Width}x{room.Height}");
            }
        }

        output.WriteLine($"{chambers} chambers, {empty.Count} with no place");
        Assert.True(chambers > 0);
        Assert.Empty(empty);
    }
}
