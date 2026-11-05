// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The floating gold numbers (floating-cost-numbers.md sections 2-3): the build wave's sum at the
/// press corner when it closes, and each paid sale's refund at its own tile.</summary>
public sealed class CostNumberTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Fact]
    public void ADraggedBuildShowsOneSumAtThePressCornerWhenTheWaveCloses()
    {
        var (sim, events) = Start();
        int cost = RoomRules.CostPerTile(sim.Database.Room(RoomTypeId.Lair));
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        sim.Tick();
        Assert.Empty(Amounts(events));
        sim.Tick();
        ViewEffectRequest shown = Assert.Single(Amounts(events));
        Assert.Equal(new EffectId(CostNumberRules.GoldEffectId), shown.EffectId);
        Assert.Equal(4 * cost, shown.Amount);
        Assert.Equal(new PlayerId(Keeper), shown.Owner);
        Assert.Equal(new TilePos(1, 1), shown.Tile);
        Assert.Equal(0, shown.Height.Raw);
        Assert.Equal(EffectVisibility.Gated, shown.Visibility);
        sim.Run(3);
        Assert.Single(Amounts(events));
    }

    [Fact]
    public void AMoneyRefusalShowsWhatWasPaidAtThePressCorner()
    {
        var (sim, events) = Start();
        int cost = RoomRules.CostPerTile(sim.Database.Room(RoomTypeId.Lair));
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 1, 1, 2, 2)));
        Assert.True(GoldStore.TryWithdraw(sim.World, Keeper, GoldStore.Total(sim.World, Keeper)));
        sim.Tick();
        ViewEffectRequest shown = Assert.Single(Amounts(events));
        Assert.Equal(cost, shown.Amount);
        Assert.Equal(new TilePos(1, 1), shown.Tile);
    }

    [Fact]
    public void ABuildThatPlacedNothingShowsNothing()
    {
        var (sim, events) = Start();
        sim.World.Tile(new TilePos(2, 1)).OwnerPlayerId = PlayerIds.Neutral;
        sim.Tick(InputFrame.Of(BuildRoomCommand.Rect(Keeper, RoomTypeId.Lair, 2, 1, 2, 1)));
        sim.Run(2);
        Assert.DoesNotContain(sim.World.RoomBuildWaves, slot => slot.HasValue);
        Assert.Empty(Amounts(events));
    }

    [Fact]
    public void ADraggedSaleShowsEachTilesRefundAtThatTileOneDiagonalATurn()
    {
        var (sim, events) = Start();
        RoomDef treasury = sim.Database.Room(RoomTypeId.Treasury);
        foreach (int x in new[] { 1, 2 })
            RoomRules.Place(sim.Database, ref sim.World.Tile(new TilePos(x, 1)), treasury, Keeper);
        int refund = RoomRules.RefundPerTile(sim.Database, treasury);
        sim.Tick(InputFrame.Of(SellRoomCommand.Rect(Keeper, 1, 1, 3, 1)));
        Assert.Equal([new TilePos(3, 1)], Amounts(events).Select(a => a.Tile));
        sim.Run(2);
        ViewEffectRequest[] shown = Amounts(events);
        Assert.Equal([new TilePos(3, 1), new TilePos(2, 1), new TilePos(1, 1)], shown.Select(a => a.Tile));
        Assert.All(shown, a =>
        {
            Assert.Equal(refund, a.Amount);
            Assert.Equal(0x1000, a.Height.Raw);
            Assert.Equal(new EffectId(CostNumberRules.GoldEffectId), a.EffectId);
        });
    }

    [Fact]
    public void AnImmediateSaleShowsTheRefundOfEachTileItSold()
    {
        var (sim, events) = Start();
        sim.Tick(InputFrame.Of(SellRoomCommand.At(Keeper, new TilePos(3, 1), new TilePos(2, 2))));
        ViewEffectRequest shown = Assert.Single(Amounts(events));
        Assert.Equal(new TilePos(3, 1), shown.Tile);
    }

    private static ViewEffectRequest[] Amounts(ListEventSink events) => events.Events
        .Where(e => e.EffectRequest?.Amount.HasValue == true).Select(e => e.EffectRequest!.Value).ToArray();

    private static (GameSimulation Sim, ListEventSink Events) Start()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 3)), db.Room(RoomTypeId.DungeonHeart), Keeper);
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 1)), db.Room(RoomTypeId.Treasury), Keeper);
        GoldStore.Deposit(world, db, Keeper, 10000);
        var sim = new GameSimulation(db, world, 11,
            [new RoomBuildSystem(), new RoomSellSystem(), new RoomBuildWaveSystem(), new RoomSellWaveSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }
}
