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

/// <summary>A manufacturer hammers in state 65 (action 32, art 22 or its melee fallback), and only then.
/// Level 6a's Trolls have no art slot 22, so the hammer is their melee swing.</summary>
public class WorkshopWorkAnimationTests
{
    private const byte Keeper = 3;

    [InstallFact]
    public void ATrollPlaysItsHammerForTheActionAndStandsStillInThePoseOtherwise()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level6a");
        GameSimulation simulation = CampaignSimulationFactory.Create(db,
            GameDatabaseLoader.LoadCampaignMap(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignLevel(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignPlayers(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignThings(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignTriggers(install, "Level6a")!, 1);
        WorldState world = simulation.World;
        simulation.Run(2);
        ManufactureRules.Enqueue(ref world.Player(Keeper), new ManufactureOrder(ManufactureRules.DoorKind, 1, 600_000));
        CreatureState troll = world.Creatures.First(c =>
            c.OwnerPlayerId == Keeper && db.CreatureById(c.CreatureTypeId).Name == "Troll");
        Assert.False(AnimationTimingRules.HasArt(db.CreatureById(troll.CreatureTypeId), WorkshopStep.WorkArtSlot));
        var view = new WorldView(db);
        AssetRef hammer = AssetRef.ForCreature("Troll", AssetKind.Animation, "working");

        int hammering = 0, standing = 0;
        for (int tick = 0; tick < 400; tick++)
        {
            simulation.Tick();
            if (troll.Dk2StateId is not (WorkshopStep.AtTheMachineState or WorkshopStep.WorkingState)) continue;
            CreatureView pose = view.Creatures(world).Single(c => c.Id == troll.Id);
            bool acting = troll.Dk2StateId == WorkshopStep.WorkingState && world.CurrentTick.Value < troll.ActionUntilTick;
            if (acting)
            {
                Assert.True(pose.PlaysAnimation);
                Assert.Equal(hammer, pose.Animation);
                Assert.Equal(new GameTick(troll.StateEnteredTick), pose.AnimationStartedAt);
                hammering++;
            }
            else
            {
                Assert.False(pose.PlaysAnimation);   // turning to face the machine: no swing yet
                standing++;
            }
        }

        Assert.True(hammering > 20, $"{hammering} turns of hammering");
        Assert.True(standing > 0);
    }
}
