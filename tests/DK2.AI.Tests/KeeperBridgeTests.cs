// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.AI.Tests;

/// <summary>Task 7 on water and lava (0x004FDC50; computer-keeper-handlers.md §9, ADR-0304): the
/// bridge chosen, the placement test it must pass, the retry, the allowance and the walk after.</summary>
public class KeeperBridgeTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;

    /// <summary>The north edge of the keeper's claimed patch; the corridor leaves it northward.</summary>
    private static readonly TilePos Shore = new(15, 13);

    private static TilePos North(int n) => new(Shore.X, Shore.Y - n);

    /// <summary>Hazard tiles from (15,12) north for <paramref name="length"/> tiles, rock beyond.</summary>
    private static WorldState Corridor(byte hazard, int length, int wood = 3, int stone = 3)
    {
        WorldState world = KeeperWorld.World(Db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        for (int i = 1; i <= length; i++) KeeperWorld.Set(world, Db, North(i), hazard);
        world.SetAvailability(Keeper, 1, (int)RoomTypeId.WoodenBridge, wood);
        world.SetAvailability(Keeper, 1, (int)RoomTypeId.StoneBridge, stone);
        return world;
    }

    private static KeeperAI Walking(TilePos from, TilePos to, byte speed = 1, KeeperTask next = KeeperTask.Done, ulong seed = 7)
    {
        var keeper = new KeeperAI(Keeper, KeeperWorld.Parameters(speed: speed), new SimRandom(seed));
        keeper.State.DigRoomAtCurrent = from;
        keeper.State.DigRoomAt = to;
        keeper.State.BeginTask(KeeperTask.DigPath, next);
        return keeper;
    }

    private static GameSimulation Sim(WorldState world) =>
        new(Db, world, 1, [new TileTagSystem(), new RoomBuildSystem()]);

    [Theory]
    [InlineData(KeeperWorld.Water, 3, 3, RoomTypeId.WoodenBridge)]
    [InlineData(KeeperWorld.Water, 1, 3, RoomTypeId.StoneBridge)]
    [InlineData(KeeperWorld.Water, 4, 3, RoomTypeId.StoneBridge)]
    [InlineData(KeeperWorld.Water, 1, 1, null)]
    [InlineData(KeeperWorld.Lava, 3, 3, RoomTypeId.StoneBridge)]
    [InlineData(KeeperWorld.Lava, 3, 1, null)]
    public void WaterTakesWoodThenStone_LavaOnlyStone_NoneEndsTheTask(byte hazard, int wood, int stone, RoomTypeId? expected)
    {
        WorldState world = Corridor(hazard, 1, wood, stone);
        KeeperAI keeper = Walking(North(1), North(6), next: KeeperTask.DigRectangle);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        BuildRoomCommand? build = commands.OfType<BuildRoomCommand>().SingleOrDefault();
        if (expected is RoomTypeId room)
        {
            Assert.NotNull(build);
            Assert.Equal((byte)room, build!.RoomId);
            Assert.Equal([North(1)], build.Tiles);
            Assert.Equal(KeeperTask.DigPath, keeper.State.Task);
        }
        else
        {
            // Task = NextTask, and NextTask is left at 4 (0x004FE074).
            Assert.Null(build);
            Assert.Equal(KeeperTask.DigRectangle, keeper.State.Task);
            Assert.Equal(KeeperTask.Done, keeper.State.NextTask);
        }
    }

    [Fact]
    public void ASpanTouchingNoOwnFloorIsRetriedOnItsTileAndSpendsTheWholeAllowance()
    {
        WorldState world = Corridor(KeeperWorld.Water, 2);
        // The cursor two tiles out, beyond an unbridged one: no own floor beside it.
        KeeperAI keeper = Walking(North(2), North(6), speed: 6);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        Assert.Empty(commands);
        Assert.Equal(North(2), keeper.State.DigRoomAtCurrent);
        Assert.Equal(KeeperTask.DigPath, keeper.State.Task);
    }

    [Fact]
    public void ASpanTheSimulationRefusesIsAskedForAgainNotWalkedPast()
    {
        WorldState world = Corridor(KeeperWorld.Water, 1);
        GameSimulation sim = Sim(world);
        KeeperAI keeper = Walking(North(1), North(6));

        IReadOnlyList<PlayerCommand> first = keeper.Think(Db, world);
        Assert.Single(first.OfType<BuildRoomCommand>());
        // Between the decision and the tick the shore goes: the placement test refuses.
        KeeperWorld.Set(world, Db, Shore, KeeperWorld.Rock);
        sim.Tick(new InputFrame(first));
        Assert.Equal(KeeperWorld.Water, world.Tile(North(1)).TerrainId);

        Assert.Empty(keeper.Think(Db, world));
        Assert.Equal(North(1), keeper.State.DigRoomAtCurrent);

        KeeperWorld.Set(world, Db, Shore, KeeperWorld.ClaimedPath, Keeper);
        BuildRoomCommand again = Assert.Single(keeper.Think(Db, world).OfType<BuildRoomCommand>());
        Assert.Equal([North(1)], again.Tiles);
    }

    [Fact]
    public void ShortOfGoldOursWaits_WhereDK2WouldBuildTheSpanFree()
    {
        WorldState world = Corridor(KeeperWorld.Water, 1);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 199; // a Wooden Bridge tile costs 200
        KeeperAI keeper = Walking(North(1), North(6));

        Assert.Empty(keeper.Think(Db, world));
        Assert.Equal(North(1), keeper.State.DigRoomAtCurrent);
    }

    /// <summary>The whole crossing through the simulation: spans in tile order, each touching the
    /// last, one a tick at any allowance; the rock beyond tagged; the path ended at its target.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void ACorridorBridgesInOrderOneSpanATick_ThenTagsTheRockBeyondAndEnds(byte speed)
    {
        WorldState world = Corridor(KeeperWorld.Water, 3);
        GameSimulation sim = Sim(world);
        KeeperAI keeper = Walking(Shore, North(5), speed);

        var spans = new List<(int Tick, TilePos Tile)>();
        var tags = new List<TilePos>();
        for (int tick = 0; tick < 40 && keeper.State.Task == KeeperTask.DigPath; tick++)
        {
            IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
            Assert.True(commands.OfType<BuildRoomCommand>().Count() <= 1);
            foreach (BuildRoomCommand b in commands.OfType<BuildRoomCommand>()) spans.Add((tick, b.Tiles[0]));
            foreach (TagTilesCommand t in commands.OfType<TagTilesCommand>()) tags.AddRange(t.Tiles);
            sim.Tick(new InputFrame(commands));
            KeeperWorld.DigWhatIsTagged(world, Db, Keeper);
        }

        Assert.Equal([North(1), North(2), North(3)], spans.Select(s => s.Tile));
        // Allowance 1 spends tick 0 stepping onto the water; more lets the first span go at once.
        Assert.Equal(speed == 1 ? [1, 2, 3] : [0, 1, 2], spans.Select(s => s.Tick));
        Assert.All(spans, s => Assert.Equal(KeeperWorld.WoodenBridgeFloor, world.Tile(s.Tile).TerrainId));
        Assert.Equal(North(4), tags[0]);
        Assert.NotEqual(KeeperTask.DigPath, keeper.State.Task);
    }

    /// <summary>The free walk after a span belongs to the path that asked for it: a new task (the
    /// timeout, the planner) forgets the request, so a path starting on that bridge pays to walk.</summary>
    [Fact]
    public void ANewPathStartingOnALaidSpanPaysForItsFirstStep()
    {
        WorldState world = Corridor(KeeperWorld.Water, 2);
        KeeperWorld.Set(world, Db, North(1), KeeperWorld.WoodenBridgeFloor, Keeper);
        var keeper = new KeeperAI(Keeper, KeeperWorld.Parameters(speed: 1), new SimRandom(7));
        keeper.State.BridgeRequestedAt = North(1); // left by an earlier path
        keeper.State.DigRoomAtCurrent = North(1);
        keeper.State.DigRoomAt = North(6);
        keeper.State.BeginTask(KeeperTask.DigPath, KeeperTask.Done);

        // The one allowance steps onto (15,11); with a refund it would also have asked to bridge it.
        Assert.Empty(keeper.Think(Db, world).OfType<BuildRoomCommand>());
        Assert.Equal(North(2), keeper.State.DigRoomAtCurrent);
    }

    [Fact]
    public void IdenticalSeedsGiveIdenticalBridgeCommandStreams()
    {
        static ulong Run(ulong seed)
        {
            WorldState world = Corridor(KeeperWorld.Lava, 4);
            GameSimulation sim = Sim(world);
            KeeperAI keeper = Walking(Shore, North(7), speed: 2, seed: seed);
            Hash64 hash = Hash64.Start();
            for (int tick = 0; tick < 30; tick++)
            {
                var frame = new InputFrame(keeper.Think(Db, world));
                frame.HashInto(ref hash);
                sim.Tick(frame);
                KeeperWorld.DigWhatIsTagged(world, Db, Keeper);
            }
            return hash.Value;
        }

        Assert.Equal(Run(42), Run(42));
    }

    /// <summary>The keeper predicts the placement verdict the simulation gives, from the captured
    /// tiles and from a restored observation alike: own floor or an own bridge beside the span
    /// counts; a diagonal, a rival's floor or the keeper's own rock does not.</summary>
    [Theory]
    [InlineData(0, 1, KeeperWorld.ClaimedPath, Keeper, true)]
    [InlineData(0, 1, KeeperWorld.WoodenBridgeFloor, Keeper, true)]
    [InlineData(1, 1, KeeperWorld.ClaimedPath, Keeper, false)]
    [InlineData(0, 1, KeeperWorld.ClaimedPath, 4, false)]
    [InlineData(0, 1, KeeperWorld.Rock, Keeper, false)]
    [InlineData(0, 1, KeeperWorld.DirtPath, 0, false)]
    public void TheObservationPredictsTheSimulationsAdjacencyVerdict(int dx, int dy, byte neighbour, byte owner, bool allowed)
    {
        WorldState world = KeeperWorld.World(Db, morePlayers: [4]);
        var span = new TilePos(22, 22);
        KeeperWorld.Set(world, Db, span, KeeperWorld.Water);
        KeeperWorld.Set(world, Db, new TilePos(span.X + dx, span.Y + dy), neighbour, owner);
        RoomDef wood = Db.Room(RoomTypeId.WoodenBridge);

        RoomPlacement simulation = RoomRules.CanBuild(Db, world, span, wood, Keeper);
        var captured = new KeeperView(Db, world, Keeper);
        KeeperView restored = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(captured), Db);

        Assert.Equal(allowed ? RoomPlacement.Allowed : RoomPlacement.NotTouchingOwnLand, simulation);
        Assert.Equal(simulation, captured.CanBuild(span, wood));
        Assert.Equal(simulation, restored.CanBuild(span, wood));
    }
}
