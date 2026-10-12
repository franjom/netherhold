// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class RoomBuildWaveTests
{
    [Fact]
    public void ReleaseCornerThenOneDiagonalPerTickWithAnEffectForEachTile()
    {
        var (sim, events) = Start();
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        Assert.Equal([new TilePos(2, 2)], EffectTiles(events));
        Assert.Equal(1, CountRoom(sim));
        sim.Tick();
        Assert.Equal([new TilePos(2, 2), new TilePos(2, 1), new TilePos(1, 2)], EffectTiles(events));
        Assert.Equal(3, CountRoom(sim));
        sim.Tick();
        Assert.Equal(4, CountRoom(sim));
        Assert.Equal(new TilePos(1, 1), EffectTiles(events)[3]);
    }

    [Fact]
    public void CompletionSkipsLaterSlotsDiagonalWithoutCatchUp()
    {
        var (sim, events) = Start();
        sim.Tick(InputFrame.Of(
            BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 3, 2, 3, 2),
            BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        Assert.Equal([new TilePos(3, 2)], EffectTiles(events));
        sim.Run(2);
        Assert.Equal(4, CountRoom(sim));
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, sim.World.Tile(new TilePos(2, 2)).TerrainId);
        sim.Run(2);
        Assert.Equal(4, CountRoom(sim));
    }

    [Fact]
    public void OnlyThreeOrdersFitAndFourthDoesNotReplaceAQueuedOrder()
    {
        var (sim, _) = Start();
        sim.Tick(InputFrame.Of(
            BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2),
            BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 3, 1, 3),
            BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 2, 3, 2, 3),
            BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 3, 2, 3, 2)));
        sim.Run(4);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, sim.World.Tile(new TilePos(3, 2)).TerrainId);
    }

    [Fact]
    public void ANewOrderReusesTheFirstFreeSlotBeforeAnOlderPendingOrder()
    {
        var (sim, events) = Start();
        sim.Tick(InputFrame.Of(
            BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 3, 2, 3, 2),
            BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 2, 3, 2, 3)));
        sim.Tick();
        Assert.Equal([new TilePos(3, 2), new TilePos(2, 3), new TilePos(1, 1)], EffectTiles(events));
        Assert.DoesNotContain(sim.World.RoomBuildWaves, slot => slot.HasValue);
    }

    [Fact]
    public void GoldSpentBetweenDiagonalsAbortsTheRemainingOrder()
    {
        var (sim, events) = Start();
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        Assert.True(GoldStore.TryWithdraw(sim.World, PlayerIds.FirstKeeper, GoldStore.Total(sim.World, PlayerIds.FirstKeeper)));
        sim.Tick();
        GoldStore.Deposit(sim.World, sim.Database, PlayerIds.FirstKeeper, 10000);
        sim.Run(3);
        Assert.Single(EffectTiles(events));
        Assert.Equal(1, CountRoom(sim));
    }

    [Fact]
    public void PlacementChangedBetweenDiagonalsSkipsOnlyThatTile()
    {
        var (sim, events) = Start();
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        sim.World.Tile(new TilePos(2, 1)).OwnerPlayerId = PlayerIds.Neutral;
        sim.Run(2);
        Assert.Equal(3, CountRoom(sim));
        Assert.DoesNotContain(new TilePos(2, 1), EffectTiles(events));
    }

    [Fact]
    public void SnapshotRestoresPendingWaveAndContinuesIdentically()
    {
        var (sim, _) = Start();
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        Assert.Equal(1, CountRoom(sim));
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database,
            [new RoomBuildSystem(), new RoomBuildWaveSystem()]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        for (int i = 0; i < 4; i++)
        {
            sim.Tick();
            restored.Tick();
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }
        Assert.Equal(4, CountRoom(restored));
    }

    [Fact]
    public void DirectBuilderRetainsImmediateTimingAndDoesNotRequestPlayerWaveEffects()
    {
        var (sim, events) = Start();
        sim.Tick(InputFrame.Of(BuildRoomCommand.At(PlayerIds.FirstKeeper, RoomTypeId.Lair,
            new TilePos(1, 1), new TilePos(1, 2))));
        Assert.Equal(2, CountRoom(sim));
        Assert.Empty(EffectTiles(events));
    }

    [Fact]
    public void FailedWholeRectanglePrecheckDoesNotLeaveAnOrderForLaterGold()
    {
        var (sim, events) = Start();
        GoldStore.TryWithdraw(sim.World, PlayerIds.FirstKeeper, GoldStore.Total(sim.World, PlayerIds.FirstKeeper));
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        GoldStore.Deposit(sim.World, sim.Database, PlayerIds.FirstKeeper, 10000);
        sim.Run(4);
        Assert.Empty(EffectTiles(events));
        Assert.DoesNotContain(sim.World.RoomBuildWaves, slot => slot.HasValue);
    }

    [Fact]
    public void DefeatedKeeperDoesNotAdvanceAnAlreadyQueuedWave()
    {
        var (sim, events) = Start();
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        sim.World.Player(PlayerIds.FirstKeeper).KilledByPlayerId = 4;
        sim.Run(4);
        Assert.Single(EffectTiles(events));
        Assert.Contains(sim.World.RoomBuildWaves, slot => slot.HasValue);
    }

    [Fact]
    public void GoldSpentAfterInputQueueingButBeforeThePlayerFramePreventsTheFirstTile()
    {
        var (sim, events) = Start(context => GoldStore.TryWithdraw(context.World, PlayerIds.FirstKeeper,
            GoldStore.Total(context.World, PlayerIds.FirstKeeper)));
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        Assert.Empty(EffectTiles(events));
        Assert.Equal(0, CountRoom(sim));
        Assert.DoesNotContain(sim.World.RoomBuildWaves, slot => slot.HasValue);
    }

    [Theory]
    [InlineData("press")]
    [InlineData("release")]
    [InlineData("stamp")]
    [InlineData("cost")]
    [InlineData("slot")]
    public void PendingWaveFieldsAndItsSlotAffectTheStateHash(string field)
    {
        var (sim, _) = Start();
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        ulong original = sim.StateHash();
        var slots = Assert.IsType<RoomBuildWaveState?[]>(sim.World.RoomBuildWaves);
        int index = Array.FindIndex(slots, slot => slot.HasValue);
        RoomBuildWaveState wave = slots[index]!.Value;
        if (field == "slot")
        {
            slots[index + 1] = wave;
            slots[index] = null;
        }
        else slots[index] = field switch
        {
            "press" => wave with { Press = new TilePos(0, 1) },
            "release" => wave with { Release = new TilePos(3, 2) },
            "stamp" => wave with { QueuedTick = wave.QueuedTick.Next },
            "cost" => wave with { AccumulatedCost = (ushort)(wave.AccumulatedCost + 1) },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        Assert.NotEqual(original, sim.StateHash());
    }

    private static TilePos[] EffectTiles(ListEventSink events) => events.Events
        .Where(e => e.ConstructionEffect.HasValue).Select(e => e.ConstructionEffect!.Value.Tile).ToArray();

    private static int CountRoom(GameSimulation sim) => Enumerable.Range(0, sim.World.TileCount)
        .Count(i => sim.World.TileAtIndex(i).TerrainId == Dk2TerrainFixture.LairTerrain);

    private static (GameSimulation Sim, ListEventSink Events) Start(Action<SimulationContext>? intervene = null)
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 3)), db.Room(RoomTypeId.DungeonHeart), PlayerIds.FirstKeeper);
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 1)), db.Room(RoomTypeId.Treasury), PlayerIds.FirstKeeper);
        GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 10000);
        ISimulationSystem[] systems = intervene is null ? [new RoomBuildSystem(), new RoomBuildWaveSystem()]
            : [new RoomBuildSystem(), new InterveningSystem(intervene), new RoomBuildWaveSystem()];
        var sim = new GameSimulation(db, world, 7, systems);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, events);
    }

    private sealed class InterveningSystem(Action<SimulationContext> action) : ISimulationSystem
    {
        public string Name => "Intervening";
        public void Execute(SimulationContext context) => action(context);
    }
}
