// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests;

/// <summary>The mesh as simulation state (ADR-0461): a refresh after a tile changed edits the
/// standing mesh around that tile instead of building a new one, so the mesh depends on the order
/// of edits and a save must carry it and the bytes it was last brought to.</summary>
public sealed class WorldSteeringMeshTests
{
    [Fact]
    public void NeighboursUseTheTileRectangleBeforeTheCombinedRadiusCheck()
    {
        var (db, world, steering, mover) = Setup();
        CreatureState touching = world.SpawnCreature(db, 7, PlayerIds.FirstKeeper, mover.Tile);
        touching.PosX += 740;
        CreatureState corner = world.SpawnCreature(db, 7, PlayerIds.FirstKeeper, new TilePos(4, 7));
        CreatureState outside = world.SpawnCreature(db, 7, PlayerIds.FirstKeeper, new TilePos(5, 7));
        WorldSteering.SyncBody(mover); WorldSteering.SyncBody(touching);
        WorldSteering.SyncBody(corner); WorldSteering.SyncBody(outside);
        var neighbours = new List<SteeringNeighbour>();
        steering.CollectNeighbours(mover.Body.Pos, 8256, neighbours);
        Assert.Contains(neighbours, n => ReferenceEquals(n.Body, touching.Body));
        Assert.Contains(neighbours, n => ReferenceEquals(n.Body, corner.Body));
        Assert.DoesNotContain(neighbours, n => ReferenceEquals(n.Body, outside.Body));
    }

    [Fact]
    public void NeighbourUpperYClampKeepsTheNativeWidthQuirk()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var tiles = Enumerable.Repeat(new MapTile(Dk2TerrainFixture.ClaimedPath, PlayerIds.FirstKeeper, 0, 0), 54).ToArray();
        WorldState world = WorldState.FromMap(db, new LevelMap(6, 9, tiles), [PlayerIds.FirstKeeper]);
        CreatureState creature = world.SpawnCreature(db, 7, PlayerIds.FirstKeeper, new TilePos(2, 8));
        WorldSteering.SyncBody(creature);
        var neighbours = new List<SteeringNeighbour>();
        new WorldSteering(db, world).CollectNeighbours(creature.Body.Pos, 8256, neighbours);
        Assert.Empty(neighbours);
    }

    [Fact]
    public void ADugTileIsEditedIntoTheStandingMeshAndRoutable()
    {
        var (db, world, steering, goblin) = Setup();
        int before = steering.Rebuilds;
        Assert.False(steering.TryRoute(goblin, new TilePos(11, 6), MoveOptions.ExactSpot));

        Claim(world, 11, 6);
        steering.Refresh();

        Assert.Equal(before + 1, steering.Rebuilds);
        Assert.True(steering.TryRoute(goblin, new TilePos(11, 6), MoveOptions.ExactSpot));
        steering.Refresh();
        Assert.Equal(before + 1, steering.Rebuilds);
    }

    [Fact]
    public void TheEditedMeshSurvivesASnapshotRoundTrip()
    {
        var (db, world, _, _) = Setup();
        var sim = new GameSimulation(db, world, 5, [new CreatureMovementSystem()]);
        sim.Tick(InputFrame.Empty);
        Claim(world, 11, 6);
        sim.Tick(InputFrame.Empty);
        Claim(world, 12, 6);
        Claim(world, 11, 7);
        sim.Tick(InputFrame.Empty);

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new CreatureMovementSystem()]);
        Assert.Equal(sim.StateHash(), copy.StateHash());

        Hash64 a = Hash64.Start(), b = Hash64.Start();
        sim.Navigation.HashInto(ref a);
        copy.Navigation.HashInto(ref b);
        Assert.Equal(a.Value, b.Value);

        Claim(sim.World, 12, 7);
        Claim(copy.World, 12, 7);
        sim.Tick(InputFrame.Empty);
        copy.Tick(InputFrame.Empty);
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }

    [Fact]
    public void ATileChangedBeforeTheSaveIsEditedAfterTheLoadAsItWouldHaveBeen()
    {
        var (db, world, _, _) = Setup();
        var sim = new GameSimulation(db, world, 5, [new CreatureMovementSystem()]);
        sim.Tick(InputFrame.Empty);
        Claim(world, 11, 6);
        // Saved with the change pending: the loaded run must edit the same tile at its next
        // refresh, not take the loaded tiles as what the mesh already shows.
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new CreatureMovementSystem()]);
        Assert.Equal(sim.StateHash(), copy.StateHash());

        sim.Tick(InputFrame.Empty);
        copy.Tick(InputFrame.Empty);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.Equal(1, copy.Navigation.Rebuilds);
    }

    [Fact]
    public void ASaveWithNothingPendingLoadsToTheSameMeshAndEditsNothing()
    {
        var (db, world, _, _) = Setup();
        var sim = new GameSimulation(db, world, 5, [new CreatureMovementSystem()]);
        sim.Tick(InputFrame.Empty);
        Claim(world, 11, 6);
        sim.Tick(InputFrame.Empty);

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new CreatureMovementSystem()]);
        for (int i = 0; i < 3; i++)
        {
            sim.Tick(InputFrame.Empty);
            copy.Tick(InputFrame.Empty);
        }
        // The loaded run's first refresh compares afresh, finds the saved bytes and platforms and
        // edits nothing, as the saved run's did.
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.Equal(0, copy.Navigation.Rebuilds);
    }

    [Fact]
    public void AnEditedMeshHashesDifferentlyFromAFreshBuildOfTheSameTiles()
    {
        var (db, world, steering, _) = Setup();
        // Tiles kept a tile clear of the map edge: an edit nearer rebuilds the whole map, as the
        // original's does, and then matches a fresh build slot for slot.
        for (int x = 11; x <= 13; x++)
        {
            Claim(world, x, 6);
            steering.Refresh();
        }
        var fresh = new WorldSteering(db, world);
        fresh.Refresh();

        Hash64 a = Hash64.Start(), b = Hash64.Start();
        steering.HashInto(ref a);
        fresh.HashInto(ref b);
        // The two meshes cover the same bytes; the edited one keeps the side points and slots its
        // edits left, so the hash sees a different mesh. Whether those are the original's is Q591.
        Assert.NotEqual(a.Value, b.Value);
    }

    private static void Claim(WorldState world, int x, int y)
    {
        ref TileState tile = ref world.Tile(x, y);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = PlayerIds.FirstKeeper;
    }

    private static (GameDatabase, WorldState, WorldSteering, CreatureState) Setup()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 2; y <= 10; y++)
            for (int x = 2; x <= 10; x++) Claim(world, x, y);
        var steering = new WorldSteering(db, world);
        steering.Refresh();
        CreatureState goblin = world.SpawnCreature(db, 7, PlayerIds.FirstKeeper, new TilePos(3, 6));
        return (db, world, steering, goblin);
    }
}
