// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Data;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class KeeperObservationTests
{
    [InstallFact]
    public void StockCreaturesSurviveObservationSerializationAndLaterWorldMutation()
    {
        string install = Dk2InstallGate.Path;
        var db = GameDatabaseLoader.Load(install);
        var map = GameDatabaseLoader.LoadCampaignMap(install, "level10")!;
        var players = GameDatabaseLoader.LoadCampaignPlayers(install, "level10")!;
        var world = WorldState.FromMap(db, map, players.Select(p => p.PlayerId));
        ThingProjection.Place(world, db, GameDatabaseLoader.LoadCampaignThings(install, "level10")!);
        var original = new KeeperView(db, world, 4);
        var restored = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(original), db);

        Assert.NotEmpty(original.Creatures);
        Assert.Equal(original.Creatures, restored.Creatures);
        var creature = world.Creatures.First(c => c.OwnerPlayerId == 4);
        KeeperCreatureView captured = original.Creatures.Single(c => c.Id == creature.Id);
        creature.Health = 0;
        creature.LastSlapTick = 1234;
        Assert.Equal(captured, restored.Creatures.Single(c => c.Id == creature.Id));
        Assert.Equal(captured, original.Creatures.Single(c => c.Id == creature.Id));
        Assert.NotEqual(captured, new KeeperView(db, world, 4).Creatures.Single(c => c.Id == creature.Id));
    }
}
