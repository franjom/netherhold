// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary>Every placed door of the shipped levels against the door update's kills
/// (door-blocking.md): its health from the definition, its tile's owner and its support walls, as
/// a first tick would test them.</summary>
public class DoorsOnRealDataTests(ITestOutputHelper output)
{
    [InstallFact]
    public void EveryPlacedDoorStartsAtItsDefinitionsHealth_AndTheFirstUpdateKillsOnlyLevel4sHeroDoor()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        string maps = Path.Combine(install, "Data", "editor", "maps");
        int levels = 0, doors = 0;
        var ownerKills = new List<string>();
        var supportKills = new List<string>();
        var byType = new SortedDictionary<string, int>();

        foreach (string file in Directory.EnumerateFiles(maps, "*Things.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Things.kld".Length];
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            List<ThingRecord>? things = GameDatabaseLoader.LoadCampaignThings(install, level);
            if (map is null || things is null || !things.OfType<DoorThing>().Any()) continue;

            WorldState world = WorldState.FromMap(
                db, map, [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper,
                    (byte)(PlayerIds.FirstKeeper + 1), (byte)(PlayerIds.FirstKeeper + 2), (byte)(PlayerIds.FirstKeeper + 3), (byte)(PlayerIds.FirstKeeper + 4)]);
            ThingProjection.Place(world, db, things);
            var navigation = new WorldSteering(db, world);
            navigation.Refresh();
            levels++;

            foreach (DoorState door in world.Doors)
            {
                doors++;
                DoorDef def = db.Doors[door.DoorId];
                byType[def.Name] = byType.GetValueOrDefault(def.Name) + 1;
                Assert.Equal(def.Health, door.Health);
                Assert.Equal(0, door.RecoveryDeadline);

                var probe = new DoorState
                {
                    X = door.X, Y = door.Y, DoorId = door.DoorId, PlayerId = door.PlayerId,
                    Orientation = door.Orientation, Health = door.Health, Unknown_0011 = new byte[3],
                };
                if (DoorRules.OwnerLost(world, probe))
                    ownerKills.Add($"{level} ({door.X},{door.Y}) {def.Name} p{door.PlayerId} tile p{world.Tile(door.X, door.Y).OwnerPlayerId}");
                probe.Health = door.Health;
                if (!def.Flags.HasFlag(DoorFlags.Barricade) && DoorRules.SupportLost(navigation, probe))
                    supportKills.Add($"{level} ({door.X},{door.Y}) {def.Name} orientation {door.Orientation}");
            }
        }

        output.WriteLine($"levels {levels}, doors {doors}: {string.Join(", ", byType.Select(p => $"{p.Key} {p.Value}"))}");
        output.WriteLine($"owner kills {ownerKills.Count}: {string.Join("; ", ownerKills.Take(40))}");
        output.WriteLine($"support kills {supportKills.Count}: {string.Join("; ", supportKills.Take(40))}");
        Assert.Equal(46, levels);
        Assert.Equal(940, doors);
        Assert.Empty(supportKills);
        // The read owner test kills this one at once: a hero door on a tile the map gives the Neutral player.
        // Consistent with Level 4 seed 1's capture: no door there from tick 2, floor on its water (Q600).
        Assert.Equal(["level4 (24,9) Wooden Door p1 tile p2"], ownerKills);
    }
}
