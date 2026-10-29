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

public sealed class HandDropFeedbackTests
{
    private static readonly PlayerId Keeper = new(3);
    private static readonly TilePos Start = new(1, 1), Floor = new(2, 2), Wall = new(4, 4);

    [InstallTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnlyAnAcceptedDropRequestsHandFeedback(bool gold, bool refused)
    {
        var (sim, item) = World(gold);
        sim.World.Player(Keeper).Hand.Add(item);
        if (item.IsObject) sim.World.ObjectById(item.Id)!.InHand = true;
        else sim.World.CreatureById(item.Id)!.InHand = true;
        var sink = new ListEventSink();
        var feedback = new HandCursorFeedback();
        var observer = new HandCursorEventSink(feedback, Keeper, sink);
        sim.ObserveWith(observer);

        sim.Tick(InputFrame.Of(new DropCommand(Keeper, refused ? Wall : Floor)));
        observer.Drain(1000);

        Assert.Equal(refused ? 0 : 1, sink.Events.Count(e => e.Kind == "hand_drop_accepted"));
        Assert.Equal(refused ? 1 : 0, sim.World.Player(Keeper).Hand.Count);
        HandCursorKind expected = refused
            ? gold ? HandCursorKind.HoldGold : HandCursorKind.HoldThing
            : gold ? HandCursorKind.DropGold : HandCursorKind.DropThing;
        Assert.Equal(expected, feedback.Resolve(HandCursorKind.Point, refused, gold, false, 1000));
    }

    [InstallTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void APendingPickupCanQueueADropButFeedbackWaitsForItsAcceptance(bool gold, bool refused)
    {
        var (sim, item) = World(gold);
        PlayerCommand pickup = item.IsObject
            ? new PickUpObjectCommand(Keeper, item.Id) : new PickUpCreatureCommand(Keeper, item.Id);
        var feedback = new HandCursorFeedback();
        var observer = new HandCursorEventSink(feedback, Keeper);
        sim.ObserveWith(observer);
        TilePos tile = refused ? Wall : Floor;
        PlayerInputSnapshot snapshot = PlayerInputSnapshot.Capture(new WorldView(sim.Database, Keeper),
            sim.World, Keeper, sim.Database, tile, [pickup]);
        var intent = new PlayerIntent(Keeper, PlayerToolMode.Hand, tile, PointerButtonTransition.None,
            SecondaryButton: PointerButtonTransition.Pressed);
        DropCommand drop = Assert.IsType<DropCommand>(Assert.Single(new PlayerInputTranslator().Translate(intent, snapshot).All));
        Assert.Equal(HandCursorKind.Point, feedback.Resolve(HandCursorKind.Point, false, false, false, 1000));

        sim.Tick(InputFrame.Of(pickup, drop));
        observer.Drain(1000);

        HandCursorKind expected = refused ? HandCursorKind.Point
            : gold ? HandCursorKind.DropGold : HandCursorKind.DropThing;
        Assert.Equal(expected, feedback.Resolve(HandCursorKind.Point, false, false, false, 1000));
        Assert.Equal(refused ? 1 : 0, sim.World.Player(Keeper).Hand.Count);
    }

    [Theory]
    [InlineData(false, 3, HandCursorKind.DropThing)]
    [InlineData(true, 3, HandCursorKind.DropGold)]
    [InlineData(false, 4, HandCursorKind.Point)]
    [InlineData(true, 4, HandCursorKind.Point)]
    public void CursorFeedbackUsesTheReleasedItemAndOnlyTheViewingPlayer(bool gold, byte player, HandCursorKind expected)
    {
        var feedback = new HandCursorFeedback();
        var sink = new ListEventSink();
        var observer = new HandCursorEventSink(feedback, Keeper, sink);
        observer.Emit(new SimEvent(GameTick.Zero, "Hand", "hand_drop_accepted", "")
            { HandDrop = new HandDropFeedback(new PlayerId(player), gold) });
        observer.Drain(1000);

        Assert.Equal(expected, feedback.Resolve(HandCursorKind.Point, false, false, false, 1000));
        Assert.Single(sink.Events);
    }

    [InstallFact]
    public void TwoDropsInOneTickAnimateTheActualStackTopRatherThanTheOldGoldTop()
    {
        var (sim, gold) = World(gold: true);
        var creature = sim.World.SpawnCreature(sim.Database, sim.Database.CreatureByName("Goblin")!.CreatureId, Keeper, Start);
        creature.InHand = true;
        sim.World.ObjectById(gold.Id)!.InHand = true;
        sim.World.Player(Keeper).Hand.Add(HandItem.Of(creature));
        sim.World.Player(Keeper).Hand.Add(gold);
        var feedback = new HandCursorFeedback();
        var sink = new ListEventSink();
        var observer = new HandCursorEventSink(feedback, Keeper, sink);
        sim.ObserveWith(observer);

        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Floor), new DropCommand(Keeper, Floor), new DropCommand(Keeper, Floor)));
        observer.Drain(1000);

        Assert.Equal(new[] { true, false }, sink.Events.Where(e => e.HandDrop.HasValue).Select(e => e.HandDrop!.Value.Gold));
        Assert.Equal(HandCursorKind.DropThing, feedback.Resolve(HandCursorKind.Point, false, false, false, 1000));
    }

    [Fact]
    public async Task EventDeliveryDoesNotStartCursorAnimationBeforeTheRenderPass()
    {
        var feedback = new HandCursorFeedback();
        var observer = new HandCursorEventSink(feedback, Keeper);
        await Task.Run(() => observer.Emit(new SimEvent(GameTick.Zero, "Hand", "hand_drop_accepted", "")
            { HandDrop = new HandDropFeedback(Keeper, false) }));

        Assert.Equal(HandCursorKind.Point, feedback.Resolve(HandCursorKind.Point, false, false, false, 1000));
        observer.Drain(1000);
        Assert.Equal(HandCursorKind.DropThing, feedback.Resolve(HandCursorKind.Point, false, false, false, 1000));
    }

    [InstallFact]
    public void AFailedPendingPickupDoesNotAnimateOrDropAnything()
    {
        var (sim, _) = World(gold: false);
        var pickup = new PickUpCreatureCommand(Keeper, int.MaxValue);
        var feedback = new HandCursorFeedback();
        var observer = new HandCursorEventSink(feedback, Keeper);
        sim.ObserveWith(observer);
        PlayerInputSnapshot snapshot = PlayerInputSnapshot.Capture(new WorldView(sim.Database, Keeper),
            sim.World, Keeper, sim.Database, Floor, [pickup]);
        var intent = new PlayerIntent(Keeper, PlayerToolMode.Hand, Floor, PointerButtonTransition.None,
            SecondaryButton: PointerButtonTransition.Pressed);
        DropCommand drop = Assert.IsType<DropCommand>(Assert.Single(new PlayerInputTranslator().Translate(intent, snapshot).All));

        sim.Tick(InputFrame.Of(pickup, drop));
        observer.Drain(1000);

        Assert.Empty(sim.World.Player(Keeper).Hand);
        Assert.Equal(HandCursorKind.Point, feedback.Resolve(HandCursorKind.Point, false, false, false, 1000));
    }

    private static (GameSimulation Sim, HandItem Item) World(bool gold)
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        byte floor = db.Terrain.Values.Single(t => t.Name == "Claimed Path").TerrainId;
        var map = new LevelMap(5, 5, Enumerable.Repeat(new MapTile(floor, Keeper.Value, 0, 0), 25).ToArray());
        WorldState world = WorldState.FromMap(db, map, [PlayerIds.Neutral, Keeper.Value]);
        world.Tile(Wall).TerrainId = db.Terrain.Values.First(t => t.IsSolid).TerrainId;
        HandItem item;
        if (gold)
        {
            ObjectState pile = world.SpawnObject(db, 1, Keeper, Start.Y * world.Width + Start.X);
            pile.GoldHeld = 300;
            item = HandItem.Of(pile);
        }
        else item = HandItem.Of(world.SpawnCreature(db, db.CreatureByName("Goblin")!.CreatureId, Keeper, Start));
        return (new GameSimulation(db, world, 42, [new HandSystem()]), item);
    }
}
