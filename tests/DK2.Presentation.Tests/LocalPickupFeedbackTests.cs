// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using DK2.TestSupport;
namespace DK2.Presentation.Tests;
public sealed class LocalPickupFeedbackTests
{
    private static readonly PlayerId Keeper = new(3);
    [InstallFact]
    public void PickupDustIsVisibleBeforeAnySimulationTickAndTheAuthoritativeEchoIsConsumedOnce()
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        var pending = new LocalPickupFeedback();
        long now = 0;
        var effects = new RoomConstructionParticles(db, 1, () => new EffectTime(now));
        var delivered = new List<ViewEffectRequest>();
        pending.DustRequested += request => { delivered.Add(request); effects.RequestPickupDust(request); };
        var events = new ListEventSink();
        var sink = new LocalPickupEffectSink(pending, events);
        var simulation = new GameSimulation(db, world, 1, [new HandSystem()]);
        simulation.ObserveWith(sink);
        GameTick tick = world.CurrentTick;
        creature.PosX += 321;
        creature.Heading = 0x321;

        Assert.True(pending.TryQueue(world, db, Keeper, new(creature.Id), 0));

        ViewEffectRequest dust = Assert.Single(delivered);
        Assert.Equal(new CreatureId(creature.Id), dust.SourceCreatureId);
        Assert.Equal(Keeper, dust.Owner);
        Assert.Equal(new FpPosition3(new(creature.PosX), new(creature.PosY), new(0x1000)), dust.Position);
        Assert.Equal(new Dk2Angle(0x321), dust.Heading);
        Assert.Equal(tick, world.CurrentTick);
        Assert.Equal(8, effects.Particles.Count);
        Assert.False(creature.InHand);
        Assert.Empty(events.Events);

        // A moving creature's authoritative point may differ from its clicked position.
        creature.PosX += 777;
        simulation.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));
        Assert.DoesNotContain(events.Events, e => e.EffectRequest.HasValue);
        Assert.Single(delivered);
        Assert.Equal(8, effects.Particles.Count);
        Assert.Contains(events.Events, e => e.Kind == "lift_prepared");
        now = 51;
        effects.UpdateNow();
        Assert.Equal(8, effects.Particles.Count);
        var echo = new SimEvent(world.CurrentTick, "Hand", "effect_requested", "") { EffectRequest = dust };
        sink.Emit(echo);
        Assert.Single(events.Events, e => e.EffectRequest.HasValue);
    }
    [InstallFact]
    public void RejectedOrRepeatedLocalPickupDoesNotEmitDust()
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        var pending = new LocalPickupFeedback();
        var dust = new List<ViewEffectRequest>();
        pending.DustRequested += dust.Add;
        creature.Health = 0;
        Assert.False(pending.TryQueue(world, db, Keeper, new(creature.Id), 0));
        Assert.Empty(dust);
        creature.Health = 100;
        Assert.True(pending.TryQueue(world, db, Keeper, new(creature.Id), 1));
        Assert.False(pending.TryQueue(world, db, Keeper, new(creature.Id), 2));
        Assert.Single(dust);
    }
    [InstallTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetOrTimeoutReleasesTheDustEchoSuppression(bool timeout)
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        var pending = new LocalPickupFeedback();
        ViewEffectRequest dust = default;
        pending.DustRequested += request => dust = request;
        var events = new ListEventSink();
        var sink = new LocalPickupEffectSink(pending, events);
        Assert.True(pending.TryQueue(world, db, Keeper, new(creature.Id), 0));
        if (timeout) pending.Advance(world, 2001); else pending.Reset();
        sink.Emit(new SimEvent(world.CurrentTick, "Hand", "effect_requested", "") { EffectRequest = dust });
        Assert.Single(events.Events);
    }
    [InstallFact]
    public void DustFromOtherCreaturesOwnersOrSourcesIsNotConsumed()
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        var pending = new LocalPickupFeedback();
        ViewEffectRequest dust = default;
        pending.DustRequested += request => dust = request;
        var events = new ListEventSink();
        var sink = new LocalPickupEffectSink(pending, events);
        Assert.True(pending.TryQueue(world, db, Keeper, new(creature.Id), 0));
        void Emit(string system, ViewEffectRequest request) => sink.Emit(
            new SimEvent(world.CurrentTick, system, "effect_requested", "") { EffectRequest = request });
        Emit("Hand", dust with { SourceCreatureId = new(creature.Id + 1) });
        Emit("Hand", dust with { Owner = PlayerIds.Neutral });
        Emit("movement dust", dust);
        Emit("Hand", dust with { EffectId = new(100) });
        Assert.Equal(4, events.Events.Count);
        Emit("Hand", dust);
        Assert.Equal(4, events.Events.Count);
    }
    [InstallFact]
    public void DustEchoStillMatchesAfterTheHandAcknowledgesTheLocalPickup()
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        var pending = new LocalPickupFeedback();
        ViewEffectRequest dust = default;
        pending.DustRequested += request => dust = request;
        var events = new ListEventSink();
        var sink = new LocalPickupEffectSink(pending, events);
        Assert.True(pending.TryQueue(world, db, Keeper, new(creature.Id), 0));
        creature.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(creature));
        pending.Advance(world, 100);
        Assert.Equal(0, pending.Count);
        sink.Emit(new SimEvent(world.CurrentTick, "Hand", "effect_requested", "") { EffectRequest = dust });
        Assert.Empty(events.Events);
    }
    [InstallFact]
    public void WithoutADustRendererTheAuthoritativeEffectIsPreserved()
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        var pending = new LocalPickupFeedback();
        var events = new ListEventSink();
        var simulation = new GameSimulation(db, world, 1, [new HandSystem()]);
        simulation.ObserveWith(new LocalPickupEffectSink(pending, events));
        Assert.True(pending.TryQueue(world, db, Keeper, new(creature.Id), 0));
        simulation.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));
        Assert.Single(events.Events, e => e.EffectRequest.HasValue);
    }
    [InstallFact]
    public void RetainedQueuedAndDispatchedHeldEntriesStillConsumePendingCapacity()
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        var pending = new LocalPickupFeedback();
        Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(creature.Id), 0));
        Assert.True(pending.TryQueueDrop(new DropCommand(Keeper, new TilePos(2, 2))));
        creature.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(creature));
        for (int i = 1; i < HandRules.Capacity(db) - 1; i++)
        {
            CreatureState held = world.SpawnCreature(db, creature.CreatureTypeId, Keeper, new TilePos(2, 2));
            held.InHand = true;
            world.Player(Keeper).Hand.Add(HandItem.Of(held));
        }
        CreatureState extra = world.SpawnCreature(db, creature.CreatureTypeId, Keeper, new TilePos(2, 2));
        Assert.False(pending.TryQueue(world, db, Keeper, new CreatureId(extra.Id), 10));
        var drops = new List<PlayerCommand>();
        pending.DrainReadyDrops(world, drops);
        Assert.Single(drops);
        Assert.False(pending.TryQueue(world, db, Keeper, new CreatureId(extra.Id), 11));
        world.Player(Keeper).Hand.RemoveAll(item => !item.IsObject && item.Id == creature.Id);
        creature.InHand = false;
        pending.Advance(world, 12);
        Assert.False(pending.Contains(new CreatureId(creature.Id)));
        Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(extra.Id), 12));
    }
    [InstallFact]
    public void VisiblePendingCreatureTakesPriorityOverActualGoldBeyondPickupTransient()
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        ObjectState gold = world.SpawnObject(db, 1, Keeper, 12);
        gold.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(gold));
        var pending = new LocalPickupFeedback();
        Assert.True(pending.HoldingGold(world, Keeper));
        Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(creature.Id), 0));
        var cursor = new HandCursorFeedback();
        cursor.Play(HandCursorKind.HoldThing, 0);
        Assert.Equal(HandCursorKind.HoldThing, cursor.Resolve(HandCursorKind.Point, true,
            pending.HoldingGold(world, Keeper), false, 1000));
        Assert.True(pending.TryQueueDrop(new DropCommand(Keeper, new TilePos(2, 2))));
        Assert.True(pending.HoldingGold(world, Keeper));
        creature.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(creature));
        pending.Advance(world, 1001);
        pending.DrainReadyDrops(world, new List<PlayerCommand>());
        Assert.False(pending.HoldingGold(world, Keeper));
        creature.InHand = false;
        world.Player(Keeper).Hand.RemoveAt(world.Player(Keeper).Hand.Count - 1);
        pending.Reset();
        Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(creature.Id), 0));
        pending.Advance(world, 2001);
        Assert.Equal(HandCursorKind.HoldGold, cursor.Resolve(HandCursorKind.Point, true,
            pending.HoldingGold(world, Keeper), false, 2001));
    }
    [InstallFact]
    public void PendingIconsPrecedeActualHandInReversePickupOrderAndOverflowAtNine()
    {
        (GameDatabase? db, WorldState? world, CreatureState? actual) = Fixture();
        actual.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(actual));
        var pending = new LocalPickupFeedback();
        var stack = new HeldItemStack();
        CreatureState first = world.SpawnCreature(db, actual.CreatureTypeId, Keeper, new TilePos(2, 2));
        CreatureState second = world.SpawnCreature(db, db.CreatureByName("Warlock")!.CreatureId, Keeper, new TilePos(2, 2));
        Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(first.Id), 0));
        Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(second.Id), 0));
        stack.Refresh(world, Keeper, pending);
        Assert.Equal(new[] { new HeldIcon(HeldIconKind.Creature, second.CreatureTypeId),
            new HeldIcon(HeldIconKind.Creature, first.CreatureTypeId),
            new HeldIcon(HeldIconKind.Creature, actual.CreatureTypeId) }, stack.Icons);
        for (int i = 0; i < 6; i++)
        {
            CreatureState extra = world.SpawnCreature(db, actual.CreatureTypeId, Keeper, new TilePos(2, 2));
            Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(extra.Id), 0));
        }
        stack.Refresh(world, Keeper, pending);
        Assert.Equal(8, stack.Icons.Count);
        Assert.True(stack.More);
        for (int i = 0; i < 6; i++)
            Assert.True(pending.TryQueueDrop(new DropCommand(Keeper, new TilePos(1, 1))));
        stack.Refresh(world, Keeper, pending);
        Assert.Equal(3, stack.Icons.Count);
        Assert.False(stack.More);
    }
    [InstallFact]
    public void SnapshotReplacementRetainsLocalPreviewUntilExplicitSessionReset()
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        var pending = new LocalPickupFeedback();
        Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(creature.Id), 0));
        (GameDatabase _, WorldState? snapshot, CreatureState _) = Fixture();
        pending.Advance(snapshot, 10);
        Assert.True(pending.Contains(new CreatureId(creature.Id)));
        pending.Reset();
        Assert.False(pending.Contains(new CreatureId(creature.Id)));
    }
    [InstallFact]
    public void PendingCapacityPermissionAndDeadEntriesAreValidatedLocally()
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        var pending = new LocalPickupFeedback();
        creature.Health = 0;
        Assert.False(pending.TryQueue(world, db, Keeper, new CreatureId(creature.Id), 0));
        creature.Health = 100;
        Assert.False(pending.TryQueue(world, db, new PlayerId(PlayerIds.Neutral), new CreatureId(creature.Id), 0));
        for (int i = 0; i < HandRules.Capacity(db); i++)
        {
            CreatureState next = world.SpawnCreature(db, creature.CreatureTypeId, Keeper, new TilePos(2, 2));
            Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(next.Id), 0));
        }
        Assert.False(pending.TryQueue(world, db, Keeper, new CreatureId(creature.Id), 0));
        pending.Advance(world, 2001);
        Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(creature.Id), 2001));
        creature.Health = 0;
        pending.Advance(world, 2002);
        Assert.Equal(1, pending.Count);
        pending.Advance(world, 4002);
        Assert.Equal(0, pending.Count);
    }
    [InstallFact]
    public void MultipleQuickDropsDispatchEligibleEntriesInOldestPendingOrder()
    {
        (GameDatabase? db, WorldState? world, CreatureState? first) = Fixture();
        CreatureState second = world.SpawnCreature(db, first.CreatureTypeId, Keeper, new TilePos(2, 2));
        var pending = new LocalPickupFeedback();
        Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(first.Id), 0));
        Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(second.Id), 0));
        var drop1 = new DropCommand(Keeper, new TilePos(1, 1));
        var drop2 = new DropCommand(Keeper, new TilePos(3, 3));
        Assert.True(pending.TryQueueDrop(drop1));
        Assert.True(pending.TryQueueDrop(drop2));
        var output = new List<PlayerCommand>();
        first.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(first));
        pending.Advance(world, 10);
        pending.DrainReadyDrops(world, output);
        Assert.Same(drop2, Assert.Single(output));
        output.Clear();
        second.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(second));
        pending.Advance(world, 11);
        pending.DrainReadyDrops(world, output);
        Assert.Same(drop1, Assert.Single(output));
        output.Clear();
        pending.DrainReadyDrops(world, output);
        Assert.Empty(output);
    }
    [InstallFact]
    public void PendingPickupHidesOnlyLocalWorldAndAddsIconWithoutAdvancingSimulation()
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        var view = new WorldView(db, Keeper);
        var remote = new WorldView(db, Keeper);
        var hand = new HeldItemStack();
        var simulation = new GameSimulation(db, world, 1);
        ulong hash = simulation.StateHash();
        Assert.True(view.LocalPickups.TryQueue(world, db, Keeper, new CreatureId(creature.Id), 100));
        Assert.DoesNotContain(view.Creatures(world), c => c.Id == creature.Id);
        Assert.Contains(remote.Creatures(world), c => c.Id == creature.Id);
        hand.Refresh(world, Keeper, view.LocalPickups);
        Assert.Equal(new HeldIcon(HeldIconKind.Creature, creature.CreatureTypeId), Assert.Single(hand.Icons));
        Assert.False(creature.InHand);
        Assert.Empty(world.Player(Keeper).Hand);
        Assert.Equal(new GameTick(0), world.CurrentTick);
        Assert.False(view.LocalPickups.TryQueue(world, db, Keeper, new CreatureId(creature.Id), 101));
        view.LocalPickups.Advance(world, 2100);
        Assert.True(view.LocalPickups.Contains(new CreatureId(creature.Id)));
        view.LocalPickups.Advance(world, 2101);
        Assert.False(view.LocalPickups.Contains(new CreatureId(creature.Id)));
        Assert.Contains(view.Creatures(world), c => c.Id == creature.Id);
        Assert.Equal(hash, simulation.StateHash());
    }
    [InstallFact]
    public void QuickDropWaitsForActualHandAndAcknowledgementDoesNotDuplicateIcon()
    {
        (GameDatabase? db, WorldState? world, CreatureState? creature) = Fixture();
        var pending = new LocalPickupFeedback();
        Assert.True(pending.TryQueue(world, db, Keeper, new CreatureId(creature.Id), 0));
        var drop = new DropCommand(Keeper, new TilePos(2, 2));
        Assert.True(pending.TryQueueDrop(drop));
        Assert.False(pending.HasVisiblePendingItems(world, Keeper));
        var stack = new HeldItemStack();
        stack.Refresh(world, Keeper, pending);
        Assert.Empty(stack.Icons);
        Assert.False(pending.HasVisibleItems(world, Keeper));
        var view = new WorldView(db, Keeper);
        Assert.True(view.LocalPickups.TryQueue(world, db, Keeper, new CreatureId(creature.Id), 0));
        Assert.True(view.LocalPickups.TryQueueDrop(drop));
        Assert.False(PlayerInputSnapshot.Capture(view, world, Keeper, db,
            pendingCommands: [new PickUpCreatureCommand(Keeper, creature.Id)]).IsHolding);
        var output = new List<PlayerCommand>();
        pending.DrainReadyDrops(world, output);
        Assert.Empty(output);
        creature.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(creature));
        pending.Advance(world, 10);
        stack.Refresh(world, Keeper, pending);
        Assert.Empty(stack.Icons);
        pending.DrainReadyDrops(world, output);
        Assert.Same(drop, Assert.Single(output));
        output.Clear();
        pending.DrainReadyDrops(world, output);
        Assert.Empty(output);
        Assert.True(pending.Contains(new CreatureId(creature.Id)));
        creature.InHand = false;
        world.Player(Keeper).Hand.Clear();
        pending.Advance(world, 11);
        Assert.False(pending.Contains(new CreatureId(creature.Id)));
    }
    private static (GameDatabase, WorldState, CreatureState) Fixture()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        byte floor = db.Terrain.Values.Single(t => t.Name == "Claimed Path").TerrainId;
        var world = WorldState.FromMap(db, new LevelMap(5, 5,
            Enumerable.Repeat(new MapTile(floor, Keeper.Value, 0, 0), 25).ToArray()), [PlayerIds.Neutral, Keeper.Value]);
        return (db, world, world.SpawnCreature(db, db.CreatureByName("Goblin")!.CreatureId, Keeper, new TilePos(2, 2)));
    }
}
