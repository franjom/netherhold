// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>Message 42 from the claim <c>0x004E5A70</c>: a refused claim with no other room of the
/// type holding a place the creature can reach, and a Prison over its limit after a claim
/// (computer-keeper-messages.md, the census; ADR-0353).</summary>
public class ClaimFailureMessageTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = Dk2TerrainFixture.GoblinCreatureId;

    /// <summary>Paved ground at (1..12, 1..8) the keeper owns, a treasury tile at (12, 8) to pay for
    /// training; rock everywhere else.</summary>
    private static WorldState World(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(width: 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 8; y++)
            for (int x = 1; x <= 12; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = Keeper;
            }
        ref TileState treasury = ref world.Tile(12, 8);
        treasury.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
        treasury.GoldHeld = 100_000;
        return world;
    }

    private static void Room(GameDatabase db, WorldState world, RoomTypeId type, int x0, int y0)
    {
        for (int y = y0; y < y0 + 3; y++)
            for (int x = x0; x < x0 + 3; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), Keeper);
    }

    private static int TrainState => JobRules.StateFor(JobType.Train)!.Value;

    /// <summary>Two goblins fill the furnished 3×3 Training Room at (5..7, 1..3); the third's
    /// claim is refused.</summary>
    private static (WorldState, CreatureState) Refused(GameDatabase db, Action<WorldState> more)
    {
        WorldState world = World(db);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1);
        more(world);
        Dk2TerrainFixture.FurnishRooms(db, world);
        CreatureState[] goblins = [.. Enumerable.Range(0, 3).Select(_ => world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2)))];
        var hooks = new StateHookContext(world, db);
        Assert.True(CreatureStateMachine.TrySetState(hooks, goblins[0], TrainState));
        Assert.True(CreatureStateMachine.TrySetState(hooks, goblins[1], TrainState));
        Assert.False(CreatureStateMachine.TrySetState(hooks, goblins[2], TrainState));
        return (world, goblins[2]);
    }

    [Fact]
    public void ARefusedClaimWithNoOtherRoomPosts42ForTheRoomsTypeToItsOwner()
    {
        var (world, _) = Refused(Dk2TerrainFixture.Database(), _ => { });

        PlayerMessage message = Assert.Single(world.Messages.Entries);
        Assert.Equal((PlayerMessageKind.RoomFull, Keeper, (int)RoomTypeId.TrainingRoom, 0),
            (message.Kind, message.PlayerId, message.Data1, message.Data2));
    }

    [Fact]
    public void AnotherRoomOfTheTypeWithAPlaceItCanReachRaisesNothing()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var (world, _) = Refused(db, w => Room(db, w, RoomTypeId.TrainingRoom, 9, 5));

        Assert.Empty(world.Messages.Entries);
    }

    [Fact]
    public void AnotherRoomWithAPlaceBehindRockStillLetsThe42Through()
    {
        // The second room sits in the rock east of the paved ground, reachable by no route (0x004C6AE0's
        // 0x80 route test); the first slab stands in for the room's floor-slot position +0x2B.
        GameDatabase db = Dk2TerrainFixture.Database();
        var (world, _) = Refused(db, w => Room(db, w, RoomTypeId.TrainingRoom, 16, 1));

        Assert.Equal(PlayerMessageKind.RoomFull, Assert.Single(world.Messages.Entries).Kind);
    }

    [Fact]
    public void APrisonClaimPastItsLimitPosts42ForThePrison_UnderOurUncheckedPrisonClaim()
    {
        // A 3×3 Prison holds two (its inner tile plus one); ours admits the third because the claim's
        // Prison tests are not built (Q504), and the limit test after the claim then raises 42.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.Prison, 5, 1);
        var hooks = new StateHookContext(world, db);
        for (int i = 0; i < 3; i++)
        {
            CreatureState captive = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));
            Assert.True(CreatureStateMachine.TrySetState(hooks, captive, PrisonRules.DecidingState));
            Assert.Equal(i < 2 ? 0 : 1, world.Messages.Entries.Count);
        }

        Assert.Equal((int)RoomTypeId.Prison, Assert.Single(world.Messages.Entries).Data1);
    }

    [Fact]
    public void A42ForARoomTypeItsSubtableOmitsLastsOneSweep()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        PlayerMessage message = world.Messages.PostRoom(world, Keeper, PlayerMessageKind.RoomFull, RoomTypeId.Crypt)!.Value;
        Assert.Equal(0u, message.Timeout);
        new GameSimulation(db, world, seed: 1, []).Tick();
        Assert.Empty(world.Messages.Entries);
    }
}
