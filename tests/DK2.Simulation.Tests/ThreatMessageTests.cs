// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>Messages 12 (<c>0x0045313E</c>) and 34 (action 11, <c>0x00497F29</c>) from tile work
/// (computer-keeper-messages.md, the census; ADR-0353).</summary>
public class ThreatMessageTests
{
    private const byte Keeper = 3;
    private const byte Rival = 4;
    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    private static WorldState World() => WorldState.FromMap(
        Db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);

    private static void Work(WorldState world, int x, int y, TileWorkKind kind, byte playerId, int times = 1)
    {
        var sim = new GameSimulation(Db, world, seed: 1, [new Systems.TileWorkSystem()]);
        for (int i = 0; i < times; i++)
        {
            world.QueueWork(new TileWorkOrder((y * world.Width) + x, kind, playerId));
            sim.Tick(InputFrame.Empty);
        }
    }

    /// <summary>Works the wall at (6, 6) until it falls; stopping there keeps the fifty-second 12 in
    /// the queue.</summary>
    private static void UntilTheWallFalls(WorldState world, TileWorkKind kind, byte playerId)
    {
        var sim = new GameSimulation(Db, world, seed: 1, [new Systems.TileWorkSystem()]);
        for (int i = 0; i < 3000 && world.Tile(6, 6).TerrainId == Dk2TerrainFixture.ReinforcedWall; i++)
        {
            world.QueueWork(new TileWorkOrder((6 * world.Width) + 6, kind, playerId));
            sim.Tick(InputFrame.Empty);
        }
    }

    [Fact]
    public void AHeroBlowThatLeavesTheRoomStandingPosts34ToItsOwner()
    {
        WorldState world = World();
        for (int x = 5; x < 8; x++) RoomRules.Place(Db, ref world.Tile(x, 5), Db.Room(RoomTypeId.Treasury), Keeper);
        int tag = SlabChain.FirstSlab(world, RoomIndex.RoomAt(world, Db, (5 * world.Width) + 6)!);

        Work(world, 6, 5, TileWorkKind.AttackRoom, PlayerIds.Good);

        PlayerMessage message = Assert.Single(world.Messages.Entries);
        Assert.Equal((PlayerMessageKind.RoomAttacked, Keeper, (int)RoomTypeId.Treasury, tag),
            (message.Kind, message.PlayerId, message.Data1, message.Data2));
        Assert.Equal((uint)SimulationTiming.TicksFromSeconds(30), message.Timeout);
    }

    [Fact]
    public void ARivalKeepersBlowAtARoomPostsNo34()
    {
        WorldState world = World();
        RoomRules.Place(Db, ref world.Tile(5, 5), Db.Room(RoomTypeId.Treasury), Keeper);
        Work(world, 5, 5, TileWorkKind.AttackRoom, Rival);
        Assert.Empty(world.Messages.Entries);
    }

    [Fact]
    public void ARivalBringingDownTheKeepersReinforcedWallPosts12WithTheTile()
    {
        WorldState world = World();
        ref TileState wall = ref world.Tile(6, 6);
        wall.TerrainId = Dk2TerrainFixture.ReinforcedWall;
        wall.OwnerPlayerId = Keeper;
        wall.Health = Db.TerrainById(Dk2TerrainFixture.ReinforcedWall).StartingHealth;
        Assert.True(Db.TerrainById(Dk2TerrainFixture.ReinforcedWall) is { IsSolid: true, IsOwnable: true });

        UntilTheWallFalls(world, TileWorkKind.DigEnemyWall, Rival);

        Assert.NotEqual(Dk2TerrainFixture.ReinforcedWall, world.Tile(6, 6).TerrainId);
        PlayerMessage message = Assert.Single(world.Messages.Entries);
        Assert.Equal((PlayerMessageKind.WallBreached, Keeper, 6, 6), (message.Kind, message.PlayerId, message.Data1, message.Data2));
    }

    [Fact]
    public void TheKeepersOwnWorkOnItsWallPostsNo12()
    {
        WorldState world = World();
        ref TileState wall = ref world.Tile(6, 6);
        wall.TerrainId = Dk2TerrainFixture.ReinforcedWall;
        wall.OwnerPlayerId = Keeper;
        wall.Health = Db.TerrainById(Dk2TerrainFixture.ReinforcedWall).StartingHealth;

        UntilTheWallFalls(world, TileWorkKind.DigOwnWall, Keeper);

        Assert.NotEqual(Dk2TerrainFixture.ReinforcedWall, world.Tile(6, 6).TerrainId);
        Assert.Empty(world.Messages.Entries);
    }
}
