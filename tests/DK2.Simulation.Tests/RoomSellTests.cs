// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>Selling a room (Q34): half of what it cost, per tile.
/// <c>RoomSellValuePercentageOfCost</c> is 50 in the shipped data, exactly what
/// the owner observed; <c>RoomsOnRealDataTests</c> reads it back out of the install
/// so this fixture cannot drift. Check our own dump before recording a number as
/// absent — Q34 stood for four sessions as a refund "in no field we read" on the
/// strength of a mis-transcribed 100. The fixture map is a claimed-path room at
/// (1..3, 1..3) owned by player 1, surrounded by rock
/// (<see cref="Dk2TerrainFixture"/>).</summary>
public class RoomSellTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;

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
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

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

    /// <summary>Gold stored on a treasury tile that is sold stays on that tile as
    /// a pile on the floor. Exact, not plausible: selling walks the room's gold
    /// objects and for each makes a floor pile on the same tile, moves the amount
    /// across and subtracts it from the player's total, so the keeper is poorer by
    /// 3000 and the world is not (gold-on-the-floor.md §4). The pile is over the
    /// limit and stays there: a treasury tile holds 3000,
    /// <c>MaxGoldPileOutsideTreasury</c> is 2000, and nothing clamps on the way
    /// out — which is why that variable is not applied as an invariant.</summary>
    [Fact]
    public void GoldOnASoldTreasuryTileEndsUpOnTheFloorOfThatTile()
    {
        Run run = Start();
        GameDatabase database = Dk2TerrainFixture.Database();
        Give(run, RoomTypeId.Treasury, Keeper, new TilePos(1, 1));
        GoldStore.Deposit(run.World, database, Keeper, 3000);

        Assert.Equal(3000, GoldStore.Total(run.World, Keeper));

        run.Tick(SellRoomCommand.At(Keeper, new TilePos(1, 1)));

        // Off the keeper's books entirely -- floor gold belongs to nobody until
        // it is carried back in.
        Assert.Equal(0, GoldStore.Total(run.World, Keeper));

        int tile = (1 * run.World.Width) + 1;
        Assert.Equal(3000 + 100, FloorGold.OnTile(run.World, database, tile));
        Assert.Equal(3000 + 100, FloorGold.Total(run.World, database));

        Assert.Contains(run.Of("gold_displaced"), e => e.Detail.Contains("amount=3000"));
        Assert.Contains(run.Of("refund_overflowed"), e => e.Detail.Contains("amount=100"));

        // Over the outside-treasury limit, and left that way on purpose.
        ObjectState biggest = run.World.Objects
            .Where(o => FloorGold.IsPile(database, o))
            .OrderByDescending(o => o.GoldHeld).First();
        Assert.True(biggest.GoldHeld > FloorGold.MaxPile(database));
        Assert.False(FloorGold.AcceptsMore(database, biggest));
    }

    /// <summary>Selling a keeper's whole treasury moves every coin to the floor,
    /// and none of it is lost. The first tile's refund of 100 is banked into the
    /// second, the only store left; then the second tile is sold, so that 100 is
    /// displaced onto the floor along with anything else on it, and the second
    /// tile's own refund has nowhere to be banked and lands on the floor too. The
    /// keeper's total is zero and the world still holds 200.</summary>
    [Fact]
    public void SellingEveryStorePutsEveryRefundOnTheFloor()
    {
        Run run = Start();
        GameDatabase database = Dk2TerrainFixture.Database();
        Give(run, RoomTypeId.Treasury, Keeper, new TilePos(1, 1), new TilePos(2, 1));

        run.Tick(SellRoomCommand.Rect(Keeper, 1, 1, 2, 1));

        Assert.Equal(0, GoldStore.Total(run.World, Keeper));
        Assert.Equal(200, FloorGold.Total(run.World, database));

        // The first refund was banked and then displaced; the second never landed.
        Assert.Contains(run.Of("gold_displaced"), e => e.Detail.Contains("amount=100"));
        Assert.Contains(run.Of("refund_overflowed"), e => e.Detail.Contains("amount=100"));
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
