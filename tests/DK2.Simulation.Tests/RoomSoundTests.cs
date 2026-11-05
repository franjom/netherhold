// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The sounds of building and selling room tiles, all handle 0 in the category of the room's
/// terrain (room definition <c>+0x44E</c>, then the terrain's <c>+0x195</c>): <c>0xF1</c> at the end of
/// <c>Player_BuildRoomTile</c> (<c>0x004C5D87</c>) for a paid tile when the game turn differs from the turn
/// the caller passed; <c>0xF5</c> at the end of <c>Player_SellRoomTile</c> (<c>0x004C5EF8</c>) for a sale
/// with a refund, at the tile's centre, height 0x1000; <c>0x2F8</c> when the build command is accepted
/// (<c>0x00442D57</c>), at the rectangle's lowest corner, height 0x1000.</summary>
public sealed class RoomSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const string Lair = "ROOM_LAIR";

    private static (GameSimulation Sim, ListEventSink Events) Start(int gold = 10000)
    {
        var db = Dk2TerrainFixture.Database();
        var world = Dk2TerrainFixture.Sellable(WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]));
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 3)), db.Room(RoomTypeId.DungeonHeart), Keeper);
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 1)), db.Room(RoomTypeId.Treasury), Keeper);
        GoldStore.Deposit(world, db, Keeper, gold);
        var sim = new GameSimulation(db, world, 7, [new RoomBuildSystem(), new RoomBuildWaveSystem(), new RoomSellSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, events);
    }

    private static ThingSoundRequest[] Sounds(ListEventSink events) =>
        events.Events.Where(e => e.ThingSound.HasValue).Select(e => e.ThingSound!.Value).ToArray();

    private static FpPosition3 At(int x, int y, int height) =>
        new(new Fp32(CreatureState.CentreOf(x)), new Fp32(CreatureState.CentreOf(y)), new Fp32(height));

    [Fact]
    public void ADraggedRoomIsAcceptedAtItsLowestCornerThenEachLaterDiagonalsTilesSoundF1AndTheFirstOnesDoNot()
    {
        var (sim, events) = Start();
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 2, 2, 1, 1)));   // pressed (2,2), released (1,1)

        ThingSoundRequest accepted = Assert.Single(Sounds(events));   // the tile built on the queueing turn is silent
        Assert.Equal(ThingSoundEvent.RoomBuildAccepted, accepted.Event);
        Assert.Equal((ushort)0x2F8, (ushort)accepted.Event);
        Assert.Equal(Lair, accepted.Category);
        Assert.Equal(At(1, 1, 0x1000), accepted.Position);
        Assert.False(accepted.Voiced);

        sim.Tick();
        ThingSoundRequest[] built = Sounds(events).Skip(1).ToArray();
        Assert.Equal(2, built.Length);
        Assert.All(built, s =>
        {
            Assert.Equal(ThingSoundEvent.RoomTileBuilt, s.Event);
            Assert.Equal((ushort)0xF1, (ushort)s.Event);
            Assert.Equal(Lair, s.Category);
            Assert.False(s.Voiced);
        });
        Assert.Equal([At(1, 2, 0), At(2, 1, 0)], built.Select(s => s.Position));   // the wave starts at the released corner (1,1)
        sim.Tick();
        Assert.Equal(At(2, 2, 0), Sounds(events)[3].Position);   // the press corner, last: elapsed 2
    }

    [Fact]
    public void ARectangleThatFailsThePrecheckMakesNoAcceptanceSound()
    {
        var (sim, events) = Start(gold: 0);
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        Assert.Empty(Sounds(events));
    }

    [Fact]
    public void ARectangleThePrecheckPassesSoundsEvenWhenNoQueueSlotIsFree_StronglyInferredSoundIsOnTheIssueSide()
    {
        var (sim, events) = Start();
        sim.Tick(InputFrame.Of(
            BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 1, 1, 1, 1), BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 1, 2, 1, 2),
            BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 2, 1, 2, 1), BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 2, 2, 2, 2)));
        Assert.Equal(4, Sounds(events).Count(s => s.Event == ThingSoundEvent.RoomBuildAccepted));
    }

    [Fact]
    public void ADirectTileBuildSoundsF1ForEachPaidTileEvenOnTheFirstTick()
    {
        // The keeper wrapper passes turn 0 and DK2's game turn is our tick + 1, never 0: it always differs.
        var (sim, events) = Start();
        sim.Tick(InputFrame.Of(BuildRoomCommand.At(Keeper, RoomTypeId.Lair, new TilePos(1, 1), new TilePos(1, 2))));

        ThingSoundRequest[] built = Sounds(events);
        Assert.Equal([ThingSoundEvent.RoomTileBuilt, ThingSoundEvent.RoomTileBuilt], built.Select(s => s.Event));
        Assert.Equal([At(1, 1, 0), At(1, 2, 0)], built.Select(s => s.Position));
        Assert.All(built, s => Assert.Equal(Lair, s.Category));
    }

    [Fact]
    public void ARefusedDirectBuildIsSilent()
    {
        var (sim, events) = Start();
        sim.Tick();
        sim.Tick(InputFrame.Of(BuildRoomCommand.At(Keeper, RoomTypeId.Lair, new TilePos(4, 4))));   // rock
        Assert.Empty(Sounds(events));
    }

    [Fact]
    public void SellingARoomTileSoundsF5InItsRoomsCategoryAtTheTileCentreHeightOne()
    {
        var (sim, events) = Start();
        sim.Tick();
        sim.Tick(InputFrame.Of(BuildRoomCommand.At(Keeper, RoomTypeId.Lair, new TilePos(1, 1))));
        int built = Sounds(events).Length;
        sim.Tick(InputFrame.Of(SellRoomCommand.At(Keeper, new TilePos(1, 1), new TilePos(5, 5))));   // the second tile is not a room

        ThingSoundRequest sold = Assert.Single(Sounds(events).Skip(built));
        Assert.Equal(ThingSoundEvent.RoomTileSold, sold.Event);
        Assert.Equal((ushort)0xF5, (ushort)sold.Event);
        Assert.Equal(Lair, sold.Category);
        Assert.Equal(At(1, 1, 0x1000), sold.Position);
        Assert.False(sold.Voiced);
    }

    /// <summary>Shows the sink does not mutate state, not that a sound could not.</summary>
    [Fact]
    public void AttachingASoundSinkDoesNotChangeTheStateHashOfABuildAndSellRun()
    {
        static GameSimulation Run(ListEventSink? sink)
        {
            var (sim, _) = Start();
            if (sink is not null) sim.ObserveWith(sink);
            sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 1, 1, 2, 2)));
            sim.Run(3);
            sim.Tick(InputFrame.Of(SellRoomCommand.At(Keeper, new TilePos(1, 1))));
            return sim;
        }
        var heard = new ListEventSink();
        GameSimulation with = Run(heard);
        Assert.Equal(5, Sounds(heard).Length);   // accepted, three tiles built after the first turn, one sold
        Assert.Equal(Run(null).StateHash(), with.StateHash());
    }
}
