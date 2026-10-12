// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Core;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>Job 22 (Wait, state 176) is reachable in shipped data, so the dispatcher's hold counter
/// <c>+0x44</c> that <c>0x00486CC0</c> arms is not dead code (Q499). Our <c>HeroJobRules.Wait</c> has no
/// hold; this pins the population that would show it, so a change to that rule is made knowingly.</summary>
public class HeroWaitJobOnRealDataTests
{
    private const byte WaitJob = 22;
    private const byte LeaderBit = 2;

    [InstallFact]
    public void Level8NearestHeartHatcherySpawnsItsGuardAndDwarf_AndTheGuardMovesWhileWaiting()
    {
        string install = Dk2InstallGate.Path;
        const string level = "Level8";
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        GameSimulation sim = CampaignSimulationFactory.Create(db,
            GameDatabaseLoader.LoadCampaignMap(install, level)!,
            GameDatabaseLoader.LoadCampaignLevel(install, level)!,
            GameDatabaseLoader.LoadCampaignPlayers(install, level)!,
            GameDatabaseLoader.LoadCampaignThings(install, level)!,
            GameDatabaseLoader.LoadCampaignTriggers(install, level)!, seed: 1);
        sim.Tick();
        TilePos spawn = new(27, 43);
        CreatureState guard = Assert.Single(sim.World.Creatures, c => c.Tile == spawn
            && c.OwnerPlayerId == PlayerIds.Good && db.CreatureById(c.CreatureTypeId).Name == "Guard");
        CreatureState dwarf = Assert.Single(sim.World.Creatures, c => c.Tile == spawn
            && c.OwnerPlayerId == PlayerIds.Good && db.CreatureById(c.CreatureTypeId).Name == "Dwarf");
        Assert.Equal(guard.PartyId, dwarf.PartyId);
        Assert.NotEqual(HeroPartyRules.NoParty, guard.PartyId);
        Assert.Equal(JobType.Wait, guard.GoodJob);
        int x = guard.PosX, y = guard.PosY;
        bool moved = false;
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            moved |= guard.PosX != x || guard.PosY != y;
        }
        Assert.True(moved);
        Assert.True(guard.Health > 0);
        Assert.Contains(guard, sim.World.Creatures);
        Assert.Equal(JobType.Wait, guard.GoodJob);
    }

    [InstallFact]
    public void Level8TrainingRoomGuardWandersAfterItsHuntCannotStart()
    {
        string install = Dk2InstallGate.Path;
        const string level = "Level8";
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        GameSimulation sim = CampaignSimulationFactory.Create(db,
            GameDatabaseLoader.LoadCampaignMap(install, level)!, GameDatabaseLoader.LoadCampaignLevel(install, level)!,
            GameDatabaseLoader.LoadCampaignPlayers(install, level)!, GameDatabaseLoader.LoadCampaignThings(install, level)!,
            GameDatabaseLoader.LoadCampaignTriggers(install, level)!, seed: 1);
        CreatureState guard = Assert.Single(sim.World.Creatures, c => c.Tile == new TilePos(19, 26)
            && db.CreatureById(c.CreatureTypeId).Name == "Guard");
        Assert.Equal(JobType.KillCreatures, guard.GoodJob);
        int startX = guard.PosX, startY = guard.PosY;
        bool wandered = false, moved = false;
        for (int i = 0; i < 1500 && !moved; i++)
        {
            sim.Tick();
            wandered |= guard.Dk2StateId == ImpStates.Wandering;
            moved |= Math.Abs(guard.PosX - startX) > 1024 || Math.Abs(guard.PosY - startY) > 1024;
            Assert.Equal(0, guard.TrainingActionId);
        }
        Assert.True(wandered);
        Assert.True(moved, "the failed-hunt idle wrapper must let the guard leave its dummy-centre spawn");
    }

    [InstallFact]
    public void JobWaitIsSetByShippedPartiesPlacedCreaturesAndScripts_Q499HoldCounterIsUnbuilt()
    {
        string install = Dk2InstallGate.Path;
        string maps = Path.Combine(install, "Data", "editor", "maps");
        int leaders = 0, followers = 0, placed = 0, scripted = 0;

        foreach (string file in Directory.EnumerateFiles(maps, "*Things.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Things.kld".Length];
            foreach (ThingRecord record in GameDatabaseLoader.LoadCampaignThings(install, level) ?? [])
            {
                if (record is GoodCreatureThing good && good.Objective == WaitJob) placed++;
                if (record is not HeroPartyThing party) continue;
                foreach (GoodCreatureThing m in party.Members.Where(m => m.CreatureId != 0 && m.Objective == WaitJob))
                {
                    if ((m.Flags & LeaderBit) != 0) leaders++;
                    else followers++;
                }
            }

            foreach (TriggerRecord t in GameDatabaseLoader.LoadCampaignTriggers(install, level) ?? [])
                if (t is ActionTriggerRecord && t.Type == 15 && t.Parameters["ObjectiveType"] == WaitJob) scripted++;
        }

        Assert.Equal((6, 14, 127, 7), (leaders, followers, placed, scripted));
    }
}
