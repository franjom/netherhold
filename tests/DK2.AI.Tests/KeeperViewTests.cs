// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI.Tests;

public class KeeperViewTests
{
    [Fact]
    public void AlliancesAndResearchUpgradesRemainFrozenAcrossPacketRoundTrip()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        world.Player(KeeperWorld.Keeper).AlliedWith = 1 << 4;
        world.SetUpgraded(KeeperWorld.Keeper, 5, 4, true);
        var captured = new KeeperView(db, world, KeeperWorld.Keeper);
        byte[] packet = KeeperViewSnapshot.ToBytes(captured);
        KeeperView restored = KeeperViewSnapshot.FromBytes(packet, db);
        Assert.Equal(packet, KeeperViewSnapshot.ToBytes(restored));
        for (byte player = 0; player <= 8; player++)
            Assert.Equal(world.AreAllied(KeeperWorld.Keeper, player), restored.AreAllied(player));
        Assert.Equal(world.PlayerIds, restored.PlayerOrder);
        Assert.True(restored.IsUpgraded(5, 4));
        Assert.False(restored.IsUpgraded(5, 255));

        world.Player(KeeperWorld.Keeper).AlliedWith = 0;
        world.SetUpgraded(KeeperWorld.Keeper, 5, 4, false);
        Assert.True(captured.AreAllied(4));
        Assert.True(captured.IsUpgraded(5, 4));
        var current = new KeeperView(db, world, KeeperWorld.Keeper);
        Assert.False(current.AreAllied(4));
        Assert.False(current.IsUpgraded(5, 4));
    }

    [Fact]
    public void EarlierObservationSchemaIsRejectedExplicitly()
    {
        GameDatabase db = KeeperWorld.Database();
        byte[] packet = KeeperViewSnapshot.ToBytes(new KeeperView(db, KeeperWorld.World(db), KeeperWorld.Keeper));
        packet[4] = 2;
        Assert.Throws<InvalidDataException>(() => KeeperViewSnapshot.FromBytes(packet, db));
    }

    [Fact]
    public void CombatMessagesSurviveTheObservationWindowAndDoNotLeakAcrossPlayers()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        var sim = new GameSimulation(db, world, 1, [new CombatMessageProducer()]);
        sim.Tick();
        var captured = new KeeperView(db, world, KeeperWorld.Keeper);
        PlayerMessage message = Assert.Single(captured.Messages);
        Assert.Equal(PlayerMessageKind.CombatStarted, message.Kind);
        Assert.Equal(0u, message.Timeout);
        Assert.Equal(captured.Messages,
            KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(captured), db).Messages);
        sim.Tick();
        Assert.Empty(new KeeperView(db, world, KeeperWorld.Keeper).Messages);
        Assert.Single(captured.Messages);
    }

    private sealed class CombatMessageProducer : ISimulationSystem
    {
        public string Name => "test combat messages";
        public void Execute(SimulationContext context)
        {
            if (context.Tick != GameTick.Zero) return;
            context.World.Messages.Post(context.World, KeeperWorld.Keeper, PlayerMessageKind.CombatStarted, 4, 5);
            context.World.Messages.Post(context.World, 2, PlayerMessageKind.CombatStarted, 6, 7);
        }
    }

    [Fact]
    public void ObservationPacketRoundTripPreservesTheDeterministicCommandStream()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        world.Tile(new TilePos(10, 10)).SetTaggedBy(KeeperWorld.Keeper, true);
        var captured = new KeeperView(db, world, KeeperWorld.Keeper);
        byte[] packet = KeeperViewSnapshot.ToBytes(captured);
        KeeperView restored = KeeperViewSnapshot.FromBytes(packet, db);
        Assert.Equal(packet, KeeperViewSnapshot.ToBytes(restored));
        Assert.True(restored.IsTaggedByMe(new TilePos(10, 10)));

        var a = new KeeperAI(KeeperWorld.Keeper, KeeperWorld.Parameters(), new SimRandom(31));
        var b = new KeeperAI(KeeperWorld.Keeper, KeeperWorld.Parameters(), new SimRandom(31));
        Hash64 first = Hash64.Start(), second = Hash64.Start();
        for (int i = 0; i < 12; i++)
        {
            new InputFrame(a.Think(captured)).HashInto(ref first);
            new InputFrame(b.Think(restored)).HashInto(ref second);
        }
        Assert.Equal(first.Value, second.Value);
    }

    [Fact]
    public void CapturedTickKeepsTerrainHoldingsAndAvailabilityAfterWorldChanges()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 900;
        var old = new KeeperView(db, world, KeeperWorld.Keeper);
        var sim = new GameSimulation(db, world, 1, []);
        sim.Tick();
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 17;
        world.SetAvailability(KeeperWorld.Keeper, 5, 4, 1);
        world.Player(KeeperWorld.Keeper).Status = PlayerStatus.Lost;
        var changed = new TilePos(13, 13);
        KeeperWorld.Set(world, db, changed, KeeperWorld.TreasuryFloor, KeeperWorld.Keeper);
        var current = new KeeperView(db, world, KeeperWorld.Keeper);

        Assert.Equal(GameTick.Zero, old.Tick);
        Assert.Equal(GameTick.Zero.Next, current.Tick);
        Assert.Equal(900, old.Gold);
        Assert.Equal(17, current.Gold);
        Assert.Equal(3, old.Availability(5, 4));
        Assert.Equal(1, current.Availability(5, 4));
        Assert.Equal(PlayerStatus.Playing, old.Status);
        Assert.Equal(PlayerStatus.Lost, current.Status);
        Assert.False(old.HasRoom(changed));
        Assert.True(current.HasRoom(changed));
        Assert.Null(old.OwnRoomOfType((byte)RoomTypeId.Treasury));
        Assert.NotNull(current.OwnRoomOfType((byte)RoomTypeId.Treasury));
    }

    [Fact]
    public void DecisionsUseTheSuppliedObservationWithoutChangingTheWorld()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        var captured = new KeeperView(db, world, KeeperWorld.Keeper);
        world.SetAvailability(KeeperWorld.Keeper, 5, 4, 1);
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        KeeperAIParameters p = KeeperWorld.Parameters(maxImps: 4) with { TilesPerImpRatio = 10 };
        var fromCapture = new KeeperAI(KeeperWorld.Keeper, p, new SimRandom(7));
        var fromCurrent = new KeeperAI(KeeperWorld.Keeper, p, new SimRandom(7));

        Assert.Contains(fromCapture.Think(captured), c => c is CreateImpCommand);
        Assert.DoesNotContain(fromCurrent.Think(db, world), c => c is CreateImpCommand);
        Assert.Equal(before, sim.StateHash());
        Assert.Throws<ArgumentException>(() => fromCapture.Think(new KeeperView(db, world, 2)));
    }

    [Fact]
    public void ReturnedTilesCannotMutateTheObservationOrWrapAcrossRows()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        var view = new KeeperView(db, world, KeeperWorld.Keeper);
        KeeperTileView copy = view.Tile(KeeperWorld.HeartAt) with { OwnerPlayerId = 2 };

        Assert.Equal(2, copy.OwnerPlayerId);
        Assert.True(view.IsMine(KeeperWorld.HeartAt));
        Assert.Equal(KeeperWorld.Keeper, world.Tile(KeeperWorld.HeartAt).OwnerPlayerId);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Tile(new TilePos(view.Width, 0)));
    }
}
