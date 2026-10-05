// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>Messages 32 to 35 from the room transfer <c>0x004E5650</c> and their subtables
/// (computer-keeper-messages.md, the census; ADR-0353).</summary>
public class RoomOwnershipMessageTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    private static RoomInstance Portal(GameDatabase db, WorldState world, byte owner)
    {
        for (int y = 2; y < 5; y++)
            for (int x = 2; x < 5; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Portal), owner);
        return RoomIndex.RoomAt(world, db, 3 * world.Width + 3)!;
    }

    private static void TakeOver(GameDatabase db, WorldState world, RoomInstance room, byte by) =>
        Assert.Equal(RoomHealthOutcome.TookOver, RoomHealthRules.Apply(world, db, room, -1_000_000, by));

    [Fact]
    public void ANeutralRoomTakenPosts35ToNeutralThen33ToTheTaker()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance portal = Portal(db, world, PlayerIds.Neutral);
        int tag = SlabChain.FirstSlab(world, portal);

        TakeOver(db, world, portal, Keeper);

        Assert.Equal(
            [(PlayerIds.Neutral, PlayerMessageKind.RoomLost), (Keeper, PlayerMessageKind.NeutralRoomCaptured)],
            world.Messages.Entries.Select(m => (m.PlayerId, m.Kind)));
        Assert.All(world.Messages.Entries, m => Assert.Equal(((int)RoomTypeId.Portal, tag), (m.Data1, m.Data2)));
        Assert.All(world.Messages.Entries, m => Assert.Equal((uint)SimulationTiming.TicksFromSeconds(20), m.Timeout));
    }

    [Fact]
    public void AKeepersRoomTakenByAnotherKeeperPosts32ToTheTaker()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance portal = Portal(db, world, Rival);

        TakeOver(db, world, portal, Keeper);

        Assert.Equal(
            [(Rival, PlayerMessageKind.RoomLost), (Keeper, PlayerMessageKind.RoomCaptured)],
            world.Messages.Entries.Select(m => (m.PlayerId, m.Kind)));
    }

    [Fact]
    public void ARoomLostAdmitsDuplicatesWhereARoomCapturedDoesNot()
    {
        WorldState world = World(Db);
        world.Messages.PostOwnership(world, Keeper, PlayerMessageKind.RoomLost, RoomTypeId.Lair, 1);
        world.Messages.PostOwnership(world, Keeper, PlayerMessageKind.RoomLost, RoomTypeId.Lair, 2);
        world.Messages.PostOwnership(world, Keeper, PlayerMessageKind.RoomCaptured, RoomTypeId.Lair, 3);
        world.Messages.PostOwnership(world, Keeper, PlayerMessageKind.RoomCaptured, RoomTypeId.Lair, 4);
        world.Messages.PostOwnership(world, Keeper, PlayerMessageKind.RoomCaptured, RoomTypeId.Portal, 5);

        Assert.Equal([1, 2, 3, 5], world.Messages.Entries.Select(m => m.Data2));
    }

    [Fact]
    public void ATypeOffTheSubtableLastsUntilTheNextSweep()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var sim = new GameSimulation(db, world, seed: 1, []);
        PlayerMessage message = world.Messages.PostOwnership(world, Keeper, PlayerMessageKind.RoomCaptured, RoomTypeId.HeroPortal, 1)!.Value;

        Assert.Equal(0u, message.Timeout);
        sim.Tick();
        Assert.Empty(world.Messages.Entries);
    }

    [Theory]
    [InlineData(PlayerMessageKind.RoomAttacked, 6, 30)]
    [InlineData(PlayerMessageKind.RoomCaptured, 22, 20)]
    [InlineData(PlayerMessageKind.RoomLost, 287, 20)]
    [InlineData(PlayerMessageKind.RoomAttacked, 287, null)]
    [InlineData(PlayerMessageKind.NeutralRoomCaptured, 5, null)]
    [InlineData(PlayerMessageKind.RoomLost, 18, null)]
    public void TheSubtablesListRoomTypesOneToSeventeenButFiveAnd22And287(PlayerMessageKind kind, int room, int? seconds) =>
        Assert.Equal(seconds, PlayerMessageQueue.OwnershipLifetimeSeconds(kind, room));
}
