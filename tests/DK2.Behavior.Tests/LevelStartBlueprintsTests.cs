// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The authored door loader <c>0x00520F40</c> leaves a door under construction when file flag
/// bit 1 is set (doors-and-traps.md). Level6a authors three such Barricades for its keeper
/// (structure-drawing.md section 5): they start as blueprints with orders and list entries, and the
/// view must draw them as plans, not as built doors.</summary>
public class LevelStartBlueprintsTests
{
    [InstallFact]
    public void Level6aStartsWithItsKeepersThreeBarricadesAsBlueprints_Ordered_AndListed()
    {
        string install = Dk2InstallGate.Path;
        const string level = "Level6a";
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        GameSimulation sim = CampaignSimulationFactory.Create(
            db, GameDatabaseLoader.LoadCampaignMap(install, level)!,
            GameDatabaseLoader.LoadCampaignLevel(install, level)!,
            GameDatabaseLoader.LoadCampaignPlayers(install, level)!,
            GameDatabaseLoader.LoadCampaignThings(install, level)!,
            GameDatabaseLoader.LoadCampaignTriggers(install, level)!, seed: 1);

        DoorState[] plans = sim.World.Doors.Where(d => d.UnderConstruction).ToArray();

        Assert.Equal([(44, 23), (44, 24), (44, 25)], plans.Select(d => (d.X, d.Y)).OrderBy(p => p.Item2).ToArray());
        Assert.All(plans, d => Assert.Equal((byte)6, d.DoorId));
        Assert.All(plans, d => Assert.Equal(PlayerIds.FirstKeeper, d.PlayerId));
        Assert.Equal(3, sim.World.Player(PlayerIds.FirstKeeper).Blueprints.Count);
        Assert.Equal(3, sim.World.Player(PlayerIds.FirstKeeper).ManufactureQueue.Count);
    }
}
