// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class PortalCreatureActionOnRealDataTests
{
    [InstallFact]
    public void ShippedAction28CreatureAndLevelPairsDoNotDrift()
    {
        string install = Dk2InstallGate.Path;
        var rows = new List<string>();
        foreach (string path in Directory.GetFiles(Path.Combine(install, "Data", "editor", "maps"), "*Triggers.kld"))
        {
            string level = Path.GetFileName(path)[..^"Triggers.kld".Length];
            foreach (TriggerRecord record in GameDatabaseLoader.LoadCampaignTriggers(install, level) ?? [])
                if (record.Tag == 214 && record.Type == 28)
                    rows.Add($"{level.ToLowerInvariant()}:{record.TriggerId}:{record.Parameters["CreatureId"]}:{record.Parameters["Level"]}");
        }
        rows.Sort(StringComparer.Ordinal);
        Assert.Equal(
            "level12:65489:24:3;level1:65526:7:1;level2:65531:5:1;level3:65477:10:1;" +
            "level3:65504:12:1;level7:65438:7:1;level7:65439:7:1;level7:65500:7:1;" +
            "level8:65392:24:1;level9:65463:3:1", string.Join(";", rows));
    }

    /// <summary>The Level 2 recording: every Warlock spends 21 ticks in state 193 and every
    /// Goblin 28, and each then stands one tile south of the portal centre.</summary>
    [InstallFact]
    public void ShippedEntranceDataGivesTheRecordedJoiningTimesAndStep()
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "level2");
        CreatureDef warlock = db.CreatureById(5), goblin = db.CreatureById(7), imp = db.CreatureById(1);

        Assert.Equal((0, 2929, 147, 30), (warlock.EntranceOffsetX, warlock.EntranceOffsetY, warlock.EntranceFrames, warlock.EntranceFramesPerSecond));
        Assert.Equal((43, 3139, 197, 30), (goblin.EntranceOffsetX, goblin.EntranceOffsetY, goblin.EntranceFrames, goblin.EntranceFramesPerSecond));
        Assert.Equal(21, PortalRules.JoiningTicks(warlock) + 1);
        Assert.Equal(28, PortalRules.JoiningTicks(goblin) + 1);
        Assert.Equal(0, PortalRules.JoiningTicks(imp));
    }

    [InstallFact]
    public void LevelTwoSixteenTileLibraryAdmitsWarlocksAtTurn635WithNoPriorWarlocks()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "level2");
        CreatureDef warlock = db.CreatureById(5);
        Assert.Equal(new CreatureAttraction(1, 6, 0), warlock.Attractions[0]);
        Assert.Equal(new CreatureAttraction(0, 0, 0), warlock.Attractions[1]);
        WorldState world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "level2")!, [0, 1, 2, 3]);
        RoomDef library = db.Room(RoomTypeId.Library);
        for (int y = 5; y < 9; y++)
            for (int x = 5; x < 9; x++)
                RoomRules.Place(db, ref world.Tile(x, y), library, 3);
        var simulation = new GameSimulation(db, world, 99, [new AutoFurnitureSystem()]);
        simulation.Run(635);
        RoomInstance[] owned = RoomIndex.Build(world, db).Where(r => r.OwnerPlayerId == 3).ToArray();
        Assert.True(PortalRules.CanComeFromPortal(warlock, db.Room(RoomTypeId.Portal)));
        Assert.True(PortalRules.IsUnlocked(world, 3, 5));
        Assert.True(PortalRules.PoolLimit(db, 3, 5) > 0);
        Assert.True(PortalRules.IsAttracted(warlock, owned));
        Assert.True(AttractionBudget.Admits(world, warlock, owned, 3));
        int budget = owned.Where(r => r.Definition.TypeId == RoomTypeId.Library)
            .Sum(r => AttractionBudget.Contribution(world, r));
        Assert.True(budget > 0);
        for (int i = 0; i < budget; i++) world.SpawnCreature(db, 5, 3, new TilePos(6, 6));
        Assert.False(AttractionBudget.Admits(world, warlock, owned, 3));
    }
}
