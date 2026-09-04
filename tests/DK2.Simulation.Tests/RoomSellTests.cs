// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// Selling a room (Q34): <b>half of what it cost, per tile</b>.
///
/// <para>Every number here is read from the shipped data, and that is the point
/// of the question's history. Q34 was recorded for four sessions as a refund
/// "in no field we read" on the strength of a mis-transcribed 100;
/// <c>RoomSellValuePercentageOfCost</c> is <b>50</b>, exactly what the owner
/// observed in the original. `RoomsOnRealDataTests` reads it back out of the
/// install so this fixture cannot drift from it.</para>
///
/// <para>The fixture map is a claimed-path room at (1..3, 1..3) owned by player
/// 1, surrounded by rock — see <see cref="Dk2TerrainFixture"/>.</para>
/// </summary>
public class RoomSellTests
{
    private const byte Keeper = 1;
    private const byte Rival = 2;

    private sealed record Run(GameSimulation Sim, ListEventSink Events)
    {
        public WorldState World => Sim.World;

        public void Tick(params PlayerCommand[] commands) =>
            Sim.Tick(commands.Length == 0 ? InputFrame.Empty : InputFrame.Of(commands));

        public IEnumerable<SimEvent> Of(string kind) => Events.Events.Where(e => e.Kind == kind);
    }

    private static Run Start(ulong seed = 11)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, Keeper, Rival]);

        var sim = new GameSimulation(database, world, seed, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink);
    }

    private static void Give(Run run, RoomTypeId type, byte player, params TilePos[] tiles)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        RoomDef room = database.Room(type);
        foreach (TilePos p in tiles)
        {
            ref TileState tile = ref run.World.Tile(p);
            RoomRules.Place(database, ref tile, room, player);
        }
    }

    // ---- the refund ------------------------------------------------------

    /// <summary>
    /// A treasury costs 200 a tile and refunds 100. Both numbers come from the
    /// data: <c>Cost</c> 200 on the room record, <c>ReturnPercentage</c> 100 on
    /// the same record, and the global <c>RoomSellValuePercentageOfCost</c> 50.
    /// </summary>
    [Fact]
    public void SellingRefundsHalfTheCostPerTile()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        RoomDef treasury = database.Room(RoomTypeId.Treasury);

        Assert.Equal(200, RoomRules.CostPerTile(treasury));
        Assert.Equal(100, RoomRules.RefundPerTile(database, treasury));
    }

    [Fact]
    public void TheRefundIsHalfForEveryBuildableRoomInTheFixture()
    {
        GameDatabase database = Dk2TerrainFixture.Database();

        foreach (RoomDef room in database.Rooms.Values.Where(r => r.IsBuildable))
        {
            Assert.Equal(RoomRules.CostPerTile(room) / 2, RoomRules.RefundPerTile(database, room));
        }
    }

    /// <summary>
    /// The refund goes into the keeper's store, because a player has no gold
    /// counter — gold lives in tiles (ADR-0015). Two treasury tiles, sell one,
    /// and the 100 lands in the other.
    /// </summary>
    [Fact]
    public void TheRefundLandsInTheKeepersOwnStore()
    {
        Run run = Start();
        GameDatabase database = Dk2TerrainFixture.Database();
        Give(run, RoomTypeId.Treasury, Keeper, new TilePos(1, 1), new TilePos(2, 2));

        Assert.Equal(0, GoldStore.Total(run.World, Keeper));

        run.Tick(SellRoomCommand.At(Keeper, new TilePos(1, 1)));

        Assert.Equal(100, GoldStore.Total(run.World, Keeper));
        Assert.Contains(run.Of("sold"), e => e.Detail.Contains("tiles=1") && e.Detail.Contains("refund=100"));
    }

    // ---- what happens to the tile ----------------------------------------

    /// <summary>
    /// The tile goes back to what the terrain record says it becomes when it is
    /// destroyed — Claimed Path — and the keeper <b>keeps the floor</b>. Selling
    /// your own room does not hand the ground to nobody.
    /// </summary>
    [Fact]
    public void ASoldRoomLeavesTheKeepersFloorBehind()
    {
        Run run = Start();
        Give(run, RoomTypeId.Lair, Keeper, new TilePos(2, 2));

        run.Tick(SellRoomCommand.At(Keeper, new TilePos(2, 2)));

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(2, 2).TerrainId);
        Assert.Equal(Keeper, run.World.Tile(2, 2).OwnerPlayerId);
    }

    [Fact]
    public void SellingSeveralTilesSellsThemAll()
    {
        Run run = Start();
        Give(run, RoomTypeId.Treasury, Keeper,
             new TilePos(1, 1), new TilePos(2, 1), new TilePos(3, 1));

        run.Tick(SellRoomCommand.Rect(Keeper, 1, 1, 3, 1));

        Assert.All(new[] { 1, 2, 3 },
            x => Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(x, 1).TerrainId));
    }

    // ---- what cannot be sold ---------------------------------------------

    /// <summary>
    /// <b>Sellable means buildable</b>, so nothing in the rules names a dungeon
    /// heart. A keeper never bought their heart and cannot sell it.
    /// </summary>
    [Fact]
    public void ADungeonHeartCannotBeSold()
    {
        Run run = Start();
        Give(run, RoomTypeId.DungeonHeart, Keeper, new TilePos(2, 2));

        run.Tick(SellRoomCommand.At(Keeper, new TilePos(2, 2)));

        Assert.Equal(Dk2TerrainFixture.DungeonHeartTerrain, run.World.Tile(2, 2).TerrainId);
        Assert.Contains(run.Of("sold"), e => e.Detail.Contains("NotSellable=1"));
    }

    [Fact]
    public void AKeeperCannotSellSomebodyElsesRoom()
    {
        Run run = Start();
        Give(run, RoomTypeId.Treasury, Rival, new TilePos(2, 2));

        run.Tick(SellRoomCommand.At(Keeper, new TilePos(2, 2)));

        Assert.Equal(Dk2TerrainFixture.TreasuryTerrain, run.World.Tile(2, 2).TerrainId);
        Assert.Equal(Rival, run.World.Tile(2, 2).OwnerPlayerId);
        Assert.Contains(run.Of("sold"), e => e.Detail.Contains("NotYourRoom=1"));
    }

    [Fact]
    public void PlainFloorAndRockAreNotRoomsAndCannotBeSold()
    {
        Run run = Start();

        run.Tick(SellRoomCommand.At(Keeper, new TilePos(2, 2), new TilePos(5, 5)));

        Assert.Contains(run.Of("sold"), e => e.Detail.Contains("NoRoomHere=2"));
        Assert.Contains(run.Of("sold"), e => e.Detail.Contains("tiles=0"));
    }

    [Fact]
    public void TilesOffTheMapAreRefusedRatherThanCrashing()
    {
        Run run = Start();

        run.Tick(SellRoomCommand.At(Keeper, new TilePos(-1, 4), new TilePos(999, 999)));

        Assert.Contains(run.Of("sold"), e => e.Detail.Contains("OffMap=2"));
    }

    // ---- what a keeper loses ---------------------------------------------

    /// <summary>
    /// Gold stored on a treasury tile that is sold has nowhere to be. In DK2 it
    /// becomes piles on the floor an imp can fetch; a pile is an OBJECT and
    /// objects arrive at M6 (Q31), so it is <b>reported and dropped</b> rather
    /// than quietly retained.
    /// </summary>
    [Fact]
    public void GoldOnASoldTreasuryTileIsReportedRatherThanQuietlyKept()
    {
        Run run = Start();
        GameDatabase database = Dk2TerrainFixture.Database();
        Give(run, RoomTypeId.Treasury, Keeper, new TilePos(1, 1));
        GoldStore.Deposit(run.World, database, Keeper, 3000);

        Assert.Equal(3000, GoldStore.Total(run.World, Keeper));

        run.Tick(SellRoomCommand.At(Keeper, new TilePos(1, 1)));

        // The store is gone, so its contents are gone with it, and the refund
        // had nowhere to go either.
        Assert.Equal(0, GoldStore.Total(run.World, Keeper));
        Assert.Contains(run.Of("gold_displaced"), e => e.Detail.Contains("amount=3000"));
        Assert.Contains(run.Of("refund_lost"), e => e.Detail.Contains("amount=100"));
    }

    /// <summary>
    /// <b>Selling a keeper's whole treasury in one command loses all of it</b>,
    /// and the chain is worth following because it is not an arithmetic bug.
    ///
    /// <para>The first tile's 100 is banked into the second, which is the only
    /// store left. Then the second tile is sold, so that 100 is displaced along
    /// with anything else on it, and the second tile's own refund has nowhere to
    /// go at all. Both halves are reported.</para>
    ///
    /// <para>In the original none of this is lost: the gold becomes piles on the
    /// floor, and with no treasury left they simply stay there for an imp to
    /// fetch once the keeper builds one. A pile is an OBJECT (M6, Q31), which is
    /// the single reason a keeper here is poorer than a keeper there.</para>
    /// </summary>
    [Fact]
    public void SellingEveryStoreLosesEveryRefund()
    {
        Run run = Start();
        Give(run, RoomTypeId.Treasury, Keeper, new TilePos(1, 1), new TilePos(2, 1));

        run.Tick(SellRoomCommand.Rect(Keeper, 1, 1, 2, 1));

        Assert.Equal(0, GoldStore.Total(run.World, Keeper));

        // The first refund was banked and then displaced; the second never landed.
        Assert.Contains(run.Of("gold_displaced"), e => e.Detail.Contains("amount=100"));
        Assert.Contains(run.Of("refund_lost"), e => e.Detail.Contains("amount=100"));
    }

    /// <summary>
    /// The same sale with somewhere else to put the money keeps all of it: a
    /// lair sold beside a treasury that survives. This is the case that shows
    /// the losses above are about the missing floor pile and not about selling.
    /// </summary>
    [Fact]
    public void ARefundSurvivesWhenTheStoreDoes()
    {
        Run run = Start();
        Give(run, RoomTypeId.Treasury, Keeper, new TilePos(1, 1));
        Give(run, RoomTypeId.Lair, Keeper, new TilePos(2, 1), new TilePos(3, 1));

        run.Tick(SellRoomCommand.Rect(Keeper, 2, 1, 3, 1));

        // Two lair tiles at 300 refund 150 each, and the treasury holds both.
        Assert.Equal(300, GoldStore.Total(run.World, Keeper));
        Assert.Empty(run.Of("refund_lost"));
        Assert.Empty(run.Of("gold_displaced"));
    }

    // ---- selling and building in one tick --------------------------------

    /// <summary>
    /// Selling runs before building, so a keeper can sell one room to afford
    /// another on the same tick. Ours, not measured — see
    /// <see cref="Systems.SimulationSystems"/> — but it is the friendlier of the
    /// two orderings and worth pinning so it cannot change unnoticed.
    /// </summary>
    [Fact]
    public void AKeeperCanSellToAffordSomethingElseOnTheSameTick()
    {
        Run run = Start();
        GameDatabase database = Dk2TerrainFixture.Database();

        // Two lairs at 300 a tile refund 150 each. A treasury tile costs 200,
        // so one lair alone would not pay for it and two do.
        Give(run, RoomTypeId.Lair, Keeper, new TilePos(1, 1), new TilePos(2, 1));
        Give(run, RoomTypeId.Treasury, Keeper, new TilePos(3, 1));

        Assert.Equal(150, RoomRules.RefundPerTile(database, database.Room(RoomTypeId.Lair)));

        run.Tick(
            SellRoomCommand.Rect(Keeper, 1, 1, 2, 1),
            BuildRoomCommand.At(Keeper, (byte)RoomTypeId.Treasury, new TilePos(2, 2)));

        Assert.Equal(Dk2TerrainFixture.TreasuryTerrain, run.World.Tile(2, 2).TerrainId);
        Assert.Equal(100, GoldStore.Total(run.World, Keeper));
    }

    // ---- determinism -----------------------------------------------------

    [Fact]
    public void SellingChangesTheStateHash()
    {
        Run sold = Start();
        Run idle = Start();
        Give(sold, RoomTypeId.Treasury, Keeper, new TilePos(2, 2));
        Give(idle, RoomTypeId.Treasury, Keeper, new TilePos(2, 2));

        sold.Tick(SellRoomCommand.At(Keeper, new TilePos(2, 2)));
        idle.Tick();

        Assert.NotEqual(sold.Sim.StateHash(), idle.Sim.StateHash());
    }
}
