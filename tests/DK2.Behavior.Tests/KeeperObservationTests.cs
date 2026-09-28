// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class KeeperObservationTests
{
    [InstallFact]
    public void LiveThreatIncludesDyingCreaturesButExcludesHeldCarriedAndCorpseRecords()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        var map = GameDatabaseLoader.LoadCampaignMap(install, "level10")!;
        var world = WorldState.FromMap(db, map, [1, 2, 3, 4]);
        var at = new TilePos(20, 20);
        byte type = db.Creatures.Values.Single(c => c.Name == "Goblin").CreatureId;
        CreatureState standing = world.SpawnCreature(db, type, 4, at);
        CreatureState dying = world.SpawnCreature(db, type, 4, at);
        CreatureState held = world.SpawnCreature(db, type, 4, at);
        CreatureState carried = world.SpawnCreature(db, type, 4, at);
        CreatureState corpse = world.SpawnCreature(db, type, 4, at);
        dying.Health = 0;
        Assert.True(CreatureStateMachine.TrySetState(dying, CreatureDeathRules.FallingState));
        held.InHand = true;
        carried.CarriedByCreatureId = standing.Id;
        CreatureStateMachine.Release(corpse);
        Assert.Equal(CreatureActivity.Dead, corpse.Activity);
        var captured = new KeeperView(db, world, 4);
        KeeperView restored = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(captured), db);
        IReadOnlyList<KeeperThreatSource> sources = ThreatEvaluator.Sources(restored);
        Assert.Equal(4, sources.Count);
        Assert.Equal(2, sources.Count(s => s.MapLayer == 2));
        int expected = 2 * db.CreatureById(type).Threat;
        Assert.True(expected > 0);
        Assert.Equal(expected, ThreatEvaluator.LocalStrength(sources, 4,
            standing.PosX, standing.PosY, Fp32.One, false));

        standing.InHand = true;
        Assert.Equal(sources, ThreatEvaluator.Sources(captured));
        Assert.Equal(expected / 2, ThreatEvaluator.LocalStrength(
            ThreatEvaluator.Sources(new KeeperView(db, world, 4)), 4,
            standing.PosX, standing.PosY, Fp32.One, false));
    }

    [InstallFact]
    public void StockCreaturesSurviveObservationSerializationAndLaterWorldMutation()
    {
        string install = Dk2InstallGate.Path;
        var db = GameDatabaseLoader.Load(install);
        var map = GameDatabaseLoader.LoadCampaignMap(install, "level10")!;
        var players = GameDatabaseLoader.LoadCampaignPlayers(install, "level10")!;
        var world = WorldState.FromMap(db, map, players.Select(p => p.PlayerId));
        ThingProjection.Place(world, db, GameDatabaseLoader.LoadCampaignThings(install, "level10")!);
        var creature = world.Creatures.First(c => c.OwnerPlayerId == 4
            && HandRules.CanPickUp(world, db, c, 4));
        creature.PosX = 12345;
        creature.PosY = 67890;
        creature.CarriedByCreatureId = 321;
        creature.RescuePriorityMask = 0x8045;
        var original = new KeeperView(db, world, 4);
        var restored = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(original), db);

        Assert.NotEmpty(original.Creatures);
        Assert.Equal(original.Creatures, restored.Creatures);
        KeeperCreatureView captured = original.Creatures.Single(c => c.Id == creature.Id);
        Assert.Equal(12345, captured.PosX);
        Assert.Equal(67890, captured.PosY);
        Assert.Equal(321, captured.CarriedByCreatureId);
        Assert.Equal((ushort)0x8045, captured.RescuePriorityMask);
        Assert.False(captured.InHand);
        Assert.True(captured.PickupPermitted);
        Assert.True(captured.CanPickUp);
        for (int i = 0; i < HandRules.Capacity(db); i++)
        {
            var held = world.SpawnCreature(db, creature.CreatureTypeId, 4, new TilePos(1, 1));
            held.InHand = true;
            world.Player(4).Hand.Add(HandItem.Of(held));
        }
        var fullHand = KeeperViewSnapshot.FromBytes(
            KeeperViewSnapshot.ToBytes(new KeeperView(db, world, 4)), db)
            .Creatures.Single(c => c.Id == creature.Id);
        Assert.True(fullHand.PickupPermitted);
        Assert.False(fullHand.CanPickUp);
        Assert.True(original.Creatures.Single(c => c.Id == creature.Id).CanPickUp);
        creature.PosX++;
        creature.PosY++;
        creature.InHand = true;
        creature.CarriedByCreatureId = -1;
        creature.RescuePriorityMask = 0;
        creature.Health = 0;
        creature.LastSlapTick = 1234;
        Assert.Equal(captured, restored.Creatures.Single(c => c.Id == creature.Id));
        Assert.Equal(captured, original.Creatures.Single(c => c.Id == creature.Id));
        var current = new KeeperView(db, world, 4);
        KeeperCreatureView changed = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(current), db)
            .Creatures.Single(c => c.Id == creature.Id);
        Assert.True(changed.InHand);
        Assert.False(changed.PickupPermitted);
        Assert.False(changed.CanPickUp);
        Assert.Equal(-1, changed.CarriedByCreatureId);
        Assert.Equal((ushort)0, changed.RescuePriorityMask);
        Assert.Equal(12346, changed.PosX);
        Assert.Equal(67891, changed.PosY);
        Assert.NotEqual(captured, changed);
    }
}
