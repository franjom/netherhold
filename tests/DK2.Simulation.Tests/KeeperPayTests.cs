// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The pay remedy's withdrawal (<c>0x004BA420</c>'s spend through <c>0x004F4510</c>) and
/// the command that mints the wage into the hand (computer-keeper-management.md §13, ADR-0353).</summary>
public class KeeperPayTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    private static void Store(GameDatabase db, WorldState world, RoomTypeId type, int x, int y, int width, int gold)
    {
        for (int i = 0; i < width; i++)
        {
            ref TileState tile = ref world.Tile(x + i, y);
            RoomRules.Place(db, ref tile, db.Room(type), Keeper);
            tile.GoldHeld = gold;
        }
    }

    private static int Held(GameDatabase db, WorldState world, int x, int y) =>
        RoomIndex.RoomAt(world, db, y * world.Width + x)!.TileIndices.Sum(i => world.TileAtIndex(i).GoldHeld);

    [Fact]
    public void TheSpendSharesOverTreasuriesPlusOneAndStopsWhenAPassTakesNothing()
    {
        // Two Treasuries hold 300 and 50; 600 is asked. Passes of 200, 116 and 83 per store take
        // 250, then 100, then nothing: 350 in all, less than asked.
        GameDatabase db = Db;
        WorldState world = World(db);
        Store(db, world, RoomTypeId.Treasury, 2, 2, 1, 300);
        Store(db, world, RoomTypeId.Treasury, 2, 5, 1, 50);

        Assert.Equal(350, GoldStore.Spend(world, db, Keeper, 600));
        Assert.Equal(0, GoldStore.Total(world, Keeper));
    }

    [Fact]
    public void AShareOfZeroLetsTheFirstStoreGiveTheWholeRemainder()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Store(db, world, RoomTypeId.Treasury, 2, 2, 1, 10);

        Assert.Equal(1, GoldStore.Spend(world, db, Keeper, 1));
        Assert.Equal(9, Held(db, world, 2, 2));
    }

    [Fact]
    public void EachStoreGivesItsShareInPassesAndEmptiesItsChainFromTheFirstSlab()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Store(db, world, RoomTypeId.Treasury, 2, 2, 3, 100);
        Store(db, world, RoomTypeId.Treasury, 2, 5, 1, 1000);

        Assert.Equal(300, GoldStore.Spend(world, db, Keeper, 300));
        RoomInstance first = RoomIndex.RoomAt(world, db, 2 * world.Width + 2)!;
        int[] chain = SlabChain.Order(world, first);
        Assert.Equal(0, world.TileAtIndex(chain[0]).GoldHeld);
        // Shares of 100, 33, 11, 4 and 1 each, then 2 from whichever store comes first.
        Assert.InRange(300 - Held(db, world, 2, 2), 149, 151);
        Assert.Equal(1300 - 300, Held(db, world, 2, 2) + Held(db, world, 2, 5));
    }

    [Fact]
    public void TheHeartGivesItsShareUnlessItIsARuin()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Store(db, world, RoomTypeId.DungeonHeart, 6, 6, 1, 500);
        Assert.Equal(100, GoldStore.Spend(world, db, Keeper, 100));

        world.Player(Keeper).HeartRuined = true;
        Assert.Equal(0, GoldStore.Spend(world, db, Keeper, 100));
    }

    private static (GameDatabase, WorldState, CreatureState, GameSimulation) Paying(int gold)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Store(db, world, RoomTypeId.Treasury, 2, 2, 1, gold);
        var at = new TilePos(4, 4);
        KeeperOwns(db, world, at);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, at);
        return (db, world, goblin, new GameSimulation(db, world, seed: 1, [new HandSystem()]));
    }

    private static void KeeperOwns(GameDatabase db, WorldState world, TilePos at)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = Keeper;
    }

    [Fact]
    public void TheMintedWageDroppedOnTheCreatureIsDeliveredToIt()
    {
        var (db, world, goblin, sim) = Paying(2000);
        int wage = PaydayRules.Wage(db, db.CreatureById(goblin.CreatureTypeId), goblin.Level);
        goblin.Anger.Pay = 20000;
        int purse = goblin.GoldHeld;

        sim.Tick(InputFrame.Of(new MintWageCommand(Keeper, goblin.Id), new DropCommand(Keeper, goblin.Tile, goblin.Id)));

        Assert.True(wage > 0);
        Assert.Equal(2000 - wage, GoldStore.Total(world, Keeper));
        Assert.Equal(0, goblin.Anger.Pay);
        Assert.True(goblin.GoldHeld > purse);
        Assert.Empty(world.Player(Keeper).Hand);
    }

    [Fact]
    public void AShortTreasuryMintsWhatItHas()
    {
        var (db, world, goblin, sim) = Paying(7);
        sim.Tick(InputFrame.Of(new MintWageCommand(Keeper, goblin.Id)));

        HandItem held = Assert.Single(world.Player(Keeper).Hand);
        Assert.Equal(7, world.ObjectById(held.Id)!.GoldHeld);
        Assert.Equal(0, GoldStore.Total(world, Keeper));
    }

    [Fact]
    public void WithoutATreasuryOrGoldTheMintIsRefused()
    {
        var (db, world, goblin, sim) = Paying(0);
        sim.Tick(InputFrame.Of(new MintWageCommand(Keeper, goblin.Id)));
        Assert.Empty(world.Player(Keeper).Hand);

        WorldState bare = World(db);
        CreatureState other = bare.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(4, 4));
        new GameSimulation(db, bare, seed: 1, [new HandSystem()]).Tick(InputFrame.Of(new MintWageCommand(Keeper, other.Id)));
        Assert.Empty(bare.Player(Keeper).Hand);
        Assert.Empty(bare.Objects);
    }

    [Fact]
    public void AFullHandLeavesTheMintedPileOnTileZeroAsDK2sFactoryMakesIt()
    {
        var (db, world, goblin, sim) = Paying(2000);
        ObjectState filler = world.SpawnObject(db, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, 5);
        for (int i = 0; i < HandRules.Capacity(db); i++) world.Player(Keeper).Hand.Add(HandItem.Of(filler));

        sim.Tick(InputFrame.Of(new MintWageCommand(Keeper, goblin.Id)));

        ObjectState pile = world.Objects.Single(o => o.Id != filler.Id);
        Assert.False(pile.InHand);
        Assert.Equal(0, pile.TileIndex);
        Assert.True(GoldStore.Total(world, Keeper) < 2000);
    }
}
