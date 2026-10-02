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

/// <summary>Every placed trap of the shipped levels against the trap update (traps.md): which ones
/// have a built firing, their seeded health, the Trigger counts the loader's completion leaves, and
/// the traps the first update's owner test kills.</summary>
public class TrapsFireOnRealDataTests(ITestOutputHelper output)
{
    [InstallFact]
    public void EveryPlacedTrapIsListedWithItsScope_TheTriggerCountsMatchTheirNeighbours_AndTheFirstUpdateKillsOnlyLevel20sHeroBoulder_PredictedNotObserved()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        string maps = Path.Combine(install, "Data", "editor", "maps");
        int levels = 0, traps = 0, built = 0, completed = 0;
        var byType = new SortedDictionary<string, int>();
        var ownerKills = new List<string>();
        var triggerNeighbours = new List<string>();
        var guardPosts = new List<string>();

        foreach (string file in Directory.EnumerateFiles(maps, "*Things.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Things.kld".Length];
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            List<ThingRecord>? things = GameDatabaseLoader.LoadCampaignThings(install, level);
            if (map is null || things is null || !things.OfType<TrapThing>().Any()) continue;

            WorldState world = WorldState.FromMap(
                db, map, [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper,
                    (byte)(PlayerIds.FirstKeeper + 1), (byte)(PlayerIds.FirstKeeper + 2), (byte)(PlayerIds.FirstKeeper + 3), (byte)(PlayerIds.FirstKeeper + 4)]);
            ThingProjection.Place(world, db, things);
            levels++;

            foreach (TrapState trap in world.Traps)
            {
                traps++;
                TrapDef def = db.Traps[trap.TrapId];
                bool firing = TrapRules.FiringBuilt(db, def);
                if (firing) built++;
                if (!trap.UnderConstruction) completed++;
                string key = $"{def.Name}{(firing ? "" : " (not built)")}";
                byType[key] = byType.GetValueOrDefault(key) + 1;
                Assert.Equal(def.Health, trap.Health);
                Assert.Equal((def.Flags & TrapRules.StartsHidden) != 0, trap.Hidden);

                // The count the loader's completions leave is the completed own Triggers on the four sides.
                int expected = trap.UnderConstruction || def.TriggerType == TrapRules.TriggerSelector ? 0
                    : world.Traps.Count(t => t != trap && t.PlayerId == trap.PlayerId && !t.UnderConstruction
                        && db.Traps[t.TrapId].TriggerType == TrapRules.TriggerSelector
                        && Math.Abs(t.X - trap.X) + Math.Abs(t.Y - trap.Y) == 1);
                Assert.True(trap.UnderConstruction || trap.TriggerCount == expected, $"{level} ({trap.X},{trap.Y}) {def.Name}");
                if ((def.Flags & TrapRules.GuardPostBranch) != 0)
                    guardPosts.Add($"{level} ({trap.X},{trap.Y}) p{trap.PlayerId} room {(trap.GuardRoomTile < 0 ? "none" : $"({trap.GuardRoomTile % world.Width},{trap.GuardRoomTile / world.Width})")}");
                if (trap.TriggerCount != 0) triggerNeighbours.Add($"{level} ({trap.X},{trap.Y}) {def.Name} x{trap.TriggerCount}");

                if (world.Tile(trap.X, trap.Y).OwnerPlayerId != trap.PlayerId)
                    ownerKills.Add($"{level} ({trap.X},{trap.Y}) {def.Name} p{trap.PlayerId} tile p{world.Tile(trap.X, trap.Y).OwnerPlayerId}");
            }
        }

        output.WriteLine($"levels {levels}, traps {traps}, firing built {built}, completed {completed}");
        foreach (var pair in byType) output.WriteLine($"  {pair.Key}: {pair.Value}");
        output.WriteLine($"beside a Trigger {triggerNeighbours.Count}: {string.Join("; ", triggerNeighbours.Take(40))}");
        output.WriteLine($"guard posts: {string.Join("; ", guardPosts)}");
        output.WriteLine($"owner kills {ownerKills.Count}: {string.Join("; ", ownerKills.Take(40))}");
        Assert.Equal((39, 959, 959), (levels, traps, completed));
        // Sentry 115, Hero Sentry 320, Lightning 80, Freeze 41, Fireburst 7 and Trigger 86 fire, and the
        // 4 Guard Posts, whose shot 0 ends the firing at once, run the gate; no level places a Magic Door
        // trap. The rest wait on their shots' unread handlers.
        Assert.Equal(653, built);
        Assert.Equal(
            ["Alarm Trap (not built)", "Boulder Trap (not built)", "Fear Trap (not built)", "Fireburst Trap", "Freeze Trap",
             "Gas Trap (not built)", "Guard Post", "Hero Alarm Trap (not built)", "Hero Sentry Trap", "Lightning Trap",
             "Sentry Trap", "Spike Trap (not built)", "Trigger Trap"],
            byType.Keys);
        Assert.Equal([2, 97, 41, 7, 41, 79, 4, 16, 320, 80, 115, 71, 86], byType.Values);
        Assert.Equal(36, triggerNeighbours.Count);
        // The owner test kills this one on its first update: a hero trap on a tile the map gives
        // the Neutral player. Whether it stands in the running game is the prediction's check (traps.md).
        Assert.Equal(["Level20 (12,5) Boulder Trap p1 tile p2"], ownerKills);
    }
}
