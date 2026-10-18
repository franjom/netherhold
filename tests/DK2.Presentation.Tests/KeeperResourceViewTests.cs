// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;
using System.Numerics;

namespace DK2.Presentation.Tests;

public sealed class KeeperResourceViewTests
{
    [Fact]
    public void GaugeFillsClockwiseFromTheTopAndClampsMissingOrRuinedHeart()
    {
        var quarter = new HudGaugeFraction(1, 4);
        Assert.True(quarter.Contains(new Vector2(1, -1)));
        Assert.False(quarter.Contains(new Vector2(1, 1)));
        Assert.False(quarter.Contains(new Vector2(-1, -1)));
        Assert.False(default(HudGaugeFraction).Contains(new Vector2(1, -1)));
        Assert.False(new HudGaugeFraction(-1, 100).Contains(new Vector2(1, -1)));
        Assert.True(new HudGaugeFraction(120, 100).Contains(new Vector2(-1, -1)));
    }
    [InstallFact]
    public void CapturesSpendableGoldAndGrossManaRatesWithoutChangingSimulation()
    {
        string install = Dk2InstallGate.Path;
        var database = GameDatabaseLoader.Load(install);
        var map = GameDatabaseLoader.LoadCampaignMap(install, "Level1")!;
        var world = WorldState.FromMap(database, map, [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        ref PlayerState player = ref world.Player(PlayerIds.FirstKeeper);
        player.Mana = 259;
        player.GoldMined = 999999;
        var simulation = new GameSimulation(database, world, seed: 7);
        ulong before = simulation.StateHash();
        var view = new KeeperResourceView(database, PlayerIds.FirstKeeper);
        KeeperResourceSnapshot snapshot = view.Capture(world);
        Assert.Equal(GoldStore.Total(world, PlayerIds.FirstKeeper), snapshot.Gold.Value);
        Assert.NotEqual(player.GoldMined, snapshot.Gold.Value);
        Assert.Equal(259, snapshot.Mana.Value);
        Assert.Equal(player.HeartHealth, snapshot.HeartHealth.Value);
        Assert.Equal(ManaRules.GainPerSecondByPlayerIndex(world, database)[0], snapshot.Income.Value);
        Assert.Equal(ManaRules.UpkeepPerSecondByPlayerIndex(world, database)[0], snapshot.Upkeep.Value);
        Assert.Equal(before, simulation.StateHash());

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) view.Capture(world);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore < 1024,
            "Paused resource capture must reuse the settled snapshot.");

        simulation.Tick();
        player.Mana = 300;
        Assert.Equal(300, view.Capture(world).Mana.Value);
    }

    [InstallFact]
    public void MissingViewerDoesNotReadAnotherKeepersResources()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!;
        var world = WorldState.FromMap(database, map, [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        Assert.Equal(default, new KeeperResourceView(database, new PlayerId(99)).Capture(world));
    }

    [InstallFact]
    public void KeeperWithoutAHeartDoesNotShowItsInitializedHealthAsAFullGauge()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!;
        var world = WorldState.FromMap(database, map, [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        for (int i = 0; i < world.TileCount; i++) world.TileAtIndex(i).OwnerPlayerId = PlayerIds.Neutral;
        Assert.True(world.Player(PlayerIds.FirstKeeper).HeartHealth > 0);
        Assert.Equal(0, new KeeperResourceView(database, PlayerIds.FirstKeeper).Capture(world).HeartHealth.Value);
    }

    [InstallFact]
    public void PrepaidCallToArmsDoesNotShowNextTurnsUpkeepEarly()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!;
        var world = WorldState.FromMap(database, map, [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        ref PlayerState player = ref world.Player(PlayerIds.FirstKeeper);
        ObjectState flag = world.SpawnObject(database, CallToArmsRules.ObjectTypeId, PlayerIds.FirstKeeper, 1);
        flag.ProcessState = CallToArmsRules.Recalling;
        player.CallToArms = new CallToArmsState(true, flag.Id, 1, 1, 0);
        int ordinary = ManaRules.ImpAndTrapUpkeepPerSecondByPlayerIndex(world, database)[0];
        Assert.True(ManaRules.UpkeepPerSecondByPlayerIndex(world, database)[0] > ordinary);
        Assert.Equal(ordinary, new KeeperResourceView(database, PlayerIds.FirstKeeper).Capture(world).Upkeep.Value);
    }
}
