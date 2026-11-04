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

/// <summary>The loose Chicken's side search compares raw navigation bytes (<c>0x00455000</c>, ADR-0649).
/// The Heart builder writes 3 on the Heart's chain tile 12 (Q601), so a bird standing there finds no
/// floor side, as DK2's seed-1 Level 5 birds on that tile drew <c>random(4)</c> and stayed.</summary>
public class LooseChickenOnRealDataTests
{
    [InstallFact]
    public void ABirdOnTheHeartCentreFindsNoFloorSide()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "level5");
        List<PlayerDef> players = GameDatabaseLoader.LoadCampaignPlayers(install, "level5")!;
        WorldState world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "level5")!, players.Select(p => p.PlayerId));
        ThingProjection.Place(world, db, GameDatabaseLoader.LoadCampaignThings(install, "level5")!);
        NavStamp centre = Assert.Single(world.NavStamps, s => s.Byte == 3);

        ObjectState bird = world.SpawnObject(db, HatcheryRules.ChickenObjectId, PlayerIds.FirstKeeper, centre.Tile);
        bird.ChickenCanEscape = true;
        ChickenMovementRules.SetProcess(bird, ChickenProcess.Loose);
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(1)), [new ChickenMovementSystem()]);
        uint before = ((Dk2Lcg)sim.Rng.Algorithm).Seed;
        sim.Tick();

        var expected = new Dk2Lcg(before);
        expected.NextInt(4);
        Assert.Equal(expected.Seed, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.Equal((byte)ChickenProcess.None, bird.ProcessState);
    }
}
