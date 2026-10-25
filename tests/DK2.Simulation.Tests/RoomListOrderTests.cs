// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

public class RoomListOrderTests
{
    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();
    private static WorldState World() => WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, (byte)3, (byte)4]);

    private static void Build(WorldState world, int x, int y, byte owner = 3)
    {
        ref TileState tile = ref world.Tile(x, y);
        byte terrain = tile.TerrainId, previousOwner = tile.OwnerPlayerId;
        RoomRules.Place(Db, ref tile, Db.Room(RoomTypeId.Treasury), owner);
        SlabChain.Changed(world, Db, y * world.Width + x, terrain, previousOwner);
    }

    private static TilePos[] Rooms(WorldState world)
    {
        var probe = new Probe();
        new GameSimulation(Db, world, seed: 1, systems: [probe]).Tick();
        return probe.Positions;
    }

    private sealed class Probe : ISimulationSystem
    {
        public string Name => "room order probe";
        public TilePos[] Positions = [];
        public void Execute(SimulationContext context) => Positions = new RoomCache().Of(context, (byte)3)
            .Where(r => r.Definition.TypeId == RoomTypeId.Treasury)
            .Select(r => new TilePos(r.MinX, r.MinY)).ToArray();
    }

    [Fact]
    public void ConstructionInsertsAtTheHeadAndGrowthKeepsItsPosition()
    {
        var world = World();
        Build(world, 5, 5);
        Build(world, 9, 9);
        Build(world, 6, 5);
        Assert.Equal([new TilePos(9, 9), new TilePos(5, 5)], Rooms(world));
    }

    [Fact]
    public void ATransferInsertsTheWholeRoomAtTheNewOwnersHead()
    {
        var world = World();
        Build(world, 9, 9, 4);
        Build(world, 5, 5);
        var room = RoomIndex.RoomAt(world, Db, 9 * world.Width + 9)!;
        RoomHealthRules.Apply(world, Db, room, -1_000_000, (byte)3);
        Assert.Equal([new TilePos(9, 9), new TilePos(5, 5)], Rooms(world));
    }

    [Fact]
    public void RemovingATileRecreatesItsPiecesAtTheHeadInReplayOrder()
    {
        var world = World();
        Build(world, 5, 5); Build(world, 6, 5); Build(world, 7, 5);
        Build(world, 10, 10);
        ref TileState tile = ref world.Tile(6, 5);
        byte terrain = tile.TerrainId, owner = tile.OwnerPlayerId;
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        SlabChain.Changed(world, Db, 5 * world.Width + 6, terrain, owner);
        Assert.Equal([new TilePos(7, 5), new TilePos(5, 5), new TilePos(10, 10)], Rooms(world));
    }

    [Fact]
    public void JoiningRoomsKeepsTheFirstNeighbourAndDropsTheAbsorbedEntry()
    {
        var world = World();
        Build(world, 5, 5); Build(world, 10, 10); Build(world, 7, 5);
        Build(world, 6, 5);
        Assert.Equal([new TilePos(5, 5), new TilePos(10, 10)], Rooms(world));
    }

    [Fact]
    public void CompletedConversionKeepsTheCapturedRoomsNewPositionAfterItsFollowupMerge()
    {
        var world = World();
        Build(world, 5, 5); Build(world, 9, 9); Build(world, 6, 5, 4);
        var room = RoomIndex.RoomAt(world, Db, 5 * world.Width + 6)!;
        RoomHealthRules.Set(world, Db, room, 1);
        world.QueueWork(6, 5, TileWorkKind.ConvertRoom, (byte)3);
        new GameSimulation(Db, world, seed: 1, systems: [new Systems.TileWorkSystem()]).Tick();
        Assert.Equal([new TilePos(5, 5), new TilePos(9, 9)], Rooms(world));
    }

    [Fact]
    public void ListPositionsAndTheirNextInsertionSurviveSnapshotAndAffectTheHash()
    {
        var world = World();
        Build(world, 5, 5); Build(world, 9, 9);
        var sim = new GameSimulation(Db, world, seed: 7, systems: []);
        byte[] saved = WorldSnapshot.ToBytes(sim);
        ulong hash = sim.StateHash();
        RoomListPosition position = world.Tile(5, 5).RoomListPosition;
        world.Tile(5, 5).RoomListPosition = new RoomListPosition(-1);
        Assert.NotEqual(hash, sim.StateHash());
        world.Tile(5, 5).RoomListPosition = position;
        var restored = WorldSnapshot.FromBytes(saved, Db, []);
        Assert.Equal(hash, restored.StateHash());
        Build(world, 10, 7); Build(restored.World, 10, 7);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal([new TilePos(10, 7), new TilePos(9, 9), new TilePos(5, 5)], Rooms(restored.World));
    }

    [Fact]
    public void LoadingKeepsTheNorthRoomsIdentityWhenAnOlderWestRoomIsAbsorbed()
    {
        var map = Dk2TerrainFixture.Map();
        var tiles = map.Tiles.ToArray();
        foreach (var pos in new[] { new TilePos(5, 4), new TilePos(7, 4), new TilePos(5, 5), new TilePos(6, 5), new TilePos(7, 5) })
            tiles[pos.Y * map.Width + pos.X] = new MapTile(Dk2TerrainFixture.TreasuryTerrain, 3, 0, 0);
        var world = WorldState.FromMap(Db, new LevelMap(map.Width, map.Height, tiles), [(byte)3]);
        Assert.Equal(world.Tile(7, 4).RoomListPosition, world.Tile(5, 4).RoomListPosition);
        Assert.NotEqual(default, world.Tile(5, 4).RoomListPosition);
        Assert.Equal(2, world.Tile(5, 4).RoomListPosition.Value);
        Assert.Single(Rooms(world));
    }

    [Fact]
    public void DefeatTransfersInPlayerListOrderAheadOfExistingNeutralRooms()
    {
        var world = World();
        ref TileState heart = ref world.Tile(3, 3);
        byte terrain = heart.TerrainId, owner = heart.OwnerPlayerId;
        RoomRules.Place(Db, ref heart, Db.Room(RoomTypeId.DungeonHeart), (byte)3);
        SlabChain.Changed(world, Db, 3 * world.Width + 3, terrain, owner);
        Build(world, 2, 8, PlayerIds.Neutral);
        Build(world, 9, 9); Build(world, 5, 5);
        var probe = new DefeatProbe();
        new GameSimulation(Db, world, seed: 1, systems: [probe]).Tick();
        Assert.Equal([new TilePos(9, 9), new TilePos(5, 5), new TilePos(2, 8)], probe.Positions);
    }

    private sealed class DefeatProbe : ISimulationSystem
    {
        public string Name => "defeat order probe";
        public TilePos[] Positions = [];
        public void Execute(SimulationContext context)
        {
            DefeatRules.OnDeathTurn(context, (byte)3);
            Positions = RoomLists.Of(context.World, context.Database, PlayerIds.Neutral)
                .Where(r => r.Definition.TypeId == RoomTypeId.Treasury)
                .Select(r => new TilePos(r.MinX, r.MinY)).ToArray();
        }
    }
}
