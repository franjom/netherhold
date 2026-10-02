// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary>Trigger action 32 against the shipped levels (hero-parties-and-effect-generators.md):
/// the one-based tile each record names, the room the map puts there, and that the ruin makes
/// every tile of each gate convertible.</summary>
public class HeroGatesOnRealDataTests(ITestOutputHelper output)
{
    /// <summary>Where the gate step-off can fire: an action 14 whose action point's centre is a gate
    /// tile, on 35 levels. On every one the step-off point is the spawn tile already: a 3x1's party
    /// stands on its middle tile, and a 2x2's lands where action 14's own adjustment puts it.</summary>
    [InstallFact]
    public void EveryShippedPartyRaisedOnAGateAlreadyStandsWhereTheStepOffWouldPutIt()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        string maps = Path.Combine(install, "Data", "editor", "maps");
        var onGate = new SortedDictionary<string, int>();
        var gateLevels = new SortedSet<string>();
        foreach (string file in Directory.EnumerateFiles(maps, "*Triggers.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Triggers.kld".Length];
            List<TriggerRecord>? triggers = GameDatabaseLoader.LoadCampaignTriggers(install, level);
            List<ThingRecord>? things = GameDatabaseLoader.LoadCampaignThings(install, level);
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (triggers is null || things is null || map is null) continue;
            var points = things.OfType<ActionPointThing>().GroupBy(a => (int)a.ActionPointId).ToDictionary(g => g.Key, g => g.First());
            foreach (ActionTriggerRecord action in triggers.OfType<ActionTriggerRecord>().Where(a => a.Type == 14))
            {
                if (!points.TryGetValue((int)action.Parameters["ActionPointId"], out ActionPointThing? ap) || ap is null) continue;
                int x = (ap.StartX + ap.EndX) / 2, y = (ap.StartY + ap.EndY) / 2;
                if (!map.Contains(x, y) || db.RoomByTerrain(map[x, y].TerrainId) is not { } room) continue;
                if ((RoomTypeId)room.RoomId is not (RoomTypeId.HeroGate2By2 or RoomTypeId.HeroGate3By1)) continue;
                // Action 14's own 2x2 adjustment, then the step-off: does it move the spawn?
                WorldState world = WorldState.FromMap(db, map, [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral]);
                var spawn = (RoomTypeId)room.RoomId == RoomTypeId.HeroGate2By2 ? new TilePos(x + 1, y + 1) : new TilePos(x, y);
                TilePos stepped = spawn;
                Assert.Equal(HeroPartyRules.StepOffResult.Moved, HeroPartyRules.StepOff(world, db, null, PlayerIds.Good, ref stepped, out _));
                string key = $"{level} {room.Name}{(stepped == spawn ? "" : $" moved {spawn}->{stepped}")}";
                onGate[key] = onGate.GetValueOrDefault(key) + 1;
                gateLevels.Add(level);
            }
        }
        foreach (var pair in onGate) output.WriteLine($"{pair.Key}: {pair.Value}");
        Assert.Equal(35, gateLevels.Count);
        Assert.DoesNotContain(onGate.Keys, k => k.Contains(" moved "));
    }

    [InstallFact]
    public void Action32NamesAGateOnSixtyFourOfSixtyFiveTiles_AndLevel7sReinforcedWallIsPredictedInert()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        string maps = Path.Combine(install, "Data", "editor", "maps");
        var hits = new List<string>();
        var levels = new SortedSet<string>();
        int gatesRuined = 0;

        foreach (string file in Directory.EnumerateFiles(maps, "*Triggers.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Triggers.kld".Length];
            List<TriggerRecord>? triggers = GameDatabaseLoader.LoadCampaignTriggers(install, level);
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (triggers is null || map is null) continue;
            WorldState world = WorldState.FromMap(db, map,
                [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper, (byte)(PlayerIds.FirstKeeper + 1)]);
            foreach (ActionTriggerRecord action in triggers.OfType<ActionTriggerRecord>().Where(a => a.Type == 32))
            {
                // The arm's own lookup: the room on the tile, ruined, then convertible by a keeper.
                int ax = (int)action.Parameters["PosX"] - 1, ay = (int)action.Parameters["PosY"] - 1;
                if (world.Contains(ax, ay) && RoomIndex.RoomAt(world, db, (ay * world.Width) + ax) is { } gate)
                {
                    Assert.True((RoomTypeId)gate.Definition.RoomId is RoomTypeId.HeroGate2By2 or RoomTypeId.HeroGate3By1, $"{level} ({ax},{ay})");
                    foreach (int tile in gate.TileIndices)
                    {
                        Assert.False(TileWorkRules.Applies(world, db, world.TileAtIndex(tile), TileWorkKind.ConvertRoom, PlayerIds.FirstKeeper));
                        world.TileAtIndex(tile).RoomRuined = true;
                        Assert.True(TileWorkRules.Applies(world, db, world.TileAtIndex(tile), TileWorkKind.ConvertRoom, PlayerIds.FirstKeeper));
                    }
                    foreach (int tile in gate.TileIndices) world.TileAtIndex(tile).RoomRuined = false;
                    gatesRuined++;
                }
                int x = (int)action.Parameters["PosX"] - 1, y = (int)action.Parameters["PosY"] - 1;
                string room = "off-map";
                if (x >= 0 && y >= 0 && x < map.Width && y < map.Height)
                {
                    byte terrain = map[x, y].TerrainId;
                    room = db.RoomByTerrain(terrain) is { } def ? $"{def.RoomId} {def.Name}" : $"terrain {terrain}";
                }
                hits.Add($"{level} ({x},{y}) {room}");
                levels.Add(level);
            }
        }

        output.WriteLine($"{hits.Count} records on {levels.Count} levels");
        foreach (string hit in hits) output.WriteLine(hit);
        List<string> tiles = hits.Distinct().ToList();
        output.WriteLine($"distinct {tiles.Count} gate2x2 {tiles.Count(t => t.EndsWith("20 Hero Gate [ 2x2 ]"))} gate3x1 {tiles.Count(t => t.EndsWith("23 Hero Gate [ 3x1 ]"))} other {string.Join("; ", tiles.Where(t => t.Contains("terrain") || t.Contains("off-map")))}");
        Assert.Equal(67, hits.Count);
        Assert.Equal(66, gatesRuined); // every record but Level 7's reaches a gate through the arm's own lookup
        Assert.Equal(17, levels.Count);
        Assert.Equal(65, tiles.Count);
        Assert.Equal(61, tiles.Count(t => t.EndsWith("20 Hero Gate [ 2x2 ]")));
        Assert.Equal(3, tiles.Count(t => t.EndsWith("23 Hero Gate [ 3x1 ]")));
        // The arm ruins only a room; on this wall it does nothing (a prediction, not an observation).
        Assert.Equal(["level7 (35,41) terrain 9"], tiles.Where(t => t.Contains("terrain")).ToList());
    }
}
