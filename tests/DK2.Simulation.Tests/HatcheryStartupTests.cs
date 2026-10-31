// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class HatcheryStartupTests
{
    [Theory]
    [InlineData(1, 15)]
    [InlineData(3, 15)]
    [InlineData(4, 13)]
    [InlineData(6, 13)]
    [InlineData(7, 12)]
    [InlineData(9, 12)]
    [InlineData(10, 11)]
    [InlineData(11, 10)]
    [InlineData(12, 9)]
    [InlineData(13, 8)]
    [InlineData(14, 7)]
    [InlineData(15, 6)]
    [InlineData(16, 5)]
    [InlineData(17, 4)]
    [InlineData(19, 4)]
    [InlineData(20, 3)]
    [InlineData(25, 3)]
    public void PreplacedRoomFirstAttemptUsesSizeTableWithoutAnExtraConstructorDelay(int tiles, int seconds)
    {
        var (sim, sink) = Start(tiles);
        Assert.Equal(seconds * 4, sim.World.Tile(1, 1).HatcheryStartupIntervalTicks);
        sim.Run(seconds * 4 - 1);
        Assert.DoesNotContain(sink.Events, IsAttempt);
        sim.Tick();
        Assert.Single(sink.Events, IsAttempt);
        sim.Run(seconds * 4);
        Assert.Equal(2, sink.Events.Count(IsAttempt));
    }

    [Fact]
    public void StartupIntervalAndLastGeneratedStampAreHashedAndSaved()
    {
        var (sim, _) = Start(9);
        sim.Run(17);
        byte[] bytes = WorldSnapshot.ToBytes(sim);
        GameSimulation restored = WorldSnapshot.FromBytes(bytes, sim.Database, [new HatcherySystem()]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        sim.Run(32);
        restored.Run(32);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        ulong before = sim.StateHash();
        sim.World.Tile(1, 1).HatcheryStartupIntervalTicks++;
        Assert.NotEqual(before, sim.StateHash());
        before = sim.StateHash();
        sim.World.Tile(1, 1).HatcheryLastGeneratedTick++;
        Assert.NotEqual(before, sim.StateHash());
    }

    [Fact]
    public void AddingATileReplacesStartupIntervalWithFormulaAndKeepsTheClock()
    {
        var (sim, sink) = Start(9);
        sim.Run(13);
        ref TileState tile = ref sim.World.Tile(10, 1);
        byte before = tile.TerrainId, owner = tile.OwnerPlayerId;
        RoomRules.Place(sim.Database, ref tile, sim.Database.Room(RoomTypeId.Hatchery), 3);
        SlabChain.Changed(sim.World, sim.Database, 1 * sim.World.Width + 10, before, owner);
        Assert.Equal(0, sim.World.Tile(1, 1).HatcheryStartupIntervalTicks);
        int interval = HatcheryRules.LayIntervalSecondsForTiles(sim.Database, 10) * 4;
        sim.Run(interval - 14);
        Assert.DoesNotContain(sink.Events, IsAttempt);
        sim.Tick();
        Assert.Single(sink.Events, IsAttempt);
    }

    [Fact]
    public void RuntimeConstructionUsesFormulaAndWaitsFromItsConstructionTurn()
    {
        var (empty, _) = Start(0);
        empty.Run(13);
        var sim = new GameSimulation(empty.Database, empty.World, 42,
            [new BuildAtTurn(), new HatcherySystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Tick();
        Assert.Equal(0, sim.World.Tile(1, 1).HatcheryStartupIntervalTicks);
        Assert.Equal(14, sim.World.Tile(1, 1).HatcheryLastGeneratedTick);
        sim.Run(27);
        Assert.DoesNotContain(sink.Events, IsAttempt);
        sim.Tick();
        Assert.Single(sink.Events, IsAttempt);
    }

    [Fact]
    public void RemovalRestartsEachPiecesClockAndClearsTheTableOverride()
    {
        var (sim, sink) = Start(9);
        sim.Run(13);
        ref TileState tile = ref sim.World.Tile(5, 1);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        SlabChain.Changed(sim.World, sim.Database, sim.World.Width + 5, Dk2TerrainFixture.HatcheryTerrain, 3);
        foreach (RoomInstance room in RoomIndex.Build(sim.World, sim.Database))
        {
            ref TileState clock = ref sim.World.TileAtIndex(room.TileIndices[0]);
            Assert.Equal(0, clock.HatcheryStartupIntervalTicks);
            Assert.Equal(14, clock.HatcheryLastGeneratedTick);
        }
        sim.Run(40);
        Assert.DoesNotContain(sink.Events, IsAttempt);
        sim.Tick();
        Assert.Equal(2, sink.Events.Count(IsAttempt));
    }

    private sealed class BuildAtTurn : ISimulationSystem
    {
        public string Name => "Build test";
        public void Execute(SimulationContext context)
        {
            if (context.Tick.Value != 13) return;
            for (int x = 1; x <= 9; x++)
            {
                ref TileState tile = ref context.World.Tile(x, 1);
                byte before = tile.TerrainId, owner = tile.OwnerPlayerId;
                RoomRules.Place(context.Database, ref tile, context.Database.Room(RoomTypeId.Hatchery), 3);
                SlabChain.Changed(context.World, context.Database, context.World.Width + x, before, owner);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureKeepsItsClockAndOnlyAnActualMergeClearsTheStartupTable(bool neighbour)
    {
        var (sim, _) = Start(9);
        for (int x = 1; x <= 9; x++) sim.World.Tile(x, 1).OwnerPlayerId = x < 5 && neighbour ? (byte)3 : (byte)2;
        RoomInstance captured = RoomIndex.Build(sim.World, sim.Database).Single(r => r.OwnerPlayerId == 2);
        ref TileState clock = ref sim.World.TileAtIndex(captured.TileIndices[0]);
        clock.HatcheryLastGeneratedTick = 7;
        clock.HatcheryStartupIntervalTicks = 52;
        if (neighbour) sim.World.Tile(1, 1).HatcheryLastGeneratedTick = 2;
        RoomHealthRules.Set(sim.World, sim.Database, captured, 1);
        RoomHealthRules.Convert(sim.World, sim.Database, new TilePos(captured.TileIndices[0] % sim.World.Width,
            captured.TileIndices[0] / sim.World.Width), 3);
        Assert.Equal(7, sim.World.Tile(1, 1).HatcheryLastGeneratedTick);
        Assert.Equal(neighbour ? 0 : 52, sim.World.Tile(1, 1).HatcheryStartupIntervalTicks);
    }

    [Fact]
    public void ConstructionMergeMigratesTheFirstJoinedRoomsClockToTheNewLowestTile()
    {
        var (sim, _) = Start(9);
        sim.World.Tile(5, 1).TerrainId = Dk2TerrainFixture.ClaimedPath;
        sim.World.Tile(1, 1).HatcheryLastGeneratedTick = 2;
        sim.World.Tile(6, 1).HatcheryLastGeneratedTick = 7;
        sim.World.Tile(6, 1).HatcheryStartupIntervalTicks = 52;
        ref TileState added = ref sim.World.Tile(5, 1);
        RoomRules.Place(sim.Database, ref added, sim.Database.Room(RoomTypeId.Hatchery), 3);
        SlabChain.Changed(sim.World, sim.Database, sim.World.Width + 5, Dk2TerrainFixture.ClaimedPath, 3);
        // East is reached before West in the binary's N/E/S/W insertion order.
        Assert.Equal(7, sim.World.Tile(1, 1).HatcheryLastGeneratedTick);
        Assert.Equal(0, sim.World.Tile(1, 1).HatcheryStartupIntervalTicks);
    }

    private static bool IsAttempt(SimEvent e) => e.Kind is "egg_laid" or "lay_refused";

    private static (GameSimulation Sim, ListEventSink Events) Start(int tiles)
    {
        var db = Dk2TerrainFixture.Database();
        int width = 40, height = 4;
        var map = Enumerable.Repeat(new MapTile(Dk2TerrainFixture.Rock, 2, 0, 0), width * height).ToArray();
        for (int x = 1; x <= tiles; x++) map[width + x] = new MapTile(Dk2TerrainFixture.HatcheryTerrain, 3, 0, 0);
        var world = WorldState.FromMap(db, new LevelMap(width, height, map), [(byte)2, (byte)3]);
        var sim = new GameSimulation(db, world, 42, [new HatcherySystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, events);
    }
}
