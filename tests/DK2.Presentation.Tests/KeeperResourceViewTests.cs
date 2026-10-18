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
    public void PaydayWarningUsesRealTimePhasesAndKeepsItsLatchForTenGameSeconds()
    {
        var animation = new KeeperResourceAnimation();
        var snapshot = new KeeperResourceSnapshot(new(50), new(20), new(2), new(1),
            WageBill: new(100), Tick: new(100), LastPaydayTick: new(0), NextPaydayTick: new(110),
            PaydayCutoffTicks: 20);
        Assert.Equal(50, animation.Evaluate(snapshot, 499).GoldNumber.Value);
        var bill = animation.Evaluate(snapshot, 500);
        Assert.Equal(-100, bill.GoldNumber.Value);
        Assert.Equal(KeeperGoldColour.UnaffordableBill, bill.GoldColour);
        snapshot = snapshot with { Tick = new(111), LastPaydayTick = new(110), NextPaydayTick = new(10000) };
        Assert.Equal(-100, animation.Evaluate(snapshot, 500).GoldNumber.Value);
        snapshot = snapshot with { Tick = new(110 + 10 * SimulationTiming.TicksPerSecond) };
        Assert.Equal(50, animation.Evaluate(snapshot, 500).GoldNumber.Value);
    }

    [Fact]
    public void GoldFlashBlankPhaseDoesNotLatchAnUndisplayedPaydayWarning()
    {
        var animation = new KeeperResourceAnimation();
        var snapshot = new KeeperResourceSnapshot(new(50), default, default, default,
            WageBill: new(100), Tick: new(100), NextPaydayTick: new(110), PaydayCutoffTicks: 20);
        Assert.False(animation.Evaluate(snapshot, 250, KeeperResourceFlash.Gold).ShowGoldNumber);
        snapshot = snapshot with { Tick = new(111), LastPaydayTick = new(110), NextPaydayTick = new(10000) };
        Assert.Equal(50, animation.Evaluate(snapshot, 500).GoldNumber.Value);
    }

    [Fact]
    public void MessageFlashesSuppressOnlyTheirNumbersOnOddQuarterSecondPhases()
    {
        var animation = new KeeperResourceAnimation();
        var snapshot = new KeeperResourceSnapshot(new(50), new(20), new(2), new(1));
        Assert.True(animation.Evaluate(snapshot, 249, KeeperResourceFlash.Gold).ShowGoldNumber);
        Assert.False(animation.Evaluate(snapshot, 250, KeeperResourceFlash.Gold).ShowGoldNumber);
        Assert.True(animation.Evaluate(snapshot, 250, KeeperResourceFlash.Gold).ShowManaNumbers);
        Assert.False(animation.Evaluate(snapshot, 250, KeeperResourceFlash.Mana).ShowManaNumbers);
        Assert.True(animation.Evaluate(snapshot, 500, KeeperResourceFlash.Mana).ShowManaNumbers);
    }

    [Theory]
    [InlineData(KeeperResourceModifiers.None, 100)]
    [InlineData(KeeperResourceModifiers.Shift, 1000)]
    [InlineData(KeeperResourceModifiers.Control, 10000)]
    [InlineData(KeeperResourceModifiers.Shift | KeeperResourceModifiers.Control, 100)]
    [InlineData(KeeperResourceModifiers.Alt, 100)]
    public void GoldCounterModifiersUseEqualityRatherThanIndependentBits(KeeperResourceModifiers modifiers, int expected)
        => Assert.Equal(expected, KeeperResourceInteraction.WithdrawAmount(modifiers).Value);

    [Fact]
    public void ResourceHitRegionsKeepNativePrecedenceAndScaleWithHeight()
    {
        Assert.Equal(KeeperResourceKind.Heart, KeeperResourceInteraction.HitTest(new(30, 30), 768));
        Assert.Equal(KeeperResourceKind.Mana, KeeperResourceInteraction.HitTest(new(100, 15), 768));
        Assert.Equal(KeeperResourceKind.Payday, KeeperResourceInteraction.HitTest(new(230, 25), 768));
        Assert.Equal(KeeperResourceKind.Gold, KeeperResourceInteraction.HitTest(new(300, 15), 768));
        Assert.Equal(KeeperResourceKind.Gold, KeeperResourceInteraction.HitTest(new(600, 30), 1536));
        Assert.Equal(KeeperResourceKind.None, KeeperResourceInteraction.HitTest(new(450, 15), 768));
        Assert.Equal(KeeperResourceKind.None, KeeperResourceInteraction.HitTest(new(12, 12), 768));
        Assert.Equal(KeeperResourceKind.Heart, KeeperResourceInteraction.HitTest(new(12, 20), 768));
        Assert.Equal(KeeperResourceKind.None, KeeperResourceInteraction.HitTest(new(220, 12), 768));
        Assert.Equal(KeeperResourceKind.Payday, KeeperResourceInteraction.HitTest(new(220, 20), 768));
    }

    [Fact]
    public void ResourceHoverUsesNativeDescriptionPairsAndTruncatedHeartPercentage()
    {
        string Resolve(uint id) => id switch
        {
            2139 => "heart", 2140 => "%19% health", 2143 => "payday", 2144 => "cost %20",
            _ => id.ToString(),
        };
        var snapshot = new KeeperResourceSnapshot(default, default, default, default,
            HeartHealth: new(2, 3), WageBill: new(547), HeartTile: new(1, 1));
        var heart = KeeperResourceInteraction.ResourceHover(snapshot, KeeperResourceKind.Heart, Resolve);
        Assert.Equal("66% health", heart.TopStatus);
        Assert.Equal("heart", heart.TooltipName);
        Assert.Equal(WorldHoverKind.Widget, heart.Hit.Kind);
        Assert.Equal("cost 547", KeeperResourceInteraction.ResourceHover(snapshot, KeeperResourceKind.Payday, Resolve).TopStatus);
        Assert.Equal("", KeeperResourceInteraction.ResourceHover(snapshot with { HeartTile = null }, KeeperResourceKind.Heart, Resolve).TopStatus);
        Assert.Equal(HoverTextSnapshot.Empty, KeeperResourceInteraction.ResourceHover(snapshot, KeeperResourceKind.None, Resolve));
    }

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

    [Fact]
    public void HeartAlarmAlternatesOriginalTurnsAndExpiresAtThreeGameSeconds()
    {
        var animation = new KeeperResourceAnimation();
        var snapshot = new KeeperResourceSnapshot(default, default, default, default,
            Tick: new(1), HeartAlarmUntil: new(1 + 3 * SimulationTiming.TicksPerSecond));
        Assert.True(animation.Evaluate(snapshot, 0).ShowHeartAlarm);
        Assert.False(animation.Evaluate(snapshot with { Tick = new(2) }, 0).ShowHeartAlarm);
        Assert.False(animation.Evaluate(snapshot with { Tick = snapshot.HeartAlarmUntil }, 0).ShowHeartAlarm);
    }

    [InstallFact]
    public void FirstAndRestoredPaydayGaugesUseTheSavedIntervalStart()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!;
        var world = WorldState.FromMap(database, map, [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        var simulation = new GameSimulation(database, world, seed: 7, []);
        simulation.Run(20);
        world.Player(PlayerIds.FirstKeeper).LastPaydayTick = new(20);
        world.Player(PlayerIds.FirstKeeper).NextPaydayTick = new(220);
        simulation.Run(50);
        var view = new KeeperResourceView(database, PlayerIds.FirstKeeper);
        var first = view.Capture(world);
        Assert.Equal(new HudGaugeFraction(150, 200), first.Payday);
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(simulation), database, []);
        Assert.Equal(first.Payday, new KeeperResourceView(database, PlayerIds.FirstKeeper).Capture(restored.World).Payday);
    }

    [InstallFact]
    public void HeartAlarmObservesDisplayedHealthRatherThanIntermediateDamageEvents()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!;
        var world = WorldState.FromMap(database, map, [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        var simulation = new GameSimulation(database, world, seed: 7, []);
        var view = new KeeperResourceView(database, PlayerIds.FirstKeeper);
        int health = world.Player(PlayerIds.FirstKeeper).HeartHealth;
        view.Capture(world);
        world.DamageHeart(PlayerIds.FirstKeeper, 1, PlayerIds.Good);
        world.RegenerateHeart(PlayerIds.FirstKeeper, 1);
        simulation.Tick();
        Assert.Equal(default, view.Capture(world).HeartAlarmUntil);
        world.DamageHeart(PlayerIds.FirstKeeper, 1, PlayerIds.Good);
        simulation.Tick();
        var damaged = view.Capture(world);
        Assert.Equal(health - 1, damaged.HeartHealth.Value);
        Assert.Equal(world.CurrentTick.Value + 3 * SimulationTiming.TicksPerSecond, damaged.HeartAlarmUntil.Value);
    }

    [InstallFact]
    public void RestoringTheHudObservesDamageWithAFreshAlarmWindowEvenOnTheSameSettledTick()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!;
        var world = WorldState.FromMap(database, map, [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        var simulation = new GameSimulation(database, world, seed: 7, []);
        var view = new KeeperResourceView(database, PlayerIds.FirstKeeper);
        view.Capture(world);
        world.DamageHeart(PlayerIds.FirstKeeper, 1, PlayerIds.Good);
        for (int i = 0; i < 20; i++)
        {
            simulation.Tick();
            Assert.Equal(default, view.Capture(world, displayHealth: false).HeartAlarmUntil);
        }
        var restored = view.Capture(world, displayHealth: true);
        Assert.Equal(world.CurrentTick.Value + 3 * SimulationTiming.TicksPerSecond, restored.HeartAlarmUntil.Value);
        Assert.Equal(restored, view.Capture(world));
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
